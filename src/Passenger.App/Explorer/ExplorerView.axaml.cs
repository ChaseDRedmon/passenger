using Avalonia.Controls;
using Avalonia.Input;

namespace Passenger.App.Explorer;

/// <summary>What the Explorer's menu asks of the window for a row.</summary>
public enum ExplorerAction
{
    NewFile,
    NewFolder,
    NewClass,
    Rename,
    Delete,
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
}

public partial class ExplorerView : UserControl
{
    public ExplorerView()
    {
        InitializeComponent();
        Rows.ItemsSource = Tree.Rows;
        // Ahead of the list's own keys: it takes Enter for its selection, so a row would never open.
        Rows.AddHandler(KeyDownEvent, OnRowsKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
    /// (a new or existing project, a solution folder or an existing item into the solution's folders; a file, a folder or
    /// a class on disk), Rename and Delete, which for what is the solution's own (a project, a solution folder, its files)
    /// is Remove, and the path of what is on disk.</summary>
    private ContextMenu? Menu(ExplorerNode node)
    {
        bool onDisk = node.Kind is NodeKind.File or NodeKind.Folder or NodeKind.Project;
        bool holds = (node.Kind is NodeKind.Folder or NodeKind.Project or NodeKind.Solution or NodeKind.File) && !node.IsSolutionItem;
        bool solutionFolder = node.Kind == NodeKind.SolutionFolder;
        if (!onDisk && !holds && !solutionFolder)
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
                add.Items.Add(Item("Class...", ExplorerAction.NewClass));
            }
            menu.Items.Add(add);
            menu.Items.Add(new Separator());
        }
        if (Renames(node))
        {
            menu.Items.Add(Item("Rename...", ExplorerAction.Rename, "F2"));
        }
        if (Removes(node))
        {
            // What is the solution's own only leaves the solution, as Rider's Remove does; its files stay on disk.
            bool removes = node.Kind is NodeKind.Project or NodeKind.SolutionFolder || node.IsSolutionItem;
            menu.Items.Add(Item(removes ? "Remove..." : "Delete...", ExplorerAction.Delete, OperatingSystem.IsMacOS() ? "Back" : "Delete"));
        }
        // A solution folder is in no folder on disk, so it has no path to copy or show.
        if (!solutionFolder)
        {
            if (onDisk)
            {
                menu.Items.Add(new Separator());
            }
            menu.Items.Add(Item("Copy Path", ExplorerAction.CopyPath));
            menu.Items.Add(Item(OperatingSystem.IsMacOS() ? "Reveal in Finder" : "Show in Explorer", ExplorerAction.Reveal));
        }
        return menu;
    }

    /// <summary>Whether F2 renames the row: what is on disk, but a file only the solution lists, and a solution folder.</summary>
    private static bool Renames(ExplorerNode node) => node.Kind is NodeKind.Folder or NodeKind.Project or NodeKind.SolutionFolder || node.Kind == NodeKind.File && !node.IsSolutionItem;

    private static bool Removes(ExplorerNode node) => node.Kind is NodeKind.File or NodeKind.Folder or NodeKind.Project or NodeKind.SolutionFolder;

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

    private void OnRowsKeyDown(object? sender, KeyEventArgs e)
    {
        if (Rows.SelectedItem is not ExplorerNode node)
        {
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
