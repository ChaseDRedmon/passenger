using System.Diagnostics;
using Passenger.App.Run;
using Passenger.Core;
using Passenger.Dap;
using Passenger.Lsp;
using Passenger.Roslyn.Extension;
using Passenger.Text;

namespace Passenger.App.Debugging;

/// <summary>What an Apply Changes came to.</summary>
public enum HotReloadResult
{
    /// <summary>The program runs the edits now.</summary>
    Applied,

    /// <summary>The edits change no code the program runs (comments, whitespace on a line).</summary>
    NothingToApply,

    /// <summary>An edit the runtime cannot take while the program runs (Roslyn's rude edits): restarting applies it.</summary>
    RestartNeeded,

    /// <summary>The code does not compile: nothing is applied until it does.</summary>
    Errors,

    /// <summary>Hot Reload could not run or the runtime refused the update; the reason says which.</summary>
    Failed,
}

/// <summary>What an Apply Changes came to, in words for the status bar and the Debug console, with the edits that could not
/// be applied (each at its line, as Problems' Hot Reload tab lists them), the files and modules the update changed, and
/// how long it took.</summary>
public sealed record HotReloadOutcome(HotReloadResult Result, string Said)
{
    public IReadOnlyList<Problem> Problems { get; init; } = [];

    /// <summary>The files whose edits the program now runs (or, with nothing to apply, whose edits needed nothing).</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>The modules updated, by name.</summary>
    public IReadOnlyList<string> Modules { get; init; } = [];

    /// <summary>How many methods the update replaced or added.</summary>
    public int Methods { get; init; }

    public TimeSpan Took { get; init; }
}

/// <summary>Hot Reload of one debugging session (Edit and Continue): Roslyn's own Edit and Continue service, in the language
/// server through Passenger's extension, compares the solution as the editors have it with what was built (and applied
/// since) and emits the deltas; Passenger's debugger applies them to the program, paused or running. The capabilities
/// Roslyn's rules are given are the ones the program's runtime reports. A UI-thread object.</summary>
public sealed class HotReload
{
    private readonly DebugSession debug;
    private readonly Func<RoslynSession?> server;
    private int? id;
    private Task<string?>? starting;
    private RoslynSession? startedOn;

    /// <summary>Why Hot Reload stopped for the session: the runtime refused an update part way.</summary>
    private string? stopped;

    private HotReload(DebugSession debug, Func<RoslynSession?> server)
    {
        this.debug = debug;
        this.server = server;
    }

    /// <summary>Starts Hot Reload for a session just launched: Roslyn's Edit and Continue session begins on the solution as
    /// the server has it now, which is what was just built.</summary>
    public static HotReload Start(DebugSession debug, Func<RoslynSession?> server)
    {
        UiThread.Check();
        HotReload hotReload = new(debug, server);
        hotReload.starting = hotReload.StartAsync();
        return hotReload;
    }

    /// <summary>Whether the files have been edited since the launch or the last update applied.</summary>
    public bool Changed { get; set; }

    /// <summary>Why Apply Changes cannot run now, or null when it can.</summary>
    public string? WhyNot => stopped is not null ? stopped
        : debug.State is not (DebugState.Running or DebugState.Paused) ? "The program is not running"
        : null;

    private async Task<string?> StartAsync()
    {
        if (server() is not { } roslyn)
        {
            return "Hot Reload needs the C# language server, which has not started";
        }
        try
        {
            HotReloadStarted started = await roslyn.ExtensionAsync<HotReloadStarted>(HotReloadHandlers.Start, new HotReloadStart([]));
            if (started.Problem is { } problem)
            {
                return problem;
            }
            id = started.Session;
            startedOn = roslyn;
            return null;
        }
        catch (LanguageServerFailed failed)
        {
            return $"Hot Reload could not start: {failed.Reason}";
        }
    }

    /// <summary>Applies the edits made since the launch or the last update: Roslyn emits them against the program's code and
    /// the debugger applies them; an edit the runtime cannot take, or code that does not compile, stops it, and nothing is
    /// applied. On an update applied, the session's built lines are the files' lines now.</summary>
    public async Task<HotReloadOutcome> ApplyAsync()
    {
        UiThread.Check();
        Stopwatch took = Stopwatch.StartNew();
        if (WhyNot is { } why)
        {
            return new HotReloadOutcome(HotReloadResult.Failed, why);
        }
        if ((starting is null ? "Hot Reload did not start" : await starting) is not null)
        {
            // The server may have been loading at the launch: Roslyn checks edited files against the PDB's checksums.
            starting = StartAsync();
            if (await starting is { } notStarted)
            {
                return new HotReloadOutcome(HotReloadResult.Failed, notStarted);
            }
        }
        if (id is not int session || startedOn is not { } roslyn || roslyn != server())
        {
            return new HotReloadOutcome(HotReloadResult.Failed, "The C# language server restarted since the program was launched: restart to apply changes");
        }
        HotReloadStateResponse state;
        try
        {
            state = await debug.HotReloadStateAsync();
        }
        catch (DapException failed)
        {
            return new HotReloadOutcome(HotReloadResult.Failed, $"The debugger could not say what the program can take: {failed.Reason}");
        }
        if (state.Capabilities is not { } capabilities)
        {
            return new HotReloadOutcome(HotReloadResult.Failed, state.CapabilitiesUnknown ?? "The program's runtime did not say which edits it can take");
        }
        bool paused = debug.State == DebugState.Paused;
        HotReloadEmitted emitted;
        try
        {
            emitted = await roslyn.ExtensionAsync<HotReloadEmitted>(HotReloadHandlers.Emit, new HotReloadEmit(session, paused, capabilities,
                [.. state.Modules.Where(m => m.Editable).Select(m => m.Mvid)],
                [.. state.ActiveStatements.Select(s => new HotReloadActiveStatement(s.Module, s.Token, s.Version, s.ILOffset, s.Document,
                    s.StartLine, s.StartColumn, s.EndLine, s.EndColumn, s.Flags))],
                [.. state.Modules.Where(m => !m.Editable).Select(m => new HotReloadLockedModule(m.Mvid, m.WhyNot ?? $"{m.Name} cannot be edited"))]));
        }
        catch (LanguageServerFailed failed)
        {
            return new HotReloadOutcome(HotReloadResult.Failed, $"Hot Reload failed in the C# language server: {failed.Reason}");
        }
        if (emitted.Problem is { } problem)
        {
            return new HotReloadOutcome(HotReloadResult.Failed, $"Hot Reload failed: {problem}");
        }
        List<Problem> problems = [.. emitted.Problems.Select(ToProblem)];
        switch (emitted.Status)
        {
            case "none":
                Changed = false;
                return new HotReloadOutcome(HotReloadResult.NothingToApply, "No managed code changes to apply.")
                {
                    Problems = problems,
                    Files = emitted.ChangedDocuments,
                    Took = took.Elapsed,
                };
            case "blocked":
                bool compiles = !emitted.Problems.Any(p => p.Error && !p.RestartRequired);
                return new HotReloadOutcome(compiles ? HotReloadResult.RestartNeeded : HotReloadResult.Errors,
                    compiles ? $"Restart is needed to apply the changes: {Describe(problems.Where(p => p.Severity == ProblemSeverity.Error))}"
                        : $"Unable to apply changes due to compilation errors: {Describe(problems.Where(p => p.Severity == ProblemSeverity.Error))}")
                {
                    Problems = problems,
                    Took = took.Elapsed,
                };
        }
        ApplyChangesResponse applied;
        try
        {
            applied = await debug.ApplyChangesAsync([.. emitted.Updates.Select(u => new DapModuleUpdate(u.Module, u.Name, u.Metadata, u.IL, u.Pdb,
                u.UpdatedMethods, u.UpdatedTypes,
                [.. u.Lines.Select(l => new DapLineUpdates(l.Document, [.. l.Updates.Select(x => new DapLineUpdate(x.OldLine, x.NewLine))]))],
                [.. u.ActiveStatements.Select(a => new DapStatementUpdate(a.Token, a.Version, a.ILOffset, a.StartLine, a.StartColumn, a.EndLine, a.EndColumn))]))]);
        }
        catch (DapException failed)
        {
            await TellAsync(roslyn, HotReloadHandlers.Discard, session);
            return new HotReloadOutcome(HotReloadResult.Failed, $"The debugger could not apply the changes: {failed.Reason}") { Problems = problems, Took = took.Elapsed };
        }
        if (applied.Modules.FirstOrDefault(m => !m.Applied) is { } refused)
        {
            await TellAsync(roslyn, HotReloadHandlers.Discard, session);
            stopped = refused.Message ?? $"The runtime refused the update to {refused.Name}: restart to apply changes";
            return new HotReloadOutcome(HotReloadResult.Failed, stopped) { Problems = problems, Took = took.Elapsed };
        }
        await TellAsync(roslyn, HotReloadHandlers.Commit, session);
        Changed = false;
        int methods = emitted.Updates.Sum(u => u.UpdatedMethods.Count);
        List<string> files = [.. emitted.ChangedDocuments];
        string named = files.Count == 0 ? string.Empty : $": {string.Join(", ", files.Select(Path.GetFileName))}";
        return new HotReloadOutcome(HotReloadResult.Applied,
            $"C# changes applied in {took.ElapsedMilliseconds} ms{named} ({(methods == 1 ? "1 method" : $"{methods} methods")} updated)")
        {
            Problems = problems,
            Files = files,
            Modules = [.. emitted.Updates.Select(u => u.Name)],
            Methods = methods,
            Took = took.Elapsed,
        };
    }

    /// <summary>Ends Roslyn's session with the debugging session's end.</summary>
    public void End()
    {
        if (id is int session && startedOn is { } roslyn)
        {
            id = null;
            _ = TellAsync(roslyn, HotReloadHandlers.End, session);
        }
    }

    private static async Task TellAsync(RoslynSession roslyn, string handler, int session)
    {
        try
        {
            HotReloadDone done = await roslyn.ExtensionAsync<HotReloadDone>(handler, new HotReloadSessionMessage(session));
            if (done.Problem is { } problem)
            {
                IdeLog.Write(IdeLog.Debugger, $"Hot Reload {handler}: {problem}");
            }
        }
        catch (LanguageServerFailed failed)
        {
            IdeLog.Write(IdeLog.Debugger, $"Hot Reload {handler}: {failed.Reason}");
        }
    }

    /// <summary>The first edits named, each with its file and line, as the status bar says them.</summary>
    private static string Describe(IEnumerable<Problem> problems)
    {
        List<Problem> named = [.. problems];
        string first = string.Join("; ", named.Take(2).Select(p => $"{p.Message} ({p.Where})"));
        return named.Count > 2 ? $"{first}; and {named.Count - 2} more" : first;
    }

    /// <summary>An edit Roslyn could not take, as Problems lists it: its id (ENC0020) the code, its place from zero.</summary>
    private static Problem ToProblem(HotReloadProblem problem) => new(
        problem.Error ? ProblemSeverity.Error : ProblemSeverity.Warning,
        problem.Message,
        problem.Id,
        problem.Path is { } path ? PathNames.Canonical(path) : null,
        new TextPosition(problem.StartLine, problem.StartColumn),
        problem.Project);
}
