using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Debugging;
using Passenger.App.LiveTree;
using Passenger.Core;
using Passenger.Dap;
using Passenger.Editor;
using Passenger.Inspector;
using Passenger.Text;
using Debuggees = Passenger.Tests.Debugging.Helpers.Debuggees;

namespace Passenger.Tests;

/// <summary>Breakpoints in an Avalonia app's XAML and on its controls' properties, debugged from the window:
/// tests/Debuggees/XamlBreakpointsApp, written into a folder of its own with a project file on the tests' own Avalonia,
/// headless, its standard input ("paint", "warn", "rename", typed into the Debug window's console) having its code-behind
/// change its controls as something a person did would. Debugged through the window, so run alone.</summary>
[Collection(Alone.Name)]
public sealed class XamlBreakpointsTests : IClassFixture<RestoredFolders>, IDisposable
{
    private readonly string folder;

    public XamlBreakpointsTests(RestoredFolders folders)
    {
        folder = PathNames.Canonical(folders.Take("xamlbreakpoints-", made =>
        {
            foreach (string file in Directory.GetFiles(Debuggees.Source("XamlBreakpointsApp")))
            {
                File.Copy(file, Path.Combine(made, Path.GetFileName(file)));
            }
            string project = Path.Combine(made, "XamlBreakpointsApp.csproj");
            File.WriteAllText(project, AvaloniaApp.HeadlessProject());
            return project;
        }));
    }

    public void Dispose() => TestFolders.Delete(folder);

    private string Xaml => Path.Combine(folder, "MainWindow.axaml");

    private string CodeBehind => Path.Combine(folder, "MainWindow.axaml.cs");

    [AvaloniaFact]
    public void A_breakpoint_on_an_axaml_element_stops_as_its_control_is_made_and_one_on_an_attribute_as_its_property_is_set()
    {
        MainWindow window = Opened();
        Assert.Null(window.OpenFile(Xaml));
        Ui.Settle();
        CodeEditor editor = window.EditorArea.CodeEditor;
        int top = Line(Xaml, "Width=\"480\"");
        int save = Line(Xaml, "x:Name=\"Save\"");
        int row = Line(Xaml, "<local:Row");

        // Toggle Breakpoint on an attribute is the attribute's; on a line's start, the line's: its element.
        editor.GoTo(new TextPosition(top, Column(Xaml, top, "Width") + 1));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        editor.GoTo(new TextPosition(save, 0));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        editor.GoTo(new TextPosition(save, Column(Xaml, save, "Background") + 3));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        editor.GoTo(new TextPosition(row, 0));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        BreakpointStore store = window.Debugger.Breakpoints;
        Assert.Equal([$"MainWindow.axaml:{top + 1} Width", $"MainWindow.axaml:{save + 1}", $"MainWindow.axaml:{save + 1} Background", $"MainWindow.axaml:{row + 1}"],
            store.In(Xaml).Select(b => b.Location));
        // The attribute's breakpoint tints the attribute, not its line.
        TextSnapshot text = editor.Document.Snapshot;
        LineMark background = editor.LineMarks.Single(m => m.Range is not null && text.GetText(m.Range.Value.Start, m.Range.Value.Length).StartsWith("Background", StringComparison.Ordinal));
        Assert.Equal("Background=\"Orange\"", text.GetText(background.Range!.Value.Start, background.Range.Value.Length));

        // The template's row stops at the second row made, as its hit count, typed in the Breakpoints window, says.
        Ui.Run(window, AppActions.ViewBreakpoints);
        BreakpointsWindow breakpoints = Assert.IsType<BreakpointsWindow>(window.Popup);
        Assert.Equal([$"MainWindow.axaml:{top + 1} Width", $"MainWindow.axaml:{save + 1}", $"MainWindow.axaml:{save + 1} Background", $"MainWindow.axaml:{row + 1}", ExceptionBreakpoint.AnyException,
            BindingErrorBreakpoint.AnyBindingError], breakpoints.Listed.Select(b => b.Location));
        Click(window, Row(breakpoints, $"MainWindow.axaml:{row + 1}"));
        Click(window, breakpoints.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText?.StartsWith("Stop on hits", StringComparison.Ordinal) == true));
        window.KeyTextInput("==2");
        Click(window, breakpoints.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Done" && b.IsEffectivelyVisible));
        Assert.Equal("==2", store.In(Xaml).Single(b => b.Line == row).HitCondition);

        Assert.Null(Ui.Await(window.DebugAsync(), "the app to build and start under the debugger", TimeSpan.FromMinutes(4)));
        // The window's Width, which XamlIl gives no code of its own: the stop is before it is set, its Title already set.
        DebugStop stop = Stopped(window, "the stop where the window's Width is set");
        AssertAt(stop, Xaml, top, Column(Xaml, top, "Width"));
        Assert.Equal("NaN", Member(window, "arg_1", "Width"));
        Assert.Equal("\"XAML Breakpoints\"", Member(window, "arg_1", "Title"));

        // Save's element: the stop as its control is made, the window's own code that made it below on the stack.
        Resume(window);
        stop = Stopped(window, "the stop where Save is made");
        AssertAt(stop, Xaml, save, Column(Xaml, save, "Button"));
        Assert.True(stop.Frames.Any(f => f.Source?.Path == CodeBehind && f.Line == Line(CodeBehind, "InitializeComponent();") + 1),
            $"The stack did not show the window's constructor making its XAML: {string.Join(" < ", stop.Frames.Select(f => $"{f.Name} {Path.GetFileName(f.Source?.Path)}:{f.Line}"))}");
        Assert.Contains(window.BottomPanel.Debug.Frames, f => f.Source?.Path == Xaml);

        // Its Background, on the same line: the stop as that property is set.
        Resume(window);
        stop = Stopped(window, "the stop where Save's Background is set");
        AssertAt(stop, Xaml, save, Column(Xaml, save, "Background"));

        // The template's row, the second one made.
        Resume(window);
        stop = Stopped(window, "the stop where the second row is made");
        AssertAt(stop, Xaml, row, Column(Xaml, row, "local:Row"));
        Assert.Equal("1", Ui.Await(window.Debugger.Session!.EvaluateAsync("XamlBreakpointsApp.Row.Made", stop.Top!.Id, "watch"), "the rows made so far").Result);

        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the debugging to end");
        // XAML's breakpoints are the folder's, kept as line breakpoints are.
        Assert.Equal(4, store.In(Xaml).Count);
        window.Close();
    }

    [AvaloniaFact]
    public void Break_when_value_changes_stops_in_the_code_the_style_or_the_binding_that_changed_that_controls_property()
    {
        MainWindow window = Opened();
        Assert.Null(Ui.Await(window.DebugAsync(), "the app to build and start under the debugger", TimeSpan.FromMinutes(4)));
        Ui.WaitFor(() => window.LiveTree.Rows.Any(r => r.Element.Name == "Save"),
            $"the Live Visual Tree to show the app's window ({window.LiveApp?.State} {window.LiveApp?.Problem})", TimeSpan.FromMinutes(2));
        BreakpointStore store = window.Debugger.Breakpoints;

        // Code-behind: Save's Background, set twice by "paint"; each change stops where the code-behind set it.
        PropertyBreakpoint background = Watch(window, "Save", "Background");
        Assert.Equal("Background on Button#Save", background.Location);
        Ui.WaitFor(() => background.Status == BreakpointStatus.Bound, $"the debugger to take the property breakpoint, not to say {background.Message}");
        Say(window, "paint");
        DebugStop stop = Stopped(window, "the stop where paint sets Save's Background");
        AssertAt(stop, CodeBehind, Line(CodeBehind, "Save.Background = Brushes.Green;"));
        Says(window, "Property Breakpoint Hit: Background on Button#Save is changed (Previous Value: Orange, New Value: Green, its local value)");
        Resume(window);
        stop = Stopped(window, "the stop where paint sets Save's Background again");
        AssertAt(stop, CodeBehind, Line(CodeBehind, "Save.Background = Brushes.Blue;"));
        Resume(window);
        Ui.WaitFor(() => Console(window).Contains("Painted", StringComparison.Ordinal), "paint to finish");

        // Listed in the Breakpoints window, where its hit count lets the first change go by.
        Ui.Run(window, AppActions.ViewBreakpoints);
        BreakpointsWindow breakpoints = Assert.IsType<BreakpointsWindow>(window.Popup);
        Assert.Equal([background.Location, ExceptionBreakpoint.AnyException, BindingErrorBreakpoint.AnyBindingError], breakpoints.Listed.Select(b => b.Location));
        Assert.Contains(breakpoints.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Avalonia Property Breakpoints");
        Click(window, Row(breakpoints, background.Location));
        Click(window, breakpoints.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText?.StartsWith("Stop on hits", StringComparison.Ordinal) == true));
        window.KeyTextInput("2");
        Click(window, breakpoints.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Done" && b.IsEffectivelyVisible));
        Assert.Equal("2", background.HitCondition);
        Ui.Settle();
        Say(window, "paint");
        stop = Stopped(window, "the stop at the second change, by the hit count");
        AssertAt(stop, CodeBehind, Line(CodeBehind, "Save.Background = Brushes.Blue;"));
        Resume(window);
        Ui.WaitFor(() => Console(window).Split("Painted").Length == 3, "paint to finish again");

        // A condition reads the change: of Save's two widths, the one over 150 alone stops. Background is let go.
        Watch(window, "Save", "Background");
        Assert.DoesNotContain(background, store.Properties);
        PropertyBreakpoint width = Watch(window, "Save", "Width");
        Ui.Run(window, AppActions.ViewBreakpoints);
        breakpoints = Assert.IsType<BreakpointsWindow>(window.Popup);
        Click(window, Row(breakpoints, width.Location));
        Click(window, breakpoints.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText?.StartsWith("A C# condition", StringComparison.Ordinal) == true));
        window.KeyTextInput("(double)newValue > 150");
        Click(window, breakpoints.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Done" && b.IsEffectivelyVisible));
        Assert.Equal("(double)newValue > 150", width.Condition);
        Ui.Settle();
        Say(window, "paint");
        stop = Stopped(window, "the stop where Width goes over 150");
        AssertAt(stop, CodeBehind, Line(CodeBehind, "Save.Width = 200;"));
        Assert.DoesNotContain("could not be evaluated", Console(window), StringComparison.Ordinal);
        Resume(window);

        // A style, which a class added in code turns on: Delete's change, another control's, does not stop.
        PropertyBreakpoint foreground = Watch(window, "Save", "Foreground");
        Say(window, "warn");
        stop = Stopped(window, "the stop where the danger style sets Save's Foreground");
        AssertAt(stop, CodeBehind, Line(CodeBehind, "Save.Classes.Add(\"danger\");"));
        Says(window, $"Property Breakpoint Hit: {foreground.Location} is changed");
        Says(window, "by style Button.danger");
        Resume(window);

        // A binding: the caption's Text, which the view model's caption changes.
        Watch(window, "Caption", "Text");
        Say(window, "rename");
        stop = Stopped(window, "the stop where the caption's binding sets its Text");
        AssertAt(stop, Path.Combine(folder, "MainViewModel.cs"), Line(Path.Combine(folder, "MainViewModel.cs"), "PropertyChanged?.Invoke"));
        Says(window, "binding");
        Resume(window);

        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the debugging to end");
        // Each named a control of that run, as a data breakpoint names an object: none outlives it.
        Assert.Empty(store.Properties);
        window.Close();
    }

    private MainWindow Opened()
    {
        MainWindow window = new() { Width = 1400, Height = 900 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    /// <summary>Break When Value Changes from a property's row of a control chosen in the Live Visual Tree: the property
    /// breakpoint it sets, or null when it took the one there away.</summary>
    private static PropertyBreakpoint Watch(MainWindow window, string element, string property)
    {
        LiveTreeView tree = window.LiveTree;
        // Chosen afresh, so its properties are as they are now (a value set since lists it as local).
        tree.Tree.SelectedItem = tree.Rows.First(r => r.Element.Name == "Panel");
        Ui.WaitFor(() => tree.Details?.Name == "Panel", "the panel's details");
        tree.Tree.SelectedItem = tree.Rows.First(r => r.Element.Name == element);
        Ui.WaitFor(() => tree.Details?.Name == element, $"{element}'s details");
        tree.ShowPage(LiveTreePage.Properties);
        Ui.Settle();
        tree.PropertyList.SelectedItem = tree.PropertyRows.First(p => p.Name == property);
        Ui.ChooseFromMenu(tree.PropertyList, LiveTreeView.BreakWhenValueChanges);
        return window.Debugger.Breakpoints.FindProperty(tree.Details!.Id, property)!;
    }

    /// <summary>Waits for the program to stop and the stop to be read, which it returns.</summary>
    private static DebugStop Stopped(MainWindow window, string what)
    {
        Ui.WaitFor(() => window.Debugger.Session?.State is DebugState.Paused or DebugState.Ended && (window.Debugger.Stop is { } || window.Debugger.Session.State == DebugState.Ended),
            what, TimeSpan.FromMinutes(1));
        return window.Debugger.Stop ?? throw new Xunit.Sdk.XunitException($"The program ended before {what}: {window.Status}");
    }

    /// <summary>Resumes, and waits for the program to leave its stop.</summary>
    private static void Resume(MainWindow window)
    {
        DebugSession session = window.Debugger.Session!;
        bool resumed = false;
        session.StateChanged += (_, _) => resumed |= session.State != DebugState.Paused;
        Ui.Run(window, AppActions.Resume);
        Ui.WaitFor(() => resumed, "the program to go on");
    }

    /// <summary>A stop shown where it is meant to be: its frame at a line (from zero) of a file, at a column (from zero).</summary>
    private static void AssertAt(DebugStop stop, string path, int line, int? column = null)
    {
        Assert.True(stop.Top?.Source?.Path == path && stop.Top.Line == line + 1 && (column is null || stop.Top.Column == column + 1),
            $"The stop was shown at {stop.Top?.Source?.Path}:{stop.Top?.Line}:{stop.Top?.Column}, not {path}:{line + 1}{(column is null ? string.Empty : $":{column + 1}")} "
            + $"({stop.Reason}; {string.Join(" < ", stop.Frames.Take(12).Select(f => $"{f.Name} {Path.GetFileName(f.Source?.Path)}:{f.Line}"))})");
    }

    /// <summary>A member of a top-level row of Variables, the row opened to show it.</summary>
    private static string? Member(MainWindow window, string row, string name)
    {
        Passenger.App.ToolWindows.DebugView view = window.BottomPanel.Debug;
        Ui.WaitFor(() => view.Tree.Rows.Any(r => r.Depth == 0 && r.Name == row), $"{row} in Variables");
        VariableNode owner = view.Tree.Rows.First(r => r.Depth == 0 && r.Name == row);
        if (!owner.IsExpanded)
        {
            _ = view.Tree.Expand(owner);
        }
        Ui.WaitFor(() => owner.Children?.Any(c => c.Name == name) == true, $"{row}'s {name} in Variables");
        return owner.Children!.First(c => c.Name == name).Value;
    }

    /// <summary>A line of the app's input, typed into the Debug window's console.</summary>
    private static void Say(MainWindow window, string line)
    {
        window.ShowToolWindow("debug");
        window.BottomPanel.Debug.ShowTab(console: true);
        Ui.Settle();
        Passenger.App.ToolWindows.OutputView console = window.BottomPanel.Debug.Console;
        Point inside = console.Editor.TranslatePoint(new Point(console.Editor.Bounds.Width / 2, console.Editor.Bounds.Height / 2), window)!.Value;
        window.MouseDown(inside, MouseButton.Left);
        window.MouseUp(inside, MouseButton.Left);
        window.KeyTextInput(line);
        Ui.Press(window, "ENTER");
    }

    private static string Console(MainWindow window) => window.BottomPanel.Debug.Console.Document.Snapshot.ToString();

    /// <summary>The status bar says something of the stop, once the stop's words are read.</summary>
    private static void Says(MainWindow window, string text) =>
        Ui.WaitFor(() => window.Status?.Contains(text, StringComparison.Ordinal) == true, $"the status bar to say \"{text}\", not \"{window.Status}\"", TimeSpan.FromSeconds(20));

    private static ListBoxItem Row(BreakpointsWindow breakpoints, string location) =>
        breakpoints.GetVisualDescendants().OfType<ListBoxItem>().First(i => (i.Tag as Breakpoint)?.Location == location);

    private static void Click(MainWindow window, Control control)
    {
        Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The control is not in the window");
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
    }

    /// <summary>The line (from zero) of a file holding some text, so a test names what it means rather than a number.</summary>
    private static int Line(string file, string text) => Array.FindIndex(File.ReadAllLines(file), l => l.Contains(text, StringComparison.Ordinal));

    /// <summary>Where (from zero) some text begins on a line (from zero) of a file.</summary>
    private static int Column(string file, int line, string text) => File.ReadAllLines(file)[line].IndexOf(text, StringComparison.Ordinal);
}
