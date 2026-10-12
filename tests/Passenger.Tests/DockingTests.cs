using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Docking;

namespace Passenger.Tests;

/// <summary>Tool windows as IntelliJ docks them: a stripe button shows its window and hides the one in its place,
/// a button or a header dragged elsewhere moves the window there, and the person's window opens on the layout
/// they left.</summary>
public sealed class DockingTests
{
    [AvaloniaFact]
    public void A_stripe_button_shows_its_window_in_its_place_and_hides_the_one_there()
    {
        MainWindow window = Open();
        Click(window, Button(window, "debug"));
        Assert.True(window.DockHost.IsShown("debug"));
        Click(window, Button(window, "run"));
        Assert.True(window.DockHost.IsShown("run"));
        Assert.False(window.DockHost.IsShown("debug"));
        Assert.True(Button(window, "run").IsChecked);
        Assert.False(Button(window, "debug").IsChecked);

        // Its button again closes it.
        Click(window, Button(window, "run"));
        Assert.False(window.DockHost.IsShown("run"));
        window.Close();
    }

    [AvaloniaFact]
    public void A_stripe_button_dragged_to_the_other_stripe_docks_its_window_on_that_side()
    {
        MainWindow window = Open();
        window.DockHost.Show("debug");
        Ui.Settle();
        ToggleButton debug = Button(window, "debug");
        Point from = Middle(window, debug);
        Point to = new(window.Bounds.Width - 20, 120);
        Drag(window, from, to);
        ToolWindow moved = window.DockHost["debug"]!;
        Assert.Equal(ToolAnchor.RightTop, moved.Anchor);
        // A shown window stays shown where it went, beside the editor and no longer under it.
        Assert.True(moved.IsShown);
        Assert.True(window.DockHost.ColumnDefinitions[5].ActualWidth > 100);
        Assert.Equal(0, window.DockHost.RowDefinitions[2].ActualHeight);
        Assert.Contains(Button(window, "debug"), Stripe(window, right: true));
        window.Close();
    }

    [AvaloniaFact]
    public void A_drop_above_the_first_lower_button_of_a_stripe_with_no_upper_ones_docks_in_its_upper_group()
    {
        MainWindow window = Open();
        DockHost dock = window.DockHost;
        dock.Move(dock["hierarchy"]!, ToolAnchor.RightBottom);
        Ui.Settle();
        Assert.Single(Stripe(window, right: true));
        Point lowest = Middle(window, Button(window, "hierarchy"));

        Drag(window, Middle(window, Button(window, "debug")), lowest - new Point(0, 4));
        Assert.Equal(ToolAnchor.RightTop, dock["debug"]!.Anchor);

        // Below the middle of the lower group's first button, a drop joins that group.
        dock.Move(dock["debug"]!, ToolAnchor.BottomLeft);
        Ui.Settle();
        Drag(window, Middle(window, Button(window, "run")), Middle(window, Button(window, "hierarchy")) + new Point(0, 4));
        Assert.Equal(ToolAnchor.RightBottom, dock["run"]!.Anchor);
        window.Close();
    }

    [AvaloniaFact]
    public void A_header_dragged_onto_the_bottom_right_docks_the_window_there_beside_the_bottom_left_one()
    {
        MainWindow window = Open();
        window.DockHost.Show("run");
        Ui.Settle();
        Control header = Header(window, "Explorer");
        Point from = Middle(window, header);
        Point to = new(window.Bounds.Width * 0.8, window.Bounds.Height * 0.85);
        Drag(window, from, to);
        Assert.Equal(ToolAnchor.BottomRight, window.DockHost["explorer"]!.Anchor);
        Assert.True(window.DockHost.IsShown("explorer"));
        Assert.True(window.DockHost.IsShown("run"));
        Assert.Equal(0, window.DockHost.ColumnDefinitions[1].ActualWidth);
        window.Close();
    }

    [AvaloniaFact]
    public void The_persons_window_opens_on_the_layout_they_left()
    {
        string settings = Directory.CreateTempSubdirectory("passenger-settings-").FullName;
        string? before = Environment.GetEnvironmentVariable("PASSENGER_SETTINGS");
        Environment.SetEnvironmentVariable("PASSENGER_SETTINGS", settings);
        try
        {
            MainWindow first = Open();
            first.KeepLayout();
            first.DockHost.Move(first.DockHost["tests"]!, ToolAnchor.RightBottom);
            first.DockHost.Show("tests");
            first.DockHost.Hide("explorer");
            first.Close();

            MainWindow second = Open();
            second.KeepLayout();
            Ui.Settle();
            Assert.Equal(ToolAnchor.RightBottom, second.DockHost["tests"]!.Anchor);
            Assert.True(second.DockHost.IsShown("tests"));
            Assert.False(second.DockHost.IsShown("explorer"));
            second.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PASSENGER_SETTINGS", before);
            TestFolders.Delete(settings);
        }
    }

    [AvaloniaFact]
    public void A_floating_tool_window_takes_its_content_to_a_window_of_its_own_and_back()
    {
        MainWindow window = Open();
        DockHost dock = window.DockHost;
        dock.Show("debug");
        ToolWindow debug = dock["debug"]!;
        dock.SetMode(debug, ToolMode.Floating);
        Ui.Settle();
        Window floating = Assert.Single(window.OwnedWindows);
        Assert.Contains(window.BottomPanel.Debug, floating.GetVisualDescendants());
        Assert.Equal(0, dock.RowDefinitions[2].ActualHeight);
        Assert.True(debug.IsShown);

        // Its stripe button hides it, closing its window; again, and it floats again.
        dock.Toggle("debug");
        Assert.Empty(window.OwnedWindows);
        dock.Toggle("debug");
        Assert.Single(window.OwnedWindows);

        // Closed by the system, it is hidden, not lost; docked again, it is in its place.
        window.OwnedWindows[0].Close();
        Ui.Settle();
        Assert.False(debug.IsShown);
        dock.SetMode(debug, ToolMode.Docked);
        dock.Show("debug");
        Ui.Settle();
        Assert.True(dock.RowDefinitions[2].ActualHeight > 100);
        Assert.Contains(window.BottomPanel.Debug, dock.GetVisualDescendants());
        window.Close();
    }

    [AvaloniaFact]
    public void Maximize_gives_a_tool_window_the_frame_and_hide_all_clears_it_for_the_editor()
    {
        MainWindow window = Open();
        DockHost dock = window.DockHost;
        dock.Show("debug");
        Ui.Settle();
        double before = dock.RowDefinitions[2].ActualHeight;
        Ui.Run(window, AppActions.MaximizeToolWindow);
        Assert.True(dock.RowDefinitions[2].ActualHeight > window.Bounds.Height * 0.75);
        Ui.Run(window, AppActions.MaximizeToolWindow);
        Assert.Equal(before, dock.RowDefinitions[2].ActualHeight, 1);

        // Hide All closes every docked window; again, and they are all back.
        Ui.Run(window, AppActions.HideAllToolWindows);
        Assert.DoesNotContain(dock.Windows, w => w.IsShown);
        Ui.Run(window, AppActions.HideAllToolWindows);
        Assert.True(dock.IsShown("debug"));
        Assert.True(dock.IsShown("explorer"));
        window.Close();
    }

    private static MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        Ui.Settle();
        return window;
    }

    private static ToggleButton Button(MainWindow window, string id) =>
        window.DockHost.GetVisualDescendants().OfType<ToggleButton>().First(b => Equals(b.Tag, id));

    private static IEnumerable<ToggleButton> Stripe(MainWindow window, bool right) =>
        window.DockHost.GetVisualDescendants().OfType<ToggleButton>()
            .Where(b => b.Tag is string && (Middle(window, b).X > window.Bounds.Width / 2) == right);

    /// <summary>A tool window's header, by the title on it.</summary>
    private static Control Header(MainWindow window, string title) =>
        window.DockHost.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == title && t.FontWeight == Avalonia.Media.FontWeight.SemiBold)
            .FindAncestorOfType<Border>()!;

    private static Point Middle(MainWindow window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void Click(MainWindow window, Control control)
    {
        Point at = Middle(window, control);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
    }

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
