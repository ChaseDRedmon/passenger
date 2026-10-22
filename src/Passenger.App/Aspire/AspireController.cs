using Passenger.App.Debugging;
using Passenger.App.LiveTree;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Profiler;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Passenger.App.Aspire;

public enum AspireRunMode
{
    Run,
    Debug,
    HotReload,
    Profile,
}

/// <summary>Owns AppHosts and their DCP-requested service processes for one workspace.</summary>
public sealed partial class AspireController(ProjectSession workspace, string stateFolder) : IAsyncDisposable
{
    private readonly List<AspireAppRun> apps = [];
    private readonly HashSet<string> startingPaths = new(PathNames.Comparer);
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    private Task? closing;
    internal CancellationToken ClosingToken => lifetime.Token;

    public IReadOnlyList<AspireAppRun> Apps => apps.AsReadOnly();
    public OutputView? LastAppConsole { get; private set; }
    public AspireResourceRun? SelectedService { get; private set; }
    public LiveApp? SelectedInspection => SelectedService?.Inspection;
    public bool IsRunning => apps.Any(app => !app.Stopping && (app.Runner.IsRunning || app.IsTest));
    public event EventHandler? Changed;
    public Func<AspireAppRun, AspireResourceRun, Task<bool>>? RestartResource { get; set; }
    public Func<AspireAppRun, AspireResourceRun, CancellationToken, Task<bool>>? RestartBuiltResource { get; set; }
    public Func<AspireAppRun, CancellationToken, Task<AspireInteractions?>>? ConnectInteractions { get; set; }
    public Func<AspireAppRun, AspireResourceRun, LaunchPlan, CancellationToken, Task<ProfilingSession>>? ProfileService { get; set; }

    public void SelectService(AspireResourceRun? service)
    {
        UiThread.Check();
        if (SelectedService != service)
        {
            SelectedService = service;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SelectDebugger(DebugTab? tab) => SelectService(tab is null ? null : apps.SelectMany(app => app.Services)
        .FirstOrDefault(service => service.DebugTab == tab));

    public AspireAppRun? FindApp(string appHost) => apps.FirstOrDefault(app => PathNames.Same(app.Configuration.Project, appHost));

    public AspireResourceRun? FindService(AspireSelection selection)
    {
        if (FindApp(selection.AppHostPath) is not { } app || selection.Resource.Source is not { } source || !Path.IsPathFullyQualified(source))
        {
            return null;
        }
        if (selection.Resource.State is not ("Running" or "Starting"))
        {
            return null;
        }
        AspireResourceRun[] matching = app.Services.Where(service => PathNames.Same(service.Project, source)).ToArray();
        int? processId = null;
        foreach (System.Text.Json.JsonProperty property in AspireJson.Properties(selection.Resource.Data, "properties"))
        {
            if (property.Name != "executable.pid")
            {
                continue;
            }
            if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Number && property.Value.TryGetDouble(out double number)
                && number is > 0 and <= int.MaxValue && number == Math.Truncate(number))
            {
                processId = (int)number;
            }
            else if (property.Value.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(property.Value.GetString(), out int value) && value > 0)
            {
                processId = value;
            }
        }
        AspireResourceRun[] named = matching.Where(service => processId is int pid
            ? service.ProcessId == pid && (service.ResourceName is null || service.ResourceName == selection.Resource.Name
                || selection.Resource.Name.StartsWith(service.ResourceName + "-", StringComparison.Ordinal) || service.ResourceName == selection.Resource.DisplayName)
            : service.ResourceName == selection.Resource.Name).Take(2).ToArray();
        if (named.Length != 1)
        {
            return null;
        }
        AspireResourceRun matched = named[0];
        if (!disposed && !workspace.IsEnded && !lifetime.IsCancellationRequested && !app.Stopping
            && !app.Lifetime.IsCancellationRequested && processId is > 0 && selection.Resource.Uid is { Length: > 0 } uid)
        {
            var identity = (Name: selection.Resource.Name, Project: matched.Project, IdentityKey: matched.IdentityKey);
            if (app.OwnedResourceIdentities.TryGetValue(uid, out var previous)
                && (previous is not { } known || known.Name != identity.Name
                    || !PathNames.Same(known.Project, identity.Project) || known.IdentityKey != identity.IdentityKey))
            {
                app.OwnedResourceIdentities[uid] = null;
            }
            else
            {
                app.OwnedResourceIdentities[uid] = identity;
            }
        }
        return matched;
    }

    public void SetDebugMode(AspireSelection selection, bool debugging)
    {
        UiThread.Check();
        AspireAppRun app = FindApp(selection.AppHostPath) ?? throw new InvalidOperationException("The selected AppHost is not owned by this workspace");
        if (disposed || workspace.IsEnded || lifetime.IsCancellationRequested || app.Stopping || app.Lifetime.IsCancellationRequested)
        {
            throw new InvalidOperationException("The workspace or owned AppHost changed");
        }
        if (FindService(selection) is { } service)
        {
            app.DebugOverrides[service.IdentityKey] = debugging;
            return;
        }
        if (selection.Resource.State is not ("NotStarted" or "Exited" or "Finished" or "FailedToStart")
            || selection.Resource.Uid is not { Length: > 0 } uid
            || !app.OwnedResourceIdentities.TryGetValue(uid, out var remembered) || remembered is not { } identity
            || identity.Name != selection.Resource.Name || selection.Resource.Source is not { } source
            || !Path.IsPathFullyQualified(source) || !PathNames.Same(identity.Project, source))
        {
            throw new InvalidOperationException("The selected resource does not identify one previously owned .NET service");
        }
        app.DebugOverrides[identity.IdentityKey] = debugging;
    }

    public bool IsAspire(RunConfiguration configuration) => configuration.Aspire
        || workspace.Projects.Any(project => project.IsAspireHost && PathNames.Same(project.Path, configuration.Project));

    public async Task<string?> StartAsync(RunConfiguration configuration, AspireRunMode mode,
        Func<BuildStep, Task<BuildOutcome>> build, CancellationToken cancellation = default)
    {
        UiThread.Check();
        ObjectDisposedException.ThrowIf(disposed, this);
        using CancellationTokenSource starting = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellation);
        List<AspireAppRun> started = [];
        List<string> reserved = [];
        try
        {
            string[] paths = [configuration.Project, .. configuration.LinkedAppHosts];
            if (paths.Distinct(PathNames.Comparer).Count() != paths.Length)
            {
                return "An Aspire configuration cannot link an AppHost more than once";
            }
            foreach (string path in paths)
            {
                if (startingPaths.Contains(path) || apps.Any(app => PathNames.Same(app.Configuration.Project, path)))
                {
                    return $"{Path.GetFileNameWithoutExtension(path)} is already running in this workspace";
                }
            }
            foreach (string path in paths)
            {
                startingPaths.Add(path);
                reserved.Add(path);
            }
            string group = Guid.NewGuid().ToString("N");
            RunConfiguration[] configurations = paths.Select(path => PathNames.Same(path, configuration.Project) ? configuration
                : workspace.Configurations.All.FirstOrDefault(candidate => candidate.Aspire && PathNames.Same(candidate.Project, path))
                    ?? throw new LaunchException($"The linked AppHost {Path.GetFileName(path)} is not available")).ToArray();
            (string AppHost, string Name)[] candidates = configurations.SelectMany(chosen => chosen.ServiceActions
                .Where(pair => pair.Value == ServiceStart.Debug && chosen.ServiceProjects.ContainsKey(pair.Key))
                .OrderBy(pair => pair.Key == chosen.LaunchService ? 0 : 1)
                .Select(pair => (chosen.Project, pair.Key))).ToArray();
            if (candidates.Length == 0)
            {
                candidates = configurations.Where(chosen => chosen.ServiceProjects.ContainsKey(chosen.LaunchService))
                    .Select(chosen => (chosen.Project, chosen.LaunchService)).ToArray();
            }
            (string AppHost, string Name)? profileTarget = candidates.Length > 0 ? candidates[0] : null;
            foreach (RunConfiguration chosen in configurations)
            {
                AspireAppRun app = await StartAppAsync(chosen, mode, group, build, starting.Token, profileTarget);
                started.Add(app);
            }
            if (mode == AspireRunMode.Profile)
            {
                using CancellationTokenSource profiling = CancellationTokenSource.CreateLinkedTokenSource(starting.Token);
                profiling.CancelAfter(TimeSpan.FromSeconds(60));
                Task winner = await Task.WhenAny(started.Select(app => (Task)app.ProfileStarted.Task)
                    .Concat(started.Select(app => (Task)app.Runner.Ended))).WaitAsync(profiling.Token);
                AspireAppRun? profiled = started.FirstOrDefault(app => ReferenceEquals(winner, app.ProfileStarted.Task));
                if (profiled is null)
                {
                    throw new LaunchException("The AppHost ended before a service could be profiled");
                }
                if (await profiled.ProfileStarted.Task is { } failed)
                {
                    throw new LaunchException(failed);
                }
            }
            return null;
        }
        catch (Exception error) when (error is LaunchException or IOException or InvalidOperationException
            or OperationCanceledException or System.ComponentModel.Win32Exception or TimeoutException or JsonException or ProfilingException)
        {
            foreach (AspireAppRun app in started)
            {
                await StopAppAsync(app);
            }
            return error is OperationCanceledException ? "Starting the Aspire application was stopped" : error.Message;
        }
        finally
        {
            foreach (string path in reserved)
            {
                startingPaths.Remove(path);
            }
        }
    }

    private async Task<AspireAppRun> StartAppAsync(RunConfiguration configuration, AspireRunMode mode, string group,
        Func<BuildStep, Task<BuildOutcome>> build, CancellationToken cancellation, (string AppHost, string Name)? profileTarget)
    {
        LaunchPlan plan = await ResolveAppHostAsync(configuration, cancellation);
        if (plan.Build is { } step && await build(step) is { Succeeded: false } failed)
        {
            throw new LaunchException(failed.Message);
        }
        cancellation.ThrowIfCancellationRequested();
        if (workspace.IsEnded || disposed)
        {
            throw new LaunchException("The solution was closed");
        }
        if (plan.WhyNot("run") is { } why)
        {
            throw new LaunchException(why);
        }
        AspireAppRun app = new(configuration, mode, group) { ProfileTarget = profileTarget };
        LastAppConsole = app.Console;
        apps.Add(app);
        try
        {
            AspireExecutionServer execution = await StartEndpointAsync(app, cancellation);
            app.Lifetime.Token.ThrowIfCancellationRequested();
            cancellation.ThrowIfCancellationRequested();
            Dictionary<string, string> environment = new(plan.Environment, StringComparer.Ordinal);
            foreach ((string name, string value) in execution.Environment)
            {
                environment[name] = value;
            }
            environment.Remove("DEBUG_SESSION_SERVER_CERTIFICATE");
            environment["DEBUG_SESSION_RUN_MODE"] = mode == AspireRunMode.Debug ? "Debug" : "NoDebug";
            environment["AppHost__FilePath"] = configuration.Project;
            environment["ASPIRE_CLI_TELEMETRY_OPTOUT"] = "1";
            ConfigureResourceService(app, environment);
            ConfigureDashboard(app, environment);
            app.Runner.Exited += (_, _) => BackgroundWork.Track(StopAppAsync(app));
            ProcessStartInfo hostStart = ChildProcesses.Command(plan.Program, plan.Arguments, plan.WorkingDirectory);
            hostStart.Environment.Remove("DEBUG_SESSION_SERVER_CERTIFICATE");
            if (app.Runner.Start(hostStart, environment) is { } refused)
            {
                throw new LaunchException(refused);
            }
            app.ProcessId = app.Runner.ProcessId;
            app.Console.SetRunning(true);
            app.Console.StopClicked += (_, _) => BackgroundWork.Track(StopGroupAsync(app.Group));
            Changed?.Invoke(this, EventArgs.Empty);
            if (ConnectInteractions is { } connect)
            {
                using CancellationTokenSource connecting = CancellationTokenSource.CreateLinkedTokenSource(cancellation, app.Lifetime.Token);
                Task<AspireInteractions?> pending = connect(app, connecting.Token);
                try
                {
                    if (await Task.WhenAny(pending, app.Runner.Ended).WaitAsync(connecting.Token) != pending)
                    {
                        throw new LaunchException("The AppHost exited before it became ready. Read its AppHost console in the Aspire window.");
                    }
                    app.Interactions = await pending;
                    connecting.Token.ThrowIfCancellationRequested();
                }
                catch
                {
                    connecting.Cancel();
                    try
                    {
                        if (await pending is { } late)
                        {
                            await late.DisposeAsync();
                        }
                    }
                    catch (Exception error) when (error is IOException or OperationCanceledException or InvalidOperationException)
                    {
                    }
                    throw;
                }
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return app;
        }
        catch
        {
            await StopAppAsync(app);
            throw;
        }
    }

    private static void ConfigureResourceService(AspireAppRun app, IDictionary<string, string> environment)
    {
        Uri? endpoint = null;
        if (environment.TryGetValue("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL", out string? configured))
        {
            Uri.TryCreate(configured.Split(';')[0], UriKind.Absolute, out endpoint);
        }
        if (endpoint is null || endpoint.Port == 0)
        {
            using TcpListener port = new(IPAddress.Loopback, 0);
            port.Start();
            int number = ((IPEndPoint)port.LocalEndpoint).Port;
            bool allowHttp = environment.TryGetValue("ASPIRE_ALLOW_UNSECURED_TRANSPORT", out string? allowed)
                && bool.TryParse(allowed, out bool unsecured) && unsecured;
            endpoint = new Uri($"{(allowHttp ? "http" : "https")}://localhost:{number}");
            environment["ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL"] = endpoint.AbsoluteUri;
        }
        if (!endpoint.IsLoopback || endpoint.Scheme is not ("http" or "https"))
        {
            throw new LaunchException("Aspire's resource service must use a loopback HTTP or HTTPS endpoint");
        }
        app.ResourceServiceEndpoint = endpoint;
        app.ResourceServiceKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        environment["ASPIRE_DASHBOARD_RESOURCESERVICE_APIKEY"] = app.ResourceServiceKey;
    }

    private static void ConfigureDashboard(AspireAppRun app, IDictionary<string, string> environment)
    {
        string urls = environment.TryGetValue("ASPNETCORE_URLS", out string? configured) ? configured
            : Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? string.Empty;
        Uri? existing = urls.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(url => Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? endpoint) ? endpoint : null)
            .FirstOrDefault(endpoint => endpoint?.Scheme == Uri.UriSchemeHttp);
        using TcpListener port = new(IPAddress.Loopback, 0);
        if (existing is null)
        {
            port.Start();
        }
        app.DashboardEndpoint = existing ?? new Uri($"http://127.0.0.1:{((IPEndPoint)port.LocalEndpoint).Port}");
        app.DashboardApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        environment["AppHost__DashboardApiKey"] = app.DashboardApiKey;
        if (existing is null)
        {
            environment["ASPNETCORE_URLS"] = string.IsNullOrWhiteSpace(urls) ? app.DashboardEndpoint.AbsoluteUri
                : $"{urls.TrimEnd(';')};{app.DashboardEndpoint.AbsoluteUri}";
        }
    }

    private Task<AspireExecutionServer> StartEndpointAsync(AspireAppRun app, CancellationToken cancellation)
    {
        return app.EndpointStarting = StartAndPublishAsync();

        async Task<AspireExecutionServer> StartAndPublishAsync()
        {
            using CancellationTokenSource startup = CancellationTokenSource.CreateLinkedTokenSource(cancellation, app.Lifetime.Token);
            AspireExecutionServer execution = await AspireExecutionServer.StartAsync(
                (launch, cancel) => CreateOnUiAsync(app, launch, cancel),
                (handle, cancel) => app.Lifetime.IsCancellationRequested ? Task.CompletedTask : UiThread.InvokeAsync(async () =>
                {
                    cancel.ThrowIfCancellationRequested();
                    if (!app.Lifetime.IsCancellationRequested)
                    {
                        await StopResourceAsync(app, handle.Id);
                    }
                    return true;
                }).WaitAsync(cancel).WaitAsync(app.Lifetime.Token), startup.Token).ConfigureAwait(false);
            app.Execution = execution;
            if (startup.IsCancellationRequested)
            {
                await execution.DisposeAsync().ConfigureAwait(false);
                startup.Token.ThrowIfCancellationRequested();
            }
            return execution;
        }
    }

    private async Task<AspireProjectSession> CreateOnUiAsync(AspireAppRun app, AspireProjectLaunch request, CancellationToken cancel)
    {
        Task<AspireProjectSession> creating = UiThread.InvokeAsync(() => StartResourceAsync(app, request, cancel));
        try
        {
            return await creating.WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            // A request can give up after its UI launch succeeds; that late process still belongs to the app.
            _ = CleanLateLaunchAsync();
            throw;
        }

        async Task CleanLateLaunchAsync()
        {
            try
            {
                AspireProjectSession handle = await creating.ConfigureAwait(false);
                if (!app.Lifetime.IsCancellationRequested)
                {
                    await UiThread.InvokeAsync(async () =>
                    {
                        if (!app.Lifetime.IsCancellationRequested)
                        {
                            await StopResourceAsync(app, handle.Id);
                        }
                        return true;
                    }).WaitAsync(app.Lifetime.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                IdeErrors.Report(error);
            }
        }
    }

    private static async Task DisposeEndpointAsync(Task<AspireExecutionServer> starting)
    {
        try
        {
            AspireExecutionServer execution = await starting.ConfigureAwait(false);
            await execution.DisposeAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<LaunchPlan> ResolveAppHostAsync(RunConfiguration configuration, CancellationToken cancellation)
    {
        if (!Path.GetExtension(configuration.Project).Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            if (configuration.Program.Length > 0)
            {
                throw new LaunchException("Owned Aspire AppHosts must launch the project's built target; clear Exe path");
            }
            return await LaunchPlan.ResolveAsync(configuration, workspace.Projects, workspace.Workspace, stateFolder, cancellation);
        }
        if (!AspireAppHosts.IsSingleFile(configuration.Project))
        {
            throw new LaunchException("The selected C# file does not declare the Aspire AppHost SDK");
        }
        if (AspireAppHosts.SingleFileWhyNot(configuration.Project,
            Directory.EnumerateFiles(Path.GetDirectoryName(configuration.Project)!, "*", SearchOption.TopDirectoryOnly)) is { } unsupported)
        {
            throw new LaunchException(unsupported);
        }
        if (configuration.Program.Length > 0 || configuration.Framework.Length > 0)
        {
            throw new LaunchException("Single-file AppHosts use the framework declared in the file; clear Exe path and Target framework");
        }
        string folder = Path.GetDirectoryName(configuration.Project)!;
        configuration = ReadFileProfile(configuration, folder);
        string output = Path.Combine(stateFolder, "aspire", Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(PathNames.Canonical(configuration.Project)))), "output");
        if (configuration.BuildBeforeLaunch)
        {
            ChildResult built = await ChildProcesses.RunAsync(ChildProcesses.DotnetCommand(
                ["build", configuration.Project, "--output", output, "--nologo"], folder), cancel: cancellation);
            if (!built.Succeeded)
            {
                throw new LaunchException($"Could not build {configuration.Name}: {built.Error.Trim()} {built.Output.Trim()}");
            }
        }
        ChildResult evaluated = await ChildProcesses.RunAsync(ChildProcesses.DotnetCommand(
            ["build", configuration.Project, "--output", output, "--no-restore", "--nologo", "-getProperty:TargetPath"], folder), cancel: cancellation);
        string target = evaluated.Output.Trim();
        if (!evaluated.Succeeded || !Path.IsPathFullyQualified(target) || !File.Exists(target))
        {
            throw new LaunchException("MSBuild could not resolve the built single-file AppHost TargetPath");
        }
        string Expand(string value)
        {
            string expanded = Environment.ExpandEnvironmentVariables(value.Replace("$(ProjectDir)", folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (expanded.Contains("$(", StringComparison.Ordinal))
            {
                throw new LaunchException("Single-file AppHosts support the ProjectDir macro, not MSBuild output properties");
            }
            return expanded;
        }
        return new LaunchPlan
        {
            Name = configuration.Name,
            Program = target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? Dotnet.Host() : target,
            Arguments = [.. target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? new[] { target } : [],
                .. CommandLine.Split(Expand(configuration.Arguments))],
            WorkingDirectory = configuration.WorkingDirectory.Length == 0 ? folder : PathNames.Canonical(Path.Combine(folder, Expand(configuration.WorkingDirectory))),
            Environment = configuration.Environment.ToDictionary(variable => variable.Key, variable => Expand(variable.Value)),
            Project = configuration.Project,
            Built = target,
        };
    }

    internal static RunConfiguration ReadFileProfile(RunConfiguration configuration, string folder)
    {
        string path = Path.Combine(folder, "Properties", "launchSettings.json");
        if (!File.Exists(path))
        {
            path = Path.ChangeExtension(configuration.Project, ".run.json");
        }
        if (!File.Exists(path))
        {
            return configuration.LaunchProfile.Length == 0 ? configuration
                : throw new LaunchException($"Launch profile {configuration.LaunchProfile} was not found");
        }
        using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        if (!settings.RootElement.TryGetProperty("profiles", out JsonElement profiles) || profiles.ValueKind != JsonValueKind.Object)
        {
            throw new LaunchException("The single-file AppHost launch profiles did not read");
        }
        JsonProperty chosen = profiles.EnumerateObject().FirstOrDefault(profile =>
            configuration.LaunchProfile.Length == 0 || profile.Name == configuration.LaunchProfile);
        if (chosen.Value.ValueKind == JsonValueKind.Undefined)
        {
            throw new LaunchException($"Launch profile {configuration.LaunchProfile} was not found");
        }
        JsonElement value = chosen.Value;
        if (value.TryGetProperty("commandName", out JsonElement command) && command.GetString() != "Project")
        {
            throw new LaunchException("Single-file AppHosts require a Project launch profile");
        }
        Dictionary<string, string> environment = [];
        if (value.TryGetProperty("environmentVariables", out JsonElement variables))
        {
            foreach (JsonProperty variable in variables.EnumerateObject())
            {
                environment[variable.Name] = variable.Value.GetString() ?? string.Empty;
            }
        }
        if (!environment.ContainsKey("ASPNETCORE_URLS") && value.TryGetProperty("applicationUrl", out JsonElement urls))
        {
            environment["ASPNETCORE_URLS"] = urls.GetString() ?? string.Empty;
        }
        foreach ((string name, string text) in configuration.Environment)
        {
            environment[name] = text;
        }
        return configuration with
        {
            Environment = environment,
            Arguments = configuration.Arguments.Length > 0 ? configuration.Arguments
                : value.TryGetProperty("commandLineArgs", out JsonElement arguments) ? arguments.GetString() ?? string.Empty : string.Empty,
            WorkingDirectory = configuration.WorkingDirectory.Length > 0 ? configuration.WorkingDirectory
                : value.TryGetProperty("workingDirectory", out JsonElement working) ? working.GetString() ?? string.Empty : string.Empty,
        };
    }

    private async Task<AspireProjectSession> StartResourceAsync(AspireAppRun app, AspireProjectLaunch request, CancellationToken cancellation)
    {
        UiThread.Check();
        cancellation.ThrowIfCancellationRequested();
        if (app.Stopping || workspace.IsEnded || disposed || app.Resources.Count >= 200)
        {
            throw new LaunchException("The Aspire app is stopping or its service limit was reached");
        }
        string path = PathNames.Canonical(request.ProjectPath);
        if (Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new LaunchException(AspireExecutionServer.FileServiceUnsupported);
        }
        ProjectModel? known = workspace.Projects.FirstOrDefault(project => PathNames.Same(project.Path, path));
        bool referenced = workspace.Projects.Any(project => project.ProjectReferences.Any(reference => PathNames.Same(reference, path)));
        if (!File.Exists(path) || !WorkspaceModel.IsProject(path)
            || (known is null && !referenced && (workspace.Folder is null || !PathNames.IsUnder(path, workspace.Folder))))
        {
            throw new LaunchException("DCP requested a project outside this workspace and its project references");
        }
        ProjectModel project = known ?? ProjectModel.Unevaluated(path);
        string? profileName = request.DisableLaunchProfile ? null : request.LaunchProfile ?? app.Configuration.LaunchProfile;
        LaunchProfile? profile = profileName is null ? null : LaunchProfile.Read(project).FirstOrDefault(candidate => candidate.Name == profileName);
        RunConfiguration configuration = profile is null
            ? new RunConfiguration { Name = project.Name, Project = path, WorkingDirectory = "$(ProjectDir)" }
            : RunConfiguration.From(project, profile) with { Name = project.Name };
        configuration = configuration with { BuildBeforeLaunch = false, LaunchBrowser = false, Console = ConsoleKind.Internal };
        LaunchPlan plan = await LaunchPlan.ResolveAsync(configuration,
            known is null ? [.. workspace.Projects, project] : workspace.Projects, workspace.Workspace, stateFolder, cancellation);
        Dictionary<string, string> environment = new(plan.Environment, StringComparer.Ordinal);
        foreach (AspireEnvironmentVariable variable in request.Environment)
        {
            environment[variable.Name] = variable.Value;
        }
        if (app.Mode == AspireRunMode.HotReload)
        {
            environment[RuntimeVariables.DefaultDiagnosticPortSuspend] = "1";
            environment[RuntimeVariables.ModifiableAssemblies] = "debug";
        }
        IReadOnlyList<string> arguments = request.Arguments is { } given
            ? [.. plan.Built is { } built && plan.Arguments.FirstOrDefault() == built ? new[] { built } : [], .. given]
            : plan.Arguments;
        plan = plan with { Name = $"{app.Configuration.Name} / {project.Name}", Environment = environment, Arguments = arguments };
        cancellation.ThrowIfCancellationRequested();
        if (app.Stopping || workspace.IsEnded || app.Resources.Count >= 200)
        {
            throw new LaunchException("The Aspire application ended or reached its service limit during launch");
        }
        AspireResourceRun resource = new(request.SessionId, request.DcpInstanceId, path, plan);
        resource.ResourceName = request.Environment.FirstOrDefault(variable => variable.Name == "OTEL_SERVICE_NAME")?.Value;
        app.Resources.Add(resource.Id, resource);
        using CancellationTokenSource starting = CancellationTokenSource.CreateLinkedTokenSource(cancellation, app.Lifetime.Token, resource.Stopping.Token);
        cancellation = starting.Token;
        bool profiling = false;
        try
        {
            bool debugging = app.DebugOverrides.TryGetValue(resource.IdentityKey, out bool overridden) ? overridden
                : app.Mode != AspireRunMode.Profile && (ResourceAction(app.Configuration, resource) is { } action ? action == ServiceStart.Debug : request.Mode == "Debug");
            profiling = app.Mode == AspireRunMode.Profile && !apps.Any(candidate => candidate.Group == app.Group && candidate.ProfileClaimed)
                && (app.ProfileTarget is not { } selected || PathNames.Same(selected.AppHost, app.Configuration.Project)
                    && resource.ResourceName == selected.Name && app.Configuration.ServiceProjects.TryGetValue(selected.Name, out string? selectedProject)
                    && PathNames.Same(selectedProject, resource.Project));
            if (profiling)
            {
                app.ProfileClaimed = true;
                debugging = false;
            }
            resource.Inspection = debugging || app.Mode != AspireRunMode.HotReload ? LiveApp.For(plan, workspace.Overlays) : null;
            if (resource.Inspection is { } inspection)
            {
                inspection.BindingFailed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            }
            LaunchPlan inspected = resource.Inspection?.Plan ?? plan;
            if (profiling)
            {
                resource.Profile = await (ProfileService ?? throw new LaunchException("The service profiler is unavailable"))(app, resource, inspected, cancellation);
                resource.ProcessId = resource.Profile.ProcessId;
                _ = ProfileEndedAsync(app, resource, resource.Profile);
            }
            else if (debugging)
            {
                DebugLaunch launched = await workspace.Debug.StartAdditionalAsync(inspected with { Name = resource.DebugName }, cancellation, tab =>
                {
                    tab.Group = app.Group;
                    tab.App = resource.Inspection;
                    resource.DebugTab = tab;
                    tab.RestartFor = outcome => resource.DebugTab == tab && !resource.Stopping.IsCancellationRequested
                        ? RestartResourcesAsync(outcome.Restart.Count > 0 ? outcome.Restart : [resource.DebugName]) : Task.FromResult(false);
                    tab.ProgramOutput += (_, output) => app.Execution?.NotifyServiceLog(request.DcpInstanceId,
                        request.SessionId, output.Output, output.Category == "stderr");
                    tab.StateChanged += (_, _) =>
                    {
                        if (resource.DebugTab == tab && tab.Session is { State: DebugState.Ended } ended)
                        {
                            Finished(app, resource, ended.ExitCode);
                        }
                    };
                });
                resource.ProcessId = launched.ProcessId;
            }
            else
            {
                ProcessRunner runner = resource.Runner = new ProcessRunner(resource.Console, takesInput: true);
                runner.OutputReceived += (_, output) => app.Execution?.NotifyServiceLog(request.DcpInstanceId, request.SessionId, output.Text, output.IsError);
                runner.Exited += (_, code) => Finished(app, resource, code);
                string? refused = runner.Start(inspected.Program, inspected.Arguments, inspected.WorkingDirectory, inspected.Environment);
                if (refused is not null)
                {
                    throw new LaunchException(refused);
                }
                resource.ProcessId = runner.ProcessId;
                if (app.Mode == AspireRunMode.HotReload && resource.ProcessId is int process)
                {
                    using CancellationTokenSource attaching = CancellationTokenSource.CreateLinkedTokenSource(cancellation, resource.Stopping.Token, app.Lifetime.Token);
                    if (await workspace.Debug.AttachForHotReloadAsync(process, resource.DebugName, cancellation: attaching.Token) is { } failed)
                    {
                        throw new LaunchException(failed);
                    }
                }
            }
            cancellation.ThrowIfCancellationRequested();
            if (profiling)
            {
                app.ProfileStarted.TrySetResult(null);
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return new AspireProjectSession(resource.Id, resource.ProcessId);
        }
        catch (Exception error)
        {
            if (profiling && !app.ProfileStarted.Task.IsCompleted)
            {
                app.ProfileStarted.TrySetResult(error.Message);
            }
            await StopResourceAsync(app, resource.Id);
            throw;
        }
    }

    private static ServiceStart? ResourceAction(RunConfiguration configuration, AspireResourceRun resource)
    {
        return resource.ResourceName is { } name && configuration.ServiceActions.TryGetValue(name, out ServiceStart action)
            && configuration.ServiceProjects.TryGetValue(name, out string? project) && PathNames.Same(project, resource.Project)
                ? action : null;
    }

    private async Task ProfileEndedAsync(AspireAppRun app, AspireResourceRun resource, ProfilingSession profile)
    {
        try
        {
            await profile.ExitedAsync().ConfigureAwait(false);
            if (resource.Closing)
            {
                return;
            }
            UiThread.Post(() =>
            {
                if (app.Resources.GetValueOrDefault(resource.Id) == resource)
                {
                    app.Execution?.NotifyProcessTerminated(resource.InstanceId, resource.Id, null);
                    BackgroundWork.Track(StopResourceAsync(app, resource.Id));
                }
            });
        }
        catch (Exception error)
        {
            IdeErrors.Report(error);
        }
    }

    public async Task<bool> RestartResourcesAsync(IReadOnlyList<string> names)
    {
        (AspireAppRun App, AspireResourceRun Service)[] targets = apps.SelectMany(app => app.Services
            .Where(service => names.Contains(service.DebugName)).Select(service => (app, service))).ToArray();
        if (targets.Length == 0 || RestartResource is null)
        {
            return false;
        }
        foreach ((AspireAppRun app, AspireResourceRun service) in targets)
        {
            if (!await RestartResource(app, service))
            {
                return false;
            }
        }
        return true;
    }

    private void Finished(AspireAppRun app, AspireResourceRun resource, int? exitCode)
    {
        UiThread.Check();
        if (disposed)
        {
            return;
        }
        app.Execution?.NotifyProcessTerminated(resource.InstanceId, resource.Id, exitCode is int code ? unchecked((uint)code) : null);
        resource.Stopping.Cancel();
        BackgroundWork.Track(workspace.Debug.DetachAsync(resource.DebugName));
        resource.Inspection?.Dispose();
        resource.Runner?.Dispose();
        app.Resources.Remove(resource.Id);
        if (SelectedService == resource)
        {
            SelectedService = null;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Task StopResourceAsync(AspireAppRun app, string id)
    {
        UiThread.Check();
        if (!app.Resources.TryGetValue(id, out AspireResourceRun? resource))
        {
            return Task.CompletedTask;
        }
        return resource.Ending ??= StopOwnedAsync();

        async Task StopOwnedAsync()
        {
            resource.Stopping.Cancel();
            if (resource.EndProfiling is { } endProfiling)
            {
                try
                {
                    await endProfiling();
                }
                catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException or OperationCanceledException)
                {
                    IdeErrors.Report(error);
                }
            }
            else if (resource.Profile is { } profile)
            {
                profile.Kill();
                await profile.ExitedAsync();
                await profile.DisposeAsync();
            }
            await workspace.Debug.DetachAsync(resource.DebugName);
            if (resource.DebugTab is { } tab)
            {
                await workspace.Debug.StopAsync(tab);
            }
            if (resource.Runner is { } runner)
            {
                if (runner.IsRunning)
                {
                    runner.Stop();
                    await runner.Ended;
                }
                runner.Dispose();
            }
            resource.Inspection?.Dispose();
            app.Resources.Remove(id);
            if (SelectedService == resource)
            {
                SelectedService = null;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task StopAppAsync(AspireAppRun app)
    {
        UiThread.Check();
        if (app.Stopping)
        {
            await app.Stopped.Task;
            return;
        }
        app.Stopping = true;
        app.Lifetime.Cancel();
        try
        {
            foreach (string id in app.Resources.Keys.ToArray())
            {
                await StopResourceAsync(app, id);
            }
            app.Runner.Stop();
            if (app.EndpointStarting is { } starting)
            {
                await DisposeEndpointAsync(starting);
            }
            if (app.Interactions is { } interactions)
            {
                await interactions.DisposeAsync();
            }
            foreach (string id in app.Resources.Keys.ToArray())
            {
                await StopResourceAsync(app, id);
            }
            if (app.Runner.IsRunning)
            {
                await app.Runner.Ended;
            }
        }
        finally
        {
            app.Runner.Stop();
            foreach (AspireResourceRun resource in app.Resources.Values)
            {
                resource.Runner?.Stop();
                resource.Profile?.Kill();
            }
            app.Console.SetRunning(false);
            app.Runner.Dispose();
            apps.Remove(app);
            app.Stopped.TrySetResult();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task StopGroupAsync(string group) => Task.WhenAll(apps.Where(app => app.Group == group).ToArray().Select(StopAppAsync));
    public Task StopAllAsync() => Task.WhenAll(apps.ToArray().Select(StopAppAsync));

    public Task CloseAsync()
    {
        UiThread.Check();
        if (disposed)
        {
            return closing ?? Task.CompletedTask;
        }
        disposed = true;
        AspireAppRun[] owned = apps.ToArray();
        foreach (AspireAppRun app in owned)
        {
            app.Stopping = true;
            foreach (AspireResourceRun resource in app.Resources.Values)
            {
                resource.Closing = true;
            }
        }
        lifetime.Cancel();
        List<Task> profiles = [];
        List<(AspireAppRun App, AspireResourceRun[] Resources, Task Debuggers)> captured = [];
        foreach (AspireAppRun app in owned)
        {
            app.Lifetime.Cancel();
            AspireResourceRun[] resources = app.Resources.Values.ToArray();
            HashSet<DebugSession> sessions = [];
            foreach (AspireResourceRun resource in resources)
            {
                resource.Stopping.Cancel();
                Task<ProfilingSession>? nativeStart = resource.StartingProfile
                    ?? (resource.Profile is { Attached: false } profile ? Task.FromResult(profile) : null);
                if (nativeStart is not null)
                {
                    if (nativeStart.IsCompletedSuccessfully && nativeStart.Result is { Attached: false } recording)
                    {
                        if (ReferenceEquals(workspace.Profiling, recording))
                        {
                            workspace.Profiling = null;
                        }
                        if (ReferenceEquals(workspace.Profiled?.Run, recording))
                        {
                            workspace.Profiled = null;
                        }
                    }
                    profiles.Add(resource.ProfileCleanup ??= CloseProfileAsync(nativeStart));
                }
                if (resource.DebugTab?.Session is { } debugged)
                {
                    sessions.Add(debugged);
                }
                foreach (DebugController.RunningReload quiet in workspace.Debug.Running.Where(run => run.Name == resource.DebugName))
                {
                    sessions.Add(quiet.Session);
                }
                resource.Inspection?.Dispose();
            }
            captured.Add((app, resources, Task.WhenAll(sessions.Select(session => session.CloseAsync()))));
            app.Resources.Clear();
            app.Interactions?.Dispose();
            app.Console.SetRunning(false);
        }
        apps.Clear();
        startingPaths.Clear();
        SelectedService = null;
        Task rundown = Task.WhenAll(profiles);
        List<Task> endings = [];
        foreach ((AspireAppRun app, AspireResourceRun[] resources, Task debuggers) in captured)
        {
            Task prerequisite = Task.WhenAll(rundown, debuggers);
            List<Task> pending = [prerequisite, app.Runner.DeferStopAfter(prerequisite)];
            foreach (AspireResourceRun resource in resources)
            {
                if (resource.Runner is { } runner)
                {
                    pending.Add(runner.DeferStopAfter(prerequisite));
                    runner.Dispose();
                }
            }
            if (app.EndpointStarting is { } endpoint)
            {
                pending.Add(DisposeEndpointAfterAsync(prerequisite, endpoint));
            }
            if (app.Interactions is { } interactions)
            {
                pending.Add(interactions.DisposeAsync().AsTask());
            }
            app.Runner.Dispose();
            endings.Add(CompleteAppAsync(app, pending));
        }
        closing = CompleteAll(endings);
        Changed?.Invoke(this, EventArgs.Empty);
        return closing;

        static async Task CompleteAppAsync(AspireAppRun app, IReadOnlyList<Task> pending)
        {
            try
            {
                await Task.WhenAll(pending).ConfigureAwait(false);
            }
            finally
            {
                app.Stopped.TrySetResult();
            }
        }

        async Task CompleteAll(IReadOnlyList<Task> pending)
        {
            try
            {
                await Task.WhenAll(pending).ConfigureAwait(false);
            }
            finally
            {
                lifetime.Dispose();
            }
        }

        static async Task DisposeEndpointAfterAsync(Task prerequisite, Task<AspireExecutionServer> endpoint)
        {
            try
            {
                await prerequisite.ConfigureAwait(false);
            }
            finally
            {
                await DisposeEndpointAsync(endpoint).ConfigureAwait(false);
            }
        }
    }

    internal static Task CloseProfileAsync(Task<ProfilingSession> starting) => Task.Run(async () =>
    {
        ProfilingSession profile;
        try
        {
            profile = await starting.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (profile.Attached)
        {
            return;
        }
        try
        {
            await profile.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                profile.Kill();
                await profile.ExitedAsync().ConfigureAwait(false);
            }
            finally
            {
                await profile.DisposeAsync().ConfigureAwait(false);
            }
        }
    });

    public ValueTask DisposeAsync() => new(CloseAsync());
}

public sealed class AspireAppRun
{
    internal AspireAppRun(RunConfiguration configuration, AspireRunMode mode, string group)
    {
        Configuration = configuration;
        Mode = mode;
        Group = group;
        Runner = new ProcessRunner(Console, takesInput: true);
    }

    public RunConfiguration Configuration { get; }
    public AspireRunMode Mode { get; }
    public string Group { get; }
    public bool IsTest { get; internal init; }
    public OutputView Console { get; } = new();
    public ProcessRunner Runner { get; }
    public int? ProcessId { get; internal set; }
    internal Uri? ResourceServiceEndpoint { get; set; }
    internal string ResourceServiceKey { get; set; } = string.Empty;
    internal Uri? DashboardEndpoint { get; set; }
    internal string DashboardApiKey { get; set; } = string.Empty;
    internal bool ProfileClaimed { get; set; }
    internal (string AppHost, string Name)? ProfileTarget { get; init; }
    internal TaskCompletionSource<string?> ProfileStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public IReadOnlyList<string> KnownContainerIds { get; internal set; } = [];
    internal AspireInteractions? Interactions { get; set; }
    internal CancellationTokenSource Lifetime { get; } = new();
    public IReadOnlyList<AspireResourceRun> Services => [.. Resources.Values];
    private AspireExecutionServer? execution;
    internal AspireExecutionServer? Execution
    {
        get => Volatile.Read(ref execution);
        set => Volatile.Write(ref execution, value);
    }
    internal Task<AspireExecutionServer>? EndpointStarting { get; set; }
    internal Dictionary<string, AspireResourceRun> Resources { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, (string Name, string Project, string IdentityKey)?> OwnedResourceIdentities { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, bool> DebugOverrides { get; } = new(PathNames.Comparer);
    internal TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool Stopping { get; set; }
}

public sealed class AspireResourceRun(string id, string instanceId, string project, LaunchPlan plan)
{
    public string Id { get; } = id;
    public string InstanceId { get; } = instanceId;
    public string Project { get; } = project;
    public LaunchPlan Plan { get; } = plan;
    internal string BuiltStamp { get; } = ReadBuiltStamp(plan);
    internal bool BuildChanged => BuiltStamp != ReadBuiltStamp(Plan);
    public string? ResourceName { get; internal set; }
    internal string IdentityKey => Plan.Environment.GetValueOrDefault("OTEL_RESOURCE_ATTRIBUTES") is { Length: > 0 } attributes
        ? $"{Project}\n{attributes}" : ResourceName ?? Project;
    public int? ProcessId { get; internal set; }
    public DebugTab? DebugTab { get; internal set; }
    internal string DebugName { get; } = $"{plan.Name} [{instanceId}/{id}/{Guid.NewGuid():N}]";
    public LiveApp? Inspection { get; internal set; }
    public OutputView Console { get; } = new();
    internal ProcessRunner? Runner { get; set; }
    internal ProfilingSession? Profile { get; set; }
    internal Task<ProfilingSession>? StartingProfile { get; set; }
    internal Task? ProfileCleanup { get; set; }
    internal volatile bool Closing;
    internal Func<Task>? EndProfiling { get; set; }
    internal Task? Ending { get; set; }
    internal CancellationTokenSource Stopping { get; } = new();

    private static string ReadBuiltStamp(LaunchPlan launch)
    {
        string? output = Path.GetDirectoryName(launch.Built ?? launch.Program);
        return output is null || !Directory.Exists(output) ? string.Empty : string.Join('|', Directory.EnumerateFiles(output)
            .Where(file => Path.GetExtension(file).ToLowerInvariant() is ".dll" or ".pdb" or ".json")
            .Order(StringComparer.Ordinal).Select(file => new FileInfo(file))
            .Select(file => $"{file.Name}:{file.Length}:{file.LastWriteTimeUtc.Ticks}"));
    }
}
