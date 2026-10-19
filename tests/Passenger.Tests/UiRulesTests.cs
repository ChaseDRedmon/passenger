using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Passenger.App;

namespace Passenger.Tests;

/// <summary>Rules the whole window keeps, each decided once in a shared piece (#213) and held here across what the tests
/// open, as <see cref="DialogButtonsTests.Check"/> holds every dialog's buttons: a menu's current value checked and still
/// enabled (MenuChoice); every tool window's tabs one look (ToolTabs, Shell.axaml's panelTab); every popup's width from
/// one scale, its title one size and its rows one height (PopupFrame); a menu opening against its widget; Rider's
/// scrollbars, thin and arrowless; the open editor tab's ×; Rider's focus ring; and the window at half a 1366-wide screen
/// with nothing cut that Rider would not cut, a stripe putting what has no room behind More.</summary>
public sealed class UiRulesTests : IDisposable
{
    private readonly string folder = TestFolders.Create("ui-rules-");

    public UiRulesTests()
    {
        File.WriteAllText(Path.Combine(folder, "Hello.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(folder, "Program.cs"),
            string.Concat(Enumerable.Range(1, 120).Select(i => $"// Line {i} of a file long enough to scroll, and wide enough to scroll sideways too: {new string('x', 160)}\n")));
        File.WriteAllText(Path.Combine(folder, "Notes.txt"), "one\ntwo\n");
        TestGit.Init(folder);
        TestGit.Run(folder, "branch", "other");
    }

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void A_menus_current_value_is_checked_with_Rider_s_tick_and_can_still_be_chosen()
    {
        MainWindow window = Open(1200, 800);
        try
        {
            Ui.WaitFor(() => window.Configurations.Any(c => c.Name == "Hello"), "the project's run configuration");
            window.OpenFile(Path.Combine(folder, "Notes.txt"));
            Ui.Settle();

            Current(window, "BranchButton", "main", "the branch menu");
            Current(window, "RunConfigButton", "Hello", "the run configuration menu");
            Current(window, "LineSeparatorButton", "LF - Unix and macOS (\\n)", "the line separator menu");
            Current(window, "EncodingButton", "UTF-8", "the encoding menu");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void The_open_editor_tab_shows_its_close_button_as_Rider_s_does_and_the_others_only_under_the_pointer()
    {
        MainWindow window = Open(1200, 800);
        try
        {
            window.OpenFile(Path.Combine(folder, "Program.cs"));
            window.OpenFile(Path.Combine(folder, "Notes.txt"));
            Ui.Settle();
            Border[] tabs = [.. window.EditorArea.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("tab") && b.IsEffectivelyVisible)];
            Assert.Equal(2, tabs.Length);
            foreach (Border tab in tabs)
            {
                bool open = tab.Classes.Contains("active");
                Button close = tab.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tabClose"));
                Assert.True(close.Opacity == (open ? 1 : 0),
                    $"The {(open ? "open" : "other")} tab's × has an opacity of {close.Opacity}, where Rider shows it on the open tab alone until the pointer is over another");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Every_tool_windows_tabs_have_one_look_the_open_one_underlined()
    {
        MainWindow window = Open(1200, 800);
        try
        {
            window.TerminalPanel.Shell = OperatingSystem.IsWindows() ? ("cmd.exe", []) : ("/bin/sh", []);
            window.OpenFile(Path.Combine(folder, "Program.cs"));
            Ui.Settle();
            Color accent = Brush(window, "Passenger.Accent");
            (string Where, double Height, double Size, Thickness Padding, Thickness Border)? first = null;
            int seen = 0;
            foreach (string action in (string[])[AppActions.ShowProblems, AppActions.ShowGit, AppActions.ShowDebug, AppActions.ShowTerminal, AppActions.ShowLocalHistory])
            {
                Assert.True(window.Perform(action), $"{action} did not run");
                Ui.Settle();
                if (action == AppActions.ShowTerminal)
                {
                    Ui.WaitFor(() => window.TerminalPanel.Active is { IsRunning: true }, "the shell to start");
                }
                Assert.False(ToolWindowParts(window).OfType<Border>().Any(b => b.Classes.Contains("tab")),
                    $"{action}'s window has an editor tab's look among its tabs, where every tool window's tab is a panelTab");
                // Read while the window is shown: one hidden leaves the tree, and its tabs their styles with it.
                foreach (ToggleButton tab in ToolWindowParts(window).OfType<ToggleButton>().Where(t => t.Classes.Contains("panelTab")))
                {
                    seen++;
                    string where = $"{action}'s tab '{Words(tab)}'";
                    first ??= (where, tab.Bounds.Height, tab.FontSize, tab.Padding, tab.BorderThickness);
                    Assert.True(tab.Bounds.Height == first.Value.Height && tab.FontSize == first.Value.Size && tab.Padding == first.Value.Padding
                        && tab.BorderThickness == first.Value.Border && tab.BorderThickness.Bottom > 0 && tab.CornerRadius == default,
                        $"{where} is {tab.Bounds.Height:0.#} px tall in {tab.FontSize} pt with padding {tab.Padding} and a border of {tab.BorderThickness}, "
                        + $"where {first.Value.Where} is {first.Value.Height:0.#} px in {first.Value.Size} pt: tool windows' tabs differ");
                    if (tab.IsChecked == true)
                    {
                        ContentPresenter frame = tab.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
                        Assert.True(frame.BorderBrush is ISolidColorBrush underline && underline.Color == accent,
                            $"{where} is open and not underlined in the accent, as Rider's tool window tabs are");
                    }
                }
            }
            Assert.True(seen >= 5, $"Only {seen} tool window tabs were found in Problems, Git, Debug and the Terminal");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Every_popup_takes_a_width_of_the_scale_one_title_size_and_rows_of_one_height()
    {
        MainWindow window = Open(1400, 900);
        try
        {
            window.OpenFile(Path.Combine(folder, "Program.cs"));
            Ui.Settle();
            List<(string What, Border Frame)> seen = [];
            void Popup(string what, Action open)
            {
                open();
                Ui.Settle();
                Ui.WaitFor(() => window.IsPopupOpen, what);
                Border frame = window.Popup!.GetSelfAndVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("popup"));
                Assert.True(PopupFrame.Widths.Any(w => Math.Abs(frame.Bounds.Width - w) < 0.5),
                    $"{what} is {frame.Bounds.Width:0.#} px wide, not one of the popups' widths ({string.Join(", ", PopupFrame.Widths)})");
                foreach (ListBoxItem row in frame.GetVisualDescendants().OfType<ListBoxItem>().Where(r => r.IsEffectivelyVisible))
                {
                    Assert.True(Math.Abs(row.Bounds.Height - PopupFrame.RowHeight) < 0.5,
                        $"A row of {what} is {row.Bounds.Height:0.#} px tall, where every popup's row is {PopupFrame.RowHeight}");
                }
                seen.Add((what, frame));
                Ui.Press(window, "ESCAPE");
            }
            Popup("Find Action", () => window.Perform(AppActions.FindAction));
            Popup("Go to File", () => window.Perform(AppActions.GoToFile));
            Popup("Go to Symbol", () => window.Perform(AppActions.GoToSymbol));
            Popup("Recent Files", () => window.ShowRecentFiles());
            Popup("Recent Locations", () => window.Perform(AppActions.RecentLocations));
            Popup("Find in Files", () => window.ShowFindInFiles(false));
            Popup("Stash Changes", () => window.Perform(AppActions.Stash));
            Popup("IDE Internal Errors", () => window.ShowErrors());

            TextBlock[] titles = [.. seen.SelectMany(s => s.Frame.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("popupTitle")))];
            Assert.True(titles.Length >= seen.Count - 2, $"Only {titles.Length} of {seen.Count} popups have a title of the popups' look");
            Assert.True(titles.All(t => t.FontSize == titles[0].FontSize && t.FontWeight == titles[0].FontWeight),
                "Popups' titles differ in size or weight: " + string.Join(", ", titles.Select(t => $"'{t.Text}' {t.FontSize} {t.FontWeight}")));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Every_scrollbar_is_Rider_s_thin_and_arrowless_and_a_sideways_one_shows_only_for_what_is_wider()
    {
        MainWindow window = Open(1200, 800);
        try
        {
            window.OpenFile(Path.Combine(folder, "Program.cs"));
            window.Perform(AppActions.ShowProblems);
            Ui.Settle();
            ScrollBar[] bars = [.. window.GetVisualDescendants().OfType<ScrollBar>().Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0)];
            Assert.Contains(bars, b => b.Orientation == Orientation.Vertical && b.FindAncestorOfType<Passenger.Editor.CodeEditor>() is not null);
            foreach (ScrollBar bar in bars)
            {
                string where = bar.FindAncestorOfType<Passenger.Editor.CodeEditor>() is not null ? "The editor's" : $"A {bar.Parent?.GetType().Name}'s";
                Assert.False(bar.GetVisualDescendants().OfType<RepeatButton>().Any(b => b.Name is "PART_LineUpButton" or "PART_LineDownButton"),
                    $"{where} {bar.Orientation} scrollbar has arrows, where Rider's has none");
                double across = bar.Orientation == Orientation.Vertical ? bar.Bounds.Width : bar.Bounds.Height;
                Assert.True(across <= 14, $"{where} {bar.Orientation} scrollbar is {across:0.#} px across, where Rider's are thin");
                Thumb thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
                double thumbAcross = bar.Orientation == Orientation.Vertical ? thumb.Bounds.Width : thumb.Bounds.Height;
                Assert.True(thumb.CornerRadius.TopLeft > 0 && thumbAcross < across, $"{where} {bar.Orientation} scrollbar's thumb is not Rider's thin rounded one");
                if (bar.Orientation == Orientation.Horizontal && bar.TemplatedParent is ScrollViewer viewer)
                {
                    Assert.True(viewer.Extent.Width > viewer.Viewport.Width, $"{where} sideways scrollbar shows for what is no wider than its view");
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void At_half_a_1366_wide_screen_the_window_cuts_nothing_and_a_stripe_too_short_puts_the_rest_behind_More()
    {
        MainWindow window = Open(683, 768);
        try
        {
            window.OpenFile(Path.Combine(folder, "Program.cs"));
            Ui.Settle();
            Assert.True(Math.Abs(window.Bounds.Width - 683) < 0.5, $"The window is {window.Bounds.Width:0.#} wide at half a 1366-wide screen: its least width is wider");
            Control end = Named<Control>(window, "ToolbarEnd");
            Control start = Named<Control>(window, "ToolbarStart");
            foreach (Button button in end.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible))
            {
                Rect at = Placed(window, button);
                Assert.True(at.Right <= window.Bounds.Width + 0.5, $"The toolbar's {ToolTip.GetTip(button)} is cut off at the window's right edge");
            }
            Assert.True(Placed(window, start).Right <= Placed(window, end).Left + 0.5,
                "The toolbar's project and branch widgets run under its run widget, where Rider's give way, ending in an ellipsis");
            TextBlock project = Named<TextBlock>(window, "ProjectName");
            Assert.True(project.TextTrimming == TextTrimming.CharacterEllipsis, "The project's name is cut rather than ending in an ellipsis");
            StripeButtonsFit(window);
            Assert.True(window.EditorArea.Bounds.Width > 200, $"The editor keeps {window.EditorArea.Bounds.Width:0.#} px beside the tool windows");

            // NuGet's search, feeds, Prerelease and Refresh stand inside its window, the search and the count giving way.
            Assert.True(window.Perform(AppActions.ShowNuGet), "NuGet did not show");
            Ui.Settle();
            CheckBox prerelease = window.DockHost.GetVisualDescendants().OfType<CheckBox>().First(c => c.Content as string == "Prerelease" && c.IsEffectivelyVisible);
            Rect nuget = Placed(window, prerelease.FindAncestorOfType<Passenger.App.ToolWindows.NuGetView>()!);
            foreach (Control part in ((Panel)prerelease.Parent!).Children.Where(c => c.IsVisible))
            {
                Rect at = Placed(window, part);
                Assert.True(at.Right <= nuget.Right + 0.5 && at.Width > 0, $"NuGet's {part.GetType().Name} is cut off at its window's right edge ({at.Right:0.#} past {nuget.Right:0.#})");
            }

            // What a tool window says while it has nothing to show wraps inside it, never running past its edges.
            foreach (string action in (string[])[AppActions.ShowTests, AppActions.ShowFind, AppActions.ShowDebug, AppActions.ShowProfiler, AppActions.ShowLocalHistory])
            {
                Assert.True(window.Perform(action), $"{action} did not run");
                Ui.Settle();
                foreach (TextBlock empty in window.DockHost.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("emptyText") && t.IsEffectivelyVisible))
                {
                    Rect text = Placed(window, empty);
                    Rect room = Placed(window, (Visual)empty.Parent!);
                    Assert.True(text.Left >= room.Left - 0.5 && text.Right <= room.Right + 0.5,
                        $"After {action}, '{empty.Text}' runs past its pane ({text.Left:0.#} to {text.Right:0.#} in {room.Left:0.#} to {room.Right:0.#})");
                }
            }

            // A window too short for every stripe button puts the rest behind More, which lists them.
            window.Height = 500;
            window.Width = MainWindow.SmallestWidth;
            Ui.Settle();
            StripeButtonsFit(window);
            ToggleButton more = window.DockHost.GetVisualDescendants().OfType<ToggleButton>().First(b => ToolTip.GetTip(b) as string == "More Tool Windows" && b.IsEffectivelyVisible);
            string[] hidden = [.. window.DockHost.GetVisualDescendants().OfType<ToggleButton>().Where(b => b.Tag is string && !b.IsVisible).Select(b => (string)b.Tag!)];
            Assert.NotEmpty(hidden);
            Point centre = more.TranslatePoint(new Point(more.Bounds.Width / 2, more.Bounds.Height / 2), window)!.Value;
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            Ui.Settle();
            ContextMenu menu = window.GetVisualDescendants().OfType<ContextMenu>().FirstOrDefault(m => m.IsOpen)
                ?? throw new Xunit.Sdk.XunitException("More opened no menu of the tool windows it holds");
            Assert.Equal(hidden.Length, menu.Items.Count);
            menu.Close();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Focus_is_Rider_s_rounded_ring_centred_on_what_has_the_keys_a_check_box_s_round_its_box_alone()
    {
        string other = TestFolders.Create("ui-rules-other-");
        MainWindow window = Open(1200, 800);
        try
        {
            window.Open(other);
            Ui.Settle();
            Window dialog = window.Asking ?? throw new Xunit.Sdk.XunitException("Opening another folder over one open did not ask where");
            bool sawBox = false;
            bool sawButton = false;
            for (int i = 0; i < 8 && !(sawBox && sawButton); i++)
            {
                Ui.Press(dialog, "TAB");
                if (dialog.FocusManager?.GetFocusedElement() is not Control focused || AdornerLayer.GetAdornerLayer(focused) is not { } layer)
                {
                    continue;
                }
                FocusRing ring = layer.Children.OfType<FocusRing>().SingleOrDefault(r => r.IsVisible)
                    ?? throw new Xunit.Sdk.XunitException($"{focused.GetType().Name} has the keys from Tab and no focus ring round it");
                (Rect edge, double radius) = ring.Surrounds();
                Point centre = ring.TranslatePoint(edge.Center, dialog)!.Value;
                if (focused is CheckBox box)
                {
                    Control square = box.GetVisualDescendants().OfType<Control>().First(c => c.Name == "NormalRectangle");
                    Point boxCentre = square.TranslatePoint(new Point(square.Bounds.Width / 2, square.Bounds.Height / 2), dialog)!.Value;
                    Assert.True(Math.Abs(edge.Width - square.Bounds.Width) < 0.5 && Off(centre, boxCentre) < 0.5,
                        $"The check box's focus ring surrounds {edge.Width:0.#} px centred {Off(centre, boxCentre):0.#} px off its box, where Rider's rings the box alone");
                    sawBox = true;
                }
                else if (focused is Button button)
                {
                    Point buttonCentre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), dialog)!.Value;
                    Assert.True(Math.Abs(edge.Width - button.Bounds.Width) < 0.5 && Off(centre, buttonCentre) < 0.5 && radius == button.CornerRadius.TopLeft && radius > 0,
                        $"'{button.Content}''s focus ring is not centred on it with its corners, as Rider's rounded ring is");
                    sawButton = true;
                }
            }
            Assert.True(sawBox && sawButton, "Tab never reached the dialog's check box and a button");
            dialog.Close();
            Ui.Settle();
        }
        finally
        {
            window.Close();
            TestFolders.Delete(other);
        }
    }

    /// <summary>Each stripe button shown lies wholly inside its stripe, above the status bar.</summary>
    private static void StripeButtonsFit(MainWindow window)
    {
        Rect dock = Placed(window, window.DockHost);
        foreach (ToggleButton button in window.DockHost.GetVisualDescendants().OfType<ToggleButton>().Where(b => b.Classes.Contains("stripe") && b.IsEffectivelyVisible))
        {
            Rect at = Placed(window, button);
            Assert.True(at.Top >= dock.Top - 0.5 && at.Bottom <= dock.Bottom + 0.5,
                $"The stripe's {ToolTip.GetTip(button)} button is cut off at {at.Bottom:0.#}, past the stripe's foot at {dock.Bottom:0.#}");
        }
    }

    /// <summary>Opens a status bar or toolbar widget's menu with a click and holds it to the rule: the current value
    /// checked and enabled, the others not checked.</summary>
    private static void Current(MainWindow window, string widget, string current, string what)
    {
        Button button = Named<Button>(window, widget);
        Point at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
        Ui.WaitFor(() => window.OpenContextMenu is not null, what);
        ContextMenu menu = window.OpenContextMenu!;
        MenuItem[] items = [.. menu.Items.OfType<MenuItem>()];
        MenuItem[] checkedOnes = [.. items.Where(i => i.IsChecked)];
        Assert.True(checkedOnes.Length == 1 && Header(checkedOnes[0]) == current,
            $"In {what}, [{string.Join(", ", checkedOnes.Select(Header))}] are checked, where {current}, the one in force, alone is");
        Assert.True(checkedOnes[0].IsEnabled, $"In {what}, {current}, the one in force, is greyed where Rider checks it");
        Assert.True(checkedOnes[0].ToggleType is MenuItemToggleType.Radio or MenuItemToggleType.CheckBox, $"In {what}, {current} shows no tick");
        DropsFrom(button, menu, what);
        menu.Close();
        Ui.Settle();
    }

    /// <summary>A widget's menu opens against it, its frame's edge meeting the widget's and one of its sides in line with
    /// the widget's, wherever the room for its shadow put the menu's popup (MenuShadow).</summary>
    private static void DropsFrom(Control widget, ContextMenu menu, string what)
    {
        Border frame = menu.GetVisualDescendants().OfType<Border>().First(b => b.BoxShadow.Count > 0);
        PixelPoint w = widget.PointToScreen(default);
        PixelPoint f = frame.PointToScreen(default);
        double scale = TopLevel.GetTopLevel(widget)!.RenderScaling;
        Rect at = new(w.X / scale, w.Y / scale, widget.Bounds.Width, widget.Bounds.Height);
        Rect drawn = new(f.X / scale, f.Y / scale, frame.Bounds.Width, frame.Bounds.Height);
        bool meets = Math.Abs(drawn.Top - at.Bottom) <= 2 || Math.Abs(drawn.Bottom - at.Top) <= 2;
        bool lined = Math.Abs(drawn.Left - at.Left) <= 2 || Math.Abs(drawn.Right - at.Right) <= 2;
        Assert.True(meets && lined, $"{what}'s frame is drawn at {drawn}, not against its widget at {at}: the menu opens away from what opened it");
    }

    private static string? Header(MenuItem item) => item.Header switch
    {
        string words => words.Replace("__", "_", StringComparison.Ordinal),
        Control control => string.Concat(control.GetSelfAndLogicalDescendants().OfType<TextBlock>().Take(1).Select(t => t.Text)),
        _ => null,
    };

    private static IEnumerable<Visual> ToolWindowParts(MainWindow window) =>
        window.DockHost.GetVisualDescendants().Where(v => v.IsEffectivelyVisible && !window.EditorArea.IsVisualAncestorOf(v) && v != window.EditorArea);

    private static string Words(ToggleButton tab) => string.Join(" ", tab.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    private static Color Brush(MainWindow window, string key) =>
        window.TryFindResource(key, window.ActualThemeVariant, out object? found) && found is ISolidColorBrush brush ? brush.Color : default;

    private static T Named<T>(MainWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name) ?? throw new Xunit.Sdk.XunitException($"The window has no {name}");

    private static double Off(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static Rect Placed(Visual window, Visual control) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    private MainWindow Open(double width, double height)
    {
        MainWindow window = new() { Width = width, Height = height, Autosaves = false };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }
}
