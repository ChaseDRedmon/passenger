using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Passenger.App.State;
using Passenger.App.Workspace;

namespace Passenger.App.Run;

/// <summary>Where a run's program reads and writes: the Run or Debug window's console, or a tab of the Terminal window,
/// which gives it a real terminal (Rider's Use external console; <see cref="TerminalRun"/>). Profile keeps to the Run
/// window's console either way.</summary>
public enum ConsoleKind
{
    Internal,
    Terminal,
}

/// <summary>Something Run, Debug and Profile launch, as Rider's run configurations are: a project (its framework, or the
/// first), the program's arguments, environment and working directory, whether the project is built first, the console,
/// and the browser a web project opens. Data alone: <see cref="LaunchPlan.ResolveAsync"/> works out what it launches. The
/// folder's own are kept with its state; the generated ones come from its executable projects and their launch profiles.</summary>
public sealed record RunConfiguration
{
    public string Name { get; init; } = string.Empty;

    /// <summary>The project's file: a canonical path in the window, relative to the open folder in the store.</summary>
    public string Project { get; init; } = string.Empty;

    /// <summary>The target framework run, for a project with several; empty for the first.</summary>
    public string Framework { get; init; } = string.Empty;

    /// <summary>The launch profile its values came from, if one did.</summary>
    public string LaunchProfile { get; init; } = string.Empty;

    /// <summary>A program run in place of the project's own (an <c>Executable</c> profile's, Rider's Exe path); empty for
    /// the project's. MSBuild's <c>$(Property)</c> and the environment's <c>%NAME%</c> are expanded, as below.</summary>
    public string Program { get; init; } = string.Empty;

    /// <summary>The program's arguments as one command line.</summary>
    public string Arguments { get; init; } = string.Empty;

    public Dictionary<string, string> Environment { get; init; } = [];

    /// <summary>Where the program runs, relative to the project's folder; empty for the project's output folder, Rider's
    /// default for a .NET project.</summary>
    public string WorkingDirectory { get; init; } = string.Empty;

    public bool BuildBeforeLaunch { get; init; } = true;

    public ConsoleKind Console { get; init; }

    /// <summary>Whether the browser opens at <see cref="BrowserUrl"/> once the program listens (#51's; kept, not yet acted on).</summary>
    public bool LaunchBrowser { get; init; }

    public string BrowserUrl { get; init; } = string.Empty;

    /// <summary>Made from the folder's projects rather than by the person, and so changed only by copying it.</summary>
    [JsonIgnore]
    public bool Generated { get; init; }

    /// <summary>Why it cannot launch here (a launch profile of a kind Passenger does not run), or null.</summary>
    [JsonIgnore]
    public string? Unsupported { get; init; }

    /// <summary>The configurations the folder's executable projects make: one per launch profile, named "Project: profile"
    /// as Rider names them, and the project itself when none of its profiles runs here.</summary>
    public static IReadOnlyList<RunConfiguration> Generate(IEnumerable<ProjectModel> projects)
    {
        List<RunConfiguration> generated = [];
        foreach (ProjectModel project in projects.Where(p => p.Exists && p.IsRunnable))
        {
            IReadOnlyList<LaunchProfile> profiles = Run.LaunchProfile.Read(project);
            if (!profiles.Any(p => p.Unsupported is null))
            {
                generated.Add(new RunConfiguration { Name = project.Name, Project = project.Path, Generated = true });
            }
            generated.AddRange(profiles.Select(p => From(project, p) with { Name = $"{project.Name}: {p.Name}", Generated = true }));
        }
        return generated;
    }

    /// <summary>A configuration with a launch profile's values: what generating one makes, and what choosing a profile in
    /// Edit Configurations fills in. A profile's own folder, else the project's, as <c>dotnet run</c> runs it.</summary>
    public static RunConfiguration From(ProjectModel project, LaunchProfile profile) => new()
    {
        Name = profile.Name,
        Project = project.Path,
        LaunchProfile = profile.Name,
        Program = profile.RunsProject ? string.Empty : profile.ExecutablePath ?? string.Empty,
        Arguments = profile.CommandLineArgs ?? string.Empty,
        Environment = profile.Environment(),
        WorkingDirectory = string.IsNullOrWhiteSpace(profile.WorkingDirectory) ? "$(ProjectDir)" : profile.WorkingDirectory,
        LaunchBrowser = profile.LaunchBrowser,
        BrowserUrl = profile.BrowserUrl(),
        Unsupported = profile.Unsupported,
    };

    /// <summary>The name a new or copied one takes: the one wanted, or with the first number that makes it unique.</summary>
    public static string UniqueName(string wanted, IEnumerable<string> taken)
    {
        HashSet<string> names = new(taken, StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(wanted))
        {
            return wanted;
        }
        int n = 1;
        while (names.Contains($"{wanted} ({n})"))
        {
            n++;
        }
        return $"{wanted} ({n})";
    }
}

/// <summary>A folder's own run configurations and the one chosen, kept in its place in the store as Rider keeps them in
/// its workspace.xml. The generated ones are not kept: they are made again from the projects each time.</summary>
public sealed record RunConfigurationsState : IStateDocument<RunConfigurationsState>
{
    public List<RunConfiguration> Configurations { get; init; } = [];

    /// <summary>The name of the configuration chosen in the toolbar, the person's or a generated one.</summary>
    public string? Selected { get; init; }

    public static IReadOnlyList<Action<JsonObject>> Upgrades { get; } = [StateStore.Unversioned];

    /// <summary>One without a name or a project is left out, as is a second of the same name; a console this build does
    /// not know is the IDE's own.</summary>
    public RunConfigurationsState Clamped()
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        List<RunConfiguration> kept = [];
        foreach (RunConfiguration configuration in Configurations ?? [])
        {
            if (configuration is null || string.IsNullOrWhiteSpace(configuration.Name) || string.IsNullOrWhiteSpace(configuration.Project) || !names.Add(configuration.Name))
            {
                continue;
            }
            kept.Add(configuration with
            {
                Framework = configuration.Framework ?? string.Empty,
                LaunchProfile = configuration.LaunchProfile ?? string.Empty,
                Program = configuration.Program ?? string.Empty,
                Arguments = configuration.Arguments ?? string.Empty,
                Environment = (configuration.Environment ?? []).Where(v => !string.IsNullOrEmpty(v.Key)).ToDictionary(v => v.Key, v => v.Value ?? string.Empty),
                WorkingDirectory = configuration.WorkingDirectory ?? string.Empty,
                BrowserUrl = configuration.BrowserUrl ?? string.Empty,
                Console = Enum.IsDefined(configuration.Console) ? configuration.Console : ConsoleKind.Internal,
            });
        }
        return this with { Configurations = kept };
    }
}
