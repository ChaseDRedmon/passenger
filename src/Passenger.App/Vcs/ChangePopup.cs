using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Passenger.App.Vcs;

/// <summary>What a click on a change bar shows, as Rider's does: the lines as the last commit had them, and the
/// change's actions (previous and next change, Rollback, Show Diff).</summary>
public sealed class ChangePopup : UserControl
{
    public ChangePopup(string? before, Action previous, Action next, Action rollback, Action diff)
    {
        Before = before;
        StackPanel tools = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children =
            {
                Tool("Icon.ArrowUp", "Previous Change", previous),
                Tool("Icon.ArrowDown", "Next Change", next),
                Tool("Icon.Rollback", "Rollback", rollback),
                Tool("Icon.Diff", "Show Diff", diff),
            },
        };
        StackPanel content = new() { Spacing = 6, Children = { tools } };
        if (before is { Length: > 0 })
        {
            TextBlock old = new() { Text = before, FontFamily = CSharp.Fonts.Mono, FontSize = 12, Padding = new Thickness(8, 6) };
            Border text = new() { CornerRadius = new CornerRadius(4), Child = new ScrollViewer { MaxHeight = 260, MaxWidth = 640, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = old } };
            text.Bind(Border.BackgroundProperty, text.GetResourceObservable("Passenger.Diff.Modified"));
            content.Children.Add(text);
        }
        Content = new Border { Classes = { "popup" }, Padding = new Thickness(6), Child = content };
    }

    /// <summary>The lines the last commit had there, or null for lines that are new.</summary>
    public string? Before { get; }

    private static Button Tool(string icon, string tip, Action run)
    {
        Button button = new() { Classes = { "tool" }, Content = new PathIcon() };
        ToolTip.SetTip(button, tip);
        ((PathIcon)button.Content).Bind(PathIcon.DataProperty, button.GetResourceObservable(icon));
        button.Click += (_, _) => run();
        return button;
    }
}
