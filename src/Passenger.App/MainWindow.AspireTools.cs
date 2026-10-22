using Passenger.App.Aspire;
using Passenger.App.Run;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Roslyn.Extension;

namespace Passenger.App;

public partial class MainWindow
{
    private readonly Dictionary<AspireAppRun, (string Name, AspireResourceRun Service)> startingAspireToolTargets = [];

    internal async Task<AspireSelection?> ResolveOwnedAspireResourceAsync(string target, RunConfiguration? configuration, CancellationToken cancel, bool allowMissing = false)
    {
        ProjectSession current = session;
        (string? path, string name) = AspireToolTarget(target);
        bool qualified = path is not null;
        if (configuration is not null && path is not null && !PathNames.Same(path, configuration.Project))
        {
            throw new ArgumentException("The resource qualifier and configuration name different AppHosts");
        }
        path ??= configuration?.Project;
        AspireAppRun[] apps = current.Aspire.Apps.Where(app => !app.IsTest && !app.Stopping
            && (path is null || PathNames.Same(app.Configuration.Project, path))).ToArray();
        List<AspireSelection> matches = [];
        foreach (AspireAppRun app in apps)
        {
            IReadOnlyList<AspireResource> resources = await (app.Interactions
                ?? throw new InvalidOperationException("The AppHost resource connection is still starting")).GetResourcesAsync(cancel);
            CheckAspireToolTarget(current, app);
            matches.AddRange(resources.Where(resource => resource.Name == name || !qualified && resource.DisplayName == name)
                .Select(resource => new AspireSelection(app.Configuration.Project, resource)));
            if (allowMissing && configuration is not null && startingAspireToolTargets.TryGetValue(app, out var starting)
                && starting.Name == name)
            {
                matches.RemoveAll(selection => PathNames.Same(selection.AppHostPath, app.Configuration.Project));
                AspireSelection[] owned = resources.Select(resource => new AspireSelection(app.Configuration.Project, resource))
                    .Where(selection => current.Aspire.FindService(selection) == starting.Service).ToArray();
                if (owned.Length > 1)
                {
                    throw new InvalidOperationException("The proven AppHost project identifies more than one resource");
                }
                if (owned.Length == 1)
                {
                    matches.Add(owned[0]);
                    startingAspireToolTargets.Remove(app);
                }
            }
        }
        return matches.Count switch
        {
            1 => matches[0],
            0 when allowMissing => null,
            0 => throw new InvalidOperationException($"No owned Aspire resource matches '{target}'; workspace lists owned AppHosts and resources"),
            _ => throw new InvalidOperationException($"Resource '{name}' is ambiguous; use absoluteAppHostPath::resourceName: "
                + string.Join(", ", matches.Select(match => $"{match.AppHostPath}::{match.Resource.Name}"))),
        };
    }

    internal async Task<bool> StartAspireToolResourceAsync(string target, RunConfiguration configuration,
        string? arguments, RunStart mode, CancellationToken cancel)
    {
        ProjectSession current = session;
        (string? path, string name) = AspireToolTarget(target);
        if (!current.Aspire.IsAspire(configuration) || path is not null && !PathNames.Same(path, configuration.Project))
        {
            throw new ArgumentException("Choose an Aspire configuration matching the resource's AppHost");
        }
        if (current.Aspire.FindApp(configuration.Project) is not null)
        {
            return false;
        }
        await WaitForAspireResourceReaderAsync(current, cancel);
        IReadOnlyList<AspireRunResource> resources = await ReadAspireConfigurationResourcesAsync(configuration, cancel);
        if (session != current || current.IsEnded)
        {
            throw new InvalidOperationException("The solution was closed");
        }
        AspireRunResource[] matches = resources.Where(resource => resource.Name == name).ToArray();
        if (matches.Length != 1 || resources.GroupBy(resource => resource.Name, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            throw new InvalidOperationException("The resource must identify one project proven by the AppHost's AddProject declarations");
        }
        RunConfiguration selected = configuration with
        {
            ServiceProjects = resources.ToDictionary(resource => resource.Name, resource => resource.Project, StringComparer.Ordinal),
            ServiceActions = resources.ToDictionary(resource => resource.Name,
                resource => resource.Name == name && mode != RunStart.Run ? ServiceStart.Debug : ServiceStart.Run, StringComparer.Ordinal),
            LaunchService = name,
        };
        // Run mode keeps unproven, dynamically added projects out of the debugger; the proven target overrides it.
        string? problem = mode == RunStart.Profile
            ? await buildRun.ProfileAspireAsync(selected, arguments, cancel)
            : await buildRun.RunAspireAsync(selected, arguments, RunStart.Run, cancel);
        if (session != current || current.IsEnded)
        {
            throw new InvalidOperationException("The solution was closed");
        }
        if (problem is not null)
        {
            throw new InvalidOperationException(problem);
        }
        AspireAppRun app = current.Aspire.FindApp(configuration.Project)
            ?? throw new InvalidOperationException("The AppHost is no longer owned by the workspace");
        try
        {
            AspireResourceRun service = await WaitForAspireToolServiceAsync(current, app, matches[0].Name, matches[0].Project, cancel);
            foreach (AspireAppRun previous in startingAspireToolTargets.Keys.Where(previous => !current.Aspire.Apps.Contains(previous)).ToArray())
            {
                startingAspireToolTargets.Remove(previous);
            }
            startingAspireToolTargets[app] = (name, service);
        }
        catch
        {
            await current.Aspire.StopGroupAsync(app.Group);
            throw;
        }
        return true;
    }

    private async Task<AspireResourceRun> WaitForAspireToolServiceAsync(ProjectSession current, AspireAppRun app,
        string name, string project, CancellationToken cancel, AspireResourceRun? previous = null, bool debugging = false,
        string? resourceName = null)
    {
        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancel, current.Aspire.ClosingToken, app.Lifetime.Token);
        waiting.CancelAfter(Waits.Of(TimeSpan.FromSeconds(60)));
        TaskCompletionSource<AspireResourceRun> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        current.Aspire.Changed += CheckStarted;
        try
        {
            CheckStarted(null, EventArgs.Empty);
            AspireResourceRun service = await ready.Task.WaitAsync(waiting.Token);
            ToolWindows.DebugTab? debugTab = service.DebugTab;
            while (true)
            {
                waiting.Token.ThrowIfCancellationRequested();
                CheckAspireToolTarget(current, app);
                IReadOnlyList<AspireResource> resources = await (app.Interactions
                    ?? throw new InvalidOperationException("The AppHost resource connection is still starting")).GetResourcesAsync(waiting.Token);
                waiting.Token.ThrowIfCancellationRequested();
                CheckAspireToolTarget(current, app);
                if (!app.Services.Contains(service) || service.Stopping.IsCancellationRequested
                    || debugging && (service.DebugTab != debugTab || debugTab is not { IsLive: true, Session: { State: not Debugging.DebugState.Ended } }))
                {
                    throw new InvalidOperationException("The requested owned service ended before its native resource became ready");
                }
                AspireSelection[] matches = resources.Where(resource => resourceName is null || resource.Name == resourceName)
                    .Select(resource => new AspireSelection(app.Configuration.Project, resource))
                    .Where(selection => current.Aspire.FindService(selection) == service).ToArray();
                if (matches.Length == 1)
                {
                    return service;
                }
                await Task.Delay(100, waiting.Token);
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested && !current.Aspire.ClosingToken.IsCancellationRequested
            && !app.Lifetime.IsCancellationRequested)
        {
            throw new TimeoutException("The requested owned service did not establish its native resource within 60 seconds");
        }
        finally
        {
            current.Aspire.Changed -= CheckStarted;
        }

        void CheckStarted(object? sender, EventArgs args)
        {
            if (session != current || current.IsEnded || app.Stopping || current.Aspire.FindApp(app.Configuration.Project) != app)
            {
                ready.TrySetException(new InvalidOperationException("The workspace or owned AppHost changed"));
                return;
            }
            AspireResourceRun[] services = app.Services.Where(service => service != previous && service.ResourceName == name
                && PathNames.Same(service.Project, project) && !service.Stopping.IsCancellationRequested).ToArray();
            if (services.Length > 1)
            {
                ready.TrySetException(new InvalidOperationException("The proven AppHost project identifies more than one native service"));
            }
            else if (services is [{ ProcessId: > 0 } service]
                && (!debugging || service.DebugTab is { IsLive: true, Session: { State: not Debugging.DebugState.Ended } }))
            {
                ready.TrySetResult(service);
            }
        }
    }

    internal async Task<string?> RunAspireToolResourceAsync(AspireSelection selection, string action, CancellationToken cancel)
    {
        ProjectSession current = session;
        AspireAppRun app = current.Aspire.FindApp(selection.AppHostPath)
            ?? throw new InvalidOperationException("The AppHost is no longer owned by this workspace");
        CheckAspireToolTarget(current, app);
        AspireSelection fresh = await ReadAspireSelectionAsync(selection.AppHostPath, selection.Resource.Name, cancel);
        CheckAspireToolTarget(current, app);
        if (fresh.Resource.Generation != selection.Resource.Generation)
        {
            throw new InvalidOperationException("The resource changed; select it again from workspace");
        }
        if (action == "stop")
        {
            AspireResourceCommand command = fresh.Resource.Commands.SingleOrDefault(command => command.Enabled && command.Name is "stop" or "resource-stop")
                ?? throw new InvalidOperationException("The resource does not advertise one enabled stop command");
            await ExecuteAspireCommandAsync(current, fresh, command.Name, () => current.Aspire.FindApp(selection.AppHostPath) == app && !app.Stopping, cancel);
            return null;
        }
        AspireResourceRun? previous = action is "run" or "debug" ? current.Aspire.FindService(fresh)
            : null;
        string? problem = action switch
        {
            "run" => await RestartAspireServiceAsync(current, fresh, debugging: false, cancel),
            "debug" => await RestartAspireServiceAsync(current, fresh, debugging: true, cancel),
            "attach" => await DebugAspireServiceAsync(current, fresh, attach: true, cancel),
            "profile" => await ProfileAspireServiceAsync(current, fresh),
            _ => throw new ArgumentException("Unknown Aspire resource operation", nameof(action)),
        };
        if (problem is null && action is "run" or "debug")
        {
            string name = previous?.ResourceName ?? fresh.Resource.DisplayName;
            string project = previous?.Project ?? fresh.Resource.Source
                ?? throw new InvalidOperationException("The owned service does not identify its AppHost resource");
            await WaitForAspireToolServiceAsync(current, app, name, project, cancel, previous, debugging: action == "debug",
                resourceName: fresh.Resource.Name);
        }
        return problem;
    }

    private void CheckAspireToolTarget(ProjectSession current, AspireAppRun app)
    {
        if (session != current || current.IsEnded || app.Stopping || current.Aspire.FindApp(app.Configuration.Project) != app)
        {
            throw new InvalidOperationException("The workspace or owned AppHost changed");
        }
    }

    private static (string? Path, string Name) AspireToolTarget(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        int separator = target.IndexOf("::", StringComparison.Ordinal);
        if (separator < 0)
        {
            return (null, target);
        }
        string path = target[..separator];
        string name = target[(separator + 2)..];
        if (!Path.IsPathFullyQualified(path) || string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Use an absolute AppHost path followed by ::resourceName");
        }
        return (PathNames.Canonical(path), name);
    }
}
