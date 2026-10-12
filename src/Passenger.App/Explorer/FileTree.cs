using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;

namespace Passenger.App.Explorer;

/// <summary>A folder's files as the Explorer shows them: a tree flattened into the rows currently
/// visible, so a virtualizing list draws only what is on screen. A folder's children are read the
/// first time it opens.</summary>
public sealed class FileTree
{
    /// <summary>Never listed: version control's own store, which nobody edits by hand.</summary>
    private static readonly HashSet<string> Hidden = [".git", ".DS_Store"];

    /// <summary>Listed but dimmed, as Rider marks excluded folders: build output and dependencies.</summary>
    private static readonly HashSet<string> Excluded = ["bin", "obj", "artifacts", "node_modules", ".vs", ".idea"];

    public ObservableCollection<FileNode> Rows { get; } = [];

    public FileNode? Root { get; private set; }

    public void Load(string folder)
    {
        Rows.Clear();
        Root = new FileNode(Path.GetFullPath(folder), isDirectory: true, depth: 0, dimmed: false);
        Rows.Add(Root);
        Expand(Root);
    }

    public void Toggle(FileNode node)
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

    public void Expand(FileNode node)
    {
        if (!node.IsDirectory || node.IsExpanded)
        {
            return;
        }
        node.Children ??= ReadChildren(node);
        node.IsExpanded = true;
        int at = Rows.IndexOf(node) + 1;
        foreach (FileNode child in Visible(node))
        {
            Rows.Insert(at++, child);
        }
    }

    public void Collapse(FileNode node)
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

    /// <summary>Opens every folder down to <paramref name="path"/> and returns its row, so a file can
    /// be shown where it lives.</summary>
    public FileNode? Reveal(string path)
    {
        if (Root is null)
        {
            return null;
        }
        string full = Path.GetFullPath(path);
        FileNode node = Root;
        while (!string.Equals(node.Path, full, StringComparison.Ordinal))
        {
            Expand(node);
            FileNode? next = node.Children?.FirstOrDefault(c =>
                full.StartsWith(c.Path + Path.DirectorySeparatorChar, StringComparison.Ordinal) || c.Path == full);
            if (next is null)
            {
                return null;
            }
            node = next;
        }
        return node;
    }

    private static List<FileNode> ReadChildren(FileNode parent)
    {
        try
        {
            DirectoryInfo directory = new(parent.Path);
            return directory.EnumerateFileSystemInfos()
                .Where(e => !Hidden.Contains(e.Name))
                .OrderBy(e => e is FileInfo)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new FileNode(e.FullName, e is DirectoryInfo, parent.Depth + 1, parent.IsDimmed || Excluded.Contains(e.Name)))
                .ToList();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    /// <summary>A node's descendants as rows, in order, through every folder already open.</summary>
    private static IEnumerable<FileNode> Visible(FileNode node)
    {
        foreach (FileNode child in node.Children ?? [])
        {
            yield return child;
            if (child.IsExpanded)
            {
                foreach (FileNode below in Visible(child))
                {
                    yield return below;
                }
            }
        }
    }
}

/// <summary>One row of the Explorer.</summary>
public sealed class FileNode(string path, bool isDirectory, int depth, bool dimmed) : Observable
{
    private bool isExpanded;

    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileName(path) is { Length: > 0 } name ? name : path;
    public bool IsDirectory { get; } = isDirectory;
    public int Depth { get; } = depth;
    public bool IsDimmed { get; } = dimmed;
    public List<FileNode>? Children { get; set; }

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

    public Thickness Indent => new(Depth * 16, 0, 0, 0);
    public double ChevronAngle => IsExpanded ? 90 : 0;
    public double Opacity => IsDimmed ? 0.55 : 1;
    public Geometry Icon => FileIcons.For(this);
    public IBrush IconBrush => FileIcons.BrushFor(this);
}
