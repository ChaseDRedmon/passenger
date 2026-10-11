using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Passenger.App;

public partial class MainWindow : Window
{
    /// <summary>Room at the toolbar's left for macOS's window buttons once the toolbar is the title bar.</summary>
    private const double MacWindowButtons = 72;

    public MainWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsMacOS())
        {
            // The toolbar is the title bar on macOS, as in Rider. Windows keeps the system's title bar
            // until the shell draws its own chrome there.
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = Toolbar.Height;
        }
        RuntimeText.Text = $".NET {Environment.Version}";
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExtendedIntoWindowDecorationsProperty)
        {
            double left = IsExtendedIntoWindowDecorations && OperatingSystem.IsMacOS() ? MacWindowButtons : 8;
            ToolbarContent.Margin = new Thickness(left, 0, 8, 0);
        }
    }

    private void OnToolbarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsExtendedIntoWindowDecorations || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        BeginMoveDrag(e);
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Open" });
        if (picked.Count == 0)
        {
            return;
        }
        string name = picked[0].Name;
        ProjectName.Text = name;
        ProjectBadgeText.Text = Initials(name);
        ProjectBadge.IsVisible = true;
        StatusText.Text = name;
        ExplorerHint.Text = "No file tree yet";
    }

    /// <summary>The project badge's two letters, as Rider draws them: the first two words' initials,
    /// or a lone word's first two letters.</summary>
    private static string Initials(string name)
    {
        string[] words = name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        string letters = words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)],
            _ => $"{words[0][0]}{words[1][0]}",
        };
        return letters.ToUpperInvariant();
    }
}
