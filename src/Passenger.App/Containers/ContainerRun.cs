using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Containers;

/// <summary>A .NET service of a container run: its Compose service, the project it runs and that project's assembly,
/// where Passenger's build puts it (mounted into the container in fast mode), what the configuration does with it, the
/// stage fast mode stops its image at, and the folder its image's paths map back to (<see cref="Paths"/>).</summary>
public sealed record DotnetService(string Service, string Project, string Assembly, string Output, ServiceStart Action, string? Stage, ContainerPaths Paths)
{
    /// <summary>Whether this run starts it.</summary>
    public bool Starts => Action != ServiceStart.DontStart;
}

/// <summary>How a container configuration is started: run; debugged, its .NET services (those marked Debug, else every one)
/// held at their start until a session per service attaches; run with Hot Reload, every .NET service held and attached
/// to quietly, no breakpoint sent, so the edits reach it through the debugger (dotnet watch would need the SDK in its image);
/// or profiled, one .NET service (<see cref="ContainerPlan.Profiled"/>) held at its start until the profiler records it there.</summary>
public enum ContainerStart
{
    Run,
    Debug,
    HotReload,
    Profile,
}

/// <summary>What a container configuration runs, worked out once from Compose's own model of its files: the Compose
/// words every command takes (<c>compose -p NAME -f … -f override --env-file … --profile …</c>, the override Passenger
/// wrote into the folder's state last), the services it starts, the .NET ones Passenger builds on the host, those Compose
/// watches, the browser's address once known, and the paths of its images. Data; <see cref="ContainerRun"/> runs it.</summary>
public sealed record ContainerPlan(string Name, string Folder, string ProjectName, IReadOnlyList<string> Compose, IReadOnlyList<string> Start,
    IReadOnlyList<DotnetService> Dotnet, IReadOnlyList<string> Backing, IReadOnlyList<string> Watched, ContainerMode Mode, bool KeepBacking,
    string? BrowserUrl, string? LaunchService, int? LaunchPort, ContainerPaths Paths)
{
    /// <summary>The projects Run builds first: each .NET service's that starts, once, in fast mode (images mode builds its
    /// images as Compose says, from their Dockerfiles).</summary>
    public IReadOnlyList<string> Builds => Mode == ContainerMode.Fast
        ? [.. Dotnet.Where(d => d.Starts).Select(d => d.Project).Distinct(StringComparer.Ordinal)]
        : [];

    /// <summary>The services Rerun may restart, those Passenger builds.</summary>
    public IEnumerable<DotnetService> Restartable => Dotnet.Where(d => d.Starts);

    /// <summary>Writes Passenger's override into the state, which <see cref="Compose"/> names: after the host's build, with the
    /// services held at their start (for the debugger or the profiler), and those of them whose code the debugger may change
    /// (DOTNET_MODIFIABLE_ASSEMBLIES=debug, which a profiled service goes without, since it would measure code built for
    /// changing).</summary>
    public Func<IReadOnlyCollection<string>, IReadOnlyCollection<string>, DevelopmentCertificate.Exported?, CancellationToken, Task> WriteOverride { get; init; } = (_, _, _, _) => Task.CompletedTask;

    /// <summary>The .NET services Debug debugs: those the configuration marks Debug, else every .NET service it starts (Run
    /// with Hot Reload: every one it starts).</summary>
    public IReadOnlyList<DotnetService> Debugged(bool marked) =>
        [.. Dotnet.Where(d => d.Starts && (Start.Count == 0 || Start.Contains(d.Service)))
            .Where(d => !marked || d.Action == ServiceStart.Debug || !Dotnet.Any(m => m.Action == ServiceStart.Debug && (Start.Count == 0 || Start.Contains(m.Service))))];

    /// <summary>The one .NET service Profile records, the profiler window following one program: of those marked Debug (else
    /// every .NET service it starts), the launched service, else the first; null when it starts none.</summary>
    public DotnetService? Profiled() => Debugged(marked: true) is { } candidates ? candidates.FirstOrDefault(d => d.Service == LaunchService) ?? candidates.FirstOrDefault() : null;

    /// <summary>Whether this run starts a .NET service (as itself, not as another's dependency alone).</summary>
    public bool Starts(string service) => Dotnet.Any(d => d.Service == service && d.Starts) && (Start.Count == 0 || Start.Contains(service));

    /// <summary>The <c>dotnet publish -t:PublishContainer</c> words that make an SDK container's image as deployed, or null.</summary>
    public IReadOnlyList<string>? Publish { get; init; }

    /// <summary>The folder's place in the state for its container runs, where the development certificate is exported.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>The .NET services that serve HTTPS (Kestrel told an https address or port, or a port of HTTPS's), which take
    /// the development certificate, or are told to serve HTTP instead when there is none.</summary>
    public IReadOnlyList<string> Https { get; init; } = [];

    /// <summary>The services <c>up</c> makes: those it starts and what they depend on (every service when it names none),
    /// whose containers it would make again over any of the project's it finds.</summary>
    public IReadOnlyList<string> Touched { get; init; } = [];

    /// <summary>What the run says of itself as it comes up (a service marked Don't start that another depends on, one run as
    /// the person on this Linux engine), after its status.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>Works out a container configuration's <see cref="ContainerPlan"/>: Compose's model of its files (a Dockerfile
/// or SDK container configuration's is a compose file Passenger writes for it, one service), the project each service
/// runs (its Dockerfile's <c>ENTRYPOINT ["dotnet", "X.dll"]</c> by assembly, else its <c>dotnet publish X.csproj</c>, else
/// the Docker Compose project's, else the person's choice), each project's build output and Visual Studio's container
/// properties through MSBuild, and the override file of Passenger's own, which every command takes as one more <c>-f</c>.
/// Throws a <see cref="LaunchException"/> saying why it cannot run.</summary>
public static class ContainerPlans
{
    /// <summary>Where fast mode mounts Passenger's build of a service's project inside its container.</summary>
    public const string AppFolder = "/passenger/app";

    public static async Task<ContainerPlan> ResolveAsync(RunConfiguration configuration, IReadOnlyList<ProjectModel> projects, WorkspaceModel? workspace,
        string stateFolder, ContainerEngine engine, IReadOnlyList<string> labels, CancellationToken cancel)
    {
        string folder = workspace?.Folder ?? throw new LaunchException("Nothing to run: no folder is open");
        string state = Path.Combine(StateFolderOf(stateFolder, folder), "containers");
        Directory.CreateDirectory(state);
        string file = Slug(configuration.Name);
        List<string> files;
        string runsIn;
        Dictionary<string, string> fixedProjects = new(configuration.ServiceProjects, StringComparer.Ordinal);
        IReadOnlyDictionary<string, string> properties = new Dictionary<string, string>();
        ProjectModel? single = null;
        ContainerMode mode = configuration.Mode;
        IReadOnlyList<string>? publish = null;
        if (configuration.Kind == RunKind.Compose)
        {
            files = [.. configuration.ComposeFiles];
            if (files.Count == 0)
            {
                throw new LaunchException($"{configuration.Name} cannot run: it names no compose file");
            }
            if (files.FirstOrDefault(f => !File.Exists(f)) is { } missing)
            {
                throw new LaunchException($"{configuration.Name} cannot run: its compose file {missing} is not there");
            }
            runsIn = Path.GetDirectoryName(files[0])!;
        }
        else
        {
            single = projects.FirstOrDefault(p => PathNames.Same(p.Path, configuration.Project))
                ?? throw new LaunchException($"{configuration.Name} cannot run: its project {Path.GetFileName(configuration.Project)} is not loaded in the open folder");
            properties = await LaunchPlan.EvaluateAsync(single, configuration.Framework.Length > 0 ? configuration.Framework : null,
                ["TargetPath", "TargetDir", "AssemblyName", "DockerfileContext", "DockerfileFile", "DockerfileFastModeStage", "ContainerDevelopmentMode",
                    "DockerfileBuildArguments", "DockerfileRunArguments", "DockerfileTag", "ContainerBaseImage", "ContainerPort", "RuntimeIdentifier", "SelfContained"],
                workspace, cancel, configuration.Kind == RunKind.SdkContainer ? "ComputeContainerBaseImage" : null);
            if (configuration.Kind == RunKind.Dockerfile && string.Equals(properties.GetValueOrDefault("ContainerDevelopmentMode"), "Regular", StringComparison.OrdinalIgnoreCase))
            {
                mode = ContainerMode.Images;
            }
            // As deployed, the SDK makes the image itself (PublishContainer, into the engine), named for the run alone.
            if (configuration.Kind == RunKind.SdkContainer && mode == ContainerMode.Images)
            {
                string repository = $"passenger-{Slug(configuration.Name)}-{ContainerLabels.FolderValue(workspace.Folder)[..8]}";
                properties = new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase) { ["PassengerImage"] = $"{repository}:{SdkImageTag}" };
                publish = ["publish", single.Path, "-t:PublishContainer", "-c", "Debug", $"-p:ContainerRepository={repository}", $"-p:ContainerImageTag={SdkImageTag}",
                    "-nologo"];
            }
            string service = DockerFiles.ServiceName(single.Name.Split(' ')[0]);
            fixedProjects[service] = single.Path;
            string composed = Path.Combine(state, $"{file}.compose.yaml");
            WriteJson(composed, SingleService(configuration, single, properties, service, workspace, mode));
            files = [composed];
            runsIn = single.Folder;
        }
        List<string> compose = Words(files, configuration, runsIn);
        ComposeModel model;
        try
        {
            model = await ComposeModel.ReadAsync(engine, compose, runsIn, cancel);
        }
        catch (ContainerCommandException said)
        {
            throw new ComposeModelException(configuration.Name, said.Message);
        }
        string projectName = model.Name.Length > 0 ? model.Name : Slug(Path.GetFileName(runsIn));
        compose.InsertRange(1, ["-p", projectName]);

        List<DotnetService> dotnet = [];
        foreach (ComposeService service in model.Services)
        {
            if (DotnetProject(service, projects, workspace, configuration, fixedProjects) is not { } project)
            {
                continue;
            }
            IReadOnlyDictionary<string, string> its = single is not null && PathNames.Same(project.Path, single.Path) ? properties
                : await LaunchPlan.EvaluateAsync(project, null, ["TargetPath", "TargetDir", "AssemblyName", "DockerfileFastModeStage", "RuntimeIdentifier", "SelfContained"], workspace, cancel);
            string output = its.GetValueOrDefault("TargetDir") is { Length: > 0 } dir ? Path.TrimEndingDirectorySeparator(dir)
                : Path.GetDirectoryName(its.GetValueOrDefault("TargetPath") ?? string.Empty) ?? project.Folder;
            string assembly = its.GetValueOrDefault("AssemblyName") is { Length: > 0 } named ? named : Path.GetFileNameWithoutExtension(project.Path);
            ServiceStart action = configuration.ServiceActions.GetValueOrDefault(service.Name, ServiceStart.Run);
            DockerfileStages? stages = service.DockerfilePath is { } dockerfile && File.Exists(dockerfile) ? DockerfileStages.Read(File.ReadAllText(dockerfile)) : null;
            string? stage = null;
            if (mode == ContainerMode.Fast && action != ServiceStart.DontStart)
            {
                if (its.GetValueOrDefault("RuntimeIdentifier") is { Length: > 0 } rid || string.Equals(its.GetValueOrDefault("SelfContained"), "true", StringComparison.OrdinalIgnoreCase))
                {
                    throw new LaunchException($"{configuration.Name} cannot run {service.Name} in fast mode: {project.Name} is built for one runtime"
                        + $"{(its.GetValueOrDefault("RuntimeIdentifier") is { Length: > 0 } r ? $" ({r})" : string.Empty)}, and fast mode runs a framework-dependent build with dotnet; "
                        + "choose Build images, as deployed in Run > Edit Configurations");
                }
                if (stages is not null)
                {
                    stage = stages.FastStage(its.GetValueOrDefault("DockerfileFastModeStage"))?.Name
                        ?? throw new LaunchException($"{configuration.Name} cannot run {service.Name} in fast mode: its Dockerfile has no runtime stage to run Passenger's build in "
                            + "(a stage named base, or one its final stage is FROM); choose Build images, as deployed in Run > Edit Configurations");
                }
            }
            ContainerPaths paths = stages is not null && service.Context is { } context ? ContainerPaths.For(stages, context) : ContainerPaths.None;
            dotnet.Add(new DotnetService(service.Name, project.Path, assembly, output, action, stage, paths));
        }
        HashSet<string> dotnetNames = [.. dotnet.Select(d => d.Service)];
        bool dontStarts = configuration.ServiceActions.Values.Any(a => a == ServiceStart.DontStart);
        List<string> start = configuration.Services.Count > 0 ? [.. configuration.Services.Where(s => configuration.ServiceActions.GetValueOrDefault(s) != ServiceStart.DontStart)]
            : dontStarts ? [.. model.Services.Select(s => s.Name).Where(s => configuration.ServiceActions.GetValueOrDefault(s) != ServiceStart.DontStart)]
            : [];
        // up given no service would start them all, those marked Don't start among them.
        if (start.Count == 0 && (configuration.Services.Count > 0 || dontStarts))
        {
            throw new LaunchException($"{configuration.Name} starts nothing: every service it would start is marked Don't start (Run > Edit Configurations)");
        }
        List<string> touched = start.Count == 0 ? [.. model.Services.Select(s => s.Name)] : Needed(model, start);
        List<string> backing = [.. model.Services.Select(s => s.Name).Where(s => !dotnetNames.Contains(s))];
        List<string> watched = [.. model.Services.Where(s => s.Watches && !dotnetNames.Contains(s.Name) && (start.Count == 0 || start.Contains(s.Name))).Select(s => s.Name)];
        List<string> notes = [.. dotnet.Where(d => !d.Starts && touched.Contains(d.Service))
            .Select(d => $"{d.Service} is marked Don't start, but a service it starts depends on it: Compose starts it from its own image, as its files build it")];
        // As deployed, the image's own user could not read what is kept for the person alone (OverrideAsync's owner).
        if (mode == ContainerMode.Images && OperatingSystem.IsLinux() && DotnetProcesses.UserAndGroup is { } owner)
        {
            foreach (DotnetService runs in dotnet.Where(d => d.Starts && touched.Contains(d.Service)))
            {
                string[] kept = [.. Serves(model[runs.Service]!) ? ["development certificate"] : Array.Empty<string>(),
                    .. !Development(model[runs.Service]!) || UserSecrets(projects.FirstOrDefault(p => PathNames.Same(p.Path, runs.Project))) is null ? Array.Empty<string>() : ["user secrets"]];
                if (kept.Length > 0)
                {
                    notes.Add($"{runs.Service} runs as you ({owner}) on this Linux engine, as fast mode does, so it can read the {string.Join(" and ", kept)} kept for you alone");
                }
            }
        }

        string overrideFile = Path.Combine(state, $"{file}.override.yaml");
        compose.InsertRange(compose.IndexOf("--project-directory"), ["-f", overrideFile]);
        // Written once the host's build is done: what fast mode mounts (its static web assets' folders) is that build's.
        bool pin = Overrides((await engine.StateAsync(cancel: cancel)).ComposeVersion);
        // Each port Passenger pins is chosen once for the plan: every override it writes after (a hold, a restart) keeps it.
        Dictionary<(string Service, int Port), int> pinned = [];
        async Task WriteOverride(IReadOnlyCollection<string> held, IReadOnlyCollection<string> modifiable, DevelopmentCertificate.Exported? certificate, CancellationToken stop) =>
            WriteJson(overrideFile, await OverrideAsync(model, projectName, dotnet, mode, labels, projects, state, pin ? pinned : null, held, modifiable, certificate, stop), overriding: true);
        if (mode == ContainerMode.Images || dotnet.Count == 0)
        {
            await WriteOverride([], [], null, cancel);
        }

        DotnetService? launched = dotnet.FirstOrDefault(d => d.Service == configuration.LaunchService) ?? dotnet.FirstOrDefault(d => d.Starts && (start.Count == 0 || start.Contains(d.Service)));
        string? launchService = configuration.LaunchService.Length > 0 ? configuration.LaunchService : launched?.Service;
        int? launchPort = launchService is null ? null : model[launchService]?.Ports.FirstOrDefault(p => p.Protocol == "tcp")?.Target;
        // Every image built from a folder here maps its paths back, a .NET service's or not.
        ContainerPaths all = model.Services
            .Where(s => s.DockerfilePath is { } dockerfile && File.Exists(dockerfile) && dotnet.All(d => d.Service != s.Name))
            .Aggregate(dotnet.Aggregate(ContainerPaths.None, (map, d) => map.With(d.Paths)),
                (map, s) => map.With(ContainerPaths.For(DockerfileStages.Read(File.ReadAllText(s.DockerfilePath!)), s.Context!)));
        return new ContainerPlan(configuration.Name, runsIn, projectName, compose, start, dotnet, backing, watched, mode, configuration.KeepBackingServices,
            configuration.LaunchBrowser && configuration.BrowserUrl.Length > 0 ? configuration.BrowserUrl : null, launchService, launchPort, all)
        {
            WriteOverride = WriteOverride,
            Publish = publish,
            State = state,
            Https = [.. model.Services.Where(s => dotnet.Any(d => d.Service == s.Name) && Serves(s)).Select(s => s.Name)],
            Touched = touched,
            Notes = notes,
        };
    }

    /// <summary>The services <c>up</c> of these makes: they and what they depend on, as Compose's model says.</summary>
    private static List<string> Needed(ComposeModel model, IReadOnlyList<string> start)
    {
        List<string> needed = [];
        Queue<string> next = new(start);
        while (next.TryDequeue(out string? service))
        {
            if (!needed.Contains(service))
            {
                needed.Add(service);
                foreach (string on in model[service]?.DependsOn ?? [])
                {
                    next.Enqueue(on);
                }
            }
        }
        return needed;
    }

    /// <summary>What holds a .NET service at its runtime's start until a debugger attaches and lets it go.</summary>
    public const string HeldVariable = "DOTNET_DefaultDiagnosticPortSuspend";

    /// <summary>The tag of an SDK container's image Passenger has the SDK make, as deployed.</summary>
    public const string SdkImageTag = "passenger";

    /// <summary>The <c>compose -f … --env-file … --profile … --project-directory …</c> words of files and a configuration.</summary>
    private static List<string> Words(IReadOnlyList<string> files, RunConfiguration configuration, string runsIn)
    {
        List<string> compose = ["compose"];
        foreach (string each in files)
        {
            compose.AddRange(["-f", each]);
        }
        foreach (string env in configuration.EnvFiles)
        {
            compose.AddRange(["--env-file", env]);
        }
        foreach (string profile in configuration.Profiles)
        {
            compose.AddRange(["--profile", profile]);
        }
        compose.AddRange(["--project-directory", runsIn]);
        return compose;
    }

    /// <summary>A Compose configuration's services as Compose's own model has them (Edit Configurations' table), each with
    /// the project it runs as a run would find it, and whether it is built from a Dockerfile here (whose project a person
    /// may choose); a <see cref="ContainerCommandException"/> with what Compose said when it cannot read the files.</summary>
    public static async Task<IReadOnlyList<(ComposeService Service, string? Project, bool Built)>> ServicesAsync(RunConfiguration configuration,
        IReadOnlyList<ProjectModel> projects, ContainerEngine engine, CancellationToken cancel = default)
    {
        List<string> files = [.. configuration.ComposeFiles.Where(File.Exists)];
        if (files.Count == 0)
        {
            throw new ContainerCommandException(configuration.ComposeFiles.Count == 0 ? "It names no compose file" : $"{configuration.ComposeFiles[0]} is not there");
        }
        string runsIn = Path.GetDirectoryName(files[0])!;
        ComposeModel model = await ComposeModel.ReadAsync(engine, Words(files, configuration, runsIn), runsIn, cancel);
        Dictionary<string, string> chosen = new(configuration.ServiceProjects, StringComparer.Ordinal);
        return [.. model.Services.Select(s => (s, DotnetProject(s, projects, null, configuration, chosen)?.Path, s.DockerfilePath is { } file && File.Exists(file)))];
    }

    /// <summary>The project a service runs: its Dockerfile's (by ENTRYPOINT's assembly, else its dotnet publish), else the
    /// person's choice kept in the configuration, else a project of the Docker Compose project named as the service.</summary>
    private static ProjectModel? DotnetProject(ComposeService service, IReadOnlyList<ProjectModel> projects, WorkspaceModel? workspace, RunConfiguration configuration,
        IReadOnlyDictionary<string, string> chosen)
    {
        if (chosen.TryGetValue(service.Name, out string? kept) && projects.FirstOrDefault(p => PathNames.Same(p.Path, kept)) is { } named)
        {
            return named;
        }
        if (service.DockerfilePath is { } dockerfile && File.Exists(dockerfile)
            && Web.ComposeDeclarations.Built(TextSnapshot.From(File.ReadAllText(dockerfile)), service.Context!, projects) is { } built)
        {
            return projects.FirstOrDefault(p => PathNames.Same(p.Path, built));
        }
        if (configuration.Project.Length > 0 && ContainerConfigurations.IsComposeProject(configuration.Project))
        {
            string dcproj = Path.GetFileName(configuration.Project);
            return projects.FirstOrDefault(p => DockerFiles.ServiceName(Path.GetFileNameWithoutExtension(p.Path)) == service.Name
                && (ContainerConfigurations.Text(p.Path) ?? string.Empty).Contains(dcproj, StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }

    /// <summary>A Dockerfile or SDK container configuration as a compose file of one service: its image built from the
    /// project's Dockerfile and context (the project's DockerfileFile and DockerfileContext, else the configuration's, else
    /// the Dockerfile beside it and the solution's folder), or the SDK's base image, with the configuration's environment
    /// and <c>docker run</c> words (ports, env files, volumes, variables).</summary>
    private static JsonObject SingleService(RunConfiguration configuration, ProjectModel project, IReadOnlyDictionary<string, string> properties, string name,
        WorkspaceModel workspace, ContainerMode mode)
    {
        JsonObject service = [];
        List<int> exposed = [];
        if (configuration.Kind == RunKind.Dockerfile)
        {
            string dockerfile = configuration.Dockerfile.Length > 0 ? configuration.Dockerfile
                : properties.GetValueOrDefault("DockerfileFile") is { Length: > 0 } file ? Path.GetFullPath(Path.Combine(project.Folder, file))
                : ContainerConfigurations.Dockerfile(project)
                    ?? throw new LaunchException($"{configuration.Name} cannot run: {project.Name} has no Dockerfile");
            string context = configuration.Context.Length > 0 ? configuration.Context
                : properties.GetValueOrDefault("DockerfileContext") is { Length: > 0 } given ? Path.GetFullPath(Path.Combine(project.Folder, given))
                : workspace.Solution is { } solution ? Path.GetDirectoryName(solution.Path)! : workspace.Folder;
            JsonObject build = new() { ["context"] = context, ["dockerfile"] = dockerfile };
            JsonObject args = [];
            foreach (string word in CommandLine.Split($"{properties.GetValueOrDefault("DockerfileBuildArguments")} {configuration.BuildArguments}"))
            {
                string pair = word.StartsWith("--build-arg", StringComparison.Ordinal) ? word[11..].TrimStart('=') : word;
                if (pair.Split('=', 2) is [{ Length: > 0 } key, var value])
                {
                    args[key] = value;
                }
            }
            if (args.Count > 0)
            {
                build["args"] = args;
            }
            service["build"] = build;
            if (properties.GetValueOrDefault("DockerfileTag") is { Length: > 0 } tag && mode == ContainerMode.Images)
            {
                service["image"] = tag;
            }
            DockerfileStages stages = DockerfileStages.Read(File.Exists(dockerfile) ? File.ReadAllText(dockerfile) : string.Empty);
            DockerfileStage? running = mode == ContainerMode.Fast ? stages.FastStage(properties.GetValueOrDefault("DockerfileFastModeStage")) : stages.Final;
            exposed.AddRange(running is null ? [] : stages.Lineage(running).SelectMany(s => s.Exposed));
        }
        else
        {
            string image = properties.GetValueOrDefault("PassengerImage") is { Length: > 0 } published ? published
                : properties.GetValueOrDefault("ContainerBaseImage") is { Length: > 0 } basis ? basis
                : $"mcr.microsoft.com/dotnet/{(ContainerConfigurations.IsWeb(project, workspace.Projects) ? "aspnet" : "runtime")}:{FrameworkTag(project)}";
            service["image"] = image;
            exposed.AddRange((properties.GetValueOrDefault("ContainerPort") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => int.TryParse(p.Split(':')[0], NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0).Where(n => n > 0));
            if (exposed.Count == 0 && ContainerConfigurations.IsWeb(project, workspace.Projects))
            {
                exposed.Add(8080);
            }
        }
        JsonArray ports = [];
        JsonArray envFiles = [];
        JsonArray volumes = [];
        JsonObject environment = [];
        foreach ((string key, string value) in configuration.Environment)
        {
            environment[key] = value;
        }
        List<string> run = [.. CommandLine.Split($"{properties.GetValueOrDefault("DockerfileRunArguments")} {configuration.RunArguments}")];
        for (int i = 0; i < run.Count; i++)
        {
            string word = run[i];
            string? Next() => i + 1 < run.Count ? run[++i] : null;
            switch (word)
            {
                case "-p" or "--publish":
                    if (Next() is { } port)
                    {
                        ports.Add(port);
                    }
                    break;
                case "-P" or "--publish-all":
                    foreach (int each in exposed.Distinct())
                    {
                        ports.Add(each.ToString(CultureInfo.InvariantCulture));
                    }
                    break;
                case "-e" or "--env":
                    if (Next()?.Split('=', 2) is [{ Length: > 0 } key, var value])
                    {
                        environment[key] = value;
                    }
                    break;
                case "--env-file":
                    if (Next() is { } env)
                    {
                        envFiles.Add(Path.GetFullPath(Path.Combine(project.Folder, env)));
                    }
                    break;
                case "-v" or "--volume":
                    if (Next() is { } volume)
                    {
                        volumes.Add(volume);
                    }
                    break;
            }
        }
        if (ports.Count > 0)
        {
            service["ports"] = ports;
        }
        if (envFiles.Count > 0)
        {
            service["env_file"] = envFiles;
        }
        if (volumes.Count > 0)
        {
            service["volumes"] = volumes;
        }
        if (environment.Count > 0)
        {
            service["environment"] = environment;
        }
        return new JsonObject { ["name"] = $"passenger-{Slug(configuration.Name)}-{ContainerLabels.FolderValue(workspace.Folder)[..8]}", ["services"] = new JsonObject { [name] = service } };
    }

    /// <summary>The image tag of a project's newest .NET (10.0 of net10.0), else 10.0.</summary>
    private static string FrameworkTag(ProjectModel project) =>
        project.TargetFrameworks.Select(f => f.Split('-')[0]).Where(f => f.StartsWith("net", StringComparison.OrdinalIgnoreCase) && Version.TryParse(f[3..], out _))
            .Select(f => Version.Parse(f[3..])).OrderByDescending(v => v).Select(v => $"{v.Major}.{Math.Max(0, v.Minor)}").FirstOrDefault() ?? "10.0";

    /// <summary>The override Passenger lays over the files: Passenger's labels on every service, image and network it makes
    /// (so Stop, the folder's end and the IDE's end take them down; an image built as deployed tagged apart from the one its
    /// files name), and for each .NET service it starts, in fast mode, its image's runtime stage, <c>dotnet
    /// /passenger/app/X.dll</c> as PID 1 with Passenger's build mounted there read-only, and in images mode a Debug build
    /// where its Dockerfile takes BUILD_CONFIGURATION; HTTPS's development certificate and the project's user secrets where
    /// they serve; a service held for the debugger started held by its runtime until the debugger lets it go, its code open
    /// to Hot Reload. JSON, which Compose reads as YAML.</summary>
    private static async Task<JsonObject> OverrideAsync(ComposeModel model, string project, IReadOnlyList<DotnetService> dotnet, ContainerMode mode, IReadOnlyList<string> labels,
        IReadOnlyList<ProjectModel> projects, string state, Dictionary<(string Service, int Port), int>? pinned, IReadOnlyCollection<string> held,
        IReadOnlyCollection<string> modifiable, DevelopmentCertificate.Exported? certificate, CancellationToken cancel)
    {
        JsonObject services = [];
        string? owner = OperatingSystem.IsLinux() ? DotnetProcesses.UserAndGroup : null;
        foreach (ComposeService service in model.Services)
        {
            JsonObject entry = new() { ["labels"] = Labels(labels) };
            if (service.Context is not null)
            {
                entry["build"] = new JsonObject { ["labels"] = Labels(labels) };
                // Built again with Passenger's labels (a .NET service's as Debug): never under the image name its files give,
                // which stays the person's own.
                if (mode == ContainerMode.Images)
                {
                    entry["image"] = $"{project}-{service.Name}:passenger";
                    entry["pull_policy"] = "build";
                }
            }
            // A .NET service marked Don't start that another depends on runs as its files say: Passenger built nothing for it.
            if (dotnet.FirstOrDefault(d => d.Service == service.Name) is { Starts: true } built)
            {
                JsonObject environment = [];
                bool development = Development(service);
                JsonArray volumes = [];
                if (mode == ContainerMode.Fast)
                {
                    // An image of its own stops at the runtime stage, tagged apart from the image a build as deployed makes; an
                    // image it only pulls (an SDK container's base) is run as it is.
                    if (entry["build"] is JsonObject build)
                    {
                        if (built.Stage is { } stage)
                        {
                            build["target"] = stage;
                        }
                        entry["image"] = $"{project}-{service.Name}:passenger-fast";
                        entry["pull_policy"] = "build";
                    }
                    entry["entrypoint"] = new JsonArray("dotnet", $"{AppFolder}/{built.Assembly}.dll");
                    entry["working_dir"] = AppFolder;
                    entry["init"] = true;
                    // Rerun restarts the container, and the engine gives a port of its own choosing a new one at each start:
                    // chosen here once, so a browser open on it stays right.
                    if (pinned is not null && service.Ports.Any(p => ComposeModel.HostPort(p.Published) is null))
                    {
                        entry["ports"] = new JsonArray([.. service.Ports.Select((p, i) =>
                            (JsonNode)Port(p, ComposeModel.HostPort(p.Published) is null ? Pinned(pinned, service.Name, i, p.HostIp) : null))]);
                    }
                    volumes.Add(new JsonObject { ["type"] = "bind", ["source"] = built.Output, ["target"] = AppFolder, ["read_only"] = true });
                    if (!service.Environment.ContainsKey("ASPNETCORE_ENVIRONMENT") && !service.Environment.ContainsKey("DOTNET_ENVIRONMENT"))
                    {
                        environment["ASPNETCORE_ENVIRONMENT"] = "Development";
                        environment["DOTNET_ENVIRONMENT"] = "Development";
                    }
                    if (development && StaticWebAssets(built, state, $"{model.Name}-{service.Name}") is { } assets)
                    {
                        foreach ((string source, string target) in assets.Mounts)
                        {
                            volumes.Add(new JsonObject { ["type"] = "bind", ["source"] = source, ["target"] = target, ["read_only"] = true });
                        }
                        environment["ASPNETCORE_STATICWEBASSETS"] = assets.Manifest;
                    }
                }
                else if (service.DockerfilePath is { } dockerfile && File.Exists(dockerfile) && DockerfileStages.Read(File.ReadAllText(dockerfile)).Declares("BUILD_CONFIGURATION"))
                {
                    JsonObject build = (JsonObject)(entry["build"] ??= new JsonObject());
                    build["args"] = new JsonObject { ["BUILD_CONFIGURATION"] = "Debug" };
                }
                (string Folder, string Id)? secrets = development ? UserSecrets(projects.FirstOrDefault(p => PathNames.Same(p.Path, built.Project))) : null;
                // Linux's engine keeps the host's owners and permissions on a mount: the app runs as the person, who can read the
                // build, the certificate and the user secrets, kept for them alone (the image's own user, as deployed, could not).
                if (owner is not null && (mode == ContainerMode.Fast || Serves(service) || secrets is not null))
                {
                    entry["user"] = owner;
                }
                if (held.Contains(service.Name))
                {
                    environment[HeldVariable] = "1";
                }
                if (modifiable.Contains(service.Name))
                {
                    environment["DOTNET_MODIFIABLE_ASSEMBLIES"] = "debug";
                }
                if (Serves(service) && certificate is { } exported)
                {
                    volumes.Add(new JsonObject { ["type"] = "bind", ["source"] = exported.Folder, ["target"] = "/https", ["read_only"] = true });
                    environment["ASPNETCORE_Kestrel__Certificates__Default__Path"] = $"/https/{Path.GetFileName(exported.File)}";
                    environment["ASPNETCORE_Kestrel__Certificates__Default__Password"] = exported.Password;
                }
                else if (Serves(service))
                {
                    OverHttp(service, environment);
                }
                // .NET reads APPDATA before HOME for user secrets on every system, so whichever user the app runs as finds this
                // project's own folder there, mounted alone.
                if (secrets is { } own)
                {
                    volumes.Add(new JsonObject { ["type"] = "bind", ["source"] = own.Folder, ["target"] = $"{AppData}/Microsoft/UserSecrets/{own.Id}", ["read_only"] = true });
                    environment["APPDATA"] = AppData;
                }
                if (volumes.Count > 0)
                {
                    entry["volumes"] = volumes;
                }
                if (environment.Count > 0)
                {
                    entry["environment"] = environment;
                }
            }
            services[service.Name] = entry;
        }
        JsonObject made = new() { ["services"] = services };
        JsonObject networks = [];
        foreach ((string network, bool external) in model.Networks)
        {
            if (!external)
            {
                networks[network] = new JsonObject { ["labels"] = Labels(labels) };
            }
        }
        if (networks.Count > 0)
        {
            made["networks"] = networks;
        }
        return made;
    }

    /// <summary>Where the container finds an ASP.NET Core app's static web assets in Development, which its build's manifest
    /// names by the host's folders (wwwroot, obj's, packages'): each folder mounted under /passenger/assets, and the manifest
    /// written again with those places into the state, mounted and named by ASPNETCORE_STATICWEBASSETS. Null for an app
    /// with none.</summary>
    private static (IReadOnlyList<(string Source, string Target)> Mounts, string Manifest)? StaticWebAssets(DotnetService built, string state, string name)
    {
        string manifest = Path.Combine(built.Output, $"{built.Assembly}.staticwebassets.runtime.json");
        if (!File.Exists(manifest))
        {
            return null;
        }
        try
        {
            JsonObject read = JsonNode.Parse(File.ReadAllText(manifest)) as JsonObject ?? throw new JsonException("not an object");
            List<(string, string)> mounts = [];
            if (read["ContentRoots"] is JsonArray roots)
            {
                for (int i = 0; i < roots.Count; i++)
                {
                    string root = roots[i]?.GetValue<string>() ?? string.Empty;
                    string target = $"/passenger/assets/{i}/";
                    // A root the build named but never made would stop the app as it starts, as it would on this machine.
                    string source = Directory.Exists(root) ? root : Directory.CreateDirectory(Path.Combine(state, "assets", name, i.ToString(CultureInfo.InvariantCulture))).FullName;
                    mounts.Add((Path.TrimEndingDirectorySeparator(source), target.TrimEnd('/')));
                    roots[i] = target;
                }
            }
            string written = Path.Combine(state, $"{name}.staticwebassets.runtime.json");
            File.WriteAllText(written, read.ToJsonString());
            mounts.Add((written, "/passenger/assets/manifest.json"));
            return (mounts, "/passenger/assets/manifest.json");
        }
        catch (Exception unreadable) when (unreadable is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A service that serves HTTPS told to serve HTTP for this run, there being no certificate to give it (the person
    /// chose so, or the export failed): its https addresses as http, and its HTTPS ports among its HTTP ones, so it listens
    /// where it would and Kestrel finds no HTTPS to want a certificate for.</summary>
    private static void OverHttp(ComposeService service, JsonObject environment)
    {
        if (service.Environment.GetValueOrDefault("ASPNETCORE_URLS") is { } urls && urls.Contains("https:", StringComparison.OrdinalIgnoreCase))
        {
            environment["ASPNETCORE_URLS"] = urls.Replace("https:", "http:", StringComparison.OrdinalIgnoreCase);
        }
        if (service.Environment.GetValueOrDefault("ASPNETCORE_HTTPS_PORTS") is { Length: > 0 } https)
        {
            string http = service.Environment.GetValueOrDefault("ASPNETCORE_HTTP_PORTS") ?? string.Empty;
            environment["ASPNETCORE_HTTP_PORTS"] = string.Join(';', (http + ";" + https).Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct());
            environment["ASPNETCORE_HTTPS_PORTS"] = string.Empty;
        }
    }

    /// <summary>Whether a service runs in Development (as its files say, or with nothing said, as Passenger runs it), where it
    /// reads its user secrets and static web assets.</summary>
    private static bool Development(ComposeService service) =>
        !service.Environment.ContainsKey("ASPNETCORE_ENVIRONMENT") && !service.Environment.ContainsKey("DOTNET_ENVIRONMENT")
        || service.Environment.GetValueOrDefault("ASPNETCORE_ENVIRONMENT") == "Development" || service.Environment.GetValueOrDefault("DOTNET_ENVIRONMENT") == "Development";

    /// <summary>Whether a service serves HTTPS: Kestrel told an https address or an HTTPS port, or a port of HTTPS's.</summary>
    private static bool Serves(ComposeService service) =>
        service.Environment.GetValueOrDefault("ASPNETCORE_URLS")?.Contains("https:", StringComparison.OrdinalIgnoreCase) == true
        || service.Environment.ContainsKey("ASPNETCORE_HTTPS_PORTS") || service.Ports.Any(p => p.Target is 443 or 8081);

    /// <summary>Where a .NET service in its container finds its user secrets: APPDATA, which Passenger sets there.</summary>
    public const string AppData = "/passenger/appdata";

    /// <summary>The folder user secrets are kept in on this machine in place of .NET's own (APPDATA's Microsoft/UserSecrets
    /// on Windows, ~/.microsoft/usersecrets elsewhere): read for that alone, so a test's run never mounts the person's own.</summary>
    public const string UserSecretsVariable = "PASSENGER_USER_SECRETS";

    /// <summary>The folder of a project's own user secrets on this machine and its id, when it has an id that names one
    /// folder (no separator, never a dot or two alone) and that folder is there.</summary>
    private static (string Folder, string Id)? UserSecrets(ProjectModel? project)
    {
        if (project?.Property(Passenger.Host.CarriedProperties.UserSecretsId) is not { Length: > 0 } id || id is "." or ".."
            || id.IndexOfAny([.. Path.GetInvalidFileNameChars(), '/', '\\']) >= 0)
        {
            return null;
        }
        string root = Environment.GetEnvironmentVariable(UserSecretsVariable) is { Length: > 0 } given ? given
            : OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets");
        string folder = Path.Combine(root, id);
        return Directory.Exists(folder) ? (folder, id) : null;
    }

    private static JsonObject Labels(IReadOnlyList<string> words)
    {
        JsonObject labels = [];
        for (int i = 0; i + 1 < words.Count; i += 2)
        {
            if (words[i] == "--label" && words[i + 1].Split('=', 2) is [var key, var value])
            {
                labels[key] = value;
            }
        }
        return labels;
    }

    /// <summary>A port nothing on this machine listens on now (at the host's address it is published on), for a service's
    /// published port Passenger chooses: chosen once for the plan, the same one after.</summary>
    private static int Pinned(Dictionary<(string Service, int Port), int> pinned, string service, int index, string? hostIp)
    {
        if (!pinned.TryGetValue((service, index), out int port))
        {
            System.Net.IPAddress at = hostIp is { Length: > 0 } given && System.Net.IPAddress.TryParse(given, out System.Net.IPAddress? parsed) ? parsed : System.Net.IPAddress.Loopback;
            using System.Net.Sockets.TcpListener listener = new(at, 0);
            listener.Start();
            pinned[(service, index)] = port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        return port;
    }

    /// <summary>A port in Compose's long syntax as its model had it (the host's address, mode, name and application protocol
    /// kept), published on <paramref name="chosen"/> where Passenger chose its port.</summary>
    private static JsonObject Port(ComposePort port, int? chosen)
    {
        JsonObject written = new() { ["target"] = port.Target, ["protocol"] = port.Protocol };
        if ((chosen?.ToString(CultureInfo.InvariantCulture) ?? port.Published) is { Length: > 0 } published)
        {
            written["published"] = published;
        }
        foreach ((string key, string? value) in (ReadOnlySpan<(string, string?)>)[("host_ip", port.HostIp), ("mode", port.Mode), ("name", port.Name), ("app_protocol", port.AppProtocol)])
        {
            if (value is { Length: > 0 })
            {
                written[key] = value;
            }
        }
        return written;
    }

    /// <summary>Whether Compose takes YAML's <c>!override</c> tag (2.24 and later), which puts a list in place of the file's
    /// rather than beside it.</summary>
    private static bool Overrides(string? composeVersion) =>
        composeVersion is { } said && Version.TryParse(new string([.. said.TrimStart('v').TakeWhile(c => char.IsAsciiDigit(c) || c == '.')]), out Version? version)
        && version >= new Version(2, 24);

    private static void WriteJson(string path, JsonObject json, bool overriding = false)
    {
        // A service's ports Passenger chose replace the file's (YAML's tag, on JSON's flow, which YAML reads alike).
        string text = json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        text = overriding ? text.Replace("\"ports\": [", "\"ports\": !override [", StringComparison.Ordinal) : text;
        string temporary = path + ".tmp";
        // It may hold the certificate's password and the configuration's environment: the person's alone to read.
        OwnFiles.Write(temporary, text);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>A name as Compose takes a project's: lowercase letters, digits, dashes and underscores.</summary>
    public static string Slug(string name)
    {
        System.Text.StringBuilder made = new();
        foreach (char c in name.ToLowerInvariant())
        {
            made.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '-');
        }
        string slug = made.ToString().Trim('-', '_');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        return slug.Length > 0 ? slug : "app";
    }

    /// <summary>The folder's own place in the state, where its override files live.</summary>
    private static string StateFolderOf(string stateFolder, string folder) => State.ProjectState.FolderOf(stateFolder, folder);
}

/// <summary>Compose would not read a configuration's files: what it said, for Problems' Containers tab.</summary>
public sealed class ComposeModelException(string name, string said) : LaunchException($"{name} cannot run: Compose could not read its files: {said}")
{
    public string Said { get; } = said;
}

/// <summary>The state's files that only the person may read on macOS and Linux (the development certificate's copy and its
/// password, a run's override and single service's compose file, which name them and the configuration's environment), so
/// from the moment each is made: a folder made 0700, a file 0600 as it is created, never set so after its text is in.</summary>
internal static class OwnFiles
{
    private const UnixFileMode OwnersFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode OwnersFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Makes a folder only the person enters (one an earlier Passenger made, put right), before anything is
    /// written in it.</summary>
    public static void Folder(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }
        Directory.CreateDirectory(path, OwnersFolder);
        File.SetUnixFileMode(path, OwnersFolder);
    }

    /// <summary>Writes a file only the person reads: one there before is replaced, never written into, since it would
    /// keep the mode it had.</summary>
    public static void Write(string path, string text)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, text);
            return;
        }
        File.Delete(path);
        using FileStream stream = new(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = OwnersFile });
        using StreamWriter writer = new(stream);
        writer.Write(text);
    }

    /// <summary>A file another program wrote in such a folder, made only the person's to read too.</summary>
    public static void Keep(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, OwnersFile);
        }
    }
}

/// <summary>The .NET command line <c>dotnet dev-certs</c> runs through: the machine's (<see cref="DevelopmentCertificate.Machine"/>),
/// or a test's stand-in, which never trusts a certificate on the machine it runs on.</summary>
public delegate ProcessStartInfo DotnetWords(IReadOnlyList<string> words, string folder);

/// <summary>ASP.NET Core's development certificate for containers that serve HTTPS: how it stands on this machine
/// (<c>dotnet dev-certs https --check --trust</c>), made and trusted when the person asks (<c>--trust</c>, which the system
/// may ask about too), and exported once into the state (<c>-ep … -p …</c>), with a password of its own kept beside it and
/// never written to a console; each through ChildProcesses, bounded, saying why when it does not come.</summary>
public static partial class DevelopmentCertificate
{
    public sealed record Exported(string Folder, string File, string Password);

    /// <summary>How this machine's development certificate stands.</summary>
    public enum Standing
    {
        Trusted,
        NotTrusted,
        Missing,
        Unknown,
    }

    /// <summary>The machine's own dotnet.</summary>
    public static readonly DotnetWords Machine = (words, folder) => ChildProcesses.DotnetCommand(words, folder);

    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>How long the export or the trust may take: one waiting on the system's certificate store (a locked keychain
    /// asking for its password) is said rather than the run waiting for good.</summary>
    public static readonly TimeSpan ExportLimit = TimeSpan.FromSeconds(60);

    /// <summary>How long the check may take, which asks nothing of the person.</summary>
    public static readonly TimeSpan CheckLimit = TimeSpan.FromSeconds(30);

    /// <summary>Whether a certificate is there and trusted, as dev-certs answers (6: none valid, 7: not trusted), what it said,
    /// and the thumbprint of the certificate it found, when it named one. Stopped, it throws <see cref="OperationCanceledException"/>.</summary>
    public static async Task<(Standing Standing, string Said, string? Thumbprint)> CheckAsync(DotnetWords dotnet, string folder, CancellationToken cancel)
    {
        try
        {
            ChildResult answered = await ChildProcesses.RunAsync(dotnet(["dev-certs", "https", "--check", "--trust"], folder), limit: CheckLimit, cancel: cancel,
                answers: [6, 7]);
            string? thumbprint = answered.ExitCode is 0 or 7 && Thumbprint().Match(answered.Output) is { Success: true } found ? found.Value.ToUpperInvariant() : null;
            return (answered.ExitCode switch { 0 => Standing.Trusted, 6 => Standing.Missing, 7 => Standing.NotTrusted, _ => Standing.Unknown }, Said(answered), thumbprint);
        }
        catch (TimeoutException)
        {
            return (Standing.Unknown, $"dotnet dev-certs https --check did not answer within {CheckLimit.TotalSeconds:0} s", null);
        }
        catch (Exception failed) when (failed is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (Standing.Unknown, failed.Message, null);
        }
    }

    /// <summary>A certificate's SHA-1 thumbprint, as dev-certs names the one it found ("A valid certificate was found: …").</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\b[0-9A-Fa-f]{40}\b")]
    private static partial System.Text.RegularExpressions.Regex Thumbprint();

    /// <summary>Makes the certificate if there is none and trusts it, as <c>dotnet dev-certs https --trust</c> does (the
    /// system may ask the person too), the copy exported before let go: null once trusted, else why not.</summary>
    public static async Task<string?> TrustAsync(DotnetWords dotnet, string folder, string state, CancellationToken cancel)
    {
        try
        {
            ChildResult trusted = await ChildProcesses.RunAsync(dotnet(["dev-certs", "https", "--trust"], folder), limit: ExportLimit, cancel: cancel);
            Forget(state);
            return trusted.Succeeded ? null : $"dotnet dev-certs https --trust ended with exit code {trusted.ExitCode}: {Said(trusted)}";
        }
        catch (TimeoutException)
        {
            return $"dotnet dev-certs https --trust did not end within {ExportLimit.TotalSeconds:0} s (the system's certificate store may be asking for a password)";
        }
        catch (Exception failed) when (failed is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return failed.Message;
        }
    }

    /// <summary>The certificate exported into the state (once for the certificate dev-certs has, <paramref name="thumbprint"/>
    /// as its check named it: again once that is another, or after <see cref="TrustAsync"/>), or why it is not. Stopped, it
    /// throws <see cref="OperationCanceledException"/>.</summary>
    public static async Task<(Exported? Certificate, string? Problem)> ExportAsync(DotnetWords dotnet, string state, string? thumbprint, CancellationToken cancel)
    {
        string folder = Path.Combine(state, "https");
        string file = Path.Combine(folder, "aspnetapp.pfx");
        string kept = Path.Combine(folder, "password");
        // The thumbprint of the certificate exported, as the check named it then: a copy of another the check no longer finds
        // (made again since, or expired and replaced) would be refused by every browser that trusts the new one.
        string exportedOf = Path.Combine(folder, "thumbprint");
        await Gate.WaitAsync(cancel);
        try
        {
            OwnFiles.Folder(folder);
            if (File.Exists(file) && File.Exists(kept)
                && (thumbprint is null || File.Exists(exportedOf) && string.Equals((await File.ReadAllTextAsync(exportedOf, cancel)).Trim(), thumbprint, StringComparison.OrdinalIgnoreCase)))
            {
                return (new Exported(folder, file, (await File.ReadAllTextAsync(kept, cancel)).Trim()), null);
            }
            Forget(state);
            string password = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            ChildResult exported = await ChildProcesses.RunAsync(dotnet(["dev-certs", "https", "-ep", file, "-p", password], folder), limit: ExportLimit, cancel: cancel);
            if (!exported.Succeeded || !File.Exists(file))
            {
                return (null, $"dotnet dev-certs https -ep ended with exit code {exported.ExitCode}: {Said(exported)}");
            }
            // Whatever mode dev-certs gave the copy, in the folder no one else enters.
            OwnFiles.Keep(file);
            OwnFiles.Write(kept, password);
            if (thumbprint is not null)
            {
                OwnFiles.Write(exportedOf, thumbprint);
            }
            return (new Exported(folder, file, password), null);
        }
        catch (TimeoutException)
        {
            return (null, $"dotnet dev-certs https -ep did not end within {ExportLimit.TotalSeconds:0} s (the system's certificate store may be asking for a password)");
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or FileNotFoundException
            or InvalidOperationException)
        {
            return (null, failed.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Lets go of the copy exported before, which a certificate made since no longer matches.</summary>
    private static void Forget(string state)
    {
        try
        {
            foreach (string kept in (string[])["aspnetapp.pfx", "thumbprint"])
            {
                if (Path.Combine(state, "https", kept) is var file && File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception kept) when (kept is IOException or UnauthorizedAccessException)
        {
            // An export kept stands until the next one replaces it.
        }
    }

    private static string Said(ChildResult result) =>
        string.Join(' ', ((string[])[result.Error.Trim(), result.Output.Trim()]).Where(s => s.Length > 0)) is { Length: > 0 } said ? said : "it said nothing";
}

/// <summary>A .NET service of a container run in its container, as the debugger and the profiler reach it: the container,
/// its program there (by its id there), Passenger's tools in it, when the container started (its log followed from then),
/// and why its image may not be debugged where it runs under emulation, or null.</summary>
public sealed record ServiceInContainer(string Container, DotnetProcess Program, string Tools, string Started, string? Emulated);

/// <summary>Has the profiler record a .NET service held at its start in its container (its program there, by its id, and
/// the place the tools record it at), which lets it go once recording: null once it records, else why not.</summary>
public delegate Task<string?> ContainerProfiler(DotnetService service, DotnetProcess program, Profiler.ProfilePlace place, CancellationToken stop);

/// <summary>A container configuration running: its plan, Compose's watch of the services that ask for one, and each .NET
/// service's build as it was when started, so Rerun restarts only those whose build changed. Brought up by
/// <see cref="UpAsync"/> (its words in the Run console), taken down by <see cref="StopAsync"/> (<c>docker compose down</c>,
/// its volumes kept, or only the .NET services when backing services are kept). A UI-thread object of the session's.</summary>
public sealed class ContainerRun(ContainerPlan plan, ContainerEngine engine, ConsoleSink console)
{
    private readonly Dictionary<string, string> stamps = new(StringComparer.Ordinal);
    private Process? watching;
    private Task? stopping;

    // Never disposed: a command that took its token may register on it after the run has ended.
    /// <summary>Canceled as the run is taken down (by whoever asks, whatever started it) or its folder ends: its own
    /// <c>up</c>, publish and restarts end with it, whichever start or action began them.</summary>
    private readonly CancellationTokenSource ending = new();

    /// <summary>Whether <c>up</c> was ended before it had made all it would: the engine may finish a container it was making.</summary>
    private bool cut;

    /// <summary>Done once <c>up</c> has ended, having made all it would make or been ended; never begun, done already.</summary>
    private TaskCompletionSource upEnded = Done();

    private static TaskCompletionSource Done()
    {
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        done.SetResult();
        return done;
    }

    public ContainerPlan Plan { get; private set; } = plan;

    /// <summary>The address the browser was opened at, which a restart on an engine that chose another port changes.</summary>
    public string? Address { get; set; }

    public ContainerEngine Engine => engine;

    /// <summary>Whether it was taken down (or is being).</summary>
    public bool IsStopped => stopping is not null;

    /// <summary>Done once <see cref="StopAsync"/> has had the engine take it down.</summary>
    public Task Stopped => stopping ?? Task.CompletedTask;

    /// <summary>The Debug tabs of the services debugged (or attached to for Hot Reload), by their names: Stop ends them before
    /// it takes their containers down.</summary>
    public HashSet<string> Debugged { get; } = new(StringComparer.Ordinal);

    /// <summary>How it was started: run, debugged, run with Hot Reload through the debugger, or profiled.</summary>
    public ContainerStart How { get; init; }

    /// <summary>What has the profiler record the service Profile holds at its start; null for any other start.</summary>
    public ContainerProfiler? Profiler { get; init; }

    /// <summary>What each .NET service is held at its start for, as last chosen (debugged, or attached to quietly for Hot
    /// Reload): a restart holds it again and attaches again. A service not here runs unheld.</summary>
    private readonly Dictionary<string, ContainerStart> holds = new(StringComparer.Ordinal);

    /// <summary>The services whose container was made held (the override in force when it was made said so), which
    /// <c>docker compose restart</c> holds again whatever was chosen since: one chosen otherwise is made anew instead.</summary>
    private readonly HashSet<string> madeHeld = new(StringComparer.Ordinal);

    /// <summary>The .NET services held at their start for the debugger, which a restart holds again.</summary>
    public IReadOnlyList<DotnetService> Held => [.. Plan.Dotnet.Where(d => holds.ContainsKey(d.Service))];

    /// <summary>What a service is held at its start for (Debug or Hot Reload), or null when it runs unheld.</summary>
    public ContainerStart? HeldFor(string service) => holds.TryGetValue(service, out ContainerStart how) ? how : null;

    /// <summary>The services as they were brought up: those held for the debugger (or the one for the profiler, whose hold its
    /// recording lets go, never asked again), their containers made so.</summary>
    public void Brought(IReadOnlyList<(DotnetService Service, ContainerStart How)> held)
    {
        foreach ((DotnetService service, ContainerStart how) in held)
        {
            if (how != ContainerStart.Profile)
            {
                holds[service.Service] = how;
            }
            madeHeld.Add(service.Service);
        }
    }

    /// <summary>The services attached to as they ran (Attach, not held at their start), which a restart attaches to again.</summary>
    public HashSet<string> AttachedRunning { get; } = new(StringComparer.Ordinal);

    /// <summary>Chooses what a service is held at its start for from now (null: unheld), and writes the override so: its
    /// next restart makes it so.</summary>
    public Task HoldAsync(string service, ContainerStart? how, CancellationToken cancel)
    {
        if (how is { } held)
        {
            holds[service] = held;
        }
        else
        {
            holds.Remove(service);
        }
        return WriteOverrideAsync(cancel);
    }

    /// <summary>The override as the holds chosen say, each held service's code open to Hot Reload.</summary>
    private Task WriteOverrideAsync(CancellationToken cancel) => Plan.WriteOverride([.. holds.Keys], [.. holds.Keys], Certificate, cancel);

    /// <summary>The service the profiler records, which Stop ends (<see cref="EndProfiling"/>, its snapshot read) before it
    /// takes the containers down; null when none is.</summary>
    public string? Profiled { get; set; }

    /// <summary>Ends the profiler's recording of <see cref="Profiled"/>, reading its snapshot, done once it has.</summary>
    public Func<Task>? EndProfiling { get; set; }

    /// <summary>Brings it up as <c>docker compose up --detach --wait</c> (images built as Compose says in images mode), its
    /// words in the console as they come: null once every service is up (healthy where it has a check), else what went
    /// wrong. <paramref name="alongside"/> runs while it comes up (the debugger attaching to the services held at their start,
    /// which no wait would see healthy before it lets them go): its problem ends the command. Canceled, the command is ended.</summary>
    public async Task<string?> UpAsync(CancellationToken cancel, Func<Task, CancellationToken, Task<string?>>? alongside = null)
    {
        Stamp();
        List<string> words = [.. Plan.Compose, "up", "--detach", "--wait"];
        if (Plan.Mode == ContainerMode.Images)
        {
            words.Add("--build");
        }
        words.AddRange(Plan.Start.Count > 0 ? ["--", .. Plan.Start] : []);
        using CancellationTokenSource upEnding = CancellationTokenSource.CreateLinkedTokenSource(cancel, ending.Token);
        upEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (ending.IsCancellationRequested)
        {
            upEnded.TrySetResult();
            throw new OperationCanceledException(ending.Token);
        }
        Task<ChildResult> coming = StreamAsync(words, upEnding.Token);
        ChildResult up;
        try
        {
            if (alongside is not null && await alongside(coming, upEnding.Token) is { } unattached)
            {
                await EndedAsync();
                return unattached;
            }
            up = await coming;
        }
        catch (OperationCanceledException)
        {
            // Stopped as it came up: Compose ends first, or it would go on making containers after Stop took the others.
            await EndedAsync();
            throw;
        }
        finally
        {
            upEnded.TrySetResult();
        }

        async Task EndedAsync()
        {
            cut = true;
            await upEnding.CancelAsync();
            try
            {
                await coming;
            }
            catch (OperationCanceledException)
            {
                // Ended for the attach that failed, or the Stop, which say why.
            }
        }
        if (!up.Succeeded)
        {
            // What the services said before they ended is why, which the down that follows would take with them.
            await StreamAsync([.. Plan.Compose, "logs", "--no-color", "--tail", "40"], cancel);
            string last = up.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: > 0 } lines
                ? lines.LastOrDefault(l => l.Contains("rror", StringComparison.Ordinal)) ?? lines[^1] : $"exit code {up.ExitCode}";
            return $"{Plan.Name} did not come up: {last}";
        }
        if (Plan.Watched.Count > 0)
        {
            Watch();
        }
        return null;
    }

    /// <summary>Compose's own watch of the services that declare <c>develop.watch</c>, while the run lasts.</summary>
    private void Watch()
    {
        try
        {
            ProcessStartInfo start = engine.Command([.. Plan.Compose, "watch", "--no-up", "--", .. Plan.Watched], Plan.Folder);
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            Process started = ChildProcesses.Start(start);
            watching = started;
            Task pumped = Task.WhenAll(console.PumpAsync(started.StandardOutput, ConsoleStream.Output), console.PumpAsync(started.StandardError, ConsoleStream.Output));
            _ = Ended();

            // A watch that ends while the run lasts says so, rather than leaving its files unsynced unsaid.
            async Task Ended()
            {
                await pumped;
                await started.WaitForExitAsync();
                if (watching == started && !IsStopped)
                {
                    console.Write($"Compose's watch of {string.Join(", ", Plan.Watched)} ended with exit code {started.ExitCode}\n", ConsoleStream.Error);
                }
            }
        }
        catch (Exception failed) when (failed is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            console.Write($"Compose's watch could not start: {failed.Message}\n", ConsoleStream.Error);
        }
    }

    /// <summary>The <c>docker compose logs -f</c> words that follow every service's output into the Run console.</summary>
    public IReadOnlyList<string> Logs => [.. Plan.Compose, "logs", "--follow", "--no-color", "--tail", "all"];

    /// <summary>The workspace folder whose label is on what the run made (<see cref="FolderContainers"/>), by which Stop
    /// finds what is its own to take down.</summary>
    public string? LabelledFor { get; init; }

    /// <summary>The development certificate its HTTPS services take, which every override it writes keeps; null when they
    /// serve HTTP for this run, or it has none to serve.</summary>
    public DevelopmentCertificate.Exported? Certificate { get; init; }

    /// <summary>Takes it down: Compose's watch ended, then, once <paramref name="first"/> is done (its debugged services'
    /// sessions ended, so no debugger loses its container under it), the containers and networks it made, by this machine's,
    /// process's and folder's labels (volumes kept, and never the person's own of the same Compose project, which
    /// <c>docker compose down --remove-orphans</c> would take), or only its .NET services' when backing services are kept;
    /// once, whoever asks. Off the UI thread.</summary>
    public Task StopAsync(Task? first = null)
    {
        if (stopping is not null)
        {
            return stopping;
        }
        if (watching is { } watch)
        {
            ChildProcesses.Kill(watch);
            watching = null;
        }
        IReadOnlyList<string>? only = Plan.KeepBacking && Plan.Backing.Count > 0 ? [.. Plan.Dotnet.Select(d => d.Service)] : null;
        Task up = upEnded.Task;
        bool coming = !up.IsCompleted || cut;
        stopping = BackgroundWork.Run(async () =>
        {
            // Compose still bringing it up (ended just below, whoever started it) makes no container after the take-down's look.
            await Task.WhenAny(Task.WhenAll(first ?? Task.CompletedTask, up), Task.Delay(TimeSpan.FromSeconds(10)));
            await FolderContainers.TakeDownAsync(engine, Plan.Compose, LabelledFor, $"as {Plan.Name} stopped", only);
            if (coming)
            {
                // Stopped as it came up: a container the engine was still making as Compose ended is taken too.
                await Task.Delay(TimeSpan.FromSeconds(2));
                await FolderContainers.TakeDownAsync(engine, Plan.Compose, LabelledFor, $"as {Plan.Name} stopped", only);
            }
        });
        // After stopping is set: a caller its end wakes at once asks again and is given the same take-down.
        ending.Cancel();
        return stopping;
    }

    /// <summary>Lets go of what the run started on this machine (Compose's watch, and its <c>up</c> or restart under way,
    /// ended) as the folder ends; its containers go with the folder's (<see cref="FolderContainers"/>), once the task given
    /// back is done: <c>up</c> has ended, making nothing after their take-down looks.</summary>
    public Task Abandon()
    {
        if (watching is { } watch)
        {
            ChildProcesses.Kill(watch);
            watching = null;
        }
        ending.Cancel();
        return upEnded.Task;
    }

    /// <summary>What the run said of itself as it came up (its notes: a service over HTTP, one run as the person, one that runs
    /// without the debugger), which an agent's answer carries too.</summary>
    public List<string> Notes { get; } = [];

    /// <summary>The .NET services whose build changed since they started (or were last restarted), Rerun's to restart.</summary>
    public IReadOnlyList<DotnetService> Changed() =>
        [.. Plan.Restartable.Where(d => !stamps.TryGetValue(d.Service, out string? was) || was != Stamp(d.Output))];

    /// <summary>Restarts services (their containers kept, the same mount reading the new build), saying so in the console
    /// (<paramref name="why"/>, else that their build changed). A service whose container was made held, or unheld, other
    /// than as it is now chosen is made anew from the override, as are all of them in images mode, from an image built again
    /// from its Dockerfile. <paramref name="alongside"/> runs while they come up, as <see cref="UpAsync"/>'s does.</summary>
    public async Task<string?> RestartAsync(IReadOnlyList<DotnetService> services, CancellationToken cancel, string? why = null,
        Func<Task, CancellationToken, Task<string?>>? alongside = null)
    {
        if (services.Count == 0)
        {
            console.Write("No service's build changed: nothing restarted\n");
            return null;
        }
        console.Write($"Restarting {string.Join(", ", services.Select(s => s.Service))}: {why ?? (services.Count == 1 ? "its build changed" : "their builds changed")}\n");
        List<string> made = [.. services.Select(s => s.Service).Where(s => Plan.Mode == ContainerMode.Images || madeHeld.Contains(s) != holds.ContainsKey(s))];
        List<string> kept = [.. services.Select(s => s.Service).Except(made)];
        if (made.Count > 0)
        {
            await WriteOverrideAsync(cancel);
        }
        // Stop of the run ends its restart too, whoever asked for it.
        using CancellationTokenSource restartEnding = CancellationTokenSource.CreateLinkedTokenSource(cancel, ending.Token);
        Task<string?> restarting = RestartEachAsync(restartEnding.Token);
        if (alongside is not null && await alongside(restarting, restartEnding.Token) is { } unattached)
        {
            await restartEnding.CancelAsync();
            try
            {
                await restarting;
            }
            catch (OperationCanceledException)
            {
                // Ended for the attach that failed, which says why.
            }
            return unattached;
        }
        string? failed = await restarting;
        // Only those restarted run the build they have now: another's that changed meanwhile is still Rerun's to restart.
        Stamp(services);
        return failed;

        async Task<string?> RestartEachAsync(CancellationToken stop)
        {
            foreach ((List<string> named, IReadOnlyList<string> words) in ((List<string>, IReadOnlyList<string>)[])[
                (kept, [.. Plan.Compose, "restart", "--", .. kept]),
                // As deployed, a service's image is built again from its Dockerfile and its container made anew from it.
                (made, [.. Plan.Compose, "up", "--detach", .. Plan.Mode == ContainerMode.Images ? (string[])["--build"] : [], "--no-deps", "--force-recreate", "--", .. made])])
            {
                if (named.Count == 0)
                {
                    continue;
                }
                ChildResult restarted = await StreamAsync(words, stop);
                if (!restarted.Succeeded)
                {
                    return $"{Plan.Name}'s services did not restart: docker compose {words[Plan.Compose.Count]} ended with exit code {restarted.ExitCode}";
                }
                foreach (string service in named)
                {
                    if (holds.ContainsKey(service))
                    {
                        madeHeld.Add(service);
                    }
                    else
                    {
                        madeHeld.Remove(service);
                    }
                }
            }
            return null;
        }
    }

    private void Stamp(IEnumerable<DotnetService>? services = null)
    {
        foreach (DotnetService service in services ?? Plan.Restartable)
        {
            stamps[service.Service] = Stamp(service.Output);
        }
    }

    /// <summary>Restarts services the run does not build (its backing services) as they are, saying so in the console;
    /// null once done, else why not. Stop of the run ends it.</summary>
    public async Task<string?> RestartOthersAsync(IReadOnlyList<string> services, CancellationToken cancel)
    {
        if (services.Count == 0)
        {
            return null;
        }
        using CancellationTokenSource restartEnding = CancellationTokenSource.CreateLinkedTokenSource(cancel, ending.Token);
        ChildResult restarted = await StreamAsync([.. Plan.Compose, "restart", "--", .. services], restartEnding.Token);
        return restarted.Succeeded ? null : $"{Plan.Name}'s services did not restart: docker compose restart ended with exit code {restarted.ExitCode}";
    }

    /// <summary>A build's output as it stands: each assembly's, symbols' and settings' name, length and time.</summary>
    private static string Stamp(string output)
    {
        try
        {
            return string.Join('|', Directory.EnumerateFiles(output)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".dll" or ".pdb" or ".json")
                .Order(StringComparer.Ordinal)
                .Select(f => new FileInfo(f))
                .Select(f => $"{f.Name}:{f.Length}:{f.LastWriteTimeUtc.Ticks}"));
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>The services' state as the engine says it now (service, state, health, ports), for an agent.</summary>
    public async Task<IReadOnlyList<(string Service, string State, string? Health, string Ports)>> StatesAsync(CancellationToken cancel)
    {
        IReadOnlyList<JsonElement> rows = await engine.JsonLinesAsync([.. Plan.Compose, "ps", "--all", "--format", "json"], cancel);
        return [.. rows.Select(r => (ContainerEngine.Text(r, "Service") ?? string.Empty, ContainerEngine.Text(r, "State") ?? string.Empty,
            ContainerEngine.Text(r, "Health") is { Length: > 0 } health ? health : null, ContainerEngine.Text(r, "Publishers") is null ? ContainerEngine.Text(r, "Ports") ?? string.Empty : Ports(r)))];

        static string Ports(JsonElement row) => row.TryGetProperty("Publishers", out JsonElement publishers) && publishers.ValueKind == JsonValueKind.Array
            ? string.Join(", ", publishers.EnumerateArray().Where(p => ContainerEngine.Text(p, "PublishedPort") is not (null or "0"))
                .Select(p => $"{ContainerEngine.Text(p, "PublishedPort")}->{ContainerEngine.Text(p, "TargetPort")}/{ContainerEngine.Text(p, "Protocol")}").Distinct())
            : string.Empty;
    }

    /// <summary>The address the browser opens: the plan's, its <c>{Scheme}</c>, <c>{ServiceHost}</c> and
    /// <c>{ServicePort}</c> filled from the port the engine published for the launched service; null without one.</summary>
    public async Task<string?> BrowserUrlAsync(CancellationToken cancel)
    {
        if (Plan.BrowserUrl is not { } url)
        {
            return null;
        }
        if (!url.Contains('{', StringComparison.Ordinal))
        {
            return url;
        }
        if (Plan.LaunchService is not { } service || Plan.LaunchPort is not { } port)
        {
            return null;
        }
        ChildResult published = await engine.RunAsync([.. Plan.Compose, "port", "--", service, port.ToString(CultureInfo.InvariantCulture)], cancel: cancel);
        string answer = published.Output.Trim().Split('\n')[0].Trim();
        int colon = answer.LastIndexOf(':');
        if (!published.Succeeded || colon < 0)
        {
            return null;
        }
        string scheme = port is 443 or 8081 || url.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        return url.Replace("{Scheme}", scheme, StringComparison.OrdinalIgnoreCase)
            .Replace("{ServiceHost}", "localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("{ServicePort}", answer[(colon + 1)..], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Runs a docker command to its end, its output streamed into the console, its words first.</summary>
    private Task<ChildResult> StreamAsync(IReadOnlyList<string> words, CancellationToken cancel) => StreamAsync(engine.Command(words, Plan.Folder), cancel);

    /// <summary>The SDK's image made, as deployed: <c>dotnet publish -t:PublishContainer</c> into the engine, its words in the
    /// console; null once the image is there, else why not.</summary>
    public async Task<string?> PublishAsync(CancellationToken cancel)
    {
        if (Plan.Publish is not { } words)
        {
            return null;
        }
        ProcessStartInfo start = ChildProcesses.DotnetCommand(words, Plan.Folder);
        // The SDK loads the image through the engine's CLI, the one Passenger asks.
        if (engine.Cli is { } cli && Path.GetDirectoryName(cli) is { } bin)
        {
            start.Environment["PATH"] = start.Environment.TryGetValue("PATH", out string? path) ? $"{bin}{Path.PathSeparator}{path}" : bin;
        }
        using CancellationTokenSource publishEnding = CancellationTokenSource.CreateLinkedTokenSource(cancel, ending.Token);
        ChildResult published = await StreamAsync(start, publishEnding.Token);
        return published.Succeeded ? null : $"{Plan.Name} could not be made as deployed: dotnet publish ended with exit code {published.ExitCode}; the Run window has what it said";
    }

    private async Task<ChildResult> StreamAsync(ProcessStartInfo start, CancellationToken cancel)
    {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        console.Write($"{ChildProcesses.Display(start)}\n");
        Queue<string> said = new();
        using Process process = ChildProcesses.Start(start);
        Task output = console.PumpAsync(process.StandardOutput, ConsoleStream.Output, lines: Line);
        // Compose writes its progress to standard error: the console's ordinary text, not an error's.
        Task errors = console.PumpAsync(process.StandardError, ConsoleStream.Output, lines: Line);
        try
        {
            await process.WaitForExitAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            ChildProcesses.Kill(process);
            // Gone before its caller goes on: Compose killed makes nothing more after a take-down has looked.
            await Within(process.WaitForExitAsync(), TimeSpan.FromSeconds(5));
            throw;
        }
        await Within(Task.WhenAll(output, errors), TimeSpan.FromSeconds(5));
        lock (said)
        {
            return new ChildResult(process.ExitCode, string.Join('\n', said), string.Empty);
        }

        // Each line read: its last lines say why it failed.
        void Line(string line)
        {
            lock (said)
            {
                said.Enqueue(line);
                if (said.Count > 8)
                {
                    said.Dequeue();
                }
            }
            // An image's build that fails says where in the workspace's files, as a host build does.
            if (Plan.Mode == ContainerMode.Images && BuildOutput.Parse(line, Plan.Paths) is { } problem)
            {
                UiThread.Post(() => ProblemFound?.Invoke(this, problem));
            }
        }
    }

    /// <summary>Waits for a process's end or its output's for a while at most: one that comes later, or fails (its pipe
    /// broken as it was killed), is let go, its failure observed.</summary>
    private static async Task Within(Task task, TimeSpan limit)
    {
        try
        {
            await task.WaitAsync(limit);
        }
        catch (TimeoutException)
        {
            _ = task.ContinueWith(static late => _ = late.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
        catch (Exception ended) when (ended is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Its output ended with the process.
        }
    }

    /// <summary>A compiler's message in an image's build, its file the workspace's; UI thread.</summary>
    public event EventHandler<Problem>? ProblemFound;
}
