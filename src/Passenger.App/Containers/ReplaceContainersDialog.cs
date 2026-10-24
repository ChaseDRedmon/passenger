using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Passenger.App.Containers;

/// <summary>The question a container run asks when its Compose project has containers of the services it would start that
/// this Passenger did not make (the person's own <c>docker compose up</c>'s): Compose would make them again with Passenger's
/// labels and settings, and Stop would remove them. Replace Them (the default) lets it, after which they are the run's;
/// Cancel runs nothing and leaves them as they are, as Escape and closing do.</summary>
public sealed class ReplaceContainersDialog : Window
{
    public const string Replace = "Replace Them";

    public ReplaceContainersDialog(string configuration, string project, IReadOnlyList<string> containers)
    {
        Title = containers.Count == 1 ? "A container Passenger did not make" : "Containers Passenger did not make";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        ReplaceButton = new Button { Content = Replace, IsDefault = true, Classes = { "primary" } };
        CancelButton = new Button { Content = "Cancel", IsCancel = true };
        ReplaceButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);

        string named = string.Join(", ", containers);
        Content = DialogFooter.Page(new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock
                {
                    Text = $"{configuration} would replace {named} of the Compose project {project}: Compose makes "
                        + $"{(containers.Count == 1 ? "it" : "them")} again with Passenger's settings for this run.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"Replace Them runs it, and {(containers.Count == 1 ? "the container is" : "the containers are")} then the run's: Stop removes "
                        + $"{(containers.Count == 1 ? "it" : "them")}. Cancel runs nothing and leaves {(containers.Count == 1 ? "it" : "them")} as "
                        + $"{(containers.Count == 1 ? "it is" : "they are")}.",
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        }, DialogFooter.Bar(DialogFooter.Answers(ReplaceButton, CancelButton)));
    }

    public Button ReplaceButton { get; }

    public Button CancelButton { get; }
}
