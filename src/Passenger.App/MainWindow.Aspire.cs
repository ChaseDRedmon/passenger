using System.Runtime.CompilerServices;
using Passenger.App.Aspire;
using Passenger.App.Aspire.Protocol;
using Passenger.App.Debugging;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.Core;
using Passenger.Editor;
using Avalonia.Controls;

namespace Passenger.App;

public partial class MainWindow
{
    /// <summary>The aspire CLI this window runs for AppHosts it did not start: by default the PATH's; a test's own stand-in.</summary>
    public string? AspireCliFrom { get; init; }

    private AspireClient AspireCli() => new(AspireCliFrom);

    public AspireView Aspire => Bottom.Aspire;
    public AspireController AspireRuntime => session.Aspire;
    private bool choosingAspireProfile;
    private EventHandler? aspireDisplayChanged;

    private async Task<string?> ProfileAspireAsync(RunConfiguration configuration, string? arguments)
    {
        if (choosingAspireProfile)
        {
            return "An Aspire service is already being chosen";
        }
        choosingAspireProfile = true;
        ProjectSession current = session;
        try
        {
            if (current.Aspire.FindApp(configuration.Project) is null)
            {
                return await buildRun.ProfileAspireAsync(configuration, arguments);
            }
            IReadOnlyList<AspireResource> resources = await ReadAspireResourcesAsync(configuration.Project);
            if (session != current || current.IsEnded)
            {
                return "The solution was closed";
            }
            AspireSelection[] choices = resources.Select(resource => new AspireSelection(configuration.Project, resource))
                .Where(selection => current.Aspire.FindService(selection)?.ProcessId is > 0).ToArray();
            if (choices.Length == 0)
            {
                return "No running .NET services are available to profile";
            }
            AspireServiceDialog dialog = new(choices);
            AspireSelection? selected = await dialog.ShowDialog<AspireSelection?>(this);
            return selected is null ? "Profiling was canceled" : await ProfileAspireServiceAsync(current, selected);
        }
        catch (Exception error) when (AspireFailure(error))
        {
            return error.Message;
        }
        finally
        {
            choosingAspireProfile = false;
        }
    }

    private void BindAspire(ProjectSession next)
    {
        Bottom.ResetAspire(AspireCli());
        AspirePane pane = Bottom.AspirePane;
        AspireView view = pane.View;
        ConditionalWeakTable<AspireInteractions, object> boundConnections = new();
        string[] ownedAppHosts = [];
        LookForLeftovers(next);
        aspireEngine?.Dispose();
        (aspireEngine, aspireEngineFor) = (null, null);
        next.Aspire.ContainersElsewhere += (_, said) =>
        {
            if (session == next && !next.IsEnded)
            {
                Say(said);
            }
        };
        next.Aspire.ContainersChanged += (_, _) =>
        {
            if (session == next && !next.IsEnded)
            {
                containersView?.Regroup();
            }
        };
        view.BindActions(Actions, id => Request(id));
        view.Browse = Browse;
        view.LeftUnstarted = (path, resource) => next.Aspire.FindApp(path)?.IsLeftUnstarted(resource) == true;
        view.TelemetryProvider = path => next.Aspire.FindApp(path)?.Telemetry;
        view.SelectContainer = id => _ = ShowAspireContainerAsync(next, id);
        if (aspireDisplayChanged is not null)
        {
            Dock.LayoutChanged -= aspireDisplayChanged;
        }
        aspireDisplayChanged = (_, _) => UpdateDisplay();
        Dock.LayoutChanged += aspireDisplayChanged;
        pane.AttachedToVisualTree += (_, _) => aspireDisplayChanged?.Invoke(this, EventArgs.Empty);
        pane.DetachedFromVisualTree += (_, _) => aspireDisplayChanged?.Invoke(this, EventArgs.Empty);
        next.Tests.BeginAspireTest = next.Aspire.BeginTestAsync;
        view.ResourceProvider = (path, cancel) => next.Aspire.FindApp(path) is { IsTest: false } app
            ? app.Interactions?.GetResourcesAsync(cancel)
                ?? Task.FromResult<IReadOnlyList<AspireResource>>([])
            : null;
        view.ConsoleProvider = (selected, cancel) => next.Aspire.FindApp(selected.AppHostPath)?.Interactions?.WatchConsoleAsync(selected.Resource, cancel);
        next.Aspire.ConnectInteractions = async (app, cancel) =>
        {
            if (app.ResourceServiceEndpoint is not { } endpoint)
            {
                return null;
            }
            AspireInteractions connection = await AspireInteractions.StartAsync(endpoint, app.ResourceServiceKey, this, app.Configuration.Name, cancel);
            if (connection.Failure is { } failure)
            {
                connection.Dispose();
                throw new IOException(failure);
            }
            return connection;
        };

        void BindConnection(AspireAppRun app, AspireInteractions connection)
        {
            if (boundConnections.TryGetValue(connection, out _))
            {
                return;
            }
            boundConnections.Add(connection, new object());
            connection.Failed += (_, message) =>
            {
                if (session == next && !next.IsEnded && next.Aspire.FindApp(app.Configuration.Project) == app)
                {
                    app.Console.Append($"Aspire interactions: {message}\n", error: true);
                }
            };
            connection.ResourcesChanged += (_, snapshot) =>
            {
                if (session != next || next.IsEnded || next.Aspire.FindApp(app.Configuration.Project) != app)
                {
                    return;
                }
                if (session == next && !next.IsEnded && view.SelectedApp is { } selected && PathNames.Same(selected.AppHostPath, app.Configuration.Project))
                {
                    view.ApplyResourceSnapshot(snapshot);
                }
            };
        }
        Func<AspireSelection, string, Func<bool>, CancellationToken, Task> executeCommand = async (selected, command, isCurrent, cancel) =>
        {
            await ResourceCommandAsync(next, selected, command, isCurrent, cancel);
        };
        view.LinkClicked += (_, link) => OpenLink(link);
        view.AppSelectionChanged += (_, app) =>
        {
            AspireAppRun? owned = app is null ? null : next.Aspire.FindApp(app.AppHostPath);
            pane.SetConsole(app is null ? next.Aspire.LastAppConsole : owned?.Console);
            view.ArgumentCommand = owned?.Interactions is null ? null : executeCommand;
            UpdateDisplay();
        };
        AspireResourceRun? selectedResourceService = null;
        string? selectedResourceKey = null;
        view.ResourceSelectionChanged += (_, selection) =>
        {
            if (session != next)
            {
                return;
            }
            string? key = selection is null ? null : $"{selection.AppHostPath}::{selection.Resource.Name}";
            bool again = key == selectedResourceKey;
            selectedResourceKey = key;
            // The same resource raised again (its list read again) takes back no other program's Debug tab chosen since.
            if (again && next.Aspire.OtherProgramChosen)
            {
                return;
            }
            AspireResourceRun? service = selection is null ? null : next.Aspire.FindService(selection);
            if (service == selectedResourceService)
            {
                return;
            }
            selectedResourceService = service;
            next.Aspire.SelectService(service);
            if (service?.DebugTab is { IsLive: true } tab)
            {
                BackgroundWork.Track(next.Debug.ChooseAsync(tab));
            }
        };
        next.Aspire.RestartCommand = (app, service, build, cancel) => RestartAspireCommandAsync(next, app, service, build, cancel);
        next.Aspire.Changed += (_, _) =>
        {
            FollowEngineForAspire(next);
            if (session == next && !next.IsEnded)
            {
                AspireApp? previous = view.SelectedApp;
                AspireApp[] external = view.AppSelector.Items.OfType<AspireApp>()
                    .Where(app => !ownedAppHosts.Any(path => PathNames.Same(path, app.AppHostPath))
                        && next.Aspire.FindApp(app.AppHostPath) is null).ToArray();
                ownedAppHosts = next.Aspire.Apps.Select(app => app.Configuration.Project).ToArray();
                bool connected = false;
                foreach (AspireAppRun app in next.Aspire.Apps)
                {
                    if (app.Interactions is { } connection && !boundConnections.TryGetValue(connection, out _))
                    {
                        BindConnection(app, connection);
                        connected |= previous is null || PathNames.Same(previous.AppHostPath, app.Configuration.Project);
                    }
                }
                pane.SetConsole(view.SelectedApp is { } selected ? next.Aspire.FindApp(selected.AppHostPath)?.Console ?? next.Aspire.LastAppConsole : next.Aspire.LastAppConsole);
                view.SetApps(next.Aspire.Apps.Select(app => new AspireApp(app.Configuration.Project,
                    app.ProcessId ?? app.Runner.ProcessId ?? 0, app.IsTest ? "Testing" : app.Interactions is null ? "Starting" : "Running")).Concat(external));
                UpdateDisplay();
                if (connected || previous != view.SelectedApp)
                {
                    view.ArgumentCommand = view.SelectedApp is { } chosen
                        && next.Aspire.FindApp(chosen.AppHostPath)?.Interactions is not null ? executeCommand : null;
                    view.Refresh();
                }
                ShowStop();
                ShowChosenConfiguration(next.Configurations);
                RunConfigurationsDialog?.ShowRunningAppHosts(next.Aspire.Apps.Where(app => !app.IsTest && app.Runner.IsRunning).Select(app => app.Configuration.Project));
            }
        };
        UpdateDisplay();

        bool Shown(string path) => session == next && !next.IsEnded && Dock.IsShown(ToolWindowIds.Aspire)
            && TopLevel.GetTopLevel(pane) is not null && pane.IsEffectivelyVisible
            && view.SelectedApp is { } selected && PathNames.Same(selected.AppHostPath, path);

        void UpdateDisplay()
        {
            bool visible = session == next && !next.IsEnded && Dock.IsShown(ToolWindowIds.Aspire)
                && TopLevel.GetTopLevel(pane) is not null && pane.IsEffectivelyVisible;
            pane.SetDisplayVisible(visible);
            foreach (AspireAppRun app in next.Aspire.Apps)
            {
                app.Interactions?.SetWatching(Shown(app.Configuration.Project));
            }
        }
    }

    public async Task<string?> DebugAspireResourceAsync(string appHost, string resource, bool attach = false, CancellationToken cancel = default)
    {
        ProjectSession current = session;
        try
        {
            AspireSelection selection = await ReadAspireSelectionAsync(appHost, resource, cancel);
            return await DebugAspireServiceAsync(current, selection, attach, cancel);
        }
        catch (Exception error) when (AspireFailure(error))
        {
            return error.Message;
        }
    }

    public async Task<string?> ProfileAspireResourceAsync(string appHost, string resource, CancellationToken cancel = default)
    {
        ProjectSession current = session;
        try
        {
            AspireSelection selection = await ReadAspireSelectionAsync(appHost, resource, cancel);
            return await ProfileAspireServiceAsync(current, selection);
        }
        catch (Exception error) when (AspireFailure(error))
        {
            return error.Message;
        }
    }

    public Task<IReadOnlyList<AspireResource>> ReadAspireResourcesAsync(string appHost, CancellationToken cancel = default) =>
        session.Aspire.FindApp(appHost) is { IsTest: false } app
            ? app.Interactions?.GetResourcesAsync(cancel)
                ?? Task.FromException<IReadOnlyList<AspireResource>>(new InvalidOperationException("The AppHost resource connection is still starting"))
            : AspireCli().GetResourcesAsync(appHost, cancel);

    private async Task<AspireSelection> ReadAspireSelectionAsync(string appHost, string resource, CancellationToken cancel)
    {
        IReadOnlyList<AspireResource> resources = await ReadAspireResourcesAsync(appHost, cancel);
        AspireResource selected = resources.FirstOrDefault(candidate => candidate.Name == resource)
            ?? throw new InvalidOperationException("The selected Aspire resource is no longer available");
        return new(AspireClient.CanonicalAppHost(appHost), selected);
    }

    public async Task<string> ExecuteAspireResourceCommandAsync(string appHost, string resource, string command, CancellationToken cancel = default)
    {
        ProjectSession current = session;
        AspireSelection selection = await ReadAspireSelectionAsync(appHost, resource, cancel);
        if (session != current || current.IsEnded)
        {
            throw new InvalidOperationException("The solution was closed");
        }
        return await ResourceCommandAsync(current, selection, command, () => true, cancel);
    }

    /// <summary>A resource command a person or an agent runs: Restart of a .NET service of the AppHost the window runs goes
    /// through its run of services, as a container's Restart does (its session ended first, held again where debugged, one
    /// attached as it ran attached again); any other goes to the AppHost. What it said, else throws why not.</summary>
    private async Task<string> ResourceCommandAsync(ProjectSession current, AspireSelection selection, string command, Func<bool> isCurrent, CancellationToken cancel)
    {
        if (command is "restart" or "resource-restart" && current.Aspire.FindService(selection) is IRunService service
            && current.Aspire.FindApp(selection.AppHostPath) is { IsTest: false } app)
        {
            return await buildRun.RestartServiceAsync(app, service.Name) is { } failed ? throw new InvalidOperationException(failed) : $"{app.Name}: restarted {service.Name}";
        }
        return await ExecuteAspireCommandAsync(current, selection, command, isCurrent, cancel);
    }

    private async Task<string> ExecuteAspireCommandAsync(ProjectSession current, AspireSelection selection, string command, Func<bool> isCurrent, CancellationToken cancel)
    {
        if (session != current || current.IsEnded || !isCurrent())
        {
            throw new InvalidOperationException("The selected resource or workspace changed");
        }
        AspireResourceCommand advertised = selection.Resource.Commands.FirstOrDefault(candidate => candidate.Name == command)
            ?? throw new InvalidOperationException("The resource does not advertise this command");
        AspireAppRun? app = current.Aspire.FindApp(selection.AppHostPath);
        if (app?.Interactions is not { } connection)
        {
            if (advertised.HasArguments)
            {
                throw new InvalidOperationException("Run this AppHost through Passenger to use native command input dialogs");
            }
            await AspireCli().ExecuteCommandAsync(selection.AppHostPath, selection.Resource, command, cancel);
            return "Resource command completed";
        }
        ResourceCommandResponse? result = await connection.ExecuteCommandAsync(AspireCli(), selection, command,
            () => session == current && !current.IsEnded && isCurrent(), cancel);
        if (result is null)
        {
            return "Resource command canceled";
        }
        ShowAspireCommandResult(app, command, result);
        return $"{result.Kind}: {result.Message}";
    }

    private static void ShowAspireCommandResult(AspireAppRun app, string command, ResourceCommandResponse result)
    {
        app.Console.Append($"{command}: {result.Kind} · {result.Message}\n", error: result.Kind != ResourceCommandResponseKind.Succeeded);
        if (result.Result is { } output && output.Value.Length > 0)
        {
            app.Console.Append(output.Value + "\n");
        }
        if (result.Kind != ResourceCommandResponseKind.Succeeded)
        {
            throw new InvalidOperationException($"{result.Kind}: {result.Message}");
        }
    }

    /// <summary>The Aspire window's (and an agent's) Debug or Attach of a resource: its live Debug tab chosen when it has one,
    /// else the run of services' Debug (restarted held, attached as it starts) or Attach (as it runs) of that service of
    /// the AppHost the window runs. Null once done, else why not (said too).</summary>
    private async Task<string?> DebugAspireServiceAsync(ProjectSession current, AspireSelection selection, bool attach, CancellationToken cancel = default)
    {
        using IDisposable held = BackgroundWork.Scope();
        try
        {
            if (session != current || current.IsEnded)
            {
                return "The solution was closed";
            }
            AspireResourceRun service = current.Aspire.FindService(selection)
                ?? throw new InvalidOperationException("Start this AppHost through Passenger before debugging its .NET services");
            if (service.DebugTab is { IsLive: true } existing)
            {
                await current.Debug.ChooseAsync(existing);
                current.Aspire.SelectService(service);
                ShowToolWindow(ToolWindowIds.Debug);
                return null;
            }
            AspireAppRun app = current.Aspire.FindApp(selection.AppHostPath)
                ?? throw new InvalidOperationException("The selected AppHost is no longer running");
            string name = ((IRunService)service).Name;
            string? problem = attach ? await buildRun.AttachServiceAsync(app, name) : await buildRun.DebugServiceAsync(app, name);
            if (problem is null && session == current && app.ServiceNamed(name) is AspireResourceRun now)
            {
                current.Aspire.SelectService(now);
                ShowToolWindow(ToolWindowIds.Debug);
            }
            return problem;
        }
        catch (Exception error) when (AspireFailure(error))
        {
            if (session == current)
            {
                Say(error.Message);
            }
            return error.Message;
        }
    }

    /// <summary>The Aspire window's (and an agent's) Profile of a resource: the run of services' Profile of that service of the
    /// AppHost the window runs, recorded as it runs. Null once it records, else why not (said too).</summary>
    private async Task<string?> ProfileAspireServiceAsync(ProjectSession current, AspireSelection selection)
    {
        using IDisposable held = BackgroundWork.Scope();
        try
        {
            if (session != current || current.IsEnded)
            {
                return "The solution was closed";
            }
            AspireResourceRun service = current.Aspire.FindService(selection)
                ?? throw new InvalidOperationException("Start this AppHost through Passenger before profiling its .NET services");
            AspireAppRun app = current.Aspire.FindApp(selection.AppHostPath)
                ?? throw new InvalidOperationException("The selected AppHost is no longer running");
            current.Aspire.SelectService(service);
            return await buildRun.ProfileServiceAsync(app, ((IRunService)service).Name);
        }
        catch (Exception error) when (AspireFailure(error))
        {
            if (session == current)
            {
                Say(error.Message);
            }
            return error.Message;
        }
    }

    /// <summary>The run of services' one recording of a service, either kind's, held at its start or as it runs: the window's
    /// profiler's. Null once it records, else why not.</summary>
    private Task<string?> RecordRunServiceAsync(ServiceRun run, IRunService service, ServiceProgram program, bool asItRuns, CancellationToken stop) =>
        profiler is { } commands ? commands.RecordServiceAsync(run, service, program, asItRuns, stop) : Task.FromResult<string?>("The profiler is not available");

    /// <summary>The Aspire window's (and an agent's) Debug or Run without Debugger of a resource that does not run now (one
    /// the window ran before): the choice kept, and its AppHost asked to start it; one that runs goes through the run of
    /// services (Run without Debugger restarting it unheld). Null once asked, else why not (said too).</summary>
    private async Task<string?> RestartAspireServiceAsync(ProjectSession current, AspireSelection selection, bool debugging, CancellationToken cancel = default)
    {
        using IDisposable held = BackgroundWork.Scope();
        try
        {
            if (session != current || current.IsEnded)
            {
                return "The solution was closed";
            }
            if (current.Aspire.FindService(selection) is { } running && current.Aspire.FindApp(selection.AppHostPath) is { } app)
            {
                return debugging ? await DebugAspireServiceAsync(current, selection, attach: false, cancel)
                    : await buildRun.RunServiceAsync(app, ((IRunService)running).Name);
            }
            current.Aspire.SetDebugMode(selection, debugging);
            AspireResourceCommand command = selection.Resource.Commands.FirstOrDefault(candidate => candidate.Name is "restart" or "resource-restart" && candidate.Enabled)
                ?? selection.Resource.Commands.FirstOrDefault(candidate => candidate.Name is "start" or "resource-start" && candidate.Enabled)
                ?? throw new InvalidOperationException("The selected service does not currently advertise an enabled start or restart command");
            await ExecuteAspireCommandAsync(current, selection, command.Name, () => true, cancel);
            if (session == current && !current.IsEnded)
            {
                Aspire.Refresh();
                Say($"Restart requested {(debugging ? "with" : "without")} the debugger for {selection.Resource.DisplayName}");
            }
            return null;
        }
        catch (Exception error) when (AspireFailure(error))
        {
            if (session == current)
            {
                Say(error.Message);
            }
            return error.Message;
        }
    }

    /// <summary>How an Aspire service restarts, the run of services' restart of it (<see cref="AspireAppRun.RestartAsync"/>):
    /// its AppHost's own commands, its stop, its project's build here and its start where <paramref name="build"/> asks it
    /// (an edit's, never building over its running files), else its restart (or start, stopped); done once asked, throwing
    /// why not.</summary>
    private async Task RestartAspireCommandAsync(ProjectSession current, AspireAppRun app, AspireResourceRun service, bool build, CancellationToken cancel)
    {
        // By its name and project, whether it runs or not: a session ended first may have ended its program with it.
        IReadOnlyList<AspireResource> resources = await ReadAspireResourcesAsync(app.Configuration.Project, cancel);
        if (session != current || current.IsEnded || app.Stopping)
        {
            throw new InvalidOperationException("The selected AppHost is no longer running");
        }
        string name = ((IRunService)service).Name;
        AspireSelection selected = new(app.Configuration.Project, One(resources, candidate => candidate.Source is { } source && PathNames.Same(source, service.Project)
                && (candidate.DisplayName == name || candidate.Name == name || candidate.Name.StartsWith(name + "-", StringComparison.Ordinal)))
            ?? throw new InvalidOperationException($"{name} is not one resource of {app.Configuration.Name}"));
        AspireResource resource = selected.Resource;
        if (!build)
        {
            AspireResourceCommand again = resource.Commands.FirstOrDefault(command => command.Name is "restart" or "resource-restart" && command.Enabled)
                ?? resource.Commands.FirstOrDefault(command => command.Name is "start" or "resource-start" && command.Enabled)
                ?? throw new InvalidOperationException($"{resource.DisplayName} advertises no enabled restart or start command");
            await ExecuteAspireCommandAsync(current, selected, again.Name, () => !app.Stopping, cancel);
            return;
        }
        AspireResourceCommand stop = resource.Commands.FirstOrDefault(command => command.Name is "stop" or "resource-stop" && command.Enabled)
            ?? throw new InvalidOperationException($"{resource.DisplayName} advertises no enabled stop command");
        await ExecuteAspireCommandAsync(current, selected, stop.Name, () => !app.Stopping, cancel);
        // Its program gone before the build writes over its files, which Windows locks while it runs.
        if (service.Runner is { } runner)
        {
            TimeSpan limit = Waits.Of(TimeSpan.FromSeconds(30));
            try
            {
                await runner.Ended.WaitAsync(limit, cancel);
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException($"{name} did not stop within {limit.TotalSeconds:0} s of its AppHost's stop command");
            }
        }
        if (session != current || current.IsEnded)
        {
            throw new InvalidOperationException("The solution was closed");
        }
        app.Rebuilt[service.Project] = DateTime.UtcNow;
        if (await buildRun.BuildBeforeLaunchAsync(new BuildStep(service.Project, service.Plan.Framework)) is { Succeeded: false } failed)
        {
            throw new InvalidOperationException(failed.Message);
        }
        AspireResource fresh = One(await ReadAspireResourcesAsync(app.Configuration.Project, cancel), candidate => candidate.Name == resource.Name && candidate.Uid == resource.Uid
                && AspireJson.String(candidate.Data, "type") == AspireJson.String(resource.Data, "type")
                && candidate.Source is { } source && PathNames.Same(source, service.Project))
            ?? throw new InvalidOperationException($"{name} is no longer one resource of {app.Configuration.Name} to start again after its build");
        AspireResourceCommand start = fresh.Commands.FirstOrDefault(command => command.Name is "start" or "resource-start" && command.Enabled)
            ?? fresh.Commands.FirstOrDefault(command => command.Name is "restart" or "resource-restart" && command.Enabled)
            ?? throw new InvalidOperationException($"{resource.DisplayName} advertises no enabled start command");
        await ExecuteAspireCommandAsync(current, new(app.Configuration.Project, fresh), start.Name,
            () => !app.Stopping && current.Aspire.FindApp(app.Configuration.Project) == app, cancel);
    }

    private async Task<AspireSelection> OwnedAspireSelectionAsync(ProjectSession current, AspireAppRun app, AspireResourceRun service, CancellationToken cancel)
    {
        IReadOnlyList<AspireResource> resources = await ReadAspireResourcesAsync(app.Configuration.Project, cancel);
        if (session != current || current.IsEnded || app.Stopping)
        {
            throw new InvalidOperationException("The selected AppHost is no longer running");
        }
        return One(resources.Select(resource => new AspireSelection(app.Configuration.Project, resource)), selection => current.Aspire.FindService(selection) == service)
            ?? throw new InvalidOperationException($"{((IRunService)service).Name} is no longer one resource of {app.Configuration.Name}");
    }

    /// <summary>The one item that matches, or null when none does or several do: what the caller then says, worded.</summary>
    private static T? One<T>(IEnumerable<T> items, Func<T, bool> matches) where T : class =>
        items.Where(matches).Take(2).ToArray() is [{ } one] ? one : null;

    private static bool AspireFailure(Exception error) => error is IOException or InvalidDataException or InvalidOperationException or TimeoutException
        or OperationCanceledException or System.ComponentModel.Win32Exception;

    /// <summary>What an Aspire start asks before its AppHost starts besides its certificate (which is every run's,
    /// <see cref="BuildRunController"/>'s): the container engine's state, said as a container run's is, and followed while
    /// the folder's AppHosts run. Null unless the folder was closed meanwhile.</summary>
    private async Task<string?> ShowAspireEngineAsync(RunConfiguration configuration, CancellationToken cancel)
    {
        ProjectSession current = session;
        Containers.EngineState engine = await Engine.StateAsync(again: true, cancel: cancel);
        if (session != current || current.IsEnded)
        {
            return "The solution was closed";
        }
        ShowAspireEngine(engine);
        FollowEngineForAspire(current);
        return null;
    }

    /// <summary>The engine's state during an Aspire run, said where a container run's is (the status bar's place and
    /// Problems' Containers tab, <see cref="ShowEngine"/>), in the same words, and on the Aspire window's line.</summary>
    private void ShowAspireEngine(Containers.EngineState engine)
    {
        UiThread.Check();
        // An AppHost's orchestrator needs no Compose: an engine without it serves the run.
        if (engine.Status == Containers.EngineStatus.NoCompose)
        {
            engine = engine with { Status = Containers.EngineStatus.Ready, Problem = null };
        }
        ShowEngine(engine);
        Bottom.AspirePane.SetContainerStatus(engine.Problem is { } problem ? $"Container resources cannot run: {problem}"
            : $"{engine.EngineName} is running; Aspire reports each container's state and health.");
    }

    private IDisposable? aspireEngine;
    private ProjectSession? aspireEngineFor;

    /// <summary>The engine followed while the folder's AppHosts run (its events, ended by the engine's stop and started again
    /// as it comes back), each change of its state said as <see cref="ShowAspireEngine"/> says it; let go once none runs, its
    /// folder is left or the window closes.</summary>
    private void FollowEngineForAspire(ProjectSession current)
    {
        UiThread.Check();
        bool running = session == current && !current.IsEnded && current.Aspire.Apps.Any(app => !app.IsTest && !app.Stopping);
        if (!running)
        {
            if (aspireEngineFor == current)
            {
                aspireEngine?.Dispose();
                (aspireEngine, aspireEngineFor) = (null, null);
            }
            return;
        }
        if (aspireEngine is not null)
        {
            return;
        }
        Containers.ContainerEngine engine = Engine;
        aspireEngineFor = current;
        aspireEngine = engine.Follow(told =>
        {
            if (told.Type != Containers.ContainerEngine.EngineEvent)
            {
                return;
            }
            UiThread.Post(() => BackgroundWork.Track(ReadAgainAsync()));
        });

        async Task ReadAgainAsync()
        {
            Containers.EngineState state = await BackgroundWork.Run(() => engine.StateAsync(again: true));
            if (session == current && !current.IsEnded && aspireEngine is not null)
            {
                ShowAspireEngine(state);
            }
        }
    }
}
