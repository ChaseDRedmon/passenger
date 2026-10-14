using Avalonia.Threading;
using Passenger.App.CSharp;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App.Web;

/// <summary>One open JavaScript, TypeScript, HTML, CSS or JSON file's share of its language server: kept in step, marked
/// with the diagnostics the server pulls or pushes (moved with the text between them, as a C# file's are), and, where the
/// server finds colours (CSS's), a swatch in the gutter beside each line that writes one. Of one server only: a server
/// started again gives the file a new one, so what asks the server asks <see cref="WebLanguages.For"/> each time.</summary>
public sealed class WebDocument : IServerDocument, IDisposable
{
    /// <summary>How long typing must pause before diagnostics and colours are asked for again.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    private readonly OpenDocument document;
    private CancellationTokenSource? pulling;
    private LanguageSession? session;
    private Diagnostic[] found = [];
    private TextRange[] ranges = [];
    private IReadOnlyList<Diagnostic>? placed = [];

    internal WebDocument(OpenDocument document, WebServerKind kind, string languageId)
    {
        this.document = document;
        Kind = kind;
        LanguageId = languageId;
        document.Text.Changed += OnChanged;
        document.Saved += OnSaved;
    }

    public WebServerKind Kind { get; }

    /// <summary>The language the server is told the file is in (<c>typescriptreact</c>, <c>scss</c>, <c>jsonc</c>).</summary>
    public string LanguageId { get; }

    public LspDocument? Lsp { get; private set; }

    /// <summary>The server's characters after which completion opens unasked, none until it has started.</summary>
    public IReadOnlyList<string> CompletionTriggers => session?.Server.CompletionTriggers ?? [];

    /// <summary>Whether the server answers a request (its capability, as <c>renameProvider</c>): the JSON server has no
    /// definitions or renames, which Passenger then gives itself.</summary>
    public bool Provides(string capability) => session?.Server.Provides(capability) == true;

    public IReadOnlyList<Diagnostic> Diagnostics => placed ??= DiagnosticMarks.Placed(document.Text.Snapshot, found, ranges);

    internal void Connect(LanguageSession started)
    {
        if (Lsp is not null)
        {
            return;
        }
        session = started;
        Lsp = started.Open(document.Path, document.Text, LanguageId);
        Lsp.DiagnosticsPushed += OnPushed;
        Refresh();
    }

    /// <summary>The diagnostics and colours asked for again (the server's refresh, a server started again).</summary>
    public void Refresh() => Pull(TimeSpan.Zero);

    public void Dispose()
    {
        pulling?.Cancel();
        document.Text.Changed -= OnChanged;
        document.Saved -= OnSaved;
        if (Lsp is { } lsp)
        {
            lsp.DiagnosticsPushed -= OnPushed;
            lsp.Dispose();
        }
        Lsp = null;
    }

    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (ranges.Length > 0)
        {
            for (int i = 0; i < ranges.Length; i++)
            {
                ranges[i] = TextEditing.MapRange(ranges[i], e.Edits);
            }
            placed = null;
        }
        if (document.Markers.Count > 0)
        {
            document.Markers = [.. document.Markers.Select(m => m with { Range = TextEditing.MapRange(m.Range, e.Edits) })];
        }
        Pull(Settle);
    }

    private void OnSaved(object? sender, EventArgs e) => Lsp?.Saved();

    /// <summary>A push from the server, on its thread: taken on the window's, if it is for the text as it stands.</summary>
    private void OnPushed(object? sender, EventArgs e)
    {
        if (sender is LspDocument lsp)
        {
            UiThread.Post(() =>
            {
                if (lsp == Lsp)
                {
                    (IReadOnlyList<Diagnostic> diagnostics, int version) = lsp.Pushed;
                    if (version == document.Text.Version)
                    {
                        Show(diagnostics);
                    }
                }
            });
        }
    }

    private void Pull(TimeSpan delay)
    {
        // A server that pushes its diagnostics and finds no colours (TypeScript's) has nothing to be asked.
        if (Lsp is not { } lsp || !(Provides("diagnosticProvider") || Provides("colorProvider")))
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
            if (session?.Server.Provides("diagnosticProvider") == true)
            {
                (IReadOnlyList<Diagnostic> diagnostics, int version) = await lsp.DiagnosticsAsync(cancel);
                if (!cancel.IsCancellationRequested && version == document.Text.Version)
                {
                    Show(diagnostics);
                }
            }
            if (session?.Server.Provides("colorProvider") == true)
            {
                (IReadOnlyList<ColorInformation> colours, int coloured) = await lsp.ColoursAsync(cancel);
                if (!cancel.IsCancellationRequested && coloured == document.Text.Version)
                {
                    document.ColourMarks = Swatches(lsp, colours);
                }
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

    private void Show(IReadOnlyList<Diagnostic> diagnostics)
    {
        ServerLines lines = ServerLines.Of(document.Text.Snapshot);
        found = [.. diagnostics];
        ranges = [.. found.Select(d => DiagnosticMarks.Offsets(lines, d.Range))];
        placed = diagnostics;
        // The server's own codes stay with the diagnostics, which its quick fixes are asked for by.
        document.Markers = [.. found.Select((d, i) => DiagnosticMarks.Marker(Named(d), ranges[i])).OfType<Marker>()];
    }

    /// <summary>A diagnostic with the code Rider shows before its message: TypeScript's as TS2322 (from its own server, or
    /// the HTML server's script), none for the rest, whose codes are lint rules' names or the JSON server's numbers.</summary>
    private static Diagnostic Named(Diagnostic diagnostic)
    {
        string? code = diagnostic.Code is { ValueKind: System.Text.Json.JsonValueKind.Number } number && diagnostic.Source is "typescript" or "ts" or "js"
            ? $"TS{number}" : null;
        return new Diagnostic
        {
            Range = diagnostic.Range,
            Severity = diagnostic.Severity,
            Code = code is null ? null : System.Text.Json.JsonSerializer.SerializeToElement(code),
            Source = diagnostic.Source,
            Message = diagnostic.Message,
            Tags = diagnostic.Tags,
            Data = diagnostic.Data,
        };
    }

    /// <summary>A swatch in the gutter for each line that writes a colour, the first on the line, as Rider shows them.</summary>
    private static IReadOnlyList<ColourMark> Swatches(LspDocument lsp, IReadOnlyList<ColorInformation> colours) =>
        [.. colours
            .Select(c => (Line: lsp.Text.Snapshot.GetLineFromOffset(Math.Min(lsp.OffsetOf(c.Range.Start), lsp.Text.Snapshot.Length)), c.Color))
            .DistinctBy(c => c.Line)
            .Select(c => new ColourMark(c.Line, Avalonia.Media.Color.FromArgb(Byte(c.Color.Alpha), Byte(c.Color.Red), Byte(c.Color.Green), Byte(c.Color.Blue))))];

    private static byte Byte(double component) => (byte)Math.Clamp(Math.Round(component * 255), 0, 255);
}
