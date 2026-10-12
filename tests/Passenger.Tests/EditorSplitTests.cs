using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Editors;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Split editors as Rider's: Split Right shows a file twice over one text, each split with its own caret; a
/// tab dragged to a split's edge splits there and onto another's strip moves into it; a split whose last tab goes
/// gives its room back.</summary>
public sealed class EditorSplitTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("passenger-splits-").FullName;

    public EditorSplitTests()
    {
        File.WriteAllText(Path.Combine(folder, "One.cs"), "class One\n{\n}\n");
        File.WriteAllText(Path.Combine(folder, "Two.cs"), "class Two\n{\n}\n");
    }

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void Split_right_shows_the_file_in_both_splits_over_one_text_each_with_its_own_caret()
    {
        MainWindow window = Open("One.cs");
        EditorArea area = window.EditorArea;
        EditorGroup left = area.ActiveGroup;
        left.Editor.GoTo(new TextPosition(2, 0));
        Assert.True(window.Perform(AppActions.SplitRight));
        Ui.Settle();
        Assert.Equal(2, area.Groups.Count);
        EditorGroup right = area.ActiveGroup;
        Assert.NotSame(left, right);
        Assert.Same(left.Active, right.Active);
        Assert.Single(area.Documents);

        // Typed in the right split, the text is in the left one too, whose caret stays where it was.
        right.Editor.GoTo(new TextPosition(0, 0));
        window.KeyTextInput("// ");
        Ui.Settle();
        Assert.StartsWith("// class One", left.Editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, left.Editor.CaretPosition.Line);
        Assert.Equal(0, right.Editor.CaretPosition.Line);

        // Closing it in one split keeps it open in the other, and the split it left goes.
        area.CloseTab(right, right.Active!);
        Ui.Settle();
        Assert.Single(area.Groups);
        Assert.Single(area.Documents);
        window.Close();
    }

    [AvaloniaFact]
    public void A_tab_dragged_to_its_splits_right_edge_splits_it_off_and_back_onto_the_first_strip_joins_it_again()
    {
        MainWindow window = Open("One.cs", "Two.cs");
        EditorArea area = window.EditorArea;
        EditorGroup first = area.ActiveGroup;
        Control two = Tab(first, "Two.cs");
        Point from = Middle(window, two);
        Point edge = first.Editor.TranslatePoint(new Point(first.Editor.Bounds.Width - 20, first.Editor.Bounds.Height / 2), window)!.Value;
        Drag(window, from, edge);
        Assert.Equal(2, area.Groups.Count);
        EditorGroup second = area.Groups[1];
        Assert.Equal(["Two.cs"], second.Tabs.Select(t => t.Document.Name));
        Assert.Equal(["One.cs"], first.Tabs.Select(t => t.Document.Name));
        // Side by side: the new split is to the right of the first.
        Assert.True(second.TranslatePoint(default, window)!.Value.X > first.TranslatePoint(default, window)!.Value.X + 100);

        // Back onto the first split's strip, after One.cs: one split again, with both tabs in order.
        Point back = Middle(window, Tab(second, "Two.cs"));
        Point strip = first.TabStrip.TranslatePoint(new Point(first.TabStrip.Bounds.Width - 30, first.TabStrip.Bounds.Height / 2), window)!.Value;
        Drag(window, back, strip);
        Assert.Single(area.Groups);
        Assert.Equal(["One.cs", "Two.cs"], area.Groups[0].Tabs.Select(t => t.Document.Name));
        window.Close();
    }

    [AvaloniaFact]
    public void The_divider_between_two_splits_dragged_gives_one_the_room_the_other_gives_up_beside_and_below()
    {
        MainWindow window = Open("One.cs");
        EditorArea area = window.EditorArea;
        Assert.True(window.Perform(AppActions.SplitRight));
        Assert.True(window.Perform(AppActions.SplitDown));
        Ui.Settle();
        Assert.Equal(3, area.Groups.Count);
        EditorGroup left = area.Groups[0];
        EditorGroup top = area.Groups[1];
        EditorGroup bottom = area.Groups[2];
        (double leftWidth, double rightWidth) = (left.Bounds.Width, top.Bounds.Width);
        (double topHeight, double bottomHeight) = (top.Bounds.Height, bottom.Bounds.Height);

        Point across = Middle(window, Divider(area, GridResizeDirection.Columns));
        Drag(window, across, across - new Point(120, 0));
        Assert.True(Math.Abs(left.Bounds.Width - (leftWidth - 120)) < 2 && Math.Abs(top.Bounds.Width - (rightWidth + 120)) < 2,
            $"The splits beside each other were {leftWidth} and {rightWidth} wide, and {left.Bounds.Width} and {top.Bounds.Width} once their divider was dragged 120 px left");

        Point down = Middle(window, Divider(area, GridResizeDirection.Rows));
        Drag(window, down, down + new Point(0, 80));
        Assert.True(Math.Abs(top.Bounds.Height - (topHeight + 80)) < 2 && Math.Abs(bottom.Bounds.Height - (bottomHeight - 80)) < 2,
            $"The splits one over the other were {topHeight} and {bottomHeight} tall, and {top.Bounds.Height} and {bottom.Bounds.Height} once their divider was dragged 80 px down");
        window.Close();
    }

    [AvaloniaFact]
    public void Find_action_finds_an_action_with_no_shortcut_by_name_and_runs_it()
    {
        MainWindow window = Open("One.cs");
        Ui.Run(window, AppActions.FindAction);
        Passenger.App.Navigation.ActionPopup popup = Assert.IsType<Passenger.App.Navigation.ActionPopup>(window.Popup);
        popup.Query = "split right";
        Ui.Settle();
        Assert.Equal(AppActions.SplitRight, popup.Rows[0].Id);
        Ui.Press(window, "ENTER");
        Assert.False(window.IsPopupOpen);
        Assert.Equal(2, window.EditorArea.Groups.Count);

        // Run once, it is the first thing offered next time.
        Ui.Run(window, AppActions.FindAction);
        Assert.Equal(AppActions.SplitRight, ((Passenger.App.Navigation.ActionPopup)window.Popup!).Rows[0].Id);
        Ui.Press(window, "ESCAPE");
        Assert.False(window.IsPopupOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void The_splits_their_tabs_and_carets_come_back_when_the_folder_opens_again_and_go_when_another_opens()
    {
        File.WriteAllText(Path.Combine(folder, "Three.cs"), "class Three\n{\n    int x;\n}\n");
        string settings = Directory.CreateTempSubdirectory("passenger-settings-").FullName;
        string? before = Environment.GetEnvironmentVariable("PASSENGER_SETTINGS");
        Environment.SetEnvironmentVariable("PASSENGER_SETTINGS", settings);
        try
        {
            MainWindow first = new() { Width = 1200, Height = 800 };
            first.Show();
            first.KeepLayout();
            first.OpenFolder(folder);
            first.OpenFile(Path.Combine(folder, "One.cs"));
            first.OpenFile(Path.Combine(folder, "Two.cs"));
            Ui.Settle();
            first.Perform(AppActions.SplitDown);
            first.OpenFile(Path.Combine(folder, "Three.cs"));
            first.EditorArea.CodeEditor.GoTo(new TextPosition(2, 8));
            Ui.Settle();
            first.Close();

            MainWindow second = new() { Width = 1200, Height = 800 };
            second.Show();
            second.KeepLayout();
            second.OpenFolder(folder);
            Ui.Settle();
            EditorArea area = second.EditorArea;
            Assert.Equal(2, area.Groups.Count);
            Assert.Equal(["One.cs", "Two.cs"], area.Groups[0].Tabs.Select(t => t.Document.Name));
            Assert.Equal("Two.cs", area.Groups[0].Active!.Name);
            Assert.Equal(["Two.cs", "Three.cs"], area.Groups[1].Tabs.Select(t => t.Document.Name));
            Assert.Same(area.Groups[1], area.ActiveGroup);
            Assert.Equal("Three.cs", area.Active!.Name);
            Assert.Equal(new TextPosition(2, 8), area.CodeEditor.CaretPosition);
            Assert.True(area.Groups[1].TranslatePoint(default, area)!.Value.Y > area.Groups[0].TranslatePoint(default, area)!.Value.Y,
                "Split Down came back as a split beside, not below");

            // Another folder has its own editors: this one's close, and come back with it.
            string other = Directory.CreateTempSubdirectory("passenger-other-").FullName;
            second.OpenFolder(other);
            Assert.Empty(area.Documents);
            second.OpenFolder(folder);
            Assert.Equal(3, area.Documents.Count);
            second.Close();
            TestFolders.Delete(other);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PASSENGER_SETTINGS", before);
            TestFolders.Delete(settings);
        }
    }

    private MainWindow Open(params string[] files)
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        foreach (string file in files)
        {
            window.OpenFile(Path.Combine(folder, file));
        }
        Ui.Settle();
        return window;
    }

    private static Control Tab(EditorGroup group, string name) =>
        group.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == name && t.Classes.Contains("tabName")).FindAncestorOfType<Border>()!;

    private static GridSplitter Divider(EditorArea area, GridResizeDirection direction) =>
        area.GetVisualDescendants().OfType<GridSplitter>().Single(s => s.ResizeDirection == direction);

    private static Point Middle(MainWindow window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void Drag(MainWindow window, Point from, Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        for (int step = 1; step <= 10; step++)
        {
            window.MouseMove(from + ((to - from) * (step / 10.0)), RawInputModifiers.LeftMouseButton);
        }
        window.MouseUp(to, MouseButton.Left);
        Ui.Settle();
    }
}
