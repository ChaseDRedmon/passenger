using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Problems as Rider keeps them without a build: the current file's as they are typed, and with solution-wide
/// analysis on, every file's, a file nobody has open included, grouped by project and file.</summary>
public sealed class ProblemsTests : IDisposable
{
    private readonly string folder = TestFolders.Create("problems-");

    public ProblemsTests()
    {
        Write("Sample.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        Write("Program.cs", "class Program\n{\n    static void Main()\n    {\n    }\n}\n");
        Write("Helper.cs", "static class Helper\n{\n    public static int Twice(int value) => value * 2;\n}\n");
        Write("Caller.cs", "static class Caller\n{\n    public static int Four() => Helper.Twice(2);\n}\n");
        Write("Bad.cs", "class Bad\n{\n    public int Count = \"none\";\n}\n");
        TestFolders.Prepare(() => Restore.Run(folder), folder);
    }

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void Solution_wide_analysis_lists_every_files_errors_and_follows_an_edit_that_breaks_a_file_nobody_has_open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile("Helper.cs");
        Ui.Settle();
        ProblemsView problems = window.BottomPanel.Problems;
        Assert.Null(problems.SolutionProblems);

        window.Perform(AppActions.ToggleSolutionAnalysis);
        Assert.True(window.Settings.SolutionWideAnalysis);
        Ui.WaitFor(() => problems.SolutionProblems?.Any(p => p.Code == "CS0029") == true, "the closed file's error");
        Problem bad = problems.SolutionProblems!.First(p => p.Code == "CS0029");
        Assert.EndsWith("Bad.cs", bad.Path, StringComparison.Ordinal);
        Assert.Equal(new TextPosition(2, 23), bad.Position);
        problems.Shown = ProblemsTab.Solution;
        Assert.Equal(["Sample", "Bad.cs"], problems.Rows.Where(r => r.Problem is null).Select(r => r.Label).Take(2));

        // Helper's method renamed by typing: Caller, closed, no longer compiles, and says so without a build.
        string text = window.EditorArea.CodeEditor.Document.Snapshot.ToString();
        int at = text.IndexOf("Twice", StringComparison.Ordinal);
        window.EditorArea.CodeEditor.Selections = SelectionSet.Single(new Selection(at, at + "Twice".Length));
        window.KeyTextInput("Thrice");
        Ui.WaitFor(() => problems.SolutionProblems?.Any(p => p.Path?.EndsWith("Caller.cs", StringComparison.Ordinal) == true) == true,
            "the broken caller's error", TimeSpan.FromMinutes(1));
        Assert.Empty(problems.FileProblems);

        // The open file's own problems are the File tab's, as they are typed.
        window.KeyTextInput(" x");
        Ui.WaitFor(() => problems.FileProblems.Any(p => p.Severity == ProblemSeverity.Error), "the open file's error");
        Assert.Empty(problems.Problems);

        // A problem's menu copies its message.
        window.Perform(AppActions.ShowProblems);
        problems.Shown = ProblemsTab.File;
        Avalonia.Controls.ListBox rows = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(problems).OfType<Avalonia.Controls.ListBox>().First();
        ProblemRow chosen = problems.Rows.First(r => r.Problem is { Severity: ProblemSeverity.Error });
        rows.SelectedItem = chosen;
        Ui.ChooseFromMenu(rows, "Copy Message");
        Ui.WaitFor(() => Ui.Clipboard(window) == chosen.Problem!.Message, "the message on the clipboard");

        window.Perform(AppActions.ToggleSolutionAnalysis);
        Assert.Null(problems.SolutionProblems);
        Assert.Equal(ProblemsTab.File, problems.Shown);
        window.Close();
    }

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(folder, name), text);
}
