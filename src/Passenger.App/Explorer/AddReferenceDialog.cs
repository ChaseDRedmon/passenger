using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Passenger.App.Explorer;

/// <summary>A project the Add Reference dialog offers: checked while the project references it, and not offered when it
/// references the project itself, directly or through others (a reference back would be a cycle MSBuild refuses).</summary>
public sealed class ReferenceChoice(string path, string name, string where, bool chosen, bool circular) : Observable
{
    private bool isChosen = chosen;

    public string Path { get; } = path;

    public string Name { get; } = name;

    /// <summary>Where it is, and why it cannot be chosen when it cannot.</summary>
    public string Detail { get; } = circular ? $"{where} (references this project)" : where;

    public bool CanChoose { get; } = !circular;

    public bool IsChosen
    {
        get => isChosen;
        set => Set(ref isChosen, value && CanChoose);
    }
}

/// <summary>Rider's Add Reference for a project: the solution's other projects, each with a check box, those it references
/// already checked. Space checks or unchecks the row chosen, Enter is OK and Escape cancels; it closes with the projects
/// checked, or null when cancelled.</summary>
public sealed class AddReferenceDialog : Window
{
    private readonly ListBox list;

    public AddReferenceDialog(string project, IReadOnlyList<ReferenceChoice> choices)
    {
        Choices = choices;
        Title = "Add Reference";
        Width = 520;
        Height = 420;
        MinWidth = 360;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Bind(BackgroundProperty, this.GetResourceObservable("Passenger.Chrome"));

        list = new ListBox
        {
            Classes = { "tree" },
            ItemsSource = choices,
            ItemTemplate = new FuncDataTemplate<ReferenceChoice>((choice, _) => choice is null ? new Panel() : Row(choice), supportsRecycling: false),
        };
        list.SelectedItem = choices.FirstOrDefault(c => c.CanChoose);
        list.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        Border rows = new() { Classes = { "framed" }, Child = list };

        Button ok = new() { Content = "OK", IsDefault = true, Classes = { "primary" } };
        Button cancel = new() { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(null);
        StackPanel buttons = DialogFooter.Of(ok, cancel);
        buttons.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(buttons, Dock.Bottom);
        TextBlock heading = new()
        {
            [DockPanel.DockProperty] = Dock.Top,
            Text = $"Projects {project} references",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Content = new DockPanel { Margin = new Thickness(16), Children = { heading, buttons, rows } };
        Opened += (_, _) => Dispatcher.UIThread.Post(FocusChosen, DispatcherPriority.Loaded);
    }

    public IReadOnlyList<ReferenceChoice> Choices { get; }

    /// <summary>Closes as OK does, with the projects checked.</summary>
    public void Accept() => Close((IReadOnlyList<string>)[.. Choices.Where(c => c.IsChosen).Select(c => c.Path)]);

    /// <summary>The keys to the row chosen, as a list in a dialog takes them.</summary>
    private void FocusChosen()
    {
        if (list.SelectedIndex >= 0)
        {
            list.ScrollIntoView(list.SelectedIndex);
            list.UpdateLayout();
            list.ContainerFromIndex(list.SelectedIndex)?.Focus();
        }
        else
        {
            list.Focus();
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }
        if (e.Key == Key.Space && list.SelectedItem is ReferenceChoice { CanChoose: true } choice)
        {
            choice.IsChosen = !choice.IsChosen;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            // Ahead of the list, which would take it for its selection.
            Accept();
            e.Handled = true;
        }
    }

    private static Control Row(ReferenceChoice choice)
    {
        CheckBox check = new() { IsChecked = choice.IsChosen, IsEnabled = choice.CanChoose, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
        check.IsCheckedChanged += (_, _) => choice.IsChosen = check.IsChecked == true;
        choice.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReferenceChoice.IsChosen))
            {
                check.IsChecked = choice.IsChosen;
            }
        };
        TextBlock detail = new() { Classes = { "muted" }, Text = choice.Detail, FontSize = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 28,
            Margin = new Thickness(6, 0),
            Opacity = choice.CanChoose ? 1 : 0.55,
            Children = { check, new TextBlock { Text = choice.Name, VerticalAlignment = VerticalAlignment.Center }, detail },
        };
    }
}
