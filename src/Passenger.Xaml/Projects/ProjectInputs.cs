using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Passenger.Xaml.Projects;

/// <summary>What a project's XAML is compiled against, read without MSBuild: the assemblies its restore resolved
/// (<c>project.assets.json</c>: each package's compile assets, the framework's reference pack), its project references,
/// its own C# files and the usings its SDK adds to them, and the items a dialect reads from its project file
/// (Avalonia's <c>AvaloniaResource</c>). A project not yet restored has no assemblies until it is.</summary>
public sealed record ProjectInputs(
    string ProjectPath,
    string AssemblyName,
    string? TargetFramework,
    IReadOnlyList<string> References,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> ImplicitUsings,
    string? AssetsFile,
    IReadOnlyList<string> Packages)
{
    public string Folder => Path.GetDirectoryName(ProjectPath)!;

    /// <summary>Whether the project references a package, by its id.</summary>
    public bool Uses(string package) => Packages.Contains(package, StringComparer.OrdinalIgnoreCase);

    /// <summary>The version of each package its restore resolved, by id.</summary>
    public IReadOnlyDictionary<string, string> PackageVersions { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where each package its restore resolved is, by id: the folder of its version in the package cache.</summary>
    public IReadOnlyDictionary<string, string> PackageFolders { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The shared frameworks its restore says it references, a profile's own name kept
    /// (<c>Microsoft.NETCore.App</c>, <c>Microsoft.WindowsDesktop.App.WPF</c>).</summary>
    public IReadOnlyList<string> FrameworkReferences { get; init; } = [];

    /// <summary>A property as the project sets it: its own file's, else the nearest Directory.Build.props' above it; null
    /// when neither sets it plainly (without other properties in it).</summary>
    public string? PropertyValue(string name)
    {
        if (Property(Load(ProjectPath), name) is { } own)
        {
            return own;
        }
        for (DirectoryInfo? at = new(Folder); at is not null; at = at.Parent)
        {
            string props = Path.Combine(at.FullName, "Directory.Build.props");
            if (File.Exists(props))
            {
                return Property(Load(props), name);
            }
        }
        return null;
    }

    /// <summary>Reads a project's inputs; <paramref name="dotnetRoot"/> is the folder of the <c>dotnet</c> that builds it,
    /// whose <c>packs/</c> hold the framework's reference assemblies.</summary>
    public static ProjectInputs Read(string projectPath, string? dotnetRoot)
    {
        string folder = Path.GetDirectoryName(projectPath)!;
        XDocument? project = Load(projectPath);
        string assemblyName = Property(project, "AssemblyName") ?? Path.GetFileNameWithoutExtension(projectPath);
        string? assets = FindAssets(projectPath);
        List<string> references = [];
        List<string> projectReferences = [];
        List<string> packages = [];
        string? framework = null;
        Assets? read = assets is null ? null : ReadAssets(assets);
        if (read is not null)
        {
            framework = read.Framework;
            references.AddRange(read.Compile);
            projectReferences.AddRange(read.Projects.Select(p => Path.GetFullPath(Path.Combine(folder, p))));
            packages.AddRange(read.Packages);
            foreach (string shared in read.FrameworkReferences)
            {
                references.AddRange(FrameworkAssemblies(dotnetRoot, read, shared));
            }
        }
        // The project file's own references, should the project not be restored yet.
        foreach (XElement reference in Elements(project, "ProjectReference"))
        {
            if (reference.Attribute("Include")?.Value is { Length: > 0 } include)
            {
                string full = Path.GetFullPath(Path.Combine(folder, include.Replace('\\', Path.DirectorySeparatorChar)));
                if (!projectReferences.Contains(full, StringComparer.Ordinal))
                {
                    projectReferences.Add(full);
                }
            }
        }
        foreach (XElement package in Elements(project, "PackageReference"))
        {
            if (package.Attribute("Include")?.Value is { Length: > 0 } id && !packages.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                packages.Add(id);
            }
        }
        // Two profiles of one framework (WPF's and Windows Forms') share its pack.
        return new ProjectInputs(projectPath, assemblyName, framework, [.. references.Distinct(StringComparer.Ordinal)], projectReferences, SourceFilesOf(folder),
            ImplicitUsingsOf(assets), assets, packages)
        {
            PackageVersions = read?.Versions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            PackageFolders = read?.Folders ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            FrameworkReferences = read?.FrameworkReferences ?? [],
        };
    }

    /// <summary>The files an item type of the project file includes (<c>AvaloniaResource</c>), its globs expanded and its
    /// removes taken away, as paths from the project's folder with forward slashes.</summary>
    public IReadOnlyList<string> Items(string itemType)
    {
        XDocument? project = Load(ProjectPath);
        HashSet<string> included = new(StringComparer.Ordinal);
        foreach (XElement item in Elements(project, itemType))
        {
            foreach (string include in Split(item.Attribute("Include")?.Value))
            {
                included.UnionWith(Glob(Folder, include));
            }
            foreach (string remove in Split(item.Attribute("Remove")?.Value))
            {
                included.ExceptWith(Glob(Folder, remove));
            }
        }
        return [.. included.Order(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> Split(string? value) =>
        value is null ? [] : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The project folder's files a glob names (<c>Assets/**</c>, <c>Assets/*.png</c>), bin and obj left out.</summary>
    private static IEnumerable<string> Glob(string folder, string pattern)
    {
        string normal = pattern.Replace('\\', '/');
        if (normal.Contains("$(", StringComparison.Ordinal))
        {
            yield break;
        }
        if (!normal.Contains('*', StringComparison.Ordinal))
        {
            if (File.Exists(Path.Combine(folder, normal)))
            {
                yield return normal;
            }
            yield break;
        }
        int star = normal.IndexOf('*', StringComparison.Ordinal);
        int slash = normal.LastIndexOf('/', star);
        string root = slash < 0 ? string.Empty : normal[..slash];
        string start = Path.Combine(folder, root);
        if (!Directory.Exists(start))
        {
            yield break;
        }
        Regex matcher = new("^" + Regex.Escape(normal).Replace(@"\*\*/", "(.*/)?", StringComparison.Ordinal).Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal).Replace(@"\?", "[^/]", StringComparison.Ordinal) + "$", RegexOptions.CultureInvariant);
        foreach (string file in Directory.EnumerateFiles(start, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (relative.StartsWith("bin/", StringComparison.Ordinal) || relative.StartsWith("obj/", StringComparison.Ordinal))
            {
                continue;
            }
            if (matcher.IsMatch(relative))
            {
                yield return relative;
            }
        }
    }

    private static XDocument? Load(string path)
    {
        try
        {
            return XDocument.Load(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static IEnumerable<XElement> Elements(XDocument? project, string name) =>
        project?.Descendants().Where(e => e.Name.LocalName == name) ?? [];

    private static string? Property(XDocument? project, string name) =>
        Elements(project, name).Select(e => e.Value.Trim()).LastOrDefault(v => v.Length > 0 && !v.Contains("$(", StringComparison.Ordinal));

    /// <summary>The project's <c>project.assets.json</c>: in its <c>obj/</c>, or under an artifacts folder above it
    /// (<c>UseArtifactsOutput</c>'s <c>artifacts/obj/&lt;project&gt;/</c>).</summary>
    public static string? FindAssets(string projectPath)
    {
        string folder = Path.GetDirectoryName(projectPath)!;
        string own = Path.Combine(folder, "obj", "project.assets.json");
        if (File.Exists(own))
        {
            return own;
        }
        string name = Path.GetFileNameWithoutExtension(projectPath);
        for (DirectoryInfo? at = new(folder); at is not null; at = at.Parent)
        {
            string artifacts = Path.Combine(at.FullName, "artifacts", "obj", name, "project.assets.json");
            if (File.Exists(artifacts))
            {
                return artifacts;
            }
        }
        return null;
    }

    private sealed record Assets(string Framework, List<string> Compile, List<string> Projects, List<string> FrameworkReferences, List<string> Packages,
        Dictionary<string, string> Versions, Dictionary<string, string> Folders)
    {
        /// <summary>The package folders the restore used, nearest first.</summary>
        public IReadOnlyList<string> PackageFolders { get; init; } = [];

        /// <summary>The packs the restore downloaded rather than found beside the SDK, by id and version
        /// (<c>Microsoft.WindowsDesktop.App.Ref</c> on a system without it).</summary>
        public IReadOnlyList<(string Id, string Version)> Downloads { get; init; } = [];
    }

    private static Assets? ReadAssets(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument json = JsonDocument.Parse(stream);
            JsonElement root = json.RootElement;
            if (!root.TryGetProperty("targets", out JsonElement targets) || !root.TryGetProperty("libraries", out JsonElement libraries))
            {
                return null;
            }
            // The first target with no runtime: what the project compiles against.
            JsonProperty target = targets.EnumerateObject().FirstOrDefault(t => !t.Name.Contains('/', StringComparison.Ordinal));
            if (target.Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            string framework = Shorten(target.Name);
            string[] packageFolders = root.TryGetProperty("packageFolders", out JsonElement folders) ? [.. folders.EnumerateObject().Select(f => f.Name)] : [];
            List<string> compile = [];
            List<string> projects = [];
            List<string> packages = [];
            Dictionary<string, string> versions = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> found = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty library in target.Value.EnumerateObject())
            {
                string type = library.Value.TryGetProperty("type", out JsonElement kind) ? kind.GetString() ?? string.Empty : string.Empty;
                if (!libraries.TryGetProperty(library.Name, out JsonElement described))
                {
                    continue;
                }
                string relative = described.TryGetProperty("path", out JsonElement at) ? at.GetString() ?? string.Empty : string.Empty;
                if (type == "project")
                {
                    if (described.TryGetProperty("msbuildProject", out JsonElement msbuild) && msbuild.GetString() is { } projectFile)
                    {
                        projects.Add(projectFile.Replace('\\', '/'));
                    }
                    continue;
                }
                string[] idAndVersion = library.Name.Split('/');
                packages.Add(idAndVersion[0]);
                if (idAndVersion.Length > 1)
                {
                    versions[idAndVersion[0]] = idAndVersion[1];
                }
                if (packageFolders.Select(f => Path.Combine(f, relative)).FirstOrDefault(Directory.Exists) is { } inCache)
                {
                    found[idAndVersion[0]] = inCache;
                }
                if (!library.Value.TryGetProperty("compile", out JsonElement assets))
                {
                    continue;
                }
                foreach (JsonProperty asset in assets.EnumerateObject())
                {
                    if (!asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    foreach (string packageFolder in packageFolders)
                    {
                        string full = Path.Combine(packageFolder, relative, asset.Name);
                        if (File.Exists(full))
                        {
                            compile.Add(full);
                            break;
                        }
                    }
                }
            }
            List<string> frameworkReferences = [];
            List<(string, string)> downloads = [];
            if (root.TryGetProperty("project", out JsonElement project) && project.TryGetProperty("frameworks", out JsonElement frameworks))
            {
                foreach (JsonProperty tfm in frameworks.EnumerateObject())
                {
                    if (tfm.Value.TryGetProperty("frameworkReferences", out JsonElement named))
                    {
                        frameworkReferences.AddRange(named.EnumerateObject().Select(f => f.Name));
                    }
                    if (tfm.Value.TryGetProperty("downloadDependencies", out JsonElement downloaded) && downloaded.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement dependency in downloaded.EnumerateArray())
                        {
                            if (dependency.TryGetProperty("name", out JsonElement id) && dependency.TryGetProperty("version", out JsonElement range)
                                && id.GetString() is { } name && range.GetString() is { } bounds)
                            {
                                downloads.Add((name, bounds.Trim('[', ']', '(', ')').Split(',')[0].Trim()));
                            }
                        }
                    }
                    break;
                }
            }
            return new Assets(framework, compile, projects, frameworkReferences, packages, versions, found) { PackageFolders = packageFolders, Downloads = downloads };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>A target's short name (<c>net10.0</c>) from the assets file's long one (<c>.NETCoreApp,Version=v10.0</c>),
    /// which older restores wrote.</summary>
    private static string Shorten(string target)
    {
        Match version = Regex.Match(target, @"^\.NETCoreApp,Version=v(\d+\.\d+)$");
        return version.Success ? $"net{version.Groups[1].Value}" : target;
    }

    /// <summary>A framework's reference assemblies: the pack the restore downloaded into a package folder, else the SDK's
    /// own (<c>packs/Microsoft.NETCore.App.Ref/10.0.x/ref/net10.0/</c>), the newest of the target's major version, else the
    /// newest of any. A profile shares its framework's pack (<c>Microsoft.WindowsDesktop.App.WPF</c>'s is
    /// <c>Microsoft.WindowsDesktop.App.Ref</c>), and a target's platform is not part of the pack's folder.</summary>
    private static IEnumerable<string> FrameworkAssemblies(string? dotnetRoot, Assets read, string framework)
    {
        string pack = (framework.StartsWith("Microsoft.WindowsDesktop.App", StringComparison.Ordinal) ? "Microsoft.WindowsDesktop.App" : framework) + ".Ref";
        string target = Regex.Match(read.Framework, @"^net\d+\.\d+").Value is { Length: > 0 } bare ? bare : read.Framework;
        foreach ((string id, string version) in read.Downloads.Where(d => string.Equals(d.Id, pack, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string folder in read.PackageFolders)
            {
                string reference = Path.Combine(folder, id.ToLowerInvariant(), version.ToLowerInvariant(), "ref", target);
                if (Directory.Exists(reference))
                {
                    return Directory.EnumerateFiles(reference, "*.dll");
                }
            }
        }
        string packs = dotnetRoot is null ? string.Empty : Path.Combine(dotnetRoot, "packs", pack);
        if (dotnetRoot is null || !Directory.Exists(packs))
        {
            return [];
        }
        string major = Regex.Match(target, @"^net(\d+)\.").Groups[1].Value;
        List<(Version Version, string Folder)> versions = [.. Directory.EnumerateDirectories(packs)
            .Select(d => (Parsed: Version.TryParse(Path.GetFileName(d).Split('-')[0], out Version? v) ? v : null, Folder: d))
            .Where(v => v.Parsed is not null)
            .Select(v => (v.Parsed!, v.Folder))
            .OrderByDescending(v => v.Item1)];
        foreach ((Version version, string folder) in versions.Where(v => v.Version.Major.ToString(System.Globalization.CultureInfo.InvariantCulture) == major).Concat(versions))
        {
            string reference = Path.Combine(folder, "ref", target);
            if (Directory.Exists(reference))
            {
                return Directory.EnumerateFiles(reference, "*.dll");
            }
            // A target newer than the pack: its newest framework's folder.
            string? newest = Directory.Exists(Path.Combine(folder, "ref")) ? Directory.EnumerateDirectories(Path.Combine(folder, "ref")).Order(StringComparer.Ordinal).LastOrDefault() : null;
            if (newest is not null)
            {
                return Directory.EnumerateFiles(newest, "*.dll");
            }
        }
        return [];
    }

    /// <summary>Whether a C# file is one of the project's own, as <see cref="SourceFiles"/> reads them, there now or not.</summary>
    public bool Compiles(string path)
    {
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !Paths.IsUnder(path, Folder))
        {
            return false;
        }
        string top = Folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (string? at = Path.GetDirectoryName(path); at is not null && at.Length > top.Length; at = Path.GetDirectoryName(at))
        {
            string name = Path.GetFileName(at);
            if (name is "bin" or "obj" or "node_modules" || name.StartsWith('.') || HasProject(at))
            {
                return false;
            }
        }
        return true;

        static bool HasProject(string folder)
        {
            try
            {
                return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.csproj").Any();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>The project's own C# files: under its folder, but for bin, obj, hidden folders and another project's.</summary>
    private static List<string> SourceFilesOf(string folder)
    {
        List<string> files = [];
        Walk(folder, top: true);
        return files;

        void Walk(string at, bool top)
        {
            try
            {
                if (!top && Directory.EnumerateFiles(at, "*.csproj").Any())
                {
                    return;
                }
                files.AddRange(Directory.EnumerateFiles(at, "*.cs"));
                foreach (string sub in Directory.EnumerateDirectories(at))
                {
                    string name = Path.GetFileName(sub);
                    if (name is "bin" or "obj" or "node_modules" || name.StartsWith('.'))
                    {
                        continue;
                    }
                    Walk(sub, top: false);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be read holds nothing the XAML can name.
            }
        }
    }

    /// <summary>The usings the SDK writes into the project's <c>GlobalUsings.g.cs</c> beside its assets file.</summary>
    private static List<string> ImplicitUsingsOf(string? assets)
    {
        List<string> usings = [];
        if (assets is null || Path.GetDirectoryName(assets) is not { } obj || !Directory.Exists(obj))
        {
            return usings;
        }
        string? generated = Directory.EnumerateFiles(obj, "*.GlobalUsings.g.cs", SearchOption.AllDirectories).FirstOrDefault();
        if (generated is null)
        {
            return usings;
        }
        foreach (string line in File.ReadLines(generated))
        {
            Match match = Regex.Match(line, @"^\s*global\s+using\s+(?:global::)?([\w.]+)\s*;");
            if (match.Success)
            {
                usings.Add(match.Groups[1].Value);
            }
        }
        return usings;
    }
}
