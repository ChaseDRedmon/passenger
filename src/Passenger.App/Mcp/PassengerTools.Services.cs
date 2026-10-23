using Passenger.App.Debugging;

namespace Passenger.App.Mcp;

/// <summary>An agent's <c>resource</c> names a service of a run of services, an Aspire AppHost's or the container run's (a
/// compose service): run, debug_start, debug_attach, profile and stop act on a compose service through the run of services
/// as the Containers window's rows do, as on an Aspire resource as the Aspire window's do.</summary>
public sealed partial class PassengerTools
{
    /// <summary>The container run's .NET service an agent's resource names, which its tools act on through the run of
    /// services; null when it names none (an Aspire resource then).</summary>
    private string? ComposeService(string resource) => Builds.Containers is not null && Builds.ServiceOfRun(resource).Service is { } service ? service.Name : null;

    /// <summary>One of a compose service's actions through the run of services, refused saying why.</summary>
    private async Task ComposeServiceActionAsync(string action, string service)
    {
        if (await window.ServiceActionAsync(action, service) is { } refused)
        {
            throw new InvalidOperationException(refused);
        }
    }

    /// <summary>debug_start's and debug_attach's compose service: debugged from its start (restarted held), or attached to as
    /// it runs, through the run of services; where it is once it stops, ends, or waitSeconds pass.</summary>
    private async Task<DebugSnapshot> DebugComposeServiceAsync(string service, bool attach, int waitSeconds, CancellationToken cancel)
    {
        AgentCall.Follow(window.Actions.Work, attach ? AppActions.ServiceAttach : AppActions.ServiceDebug);
        await ComposeServiceActionAsync(attach ? AppActions.ServiceAttach : AppActions.ServiceDebug, service);
        DebugSession debugged = Session.Debug.SessionNamed(service) ?? throw new InvalidOperationException($"{service} has no debugging session");
        return await AfterResume(TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 1, 120)), cancel, debugged);
    }
}
