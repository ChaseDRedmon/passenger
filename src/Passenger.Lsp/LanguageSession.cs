using Passenger.Core;
using Passenger.Text;

namespace Passenger.Lsp;

/// <summary>A language server other than Roslyn working on one folder (a web language's: TypeScript's, or VS Code's for
/// HTML, CSS or JSON): it keeps every open document of its languages in step, each opened under its language's id, and
/// gives each the diagnostics the server pushes for it. Requests are made per document (<see cref="LspDocument"/>), in the
/// editor's own offsets. Documents are opened and closed on the caller's thread; what the server pushes comes on any.</summary>
public sealed class LanguageSession : IAsyncDisposable, IDocumentHost
{
    /// <summary>The open documents by their canonical path, as a server may spell a URI its own way.</summary>
    private readonly Dictionary<string, LspDocument> documents = new(StringComparer.FromComparison(PathNames.Comparison));

    private LanguageSession(LanguageServer server, PushPace pushes)
    {
        Server = server;
        Pushes = pushes;
        server.DiagnosticsPublished += OnPublished;
    }

    public LanguageServer Server { get; }

    public PushPace Pushes { get; }

    /// <summary>Starts a server and initializes it; cancelled or failed, it leaves no process behind.</summary>
    public static async Task<LanguageSession> StartAsync(ServerStart start, CancellationToken cancel = default) =>
        new(await LanguageServer.StartAsync(start, cancel).ConfigureAwait(false), start.Pushes);

    /// <summary>Starts following a document open in an editor, in a language the server answers for: it is sent its text,
    /// then every change to it, in order.</summary>
    public LspDocument Open(string path, TextDocument text, string languageId)
    {
        string canonical = PathNames.Canonical(path);
        lock (documents)
        {
            if (documents.TryGetValue(canonical, out LspDocument? open))
            {
                return open;
            }
        }
        LspDocument document = new(this, DocumentUris.FromPath(canonical), text, languageId);
        lock (documents)
        {
            documents[canonical] = document;
        }
        return document;
    }

    void IDocumentHost.Forget(LspDocument document)
    {
        lock (documents)
        {
            documents.Remove(DocumentUris.ToPath(document.Uri));
        }
    }

    void IDocumentHost.Coloured()
    {
    }

    /// <summary>VS Code's servers are asked (they say <c>diagnosticProvider</c>); TypeScript's sends them as it finds them.</summary>
    bool IDocumentHost.PullsDiagnostics => Server.Provides("diagnosticProvider");

    private void OnPublished(object? sender, PublishDiagnosticsParams published)
    {
        LspDocument? document;
        lock (documents)
        {
            documents.TryGetValue(DocumentUris.ToPath(published.Uri), out document);
        }
        document?.Publish(published);
    }

    /// <summary>Closes the documents, on the caller's thread as they are its, then ends the server without holding it.</summary>
    public async ValueTask DisposeAsync()
    {
        LspDocument[] open;
        lock (documents)
        {
            open = [.. documents.Values];
        }
        foreach (LspDocument document in open)
        {
            document.Dispose();
        }
        Server.DiagnosticsPublished -= OnPublished;
        await Server.DisposeAsync().ConfigureAwait(false);
    }
}
