using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Passenger.App.Explorer;

/// <summary>Rider's File Nesting, from the Solution view's options: its check box, "Show files with the same names as
/// nested", over the nesting rules, each parent file suffix with the child file suffixes its files take under them
/// (Rider's own, shown greyed while the box is clear). OK hands the box back.</summary>
public sealed class FileNestingDialog : Window
{
    public FileNestingDialog(bool nest)
    {
        Title = "File Nesting";
        Width = 720;
        Height = 480;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Bind(BackgroundProperty, this.GetResourceObservable("Passenger.Chrome"));
        NestBox = new CheckBox { Content = "Show files with the same names as nested", IsChecked = nest };

        Grid table = new() { ColumnDefinitions = new ColumnDefinitions("150,*"), Margin = new Thickness(8, 4) };
        void Row(string parent, string children, bool heading = false)
        {
            int row = table.RowDefinitions.Count;
            table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            foreach ((string text, int column) in (IEnumerable<(string, int)>)[(parent, 0), (children, 1)])
            {
                TextBlock cell = new()
                {
                    Text = text,
                    Margin = new Thickness(0, 3, 12, 3),
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    FontWeight = heading ? FontWeight.SemiBold : FontWeight.Normal,
                    [Grid.RowProperty] = row,
                    [Grid.ColumnProperty] = column,
                };
                if (heading)
                {
                    cell.Classes.Add("muted");
                }
                table.Children.Add(cell);
            }
        }
        Row("Parent File Suffix", "Child File Suffix", heading: true);
        foreach (IGrouping<string, (string Parent, string Child)> rule in FileNesting.Defaults.GroupBy(r => r.Parent, StringComparer.OrdinalIgnoreCase))
        {
            Row(rule.Key, string.Join("; ", rule.Select(r => r.Child)));
        }
        Row(FileNesting.EnvironmentParent, FileNesting.EnvironmentChildren);
        Rules = table;
        Border framed = new() { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = new ScrollViewer { Content = table } };
        framed.Bind(Border.BorderBrushProperty, framed.GetResourceObservable("Passenger.Divider"));
        Rules.IsEnabled = nest;
        NestBox.IsCheckedChanged += (_, _) => Rules.IsEnabled = NestBox.IsChecked == true;

        DockPanel body = new() { Margin = new Thickness(16, 14, 16, 8) };
        TextBlock title = new() { Text = "Nesting rules:", Margin = new Thickness(0, 12, 0, 6) };
        DockPanel.SetDock(NestBox, Dock.Top);
        DockPanel.SetDock(title, Dock.Top);
        body.Children.Add(NestBox);
        body.Children.Add(title);
        body.Children.Add(framed);

        OkButton = new Button { Content = "OK", IsDefault = true, Classes = { "primary" }, MinWidth = 80 };
        Button cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        OkButton.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(16, 10), Children = { cancel, OkButton } };
        Border bottom = new() { BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons };
        bottom.Bind(Border.BorderBrushProperty, bottom.GetResourceObservable("Passenger.Divider"));
        DockPanel root = new();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(body);
        Border page = new() { Child = root };
        page.Bind(Border.BackgroundProperty, page.GetResourceObservable("Passenger.Editor.Background"));
        Content = page;
    }

    /// <summary>Rider's "Show files with the same names as nested".</summary>
    public CheckBox NestBox { get; }

    /// <summary>The rules, shown greyed while the box is clear.</summary>
    public Control Rules { get; }

    public Button OkButton { get; }

    public bool Nest => NestBox.IsChecked == true;
}
