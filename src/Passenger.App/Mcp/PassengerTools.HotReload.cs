using System.ComponentModel;
using ModelContextProtocol.Server;
using Passenger.App.Debugging;

namespace Passenger.App.Mcp;

/// <summary>Hot Reload for an agent, as the window's Apply Hot Reload Changes does it: the edits in the editors applied to the
/// program debugged (Edit and Continue), or saved for the dotnet watch that Run runs with Hot Reload, answered with what the
/// apply came to, never with what the status bar says.</summary>
public sealed partial class PassengerTools
{
    [McpServerTool(Name = "hot_reload", Title = "Apply Hot Reload changes", Destructive = true), Description(
        "Applies the code edits made since the launch or the last apply to the running program, as Apply Hot Reload Changes " +
        "does: to one debug_start launched, paused or running (Edit and Continue), or to one run with Hot Reload, saving the " +
        "files for dotnet watch. The edits are the editors', saved or not. Returns the result (applied, nothingToApply, " +
        "restartNeeded, errors, failed), what it said, what was updated, each edit it could not apply or that does not compile " +
        "(file, line, Roslyn's ENC id), and whether the program was restarted for it (restart true, or Settings' Always " +
        "restart for a rude edit, on by default) and where the new session is. debug_continue and debug_step also apply edits " +
        "made while paused.")]
    public Task<HotReloadInfo> HotReload(
        [Description("Restart when an edit needs it (true) or keep the code running (false); Settings' choice by default")] bool? restart = null,
        [Description("After a restart, how long to wait for a stop (an app with a window: its UI)")] int waitSeconds = 30,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        DebugController debugger = Session.Debug;
        if (window.HotReloadWatched)
        {
            HotReloadOutcome? said = await window.SaveForWatchAsync().WaitAsync(cancel);
            return said is null
                ? new HotReloadInfo("unanswered", "The files were saved, and dotnet watch said nothing of them in 30 s: debug_output or the Run window shows what it printed",
                    [], [], 0, [], false, false)
                : new HotReloadInfo(Result(said.Result), said.Said, [], [], 0, [], said.Result == HotReloadResult.RestartNeeded,
                    said.Result == HotReloadResult.RestartNeeded);
        }
        if (window.WhyNoHotReload() is { } why)
        {
            throw new InvalidOperationException($"Hot Reload cannot apply changes: {why}");
        }
        DebugSession before = debugger.Session!;
        HotReloadOutcome outcome = await debugger.ApplyChangesAsync().WaitAsync(cancel);
        bool needed = outcome.Result == HotReloadResult.RestartNeeded;
        bool restarted = false;
        if (needed && (restart ?? window.Settings.RestartOnRudeEdit))
        {
            restarted = await window.RestartForAgentAsync(outcome);
        }
        else if (needed && debugger.HotReload is { } reload)
        {
            // Declined, as dotnet watch's No: the next resume goes on in the code it has rather than restarting for these edits.
            reload.Changed = false;
        }
        HotReloadInfo answer = new(Result(outcome.Result), outcome.Said, [.. outcome.Files.Select(Relative)], outcome.Modules, outcome.Methods,
            [.. outcome.Problems.Select(p => new Finding(p.Severity == Passenger.App.Run.ProblemSeverity.Error ? "error" : "warning", p.Code, p.Message,
                p.Path is { } at ? Relative(at) : p.Project, p.Position.Line + 1, p.Position.Column + 1))],
            needed, restarted);
        if (restarted && Session.Debug.Session != before)
        {
            return answer with { Debugger = await AfterLaunch(TimeSpan.FromSeconds(waitSeconds), cancel) };
        }
        return answer;

        static string Result(HotReloadResult result) => result switch
        {
            HotReloadResult.Applied => "applied",
            HotReloadResult.NothingToApply => "nothingToApply",
            HotReloadResult.RestartNeeded => "restartNeeded",
            HotReloadResult.Errors => "errors",
            _ => "failed",
        };
    });
}

/// <summary>What a Hot Reload apply came to: its result, its words, the files (the folder's paths), modules and methods it updated, each
/// edit it could not apply or that does not compile at its place, whether a restart is needed and whether one was made; after a
/// restart, where the new session is.</summary>
public sealed record HotReloadInfo(string Result, string Said, IReadOnlyList<string> Files, IReadOnlyList<string> Modules, int Methods,
    IReadOnlyList<Finding> Edits, bool RestartNeeded, bool Restarted)
{
    public DebugSnapshot? Debugger { get; init; }
}
