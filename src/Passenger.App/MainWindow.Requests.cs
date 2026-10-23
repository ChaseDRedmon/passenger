using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Passenger.App.Endpoints;
using Passenger.App.Run;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Profiler;
using Passenger.Roslyn.Extension;
using Passenger.Text;

namespace Passenger.App;

/// <summary>The Requests window (#418): the live requests of the web apps running from the folder and the database commands
/// each issued, heard from ASP.NET Core's and EF Core's own diagnostic events at each program's diagnostic port
/// (<see cref="LiveRequests"/>), with no change to the apps: Run's program, Profile's, each Debug tab's on this machine, and
/// each .NET service of a container run (through Passenger's tools in its container) or an AppHost's run (on this machine).
/// They are followed only while the window shows, from the moment it does or a program starts, and let go as it hides or
/// the program ends. A request opens at its route's handler (the folder's routes, <see cref="ProjectSession.Routes"/>), or
/// where its exception was thrown; a command's text opens in a read-only tab.</summary>
public partial class MainWindow
{
    private RequestsView? requestsView;
    private RequestLog? requestLog;
    private readonly Dictionary<string, (RequestTarget Target, LiveRequests Live)> followedRequests = new(StringComparer.Ordinal);
    private bool followingRequests;
    private bool followRequestsAgain;

    /// <summary>Whether an agent has asked for the requests: from then on they are followed as while the window shows, which a
    /// windowless IDE's never does.</summary>
    private bool requestsAsked;

    /// <summary>Whether someone looks at the requests: the window shows, or an agent asked for them.</summary>
    private bool RequestsLooked => requestsView is { IsShown: true } || requestsAsked;

    /// <summary>A program whose requests are followed: what it is known by, what the window calls it, its project when known,
    /// its process id where it runs, and where that is when not on this machine (a container's tools).</summary>
    private sealed record RequestTarget(string Key, string Name, string? Project, int Process, ProfilePlace? Place);

    /// <summary>The Requests window.</summary>
    public RequestsView Requests => requestsView!;

    /// <summary>The live requests heard.</summary>
    public RequestLog RequestLog => requestLog!;

    private void AddRequests()
    {
        RequestLog log = new(RouteReached);
        RequestsView view = new(log);
        requestLog = log;
        requestsView = view;
        view.ShownChanged += (_, shown) =>
        {
            // The routes the requests reach are read while the window shows.
            if (shown)
            {
                WantRoutes();
            }
            _ = FollowRequestsAsync();
        };
        view.RequestOpened += (_, request) => OpenRequest(request);
        view.CommandOpened += (_, command) => OpenCommand(command);
        Dock.Add(new Docking.ToolWindow(ToolWindowIds.Requests, "Requests", "Icon.Endpoints", view, Docking.ToolAnchor.BottomRight)
        {
            Action = AppActions.ShowRequests,
            Menus = [new MenuPlace("Tools", 0, 4)],
        });
        Closed += (_, _) => _ = LetGoOfRequestsAsync();
    }

    /// <summary>A session's runs, debugging and profiling began or ended: the programs followed follow them.</summary>
    private void BindRequests(ProjectSession next)
    {
        // Another folder: an agent asks again for its requests.
        requestsAsked = false;
        buildRun.RunningChanged += (_, _) =>
        {
            if (session == next)
            {
                _ = FollowRequestsAsync();
            }
        };
        next.Debug.StateChanged += (_, _) =>
        {
            if (session == next)
            {
                _ = FollowRequestsAsync();
            }
        };
        next.ProfiledChanged += (_, _) =>
        {
            if (session == next)
            {
                _ = FollowRequestsAsync();
            }
        };
        _ = FollowRequestsAsync();
    }

    /// <summary>The programs whose requests are followed now: Run's, Profile's, each live Debug tab's on this machine, and
    /// each started .NET service of the runs of services under way; one program once.</summary>
    private async Task<IReadOnlyList<RequestTarget>> RequestTargetsAsync()
    {
        List<RequestTarget> targets = [];
        if (session.Runner.IsRunning && session.Runner.ProcessId is int run && buildRun.RunPlan is { } plan)
        {
            targets.Add(new RequestTarget("run", plan.Name, plan.Project, run, null));
        }
        if (session.Profiled is { Run.Place: null } profiled && !profiled.Landed.IsCompleted)
        {
            targets.Add(new RequestTarget("profile", profiled.Name, null, profiled.Run.ProcessId, null));
        }
        foreach (ToolWindows.DebugTab tab in session.Debug.Tabs.Where(t => t.IsLive && t.Session is { IsLocal: true } debugged && debugged.ProcessStarted.IsCompletedSuccessfully))
        {
            targets.Add(new RequestTarget($"debug:{tab.Name}", tab.Name, null, await tab.Session!.ProcessStarted, null));
        }
        foreach (ServiceRun services in buildRun.ServiceRuns.Where(r => !r.IsStopped).ToArray())
        {
            foreach (IRunService service in services.Services.Where(s => s.Starts && !services.LeftUnstarted(s)).ToArray())
            {
                try
                {
                    if (await services.ProgramAsync(service, Task.CompletedTask, CancellationToken.None) is { WhyNot: null } program)
                    {
                        targets.Add(new RequestTarget($"{services.Name}:{service.Name}", services.NameOf(service), service.Project, program.Id, program.Recorded));
                    }
                }
                catch (Exception error) when (error is ServiceUnreachedException || services.Refused(error))
                {
                    // A service not yet started, or one its tools cannot reach, is followed once it can be.
                }
            }
        }
        return [.. targets.DistinctBy(t => t.Place is null ? $"process {t.Process}" : t.Key)];
    }

    /// <summary>Follows the programs due (none while the window is hidden), letting go of those no longer due, one pass at a
    /// time: a change during a pass makes another. A program whose runtime has yet to open its diagnostic port is asked
    /// again for a few seconds.</summary>
    private async Task FollowRequestsAsync()
    {
        UiThread.Check();
        if (followingRequests)
        {
            followRequestsAgain = true;
            return;
        }
        followingRequests = true;
        using IDisposable held = BackgroundWork.Scope();
        try
        {
            do
            {
                followRequestsAgain = false;
                IReadOnlyList<RequestTarget> due = RequestsLooked ? await RequestTargetsAsync() : [];
                foreach ((string key, (RequestTarget target, LiveRequests live)) in followedRequests.ToArray())
                {
                    if (!due.Contains(target))
                    {
                        followedRequests.Remove(key);
                        await live.DisposeAsync();
                    }
                }
                foreach (RequestTarget target in due.Where(t => !followedRequests.ContainsKey(t.Key)))
                {
                    await FollowAsync(target);
                }
                SayFollowed(due.Count == 0 ? null : string.Empty);
            }
            while (followRequestsAgain);
        }
        finally
        {
            followingRequests = false;
        }
    }

    /// <summary>One program followed, asked again while its runtime has yet to open its port; why not said.</summary>
    private async Task FollowAsync(RequestTarget target)
    {
        RequestLog log = RequestLog;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                LiveRequests live = target.Place is { } place
                    ? await LiveRequests.AttachAsync(target.Process, place, told => log.Hear(told, target.Name, target.Project))
                    : await LiveRequests.AttachAsync(target.Process, told => log.Hear(told, target.Name, target.Project));
                followedRequests[target.Key] = (target, live);
                _ = live.Ended.ContinueWith(_ => UiThread.Post(() =>
                {
                    if (followedRequests.TryGetValue(target.Key, out (RequestTarget Target, LiveRequests Live) now) && now.Live == live)
                    {
                        followedRequests.Remove(target.Key);
                        SayFollowed($"{target.Name} ended");
                    }
                }), TaskScheduler.Default);
                return;
            }
            catch (ProfilingException failed)
            {
                Requests.Following = failed.Message;
                if (target.Place is not null)
                {
                    // Tools that answered have said why: asking again would only say it again.
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }
    }

    /// <summary>The requests heard, for an agent: the window shown first (which follows the running apps), the last ones
    /// as lines, each request's method, path, status, time, route and handler, then its commands, repeated and slow ones
    /// marked; what the window follows heads them.</summary>
    public async Task<string> RequestsForAgentAsync(int most, CancellationToken cancel)
    {
        UiThread.Check();
        requestsAsked = true;
        ShowToolWindow(ToolWindowIds.Requests);
        // The routes the requests reached, read now where the language server is ready, later as it is.
        WantRoutes();
        if (session.CSharp is { IsReady: true })
        {
            await RoutesAsync(cancel);
            RequestLog.Reroute();
        }
        // The programs due are followed before the answer, so a request sent after it is heard.
        while (followingRequests)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancel);
        }
        await FollowRequestsAsync();
        StringBuilder said = new(Requests.Following);
        foreach (LiveRequest request in RequestLog.Requests.TakeLast(Math.Max(1, most)))
        {
            said.Append('\n').Append(request.Method).Append(' ').Append(request.Path).Append(request.Query)
                .Append(request.Status is { } status ? $" {status.ToString(CultureInfo.InvariantCulture)}" : " (under way)")
                .Append(request.Milliseconds is { } ms ? string.Create(CultureInfo.InvariantCulture, $" {ms:0} ms") : string.Empty);
            if (request.Route is { } route)
            {
                said.Append(" -> ").Append(RouteRequests.Title(route)).Append(", ").Append(route.Handler)
                    .Append(route.HandlerPath is { } handler ? $" ({PathNames.Shown(handler, session.Folder)}:{(route.HandlerAt?.Line ?? route.At.Line) + 1})" : string.Empty);
            }
            if (request.HasRepeats)
            {
                said.Append(" N+1");
            }
            if (request.Error is { } error)
            {
                said.Append("\n  failed: ").Append(error.Split('\n')[0].Trim());
            }
            foreach (LiveCommand command in request.Commands)
            {
                said.Append("\n  ").Append(string.Join(' ', command.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
                    .Append(string.Create(CultureInfo.InvariantCulture, $" ({command.Milliseconds:0.#} ms"))
                    .Append(command.Rows is { } rows and >= 0 ? $", {rows} rows changed" : command.Reads is { } reads ? $", {reads} reads" : string.Empty)
                    .Append(command.Parameters is { Length: > 0 } parameters ? $", {parameters}" : string.Empty)
                    .Append(command.Repeated ? $", run {command.Runs} times in this request" : string.Empty)
                    .Append(command.Slow ? ", slow" : string.Empty)
                    .Append(command.Error is { } failed ? $", failed: {failed}" : string.Empty).Append(')');
            }
        }
        return said.ToString();
    }

    /// <summary>What the window says it follows; with none, why (<paramref name="none"/>, else that nothing runs).</summary>
    private void SayFollowed(string? none)
    {
        if (followedRequests.Count > 0)
        {
            Requests.Following = "Following " + string.Join(", ", followedRequests.Values.Select(f =>
                f.Target.Place is { } place ? $"{f.Target.Name} in {place.Where} (process {f.Target.Process})" : $"{f.Target.Name} (process {f.Target.Process})"));
        }
        else if (none is { Length: > 0 })
        {
            Requests.Following = none;
        }
        else if (none is null)
        {
            Requests.Following = RequestsLooked ? "Nothing runs: Run, Debug or Profile a web app, or start its containers or AppHost, to see its requests" : string.Empty;
        }
    }

    private async Task LetGoOfRequestsAsync()
    {
        foreach ((RequestTarget _, LiveRequests live) in followedRequests.Values.ToArray())
        {
            await live.DisposeAsync();
        }
        followedRequests.Clear();
    }

    /// <summary>The folder's route a request's method and path reach, as ASP.NET Core's matcher would: of the program's own
    /// project when known, the first whose template fits, a literal one before one with parameters.</summary>
    private WebRoute? RouteReached(string method, string path, string? project) =>
        session.Routes.All.Where(r => r.Kind != RouteKinds.Grpc && (project is null || PathNames.Same(r.Project, project))
                && (r.Methods.Count == 0 || r.Methods.Contains(method, StringComparer.OrdinalIgnoreCase)) && RouteRequests.Fits(r.Template, path))
            .OrderBy(r => r.Template.Count(c => c == '{'))
            .FirstOrDefault();

    /// <summary>A request opened: where the exception that ended it was thrown in the folder's code, else its route's handler.</summary>
    private void OpenRequest(LiveRequest request)
    {
        if (request.Error is { } error && ThrownAt(error) is { } thrown)
        {
            NavigateTo(thrown.Path, new TextPosition(thrown.Line - 1, 0));
            return;
        }
        if (request.Route is { } route)
        {
            NavigateTo(route.HandlerPath ?? route.Path, route.HandlerAt is { } handler ? new Lsp.LspPosition(handler.Line, handler.Character) : new Lsp.LspPosition(route.At.Line, route.At.Character));
            return;
        }
        status.Text = $"{request.Method} {request.Path} reached no route of the folder's code";
    }

    /// <summary>The first frame of an exception's stack in a file of the open folder.</summary>
    private (string Path, int Line)? ThrownAt(string error)
    {
        foreach (Match frame in StackFrame().Matches(error))
        {
            string path = frame.Groups["path"].Value.Trim();
            if (session.Folder is { } folder && File.Exists(path) && PathNames.IsUnder(PathNames.Canonical(path), folder))
            {
                return (PathNames.Canonical(path), int.Parse(frame.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return null;
    }

    [GeneratedRegex(@" in (?<path>.+?):line (?<line>\d+)")]
    private static partial Regex StackFrame();

    /// <summary>A command's text in a read-only tab, from a copy under the state's cache.</summary>
    private void OpenCommand(LiveCommand command)
    {
        string folder = Path.Combine(StateFolder, "cache", RequestLog.CommandCopies);
        string path = Path.Combine(folder, $"command-{(command.Id.Length > 0 ? command.Id : Guid.NewGuid().ToString("N"))}.sql");
        try
        {
            Directory.CreateDirectory(folder);
            // Its parameters, masked, as a comment over it.
            string text = command.Parameters is { Length: > 0 } parameters ? $"-- {parameters}\n{command.Text}" : command.Text;
            AtomicFile.Write(path, Encoding.UTF8.GetBytes(text));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            status.Text = $"The command could not be opened: {error.Message}";
            return;
        }
        OpenFile(path);
    }
}
