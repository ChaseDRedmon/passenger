using System.ComponentModel;
using ModelContextProtocol.Server;
using Passenger.App.Debugging;
using Passenger.App.ToolWindows;

namespace Passenger.App.Mcp;

/// <summary>Hot Reload for an agent, as the window's Apply Hot Reload Changes does it: the edits in the editors applied to the
/// program debugged (Edit and Continue), or saved for the dotnet watch that Run runs with Hot Reload, answered with what the
/// apply came to, never with what the status bar says.</summary>
public sealed partial class PassengerTools
{
    [McpServerTool(Name = "hot_reload", Title = "Apply Hot Reload changes", Destructive = true), Description(
        "Applies the edits made since the launch or the last apply to the running program, as Apply Hot Reload Changes does: " +
        "a debug_start launch, paused or running (Edit and Continue), or a run with Hot Reload (saved for dotnet watch). The " +
        "edits are the editors', saved or not. Returns the result (applied, nothingToApply, restartNeeded, errors, failed), " +
        "what it said, what was updated, each edit it could not apply or compile (file, line, ENC id), and whether the program " +
        "was restarted (restart true, or Settings' Always restart for a rude edit, on by default) and the new session. " +
        "debug_continue and debug_step also apply edits made while paused.")]
    public Task<HotReloadInfo> HotReload(
        [Description("Restart when an edit needs it (true) or keep the code running (false); Settings' choice by default")] bool? restart = null,
        [Description("After a restart, how long to wait for a stop (an app with a window: its UI)")] int waitSeconds = 30,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        DebugController debugger = Session.Debug;
        if (window.HotReloadWatched)
        {
            AgentCall.Say("Saving the edits for dotnet watch and waiting for what it makes of them");
            HotReloadOutcome? said = await window.SaveForWatchAsync().WaitAsync(cancel);
            return said is null
                ? new HotReloadInfo("unanswered", "The files were saved, and dotnet watch said nothing of them in 30 s: output or the Run window shows what it printed",
                    [], [], 0, [], false, false)
                : new HotReloadInfo(Result(said.Result), said.Said, [], [], 0, [], said.Result == HotReloadResult.RestartNeeded,
                    said.Result == HotReloadResult.RestartNeeded);
        }
        if (window.WhyNoHotReload() is { } why)
        {
            throw new InvalidOperationException($"Hot Reload cannot apply changes: {why}");
        }
        // The window's Apply Hot Reload Changes, its restarts made as the agent asks (else as Settings say) and never asked.
        DebugSession? before = debugger.Session;
        bool restarting = restart ?? window.Settings.RestartOnRudeEdit;
        AgentCall.Follow(window.Actions.Work, AppActions.ApplyHotReloadChanges);
        HotReloadApplied applied = await window.ApplyHotReloadAsync(async (rude, running) =>
        {
            if (!restarting)
            {
                return false;
            }
            AgentCall.Say(running ? $"Restarting {string.Join(", ", rude.Restart)} for edits they cannot take running" : "Restarting the program for edits it cannot take running");
            if (!running)
            {
                AgentCall.Follow(window.Actions.Work, AppActions.Debug, AppActions.Build);
            }
            return await window.RestartForAgentAsync(rude, running);
        }).WaitAsync(cancel) ?? throw new InvalidOperationException(window.Actions.Work.Of(AppActions.ApplyHotReloadChanges) is { } under
            ? ActionWork.Busy(under) : "Apply Hot Reload Changes is under way");
        HotReloadOutcome outcome = applied.Outcome;
        HotReloadInfo answer = new(Result(outcome.Result), outcome.Said, [.. outcome.Files.Select(Relative)], outcome.Modules, outcome.Methods,
            [.. outcome.Problems.Select(p => new Finding(p.Severity == Passenger.App.Run.ProblemSeverity.Error ? "error" : "warning", p.Code, p.Message,
                p.Path is { } at ? Relative(at) : p.Project, p.Position.Line + 1, p.Position.Column + 1))],
            outcome.Result == HotReloadResult.RestartNeeded, applied.Restarted);
        if (applied.Restarted && Session.Debug.Session != before)
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
