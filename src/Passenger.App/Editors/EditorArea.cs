using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Editors;

/// <summary>The editors, as Rider's: one group of tabs, or several split right and down, one of them active (the one
/// the person works in, whose editor <see cref="CodeEditor"/> is). A tab dragged onto a group's edge splits it there,
/// onto its middle or its tab strip moves into it; a group whose last tab closes goes, and its neighbour takes its
/// room. A file shows in as many groups as it is open in, over one text.</summary>
public sealed class EditorArea : UserControl
{
    private const double DragThreshold = 6;

    private readonly ObservableCollection<OpenDocument> documents = [];
    private readonly ContentControl splits = new();
    private readonly Canvas assist = new() { ClipToBounds = false };
    private readonly Canvas dropLayer = new() { IsHitTestVisible = false, IsVisible = false };
    private readonly Border dropZone = new() { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4) };
    private readonly StackPanel emptyState;
    private readonly TextBlock emptyTitle;
    private readonly TextBlock emptyHint;
    private readonly Button openButton;
    private readonly List<Action<CodeEditor>> editorHooks = [];
    private Node root;
    private EditorGroup activeGroup;
    private (EditorGroup Group, OpenDocument Document, Point Start, bool Dragging)? drag;

    public EditorArea()
    {
        activeGroup = NewGroup();
        activeGroup.IsCurrent = true;
        root = new Leaf(activeGroup);
        emptyTitle = new TextBlock { Text = "Passenger", FontSize = 28, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        emptyTitle.Bind(TextBlock.ForegroundProperty, emptyTitle.GetResourceObservable("Passenger.Text"));
        emptyHint = new TextBlock { Classes = { "muted" }, Text = "Open a solution, project or folder to start.", HorizontalAlignment = HorizontalAlignment.Center };
        openButton = new Button { Classes = { "primary" }, Content = "Open…", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        openButton.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        emptyState = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { emptyTitle, emptyHint, openButton } };
        dropZone.Bind(Border.BorderBrushProperty, dropZone.GetResourceObservable("Passenger.Accent"));
        dropZone.Bind(Border.BackgroundProperty, dropZone.GetResourceObservable("Passenger.DropZone"));
        dropLayer.Children.Add(dropZone);
        Content = new Panel { Children = { splits, emptyState, assist, dropLayer } };
        Rebuild();
    }

    public event EventHandler? ActiveChanged;

    /// <summary>A file opened in a tab, before it is shown; and one whose last tab closed.</summary>
    public event EventHandler<OpenDocument>? Opened;

    public event EventHandler<OpenDocument>? Closed;
    public event EventHandler? CaretMoved;
    public event EventHandler? OpenRequested;

    /// <summary>A file that could not be written, and why: it stays open with its edits.</summary>
    public event EventHandler<string>? SaveFailed;

    /// <summary>A file not written because something else changed it on disk since it was last loaded or saved: which
    /// to keep is the person's question (File Cache Conflict), never settled by writing over the disk's.</summary>
    public event EventHandler<OpenDocument>? SaveConflicted;

    /// <summary>A file or folder moved through <see cref="MoveFile"/>, with the open files that went with it.</summary>
    public event EventHandler<FileMove>? Moved;

    /// <summary>The active group's editor: the one keys and the IDE's actions go to.</summary>
    public CodeEditor CodeEditor => activeGroup.Editor;

    /// <summary>The file the active group shows.</summary>
    public OpenDocument? Active => activeGroup.Active;

    /// <summary>Every file open, in any group, once each.</summary>
    public IReadOnlyList<OpenDocument> Documents => documents;

    public IReadOnlyList<EditorGroup> Groups => [.. root.Groups];

    public EditorGroup ActiveGroup => activeGroup;

    public FindBar FindBar => activeGroup.Find;

    /// <summary>The layer over the editors that completion and the code actions' popups sit on.</summary>
    public Canvas AssistLayer => assist;

    /// <summary>Runs for every editor there is and every one a split makes, as wiring for its events.</summary>
    public void ForEachEditor(Action<CodeEditor> hook)
    {
        editorHooks.Add(hook);
        foreach (EditorGroup group in root.Groups)
        {
            hook(group.Editor);
        }
    }

    /// <summary>The empty area's words once a folder is open: it has files now, just none showing.</summary>
    public void ShowFolderHint(string folder)
    {
        emptyTitle.Text = Path.GetFileName(folder);
        emptyHint.Text = "Open a file from the Explorer.";
        openButton.IsVisible = false;
    }

    /// <summary>Opens a file in a tab of the active group (or shows its tab if open), returning why not if it can't be.</summary>
    public string? Open(string path)
    {
        string full = Path.GetFullPath(path);
        if (documents.FirstOrDefault(d => d.Path == full) is { } open)
        {
            Activate(open);
            return null;
        }
        FileText? file;
        try
        {
            file = FileText.Load(full);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return error.Message;
        }
        if (file is null)
        {
            return $"{Path.GetFileName(full)} looks like a binary file.";
        }
        OpenDocument document = new(full, file);
        Opened?.Invoke(this, document);
        documents.Add(document);
        activeGroup.Add(document);
        Activate(document);
        return null;
    }

    /// <summary>Shows a file: in the active group if it has its tab, else in the group that does.</summary>
    public void Activate(OpenDocument document)
    {
        EditorGroup group = activeGroup.Has(document) ? activeGroup : root.Groups.FirstOrDefault(g => g.Has(document)) ?? activeGroup;
        group.Show(document);
        SetActiveGroup(group);
        Refresh();
        group.Editor.Focus();
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closes a file everywhere it is open, saved first as Rider never asks; false, the file left open, when
    /// it could not be written.</summary>
    public bool Close(OpenDocument document)
    {
        if (document.IsModified && !TrySave(document))
        {
            return false;
        }
        foreach (EditorGroup group in root.Groups.Where(g => g.Has(document)).ToList())
        {
            group.Remove(document);
        }
        documents.Remove(document);
        Closed?.Invoke(this, document);
        document.Close();
        Collapse();
        ActiveChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Closes a tab of one group: the file stays open if another group has it.</summary>
    public bool CloseTab(EditorGroup group, OpenDocument document)
    {
        if (root.Groups.Count(g => g.Has(document)) > 1)
        {
            group.Remove(document);
            Collapse();
            ActiveChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return Close(document);
    }

    public void CloseOthers(EditorGroup group, OpenDocument keep)
    {
        foreach (OpenDocument other in group.Tabs.Select(t => t.Document).Where(d => d != keep).ToList())
        {
            if (!CloseTab(group, other))
            {
                return;
            }
        }
    }

    public void CloseAllTabs(EditorGroup group)
    {
        foreach (OpenDocument each in group.Tabs.Select(t => t.Document).ToList())
        {
            if (!CloseTab(group, each))
            {
                return;
            }
        }
    }

    /// <summary>Opens a file in a new group beside one (right for horizontal, below for vertical), as Rider's Split
    /// Right and Split Down; with <paramref name="move"/> its tab leaves the first group.</summary>
    public void Split(EditorGroup beside, OpenDocument document, Orientation orientation, bool move, bool before = false)
    {
        EditorGroup created = NewGroup();
        root = root.Replace(beside, leaf => new SplitNode(orientation, before ? new Leaf(created) : leaf, before ? leaf : new Leaf(created)));
        created.Add(document);
        created.Show(document);
        if (move)
        {
            beside.Remove(document);
        }
        Rebuild();
        Collapse();
        SetActiveGroup(created);
        created.Editor.Focus();
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves a tab to another group, at a place on its strip or after its active tab.</summary>
    public void Move(EditorGroup from, OpenDocument document, EditorGroup to, int? index = null)
    {
        if (from == to)
        {
            if (index is int at)
            {
                from.Reorder(document, at);
            }
            return;
        }
        to.Add(document, index);
        to.Show(document);
        from.Remove(document);
        Collapse();
        SetActiveGroup(to);
        to.Editor.Focus();
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Rider's Move to Opposite Group: the tab goes to the group beside its own, if there is one.</summary>
    public void MoveToOpposite(EditorGroup group, OpenDocument document)
    {
        List<EditorGroup> all = [.. root.Groups];
        if (all.Count > 1)
        {
            Move(group, document, all[(all.IndexOf(group) + 1) % all.Count]);
        }
    }

    /// <summary>The group the person works in, from a click or the focus.</summary>
    public void SetActiveGroup(EditorGroup group)
    {
        if (group == activeGroup || !root.Groups.Contains(group))
        {
            return;
        }
        activeGroup.Keep();
        activeGroup.IsCurrent = false;
        activeGroup = group;
        group.IsCurrent = true;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A tab of a group was shown by a click on it.</summary>
    internal void Activated(EditorGroup group)
    {
        Refresh();
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens the find bar over the active file, with the replace row if asked.</summary>
    public void ShowFind(bool replace)
    {
        if (Active is not null)
        {
            activeGroup.Find.Open(replace);
        }
    }

    public void SaveActive()
    {
        if (Active is { } active)
        {
            TrySave(active);
        }
    }

    /// <summary>Saves every changed file, but those <paramref name="waiting"/> on a question; whether all were written.</summary>
    public bool SaveAll(IReadOnlySet<OpenDocument>? waiting = null)
    {
        bool all = true;
        foreach (OpenDocument document in documents.Where(d => d.IsModified && waiting?.Contains(d) != true).ToList())
        {
            all &= TrySave(document);
        }
        return all;
    }

    /// <summary>Writes a file even over a version on disk newer than its own, as Keep Memory Changes does.</summary>
    public bool SaveOver(OpenDocument document) => TrySave(document, overDisk: true);

    /// <summary>Writes a file, unless something else changed it on disk meanwhile; whether it was written.</summary>
    public bool Save(OpenDocument document) => TrySave(document);

    /// <summary>Moves a file or folder on disk with its open files, which keep their tabs in every split, their undo
    /// and carets: each is saved first, so what moves is what the editor shows. Null when it moved, else why not, with
    /// nothing moved.</summary>
    public string? MoveFile(string from, string to)
    {
        string source = PathNames.Canonical(from);
        List<OpenDocument> moving = [.. documents.Where(d => PathNames.Within(d.Path, source))];
        foreach (OpenDocument document in moving)
        {
            if (document.IsModified && !TrySave(document))
            {
                return $"{document.Name} could not be saved before it moved";
            }
        }
        try
        {
            if (Directory.Exists(from))
            {
                Directory.Move(from, to);
            }
            else
            {
                File.Move(from, to);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return error.Message;
        }
        string target = PathNames.Canonical(to);
        foreach (OpenDocument document in moving)
        {
            document.MoveTo(PathNames.Moved(document.Path, source, target));
        }
        Moved?.Invoke(this, new FileMove(source, target, moving));
        if (Active is { } shown && moving.Contains(shown))
        {
            // What names the file shown (the title, the status bar's path, the structure) reads it again.
            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }
        return null;
    }

    private bool TrySave(OpenDocument document, bool overDisk = false)
    {
        if (!overDisk && document.ChangedOnDisk())
        {
            SaveConflicted?.Invoke(this, document);
            return false;
        }
        try
        {
            document.Save();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SaveFailed?.Invoke(this, $"{document.Name} could not be saved: {error.Message}");
            return false;
        }
    }

    /// <summary>The splits, each group's tabs with their carets and the one shown, for the folder to open on next time.</summary>
    public EditorLayout Capture()
    {
        activeGroup.Keep();
        return Capture(root);
    }

    private EditorLayout Capture(Node node)
    {
        if (node is SplitNode split)
        {
            return new EditorLayout
            {
                Split = split.Orientation == Orientation.Horizontal ? "right" : "down",
                Ratio = split.Ratio,
                First = Capture(split.First),
                Second = Capture(split.Second),
            };
        }
        EditorGroup group = ((Leaf)node).Group;
        return new EditorLayout
        {
            Tabs = [.. group.Tabs.Select(t =>
            {
                TextPosition caret = t.Document.Text.Snapshot.GetPosition(Math.Min(group.SelectionsOf(t.Document).Primary.Caret, t.Document.Text.Snapshot.Length));
                return new KeptTab(t.Document.Path, caret.Line, caret.Column);
            })],
            Shown = group.Active is { } shown ? group.Tabs.Select(t => t.Document).ToList().IndexOf(shown) : 0,
            Current = group == activeGroup,
        };
    }

    /// <summary>Opens what a layout kept, into an area with nothing open: its splits, each group's tabs with their
    /// carets and the one shown. A file gone from disk is left out, and a group left with none goes.</summary>
    public void Restore(EditorLayout layout)
    {
        if (documents.Count > 0 || Restore(layout, out EditorGroup? current) is not { } restored)
        {
            return;
        }
        activeGroup.IsCurrent = false;
        root = restored;
        activeGroup = current ?? root.Groups.First();
        activeGroup.IsCurrent = true;
        Rebuild();
        activeGroup.Editor.Focus();
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    private Node? Restore(EditorLayout kept, out EditorGroup? current)
    {
        current = null;
        if (kept.Split is { } direction && kept.First is { } firstKept && kept.Second is { } secondKept)
        {
            Node? first = Restore(firstKept, out EditorGroup? inFirst);
            Node? second = Restore(secondKept, out EditorGroup? inSecond);
            current = inFirst ?? inSecond;
            return first is null ? second : second is null ? first
                : new SplitNode(direction == "down" ? Orientation.Vertical : Orientation.Horizontal, first, second) { Ratio = Math.Clamp(kept.Ratio, 0.05, 0.95) };
        }
        EditorGroup group = NewGroup();
        OpenDocument? shown = null;
        TextPosition shownAt = default;
        foreach ((KeptTab tab, int index) in (kept.Tabs ?? []).Select((t, i) => (t, i)))
        {
            if (Load(tab.Path) is not { } document)
            {
                continue;
            }
            group.Add(document, group.Tabs.Count);
            TextPosition at = new(Math.Clamp(tab.Line, 0, document.Text.Snapshot.LineCount - 1), Math.Max(0, tab.Column));
            group.Remember(document, SelectionSet.Single(document.Text.Snapshot.GetOffset(at)));
            if (index == kept.Shown || shown is null)
            {
                (shown, shownAt) = (document, at);
            }
        }
        if (shown is null)
        {
            return null;
        }
        group.Show(shown);
        group.Editor.GoTo(shownAt);
        current = kept.Current ? group : null;
        return new Leaf(group);
    }

    /// <summary>A file's document, open already or read from disk now; null when it cannot be read as text.</summary>
    private OpenDocument? Load(string path)
    {
        string full = Path.GetFullPath(path);
        if (documents.FirstOrDefault(d => d.Path == full) is { } open)
        {
            return open;
        }
        try
        {
            if (FileText.Load(full) is not { } file)
            {
                return null;
            }
            OpenDocument document = new(full, file);
            Opened?.Invoke(this, document);
            documents.Add(document);
            return document;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Closes every file, each saved first; false when one could not be, and stays open.</summary>
    public bool CloseAll()
    {
        foreach (OpenDocument document in documents.ToList())
        {
            if (!Close(document))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The next tab of the active group (or previous, for a negative step), wrapping at the ends.</summary>
    public void Cycle(int step)
    {
        activeGroup.Cycle(step);
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    // Dragging a tab.

    internal void BeginTabDrag(EditorGroup group, OpenDocument document, PointerPressedEventArgs e) =>
        drag = (group, document, e.GetPosition(this), false);

    internal void ContinueTabDrag(PointerEventArgs e)
    {
        if (drag is not { } current)
        {
            return;
        }
        Point at = e.GetPosition(this);
        if (!current.Dragging && Math.Abs(at.X - current.Start.X) + Math.Abs(at.Y - current.Start.Y) < DragThreshold)
        {
            return;
        }
        drag = current with { Dragging = true };
        if (Target(at) is { } target)
        {
            Canvas.SetLeft(dropZone, target.Zone.X + 2);
            Canvas.SetTop(dropZone, target.Zone.Y + 2);
            dropZone.Width = Math.Max(0, target.Zone.Width - 4);
            dropZone.Height = Math.Max(0, target.Zone.Height - 4);
            dropLayer.IsVisible = true;
        }
        else
        {
            dropLayer.IsVisible = false;
        }
    }

    internal void EndTabDrag(PointerEventArgs e)
    {
        if (drag is not { } current)
        {
            return;
        }
        drag = null;
        dropLayer.IsVisible = false;
        if (!current.Dragging || Target(e.GetPosition(this)) is not { } target)
        {
            return;
        }
        switch (target.Place)
        {
            case DropPlace.Strip:
                Move(current.Group, current.Document, target.Group, target.Index);
                break;
            case DropPlace.Middle:
                Move(current.Group, current.Document, target.Group);
                break;
            default:
                // A tab dropped on its own group's edge, the group's only tab, has nothing to split from.
                if (target.Group == current.Group && current.Group.Tabs.Count == 1)
                {
                    return;
                }
                bool across = target.Place is DropPlace.Left or DropPlace.Right;
                bool before = target.Place is DropPlace.Left or DropPlace.Top;
                Split(target.Group, current.Document, across ? Orientation.Horizontal : Orientation.Vertical, move: target.Group == current.Group, before);
                if (target.Group != current.Group)
                {
                    current.Group.Remove(current.Document);
                    Collapse();
                }
                break;
        }
    }

    private enum DropPlace
    {
        Strip,
        Middle,
        Left,
        Right,
        Top,
        Bottom,
    }

    /// <summary>Where a tab dropped at a point goes: onto a group's strip at a place, its middle, or one of its edges
    /// (the outer quarter), with the rectangle that lights up for it.</summary>
    private (EditorGroup Group, DropPlace Place, int Index, Rect Zone)? Target(Point at)
    {
        foreach (EditorGroup group in root.Groups)
        {
            if (group.TranslatePoint(default, this) is not { } origin)
            {
                continue;
            }
            Rect bounds = new(origin, group.Bounds.Size);
            if (!bounds.Contains(at))
            {
                continue;
            }
            Rect strip = new(origin, new Size(group.Bounds.Width, group.TabStrip.Bounds.Height));
            if (strip.Contains(at))
            {
                return (group, DropPlace.Strip, group.IndexAt(this.TranslatePoint(at, group.TabStrip) ?? at), strip);
            }
            Rect body = new(bounds.X, strip.Bottom, bounds.Width, bounds.Bottom - strip.Bottom);
            double x = (at.X - body.X) / body.Width;
            double y = (at.Y - body.Y) / body.Height;
            return x < 0.25 ? (group, DropPlace.Left, 0, body.WithWidth(body.Width / 2))
                : x > 0.75 ? (group, DropPlace.Right, 0, new Rect(body.X + (body.Width / 2), body.Y, body.Width / 2, body.Height))
                : y < 0.25 ? (group, DropPlace.Top, 0, body.WithHeight(body.Height / 2))
                : y > 0.75 ? (group, DropPlace.Bottom, 0, new Rect(body.X, body.Y + (body.Height / 2), body.Width, body.Height / 2))
                : (group, DropPlace.Middle, 0, body);
        }
        return null;
    }

    // The split tree.

    private EditorGroup NewGroup()
    {
        EditorGroup group = new(this) { IsCurrent = false };
        group.Editor.SelectionChanged += (_, _) =>
        {
            if (group == activeGroup)
            {
                CaretMoved?.Invoke(this, EventArgs.Empty);
            }
        };
        foreach (Action<CodeEditor> hook in editorHooks)
        {
            hook(group.Editor);
        }
        return group;
    }

    /// <summary>Groups left with no tab go (all but the last), their neighbours taking their room.</summary>
    private void Collapse()
    {
        bool changed = false;
        foreach (EditorGroup empty in root.Groups.Where(g => g.Tabs.Count == 0).ToList())
        {
            if (root.Groups.Count() > 1)
            {
                root = root.Without(empty) ?? root;
                changed = true;
            }
        }
        if (changed)
        {
            Rebuild();
        }
        if (!root.Groups.Contains(activeGroup))
        {
            activeGroup = root.Groups.FirstOrDefault(g => g.Active is not null) ?? root.Groups.First();
            activeGroup.IsCurrent = true;
            if (activeGroup.Active is not null)
            {
                activeGroup.Editor.Focus();
            }
        }
        Refresh();
    }

    private void Rebuild()
    {
        foreach (EditorGroup group in root.Groups)
        {
            (group.Parent as Panel)?.Children.Remove(group);
        }
        splits.Content = Build(root);
        Refresh();
    }

    private void Refresh() => emptyState.IsVisible = documents.Count == 0;

    private static Control Build(Node node)
    {
        if (node is Leaf leaf)
        {
            (leaf.Group.Parent as Panel)?.Children.Remove(leaf.Group);
            return new Panel { Children = { leaf.Group } };
        }
        SplitNode split = (SplitNode)node;
        bool across = split.Orientation == Orientation.Horizontal;
        Grid grid = across
            ? new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*") }
            : new Grid { RowDefinitions = new RowDefinitions("*,1,*") };
        Control first = Build(split.First);
        Control second = Build(split.Second);
        // Drawn again (a group gone elsewhere), a split keeps the share it was dragged to.
        double ratio = split.Ratio;
        split.Built = grid;
        if (across)
        {
            grid.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
            grid.ColumnDefinitions[2].Width = new GridLength(1 - ratio, GridUnitType.Star);
        }
        else
        {
            grid.RowDefinitions[0].Height = new GridLength(ratio, GridUnitType.Star);
            grid.RowDefinitions[2].Height = new GridLength(1 - ratio, GridUnitType.Star);
        }
        Border line = new() { IsHitTestVisible = false };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("Passenger.Divider"));
        GridSplitter splitter = new() { Classes = { across ? "hairline" : "hairlineRows" }, ResizeDirection = across ? GridResizeDirection.Columns : GridResizeDirection.Rows };
        Panel divider = new() { Children = { line, splitter } };
        if (across)
        {
            Grid.SetColumn(second, 2);
            Grid.SetColumn(divider, 1);
        }
        else
        {
            Grid.SetRow(second, 2);
            Grid.SetRow(divider, 1);
        }
        grid.Children.Add(first);
        grid.Children.Add(divider);
        grid.Children.Add(second);
        return grid;
    }

    private abstract class Node
    {
        public abstract IEnumerable<EditorGroup> Groups { get; }

        /// <summary>The tree with a group's leaf replaced by what a function makes of it.</summary>
        public abstract Node Replace(EditorGroup group, Func<Leaf, Node> with);

        /// <summary>The tree without a group's leaf, its sibling in its split's place; null when it was all.</summary>
        public abstract Node? Without(EditorGroup group);
    }

    private sealed class Leaf(EditorGroup group) : Node
    {
        public EditorGroup Group { get; } = group;
        public override IEnumerable<EditorGroup> Groups => [Group];
        public override Node Replace(EditorGroup target, Func<Leaf, Node> with) => target == Group ? with(this) : this;
        public override Node? Without(EditorGroup target) => target == Group ? null : this;
    }

    private sealed class SplitNode(Orientation orientation, Node first, Node second) : Node
    {
        public Orientation Orientation { get; } = orientation;
        public Node First { get; } = first;
        public Node Second { get; } = second;

        /// <summary>The first part's share of the room: as dragged, once its grid is laid out.</summary>
        public double Ratio
        {
            get
            {
                if (Built is not { } grid)
                {
                    return ratio;
                }
                (double a, double b) = Orientation == Orientation.Horizontal
                    ? (grid.ColumnDefinitions[0].ActualWidth, grid.ColumnDefinitions[2].ActualWidth)
                    : (grid.RowDefinitions[0].ActualHeight, grid.RowDefinitions[2].ActualHeight);
                return a + b > 0 ? a / (a + b) : ratio;
            }
            init => ratio = value;
        }

        private readonly double ratio = 0.5;

        /// <summary>The grid this split was last drawn as.</summary>
        public Grid? Built { get; set; }
        public override IEnumerable<EditorGroup> Groups => First.Groups.Concat(Second.Groups);

        public override Node Replace(EditorGroup target, Func<Leaf, Node> with) =>
            new SplitNode(Orientation, First.Replace(target, with), Second.Replace(target, with)) { Ratio = Ratio };

        public override Node? Without(EditorGroup target)
        {
            Node? first = First.Without(target);
            Node? second = Second.Without(target);
            return first is null ? second : second is null ? first : new SplitNode(Orientation, first, second) { Ratio = Ratio };
        }
    }
}

/// <summary>A file or folder that moved on disk (both paths canonical), and the open files that went with it.</summary>
public sealed record FileMove(string From, string To, IReadOnlyList<OpenDocument> Documents);
