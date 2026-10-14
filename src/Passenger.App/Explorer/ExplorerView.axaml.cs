using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Passenger.App.Docking;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.App.Explorer;

/// <summary>What the Explorer's menu asks of the window for a row.</summary>
public enum ExplorerAction
{
    NewFile,
    NewFolder,
    NewClass,
    Rename,
    Delete,
    Cut,
    Copy,
    Paste,
    Undo,
    Redo,
    AddReference,
    Compare,
    OpenInTerminal,
    CopyPath,
    Reveal,
    Build,
    Rebuild,
    Clean,
    ManageNuGet,
    EditProjectFile,
    Unload,
    Reload,
    NewSolutionFolder,
    NewProject,
    AddExistingProject,
    AddExistingItem,
    RunTests,
    DebugTests,
    ShowHistory,
    ShowLocalHistory,
    CompareWithRevision,
    CompareWithBranch,
    FindInFiles,
    ReplaceInFiles,
}

/// <summary>Files and folders dragged onto a row: moved into the folder it stands for, or copied with the platform's
/// copy key held (Option on the Mac, Ctrl elsewhere).</summary>
public sealed record ExplorerDrop(IReadOnlyList<ExplorerNode> Items, ExplorerNode Target, bool Copy);

public partial class ExplorerView : UserControl
{
    private readonly DropOverlay drop = new();
    private readonly DragGesture<IReadOnlyList<ExplorerNode>> drag;
    private bool copyHeld;

    public ExplorerView()
    {
        InitializeComponent();
        Rows.ItemsSource = Tree.Rows;
        // Ahead of the list's own keys: it takes Enter for its selection, so a row would never open.
        Rows.AddHandler(KeyDownEvent, OnRowsKeyDown, RoutingStrategies.Tunnel);
        Surface.Children.Add(drop);
        drag = new DragGesture<IReadOnlyList<ExplorerNode>>(Surface, drop, DropZoneAt, (items, at) =>
        {
            if (TargetAt(items, at) is { } target)
            {
                Dropped?.Invoke(this, new ExplorerDrop(items, target, copyHeld));
            }
        });
        // Seen before a row takes the press for its selection, which the drag leaves as it is.
        Rows.AddHandler(PointerPressedEvent, OnRowsPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Rows.AddHandler(PointerMovedEvent, OnRowsMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        Rows.AddHandler(PointerReleasedEvent, OnRowsReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        Rows.ContextRequested += (_, e) =>
        {
            if (Rows.SelectedItem is ExplorerNode node && Menu(node) is { } menu)
            {
                OpenMenu?.Close();
                OpenMenu = menu;
                menu.Closed += (_, _) => OpenMenu = OpenMenu == menu ? null : OpenMenu;
                menu.Open(Rows);
                e.Handled = true;
            }
        };
    }

    /// <summary>The row's right-click menu while it is open.</summary>
    public ContextMenu? OpenMenu { get; private set; }

    /// <summary>A file chosen with a double-click or Enter.</summary>
    public event EventHandler<string>? FileOpened;

    /// <summary>A row's menu item or key: what to do, and to which row.</summary>
    public event EventHandler<(ExplorerAction Action, ExplorerNode Node)>? ActionRequested;

    /// <summary>Whether Git has a path's history (it is in the folder's working tree), for a row's Git items.</summary>
    public Func<string, bool> IsVersioned { get; set; } = _ => false;

    /// <summary>Files and folders dragged onto a folder, a project or a file's folder.</summary>
    public event EventHandler<ExplorerDrop>? Dropped;

    /// <summary>Whether a Paste would put something in: the menu's Paste is disabled otherwise, as Rider's is.</summary>
    public Func<bool> CanPaste { get; set; } = () => false;

    /// <summary>The row chosen, if any.</summary>
    public ExplorerNode? Selected => Rows.SelectedItem as ExplorerNode;

    /// <summary>Every row chosen: several with Shift or Cmd (Ctrl), as Rider's Solution view allows.</summary>
    public IReadOnlyList<ExplorerNode> SelectedNodes => [.. Rows.SelectedItems?.OfType<ExplorerNode>() ?? []];

    /// <summary>Chooses rows, as clicks with Cmd (Ctrl) held would.</summary>
    public void Choose(params ExplorerNode[] nodes)
    {
        Rows.SelectedItems?.Clear();
        foreach (ExplorerNode node in nodes)
        {
            Rows.SelectedItems?.Add(node);
        }
    }

    /// <summary>Reads the folder again, keeping the folders that were open open and the chosen row chosen.</summary>
    public void Refresh()
    {
        // A row is known by its path, a solution folder by its path in the solution, the dependencies by where they are.
        static string Key(ExplorerNode row) => (row.Kind == NodeKind.SolutionFolder ? row.SolutionPath : row.Path) ?? $"{row.Depth}/{row.Kind}/{row.Name}";
        List<(int Depth, string Key)> open = [.. Tree.Rows.Where(r => r.IsExpanded).Select(r => (r.Depth, Key(r)))];
        string? chosen = Selected is { } selected ? Key(selected) : null;
        Show(Tree.IsSolutionView);
        // Outermost first, so each opens inside one already open.
        foreach ((_, string key) in open.OrderBy(o => o.Depth))
        {
            if (Tree.Rows.FirstOrDefault(r => Key(r) == key) is { HasChildren: true, IsExpanded: false } row)
            {
                Tree.Expand(row);
            }
        }
        if (chosen is not null && Tree.Rows.FirstOrDefault(r => Key(r) == chosen) is { } again)
        {
            Rows.SelectedItem = again;
        }
    }

    /// <summary>Opens the solution folders down to one (by its path in the solution) and chooses it, or what it holds at a
    /// path, opening it for that, or when asked to.</summary>
    public void RevealSolutionFolder(string path, bool open = false, string? child = null)
    {
        if (Tree.RevealSolutionFolder(path) is not { } node)
        {
            return;
        }
        if (open || child is not null)
        {
            Tree.Expand(node);
        }
        ExplorerNode chosen = child is not null && node.Children?.FirstOrDefault(c => c.Path == child) is { } row ? row : node;
        Rows.SelectedItem = chosen;
        Rows.ScrollIntoView(chosen);
    }

    /// <summary>Rider's menu for a row: Build, Rebuild and Clean for the solution or the projects chosen (a solution
    /// folder's, every one under it), their unit tests, a project's packages, file and unloading, Add where things can go
    /// (a new or existing project, a solution folder or an existing item into the solution's folders; a file, a folder, a
    /// type or a project's reference on disk), Edit (Cut, Copy, Paste, Rename, Delete or for the solution's own Remove,
    /// Find and Replace in Files), the path of what is on disk, Open In (the file manager, a terminal), Tools' Compare and
    /// Git's history. A project's Dependencies add a reference, and a project reference is removed.</summary>
    private ContextMenu? Menu(ExplorerNode node)
    {
        bool onDisk = node.Kind is NodeKind.File or NodeKind.Folder or NodeKind.Project;
        bool holds = (node.Kind is NodeKind.Folder or NodeKind.Project or NodeKind.Solution or NodeKind.File) && !node.IsSolutionItem;
        bool solutionFolder = node.Kind == NodeKind.SolutionFolder;
        bool dependency = node is { Kind: NodeKind.Dependencies or NodeKind.ProjectReference, Owner: not null };
        if (!onDisk && !holds && !solutionFolder && !dependency)
        {
            return null;
        }
        ContextMenu menu = new();
        MenuItem Item(string header, ExplorerAction action, string? keys = null)
        {
            MenuItem item = new() { Header = header, InputGesture = keys is null ? null : KeyGesture.Parse(keys) };
            item.Click += (_, _) => ActionRequested?.Invoke(this, (action, node));
            return item;
        }
        MenuItem Action(string header, ExplorerAction action, string id)
        {
            MenuItem item = Item(header, action);
            if (AppActions.Keymap().ShortcutsFor(id).FirstOrDefault(s => s.Second is null) is { } keys)
            {
                item.InputGesture = new KeyGesture(keys.First.Key, keys.First.Modifiers);
            }
            return item;
        }
        if (node.Kind is NodeKind.Solution or NodeKind.Project or NodeKind.SolutionFolder)
        {
            string what = node.Kind == NodeKind.Solution ? "Solution" : "Selected Projects";
            menu.Items.Add(Item($"Build {what}", ExplorerAction.Build));
            menu.Items.Add(Item($"Rebuild {what}", ExplorerAction.Rebuild));
            menu.Items.Add(Item($"Clean {what}", ExplorerAction.Clean));
            menu.Items.Add(new Separator());
        }
        if (node.Kind is NodeKind.Project or NodeKind.SolutionFolder)
        {
            menu.Items.Add(Item("Run Unit Tests", ExplorerAction.RunTests));
            menu.Items.Add(Item("Debug Unit Tests", ExplorerAction.DebugTests));
            menu.Items.Add(new Separator());
        }
        if (node.Kind == NodeKind.Project)
        {
            bool unloaded = node.Path is { } project && Tree.Unloaded.Contains(project);
            menu.Items.Add(Item("Manage NuGet Packages", ExplorerAction.ManageNuGet));
            menu.Items.Add(Item("Edit Project File", ExplorerAction.EditProjectFile));
            menu.Items.Add(unloaded ? Item("Reload Project", ExplorerAction.Reload) : Item("Unload Project", ExplorerAction.Unload));
            menu.Items.Add(new Separator());
        }
        if (holds || solutionFolder)
        {
            MenuItem add = new() { Header = "Add" };
            if (node.Kind is NodeKind.Solution or NodeKind.SolutionFolder)
            {
                add.Items.Add(Action("New Project...", ExplorerAction.NewProject, AppActions.NewProject));
                add.Items.Add(Item("New Solution Folder...", ExplorerAction.NewSolutionFolder));
                add.Items.Add(Item("Existing Project...", ExplorerAction.AddExistingProject));
            }
            if (solutionFolder)
            {
                add.Items.Add(Item("Existing Item...", ExplorerAction.AddExistingItem));
            }
            if (holds)
            {
                if (add.Items.Count > 0)
                {
                    add.Items.Add(new Separator());
                }
                add.Items.Add(Item("New File...", ExplorerAction.NewFile));
                add.Items.Add(Item("New Folder...", ExplorerAction.NewFolder));
                // Rider's C# file templates share this place: Class, Interface, Record, Struct and Enum, chosen in its popup.
                add.Items.Add(Item("Class/Interface...", ExplorerAction.NewClass));
            }
            if (node.Kind == NodeKind.Project)
            {
                add.Items.Add(new Separator());
                add.Items.Add(Item("Reference...", ExplorerAction.AddReference));
            }
            menu.Items.Add(add);
            menu.Items.Add(new Separator());
        }
        if (node.Kind == NodeKind.Dependencies && node.Owner is not null)
        {
            menu.Items.Add(Item("Add Reference...", ExplorerAction.AddReference));
            menu.Items.Add(Item("Manage NuGet Packages", ExplorerAction.ManageNuGet));
            return menu;
        }
        if (node.Kind == NodeKind.ProjectReference && node.Referenced is not null)
        {
            menu.Items.Add(Item("Remove...", ExplorerAction.Delete, DeleteKey));
            return menu;
        }
        // Rider's Edit submenu: the clipboard's actions, Delete and Rename, then Find and Replace in Files on the row.
        MenuItem edit = new() { Header = "Edit" };
        if (node.IsMovable)
        {
            edit.Items.Add(Action("Cut", ExplorerAction.Cut, EditorActions.Cut));
            edit.Items.Add(Action("Copy", ExplorerAction.Copy, EditorActions.Copy));
        }
        if (node.Into is not null)
        {
            MenuItem paste = Action("Paste", ExplorerAction.Paste, EditorActions.Paste);
            paste.IsEnabled = CanPaste();
            edit.Items.Add(paste);
        }
        if (edit.Items.Count > 0 && (Removes(node) || Renames(node)))
        {
            edit.Items.Add(new Separator());
        }
        if (Removes(node))
        {
            // What is the solution's own only leaves the solution, as Rider's Remove does; its files stay on disk.
            bool removes = node.Kind is NodeKind.Project or NodeKind.SolutionFolder || node.IsSolutionItem;
            edit.Items.Add(Item(removes ? "Remove..." : "Delete...", ExplorerAction.Delete, DeleteKey));
        }
        if (Renames(node))
        {
            edit.Items.Add(Item("Rename...", ExplorerAction.Rename, "F2"));
        }
        if (edit.Items.Count > 0)
        {
            edit.Items.Add(new Separator());
        }
        edit.Items.Add(Action("Find in Files...", ExplorerAction.FindInFiles, AppActions.FindInFiles));
        edit.Items.Add(Action("Replace in Files...", ExplorerAction.ReplaceInFiles, AppActions.ReplaceInFiles));
        menu.Items.Add(edit);
        // A solution folder is in no folder on disk, so it has no path to copy or show.
        if (!solutionFolder)
        {
            menu.Items.Add(Item("Copy Path", ExplorerAction.CopyPath));
            MenuItem openIn = new() { Header = "Open In" };
            openIn.Items.Add(Item(ChildProcesses.FileManager, ExplorerAction.Reveal));
            openIn.Items.Add(Item("Terminal", ExplorerAction.OpenInTerminal));
            menu.Items.Add(openIn);
        }
        if (node.Kind == NodeKind.File && !node.IsSolutionItem)
        {
            // Rider's Compare With... for one file, Compare Files for two chosen together.
            bool two = SelectedNodes.Contains(node) && SelectedNodes.Count(n => n.IsFile) == 2;
            MenuItem tools = new() { Header = "Tools" };
            MenuItem compare = Item(two ? "Compare Files" : "Compare With...", ExplorerAction.Compare);
            compare.InputGesture = CompareKey;
            tools.Items.Add(compare);
            menu.Items.Add(tools);
        }
        bool histories = node.Kind is NodeKind.File or NodeKind.Folder or NodeKind.Project or NodeKind.Solution && node.Path is not null && !node.IsSolutionItem;
        if (histories)
        {
            // Rider's Show Local History, just before its Git submenu: a file's versions, or a folder's (a project's or the
            // solution's folder).
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Show Local History", ExplorerAction.ShowLocalHistory));
        }
        if (node.Kind is NodeKind.File or NodeKind.Folder or NodeKind.Project or NodeKind.Solution && node.Path is { } versioned && IsVersioned(versioned))
        {
            // Rider's Git submenu, last: a file's or a folder's history (a project's or the solution's folder), a file's versions.
            MenuItem git = new() { Header = "Git" };
            if (node.Kind == NodeKind.File)
            {
                git.Items.Add(Item("Compare with Revision...", ExplorerAction.CompareWithRevision));
                git.Items.Add(Item("Compare with Branch or Tag...", ExplorerAction.CompareWithBranch));
            }
            git.Items.Add(Item("Show History", ExplorerAction.ShowHistory));
            if (!histories)
            {
                menu.Items.Add(new Separator());
            }
            menu.Items.Add(git);
        }
        return menu;
    }

    private static string DeleteKey => OperatingSystem.IsMacOS() ? "Back" : "Delete";

    /// <summary>Rider's Compare Files (CompareTwoFiles): Ctrl+D in its Visual Studio keymap, Cmd+D in Visual Studio OSX
    /// (its Mac parent's Ctrl made Cmd), where the editor has the same keys for Duplicate.</summary>
    private static KeyGesture CompareKey => new(Key.D, OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);

    /// <summary>Whether F2 renames the row: what is on disk, but a file only the solution lists, and a solution folder.</summary>
    private static bool Renames(ExplorerNode node) => node.Kind is NodeKind.Folder or NodeKind.Project or NodeKind.SolutionFolder || node.Kind == NodeKind.File && !node.IsSolutionItem;

    private static bool Removes(ExplorerNode node) => node.Kind is NodeKind.File or NodeKind.Folder or NodeKind.Project or NodeKind.SolutionFolder
        || node is { Kind: NodeKind.ProjectReference, Referenced: not null };

    public ExplorerTree Tree { get; } = new();

    private string? folder;
    private Workspace.SolutionModel? solution;

    /// <summary>The project opened alone, as Rider opens a project file, which the Solution view shows at its top.</summary>
    private Workspace.ProjectModel? project;

    /// <summary>Shows a workspace: its solution's view if it has one (or the project it was opened as), its folder's files
    /// otherwise.</summary>
    public void Load(Workspace.WorkspaceModel workspace)
    {
        folder = workspace.Folder;
        Take(workspace);
        Show(solutionView: ViewSwitch.IsVisible);
        Hint.IsVisible = false;
        Rows.IsVisible = true;
    }

    /// <summary>Shows nothing, its hint in its place, as it did before a folder opened: the solution was closed.</summary>
    public void Unload()
    {
        folder = null;
        solution = null;
        project = null;
        ViewSwitch.IsVisible = false;
        Tree.Clear();
        Rows.IsVisible = false;
        Hint.IsVisible = true;
    }

    /// <summary>Reads again only the folders that changed on disk, the rows open and chosen kept.</summary>
    public void RefreshFolders(IEnumerable<string> folders) => Tree.RefreshFolders(folders);

    /// <summary>The folder's solution read again (a project added or changed on disk), the rows open kept.</summary>
    public void Reload(Workspace.WorkspaceModel workspace)
    {
        Take(workspace);
        Refresh();
    }

    private void Take(Workspace.WorkspaceModel workspace)
    {
        solution = workspace.Solution;
        project = workspace.ProjectFile is null ? null : workspace.Projects.FirstOrDefault();
        ViewSwitch.IsVisible = solution is not null || project is not null;
    }

    private void Show(bool solutionView)
    {
        if (solutionView && solution is not null)
        {
            Tree.LoadSolution(solution);
            ViewName.Text = "Solution";
        }
        else if (solutionView && project is not null)
        {
            Tree.LoadProject(project);
            ViewName.Text = "Solution";
        }
        else if (folder is not null)
        {
            Tree.LoadFileSystem(folder);
            ViewName.Text = "Files";
        }
    }

    /// <summary>Its view switch, for a tool window's header to carry, and its own title row gone: in a tool window
    /// the header names it, as Rider's Explorer has its view in its title bar.</summary>
    public Control TakeHeaderTools()
    {
        OwnHeader.Children.Remove(ViewSwitch);
        OwnHeader.IsVisible = false;
        return ViewSwitch;
    }

    private void OnViewSwitchClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Show(!Tree.IsSolutionView);

    /// <summary>Gives the tree the keys on its chosen row (the first when none is), as Rider's Project view takes them: the
    /// view itself cannot take the focus, only a row.</summary>
    public void FocusRows()
    {
        if (!Rows.IsVisible || Tree.Rows.Count == 0)
        {
            return;
        }
        if (Rows.SelectedIndex < 0)
        {
            Rows.SelectedIndex = 0;
        }
        int index = Rows.SelectedIndex;
        // A row scrolled out of sight, or in a window just shown, has no container until it is laid out in view.
        Rows.ScrollIntoView(index);
        Rows.UpdateLayout();
        Rows.ContainerFromIndex(index)?.Focus();
    }

    /// <summary>Opens the folders down to a file and selects its row.</summary>
    public void Reveal(string path)
    {
        if (Tree.Reveal(path) is { } node)
        {
            Rows.SelectedItem = node;
            Rows.ScrollIntoView(node);
        }
    }

    private void Activate(ExplorerNode node)
    {
        if (node.IsFile && node.Path is { } path)
        {
            FileOpened?.Invoke(this, path);
        }
        else if (node.HasChildren)
        {
            Tree.Toggle(node);
        }
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Rows.SelectedItem is ExplorerNode node)
        {
            Activate(node);
            e.Handled = true;
        }
    }

    private void OnChevronPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ExplorerNode { HasChildren: true } node })
        {
            Tree.Toggle(node);
            e.Handled = true;
        }
    }

    /// <summary>A left press on a file or folder on disk may begin a drag of it, or of the rows chosen with it.</summary>
    private void OnRowsPressed(object? sender, PointerPressedEventArgs e)
    {
        if (RowAt(e.GetPosition(Rows)) is { IsMovable: true } row)
        {
            // The rows chosen as it was pressed: the list then chooses the row alone.
            IReadOnlyList<ExplorerNode> chosen = SelectedNodes;
            drag.Begin(chosen.Contains(row) ? [.. chosen.Where(n => n.IsMovable)] : [row], e);
        }
    }

    private void OnRowsMoved(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(Rows).Properties.IsLeftButtonPressed)
        {
            // Released outside the rows before it became a drag, which they never heard.
            drag.Cancel();
            return;
        }
        copyHeld = CopyHeld(e.KeyModifiers);
        // Once a drag, the pointer is the rows' until the release, wherever it goes.
        if (drag.Continue(e) && e.Pointer.Captured != Rows)
        {
            e.Pointer.Capture(Rows);
        }
    }

    private void OnRowsReleased(object? sender, PointerReleasedEventArgs e)
    {
        copyHeld = CopyHeld(e.KeyModifiers);
        if (drag.End(e))
        {
            e.Handled = true;
        }
        if (e.Pointer.Captured == Rows)
        {
            e.Pointer.Capture(null);
        }
    }

    /// <summary>The platform's key for a copy rather than a move as files are dropped: Option on the Mac, Ctrl elsewhere.</summary>
    private static bool CopyHeld(KeyModifiers modifiers) => modifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Alt : KeyModifiers.Control);

    /// <summary>The row drawn under a point of the list.</summary>
    private ExplorerNode? RowAt(Point inRows)
    {
        foreach (Control container in Rows.GetRealizedContainers())
        {
            if (Rows.TranslatePoint(inRows, container) is { } at && new Rect(container.Bounds.Size).Contains(at))
            {
                return container.DataContext as ExplorerNode;
            }
        }
        return null;
    }

    /// <summary>The row of what holds a row: the nearest above it, less deep.</summary>
    private ExplorerNode? Holder(ExplorerNode row)
    {
        for (int at = Tree.Rows.IndexOf(row) - 1; at >= 0; at--)
        {
            if (Tree.Rows[at].Depth < row.Depth)
            {
                return Tree.Rows[at];
            }
        }
        return null;
    }

    /// <summary>The folder or project what is dragged would go into at a point: the one under it, or a file's; none where
    /// it would not move (all of it there already) or a folder would go inside itself.</summary>
    private ExplorerNode? TargetAt(IReadOnlyList<ExplorerNode> items, Point at)
    {
        if (Surface.TranslatePoint(at, Rows) is not { } inRows || RowAt(inRows) is not { } row)
        {
            return null;
        }
        ExplorerNode? target = row.Kind == NodeKind.File ? Holder(row) : row;
        if (target is null or { Kind: NodeKind.File } || target.Into is not { } into)
        {
            return null;
        }
        if (items.Any(i => i.Kind == NodeKind.Folder && PathNames.Within(into, i.Path!))
            || !copyHeld && items.All(i => PathNames.Same(Path.GetDirectoryName(i.Path!)!, into)))
        {
            return null;
        }
        return target;
    }

    /// <summary>Where a drop lights up: the folder's row with the rows under it that show, as one place.</summary>
    private DropZone? DropZoneAt(IReadOnlyList<ExplorerNode> items, Point at)
    {
        if (TargetAt(items, at) is not { } target)
        {
            return null;
        }
        int first = Tree.Rows.IndexOf(target);
        int last = first;
        while (last + 1 < Tree.Rows.Count && Tree.Rows[last + 1].Depth > target.Depth)
        {
            last++;
        }
        Rect? place = null;
        foreach (Control container in Rows.GetRealizedContainers())
        {
            int index = Rows.IndexFromContainer(container);
            if (index >= first && index <= last && container.TranslatePoint(default, Surface) is { } corner)
            {
                Rect bounds = new(corner, container.Bounds.Size);
                place = place is { } so ? so.Union(bounds) : bounds;
            }
        }
        return place is { } lit ? new DropZone(lit.Intersect(new Rect(Surface.Bounds.Size))) : null;
    }

    private void OnRowsKeyDown(object? sender, KeyEventArgs e)
    {
        if (Rows.SelectedItem is not ExplorerNode node)
        {
            return;
        }
        // The editor's Cut, Copy, Paste, Undo and Redo keys, whatever the keymap makes them, act on the rows that have them.
        ExplorerAction? clipboard = AppActions.Keymap().ActionFor(new Shortcut(Keystroke.From(e))) switch
        {
            EditorActions.Cut when node.IsMovable => ExplorerAction.Cut,
            EditorActions.Copy when node.IsMovable => ExplorerAction.Copy,
            EditorActions.Paste when node.Into is not null => ExplorerAction.Paste,
            EditorActions.Undo => ExplorerAction.Undo,
            EditorActions.Redo => ExplorerAction.Redo,
            _ => null,
        };
        if (clipboard is { } asked)
        {
            ActionRequested?.Invoke(this, (asked, node));
            e.Handled = true;
            return;
        }
        if (node is { Kind: NodeKind.File, IsSolutionItem: false } && CompareKey.Matches(e))
        {
            ActionRequested?.Invoke(this, (ExplorerAction.Compare, node));
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Enter:
                Activate(node);
                e.Handled = true;
                break;
            case Key.F2 when Renames(node):
                ActionRequested?.Invoke(this, (ExplorerAction.Rename, node));
                e.Handled = true;
                break;
            case Key.Delete or Key.Back when Removes(node):
                ActionRequested?.Invoke(this, (ExplorerAction.Delete, node));
                e.Handled = true;
                break;
            case Key.Right when node.HasChildren && !node.IsExpanded:
                Tree.Expand(node);
                e.Handled = true;
                break;
            case Key.Left when node.HasChildren && node.IsExpanded:
                Tree.Collapse(node);
                e.Handled = true;
                break;
            case Key.Left when node.Depth > 0:
                int at = Tree.Rows.IndexOf(node);
                while (at > 0 && Tree.Rows[at].Depth >= node.Depth)
                {
                    at--;
                }
                Rows.SelectedIndex = at;
                e.Handled = true;
                break;
        }
    }
}
