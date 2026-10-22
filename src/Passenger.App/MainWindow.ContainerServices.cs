using Passenger.App.Containers;
using Passenger.App.Run;

namespace Passenger.App;

/// <summary>A .NET service of the container run under way, acted on without taking the run down: from the Containers
/// window's service and container rows and from the gutter's run icon, Debug restarts it held and attaches (kept for the
/// run's restarts and Rerun), Attach attaches as it runs, Run without Debugger lets its session go, and Profile records it
/// in its container as Attach Profiler does; the window's Restart, Stop and Remove of one, and an agent's, go through the
/// run, its session ended first and, restarted, attached again.</summary>
public partial class MainWindow
{
    /// <summary>The dotnet a container run's <c>dev-certs</c> runs through: the machine's, unless a test gives a stand-in.</summary>
    public DotnetWords? DevCertificates { get; init; }

    /// <summary>The question a container run asked last of its HTTPS development certificate, for the tests.</summary>
    public CertificateQuestionDialog? CertificateQuestion { get; private set; }

    /// <summary>The question a container run asked last of containers it would replace, for the tests.</summary>
    public ReplaceContainersDialog? ReplaceQuestion { get; private set; }

    /// <summary>Whether this is an agent's own IDE (<c>--mcp</c>): nobody is at it, so what would ask the person (a container
    /// run's certificate, containers it would replace) takes the way that asks nothing and says so.</summary>
    public bool AgentsOwn { get; init; }

    /// <summary>Asks whether to make and trust the development certificate for a run's HTTPS services, or run them over HTTP;
    /// a start stopped meanwhile closes the question, answered Run over HTTP.</summary>
    private async Task<bool> AskCertificateAsync(string configuration, string services, bool missing, CancellationToken stop)
    {
        CertificateQuestionDialog question = new(configuration, services, missing);
        CertificateQuestion = question;
        try
        {
            return await AskAsync(question, stop);
        }
        finally
        {
            if (CertificateQuestion == question)
            {
                CertificateQuestion = null;
            }
        }
    }

    /// <summary>Asks whether a container run may replace containers of its Compose project Passenger did not make; a start
    /// stopped meanwhile closes the question, answered Cancel.</summary>
    private async Task<bool> AskReplaceAsync(string configuration, string project, IReadOnlyList<string> containers, CancellationToken stop)
    {
        ReplaceContainersDialog question = new(configuration, project, containers);
        ReplaceQuestion = question;
        try
        {
            return await AskAsync(question, stop);
        }
        finally
        {
            if (ReplaceQuestion == question)
            {
                ReplaceQuestion = null;
            }
        }
    }

    /// <summary>A yes-or-no question's answer, the question closed unanswered (no) once <paramref name="stop"/> is canceled.</summary>
    private async Task<bool> AskAsync(Avalonia.Controls.Window question, CancellationToken stop)
    {
        using CancellationTokenRegistration closing = stop.Register(() => UiThread.Post(() =>
        {
            if (question.IsVisible)
            {
                question.Close(false);
            }
        }));
        return await question.ShowDialog<bool>(this);
    }

    private void AddServiceActions(ContainersView view)
    {
        foreach ((string id, string title) in (ReadOnlySpan<(string, string)>)[
            (AppActions.ServiceDebug, "Debug Service"),
            (AppActions.ServiceAttach, "Attach to Service"),
            (AppActions.ServiceRun, "Run Service without Debugger"),
            (AppActions.ServiceProfile, "Profile Service")])
        {
            actions!.Add(new IdeAction(id, title, "Containers", context => _ = ServiceActionAsync(id, ServiceOfRow(view.Chosen)))
            {
                WhyNot = _ => buildRun.ServiceOfRun(ServiceOfRow(view.Chosen)).WhyNot,
                Works = id,
                Words = ["Debug container service", "Profile container service"],
            });
        }
    }

    /// <summary>The service of the container run a row of the Containers window stands for (a service of its Compose
    /// project, or one of its containers), or null.</summary>
    private string? ServiceOfRow(ContainerRow? row)
    {
        if (buildRun.Containers is not { } run || row is null)
        {
            return null;
        }
        (string? project, string? service) = row.Project is { } compose ? (compose.Name, row.Service) : (row.Container?.Project, row.Container?.Service);
        return project == run.Plan.ProjectName ? service : null;
    }

    /// <summary>One of a service's actions on the container run under way: null once done, else why not (said too).</summary>
    public Task<string?> ServiceActionAsync(string id, string? service)
    {
        if (buildRun.ServiceOfRun(service) is not ({ } running, null))
        {
            string why = buildRun.ServiceOfRun(service).WhyNot!;
            status.Text = why;
            return Task.FromResult<string?>(why);
        }
        return id switch
        {
            AppActions.ServiceDebug => buildRun.DebugServiceAsync(running.Service),
            AppActions.ServiceAttach => buildRun.AttachServiceAsync(running.Service),
            AppActions.ServiceRun => buildRun.RunServiceAsync(running.Service),
            _ => ProfileServiceAsync(running),
        };

        async Task<string?> ProfileServiceAsync(DotnetService profiled)
        {
            string? refused = await profiler!.AttachServiceAsync(profiled, buildRun.Containers!.Plan.Name);
            if (refused is not null)
            {
                status.Text = refused;
            }
            return refused;
        }
    }

    /// <summary>A Containers window action on a row, through the container run when it is one of its .NET services: its
    /// session (and the profiler's recording) ended before Stop or Remove takes its container, and Restart restarting it as
    /// the run does, held again and attached to again where it was debugged; and on the run's own project, its Restart, Stop
    /// and Remove the run's (<see cref="BuildRunController.ProjectActionAsync"/>). Null once done, else why not.</summary>
    public async Task<string?> ContainerActAsync(string id, string action, ContainerRow row)
    {
        // The run's own project: its Restart, Stop and Remove are the run's, its sessions ended first.
        if (row is { Kind: ContainerRowKind.Project, Project: { } project } && buildRun.Containers?.Plan.ProjectName == project.Name && action is "restart" or "stop" or "remove")
        {
            return await buildRun.ProjectActionAsync(action);
        }
        if (ServiceOfRow(row) is { } service && buildRun.ServiceOfRun(service).Service is not null)
        {
            if (action == "restart")
            {
                return await buildRun.RestartServiceAsync(service);
            }
            if (action is "stop" or "remove")
            {
                await buildRun.EndServiceSessionsAsync(service);
            }
        }
        return await containersView!.ActAsync(id, action, row);
    }
}
