using Avalonia.Threading;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Languages.CSharp;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App.CSharp;

/// <summary>One open C# file's share of the language server: it is kept in step, coloured by what the
/// compiler knows, and marked with the diagnostics the server reports, pulled once typing pauses.
/// Between pulls the markers move with the text, so a squiggle never drifts off its code. Of one server only: a server
/// started again gives every open file a new one (<see cref="CSharpService"/>), so what asks the server for a file asks
/// <see cref="CSharpService.For"/> each time rather than keep this or its <see cref="Lsp"/>.</summary>
public sealed class CSharpDocument : IDisposable
{
    /// <summary>How long typing must pause before diagnostics are asked for again.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(600);

    /// <summary>Parameter names that say nothing an argument does not (Console.WriteLine's value, a list's item),
    /// whose hints Rider leaves off too.</summary>
    private static readonly HashSet<string> Generic = new(StringComparer.Ordinal) { "value:", "obj:", "item:", "o:", "arg:" };

    private readonly CSharpService service;
    private readonly OpenDocument document;
    private SemanticHighlighter? semantic;
    private CancellationTokenSource? pulling;

    /// <summary>The diagnostics as pulled, and each one's range in the text as it stands, moved with every edit since.</summary>
    private Diagnostic[] pulled = [];
    private TextRange[] ranges = [];

    /// <summary>The diagnostics placed on the server's lines of the text as it stands, made when first asked for after an
    /// edit, so a key costs a move of each range rather than a line table of the file.</summary>
    private IReadOnlyList<Diagnostic>? placed = [];

    internal CSharpDocument(CSharpService service, OpenDocument document)
    {
        this.service = service;
        this.document = document;
        document.Text.Changed += OnChanged;
        document.Saved += OnSaved;
    }

    public LspDocument? Lsp { get; private set; }

    /// <summary>The diagnostics last pulled, moved with the text since, so their lines are the text's as it is now.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => placed ??= Placed();

    internal void Connect(RoslynSession session)
    {
        if (Lsp is not null)
        {
            return;
        }
        Lsp = session.Open(document.Path, document.Text);
        Colour(Lsp, session.Server);
        Pull(TimeSpan.Zero);
    }

    /// <summary>The server's colours over the parser's, once the server has said its legend: a Razor file's is the one Razor
    /// registers as it starts, which can come after the file was opened.</summary>
    private void Colour(LspDocument lsp, LanguageServer server)
    {
        if (semantic is null && (lsp.IsRazor ? server.RazorLegend : server.Legend) is { } legend && document.Highlighter is { } syntax)
        {
            semantic = new SemanticHighlighter(syntax, lsp, legend);
            document.Highlighter = semantic;
        }
    }

    public void Refresh()
    {
        RefreshColours();
        RefreshDiagnostics();
    }

    public void RefreshColours()
    {
        if (semantic is null && Lsp is { } lsp && service.Session is { } session)
        {
            Colour(lsp, session.Server);
            return;
        }
        semantic?.Refresh();
    }

    public void RefreshDiagnostics() => Pull(TimeSpan.Zero);

    public void Dispose()
    {
        pulling?.Cancel();
        document.Text.Changed -= OnChanged;
        document.Saved -= OnSaved;
        if (semantic is not null)
        {
            semantic.Dispose();
            if (document.Highlighter == semantic)
            {
                document.Highlighter = semantic.Syntax;
            }
        }
        Lsp?.Dispose();
    }

    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (ranges.Length > 0)
        {
            // Moved with the text, as the markers are, so a fix asked for at the caret finds the problems on its line.
            for (int i = 0; i < ranges.Length; i++)
            {
                ranges[i] = TextEditing.MapRange(ranges[i], e.Edits);
            }
            placed = null;
        }
        if (document.Markers.Count > 0)
        {
            document.Markers = document.Markers
                .Select(m => m with { Range = TextEditing.MapRange(m.Range, e.Edits) })
                .ToList();
        }
        if (document.InlayHints.Count > 0)
        {
            // Text typed at a parameter's name goes after it, into the argument; at a type, before it, onto the name.
            document.InlayHints = [.. document.InlayHints.Select(h => h with
            {
                Offset = TextEditing.MapOffset(h.Offset, e.Edits, afterInsertions: h.Kind == InlayHintKind.Type),
            })];
        }
        Pull(Settle);
    }

    private void OnSaved(object? sender, EventArgs e) => Lsp?.Saved();

    /// <summary>The diagnostics pulled, each on the server's lines where its range has been carried to.</summary>
    private Diagnostic[] Placed()
    {
        ServerLines lines = ServerLines.Of(document.Text.Snapshot);
        int length = lines.Text.Length;
        Diagnostic[] moved = new Diagnostic[pulled.Length];
        for (int i = 0; i < pulled.Length; i++)
        {
            Diagnostic diagnostic = pulled[i];
            TextRange now = ranges[i];
            moved[i] = new Diagnostic
            {
                Range = new LspRange(lines.PositionOf(Math.Min(now.Start, length)), lines.PositionOf(Math.Min(now.End, length))),
                Severity = diagnostic.Severity,
                Code = diagnostic.Code,
                Source = diagnostic.Source,
                Message = diagnostic.Message,
                Tags = diagnostic.Tags,
                Data = diagnostic.Data,
            };
        }
        return moved;
    }

    private void Pull(TimeSpan delay)
    {
        if (Lsp is not { } lsp || !service.IsReady && delay > TimeSpan.Zero)
        {
            return;
        }
        pulling?.Cancel();
        CancellationTokenSource cancel = new();
        pulling = cancel;
        BackgroundWork.Begin();
        _ = PullAsync(lsp, delay, cancel.Token);
    }

    private async Task PullAsync(LspDocument lsp, TimeSpan delay, CancellationToken cancel)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancel);
            }
            (IReadOnlyList<Diagnostic> found, int version) = await lsp.DiagnosticsAsync(cancel);
            if (cancel.IsCancellationRequested || version != document.Text.Version)
            {
                return;
            }
            ServerLines lines = ServerLines.Of(document.Text.Snapshot);
            pulled = [.. found];
            ranges = [.. pulled.Select(d => Offsets(lines, d.Range))];
            placed = found;
            document.Markers = pulled.Select((d, i) => Marker(d, ranges[i])).OfType<Marker>().ToList();
            service.FileProblemsChanged();
            (IReadOnlyList<LspInlayHint> hints, int hinted) = await lsp.InlayHintsAsync(cancel);
            if (!cancel.IsCancellationRequested && hinted == document.Text.Version)
            {
                document.InlayHints = Shown(lsp, hints);
            }
        }
        catch (OperationCanceledException)
        {
            // Typing went on, or the file was let go.
        }
        catch (LanguageServerFailed)
        {
            // The markers stand until the server answers again, or a server started again does.
        }
        finally
        {
            Dispatcher.UIThread.Post(BackgroundWork.End);
        }
    }

    /// <summary>The hints Rider would show of those the server gave for the text as it stands: parameter names but those
    /// that say nothing, and types where Rider puts them (<see cref="TypeHints"/>), each kept with the server's own for
    /// its hover card.</summary>
    private IReadOnlyList<InlayHint> Shown(LspDocument lsp, IReadOnlyList<LspInlayHint> hints)
    {
        ParsedText? syntax = hints.Any(h => h.Kind == 1) ? document.Syntax?.Current : null;
        List<InlayHint> shown = [];
        foreach (LspInlayHint hint in hints)
        {
            string label = hint.Label.Trim();
            if (hint.Kind == 1)
            {
                if (syntax is not null && TypeHints.Place(syntax, lsp.OffsetOf(hint.Position), label) is { } placed)
                {
                    shown.Add(placed with { Data = hint });
                }
            }
            else if (!Generic.Contains(label) && !(lsp.IsRazor && OnDirective(lsp.OffsetOf(hint.Position))))
            {
                shown.Add(new InlayHint(lsp.OffsetOf(hint.Position), label, hint.PaddingLeft, hint.PaddingRight, InlayHintKind.Parameter, hint));
            }
        }
        return shown;
    }

    /// <summary>Whether a place is on a Razor directive's line (<c>@page "/counter"</c>), whose value Razor's generated C#
    /// passes to an attribute: the server names the attribute's parameter there, which Rider never shows.</summary>
    private bool OnDirective(int offset)
    {
        TextSnapshot text = document.Text.Snapshot;
        return Directive.IsMatch(text.GetLineText(text.GetLineFromOffset(Math.Min(offset, text.Length))));
    }

    private static readonly System.Text.RegularExpressions.Regex Directive =
        new(@"^\s*@(page|layout|inherits|implements|namespace|attribute|typeparam|rendermode|preservewhitespace|model|inject|section)\b");

    /// <summary>What a diagnostic draws as: errors, warnings and information squiggled; code the
    /// compiler calls unnecessary faded; the server's quieter hints left off the page.</summary>
    private static Marker? Marker(Diagnostic diagnostic, TextRange range)
    {
        bool unnecessary = diagnostic.Tags?.Contains(1) == true;
        MarkerKind? kind = diagnostic.Severity switch
        {
            1 => MarkerKind.Error,
            2 => unnecessary ? MarkerKind.Unnecessary : MarkerKind.Warning,
            3 => unnecessary ? MarkerKind.Unnecessary : MarkerKind.Info,
            _ => unnecessary ? MarkerKind.Unnecessary : null,
        };
        if (kind is not { } k)
        {
            return null;
        }
        string code = diagnostic.Code?.ToString() is { Length: > 0 } c ? $"{c}: " : string.Empty;
        return new Marker(range, k, code + diagnostic.Message);
    }

    /// <summary>A range of the server's as offsets in the text it was given for, never ending before it starts.</summary>
    private static TextRange Offsets(ServerLines lines, LspRange range)
    {
        int start = lines.OffsetOf(range.Start);
        return TextRange.FromBounds(start, Math.Max(start, lines.OffsetOf(range.End)));
    }
}
