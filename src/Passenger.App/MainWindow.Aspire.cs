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
        PrepareAspireContainerCleanup(next);
        view.BindActions(Actions, id => Request(id));
        view.TelemetryProvider = path => next.Aspire.FindApp(path) is { DashboardEndpoint: { } endpoint } app
            ? (endpoint, app.DashboardApiKey) : null;
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
                _ = ReportAspireContainerWorkAsync(AspireContainers.Observe(app.Configuration.Project, snapshot));
                app.KnownContainerIds = snapshot.SelectMany(resource => AspireJson.Properties(resource.Data, "properties"))
                    .Where(property => property.Name == "container.id" && property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(property => property.Value.GetString()).OfType<string>().Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
                if (session == next && !next.IsEnded && view.SelectedApp is { } selected && PathNames.Same(selected.AppHostPath, app.Configuration.Project))
                {
                    view.ApplyResourceSnapshot(snapshot);
                }
            };
        }
        Func<AspireSelection, string, Func<bool>, CancellationToken, Task> executeCommand = async (selected, command, isCurrent, cancel) =>
        {
            await ExecuteAspireCommandAsync(next, selected, command, isCurrent, cancel);
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
        view.ResourceSelectionChanged += (_, selection) =>
        {
            if (session != next)
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
        next.Aspire.RestartResource = (app, service) => RestartAspireAfterEditAsync(next, app, service);
        next.Aspire.RestartBuiltResource = async (app, service, cancel) =>
        {
            AspireSelection selected = await OwnedAspireSelectionAsync(next, app, service, cancel);
            AspireResourceCommand restart = selected.Resource.Commands.First(command => command.Name is "restart" or "resource-restart" && command.Enabled);
            await ExecuteAspireCommandAsync(next, selected, restart.Name, () => !app.Stopping, cancel);
            return session == next && !next.IsEnded;
        };
        next.Aspire.ProfileService = async (app, service, plan, cancel) =>
        {
            if (session != next || next.IsEnded || profiler is not { } commands)
            {
                throw new LaunchException("The service profiler is unavailable");
            }
            ConsoleSink console = service.Console.Sink;
            Passenger.Profiler.ProfilingSession recording;
            try
            {
                recording = await commands.StartServiceAsync(plan, (reader, error, lines) =>
                    console.PumpAsync(reader, error ? ConsoleStream.Error : ConsoleStream.Output, next.Aspire.ClosingToken, lines,
                        chunks: text => UiThread.Post(() => app.Execution?.NotifyServiceLog(service.InstanceId, service.Id, text, error))),
                    cancel, nativeStart => service.StartingProfile = nativeStart, () => service.Closing);
            }
            catch
            {
                if (!service.Closing && service.StartingProfile is { } nativeStart)
                {
                    await (service.ProfileCleanup ??= AspireController.CloseProfileAsync(nativeStart));
                }
                throw;
            }
            if (service.Closing)
            {
                throw new OperationCanceledException("The Aspire service is closing");
            }
            service.Console.Input = recording.Input is { } input ? ProgramInput.Of(input) : null;
            CancellationToken closing = next.Aspire.ClosingToken;
            service.EndProfiling = async () =>
            {
                try
                {
                    await commands.EndServiceAsync(recording, () => service.Closing,
                        () => service.ProfileCleanup ??= AspireController.CloseProfileAsync(service.StartingProfile!)).ConfigureAwait(false);
                }
                finally
                {
                    if (!closing.IsCancellationRequested)
                    {
                        try
                        {
                            await UiThread.InvokeAsync(() =>
                            {
                                if (!closing.IsCancellationRequested && !next.IsEnded && !service.Closing)
                                {
                                    service.Console.Input = null;
                                    service.Console.Sink.End(ConsoleStream.Output);
                                    service.Console.Sink.End(ConsoleStream.Error);
                                    service.Console.Flush();
                                }
                                return true;
                            }).WaitAsync(closing).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (closing.IsCancellationRequested)
                        {
                        }
                    }
                }
            };
            return recording;
        };
        next.Aspire.Changed += (_, _) =>
        {
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
        return await ExecuteAspireCommandAsync(current, selection, command, () => true, cancel);
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
            if (!attach)
            {
                return await RestartAspireServiceAsync(current, selection, debugging: true, cancel);
            }
            if (service.ProcessId is not int process)
            {
                return "The selected service has not started a .NET process yet";
            }
            AspireAppRun app = current.Aspire.FindApp(selection.AppHostPath)
                ?? throw new InvalidOperationException("The selected AppHost is no longer running");
            DebugLaunch launched = await current.Debug.AttachAdditionalAsync(process, service.DebugName, cancel, tab =>
            {
                tab.Group = app.Group;
                tab.App = service.Inspection;
                service.DebugTab = tab;
                tab.RestartFor = outcome => service.DebugTab == tab && !service.Stopping.IsCancellationRequested
                    ? current.Aspire.RestartResourcesAsync(outcome.Restart.Count > 0 ? outcome.Restart : [service.DebugName]) : Task.FromResult(false);
                tab.StateChanged += (_, _) =>
                {
                    if (tab.Session is { State: DebugState.Ended } && service.DebugTab == tab)
                    {
                        service.DebugTab = null;
                    }
                };
            });
            if (session != current || current.IsEnded)
            {
                await current.Debug.StopAsync(launched.Tab);
                return "The solution was closed";
            }
            current.Aspire.SelectService(service);
            ShowToolWindow(ToolWindowIds.Debug);
            Say($"Attached to {service.Plan.Name}");
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
            if (service.ProcessId is not int process || profiler is null)
            {
                return "The selected service has not started a .NET process yet";
            }
            current.Aspire.SelectService(service);
            return await profiler.AttachAsync(process, service.Plan.Name, service.Plan.Built ?? service.Plan.Program);
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

    private async Task<string?> RestartAspireServiceAsync(ProjectSession current, AspireSelection selection, bool debugging, CancellationToken cancel = default)
    {
        using IDisposable held = BackgroundWork.Scope();
        try
        {
            if (session != current || current.IsEnded)
            {
                return "The solution was closed";
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

    private async Task<bool> RestartAspireAfterEditAsync(ProjectSession current, AspireAppRun app, AspireResourceRun service)
    {
        if (session != current || current.IsEnded)
        {
            return false;
        }
        try
        {
            AspireSelection selected = await OwnedAspireSelectionAsync(current, app, service, CancellationToken.None);
            AspireResource resource = selected.Resource;
            AspireResourceCommand stop = resource.Commands.First(command => command.Name is "stop" or "resource-stop" && command.Enabled);
            await ExecuteAspireCommandAsync(current, selected, stop.Name, () => !app.Stopping, CancellationToken.None);
            if (session != current || current.IsEnded || await buildRun.BuildBeforeLaunchAsync(new BuildStep(service.Project, service.Plan.Framework)) is { Succeeded: false })
            {
                return false;
            }
            AspireResource fresh = (await ReadAspireResourcesAsync(app.Configuration.Project, CancellationToken.None))
                .Single(candidate => candidate.Name == resource.Name && candidate.Uid == resource.Uid
                    && AspireJson.String(candidate.Data, "type") == AspireJson.String(resource.Data, "type")
                    && candidate.Source is { } source && PathNames.Same(source, service.Project));
            AspireResourceCommand start = fresh.Commands.FirstOrDefault(command => command.Name is "start" or "resource-start" && command.Enabled)
                ?? fresh.Commands.First(command => command.Name is "restart" or "resource-restart" && command.Enabled);
            await ExecuteAspireCommandAsync(current, new(app.Configuration.Project, fresh), start.Name,
                () => !app.Stopping && current.Aspire.FindApp(app.Configuration.Project) == app, CancellationToken.None);
            return session == current && !current.IsEnded;
        }
        catch (Exception error) when (AspireFailure(error))
        {
            if (session == current)
            {
                Say($"Could not restart {service.Plan.Name}: {error.Message}");
            }
            return false;
        }
    }

    private async Task<AspireSelection> OwnedAspireSelectionAsync(ProjectSession current, AspireAppRun app, AspireResourceRun service, CancellationToken cancel)
    {
        IReadOnlyList<AspireResource> resources = await ReadAspireResourcesAsync(app.Configuration.Project, cancel);
        if (session != current || current.IsEnded || app.Stopping)
        {
            throw new InvalidOperationException("The selected AppHost is no longer running");
        }
        return resources.Select(resource => new AspireSelection(app.Configuration.Project, resource))
            .Single(selection => current.Aspire.FindService(selection) == service);
    }

    private static bool AspireFailure(Exception error) => error is IOException or InvalidDataException or InvalidOperationException or TimeoutException
        or OperationCanceledException or System.ComponentModel.Win32Exception;

    /// <summary>What an Aspire start asks before its AppHost starts besides its certificate (which is every run's,
    /// <see cref="BuildRunController"/>'s): the container engine's state, said in the Aspire window. Null unless the folder
    /// was closed meanwhile.</summary>
    private async Task<string?> ShowAspireEngineAsync(RunConfiguration configuration, CancellationToken cancel)
    {
        ProjectSession current = session;
        Containers.EngineState engine = await Engine.StateAsync(cancel: cancel);
        string engineStatus = engine.IsReady ? "Container engine is ready; Aspire reports each container's actual state and health."
            : engine.Problem ?? "Container resources need a running container engine; container-free apps can still run.";
        if (session != current || current.IsEnded)
        {
            return "The solution was closed";
        }
        Bottom.AspirePane.SetContainerStatus(engineStatus);
        return null;
    }
}
