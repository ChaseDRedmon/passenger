using System.Text.Json;
using Passenger.Text;

namespace Passenger.Lsp;

/// <summary>Roslyn's language server working on one folder: it opens the solution it is given (or, with
/// none, the projects) and keeps every open C# document in step. Requests are made per document
/// (<see cref="LspDocument"/>), in the editor's own offsets.</summary>
public sealed class RoslynSession : IAsyncDisposable
{
    private readonly Dictionary<string, LspDocument> documents = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource coloured = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private RoslynSession(LanguageServer server, string folder, string? solution, IReadOnlyList<string> projects)
    {
        Server = server;
        Folder = folder;
        Solution = solution;
        Projects = solution is null ? projects : [];
        server.ProjectsLoaded += (_, _) =>
        {
            IsLoaded = true;
            Loaded?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>The projects have loaded, so answers now know the whole solution. Raised on any
    /// thread.</summary>
    public event EventHandler? Loaded;

    public LanguageServer Server { get; }
    public string Folder { get; }
    public string? Solution { get; }

    /// <summary>The projects it opened, with no solution to open.</summary>
    public IReadOnlyList<string> Projects { get; }

    public bool IsLoaded { get; private set; }

    /// <summary>Done once the server has first given a document its colours, the last of the phases a folder's opening
    /// is timed by.</summary>
    public Task FirstColours => coloured.Task;

    internal void Coloured() => coloured.TrySetResult();

    /// <summary>Starts a server on a folder and has it open the solution or projects. Cancelled or failed, it ends the
    /// server it started: one given up (another folder opened) never goes on loading this folder unseen.</summary>
    /// <param name="solution">The solution to open; with none, <paramref name="projects"/> are opened. The caller decides
    /// both (the IDE's workspace model), so the server opens what the Explorer and the build see.</param>
    /// <param name="options">Server options to start with, over Passenger's own (the solution-wide analysis' scope).</param>
    /// <param name="started">Given the server once it has answered, before it is asked to open anything, so what it says
    /// of the load (a project that did not load, its progress) is heard from the start.</param>
    public static async Task<RoslynSession> StartAsync(string folder, string? solution, IReadOnlyList<string> projects, string logFolder,
        CancellationToken cancel = default, IReadOnlyDictionary<string, object?>? options = null, Action<LanguageServer>? started = null)
    {
        LanguageServer server = await LanguageServer.StartRoslynAsync(folder, logFolder, cancel, options).ConfigureAwait(false);
        RoslynSession session = new(server, folder, solution, projects);
        try
        {
            cancel.ThrowIfCancellationRequested();
            started?.Invoke(server);
            if (solution is not null)
            {
                await server.NotifyAsync("solution/open", new OpenSolutionParams(DocumentUris.FromPath(solution))).ConfigureAwait(false);
            }
            else
            {
                await server.NotifyAsync("project/open", new OpenProjectParams([.. projects.Select(DocumentUris.FromPath)])).ConfigureAwait(false);
            }
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return session;
    }

    /// <summary>Starts following a document open in an editor: the server is sent its text, then every
    /// change to it, in order.</summary>
    public LspDocument Open(string path, TextDocument text)
    {
        string uri = DocumentUris.FromPath(path);
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

    /// <summary>Closes the documents, on the caller's thread as they are its, then ends the server without holding it.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (LspDocument document in documents.Values.ToList())
        {
            document.Dispose();
        }
        await Server.DisposeAsync().ConfigureAwait(false);
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
        session.Server.Notify("textDocument/didOpen",
            new DidOpenTextDocumentParams(new TextDocumentItem(uri, "csharp", text.Version, text.Snapshot.ToString())));
        text.Changed += OnChanged;
    }

    public string Uri { get; }
    public TextDocument Text { get; }
    public TextDocumentIdentifier Identifier { get; }
    private LanguageServer Server => session.Server;

    public void Saved() => Server.Notify("textDocument/didSave", new DidSaveTextDocumentParams(Identifier));

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
            Server.Notify("textDocument/didClose", new DidCloseTextDocumentParams(Identifier));
        }
    }

    /// <summary>Completion at an offset: invoked, after a trigger character, or asked again as more is typed into a list
    /// the server said was incomplete.</summary>
    public async Task<(CompletionList? List, int Version)> CompletionAsync(int offset, string? trigger, CancellationToken cancel = default, bool incomplete = false)
    {
        int version = Text.Version;
        CompletionContext context = incomplete ? new CompletionContext(3) : trigger is null ? new CompletionContext(1) : new CompletionContext(2, trigger);
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

    /// <summary>Where the type of the symbol at an offset (a variable's, a member's, a parameter's) is declared.</summary>
    public async Task<IReadOnlyList<LspLocation>> TypeDefinitionAsync(int offset, CancellationToken cancel = default) =>
        Shapes.Locations(await Server.RequestAsync<JsonElement>("textDocument/typeDefinition", new TextDocumentPositionParams(Identifier, PositionOf(offset)), cancel));

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

    /// <summary>A chosen code action with its edit: as it came when it had one, resolved when the server left it for later,
    /// and a fix-all's resolved for the scope chosen (Roslyn's codeAction/resolveFixAll).</summary>
    public async Task<CodeAction> ResolveChosenAsync(CodeAction action, string? scope, CancellationToken cancel = default)
    {
        if (scope is not null)
        {
            return await Server.RequestAsync<CodeAction>("codeAction/resolveFixAll", ClientCommands.FixAll(action, scope), cancel) ?? action;
        }
        return action.Edit is null && action.Data is not null ? await ResolveAsync(action, cancel) ?? action : action;
    }

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
        if (tokens is { Data.Length: > 0 })
        {
            session.Coloured();
        }
        return (tokens, version);
    }

    /// <summary>The tokens of a range only, encoded from the document's start as the whole file's are: Roslyn colours a
    /// screen of lines in milliseconds, where a long file takes it about a second.</summary>
    public async Task<(SemanticTokens? Tokens, int Version)> SemanticTokensAsync(TextRange range, CancellationToken cancel = default)
    {
        int version = Text.Version;
        SemanticTokens? tokens = await Server.RequestAsync<SemanticTokens>("textDocument/semanticTokens/range", new SemanticTokensRangeParams(Identifier, RangeOf(range)), cancel);
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

    /// <summary>What the server says of a hint it gave (a type's description, a parameter's), as markdown; empty when it
    /// says nothing.</summary>
    public async Task<string> InlayHintTooltipAsync(LspInlayHint hint, CancellationToken cancel = default)
    {
        JsonElement result = await Server.RequestAsync<JsonElement>("inlayHint/resolve",
            new { position = hint.Position, label = hint.Label, kind = hint.Kind, data = hint.Data }, cancel);
        return result.ValueKind == JsonValueKind.Object && result.TryGetProperty("tooltip", out JsonElement tooltip) ? Shapes.Text(tooltip) : string.Empty;
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

    /// <summary>An offset as an LSP position: the server's line (<see cref="ServerLines"/>), and a column in UTF-16
    /// units, which is what the editor's columns already are.</summary>
    public LspPosition PositionOf(int offset) => ServerLines.Of(Text.Snapshot).PositionOf(offset);

    public LspRange RangeOf(TextRange range) => ServerLines.Of(Text.Snapshot).RangeOf(range);

    public int OffsetOf(LspPosition position) => ServerLines.Of(Text.Snapshot).OffsetOf(position);

    /// <summary>The edits sent last to first, the order an incremental sync is read in, so each change's place in the text
    /// before the edit is its place in the text the changes sent before it left. Edits that touch go as one change: the
    /// later one can decide whether a '\r' where they meet ends a line. The server's lines are carried to the new text
    /// here, so the next change, and every request until then, finds them without reading the file.</summary>
    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        ServerLines before = ServerLines.Of(e.Before);
        ServerLines.After(e);
        List<TextDocumentContentChangeEvent> changes = [];
        for (int last = e.Edits.Count - 1; last >= 0;)
        {
            int first = last;
            while (first > 0 && e.Edits[first - 1].End == e.Edits[first].Start)
            {
                first--;
            }
            TextRange range = TextRange.FromBounds(e.Edits[first].Start, e.Edits[last].End);
            string text = first == last ? e.Edits[last].Text : string.Concat(e.Edits.Skip(first).Take(last - first + 1).Select(edit => edit.Text));
            changes.Add(new TextDocumentContentChangeEvent(before.RangeOf(range), text));
            last = first - 1;
        }
        Server.Notify("textDocument/didChange", new DidChangeTextDocumentParams(new VersionedTextDocumentIdentifier(Uri, e.Version), changes));
    }
}
