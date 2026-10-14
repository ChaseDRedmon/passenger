using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace Passenger.App;

/// <summary>Rider's IDE Internal Errors: the errors caught since the last Clear All, each once with how many times it
/// came, the chosen one's details under them, and the log a button away.</summary>
public sealed class ErrorsPopup : UserControl
{
    private readonly ListBox list;
    private readonly TextBlock occurred;
    private readonly TextBox details;

    /// <param name="unlisted">The errors that came once the list was full, which only the log has.</param>
    public ErrorsPopup(IReadOnlyList<IdeError> errors, int unlisted)
    {
        Width = 720;
        list = new ListBox
        {
            Classes = { "tree" },
            Height = 150,
            ItemsSource = errors,
            ItemTemplate = new FuncDataTemplate<IdeError>((error, _) => Row(error), supportsRecycling: false),
        };
        occurred = new TextBlock { Classes = { "muted" } };
        details = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true,
            Height = 220,
            FontFamily = new FontFamily("avares://Passenger.Editor/Fonts#JetBrains Mono"),
            FontSize = 11.5,
        };
        list.SelectionChanged += (_, _) =>
        {
            IdeError? chosen = list.SelectedItem as IdeError;
            details.Text = chosen?.Details ?? string.Empty;
            occurred.Text = chosen is null ? string.Empty : Occurred(chosen);
        };
        list.SelectedIndex = errors.Count - 1;

        Button clear = new() { Content = "Clear All" };
        clear.Click += (_, _) =>
        {
            IdeErrors.Clear();
            Done?.Invoke(this, EventArgs.Empty);
        };
        Button log = new() { Content = $"Show Log in {Core.ChildProcesses.FileManager}" };
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
                        Text = unlisted == 0
                            ? "Passenger caught these and kept running. Each is in the log with its details."
                            : $"Passenger caught these and kept running. Each is in the log with its details, as are {unlisted} more that came after the first {IdeErrors.Limit}.",
                    },
                    list,
                    occurred,
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

    /// <summary>An error's row: when it first came, its kind and first line, and how many times when more than once.</summary>
    private static Control Row(IdeError? error)
    {
        if (error is null)
        {
            return new TextBlock();
        }
        TextBlock count = new()
        {
            Classes = { "muted" },
            Text = error.Count == 1 ? string.Empty : $"{error.Count} times",
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            [DockPanel.DockProperty] = Dock.Right,
        };
        TextBlock what = new()
        {
            Text = $"{error.At:HH:mm:ss}  {error.Kind}: {error.Message.ReplaceLineEndings(" ")}",
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return new DockPanel { Children = { count, what } };
    }

    /// <summary>Rider's line over an error's details: how often it came, and when.</summary>
    private static string Occurred(IdeError error) => error.Count == 1
        ? $"Occurred once, at {error.At:HH:mm:ss}"
        : $"Occurred {error.Count} times, from {error.At:HH:mm:ss} to {error.Last:HH:mm:ss}";

    /// <summary>Help, Show Log in Finder (Explorer on Windows): the log selected in the logs folder.</summary>
    public static void ShowLog()
    {
        IdeErrors.Flush();
        Directory.CreateDirectory(IdeErrors.LogFolder);
        Core.ChildProcesses.Reveal(File.Exists(IdeErrors.LogPath) ? IdeErrors.LogPath : IdeErrors.LogFolder);
    }
}
