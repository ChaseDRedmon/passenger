using System.Text.RegularExpressions;
using System.Xml.Linq;

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

    public static SolutionModel? Load(string path)
    {
        try
        {
            return System.IO.Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase) ? LoadSlnx(path) : LoadSln(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>The solution at a folder's top, the one named for the folder if there are several.</summary>
    public static string? Find(string folder)
    {
        List<string> found = Directory.EnumerateFiles(folder, "*.slnx").Concat(Directory.EnumerateFiles(folder, "*.sln")).ToList();
        string name = System.IO.Path.GetFileName(folder);
        return found.FirstOrDefault(f => System.IO.Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? found.OrderBy(f => f.EndsWith(".slnx", StringComparison.Ordinal) ? 0 : 1).ThenBy(f => f, StringComparer.Ordinal).FirstOrDefault();
    }

    private static SolutionModel LoadSlnx(string path)
    {
        string root = System.IO.Path.GetDirectoryName(path)!;
        XElement solution = XDocument.Load(path).Root!;
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
            into.Add(ProjectModel.Load(System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative.Replace('\\', '/')))));
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
        foreach (string line in File.ReadLines(path))
        {
            if (project.Match(line) is { Success: true } m)
            {
                string id = m.Groups["id"].Value;
                byId[id] = m.Groups["type"].Value.Equals(SolutionFolderType, StringComparison.OrdinalIgnoreCase)
                    ? new SolutionFolder(m.Groups["name"].Value, [])
                    : ProjectModel.Load(System.IO.Path.GetFullPath(System.IO.Path.Combine(root, m.Groups["path"].Value.Replace('\\', '/'))));
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
    bool Exists) : SolutionEntry(Name)
{
    public string Folder => System.IO.Path.GetDirectoryName(Path)!;

    /// <summary>Whether it builds something that runs: an executable, not a library or a test.</summary>
    public bool IsRunnable => OutputType is "Exe" or "WinExe";

    public static ProjectModel Load(string path)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        if (!File.Exists(path))
        {
            return new ProjectModel(path, name, null, [], [], [], false);
        }
        try
        {
            XElement project = XDocument.Load(path).Root!;
            IReadOnlyList<XElement> shared = SharedProperties(System.IO.Path.GetDirectoryName(path)!);
            // The project's own value wins over the one Directory.Build.props gives every project, as MSBuild's order has it.
            string? Property(string key) => Declared(project, key) ?? shared.Select(props => Declared(props, key)).FirstOrDefault(v => v is not null);
            List<string> frameworks = (Property("TargetFrameworks") ?? Property("TargetFramework") ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            List<(string, string?)> packages = project.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference" && e.Attribute("Include") is not null)
                .Select(e => (e.Attribute("Include")!.Value, e.Attribute("Version")?.Value ?? e.Elements().FirstOrDefault(c => c.Name.LocalName == "Version")?.Value))
                .ToList();
            List<string> references = project.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference" && e.Attribute("Include") is not null)
                .Select(e => System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, e.Attribute("Include")!.Value.Replace('\\', '/'))))
                .ToList();
            string? outputType = Property("OutputType");
            if (Property("AssemblyName") is { Length: > 0 } assembly && assembly != name)
            {
                name = $"{name} ({assembly})";
            }
            return new ProjectModel(path, name, outputType, frameworks, packages, references, true);
        }
        catch (System.Xml.XmlException)
        {
            return new ProjectModel(path, name, null, [], [], [], true);
        }
    }

    private static string? Declared(XElement root, string key) =>
        root.Descendants().FirstOrDefault(e => e.Name.LocalName == key)?.Value.Trim() is { Length: > 0 } value ? value : null;

    /// <summary>The Directory.Build.props files above a project, nearest first: where a repository sets
    /// the target framework for all its projects. Read as declared, until MSBuild evaluates projects.</summary>
    private static IReadOnlyList<XElement> SharedProperties(string folder)
    {
        List<XElement> found = [];
        for (DirectoryInfo? at = new(folder); at is not null; at = at.Parent)
        {
            string props = System.IO.Path.Combine(at.FullName, "Directory.Build.props");
            if (File.Exists(props))
            {
                try
                {
                    found.Add(XDocument.Load(props).Root!);
                }
                catch (System.Xml.XmlException)
                {
                }
            }
        }
        return found;
    }
}
