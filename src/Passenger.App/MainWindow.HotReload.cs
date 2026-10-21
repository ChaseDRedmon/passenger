using System.Diagnostics;
using Passenger.App.Debugging;
using Passenger.App.ToolWindows;
using Passenger.Core;

namespace Passenger.App;

/// <summary>Hot Reload while debugging in the window: what an Apply Changes came to in the status bar and Problems' Hot
/// Reload tab, and the restart an edit the runtime cannot take needs: at once (Settings' Always restart, on by default), or
/// asked as dotnet watch asks it, Yes, No, Always or Never.</summary>
public partial class MainWindow
{
    /// <summary>The debugging session the person said Never to restarting for, which is not asked again.</summary>
    private DebugSession? neverRestart;

    /// <summary>How long the last restart for an edit Hot Reload could not apply took, from the program stopped to the new
    /// one launched: said with it, and read by tests.</summary>
    public TimeSpan? LastHotReloadRestart { get; private set; }

    /// <summary>dotnet watch's question, and its answers, in the order the dialog shows them (the last its default).</summary>
    public const string RestartQuestion = "Do you want to restart your app?";

    public static readonly string[] RestartAnswers = ["Never", "Always", "No", "Yes"];

    /// <summary>Rider's Apply Hot Reload Changes (Alt+F10 in both its Visual Studio keymaps, after Rerun and Stop in its Run
    /// menu): the program debugged takes the edits through Edit and Continue; one Run runs under dotnet watch takes them as
    /// the files are saved, which dotnet watch applies.</summary>
    private void AddHotReload()
    {
        Add(AppActions.ApplyHotReloadChanges, "Apply Hot Reload Changes", "Run", context =>
        {
            if (session.Debug.HotReload is not null)
            {
                _ = WorkingAsync(AppActions.ApplyHotReloadChanges, "Applying Hot Reload changes…", session.Debug.ApplyAndRestartAsync);
            }
            else if (session.Debug.Running.Count > 0 && !session.Debug.IsDebugging)
            {
                // A container run's services take the edits through the debugger attached to each.
                _ = WorkingAsync(AppActions.ApplyHotReloadChanges, "Applying Hot Reload changes…", session.Debug.ApplyRunningAndRestartAsync);
            }
            else
            {
                _ = WorkingAsync(AppActions.ApplyHotReloadChanges, "Applying Hot Reload changes…", SaveForWatchAsync);
            }
        }, _ => session.Debug.HotReload is not null || session.Debug.IsDebugging ? session.Debug.WhyNotApply()
            : session.Debug.Running.Count > 0 || buildRun.WatchesHotReload ? null
            : buildRun.IsRunning ? "This program runs without Hot Reload: Run it with Hot Reload on (Settings, Hot Reload), or Debug it"
            : "Nothing runs: Debug the program, or Run it with Hot Reload on (Settings, Hot Reload)", ("Run", 0, 9));
    }

    /// <summary>The words dotnet watch says an apply came to with, what each means, and what it came to.</summary>
    private static readonly (string Words, string Said, HotReloadResult Result)[] WatchSays =
    [
        ("changes applied", "Hot Reload: dotnet watch applied the changes", HotReloadResult.Applied),
        ("No managed code changes to apply", "Hot Reload: no managed code changes to apply", HotReloadResult.NothingToApply),
        ("Restart is needed to apply the changes", "Hot Reload: dotnet watch is restarting the application to apply the changes", HotReloadResult.RestartNeeded),
        ("Attempted to invoke a deleted method implementation",
            "Hot Reload: the program called a method as it was before the edit changed its signature: dotnet watch is restarting the application to apply the changes",
            HotReloadResult.RestartNeeded),
        ("Restarting application", "Hot Reload: dotnet watch is restarting the application to apply the changes", HotReloadResult.RestartNeeded),
        ("Unable to apply changes due to compilation errors", "Hot Reload: dotnet watch could not apply the changes: the code does not compile", HotReloadResult.Errors),
    ];

    /// <summary>The variable that gives <see cref="WatchSaysLater"/> in milliseconds (the tests' run, shorter).</summary>
    public const string WatchSaysLaterVariable = "PASSENGER_WATCH_LATER_MS";

    /// <summary>How long dotnet watch's later words are followed once it has said what it made of a save: changes it applied
    /// can still end in a restart, when the program calls a method the update deleted (one whose signature changed). 10 s,
    /// or what <see cref="WatchSaysLaterVariable"/> gives; a value there that is no whole number of milliseconds above 0 is
    /// fatal, as a malformed flag is (Program checks it before the window opens).</summary>
    internal static TimeSpan WatchSaysLater => Environment.GetEnvironmentVariable(WatchSaysLaterVariable) is { Length: > 0 } given
        ? int.TryParse(given, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int milliseconds) && milliseconds > 0
            ? TimeSpan.FromMilliseconds(milliseconds)
            : throw new FormatException($"{WatchSaysLaterVariable} takes a whole number of milliseconds above 0, not '{given}'")
        : TimeSpan.FromSeconds(10);

    /// <summary>A program under dotnet watch takes edits as their files are saved: they are saved, and what dotnet watch
    /// says of them (in the Run window) is said in the status bar, its last word standing, which is the answer (null when
    /// it said nothing of them).</summary>
    internal async Task<HotReloadOutcome?> SaveForWatchAsync()
    {
        TaskCompletionSource said = new(TaskCreationOptions.RunContinuationsAsynchronously);
        HotReloadOutcome? last = null;
        EventHandler<string> heard = (_, text) =>
        {
            foreach ((string words, string meaning, HotReloadResult result) in WatchSays)
            {
                if (text.Contains(words, StringComparison.Ordinal))
                {
                    // dotnet watch writes the compiler's errors in the Run window.
                    Say(meaning, result == HotReloadResult.Errors ? StatusPlace.Window(Dock, "run") : null);
                    last = new HotReloadOutcome(result, meaning);
                    said.TrySetResult();
                    return;
                }
            }
        };
        Bottom.Run.Written += heard;
        try
        {
            Editors.SaveAll();
            status.Text = "Hot Reload: saved for dotnet watch to apply";
            if (await Task.WhenAny(said.Task, Task.Delay(TimeSpan.FromSeconds(30))) == said.Task)
            {
                await Task.Delay(WatchSaysLater);
            }
        }
        finally
        {
            Bottom.Run.Written -= heard;
        }
        return last;
    }

    /// <summary>Whether Apply Hot Reload Changes goes to dotnet watch: what Run runs with Hot Reload, nothing being debugged.</summary>
    internal bool HotReloadWatched => session.Debug.HotReload is null && !session.Debug.IsDebugging && session.Debug.Running.Count == 0 && buildRun.WatchesHotReload;

    /// <summary>Whether Apply Hot Reload Changes goes to the services of a container run with Hot Reload, through the
    /// debugger attached quietly to each, nothing being debugged.</summary>
    internal bool HotReloadRunning => session.Debug.HotReload is null && !session.Debug.IsDebugging && session.Debug.Running.Count > 0;

    /// <summary>Why Apply Hot Reload Changes cannot run, as the action says it, or null.</summary>
    internal string? WhyNoHotReload() => actions!.WhyNot(AppActions.ApplyHotReloadChanges, actions.Context());

    /// <summary>The restart for edits Hot Reload could not apply, as an agent asks it: never the question, which is the person's.</summary>
    internal Task<bool> RestartForAgentAsync(HotReloadOutcome outcome) => HotReloadRunning
        ? buildRun.RestartContainerServicesAsync(outcome, outcome.Restart)
        : RestartForEditAsync(session, outcome, ask: false);

    /// <summary>Apply Hot Reload Changes on the toolbar, and in the Debug and Run windows, while a program runs with Hot
    /// Reload: debugged with it, or run under dotnet watch.</summary>
    private void ShowHotReloadButtons()
    {
        bool debugged = session.Debug.HotReload is not null;
        bool watched = buildRun.WatchesHotReload || session.Debug.Running.Count > 0;
        HotReloadButton.IsVisible = debugged || watched;
        HotReloadButton.IsEnabled = !debugged || session.Debug.WhyNotApply() is null;
        Bottom.Debug.ShowHotReload(debugged, debugged && session.Debug.WhyNotApply() is null);
        Bottom.Run.ShowHotReload(watched);
    }

    private void BindHotReload(ProjectSession next)
    {
        DebugController debugger = next.Debug;
        debugger.Applied += (_, outcome) =>
        {
            if (session == next)
            {
                Bottom.Problems.ShowHotReload(outcome.Said, outcome.Problems);
                if (outcome.Problems.Any(p => p.Severity == Run.ProblemSeverity.Error))
                {
                    Dock.Show("problems");
                }
                // The debugger has said it; its edits that could not apply, or warned, are where to see more.
                if (outcome.Problems.Count > 0)
                {
                    Say(outcome.Said, HotReloadPlace);
                }
            }
        };
        debugger.RestartFor = outcome => session == next ? RestartForEditAsync(next, outcome) : Task.FromResult(false);
        // A container run's services, under Run with Hot Reload: built again and restarted, as dotnet watch restarts its program.
        debugger.RestartRunningFor = outcome => session == next && settings.RestartOnRudeEdit
            ? buildRun.RestartContainerServicesAsync(outcome, outcome.Restart)
            : Task.FromResult(false);
    }

    /// <summary>Restarts the program Debug launched so the edits Hot Reload could not apply run: stopped, built and launched
    /// again with its configuration and the breakpoints, asked first as Settings says unless <paramref name="ask"/> is false.
    /// True once the new session is open.</summary>
    private async Task<bool> RestartForEditAsync(ProjectSession next, HotReloadOutcome outcome, bool ask = true)
    {
        // A container run's services are built again and restarted, each attached to again as it starts held.
        if (next.Debug.Chosen is { Group: not null } service && next.Debug.Session is { Attached: true })
        {
            return (!ask || settings.RestartOnRudeEdit) && await buildRun.RestartContainerServicesAsync(outcome, outcome.Restart.Count > 0 ? outcome.Restart : [service.Name]);
        }
        if (next.Debug.Session is not { Attached: false } debugged)
        {
            return false;
        }
        if (ask && !settings.RestartOnRudeEdit)
        {
            if (neverRestart == debugged)
            {
                return false;
            }
            string? answer = await new ChoiceDialog("Hot Reload", $"{outcome.Said}\n\n{RestartQuestion}", RestartAnswers, "Yes").ShowDialog<string?>(this);
            switch (answer)
            {
                case "Always":
                    ApplySettings(settings with { RestartOnRudeEdit = true });
                    break;
                case "Yes":
                    break;
                case "Never":
                    neverRestart = debugged;
                    return false;
                default:
                    return false;
            }
            if (session != next)
            {
                return false;
            }
        }
        status.Text = "Restarting application to apply changes ...";
        Bottom.Debug.Console.Append("Hot Reload: Restarting application to apply changes ...\n");
        Stopwatch took = Stopwatch.StartNew();
        string? refused = await buildRun.RestartDebugAsync(next.Debug.Chosen!.Name);
        LastHotReloadRestart = took.Elapsed;
        if (refused is not null)
        {
            status.Text = $"Could not restart to apply changes: {refused}";
            return false;
        }
        // The restart's build took Problems to its own tab; the edits that needed the restart are what to see.
        Bottom.Problems.Shown = ProblemsTab.HotReload;
        string said = $"Restarted in {took.Elapsed.TotalSeconds:0.0} s to apply changes: {outcome.Said}";
        IdeLog.Write(IdeLog.Debugger, said);
        Bottom.Debug.Console.Append($"Hot Reload: {said}\n");
        // Each edit that needed it, in a compiler message's shape, at the compiler's line, which the console links to its line.
        foreach (Run.Problem rude in outcome.Problems.Where(p => p.Path is not null))
        {
            Lsp.LspPosition at = CSharp.ServerPlaces.ServerPositionOf(CSharp.ServerPlaces.TextOf(Editors, rude.Path!), rude.Position);
            Bottom.Debug.Console.Append($"{rude.Path}({at.Line + 1},{at.Character + 1}): {(rude.Severity == Run.ProblemSeverity.Error ? "error" : "warning")} {rude.Code}: {rude.Message}\n");
        }
        Say(said, outcome.Problems.Count > 0 ? HotReloadPlace : null);
        return true;
    }

    /// <summary>Problems' Hot Reload tab, the first edit it lists chosen.</summary>
    private StatusPlace HotReloadPlace => StatusPlace.Problems(Dock, Bottom.Problems, ProblemsTab.HotReload, _ => true);
}
