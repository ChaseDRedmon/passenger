using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Passenger.App;
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
    private TextPosition Misspelt => new(Line("{Binding Nmae}") - 1, File.ReadAllLines(Xaml)[Line("{Binding Nmae}") - 1].IndexOf("Nmae", StringComparison.Ordinal));

    [AvaloniaFact]
    public void A_running_apps_misspelt_binding_shows_in_run_linked_to_its_line_and_in_problems_once_with_its_count_and_fix()
    {
        MainWindow window = Opened();
        Assert.Null(Ui.Await(window.RunAsync(), "the app to build and start", TimeSpan.FromMinutes(4)));
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
        Assert.Equal(["Change 'Nmae' to 'Name'"], window.Assist.Offered);
        Ui.Press(window, "ENTER");
        Assert.Contains("Text=\"{Binding Name}\"", window.EditorArea.CodeEditor.Document.Snapshot.ToString(), StringComparison.Ordinal);

        Stop(window);
        Assert.Single(problems.BindingProblems);
        window.Close();
    }

    [AvaloniaFact]
    public void A_debugged_apps_misspelt_binding_shows_in_the_debug_console_linked_to_its_line_and_in_problems()
    {
        MainWindow window = Opened();
        Assert.Null(Ui.Await(window.DebugAsync(), "the app to build and start under the debugger", TimeSpan.FromMinutes(4)));
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

    /// <summary>The folder opened in a window.</summary>
    private MainWindow Opened()
    {
        MainWindow window = new() { Width = 1400, Height = 900 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    /// <summary>The console's line saying the caption's binding failed, once it has it.</summary>
    private static string WaitForLine(OutputView console, string what)
    {
        string? line = null;
        Ui.WaitFor(() =>
        {
            console.Flush();
            line = console.Document.Snapshot.ToString().Split('\n').FirstOrDefault(l => l.Contains("binding error", StringComparison.Ordinal));
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

    /// <summary>The line (from one) of MainWindow.axaml holding some text, so a test names what it means rather than a number.</summary>
    private int Line(string text) => Array.FindIndex(File.ReadAllLines(Xaml), l => l.Contains(text, StringComparison.Ordinal)) + 1;

}
