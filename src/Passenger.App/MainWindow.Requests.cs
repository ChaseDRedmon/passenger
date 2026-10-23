using System.Text.RegularExpressions;
using Passenger.App.Endpoints;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Profiler;
using Passenger.Roslyn.Extension;
using Passenger.Text;

namespace Passenger.App;

/// <summary>The Requests window (#418): the live requests of the web app Run runs, else the program the chosen Debug tab
/// debugs, and the database commands each issued, heard from ASP.NET Core's and EF Core's own diagnostic events at the
/// program's diagnostic port (<see cref="LiveRequests"/>), with no change to the app. The app is followedRequests only while the
/// window shows, from the moment it does or the program starts, and let go as it hides or the program ends. A request opens
/// at its route's handler (the folder's routes, <see cref="ProjectSession.Routes"/>), or where its exception was thrown; a
/// command's text opens in a read-only tab.</summary>
public partial class MainWindow
{
    private RequestsView? requestsView;
    private RequestLog? requestLog;
    private LiveRequests? followedRequests;
    private int followedProcess;
    private int follows;

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

    /// <summary>A session's run or debugging began or ended: the app followedRequests follows it.</summary>
    private void BindRequests(ProjectSession next)
    {
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
        _ = FollowRequestsAsync();
    }

    /// <summary>The program whose requests are followedRequests now: Run's, else the chosen Debug tab's, while it runs here.</summary>
    private async Task<(int Process, string Name)?> RequestTargetAsync()
    {
        if (session.Runner.IsRunning && session.Runner.ProcessId is int run && buildRun.RunPlan is { } plan)
        {
            return (run, plan.Name);
        }
        if (session.Debug.Session is { IsLocal: true, State: not Debugging.DebugState.Ended } debugged && debugged.ProcessStarted.IsCompletedSuccessfully)
        {
            return (await debugged.ProcessStarted, session.Debug.Chosen?.Name ?? "the program debugged");
        }
        return null;
    }

    /// <summary>Follows the program due (none while the window is hidden), letting go of the one before; a program whose
    /// runtime has yet to open its diagnostic port is asked again for a few seconds.</summary>
    private async Task FollowRequestsAsync()
    {
        UiThread.Check();
        int follow = ++follows;
        (int Process, string Name)? target = requestsView is { IsShown: true } ? await RequestTargetAsync() : null;
        if (follow != follows || (target?.Process ?? 0) == followedProcess && (followedRequests is not null || target is null))
        {
            return;
        }
        await LetGoOfRequestsAsync();
        if (follow != follows || target is not { } program)
        {
            Requests.Following = requestsView is { IsShown: true } ? "Nothing runs: Run or Debug a web app to see its requests" : string.Empty;
            return;
        }
        Requests.Following = $"Following {program.Name}…";
        RequestLog log = RequestLog;
        using IDisposable held = BackgroundWork.Scope();
        for (int attempt = 0; attempt < 30 && follow == follows; attempt++)
        {
            try
            {
                LiveRequests live = await LiveRequests.AttachAsync(program.Process, log.Hear);
                if (follow != follows)
                {
                    await live.DisposeAsync();
                    return;
                }
                followedRequests = live;
                followedProcess = program.Process;
                Requests.Following = $"Following {program.Name} (process {program.Process})";
                _ = live.Ended.ContinueWith(_ => UiThread.Post(() =>
                {
                    if (followedRequests == live)
                    {
                        followedRequests = null;
                        followedProcess = 0;
                        Requests.Following = $"{program.Name} ended";
                    }
                }), TaskScheduler.Default);
                return;
            }
            catch (ProfilingException failed)
            {
                Requests.Following = failed.Message;
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }
    }

    private async Task LetGoOfRequestsAsync()
    {
        if (followedRequests is { } live)
        {
            followedRequests = null;
            followedProcess = 0;
            await live.DisposeAsync();
        }
    }

    /// <summary>The folder's route a request's method and path reach, as ASP.NET Core's matcher would: the first whose
    /// template fits, a literal one before one with parameters.</summary>
    private WebRoute? RouteReached(string method, string path) =>
        session.Routes.All.Where(r => r.Kind != RouteKinds.Grpc && (r.Methods.Count == 0 || r.Methods.Contains(method, StringComparer.OrdinalIgnoreCase))
                && RouteRequests.Fits(r.Template, path))
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
    private void OpenCommand(LiveCommand command) =>
        OpenSql($"command-{(command.Id.Length > 0 ? command.Id : Guid.NewGuid().ToString("N"))}", command.Text, "The command");

    /// <summary>An EF Core query's SQL the debugger read (#420) in a read-only tab, as a command's opens: one copy for each
    /// text, so the same SQL opened again shows the tab it has.</summary>
    private void OpenQuerySql(string sql) =>
        OpenSql($"query-{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sql)))[..16]}", sql, "The query's SQL");

    /// <summary>SQL opened whole in a read-only tab, from a copy under the state's cache (<see cref="RequestLog.IsCommandCopy"/>).</summary>
    private void OpenSql(string name, string text, string what)
    {
        string folder = Path.Combine(StateFolder, "cache", RequestLog.CommandCopies);
        string path = Path.Combine(folder, $"{name}.sql");
        try
        {
            Directory.CreateDirectory(folder);
            AtomicFile.Write(path, System.Text.Encoding.UTF8.GetBytes(text));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            status.Text = $"{what} could not be opened: {error.Message}";
            return;
        }
        OpenFile(path);
    }
}
