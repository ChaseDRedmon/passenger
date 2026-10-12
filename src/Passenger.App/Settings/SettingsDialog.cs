using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Passenger.Editor;

namespace Passenger.App.Settings;

/// <summary>Rider's Settings: the pages down the left under a search box, the chosen page's options on the right,
/// OK, Cancel and Apply below. A search keeps the pages with an option whose words match and shows only those options,
/// as Rider's search narrows the tree. The options edit a copy, which Apply and OK hand back.</summary>
public sealed class SettingsDialog : Window
{
    public const string Appearance = "Appearance";
    public const string EditorPage = "Editor";
    public const string TerminalPage = "Terminal";
    public const string KeymapPage = "Keymap";

    private static readonly string[] Order = [Appearance, EditorPage, TerminalPage, KeymapPage];

    private readonly Action<AppSettings> apply;
    private readonly List<Option> options = [];
    private readonly TextBox search;
    private readonly ListBox pages;
    private readonly TextBlock heading;
    private readonly StackPanel body;
    private readonly TextBlock nothing;

    public SettingsDialog(AppSettings current, Keymap keymap, Action<AppSettings> apply)
    {
        this.apply = apply;
        Working = current;
        Title = "Settings";
        Width = 860;
        Height = 600;
        MinWidth = 640;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Bind(BackgroundProperty, this.GetResourceObservable("Passenger.Chrome"));

        AddOptions(keymap);
        search = new TextBox { Classes = { "search" }, PlaceholderText = "Search settings", Margin = new Thickness(10, 10, 10, 6) };
        search.TextChanged += (_, _) => Filter();
        pages = new ListBox { Classes = { "tree" } };
        pages.SelectionChanged += (_, _) => ShowPage();
        heading = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 14) };
        body = new StackPanel { Spacing = 12 };
        nothing = new TextBlock { Classes = { "muted" }, Text = "Nothing matches", IsVisible = false, Margin = new Thickness(12) };

        Button ok = new() { Content = "OK", IsDefault = true, Classes = { "primary" }, MinWidth = 80 };
        Button cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        Button applyButton = new() { Content = "Apply", MinWidth = 80 };
        ok.Click += (_, _) => Ok();
        cancel.Click += (_, _) => Close(false);
        applyButton.Click += (_, _) => apply(Working);

        DockPanel left = new() { Width = 240 };
        DockPanel.SetDock(search, Dock.Top);
        left.Children.Add(search);
        left.Children.Add(new Panel { Children = { pages, nothing } });
        Border leftPane = new() { BorderThickness = new Thickness(0, 0, 1, 0), Child = left };
        leftPane.Bind(Border.BorderBrushProperty, leftPane.GetResourceObservable("Passenger.Divider"));
        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(16, 10),
            Children = { cancel, applyButton, ok },
        };
        Border bottom = new() { BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons };
        bottom.Bind(Border.BorderBrushProperty, bottom.GetResourceObservable("Passenger.Divider"));
        DockPanel right = new() { Margin = new Thickness(20, 16, 20, 0) };
        DockPanel.SetDock(heading, Dock.Top);
        right.Children.Add(heading);
        right.Children.Add(new ScrollViewer { Content = body });
        Border editorPane = new() { Child = right };
        editorPane.Bind(Border.BackgroundProperty, editorPane.GetResourceObservable("Passenger.Editor.Background"));

        DockPanel root = new();
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(leftPane, Dock.Left);
        root.Children.Add(bottom);
        root.Children.Add(leftPane);
        root.Children.Add(editorPane);
        Content = root;
        Filter();
        Opened += (_, _) => Dispatcher.UIThread.Post(() => search.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>The settings as the options now stand.</summary>
    public AppSettings Working { get; private set; }

    public string Query
    {
        get => search.Text ?? string.Empty;
        set => search.Text = value;
    }

    /// <summary>The pages listed, after the search.</summary>
    public IReadOnlyList<string> Pages => pages.ItemsSource as IReadOnlyList<string> ?? [];

    public string? Page
    {
        get => pages.SelectedItem as string;
        set => pages.SelectedItem = value;
    }

    /// <summary>The options the chosen page shows now, by their labels.</summary>
    public IReadOnlyList<string> Shown => [.. options.Where(o => o.Page == Page && o.Row.IsVisible).Select(o => o.Label)];

    /// <summary>OK: the settings applied, and the dialog closed.</summary>
    public void Ok()
    {
        apply(Working);
        Close(true);
    }

    /// <summary>Changes one setting, as its option does.</summary>
    public void Change(Func<AppSettings, AppSettings> change) => Working = change(Working);

    private void Filter()
    {
        string[] words = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (Option option in options)
        {
            option.Row.IsVisible = words.All(w => option.Words.Contains(w, StringComparison.OrdinalIgnoreCase));
        }
        string? was = Page;
        string[] listed = [.. Order.Where(p => options.Any(o => o.Page == p && o.Row.IsVisible))];
        pages.ItemsSource = listed;
        nothing.IsVisible = listed.Length == 0;
        pages.SelectedItem = listed.Contains(was) ? was : listed.FirstOrDefault();
        ShowPage();
    }

    private void ShowPage()
    {
        body.Children.Clear();
        heading.Text = Page ?? string.Empty;
        foreach (Option option in options.Where(o => o.Page == Page))
        {
            body.Children.Add(option.Row);
        }
    }

    private void AddOptions(Keymap keymap)
    {
        ComboBox theme = new() { ItemsSource = new[] { "System", "Dark", "Light" }, SelectedIndex = Working.Theme switch { ThemeChoice.Dark => 1, ThemeChoice.Light => 2, _ => 0 }, Width = 160 };
        theme.SelectionChanged += (_, _) => Change(s => s with { Theme = theme.SelectedIndex switch { 1 => ThemeChoice.Dark, 2 => ThemeChoice.Light, _ => ThemeChoice.System } });
        Add(Appearance, "Theme", theme, "colour color dark light");

        Add(EditorPage, "Font size", Number(Working.EditorFontSize, 6, 48, 1, v => Change(s => s with { EditorFontSize = v })), "font size text");
        Add(EditorPage, "Line spacing", Number(Working.EditorLineSpacing, 0.8, 3, 0.1, v => Change(s => s with { EditorLineSpacing = v })), "font line height");
        Add(EditorPage, "Enable ligatures", Check(Working.Ligatures, v => Change(s => s with { Ligatures = v })), "font arrows");
        Add(EditorPage, "Show line numbers", Check(Working.ShowLineNumbers, v => Change(s => s with { ShowLineNumbers = v })), "gutter appearance");

        Add(TerminalPage, "Font size", Number(Working.TerminalFontSize, 6, 48, 1, v => Change(s => s with { TerminalFontSize = v })), "font size shell");
        (string shell, _) = Passenger.Terminal.TerminalSession.DefaultShell();
        TextBox path = new() { Classes = { "search" }, Text = Working.TerminalShell ?? string.Empty, PlaceholderText = shell, Width = 320 };
        path.TextChanged += (_, _) => Change(s => s with { TerminalShell = string.IsNullOrWhiteSpace(path.Text) ? null : path.Text.Trim() });
        Add(TerminalPage, "Shell path", path, "shell zsh bash powershell program");

        foreach (ActionInfo action in ActionCatalog.All(keymap).OrderBy(a => a.Title, StringComparer.Ordinal))
        {
            TextBlock keys = new() { Text = action.Shortcut ?? "No shortcut", VerticalAlignment = VerticalAlignment.Center };
            keys.Bind(TextBlock.ForegroundProperty, keys.GetResourceObservable("Passenger.TextMuted"));
            Add(KeymapPage, action.Title, keys, $"{action.Group} {action.Shortcut} keymap shortcut");
        }
    }

    private void Add(string page, string label, Control editor, string keywords)
    {
        TextBlock name = new() { Text = label, Width = 220, VerticalAlignment = VerticalAlignment.Center };
        StackPanel row = new() { Orientation = Orientation.Horizontal, Children = { name, editor } };
        options.Add(new Option(page, label, row, $"{page} {label} {keywords}"));
    }

    private static NumericUpDown Number(double value, double min, double max, double step, Action<double> changed)
    {
        NumericUpDown number = new() { Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)step, Width = 140, FormatString = step < 1 ? "0.0" : "0" };
        number.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } next)
            {
                changed((double)next);
            }
        };
        return number;
    }

    private static CheckBox Check(bool value, Action<bool> changed)
    {
        CheckBox box = new() { IsChecked = value };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    private sealed record Option(string Page, string Label, Control Row, string Words);
}
