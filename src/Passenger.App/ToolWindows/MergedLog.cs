using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Passenger.App.ToolWindows;

/// <summary>A console of several sources' lines merged, a Compose project's services' and an Aspire AppHost's resources'
/// alike: a toggle per source whose lines it shows, the editor's own Find over it (its button, and Find's keys while the
/// window has them: case, whole words, regular expressions, every match marked), and the log's own words or toggles at the
/// bar's right (a container's Timestamps). Its owner streams what the toggles choose, again as one changes.</summary>
public sealed class MergedLog : DockPanel
{
    private readonly StackPanel sources = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly HashSet<string> hidden = new(StringComparer.Ordinal);

    /// <param name="findName">The Find button's name, as a test finds it.</param>
    /// <param name="own">What stands after Find at the bar's right.</param>
    public MergedLog(string findName, params Control[] own)
    {
        Console = new OutputView(toolbar: false);
        Find = new Editors.FindBar { [DockProperty] = Dock.Top };
        Find.Attach(Console.Editor);
        Button finding = ToolButtons.Of("Icon.Search", "Find in the log", OpenFind);
        finding.Name = findName;
        finding.VerticalAlignment = VerticalAlignment.Center;
        StackPanel right = new() { Orientation = Orientation.Horizontal, Spacing = 4, [DockProperty] = Dock.Right, Children = { finding } };
        foreach (Control control in own)
        {
            right.Children.Add(control);
        }
        DockPanel bar = new() { [DockProperty] = Dock.Top, Height = 32, Margin = new Thickness(8, 0) };
        bar.Children.Add(right);
        bar.Children.Add(sources);
        Children.Add(bar);
        Children.Add(Find);
        Children.Add(Console);
    }

    /// <summary>The console the sources' lines stream into.</summary>
    public OutputView Console { get; }

    /// <summary>Find over the log, as the editor's.</summary>
    public Editors.FindBar Find { get; }

    /// <summary>Opens Find over the log.</summary>
    public void OpenFind() => Find.Open(replace: false);

    /// <summary>The sources' toggles, each on while its lines are shown.</summary>
    public IReadOnlyList<ToggleButton> Toggles => [.. sources.Children.OfType<ToggleButton>()];

    /// <summary>A toggle was turned: the owner streams what is shown now.</summary>
    public event EventHandler? Toggled;

    /// <summary>Whether a source's lines are shown (its toggle on).</summary>
    public bool Shows(string source) => !hidden.Contains(source);

    /// <summary>The sources offered, a toggle each (by key, in its words); one turned off before stays off unless
    /// <paramref name="forget"/> says the sources are another's.</summary>
    public void Offer(IReadOnlyList<(string Key, string Words)> offered, bool forget)
    {
        if (forget)
        {
            hidden.Clear();
        }
        sources.Children.Clear();
        foreach ((string key, string words) in offered)
        {
            ToggleButton toggle = new() { Classes = { "segment" }, Content = words, IsChecked = !hidden.Contains(key), VerticalAlignment = VerticalAlignment.Center };
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (toggle.IsChecked == true)
                {
                    hidden.Remove(key);
                }
                else
                {
                    hidden.Add(key);
                }
                Toggled?.Invoke(this, EventArgs.Empty);
            };
            sources.Children.Add(toggle);
        }
    }
}
