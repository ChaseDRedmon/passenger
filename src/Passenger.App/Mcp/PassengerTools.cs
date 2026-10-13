using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Passenger.App.CSharp;
using Passenger.App.Debugging;
using Passenger.App.Run;
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
/// whether it is Destructive: a person's window asks before an agent first uses one that is not declared harmless.</summary>
public sealed partial class PassengerTools(MainWindow window)
{
    private static readonly TimeSpan ServerLimit = TimeSpan.FromSeconds(120);

    private const string Arguments = "The program's command line, in place of the configuration's (a launch profile's commandLineArgs)";

    // Workspace and editor

    [McpServerTool(Name = "open", Title = "Open a folder, solution or file", Destructive = false), Description(
        "Opens a folder or solution (a directory, .sln or .slnx) as the workspace, or a file in the editor (opening its folder " +
        "first if none is open). Starts the C# language server for the workspace. Returns the workspace.")]
    public Task<Workspace> Open([Description("Absolute path, or relative to the open folder")] string path, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        string full = Resolve(path);
        if (Directory.Exists(full) || full.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || full.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            window.OpenFolder(full);
        }
        else if (File.Exists(full))
        {
            if (window.Folder is null)
            {
                window.OpenFolder(Path.GetDirectoryName(full)!);
            }
            window.OpenFile(full);
        }
        else
        {
            throw new FileNotFoundException($"No folder or file at {full}");
        }
        // A folder without a solution has its projects once the walk for them lands.
        await window.WorkspaceRead.WaitAsync(cancel);
        await Task.Yield();
        return Snapshot();
    });

    [McpServerTool(Name = "workspace", Title = "The workspace", ReadOnly = true), Description(
        "The open folder and solution, its projects (output type, frameworks), the run configurations and the chosen one, " +
        "the language server's state, the open files (unsaved ones marked) and the active file with its caret.")]
    public Task<Workspace> GetWorkspace(CancellationToken cancel = default) => OnUi(cancel, () => Task.FromResult(Snapshot()));

    [McpServerTool(Name = "open_file", Title = "Show a file", Destructive = false), Description("Opens a file in the editor, at a line and column if given, as the user would see it.")]
    public Task<string> OpenFile(string path, [Description("From 1")] int? line = null, [Description("From 1")] int? column = null, CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        OpenDocument document = Document(path);
        if (line is int l)
        {
            window.NavigateTo(document.Path, new TextPosition(Math.Max(0, l - 1), Math.Max(0, (column ?? 1) - 1)));
        }
        return Task.FromResult($"{Relative(document.Path)} is open");
    });

    [McpServerTool(Name = "editor_text", Title = "A file's text in the editor", ReadOnly = true), Description(
        "The text of a file as the editor holds it, unsaved changes included, with line numbers. The file is opened if it is not.")]
    public Task<string> EditorText(string path, [Description("First line, from 1")] int? from = null, [Description("Last line")] int? to = null, CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        TextSnapshot text = Document(path).Text.Snapshot;
        int first = Math.Clamp((from ?? 1) - 1, 0, text.LineCount - 1);
        int last = Math.Clamp((to ?? text.LineCount) - 1, first, text.LineCount - 1);
        return Task.FromResult(string.Join('\n', Enumerable.Range(first, last - first + 1).Select(i => $"{i + 1,6}\t{text.GetLineText(i)}")));
    });

    [McpServerTool(Name = "save_all", Title = "Save every file", Destructive = false), Description("Saves every open file with unsaved changes.")]
    public Task<string> SaveAll(CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        int unsaved = window.EditorArea.Documents.Count(d => d.IsModified);
        window.EditorArea.SaveAll();
        return Task.FromResult(unsaved == 0 ? "Nothing to save" : $"Saved {unsaved} file{(unsaved == 1 ? "" : "s")}");
    });

    // Code: the language server's

    [McpServerTool(Name = "diagnostics", Title = "Errors and warnings in a file", ReadOnly = true), Description(
        "The C# language server's errors, warnings and messages for a file, as the editor marks them, fresh.")]
    public Task<IReadOnlyList<Finding>> Diagnostics(string path, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (OpenDocument document, LspDocument lsp) = await Code(path, cancel);
        (IReadOnlyList<Diagnostic> found, _) = await lsp.DiagnosticsAsync();
        return (IReadOnlyList<Finding>)[.. found.Select(d => new Finding(
            d.Severity switch { 1 => "error", 2 => "warning", 3 => "info", _ => "hint" },
            d.Code?.ToString(),
            d.Message,
            Relative(document.Path),
            d.Range.Start.Line + 1,
            d.Range.Start.Character + 1))];
    });

    [McpServerTool(Name = "symbol_info", Title = "What a symbol is", ReadOnly = true), Description(
        "What the language server says about the symbol at a position: its declaration and documentation (the hover card).")]
    public Task<string> SymbolInfo(string path, int line, int column, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (OpenDocument document, LspDocument lsp) = await Code(path, cancel);
        Hover? hover = await lsp.HoverAsync(Offset(document, line, column));
        return hover is null ? "Nothing is known about this position" : Shapes.Text(hover.Contents).Trim();
    });

    [McpServerTool(Name = "definition", Title = "Go to definition", ReadOnly = true), Description(
        "Where the symbol at a position is defined. A symbol from a package or the framework has no source, and says so.")]
    public Task<IReadOnlyList<Place>> Definition(string path, int line, int column, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (OpenDocument document, LspDocument lsp) = await Code(path, cancel);
        return await Places(await lsp.DefinitionAsync(Offset(document, line, column)), cancel);
    });

    [McpServerTool(Name = "usages", Title = "Find usages", ReadOnly = true), Description("Every place the symbol at a position is used, its declaration included, with each line's text.")]
    public Task<IReadOnlyList<Place>> Usages(string path, int line, int column, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (OpenDocument document, LspDocument lsp) = await Code(path, cancel);
        return await Places(await lsp.ReferencesAsync(Offset(document, line, column)), cancel);
    });

    [McpServerTool(Name = "hierarchy", Title = "Call or type hierarchy", ReadOnly = true), Description(
        "One level of the symbol at a position's hierarchy, as Rider's Hierarchy window: direction callers (each at its call), "
        + "callees, supertypes (base types and interfaces) or subtypes. Each entry names the symbol.")]
    public Task<IReadOnlyList<HierarchyEntry>> Hierarchy(string path, int line, int column, string direction = "callers", CancellationToken cancel = default) => OnUi<IReadOnlyList<HierarchyEntry>>(cancel, async () =>
    {
        (OpenDocument document, LspDocument lsp) = await Code(path, cancel);
        int offset = Offset(document, line, column);
        LanguageServer server = window.CSharpService?.Session?.Server ?? throw new InvalidOperationException("C# is not ready");
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
        IReadOnlyList<(string Path, string Text)> lines = await Lines([.. found.Select(f => (f.Item.Uri, f.At.Start.Line))], cancel);
        return [.. found.Select((f, i) => new HierarchyEntry(f.Item.Name, f.Item.Detail, lines[i].Path, f.At.Start.Line + 1, f.At.Start.Character + 1, lines[i].Text))];
    });

    [McpServerTool(Name = "implementations", Title = "Go to implementation", ReadOnly = true), Description("The implementations of the interface, abstract or virtual member at a position.")]
    public Task<IReadOnlyList<Place>> Implementations(string path, int line, int column, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (OpenDocument document, LspDocument lsp) = await Code(path, cancel);
        return await Places(await lsp.ImplementationAsync(Offset(document, line, column)), cancel);
    });

    [McpServerTool(Name = "rename", Title = "Rename a symbol", Destructive = true), Description(
        "Renames the symbol at a position everywhere it is used, as the IDE's Rename does, and saves the files it changes.")]
    public Task<string> Rename(string path, int line, int column, [Description("The new name")] string newName, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (OpenDocument document, LspDocument lsp) = await Code(path, cancel);
        if (await lsp.RenameAsync(Offset(document, line, column), newName) is not { } edit)
        {
            return "This can't be renamed";
        }
        IReadOnlyList<string> files = [.. Shapes.WorkspaceEdit(edit).Select(f => DocumentUris.ToPath(f.Uri)).Distinct()];
        WorkspaceEdits.Apply(window.EditorArea, edit);
        window.EditorArea.SaveAll();
        return $"Renamed in {files.Count} file{(files.Count == 1 ? "" : "s")}: {string.Join(", ", files.Select(Relative))}";
    });

    [McpServerTool(Name = "code_actions", Title = "Fixes and refactorings", ReadOnly = true), Description(
        "The fixes and refactorings the language server offers at a position (Alt+Enter), or over a selection from there to " +
        "endLine:endColumn (where Extract Method and Introduce Variable are offered), as 'title [kind]'; one that offers choices " +
        "(the actions nested under it, the scopes a Fix All may cover) is followed by them, each one apply_code_action's choice.")]
    public Task<IReadOnlyList<string>> CodeActions(string path, int line, int column, int? endLine = null, int? endColumn = null, CancellationToken cancel = default) => OnUi(cancel, async () =>
        (IReadOnlyList<string>)[.. (await Actions(path, line, column, endLine, endColumn, cancel)).Actions.Select(Described)]);

    [McpServerTool(Name = "apply_code_action", Title = "Apply a fix or refactoring", Destructive = true), Description(
        "Applies the fix or refactoring with this title at a position (or over the selection to endLine:endColumn), as choosing it from " +
        "Alt+Enter does, and saves the files. One that offers choices takes the one named as choice, as code_actions lists them.")]
    public Task<string> ApplyCodeAction(string path, int line, int column, string title, int? endLine = null, int? endColumn = null,
        [Description("For an action that offers choices, the one to take")] string? choice = null, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (LspDocument lsp, IReadOnlyList<CodeAction> actions) = await Actions(path, line, column, endLine, endColumn, cancel);
        CodeAction chosen = actions.FirstOrDefault(a => a.Title == title)
            ?? throw new InvalidOperationException($"No action '{title}' here; there are: {string.Join("; ", actions.Select(a => a.Title))}");
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
        int files = WorkspaceEdits.Apply(window.EditorArea, edit);
        window.EditorArea.SaveAll();
        return $"Applied {what} to {files} file{(files == 1 ? "" : "s")}{unrun}";
    });

    /// <summary>A code action as code_actions lists it: its title, its kind, and the choices it offers.</summary>
    private static string Described(CodeAction action)
    {
        string described = action.Kind is { Length: > 0 } kind ? $"{action.Title} [{kind}]" : action.Title;
        IReadOnlyList<ActionChoice> choices = ClientCommands.Choices(action);
        return choices.Count == 0 ? described : $"{described}, choices: {string.Join("; ", choices.Select(c => c.Text))}";
    }

    [McpServerTool(Name = "format", Title = "Reformat a file", Destructive = true), Description("Reformats a file as the IDE's Reformat Code does, and saves it.")]
    public Task<string> Format(string path, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (OpenDocument document, LspDocument lsp) = await Code(path, cancel);
        window.OpenFile(document.Path);
        CodeEditor editor = window.EditorArea.CodeEditor;
        using Passenger.Text.ChangeTracker asked = new(document.Text);
        IReadOnlyList<LspTextEdit> edits = await lsp.FormatAsync(editor.Indent);
        if (WorkspaceEdits.Carried(asked, edits) is not { } placed)
        {
            return "Not reformatted: the file was edited while the language server worked on it";
        }
        WorkspaceEdits.ApplyTo(window.EditorArea, document, placed);
        document.Save();
        return edits.Count == 0 ? "Already formatted" : $"Reformatted ({edits.Count} edits)";
    });

    [McpServerTool(Name = "find_symbol", Title = "Go to symbol", ReadOnly = true), Description(
        "Types, members and other symbols across the workspace whose names match a query, as Go to Symbol finds them: by " +
        "substring, initials or camel humps (\"GH\" finds GetHashCode). Only the solution's own code, not packages'.")]
    public Task<IReadOnlyList<SymbolFound>> FindSymbol(
        string query,
        [Description("Only symbols of this kind: class, interface, struct, enum, method, property, field, event, constructor...")] string? kind = null,
        int limit = 50, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        RoslynSession session = await Server(cancel);
        IReadOnlyList<SymbolInformation> found = await session.SymbolsAsync(query);
        return (IReadOnlyList<SymbolFound>)[.. found
            .Where(s => kind is null || SymbolKinds.Name(s.Kind).Equals(kind, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(s => new SymbolFound(s.Name, SymbolKinds.Name(s.Kind), s.ContainerName, Relative(DocumentUris.ToPath(s.Location.Uri)), s.Location.Range.Start.Line + 1, s.Location.Range.Start.Character + 1))];
    });

    [McpServerTool(Name = "outline", Title = "A file's structure", ReadOnly = true), Description(
        "A file's types and their members, indented, each with its line, as the File Structure window shows them.")]
    public Task<string> Outline(string path, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (_, LspDocument lsp) = await Code(path, cancel);
        System.Text.StringBuilder text = new();
        Write(await lsp.OutlineAsync(), 0);
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
                text.Append(" (").Append(SymbolKinds.Name(symbol.Kind)).Append(", line ").Append(symbol.SelectionRange.Start.Line + 1).Append(")\n");
                Write(symbol.Children ?? [], depth + 1);
            }
        }
    });

    // Build and run

    [McpServerTool(Name = "build", Title = "Build", Destructive = true), Description(
        "Builds the solution (or the folder's project) as Build does, waiting for it, and returns whether it succeeded, how it ended " +
        "(failed, stopped, refused while another build runs, unable to start) and every error and warning.")]
    public Task<BuildResult> Build(CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        // The build goes on without an agent that gives up on it, shown in the Build window.
        BuildOutcome built = await window.BuildAsync().WaitAsync(cancel);
        IReadOnlyList<Finding> problems = [.. window.BottomPanel.Problems.Problems.Select(p => new Finding(
            p.Severity == ProblemSeverity.Error ? "error" : "warning", p.Code, p.Message, p.Path is { } at ? Relative(at) : p.Project, p.Position.Line + 1, p.Position.Column + 1))];
        return new BuildResult(built.Succeeded, built.Message, problems.Count(p => p.Severity == "error"), problems.Count(p => p.Severity == "warning"), problems, Tail(window.BottomPanel.Build, 20));
    });

    [McpServerTool(Name = "run", Title = "Run a configuration", Destructive = true), Description(
        "Runs a run configuration (the chosen one if none is named) as Run does, and waits up to waitSeconds for it to end. " +
        "Returns its output so far and its exit code once it has one.")]
    public Task<RunResult> Run(
        string? configuration = null,
        [Description(Arguments)] string? arguments = null,
        int waitSeconds = 30, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        Choose(configuration);
        window.RunSelected(arguments);
        await Until(() => !window.Runner.IsRunning, TimeSpan.FromSeconds(waitSeconds), cancel);
        return new RunResult(window.Runner.IsRunning, window.Status, Tail(window.BottomPanel.Run, 200));
    });

    [McpServerTool(Name = "stop", Title = "Stop", Destructive = false), Description("Stops whatever is running or being debugged, as Stop does.")]
    public Task<string> Stop(CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        window.Runner.Stop();
        await window.Debugger.StopAsync();
        return "Stopped";
    });

    // Unit tests

    [McpServerTool(Name = "tests_run", Title = "Run unit tests", Destructive = true), Description(
        "Builds and runs unit tests through VSTest, as the Unit Tests window does: all of them, or those whose full name (Namespace.Class.Method) " +
        "contains filter. With debug, under the debugger (breakpoints stop in tests). Waits for the run and returns the tally and every failure " +
        "with its message and stack trace.")]
    public Task<TestRunInfo> TestsRun(
        [Description("Part of a test's full name: a method, a class, a namespace")] string? filter = null,
        bool debug = false, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        string folder = window.Folder ?? throw new InvalidOperationException("No folder is open");
        Testing.TestController tests = window.Tests;
        if (filter is not null && tests.Tree.Roots.Count == 0 && await tests.DiscoverAsync(folder).WaitAsync(cancel) is { } discovery)
        {
            throw new InvalidOperationException(discovery);
        }
        List<Testing.TestNode>? chosen = filter is null ? null
            : [.. tests.Tree.Roots.SelectMany(r => r.Methods).Where(m => m.Cases.Any(c => c.FullyQualifiedName.Contains(filter, StringComparison.Ordinal)))];
        if (chosen is { Count: 0 })
        {
            throw new InvalidOperationException($"No test's name contains '{filter}'");
        }
        Task<string?> run = tests.RunAsync(folder, chosen, debug);
        if (debug)
        {
            // A debugged test that stops is the agent's to look at: the stop comes back, the run goes on after it.
            await Until(() => run.IsCompleted || window.Debugger.Session is { State: DebugState.Paused }, TimeSpan.FromMinutes(5), cancel);
            if (!run.IsCompleted)
            {
                await Until(() => window.BottomPanel.Debug.Frames.Count > 0, TimeSpan.FromSeconds(5), cancel);
                return TestInfo(tests) with { Debugger = await DebugSnapshotAsync() };
            }
        }
        if (await run.WaitAsync(cancel) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }
        return TestInfo(tests);
    });

    [McpServerTool(Name = "tests_list", Title = "The unit tests", ReadOnly = true), Description(
        "Every test found (after tests_run or Run All), each with its state from the last run: passed, failed, skipped, notRun.")]
    public Task<IReadOnlyList<TestInfoRow>> TestsList(CancellationToken cancel = default) => OnUi(cancel, () => Task.FromResult((IReadOnlyList<TestInfoRow>)[.. window.Tests.Tree.Roots
        .SelectMany(r => r.Methods)
        .Select(m => new TestInfoRow(string.Join('.', Ancestry(m)), m.State.ToString().ToLowerInvariant(), m.Path is { } at ? $"{Relative(at)}:{m.Line}" : null))]));

    private TestRunInfo TestInfo(Testing.TestController tests)
    {
        List<Testing.TestNode> failed = [.. tests.Tree.Roots.SelectMany(r => r.Methods).Where(m => m.State == Testing.TestState.Failed)];
        return new TestRunInfo(tests.Summary, [.. failed.Select(m => new TestFailure(string.Join('.', Ancestry(m)), m.Path is { } at ? $"{Relative(at)}:{m.Line}" : null, m.Details))]);
    }

    private static IEnumerable<string> Ancestry(Testing.TestNode method)
    {
        Stack<string> names = new();
        for (Testing.TestNode? node = method; node is { Kind: not Testing.TestNodeKind.Project }; node = node.Parent)
        {
            names.Push(node.Name);
        }
        return names;
    }

    // The debugger

    [McpServerTool(Name = "breakpoint_set", Title = "Set a breakpoint", Destructive = false), Description(
        "Sets a line breakpoint, with a condition (a C# bool expression) or a hit condition (\"==3\", \">=2\", \"%2\") if given, or changes " +
        "those of the one already there. A logMessage (\"count is {count}\") is written to the program's output on each hit; with " +
        "suspend false the breakpoint is a tracepoint and the program runs on. Bound at once while debugging.")]
    public Task<IReadOnlyList<BreakpointInfo>> SetBreakpoint(
        string path,
        int line,
        string? condition = null,
        string? hitCondition = null,
        [Description("Written on each hit, its {expressions} evaluated there")] string? logMessage = null,
        [Description("Whether a hit stops the program")] bool suspend = true, CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        string full = Resolve(path);
        BreakpointStore store = window.Debugger.Breakpoints;
        if (store.In(full).FirstOrDefault(b => b.Line == line - 1) is not { } breakpoint)
        {
            store.Toggle(full, line - 1);
            breakpoint = store.In(full).First(b => b.Line == line - 1);
        }
        store.Update(breakpoint, enabled: true, suspend, condition, hitCondition, logMessage);
        return Task.FromResult(Breakpoints(full));
    });

    [McpServerTool(Name = "breakpoint_remove", Title = "Remove a breakpoint", Destructive = false), Description("Removes the breakpoint on a line.")]
    public Task<IReadOnlyList<BreakpointInfo>> RemoveBreakpoint(string path, int line, CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        string full = Resolve(path);
        if (window.Debugger.Breakpoints.In(full).FirstOrDefault(b => b.Line == line - 1) is { } breakpoint)
        {
            window.Debugger.Breakpoints.Remove(breakpoint);
        }
        return Task.FromResult(Breakpoints(full));
    });

    [McpServerTool(Name = "breakpoints", Title = "Every breakpoint", ReadOnly = true), Description("Every breakpoint, and whether the running debugger bound it.")]
    public Task<IReadOnlyList<BreakpointInfo>> ListBreakpoints(CancellationToken cancel = default) => OnUi(cancel, () => Task.FromResult(Breakpoints(null)));

    [McpServerTool(Name = "debug_start", Title = "Debug a configuration", Destructive = true), Description(
        "Builds and launches a run configuration (the chosen one if none is named) under the debugger, as Debug (Alt+F5) does, then " +
        "waits up to waitSeconds for it to stop at a breakpoint or exception, or end. Returns where it is.")]
    public Task<DebugSnapshot> DebugStart(
        string? configuration = null,
        [Description(Arguments)] string? arguments = null,
        int waitSeconds = 60, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        Choose(configuration);
        if (await window.DebugAsync(arguments).WaitAsync(cancel) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }
        return await AfterResume(TimeSpan.FromSeconds(waitSeconds), cancel);
    });

    [McpServerTool(Name = "debug_state", Title = "Where the debugger is", ReadOnly = true), Description(
        "The debugger's state, and while paused where: the reason, the stopped thread's frames, and the chosen frame's variables.")]
    public Task<DebugSnapshot> GetDebugState(CancellationToken cancel = default) => OnUi(cancel, DebugSnapshotAsync);

    [McpServerTool(Name = "debug_continue", Title = "Resume", Destructive = false), Description("Resumes the program, and waits up to waitSeconds for it to stop again or end.")]
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
        DebugSession session = Session();
        await session.PauseAsync();
        await Until(() => session.State != DebugState.Running, TimeSpan.FromSeconds(waitSeconds), cancel);
        return await DebugSnapshotAsync();
    });

    [McpServerTool(Name = "debug_run_to", Title = "Run to a line", Destructive = false), Description(
        "Runs on to a line, as Run to Cursor does, stopping sooner at a breakpoint, and waits up to waitSeconds for the stop.")]
    public Task<DebugSnapshot> DebugRunTo(string path, int line, int waitSeconds = 30, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        await Paused().RunToCursorAsync(Resolve(path), line - 1);
        return await AfterResume(TimeSpan.FromSeconds(waitSeconds), cancel);
    });

    [McpServerTool(Name = "debug_set_next_statement", Title = "Set Next Statement", Destructive = false), Description(
        "Moves where the paused program goes on from to another line of the method it is paused in, as Rider's Set Next Statement: "
        + "the lines between are skipped, or done again when the line is earlier. Nothing runs; the snapshot is the new stop.")]
    public Task<DebugSnapshot> DebugSetNextStatement(string path, int line, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        DebugSession session = Paused();
        if (await window.Debugger.SetNextStatementAsync(session, Resolve(path), line - 1) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }
        await Until(() => session.StopReason == "goto", TimeSpan.FromSeconds(10), cancel);
        return await DebugSnapshotAsync();
    });

    [McpServerTool(Name = "debug_input", Title = "Type into the program's console", Destructive = false), Description(
        "Sends a line to the debugged program's standard input, as the Debug window's console does. The program reads it when it next reads.")]
    public Task<string> DebugInput(string text, [Description("End the line with a newline")] bool newline = true, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        await Session().InputAsync(newline ? text + "\n" : text);
        return "Sent";
    });

    [McpServerTool(Name = "debug_evaluate", Title = "Evaluate an expression", ReadOnly = true), Description(
        "Evaluates a C# expression in a frame of the stopped thread (0, the top, by default), as the Debug window's evaluate box does. " +
        "A result with members has a reference debug_variables opens.")]
    public Task<VariableInfo> DebugEvaluate(string expression, int frame = 0, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        DebugSession session = Paused();
        DapStackFrame chosen = (await Stack(session))[frame];
        EvaluateResponse value = await session.EvaluateAsync(expression, chosen.Id, "repl");
        return new VariableInfo(expression, value.Result, value.Type, value.VariablesReference);
    });

    [McpServerTool(Name = "debug_set_value", Title = "Set a value", Destructive = false), Description(
        "Assigns a C# expression's value to what another names (a local, a member, an element: \"order.Quantity\", \"list[2]\") in a frame " +
        "of the stopped thread, as Set Value (F2) in the Variables view does; the program goes on with it. Returns the value as it now is.")]
    public Task<VariableInfo> DebugSetValue(string expression, [Description("A C# expression: 42, \"text\", null, new List<int>()")] string value, int frame = 0, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        DebugSession session = Paused();
        DapStackFrame chosen = (await Stack(session))[frame];
        SetExpressionResponse set = await session.SetExpressionAsync(expression, value, chosen.Id);
        await window.Debugger.RefreshAsync();
        return new VariableInfo(expression, set.Value, set.Type, set.VariablesReference);
    });

    [McpServerTool(Name = "debug_variables", Title = "A value's members", ReadOnly = true), Description(
        "The members, elements or locals behind a reference from debug_state, debug_evaluate or an earlier call.")]
    public Task<IReadOnlyList<VariableInfo>> DebugVariables(int reference, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        DebugSession session = Paused();
        return (IReadOnlyList<VariableInfo>)[.. (await session.VariablesAsync(reference)).Select(v => new VariableInfo(v.Name, v.Value, v.Type, v.VariablesReference))];
    });

    [McpServerTool(Name = "debug_output", Title = "The program's output", ReadOnly = true), Description("What the debugged program has written, the last lines of it.")]
    public Task<string> DebugOutput(int lines = 200, CancellationToken cancel = default) => OnUi(cancel, () => Task.FromResult(Tail(window.BottomPanel.Debug.Console, lines)));

    // The IDE itself

    [McpServerTool(Name = "screenshot", Title = "What the window shows", ReadOnly = true), Description(
        "A PNG of the IDE's frontmost window as drawn now (a diff or a dialog when one is open over the main window), " +
        "or of a part of it (logical units, the window's own coordinates).")]
    public Task<ImageContentBlock> Screenshot(int? x = null, int? y = null, int? width = null, int? height = null, CancellationToken cancel = default) => OnUi(cancel, () =>
        Task.FromResult(ImageContentBlock.FromBytes(WindowPicture.Png(window.Frontmost, x is null ? null : new Avalonia.Rect(x.Value, y ?? 0, width ?? 400, height ?? 300)), "image/png")));

    [McpServerTool(Name = "ide_action", Title = "Run an IDE action", Destructive = true), Description(
        "Runs one of the IDE's actions by its id, as its shortcut would: app.* (app.build, app.debug.stepOver, app.code.rename and the rest) " +
        "or an editor action on the active editor. ide_actions lists them.")]
    public Task<string> IdeAction(string action, CancellationToken cancel = default) => OnUi(cancel, () => Task.FromResult(window.Perform(action) ? $"Ran {action}" : $"{action} did nothing here"));

    [McpServerTool(Name = "ide_actions", Title = "The IDE's actions", ReadOnly = true), Description(
        "Every action ide_action runs (those with no shortcut too), as id: name [group] (shortcut on this platform).")]
    public Task<IReadOnlyList<string>> IdeActions(CancellationToken cancel = default) => OnUi(cancel, () =>
        Task.FromResult((IReadOnlyList<string>)[.. ActionCatalog.All(AppActions.Keymap()).Select(a =>
            a.Shortcut is { } keys ? $"{a.Id}: {a.Title} [{a.Group}] ({keys})" : $"{a.Id}: {a.Title} [{a.Group}]")]));

    [McpServerTool(Name = "type", Title = "Type into the editor", Destructive = true), Description(
        "Types text into the active editor at its caret, as the keyboard would (typing a closing bracket over one already there, and so on).")]
    public Task<string> Type(string text, CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        if (window.EditorArea.Active is null)
        {
            throw new InvalidOperationException("No file is open in the editor");
        }
        window.EditorArea.CodeEditor.Type(text);
        return Task.FromResult($"Typed at {window.EditorArea.CodeEditor.CaretPosition.Line + 1}:{window.EditorArea.CodeEditor.CaretPosition.Column + 1}");
    });

    // The work behind them

    /// <summary>A tool's work on the UI thread, its waits ended by <paramref name="cancel"/> (the agent gave up on the
    /// call, or went). What it refuses (nothing to debug, no such file) reaches the agent in its own words: the SDK
    /// passes only an McpException's message on, and hides every other's.</summary>
    private static async Task<T> OnUi<T>(CancellationToken cancel, Func<Task<T>> work)
    {
        cancel.ThrowIfCancellationRequested();
        try
        {
            return await UiThread.InvokeAsync(work);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or TimeoutException or DapException or LanguageServerFailed)
        {
            throw new ModelContextProtocol.McpException(error is DapException dap ? $"The debugger refused: {dap.Reason}" : error.Message, error);
        }
    }

    private Task<DebugSnapshot> Resume(Func<DebugSession, Task> resume, int waitSeconds, CancellationToken cancel) => OnUi(cancel, async () =>
    {
        await resume(Paused());
        return await AfterResume(TimeSpan.FromSeconds(waitSeconds), cancel);
    });

    /// <summary>Waits for the program to stop or end after it was set going, then says where it is.</summary>
    private async Task<DebugSnapshot> AfterResume(TimeSpan limit, CancellationToken cancel)
    {
        await Until(() => window.Debugger.Session is not { State: DebugState.Running or DebugState.Starting }, limit, cancel);
        // A stop is shown (its frames chosen) a moment after the state says paused.
        await Until(() => window.Debugger.Session is not { State: DebugState.Paused } || window.BottomPanel.Debug.Frames.Count > 0, TimeSpan.FromSeconds(5), cancel);
        return await DebugSnapshotAsync();
    }

    private async Task<DebugSnapshot> DebugSnapshotAsync()
    {
        if (window.Debugger.Session is not { } session || session.State == DebugState.Ended)
        {
            return new DebugSnapshot("not debugging", null, null, [], [], window.Debugger.Session?.ExitCode, window.Status);
        }
        if (session.State != DebugState.Paused || session.StoppedThread is null)
        {
            return new DebugSnapshot(session.State.ToString().ToLowerInvariant(), null, null, [], [], null, window.Status);
        }
        IReadOnlyList<DapStackFrame> frames = await Stack(session);
        DapStackFrame? top = frames.FirstOrDefault(f => f.Source?.Path is not null) ?? (frames.Count > 0 ? frames[0] : null);
        List<VariableInfo> locals = [];
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
            top?.Source?.Path is { } path ? $"{Relative(path)}:{top.Line}:{top.Column}" : null,
            [.. frames.Take(20).Select((f, i) => $"{i}: {f.Name}{(f.Source?.Path is { } p ? $" at {Relative(p)}:{f.Line}" : "")}")],
            locals,
            null,
            window.Status,
            exception);
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

    private DebugSession Session() => window.Debugger.Session is { State: not DebugState.Ended } session
        ? session
        : throw new InvalidOperationException("Nothing is being debugged; debug_start starts it");

    private DebugSession Paused() => Session() is { State: DebugState.Paused } session
        ? session
        : throw new InvalidOperationException("The program is running; debug_pause stops it where it is");

    private void Choose(string? configuration)
    {
        if (configuration is not null && !window.SelectConfiguration(configuration))
        {
            throw new InvalidOperationException($"No run configuration '{configuration}'; there are: {string.Join(", ", window.Configurations.Select(c => c.Name))}");
        }
    }

    private IReadOnlyList<BreakpointInfo> Breakpoints(string? path) =>
        [.. (path is null ? window.Debugger.Breakpoints.All : window.Debugger.Breakpoints.In(path))
            .Select(b => new BreakpointInfo(Relative(b.Path), b.Line + 1, b.Enabled, b.Status.ToString().ToLowerInvariant(), b.Condition, b.HitCondition, b.LogMessage, b.Suspend, b.Message))];

    private async Task<(LspDocument Lsp, IReadOnlyList<CodeAction> Actions)> Actions(string path, int line, int column, int? endLine, int? endColumn, CancellationToken cancel)
    {
        (OpenDocument document, LspDocument lsp) = await Code(path, cancel);
        int offset = Offset(document, line, column);
        int end = endLine is { } last ? Offset(document, last, endColumn ?? 1) : offset;
        CSharpDocument? csharp = window.CSharpService?.For(document);
        IReadOnlyList<Diagnostic> here = [.. (csharp?.Diagnostics ?? []).Where(d => d.Range.Start.Line <= line - 1 && line - 1 <= d.Range.End.Line)];
        IReadOnlyList<CodeAction> actions = await lsp.CodeActionsAsync(TextRange.FromBounds(Math.Min(offset, end), Math.Max(offset, end)), here);
        return (lsp, [.. actions.Where(a => a.Disabled is null)]);
    }

    /// <summary>The language server, once it has loaded the workspace.</summary>
    private async Task<RoslynSession> Server(CancellationToken cancel)
    {
        if (window.CSharpService is null)
        {
            throw new InvalidOperationException("No C# language server runs for this folder");
        }
        await Until(() => window.CSharpService.IsReady, ServerLimit, cancel);
        return window.CSharpService is { IsReady: true, Session: { } session }
            ? session
            : throw new TimeoutException($"The language server has not loaded the workspace ({window.CSharpService.State})");
    }

    /// <summary>A file opened as the editor would, with the language server's document for it once the
    /// server has loaded the workspace.</summary>
    private async Task<(OpenDocument Document, LspDocument Lsp)> Code(string path, CancellationToken cancel)
    {
        OpenDocument document = Document(path);
        if (window.CSharpService is null)
        {
            throw new InvalidOperationException("No C# language server runs for this folder");
        }
        // Answers before the server has loaded the solution come from a file it knows alone, and are wrong.
        await Until(() => window.CSharpService.IsReady && window.CSharpService.For(document)?.Lsp is not null, ServerLimit, cancel);
        return (document, window.CSharpService.For(document)?.Lsp
            ?? throw new TimeoutException($"The language server has not loaded {Path.GetFileName(document.Path)} ({window.CSharpService.State})"));
    }

    private OpenDocument Document(string path)
    {
        string full = Resolve(path);
        if (window.EditorArea.Documents.FirstOrDefault(d => d.Path == full) is { } open)
        {
            return open;
        }
        window.OpenFile(full);
        return window.EditorArea.Documents.FirstOrDefault(d => d.Path == full)
            ?? throw new FileNotFoundException($"{full} could not be opened: {window.Status}");
    }

    private static int Offset(OpenDocument document, int line, int column)
    {
        TextSnapshot text = document.Text.Snapshot;
        int l = Math.Clamp(line - 1, 0, text.LineCount - 1);
        return Math.Min(text.GetLineStart(l) + Math.Max(0, column - 1), text.GetLine(l).End);
    }

    private async Task<IReadOnlyList<Place>> Places(IReadOnlyList<LspLocation> locations, CancellationToken cancel)
    {
        IReadOnlyList<(string Path, string Text)> lines = await Lines([.. locations.Select(l => (l.Uri, l.Range.Start.Line))], cancel);
        return [.. locations.Select((l, i) => new Place(lines[i].Path, l.Range.Start.Line + 1, l.Range.Start.Character + 1, lines[i].Text))];
    }

    /// <summary>Each place's path as the agent is shown it and its line's text, trimmed, worked out on the thread pool,
    /// each file read once: a symbol used thousands of times in a large solution never holds the person's window.</summary>
    private Task<IReadOnlyList<(string Path, string Text)>> Lines(IReadOnlyList<(string Uri, int Line)> places, CancellationToken cancel)
    {
        string? folder = window.Folder;
        // An editor's snapshot is immutable, so an open file's lines (unsaved edits and all) are read off the UI thread too.
        Dictionary<string, TextSnapshot> open = new(StringComparer.Ordinal);
        foreach (OpenDocument document in window.EditorArea.Documents)
        {
            open.TryAdd(document.Path, document.Text.Snapshot);
        }
        return BackgroundWork.Run(() =>
        {
            Dictionary<string, string> byUri = places.Select(p => p.Uri).Distinct(StringComparer.Ordinal).ToDictionary(u => u, DocumentUris.ToPath, StringComparer.Ordinal);
            string[] paths = [.. places.Select(p => byUri[p.Uri])];
            Dictionary<(string Path, int Line), string> texts = ReadLines([.. paths.Select((path, i) => (path, places[i].Line))], open, cancel);
            return (IReadOnlyList<(string, string)>)[.. paths.Select((path, i) => (Relative(folder, path), texts.GetValueOrDefault((path, places[i].Line), string.Empty)))];
        }, cancel);
    }

    /// <summary>The lines asked for, trimmed: an open file's from the editor, any other's from disk, read once and only as
    /// far as its last line asked for. A file that cannot be read has no lines, rather than failing the tool.</summary>
    private static Dictionary<(string Path, int Line), string> ReadLines(IReadOnlyList<(string Path, int Line)> wanted, Dictionary<string, TextSnapshot> open, CancellationToken cancel)
    {
        Dictionary<(string Path, int Line), string> texts = [];
        foreach (IGrouping<string, int> file in wanted.GroupBy(w => w.Path, w => w.Line, StringComparer.Ordinal))
        {
            cancel.ThrowIfCancellationRequested();
            HashSet<int> lines = [.. file];
            if (open.TryGetValue(file.Key, out TextSnapshot? text))
            {
                foreach (int line in lines.Where(l => l < text.LineCount))
                {
                    texts[(file.Key, line)] = text.GetLineText(line).Trim();
                }
                lines.RemoveWhere(l => l < text.LineCount);
            }
            if (lines.Count == 0)
            {
                continue;
            }
            int last = lines.Max();
            try
            {
                int at = 0;
                foreach (string line in File.ReadLines(file.Key))
                {
                    if (lines.Contains(at))
                    {
                        texts[(file.Key, at)] = line.Trim();
                    }
                    if (++at > last)
                    {
                        break;
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Gone or refused since the language server read it: its places are still places, without their text.
            }
        }
        return texts;
    }

    private Workspace Snapshot()
    {
        OpenDocument? active = window.EditorArea.Active;
        TextPosition? caret = active is null ? null : window.EditorArea.CodeEditor.CaretPosition;
        return new Workspace(
            window.Folder,
            window.Solution?.Path,
            [.. (window.WorkspaceModel?.Projects ?? []).Select(p => new ProjectInfo(p.Name, Relative(p.Path), p.OutputType, p.TargetFrameworks))],
            [.. window.Configurations.Select(c => c.Name)],
            window.Configuration?.Name,
            window.CSharpService is null ? "none" : window.CSharpService.IsReady ? "ready" : window.CSharpService.State,
            [.. window.EditorArea.Documents.Select(d => Relative(d.Path) + (d.IsModified ? " (unsaved)" : ""))],
            active is null ? null : $"{Relative(active.Path)}:{caret!.Value.Line + 1}:{caret.Value.Column + 1}",
            window.Debugger.Session?.State.ToString().ToLowerInvariant() ?? "not debugging");
    }

    /// <summary>A path an agent gave, from the open folder unless rooted, as the IDE names it (in any letter case).</summary>
    private string Resolve(string path) =>
        PathNames.Canonical(Path.IsPathRooted(path) || window.Folder is null ? path : Path.Combine(window.Folder, path));

    private string Relative(string path) => Relative(window.Folder, path);

    private static string Relative(string? folder, string path) => folder is not null && PathNames.IsUnder(path, folder)
        ? Path.GetRelativePath(folder, path)
        : path;

    /// <summary>A console's last lines, with what is on its way to it.</summary>
    private static string Tail(ToolWindows.OutputView console, int lines)
    {
        console.Flush();
        TextSnapshot text = console.Document.Snapshot;
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

public sealed record BuildResult(bool Succeeded, string Outcome, int Errors, int Warnings, IReadOnlyList<Finding> Problems, string OutputTail);

public sealed record RunResult(bool StillRunning, string? Status, string Output);

public sealed record BreakpointInfo(string Path, int Line, bool Enabled, string Status, string? Condition, string? HitCondition, string? LogMessage, bool Suspend, string? Message);

public sealed record VariableInfo(string Name, string Value, string? Type, int Reference);

public sealed record DebugSnapshot(
    string State,
    string? Reason,
    string? Location,
    IReadOnlyList<string> Frames,
    IReadOnlyList<VariableInfo> Locals,
    int? ExitCode,
    string? Status,
    string? Exception = null);

public sealed record TestRunInfo(string Summary, IReadOnlyList<TestFailure> Failures, DebugSnapshot? Debugger = null);

public sealed record TestFailure(string Test, string? Location, string Details);

public sealed record TestInfoRow(string Test, string State, string? Location);

public sealed record SymbolFound(string Name, string Kind, string? Container, string Path, int Line, int Column);
