using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Passenger.App;

/// <summary>Rider's IDE Internal Errors: the errors caught since the last Clear All, the chosen one's details under
/// them, and the log a button away.</summary>
public sealed class ErrorsPopup : UserControl
{
    private readonly ListBox list;
    private readonly TextBox details;

    public ErrorsPopup(IReadOnlyList<IdeError> errors)
    {
        Width = 720;
        list = new ListBox
        {
            Classes = { "tree" },
            Height = 150,
            ItemsSource = errors.Select(e => $"{e.At:HH:mm:ss}  {e.Kind}: {e.Message}").ToList(),
        };
        details = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true,
            Height = 220,
            FontFamily = new FontFamily("avares://Passenger.Editor/Fonts#JetBrains Mono"),
            FontSize = 11.5,
        };
        list.SelectionChanged += (_, _) => details.Text = list.SelectedIndex >= 0 ? errors[list.SelectedIndex].Details : string.Empty;
        list.SelectedIndex = errors.Count - 1;

        Button clear = new() { Content = "Clear All" };
        clear.Click += (_, _) =>
        {
            IdeErrors.Clear();
            Done?.Invoke(this, EventArgs.Empty);
        };
        Button log = new() { Content = OperatingSystem.IsMacOS() ? "Show Log in Finder" : "Show Log in Explorer" };
        log.Click += (_, _) => ShowLog();
        Button close = new() { Content = "Close", Classes = { "primary" }, IsDefault = true };
        close.Click += (_, _) => Done?.Invoke(this, EventArgs.Empty);
        Content = new Border
        {
            Classes = { "popup" },
            Padding = new Thickness(14, 12),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Classes = { "paneTitle" }, Text = "IDE Internal Errors" },
                    new TextBlock
                    {
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                        Text = "Passenger caught these and kept running. Each is in the log with its details.",
                    },
                    list,
                    details,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { log, clear, close },
                    },
                },
            },
        };
    }

    /// <summary>Raised when the popup is done with, by Close or Clear All.</summary>
    public event EventHandler? Done;

    /// <summary>Help, Show Log in Finder (Explorer on Windows): the log selected in the logs folder.</summary>
    public static void ShowLog()
    {
        Directory.CreateDirectory(IdeErrors.LogFolder);
        string path = File.Exists(IdeErrors.LogPath) ? IdeErrors.LogPath : IdeErrors.LogFolder;
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", ["-R", path])?.Dispose();
            }
            else if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();
            }
            else
            {
                System.Diagnostics.Process.Start("xdg-open", [IdeErrors.LogFolder])?.Dispose();
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
