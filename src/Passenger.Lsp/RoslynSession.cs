using System.Text.Json;
using Passenger.Text;

namespace Passenger.Lsp;

/// <summary>Roslyn's language server working on one folder: it opens the folder's solution (or, with
/// none, its projects) and keeps every open C# document in step. Requests are made per document
/// (<see cref="LspDocument"/>), in the editor's own offsets.</summary>
public sealed class RoslynSession : IAsyncDisposable
{
    /// <summary>The most projects opened from a folder with no solution, so a monorepo does not ask
    /// Roslyn to load hundreds at once.</summary>
    private const int ProjectLimit = 200;

    private static readonly HashSet<string> Skipped = [".git", "bin", "obj", "artifacts", "node_modules", ".vs", ".idea"];

    private readonly Dictionary<string, LspDocument> documents = new(StringComparer.Ordinal);

    private RoslynSession(LanguageServer server, string folder, string? solution)
    {
        Server = server;
        Folder = folder;
        Solution = solution;
        server.ProjectsLoaded += (_, _) =>
        {
            IsLoaded = true;
            Loaded?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>The projects have loaded, so answers now know the whole solution. Raised on the
    /// thread pool.</summary>
    public event EventHandler? Loaded;

    public LanguageServer Server { get; }
    public string Folder { get; }
    public string? Solution { get; }
    public bool IsLoaded { get; private set; }

    public static async Task<RoslynSession> StartAsync(string folder, string logFolder, CancellationToken cancel = default)
    {
        LanguageServer server = await LanguageServer.StartRoslynAsync(folder, logFolder, cancel);
        string? solution = FindSolution(folder);
        RoslynSession session = new(server, folder, solution);
        if (solution is not null)
        {
            await server.NotifyAsync("solution/open", new OpenSolutionParams(new Uri(solution).AbsoluteUri));
        }
        else
        {
            List<string> projects = FindProjects(folder);
            await server.NotifyAsync("project/open", new OpenProjectParams(projects.Select(p => new Uri(p).AbsoluteUri).ToList()));
        }
        return session;
    }

    /// <summary>Starts following a document open in an editor: the server is sent its text, then every
    /// change to it, in order.</summary>
    public LspDocument Open(string path, TextDocument text)
    {
        string uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
        if (documents.TryGetValue(uri, out LspDocument? open))
        {
            return open;
        }
        LspDocument document = new(this, uri, text);
        documents[uri] = document;
        return document;
    }

    internal void Forget(LspDocument document) => documents.Remove(document.Uri);

    /// <summary>Symbols across the workspace whose names match a query, as Go to Symbol finds them
    /// (Roslyn matches by pattern: initials and camel humps as well as substrings).</summary>
    public async Task<IReadOnlyList<SymbolInformation>> SymbolsAsync(string query, CancellationToken cancel = default) =>
        await Server.RequestAsync<List<SymbolInformation>>("workspace/symbol", new WorkspaceSymbolParams(query), cancel) ?? [];

    public async ValueTask DisposeAsync()
    {
        foreach (LspDocument document in documents.Values.ToList())
        {
            document.Dispose();
        }
        await Server.DisposeAsync();
    }

    /// <summary>A solution at the folder's top, the one named for the folder if there are several.</summary>
    private static string? FindSolution(string folder)
    {
        List<string> found = Directory.EnumerateFiles(folder, "*.sln").Concat(Directory.EnumerateFiles(folder, "*.slnx")).ToList();
        if (found.Count == 0)
        {
            return null;
        }
        string name = Path.GetFileName(folder);
        return found.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? found.OrderBy(f => f.EndsWith(".slnx", StringComparison.Ordinal) ? 0 : 1).ThenBy(f => f, StringComparer.Ordinal).First();
    }

    private static List<string> FindProjects(string folder)
    {
        List<string> projects = [];
        Stack<string> folders = new([folder]);
        while (folders.Count > 0 && projects.Count < ProjectLimit)
        {
            string current = folders.Pop();
            try
            {
                projects.AddRange(Directory.EnumerateFiles(current, "*.csproj"));
                foreach (string child in Directory.EnumerateDirectories(current))
                {
                    if (!Skipped.Contains(Path.GetFileName(child)))
                    {
                        folders.Push(child);
                    }
                }
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException)
            {
            }
        }
        return projects;
    }
}

/// <summary>One document as the server knows it: opened with its text, then sent every change as it
/// happens (an LSP incremental sync), closed when the editor lets go. Each request is made against
/// the document as it is now, and reports which version it answered for, so a late answer can be
/// recognised and dropped.</summary>
public sealed class LspDocument : IDisposable
{
    private readonly RoslynSession session;
    private bool closed;

    internal LspDocument(RoslynSession session, string uri, TextDocument text)
    {
        this.session = session;
        Uri = uri;
        Text = text;
        Identifier = new TextDocumentIdentifier(uri);
        _ = session.Server.NotifyAsync("textDocument/didOpen",
            new DidOpenTextDocumentParams(new TextDocumentItem(uri, "csharp", text.Version, text.Snapshot.ToString())));
        text.Changed += OnChanged;
    }

    public string Uri { get; }
    public TextDocument Text { get; }
    public TextDocumentIdentifier Identifier { get; }
    private LanguageServer Server => session.Server;

    public void Saved() => _ = Server.NotifyAsync("textDocument/didSave", new DidSaveTextDocumentParams(Identifier));

    public void Dispose()
    {
        if (closed)
        {
            return;
        }
        closed = true;
        Text.Changed -= OnChanged;
        session.Forget(this);
        if (!Server.HasExited)
        {
            _ = Server.NotifyAsync("textDocument/didClose", new DidCloseTextDocumentParams(Identifier));
        }
    }

    public async Task<(CompletionList? List, int Version)> CompletionAsync(int offset, string? trigger, CancellationToken cancel = default)
    {
        int version = Text.Version;
        CompletionContext context = trigger is null ? new CompletionContext(1) : new CompletionContext(2, trigger);
        JsonElement result = await Server.RequestAsync<JsonElement>("textDocument/completion",
            new CompletionParams(Identifier, PositionOf(offset), context), cancel);
        return (Shapes.CompletionList(result), version);
    }

    public Task<CompletionItem?> ResolveAsync(CompletionItem item, CancellationToken cancel = default) =>
        Server.RequestAsync<CompletionItem>("completionItem/resolve", item, cancel);

    public async Task<Hover?> HoverAsync(int offset, CancellationToken cancel = default) =>
        await Server.RequestAsync<Hover>("textDocument/hover", new TextDocumentPositionParams(Identifier, PositionOf(offset)), cancel);

    public async Task<SignatureHelp?> SignatureHelpAsync(int offset, CancellationToken cancel = default) =>
        await Server.RequestAsync<SignatureHelp>("textDocument/signatureHelp", new TextDocumentPositionParams(Identifier, PositionOf(offset)), cancel);

    public async Task<IReadOnlyList<LspLocation>> DefinitionAsync(int offset, CancellationToken cancel = default) =>
        Shapes.Locations(await Server.RequestAsync<JsonElement>("textDocument/definition", new TextDocumentPositionParams(Identifier, PositionOf(offset)), cancel));

    public async Task<IReadOnlyList<LspLocation>> ImplementationAsync(int offset, CancellationToken cancel = default) =>
        Shapes.Locations(await Server.RequestAsync<JsonElement>("textDocument/implementation", new TextDocumentPositionParams(Identifier, PositionOf(offset)), cancel));

    public async Task<IReadOnlyList<LspLocation>> ReferencesAsync(int offset, CancellationToken cancel = default) =>
        Shapes.Locations(await Server.RequestAsync<JsonElement>("textDocument/references",
            new ReferenceParams(Identifier, PositionOf(offset), new ReferenceContext(true)), cancel));

    public async Task<IReadOnlyList<DocumentHighlight>> HighlightsAsync(int offset, CancellationToken cancel = default) =>
        await Server.RequestAsync<List<DocumentHighlight>>("textDocument/documentHighlight", new TextDocumentPositionParams(Identifier, PositionOf(offset)), cancel) ?? [];

    public async Task<JsonElement?> RenameAsync(int offset, string newName, CancellationToken cancel = default)
    {
        JsonElement result = await Server.RequestAsync<JsonElement>("textDocument/rename", new RenameParams(Identifier, PositionOf(offset), newName), cancel);
        return result.ValueKind == JsonValueKind.Object ? result : null;
    }

    public async Task<IReadOnlyList<CodeAction>> CodeActionsAsync(TextRange range, IReadOnlyList<Diagnostic> diagnostics, CancellationToken cancel = default)
    {
        JsonElement result = await Server.RequestAsync<JsonElement>("textDocument/codeAction",
            new CodeActionParams(Identifier, RangeOf(range), new CodeActionContext(diagnostics, TriggerKind: 1)), cancel);
        return Shapes.CodeActions(result);
    }

    public Task<CodeAction?> ResolveAsync(CodeAction action, CancellationToken cancel = default) =>
        Server.RequestAsync<CodeAction>("codeAction/resolve", action, cancel);

    /// <summary>The document's outline: its types, and their members under them.</summary>
    public async Task<IReadOnlyList<DocumentSymbol>> OutlineAsync(CancellationToken cancel = default) =>
        await Server.RequestAsync<List<DocumentSymbol>>("textDocument/documentSymbol", new DocumentSymbolParams(Identifier), cancel) ?? [];

    public async Task<IReadOnlyList<LspTextEdit>> FormatAsync(IndentOptions indent, CancellationToken cancel = default) =>
        await Server.RequestAsync<List<LspTextEdit>>("textDocument/formatting",
            new DocumentFormattingParams(Identifier, new FormattingOptions(indent.Size, !indent.UseTabs)), cancel) ?? [];

    public async Task<(SemanticTokens? Tokens, int Version)> SemanticTokensAsync(CancellationToken cancel = default)
    {
        int version = Text.Version;
        SemanticTokens? tokens = await Server.RequestAsync<SemanticTokens>("textDocument/semanticTokens/full", new SemanticTokensParams(Identifier), cancel);
        return (tokens, version);
    }

    /// <summary>The symbol at an offset as a call hierarchy's root, if it is one that calls or is called.</summary>
    public async Task<HierarchyItem?> PrepareCallHierarchyAsync(int offset, CancellationToken cancel = default) =>
        (await Server.RequestAsync<List<HierarchyItem>>("textDocument/prepareCallHierarchy", new TextDocumentPositionParams(Identifier, PositionOf(offset)), cancel))?.FirstOrDefault();

    /// <summary>The type at an offset as a type hierarchy's root.</summary>
    public async Task<HierarchyItem?> PrepareTypeHierarchyAsync(int offset, CancellationToken cancel = default) =>
        (await Server.RequestAsync<List<HierarchyItem>>("textDocument/prepareTypeHierarchy", new TextDocumentPositionParams(Identifier, PositionOf(offset)), cancel))?.FirstOrDefault();

    /// <summary>The words the server would show inside the document's lines (parameter names before their
    /// arguments), and the version they are for.</summary>
    public async Task<(IReadOnlyList<LspInlayHint> Hints, int Version)> InlayHintsAsync(CancellationToken cancel = default)
    {
        int version = Text.Version;
        LspRange whole = new(new LspPosition(0, 0), PositionOf(Text.Snapshot.Length));
        JsonElement result = await Server.RequestAsync<JsonElement>("textDocument/inlayHint", new InlayHintParams(Identifier, whole), cancel);
        return (Shapes.InlayHints(result), version);
    }

    /// <summary>Every diagnostic the server has for the document, pulled from each source it
    /// registered, and the version they are for.</summary>
    public async Task<(IReadOnlyList<Diagnostic> Diagnostics, int Version)> DiagnosticsAsync(CancellationToken cancel = default)
    {
        int version = Text.Version;
        List<Diagnostic> all = [];
        foreach (string? source in Server.DiagnosticSources)
        {
            DocumentDiagnosticReport? report = await Server.RequestAsync<DocumentDiagnosticReport>("textDocument/diagnostic",
                new DocumentDiagnosticParams(Identifier, source), cancel);
            all.AddRange(report?.Items ?? []);
        }
        List<Diagnostic> distinct = all
            .DistinctBy(d => (d.Range, d.Message, d.Code?.ToString()))
            .ToList();
        return (distinct, version);
    }

    /// <summary>An offset as an LSP position: a line, and a column in UTF-16 units, which is what the
    /// editor's columns already are.</summary>
    public LspPosition PositionOf(int offset)
    {
        TextPosition position = Text.Snapshot.GetPosition(offset);
        return new LspPosition(position.Line, position.Column);
    }

    public LspRange RangeOf(TextRange range) => new(PositionOf(range.Start), PositionOf(range.End));

    public int OffsetOf(LspPosition position) => Text.Snapshot.GetOffset(new TextPosition(position.Line, position.Character));

    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        // Applied last to first, each change is valid in the text the one before left, which is the
        // order an incremental sync is read in; positions come from the text before the change.
        TextSnapshot before = e.Before;
        List<TextDocumentContentChangeEvent> changes = [];
        for (int i = e.Edits.Count - 1; i >= 0; i--)
        {
            Passenger.Text.TextEdit edit = e.Edits[i];
            TextPosition start = before.GetPosition(edit.Start);
            TextPosition end = before.GetPosition(edit.End);
            changes.Add(new TextDocumentContentChangeEvent(
                new LspRange(new LspPosition(start.Line, start.Column), new LspPosition(end.Line, end.Column)), edit.Text));
        }
        _ = Server.NotifyAsync("textDocument/didChange", new DidChangeTextDocumentParams(new VersionedTextDocumentIdentifier(Uri, e.Version), changes));
    }
}
