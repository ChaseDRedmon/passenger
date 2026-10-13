using Avalonia.Controls;
using Avalonia.Interactivity;
using Passenger.App.Run;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.App;

/// <summary>The window's run configurations and what launches them: those the folder's projects generate beside the
/// folder's own (kept with its state), the one chosen in the toolbar, Edit Configurations, and Run and Debug, which resolve
/// the chosen one into a <see cref="LaunchPlan"/>, build as it says and start that plan, as Profile does in its partial.</summary>
public partial class MainWindow
{
    /// <summary>The person's own configurations for the open folder, in their order.</summary>
    private List<RunConfiguration> own = [];

    /// <summary>The folder <see cref="own"/> belongs to, so another folder's are never shown with this one.</summary>
    private string? ownFolder;

    private IReadOnlyList<RunConfiguration> configurations = [];
    private RunConfiguration? configuration;

    /// <summary>A Run between its press and its program's start (the plan resolved, the project built), which a second
    /// press may not start again.</summary>
    private bool launching;

    /// <summary>The run configuration the Run window last ran.</summary>
    private string ran = string.Empty;

    /// <summary>The program Run last started in the terminal, until a run in the Run window comes after it.</summary>
    private TerminalRun? terminalRun;

    /// <summary>Whether a program Run started runs, in the Run window or the terminal.</summary>
    public bool IsRunning => runner.IsRunning || terminalRun is { IsRunning: true };

    /// <summary>The program Run last started, when it runs in the terminal; null when the last ran in the Run window.</summary>
    public TerminalRun? RunInTerminal => terminalRun;

    /// <summary>The end of the program Run last started, in the Run window or the terminal, with its exit code.</summary>
    public Task<int> RunEnd => terminalRun?.Ended ?? runner.Ended;

    /// <summary>Every configuration: the generated ones, then the folder's own.</summary>
    public IReadOnlyList<RunConfiguration> Configurations => configurations;

    /// <summary>The configuration chosen in the toolbar, which Run, Debug and Profile launch.</summary>
    public RunConfiguration? Configuration => configuration;

    /// <summary>The run configurations dialog open over the window, or null.</summary>
    public RunConfigurationsDialog? RunConfigurationsDialog { get; private set; }

    /// <summary>How a run that ended is named, in the status bar and to an agent.</summary>
    public static string RunEnded(string name, int code) => $"{name} exited with code {code}";

    /// <summary>Reads the open folder's configurations: its own from the store when the window keeps state (else those made
    /// since it opened), and those its projects generate; the one chosen is the one kept, else the first.</summary>
    private void LoadConfigurations()
    {
        string? keep = configuration?.Name;
        if (folderState is { } kept)
        {
            RunConfigurationsState read = kept.RunConfigurations.Value;
            own = [.. read.Configurations.Select(FromStore)];
            keep = read.Selected ?? (ownFolder == folder ? keep : null);
        }
        else if (ownFolder != folder)
        {
            own = [];
            keep = null;
        }
        ownFolder = folder;
        Configure(keep);
    }

    /// <summary>Makes the list again from the projects loaded now and the folder's own, the chosen one kept while it is there.</summary>
    private void Configure() => Configure(configuration?.Name);

    private void Configure(string? keep)
    {
        configurations = [.. RunConfiguration.Generate(Projects), .. own];
        configuration = configurations.FirstOrDefault(c => string.Equals(c.Name, keep, StringComparison.OrdinalIgnoreCase)) ?? configurations.FirstOrDefault();
        RunConfigName.Text = configuration?.Name ?? "No configurations";
    }

    /// <summary>Chooses the run configuration by name, as the toolbar's menu does; false when there is none.</summary>
    public bool SelectConfiguration(string name)
    {
        if (configurations.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } chosen)
        {
            return false;
        }
        configuration = chosen;
        RunConfigName.Text = chosen.Name;
        KeepConfigurations();
        return true;
    }

    /// <summary>Takes the folder's own configurations as Edit Configurations leaves them, and the one chosen there; kept
    /// with the folder when the window keeps state.</summary>
    public void SetConfigurations(IReadOnlyList<RunConfiguration> mine, string? chosen)
    {
        UiThread.Check();
        own = [.. mine.Select(c => c with { Generated = false, Unsupported = null })];
        ownFolder = folder;
        Configure(chosen ?? configuration?.Name);
        KeepConfigurations();
    }

    private void KeepConfigurations() =>
        folderState?.RunConfigurations.Set(new RunConfigurationsState { Configurations = [.. own.Select(ForStore)], Selected = configuration?.Name });

    /// <summary>A configuration as the store keeps it: its project relative to the folder, so a hand edit reads plainly.</summary>
    private RunConfiguration ForStore(RunConfiguration kept) => folder is not null && PathNames.IsUnder(kept.Project, folder)
        ? kept with { Project = Path.GetRelativePath(folder, kept.Project).Replace('\\', '/') }
        : kept;

    private RunConfiguration FromStore(RunConfiguration kept) => folder is null ? kept : kept with { Project = PathNames.Canonical(Path.Combine(folder, kept.Project)) };

    /// <summary>Run's Edit Configurations: the dialog over the window, its changes the folder's once applied.</summary>
    public RunConfigurationsDialog ShowRunConfigurations()
    {
        RunConfigurationsDialog dialog = new([.. configurations.Where(c => c.Generated)], own, [.. Projects.Where(p => p.Exists && p.IsRunnable)],
            configuration?.Name, SetConfigurations);
        RunConfigurationsDialog = dialog;
        dialog.Closed += (_, _) =>
        {
            if (RunConfigurationsDialog == dialog)
            {
                RunConfigurationsDialog = null;
            }
        };
        _ = dialog.ShowDialog(this);
        return dialog;
    }

    /// <summary>A configuration's plan, with these arguments in place of its own if given, or why it has none.</summary>
    private async Task<(LaunchPlan? Plan, string? Problem)> ResolvePlanAsync(RunConfiguration chosen, string? arguments)
    {
        try
        {
            return (await LaunchPlan.ResolveAsync(arguments is null ? chosen : chosen with { Arguments = arguments }, Projects), null);
        }
        catch (Exception error) when (error is LaunchException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (null, error.Message);
        }
    }

    /// <summary>A configuration's plan with its build before launch done: the plan to start, or why there is none.</summary>
    private async Task<(LaunchPlan? Plan, string? Problem)> PrepareAsync(RunConfiguration chosen, string? arguments, bool build = true)
    {
        (LaunchPlan? plan, string? problem) = await ResolvePlanAsync(chosen, arguments);
        if (build && plan?.Build is { } step && await BuildAsync(step) is { Succeeded: false } unbuilt)
        {
            return (null, unbuilt.Message);
        }
        return (plan, problem);
    }

    /// <summary>A plan's build before launch, in the Build window as Build Selected Projects shows it.</summary>
    private Task<BuildOutcome> BuildAsync(BuildStep step)
    {
        TaskCompletionSource<BuildOutcome> built = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Build(BuildKind.Build, [step.Project], outcome => built.TrySetResult(outcome), step.Framework);
        return built.Task;
    }

    /// <summary>Runs the chosen configuration, with these arguments in place of its own if given: its plan's program started
    /// after its build, its output and input in the Run window, or in a tab of the Terminal window when the configuration
    /// uses the terminal. Null once it runs (<see cref="RunEnd"/> is its end), else why it could not, which the status bar
    /// says too.</summary>
    public async Task<string?> RunAsync(string? arguments = null)
    {
        if (configuration is not { } chosen)
        {
            return Said("Nothing to run: no executable project");
        }
        if (IsRunning || launching)
        {
            return Said("Already running");
        }
        launching = true;
        using IDisposable held = BackgroundWork.Scope();
        try
        {
            Editors.SaveAll();
            Bottom.Run.Clear();
            // dotnet watch builds the project itself, and runs it again after an edit it cannot apply.
            bool hotReload = settings.HotReload && chosen.Program.Length == 0;
            (LaunchPlan? plan, string? problem) = await PrepareAsync(chosen, arguments, build: !hotReload);
            if (plan is null)
            {
                return Said(problem!);
            }
            if (plan.WhyNot("run", built: !hotReload) is { } why)
            {
                return Said(why);
            }
            ran = chosen.Name;
            runProject = Path.GetDirectoryName(plan.Project);
            string running = hotReload ? $"Running {chosen.Name} with Hot Reload: a save applies its changes" : $"Running {chosen.Name}";
            Dictionary<string, string> variables = hotReload
                ? new Dictionary<string, string>(plan.Environment) { ["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1", ["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "true" }
                : new Dictionary<string, string>(plan.Environment);
            if (plan.Console == ConsoleKind.Terminal)
            {
                string? refused = await RunInTerminalAsync(chosen.Name, hotReload ? DotnetStart(Watch(plan), plan.WorkingDirectory) : ChildProcesses.Command(plan.Program, plan.Arguments, plan.WorkingDirectory), variables, running);
                if (refused is null && terminalRun is { } inTerminal)
                {
                    OpenBrowserOnceListening(plan, inTerminal.Ended);
                }
                return refused;
            }
            terminalRun = null;
            Bottom.Run.SetRunning(true);
            StopButton.IsVisible = true;
            ShowToolWindow("run");
            StatusText.Text = running;
            string? notStarted = Started(hotReload
                ? runner.StartDotnet(Watch(plan), plan.WorkingDirectory, variables)
                : runner.Start(plan.Program, plan.Arguments, plan.WorkingDirectory, variables));
            if (notStarted is null)
            {
                OpenBrowserOnceListening(plan, runner.Ended);
            }
            return notStarted;
        }
        finally
        {
            launching = false;
        }
    }

    /// <summary>The .NET command line as the IDE runs it, or null where there is none (which the run then says).</summary>
    private static System.Diagnostics.ProcessStartInfo? DotnetStart(IReadOnlyList<string> arguments, string folder)
    {
        try
        {
            return ChildProcesses.DotnetCommand(arguments, folder);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Runs a plan's program in a tab of the Terminal window named for its configuration, the keys going to it, as
    /// Rider's Use external console runs one in a console of its own; Stop ends it, and its end shows in the status bar as
    /// a run's in the Run window does. Null once it runs, else why it could not.</summary>
    private async Task<string?> RunInTerminalAsync(string name, System.Diagnostics.ProcessStartInfo? start, IReadOnlyDictionary<string, string> variables, string running)
    {
        if (start is null)
        {
            return Said("No .NET command line found: install the .NET SDK, or set DOTNET_ROOT to where it is.");
        }
        foreach ((string variable, string value) in variables)
        {
            start.Environment[variable] = value;
        }
        TerminalRun run;
        try
        {
            run = await ShowInTerminal(name, start);
        }
        catch (LaunchException failed)
        {
            return Said(failed.Message);
        }
        terminalRun = run;
        StopButton.IsVisible = true;
        StatusText.Text = running;
        _ = Ended();
        return null;

        async Task Ended()
        {
            int code = await run.Ended;
            StopButton.IsVisible = IsRunning || debug.IsDebugging;
            if (terminalRun == run)
            {
                StatusText.Text = RunEnded(name, code);
            }
        }
    }

    /// <summary>Starts in a tab of the Terminal window, named for the configuration being debugged, the program the debugger
    /// asks for (its runInTerminal request): held at its start by the variables the debugger gives it until the debugger
    /// has hold of it.</summary>
    private Task<TerminalRun> RunForDebugger(string name, Dap.RunInTerminalArguments arguments)
    {
        if (arguments.Args is not [string program, ..])
        {
            throw new LaunchException("The debugger asked the terminal to run nothing");
        }
        IEnumerable<string> rest = arguments.Args.Skip(1);
        // A program named without a folder (a .dll's "dotnet") is the one the IDE builds with, or one on the PATH.
        System.Diagnostics.ProcessStartInfo start = ChildProcesses.Command(program is "dotnet" ? Core.Dotnet.Host() : program, rest, arguments.Cwd ?? folder);
        foreach ((string variable, string value) in arguments.Env ?? new Dictionary<string, string>())
        {
            start.Environment[variable] = value;
        }
        return ShowInTerminal(name, start);
    }

    /// <summary>Runs a program in a tab of the Terminal window and shows the window on it: the tab first, so a window opened
    /// for it starts no shell of its own.</summary>
    private Task<TerminalRun> ShowInTerminal(string name, System.Diagnostics.ProcessStartInfo start)
    {
        Task<TerminalRun> starting = terminal!.RunAsync(name, start);
        ShowToolWindow("terminal");
        return starting;
    }

    /// <summary>dotnet watch's command line for a plan, as Rider's Hot Reload runs it: the project for its framework, no
    /// launch profile (the plan has its values already), the program's own arguments after <c>--</c>. It runs the program in
    /// its own working folder, which is the plan's.</summary>
    private static List<string> Watch(LaunchPlan plan)
    {
        List<string> watch = ["watch", "--non-interactive", "run", "--project", plan.Project!, "--no-launch-profile"];
        if (plan.Framework is { } framework)
        {
            watch.AddRange(["--framework", framework]);
        }
        watch.Add("--");
        watch.AddRange(plan.Built is { } assembly && plan.Arguments.Count > 0 && plan.Arguments[0] == assembly ? plan.Arguments.Skip(1) : plan.Arguments);
        return watch;
    }

    /// <summary>Run, from its key, the toolbar or the Run window: what stopped it, if anything, is in the status bar.</summary>
    private void RunSelected() => _ = RunAsync();

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

    /// <summary>Resolves and builds the chosen configuration, then launches its plan under the debugger, as Rider's Debug
    /// does, with these arguments in place of its own if given; the Debug window opens on its Console until the program
    /// stops somewhere. Null once its session is open, or what stopped it (a failed build, no configuration, the adapter's
    /// refusal).</summary>
    public async Task<string?> DebugAsync(string? arguments = null)
    {
        if (configuration is not { } chosen)
        {
            return "Nothing to debug: no executable project";
        }
        using IDisposable held = BackgroundWork.Scope();
        (LaunchPlan? plan, string? problem) = await PrepareAsync(chosen, arguments);
        if (plan is null)
        {
            return problem;
        }
        if (plan.WhyNot("debug") is { } why)
        {
            return why;
        }
        ShowToolWindow("debug");
        Bottom.Debug.ShowTab(console: true);
        // Before the session clears the Debug window's console, which forgets the links the last one's output had.
        debugProject = Path.GetDirectoryName(plan.Project);
        string? refused = await debug.StartAsync(plan);
        if (refused is null)
        {
            OpenBrowserOnceListening(plan, DebugEnded());
        }
        return refused;
    }

    /// <summary>The end of the session being debugged; done already when there is none.</summary>
    private Task DebugEnded()
    {
        if (debug.Session is not { State: not Debugging.DebugState.Ended } session)
        {
            return Task.CompletedTask;
        }
        TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += (_, _) =>
        {
            if (session.State == Debugging.DebugState.Ended)
            {
                ended.TrySetResult();
            }
        };
        return ended.Task;
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

    /// <summary>The run configurations to choose from, as a menu under the toolbar widget, and Edit Configurations below
    /// them, as Rider's run widget lists them.</summary>
    private void OnRunConfigClicked(object? sender, RoutedEventArgs e)
    {
        ContextMenu menu = new();
        foreach (RunConfiguration each in configurations)
        {
            MenuItem item = new() { Header = each.Name };
            string name = each.Name;
            item.Click += (_, _) => SelectConfiguration(name);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No executable projects", IsEnabled = false });
        }
        menu.Items.Add(new Separator());
        MenuItem edit = new() { Header = "Edit Configurations..." };
        edit.Click += (_, _) => Request(AppActions.EditConfigurations);
        menu.Items.Add(edit);
        Open(menu, RunConfigButton);
    }
}
