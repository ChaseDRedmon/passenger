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

    /// <summary>Asks whether to make and trust the development certificate for a run's HTTPS services, or run them over HTTP;
    /// an agent's own IDE (--mcp) has nobody to ask, and runs them over HTTP.</summary>
    private async Task<bool> AskCertificateAsync(string configuration, string services, bool missing)
    {
        if (!AgentsAllowed)
        {
            return false;
        }
        CertificateQuestionDialog question = new(configuration, services, missing);
        CertificateQuestion = question;
        try
        {
            return await question.ShowDialog<bool>(this);
        }
        finally
        {
            if (CertificateQuestion == question)
            {
                CertificateQuestion = null;
            }
        }
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
    /// the run does, held again and attached to again where it was debugged. Null once done, else why not.</summary>
    public async Task<string?> ContainerActAsync(string id, string action, ContainerRow row)
    {
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
