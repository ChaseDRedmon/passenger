using Passenger.Editor;
using Passenger.Lsp;
using Passenger.Roslyn.Extension;
using Passenger.Text;

namespace Passenger.App.CSharp;

public sealed partial class CSharpDocument
{
    private const string AspireDiagnosticSource = "Passenger.Aspire";

    internal void AspireChanged()
    {
        ClearAspireDiagnostics();
        Pull(Settle);
    }

    private void ClearAspireDiagnostics()
    {
        if (!pulled.Any(d => d.Source == AspireDiagnosticSource))
        {
            return;
        }
        int[] kept = [.. Enumerable.Range(0, pulled.Length).Where(i => pulled[i].Source != AspireDiagnosticSource)];
        ranges = [.. kept.Select(i => ranges[i])];
        pulled = [.. kept.Select(i => pulled[i])];
        placed = null;
        document.Markers = pulled.Select((d, i) => DiagnosticMarks.Marker(d, ranges[i])).OfType<Marker>().ToList();
        service.FileProblemsChanged();
    }

    private static async Task<IReadOnlyList<Diagnostic>> AspireDiagnosticsAsync(LspDocument lsp, CancellationToken cancel)
    {
        try
        {
            AspireHelp help = await lsp.ExtensionAsync<AspireHelp>(Handlers.AspireCodeHelp, new AspireHelpRequest(null), cancel);
            return [.. help.Missing.Select(m => new Diagnostic
            {
                Range = new LspRange(new LspPosition(m.Start.Line, m.Start.Character), new LspPosition(m.End.Line, m.End.Character)),
                Severity = 2,
                Source = AspireDiagnosticSource,
                Message = $"AppHost resource '{m.Service}' does not reference '{m.Resource}'. Add WithReference({m.Resource}) to '{m.Service}'.",
            })];
        }
        catch (LanguageServerFailed)
        {
            return [];
        }
    }
}
