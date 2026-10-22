using System.Diagnostics;
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
public sealed class CSharpDocument : IServerDocument, IDisposable
{
    /// <summary>How long typing must pause before diagnostics are asked for again.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(600);

    /// <summary>Parameter names that say nothing an argument does not (Console.WriteLine's value, a list's item),
    /// whose hints Rider leaves off too.</summary>
    private static readonly HashSet<string> Generic = new(StringComparer.Ordinal) { "value:", "obj:", "item:", "o:", "arg:" };

    private readonly CSharpService service;
    private readonly OpenDocument document;
    private readonly ServerDiagnostics diagnostics;
    private SemanticHighlighter? semantic;

    internal CSharpDocument(CSharpService service, OpenDocument document)
    {
        this.service = service;
        this.document = document;
        diagnostics = new ServerDiagnostics(document, holdsChecks: false);
        document.Text.Changed += OnChanged;
        document.Saved += OnSaved;
    }

    public LspDocument? Lsp { get; private set; }

    /// <summary>The diagnostics last pulled, moved with the text since, so their lines are the text's as it is now.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => diagnostics.Placed;

    internal void Connect(RoslynSession session)
    {
        if (Lsp is not null)
        {
            return;
        }
        Lsp = session.Open(document.Path, document.Text);
        Colour(Lsp, session.Server);
        Pull(TimeSpan.Zero);
        FindContexts();
    }

    /// <summary>The projects the server has the file in, by Roslyn's own <c>textDocument/_vs_getProjectContexts</c>,
    /// asked again as the server's projects load: the one chosen stays while the server still has it.</summary>
    public IReadOnlyList<ProjectContext> Contexts { get; private set; } = [];

    /// <summary>When the server first said it has the file in a project of the folder's, not Miscellaneous Files: when
    /// it was asked (what it answers asked after that is that project's), when it said so (Stopwatch timestamps) and
    /// the project; null before. A start's timing reads it.</summary>
    internal (long Asked, long At, string Project)? Placed { get; private set; }

    /// <summary>Asks the server which projects it has the file in, as background work a screenshot waits for.</summary>
    public void FindContexts()
    {
        if (Lsp is { IsRazor: false } lsp)
        {
            BackgroundWork.Begin();
            _ = FindContextsAsync(lsp);
        }
    }

    private async Task FindContextsAsync(LspDocument lsp)
    {
        try
        {
            long asked = Stopwatch.GetTimestamp();
            ProjectContextList? found = await lsp.ProjectContextsAsync();
            if (Lsp != lsp || found is null)
            {
                return;
            }
            Contexts = [.. found.ProjectContexts.Where(c => !c.IsMiscellaneous)];
            ProjectContext? kept = lsp.Context is { } chosen ? Contexts.FirstOrDefault(c => c.Label == chosen.Label) : null;
            if (lsp.Context is not null && kept is null)
            {
                // The project it was chosen in is gone (a framework no longer targeted): the server's choice from now on.
                lsp.Context = null;
                Refresh();
            }
            else
            {
                lsp.Context = kept;
            }
            document.ProjectContexts = [.. Contexts.Select(c => c.Label)];
            document.ProjectContext = (kept ?? found.Default)?.Label;
            if (Placed is null && Contexts.Count > 0)
            {
                Placed = (asked, Stopwatch.GetTimestamp(), document.ProjectContext ?? Contexts[0].Label);
            }
        }
        catch (LanguageServerFailed)
        {
            // Not yet able to say (its projects still loading), or gone: asked again as they load.
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    /// <summary>Has the server answer for the file in another of its projects (Rider's target framework switcher): its
    /// colours and diagnostics are asked for again in it.</summary>
    public void Choose(ProjectContext context)
    {
        if (Lsp is not { } lsp)
        {
            return;
        }
        lsp.Context = context;
        document.ProjectContext = context.Label;
        Refresh();
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

    /// <summary>The server's word that its colours moved: asked for again unless the request out already answers with it.</summary>
    public void ColoursMoved()
    {
        if (semantic is null)
        {
            RefreshColours();
            return;
        }
        semantic.Refreshed();
    }

    public void RefreshDiagnostics() => Pull(TimeSpan.Zero);

    public void Dispose()
    {
        diagnostics.Stop();
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
        // Moved with the text, as the markers are, so a fix asked for at the caret finds the problems on its line.
        diagnostics.Move(e);
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

    private void Pull(TimeSpan delay)
    {
        if (Lsp is not { } lsp || !service.IsReady && delay > TimeSpan.Zero)
        {
            return;
        }
        diagnostics.Pull(delay, cancel => PullAsync(lsp, cancel));
    }

    /// <summary>The file's diagnostics, then its inlay hints, each for the text as it stands.</summary>
    private async Task PullAsync(LspDocument lsp, CancellationToken cancel)
    {
        if (await diagnostics.PulledAsync(lsp, cancel) is not { } found)
        {
            return;
        }
        document.Markers = diagnostics.Take(found, DiagnosticMarks.Marker);
        service.FileProblemsChanged(document);
        (IReadOnlyList<LspInlayHint> hints, int hinted) = await lsp.InlayHintsAsync(cancel);
        if (!cancel.IsCancellationRequested && hinted == document.Text.Version)
        {
            document.InlayHints = Shown(lsp, hints);
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

}
