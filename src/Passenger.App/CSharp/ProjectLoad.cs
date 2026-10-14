using System.Text.RegularExpressions;
using Passenger.App.Run;
using Passenger.Core;

namespace Passenger.App.CSharp;

/// <summary>What the language server says to the person, for the status bar: its words, and whether they are an error or a
/// warning (null for neither).</summary>
public sealed record ServerSaid(string Text, ProblemSeverity? Severity);

/// <summary>A warning or error the language server met loading a project (a package with a known vulnerability, an SDK
/// that cannot be found): the project file, canonical, and what MSBuild or NuGet said, less its repeat of the
/// file.</summary>
public sealed record ProjectLoadProblem(string Project, ProblemSeverity Severity, string Message);

/// <summary>What a line of the server's project system says of a load.</summary>
internal enum LoadReportKind
{
    /// <summary>A warning or error of a project's load.</summary>
    Problem,

    /// <summary>A project loaded with neither.</summary>
    Loaded,

    /// <summary>The server finished loading (or reloading) every project it was loading.</summary>
    BatchEnded,
}

/// <summary>A line Roslyn's project system logs as it loads projects: "Warning while loading P: message" and "Error while
/// loading P: message" (its kind and path its own, the message MSBuild's or NuGet's), "Successfully completed load of P"
/// when there were none, and "Completed (re)load of all projects in T" at a batch's end. The last two are the server's
/// English resources; a server in another language leaves a project's problems in place until it reports again.</summary>
internal sealed partial record LoadReport(LoadReportKind Kind, ProjectLoadProblem? Problem, string? Project)
{
    /// <summary>The report a logged line is, its path canonical (read on the server's thread, where the file system may be
    /// asked); null for any other line.</summary>
    public static LoadReport? Read(string logged)
    {
        if (ProblemLine().Match(logged) is { Success: true } problem)
        {
            string given = problem.Groups["path"].Value;
            string message = problem.Groups["message"].Value.Trim();
            // MSBuild ends an error with the project it came from, which the line has named already.
            if (message.EndsWith(given, StringComparison.Ordinal))
            {
                message = message[..^given.Length].TrimEnd();
            }
            message = Spaces().Replace(message, " ");
            ProblemSeverity severity = problem.Groups["kind"].Value == "Error" ? ProblemSeverity.Error : ProblemSeverity.Warning;
            return new LoadReport(LoadReportKind.Problem, new ProjectLoadProblem(PathNames.Canonical(given), severity, message), null);
        }
        if (LoadedLine().Match(logged) is { Success: true } loaded)
        {
            return new LoadReport(LoadReportKind.Loaded, null, PathNames.Canonical(loaded.Groups["path"].Value.Trim()));
        }
        return BatchLine().IsMatch(logged) ? new LoadReport(LoadReportKind.BatchEnded, null, null) : null;
    }

    [GeneratedRegex(@"^\s*(?:\[[^\]]*\]\s*)*(?<kind>Warning|Error) while loading (?<path>.+?\.(?:[A-Za-z]+proj|cs)): (?<message>[\s\S]*)$")]
    private static partial Regex ProblemLine();

    [GeneratedRegex(@"^\s*(?:\[[^\]]*\]\s*)*Successfully completed load of (?<path>.+?\.(?:[A-Za-z]+proj|cs))\s*$")]
    private static partial Regex LoadedLine();

    [GeneratedRegex(@"^\s*(?:\[[^\]]*\]\s*)*Completed \(re\)load of all projects")]
    private static partial Regex BatchLine();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
