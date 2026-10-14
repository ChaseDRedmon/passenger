using Passenger.Xaml.Dialects;
using Passenger.Xaml.Metadata;
using Passenger.Xaml.Syntax;

namespace Passenger.Xaml.Projects;

/// <summary>Keeps one project's XAML context current: its assemblies read once, each C# file's types and each XAML file's
/// summary read again only when that file changes, and a new <see cref="XamlProject"/> made from them on demand. The
/// first load reads every file; after that a change costs its own file and a type system over what is kept. Safe on any
/// thread: every call takes the builder's lock, and the projects it hands out are immutable.</summary>
public sealed class XamlProjectBuilder
{
    private readonly object gate = new();
    private readonly string? dotnetRoot;
    private readonly Dictionary<string, SourceTypes> sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XamlFileSummary> files = new(StringComparer.Ordinal);
    private ProjectInputs inputs;
    private List<(ProjectInputs Inputs, List<string> Files)> referenced = [];
    private IReadOnlyList<AssemblyTypes> assemblies = [];
    private IReadOnlySet<string> themeKeys = new HashSet<string>();
    private IReadOnlyList<string> assets = [];
    private XamlProject? built;

    private XamlProjectBuilder(ProjectInputs inputs, XamlDialect dialect, string? dotnetRoot)
    {
        this.inputs = inputs;
        Dialect = dialect;
        this.dotnetRoot = dotnetRoot;
    }

    public XamlDialect Dialect { get; }

    public string ProjectPath => inputs.ProjectPath;

    public string Folder => inputs.Folder;

    /// <summary>A builder for a project whose XAML Passenger reads (an Avalonia or WPF project), or null for another.</summary>
    public static XamlProjectBuilder? For(string projectPath, string? dotnetRoot)
    {
        ProjectInputs inputs = ProjectInputs.Read(projectPath, dotnetRoot);
        return DialectOf(inputs) is { } dialect ? new XamlProjectBuilder(inputs, dialect, dotnetRoot) : null;
    }

    /// <summary>The dialect a project's XAML is in, by the framework it references: Avalonia's for a project that references
    /// its package, WPF's for one whose restore references WPF's shared framework (<c>UseWPF</c>, which the project file
    /// says before a restore); null for any other (MAUI, WinUI), whose XAML Passenger does not read.</summary>
    public static XamlDialect? DialectOf(ProjectInputs inputs)
    {
        if (inputs.Uses("Avalonia"))
        {
            return AvaloniaDialect.Instance;
        }
        bool wpf = inputs.FrameworkReferences.Any(f => f is "Microsoft.WindowsDesktop.App.WPF" or "Microsoft.WindowsDesktop.App")
            || string.Equals(inputs.PropertyValue("UseWPF"), "true", StringComparison.OrdinalIgnoreCase);
        return wpf ? WpfDialect.Instance : null;
    }

    /// <summary>Whether a file is one of the project's XAML files.</summary>
    public bool Owns(string path) =>
        Dialect.Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()) && Passenger.Xaml.Projects.Paths.IsUnder(path, Folder) && !InBuildOutput(path);

    /// <summary>Reads everything: the assemblies, every C# and XAML file (an open file's text from <paramref name="open"/>
    /// rather than disk), the assets.</summary>
    public XamlProject Load(Func<string, string?>? open = null)
    {
        lock (gate)
        {
            inputs = ProjectInputs.Read(inputs.ProjectPath, dotnetRoot);
            assemblies = [.. inputs.References.AsParallel().AsOrdered().Select(AssemblyTypes.Get).OfType<AssemblyTypes>()];
            themeKeys = ThemeKeys(assemblies);
            referenced = [];
            CollectReferences(inputs, new HashSet<string>(StringComparer.Ordinal) { inputs.ProjectPath });
            sources.Clear();
            List<(string Path, string Assembly)> cs = [.. inputs.SourceFiles.Select(f => (f, inputs.AssemblyName))];
            cs.AddRange(referenced.SelectMany(r => r.Inputs.SourceFiles.Select(f => (f, r.Inputs.AssemblyName))));
            foreach (SourceTypes read in cs.AsParallel().Select(f => SourceTypes.Read(f.Path, open?.Invoke(f.Path) ?? ReadText(f.Path), f.Assembly)))
            {
                sources[read.Path] = read;
            }
            files.Clear();
            assets = [.. Dialect.AssetItems.SelectMany(inputs.Items).Distinct(StringComparer.Ordinal)];
            List<string> xaml = [.. XamlFilesUnder(inputs.Folder)];
            XamlProject shell = Make();
            foreach (string path in xaml)
            {
                string text = open?.Invoke(path) ?? ReadText(path);
                files[path] = XamlFileSummary.Of(path, XmlTree.Parse(text), shell.ResolveSource);
            }
            return built = Make();
        }
    }

    /// <summary>The project as last made, made now if nothing has been.</summary>
    public XamlProject Current
    {
        get
        {
            lock (gate)
            {
                return built ??= Make();
            }
        }
    }

    /// <summary>A C# file's text changed (typed, or written on disk): its types are read again; a file deleted goes.</summary>
    public XamlProject SourceChanged(string path, string? text)
    {
        lock (gate)
        {
            string? assembly = AssemblyOf(path);
            if (assembly is null)
            {
                return Current;
            }
            if (text is null && !File.Exists(path))
            {
                sources.Remove(path);
            }
            else
            {
                sources[path] = SourceTypes.Read(path, text ?? ReadText(path), assembly);
            }
            return built = Make();
        }
    }

    /// <summary>A XAML file's text changed: its summary is made again; a file deleted goes.</summary>
    public XamlProject XamlChanged(string path, string? text, XmlTree? tree = null)
    {
        lock (gate)
        {
            if (text is null && tree is null && !File.Exists(path))
            {
                files.Remove(path);
            }
            else
            {
                XamlProject current = built ?? Make();
                files[path] = XamlFileSummary.Of(path, tree ?? XmlTree.Parse(text ?? ReadText(path)), current.ResolveSource);
            }
            return built = new XamlProject(inputs, built?.Types ?? Make().Types, Dialect, new Dictionary<string, XamlFileSummary>(files, StringComparer.Ordinal),
                assets, themeKeys, [.. referenced.Select(r => (r.Inputs.AssemblyName, r.Inputs.Folder))]);
        }
    }

    /// <summary>An open XAML file's text as it is typed: its summary taken when it says something new (a resource, an
    /// include, a name, a class), so the project's other files see it; null when it says nothing new.</summary>
    public XamlProject? XamlTyped(string path, XmlTree tree)
    {
        lock (gate)
        {
            XamlProject current = built ?? Make();
            XamlFileSummary summary = XamlFileSummary.Of(path, tree, current.ResolveSource);
            if (files.TryGetValue(path, out XamlFileSummary? known) && known.Signature == summary.Signature)
            {
                return null;
            }
            files[path] = summary;
            return built = new XamlProject(inputs, current.Types, Dialect, new Dictionary<string, XamlFileSummary>(files, StringComparer.Ordinal),
                assets, themeKeys, [.. referenced.Select(r => (r.Inputs.AssemblyName, r.Inputs.Folder))]);
        }
    }

    /// <summary>Which project's assembly a C# file is compiled into, of this one and those it references; null for none.</summary>
    private string? AssemblyOf(string path)
    {
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || InBuildOutput(path))
        {
            return null;
        }
        if (Paths.IsUnder(path, inputs.Folder) && !referenced.Any(r => Paths.IsUnder(path, r.Inputs.Folder) && r.Inputs.Folder.Length > inputs.Folder.Length))
        {
            return inputs.AssemblyName;
        }
        return referenced.FirstOrDefault(r => Paths.IsUnder(path, r.Inputs.Folder)).Inputs?.AssemblyName;
    }

    private XamlProject Make()
    {
        TypeSystem types = new(assemblies, [.. sources.Values], inputs.AssemblyName, inputs.ImplicitUsings);
        return new XamlProject(inputs, types, Dialect, new Dictionary<string, XamlFileSummary>(files, StringComparer.Ordinal), assets, themeKeys,
            [.. referenced.Select(r => (r.Inputs.AssemblyName, r.Inputs.Folder))]);
    }

    private void CollectReferences(ProjectInputs from, HashSet<string> seen)
    {
        foreach (string project in from.ProjectReferences)
        {
            if (!seen.Add(project) || !File.Exists(project))
            {
                continue;
            }
            ProjectInputs read = ProjectInputs.Read(project, dotnetRoot);
            referenced.Add((read, [.. XamlFilesUnder(read.Folder)]));
            CollectReferences(read, seen);
        }
    }

    /// <summary>The keys a theme's compiled XAML holds: its string constants that read as resource keys.</summary>
    private static IReadOnlySet<string> ThemeKeys(IReadOnlyList<AssemblyTypes> assemblies)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (AssemblyTypes assembly in assemblies)
        {
            if (!assembly.Name.Contains(".Themes.", StringComparison.Ordinal))
            {
                continue;
            }
            foreach (string value in assembly.Strings)
            {
                if (value.Length >= 3 && char.IsUpper(value[0]) && value.All(c => char.IsLetterOrDigit(c) || c is '.' or '_'))
                {
                    keys.Add(value);
                }
            }
        }
        return keys;
    }

    private IEnumerable<string> XamlFilesUnder(string folder)
    {
        Stack<string> waiting = new([folder]);
        while (waiting.Count > 0)
        {
            string at = waiting.Pop();
            IEnumerable<string> found;
            try
            {
                found = Directory.EnumerateFileSystemEntries(at).ToList();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string entry in found)
            {
                string name = Path.GetFileName(entry);
                if (Directory.Exists(entry))
                {
                    if (name is not ("bin" or "obj" or "node_modules") && !name.StartsWith('.') && (entry == folder || !Directory.EnumerateFiles(entry, "*.csproj").Any()))
                    {
                        waiting.Push(entry);
                    }
                }
                else if (Dialect.Extensions.Contains(Path.GetExtension(name).ToLowerInvariant()))
                {
                    yield return entry;
                }
            }
        }
    }

    private bool InBuildOutput(string path)
    {
        string relative = Path.GetRelativePath(inputs.Folder, path).Replace('\\', '/');
        return relative.StartsWith("bin/", StringComparison.Ordinal) || relative.StartsWith("obj/", StringComparison.Ordinal);
    }

    private static string ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}

/// <summary>Path comparisons as the file system makes them.</summary>
public static class Paths
{
    public static StringComparison Comparison { get; } = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>Whether a path is inside a folder (or is it).</summary>
    public static bool IsUnder(string path, string folder)
    {
        string trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.Equals(trimmed, Comparison) || path.StartsWith(trimmed + Path.DirectorySeparatorChar, Comparison)
            || path.StartsWith(trimmed + Path.AltDirectorySeparatorChar, Comparison);
    }
}
