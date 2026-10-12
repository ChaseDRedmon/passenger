using Avalonia.Controls;
using Avalonia.Input;

namespace Passenger.App.Explorer;

public partial class ExplorerView : UserControl
{
    public ExplorerView()
    {
        InitializeComponent();
        Rows.ItemsSource = Tree.Rows;
    }

    /// <summary>A file chosen with a double-click or Enter.</summary>
    public event EventHandler<string>? FileOpened;

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
