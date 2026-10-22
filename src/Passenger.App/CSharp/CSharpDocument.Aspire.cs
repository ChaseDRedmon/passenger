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
        if (diagnostics.Drop(d => d.Source == AspireDiagnosticSource, DiagnosticMarks.Marker) is { } markers)
        {
            document.Markers = markers;
            service.FileProblemsChanged(document);
        }
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
