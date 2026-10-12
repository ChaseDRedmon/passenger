using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Passenger.App.CSharp;
using Passenger.App.Debugging;
using Passenger.App.Editors;
using Passenger.App.Run;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App;

public partial class MainWindow : Window
{
    /// <summary>Room at the toolbar's left for macOS's window buttons once the toolbar is the title bar.</summary>
    private const double MacWindowButtons = 72;

    /// <summary>How many recently opened files Go to File offers before anything is typed.</summary>
    private const int RecentLimit = 25;

    private readonly KeymapDispatcher keys = new(AppActions.Keymap());
    private readonly FileIndex index = new();
    private CSharpService? csharp;
    private readonly CodeAssist assist;
    private readonly List<(string Path, int Offset)> history = [];
    private readonly ProcessRunner builder;
    private readonly ProcessRunner runner;
    private readonly DebugController debug;
    private readonly Testing.TestController tests;
    private Action<bool>? afterBuild;
    private readonly List<Problem> problems = [];
    private IReadOnlyList<RunConfiguration> configurations = [];
    private RunConfiguration? configuration;
    private readonly Passenger.App.Explorer.ExplorerView Explorer = new();
    private readonly ToolWindows.ToolViews Bottom = new();
    private readonly ToolWindows.StructureView Structure = new();
    private int historyAt = -1;
    private readonly List<string> recent = [];
    private string? folder;

    public MainWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsMacOS())
        {
            // The toolbar is the title bar on macOS, as in Rider. Windows keeps the system's title bar
            // until the shell draws its own chrome there.
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = Toolbar.Height;
        }
        // One keymap for the whole window, seen before any control: IDE actions run from anywhere,
        // editor actions go to the editor when it has the focus.
        AddHandler(KeyDownEvent, OnKeyDownFirst, RoutingStrategies.Tunnel);
        AddToolWindows();
        AddVcs();
        AddTerminal();
        AddSettings();
        AddPackages();
        AddProfiler();
        AddBreadcrumbs();
        AddBookmarks();
        AddHierarchy();
        AddFileOperations();
        AddAutosave();
        AddConflicts();
        AddErrors();
        Explorer.FileOpened += (_, path) => OpenFile(path);
        Structure.Follow(() => Editors.Active is { } shown
            ? (shown.Path, csharp?.For(shown)?.Lsp, Editors.CodeEditor.CaretPosition.Line)
            : null);
        Structure.GoToRequested += (_, row) => NavigateTo(row.Path, row.Position);
        Editors.ActiveChanged += (_, _) => Structure.Invalidate();
        Editors.CaretMoved += (_, _) => Structure.FollowCaret();
        Editors.Opened += (_, document) => document.Text.Changed += (_, _) =>
        {
            if (Editors.Active == document)
            {
                Structure.Invalidate();
            }
        };
        Editors.OpenRequested += (_, _) => _ = PickFolder();
        Editors.ActiveChanged += (_, _) => UpdateStatus();
        Editors.CaretMoved += (_, _) => UpdateCaret();
        Editors.SaveFailed += (_, problem) => StatusText.Text = problem;
        Editors.Opened += (_, document) =>
        {
            csharp?.Attach(document);
            debug?.Attach(document);
        };
        Editors.Closed += (_, document) =>
        {
            csharp?.Detach(document);
            debug?.Detach(document);
        };
        Editors.ForEachEditor(editor => editor.ProblemsRequested += (_, _) => ShowToolWindow("problems"));
        Editors.ForEachEditor(editor => editor.ExecutionPointDragged += (_, line) =>
        {
            if (Editors.Active is { } active && debug?.Session is { State: DebugState.Paused } session)
            {
                _ = debug.SetNextStatementAsync(session, active.Path, line);
            }
        });
        Editors.ForEachEditor(editor => editor.GutterClicked += (_, click) =>
        {
            if (Editors.Active is not { } active || debug is null)
            {
                return;
            }
            if (!click.Context && editor.IsInMarkColumn(click.Position) && active.TestMarks.Any(m => m.Line == click.Line))
            {
                // A test's run icon: Rider's menu of Run and Debug for it.
                ShowTestMenu(editor, active, click.Line);
                return;
            }
            if (!click.Context && editor.IsInAnnotationColumn(click.Position))
            {
                _ = ShowBlamedCommitAsync(active, click.Line);
                return;
            }
            if (!click.Context && editor.IsInChangeColumn(click.Position) && ChangeAt(active, click.Line) is { } change)
            {
                _ = ShowChangePopupAsync(editor, active, change);
                return;
            }
            if (click.Context)
            {
                // A right-click on a breakpoint opens its properties, as in Rider.
                if (debug.Breakpoints.In(active.Path).FirstOrDefault(b => b.Line == click.Line) is { } breakpoint)
                {
                    ShowBreakpointPopup(breakpoint);
                }
                else
                {
                    ShowGutterMenu(editor, active);
                }
            }
            else if (click.Modifiers.HasFlag(KeyModifiers.Shift))
            {
                debug.Breakpoints.ToggleTracepoint(active.Path, click.Line);
            }
            else
            {
                debug.Breakpoints.Toggle(active.Path, click.Line);
            }
        });
        Closing += (_, _) => _ = csharp?.DisposeAsync();
        Closing += (_, _) => watcher?.Dispose();
        assist = new CodeAssist(Editors, Editors.AssistLayer, () => csharp);
        assist.Navigate += (_, target) => NavigateTo(target.Path, target.Position);
        assist.Message += (_, message) => StatusText.Text = message;
        builder = new ProcessRunner(Bottom.Build);
        builder.Line += (_, line) =>
        {
            if (BuildOutput.Parse(line) is { } problem && !problems.Contains(problem))
            {
                problems.Add(problem);
            }
        };
        builder.Exited += (_, code) => OnBuilt(code);
        runner = new ProcessRunner(Bottom.Run);
        runner.Exited += (_, code) =>
        {
            Bottom.Run.SetRunning(false);
            StopButton.IsVisible = false;
            StatusText.Text = $"{configuration?.Name} exited with code {code}";
        };
        Bottom.Build.RerunClicked += (_, _) => BuildSolution();
        Bottom.Build.StopClicked += (_, _) => builder.Stop();
        Bottom.Run.RerunClicked += (_, _) => RunSelected();
        Bottom.Run.StopClicked += (_, _) => runner.Stop();
        Bottom.Problems.Chosen += (_, problem) =>
        {
            if (problem.Path is { } path)
            {
                NavigateTo(path, problem.Position);
            }
        };
        debug = new DebugController(Editors, Bottom.Debug);
        tests = new Testing.TestController(Bottom.TestTree, BuildAsync, () => Projects, line => Bottom.Build.Document.Append($"{line}\n"));
        tests.Message += (_, message) =>
        {
            StatusText.Text = message;
            Bottom.Tests.Status = message;
        };
        tests.LaunchUnderDebugger = LaunchTestHost;
        Closing += (_, _) => _ = tests.DisposeAsync();
        DispatcherTimer marksTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
        marksTimer.Tick += (_, _) =>
        {
            marksTimer.Stop();
            UpdateAllTestMarks();
        };
        Bottom.TestTree.Updated += (_, _) =>
        {
            marksTimer.Stop();
            UpdateAllTestMarks();
        };
        Bottom.TestTree.ResultApplied += (_, _) =>
        {
            marksTimer.Stop();
            marksTimer.Start();
        };
        Editors.Opened += (_, document) =>
        {
            UpdateTestMarks(document);
            document.Text.Changed += (_, _) =>
            {
                marksTimer.Stop();
                marksTimer.Start();
            };
        };
        tests.AttachDebugger = async processId =>
        {
            ShowToolWindow("debug");
            await debug.AttachAsync(processId, "Tests");
            if (debug.Session is not { } session)
            {
                return false;
            }
            // VSTest runs the tests the moment this says yes: it waits until the debugger has hold of the test host.
            try
            {
                return await session.ProcessStarted.WaitAsync(TimeSpan.FromSeconds(30)) == processId;
            }
            catch (TimeoutException)
            {
                return false;
            }
        };
        Bottom.Tests.ActionRequested += (_, action) => RunAppAction(action);
        Bottom.Tests.RunRequested += (_, debugging) =>
        {
            IReadOnlyList<Testing.TestNode> chosen = Bottom.Tests.Selected;
            RunTests(() => tests.RunAsync(folder!, chosen, debugging));
        };
        Bottom.Tests.UntilFailureRequested += (_, _) => RunUntilFailure(Bottom.Tests.Selected);
        Bottom.Problems.FixRequested += (_, problem) =>
        {
            NavigateTo(problem.Path!, problem.Position);
            assist.Run(AppActions.QuickFix);
        };
        Structure.UsagesRequested += (_, row) =>
        {
            NavigateTo(row.Path, row.Position);
            assist.Run(AppActions.FindUsages);
        };
        Bottom.Tests.GoToRequested += (_, node) =>
        {
            if (node.Path is { } path && File.Exists(path))
            {
                NavigateTo(PathNames.Canonical(path), new TextPosition(Math.Max(0, node.Line - 1), 0));
            }
        };
        debug.ActionRequested += (_, action) => RunAppAction(action);
        debug.Message += (_, message) => StatusText.Text = message;
        debug.Navigate += (_, target) => ShowStop(target.Path, target.Position);
        debug.StateChanged += (_, _) => OnDebugStateChanged();
        assist.DebuggerValue = debug.ValueAt;
        Closing += (_, _) => _ = debug.StopAsync();
        AddFind();
        AddProblems();
        AddMainMenu();
        AddContextMenus();
        UpdateStatus();
    }

    /// <summary>The C# service for the open folder, or null when it has no projects.</summary>
    public CSharpService? CSharpService => csharp;

    public EditorArea EditorArea => Editors;

    public Explorer.ExplorerView ExplorerView => Explorer;

    /// <summary>Every file of the open folder, as Go to File searches them.</summary>
    public FileIndex FileIndex => index;

    /// <summary>The open folder's solution, read from its files, or null for a folder without one.</summary>
    public SolutionModel? Solution { get; private set; }

    /// <summary>Opens what the command line named: a folder, files in it, a caret position.</summary>
    public void Start(LaunchOptions options)
    {
        if (options.Folder is { } path)
        {
            OpenFolder(path);
        }
        foreach (string file in options.Files)
        {
            if (file.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
            {
                // A profiler's trace opens as a snapshot, as a dotTrace snapshot opens in Rider.
                _ = OpenSnapshotAsync(Path.GetFullPath(file));
                continue;
            }
            OpenFile(file);
        }
        if (options.Caret is { } caret && Editors.Active is not null)
        {
            Editors.CodeEditor.GoTo(caret);
        }
    }

    public void OpenFolder(string path)
    {
        string opening = PathNames.Canonical(path);
        if (folder is not null && folder != opening)
        {
            // Another folder's files are its own: this one's are kept with it and close, saved, as Rider's do.
            KeepWorkspace();
            if (!Editors.CloseAll())
            {
                return;
            }
            recent.Clear();
        }
        Autosave();
        folder = opening;
        string name = Path.GetFileName(folder);
        Solution = SolutionModel.Find(folder) is { } solutionPath ? SolutionModel.Load(solutionPath) : null;
        Explorer.Load(folder, Solution);
        _ = vcs.OpenAsync(folder);
        Explorer.Tree.Unloaded.Clear();
        configurations = Configure();
        configuration = configurations.FirstOrDefault();
        RunConfigName.Text = configuration?.Name ?? "No configurations";
        index.Build(folder);
        Watch(folder);
        _ = csharp?.DisposeAsync();
        csharp = null;
        if (CSharpService.Wants(folder))
        {
            csharp = new CSharpService();
            csharp.StateChanged += (_, _) => LanguageText.Text = LanguageLabel();
            csharp.Stopped += (_, message) => StatusText.Text = message;
            WatchProblems(csharp);
            csharp.Start(folder);
        }
        ShowSolutionProblems();
        Editors.ShowFolderHint(folder);
        RestoreWorkspace();
        // Rider names the window's project widget after the solution, the folder only without one.
        string shown = Solution?.Name ?? name;
        ProjectName.Text = shown;
        ProjectBadgeText.Text = Initials(shown);
        ProjectBadge.IsVisible = true;
        UpdateStatus();
    }

    public void OpenFile(string path)
    {
        string full = PathNames.Canonical(Path.IsPathRooted(path) || folder is null ? path : Path.Combine(folder, path));
        if (Editors.Open(full) is { } problem)
        {
            StatusText.Text = problem;
            return;
        }
        recent.Remove(full);
        recent.Insert(0, full);
        if (recent.Count > RecentLimit)
        {
            recent.RemoveAt(recent.Count - 1);
        }
        Explorer.Reveal(full);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExtendedIntoWindowDecorationsProperty)
        {
            double left = IsExtendedIntoWindowDecorations && OperatingSystem.IsMacOS() ? MacWindowButtons : 8;
            ToolbarContent.Margin = new Thickness(left, 0, 8, 0);
        }
    }

    /// <summary>A key's action that throws is reported as an IDE error, never the IDE's end.</summary>
    private void OnKeyDownFirst(object? sender, KeyEventArgs e)
    {
        try
        {
            OnKey(e);
        }
        catch (Exception error)
        {
            IdeErrors.Report(error);
            e.Handled = true;
        }
    }

    private void OnKey(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && Overlay.IsVisible)
        {
            // Escape closes what is over the window before anything under it hears it.
            DismissPopup();
            e.Handled = true;
            return;
        }
        if (TerminalTakes(e))
        {
            return;
        }
        string? action = keys.Press(e, out bool consumed, out bool shifted);
        if (action is null)
        {
            e.Handled |= consumed;
            return;
        }
        if (AppActions.IsApp(action))
        {
            if (!shifted)
            {
                RunAppAction(action);
                e.Handled = true;
            }
            return;
        }
        CodeEditor editor = Editors.CodeEditor;
        if (editor.IsFocused && !shifted && assist.TakeKey(action))
        {
            e.Handled = true;
        }
        else if (editor.IsFocused && (!shifted || EditorActions.IsMovement(action)) && editor.TryExecute(action, shifted))
        {
            e.Handled = true;
        }
        else if (action == EditorActions.Escape && editor.IsFocused && Editors.FindBar.IsVisible)
        {
            // Escape with nothing of the editor's own to undo closes what sits over it, as in Rider.
            Editors.FindBar.Close();
            e.Handled = true;
        }
    }

    private void RunAppAction(string action)
    {
        if (action.StartsWith("app.code.", StringComparison.Ordinal))
        {
            if (Editors.Active is not null)
            {
                assist.Run(action);
            }
            return;
        }
        if (action.StartsWith("app.vcs.", StringComparison.Ordinal))
        {
            RunVcsAction(action);
            return;
        }
        if (action.StartsWith("app.bookmarks.", StringComparison.Ordinal))
        {
            RunBookmarkAction(action);
            return;
        }
        switch (action)
        {
            case AppActions.ExtendSelection:
                ExtendSelection();
                break;
            case AppActions.CallHierarchy:
                _ = ShowCallHierarchyAsync();
                break;
            case AppActions.TypeHierarchy:
                _ = ShowTypeHierarchyAsync();
                break;
            case AppActions.ShrinkSelection:
                ShrinkSelection();
                break;
            case AppActions.Build:
                BuildSolution();
                break;
            case AppActions.RebuildSolution:
                Build(BuildKind.Rebuild);
                break;
            case AppActions.CleanSolution:
                Build(BuildKind.Clean);
                break;
            case AppActions.BuildSelected:
                BuildSelected(BuildKind.Build);
                break;
            case AppActions.RebuildSelected:
                BuildSelected(BuildKind.Rebuild);
                break;
            case AppActions.CleanSelected:
                BuildSelected(BuildKind.Clean);
                break;
            case AppActions.RunProgram:
                RunSelected();
                break;
            case AppActions.Rerun when debug.IsDebugging:
                DebugSelected();
                break;
            case AppActions.Rerun:
                RunSelected();
                break;
            case AppActions.Debug:
                DebugSelected();
                break;
            case AppActions.Resume when !debug.IsDebugging:
                // F5 starts debugging when nothing is being debugged, as in Visual Studio.
                DebugSelected();
                break;
            case AppActions.Stop:
                _ = debug.StopAsync();
                runner.Stop();
                builder.Stop();
                afterBuild = null;
                break;
            case AppActions.ShowDebug:
                ShowToolWindow("debug");
                break;
            case { } debugging when debugging.StartsWith("app.debug.", StringComparison.Ordinal):
                debug.Run(debugging);
                break;
            case AppActions.EditBreakpoint when Editors.Active is { } file:
                int caretLine = Editors.CodeEditor.CaretPosition.Line;
                if (debug.Breakpoints.In(file.Path).FirstOrDefault(b => b.Line == caretLine) is not { } editing)
                {
                    // Alt+F9 on a line with none sets one to edit, as Rider's Edit Breakpoint does.
                    debug.Breakpoints.Toggle(file.Path, caretLine);
                    editing = debug.Breakpoints.In(file.Path).First(b => b.Line == caretLine);
                }
                ShowBreakpointPopup(editing);
                break;
            case AppActions.ViewBreakpoints:
                ShowBreakpoints(null);
                break;
            case AppActions.ShowTests:
                ShowToolWindow("tests");
                break;
            case AppActions.ShowTerminal:
                ToggleTerminal();
                break;
            case AppActions.FindAction:
                ShowFindAction();
                break;
            case AppActions.RecentFiles:
                ShowRecentFiles();
                break;
            case AppActions.FindInFiles or AppActions.ReplaceInFiles:
                ShowFindInFiles(replace: action == AppActions.ReplaceInFiles);
                break;
            case AppActions.ShowSettings:
                ShowSettings();
                break;
            case AppActions.ShowLog:
                ErrorsPopup.ShowLog();
                break;
            case AppActions.ShowNuGet:
                ShowToolWindow("nuget");
                break;
            case AppActions.ShowProfiler:
                ShowToolWindow("profiler");
                break;
            case AppActions.Profile:
                _ = ProfileReportingAsync();
                break;
            case AppActions.ShowStructure:
                ShowToolWindow("structure");
                _ = Structure.Refresh();
                break;
            case AppActions.SplitRight when Editors.Active is { } splitting:
                Editors.Split(Editors.ActiveGroup, splitting, Avalonia.Layout.Orientation.Horizontal, move: false);
                break;
            case AppActions.SplitDown when Editors.Active is { } splitting:
                Editors.Split(Editors.ActiveGroup, splitting, Avalonia.Layout.Orientation.Vertical, move: false);
                break;
            case AppActions.MoveToOppositeGroup when Editors.Active is { } moving:
                Editors.MoveToOpposite(Editors.ActiveGroup, moving);
                break;
            case AppActions.MaximizeToolWindow:
                Dock.ToggleMaximize();
                break;
            case AppActions.HideAllToolWindows:
                Dock.ToggleHideAll();
                break;
            case AppActions.RunAllTests:
                RunTests(() => tests.RunAsync(folder!));
                break;
            case AppActions.RunTests or AppActions.DebugTests:
                RunTestsInContext(debug: action == AppActions.DebugTests);
                break;
            case AppActions.RunTestsUntilFailure:
                RunTestsInContext(debug: false, untilFailure: true);
                break;
            case AppActions.RerunTests:
                RunTests(() => tests.RerunAsync(folder!));
                break;
            case AppActions.RerunFailedTests:
                RunTests(() => tests.RerunFailedAsync(folder!));
                break;
            case AppActions.StopTests:
                tests.Stop();
                break;
            case AppActions.ShowBuild:
                ShowToolWindow("build");
                break;
            case AppActions.ShowRun:
                ShowToolWindow("run");
                break;
            case AppActions.ShowProblems:
                ShowToolWindow("problems");
                break;
            case AppActions.ShowFind:
                ShowToolWindow("find");
                break;
            case AppActions.ToggleSolutionAnalysis:
                ToggleSolutionAnalysis();
                break;
            case AppActions.HideToolWindow:
                HideToolWindow();
                break;
            case AppActions.Back:
                StepHistory(-1);
                break;
            case AppActions.Forward:
                StepHistory(1);
                break;
            case AppActions.Open:
                _ = PickFolder();
                break;
            case AppActions.Exit:
                Close();
                break;
            case AppActions.ShowContextMenu:
                ShowFocusedContextMenu();
                break;
            case AppActions.Save:
                Editors.SaveActive();
                break;
            case AppActions.SaveAll:
                Editors.SaveAll();
                break;
            case AppActions.CloseTab:
                if (Editors.Active is { } active)
                {
                    Editors.Close(active);
                }
                break;
            case AppActions.NextTab:
                Editors.Cycle(1);
                break;
            case AppActions.PreviousTab:
                Editors.Cycle(-1);
                break;
            case AppActions.FocusExplorer:
                Dock.Show("explorer");
                Explorer.Focus();
                break;
            case AppActions.GoToFile:
                ShowGoToFile();
                break;
            case AppActions.GoToLine:
                ShowGoToLine();
                break;
            case AppActions.GoToSymbol:
                ShowSymbols("Go to Symbol", "A type or member, by name or CamelHumps", SearchSymbols);
                break;
            case AppActions.GoToEverything:
                ShowSymbols("Go to Everything", "A type, member or file", SearchEverything);
                break;
            case AppActions.FileStructure:
                ShowFileStructure();
                break;
            case AppActions.Find:
                Editors.ShowFind(replace: false);
                break;
            case AppActions.Replace:
                Editors.ShowFind(replace: true);
                break;
            case AppActions.FindNext:
                Editors.FindBar.Next();
                break;
            case AppActions.FindPrevious:
                Editors.FindBar.Previous();
                break;
        }
    }

    public bool IsPopupOpen => Overlay.IsVisible;

    /// <summary>What the open popup is (Go to File, a breakpoint's properties, the Breakpoints window), or null.</summary>
    public Control? Popup => Overlay.IsVisible ? OverlayHost.Content as Control : null;

    public CodeAssist Assist => assist;

    /// <summary>The tool windows' views: Build, Run, Debug, Unit Tests, Problems.</summary>
    public ToolWindows.ToolViews BottomPanel => Bottom;

    public DebugController Debugger => debug;

    /// <summary>The open folder, canonical, or null before one is opened.</summary>
    public string? Folder => folder;

    public IReadOnlyList<RunConfiguration> Configurations => configurations;

    public RunConfiguration? Configuration => configuration;

    /// <summary>The program the Run window shows, while it runs or since it last did.</summary>
    public ProcessRunner Runner => runner;

    /// <summary>Chooses the run configuration by name, as the toolbar's menu does; false when there is none.</summary>
    public bool SelectConfiguration(string name)
    {
        if (configurations.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } chosen)
        {
            return false;
        }
        configuration = chosen;
        RunConfigName.Text = chosen.Name;
        return true;
    }

    /// <summary>Builds as Build does, and says whether it succeeded once it has.</summary>
    public Task<bool> BuildAsync()
    {
        TaskCompletionSource<bool> built = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (folder is null || builder.IsRunning)
        {
            built.SetResult(false);
            return built.Task;
        }
        BuildSolution(succeeded => built.TrySetResult(succeeded));
        return built.Task;
    }

    /// <summary>Builds, then launches the chosen configuration under the debugger, as Rider's Debug does,
    /// with these arguments in place of its own if given; the Debug window opens on its Console until the
    /// program stops somewhere. Null once it runs, or what stopped it (a failed build, no configuration,
    /// the adapter's refusal).</summary>
    public async Task<string?> DebugAsync(string? arguments = null)
    {
        if (configuration is not { } chosen)
        {
            return "Nothing to debug: no executable project";
        }
        if (!await BuildAsync())
        {
            return "The build failed; Problems lists why";
        }
        BackgroundWork.Begin();
        try
        {
            DebugTarget target = await DebugTarget.ResolveAsync(chosen, arguments);
            ShowToolWindow("debug");
            Bottom.Debug.ShowTab(console: true);
            await debug.StartAsync(target, chosen.Name);
            return debug.IsDebugging ? null : StatusText.Text;
        }
        catch (Exception error) when (error is InvalidOperationException or IOException)
        {
            return error.Message;
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    /// <summary>Runs an action by id, as its shortcut would: the IDE's own (app.*) wherever the focus is,
    /// an editor's on the active editor. False for one that does nothing here.</summary>
    public bool Perform(string action)
    {
        if (AppActions.IsApp(action))
        {
            RunAppAction(action);
            return true;
        }
        return Editors.Active is not null && Editors.CodeEditor.TryExecute(action);
    }

    /// <summary>The status bar's text, which says what the last action came to.</summary>
    public string? Status => StatusText.Text;

    /// <summary>Agents attached to this window's MCP socket, shown in the status bar so the person knows
    /// another hand is on the IDE.</summary>
    public int Agents
    {
        get => agents;
        set
        {
            agents = value;
            AgentText.IsVisible = value > 0;
            AgentText.Text = value == 1 ? "Agent connected" : $"{value} agents connected";
        }
    }

    private int agents;

    /// <summary>Builds the solution (or the folder's project) with the .NET command line, its output
    /// in the Build window and what it finds in Problems; <paramref name="then"/> hears whether it
    /// succeeded.</summary>
    public void BuildSolution(Action<bool>? then = null) => Build(BuildKind.Build, null, then);

    /// <summary>Builds, rebuilds or cleans the solution, or the projects given one after another (stopping at the first
    /// that fails), as Rider's Build menu and the Solution view's do: the output in the Build window, the problems of all
    /// of them in Problems.</summary>
    public void Build(BuildKind kind, IReadOnlyList<string>? projects = null, Action<bool>? then = null)
    {
        if (folder is null || builder.IsRunning)
        {
            return;
        }
        afterBuild = then;
        buildKind = kind;
        string solution = Solution?.Path ?? Directory.EnumerateFiles(folder, "*.csproj").FirstOrDefault() ?? folder;
        // With projects unloaded, the solution is the projects still loaded, built one after another.
        IReadOnlyList<string> whole = Solution is not null && Explorer.Tree.Unloaded.Count > 0 ? [.. Projects.Select(p => p.Path)] : [solution];
        buildQueue.Clear();
        foreach (string target in projects is { Count: > 0 } ? projects : whole)
        {
            buildQueue.Enqueue(target);
        }
        if (buildQueue.Count == 0)
        {
            StatusText.Text = "Nothing to build: every project is unloaded";
            afterBuild = null;
            then?.Invoke(false);
            return;
        }
        Editors.SaveAll();
        problems.Clear();
        Bottom.Build.Document.Clear();
        Bottom.Build.SetRunning(true);
        ShowToolWindow("build");
        BackgroundWork.Begin();
        BuildNext();
    }

    /// <summary>Whether a build, rebuild or clean is under way, with more of its projects to come.</summary>
    public bool IsBuilding => builder.IsRunning || buildQueue.Count > 0;

    private readonly Queue<string> buildQueue = new();
    private BuildKind buildKind;

    private void BuildNext()
    {
        string target = buildQueue.Dequeue();
        string verb = buildKind switch { BuildKind.Rebuild => "Rebuilding", BuildKind.Clean => "Cleaning", _ => "Building" };
        StatusText.Text = $"{verb} {Path.GetFileName(target)}…";
        builder.Start(Lsp.Dotnet.Host(), BuildOutput.Arguments(target, buildKind), folder!);
    }

    /// <summary>The projects Build Selected Projects means: those chosen in the Explorer, else the active file's.</summary>
    public IReadOnlyList<string> SelectedProjects()
    {
        List<string> chosen = [.. Explorer.SelectedNodes.Where(n => n.Kind == Passenger.App.Explorer.NodeKind.Project && n.Path is not null).Select(n => n.Path!)];
        if (chosen.Count == 0 && Editors.Active is { } file
            && Projects.Where(p => file.Path.StartsWith(p.Folder + Path.DirectorySeparatorChar, StringComparison.Ordinal)).MaxBy(p => p.Folder.Length) is { } owner)
        {
            chosen.Add(owner.Path);
        }
        return chosen;
    }

    private void BuildSelected(BuildKind kind)
    {
        if (SelectedProjects() is { Count: > 0 } chosen)
        {
            Build(kind, chosen);
        }
        else
        {
            StatusText.Text = "No project is chosen in the Explorer or open in the editor";
        }
    }

    /// <summary>Runs the chosen configuration with <c>dotnet run</c>, its output in the Run window, with these
    /// arguments in place of its own if given.</summary>
    public void RunSelected(string? arguments = null)
    {
        if (configuration is not { } chosen || runner.IsRunning)
        {
            StatusText.Text = configuration is null ? "Nothing to run: no executable project" : "Already running";
            return;
        }
        Editors.SaveAll();
        Bottom.Run.Document.Clear();
        Bottom.Run.SetRunning(true);
        StopButton.IsVisible = true;
        ShowToolWindow("run");
        if (settings.HotReload)
        {
            // dotnet watch applies each save to the running program, as Rider's Hot Reload does, and restarts it
            // for an edit that cannot be applied rather than asking.
            StatusText.Text = $"Running {chosen.Name} with Hot Reload: a save applies its changes";
            runner.Start(Lsp.Dotnet.Host(), ["watch", "--non-interactive", .. chosen.RunArguments(arguments)], chosen.Project.Folder,
                new Dictionary<string, string> { ["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1", ["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "true" });
            return;
        }
        StatusText.Text = $"Running {chosen.Name}";
        runner.Start(Lsp.Dotnet.Host(), chosen.RunArguments(arguments), chosen.Project.Folder);
    }

    /// <summary>Debug, from its key or the toolbar: what stopped it, if anything, goes to the status bar.</summary>
    public void DebugSelected()
    {
        _ = Debug();

        async Task Debug()
        {
            if (await DebugAsync() is { } problem)
            {
                StatusText.Text = problem;
            }
        }
    }

    /// <summary>Shows where the debugger stopped: the file and line, and the Debug window's frames.</summary>
    private void ShowStop(string path, TextPosition position)
    {
        NavigateTo(path, position);
        ShowToolWindow("debug");
        Bottom.Debug.ShowTab(console: false);
    }

    private void OnDebugStateChanged()
    {
        StopButton.IsVisible = debug.IsDebugging || runner.IsRunning;
    }

    private void OnBuilt(int code)
    {
        if (code == 0 && buildQueue.Count > 0)
        {
            Bottom.Build.Document.Append("\n");
            BuildNext();
            return;
        }
        buildQueue.Clear();
        BackgroundWork.End();
        Action<bool>? then = afterBuild;
        afterBuild = null;
        Bottom.Build.SetRunning(false);
        int errors = problems.Count(p => p.Severity == ProblemSeverity.Error);
        int warnings = problems.Count - errors;
        Bottom.Problems.Show(problems);
        if (Dock["problems"] is { } problemsWindow)
        {
            problemsWindow.Title = problems.Count == 0 ? "Problems" : $"Problems {problems.Count}";
        }
        string noun = buildKind switch { BuildKind.Rebuild => "Rebuild", BuildKind.Clean => "Clean", _ => "Build" };
        StatusText.Text = code == 0
            ? $"{noun} succeeded{(warnings > 0 ? $" with {warnings} warning{(warnings == 1 ? "" : "s")}" : "")}"
            : $"{noun} failed: {errors} error{(errors == 1 ? "" : "s")}, {warnings} warning{(warnings == 1 ? "" : "s")}";
        if (code != 0 && errors > 0)
        {
            ShowToolWindow("problems");
        }
        then?.Invoke(code == 0);
    }

    /// <summary>Opens a tool window in its place ("debug", "build", "tests"...), as its stripe button does.</summary>
    public void ShowToolWindow(string id) => Dock.Show(id);

    /// <summary>Shift+Escape: the tool window open last closes, and the editor has the keys again.</summary>
    private void HideToolWindow()
    {
        Dock.HideActive();
        if (Editors.Active is not null)
        {
            Editors.CodeEditor.Focus();
        }
    }

    /// <summary>The tool windows, each in the place IntelliJ gives it at first.</summary>
    private void AddToolWindows()
    {
        Dock.Add(new Docking.ToolWindow("explorer", "Explorer", "Icon.Explorer", Explorer, Docking.ToolAnchor.LeftTop)
        {
            Action = AppActions.FocusExplorer,
            HeaderTools = Explorer.TakeHeaderTools(),
        });
        Dock.Add(new Docking.ToolWindow("structure", "Structure", "Icon.Structure", Structure, Docking.ToolAnchor.LeftBottom) { Action = AppActions.ShowStructure });
        Dock.Add(new Docking.ToolWindow("run", "Run", "Icon.RunOutline", Bottom.Run, Docking.ToolAnchor.BottomLeft) { Action = AppActions.ShowRun });
        Dock.Add(new Docking.ToolWindow("debug", "Debug", "Icon.Debug", Bottom.Debug, Docking.ToolAnchor.BottomLeft) { Action = AppActions.ShowDebug });
        Dock.Add(new Docking.ToolWindow("tests", "Unit Tests", "Icon.Tests", Bottom.Tests, Docking.ToolAnchor.BottomLeft) { Action = AppActions.ShowTests });
        Dock.Add(new Docking.ToolWindow("build", "Build", "Icon.Build", Bottom.Build, Docking.ToolAnchor.BottomLeft) { Action = AppActions.ShowBuild });
        Dock.Add(new Docking.ToolWindow("problems", "Problems", "Icon.Problems", Bottom.Problems, Docking.ToolAnchor.BottomLeft) { Action = AppActions.ShowProblems });
        Dock.Show("explorer");
    }

    /// <summary>The window a person uses keeps its layout: it opens on the one kept, and keeps each change; and each
    /// folder's editors and recent files, which come back when the folder opens again.</summary>
    public void KeepLayout()
    {
        if (Docking.DockLayout.Load() is { } kept)
        {
            Dock.Apply(kept);
        }
        Dock.LayoutChanged += (_, _) => Dock.Capture().Save();
        Closing += (_, _) => Dock.Capture().Save();
        keepsWorkspace = true;
        Closing += (_, _) => KeepWorkspace();
        RestoreWorkspace();
    }

    private bool keepsWorkspace;

    private void KeepWorkspace()
    {
        if (keepsWorkspace && folder is not null)
        {
            new WorkspaceState { Editors = Editors.Capture(), RecentFiles = [.. recent], UnloadedProjects = [.. Explorer.Tree.Unloaded] }.Save(folder);
        }
    }

    private void RestoreWorkspace()
    {
        if (!keepsWorkspace || folder is null || WorkspaceState.Load(folder) is not { } kept)
        {
            return;
        }
        if (kept.UnloadedProjects.Count > 0)
        {
            foreach (string project in kept.UnloadedProjects)
            {
                Explorer.Tree.Unloaded.Add(project);
            }
            Explorer.Refresh();
            configurations = Configure();
            configuration = configurations.FirstOrDefault();
            RunConfigName.Text = configuration?.Name ?? "No configurations";
        }
        if (kept.Editors is { } editors)
        {
            Editors.Restore(editors);
        }
        foreach (string file in kept.RecentFiles.Where(File.Exists).Reverse())
        {
            recent.Remove(file);
            recent.Insert(0, file);
        }
        if (Editors.Active is { } shown)
        {
            recent.Remove(shown.Path);
            recent.Insert(0, shown.Path);
        }
    }

    /// <summary>The Structure tool window.</summary>
    public ToolWindows.StructureView StructureView => Structure;

    /// <summary>The tool windows' host, for the layout and the tests.</summary>
    public Docking.DockHost DockHost => Dock;

    private void OnBuildClicked(object? sender, RoutedEventArgs e) => BuildSolution();

    private void OnRunClicked(object? sender, RoutedEventArgs e) => RunSelected();

    private void OnDebugClicked(object? sender, RoutedEventArgs e) => RunAppAction(AppActions.Debug);

    private void OnStopClicked(object? sender, RoutedEventArgs e) => RunAppAction(AppActions.Stop);

    /// <summary>The run configurations to choose from, as a menu under the toolbar widget.</summary>
    private void OnRunConfigClicked(object? sender, RoutedEventArgs e)
    {
        ContextMenu menu = new();
        foreach (RunConfiguration each in configurations)
        {
            MenuItem item = new() { Header = each.Name };
            RunConfiguration chosen = each;
            item.Click += (_, _) =>
            {
                configuration = chosen;
                RunConfigName.Text = chosen.Name;
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No executable projects", IsEnabled = false });
        }
        menu.Open(RunConfigButton);
    }

    /// <summary>Goes to a file and position (go to definition, a usage), remembering where it came
    /// from for Back.</summary>
    public void NavigateTo(string path, TextPosition position)
    {
        Remember();
        OpenFile(path);
        // Opened files are known by their canonical path: the one given may run through a symlink (macOS's /var).
        if (Editors.Active is { } active && active.Path == PathNames.Canonical(Path.GetFullPath(path)))
        {
            Editors.CodeEditor.GoTo(position);
            Remember();
        }
    }

    /// <summary>Records the caret's place as the newest in the history, dropping anything forward of it.</summary>
    private void Remember()
    {
        if (Editors.Active is not { } active)
        {
            return;
        }
        (string Path, int Offset) here = (active.Path, Editors.CodeEditor.Selections.Primary.Caret);
        if (historyAt >= 0 && history[historyAt] == here)
        {
            return;
        }
        history.RemoveRange(historyAt + 1, history.Count - historyAt - 1);
        history.Add(here);
        historyAt = history.Count - 1;
    }

    private void StepHistory(int step)
    {
        Remember();
        int target = historyAt + step;
        if (target < 0 || target >= history.Count)
        {
            return;
        }
        historyAt = target;
        (string path, int offset) = history[target];
        OpenFile(path);
        if (Editors.Active is { } active && active.Path == path)
        {
            Editors.CodeEditor.GoTo(active.Text.Snapshot.GetPosition(Math.Min(offset, active.Text.Snapshot.Length)));
        }
    }

    private void ShowGoToFile()
    {
        if (folder is null)
        {
            return;
        }
        Navigation.GoToFilePopup popup = new(index, recent.Skip(Editors.Active is null ? 0 : 1).ToList());
        popup.Done += (_, path) =>
        {
            ClosePopup();
            if (path is not null)
            {
                OpenFile(path);
            }
        };
        ShowPopup(popup);
    }

    /// <summary>The actions run from Find Action, the latest first, which it lists before anything is typed.</summary>
    private readonly List<string> recentActions = [];

    /// <summary>Find Action: every action by name, run as its shortcut would run it.</summary>
    public void ShowFindAction()
    {
        Navigation.ActionPopup popup = new(keys.Keymap, recentActions);
        popup.Done += (_, chosen) =>
        {
            ClosePopup();
            if (chosen is null)
            {
                return;
            }
            recentActions.Remove(chosen.Id);
            recentActions.Insert(0, chosen.Id);
            if (recentActions.Count > 10)
            {
                recentActions.RemoveAt(recentActions.Count - 1);
            }
            if (chosen.Id == AppActions.FindAction)
            {
                return;
            }
            if (!Perform(chosen.Id))
            {
                StatusText.Text = $"{chosen.Title} does nothing here";
            }
        };
        ShowPopup(popup);
    }

    private void ShowSymbols(string title, string placeholder, Func<string, CancellationToken, Task<IReadOnlyList<Navigation.SymbolRow>>> search)
    {
        if (folder is null)
        {
            return;
        }
        Navigation.SymbolPopup popup = new(title, placeholder, search, SelectedWord());
        popup.Done += (_, row) =>
        {
            ClosePopup();
            if (row is not null)
            {
                NavigateTo(row.Path, row.Position);
            }
        };
        ShowPopup(popup);
    }

    /// <summary>What a symbol search starts from: the editor's selection, if it is one word, as Rider seeds it.</summary>
    private string? SelectedWord()
    {
        if (Editors.Active is null)
        {
            return null;
        }
        Selection primary = Editors.CodeEditor.Selections.Primary;
        string selected = primary.IsEmpty ? string.Empty : Editors.CodeEditor.Document.Snapshot.GetText(primary.Start, primary.Length);
        return selected.Length is > 0 and < 80 && selected.All(c => char.IsLetterOrDigit(c) || c == '_') ? selected : null;
    }

    private async Task<IReadOnlyList<Navigation.SymbolRow>> SearchSymbols(string query, CancellationToken cancel)
    {
        if (query.Trim().Length == 0)
        {
            return [];
        }
        if (csharp?.Session is not { IsLoaded: true } session)
        {
            throw new InvalidOperationException("The C# language server is still loading the solution");
        }
        IReadOnlyList<Lsp.SymbolInformation> found = await session.SymbolsAsync(query.Trim(), cancel);
        return [.. found.Take(200).Select(SymbolRowOf)];
    }

    /// <summary>Go to Everything: types first, then files, then members, as Rider orders them; the recent files
    /// before anything is typed.</summary>
    private async Task<IReadOnlyList<Navigation.SymbolRow>> SearchEverything(string query, CancellationToken cancel)
    {
        string root = index.Root ?? folder ?? string.Empty;
        if (query.Trim().Length == 0)
        {
            return [.. recent.Skip(Editors.Active is null ? 0 : 1).Select(path => FileRow(Path.GetRelativePath(root, path)))];
        }
        IReadOnlyList<Navigation.SymbolRow> symbols = csharp?.Session is { IsLoaded: true } ? await SearchSymbols(query, cancel) : [];
        IEnumerable<Navigation.SymbolRow> files = index.Search(query).Take(20).Select(FileRow);
        static bool IsType(int kind) => kind is 5 or 10 or 11 or 23;
        return [.. symbols.Where(r => IsType(r.Kind)), .. files, .. symbols.Where(r => !IsType(r.Kind))];

        Navigation.SymbolRow FileRow(string relative) =>
            new(Path.GetFileName(relative), null, Path.GetDirectoryName(relative), Navigation.SymbolRow.FileKind, Path.Combine(root, relative), new TextPosition(0, 0));
    }

    private Navigation.SymbolRow SymbolRowOf(Lsp.SymbolInformation symbol)
    {
        string path = PathNames.Canonical(new Uri(symbol.Location.Uri).LocalPath);
        string where = folder is { } root && path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? Path.GetRelativePath(root, path) : Path.GetFileName(path);
        Lsp.LspPosition start = symbol.Location.Range.Start;
        return new Navigation.SymbolRow(symbol.Name, Container(symbol.ContainerName), $"{where}:{start.Line + 1}", symbol.Kind, path, new TextPosition(start.Line, start.Character));
    }

    /// <summary>Roslyn's container ("in Greeter (project Greetings (net10.0))") as Rider names it: the type, or the
    /// project for a type.</summary>
    private static string? Container(string? container)
    {
        if (string.IsNullOrEmpty(container))
        {
            return null;
        }
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(container, @"^(in )?(?<owner>.*?)\s*\(?project (?<project>[^()]+?)\s*(\([^)]*\))?\)?$");
        if (!match.Success)
        {
            return container;
        }
        string owner = match.Groups["owner"].Value.Trim();
        return owner.Length > 0 ? owner : match.Groups["project"].Value.Trim();
    }

    /// <summary>Rider's File Structure: the active file's types and members, indented, filtered as typed with the
    /// containers of what matches kept.</summary>
    private void ShowFileStructure()
    {
        if (Editors.Active is not { } active || csharp?.For(active)?.Lsp is not { } lsp)
        {
            StatusText.Text = "File Structure needs a C# file the language server has open";
            return;
        }
        IReadOnlyList<Navigation.SymbolRow>? outline = null;
        int caretLine = Editors.CodeEditor.CaretPosition.Line;
        Navigation.SymbolPopup popup = new($"File Structure: {active.Name}", "Filter by name, CamelHumps too", async (query, cancel) =>
        {
            outline ??= Flatten(await lsp.OutlineAsync(cancel), 0, active.Path);
            return FilterOutline(outline, query.Trim());
        })
        {
            // The innermost member the caret is in, as Rider's popup opens on it.
            Initial = rows => rows.Select((row, i) => (row, i)).LastOrDefault(p => p.row.Spans(caretLine), (null!, 0)).Item2,
        };
        popup.Done += (_, row) =>
        {
            ClosePopup();
            if (row is not null)
            {
                NavigateTo(row.Path, row.Position);
            }
        };
        ShowPopup(popup);

        static IReadOnlyList<Navigation.SymbolRow> Flatten(IReadOnlyList<Lsp.DocumentSymbol> symbols, int depth, string path) =>
            ToolWindows.StructureView.Flatten(symbols, depth, path);
    }

    private static IReadOnlyList<Navigation.SymbolRow> FilterOutline(IReadOnlyList<Navigation.SymbolRow> outline, string query)
    {
        if (query.Length == 0)
        {
            return outline;
        }
        List<Navigation.SymbolRow> shown = [];
        List<Navigation.SymbolRow> path = [];
        foreach (Navigation.SymbolRow row in outline)
        {
            while (path.Count > 0 && path[^1].Depth >= row.Depth)
            {
                path.RemoveAt(path.Count - 1);
            }
            bool matches = row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || FuzzyMatch.Score(query, row.Name) is not null;
            if (matches)
            {
                // The containers of a match stay, so it reads in its place.
                foreach (Navigation.SymbolRow container in path.Where(c => !shown.Contains(c)))
                {
                    shown.Add(container);
                }
                shown.Add(row);
            }
            path.Add(row);
        }
        return shown;
    }

    private void ShowGoToLine()
    {
        if (Editors.Active is null)
        {
            return;
        }
        CodeEditor editor = Editors.CodeEditor;
        Navigation.GoToLinePopup popup = new(editor.CaretPosition, editor.Document.Snapshot.LineCount);
        popup.Done += (_, position) =>
        {
            ClosePopup();
            if (position is { } at)
            {
                editor.GoTo(at);
            }
        };
        ShowPopup(popup);
    }

    /// <summary>A popup over the window: centred under the toolbar, as Go to File's is, or beside a point of the
    /// window (a breakpoint's), kept inside it.</summary>
    private void ShowPopup(Control popup, Point? at = null)
    {
        if (at is { } point)
        {
            popup.Measure(Size.Infinity);
            double width = popup.Width is double w && !double.IsNaN(w) ? w : popup.DesiredSize.Width;
            OverlayHost.HorizontalAlignment = HorizontalAlignment.Left;
            OverlayHost.Margin = new Thickness(Math.Clamp(point.X, 8, Math.Max(8, Bounds.Width - width - 8)), Math.Clamp(point.Y, 8, Math.Max(8, Bounds.Height - popup.DesiredSize.Height - 8)), 0, 0);
        }
        else
        {
            OverlayHost.HorizontalAlignment = HorizontalAlignment.Center;
            OverlayHost.Margin = new Thickness(0, 84, 0, 0);
        }
        OverlayHost.Content = popup;
        Overlay.IsVisible = true;
    }

    private void ClosePopup()
    {
        Overlay.IsVisible = false;
        OverlayHost.Content = null;
        if (Editors.Active is not null)
        {
            Editors.CodeEditor.Focus();
        }
    }

    private void OnOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == Overlay)
        {
            DismissPopup();
        }
    }

    /// <summary>Closes the popup as a click outside it or Escape does: a breakpoint's popup or window keeps what
    /// was typed in it, as Rider's do.</summary>
    private void DismissPopup()
    {
        switch (OverlayHost.Content)
        {
            case Debugging.BreakpointPopup breakpoint:
                breakpoint.Close(debug.Breakpoints, more: false);
                break;
            case Debugging.BreakpointsWindow breakpoints:
                breakpoints.Close();
                break;
            default:
                ClosePopup();
                break;
        }
    }

    /// <summary>The unit test runner, its tree and its runs.</summary>
    public Testing.TestController Tests => tests;

    /// <summary>The projects of the solution, or the folder's with none.</summary>
    public IReadOnlyList<ProjectModel> Projects => Solution is { } solution
        ? [.. solution.Projects.Where(p => !Explorer.Tree.Unloaded.Contains(p.Path))]
        : folder is null ? [] : [.. Directory.EnumerateFiles(folder, "*.csproj").Select(ProjectModel.Load)];

    /// <summary>The run configurations of the projects loaded.</summary>
    private IReadOnlyList<RunConfiguration> Configure() =>
        folder is null ? [] : [.. RunConfiguration.From(Solution, folder).Where(c => !Explorer.Tree.Unloaded.Contains(c.Project.Path))];

    /// <summary>Rider's Unload Project: the project shows dimmed with nothing under it, and leaves the run configurations,
    /// the solution's builds and the test runner until it is reloaded; the folder keeps the choice.</summary>
    public void SetUnloaded(string project, bool unloaded)
    {
        if (unloaded ? !Explorer.Tree.Unloaded.Add(project) : !Explorer.Tree.Unloaded.Remove(project))
        {
            return;
        }
        Explorer.Refresh();
        string? chosen = configuration?.Name;
        configurations = Configure();
        configuration = configurations.FirstOrDefault(c => c.Name == chosen) ?? configurations.FirstOrDefault();
        RunConfigName.Text = configuration?.Name ?? "No configurations";
        StatusText.Text = $"{Path.GetFileNameWithoutExtension(project)} {(unloaded ? "unloaded" : "reloaded")}";
        KeepWorkspace();
    }

    /// <summary>A run of tests shown in the Unit Tests window; what stopped it, if anything, in the status bar.</summary>
    private void RunTests(Func<Task<string?>> run)
    {
        if (folder is null)
        {
            return;
        }
        ShowToolWindow("tests");
        _ = Go();

        async Task Go()
        {
            if (await run() is { } problem)
            {
                StatusText.Text = problem;
                Bottom.Tests.Status = problem;
            }
        }
    }

    /// <summary>The run icons of a file's tests, coloured by how each last ran (a class's by all of its tests').</summary>
    private void UpdateTestMarks(OpenDocument document)
    {
        if (!document.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        string text = document.Text.Snapshot.ToString();
        List<TestMark> marks = [];
        foreach ((string type, int line) in Testing.TestsAtCaret.DeclaredTypes(text))
        {
            marks.Add(new TestMark(line, MarkOf(Found(type, null))));
        }
        foreach ((string type, string method, int line) in Testing.TestsAtCaret.Declared(text))
        {
            marks.Add(new TestMark(line, MarkOf(Found(type, method))));
        }
        document.TestMarks = marks;

        IReadOnlyList<Testing.TestNode> Found(string type, string? method) =>
            [.. tests.Tree.Roots.SelectMany(r => r.Methods).Where(m => m.Cases.Any(c => Testing.TestsAtCaret.Matches(c.FullyQualifiedName, type, method)))];

        static TestMarkKind MarkOf(IReadOnlyList<Testing.TestNode> methods) =>
            methods.Any(m => m.State == Testing.TestState.Running) ? TestMarkKind.Running
            : methods.Any(m => m.State == Testing.TestState.Failed) ? TestMarkKind.Failed
            : methods.Count > 0 && methods.All(m => m.State is Testing.TestState.Passed or Testing.TestState.Skipped) && methods.Any(m => m.State == Testing.TestState.Passed) ? TestMarkKind.Passed
            : TestMarkKind.NotRun;
    }

    private void UpdateAllTestMarks()
    {
        foreach (OpenDocument document in Editors.Documents)
        {
            UpdateTestMarks(document);
        }
    }

    /// <summary>Rider's menu on a test's run icon: run or debug that test, or all of its class's.</summary>
    private void ShowTestMenu(CodeEditor editor, OpenDocument document, int line)
    {
        string text = document.Text.Snapshot.ToString();
        (string Type, string? Method)? target = Testing.TestsAtCaret.Declared(text).Where(d => d.Line == line).Select(d => ((string, string?)?)(d.Type, d.Method)).FirstOrDefault()
            ?? Testing.TestsAtCaret.DeclaredTypes(text).Where(d => d.Line == line).Select(d => ((string, string?)?)(d.Type, null)).FirstOrDefault();
        if (target is not { } chosen)
        {
            return;
        }
        string name = chosen.Method ?? chosen.Type[(chosen.Type.LastIndexOfAny(['.', '+']) + 1)..];
        MenuFlyout menu = new();
        MenuItem run = new() { Header = $"Run '{name}'" };
        run.Click += (_, _) => RunTestsAt(chosen.Type, chosen.Method, debug: false);
        MenuItem debugIt = new() { Header = $"Debug '{name}'" };
        debugIt.Click += (_, _) => RunTestsAt(chosen.Type, chosen.Method, debug: true);
        MenuItem untilFailure = new() { Header = $"Run '{name}' Until Failure" };
        untilFailure.Click += (_, _) => RunTestsAt(chosen.Type, chosen.Method, debug: false, untilFailure: true);
        menu.Items.Add(run);
        menu.Items.Add(debugIt);
        menu.Items.Add(untilFailure);
        menu.ShowAt(editor, showAtPointer: true);
    }

    /// <summary>Rider's Run (or Debug) Unit Tests in context: the Unit Tests window's chosen rows when it has the
    /// focus, else the test method or class at the editor's caret, found once the tests have been.</summary>
    private void RunTestsInContext(bool debug, bool untilFailure = false)
    {
        if (folder is null)
        {
            return;
        }
        if (Bottom.Tests.HasFocus && Bottom.Tests.Selected.Count > 0)
        {
            IReadOnlyList<Testing.TestNode> chosen = Bottom.Tests.Selected;
            if (untilFailure)
            {
                RunUntilFailure(chosen);
                return;
            }
            RunTests(() => tests.RunAsync(folder, chosen, debug));
            return;
        }
        if (Editors.Active is not { } active || Testing.TestsAtCaret.At(active.Text.Snapshot.ToString(), Editors.CodeEditor.CaretPosition.Line) is not { } context)
        {
            StatusText.Text = "No test is at the caret";
            return;
        }
        RunTestsAt(context.Type, context.Method, debug, untilFailure);
    }

    /// <summary>Runs tests until one fails, as Rider's Run Until Failure, and shows the failed one's output.</summary>
    private void RunUntilFailure(IReadOnlyList<Testing.TestNode> chosen)
    {
        if (folder is null || chosen.Count == 0)
        {
            return;
        }
        RunTests(async () =>
        {
            string? problem = await tests.RunUntilFailureAsync(folder, chosen);
            if (tests.FirstFailed(chosen) is { } failed)
            {
                Bottom.Tests.Choose(failed);
            }
            return problem;
        });
    }

    /// <summary>Runs (or debugs) a test method, or every test of a type, finding the tests first if need be.</summary>
    private void RunTestsAt(string type, string? method, bool debug, bool untilFailure = false)
    {
        if (folder is null)
        {
            return;
        }
        (string Type, string? Method) context = (type, method);
        RunTests(async () =>
        {
            if (Matching() is not { Count: > 0 })
            {
                // Tests not yet found are found first, as a build would make them.
                if (await tests.DiscoverAsync(folder) is { } problem)
                {
                    return problem;
                }
            }
            if (Matching() is not { Count: > 0 } found)
            {
                return $"No test of {context.Method ?? context.Type} was found; is the project a test project?";
            }
            if (!untilFailure)
            {
                return await tests.RunAsync(folder, found, debug);
            }
            string? stopped = await tests.RunUntilFailureAsync(folder, found);
            if (tests.FirstFailed(found) is { } failed)
            {
                Bottom.Tests.Choose(failed);
            }
            return stopped;
        });

        List<Testing.TestNode> Matching() =>
            [.. tests.Tree.Roots.SelectMany(r => r.Methods).Where(m => m.Cases.Any(c => Testing.TestsAtCaret.Matches(c.FullyQualifiedName, context.Type, context.Method)))];
    }

    /// <summary>A test host VSTest would start, started under the debugger; its process for VSTest to talk to.</summary>
    private async Task<int> LaunchTestHost(Microsoft.VisualStudio.TestPlatform.ObjectModel.TestProcessStartInfo start)
    {
        DebugTarget target = new(start.FileName ?? throw new InvalidOperationException("VSTest gave no test host"),
            start.WorkingDirectory ?? folder ?? Environment.CurrentDirectory,
            CommandLine.Split(start.Arguments),
            start.EnvironmentVariables?.Where(v => v.Value is not null).ToDictionary(v => v.Key, v => v.Value!) ?? []);
        ShowToolWindow("debug");
        await debug.StartAsync(target, "Tests");
        if (debug.Session is not { } session)
        {
            throw new InvalidOperationException("The debugger could not start the test host");
        }
        return await session.ProcessStarted.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>A breakpoint's properties beside its line in the gutter, its condition ready to type.</summary>
    public void ShowBreakpointPopup(Debugging.LineBreakpoint breakpoint)
    {
        CodeEditor editor = Editors.CodeEditor;
        Point at = editor.TranslatePoint(editor.GutterPoint(breakpoint.Line), this) ?? new Point(100, 100);
        Debugging.BreakpointPopup popup = new(breakpoint, debug.Breakpoints, AppActions.Keymap().ShortcutsFor(AppActions.ViewBreakpoints) is { Count: > 0 } keys ? keys[0].Display() : null);
        popup.Closed += (_, more) =>
        {
            ClosePopup();
            if (more)
            {
                ShowBreakpoints(breakpoint);
            }
        };
        ShowPopup(popup, at);
        Dispatcher.UIThread.Post(popup.Properties.FocusCondition, DispatcherPriority.Loaded);
    }

    /// <summary>The Breakpoints window, a breakpoint chosen in it.</summary>
    public void ShowBreakpoints(Debugging.LineBreakpoint? chosen)
    {
        Debugging.BreakpointsWindow window = new(debug.Breakpoints, chosen);
        window.Closed += (_, show) =>
        {
            ClosePopup();
            if (show is not null)
            {
                NavigateTo(show.Path, new TextPosition(show.Line, 0));
            }
        };
        ShowPopup(window);
    }

    private async Task PickFolder()
    {
        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open" });
        if (picked.Count == 1 && picked[0].TryGetLocalPath() is { } path)
        {
            OpenFolder(path);
        }
    }

    private void UpdateStatus()
    {
        OpenDocument? active = Editors.Active;
        string where = folder is null ? "No solution" : Path.GetFileName(folder);
        if (active is not null)
        {
            where = folder is not null && active.Path.StartsWith(folder, StringComparison.Ordinal)
                ? Path.GetRelativePath(folder, active.Path)
                : active.Path;
        }
        // With a file open the status bar's path names it, so the message line is left for messages.
        StatusText.Text = active is null ? where : string.Empty;
        Title = active is null ? where : $"{(folder is null ? "" : Path.GetFileName(folder) + " – ")}{active.Name}";
        LineEndingText.Text = active is null ? "" : active.Text.LineEnding == "\r\n" ? "CRLF" : "LF";
        EncodingText.Text = active?.EncodingName ?? "";
        IndentText.Text = active is null ? "" : $"{active.Language.IndentSize} spaces";
        LanguageText.Text = LanguageLabel();
        UpdateCaret();
    }

    /// <summary>The file's language, with the language server's state beside it while it is not ready.</summary>
    private string LanguageLabel()
    {
        string language = Editors.Active?.Language.Name ?? string.Empty;
        if (csharp is { IsReady: false, State: { Length: > 0 } state })
        {
            return language.Length > 0 ? $"{language} · {state}" : state;
        }
        return language;
    }

    private void UpdateCaret()
    {
        if (Editors.Active is null)
        {
            CaretText.Text = "";
            return;
        }
        CodeEditor editor = Editors.CodeEditor;
        TextPosition caret = editor.CaretPosition;
        int carets = editor.Selections.Count;
        int selected = editor.Selections.Items.Sum(s => s.Length);
        string position = $"{caret.Line + 1}:{caret.Column + 1}";
        CaretText.Text = carets > 1 ? $"{carets} carets" : selected > 0 ? $"{position} ({selected} chars)" : position;
    }

    private void OnToolbarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsExtendedIntoWindowDecorations || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        BeginMoveDrag(e);
    }

    private void OnOpenClicked(object? sender, RoutedEventArgs e) => _ = PickFolder();

    /// <summary>The project badge's two letters, as Rider draws them: the first two words' initials,
    /// or a lone word's first two letters.</summary>
    private static string Initials(string name)
    {
        string[] words = name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        string letters = words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)],
            _ => $"{words[0][0]}{words[1][0]}",
        };
        return letters.ToUpperInvariant();
    }
}
