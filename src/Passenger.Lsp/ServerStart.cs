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

    /// <summary>Makes a workspace edit the server asks for (<c>workspace/applyEdit</c>, as ESLint's commands make their
    /// fixes): null once it is made, else why not. Without it, every such edit is refused.</summary>
    public Func<JsonElement, Task<string?>>? ApplyEdit { get; init; }

    /// <summary>How the diagnostics it pushes naming no version follow the texts it is sent.</summary>
    public PushPace Pushes { get; init; } = PushPace.Latest;
}

/// <summary>How a server that pushes a document's diagnostics without naming the version they are for answers the texts
/// it is sent, so each push is told the version it answers as it arrives (<see cref="LspDocument.Pushed"/>): once for
/// each text that changed, in order (<see cref="EachText"/>, the Dockerfile server's), or for the latest text once it
/// has rested <see cref="Rest"/> (a debounce, the YAML server's 200 ms), a push before then answering no text known;
/// else (<see cref="Latest"/>) for the latest text sent when it arrived.</summary>
public sealed record PushPace(bool EachText, TimeSpan Rest)
{
    public static readonly PushPace Latest = new(false, TimeSpan.Zero);
}
