using System.Text.Json;
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

    /// <summary>The references the file's Aspire warnings say its AppHost lacks, on a server's line of the text as it stands
    /// (any line for null): each with the place it is warned at, the service and the resource, from which the WithReference
    /// fix is worked out as it is chosen.</summary>
    internal IReadOnlyList<(TextRange Range, string Service, string Resource)> AspireMissingOn(int? line)
    {
        List<(TextRange, string, string)> missing = [];
        foreach ((Diagnostic diagnostic, TextRange range) in diagnostics.PlacedRanges)
        {
            if (diagnostic.Source == AspireDiagnosticSource
                && (line is not { } on || diagnostic.Range.Start.Line <= on && on <= diagnostic.Range.End.Line)
                && diagnostic.Data is { ValueKind: JsonValueKind.Object } data
                && data.TryGetProperty("service", out JsonElement serviceName) && serviceName.GetString() is { } named
                && data.TryGetProperty("resource", out JsonElement resourceName) && resourceName.GetString() is { } wanted)
            {
                missing.Add((range, named, wanted));
            }
        }
        return missing;
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
                Data = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["service"] = m.Service, ["resource"] = m.Resource }),
            })];
        }
        catch (LanguageServerFailed)
        {
            return [];
        }
    }
}
