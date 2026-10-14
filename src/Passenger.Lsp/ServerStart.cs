using System.Diagnostics;
using System.Text.Json;

namespace Passenger.Lsp;

/// <summary>How a language server other than Roslyn starts (a web language's, on Node.js): the program as
/// <c>ChildProcesses</c> made it, the folder it works on, where its log goes, the capabilities Passenger tells it, its own
/// initialization options, what <c>workspace/configuration</c> answers for each section (null for its default), and the
/// content it asks the client for by URI (<c>vscode/content</c>: the JSON server's schemas).</summary>
public sealed record ServerStart(ProcessStartInfo Process, string Folder, string LogFolder, JsonElement Capabilities, object? InitializationOptions = null)
{
    public Func<string?, object?>? Configuration { get; init; }

    public Func<string, CancellationToken, Task<string>>? Content { get; init; }
}
