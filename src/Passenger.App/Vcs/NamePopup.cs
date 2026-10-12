using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace Passenger.App.Vcs;

/// <summary>Asks for a name, as Rider's Create New Branch does: Enter tries it, and when that fails the reason shows
/// under the box and the name stays to correct; Escape gives up.</summary>
public sealed class NamePopup : UserControl
{
    private readonly TextBox name;
    private readonly TextBlock problem;
    private readonly Func<string, Task<string?>> attempt;
    private bool trying;

    /// <param name="attempt">Tries the name: null when it worked, else why not.</param>
    public NamePopup(string title, string initial, Func<string, Task<string?>> attempt)
    {
        this.attempt = attempt;
        Width = 360;
        name = new TextBox { Classes = { "search" }, Text = initial };
        problem = new TextBlock { Classes = { "muted" }, FontSize = 12, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        Content = new Border
        {
            Classes = { "popup" },
            Padding = new Thickness(12, 10),
            Child = new StackPanel
            {
                Spacing = 8,
                Children = { new TextBlock { Classes = { "paneTitle" }, Text = title }, name, problem },
            },
        };
        name.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Dispatcher.UIThread.Post(() =>
        {
            name.Focus();
            name.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Raised once the name worked (true) or was given up (false).</summary>
    public event EventHandler<bool>? Done;

    public string Text
    {
        get => name.Text ?? string.Empty;
        set => name.Text = value;
    }

    /// <summary>Why the last name tried did not work, or null.</summary>
    public string? Problem => problem.IsVisible ? problem.Text : null;

    /// <summary>Tries the name typed, as Enter does; Enter again while it is being tried (a stash running) does nothing.</summary>
    public async Task SubmitAsync()
    {
        if (trying)
        {
            return;
        }
        string typed = Text.Trim();
        trying = true;
        string? failed;
        try
        {
            failed = typed.Length == 0 ? "A name is needed" : await attempt(typed);
        }
        finally
        {
            trying = false;
        }
        if (failed is null)
        {
            Done?.Invoke(this, true);
            return;
        }
        problem.Text = failed;
        problem.IsVisible = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Done?.Invoke(this, false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _ = SubmitAsync();
            e.Handled = true;
        }
    }
}
