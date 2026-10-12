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
}

public partial class ExplorerView : UserControl
{
    public ExplorerView()
    {
        InitializeComponent();
        Rows.ItemsSource = Tree.Rows;
        Rows.ContextRequested += (_, e) =>
        {
            if (Rows.SelectedItem is ExplorerNode node && Menu(node) is { } menu)
            {
                menu.Open(Rows);
                e.Handled = true;
            }
        };
    }

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
        // A row is known by its path, or (a solution folder, the dependencies) by where it is and what it is called.
        static string Key(ExplorerNode row) => row.Path ?? $"{row.Depth}/{row.Kind}/{row.Name}";
        List<(int Depth, string Key)> open = [.. Tree.Rows.Where(r => r.IsExpanded).Select(r => (r.Depth, Key(r)))];
        string? chosen = Selected?.Path;
        Show(Tree.IsSolutionView);
        // Outermost first, so each opens inside one already open.
        foreach ((_, string key) in open.OrderBy(o => o.Depth))
        {
            if (Tree.Rows.FirstOrDefault(r => Key(r) == key) is { HasChildren: true, IsExpanded: false } row)
            {
                Tree.Expand(row);
            }
        }
        if (chosen is not null && Tree.Rows.FirstOrDefault(r => r.Path == chosen) is { } again)
        {
            Rows.SelectedItem = again;
        }
    }

    /// <summary>Rider's menu for a row: Build, Rebuild and Clean for the solution or the projects chosen, a project's
    /// packages, file and unloading, New (a file, a folder, a class) where things can go, Rename and Delete for what is on
    /// disk, and its path.</summary>
    private ContextMenu? Menu(ExplorerNode node)
    {
        bool onDisk = node.Kind is NodeKind.File or NodeKind.Folder or NodeKind.Project;
        bool holds = node.Kind is NodeKind.Folder or NodeKind.Project or NodeKind.Solution or NodeKind.File;
        if (!onDisk && !holds)
        {
            return null;
        }
        ContextMenu menu = new();
        MenuItem Item(string header, ExplorerAction action, string? keys = null)
        {
            MenuItem item = new() { Header = keys is null ? header : $"{header}    {keys}" };
            item.Click += (_, _) => ActionRequested?.Invoke(this, (action, node));
            return item;
        }
        if (node.Kind is NodeKind.Solution or NodeKind.Project)
        {
            string what = node.Kind == NodeKind.Solution ? "Solution" : "Selected Projects";
            menu.Items.Add(Item($"Build {what}", ExplorerAction.Build));
            menu.Items.Add(Item($"Rebuild {what}", ExplorerAction.Rebuild));
            menu.Items.Add(Item($"Clean {what}", ExplorerAction.Clean));
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
        if (holds)
        {
            MenuItem add = new() { Header = "Add" };
            add.Items.Add(Item("New File...", ExplorerAction.NewFile));
            add.Items.Add(Item("New Folder...", ExplorerAction.NewFolder));
            add.Items.Add(Item("Class...", ExplorerAction.NewClass));
            menu.Items.Add(add);
            menu.Items.Add(new Separator());
        }
        if (onDisk)
        {
            menu.Items.Add(Item("Rename...", ExplorerAction.Rename, "F2"));
            // A project's Delete only takes it out of the solution, as Rider's Remove Projects does.
            menu.Items.Add(Item(node.Kind == NodeKind.Project ? "Remove..." : "Delete...", ExplorerAction.Delete, OperatingSystem.IsMacOS() ? "⌫" : "Delete"));
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(Item("Copy Path", ExplorerAction.CopyPath));
        menu.Items.Add(Item(OperatingSystem.IsMacOS() ? "Reveal in Finder" : "Show in Explorer", ExplorerAction.Reveal));
        return menu;
    }

    public ExplorerTree Tree { get; } = new();

    private string? folder;
    private Workspace.SolutionModel? solution;

    /// <summary>Shows a folder: its solution's view if it has one, its files otherwise.</summary>
    public void Load(string path, Workspace.SolutionModel? model)
    {
        folder = path;
        solution = model;
        ViewSwitch.IsVisible = model is not null;
        Show(solutionView: model is not null);
        Hint.IsVisible = false;
        Rows.IsVisible = true;
    }

    /// <summary>Reads again only the folders that changed on disk, the rows open and chosen kept.</summary>
    public void RefreshFolders(IEnumerable<string> folders) => Tree.RefreshFolders(folders);

    /// <summary>The folder's solution read again (a project added or changed on disk), the rows open kept.</summary>
    public void Reload(Workspace.SolutionModel? model)
    {
        solution = model;
        ViewSwitch.IsVisible = model is not null;
        Refresh();
    }

    private void Show(bool solutionView)
    {
        if (solutionView && solution is not null)
        {
            Tree.LoadSolution(solution);
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
            case Key.F2 when node.Kind is NodeKind.File or NodeKind.Folder or NodeKind.Project:
                ActionRequested?.Invoke(this, (ExplorerAction.Rename, node));
                e.Handled = true;
                break;
            case Key.Delete or Key.Back when node.Kind is NodeKind.File or NodeKind.Folder or NodeKind.Project:
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
