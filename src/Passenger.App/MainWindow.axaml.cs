using System.Globalization;
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
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App;

public partial class MainWindow : Window
{
    /// <summary>Room at the toolbar's left for macOS's window buttons once the toolbar is the title bar.</summary>
    private const double MacWindowButtons = 72;

    /// <summary>How many recently opened files Go to File offers before anything is typed.</summary>
    private const int RecentLimit = 25;

    /// <summary>The window's least width and height (its MinWidth and MinHeight), which a screenshot's <c>--size</c> is
    /// held to: Avalonia would widen a smaller window to them without a word.</summary>
    public const double SmallestWidth = 800;

    public const double SmallestHeight = 500;

    private readonly KeymapDispatcher keys = new(AppActions.Keymap());
    private readonly FileIndex index = new();
    private CSharpService? csharp;
    private readonly CodeAssist assist;
    private readonly Navigation.NavigationHistory history = new();
    private readonly ProcessRunner builder;
    private readonly ProcessRunner runner;
    private readonly DebugController debug;
    private readonly Testing.TestController tests;
    private readonly Testing.TestGutter testGutter;
    private Action<BuildOutcome>? afterBuild;
    private TaskCompletionSource? building;
    private readonly List<Problem> problems = [];

    /// <summary>The build's last lines, which its outcome carries.</summary>
    private readonly Queue<string> builtLines = new();
    private const int BuiltLinesKept = 20;
    private IReadOnlyList<RunConfiguration> configurations = [];
    private RunConfiguration? configuration;
    private readonly Passenger.App.Explorer.ExplorerView Explorer = new();
    private readonly ToolWindows.ToolViews Bottom = new();
    private readonly ToolWindows.StructureView Structure = new();
    private readonly List<string> recent = [];
    private string? folder;

    public MainWindow()
    {
        UiThread.Own();
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
        AddActions();
        AddWelcome();
        AddChords();
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
        AddAbout();
        AddAgents();
        AddShutdown();
        AddEditorWindows();
        Explorer.FileOpened += (_, path) => OpenFile(path);
        Editors.ActiveChanged += (_, _) => FollowInExplorer();
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
        Editors.Opened += (_, document) => document.Text.Changed += (_, change) => history.Follow(document.Path, change);
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
        // IntelliJ's words for its caret limit.
        Editors.ForEachEditor(editor => editor.CaretLimitReached += (_, _) =>
            StatusText.Text = string.Create(CultureInfo.InvariantCulture, $"Cannot create more than {SelectionSet.MaxCount:N0} carets"));
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
            builtLines.Enqueue(line);
            if (builtLines.Count > BuiltLinesKept)
            {
                builtLines.Dequeue();
            }
        };
        builder.Exited += (_, code) => OnBuilt(code);
        runner = new ProcessRunner(Bottom.Run, takesInput: true);
        runner.Exited += (_, code) =>
        {
            Bottom.Run.SetRunning(false);
            StopButton.IsVisible = false;
            StatusText.Text = RunEnded(ran, code);
        };
        Bottom.Build.RerunClicked += (_, _) => Build(buildKind, lastProjects);
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
        tests = new Testing.TestController(Bottom.TestTree, BuildAsync, () => Projects, line => Bottom.Build.Append($"{line}\n"));
        tests.Message += (_, message) =>
        {
            StatusText.Text = message;
            Bottom.Tests.Status = message;
        };
        tests.LaunchUnderDebugger = LaunchTestHost;
        testGutter = new Testing.TestGutter(Bottom.TestTree);
        Editors.Opened += (_, document) => testGutter.Watch(document);
        Editors.Closed += (_, document) => testGutter.Forget(document);
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
                if (await session.ProcessStarted.WaitAsync(TimeSpan.FromSeconds(30)) == processId)
                {
                    return true;
                }
            }
            catch (TimeoutException)
            {
            }
            // Given up on: left running, the session would say the tests run until Stop, with nothing under it.
            await debug.StopAsync();
            return false;
        };
        Bottom.Tests.ActionRequested += (_, action) => Request(action);
        Bottom.Tests.RunRequested += (_, debugging) =>
        {
            IReadOnlyList<Testing.TestNode> chosen = Bottom.Tests.Selected;
            RunTests(() => tests.RunAsync(folder!, chosen, debugging));
        };
        Bottom.Tests.UntilFailureRequested += (_, _) => RunUntilFailure(Bottom.Tests.Selected);
        Bottom.Problems.FixRequested += (_, problem) =>
        {
            NavigateTo(problem.Path!, problem.Position);
            Request(AppActions.QuickFix);
        };
        Structure.UsagesRequested += (_, row) =>
        {
            NavigateTo(row.Path, row.Position);
            Request(AppActions.FindUsages);
        };
        Bottom.Tests.GoToRequested += (_, node) =>
        {
            if (node.Path is { } path && File.Exists(path))
            {
                NavigateTo(PathNames.Canonical(path), new TextPosition(Math.Max(0, node.Line - 1), 0));
            }
        };
        debug.ActionRequested += (_, action) => Request(action);
        debug.Message += (_, message) => StatusText.Text = message;
        debug.Navigate += (_, target) => ShowStop(target.Path, target.Position);
        debug.StateChanged += (_, _) => OnDebugStateChanged();
        assist.DebuggerValue = debug.ValueAt;
        assist.AddActions(actions!);
        debug.AddActions(actions!);
        AddFind();
        AddProblems();
        AddContextMenus();
        AddMainMenu();
        UpdateStatus();
    }

    /// <summary>The C# service for the open folder, or null when it has no projects.</summary>
    public CSharpService? CSharpService => csharp;

    public EditorArea EditorArea => Editors;

    public Explorer.ExplorerView ExplorerView => Explorer;

    /// <summary>Every file of the open folder, as Go to File searches them.</summary>
    public FileIndex FileIndex => index;

    /// <summary>Opens what the command line named: a folder or solution, files in it, a caret position. Named nothing,
    /// the window that keeps state reopens what was opened last, or shows the Welcome screen.</summary>
    public void Start(LaunchOptions options)
    {
        if ((options.Solution ?? options.Folder) is { } path)
        {
            OpenFolder(path);
        }
        else if (options.Files.Count == 0)
        {
            ReopenLast();
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
        if (folder is null && Editors.Documents.Count == 0)
        {
            welcome.FocusSearch();
        }
    }

    /// <summary>Opens a folder, or a solution (.sln, .slnx) and its folder, as the workspace. The workspace already open is
    /// only read again: its language server and open files are kept.</summary>
    public void OpenFolder(string path)
    {
        string named = PathNames.Canonical(path);
        bool solutionNamed = File.Exists(named) && (named.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || named.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));
        string opening = solutionNamed ? Path.GetDirectoryName(named)! : named;
        WorkspaceModel? withSolution = WorkspaceModel.ReadSolution(opening, solutionNamed ? named : opening == folder ? namedSolution : null);
        if (opening == folder && withSolution?.Solution?.Path == Solution?.Path)
        {
            Read(withSolution);
            return;
        }
        if (folder is not null && folder != opening)
        {
            // Another folder's files are its own: this one's are kept with it and close, saved, as Rider's do.
            KeepWorkspace();
            if (!Editors.CloseAll())
            {
                return;
            }
            recent.Clear();
            // Breakpoints, bookmarks and the places Back goes to are a folder's own, as Rider keeps them with the project.
            folderState = null;
            debug.Breakpoints.Clear();
            bookmarks.Clear();
            history.Clear();
        }
        Autosave();
        folder = opening;
        // Where the log's timing of the language server's start (MainWindow.Workspace's CSharpService) begins.
        IdeErrors.Write($"Opened {opening}{(withSolution?.Solution is { } solution ? $" with {Path.GetFileName(solution.Path)}" : string.Empty)}");
        agentSocket?.Describe(folder);
        namedSolution = solutionNamed ? named : null;
        // A folder without a solution has no projects until the walk for them lands: none to load, run or build meanwhile.
        reads++;
        workspace = withSolution ?? new WorkspaceModel(opening, null, []);
        Explorer.Load(folder, Solution);
        _ = vcs.OpenAsync(folder);
        Explorer.Tree.Unloaded.Clear();
        configurations = Configure();
        configuration = configurations.FirstOrDefault();
        RunConfigName.Text = configuration?.Name ?? "No configurations";
        index.Build(folder);
        Watch(folder);
        StartCSharp();
        if (withSolution is null)
        {
            WalkForProjects();
        }
        Editors.ShowFolderHint(folder);
        RestoreWorkspace();
        ShowWorkspaceName();
        UpdateStatus();
        Remember();
        _ = KeepSnapshotsAsync();
    }

    private string? followed;

    /// <summary>Chooses the file shown in the Explorer as another is shown, a tab switched to as well as a file opened, as
    /// Rider's Always Select Opened File; a row chosen there meanwhile stays chosen until then.</summary>
    private void FollowInExplorer()
    {
        string? shown = Editors.Active?.Path;
        if (shown is not null && shown != followed)
        {
            Explorer.Reveal(shown);
        }
        followed = shown;
    }

    /// <summary>Opens a file in the editor; null once it is open, else why not, which the status bar says too.</summary>
    public string? OpenFile(string path)
    {
        string full = PathNames.Canonical(Path.IsPathRooted(path) || folder is null ? path : Path.Combine(folder, path));
        if (Editors.Open(full) is { } problem)
        {
            StatusText.Text = problem;
            return problem;
        }
        recent.Remove(full);
        recent.Insert(0, full);
        if (recent.Count > RecentLimit)
        {
            recent.RemoveAt(recent.Count - 1);
        }
        Explorer.Reveal(full);
        return null;
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

    /// <summary>A key's action that throws is reported as an IDE error, never the IDE's end. The window it was pressed in
    /// (this one or an editor window) is the one the person works in.</summary>
    private void OnKeyDownFirst(object? sender, KeyEventArgs e)
    {
        try
        {
            Editors.Front(sender as TopLevel);
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
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && IsPopupOpen)
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
        if (actions![action] is { InEditor: false })
        {
            if (!shifted)
            {
                Request(action);
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

    public bool IsPopupOpen => Popups.Layer.IsVisible;

    /// <summary>What the open popup is (Go to File, a breakpoint's properties, the Breakpoints window), or null.</summary>
    public Control? Popup => IsPopupOpen ? Popups.Host.Content as Control : null;

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

    /// <summary>The build, rebuild or clean the Build window shows, while it runs or since it last did.</summary>
    public ProcessRunner Builder => builder;

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

    /// <summary>Builds as Build does, and says how it ended once it has: built, failed, stopped, refused or unable to start.
    /// Canceled (the Unit Tests window's Stop), the build is stopped as the Build window's Stop stops it.</summary>
    public Task<BuildOutcome> BuildAsync(CancellationToken cancel = default)
    {
        TaskCompletionSource<BuildOutcome> built = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration stopping = default;
        BuildSolution(outcome =>
        {
            stopping.Dispose();
            built.TrySetResult(outcome);
        });
        if (!built.Task.IsCompleted)
        {
            // Canceled on the UI thread, by Stop, so the build it stops is the one begun here, still running.
            stopping = cancel.Register(builder.Stop);
        }
        return built.Task;
    }

    /// <summary>Builds, then launches the chosen configuration under the debugger, as Rider's Debug does,
    /// with these arguments in place of its own if given; the Debug window opens on its Console until the
    /// program stops somewhere. Null once its session is open, or what stopped it (a failed build, no configuration,
    /// the adapter's refusal).</summary>
    public async Task<string?> DebugAsync(string? arguments = null)
    {
        if (configuration is not { } chosen)
        {
            return "Nothing to debug: no executable project";
        }
        if (await BuildAsync() is { Succeeded: false } unbuilt)
        {
            return unbuilt.Message;
        }
        BackgroundWork.Begin();
        try
        {
            DebugTarget target = await DebugTarget.ResolveAsync(chosen, arguments);
            ShowToolWindow("debug");
            Bottom.Debug.ShowTab(console: true);
            return await debug.StartAsync(target, chosen.Name);
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

    /// <summary>The status bar's text, which says what the last action came to, for the person. What an operation came to
    /// is its own answer (a build's outcome, a commit's), never this, which anything may have replaced since.</summary>
    public string? Status => StatusText.Text;

    /// <summary>Builds the solution (or, with none, the folder's projects) with the .NET command line, its output
    /// in the Build window and what it finds in Problems; <paramref name="then"/> hears how it ended.</summary>
    public void BuildSolution(Action<BuildOutcome>? then = null) => Build(BuildKind.Build, null, then);

    /// <summary>Builds, rebuilds or cleans the solution, or the projects given one after another (stopping at the first
    /// that fails), as Rider's Build menu and the Solution view's do: the output in the Build window, the problems of all
    /// of them in Problems. <paramref name="then"/> always hears how it ended, a build refused or never started included.</summary>
    public void Build(BuildKind kind, IReadOnlyList<string>? projects = null, Action<BuildOutcome>? then = null)
    {
        if (folder is null || IsBuilding)
        {
            BuildOutcome refused = new(BuildStatus.Refused, folder is null ? "Nothing to build: no folder is open" : "Another build is running");
            StatusText.Text = refused.Message;
            then?.Invoke(refused);
            return;
        }
        buildKind = kind;
        lastProjects = projects;
        IReadOnlyList<string> whole = workspace!.BuildTargets(Projects);
        buildQueue.Clear();
        foreach (string target in projects is { Count: > 0 } ? projects : whole)
        {
            buildQueue.Enqueue(target);
        }
        if (buildQueue.Count == 0)
        {
            BuildOutcome nothing = new(BuildStatus.Refused, "Nothing to build: every project is unloaded");
            StatusText.Text = nothing.Message;
            then?.Invoke(nothing);
            return;
        }
        afterBuild = then;
        Editors.SaveAll();
        problems.Clear();
        builtLines.Clear();
        Bottom.Build.Clear();
        Bottom.Build.SetRunning(true);
        ShowToolWindow("build");
        building = new TaskCompletionSource();
        BackgroundWork.Track(building.Task);
        BuildNext();
    }

    /// <summary>Whether a build, rebuild or clean is under way, from its start until its callers have heard how it ended.</summary>
    public bool IsBuilding => building is not null;

    private readonly Queue<string> buildQueue = new();
    private BuildKind buildKind;
    private IReadOnlyList<string>? lastProjects;

    private void BuildNext()
    {
        string target = buildQueue.Dequeue();
        string verb = buildKind switch { BuildKind.Rebuild => "Rebuilding", BuildKind.Clean => "Cleaning", _ => "Building" };
        StatusText.Text = $"{verb} {Path.GetFileName(target)}…";
        if (builder.StartDotnet(BuildOutput.Arguments(target, buildKind), folder!) is { } problem)
        {
            FinishBuild(new BuildOutcome(BuildStatus.NotStarted, problem));
        }
    }

    /// <summary>The projects Build Selected Projects means: those chosen in the Explorer, else the active file's.</summary>
    public IReadOnlyList<string> SelectedProjects()
    {
        List<string> chosen = [.. Explorer.SelectedNodes.Where(n => n.Kind == Passenger.App.Explorer.NodeKind.Project && n.Path is not null).Select(n => n.Path!)];
        if (chosen.Count == 0 && Editors.Active is { } file && ProjectOf(file.Path) is { } owner)
        {
            chosen.Add(owner.Path);
        }
        return chosen;
    }

    /// <summary>Runs the chosen configuration with <c>dotnet run</c>, its output in the Run window, with these
    /// arguments in place of its own if given. Null once it runs (<see cref="ProcessRunner.Ended"/> is its end), else why
    /// it could not, which the status bar says too.</summary>
    public string? RunSelected(string? arguments = null)
    {
        if (configuration is not { } chosen || runner.IsRunning)
        {
            StatusText.Text = configuration is null ? "Nothing to run: no executable project" : "Already running";
            return StatusText.Text;
        }
        Editors.SaveAll();
        Bottom.Run.Clear();
        Bottom.Run.SetRunning(true);
        StopButton.IsVisible = true;
        ShowToolWindow("run");
        ran = chosen.Name;
        if (settings.HotReload)
        {
            // dotnet watch applies each save to the running program, as Rider's Hot Reload does, and restarts it
            // for an edit that cannot be applied rather than asking.
            StatusText.Text = $"Running {chosen.Name} with Hot Reload: a save applies its changes";
            return Started(runner.StartDotnet(["watch", "--non-interactive", .. chosen.RunArguments(arguments)], chosen.Project.Folder,
                new Dictionary<string, string> { ["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1", ["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "true" }));
        }
        StatusText.Text = $"Running {chosen.Name}";
        return Started(runner.StartDotnet(chosen.RunArguments(arguments), chosen.Project.Folder));
    }

    /// <summary>The run configuration the Run window last ran.</summary>
    private string ran = string.Empty;

    /// <summary>How a run that ended is named, in the status bar and to an agent.</summary>
    public static string RunEnded(string name, int code) => $"{name} exited with code {code}";

    /// <summary>A run that could not start leaves the Run window idle, Stop hidden, and says why.</summary>
    private string? Started(string? problem)
    {
        if (problem is not null)
        {
            Bottom.Run.SetRunning(false);
            StopButton.IsVisible = debug.IsDebugging;
            StatusText.Text = problem;
        }
        return problem;
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
        if (code == 0 && buildQueue.Count > 0 && !builder.WasStopped)
        {
            Bottom.Build.Append("\n");
            BuildNext();
            return;
        }
        int errors = problems.Count(p => p.Severity == ProblemSeverity.Error);
        int warnings = problems.Count - errors;
        string noun = buildKind switch { BuildKind.Rebuild => "Rebuild", BuildKind.Clean => "Clean", _ => "Build" };
        FinishBuild(builder.WasStopped ? new BuildOutcome(BuildStatus.Stopped, $"{noun} stopped")
            : code == 0 ? new BuildOutcome(BuildStatus.Succeeded, $"{noun} succeeded{(warnings > 0 ? $" with {warnings} warning{(warnings == 1 ? "" : "s")}" : "")}")
            : new BuildOutcome(BuildStatus.Failed, $"{noun} failed: {errors} error{(errors == 1 ? "" : "s")}, {warnings} warning{(warnings == 1 ? "" : "s")}; Problems lists them"));
    }

    /// <summary>The one way a build that began ends: its window, Problems and the status bar say how, its caller hears it
    /// with what it found, and the background work it held is let go.</summary>
    private void FinishBuild(BuildOutcome ended)
    {
        BuildOutcome outcome = ended with { Problems = [.. problems], Output = [.. builtLines] };
        buildQueue.Clear();
        Action<BuildOutcome>? then = afterBuild;
        afterBuild = null;
        TaskCompletionSource? held = building;
        building = null;
        Bottom.Build.SetRunning(false);
        Bottom.Problems.Show(problems);
        if (Dock["problems"] is { } problemsWindow)
        {
            problemsWindow.Title = problems.Count == 0 ? "Problems" : $"Problems {problems.Count}";
        }
        StatusText.Text = outcome.Message;
        if (outcome.Status == BuildStatus.Failed && problems.Any(p => p.Severity == ProblemSeverity.Error))
        {
            ShowToolWindow("problems");
        }
        try
        {
            then?.Invoke(outcome);
        }
        finally
        {
            held?.TrySetResult();
        }
    }

    /// <summary>Opens a tool window in its place ("debug", "build", "tests"...), as its stripe button does.</summary>
    public void ShowToolWindow(string id) => Dock.Show(id);

    /// <summary>Shift+Escape: the tool window open last closes, and the editor has the keys again.</summary>
    private void HideToolWindow()
    {
        Dock.HideActive();
        FocusEditor();
    }

    /// <summary>Rider's Focus Editor: the file shown has the keys again, the tool windows left as they are.</summary>
    private void FocusEditor()
    {
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
            Activate = () =>
            {
                Dock.Show("explorer");
                Explorer.FocusRows();
            },
        });
        Dock.Add(new Docking.ToolWindow("structure", "Structure", "Icon.Structure", Structure, Docking.ToolAnchor.LeftBottom)
        {
            Action = AppActions.ShowStructure,
            Activate = () =>
            {
                ShowToolWindow("structure");
                _ = Structure.Refresh();
            },
        });
        Dock.Add(new Docking.ToolWindow("run", "Run", "Icon.RunOutline", Bottom.Run, Docking.ToolAnchor.BottomLeft) { Action = AppActions.ShowRun, ActionTitle = "Run Tool Window" });
        Dock.Add(new Docking.ToolWindow("debug", "Debug", "Icon.Debug", Bottom.Debug, Docking.ToolAnchor.BottomLeft) { Action = AppActions.ShowDebug, ActionTitle = "Debug Tool Window" });
        Dock.Add(new Docking.ToolWindow("tests", "Unit Tests", "Icon.Tests", Bottom.Tests, Docking.ToolAnchor.BottomLeft)
        {
            Action = AppActions.ShowTests,
            Menus = [new MenuPlace("Tests", 2, 0)],
            Chosen = () => Bottom.Tests.Selected,
        });
        Dock.Add(new Docking.ToolWindow("build", "Build", "Icon.Build", Bottom.Build, Docking.ToolAnchor.BottomLeft) { Action = AppActions.ShowBuild, ActionTitle = "Build Tool Window" });
        Dock.Add(new Docking.ToolWindow("problems", "Problems", "Icon.Problems", Bottom.Problems, Docking.ToolAnchor.BottomLeft) { Action = AppActions.ShowProblems, ActionTitle = "Problems" });
        Dock.Show("explorer");
    }

    /// <summary>The window a person uses keeps its layout: it opens on the one kept, and keeps each change; and each
    /// folder's editors, recent files, layout, breakpoints and bookmarks, which come back when the folder opens again.</summary>
    public void KeepLayout()
    {
        State.StateStore store = Store();
        if (store.Layout.IsKept)
        {
            Dock.Apply(store.Layout.Value);
        }
        Dock.LayoutChanged += (_, _) => KeepDock();
        debug.Breakpoints.Changed += (_, _) => folderState?.Breakpoints.Set(debug.Breakpoints.Capture());
        bookmarks.Changed += (_, _) => folderState?.Bookmarks.Set(bookmarks.Capture());
        keepsWorkspace = true;
        RestoreWorkspace();
        ShowRecent();
    }

    /// <summary>The folder this window keeps its state and its folders' snapshots in: the store's own unless one is named,
    /// as a test names a fresh one without changing the process's environment, which the programs it starts inherit.</summary>
    public string StateFolder { get; init; } = State.StateStore.DefaultRoot;

    /// <summary>The store of the window a person uses, made the first time it keeps something; null in a headless one.</summary>
    private State.StateStore? state;

    /// <summary>The open folder's documents in the store, while this window keeps them.</summary>
    private State.ProjectState? folderState;

    private bool keepsWorkspace;

    private State.StateStore Store()
    {
        if (state is null)
        {
            state = new State.StateStore(StateFolder);
            // The last changes are written as the window closes, since the process will not wait for their timers.
            Closing += (_, _) =>
            {
                if (keepsWorkspace)
                {
                    KeepDock();
                    KeepWorkspace();
                }
                state.Flush();
            };
        }
        return state;
    }

    /// <summary>The layout as it stands, kept with the folder open and as the one a folder without its own opens on.</summary>
    private void KeepDock()
    {
        Docking.DockLayout now = Dock.Capture();
        state!.Layout.Set(now);
        folderState?.Layout.Set(now);
    }

    private void KeepWorkspace() => folderState?.Workspace.Set(new WorkspaceState
    {
        Editors = Editors.Capture(),
        EditorWindows = Editors.CaptureWindows(),
        RecentFiles = [.. recent],
        UnloadedProjects = [.. Explorer.Tree.Unloaded],
    });

    private void RestoreWorkspace()
    {
        if (!keepsWorkspace || folder is null)
        {
            return;
        }
        State.ProjectState opening = state!.Project(folder);
        if (opening.Layout.IsKept)
        {
            Dock.Apply(opening.Layout.Value);
        }
        // Before the folder's documents are this window's, so what restoring them changes is not written back.
        debug.Breakpoints.Restore(opening.Breakpoints.Value);
        bookmarks.Restore(opening.Bookmarks.Value);
        folderState = opening;
        WorkspaceState kept = opening.Workspace.Value;
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
        Editors.Restore(kept.Editors, kept.EditorWindows);
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

    private void OnDebugClicked(object? sender, RoutedEventArgs e) => Request(AppActions.Debug);

    private void OnStopClicked(object? sender, RoutedEventArgs e) => Request(AppActions.Stop);

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
        Navigation.HistoryPlace? from = Here();
        OpenFile(path);
        // Opened files are known by their canonical path: the one given may run through a symlink (macOS's /var).
        if (Editors.Active is { } active && active.Path == PathNames.Canonical(path))
        {
            Editors.CodeEditor.GoTo(position);
            // Only a move to another line or file is a place to come back to, as in IntelliJ.
            if (from is not null && Here() is { } to && (to.Path != from.Path || to.Line != from.Line))
            {
                history.Left(from);
            }
        }
    }

    /// <summary>The caret's place in the file shown, as the navigation history keeps it.</summary>
    private Navigation.HistoryPlace? Here()
    {
        if (Editors.Active is not { } active)
        {
            return null;
        }
        int caret = Editors.CodeEditor.Selections.Primary.Caret;
        return new Navigation.HistoryPlace(active.Path, caret, active.Text.Snapshot.GetLineFromOffset(caret));
    }

    /// <summary>Back (a step of -1) or Forward (1): the caret goes to the place the history gives.</summary>
    private void StepHistory(int step)
    {
        Navigation.HistoryPlace? here = Here();
        Navigation.HistoryPlace? target = step < 0 ? history.Back(here, Reachable) : history.Forward(here, Reachable);
        if (target is null)
        {
            return;
        }
        OpenFile(target.Path);
        if (Editors.Active is { } active && active.Path == target.Path)
        {
            Editors.CodeEditor.GoTo(active.Text.Snapshot.GetPosition(Math.Min(target.Offset, active.Text.Snapshot.Length)));
        }

        bool Reachable(string path) => Editors.Documents.Any(d => d.Path == path) || File.Exists(path);
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

    /// <summary>Find Action: every action by name, those that cannot run where it was opened greyed; the one chosen runs
    /// there as its shortcut would, or says why it cannot.</summary>
    public void ShowFindAction()
    {
        ActionContext context = actions!.Context();
        Navigation.ActionPopup popup = new(actions, context, recentActions);
        popup.Done += (_, chosen) =>
        {
            ClosePopup();
            if (chosen is null || chosen.Id == AppActions.FindAction || !Request(chosen.Id, context))
            {
                return;
            }
            recentActions.Remove(chosen.Id);
            recentActions.Insert(0, chosen.Id);
            if (recentActions.Count > 10)
            {
                recentActions.RemoveAt(recentActions.Count - 1);
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
            throw new Lsp.LanguageServerFailed(csharp is null ? "no C# language server runs for this folder" : $"the C# language server is not ready: {csharp.State}");
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
        IReadOnlyList<Navigation.SymbolRow> symbols = [];
        try
        {
            symbols = csharp?.Session is { IsLoaded: true } ? await SearchSymbols(query, cancel) : [];
        }
        catch (Lsp.LanguageServerFailed)
        {
            // The files still match without the server.
        }
        IEnumerable<Navigation.SymbolRow> files = index.Search(query).Take(20).Select(FileRow);
        static bool IsType(int kind) => kind is 5 or 10 or 11 or 23;
        return [.. symbols.Where(r => IsType(r.Kind)), .. files, .. symbols.Where(r => !IsType(r.Kind))];

        Navigation.SymbolRow FileRow(string relative) =>
            new(Path.GetFileName(relative), null, Path.GetDirectoryName(relative), Navigation.SymbolRow.FileKind, Path.Combine(root, relative), new TextPosition(0, 0));
    }

    private Navigation.SymbolRow SymbolRowOf(Lsp.SymbolInformation symbol)
    {
        string path = Lsp.DocumentUris.ToPath(symbol.Location.Uri);
        string where = folder is { } root && PathNames.IsUnder(path, root) ? Path.GetRelativePath(root, path) : Path.GetFileName(path);
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
        if (Editors.Active is not { } active || csharp?.For(active)?.Lsp is null)
        {
            StatusText.Text = "File Structure needs a C# file the language server has open";
            return;
        }
        IReadOnlyList<Navigation.SymbolRow>? outline = null;
        int caretLine = Editors.CodeEditor.CaretPosition.Line;
        Navigation.SymbolPopup popup = new($"File Structure: {active.Name}", "Filter by name, CamelHumps too", async (query, cancel) =>
        {
            // The server's document now: one started again while the popup is open has a new one.
            Lsp.LspDocument lsp = csharp?.For(active)?.Lsp ?? throw new Lsp.LanguageServerFailed("the C# language server is not running");
            outline ??= ToolWindows.StructureView.Flatten(await lsp.OutlineAsync(cancel), 0, active.Path, Lsp.ServerLines.Of(lsp.Text.Snapshot));
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

    /// <summary>A popup over the window the person works in (this one or an editor window): centred under the toolbar, as
    /// Go to File's is, or beside a point of that window (a breakpoint's, from <see cref="PopupPoint"/>), kept inside it.</summary>
    private void ShowPopup(Control popup, Point? at = null)
    {
        (Panel layer, ContentControl host) = PopupsInFront();
        if (Popups.Layer != layer)
        {
            Popups.Layer.IsVisible = false;
            Popups.Host.Content = null;
        }
        popups = (layer, host);
        if (at is { } point)
        {
            popup.Measure(Size.Infinity);
            double width = popup.Width is double w && !double.IsNaN(w) ? w : popup.DesiredSize.Width;
            host.HorizontalAlignment = HorizontalAlignment.Left;
            host.Margin = new Thickness(Math.Clamp(point.X, 8, Math.Max(8, layer.Bounds.Width - width - 8)), Math.Clamp(point.Y, 8, Math.Max(8, layer.Bounds.Height - popup.DesiredSize.Height - 8)), 0, 0);
        }
        else
        {
            host.HorizontalAlignment = HorizontalAlignment.Center;
            host.Margin = new Thickness(0, layer == Overlay ? 84 : EditorWindowPopupTop, 0, 0);
        }
        host.Content = popup;
        layer.IsVisible = true;
    }

    private void ClosePopup()
    {
        Popups.Layer.IsVisible = false;
        Popups.Host.Content = null;
        if (Editors.Active is not null)
        {
            Editors.CodeEditor.Focus();
        }
    }

    private void OnOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == sender)
        {
            DismissPopup();
        }
    }

    /// <summary>Closes the popup as a click outside it or Escape does: a breakpoint's popup or window keeps what
    /// was typed in it, as Rider's do.</summary>
    private void DismissPopup()
    {
        switch (Popups.Host.Content)
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

    /// <summary>The run icons beside the tests of each open file.</summary>
    public Testing.TestGutter TestGutter => testGutter;

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

    /// <summary>Rider's menu on a test's run icon: run or debug that test, or all of its class's.</summary>
    private void ShowTestMenu(CodeEditor editor, OpenDocument document, int line)
    {
        if (testGutter.At(document, line) is not { } chosen)
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
    /// keys, else the test method or class at the editor's caret, found once the tests have been.</summary>
    private void RunTestsInContext(ActionContext context, bool debug, bool untilFailure = false)
    {
        if (ChosenTests(context) is { } chosen)
        {
            if (untilFailure)
            {
                RunUntilFailure(chosen);
                return;
            }
            RunTests(() => tests.RunAsync(folder!, chosen, debug));
            return;
        }
        if (TestAtCaret(context) is { } test)
        {
            RunTestsAt(test.Type, test.Method, debug, untilFailure);
        }
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
        string? problem = await debug.StartAsync(target, "Tests");
        if (debug.Session is not { } session)
        {
            throw new InvalidOperationException(problem ?? "The debugger could not start the test host");
        }
        return await session.ProcessStarted.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>A breakpoint's properties beside its line in the gutter, its condition ready to type.</summary>
    public void ShowBreakpointPopup(Debugging.LineBreakpoint breakpoint)
    {
        CodeEditor editor = Editors.CodeEditor;
        Point at = PopupPoint(editor, editor.GutterPoint(breakpoint.Line)) ?? new Point(100, 100);
        Debugging.BreakpointPopup popup = new(breakpoint, debug.Breakpoints, actions!.Shortcut(AppActions.ViewBreakpoints));
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
            where = folder is not null && PathNames.IsUnder(active.Path, folder)
                ? Path.GetRelativePath(folder, active.Path)
                : active.Path;
        }
        // With a file open the status bar's path names it, so the message line is left for messages.
        StatusText.Text = active is null ? where : string.Empty;
        // Nothing open, the window is Rider's Welcome window; else it names the file its own editors show, an editor
        // window in front or not.
        OpenDocument? shown = Editors.ShownInMain;
        string idle = folder is null ? "Welcome to Passenger" : Path.GetFileName(folder);
        Title = shown is null ? idle : $"{(folder is null ? "" : Path.GetFileName(folder) + " – ")}{shown.Name}";
        LineEndingText.Text = active is null ? "" : active.Text.LineEnding == "\r\n" ? "CRLF" : "LF";
        EncodingText.Text = active?.EncodingName ?? "";
        IndentText.Text = active is null ? "" : $"{active.Language.IndentSize} spaces";
        LanguageText.Text = LanguageLabel();
        UpdateCaret();
    }

    /// <summary>The file's language, with the language server's state beside it while it is not ready, or the work it
    /// reports under way (its projects loading, and how far) whenever there is some.</summary>
    private string LanguageLabel()
    {
        string language = Editors.Active?.Language.Name ?? string.Empty;
        string? shown = csharp?.Progress ?? (csharp is { IsReady: false, State: { Length: > 0 } state } ? state : null);
        if (shown is not null)
        {
            return language.Length > 0 ? $"{language} · {shown}" : shown;
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
    internal static string Initials(string name)
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
