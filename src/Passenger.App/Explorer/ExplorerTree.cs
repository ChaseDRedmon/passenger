using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Passenger.App.Workspace;

namespace Passenger.App.Explorer;

/// <summary>What a row of the Explorer stands for.</summary>
public enum NodeKind
{
    Folder,
    File,
    Solution,
    SolutionFolder,
    Project,
    Dependencies,
    Framework,
    Package,
    ProjectReference,
}

/// <summary>The Explorer's tree, in either of Rider's two views: the Solution (folders and projects,
/// each project's dependencies and its files) or the File System. Flattened into the rows currently
/// visible, so a virtualizing list draws only what is on screen; a node's children are made the first
/// time it opens.</summary>
public sealed class ExplorerTree
{
    /// <summary>Never listed: version control's own store, which nobody edits by hand.</summary>
    private static readonly HashSet<string> Hidden = [".git", ".DS_Store"];

    /// <summary>Listed but dimmed, as Rider marks excluded folders: build output and dependencies.</summary>
    private static readonly HashSet<string> Excluded = ["bin", "obj", "artifacts", "node_modules", ".vs", ".idea"];

    private IReadOnlyDictionary<string, Vcs.ChangeKind> changes = new Dictionary<string, Vcs.ChangeKind>();

    public ObservableCollection<ExplorerNode> Rows { get; } = [];

    /// <summary>Colours the files version control says changed, as Rider's Project view does.</summary>
    public void SetChanges(IReadOnlyDictionary<string, Vcs.ChangeKind> changed)
    {
        changes = changed;
        foreach (ExplorerNode row in Rows)
        {
            Mark(row);
        }
    }

    private void Mark(ExplorerNode node) =>
        node.Change = node.IsFile && node.Path is { } path && changes.TryGetValue(path, out Vcs.ChangeKind kind) ? kind : null;

    public ExplorerNode? Root { get; private set; }

    public bool IsSolutionView { get; private set; }

    public void LoadFileSystem(string folder)
    {
        IsSolutionView = false;
        Show(Folder(Path.GetFullPath(folder), 0, dimmed: false));
    }

    public void LoadSolution(SolutionModel solution)
    {
        IsSolutionView = true;
        int count = solution.Projects.Count();
        ExplorerNode root = new(NodeKind.Solution, solution.Name, solution.Path, 0, false,
            node => solution.Entries.Select(e => Entry(e, node.Depth + 1)).ToList(),
            $"{count} project{(count == 1 ? "" : "s")}");
        Show(root);
    }

    public void Toggle(ExplorerNode node)
    {
        if (node.IsExpanded)
        {
            Collapse(node);
        }
        else
        {
            Expand(node);
        }
    }

    public void Expand(ExplorerNode node)
    {
        if (!node.HasChildren || node.IsExpanded)
        {
            return;
        }
        node.IsExpanded = true;
        int at = Rows.IndexOf(node) + 1;
        foreach (ExplorerNode child in Visible(node))
        {
            Mark(child);
            Rows.Insert(at++, child);
        }
    }

    public void Collapse(ExplorerNode node)
    {
        if (!node.IsExpanded)
        {
            return;
        }
        int at = Rows.IndexOf(node) + 1;
        while (at < Rows.Count && Rows[at].Depth > node.Depth)
        {
            Rows.RemoveAt(at);
        }
        node.IsExpanded = false;
    }

    /// <summary>Opens every node down to <paramref name="path"/> and returns its row, so a file can be
    /// shown where it lives: in its folder, or in the solution, in its project.</summary>
    public ExplorerNode? Reveal(string path)
    {
        if (Root is null)
        {
            return null;
        }
        string full = Path.GetFullPath(path);
        ExplorerNode node = Root;
        while (node.Path != full)
        {
            Expand(node);
            ExplorerNode? next = node.Children?
                .Where(c => c.Kind is not (NodeKind.Dependencies or NodeKind.Framework or NodeKind.Package or NodeKind.ProjectReference))
                .FirstOrDefault(c => c.Path == full || c.Contains(full));
            if (next is null)
            {
                return null;
            }
            node = next;
        }
        return node;
    }

    private void Show(ExplorerNode root)
    {
        Rows.Clear();
        Root = root;
        Rows.Add(root);
        Expand(root);
    }

    /// <summary>The projects unloaded, by path: shown dimmed, with nothing under them.</summary>
    public ISet<string> Unloaded { get; } = new HashSet<string>(StringComparer.Ordinal);

    private ExplorerNode Entry(SolutionEntry entry, int depth) => entry switch
    {
        SolutionFolder folder => new ExplorerNode(NodeKind.SolutionFolder, folder.Name, null, depth, false,
            node => folder.Children.Select(c => Entry(c, node.Depth + 1)).ToList()),
        ProjectModel project => Project(project, depth),
        _ => throw new InvalidOperationException($"Unknown solution entry {entry}"),
    };

    private ExplorerNode Project(ProjectModel project, int depth) => Unloaded.Contains(project.Path)
        ? new(NodeKind.Project, project.Name, project.Path, depth, dimmed: true, null, "(unloaded)")
        : new(NodeKind.Project, project.Name, project.Path, depth, !project.Exists, node =>
        {
            List<ExplorerNode> children = [Dependencies(project, node.Depth + 1)];
            children.AddRange(Contents(project.Folder, node.Depth + 1, dimmed: false, skip: project.Path));
            return children;
        }, project.TargetFrameworks.Count > 0 ? string.Join(", ", project.TargetFrameworks) : null);

    private static ExplorerNode Dependencies(ProjectModel project, int depth) =>
        new(NodeKind.Dependencies, "Dependencies", null, depth, false, node =>
        {
            int below = node.Depth + 1;
            List<ExplorerNode> children = [];
            children.AddRange(project.TargetFrameworks.Select(f => Leaf(NodeKind.Framework, f, null, below)));
            children.AddRange(project.ProjectReferences.Select(r => Leaf(NodeKind.ProjectReference, Path.GetFileNameWithoutExtension(r), null, below)));
            children.AddRange(project.Packages.Select(p => Leaf(NodeKind.Package, p.Name, p.Version, below)));
            return children;
        });

    private static ExplorerNode Leaf(NodeKind kind, string name, string? detail, int depth) => new(kind, name, null, depth, false, null, detail);

    private static ExplorerNode Folder(string path, int depth, bool dimmed) =>
        new(NodeKind.Folder, Path.GetFileName(path) is { Length: > 0 } name ? name : path, path, depth, dimmed,
            node => Contents(path, node.Depth + 1, node.IsDimmed, skip: null));

    /// <summary>A folder's entries, folders first; a project hides its own build output and file.</summary>
    private static List<ExplorerNode> Contents(string folder, int depth, bool dimmed, string? skip)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFileSystemInfos()
                .Where(e => !Hidden.Contains(e.Name))
                .Where(e => skip is null || (e.FullName != skip && !(e is DirectoryInfo && e.Name is "bin" or "obj")))
                .OrderBy(e => e is FileInfo)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => e is DirectoryInfo
                    ? Folder(e.FullName, depth, dimmed || Excluded.Contains(e.Name))
                    : new ExplorerNode(NodeKind.File, e.Name, e.FullName, depth, dimmed, null))
                .ToList();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    /// <summary>A node's descendants as rows, in order, through every node already open.</summary>
    private static IEnumerable<ExplorerNode> Visible(ExplorerNode node)
    {
        foreach (ExplorerNode child in node.Children ?? [])
        {
            yield return child;
            if (child.IsExpanded)
            {
                foreach (ExplorerNode below in Visible(child))
                {
                    yield return below;
                }
            }
        }
    }
}

/// <summary>One row of the Explorer: a folder or file, or a part of the solution.</summary>
public sealed class ExplorerNode : Observable
{
    private readonly Func<ExplorerNode, List<ExplorerNode>>? load;
    private List<ExplorerNode>? children;
    private bool isExpanded;

    public ExplorerNode(NodeKind kind, string name, string? path, int depth, bool dimmed, Func<ExplorerNode, List<ExplorerNode>>? load, string? detail = null)
    {
        Kind = kind;
        Name = name;
        Path = path;
        Depth = depth;
        IsDimmed = dimmed;
        Detail = detail;
        this.load = load;
    }

    public NodeKind Kind { get; }
    public string Name { get; }
    public string? Path { get; }
    public int Depth { get; }
    public bool IsDimmed { get; }

    /// <summary>A quieter word beside the name: a package's version, a project's frameworks.</summary>
    public string? Detail { get; }

    public bool HasChildren => load is not null;
    public bool IsDirectory => Kind == NodeKind.Folder;
    public bool IsFile => Kind == NodeKind.File;
    public List<ExplorerNode>? Children => load is null ? null : children ??= load(this);

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (Set(ref isExpanded, value))
            {
                Raise(nameof(Icon));
                Raise(nameof(ChevronAngle));
            }
        }
    }

    /// <summary>How version control says the file changed, or null when it did not.</summary>
    public Vcs.ChangeKind? Change
    {
        get => change;
        set
        {
            if (Set(ref change, value))
            {
                Raise(nameof(IsVcsModified));
                Raise(nameof(IsVcsAdded));
                Raise(nameof(IsVcsUnversioned));
                Raise(nameof(IsVcsConflicted));
            }
        }
    }

    private Vcs.ChangeKind? change;

    public bool IsVcsModified => change is Vcs.ChangeKind.Modified or Vcs.ChangeKind.Renamed;
    public bool IsVcsAdded => change is Vcs.ChangeKind.Added;
    public bool IsVcsUnversioned => change is Vcs.ChangeKind.Unversioned;
    public bool IsVcsConflicted => change is Vcs.ChangeKind.Conflicted;

    public Thickness Indent => new(Depth * 16, 0, 0, 0);
    public double ChevronAngle => IsExpanded ? 90 : 0;
    public double Opacity => IsDimmed ? 0.55 : 1;
    public Geometry Icon => FileIcons.For(this);
    public IBrush IconBrush => FileIcons.BrushFor(this);

    /// <summary>Whether a path lies under this node: inside its folder, or its project's folder.</summary>
    public bool Contains(string path)
    {
        string? folder = Kind switch
        {
            NodeKind.Folder => Path,
            NodeKind.Project => System.IO.Path.GetDirectoryName(Path),
            _ => null,
        };
        if (folder is null)
        {
            return Kind == NodeKind.SolutionFolder && (Children?.Any(c => c.Path == path || c.Contains(path)) ?? false);
        }
        return path.StartsWith(folder + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
