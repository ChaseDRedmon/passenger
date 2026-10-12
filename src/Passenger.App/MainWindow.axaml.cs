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
    private Action<bool>? afterBuild;
    private readonly List<Problem> problems = [];
    private IReadOnlyList<RunConfiguration> configurations = [];
    private RunConfiguration? configuration;
    private double bottomHeight = 260;
    private double explorerWidth = 280;
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
        Explorer.FileOpened += (_, path) => OpenFile(path);
        Editors.OpenRequested += (_, _) => _ = PickFolder();
        Editors.ActiveChanged += (_, _) => UpdateStatus();
        Editors.CaretMoved += (_, _) => UpdateCaret();
        Editors.AskToSave = AskToSave;
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
        Editors.CodeEditor.GutterClicked += (_, click) =>
        {
            if (Editors.Active is not { } active || debug is null)
            {
                return;
            }
            if (click.Context)
            {
                // A right-click on a breakpoint opens its properties, as in Rider.
                if (debug.Breakpoints.In(active.Path).FirstOrDefault(b => b.Line == click.Line) is { } breakpoint)
                {
                    ShowBreakpointPopup(breakpoint);
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
        };
        Closing += (_, _) => _ = csharp?.DisposeAsync();
        assist = new CodeAssist(Editors, Editors.AssistLayer, () => csharp);
        assist.Navigate += (_, target) => NavigateTo(target.Path, target.Position);
        assist.Message += (_, message) => StatusText.Text = message;
        builder = new ProcessRunner(Bottom.Build.Document);
        builder.Line += (_, line) =>
        {
            if (BuildOutput.Parse(line) is { } problem && !problems.Contains(problem))
            {
                problems.Add(problem);
            }
        };
        builder.Exited += (_, code) => OnBuilt(code);
        runner = new ProcessRunner(Bottom.Run.Document);
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
        Bottom.HideRequested += (_, _) => HideBottom();
        debug = new DebugController(Editors, Bottom.Debug);
        debug.ActionRequested += (_, action) => RunAppAction(action);
        debug.Message += (_, message) => StatusText.Text = message;
        debug.Navigate += (_, target) => ShowStop(target.Path, target.Position);
        debug.StateChanged += (_, _) => OnDebugStateChanged();
        assist.DebuggerValue = debug.ValueAt;
        Closing += (_, _) => _ = debug.StopAsync();
        UpdateStatus();
    }

    /// <summary>The C# service for the open folder, or null when it has no projects.</summary>
    public CSharpService? CSharpService => csharp;

    public EditorArea EditorArea => Editors;

    public Explorer.ExplorerView ExplorerView => Explorer;

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
            OpenFile(file);
        }
        if (options.Caret is { } caret && Editors.Active is not null)
        {
            Editors.CodeEditor.GoTo(caret);
        }
    }

    public void OpenFolder(string path)
    {
        folder = PathNames.Canonical(path);
        string name = Path.GetFileName(folder);
        Solution = SolutionModel.Find(folder) is { } solutionPath ? SolutionModel.Load(solutionPath) : null;
        Explorer.Load(folder, Solution);
        configurations = RunConfiguration.From(Solution, folder);
        configuration = configurations.FirstOrDefault();
        RunConfigName.Text = configuration?.Name ?? "No configurations";
        index.Build(folder);
        _ = csharp?.DisposeAsync();
        csharp = null;
        if (CSharpService.Wants(folder))
        {
            csharp = new CSharpService();
            csharp.StateChanged += (_, _) => LanguageText.Text = LanguageLabel();
            csharp.Start(folder);
        }
        Editors.ShowFolderHint(folder);
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

    private void OnKeyDownFirst(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && Overlay.IsVisible)
        {
            // Escape closes what is over the window before anything under it hears it.
            DismissPopup();
            e.Handled = true;
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
        switch (action)
        {
            case AppActions.Build:
                BuildSolution();
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
                ShowBottom("Debug");
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
            case AppActions.ShowBuild:
                ShowBottom("Build");
                break;
            case AppActions.ShowRun:
                ShowBottom("Run");
                break;
            case AppActions.ShowProblems:
                ShowBottom("Problems");
                break;
            case AppActions.HideToolWindow:
                HideBottom();
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
            case AppActions.Save:
                Editors.SaveActive();
                break;
            case AppActions.SaveAll:
                Editors.SaveAll();
                break;
            case AppActions.CloseTab:
                if (Editors.Active is { } active)
                {
                    _ = Editors.Close(active);
                }
                break;
            case AppActions.NextTab:
                Editors.Cycle(1);
                break;
            case AppActions.PreviousTab:
                Editors.Cycle(-1);
                break;
            case AppActions.FocusExplorer:
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

    public ToolWindows.BottomPanel BottomPanel => Bottom;

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
            ShowBottom("Debug");
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
    public void BuildSolution(Action<bool>? then = null)
    {
        if (folder is null || builder.IsRunning)
        {
            return;
        }
        afterBuild = then;
        string target = Solution?.Path ?? Directory.EnumerateFiles(folder, "*.csproj").FirstOrDefault() ?? folder;
        Editors.SaveAll();
        problems.Clear();
        Bottom.Build.Document.Clear();
        Bottom.Build.SetRunning(true);
        ShowBottom("Build");
        StatusText.Text = $"Building {Path.GetFileName(target)}…";
        BackgroundWork.Begin();
        builder.Start(Lsp.Dotnet.Host(), BuildOutput.Arguments(target), folder);
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
        ShowBottom("Run");
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
        ShowBottom("Debug");
        Bottom.Debug.ShowTab(console: false);
    }

    private void OnDebugStateChanged()
    {
        StopButton.IsVisible = debug.IsDebugging || runner.IsRunning;
        UpdateStripes();
    }

    private void OnBuilt(int code)
    {
        BackgroundWork.End();
        Action<bool>? then = afterBuild;
        afterBuild = null;
        Bottom.Build.SetRunning(false);
        int errors = problems.Count(p => p.Severity == ProblemSeverity.Error);
        int warnings = problems.Count - errors;
        Bottom.Problems.Show(problems);
        Bottom.Caption("Problems", problems.Count == 0 ? "Problems" : $"Problems {problems.Count}");
        StatusText.Text = code == 0
            ? $"Build succeeded{(warnings > 0 ? $" with {warnings} warning{(warnings == 1 ? "" : "s")}" : "")}"
            : $"Build failed: {errors} error{(errors == 1 ? "" : "s")}, {warnings} warning{(warnings == 1 ? "" : "s")}";
        if (code != 0 && errors > 0)
        {
            ShowBottom("Problems");
        }
        then?.Invoke(code == 0);
    }

    public void ShowBottom(string window)
    {
        Center.RowDefinitions[1].Height = new GridLength(1);
        Center.RowDefinitions[2].Height = new GridLength(bottomHeight);
        Bottom.Show(window);
        UpdateStripes();
    }

    private void HideBottom()
    {
        if (Center.RowDefinitions[2].Height.Value > 0)
        {
            bottomHeight = Center.RowDefinitions[2].ActualHeight;
        }
        Center.RowDefinitions[1].Height = new GridLength(0);
        Center.RowDefinitions[2].Height = new GridLength(0);
        UpdateStripes();
        if (Editors.Active is not null)
        {
            Editors.CodeEditor.Focus();
        }
    }

    private bool IsBottomOpen => Center.RowDefinitions[2].Height.Value > 0;

    private void UpdateStripes()
    {
        RunStripe.IsChecked = IsBottomOpen && Bottom.Current == "Run";
        DebugStripe.IsChecked = IsBottomOpen && Bottom.Current == "Debug";
        BuildStripe.IsChecked = IsBottomOpen && Bottom.Current == "Build";
        ProblemsStripe.IsChecked = IsBottomOpen && Bottom.Current == "Problems";
    }

    private void OnBottomStripe(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string window })
        {
            return;
        }
        if (IsBottomOpen && Bottom.Current == window)
        {
            HideBottom();
        }
        else
        {
            ShowBottom(window);
        }
    }

    private void OnExplorerStripe(object? sender, RoutedEventArgs e)
    {
        ColumnDefinition column = Upper.ColumnDefinitions[0];
        if (column.Width.Value > 0)
        {
            explorerWidth = column.ActualWidth;
            column.Width = new GridLength(0);
            ExplorerStripe.IsChecked = false;
        }
        else
        {
            column.Width = new GridLength(explorerWidth);
            ExplorerStripe.IsChecked = true;
        }
    }

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
        if (Editors.Active is { } active && active.Path == Path.GetFullPath(path))
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
            [.. symbols.SelectMany(symbol => (IEnumerable<Navigation.SymbolRow>)[Row(symbol, depth, path), .. Flatten(symbol.Children ?? [], depth + 1, path)])];

        // Roslyn names a member with its type ("Main() : void"); Rider shows the type after the name, muted.
        static Navigation.SymbolRow Row(Lsp.DocumentSymbol symbol, int depth, string path)
        {
            string name = symbol.Name;
            string? type = string.IsNullOrEmpty(symbol.Detail) || symbol.Detail == symbol.Name ? null : symbol.Detail;
            int typed = name.LastIndexOf(" : ", StringComparison.Ordinal);
            if (typed > 0)
            {
                type = name[(typed + 3)..];
                name = name[..typed];
            }
            return new Navigation.SymbolRow(name, type is null ? null : $": {type}", null, symbol.Kind, path,
                new TextPosition(symbol.SelectionRange.Start.Line, symbol.SelectionRange.Start.Character), depth, symbol.Range.End.Line);
        }
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

    private async Task<SaveChoice> AskToSave(OpenDocument document)
    {
        SaveDialog dialog = new(document.Name);
        return await dialog.ShowDialog<SaveChoice>(this);
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
        StatusText.Text = where;
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
