using System.Globalization;
using System.Xml.Linq;
using Passenger.App.Workspace;
using Passenger.Core;

namespace Passenger.App.Testing;

/// <summary>A test project's assembly for one of its target frameworks, where its build puts it.</summary>
public sealed record TestAssembly(ProjectModel Project, string Framework, string Path);

/// <summary>Where the test projects build their assemblies, one per target framework, asked of MSBuild for all of them in
/// one request rather than a process per project: each project's target frameworks as MSBuild evaluates them (from a
/// property, an import, Directory.Build.props), then each framework's TargetPath, through the targets MSBuild's own
/// project references use (GetTargetFrameworks, GetTargetPath).</summary>
public static class TestAssemblies
{
    private const string Marker = "passenger-test-assembly";

    /// <summary>The assemblies, built or not; a project MSBuild cannot evaluate is logged and left out.</summary>
    public static async Task<IReadOnlyList<TestAssembly>> ResolveAsync(IReadOnlyList<ProjectModel> projects, string folder, Action<string> log, CancellationToken cancel)
    {
        if (projects.Count == 0)
        {
            return [];
        }
        // A file of its own, outside the folder: MSBuild reads a project only from a file.
        string request = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"passenger-tests-{Environment.ProcessId}-{Guid.NewGuid():N}.proj");
        await File.WriteAllTextAsync(request, Request(projects).ToString(), cancel);
        try
        {
            ChildResult msbuild = await ChildProcesses.RunAsync(
                ChildProcesses.DotnetCommand(["msbuild", request, "-t:PassengerTestAssemblies", "-nologo", "-tl:off", "-v:m", "-nodeReuse:false", "-clp:NoSummary;ForceNoAlign"], folder),
                cancel: cancel);
            List<(int Project, int Framework, TestAssembly Assembly)> found = [];
            foreach (string line in msbuild.Output.Split('\n'))
            {
                string[] fields = line.Trim().Split('\t');
                if (fields.Length == 5 && fields[0] == Marker && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index < projects.Count)
                {
                    found.Add((index, Array.IndexOf(fields[3].Split(';', StringSplitOptions.TrimEntries), fields[2]), new TestAssembly(projects[index], fields[2], PathNames.Canonical(fields[4]))));
                }
                else if (line.Contains(": error ", StringComparison.Ordinal) || line.Contains(": warning MSB", StringComparison.Ordinal))
                {
                    log(line.Trim());
                }
            }
            if (found.Count == 0 && !msbuild.Succeeded)
            {
                throw new InvalidOperationException($"MSBuild could not say where the test projects build: {msbuild.Error.Trim()} {msbuild.Output.Trim()}".Trim());
            }
            return [.. found.OrderBy(f => f.Project).ThenBy(f => f.Framework).Select(f => f.Assembly)];
        }
        finally
        {
            try
            {
                File.Delete(request);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>The request: each project's frameworks, then each framework's assembly, written out a line each, a project
    /// at a time so one that does not evaluate leaves the others. The MSBuild task copies a project item's metadata onto
    /// what its targets return, which carries the project's index and the framework through.</summary>
    private static XElement Request(IReadOnlyList<ProjectModel> projects) =>
        new("Project",
            new XElement("ItemGroup", projects.Select((project, index) => new XElement("PassengerTestProject",
                new XAttribute("Include", Escape(project.Path)),
                new XAttribute("PassengerIndex", index.ToString(CultureInfo.InvariantCulture))))),
            new XElement("Target", new XAttribute("Name", "PassengerTestAssemblies"),
                new XElement("MSBuild",
                    new XAttribute("Projects", "@(PassengerTestProject)"),
                    new XAttribute("Targets", "GetTargetFrameworks"),
                    new XAttribute("Condition", "'%(PassengerTestProject.PassengerIndex)' != ''"),
                    new XAttribute("SkipNonexistentProjects", "true"),
                    new XAttribute("ContinueOnError", "true"),
                    new XElement("Output", new XAttribute("TaskParameter", "TargetOutputs"), new XAttribute("ItemName", "_PassengerProject"))),
                new XElement("ItemGroup",
                    new XElement("_PassengerFramework",
                        new XAttribute("Include", "%(_PassengerProject.TargetFrameworks)"),
                        new XAttribute("PassengerProject", "%(_PassengerProject.Identity)"),
                        new XAttribute("PassengerIndex", "%(_PassengerProject.PassengerIndex)"),
                        new XAttribute("PassengerSingle", "%(_PassengerProject.HasSingleTargetFramework)"),
                        new XAttribute("PassengerFrameworks", "%(_PassengerProject.TargetFrameworks)")),
                    // A project of one framework is asked as its build builds it, with no TargetFramework of the request's.
                    new XElement("_PassengerBuild",
                        new XAttribute("Include", "@(_PassengerFramework->'%(PassengerProject)')"),
                        new XAttribute("PassengerFramework", "%(Identity)"),
                        new XAttribute("AdditionalProperties", "TargetFramework=%(Identity)"),
                        new XAttribute("Condition", "'%(PassengerSingle)' != 'true'")),
                    new XElement("_PassengerBuild",
                        new XAttribute("Include", "@(_PassengerFramework->'%(PassengerProject)')"),
                        new XAttribute("PassengerFramework", "%(Identity)"),
                        new XAttribute("Condition", "'%(PassengerSingle)' == 'true'"))),
                new XElement("MSBuild",
                    new XAttribute("Projects", "@(_PassengerBuild)"),
                    new XAttribute("Targets", "GetTargetPath"),
                    new XAttribute("Condition", "'%(_PassengerBuild.PassengerIndex)/%(_PassengerBuild.PassengerFramework)' != ''"),
                    new XAttribute("ContinueOnError", "true"),
                    new XElement("Output", new XAttribute("TaskParameter", "TargetOutputs"), new XAttribute("ItemName", "_PassengerAssembly"))),
                new XElement("Message",
                    new XAttribute("Importance", "high"),
                    new XAttribute("Text", $"{Marker}%09%(_PassengerAssembly.PassengerIndex)%09%(_PassengerAssembly.PassengerFramework)%09%(_PassengerAssembly.PassengerFrameworks)%09%(_PassengerAssembly.FullPath)"))));

    /// <summary>A path as an item's Include takes it literally: MSBuild's own escapes for its special characters.</summary>
    private static string Escape(string path)
    {
        System.Text.StringBuilder escaped = new(path.Length);
        foreach (char c in path)
        {
            escaped.Append(c is '%' or ';' or '$' or '@' or '\'' or '*' or '?' or '(' or ')' ? $"%{(int)c:X2}" : c.ToString());
        }
        return escaped.ToString();
    }
}
