using System.Collections.Concurrent;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Evaluation.Context;
using Microsoft.Build.Exceptions;

namespace Passenger.Host;

/// <summary>MSBuild's own evaluation of projects (conditions, imports, wildcards, Directory.Build.props and central
/// package management all as a build sees them), many at once on one shared context, as the static graph evaluates a
/// solution. Every project's outer evaluation comes first, in the order asked, so each shows as soon as it can; a
/// multi-targeted project's frameworks, which cost it as much again each and change little of what it shows, follow once
/// every outer one is in.</summary>
internal static class Evaluation
{
    /// <summary>As Roslyn's build host loads a project: a missing or broken import is no failure, an unknown SDK is.</summary>
    internal const ProjectLoadSettings Settings = ProjectLoadSettings.IgnoreMissingImports | ProjectLoadSettings.IgnoreEmptyImports
        | ProjectLoadSettings.IgnoreInvalidImports | ProjectLoadSettings.RejectCircularImports
        | ProjectLoadSettings.DoNotEvaluateElementsWithFalseCondition | ProjectLoadSettings.FailOnUnresolvedSdk;

    /// <summary>The item types that are a project's files, as Visual Studio's and Rider's trees show them, beside the ones
    /// a project's own targets add (AvailableItemName, the item types an IDE offers as a file's build action).</summary>
    private static readonly string[] FileItemTypes =
        ["Compile", "None", "Content", "EmbeddedResource", "AdditionalFiles", "Page", "Resource", "ApplicationDefinition", "Folder"];

    /// <summary>The most folders stamped for one project, so a wildcard over a folder of thousands is not walked at length.</summary>
    private const int FolderLimit = 20_000;

    private static StringComparer PathComparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>Evaluates the projects asked, handing each over as it is done; cancelled, it starts no other project.</summary>
    public static void Run(EvaluateParams asked, Action<EvaluatedProject> evaluated, CancellationToken cancel)
    {
        using ProjectCollection collection = new(asked.Properties.ToDictionary(p => p.Key, p => p.Value), loggers: null, ToolsetDefinitionLocations.Default);
        EvaluationContext context = EvaluationContext.Create(EvaluationContext.SharingPolicy.Shared);
        ParallelOptions parallel = new() { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancel };
        ConcurrentDictionary<string, (EvaluatedProject Outer, IReadOnlyList<string> Frameworks)> targeted = new(PathComparer);
        // Load-balanced, so each worker takes the next project in the order asked: the active file's first.
        Parallel.ForEach(Partitioner.Create(asked.Projects.ToArray(), loadBalance: true), parallel, path =>
        {
            (EvaluatedProject outer, IReadOnlyList<string> frameworks) = Outer(path, collection, context, asked.Properties);
            evaluated(outer);
            if (!outer.Complete)
            {
                targeted[path] = (outer, frameworks);
            }
        });
        List<(EvaluatedProject Outer, IReadOnlyList<string> Frameworks)> inOrder =
            [.. asked.Projects.Distinct(PathComparer).Where(targeted.ContainsKey).Select(p => targeted[p])];
        Parallel.ForEach(Partitioner.Create(inOrder.ToArray(), loadBalance: true), parallel,
            t => evaluated(Frameworks(t.Outer, t.Frameworks, collection, context, asked.Properties)));
    }

    /// <summary>A project's outer evaluation: all of it for a project with one framework (or none), and for a
    /// multi-targeted one what it shows until its frameworks are in.</summary>
    private static (EvaluatedProject Project, IReadOnlyList<string> Frameworks) Outer(string path, ProjectCollection collection, EvaluationContext context, IReadOnlyDictionary<string, string> properties)
    {
        // Stamped before they are read: whatever changes once evaluation has begun changes the stamps too.
        Stamp file = Stamps.File(path);
        IReadOnlyList<Stamp> folders = Walked([Path.GetDirectoryName(path)!]);
        if (file.Ticks == 0)
        {
            return (new EvaluatedProject { Path = path, Problem = $"{path} does not exist", Inputs = [file] }, []);
        }
        Project? outer = null;
        try
        {
            outer = Load(path, collection, context, properties, null);
            string single = outer.GetPropertyValue("TargetFramework");
            List<string> frameworks = single.Length > 0
                ? [single]
                : [.. outer.GetPropertyValue("TargetFrameworks").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase)];
            bool multi = single.Length == 0 && frameworks.Count > 0;
            return (Read(path, [outer], frameworks, [null], file, folders) with { Complete = !multi }, frameworks);
        }
        catch (Exception error) when (error is InvalidProjectFileException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return (new EvaluatedProject { Path = path, Problem = error.Message, Inputs = [file], Folders = folders }, []);
        }
        finally
        {
            if (outer is not null)
            {
                collection.UnloadProject(outer);
            }
        }
    }

    /// <summary>A multi-targeted project complete: each framework's evaluation, with what its outer one depended on.</summary>
    private static EvaluatedProject Frameworks(EvaluatedProject outer, IReadOnlyList<string> frameworks, ProjectCollection collection, EvaluationContext context, IReadOnlyDictionary<string, string> properties)
    {
        List<Project> loaded = [];
        try
        {
            foreach (string framework in frameworks)
            {
                loaded.Add(Load(outer.Path, collection, context, properties, framework));
            }
            EvaluatedProject inner = Read(outer.Path, loaded, frameworks, [.. frameworks], outer.Inputs[0], []);
            return inner with
            {
                Inputs = [.. outer.Inputs.Concat(inner.Inputs).DistinctBy(s => s.Path, PathComparer)],
                Folders = [.. outer.Folders.Concat(inner.Folders).DistinctBy(s => s.Path, PathComparer)],
            };
        }
        catch (Exception error) when (error is InvalidProjectFileException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return outer with { Complete = true, Problem = error.Message };
        }
        finally
        {
            foreach (Project project in loaded)
            {
                collection.UnloadProject(project);
            }
        }
    }

    /// <summary>What the Solution view shows of evaluations of one project (its outer one, or each framework's), and its
    /// inputs: the project file stamped as it was read, the imports outside the SDK and package folders, and the folders
    /// its files come from.</summary>
    private static EvaluatedProject Read(string path, IReadOnlyList<Project> each, IReadOnlyList<string> frameworks, IReadOnlyList<string?> named, Stamp file, IReadOnlyList<Stamp> folders)
    {
        Project first = each[0];
        HashSet<string> excluded = OutsideRoots(first);
        List<EvaluatedItem> items = Items(each);
        string own = Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar;
        return new EvaluatedProject
        {
            Path = path,
            AssemblyName = NonEmpty(first.GetPropertyValue("AssemblyName")),
            OutputType = NonEmpty(first.GetPropertyValue("OutputType")),
            TargetFrameworks = frameworks,
            IsTestProject = each.Any(p => p.GetPropertyValue("IsTestProject").Equals("true", StringComparison.OrdinalIgnoreCase)),
            Dependencies = [.. each.Select((p, i) => Dependencies(p, named[i]))],
            Items = items,
            Inputs = [file, .. each
                .SelectMany(p => p.Imports.Select(i => i.ImportedProject.FullPath))
                .Distinct(PathComparer)
                .Where(i => !excluded.Any(root => i.StartsWith(root, PathComparison)))
                .Select(Stamps.File)],
            // A file the project takes from elsewhere (../Shared/*.cs) has its own folder stamped, not all under it: a file
            // at the repository's top (a README packed with every project) would have the whole repository walked.
            Folders = [.. folders, .. items
                .Where(i => !i.Path.StartsWith(own, PathComparison))
                .Select(i => Path.GetDirectoryName(i.Path)!)
                .Distinct(PathComparer)
                .Where(d => !folders.Any(f => PathComparer.Equals(f.Path, d)) && Directory.Exists(d))
                .Select(Stamps.Folder)],
        };
    }

    private static Project Load(string path, ProjectCollection collection, EvaluationContext context, IReadOnlyDictionary<string, string> properties, string? framework)
    {
        Dictionary<string, string> global = new(properties, StringComparer.OrdinalIgnoreCase);
        if (framework is not null)
        {
            global["TargetFramework"] = framework;
        }
        return WindowsTargeting.Load(given => Project.FromFile(path, new ProjectOptions
        {
            GlobalProperties = given,
            ProjectCollection = collection,
            EvaluationContext = context,
            LoadSettings = Settings,
        }), global, collection);
    }

    private static string? NonEmpty(string value) => value.Length > 0 ? value : null;

    /// <summary>A framework's packages, each with the version restore takes (its own, its VersionOverride, else under
    /// central package management its PackageVersion's), and the projects it references.</summary>
    private static FrameworkDependencies Dependencies(Project project, string? framework)
    {
        Dictionary<string, string> central = new(StringComparer.OrdinalIgnoreCase);
        if (project.GetPropertyValue("ManagePackageVersionsCentrally").Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            foreach (ProjectItem version in project.GetItems("PackageVersion"))
            {
                central[version.EvaluatedInclude] = version.GetMetadataValue("Version");
            }
        }
        List<PackageDependency> packages = [.. project.GetItems("PackageReference")
            .Select(p => new PackageDependency(
                p.EvaluatedInclude,
                NonEmpty(p.GetMetadataValue("VersionOverride")) ?? NonEmpty(p.GetMetadataValue("Version")) ?? central.GetValueOrDefault(p.EvaluatedInclude),
                p.GetMetadataValue("IsImplicitlyDefined").Equals("true", StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
        List<string> projects = [.. project.GetItems("ProjectReference").Select(r => r.GetMetadataValue("FullPath")).Distinct(PathComparer)];
        return new FrameworkDependencies(framework, packages, projects)
        {
            FrameworkReferences = [.. project.GetItems("FrameworkReference").Select(r => r.EvaluatedInclude).Distinct(StringComparer.OrdinalIgnoreCase)],
            WindowsTargeting = WindowsTargeting.Given(project),
        };
    }

    /// <summary>The project's files, every evaluation's, each once: of the file item types and those the project names as
    /// such, but those it hides (Visible false).</summary>
    private static List<EvaluatedItem> Items(IReadOnlyList<Project> evaluations)
    {
        List<EvaluatedItem> items = [];
        HashSet<(string, string)> seen = [];
        foreach (Project project in evaluations)
        {
            IEnumerable<string> types = FileItemTypes.Concat(project.GetItems("AvailableItemName").Select(i => i.EvaluatedInclude)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string type in types)
            {
                foreach (ProjectItem item in project.GetItems(type))
                {
                    if (item.GetMetadataValue("Visible").Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string full = item.GetMetadataValue("FullPath");
                    if (full.Length == 0 || !seen.Add((type, full)))
                    {
                        continue;
                    }
                    items.Add(new EvaluatedItem(type, full, NonEmpty(item.GetMetadataValue("Link")), NonEmpty(item.GetMetadataValue("DependentUpon"))));
                }
            }
        }
        return items;
    }

    /// <summary>Where imports never change but with their versions, which the imports kept say: the .NET install (the
    /// SDK, its props and targets) and the NuGet package folders.</summary>
    internal static HashSet<string> OutsideRoots(Project project)
    {
        HashSet<string> roots = new(PathComparer);
        foreach (string property in new[] { "NetCoreRoot", "NetCoreTargetingPackRoot", "MSBuildBinPath", "MSBuildExtensionsPath", "NuGetPackageRoot" })
        {
            Add(project.GetPropertyValue(property));
        }
        foreach (string folder in project.GetPropertyValue("NuGetPackageFolders").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Add(folder);
        }
        return roots;

        void Add(string folder)
        {
            if (folder.Length > 0 && Path.IsPathFullyQualified(folder))
            {
                roots.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar);
            }
        }
    }

    /// <summary>Every folder under these that a wildcard would enter, stamped.</summary>
    internal static IReadOnlyList<Stamp> Walked(IEnumerable<string> roots)
    {
        List<Stamp> stamps = [];
        Queue<string> walking = new(roots.Distinct(PathComparer));
        while (walking.Count > 0 && stamps.Count < FolderLimit)
        {
            string folder = walking.Dequeue();
            stamps.Add(Stamps.Folder(folder));
            try
            {
                foreach (string child in Directory.EnumerateDirectories(folder))
                {
                    if (Stamps.Counts(Path.GetFileName(child), folder: true))
                    {
                        walking.Enqueue(child);
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Gone or shut, as a wildcard would find it.
            }
        }
        return stamps;
    }
}
