using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Passenger.App.Editors;

/// <summary>"Save changes to X before closing?" with Save, Don't Save and Cancel, closing with the
/// choice. Escape cancels; Enter saves.</summary>
public sealed class SaveDialog : Window
{
    public SaveDialog(string name)
    {
        Title = "Passenger";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Bind(BackgroundProperty, this.GetResourceObservable("Passenger.Chrome"));

        Button save = new() { Content = "Save", IsDefault = true, Classes = { "primary" } };
        Button discard = new() { Content = "Don't Save" };
        Button cancel = new() { Content = "Cancel", IsCancel = true };
        save.Click += (_, _) => Close(SaveChoice.Save);
        discard.Click += (_, _) => Close(SaveChoice.Discard);
        cancel.Click += (_, _) => Close(SaveChoice.Cancel);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = $"Save changes to {name} before closing?", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Your changes will be lost if you don't save them.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { discard, cancel, save },
                },
            },
        };
    }
}
