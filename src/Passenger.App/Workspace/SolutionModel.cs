using System.Text.RegularExpressions;
using System.Xml.Linq;
using Passenger.Core;

namespace Passenger.App.Workspace;

/// <summary>A solution as its files say it is: its folders and projects from the .slnx or .sln, and
/// each SDK-style project's own declarations (output type, target frameworks, packages, project
/// references) from its project file. Read directly, without MSBuild: enough for the Solution view
/// and to know what can run. Evaluation proper (conditions, imports, globbing rules) belongs to the
/// Host process when it comes.</summary>
public sealed record SolutionModel(string Path, string Name, IReadOnlyList<SolutionEntry> Entries)
{
    /// <summary>The solution folder's GUID in a classic .sln, which marks an entry as a folder.</summary>
    private const string SolutionFolderType = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    public IEnumerable<ProjectModel> Projects => Entries.SelectMany(Flatten);

    /// <summary>Reads a solution, its own path and its projects' named as the file system names them
    /// (<see cref="PathNames.Canonical"/>), whatever letter case or links the solution's lines use.</summary>
    public static SolutionModel? Load(string path)
    {
        path = PathNames.Canonical(path);
        try
        {
            return System.IO.Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase) ? LoadSlnx(path) : LoadSln(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>The solution at a folder's top: the one named for the folder if there are several, and of a .sln and a
    /// .slnx alike the .slnx, which <c>dotnet sln migrate</c> writes beside the .sln it leaves; none in a folder that is
    /// gone (deleted under the window) or cannot be read. The window asks <see cref="WorkspaceModel"/>, never this.</summary>
    public static string? Find(string folder)
    {
        List<string> found;
        try
        {
            found = [.. Directory.EnumerateFiles(folder).Where(f => System.IO.Path.GetExtension(f).ToLowerInvariant() is ".sln" or ".slnx")];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        string name = System.IO.Path.GetFileName(folder);
        return found
            .OrderBy(f => System.IO.Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(f => f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(f => f, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static SolutionModel LoadSlnx(string path)
    {
        string root = System.IO.Path.GetDirectoryName(path)!;
        XElement solution = SharedRead.Xml(path).Root!;
        List<SolutionEntry> entries = [];
        foreach (XElement element in solution.Elements())
        {
            AddSlnx(element, root, entries);
        }
        return new SolutionModel(path, System.IO.Path.GetFileNameWithoutExtension(path), entries);
    }

    private static void AddSlnx(XElement element, string root, List<SolutionEntry> into)
    {
        if (element.Name.LocalName == "Project" && element.Attribute("Path")?.Value is { } relative)
        {
            into.Add(ProjectModel.Load(System.IO.Path.Combine(root, relative.Replace('\\', '/'))));
        }
        else if (element.Name.LocalName == "Folder" && element.Attribute("Name")?.Value is { } name)
        {
            List<SolutionEntry> children = [];
            foreach (XElement child in element.Elements())
            {
                AddSlnx(child, root, children);
            }
            // A folder named "/src/tools/" is "tools" inside "src": slnx writes the whole path on each.
            string[] parts = name.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            List<SolutionEntry> level = into;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                SolutionFolder? parent = level.OfType<SolutionFolder>().FirstOrDefault(f => f.Name == parts[i]);
                if (parent is null)
                {
                    parent = new SolutionFolder(parts[i], []);
                    level.Add(parent);
                }
                level = parent.Children;
            }
            SolutionFolder? existing = level.OfType<SolutionFolder>().FirstOrDefault(f => f.Name == parts[^1]);
            if (existing is null)
            {
                level.Add(new SolutionFolder(parts[^1], children));
            }
            else
            {
                existing.Children.AddRange(children);
            }
        }
    }

    private static SolutionModel LoadSln(string path)
    {
        string root = System.IO.Path.GetDirectoryName(path)!;
        Regex project = new(@"^Project\(""\{(?<type>[^}]+)\}""\)\s*=\s*""(?<name>[^""]*)"",\s*""(?<path>[^""]*)"",\s*""\{(?<id>[^}]+)\}""");
        Regex nesting = new("""^\s*\{(?<child>[^}]+)\}\s*=\s*\{(?<parent>[^}]+)\}""");
        Dictionary<string, SolutionEntry> byId = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> parents = new(StringComparer.OrdinalIgnoreCase);
        List<string> order = [];
        bool inNesting = false;
        foreach (string line in SharedRead.Lines(path))
        {
            if (project.Match(line) is { Success: true } m)
            {
                string id = m.Groups["id"].Value;
                byId[id] = m.Groups["type"].Value.Equals(SolutionFolderType, StringComparison.OrdinalIgnoreCase)
                    ? new SolutionFolder(m.Groups["name"].Value, [])
                    : ProjectModel.Load(System.IO.Path.Combine(root, m.Groups["path"].Value.Replace('\\', '/')));
                order.Add(id);
            }
            else if (line.Contains("GlobalSection(NestedProjects)", StringComparison.Ordinal))
            {
                inNesting = true;
            }
            else if (inNesting && line.Contains("EndGlobalSection", StringComparison.Ordinal))
            {
                inNesting = false;
            }
            else if (inNesting && nesting.Match(line) is { Success: true } n)
            {
                parents[n.Groups["child"].Value] = n.Groups["parent"].Value;
            }
        }
        List<SolutionEntry> top = [];
        foreach (string id in order)
        {
            if (parents.TryGetValue(id, out string? parent) && byId.GetValueOrDefault(parent) is SolutionFolder folder)
            {
                folder.Children.Add(byId[id]);
            }
            else
            {
                top.Add(byId[id]);
            }
        }
        return new SolutionModel(path, System.IO.Path.GetFileNameWithoutExtension(path), top);
    }

    private static IEnumerable<ProjectModel> Flatten(SolutionEntry entry) => entry switch
    {
        ProjectModel project => [project],
        SolutionFolder folder => folder.Children.SelectMany(Flatten),
        _ => [],
    };
}

public abstract record SolutionEntry(string Name);

public sealed record SolutionFolder(string Name, List<SolutionEntry> Children) : SolutionEntry(Name);

/// <summary>One project, from its project file's own declarations.</summary>
public sealed record ProjectModel(
    string Path,
    string Name,
    string? OutputType,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<(string Name, string? Version)> Packages,
    IReadOnlyList<string> ProjectReferences,
    bool Exists,
    bool DeclaredTest = false) : SolutionEntry(Name)
{
    private static readonly HashSet<string> TestPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.NET.Test.Sdk", "xunit.runner.visualstudio", "NUnit3TestAdapter", "MSTest.TestAdapter", "MSTest",
    };

    public string Folder => System.IO.Path.GetDirectoryName(Path)!;

    /// <summary>The SDKs that make a project an executable without its saying so: their own props set OutputType.</summary>
    private static readonly HashSet<string> ExecutableSdks = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.NET.Sdk.Web", "Microsoft.NET.Sdk.Worker", "Microsoft.NET.Sdk.BlazorWebAssembly",
    };

    /// <summary>Whether it holds tests VSTest runs: it says so (IsTestProject, MSTest.Sdk), or references a test SDK or adapter.</summary>
    public bool IsTestProject => DeclaredTest || Packages.Any(p => TestPackages.Contains(p.Name));

    /// <summary>Whether it builds something that runs: an executable, not a library or a test.</summary>
    public bool IsRunnable => (string.Equals(OutputType, "Exe", StringComparison.OrdinalIgnoreCase)
        || string.Equals(OutputType, "WinExe", StringComparison.OrdinalIgnoreCase)) && !IsTestProject;

    /// <summary>The project a file or folder belongs to: of the projects whose folders hold it, the one nearest it, as a
    /// nested project's files are its own; null when none does. Paths are canonical, as the projects' are.</summary>
    public static ProjectModel? Owning(IEnumerable<ProjectModel> projects, string path) =>
        projects.Where(p => PathNames.Within(path, p.Folder)).MaxBy(p => p.Folder.Length);

    /// <summary>Reads a project file, its path (and those it references) as the file system names them.</summary>
    internal static ProjectModel Load(string path)
    {
        path = PathNames.Canonical(path);
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        if (!File.Exists(path))
        {
            return new ProjectModel(path, name, null, [], [], [], false);
        }
        // A project file being written (dotnet add package) can be read half done: it is read again a moment later.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return Read(path, name, SharedRead.Xml(path).Root!);
            }
            catch (Exception error) when (error is System.Xml.XmlException or IOException && attempt < 3)
            {
                Thread.Sleep(50);
            }
            catch (Exception error) when (error is System.Xml.XmlException or IOException)
            {
                return new ProjectModel(path, name, null, [], [], [], true);
            }
        }
    }

    private static ProjectModel Read(string path, string name, XElement project)
    {
        IReadOnlyList<XElement> shared = SharedProperties(System.IO.Path.GetDirectoryName(path)!);
        IReadOnlyList<string> sdks = Sdks(project);
        // The project's own value wins over the one Directory.Build.props gives every project, as MSBuild's order has it.
        string? Property(string key) => Declared(project, key) ?? shared.Select(props => Declared(props, key)).FirstOrDefault(v => v is not null);
        List<string> frameworks = (Property("TargetFrameworks") ?? Property("TargetFramework") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        // Under central package management the version is the Directory.Packages.props one, not the project's.
        IReadOnlyDictionary<string, string> central = CentralVersions(System.IO.Path.GetDirectoryName(path)!);
        List<(string, string?)> packages = project.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference" && e.Attribute("Include") is not null)
            .Select(e => (e.Attribute("Include")!.Value, e.Attribute("Version")?.Value ?? e.Elements().FirstOrDefault(c => c.Name.LocalName == "Version")?.Value
                ?? e.Attribute("VersionOverride")?.Value ?? central.GetValueOrDefault(e.Attribute("Include")!.Value)))
            .ToList();
        List<string> references = project.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference" && e.Attribute("Include") is not null)
            .Select(e => PathNames.Canonical(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, e.Attribute("Include")!.Value.Replace('\\', '/'))))
            .ToList();
        string? outputType = Property("OutputType") ?? (sdks.Any(ExecutableSdks.Contains) ? "Exe" : null);
        if (Property("AssemblyName") is { Length: > 0 } assembly && assembly != name)
        {
            name = $"{name} ({assembly})";
        }
        // Test packages a Directory.Build.props gives every project under it make those projects tests too.
        bool test = string.Equals(Property("IsTestProject"), "true", StringComparison.OrdinalIgnoreCase)
            || sdks.Contains("MSTest.Sdk", StringComparer.OrdinalIgnoreCase)
            || shared.Any(props => props.Descendants().Any(e => e.Name.LocalName == "PackageReference" && TestPackages.Contains(e.Attribute("Include")?.Value ?? string.Empty)));
        return new ProjectModel(path, name, outputType, frameworks, packages, references, true, test);
    }

    /// <summary>The SDKs a project names, without their versions: Sdk="A;B/1.0" on the project, &lt;Sdk Name&gt; and &lt;Import Sdk&gt;.</summary>
    private static IReadOnlyList<string> Sdks(XElement project) =>
        [.. new[] { project.Attribute("Sdk")?.Value }
            .Concat(project.Elements().Where(e => e.Name.LocalName == "Sdk").Select(e => e.Attribute("Name")?.Value))
            .Concat(project.Elements().Where(e => e.Name.LocalName == "Import").Select(e => e.Attribute("Sdk")?.Value))
            .SelectMany(names => (names ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(sdk => sdk.Split('/')[0])];

    /// <summary>A property's value as the file sets it outside any condition, the last setting winning as in MSBuild;
    /// a conditional one only when there is no other.</summary>
    private static string? Declared(XElement root, string key)
    {
        string? plain = null;
        string? conditional = null;
        foreach (XElement group in root.Descendants().Where(g => g.Name.LocalName == "PropertyGroup"))
        {
            foreach (XElement set in group.Elements().Where(e => e.Name.LocalName == key && e.Value.Trim().Length > 0))
            {
                if (group.Parent == root && group.Attribute("Condition") is null && set.Attribute("Condition") is null)
                {
                    plain = set.Value.Trim();
                }
                else
                {
                    conditional ??= set.Value.Trim();
                }
            }
        }
        return plain ?? conditional;
    }

    /// <summary>The versions the nearest Directory.Packages.props above a project sets (central package management), by
    /// package; none when there is no such file.</summary>
    public static IReadOnlyDictionary<string, string> CentralVersions(string folder)
    {
        for (DirectoryInfo? at = new(folder); at is not null; at = at.Parent)
        {
            string props = System.IO.Path.Combine(at.FullName, "Directory.Packages.props");
            if (!File.Exists(props))
            {
                continue;
            }
            try
            {
                return SharedRead.Xml(props).Root!.Descendants()
                    .Where(e => e.Name.LocalName == "PackageVersion" && e.Attribute("Include") is not null && e.Attribute("Version") is not null)
                    .GroupBy(e => e.Attribute("Include")!.Value, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Attribute("Version")!.Value, StringComparer.OrdinalIgnoreCase);
            }
            catch (System.Xml.XmlException)
            {
                return new Dictionary<string, string>();
            }
        }
        return new Dictionary<string, string>();
    }

    /// <summary>The Directory.Build.props MSBuild imports for a project, the nearest above it, and the ones it goes on to
    /// import from further up (GetPathOfFileAbove), nearest first: where a repository sets the target framework for all
    /// its projects. Read as declared, until MSBuild evaluates projects.</summary>
    private static IReadOnlyList<XElement> SharedProperties(string folder)
    {
        List<XElement> found = [];
        for (DirectoryInfo? at = new(folder); at is not null; at = at.Parent)
        {
            string props = System.IO.Path.Combine(at.FullName, "Directory.Build.props");
            if (!File.Exists(props))
            {
                continue;
            }
            XElement root;
            try
            {
                root = SharedRead.Xml(props).Root!;
            }
            catch (Exception error) when (error is System.Xml.XmlException or IOException)
            {
                break;
            }
            found.Add(root);
            bool importsAbove = root.Elements().Any(e => e.Name.LocalName == "Import"
                && (e.Attribute("Project")?.Value ?? string.Empty).Contains("Directory.Build.props", StringComparison.OrdinalIgnoreCase));
            if (!importsAbove)
            {
                break;
            }
        }
        return found;
    }
}

/// <summary>A solution's or project's file read without ever standing in the way of a program writing it: Windows refuses
/// to let a file be written while a reader holds it without sharing writes, and the folder watcher reads a project the
/// moment it changes, just as dotnet add package or a checkout goes on to write it again.</summary>
internal static class SharedRead
{
    public static XDocument Xml(string path)
    {
        using FileStream stream = Open(path);
        return XDocument.Load(stream);
    }

    public static IEnumerable<string> Lines(string path)
    {
        using StreamReader reader = new(Open(path));
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
