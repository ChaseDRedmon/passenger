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

    /// <summary>Restarts a service through its AppHost (the window's resource commands): stopped, its project built again
    /// when asked, and started, or restarted; done once asked.</summary>
    public Func<AspireAppRun, AspireResourceRun, bool, CancellationToken, Task>? RestartCommand { get; set; }

    /// <summary>Attaches to a service held at its start as the start comes, once: the run of services' one attach
    /// (BuildRunController's), null once attached, else why not.</summary>
    public Func<AspireAppRun, AspireResourceRun, RunStart, CancellationToken, Task<string?>>? AttachStart { get; set; }

    /// <summary>What a person chose for a service of a configuration's run as it ran (debugged, or not), which its first start
    /// in a run that brings the configuration up again keeps; null when nothing was chosen.</summary>
    public Func<string, string, bool?>? Chosen { get; set; }
    public Func<AspireAppRun, CancellationToken, Task<AspireInteractions?>>? ConnectInteractions { get; set; }
    public Func<AspireAppRun, AspireResourceRun, LaunchPlan, CancellationToken, Task<ProfilingSession>>? ProfileService { get; set; }

    /// <summary>Whether the Debug window's chosen tab is another program's than any service's (a plain launch beside the
    /// AppHost), which the Aspire window's resources read again do not take back: only a person's choice of a resource does.</summary>
    public bool OtherProgramChosen { get; private set; }

    public void SelectService(AspireResourceRun? service)
    {
        UiThread.Check();
        OtherProgramChosen = false;
        if (SelectedService != service)
        {
            SelectedService = service;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SelectDebugger(DebugTab? tab)
    {
        AspireResourceRun? service = tab is null ? null : apps.SelectMany(app => app.Services).FirstOrDefault(service => service.DebugTab == tab);
        SelectService(service);
        OtherProgramChosen = tab is not null && service is null;
    }

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
            app.Choose(((IRunService)service).Name, debugging);
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
        app.Choose(selection.Resource.DisplayName, debugging);
    }

    public bool IsAspire(RunConfiguration configuration) => configuration.Aspire
        || workspace.Projects.Any(project => project.IsAspireHost && PathNames.Same(project.Path, configuration.Project));

    /// <summary>The AppHost's own addresses, as its launch profile gives them, that its dashboard, telemetry and resource
    /// service serve on, which over HTTP serve HTTP.</summary>
    private static readonly string[] AppHostAddresses = ["ASPNETCORE_URLS", "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", "ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL",
        "ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL", "DOTNET_DASHBOARD_OTLP_ENDPOINT_URL", "DOTNET_DASHBOARD_OTLP_HTTP_ENDPOINT_URL", "DOTNET_RESOURCE_SERVICE_ENDPOINT_URL"];

    /// <summary>The AppHosts of an Aspire configuration (it and those it links) that serve HTTPS on this machine, by name, as
    /// their launch profiles say: what the development certificate is checked for before any of them starts. Throws a
    /// <see cref="LaunchException"/> for a single-file AppHost's profile that does not read.</summary>
    public IReadOnlyList<string> ServingHttps(RunConfiguration configuration) =>
        [.. new[] { configuration }.Concat(configuration.LinkedAppHosts
            .Select(path => workspace.Configurations.All.FirstOrDefault(candidate => candidate.Aspire && PathNames.Same(candidate.Project, path)))
            .OfType<RunConfiguration>())
            .Where(candidate => (Path.GetExtension(candidate.Project).Equals(".cs", StringComparison.OrdinalIgnoreCase)
                ? ReadFileProfile(candidate, Path.GetDirectoryName(candidate.Project)!) : candidate).Environment
                .Where(variable => AppHostAddresses.Contains(variable.Key))
                .SelectMany(variable => variable.Value.Split(';'))
                .Any(url => Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri) && uri.Scheme == "https"
                    && (uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))))
            .Select(candidate => Path.GetFileNameWithoutExtension(candidate.Project))];

    /// <summary>Starts an Aspire configuration's AppHosts, each built first as it says, and once each is connected to answers
    /// null, else why not. <paramref name="https"/> is how its HTTPS is served, as the start worked out before: over HTTP, the
    /// AppHosts' own addresses and the services they start serve HTTP for this run.</summary>
    public async Task<string?> StartAsync(RunConfiguration configuration, RunStart mode,
        Func<BuildStep, Task<BuildOutcome>> build, CancellationToken cancellation = default, HttpsChoice? https = null)
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
            https ??= HttpsChoice.None;
            foreach (RunConfiguration chosen in configurations)
            {
                AspireAppRun app = await StartAppAsync(chosen, mode, group, build, starting.Token, profileTarget, https, said: started.Count == 0 ? https.Said : []);
                started.Add(app);
            }
            if (mode == RunStart.Profile)
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

    private async Task<AspireAppRun> StartAppAsync(RunConfiguration configuration, RunStart mode, string group,
        Func<BuildStep, Task<BuildOutcome>> build, CancellationToken cancellation, (string AppHost, string Name)? profileTarget, HttpsChoice https,
        IReadOnlyList<string> said)
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
        AspireAppRun app = new(configuration, mode, group)
        {
            ProfileTarget = profileTarget,
            OverHttp = https.OverHttp is not null,
            Restarting = (run, resource, build, cancel) => (RestartCommand ?? throw new InvalidOperationException("Aspire resource restart is unavailable"))(run, resource, build, cancel),
        };
        if (https.OverHttp is { } note)
        {
            app.Notes.Add(note);
        }
        foreach (string line in said)
        {
            app.Console.Append($"{line}\n", error: true);
        }
        LastAppConsole = app.Console;
        apps.Add(app);
        try
        {
            AspireExecutionServer execution = await StartEndpointAsync(app, cancellation);
            app.Lifetime.Token.ThrowIfCancellationRequested();
            cancellation.ThrowIfCancellationRequested();
            Dictionary<string, string> environment = new(plan.Environment, StringComparer.Ordinal);
            if (app.OverHttp)
            {
                // Its dashboard, telemetry and resource service serve HTTP for this run, which Aspire takes once told it may.
                foreach (string address in AppHostAddresses)
                {
                    if (environment.GetValueOrDefault(address) is { } urls && urls.Contains("https:", StringComparison.OrdinalIgnoreCase))
                    {
                        environment[address] = urls.Replace("https:", "http:", StringComparison.OrdinalIgnoreCase);
                    }
                }
                foreach ((string name, string value) in DevelopmentCertificate.OverHttp(environment))
                {
                    environment[name] = value;
                }
                environment["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";
            }
            foreach ((string name, string value) in execution.Environment)
            {
                environment[name] = value;
            }
            environment.Remove("DEBUG_SESSION_SERVER_CERTIFICATE");
            environment["DEBUG_SESSION_RUN_MODE"] = mode == RunStart.Debug ? "Debug" : "NoDebug";
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
        if (app.OverHttp)
        {
            // No certificate for this run: a service told to serve HTTPS serves HTTP where it would have, as its AppHost does.
            foreach ((string name, string value) in DevelopmentCertificate.OverHttp(environment))
            {
                environment[name] = value;
            }
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
        string? failure = null;
        try
        {
            profiling = app.Mode == RunStart.Profile && !apps.Any(candidate => candidate.Group == app.Group && candidate.ProfileClaimed)
                && (app.ProfileTarget is not { } selected || PathNames.Same(selected.AppHost, app.Configuration.Project)
                    && resource.ResourceName == selected.Name && app.Configuration.ServiceProjects.TryGetValue(selected.Name, out string? selectedProject)
                    && PathNames.Same(selectedProject, resource.Project));
            if (profiling)
            {
                app.ProfileClaimed = true;
            }
            // Held for the debugger (quietly for Hot Reload) as the run of services holds it: from its first start in this run,
            // as a person chose it in an earlier one, else as the configuration and DCP's request say.
            RunStart? held = profiling ? null : app.Starting(resource, () => FirstHold(app, resource, request));
            resource.Inspection = held != RunStart.HotReload ? LiveApp.For(plan, workspace.Overlays) : null;
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
            else
            {
                if (held is not null)
                {
                    // Held at its runtime's start, its code open to Hot Reload, until the debugger attached to it lets it go.
                    inspected = inspected with
                    {
                        Environment = new Dictionary<string, string>(inspected.Environment, StringComparer.Ordinal)
                        {
                            [RuntimeVariables.DefaultDiagnosticPortSuspend] = "1",
                            [RuntimeVariables.ModifiableAssemblies] = "debug",
                        },
                    };
                }
                ProcessRunner runner = resource.Runner = new ProcessRunner(resource.Console, takesInput: true);
                runner.OutputReceived += (_, output) => app.Execution?.NotifyServiceLog(request.DcpInstanceId, request.SessionId, output.Text, output.IsError);
                runner.Exited += (_, code) => Finished(app, resource, code);
                string? refused = runner.Start(inspected.Program, inspected.Arguments, inspected.WorkingDirectory, inspected.Environment);
                if (refused is not null)
                {
                    throw new LaunchException(refused);
                }
                resource.ProcessId = runner.ProcessId;
                resource.Held = held;
                // The run of services' one attach to this start, whose session lets it go.
                if (held is { } purpose && await (AttachStart ?? throw new LaunchException("The debugger is unavailable"))(app, resource, purpose, cancellation) is { } unattached)
                {
                    throw new LaunchException(unattached);
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
            failure = error.Message;
            if (profiling && !app.ProfileStarted.Task.IsCompleted)
            {
                app.ProfileStarted.TrySetResult(error.Message);
            }
            await StopResourceAsync(app, resource.Id);
            throw;
        }
        finally
        {
            app.Landed(resource, failure);
        }
    }

    /// <summary>What a service is held at its start for, its first start in a run: as a person chose it as the configuration
    /// last ran, else debugged where the configuration or DCP's request says, quietly for Hot Reload under Run with Hot
    /// Reload, else unheld (and every one unheld under Profile but the one profiled).</summary>
    private RunStart? FirstHold(AspireAppRun app, AspireResourceRun resource, AspireProjectLaunch request)
    {
        if (app.Mode == RunStart.Profile)
        {
            return null;
        }
        bool debugging = Chosen?.Invoke(app.Configuration.Name, ((IRunService)resource).Name)
            ?? (ResourceAction(app.Configuration, resource) is { } action ? action == ServiceStart.Debug : request.Mode == "Debug");
        return debugging ? RunStart.Debug : app.Mode == RunStart.HotReload ? RunStart.HotReload : null;
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

    private void Finished(AspireAppRun app, AspireResourceRun resource, int? exitCode)
    {
        UiThread.Check();
        if (disposed)
        {
            return;
        }
        app.Execution?.NotifyProcessTerminated(resource.InstanceId, resource.Id, exitCode is int code ? unchecked((uint)code) : null);
        resource.Stopping.Cancel();
        // Its own session alone: a later start of its service has its own, of the same name.
        if (resource.Debugged is { State: not DebugState.Ended } debugged)
        {
            BackgroundWork.Track(workspace.Debug.DetachAsync(debugged));
        }
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
            // Its session before its program, its own alone: a later start of its service has its own, of the same name.
            if (resource.DebugTab is { } tab)
            {
                await workspace.Debug.StopAsync(tab);
            }
            else if (resource.Debugged is { State: not DebugState.Ended } debugged)
            {
                await workspace.Debug.DetachAsync(debugged);
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
            // Stop's order, every run of services': the sessions and the recording reaching its services end first.
            await app.EndSessionsAsync(workspace.Debug);
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
                // Its own session (in its tab, or quietly for Hot Reload) ends before its program, as Stop's order has it.
                if ((resource.DebugTab?.Session ?? resource.Debugged) is { } debugged)
                {
                    sessions.Add(debugged);
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

/// <summary>An Aspire AppHost the window started, a run of services (<see cref="ServiceRun"/>) whose services DCP asks the
/// IDE to start on this machine: each start is the controller's (<c>StartResourceAsync</c>), held at its runtime's start as
/// the model says (<see cref="ServiceRun.HeldFor"/>) and attached to as it starts, once (<see cref="AttachesItsStarts"/>).
/// What is its own: a service restarts through its AppHost's resource commands (<see cref="RestartAsync"/>), its program
/// is the newest started for its name, and its session is named by its AppHost too.</summary>
public sealed class AspireAppRun : ServiceRun
{
    /// <summary>The services started at least once in this run, whose hold is the model's from then on.</summary>
    private readonly HashSet<string> decided = new(StringComparer.Ordinal);

    /// <summary>Each service's next start, as a restart waits for it: its failure, or null once started and attached to.</summary>
    private readonly Dictionary<string, TaskCompletionSource<string?>> nextStarts = new(StringComparer.Ordinal);

    private long started;

    internal AspireAppRun(RunConfiguration configuration, RunStart mode, string group)
    {
        Configuration = configuration;
        How = mode;
        Group = group;
        Runner = new ProcessRunner(Console, takesInput: true);
    }

    public RunConfiguration Configuration { get; }
    public RunStart Mode => How;
    public override string Group { get; }
    public override string Name => Configuration.Name;
    public bool IsTest { get; internal init; }

    /// <summary>Whether it and the services it starts serve HTTP for this run where they would serve HTTPS, there being no
    /// trusted development certificate to serve it with (<see cref="HttpsChoice"/>).</summary>
    public bool OverHttp { get; internal init; }
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

    /// <summary>The services it runs now, in the order they started.</summary>
    public override IReadOnlyList<AspireResourceRun> Services => [.. Resources.Values.OrderBy(resource => resource.Sequence)];
    private AspireExecutionServer? execution;
    internal AspireExecutionServer? Execution
    {
        get => Volatile.Read(ref execution);
        set => Volatile.Write(ref execution, value);
    }
    internal Task<AspireExecutionServer>? EndpointStarting { get; set; }
    internal Dictionary<string, AspireResourceRun> Resources { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, (string Name, string Project, string IdentityKey)?> OwnedResourceIdentities { get; } = new(StringComparer.Ordinal);
    internal TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool Stopping { get; set; }

    /// <summary>Restarts a service through its AppHost (the controller's, the window's resource commands): its stop, its
    /// project's build when asked, and its start, or its restart; done once asked.</summary>
    internal Func<AspireAppRun, AspireResourceRun, bool, CancellationToken, Task>? Restarting { get; set; }

    public override bool IsStopped => Stopping;

    public override bool AttachesItsStarts => true;

    public override bool RestartsToRunWithoutDebugger => true;

    /// <summary>A service's build is written over its files, which Windows keeps while it runs: built between its stop and
    /// its start (<see cref="RestartAsync"/>'s build).</summary>
    public override bool BuildsBeforeRestart => false;

    /// <summary>By its AppHost too, as two AppHosts may each run a resource of one name.</summary>
    public override string SessionName(IRunService service) => $"{Path.GetFileNameWithoutExtension(Configuration.Project)}: {service.Name}";

    /// <summary>The newest start of a service of that name still running.</summary>
    public override IRunService? ServiceNamed(string name) =>
        Resources.Values.Where(resource => ((IRunService)resource).Name == name && !resource.Stopping.IsCancellationRequested).MaxBy(resource => resource.Sequence);

    public override bool Refused(Exception failure) => failure is IOException or InvalidOperationException or TimeoutException or LaunchException or InvalidDataException
        or System.ComponentModel.Win32Exception;

    /// <summary>Nothing to write: a service's next start reads its hold.</summary>
    protected override Task HoldsChangedAsync(CancellationToken cancel) => Task.CompletedTask;

    /// <summary>A service's program on this machine: the newest start of its name, with its inspected app, open to Hot Reload
    /// when it started held (with DOTNET_MODIFIABLE_ASSEMBLIES=debug).</summary>
    public override Task<ServiceProgram?> ProgramAsync(IRunService service, Task up, CancellationToken stop) =>
        ServiceNamed(service.Name) is AspireResourceRun { ProcessId: int id } running
            ? Task.FromResult<ServiceProgram?>(new ServiceProgram(id, null, null) { HotReload = running.Held is not null, App = running.Inspection })
            : throw new ServiceUnreachedException($"{service.Name} of {Name} has no program running");

    /// <summary>Restarts services through the AppHost, one after another, each waited for until its next start has started
    /// and been attached to (a held one) as it started; built between its stop and its start when asked.</summary>
    public override async Task<string?> RestartAsync(IReadOnlyList<IRunService> services, CancellationToken cancel, string? why = null, bool build = false)
    {
        foreach (IRunService service in services)
        {
            // The start given, whose program its ended session may have ended with it, else the newest of its name.
            if ((service as AspireResourceRun ?? ServiceNamed(service.Name)) is not AspireResourceRun running)
            {
                return $"{service.Name} of {Name} is not running";
            }
            Console.Append($"Restarting {service.Name}{(why is null ? string.Empty : $": {why}")}\n");
            Task<string?> next = NextStart(service.Name);
            await (Restarting ?? throw new InvalidOperationException("Aspire resource restart is unavailable"))(this, running, build, cancel);
            if (await next.WaitAsync(StartLimit, cancel) is { } failed)
            {
                return $"{service.Name} did not start again: {failed}";
            }
        }
        return null;
    }

    /// <summary>How long a restart waits for its service's next start, which DCP may take a while to ask for.</summary>
    private static readonly TimeSpan StartLimit = TimeSpan.FromMinutes(3);

    /// <summary>A service's next start, its failure or null.</summary>
    private Task<string?> NextStart(string name)
    {
        if (!nextStarts.TryGetValue(name, out TaskCompletionSource<string?>? next))
        {
            nextStarts[name] = next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return next.Task;
    }

    /// <summary>A service's start is under way: numbered, and, its first in this run, what it is held at its start for decided
    /// (<paramref name="first"/>: a person's choice in an earlier run, else the configuration's and DCP's), the model's hold
    /// from then on. What it is held for now.</summary>
    internal RunStart? Starting(AspireResourceRun resource, Func<RunStart?> first)
    {
        resource.Sequence = ++started;
        string name = ((IRunService)resource).Name;
        if (decided.Add(name) && first() is { } held)
        {
            Brought([(resource, held)]);
        }
        return HeldFor(name);
    }

    /// <summary>A service's start has landed: started, attached to where held, or failed (<paramref name="failure"/>).</summary>
    internal void Landed(AspireResourceRun resource, string? failure)
    {
        if (nextStarts.Remove(((IRunService)resource).Name, out TaskCompletionSource<string?>? next))
        {
            next.TrySetResult(failure);
        }
    }

    /// <summary>A person's choice of a service: debugged from its next start, or run without the debugger.</summary>
    internal void Choose(string name, bool debugging)
    {
        decided.Add(name);
        _ = HoldAsync(name, debugging ? RunStart.Debug : null, CancellationToken.None);
    }

    /// <summary>The service's start the session attached to keeps its tab (which the Aspire window chooses) and session.</summary>
    public override void Attached(IRunService service, ServiceProgram program, DebugTab? tab, DebugSession? debugged)
    {
        if (Resources.Values.FirstOrDefault(resource => resource.ProcessId == program.Id) is not { } run)
        {
            return;
        }
        run.DebugTab = tab;
        run.Debugged = debugged;
        if (tab is not null)
        {
            tab.StateChanged += (_, _) =>
            {
                if (run.DebugTab == tab && tab.Session is { State: DebugState.Ended })
                {
                    run.DebugTab = null;
                }
            };
        }
    }
}

/// <summary>One start of a service DCP asked the IDE for, a service of its AppHost's run (<see cref="IRunService"/>, by its
/// resource's name): its program, console, session and inspected app.</summary>
public sealed class AspireResourceRun(string id, string instanceId, string project, LaunchPlan plan) : IRunService
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

    /// <summary>The session attached to this start (in its tab, or quietly for Hot Reload), which its end lets go.</summary>
    internal DebugSession? Debugged { get; set; }

    /// <summary>What it started held for (the debugger, quietly for Hot Reload), or null when it started unheld.</summary>
    internal RunStart? Held { get; set; }

    /// <summary>Its start's number in its AppHost's run, the newest of a name its service's program.</summary>
    internal long Sequence { get; set; }
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

    string IRunService.Name => ResourceName ?? Path.GetFileNameWithoutExtension(Project);

    bool IRunService.Starts => true;

    private static string ReadBuiltStamp(LaunchPlan launch)
    {
        string? output = Path.GetDirectoryName(launch.Built ?? launch.Program);
        return output is null || !Directory.Exists(output) ? string.Empty : string.Join('|', Directory.EnumerateFiles(output)
            .Where(file => Path.GetExtension(file).ToLowerInvariant() is ".dll" or ".pdb" or ".json")
            .Order(StringComparer.Ordinal).Select(file => new FileInfo(file))
            .Select(file => $"{file.Name}:{file.Length}:{file.LastWriteTimeUtc.Ticks}"));
    }
}
