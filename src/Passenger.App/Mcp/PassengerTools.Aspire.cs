using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Passenger.App.Aspire;
using Passenger.App.Debugging;
using Passenger.App.Run;
using Passenger.App.Workspace;

namespace Passenger.App.Mcp;

public sealed partial class PassengerTools
{
    private async Task<(AspireSelection Selection, bool Started)> PrepareAspireResource(string resource, string? configuration,
        RunStart? mode, string? arguments, CancellationToken cancel)
    {
        ProjectSession current = Session;
        RunConfiguration? explicitConfiguration = configuration is null ? null
            : current.Configurations.All.FirstOrDefault(candidate => candidate.Name.Equals(configuration, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"No run configuration '{configuration}'");
        if (explicitConfiguration is not null && !current.Aspire.IsAspire(explicitConfiguration))
        {
            throw new ArgumentException("resource requires an Aspire configuration");
        }
        AspireSelection? selected = await window.ResolveOwnedAspireResourceAsync(resource, explicitConfiguration, cancel, allowMissing: mode is not null);
        CheckWorkspace();
        if (selected is not null)
        {
            if (arguments is not null)
            {
                throw new ArgumentException("A running resource uses its AppHost's arguments; omit arguments");
            }
            return (selected, false);
        }
        RunConfiguration chosen = explicitConfiguration ?? current.Configurations.Chosen
            ?? throw new InvalidOperationException("Select or name an Aspire configuration before starting a resource");
        bool started = await window.StartAspireToolResourceAsync(resource, chosen, arguments, mode!.Value, cancel).WaitAsync(cancel);
        CheckWorkspace();
        if (!started)
        {
            throw new InvalidOperationException("The selected AppHost is running but does not expose that resource; workspace lists targets");
        }
        DateTime until = DateTime.UtcNow.AddSeconds(60);
        do
        {
            selected = await window.ResolveOwnedAspireResourceAsync(resource, chosen, cancel, allowMissing: true);
            CheckWorkspace();
            if (selected is not null)
            {
                return (selected, true);
            }
            await Task.Delay(100, cancel);
        } while (DateTime.UtcNow < until);
        throw new InvalidOperationException("The AppHost started but the requested resource was not reported within 60 seconds");

        void CheckWorkspace()
        {
            if (Session != current || current.IsEnded)
            {
                throw new InvalidOperationException("The workspace changed");
            }
        }
    }

    private async Task<DebugSnapshot> DebugAspireTarget(string resource, string? configuration, string? arguments,
        bool attach, int waitSeconds, CancellationToken cancel)
    {
        ProjectSession current = Session;
        AgentCall.Follow(window.Actions.Work, AppActions.Debug, AppActions.RunProgram, AppActions.Build);
        using LaunchBuild built = new(Builds);
        (AspireSelection selection, bool started) = await PrepareAspireResource(resource, configuration,
            attach ? null : RunStart.Debug, arguments, cancel);
        DebugSession? previous = current.Aspire.FindService(selection)?.DebugTab?.Session;
        if (!started && await window.RunAspireToolResourceAsync(selection, attach ? "attach" : "debug", cancel) is { } problem)
        {
            throw new InvalidOperationException(WithBuildErrors(problem, built.Ended));
        }
        DebugSession? debugged = null;
        DateTime until = DateTime.UtcNow.AddSeconds(Math.Clamp(waitSeconds, 1, 120));
        do
        {
            if (Session != current || current.IsEnded)
            {
                break;
            }
            AspireSelection? fresh = await window.ResolveOwnedAspireResourceAsync($"{selection.AppHostPath}::{selection.Resource.Name}", null, cancel, allowMissing: true);
            if (Session != current || current.IsEnded)
            {
                break;
            }
            debugged = fresh is null ? null : current.Aspire.FindService(fresh)?.DebugTab?.Session;
            if (debugged is not null && (attach || started || debugged != previous))
            {
                break;
            }
            debugged = null;
            await Task.Delay(100, cancel);
        } while (DateTime.UtcNow < until);
        if (Session != current || current.IsEnded || debugged is null)
        {
            throw new InvalidOperationException("The requested resource did not establish a debug session in this workspace");
        }
        DebugSnapshot snapshot = await AfterResume(until - DateTime.UtcNow, cancel, debugged);
        if (Session != current || current.IsEnded)
        {
            throw new InvalidOperationException("The workspace changed while reading the resource's debug session");
        }
        return snapshot;
    }

    private async Task<Workspace> AspireWorkspace(Workspace snapshot, CancellationToken cancel)
    {
        ProjectSession current = Session;
        List<AspireOwnedAppInfo> apps = [];
        foreach (AspireAppRun app in current.Aspire.Apps.Where(app => !app.IsTest && !app.Stopping).ToArray())
        {
            IReadOnlyList<AspireResource> resources = [];
            string state = app.Interactions is null ? "Starting" : "Running";
            try
            {
                resources = app.Interactions is { } connection ? await connection.GetResourcesAsync(cancel) : [];
            }
            catch (Exception unread) when (unread is IOException or TimeoutException)
            {
                // The rest of the workspace still answers: this AppHost's state is why its resources cannot be read.
                state = unread.Message;
            }
            if (Session != current || current.IsEnded || current.Aspire.FindApp(app.Configuration.Project) != app || app.Stopping)
            {
                throw new InvalidOperationException("The workspace or owned AppHost changed");
            }
            apps.Add(new AspireOwnedAppInfo(app.Configuration.Project, app.ProcessId ?? 0, state, app.Group,
                [.. resources.Select(resource => ResourceInfo(app.Configuration.Project, resource))]));
        }
        return snapshot with { AspireApps = apps };
    }

    private static AspireResourceInfo ResourceInfo(string path, AspireResource resource) => new(resource.Name, resource.Generation,
        resource.DisplayName, AspireJson.String(resource.Data, "resourceType"), resource.State, resource.HealthStatus,
        [.. AspireJson.Array(resource.Data, "urls").Select(url => JsonSerializer.SerializeToElement(new
        {
            name = AspireJson.String(url, "name"),
            url = SafeUrl(AspireJson.String(url, "url")),
        }))], AspireJson.Array(resource.Data, "relationships"),
        [.. resource.Commands.Where(command => command.Enabled).Select(command => command.Name)]) { Target = $"{path}::{resource.Name}" };

    private static string? SafeUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https"
        ? new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty, Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri : null;
}

public sealed record AspireAppInfo(string AppHostPath, int ProcessId, string State, bool Owned, string? Group);
public sealed record AspireResourceInfo(string Name, string Generation, string DisplayName, string? Type, string? State,
    string? Health, IReadOnlyList<JsonElement> Urls, IReadOnlyList<JsonElement> Relationships, IReadOnlyList<string> Commands)
{
    public string? Target { get; init; }
}
public sealed record AspireOwnedAppInfo(string AppHostPath, int ProcessId, string State, string Group, IReadOnlyList<AspireResourceInfo> Resources);
