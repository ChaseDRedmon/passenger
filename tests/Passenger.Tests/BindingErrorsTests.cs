using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Debugging;
using Passenger.App.LiveTree;
using Passenger.App.ToolWindows;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;
using Debuggees = Passenger.Tests.Debugging.Helpers.Debuggees;

namespace Passenger.Tests;

/// <summary>An Avalonia app's failed bindings, from the app run and debugged from the window: tests/Debuggees/BindingErrorsApp,
/// written into a folder of its own with a project file on the tests' own Avalonia, whose caption binds a misspelt Name. It
/// runs headless, its standard input ("rebind", typed into its console) giving the window a new view model as something
/// done in the app would. Debugged through the window, so run alone.</summary>
[Collection(Alone.Name)]
public sealed class BindingErrorsTests : IClassFixture<RestoredFolders>, IDisposable
{
    private readonly string folder;

    public BindingErrorsTests(RestoredFolders folders)
    {
        folder = PathNames.Canonical(folders.Take("bindings-", made =>
        {
            foreach (string file in Directory.GetFiles(Debuggees.Source("BindingErrorsApp")))
            {
                File.Copy(file, Path.Combine(made, Path.GetFileName(file)));
            }
            string project = Path.Combine(made, "BindingErrorsApp.csproj");
            File.WriteAllText(project, AvaloniaApp.HeadlessProject());
            return project;
        }));
    }

    public void Dispose() => TestFolders.Delete(folder);

    private string Xaml => Path.Combine(folder, "MainWindow.axaml");

    /// <summary>Where the caption's binding names Nmae, on its start tag's third line, from zero.</summary>
    private TextPosition Misspelt => new(Line("{Binding Nmae}") - 1, Lines()[Line("{Binding Nmae}") - 1].IndexOf("Nmae", StringComparison.Ordinal));

    [AvaloniaFact]
    public void A_running_apps_misspelt_binding_shows_in_run_linked_to_its_line_and_in_problems_once_with_its_count_and_fix()
    {
        MainWindow window = Opened();
        Ui.Launched(window, window.RunAsync(), "the app to build and start", TimeSpan.FromMinutes(4));
        OutputView run = window.BottomPanel.Run;
        string line = WaitForLine(run, "the Run window to say the caption's binding failed");

        // Its place first, linked: a click opens MainWindow.axaml at the misspelt member, its start tag's third line.
        Assert.StartsWith($"MainWindow.axaml({Misspelt.Line + 1},{Misspelt.Column + 1}): ", line, StringComparison.Ordinal);
        Assert.Contains("TextBlock#Caption.Text to Nmae", line, StringComparison.Ordinal);
        Assert.Contains("Did you mean Name?", line, StringComparison.Ordinal);
        ClickLink(window, run, line);
        Assert.Equal(Xaml, window.EditorArea.Active?.Path);
        Assert.Equal(Misspelt, window.EditorArea.CodeEditor.CaretPosition);

        // In Problems' Bindings, under its file, once, with its control, property, path and count, at the misspelt member.
        ProblemsView problems = window.BottomPanel.Problems;
        BindingProblem caption = Assert.Single(problems.BindingProblems);
        Assert.Equal(("TextBlock#Caption", "Text", "Nmae", 1), (caption.Failure.Control, caption.Failure.Property, caption.Failure.Path, caption.Failure.Count));
        Assert.Equal((Xaml, Misspelt), (caption.Problem.Path, caption.Problem.Position));
        Assert.Equal(Line("x:Name=\"Caption\""), caption.Failure.Place?.Line);
        window.Perform(AppActions.ShowProblems);
        problems.Shown = ProblemsTab.Bindings;
        Ui.Settle();
        Assert.Equal(["MainWindow.axaml"], problems.Rows.Where(r => r.Problem is null).Select(r => r.Label));
        Assert.Equal($"TextBlock#Caption.Text · Nmae · MainWindow.axaml:{Misspelt.Line + 1}:{Misspelt.Column + 1} · ×1",
            problems.Rows.Single(r => r.Problem is not null).Detail);

        // Failing again, it is counted, not listed again, and the console says so.
        window.ShowToolWindow("run");
        Ui.Settle();
        Type(window, run, "rebind");
        Ui.WaitFor(() => problems.BindingProblems.SingleOrDefault()?.Failure.Count == 2, "the caption's failure to be counted twice");
        Assert.Single(problems.BindingProblems);
        Assert.Contains("×2", problems.Rows.Single(r => r.Problem is not null).Detail, StringComparison.Ordinal);
        Ui.WaitFor(() => run.Document.Snapshot.ToString().Contains("Nmae: Could not find", StringComparison.Ordinal)
            && run.Document.Snapshot.ToString().Contains("(×2)", StringComparison.Ordinal), "the Run window to say it failed again");

        // Its fix, from Problems: the editor goes to the misspelt member and offers the view model's nearest, which it takes.
        ListBox list = problems.GetVisualDescendants().OfType<ListBox>().First();
        list.SelectedItem = problems.Rows.Single(r => r.Problem is not null);
        Ui.ChooseFromMenu(list, "Show Context Actions");
        Ui.WaitFor(() => window.Assist.Offered.Count > 0, "the binding's fixes");
        // The fix comes first; the XAML refactorings that apply at the element (Extract Style) follow it, as in Rider.
        Assert.Equal("Change 'Nmae' to 'Name'", window.Assist.Offered[0]);
        Assert.Single(window.Assist.Offered, offered => offered.StartsWith("Change ", StringComparison.Ordinal));
        Ui.Press(window, "ENTER");
        Assert.Contains("Text=\"{Binding Name}\"", window.EditorArea.CodeEditor.Document.Snapshot.ToString(), StringComparison.Ordinal);

        Stop(window);
        Assert.Single(problems.BindingProblems);
        window.Close();
    }

    /// <summary>An app run while the temporary folder's path is longer than a Unix socket's address holds (an agent's TMPDIR
    /// can be) gets its inspector all the same, in Passenger's own socket folder: a socket there had failed Run, Debug and
    /// Profile of every Avalonia or WPF app with an IDE error. macOS and Linux read TMPDIR; Windows reads TMP, so there the
    /// run is as any other.</summary>
    [AvaloniaFact]
    public void A_running_app_is_inspected_under_a_temporary_folder_too_long_for_a_socket()
    {
        string temp = Path.Combine(TestFolders.Create("long-temp-"), new string('t', 60), new string('u', 60));
        Directory.CreateDirectory(temp);
        MainWindow window = Opened();
        string? was = Environment.GetEnvironmentVariable("TMPDIR");
        try
        {
            Environment.SetEnvironmentVariable("TMPDIR", temp);
            Assert.True(OperatingSystem.IsWindows() || Path.GetTempPath().Length > 104, $"The temporary folder {Path.GetTempPath()} was to be too long for a socket");
            Ui.Launched(window, window.RunAsync(), "the app to build and start", TimeSpan.FromMinutes(4));
            Ui.WaitFor(() => window.Session.RunApp is { State: LiveAppState.Connected }, $"the app's inspector to say hello ({window.Status})", TimeSpan.FromMinutes(2));
            Assert.Contains("TextBlock#Caption.Text to Nmae", WaitForLine(window.BottomPanel.Run, "the Run window to say the caption's binding failed"), StringComparison.Ordinal);
            Assert.True(window.Session.RunApp!.SocketPath.Length <= 104, $"The inspector's socket {window.Session.RunApp.SocketPath} is longer than macOS allows");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMPDIR", was);
        }
        Stop(window);
        window.Close();
    }

    /// <summary>Avalonia's XAML compiler counts lines as an XML reader does, ending one at a lone '\r' and at no U+0085, U+2028
    /// or U+2029, so below a lone '\r' in the window's tag and a comment holding all four before the caption on the caption's
    /// own line, the place the app reports runs two lines ahead of the editor's and its column starts after the comment's:
    /// the Run window's line, its link, Problems and the fix all find the misspelt member at its own line.</summary>
    [AvaloniaFact]
    public void A_misspelt_binding_below_line_breaks_only_an_xml_reader_counts_is_placed_at_the_editors_line()
    {
        string text = File.ReadAllText(Xaml)
            .Replace("Title=\"Binding Errors App\" Width", "Title=\"Binding Errors App\"\r        Width", StringComparison.Ordinal)
            .Replace("<TextBlock x:Name=\"Caption\"", $"<!-- Pasted:{(char)0x2028}a{(char)0x2029}b{(char)0x85}c\rd --> <TextBlock x:Name=\"Caption\"", StringComparison.Ordinal);
        File.WriteAllText(Xaml, text);
        (int line, int column) = EditorPlace(text, "Nmae");
        TextPosition misspelt = new(line, column);
        MainWindow window = Opened();
        Ui.Launched(window, window.RunAsync(), "the app to build and start", TimeSpan.FromMinutes(4));
        OutputView run = window.BottomPanel.Run;
        string said = WaitForLine(run, "the Run window to say the caption's binding failed");
        Assert.StartsWith($"MainWindow.axaml({line + 1},{column + 1}): ", said, StringComparison.Ordinal);
        ClickLink(window, run, said);
        Assert.Equal((Xaml, misspelt), (window.EditorArea.Active?.Path, window.EditorArea.CodeEditor.CaretPosition));

        // The app reports XamlIl's place of the caption's element: past both lone '\r's, at the column after the comment's.
        ProblemsView problems = window.BottomPanel.Problems;
        BindingProblem caption = Assert.Single(problems.BindingProblems);
        int element = text.IndexOf("TextBlock x:Name=\"Caption\"", StringComparison.Ordinal);
        Assert.Equal((EditorPlace(text, "TextBlock x:Name=\"Caption\"").Line + 3, element - text.LastIndexOf('\r', element)),
            (caption.Failure.Place?.Line, caption.Failure.Place?.Column));
        Assert.Equal((Xaml, misspelt), (caption.Problem.Path, caption.Problem.Position));

        // Its fix, from Problems, at the misspelt member.
        window.Perform(AppActions.ShowProblems);
        problems.Shown = ProblemsTab.Bindings;
        Ui.Settle();
        ListBox list = problems.GetVisualDescendants().OfType<ListBox>().First();
        list.SelectedItem = problems.Rows.Single(r => r.Problem is not null);
        Ui.ChooseFromMenu(list, "Show Context Actions");
        Ui.WaitFor(() => window.Assist.Offered.Count > 0, "the binding's fixes");
        Assert.Equal("Change 'Nmae' to 'Name'", window.Assist.Offered[0]);
        Ui.Press(window, "ENTER");
        Assert.Contains("Text=\"{Binding Name}\"", window.EditorArea.CodeEditor.Document.Snapshot.ToString(), StringComparison.Ordinal);

        Stop(window);
        window.Close();
    }

    [AvaloniaFact]
    public void A_debugged_apps_misspelt_binding_shows_in_the_debug_console_linked_to_its_line_and_in_problems()
    {
        MainWindow window = Opened();
        Ui.Launched(window, window.DebugAsync(), "the app to build and start under the debugger", TimeSpan.FromMinutes(4));
        window.BottomPanel.Debug.ShowTab(console: true);
        OutputView console = window.BottomPanel.Debug.Console;
        string line = WaitForLine(console, "the Debug window's console to say the caption's binding failed");
        Assert.StartsWith($"MainWindow.axaml({Misspelt.Line + 1},{Misspelt.Column + 1}): ", line, StringComparison.Ordinal);
        ClickLink(window, console, line);
        Assert.Equal(Xaml, window.EditorArea.Active?.Path);
        Assert.Equal(Misspelt, window.EditorArea.CodeEditor.CaretPosition);

        BindingProblem caption = Assert.Single(window.BottomPanel.Problems.BindingProblems);
        Assert.Equal(("TextBlock#Caption", "Text", "Nmae", Xaml), (caption.Failure.Control, caption.Failure.Property, caption.Failure.Path, caption.Problem.Path));
        Assert.Equal(["Name"], caption.Failure.Nearest);
        // The window's bindings that worked said nothing.
        Assert.DoesNotContain("Greeting", console.Document.Snapshot.ToString(), StringComparison.Ordinal);

        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the debugging to end");
        window.Close();
    }

    [AvaloniaFact]
    public void Avalonia_binding_errors_stops_a_debugged_app_where_its_binding_fails_saying_what_failed_and_its_condition_and_log_choose()
    {
        // XamlIl counts the lone '\r' in the window's tag as a line's end, which the editor reads inside its line.
        File.WriteAllText(Xaml, File.ReadAllText(Xaml).Replace("Title=\"Binding Errors App\" Width", "Title=\"Binding Errors App\"\r        Width", StringComparison.Ordinal));
        MainWindow window = Opened();
        BreakpointStore store = window.Debugger.Breakpoints;
        string app = Path.Combine(folder, "App.axaml.cs");
        string program = Path.Combine(folder, "Program.cs");

        // Off until turned on, by its box in the Breakpoints window, under its own group after the CLR exception breakpoints.
        Assert.False(store.BindingErrors.Enabled, "XAML Binding Errors was on before anything turned it on");
        Ui.Run(window, AppActions.ViewBreakpoints);
        BreakpointsWindow breakpoints = Assert.IsType<BreakpointsWindow>(window.Popup);
        Assert.Equal([ExceptionBreakpoint.AnyException, BindingErrorBreakpoint.AnyBindingError], breakpoints.Listed.Select(b => b.Location));
        Assert.Contains(breakpoints.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "XAML Binding Errors");
        Click(window, Row(breakpoints, BindingErrorBreakpoint.AnyBindingError).GetVisualDescendants().OfType<CheckBox>().First());
        Click(window, Button(breakpoints, "Done"));
        Assert.True(store.BindingErrors.Enabled, "Its box did not turn XAML Binding Errors on");

        // The caption's binding fails as the app gives its window a view model: the stop is there, in the app's own code.
        Ui.Launched(window, window.DebugAsync(), "the app to build and start under the debugger", TimeSpan.FromMinutes(4));
        DebugStop stop = Stopped(window, "the stop where the window's view model makes the caption's binding fail");
        AssertAt(stop, app, LineIn(app, "DataContext = new MainViewModel()"));
        // Said where it is in the XAML, what it binds, its DataContext, the part of the path that failed and why.
        Assert.Equal((Xaml, Misspelt), (stop.Binding?.Problem.Path, stop.Binding?.Problem.Position));
        Says(window, $"paused on a binding error at MainWindow.axaml:{Misspelt.Line + 1}: TextBlock#Caption.Text to Nmae (DataContext MainViewModel): "
            + "Could not find a matching property accessor for 'Nmae' on 'BindingErrorsApp.MainViewModel'");
        // Which the status bar's words go to see in Problems' Bindings.
        window.ShowToolWindow("debug");
        Click(window, window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StatusMessage"));
        Assert.True(window.DockHost.IsShown("problems") && window.BottomPanel.Problems.Shown == ProblemsTab.Bindings,
            $"A click on \"{window.Status}\" in the status bar showed {(window.DockHost.IsShown("problems") ? $"Problems' {window.BottomPanel.Problems.Shown} tab" : "nothing")}, not Problems' Bindings");
        window.ShowToolWindow("debug");
        Assert.Contains(window.EditorArea.CodeEditor.LineAnnotations, a => a.Kind == LineAnnotationKind.Exception && a.Text.StartsWith("Binding error at MainWindow.axaml", StringComparison.Ordinal));
        // $bindingError above the frame's values, as $exception is, opening into the failure.
        Assert.Equal("\"Nmae\"", Member(window, "$bindingError", "Path"));
        Assert.Equal("\"Nmae\"", Member(window, "$bindingError", "FailedPart"));
        Assert.Contains("MainViewModel", Member(window, "$bindingError", "DataContext"), StringComparison.Ordinal);
        Assert.Contains("TextBlock", Member(window, "$bindingError", "Control"), StringComparison.Ordinal);
        // Its control's element, where Avalonia records it, at the editor's line though XamlIl's runs one on past the lone '\r':
        // the line, and the column of its name.
        Assert.Equal($"\"MainWindow.axaml:{Line("x:Name=\"Caption\"")}:{Lines()[Line("x:Name=\"Caption\"") - 1].IndexOf("TextBlock", StringComparison.Ordinal) + 1}\"",
            Member(window, "$bindingError", "Xaml"));
        // The Debug console links the binding's XAML: a click shows the misspelt member.
        window.BottomPanel.Debug.ShowTab(console: true);
        OutputView console = window.BottomPanel.Debug.Console;
        string line = WaitForLine(console, "the Debug console to link the binding that stopped the app", "paused on a binding error");
        Assert.StartsWith($"MainWindow.axaml({Misspelt.Line + 1},{Misspelt.Column + 1}): paused on a binding error: TextBlock#Caption.Text to Nmae", line, StringComparison.Ordinal);
        ClickLink(window, console, line);
        Assert.Equal((Xaml, Misspelt), (window.EditorArea.Active?.Path, window.EditorArea.CodeEditor.CaretPosition));
        Resume(window);

        // A condition reads the failure: one that holds for none lets the next failure go by, one that holds stops there.
        Condition(window, "$bindingError.Path == \"Name\"");
        Say(window, "rebind");
        Ui.WaitFor(() => Console(window).Contains("Bound again", StringComparison.Ordinal), "the app to bind its window again");
        Assert.True(window.Debugger.Session?.State == DebugState.Running, $"A condition that held for no failure stopped the app: {window.Status}");
        Condition(window, " || $bindingError.FailedPart == \"Nmae\"");
        Say(window, "rebind");
        stop = Stopped(window, "the stop where rebind makes the caption's binding fail again");
        AssertAt(stop, program, LineIn(program, "window.DataContext = new MainViewModel();"));
        Says(window, "paused on a binding error");
        Resume(window);
        Ui.WaitFor(() => Console(window).Split("Bound again").Length == 3, "the app to bind its window a second time");

        // Not suspending, it logs its message, which reads the failure, and the app goes on.
        Ui.Run(window, AppActions.ViewBreakpoints);
        breakpoints = Assert.IsType<BreakpointsWindow>(window.Popup);
        Click(window, Row(breakpoints, BindingErrorBreakpoint.AnyBindingError));
        Click(window, breakpoints.GetVisualDescendants().OfType<CheckBox>().First(c => (c.Content as string) == "Suspend execution"));
        Click(window, breakpoints.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText?.StartsWith("Logged on each hit", StringComparison.Ordinal) == true));
        window.KeyTextInput("{$bindingError.FailedPart} could not be read");
        Click(window, Button(breakpoints, "Done"));
        Assert.Equal((false, "{$bindingError.FailedPart} could not be read"), (store.BindingErrors.Suspend, store.BindingErrors.LogMessage));
        Ui.Settle();
        Say(window, "rebind");
        Ui.WaitFor(() => Console(window).Split("Bound again").Length == 4, "the app to bind its window a third time, not stopping");
        Ui.WaitFor(() => Console(window).Split('\n').Any(l => l.Contains("Nmae", StringComparison.Ordinal) && l.Contains("could not be read", StringComparison.Ordinal)),
            $"the log message to say what failed:\n{Console(window)}");
        Assert.True(window.Debugger.Session?.State == DebugState.Running, $"A breakpoint that does not suspend stopped the app: {window.Status}");

        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the debugging to end");
        window.Close();
    }

    [AvaloniaFact]
    public void Avalonia_binding_errors_hit_count_counts_on_past_a_property_breakpoint_set_between_its_hits_and_stops_on_the_third()
    {
        MainWindow window = Opened();
        BreakpointStore store = window.Debugger.Breakpoints;
        string program = Path.Combine(folder, "Program.cs");
        // The third failure stops: the caption's as the app starts, then one at each rebind.
        store.Update(store.BindingErrors, enabled: true, suspend: true, condition: null, hitCondition: "3", logMessage: null);

        Ui.Launched(window, window.DebugAsync(), "the app to build and start under the debugger", TimeSpan.FromMinutes(4));
        Ui.WaitFor(() => window.LiveTree.Rows.Any(r => r.Element.Name == "Caption"),
            $"the Live Visual Tree to show the app's window ({window.LiveApp?.State} {window.LiveApp?.Problem})", TimeSpan.FromMinutes(2));
        Say(window, "rebind");
        Ui.WaitFor(() => Console(window).Contains("Bound again", StringComparison.Ordinal), "the app's second failed binding, which the hit count lets go by");
        Assert.True(window.Debugger.Session?.State == DebugState.Running, $"XAML Binding Errors stopped before its third hit: {window.Status}");

        // Break When Value Changes on the caption sends the function breakpoints again, XAML Binding Errors' unchanged.
        LiveTreeView tree = window.LiveTree;
        tree.Tree.SelectedItem = tree.Rows.First(r => r.Element.Name == "Caption");
        Ui.WaitFor(() => tree.Details?.Name == "Caption", "the caption's details");
        tree.ShowPage(LiveTreePage.Properties);
        Ui.Settle();
        tree.PropertyList.SelectedItem = tree.PropertyRows.First(p => p.Name == "FontSize");
        Ui.ChooseFromMenu(tree.PropertyList, LiveTreeView.BreakWhenValueChanges);
        PropertyBreakpoint size = store.FindProperty(tree.Details!.Id, "FontSize")
            ?? throw new Xunit.Sdk.XunitException("Break When Value Changes set no property breakpoint on the caption's FontSize");
        Ui.WaitFor(() => size.Status == BreakpointStatus.Bound, $"the debugger to take the property breakpoint, not to say {size.Message}");

        Say(window, "rebind");
        DebugStop stop;
        try
        {
            stop = Stopped(window, "the stop at the third failed binding");
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException($"XAML Binding Errors let its third hit go, its count started again by the property breakpoint set beside it: {window.Status}");
        }
        AssertAt(stop, program, LineIn(program, "window.DataContext = new MainViewModel();"));
        Says(window, "paused on a binding error");

        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the debugging to end");
        window.Close();
    }

    /// <summary>The app's flood (tests/Debuggees/BindingErrorsApp/Flood.cs): 3,000 rows whose bindings fail as its window opens
    /// and again every 50 ms until it is calmed, far more than its inspector lists. Asked meanwhile, the Live Visual Tree is
    /// answered, its answers going ahead of the failures waiting to be said; once the app is calm, every failure Avalonia
    /// logged is either listed in Problems' Bindings, counted on its row, or counted under them as not listed.</summary>
    [AvaloniaFact]
    public void An_app_failing_thousands_of_bindings_as_it_opens_answers_the_live_visual_tree_and_problems_counts_every_failure()
    {
        MainWindow window = Opened();
        // While the app floods, its failures keep the window busy: the waits run its loop rather than wait for it to be quiet.
        Task<string?> launch = window.DebugAsync("flood");
        Ui.PumpUntil(() => launch.IsCompleted, "the app to build and start under the debugger", TimeSpan.FromMinutes(4));
        Assert.True(launch.Result is null, $"The window refused to debug the app: {launch.Result}{Ui.BuildErrors(window)}");
        LiveTreeView tree = window.LiveTree;
        Ui.PumpUntil(() => window.LiveApp?.State is LiveAppState.Connected or LiveAppState.Refused or LiveAppState.Ended, "the app's inspector to say hello", TimeSpan.FromMinutes(2));
        Assert.True(window.LiveApp?.State == LiveAppState.Connected, $"The app's inspector did not connect: {window.LiveApp?.State} {window.LiveApp?.Problem}");
        // Past its first thousand failed bindings as it opens, the Debug console says the rest are counted, not listed (looked
        // at as it comes, before the repeats' lines push it out of the console's head).
        OutputView console = window.BottomPanel.Debug.Console;
        Ui.PumpUntil(() =>
        {
            console.Flush();
            return console.Document.Snapshot.ToString().Contains("binding errors past the first 1000 failed bindings are counted, not listed", StringComparison.Ordinal);
        }, "the Debug console to say the failures past the first thousand are counted, not listed", TimeSpan.FromMinutes(1));
        // Asked as the app connected: its answer comes within the time the window waits for one, the flood going on.
        string last = $"Flooded{3000 - 1}";
        if (!Ui.Pumped(() => tree.Rows.Any(r => r.Element.Name == last), LiveApp.AnswerLimit))
        {
            throw new Xunit.Sdk.XunitException($"The Live Visual Tree showed no flooded rows within {LiveApp.AnswerLimit.TotalSeconds} s of the app's hello: \"{tree.StatusLine}\"");
        }
        tree.Tree.SelectedItem = tree.Rows.First(r => r.Element.Name == "Flooded1500");
        if (!Ui.Pumped(() => tree.Details?.Name == "Flooded1500", LiveApp.AnswerLimit))
        {
            throw new Xunit.Sdk.XunitException($"The Live Visual Tree showed no details of a flooded row within {LiveApp.AnswerLimit.TotalSeconds} s: \"{tree.StatusLine}\"");
        }
        Assert.DoesNotContain("did not answer", tree.StatusLine, StringComparison.Ordinal);

        // Calmed, the app says how many failures Avalonia logged in all.
        window.ShowToolWindow("debug");
        window.BottomPanel.Debug.ShowTab(console: true);
        Ui.PumpUntil(() => console.Editor.IsEffectivelyVisible && console.Editor.Bounds.Width > 0, "the Debug window's console to show");
        console.Editor.Focus();
        window.KeyTextInput("calm");
        window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        window.KeyRelease(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        int failed = 0;
        Ui.PumpUntil(() =>
        {
            console.Flush();
            string? calm = console.Document.Snapshot.ToString().Split('\n').FirstOrDefault(l => l.StartsWith("Calm after ", StringComparison.Ordinal));
            return calm is not null && int.TryParse(calm.Split(' ')[2], out failed);
        }, "the app to say how many of its bindings failed", TimeSpan.FromMinutes(1));
        Assert.True(failed >= 2 * 3001, $"The app's 3,001 failing bindings failed only {failed} times before it was calmed, not as it opened and again since");

        // Problems lists the first thousand, each with its count, and counts the rest beneath them: every failure, once.
        ProblemsView problems = window.BottomPanel.Problems;
        LiveApp app = window.LiveApp!;
        int Accounted() => problems.BindingProblems.Sum(b => b.Failure.Count) + app.Untold.Bindings;
        try
        {
            Ui.WaitFor(() => Accounted() == failed, $"Problems to account for all {failed} failures, listed or counted");
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException($"Of the app's {failed} binding failures, Problems listed {problems.BindingProblems.Sum(b => b.Failure.Count)} "
                + $"(in {problems.BindingProblems.Count} rows) and counted {app.Untold.Bindings} more: {failed - Accounted()} went untold");
        }
        Assert.Equal(1000, problems.BindingProblems.Count);
        Assert.Equal(0, app.Untold.Messages);
        window.Perform(AppActions.ShowProblems);
        problems.Shown = ProblemsTab.Bindings;
        Ui.Settle();
        Assert.Contains(problems.Rows, r => r.Problem is null && r.Label == $"{app.Untold.Bindings} more binding failures, not listed"
            && r.Detail == "BindingErrorsApp lists its first 1000 failed bindings and only counts the failures of any after them");

        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the debugging to end");
        window.Close();
    }

    /// <summary>Text typed after XAML Binding Errors' condition in the Breakpoints window.</summary>
    private static void Condition(MainWindow window, string typed)
    {
        Ui.Run(window, AppActions.ViewBreakpoints);
        BreakpointsWindow breakpoints = Assert.IsType<BreakpointsWindow>(window.Popup);
        Click(window, Row(breakpoints, BindingErrorBreakpoint.AnyBindingError));
        Click(window, breakpoints.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText?.StartsWith("A C# condition", StringComparison.Ordinal) == true));
        Ui.Press(window, "End");
        window.KeyTextInput(typed);
        Click(window, Button(breakpoints, "Done"));
        Ui.Settle();
    }

    /// <summary>Waits for the program to stop and the stop to be read, which it returns; a failure says what the console said.</summary>
    private static DebugStop Stopped(MainWindow window, string what)
    {
        try
        {
            Ui.WaitFor(() => window.Debugger.Session?.State is DebugState.Paused or DebugState.Ended && (window.Debugger.Stop is { } || window.Debugger.Session.State == DebugState.Ended),
                what, TimeSpan.FromMinutes(1));
        }
        catch (TimeoutException)
        {
            string said = Console(window);
            throw new TimeoutException($"Waited a minute for {what} ({window.Status}; XAML Binding Errors {window.Debugger.Breakpoints.BindingErrors.Status}: "
                + $"{window.Debugger.Breakpoints.BindingErrors.Message}); the console said:\n{said[Math.Max(0, said.Length - 3000)..]}");
        }
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

    /// <summary>A stop shown at a line (from zero) of a file.</summary>
    private static void AssertAt(DebugStop stop, string path, int line) =>
        Assert.True(stop.Top?.Source?.Path == path && stop.Top.Line == line + 1,
            $"The stop was shown at {stop.Top?.Source?.Path}:{stop.Top?.Line}, not {path}:{line + 1} "
            + $"({stop.Reason}; {string.Join(" < ", stop.Frames.Take(12).Select(f => $"{f.Name} {Path.GetFileName(f.Source?.Path)}:{f.Line}"))})");

    /// <summary>A member of a top-level row of Variables, the row opened to show it.</summary>
    private static string Member(MainWindow window, string row, string name)
    {
        DebugView view = window.BottomPanel.Debug;
        Ui.WaitFor(() => view.Tree.Rows.Any(r => r.Depth == 0 && r.Name == row),
            $"{row} in Variables, not only {string.Join(", ", view.Tree.Rows.Where(r => r.Depth == 0).Select(r => r.Name))}");
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
        Type(window, window.BottomPanel.Debug.Console, line);
    }

    private static string Console(MainWindow window)
    {
        window.BottomPanel.Debug.Console.Flush();
        return window.BottomPanel.Debug.Console.Document.Snapshot.ToString();
    }

    /// <summary>The status bar says something of the stop, once the stop's words are read.</summary>
    private static void Says(MainWindow window, string text) =>
        Ui.WaitFor(() => window.Status?.Contains(text, StringComparison.Ordinal) == true, $"the status bar to say \"{text}\", not \"{window.Status}\"", TimeSpan.FromSeconds(20));

    private static ListBoxItem Row(BreakpointsWindow breakpoints, string location) =>
        breakpoints.GetVisualDescendants().OfType<ListBoxItem>().First(i => (i.Tag as Breakpoint)?.Location == location);

    private static Button Button(Control within, string content) =>
        within.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == content && b.IsEffectivelyVisible);

    private static void Click(MainWindow window, Control control)
    {
        Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The control is not in the window");
        window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        Ui.Settle();
    }

    /// <summary>The line (from zero) of a file holding some text.</summary>
    private static int LineIn(string file, string text) => Array.FindIndex(File.ReadAllLines(file), l => l.Contains(text, StringComparison.Ordinal));

    /// <summary>The folder opened in a window.</summary>
    private MainWindow Opened()
    {
        MainWindow window = new() { Width = 1400, Height = 900 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    /// <summary>The console's line saying the caption's binding failed (or what else it says), once it has it.</summary>
    private static string WaitForLine(OutputView console, string what, string saying = "binding error")
    {
        string? line = null;
        Ui.WaitFor(() =>
        {
            console.Flush();
            line = console.Document.Snapshot.ToString().Split('\n').FirstOrDefault(l => l.Contains(saying, StringComparison.Ordinal));
            return line is not null;
        }, what, TimeSpan.FromMinutes(2));
        return line!;
    }

    /// <summary>A click on a console line's link, at its first character, as a person clicks it.</summary>
    private static void ClickLink(MainWindow window, OutputView console, string line)
    {
        CodeEditor editor = console.Editor;
        int offset = editor.Document.Snapshot.ToString().IndexOf(line, StringComparison.Ordinal);
        Point top = editor.PointOf(offset);
        Point at = editor.TranslatePoint(top + new Vector(2, editor.LineHeight / 2), window)!.Value;
        window.MouseMove(at);
        Ui.Settle();
        Assert.True(editor.HoveredLink is { Target: FileLink }, $"The pointer rests on the line's place but nothing is underlined:\n{line}");
        window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        Ui.Settle();
    }

    /// <summary>A line typed into a console, which sends it to the program's input.</summary>
    private static void Type(MainWindow window, OutputView console, string text)
    {
        Point inside = console.Editor.TranslatePoint(new Point(console.Editor.Bounds.Width / 2, console.Editor.Bounds.Height / 2), window)!.Value;
        window.MouseDown(inside, Avalonia.Input.MouseButton.Left);
        window.MouseUp(inside, Avalonia.Input.MouseButton.Left);
        window.KeyTextInput(text);
        Ui.Press(window, "ENTER");
    }

    private static void Stop(MainWindow window)
    {
        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Runner.IsRunning, "the app to stop");
    }

    /// <summary>Where (line and column from zero) some text begins in a text as the editor counts lines, at each '\n' alone.</summary>
    private static (int Line, int Column) EditorPlace(string text, string find)
    {
        int at = text.IndexOf(find, StringComparison.Ordinal);
        return (text.AsSpan(0, at).Count('\n'), at - text.LastIndexOf('\n', at) - 1);
    }

    /// <summary>The line (from one) of MainWindow.axaml holding some text, so a test names what it means rather than a number.</summary>
    private int Line(string text) => Array.FindIndex(Lines(), l => l.Contains(text, StringComparison.Ordinal)) + 1;

    /// <summary>MainWindow.axaml's lines as the editor counts them, at each '\n' alone.</summary>
    private string[] Lines() => File.ReadAllText(Xaml).Split('\n');

}
