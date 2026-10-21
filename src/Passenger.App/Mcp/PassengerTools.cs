using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Passenger.App.CSharp;
using Passenger.App.Debugging;
using Passenger.App.Run;
using Passenger.App.Web;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Dap;
using Passenger.Editor;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App.Mcp;

/// <summary>Passenger's tools for an agent, over the Model Context Protocol: the window's own workspace,
/// the language server's knowledge of the code, builds and runs, the debugger, and the IDE itself
/// (screenshots, actions, typing), each run on the UI thread as the window's own commands are. Lines and
/// columns count from one, as an editor shows them; a relative path is the open folder's. A tool not ReadOnly says
/// whether it is Destructive: a person's window asks before an agent first uses one that is not declared harmless.
/// What a folder has and runs a tool asks of the window's session (<see cref="ProjectSession"/>) as the call finds it, and
/// what it starts it starts through the session's controllers; the window itself only for what is the window's: opening
/// a folder in it, its editors, its terminal, its actions and what it draws. No tool reads the status bar.
/// <paramref name="connection"/> is the agent's, through which a person's window asks before the agent replaces its folder;
/// none is an agent's own IDE's.</summary>
public sealed partial class PassengerTools(MainWindow window, AgentConnection? connection = null)
{
    private static readonly TimeSpan ServerLimit = TimeSpan.FromSeconds(120);

    /// <summary>How long a file's markers are waited for: its checks are quick once its project is read.</summary>
    private static readonly TimeSpan MarkersLimit = TimeSpan.FromSeconds(30);

    /// <summary>The folder open in the window as this call finds it: an agent's Open makes another.</summary>
    private ProjectSession Session => window.Session;

    /// <summary>The session's builds, runs and debug launches as the window shows them.</summary>
    private BuildRunController Builds => window.Builds;

    private Passenger.App.Editors.EditorArea Editors => window.EditorArea;

    private const string Arguments = "The command line, in place of the configuration's";

    // Workspace and editor

    [McpServerTool(Name = "open", Title = "Open a folder, solution, project or file", Destructive = false), Description(
        "Opens a folder, solution or project (a directory, .sln, .slnx or .csproj) as the workspace, or a file in the editor " +
        "(its folder first if none is open), starting the C# language server. On a person's window, replacing their folder is " +
        "asked of them first. Returns the workspace.")]
    public Task<Workspace> Open(string path, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        string full = Resolve(path);
        // Opened once what another folder ran has ended.
        if (Directory.Exists(full) || (File.Exists(full) && WorkspaceModel.Opens(full)))
        {
            string opening = Directory.Exists(full) ? PathNames.Canonical(full) : Path.GetDirectoryName(PathNames.Canonical(full))!;
            // The person's folder is theirs: an agent on their window replaces it only with their leave.
            if (connection is { Attached: true } && !window.AgentsUnattended && Session.Folder is { } open && !PathNames.Same(open, opening))
            {
                AgentCall.Current?.Waits($"Waiting for the person at {AgentConnection.Where(window)} to let {connection.Agent} open {full} (at most {AgentConnection.Limit(window)})");
                Admission said = await window.AllowAgentOpenAsync(connection.Agent, full, open, cancel);
                AgentCall.Current?.Waited(said.ToString());
                cancel.ThrowIfCancellationRequested();
                if (said != Admission.Allowed)
                {
                    throw new InvalidOperationException(said == Admission.NotAllowed
                        ? $"The person at this Passenger window did not let {connection.Agent} open {full} in place of {open}. Ask them, or work in {open}."
                        : $"Nobody at {AgentConnection.Where(window)} answered within {AgentConnection.Limit(window)} whether {connection.Agent} may open {full} in place of {open}. A later call asks again.");
                }
            }
            await window.OpenFolder(full, connection).WaitAsync(cancel);
        }
        else if (File.Exists(full))
        {
            if (Session.Folder is null)
            {
                await window.OpenFolder(Path.GetDirectoryName(full)!, connection).WaitAsync(cancel);
            }
            if (window.OpenFile(full) is { } problem)
            {
                throw new InvalidOperationException(problem);
            }
            if (Editors.Active is { } shown)
            {
                await Previewed(shown, cancel);
            }
        }
        else
        {
            throw new FileNotFoundException($"No folder or file at {full}");
        }
        // A folder without a solution has its projects once the walk for them lands, and each project what MSBuild
        // evaluates once its evaluation lands.
        await Session.WorkspaceRead.WaitAsync(cancel);
        await Task.Yield();
        await Session.Models.Idle.WaitAsync(cancel);
        return Snapshot();
    });

    [McpServerTool(Name = "workspace", Title = "The workspace", ReadOnly = true), Description(
        "The open folder and solution, its projects (output type, frameworks), the run configurations and the chosen one, " +
        "the language server's state, the open files (unsaved ones marked) and the active file with its caret.")]
    public Task<Workspace> GetWorkspace(CancellationToken cancel = default) => OnUi(cancel, () => Task.FromResult(Snapshot()));

    [McpServerTool(Name = "open_file", Title = "Show a file", Destructive = false), Description(
        "Opens a file in the editor, at a line and column if given, as the person sees it.")]
    public Task<string> OpenFile(string path, int? line = null, int? column = null, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        OpenDocument document = Document(path);
        await Previewed(document, cancel);
        if (line is int l)
        {
            window.NavigateTo(document.Path, new TextPosition(Math.Max(0, l - 1), Math.Max(0, (column ?? 1) - 1)));
        }
        return $"{Relative(document.Path)} is open";
    });

    /// <summary>Waits for an image's or a font's decode, which lands off the window's thread: one whose first decode
    /// failed has closed its tab, and the agent hears why rather than that the file is open.</summary>
    private async Task Previewed(OpenDocument document, CancellationToken cancel)
    {
        await document.DecodedAsync().WaitAsync(cancel);
        if (document.PreviewFailure is { } failure)
        {
            throw new InvalidOperationException($"{Relative(document.Path)} could not be opened: {failure}");
        }
    }

    [McpServerTool(Name = "editor_text", Title = "A file's text in the editor", ReadOnly = true), Description(
        "A file's text as the editor holds it, unsaved changes included, its lines numbered; opened if it is not.")]
    public Task<string> EditorText(string path, int? from = null, int? to = null, CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        TextSnapshot text = Textual(path).Text.Snapshot;
        int first = Math.Clamp((from ?? 1) - 1, 0, text.LineCount - 1);
        int last = Math.Clamp((to ?? text.LineCount) - 1, first, text.LineCount - 1);
        return Task.FromResult(string.Join('\n', Enumerable.Range(first, last - first + 1).Select(i => $"{i + 1,6}\t{text.GetLineText(i)}")));
    });

    [McpServerTool(Name = "save_all", Title = "Save every file", Destructive = false), Description("Saves every open file with unsaved changes.")]
    public Task<string> SaveAll(CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        int unsaved = Editors.Documents.Count(d => d.IsModified);
        Editors.SaveAll();
        // A read-only file, or one the system refused, keeps its changes, and the agent is told so rather than "Saved".
        List<OpenDocument> left = [.. Editors.Documents.Where(d => d.IsModified)];
        if (left.Count > 0)
        {
            string names = string.Join(", ", left.Select(d => FileText.IsReadOnly(d.Path) ? $"{Relative(d.Path)} (read-only)" : Relative(d.Path)));
            throw new InvalidOperationException($"Saved {unsaved - left.Count} of {unsaved} files; not saved: {names}");
        }
        return Task.FromResult(unsaved == 0 ? "Nothing to save" : $"Saved {unsaved} file{(unsaved == 1 ? "" : "s")}");
    });

    // Code: the language server's

    [McpServerTool(Name = "diagnostics", Title = "Errors and warnings in a file", ReadOnly = true), Description(
        "A file's errors and warnings as the editor marks them, fresh: C#'s from the language server (its messages too); any " +
        "other file's once its checks have run: XAML's (Passenger's own), a project file's (MSBuild's evaluation, NuGet's " +
        "advisories), the web languages' (their server's and ESLint's).")]
    public Task<IReadOnlyList<Finding>> Diagnostics(string path, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        if (Document(path) is { Language.Name: not ("C#" or "Razor") } marked)
        {
            // Its own checks run after it opens or changes, its markers set as they end; the rest of the IDE's work (a build,
            // a test run, a busy console) is not waited for.
            await Until(() => !marked.IsChecking, MarkersLimit, cancel);
            if (Session.Web is { } web && web.For(marked) is { Lsp: null })
            {
                // A web file whose server cannot start has no checks: none found would read as none there.
                throw new InvalidOperationException(web.Unready("Its diagnostics", marked));
            }
            TextSnapshot text = marked.Text.Snapshot;
            return (IReadOnlyList<Finding>)[.. marked.Markers.Where(m => m.Kind is MarkerKind.Error or MarkerKind.Warning).Select(m =>
            {
                TextPosition at = text.GetPosition(Math.Min(m.Range.Start, text.Length));
                return new Finding(m.Kind == MarkerKind.Error ? "error" : "warning", null, m.Message, Relative(marked.Path), at.Line + 1, at.Column + 1);
            })];
        }
        (OpenDocument document, LspDocument lsp) = await Code(path, cancel);
        (IReadOnlyList<Diagnostic> found, _) = await lsp.DiagnosticsAsync(cancel);
        TextSnapshot answered = lsp.Text.Snapshot;
        return (IReadOnlyList<Finding>)[.. found.Select(d =>
        {
            TextPosition at = ServerPlaces.PositionOf(answered, d.Range.Start);
            return new Finding(d.Severity switch { 1 => "error", 2 => "warning", 3 => "info", _ => "hint" }, d.Code?.ToString(), d.Message,
                Relative(document.Path), at.Line + 1, at.Column + 1);
        })];
    });

    [McpServerTool(Name = "symbol_info", Title = "What a symbol is", ReadOnly = true), Description(
        "What the symbol at a position is, as the hover card says: in C#, Razor and the web languages their language server's " +
        "description (declaration and documentation, a JSON property's schema); in XAML the type, property, event, resource or " +
        "element a name is; in a project, .props or .targets file what MSBuild's evaluation makes of a name (a property's " +
        "value and where it was set, an item's items, a target, an import's files).")]
    public Task<string> SymbolInfo(string path, int line, int column, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        Served served = await Serve(path, cancel);
        int offset = Offset(served.Document, line, column);
        switch (served)
        {
            case XamlServed xaml:
                return await XamlSymbolInfo(xaml, offset);
            case MSBuildServed msbuild:
                return await msbuild.MSBuild.DescribeAsync(offset) ?? "Nothing is known about this position";
        }
        Hover? hover = await ((ServerServed)served).Lsp.HoverAsync(offset);
        return hover is null ? "Nothing is known about this position" : Shapes.Text(hover.Contents).Trim();
    });

    [McpServerTool(Name = "definition", Title = "Go to definition", ReadOnly = true), Description(
        "Where the symbol at a position is defined, as Go to Declaration goes; a C# symbol from a package or the framework has " +
        "no source, and says so. From XAML: the C# or XAML declaring a type, property, event, resource or element, a library's " +
        "type decompiled (read-only), or a theme's XAML through its Source Link. In the web languages, their server's; in " +
        "package.json, a dependency's package and an npm run's script. In an MSBuild file: where a property was set (the one " +
        "in force first), an item type or target defined, or the files an import or SDK brings in.")]
    public Task<IReadOnlyList<Place>> Definition(string path, int line, int column, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        Served served = await Serve(path, cancel);
        int offset = Offset(served.Document, line, column);
        switch (served)
        {
            case XamlServed xaml:
                return await XamlDefinition(xaml, offset, cancel);
            case MSBuildServed msbuild:
                return await MSBuildDefinition(msbuild, offset, cancel);
            case ServerServed json when !json.Provides("definitionProvider"):
                return await JsonDefinition(json, offset, cancel);
        }
        return await Places(await ((ServerServed)served).Lsp.DefinitionAsync(offset), cancel);
    });

    [McpServerTool(Name = "usages", Title = "Find usages", ReadOnly = true), Description(
        "Every place the symbol at a position is used, its declaration included, each with its line's text, as Find Usages " +
        "finds them: in C#, Razor and the web languages, their server's (JSON's has none); in XAML, an element's name in its " +
        "file and its code-behind field's uses, or a resource's key across the project's XAML. MSBuild files have none.")]
    public Task<IReadOnlyList<Place>> Usages(string path, int line, int column, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        Served served = await Serve(path, cancel);
        int offset = Offset(served.Document, line, column);
        switch (served)
        {
            case XamlServed xaml:
                return await XamlUsages(xaml, offset, cancel);
            case MSBuildServed:
                throw Lacks(served, "Find Usages", MSBuildHas);
            case ServerServed unfound when !unfound.Provides("referencesProvider"):
                throw new InvalidOperationException($"{unfound.Language} has no usages: its language server finds none, so Find Usages finds nothing in {Path.GetFileName(unfound.Document.Path)}");
        }
        return await Places(await ((ServerServed)served).Lsp.ReferencesAsync(offset), cancel);
    });

    [McpServerTool(Name = "hierarchy", Title = "Call or type hierarchy", ReadOnly = true), Description(
        "One level of the hierarchy of the symbol at a position, as the Hierarchy window: direction callers (each at its " +
        "call), callees, supertypes (base types and interfaces) or subtypes.")]
    public Task<IReadOnlyList<HierarchyEntry>> Hierarchy(string path, int line, int column, string direction = "callers", CancellationToken cancel = default) => OnUi<IReadOnlyList<HierarchyEntry>>(cancel, async () =>
    {
        (OpenDocument document, LspDocument lsp) = await Code(path, cancel);
        int offset = Offset(document, line, column);
        LanguageServer server = Session.CSharp?.Session?.Server ?? throw new InvalidOperationException("C# is not ready");
        bool calls = direction is "callers" or "callees";
        if (!calls && direction is not ("supertypes" or "subtypes"))
        {
            throw new ArgumentException($"No direction '{direction}'; there are callers, callees, supertypes and subtypes", nameof(direction));
        }
        HierarchyItem item = (calls ? await lsp.PrepareCallHierarchyAsync(offset) : await lsp.PrepareTypeHierarchyAsync(offset))
            ?? throw new InvalidOperationException(calls ? "No method, property or event there" : "No type there");
        IReadOnlyList<(HierarchyItem Item, LspRange At)> found = direction switch
        {
            "callers" => [.. (await server.IncomingCallsAsync(item)).Select(c => (c.From, c.FromRanges.FirstOrDefault() ?? c.From.SelectionRange))],
            "callees" => [.. (await server.OutgoingCallsAsync(item)).Select(c => (c.To, c.To.SelectionRange))],
            "supertypes" => [.. (await server.SupertypesAsync(item)).Select(t => (t, t.SelectionRange))],
            _ => [.. (await server.SubtypesAsync(item)).Select(t => (t, t.SelectionRange))],
        };
        IReadOnlyList<Place> places = await Places([.. found.Select(f => new LspLocation(f.Item.Uri, f.At))], cancel);
        return [.. found.Select((f, i) => new HierarchyEntry(f.Item.Name, f.Item.Detail, places[i].Path, places[i].Line, places[i].Column, places[i].Text))];
    });

    [McpServerTool(Name = "implementations", Title = "Go to implementation", ReadOnly = true), Description("The implementations of the interface, abstract or virtual member at a position.")]
    public Task<IReadOnlyList<Place>> Implementations(string path, int line, int column, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (OpenDocument document, LspDocument lsp) = await Code(path, cancel);
        return await Places(await lsp.ImplementationAsync(Offset(document, line, column)), cancel);
    });

    [McpServerTool(Name = "rename", Title = "Rename a symbol", Destructive = true), Description(
        "Renames the symbol at a position everywhere it is used, as Rename does, and saves the files. A type's files named " +
        "after it (Foo.cs, Foo.Designer.cs) take its new name unless renameFiles is false. In XAML: an element's name and its " +
        "code-behind field's uses. In the web languages, their server's; in JSON a property, and in package.json's scripts " +
        "every npm run of one. MSBuild files have none.")]
    public Task<string> Rename(string path, int line, int column, string newName,
        [Description("Rename a type's files named after it too")] bool renameFiles = true,
        CancellationToken cancel = default) => OnUi(cancel, () => Patience.RunAsync(async patience =>
    {
        Served served = await Serve(path, cancel);
        int offset = Offset(served.Document, line, column);
        switch (served)
        {
            case XamlServed xaml:
                return await XamlRename(xaml, offset, newName, cancel);
            case ServerServed json when !json.Provides("renameProvider"):
                return await JsonRename(json, offset, newName);
            case MSBuildServed:
                throw Lacks(served, "Rename", MSBuildHas);
        }
        ServerServed server = (ServerServed)served;
        LspDocument lsp = server.Lsp;
        (string Type, IReadOnlyList<string> Files)? named = server.Web is null && Session.CSharp?.Session?.Folder is { } folder
            ? await TypeRename.NamedAsync(Editors, folder, lsp, offset) : null;
        DateTime asked = DateTime.UtcNow;
        if (await lsp.RenameAsync(offset, newName) is not { } edit)
        {
            return "This can't be renamed";
        }
        if (await WorkspaceEdits.StaleAsync(Editors, lsp, edit, asked, patience) is { } stale)
        {
            throw new InvalidOperationException($"Nothing was renamed: {stale}");
        }
        IReadOnlyList<string> files = [.. Shapes.WorkspaceEdit(edit).Select(f => DocumentUris.ToPath(f.Uri)).Distinct()];
        List<(string From, string To)> moves = renameFiles && named is { } type ? [.. type.Files.Select(f => (f, TypeRename.Renamed(f, newName)))] : [];
        TypeRename.Apply(Editors, edit, moves, $"Rename {named?.Type ?? "symbol"} to {newName}", null, []);
        Editors.SaveAll();
        string renamed = moves.Count > 0 ? $"; renamed {string.Join(", ", moves.Select(m => $"{Relative(m.From)} to {Relative(m.To)}"))}" : string.Empty;
        return $"Renamed in {files.Count} file{(files.Count == 1 ? "" : "s")}: {string.Join(", ", files.Select(Relative))}{renamed}";
    }, cancel));

    [McpServerTool(Name = "code_actions", Title = "Fixes and refactorings", ReadOnly = true), Description(
        "The fixes and refactorings offered at a position (Alt+Enter), or over a selection to endLine:endColumn (where Extract " +
        "Method and Introduce Variable are), as 'title [kind]', each followed by the choices it offers (actions nested under " +
        "it, a Fix All's scopes, where a new XAML resource or style goes), which apply_code_action takes as choice. In C#, " +
        "Razor and the web languages, the language server's, ESLint's fixes first where it checks the file; in XAML, " +
        "Passenger's own: the line's problems' fixes (a binding's missing property or command, a missing converter), Extract " +
        "Resource and Extract Style. MSBuild files have none.")]
    public Task<IReadOnlyList<string>> CodeActions(string path, int line, int column, int? endLine = null, int? endColumn = null, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        Served served = await Serve(path, cancel);
        switch (served)
        {
            case XamlServed xaml:
                return (IReadOnlyList<string>)[.. (await XamlActions(xaml, Offset(xaml.Document, line, column)))
                    .Select(a => a.Choices.Count == 0 ? $"{a.Title} [{a.Kind}]" : $"{a.Title} [{a.Kind}], choices: {string.Join("; ", a.Choices)}")];
            case MSBuildServed:
                throw Lacks(served, "fixes or refactorings", MSBuildHas);
        }
        IReadOnlyList<EslintFix> linted = await LintFixes(served, Offset(served.Document, line, column));
        return (IReadOnlyList<string>)[.. linted.Select(f => f.Action.Kind is { Length: > 0 } kind ? $"{f.Title} [{kind}]" : f.Title),
            .. (await Actions((ServerServed)served, line, column, endLine, endColumn)).Select(Described)];
    });

    [McpServerTool(Name = "apply_code_action", Title = "Apply a fix or refactoring", Destructive = true), Description(
        "Applies the fix or refactoring with this title at a position (or over a selection to endLine:endColumn), as choosing " +
        "it from Alt+Enter does, and saves the files; of one that offers choices, choice names the one (as code_actions lists " +
        "them). XAML's Extract Resource and Extract Style take name as the new key or style's name (else their dialog's), " +
        "replace every use of the value and extract the element's properties that are not bindings.")]
    public Task<string> ApplyCodeAction(string path, int line, int column, string title, int? endLine = null, int? endColumn = null,
        string? choice = null,
        string? name = null,
        CancellationToken cancel = default) => OnUi(cancel, () => Patience.RunAsync(async patience =>
    {
        Served served = await Serve(path, cancel);
        switch (served)
        {
            case XamlServed xaml:
                return await ApplyXamlAction(xaml, Offset(xaml.Document, line, column), title, choice, name);
            case MSBuildServed:
                throw Lacks(served, "fixes or refactorings", MSBuildHas);
        }
        ServerServed server = (ServerServed)served;
        IReadOnlyList<EslintFix> linted = await LintFixes(served, Offset(served.Document, line, column));
        if (linted.FirstOrDefault(f => f.Title == title) is { } lint)
        {
            return await ApplyLintFix(server, lint);
        }
        // Listed now, with any edit the server gives them as it lists them.
        DateTime asked = DateTime.UtcNow;
        LspDocument lsp = server.Lsp;
        IReadOnlyList<CodeAction> actions = await Actions(server, line, column, endLine, endColumn);
        CodeAction chosen = actions.FirstOrDefault(a => a.Title == title)
            ?? throw new InvalidOperationException($"No action '{title}' here; there are: {string.Join("; ", [.. linted.Select(f => f.Title), .. actions.Select(a => a.Title)])}");
        IReadOnlyList<ActionChoice> choices = ClientCommands.Choices(chosen);
        string? scope = null;
        if (choices.Count > 0)
        {
            ActionChoice taken = choices.FirstOrDefault(c => c.Text == choice)
                ?? throw new InvalidOperationException($"'{title}' needs a choice{(choice is null ? "" : $", and '{choice}' is not one")}: {string.Join("; ", choices.Select(c => c.Text))}");
            (chosen, scope) = (taken.Action, taken.Scope);
        }
        else if (choice is not null)
        {
            throw new InvalidOperationException($"'{title}' offers no choices");
        }
        string what = choice is null ? $"'{title}'" : $"'{title}' ({choice})";
        chosen = await lsp.ResolveChosenAsync(chosen, scope);
        string unrun = chosen.Command is { } command ? $", but not {command.Command}, which Passenger cannot run" : string.Empty;
        if (chosen.Edit is not { } edit)
        {
            return $"{what} changes nothing it can apply{unrun}";
        }
        if (await WorkspaceEdits.StaleAsync(Editors, lsp, edit, asked, patience) is { } stale)
        {
            throw new InvalidOperationException($"{what} was not applied: {stale}");
        }
        int files = WorkspaceEdits.ApplyAsCommand(Editors, edit, title);
        Editors.SaveAll();
        return $"Applied {what} to {files} file{(files == 1 ? "" : "s")}{unrun}";
    }, cancel));

    /// <summary>A code action as code_actions lists it: its title, its kind, and the choices it offers.</summary>
    private static string Described(CodeAction action)
    {
        string described = action.Kind is { Length: > 0 } kind ? $"{action.Title} [{kind}]" : action.Title;
        IReadOnlyList<ActionChoice> choices = ClientCommands.Choices(action);
        return choices.Count == 0 ? described : $"{described}, choices: {string.Join("; ", choices.Select(c => c.Text))}";
    }

    [McpServerTool(Name = "format", Title = "Reformat a file", Destructive = true), Description(
        "Reformats a file as Reformat Code does, and saves it: C# and Razor by the language server, XAML by Rider's XAML style " +
        "under its .editorconfig, the web languages by the project's Prettier where it formats the file, else by their server. " +
        "MSBuild files have none.")]
    public Task<string> Format(string path, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        if (Passenger.App.Xaml.XamlService.IsXaml(Resolve(path)))
        {
            return await FormatXamlAsync(Document(path));
        }
        Served file = await Serve(path, cancel);
        ServerServed served = file as ServerServed ?? throw Lacks(file, "Reformat Code", MSBuildHas);
        if (served.Web is not null && await Prettify(served) is { } prettified)
        {
            return prettified;
        }
        (OpenDocument document, LspDocument lsp) = (served.Document, served.Lsp);
        window.OpenFile(document.Path);
        using Passenger.Text.ChangeTracker asked = new(document.Text);
        IReadOnlyList<LspTextEdit> edits = await lsp.FormatAsync(document.Indentation.Options);
        if (WorkspaceEdits.Carried(asked, edits, document.Indentation.Options) is not { } placed)
        {
            return "Not reformatted: the file was edited while the language server worked on it";
        }
        WorkspaceEdits.ApplyTo(Editors, document, placed);
        document.Save();
        return edits.Count == 0 ? "Already formatted" : $"Reformatted ({edits.Count} edits)";
    });

    /// <summary>A XAML file reformatted, saved: Passenger's own layout, by the window's XAML code style with the file's
    /// .editorconfig over it, which is Code Cleanup's in XAML too.</summary>
    private async Task<string> FormatXamlAsync(OpenDocument markup)
    {
        window.OpenFile(markup.Path);
        using Passenger.Text.ChangeTracker tracked = new(markup.Text);
        Passenger.Xaml.Features.XamlFormatted formatted = await CodeAssist.FormatXamlAsync(markup, window.Settings.XamlFormat);
        if (formatted.Problem is { } problem)
        {
            return $"Not reformatted: {problem}";
        }
        if (tracked.Map(formatted.Edits) is not { } laid)
        {
            return "Not reformatted: the file was edited meanwhile";
        }
        if (laid.Count > 0)
        {
            WorkspaceEdits.ApplyTo(Editors, markup, laid);
            markup.Save();
        }
        return laid.Count == 0 ? "Already formatted" : $"Reformatted ({laid.Count} edits)";
    }

    [McpServerTool(Name = "find_symbol", Title = "Go to symbol", ReadOnly = true), Description(
        "Types, members and other symbols of the solution's own C# and Razor code (not packages') whose names match a query by " +
        "substring, initials or camel humps (\"GH\" finds GetHashCode), as Go to Symbol finds them. A XAML, web or MSBuild " +
        "file's names are in its outline.")]
    public Task<IReadOnlyList<SymbolFound>> FindSymbol(
        string query,
        [Description("class, interface, struct, enum, method, property, field, event, constructor...")] string? kind = null,
        int limit = 50, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        RoslynSession session = await Server(cancel);
        IReadOnlyList<SymbolInformation> found = await session.SymbolsAsync(query);
        // The server's order is not by how well a name matches: the query's own name first, so a limit never cuts it.
        List<SymbolInformation> kept = [.. found
            .Where(s => kind is null || SymbolKinds.Name(s.Kind).Equals(kind, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => NameMatch(s.Name, query))
            .Take(limit)];
        IReadOnlyList<Place> places = await Places([.. kept.Select(s => s.Location)], cancel);
        return (IReadOnlyList<SymbolFound>)[.. kept.Select((s, i) => new SymbolFound(s.Name, SymbolKinds.Name(s.Kind), s.ContainerName, places[i].Path, places[i].Line, places[i].Column))];
    });

    /// <summary>How well a symbol's name matches a query: the name itself, then in another letter case, then one that starts
    /// with it, then one that has it, then Go to Symbol's other matches (initials, humps).</summary>
    private static int NameMatch(string name, string query) =>
        name == query ? 0
        : name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 1
        : name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 2
        : name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 3
        : 4;

    [McpServerTool(Name = "outline", Title = "A file's structure", ReadOnly = true), Description(
        "A file's structure, indented, each part with its line: a C# file's types and members; a web language's symbols as its " +
        "server outlines them; a XAML file's elements with what names them (x:Class, x:Name, x:Key, a style's selector or " +
        "target type); an MSBuild file's properties with their values, items, targets and imports.")]
    public Task<string> Outline(string path, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        Served served = await Serve(path, cancel);
        switch (served)
        {
            case XamlServed xaml:
                return XamlOutline(xaml);
            case MSBuildServed msbuild:
                return MSBuildOutline(msbuild);
        }
        LspDocument lsp = ((ServerServed)served).Lsp;
        System.Text.StringBuilder text = new();
        IReadOnlyList<DocumentSymbol> outline = await lsp.OutlineAsync();
        TextSnapshot answered = lsp.Text.Snapshot;
        Write(outline, 0);
        return text.Length == 0 ? "Nothing is declared in this file" : text.ToString().TrimEnd();

        void Write(IReadOnlyList<DocumentSymbol> symbols, int depth)
        {
            foreach (DocumentSymbol symbol in symbols)
            {
                text.Append(' ', depth * 2).Append(symbol.Name);
                if (!string.IsNullOrEmpty(symbol.Detail) && symbol.Detail != symbol.Name)
                {
                    text.Append(" : ").Append(symbol.Detail);
                }
                text.Append(" (").Append(SymbolKinds.Name(symbol.Kind)).Append(", line ").Append(ServerPlaces.PositionOf(answered, symbol.SelectionRange.Start).Line + 1).Append(")\n");
                Write(symbol.Children ?? [], depth + 1);
            }
        }
    });

    // Build and run

    [McpServerTool(Name = "build", Title = "Build", Destructive = true), Description(
        "Builds the solution (or the folder's project) as Build does and returns whether it succeeded, how it ended (failed, " +
        "stopped, refused while another build runs, unable to start) and every error and warning, with MSBuild's last lines " +
        "(outputTail) when it did not succeed; build_output has them always.")]
    public Task<BuildResult> Build(CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        AgentCall.Follow(window.Actions.Work, AppActions.Build);
        // The build goes on without an agent that gives up on it, shown in the Build window.
        BuildOutcome built = await Builds.BuildAsync(BuildCause.Explicit, CancellationToken.None).WaitAsync(cancel);
        IReadOnlyList<Finding> problems = [.. built.Problems.Select(p => new Finding(
            p.Severity == ProblemSeverity.Error ? "error" : "warning", p.Code, p.Message, p.Path is { } at ? Relative(at) : p.Project, p.Position.Line + 1, p.Position.Column + 1))];
        return new BuildResult(built.Succeeded, built.Message, problems.Count(p => p.Severity == "error"), problems.Count(p => p.Severity == "warning"), problems,
            built.Succeeded ? null : string.Join('\n', built.Output));
    });

    [McpServerTool(Name = "run", Title = "Run a configuration", Destructive = true), Description(
        "Runs a run configuration (the chosen one by default) as Run does, built first when it says so, and waits up to " +
        "waitSeconds for it to end or an Avalonia or WPF app's UI to be up. Returns its output so far and its exit code once " +
        "it has one; a run refused (one running already, nothing to run, a failed build) is an error. A configuration that " +
        "uses the terminal runs in a Terminal tab, whose text is its output; terminal_run types into it.")]
    public Task<RunResult> Run(
        string? configuration = null,
        [Description(Arguments)] string? arguments = null,
        int waitSeconds = 30,
        [Description("Under dotnet watch with Hot Reload (Settings' choice by default): hot_reload, or a save, applies the edits")] bool? hotReload = null,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        Choose(configuration);
        string name = Session.Configurations.Chosen?.Name ?? string.Empty;
        BuildRunController builds = Builds;
        AgentCall.Follow(window.Actions.Work, AppActions.RunProgram, AppActions.Build);
        if (await builds.RunAsync(arguments, hotReload).WaitAsync(cancel) is { } refused)
        {
            throw new InvalidOperationException(WithBuildErrors(refused));
        }
        Task<int> ended = builds.RunEnd;
        AgentCall.Say($"Running {name}: waiting up to {Math.Max(0, waitSeconds)} s for it to end or its UI to be up");
        // An app with a window never ends by itself: the wait is over once its UI is up, which the app_ tools look at.
        LiveTree.LiveApp? app = Session.RunApp;
        await Until(() => ended.IsCompleted || app is { State: not LiveTree.LiveAppState.Waiting }, TimeSpan.FromSeconds(Math.Max(0, waitSeconds)), cancel);
        int? code = ended.IsCompletedSuccessfully ? ended.Result : null;
        string output = Session.InTerminal is { } inTerminal ? Tail(inTerminal.Session, 200) : Tail(Session.Runner.Shown(), 200);
        string status = code is int exit ? BuildRunController.RunEnded(name, exit)
            : app is { State: LiveTree.LiveAppState.Connected } ? $"Running {name}, its UI up: app_windows, app_screenshot and app_click look at it and use it"
            : $"Running {name}";
        return new RunResult(code is null, code, status, output);
    });

    [McpServerTool(Name = "run_output", Title = "What Run's program wrote", ReadOnly = true), Description(
        "The last lines the program Run runs (or last ran) has written: the Run window's console (under Hot Reload, dotnet " +
        "watch's words too), or a terminal configuration's tab as it shows now.")]
    public Task<string> RunOutput(int lines = 200, CancellationToken cancel = default) => OnUi(cancel, () => Task.FromResult(
        Session.InTerminal is { } inTerminal ? Tail(inTerminal.Session, lines) : Tail(Session.Runner.Shown(), lines)));

    [McpServerTool(Name = "stop", Title = "Stop", Destructive = false), Description("Stops whatever is running or being debugged, as Stop does.")]
    public Task<string> Stop(CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        Session.Runner.Stop();
        Session.InTerminal?.Stop();
        Session.Profiled?.Run.Kill();
        await Session.Debug.StopAsync();
        return "Stopped";
    });

    // Unit tests

    [McpServerTool(Name = "tests_run", Title = "Run unit tests", Destructive = true), Description(
        "Builds and runs unit tests as the Unit Tests window does, each test project through the runner its build asks for (a " +
        "Microsoft Testing Platform application, as xUnit v3, MSTest's runner or TUnit, through the platform's protocol; any " +
        "other through VSTest): all, or those whose full name contains filter. With debug, under the debugger: a stop in a " +
        "test comes back, and debug_continue goes on. Returns the tally and every failure with its message and stack trace.")]
    public Task<TestRunInfo> TestsRun(
        [Description("Part of a test's full name (Namespace.Class.Method)")] string? filter = null,
        bool debug = false, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        string folder = Session.Folder ?? throw new InvalidOperationException("No folder is open");
        Testing.TestController tests = Session.Tests;
        List<Testing.TestNode> Matching() =>
            [.. tests.Tree.Methods.Where(m => m.FullName is { } name && name.Contains(filter!, StringComparison.Ordinal))];
        // A test the code's reading does not know (an attribute of the project's own) is found by a runner.
        if (filter is not null && Matching().Count == 0 && await tests.DiscoverAsync(folder).WaitAsync(cancel) is { } discovery)
        {
            throw new InvalidOperationException(discovery);
        }
        List<Testing.TestNode>? chosen = filter is null ? null : Matching();
        if (chosen is { Count: 0 })
        {
            throw new InvalidOperationException($"No test's name contains '{filter}'");
        }
        AgentCall.Say(chosen is null ? "Building and running every test" : $"Building and running {chosen.Count} test{(chosen.Count == 1 ? string.Empty : "s")}");
        AgentCall.Follow(window.Actions.Work, AppActions.Build);
        Task<string?> run = tests.RunAsync(folder, chosen, debug);
        if (debug)
        {
            // A debugged test that stops is the agent's to look at: the stop comes back, the run goes on after it.
            await Until(() => run.IsCompleted || Session.Debug.Session is { State: DebugState.Paused }, TimeSpan.FromMinutes(5), cancel);
            if (!run.IsCompleted)
            {
                await Until(() => Session.Debug.Stop is not null, TimeSpan.FromSeconds(5), cancel);
                return TestInfo(tests) with { Debugger = await DebugSnapshotAsync() };
            }
        }
        if (await run.WaitAsync(cancel) is { } problem)
        {
            throw new InvalidOperationException(WithBuildErrors(problem));
        }
        return TestInfo(tests);
    });

    [McpServerTool(Name = "tests_list", Title = "The unit tests", ReadOnly = true), Description(
        "The tests the Unit Tests window shows (from the test projects' code, and their runner once built), each with its full " +
        "name, its state from the last run (passed, failed, skipped, notRun) and file:line: those whose full name contains " +
        "filter and whose state is state, at most limit.")]
    public Task<IReadOnlyList<TestInfoRow>> TestsList(
        [Description("Part of a test's full name (Namespace.Class.Method)")] string? filter = null,
        [Description("passed, failed, skipped or notRun")] string? state = null,
        int limit = 200,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        Testing.TestState? only = state is null ? null
            : Enum.TryParse(state, ignoreCase: true, out Testing.TestState parsed) && Enum.IsDefined(parsed) ? parsed
            : throw new ArgumentException($"No state '{state}'; there are passed, failed, skipped and notRun");
        // Found from code as the folder opens: an agent's first look waits for that, as the window says it is looking.
        await Until(() => !Session.Tests.IsLooking, TestsLookLimit, cancel);
        return (IReadOnlyList<TestInfoRow>)[.. Session.Tests.Tree.Methods
            .Where(m => (filter is null || (m.FullName ?? m.Name).Contains(filter, StringComparison.Ordinal)) && (only is null || m.State == only))
            .Take(Math.Max(1, limit))
            .Select(m => new TestInfoRow(TestName(m), m.State.ToString().ToLowerInvariant(), m.Path is { } at ? $"{Relative(at)}:{m.Line}" : null))];
    });

    /// <summary>How long tests_list waits for the tests to be found from the test projects' code.</summary>
    private static readonly TimeSpan TestsLookLimit = TimeSpan.FromSeconds(60);

    private TestRunInfo TestInfo(Testing.TestController tests)
    {
        List<Testing.TestNode> failed = [.. tests.Tree.Methods.Where(m => m.State == Testing.TestState.Failed)];
        return new TestRunInfo(tests.Summary, [.. failed.Select(m => new TestFailure(TestName(m), m.Path is { } at ? $"{Relative(at)}:{m.Line}" : null, m.Details))]);
    }

    /// <summary>A test's full name, Namespace.Class.Method, and the target framework it ran under when its project has several.</summary>
    private static string TestName(Testing.TestNode method)
    {
        string name = method.FullName ?? method.Name;
        return method.Framework is { } framework ? $"{name} ({framework})" : name;
    }

    // The debugger

    [McpServerTool(Name = "breakpoint_set", Title = "Set a breakpoint", Destructive = true), Description(
        "Sets a breakpoint, or changes the one there: a line breakpoint at path and line; a CLR exception breakpoint on " +
        "exception, stopping in its scenarios (Rider's Suspend If; a new one on a type in all of them); XAML Binding Errors " +
        "with bindingErrors true, stopping a debugged Avalonia or WPF app as a binding fails, its condition and logMessage " +
        "reading $bindingError (Control, Property, Path, FailedPart, DataContext, Message, Xaml); or, with control and " +
        "property, the Live Visual Tree's Break When Value Changes on a property of the debugged app's element (as app_details " +
        "lists them), stopping in the code that changes it (a style's, a binding's, code-behind's), its condition reading " +
        "newValue, oldValue and control, for the session. condition is a C# bool expression, hitCondition \"==3\", \">=2\" or " +
        "\"%2\"; logMessage (\"count is {count}\") is written on each hit, and suspend false makes a tracepoint. Bound at once " +
        "while debugging. In .axaml a line's breakpoint stops where the first element or attribute on it is made or set; with " +
        "attribute, where that property is set as its element is made.")]
    public Task<IReadOnlyList<BreakpointInfo>> SetBreakpoint(
        string? path = null,
        int? line = null,
        [Description("In .axaml, an attribute on the line, as written")] string? attribute = null,
        [Description("An exception type's full name, or \"Any exception\"")] string? exception = null,
        bool bindingErrors = false,
        [Description("For a property breakpoint, the element: " + ControlQuery)] string? control = null,
        [Description("For a property breakpoint, its property as app_details names it (Text, Background, Grid.Row)")] string? property = null,
        string? condition = null,
        string? hitCondition = null,
        string? logMessage = null,
        bool suspend = true,
        [Description("Where it was thrown (thrownInUserCode, thrownInExternalCode) and how it is handled (handledInUserCode, handledInExternalCode, " +
            "unhandled): it stops when one of each holds")] IReadOnlyList<string>? scenarios = null,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        BreakpointStore store = Session.Debug.Breakpoints;
        if (bindingErrors)
        {
            store.Update(store.BindingErrors, enabled: true, suspend, condition, hitCondition, logMessage);
            await InTheProgram(cancel);
            return ExceptionBreakpoints();
        }
        if (control is { Length: > 0 } || property is { Length: > 0 })
        {
            (long element, string described, string name) = await WatchedProperty(control, property, cancel);
            PropertyBreakpoint watched = store.AddProperty(element, described, name);
            store.Update(watched, enabled: true, suspend, condition, hitCondition, logMessage);
            await InTheApp(cancel);
            return DataBreakpoints();
        }
        if (exception is { Length: > 0 })
        {
            ExceptionBreakpoint breakpoint = store.FindException(exception) ?? store.AddException(exception);
            if (scenarios is not null)
            {
                store.SetScenarios(breakpoint, ExceptionScenarioNames.Parse(scenarios));
            }
            store.Update(breakpoint, enabled: true, suspend, condition, hitCondition, logMessage);
            await InTheProgram(cancel);
            return ExceptionBreakpoints();
        }
        (string full, int at) = LinePlace(path, line);
        LineBreakpoint set = store.Set(full, at, attribute is { Length: > 0 } ? attribute : null);
        store.Update(set, enabled: true, suspend, condition, hitCondition, logMessage);
        await InTheProgram(cancel);
        return Breakpoints(full);
    });

    [McpServerTool(Name = "breakpoint_remove", Title = "Remove a breakpoint", Destructive = false), Description(
        "Removes the breakpoint on a line (or on an .axaml attribute of it), or the exception breakpoint on a type (\"Any " +
        "exception\" is only turned off, by breakpoint_set's scenarios); with bindingErrors true turns XAML Binding Errors " +
        "off; with control and property, removes that property breakpoint.")]
    public Task<IReadOnlyList<BreakpointInfo>> RemoveBreakpoint(string? path = null, int? line = null,
        [Description("In .axaml, the attribute")] string? attribute = null,
        [Description("An exception type's full name")] string? exception = null,
        bool bindingErrors = false,
        [Description("For a property breakpoint, the element: " + ControlQuery)] string? control = null,
        [Description("For a property breakpoint, its property")] string? property = null,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        BreakpointStore store = Session.Debug.Breakpoints;
        if (control is { Length: > 0 } || property is { Length: > 0 })
        {
            if (control is not { Length: > 0 } || property is not { Length: > 0 })
            {
                throw new ArgumentException("A property breakpoint is named by its control and its property, both");
            }
            string wanted = property.Trim();
            // By the id breakpoints shows, else the element the query finds now (the app paused answers no query).
            PropertyBreakpoint? watched = store.Properties.FirstOrDefault(p => Id(p.Element) == control.Trim() && p.Property.Equals(wanted, StringComparison.OrdinalIgnoreCase));
            if (watched is null)
            {
                (long element, _, _) = await WatchedElement(control, cancel);
                watched = store.Properties.FirstOrDefault(p => p.Element == element && p.Property.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"No property breakpoint is on {wanted} of {control}; breakpoints lists those there are");
            }
            store.Remove(watched);
            await InTheApp(cancel);
            return DataBreakpoints();
        }
        if (bindingErrors)
        {
            BindingErrorBreakpoint failures = store.BindingErrors;
            store.Update(failures, enabled: false, failures.Suspend, failures.Condition, failures.HitCondition, failures.LogMessage);
            await InTheProgram(cancel);
            return ExceptionBreakpoints();
        }
        if (exception is { Length: > 0 })
        {
            if (store.FindException(exception) is { IsAny: false } breakpoint)
            {
                store.Remove(breakpoint);
            }
            await InTheProgram(cancel);
            return ExceptionBreakpoints();
        }
        (string full, int at) = LinePlace(path, line);
        string? on = attribute is { Length: > 0 } ? attribute : null;
        if (store.In(full).FirstOrDefault(b => b.Line == at && b.Attribute == on) is { } set)
        {
            store.Remove(set);
        }
        await InTheProgram(cancel);
        return Breakpoints(full);
    });

    /// <summary>The element of the debugged app a query names, as a property breakpoint describes it (Type#Name), with its
    /// details' properties.</summary>
    private async Task<(long Element, string Described, IReadOnlyList<Passenger.Inspector.PropertyValue> Properties)> WatchedElement(string control, CancellationToken cancel)
    {
        if (Session.LiveApp is null)
        {
            throw new InvalidOperationException(Session.Debug.IsDebugging
                ? $"A property breakpoint watches an element of the app being debugged, and what is being debugged is not {Inspectable}"
                : "A property breakpoint watches an element of the app being debugged: debug_start one first");
        }
        LiveTree.LiveApp live = await InspectedAsync("debug", cancel);
        (List<LiveTree.AppNode> nodes, _) = await TreeOf(live, null, cancel);
        Passenger.Inspector.Element element = LiveTree.AppQuery.One(nodes, control).Element;
        Passenger.Inspector.ElementDetails details = await live.DetailsAsync(element.Id);
        return (element.Id, details.Name is { Length: > 0 } name ? $"{details.Type}#{name}" : details.Type, details.Properties);
    }

    /// <summary>A property breakpoint's element and property, the property by the name the element's details list it under.</summary>
    private async Task<(long Element, string Described, string Property)> WatchedProperty(string? control, string? property, CancellationToken cancel)
    {
        if (control is not { Length: > 0 } || property is not { Length: > 0 })
        {
            throw new ArgumentException("A property breakpoint needs both control, the element, and property, the property of it to watch");
        }
        (long element, string described, IReadOnlyList<Passenger.Inspector.PropertyValue> properties) = await WatchedElement(control, cancel);
        string wanted = property.Trim();
        Passenger.Inspector.PropertyValue found = properties.FirstOrDefault(p => p.Name == wanted)
            ?? properties.FirstOrDefault(p => p.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"{described} has no property {wanted}; app_details lists the properties it has");
        return (element, described, found.Name);
    }

    /// <summary>Waits for the debugger and the inspector in the app to take a property breakpoint's change, so the agent's next
    /// click or key finds the property watched; while the app is paused, the inspector watches it once the app runs.</summary>
    private async Task InTheApp(CancellationToken cancel)
    {
        await InTheProgram(cancel);
        await Task.WhenAny(window.PropertyWatches, Task.Delay(BreakpointLimit, cancel));
        cancel.ThrowIfCancellationRequested();
    }

    /// <summary>How long a breakpoint change waits for the debugger to take it into the program running.</summary>
    private static readonly TimeSpan BreakpointLimit = TimeSpan.FromSeconds(10);

    /// <summary>Waits for the debugger to answer the breakpoint changes just made, so the program has them (and the answer
    /// says whether each bound) before the agent's next step: a click or key into the app, which goes no other way.</summary>
    private async Task InTheProgram(CancellationToken cancel)
    {
        if (Session.Debug.Session is { State: DebugState.Running or DebugState.Paused } debugged)
        {
            await Task.WhenAny(debugged.BreakpointsAnswered, Task.Delay(BreakpointLimit, cancel));
            cancel.ThrowIfCancellationRequested();
        }
    }

    [McpServerTool(Name = "breakpoints", Title = "Every breakpoint", ReadOnly = true), Description(
        "Every breakpoint: the line breakpoints (an .axaml one naming its attribute), the session's data and property " +
        "breakpoints, then the CLR exception breakpoints (\"Any exception\" among them) with their scenarios and XAML Binding " +
        "Errors (bindingErrors), each with whether the debugger took it. While Mute Breakpoints (ide_action " +
        "app.debug.muteBreakpoints) is on, none stops.")]
    public Task<IReadOnlyList<BreakpointInfo>> ListBreakpoints(CancellationToken cancel = default) =>
        OnUi(cancel, () => Task.FromResult<IReadOnlyList<BreakpointInfo>>([.. Breakpoints(null), .. DataBreakpoints(), .. ExceptionBreakpoints()]));

    [McpServerTool(Name = "debug_start", Title = "Debug a configuration", Destructive = true), Description(
        "Builds and launches a run configuration (the chosen one by default) under the debugger, as Debug does, and waits up " +
        "to waitSeconds for a stop at a breakpoint or exception, its end, or an Avalonia or WPF app's UI to be up. Returns " +
        "where it is.")]
    public Task<DebugSnapshot> DebugStart(
        string? configuration = null,
        [Description(Arguments)] string? arguments = null,
        int waitSeconds = 60, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        Choose(configuration);
        AgentCall.Follow(window.Actions.Work, AppActions.Debug, AppActions.Build);
        if (await Builds.DebugAsync(arguments).WaitAsync(cancel) is { } problem)
        {
            throw new InvalidOperationException(WithBuildErrors(problem));
        }
        return await AfterLaunch(TimeSpan.FromSeconds(waitSeconds), cancel);
    });

    /// <summary>A run, debug, test or profile refused for a failed build, with the build's errors after it (path:line:column,
    /// code and message, at most ten), so an agent fixes them without asking problems first.</summary>
    private string WithBuildErrors(string refused)
    {
        List<Problem> errors = [.. window.BottomPanel.Problems.Problems.Where(p => p.Severity == ProblemSeverity.Error)];
        if (errors.Count == 0 || !refused.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return refused;
        }
        IEnumerable<string> lines = errors.Take(10).Select(p =>
            $"{(p.Path is { } at ? $"{Relative(at)}:{p.Position.Line + 1}:{p.Position.Column + 1}" : p.Project)}: {p.Code} {p.Message}");
        return $"{refused}:\n{string.Join('\n', lines)}{(errors.Count > 10 ? $"\n… {errors.Count - 10} more: problems lists them" : "")}";
    }

    /// <summary>How long a launched app whose UI is up is watched for a stop in its first layout.</summary>
    private static readonly TimeSpan LaunchSettle = TimeSpan.FromSeconds(2);

    private static TimeSpan Min(TimeSpan one, TimeSpan other) => one < other ? one : other;

    /// <summary>Where a program just launched under the debugger is once it stops or ends, or, for an app with a window,
    /// which runs until it is closed, once its UI is up, saying so: an agent waits for no more than it needs.</summary>
    private async Task<DebugSnapshot> AfterLaunch(TimeSpan limit, CancellationToken cancel)
    {
        LiveTree.LiveApp? app = Session.LiveApp;
        DateTime until = DateTime.UtcNow + limit;
        AgentCall.Say($"Debugging: waiting up to {limit.TotalSeconds:0} s for a stop, the program's end or its UI");
        Func<bool> going = () => Session.Debug.Session is { State: DebugState.Running or DebugState.Starting };
        await Until(() => !going() || app is { State: not LiveTree.LiveAppState.Waiting }, limit, cancel);
        if (going() && app is { State: LiveTree.LiveAppState.Connected })
        {
            // The inspector says hello as the app's dispatcher first runs, a moment before the first layout, where a breakpoint
            // or a failed binding may stop it: that stop is waited for too.
            await Until(() => !going(), Min(LaunchSettle, until - DateTime.UtcNow), cancel);
        }
        DebugSnapshot started = await AfterResume(TimeSpan.Zero, cancel);
        return started.State == "running" && app is { State: LiveTree.LiveAppState.Connected }
            ? started with { Status = $"{started.Status}, its UI up: app_windows, app_screenshot and app_click look at it and use it" }
            : started;
    }

    [McpServerTool(Name = "debug_processes", Title = "The .NET programs to attach to", ReadOnly = true), Description(
        "The .NET programs running here that the debugger can attach to: each one's process id, executable, program (the .dll " +
        "the dotnet host runs, else the executable), user and command line.")]
    public Task<IReadOnlyList<ProcessInfo>> DebugProcesses(CancellationToken cancel = default) => OnUi(cancel, async () =>
        (IReadOnlyList<ProcessInfo>)[.. (await BackgroundWork.Run(DotnetProcesses.List)).Select(p => new ProcessInfo(p.Id, p.Name, p.Program, p.User, p.CommandLine))]);

    [McpServerTool(Name = "debug_attach", Title = "Attach to a running program", Destructive = true), Description(
        "Attaches the debugger to a running .NET program, as Attach to Process does: by processId, or by program when one " +
        "running program has that name (debug_processes lists them). The breakpoints apply at once; it waits up to waitSeconds " +
        "for a stop or its end and returns where it is. debug_detach lets it go running on; stop ends it.")]
    public Task<DebugSnapshot> DebugAttach(int? processId = null, [Description("A program's name, as debug_processes gives it")] string? program = null,
        int waitSeconds = 10, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        IReadOnlyList<DotnetProcess> running = await BackgroundWork.Run(DotnetProcesses.List);
        List<DotnetProcess> named = processId is int id ? [.. running.Where(p => p.Id == id)]
            : program is { Length: > 0 } name ? [.. running.Where(p => p.Program.Equals(name, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))]
            : throw new ArgumentException("Name the program to attach to by its processId, or by its program's name");
        DotnetProcess chosen = named switch
        {
            [{ } only] => only,
            [] => throw new InvalidOperationException($"No .NET program {(processId is int pid ? $"with process id {pid}" : $"named {program}")} is running for the debugger to attach to; debug_processes lists those there are"),
            _ => throw new InvalidOperationException($"{named.Count} running programs are named {program}: name one by its processId ({string.Join(", ", named.Select(p => p.Id))})"),
        };
        if (await window.AttachAsync(chosen) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }
        return await AfterResume(TimeSpan.FromSeconds(waitSeconds), cancel);
    });

    [McpServerTool(Name = "debug_detach", Title = "Detach", Destructive = false), Description(
        "Lets go of the program the debugger attached to, as Detach does: its breakpoints are taken out and it runs on.")]
    public Task<string> DebugDetach(CancellationToken cancel = default) => OnUi(cancel, async () =>
        await Session.Debug.DetachAsync() is { } problem ? throw new InvalidOperationException(problem) : Session.Debug.Summary ?? "Detached");

    [McpServerTool(Name = "debug_state", Title = "Where the debugger is", ReadOnly = true), Description(
        "The debugger's state and, while paused, the stop: its reason, the stopped thread's frames and the chosen frame's " +
        "variables; in a debugged Avalonia app also the binding values of the .axaml files open in the editor (open_file one " +
        "to see them), read on its UI thread.")]
    public Task<DebugSnapshot> GetDebugState(CancellationToken cancel = default) => OnUi(cancel, DebugSnapshotAsync);

    [McpServerTool(Name = "debug_continue", Title = "Resume", Destructive = false), Description(
        "Resumes the program and waits up to waitSeconds for it to stop again or end. A debugged test run that ends with it " +
        "answers its tally and failures too (tests), as tests_run does.")]
    public Task<DebugSnapshot> DebugContinue(int waitSeconds = 30, CancellationToken cancel = default) => Resume(s => s.ContinueAsync(), waitSeconds, cancel);

    [McpServerTool(Name = "debug_step", Title = "Step", Destructive = false), Description("Steps over, into or out of the current line, and waits for the step to stop.")]
    public Task<DebugSnapshot> DebugStep([Description("over, into or out")] string kind = "over", int waitSeconds = 30, CancellationToken cancel = default) => Resume(kind switch
    {
        "over" => s => s.StepOverAsync(),
        "into" => s => s.StepIntoAsync(),
        "out" => s => s.StepOutAsync(),
        _ => throw new ArgumentException($"A step is over, into or out, not '{kind}'"),
    }, waitSeconds, cancel);

    [McpServerTool(Name = "debug_pause", Title = "Pause", Destructive = false), Description("Pauses the running program wherever it is.")]
    public Task<DebugSnapshot> DebugPause(int waitSeconds = 10, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        DebugSession session = Debugged();
        await session.PauseAsync();
        await Until(() => session.State != DebugState.Running, TimeSpan.FromSeconds(waitSeconds), cancel);
        return await DebugSnapshotAsync();
    });

    [McpServerTool(Name = "debug_run_to", Title = "Run to a line", Destructive = false), Description(
        "Runs on to a line, as Run to Cursor does, stopping sooner at a breakpoint, and waits up to waitSeconds for the stop.")]
    public Task<DebugSnapshot> DebugRunTo(string path, int line, int waitSeconds = 30, CancellationToken cancel = default) =>
        Resume(session => session.RunToCursorAsync(Resolve(path), line - 1), waitSeconds, cancel);

    [McpServerTool(Name = "debug_set_next_statement", Title = "Set Next Statement", Destructive = false), Description(
        "Moves where the paused program goes on to another line of its method, as Set Next Statement: the lines between are " +
        "skipped, or done again when the line is earlier. Nothing runs; returns the new stop.")]
    public Task<DebugSnapshot> DebugSetNextStatement(string path, int line, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        DebugSession session = Paused();
        if (await Session.Debug.SetNextStatementAsync(session, Resolve(path), line - 1) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }
        await Until(() => session.StopReason == "goto", TimeSpan.FromSeconds(10), cancel);
        return await DebugSnapshotAsync();
    });

    [McpServerTool(Name = "debug_input", Title = "Type into the program's console", Destructive = true), Description(
        "Sends a line to the debugged program's standard input, as the Debug window's console does, or types it into a " +
        "terminal configuration's tab, Enter for the newline.")]
    public Task<string> DebugInput(string text, bool newline = true, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        DebugSession session = Debugged();
        if (session.Terminal is { } inTerminal)
        {
            inTerminal.Session.Write(newline ? text + "\r" : text);
            return $"Typed into {inTerminal.Name}'s tab of the Terminal window";
        }
        await session.InputAsync(newline ? text + "\n" : text);
        return "Sent";
    });

    [McpServerTool(Name = "debug_evaluate", Title = "Evaluate an expression", Destructive = true), Description(
        "Evaluates a C# expression, which may call the program's methods, in a frame of the stopped thread (0, the top, by " +
        "default), as the Debug window's evaluate box does. A result with members has a reference debug_variables opens.")]
    public Task<VariableInfo> DebugEvaluate(string expression, int frame = 0, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        DebugSession session = Paused();
        DapStackFrame chosen = await FrameAt(session, frame);
        EvaluateResponse value = await session.EvaluateAsync(expression, chosen.Id, "repl");
        return new VariableInfo(expression, value.Result, value.Type, value.VariablesReference);
    });

    [McpServerTool(Name = "debug_set_value", Title = "Set a value", Destructive = true), Description(
        "Assigns a C# expression's value to a local, member or element (\"order.Quantity\", \"list[2]\") in a frame of the " +
        "stopped thread, as Set Value does; the program goes on with it. Returns the value as it now is.")]
    public Task<VariableInfo> DebugSetValue(string expression, [Description("A C# expression: 42, \"text\", null, new List<int>()")] string value, int frame = 0, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        DebugSession session = Paused();
        DapStackFrame chosen = await FrameAt(session, frame);
        SetExpressionResponse set = await session.SetExpressionAsync(expression, value, chosen.Id);
        await Session.Debug.RefreshAsync();
        return new VariableInfo(expression, set.Value, set.Type, set.VariablesReference);
    });

    [McpServerTool(Name = "debug_variables", Title = "A value's members", ReadOnly = true), Description(
        "The members, elements or locals behind a reference from debug_state, debug_evaluate or an earlier call.")]
    public Task<IReadOnlyList<VariableInfo>> DebugVariables(int reference, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        DebugSession session = Paused();
        return (IReadOnlyList<VariableInfo>)[.. (await session.VariablesAsync(reference)).Select(v => new VariableInfo(v.Name, v.Value, v.Type, v.VariablesReference))];
    });

    [McpServerTool(Name = "debug_output", Title = "The program's output", ReadOnly = true), Description(
        "The last lines the debugged program has written: the Debug window's console, or a terminal configuration's tab, " +
        "scrollback and screen.")]
    public Task<string> DebugOutput(int lines = 200, CancellationToken cancel = default) => OnUi(cancel, () => Task.FromResult(
        Session.Debug.Session?.Terminal is { } inTerminal ? Tail(inTerminal.Session, lines) : Tail(Session.Debug.Output(), lines)));

    // The IDE itself

    [McpServerTool(Name = "screenshot", Title = "What the window shows", ReadOnly = true), Description(
        "A PNG of the IDE's frontmost window as drawn now (a dialog or diff over the main window included), or of an area of " +
        "it in its logical units.")]
    public Task<ImageContentBlock> Screenshot(int? x = null, int? y = null, int? width = null, int? height = null, CancellationToken cancel = default) => OnUi(cancel, () =>
        Task.FromResult(ImageContentBlock.FromBytes(WindowPicture.Png(window.Frontmost, x is null ? null : new Avalonia.Rect(x.Value, y ?? 0, width ?? 400, height ?? 300)), "image/png")));

    [McpServerTool(Name = "ide_action", Title = "Run an IDE action", Destructive = true), Description(
        "Runs one of the IDE's actions by its id, as its shortcut would (app.build, app.debug.stepOver, app.code.rename...; " +
        "ide_actions lists them). An id no action has, or one that cannot run now, is an error saying why.")]
    public Task<string> IdeAction(string action, CancellationToken cancel = default) => OnUi(cancel, () =>
        Task.FromResult(window.Actions.Run(action) is { } why ? throw new InvalidOperationException($"{action} did not run: {why}") : $"Ran {action}"));

    [McpServerTool(Name = "ide_actions", Title = "The IDE's actions", ReadOnly = true), Description(
        "Every action ide_action runs (those with no shortcut too), as id: name [group] (shortcut on this platform), " +
        "and why one cannot run here now.")]
    public Task<IReadOnlyList<string>> IdeActions(CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        ActionRegistry actions = window.Actions;
        ActionContext context = actions.Context();
        return Task.FromResult((IReadOnlyList<string>)[.. actions.All.Select(a =>
            $"{a.Id}: {a.Title} [{a.Group}]{(actions.Shortcut(a.Id) is { } keys ? $" ({keys})" : string.Empty)}{(a.WhyNot(context) is { } why ? $" - cannot run: {why}" : string.Empty)}")]);
    });

    [McpServerTool(Name = "type", Title = "Type into the editor", Destructive = true), Description(
        "Types text into the active editor at its caret (open_file puts it at a line and column: before that column's character), as " +
        "the keyboard would (typing a closing bracket over one already there, and so on), leaving the file unsaved. To change or delete " +
        "text, edit.")]
    public Task<string> Type(string text, CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        if (Editors.Active is null)
        {
            throw new InvalidOperationException("No file is open in the editor");
        }
        Editors.CodeEditor.Type(text);
        return Task.FromResult($"Typed at {Editors.CodeEditor.CaretPosition.Line + 1}:{Editors.CodeEditor.CaretPosition.Column + 1}");
    });

    // The work behind them

    /// <summary>A tool's work on the UI thread, its waits ended by <paramref name="cancel"/> (the agent gave up on the
    /// call, or went), once the folder's projects are read and evaluated, which is what every tool that runs, builds or
    /// tests a project asks about. What it refuses (nothing to debug, no such file) reaches the agent in its own words: the
    /// SDK passes only an McpException's message on, and hides every other's.</summary>
    private async Task<T> OnUi<T>(CancellationToken cancel, Func<Task<T>> work)
    {
        cancel.ThrowIfCancellationRequested();
        try
        {
            return await UiThread.InvokeAsync(async () =>
            {
                // Ended while it waited its turn (its agent shut out as the window left the folder): it does nothing in the next.
                cancel.ThrowIfCancellationRequested();
                if (!Session.WorkspaceRead.IsCompleted || !Session.Models.Idle.IsCompleted)
                {
                    AgentCall.Say($"Waiting for {Session.Folder ?? "the folder"}'s projects to be read");
                    await Session.WorkspaceRead.WaitAsync(cancel);
                    await Session.Models.Idle.WaitAsync(cancel);
                    AgentCall.Say(null);
                }
                return await work();
            });
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or TimeoutException
            or HttpRequestException or DapException or LanguageServerFailed or LiveTree.LiveAppException)
        {
            throw new ModelContextProtocol.McpException(error is DapException dap ? $"The debugger refused: {dap.Reason}" : error.Message, error);
        }
        catch (Exception error) when (error is not (OperationCanceledException or ModelContextProtocol.McpException))
        {
            // No answer but a fault of the IDE's: logged as one on the window's own thread is, and its words the agent's,
            // where the SDK would answer only "An error occurred invoking" the tool.
            IdeErrors.Report(error);
            throw new ModelContextProtocol.McpException($"Passenger failed: {error.GetType().Name}: {error.Message}", error);
        }
    }

    /// <summary>Sets the paused program going as the window's keys do, the edits made while it was paused applied first.</summary>
    private Task<DebugSnapshot> Resume(Func<DebugSession, Task> resume, int waitSeconds, CancellationToken cancel) => OnUi(cancel, async () =>
    {
        DebugSession paused = Paused();
        // A debugged test run (tests_run's, or the Unit Tests window's Debug) whose host ends is answered with its tally.
        bool testing = Session.Tests.IsBusy;
        if (await Session.Debug.ApplyBeforeResumingAsync(paused))
        {
            await resume(paused);
        }
        DebugSnapshot after = await AfterResume(TimeSpan.FromSeconds(waitSeconds), cancel);
        if (!testing || after.State != "not debugging")
        {
            return after;
        }
        // The runner reports the host's results a moment after it ends, or starts the next project's host.
        await Until(() => !Session.Tests.IsBusy || Session.Debug.Session is { State: not DebugState.Ended }, TestsTallyLimit, cancel);
        return Session.Tests.IsBusy ? await AfterResume(TimeSpan.FromSeconds(waitSeconds), cancel) : after with { Tests = TestInfo(Session.Tests) };
    });

    /// <summary>How long a resumed debugged test run whose host ended is waited for to tell its results.</summary>
    private static readonly TimeSpan TestsTallyLimit = TimeSpan.FromSeconds(60);

    /// <summary>Waits for the program to stop or end after it was set going, then says where it is.</summary>
    private async Task<DebugSnapshot> AfterResume(TimeSpan limit, CancellationToken cancel)
    {
        if (limit > TimeSpan.Zero)
        {
            AgentCall.Say($"The program runs: waiting up to {limit.TotalSeconds:0} s for it to stop or end");
        }
        await Until(() => Session.Debug.Session is not { State: DebugState.Running or DebugState.Starting }, limit, cancel);
        // A stop is read (its frames, the exception it is on) a moment after the state says paused.
        await Until(() => Session.Debug.Session is not { State: DebugState.Paused } || Session.Debug.Stop is not null, TimeSpan.FromSeconds(5), cancel);
        return await DebugSnapshotAsync();
    }

    /// <summary>Where the debugger is, from its own model: the session's state in its words, and while paused the stop
    /// it read with the top frame's locals.</summary>
    private async Task<DebugSnapshot> DebugSnapshotAsync()
    {
        DebugController debugger = Session.Debug;
        if (debugger.Session is not { } session || session.State == DebugState.Ended)
        {
            return new DebugSnapshot("not debugging", null, null, [], [], debugger.Session?.ExitCode, debugger.Summary);
        }
        if (session.State != DebugState.Paused || session.StoppedThread is null)
        {
            return new DebugSnapshot(session.State.ToString().ToLowerInvariant(), null, null, [], [], null, debugger.Summary);
        }
        // The controller's stop is always the one paused at now, since every change drops it; before it is read, the session's.
        DebugStop? read = debugger.Stop is { } stop && stop.Thread == session.StoppedThread ? stop : null;
        IReadOnlyList<DapStackFrame> frames = read?.Frames ?? await Stack(session);
        // The frame the stop shows: past the Live Visual Tree inspector's call a property or binding error breakpoint stops in.
        DapStackFrame? top = read?.Top ?? frames.FirstOrDefault(f => f.Source?.Path is not null) ?? (frames.Count > 0 ? frames[0] : null);
        List<VariableInfo> locals = [];
        if (debugger.BindingStop is { } failed)
        {
            locals.Add(new VariableInfo(BindingStop.Name, failed.Value, failed.Type, failed.Reference));
        }
        if (top is not null)
        {
            foreach (DapScope scope in await session.ScopesAsync(top.Id))
            {
                locals.AddRange((await session.VariablesAsync(scope.VariablesReference)).Select(v => new VariableInfo(v.Name, v.Value, v.Type, v.VariablesReference)));
            }
        }
        string? exception = null;
        if (session.StopReason == "exception")
        {
            try
            {
                ExceptionInfoResponse info = await session.ExceptionInfoAsync(session.StoppedThread.Value);
                exception = Describe(info.Details) ?? $"{info.ExceptionId}: {info.Description}";
            }
            catch (DapException error)
            {
                exception = error.Message;
            }
        }
        return new DebugSnapshot(
            "paused",
            session.StopReason,
            top?.Source?.Path is { } path && debugger.PlaceOf(top) is { } at ? $"{Relative(path)}:{at.Line + 1}:{at.Column + 1}" : null,
            Frames(frames),
            locals,
            null,
            debugger.Summary,
            exception)
        {
            BindingValues = await BindingValuesShownAsync(),
        };
    }

    /// <summary>A stop's frames as an agent reads them, each by its index (which debug_evaluate's frame takes): the folder's
    /// own code by its place, and a run of any other (a framework's, with or without source: an Avalonia click is some 25
    /// of them under the handler, and Passenger's inspector's) as one line of their indexes and modules, at most 20 lines.</summary>
    private IReadOnlyList<string> Frames(IReadOnlyList<DapStackFrame> frames)
    {
        string? folder = Session.Folder;
        bool Own(DapStackFrame frame) => frame.Source?.Path is { } path && (folder is null || PathNames.IsUnder(path, folder));
        // The editor's line (from one) of the frame's code, in its file now: the debugger's is its build's compiler's.
        int LineOf(DapStackFrame frame) => (Session.Debug.PlaceOf(frame)?.Line ?? frame.Line - 1) + 1;
        List<string> lines = [];
        for (int i = 0; i < frames.Count && lines.Count < 20; i++)
        {
            if (Own(frames[i]))
            {
                lines.Add($"{i}: {frames[i].Name} at {Relative(frames[i].Source!.Path!)}:{LineOf(frames[i])}");
                continue;
            }
            int last = i;
            while (last + 1 < frames.Count && !Own(frames[last + 1]))
            {
                last++;
            }
            if (last == i)
            {
                lines.Add($"{i}: {frames[i].Name}{(frames[i].Source?.Path is { } path ? $" at {path}:{LineOf(frames[i])}" : "")}");
                continue;
            }
            string[] modules = [.. frames.Skip(i).Take(last - i + 1).Select(f => f.Name.IndexOf('!', StringComparison.Ordinal) is > 0 and int bang ? f.Name[..bang] : null)
                .OfType<string>().Distinct()];
            lines.Add($"{i}-{last}: {last - i + 1} frames outside the folder's code{(modules.Length > 0 ? $" ({string.Join(", ", modules.Take(4))}{(modules.Length > 4 ? ", …" : "")})" : "")}");
            i = last;
        }
        return lines;
    }

    /// <summary>How long a stop's answer waits for the binding values of the .axaml files shown to be read.</summary>
    private static readonly TimeSpan BindingValuesLimit = TimeSpan.FromSeconds(20);

    /// <summary>The binding values after the lines of the .axaml files the editors show, as they show them once read at this
    /// stop (path:line: Property: value, or that it failed and why); null when no Avalonia app is debugged or none is shown.</summary>
    private async Task<IReadOnlyList<string>?> BindingValuesShownAsync()
    {
        if (Session.LiveApp is null)
        {
            return null;
        }
        LiveTree.BindingValuesController values = window.BindingValues;
        values.CatchUp();
        await Until(() => !values.IsReading, BindingValuesLimit, CancellationToken.None);
        List<string> shown = [.. Editors.Groups.Select(g => g.Active).OfType<OpenDocument>().Distinct()
            .SelectMany(d => d.LiveValues.OrderBy(a => a.Line).Select(a => $"{Relative(d.Path)}:{a.Line + 1}: {a.Text}"))];
        return shown.Count == 0 ? null : shown;
    }

    /// <summary>An exception as its type and message, its inner exceptions after it.</summary>
    private static string? Describe(DapExceptionDetails? details)
    {
        if (details is null)
        {
            return null;
        }
        string text = $"{details.FullTypeName ?? details.TypeName}: {details.Message}";
        foreach (DapExceptionDetails inner in details.InnerException ?? [])
        {
            text += $"\n ---> {Describe(inner)}";
        }
        return text;
    }

    private static async Task<IReadOnlyList<DapStackFrame>> Stack(DebugSession session) =>
        await session.StackTraceAsync(session.StoppedThread ?? throw new InvalidOperationException("No thread is stopped"));

    /// <summary>A frame of the stopped thread by its place from the top, as debug_state numbers them; one past the stack
    /// says how deep the stack is.</summary>
    private static async Task<DapStackFrame> FrameAt(DebugSession session, int frame)
    {
        IReadOnlyList<DapStackFrame> frames = await Stack(session);
        return frame >= 0 && frame < frames.Count
            ? frames[frame]
            : throw new ArgumentException(frames.Count switch
            {
                0 => $"There is no frame {frame}: the stopped thread has no frames",
                1 => $"There is no frame {frame}: the stopped thread has 1 frame, 0",
                _ => $"There is no frame {frame}: the stopped thread has {frames.Count} frames, 0 (the top) to {frames.Count - 1}, as debug_state numbers them",
            });
    }

    private DebugSession Debugged() => Session.Debug.Session is { State: not DebugState.Ended } session
        ? session
        : throw new InvalidOperationException("Nothing is being debugged; debug_start starts it");

    private DebugSession Paused() => Debugged() is { State: DebugState.Paused } session
        ? session
        : throw new InvalidOperationException("The program is running; debug_pause stops it where it is");

    private void Choose(string? configuration)
    {
        RunConfigurations configurations = Session.Configurations;
        if (configuration is not null && !configurations.Select(configuration))
        {
            throw new InvalidOperationException($"No run configuration '{configuration}'; there are: {string.Join(", ", configurations.All.Select(c => c.Name))}");
        }
    }

    private IReadOnlyList<BreakpointInfo> Breakpoints(string? path) =>
        [.. (path is null ? Session.Debug.Breakpoints.All : Session.Debug.Breakpoints.In(path))
            .Select(b => new BreakpointInfo(Relative(b.Path), b.Line + 1, b.Enabled, b.Status.ToString().ToLowerInvariant(), b.Condition, b.HitCondition, b.LogMessage, b.Suspend, b.Message,
                Attribute: b.Attribute))];

    private IReadOnlyList<BreakpointInfo> DataBreakpoints() =>
        [.. Session.Debug.Breakpoints.Data.Select(b => new BreakpointInfo(null, null, b.Enabled, b.Status.ToString().ToLowerInvariant(), b.Condition, b.HitCondition,
            b.LogMessage, b.Suspend, b.Message, Data: b.Location)),
         .. Session.Debug.Breakpoints.Properties.Select(b => new BreakpointInfo(null, null, b.Enabled, b.Status.ToString().ToLowerInvariant(), b.Condition, b.HitCondition,
            b.LogMessage, b.Suspend, b.Message, Property: b.Location, Control: Id(b.Element)))];

    private IReadOnlyList<BreakpointInfo> ExceptionBreakpoints()
    {
        BindingErrorBreakpoint bindings = Session.Debug.Breakpoints.BindingErrors;
        return [.. Session.Debug.Breakpoints.Exceptions.Select(b => new BreakpointInfo(null, null, b.Enabled, b.Status.ToString().ToLowerInvariant(), b.Condition, b.HitCondition,
                b.LogMessage, b.Suspend, b.Message, b.Location, ExceptionScenarioNames.Of(b.Scenarios))),
            new BreakpointInfo(null, null, bindings.Enabled, bindings.Status.ToString().ToLowerInvariant(), bindings.Condition, bindings.HitCondition, bindings.LogMessage,
                bindings.Suspend, bindings.Message, BindingErrors: true)];
    }

    /// <summary>A line breakpoint's file and line from zero, as an agent names them.</summary>
    private (string Path, int Line) LinePlace(string? path, int? line) => path is { Length: > 0 } && line is int at
        ? (Resolve(path), at - 1)
        : throw new InvalidOperationException("Name a path and a line for a line breakpoint, or an exception type for an exception breakpoint");

    /// <summary>The server's actions at a place or over a selection, asked with the diagnostics on the place's line.</summary>
    private async Task<IReadOnlyList<CodeAction>> Actions(ServerServed served, int line, int column, int? endLine, int? endColumn)
    {
        OpenDocument document = served.Document;
        int offset = Offset(document, line, column);
        int end = endLine is { } last ? Offset(document, last, endColumn ?? 1) : offset;
        IReadOnlyList<Diagnostic> marked = served.Web?.Diagnostics ?? Session.CSharp?.For(document)?.Diagnostics ?? [];
        int at = served.Lsp.PositionOf(offset).Line;
        IReadOnlyList<Diagnostic> here = [.. marked.Where(d => d.Range.Start.Line <= at && at <= d.Range.End.Line)];
        IReadOnlyList<CodeAction> actions = await served.Lsp.CodeActionsAsync(TextRange.FromBounds(Math.Min(offset, end), Math.Max(offset, end)), here);
        return [.. actions.Where(a => a.Disabled is null)];
    }

    /// <summary>The language server, once it has loaded the workspace.</summary>
    private async Task<RoslynSession> Server(CancellationToken cancel)
    {
        if (Session.CSharp is null)
        {
            throw new InvalidOperationException("No C# language server runs for this folder");
        }
        // The session's server, which starts again when its projects change: each look is at the one there is then.
        if (Session.CSharp is { IsReady: false })
        {
            AgentCall.Say("Waiting for the C# language server to load the solution");
            await Until(() => Session.CSharp?.IsReady != false, ServerLimit, cancel);
            AgentCall.Say(null);
        }
        return Session.CSharp is { IsReady: true, Session: { } server }
            ? server
            : throw new TimeoutException($"The language server has not loaded the workspace ({Session.CSharp?.State ?? "none"})");
    }

    /// <summary>A file opened as the editor would, with the language server's document for it once the
    /// server has loaded the workspace.</summary>
    private async Task<(OpenDocument Document, LspDocument Lsp)> Code(string path, CancellationToken cancel)
    {
        OpenDocument document = Document(path);
        if (Session.CSharp is null)
        {
            throw new InvalidOperationException("No C# language server runs for this folder");
        }
        // The server is never given another language's file, so waiting for it to load one would wait out the limit.
        if (document.Language.Name is not ("C#" or "Razor"))
        {
            throw new InvalidOperationException($"This is the C# language server's, which answers for C# and Razor files, not {LanguageOf(document)} ones such as {Path.GetFileName(document.Path)}");
        }
        // Answers before the server has loaded the solution come from a file it knows alone, and are wrong.
        await Until(() => Session.CSharp is not { } csharp || csharp.IsReady && csharp.For(document)?.Lsp is not null, ServerLimit, cancel);
        return (document, Session.CSharp?.For(document)?.Lsp
            ?? throw new TimeoutException($"The language server has not loaded {Path.GetFileName(document.Path)} ({Session.CSharp?.State ?? "none"})"));
    }

    private OpenDocument Document(string path)
    {
        string full = Resolve(path);
        if (Editors.Documents.FirstOrDefault(d => d.Path == full) is { } open)
        {
            // An agent's own file tools write to disk and ask about the file at once, before the folder's watcher has told
            // the editor: an open file it has not changed takes the disk's text first, so no answer is about the text before.
            // A preview has no text to take, and is decoded again only when the watcher hears its file change.
            if (!open.IsModified && open.HoldsText)
            {
                open.Reload();
            }
            return open;
        }
        if (window.OpenFile(full) is { } problem)
        {
            throw new FileNotFoundException($"{full} could not be opened: {problem}");
        }
        return Editors.Documents.FirstOrDefault(d => d.Path == full)
            ?? throw new FileNotFoundException($"{full} is not open in the editor");
    }

    /// <summary>A file opened as <see cref="Document"/> opens it, refused when its tab holds no text of the file's (an
    /// image's or a font's preview, a workbook's cells), so no tool reads or edits that empty stand-in as the file.</summary>
    private OpenDocument Textual(string path)
    {
        OpenDocument document = Document(path);
        return document.HoldsText ? document
            : throw new InvalidOperationException($"{Relative(document.Path)} is {document.ShownAs}: it has no text to read or edit");
    }

    private static int Offset(OpenDocument document, int line, int column)
    {
        TextSnapshot text = document.Text.Snapshot;
        int l = Math.Clamp(line - 1, 0, text.LineCount - 1);
        return Math.Min(text.GetLineStart(l) + Math.Max(0, column - 1), text.GetLine(l).End);
    }

    /// <summary>The places the language server names, in its lines, as an agent reads them (<see cref="Lines"/>).</summary>
    private Task<IReadOnlyList<Place>> Places(IReadOnlyList<LspLocation> locations, CancellationToken cancel) =>
        Lines([.. locations.Select(l => (l.Uri, l.Range.Start, true))], cancel);

    /// <summary>Each place's path as the agent is shown it, its line and column in the editor's lines and that line's text,
    /// trimmed: one the server names (<paramref name="places"/>' Server) read in its file's text, as the server's lines
    /// are not the editor's in a file with a lone '\r', U+0085, U+2028 or U+2029. Worked out on the thread pool, each file
    /// read once: a symbol used thousands of times in a large solution never holds the person's window.</summary>
    private Task<IReadOnlyList<Place>> Lines(IReadOnlyList<(string Uri, LspPosition At, bool Server)> places, CancellationToken cancel)
    {
        string? folder = Session.Folder;
        // An editor's snapshot is immutable, so an open file's lines (unsaved edits and all) are read off the UI thread too.
        Dictionary<string, TextSnapshot> open = new(StringComparer.Ordinal);
        foreach (OpenDocument document in Editors.Documents)
        {
            open.TryAdd(document.Path, document.Text.Snapshot);
        }
        return BackgroundWork.Run(() =>
        {
            Dictionary<string, string> byUri = places.Select(p => p.Uri).Distinct(StringComparer.Ordinal).ToDictionary(u => u, DocumentUris.ToPath, StringComparer.Ordinal);
            Dictionary<string, TextSnapshot?> texts = new(StringComparer.Ordinal);
            return (IReadOnlyList<Place>)[.. places.Select(place =>
            {
                cancel.ThrowIfCancellationRequested();
                string path = byUri[place.Uri];
                if (!texts.TryGetValue(path, out TextSnapshot? text))
                {
                    texts[path] = text = open.GetValueOrDefault(path) ?? ServerPlaces.Read(path);
                }
                // Gone or refused since the language server read it: its places are still places, without their text.
                TextPosition at = place.Server ? ServerPlaces.PositionOf(text, place.At) : new TextPosition(place.At.Line, place.At.Character);
                string line = text is not null && at.Line < text.LineCount ? text.GetLineText(at.Line).Trim() : string.Empty;
                return new Place(Relative(folder, path), at.Line + 1, at.Column + 1, line);
            })];
        }, cancel);
    }

    private Workspace Snapshot()
    {
        ProjectSession session = Session;
        OpenDocument? active = Editors.Active;
        TextPosition? caret = active is null ? null : Editors.CodeEditor.CaretPosition;
        return new Workspace(
            session.Folder,
            session.Solution?.Path,
            [.. (session.Workspace?.Projects ?? []).Select(p => new ProjectInfo(p.Name, Relative(p.Path), p.OutputType, p.TargetFrameworks))],
            [.. session.Configurations.All.Select(c => c.Name)],
            session.Configurations.Chosen?.Name,
            session.CSharp is not { } csharp ? "none" : csharp.IsReady ? "ready" : csharp.State,
            [.. Editors.Documents.Select(d => Relative(d.Path) + (d.IsModified ? " (unsaved)" : ""))],
            active is null ? null : $"{Relative(active.Path)}:{caret!.Value.Line + 1}:{caret.Value.Column + 1}",
            Session.Debug.Session?.State.ToString().ToLowerInvariant() ?? "not debugging");
    }

    /// <summary>A path an agent gave, from the open folder unless rooted, as the IDE names it (in any letter case).</summary>
    private string Resolve(string path) =>
        PathNames.Canonical(Path.IsPathRooted(path) || Session.Folder is not { } folder ? path : Path.Combine(folder, path));

    private string Relative(string path) => Relative(Session.Folder, path);

    private static string Relative(string? folder, string path) => folder is not null && PathNames.IsUnder(path, folder)
        ? Path.GetRelativePath(folder, path)
        : path;

    /// <summary>The last lines of a terminal's scrollback and screen, as a program in it left them.</summary>
    private static string Tail(global::Passenger.Terminal.TerminalSession terminal, int lines)
    {
        global::Passenger.Terminal.TerminalScreen screen = terminal.Screen;
        List<string> all = [.. screen.Scrollback.Select(l => l.Text()).Concat(screen.Lines.Select(l => l.Text()))];
        while (all.Count > 0 && all[^1].Length == 0)
        {
            all.RemoveAt(all.Count - 1);
        }
        return string.Join('\n', all.Skip(Math.Max(0, all.Count - lines)));
    }

    /// <summary>A console's last lines.</summary>
    private static string Tail(TextSnapshot text, int lines)
    {
        int first = Math.Max(0, text.LineCount - lines);
        return text.GetText(text.GetLineStart(first), text.Length - text.GetLineStart(first));
    }

    /// <summary>Waits on the UI thread, letting it run, until something holds, the time is up, or the agent no longer
    /// wants the answer.</summary>
    private static async Task Until(Func<bool> condition, TimeSpan limit, CancellationToken cancel)
    {
        DateTime until = DateTime.UtcNow + limit;
        while (!condition() && DateTime.UtcNow < until)
        {
            await Task.Delay(50, cancel);
        }
    }
}

public sealed record Workspace(
    string? Folder,
    string? Solution,
    IReadOnlyList<ProjectInfo> Projects,
    IReadOnlyList<string> RunConfigurations,
    string? ChosenConfiguration,
    string LanguageServer,
    IReadOnlyList<string> OpenFiles,
    string? ActiveFile,
    string Debugger);

public sealed record ProjectInfo(string Name, string Path, string? OutputType, IReadOnlyList<string> TargetFrameworks);

public sealed record Finding(string Severity, string? Code, string Message, string? Path, int Line, int Column);

public sealed record Place(string Path, int Line, int Column, string Text);

public sealed record HierarchyEntry(string Name, string? Detail, string Path, int Line, int Column, string Text);

/// <summary>A build's answer: MSBuild's last lines only when it failed, since a build that succeeded has nothing in them
/// an agent acts on (build_output has them).</summary>
public sealed record BuildResult(bool Succeeded, string Outcome, int Errors, int Warnings, IReadOnlyList<Finding> Problems, string? OutputTail);

public sealed record RunResult(bool StillRunning, int? ExitCode, string Status, string Output);

/// <summary>A breakpoint as an agent reads it: a line breakpoint's path and line, an exception breakpoint's type and scenarios,
/// what a data breakpoint watches ("count on counter (Counter)"), or a property breakpoint's property and element ("Text on
/// TextBlock#Counter", and the element's @id).</summary>
public sealed record BreakpointInfo(string? Path, int? Line, bool Enabled, string Status, string? Condition, string? HitCondition, string? LogMessage, bool Suspend,
    string? Message, string? Exception = null, IReadOnlyList<string>? Scenarios = null, string? Data = null, string? Attribute = null, string? Property = null,
    bool? BindingErrors = null, string? Control = null);

public sealed record VariableInfo(string Name, string Value, string? Type, int Reference);

/// <summary>A .NET program the debugger can attach to, as Attach to Process lists it.</summary>
public sealed record ProcessInfo(int ProcessId, string Executable, string Program, string User, string CommandLine);

public sealed record DebugSnapshot(
    string State,
    string? Reason,
    string? Location,
    IReadOnlyList<string> Frames,
    IReadOnlyList<VariableInfo> Locals,
    int? ExitCode,
    string? Status,
    string? Exception = null)
{
    /// <summary>At a stop in a debugged Avalonia app, the binding values after the lines of the .axaml files the editors show,
    /// as the editor shows them (path:line: Property: value).</summary>
    public IReadOnlyList<string>? BindingValues { get; init; }

    /// <summary>A debugged test run that ended as the program went on: its tally and failures, as tests_run answers them.</summary>
    public TestRunInfo? Tests { get; init; }
}

public sealed record TestRunInfo(string Summary, IReadOnlyList<TestFailure> Failures, DebugSnapshot? Debugger = null);

public sealed record TestFailure(string Test, string? Location, string Details);

public sealed record TestInfoRow(string Test, string State, string? Location);

public sealed record SymbolFound(string Name, string Kind, string? Container, string Path, int Line, int Column);
