using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;

namespace Passenger.App.ToolWindows;

/// <summary>The tool windows that dock at the bottom (Build, Run, Debug, Problems), one showing at a
/// time, chosen by the tabs across the top as Rider's are; the stripe buttons and Cmd+0, Cmd+4, Cmd+5,
/// Cmd+6 open them too, and Shift+Escape hides the panel.</summary>
public sealed class BottomPanel : DockPanel
{
    private readonly Dictionary<string, (ToggleButton Tab, Control View)> windows = [];
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };

    public BottomPanel()
    {
        Build = new OutputView();
        Run = new OutputView();
        Problems = new ProblemsView();
        Debug = new DebugView();
        Button hide = new() { Classes = { "tool" }, Content = new PathIcon() };
        ToolTip.SetTip(hide, "Hide");
        ((PathIcon)hide.Content).Bind(PathIcon.DataProperty, hide.GetResourceObservable("Icon.Close"));
        ((PathIcon)hide.Content).Classes.Add("small");
        hide.Click += (_, _) => HideRequested?.Invoke(this, EventArgs.Empty);
        Border header = new()
        {
            [DockProperty] = Dock.Top,
            Height = 32,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 0),
            Child = new DockPanel { Children = { new Panel { [DockProperty] = Dock.Right, Children = { hide } }, tabs } },
        };
        header.Bind(Border.BorderBrushProperty, header.GetResourceObservable("Passenger.Divider"));
        Children.Add(header);
        Panel views = new();
        Children.Add(views);
        foreach ((string name, Control view) in new (string, Control)[] { ("Build", Build), ("Run", Run), ("Debug", Debug), ("Problems", Problems) })
        {
            ToggleButton tab = new() { Content = name, Classes = { "panelTab" } };
            tab.Click += (_, _) => Show(name);
            tabs.Children.Add(tab);
            view.IsVisible = false;
            views.Children.Add(view);
            windows[name] = (tab, view);
        }
        Bind(BackgroundProperty, this.GetResourceObservable("Passenger.Chrome"));
        Show("Build");
    }

    public event EventHandler? HideRequested;

    public OutputView Build { get; }
    public OutputView Run { get; }
    public ProblemsView Problems { get; }
    public DebugView Debug { get; }

    public string Current { get; private set; } = "Build";

    public void Show(string name)
    {
        Current = name;
        foreach ((string key, (ToggleButton tab, Control view)) in windows)
        {
            tab.IsChecked = key == name;
            view.IsVisible = key == name;
        }
    }

    /// <summary>A tab's caption, for a count beside it ("Problems 3").</summary>
    public void Caption(string name, string caption) => windows[name].Tab.Content = caption;
}
