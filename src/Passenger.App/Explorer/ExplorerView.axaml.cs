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

    public FileTree Tree { get; } = new();

    public void Load(string folder)
    {
        Tree.Load(folder);
        Hint.IsVisible = false;
        Rows.IsVisible = true;
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

    private void Activate(FileNode node)
    {
        if (node.IsDirectory)
        {
            Tree.Toggle(node);
        }
        else
        {
            FileOpened?.Invoke(this, node.Path);
        }
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Rows.SelectedItem is FileNode node)
        {
            Activate(node);
            e.Handled = true;
        }
    }

    private void OnChevronPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: FileNode { IsDirectory: true } node })
        {
            Tree.Toggle(node);
            e.Handled = true;
        }
    }

    private void OnRowsKeyDown(object? sender, KeyEventArgs e)
    {
        if (Rows.SelectedItem is not FileNode node)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Enter:
                Activate(node);
                e.Handled = true;
                break;
            case Key.Right when node.IsDirectory && !node.IsExpanded:
                Tree.Expand(node);
                e.Handled = true;
                break;
            case Key.Left when node.IsDirectory && node.IsExpanded:
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
