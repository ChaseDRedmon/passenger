using Avalonia.Threading;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Languages.CSharp;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App.CSharp;

/// <summary>One open C# file's share of the language server: it is kept in step, coloured by what the
/// compiler knows, and marked with the diagnostics the server reports, pulled once typing pauses.
/// Between pulls the markers move with the text, so a squiggle never drifts off its code.</summary>
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

    internal CSharpDocument(CSharpService service, OpenDocument document)
    {
        this.service = service;
        this.document = document;
        document.Text.Changed += OnChanged;
        document.Saved += OnSaved;
    }

    public LspDocument? Lsp { get; private set; }

    /// <summary>The diagnostics last pulled, mapped to the text as it is now.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; private set; } = [];

    internal void Connect(RoslynSession session)
    {
        if (Lsp is not null)
        {
            return;
        }
        Lsp = session.Open(document.Path, document.Text);
        if (session.Server.Legend is { } legend && document.Highlighter is { } syntax)
        {
            semantic = new SemanticHighlighter(syntax, Lsp, legend);
            document.Highlighter = semantic;
        }
        Pull(TimeSpan.Zero);
    }

    public void Refresh()
    {
        semantic?.Refresh();
        Pull(TimeSpan.Zero);
    }

    public void Dispose()
    {
        pulling?.Cancel();
        document.Text.Changed -= OnChanged;
        document.Saved -= OnSaved;
        semantic?.Dispose();
        Lsp?.Dispose();
    }

    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (document.Markers.Count > 0)
        {
            document.Markers = document.Markers
                .Select(m => m with { Range = TextEditing.MapRange(m.Range, e.Edits) })
                .ToList();
        }
        if (document.InlayHints.Count > 0)
        {
            // A hint keeps its place before its argument; text typed at its place goes after it.
            document.InlayHints = [.. document.InlayHints.Select(h => h with { Offset = TextEditing.MapOffset(h.Offset, e.Edits, afterInsertions: false) })];
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
            Diagnostics = found;
            document.Markers = found.Select(Marker).OfType<Marker>().ToList();
            (IReadOnlyList<LspInlayHint> hints, int hinted) = await lsp.InlayHintsAsync(cancel);
            if (!cancel.IsCancellationRequested && hinted == document.Text.Version)
            {
                document.InlayHints = [.. hints.Where(h => (h.Kind is null or 2) && !Generic.Contains(h.Label.TrimEnd())).Select(h => new InlayHint(lsp.OffsetOf(h.Position), h.Label.TrimEnd(), h.PaddingLeft, h.PaddingRight))];
            }
        }
        catch (Exception error) when (error is OperationCanceledException or StreamJsonRpc.RemoteInvocationException or StreamJsonRpc.ConnectionLostException or ObjectDisposedException)
        {
        }
        finally
        {
            Dispatcher.UIThread.Post(BackgroundWork.End);
        }
    }

    /// <summary>What a diagnostic draws as: errors, warnings and information squiggled; code the
    /// compiler calls unnecessary faded; the server's quieter hints left off the page.</summary>
    private Marker? Marker(Diagnostic diagnostic)
    {
        bool unnecessary = diagnostic.Tags?.Contains(1) == true;
        MarkerKind? kind = diagnostic.Severity switch
        {
            1 => MarkerKind.Error,
            2 => unnecessary ? MarkerKind.Unnecessary : MarkerKind.Warning,
            3 => unnecessary ? MarkerKind.Unnecessary : MarkerKind.Info,
            _ => unnecessary ? MarkerKind.Unnecessary : null,
        };
        if (kind is not { } k || Lsp is not { } lsp)
        {
            return null;
        }
        int start = lsp.OffsetOf(diagnostic.Range.Start);
        int end = lsp.OffsetOf(diagnostic.Range.End);
        string code = diagnostic.Code?.ToString() is { Length: > 0 } c ? $"{c}: " : string.Empty;
        return new Marker(TextRange.FromBounds(start, Math.Max(start, end)), k, code + diagnostic.Message);
    }
}
