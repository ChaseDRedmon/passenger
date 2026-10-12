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

    public void Dispose() => Directory.Delete(folder, recursive: true);

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
        _ = area.CloseTab(right, right.Active!);
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
