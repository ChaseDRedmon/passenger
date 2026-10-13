using System.Text.RegularExpressions;
using Passenger.App.Workspace;
using Passenger.Core;
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

/// <summary>A build's end and what to tell the person about it, with what it found and its last words: the build's own,
/// whatever the Problems and Build windows show by the time a caller reads them.</summary>
public sealed record BuildOutcome(BuildStatus Status, string Message)
{
    public bool Succeeded => Status == BuildStatus.Succeeded;

    /// <summary>The errors and warnings the build reported, each once, as Problems lists them.</summary>
    public IReadOnlyList<Problem> Problems { get; init; } = [];

    /// <summary>The last lines the build wrote.</summary>
    public IReadOnlyList<string> Output { get; init; } = [];
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
    /// lines not wrapped, so every problem is one line read once. A framework given builds a project for that one alone,
    /// as a run configuration's build before launch does.</summary>
    public static IReadOnlyList<string> Arguments(string target, BuildKind kind = BuildKind.Build, string? framework = null)
    {
        List<string> arguments = kind switch
        {
            BuildKind.Clean => ["clean", target],
            BuildKind.Rebuild => ["build", target, "--no-incremental"],
            _ => ["build", target],
        };
        if (framework is not null)
        {
            arguments.AddRange(["--framework", framework]);
        }
        arguments.AddRange(["-nologo", "-tl:off", "-clp:NoSummary;ForceNoAlign"]);
        return arguments;
    }

    public static Problem? Parse(string line)
    {
        Match match = Diagnostic().Match(line);
        if (!match.Success)
        {
            return null;
        }
        ProblemSeverity severity = match.Groups["severity"].Value == "error" ? ProblemSeverity.Error : ProblemSeverity.Warning;
        string? path = match.Groups["file"].Value.Trim() is { Length: > 0 } file && !file.StartsWith("MSBUILD", StringComparison.Ordinal) ? file : null;
        // MSBuild names a file as the project or solution it came through spelled it, which may not be the disk's case.
        path = path is not null && System.IO.Path.IsPathFullyQualified(path) ? PathNames.Canonical(path) : path;
        int lineNumber = int.TryParse(match.Groups["line"].Value, out int l) ? l - 1 : 0;
        int column = int.TryParse(match.Groups["column"].Value, out int c) ? c - 1 : 0;
        string? project = match.Groups["project"].Success ? System.IO.Path.GetFileNameWithoutExtension(match.Groups["project"].Value) : null;
        return new Problem(severity, match.Groups["message"].Value.Trim(), match.Groups["code"].Value, path, new TextPosition(lineNumber, column), project);
    }

    /// <summary>The location is the parenthesis just before ": error", so the path may hold parentheses of its own
    /// ("Shop (copy)", "C:\Program Files (x86)").</summary>
    [GeneratedRegex(@"^\s*(?<file>.*?)(?:\((?<line>\d+)(?:,(?<column>\d+))?(?:[-,]\d+)*\))?\s*:\s*(?<severity>error|warning)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*?)(?:\s+\[(?<project>[^\]]+)\])?\s*$")]
    private static partial Regex Diagnostic();
}
