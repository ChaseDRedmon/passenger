using System.Text.RegularExpressions;
using Passenger.App.Workspace;
using Passenger.Text;

namespace Passenger.App.Run;

/// <summary>A problem a build or the language server reported: where it is and what it says.</summary>
public sealed record Problem(ProblemSeverity Severity, string Message, string? Code, string? Path, TextPosition Position, string? Project)
{
    public string Where => Path is null ? Project ?? string.Empty : $"{System.IO.Path.GetFileName(Path)}:{Position.Line + 1}:{Position.Column + 1}";
}

public enum ProblemSeverity
{
    Error,
    Warning,
}

/// <summary>Something that can be run from the toolbar: an executable project, or one of its launch
/// profiles (named "Project: profile", as Rider names them), run with <c>dotnet run</c>, which builds it
/// first.</summary>
public sealed record RunConfiguration(string Name, ProjectModel Project, LaunchProfile? Profile = null)
{
    public static IReadOnlyList<RunConfiguration> From(SolutionModel? solution, string folder)
    {
        IEnumerable<ProjectModel> projects = solution?.Projects
            ?? ProjectModel.InFolder(folder);
        List<RunConfiguration> configurations = [];
        foreach (ProjectModel project in projects.Where(p => p.Exists && p.IsRunnable))
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(project.Path);
            IReadOnlyList<LaunchProfile> profiles = LaunchProfile.Read(project);
            if (profiles.Count == 0)
            {
                configurations.Add(new RunConfiguration(name, project));
            }
            configurations.AddRange(profiles.Select(p => new RunConfiguration($"{name}: {p.Name}", project, p)));
        }
        return configurations;
    }

    /// <summary>The program's arguments: the ones given, or else the profile's.</summary>
    public IReadOnlyList<string> Arguments(string? given = null) => CommandLine.Split(given ?? Profile?.CommandLineArgs);

    /// <summary><c>dotnet run</c>'s own arguments for this configuration, with the program's after <c>--</c>
    /// when they replace the profile's.</summary>
    public IReadOnlyList<string> RunArguments(string? given = null)
    {
        List<string> arguments = ["run", "--project", Project.Path];
        arguments.AddRange(Profile is null ? ["--no-launch-profile"] : ["--launch-profile", Profile.Name]);
        if (given is not null)
        {
            arguments.Add("--");
            arguments.AddRange(CommandLine.Split(given));
        }
        return arguments;
    }
}

/// <summary>How a build ended, as each caller hears it.</summary>
public enum BuildStatus
{
    Succeeded,
    Failed,
    Stopped,

    /// <summary>Not begun: another build was under way, or there was nothing to build.</summary>
    Refused,

    /// <summary>The .NET command line could not be started (no SDK, a folder gone).</summary>
    NotStarted,
}

/// <summary>A build's end and what to tell the person about it.</summary>
public sealed record BuildOutcome(BuildStatus Status, string Message)
{
    public bool Succeeded => Status == BuildStatus.Succeeded;
}

/// <summary>What a build does: Rider's Build, Rebuild (everything again, nothing taken from the last build) and Clean.</summary>
public enum BuildKind
{
    Build,
    Rebuild,
    Clean,
}

/// <summary>Reads MSBuild's error and warning lines ("File.cs(12,5): error CS0103: ... [Project.csproj]")
/// out of a build's output.</summary>
public static partial class BuildOutput
{
    /// <summary>The arguments a build, rebuild or clean runs with: no terminal logger animation, no repeated summary,
    /// lines not wrapped, so every problem is one line read once.</summary>
    public static IReadOnlyList<string> Arguments(string target, BuildKind kind = BuildKind.Build) => kind switch
    {
        BuildKind.Clean => ["clean", target, "-nologo", "-tl:off", "-clp:NoSummary;ForceNoAlign"],
        BuildKind.Rebuild => ["build", target, "--no-incremental", "-nologo", "-tl:off", "-clp:NoSummary;ForceNoAlign"],
        _ => ["build", target, "-nologo", "-tl:off", "-clp:NoSummary;ForceNoAlign"],
    };

    public static Problem? Parse(string line)
    {
        Match match = Diagnostic().Match(line);
        if (!match.Success)
        {
            return null;
        }
        ProblemSeverity severity = match.Groups["severity"].Value == "error" ? ProblemSeverity.Error : ProblemSeverity.Warning;
        string? path = match.Groups["file"].Value.Trim() is { Length: > 0 } file && !file.StartsWith("MSBUILD", StringComparison.Ordinal) ? file : null;
        int lineNumber = int.TryParse(match.Groups["line"].Value, out int l) ? l - 1 : 0;
        int column = int.TryParse(match.Groups["column"].Value, out int c) ? c - 1 : 0;
        string? project = match.Groups["project"].Success ? System.IO.Path.GetFileNameWithoutExtension(match.Groups["project"].Value) : null;
        return new Problem(severity, match.Groups["message"].Value.Trim(), match.Groups["code"].Value, path, new TextPosition(lineNumber, column), project);
    }

    [GeneratedRegex(@"^\s*(?<file>[^(]*?)(?:\((?<line>\d+)(?:,(?<column>\d+))?(?:,\d+,\d+)?\))?\s*:\s*(?<severity>error|warning)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*?)(?:\s+\[(?<project>[^\]]+)\])?\s*$")]
    private static partial Regex Diagnostic();
}
