using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Navigation;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>The language server's help through the whole window, on a real project: completion as
/// you type, go to definition, rename. Each test opens its own window, so its own server.</summary>
public sealed class CodeAssistTests : IClassFixture<CodeAssistTests.Project>
{
    private readonly Project project;

    public CodeAssistTests(Project project) => this.project = project;

    [AvaloniaFact]
    public void Go_to_symbol_finds_a_member_in_another_file_by_name_and_goes_to_it()
    {
        MainWindow window = project.Open("Program.cs");
        Ui.Run(window, AppActions.GoToSymbol);
        SymbolPopup popup = Assert.IsType<SymbolPopup>(window.Popup);
        window.KeyTextInput("Twic");
        Ui.WaitFor(() => popup.Rows.Any(r => r.Name.StartsWith("Twice", StringComparison.Ordinal)), "the server's symbols for what was typed");
        SymbolRow twice = popup.Rows.First(r => r.Name.StartsWith("Twice", StringComparison.Ordinal));
        Assert.Equal("Helper", twice.Detail);
        Assert.Equal("Helper.cs:3", twice.Where);

        Ui.Press(window, "Enter");
        Assert.Null(window.Popup);
        Assert.Equal(Passenger.App.Workspace.PathNames.Canonical(project.PathOf("Helper.cs")), window.EditorArea.Active?.Path);
        Assert.Equal(2, window.EditorArea.CodeEditor.CaretPosition.Line);
        window.Close();
    }

    [AvaloniaFact]
    public void File_structure_lists_the_files_members_opens_on_the_carets_and_filters_keeping_their_type()
    {
        MainWindow window = project.Open("Program.cs");
        window.EditorArea.CodeEditor.GoTo(new TextPosition(4, 8));
        Ui.Run(window, AppActions.FileStructure);
        SymbolPopup popup = Assert.IsType<SymbolPopup>(window.Popup);
        Ui.WaitFor(() => popup.IsSettled && popup.Rows.Count > 0, "the file's outline");
        Assert.Equal([("Program", 0), ("Main()", 1)], popup.Rows.Select(r => (r.Name, r.Depth)));
        Assert.Equal(": void", popup.Rows[1].Detail);
        Assert.Equal("Main()", popup.Selected?.Name);

        window.KeyTextInput("Ma");
        Ui.WaitFor(() => popup.IsSettled && popup.Rows.Count == 2, "the filtered outline");
        Ui.Press(window, "Enter");
        Assert.Equal(2, window.EditorArea.CodeEditor.CaretPosition.Line);
        window.Close();
    }

    [AvaloniaFact]
    public void Typing_after_a_dot_opens_completion_filtered_as_you_type_and_enter_commits_it()
    {
        MainWindow window = project.Open("Program.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "static void Main()\n    {\n");
        window.KeyTextInput("        System.Console.");
        Ui.Settle();
        window.KeyTextInput("W");
        Ui.Settle();
        window.KeyTextInput("riteL");
        Ui.Settle();
        Assert.True(window.Assist.IsCompletionOpen);

        Ui.Press(window, "ENTER");
        Ui.Settle();
        Assert.False(window.Assist.IsCompletionOpen);
        Assert.Contains("System.Console.WriteLine", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Go_to_declaration_opens_the_other_file_at_the_method()
    {
        MainWindow window = project.Open("Program.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "Helper.Tw");
        Ui.Press(window, Ui.Shortcut(AppActions.GoToDeclaration));
        Ui.Settle();
        Assert.Equal("Helper.cs", window.EditorArea.Active?.Name);
        Assert.Equal(2, editor.CaretPosition.Line);
        window.Close();
    }

    [AvaloniaFact]
    public void Rename_changes_the_name_in_every_file_that_uses_it()
    {
        string helper = File.ReadAllText(project.PathOf("Helper.cs"));
        MainWindow window = project.Open("Program.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "Helper.Tw");
        Ui.Run(window, AppActions.Rename);
        Ui.Settle();
        window.KeyTextInput("Double");
        Ui.Press(window, "ENTER");
        Ui.Settle();
        try
        {
            Assert.Contains("Helper.Double(", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
            Assert.Contains("public static int Double(", File.ReadAllText(project.PathOf("Helper.cs")), StringComparison.Ordinal);
        }
        finally
        {
            File.WriteAllText(project.PathOf("Helper.cs"), helper);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resting_the_pointer_on_a_method_shows_a_card_with_its_signature()
    {
        MainWindow window = project.Open("Program.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        int at = editor.Document.Snapshot.ToString().IndexOf("Twice", StringComparison.Ordinal) + 2;
        Point point = editor.TranslatePoint(editor.PointOf(at) + new Point(1, editor.LineHeight / 2), window)!.Value;
        window.MouseMove(point, Avalonia.Input.RawInputModifiers.None);
        Ui.WaitFor(() => window.Assist.HoverText is not null, "the hover card");
        Assert.Contains("int Helper.Twice(int value)", window.Assist.HoverText, StringComparison.Ordinal);
        window.Close();
    }

    private static void PlaceAfter(CodeEditor editor, string text)
    {
        int at = editor.Document.Snapshot.ToString().IndexOf(text, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{text}' is not in the file");
        editor.Selections = SelectionSet.Single(at + text.Length);
    }

    /// <summary>A small console project on disk, restored once for every test.</summary>
    public sealed class Project : IDisposable
    {
        private readonly string folder = Directory.CreateTempSubdirectory("passenger-assist-").FullName;

        public Project()
        {
            File.WriteAllText(PathOf("Sample.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
            File.WriteAllText(PathOf("Program.cs"), "class Program\n{\n    static void Main()\n    {\n        int result = Helper.Twice(2);\n    }\n}\n");
            File.WriteAllText(PathOf("Helper.cs"), "static class Helper\n{\n    public static int Twice(int value) => value * 2;\n}\n");
            using Process restore = Process.Start(new ProcessStartInfo("dotnet", ["restore", folder]) { RedirectStandardOutput = true, RedirectStandardError = true })!;
            restore.WaitForExit();
        }

        public string PathOf(string name) => Path.Combine(folder, name);

        /// <summary>A window on the project with a file open, once C# is ready.</summary>
        public MainWindow Open(string file)
        {
            MainWindow window = new() { Width = 1200, Height = 800 };
            window.Show();
            window.OpenFolder(folder);
            window.OpenFile(file);
            Ui.Settle();
            Assert.True(window.CSharpService?.IsReady, window.CSharpService?.State);
            return window;
        }

        public void Dispose() => Directory.Delete(folder, recursive: true);
    }
}
