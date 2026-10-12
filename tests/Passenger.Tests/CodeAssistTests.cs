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
    public void The_structure_window_follows_the_editor_and_goes_to_a_member()
    {
        MainWindow window = project.Open("Program.cs");
        window.EditorArea.CodeEditor.GoTo(new TextPosition(4, 8));
        Ui.Run(window, AppActions.ShowStructure);
        Passenger.App.ToolWindows.StructureView structure = window.StructureView;
        Ui.WaitFor(() => structure.Rows.Count > 0, "the outline");
        Assert.Equal(["Program", "Main()"], structure.Rows.Select(r => r.Name));
        Assert.Equal("Main()", structure.Selected?.Name);

        // Another file shown, its outline follows.
        window.OpenFile(project.PathOf("Helper.cs"));
        Ui.WaitFor(() => structure.Rows.FirstOrDefault()?.Name == "Helper", "the other file's outline");
        Assert.Contains(structure.Rows, r => r.Name == "Twice(int)");
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
    public void Go_to_declaration_on_a_library_type_opens_its_decompiled_source_read_only()
    {
        MainWindow window = project.Open("Program.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "        in");
        Ui.Press(window, Ui.Shortcut(AppActions.GoToDeclaration));
        Ui.WaitFor(() => window.EditorArea.Active?.IsDecompiled == true, "the decompiled source to open");
        Passenger.App.Workspace.OpenDocument decompiled = window.EditorArea.Active!;
        Assert.Equal("Int32.cs", decompiled.Name);
        Assert.Contains("struct Int32", decompiled.Text.Snapshot.ToString(), StringComparison.Ordinal);
        // At the declaration, as for the project's own code.
        Assert.Contains("struct Int32", decompiled.Text.Snapshot.GetLineText(editor.CaretPosition.Line), StringComparison.Ordinal);

        // Nothing typed there reaches the library, so nothing is typed there.
        string before = decompiled.Text.Snapshot.ToString();
        window.KeyTextInput("x");
        Ui.Settle();
        Assert.Equal(before, decompiled.Text.Snapshot.ToString());
        Assert.True(editor.IsReadOnly);
        window.Close();
    }

    [AvaloniaFact]
    public void Code_cleanup_removes_unused_usings_sorts_the_rest_and_reformats()
    {
        MainWindow window = project.Open("Messy.cs");
        Passenger.App.Workspace.OpenDocument messy = window.EditorArea.Active!;
        Ui.WaitFor(() => window.CSharpService!.For(messy)!.Diagnostics.Any(d => d.Code?.ToString() == "IDE0005"), "the unused using to be found");
        Ui.Run(window, AppActions.CodeCleanup);
        Ui.WaitFor(() => window.Status?.StartsWith("Code cleanup", StringComparison.Ordinal) == true, "the cleanup to finish");
        string text = messy.Text.Snapshot.ToString();
        Assert.StartsWith("using System;\nusing System.Collections.Generic;\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text", text, StringComparison.Ordinal);
        Assert.Contains("\n        List<string> names = [];\n", text, StringComparison.Ordinal);
        Assert.Contains("\n        return names.Count;\n", text, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Extract_method_turns_the_selection_into_a_method_and_asks_for_its_name()
    {
        MainWindow window = project.Open("Loop.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        string text = editor.Document.Snapshot.ToString();
        int from = text.IndexOf("for (", StringComparison.Ordinal);
        int to = text.IndexOf("        }\n        return", StringComparison.Ordinal) + "        }".Length;
        editor.Selections = SelectionSet.Single(new Selection(from, to));
        Ui.Run(window, AppActions.ExtractMethod);
        Ui.WaitFor(() => editor.Document.Snapshot.ToString().Contains("NewMethod(", StringComparison.Ordinal), "the method to be extracted");
        Ui.Settle();

        // Rider asks for the new method's name at once; it is the rename box at the call.
        window.KeyTextInput("Accumulate");
        Ui.Press(window, "ENTER");
        Ui.WaitFor(() => editor.Document.Snapshot.ToString().Contains("Accumulate(", StringComparison.Ordinal), "the new method to take its name");
        string after = editor.Document.Snapshot.ToString();
        Assert.DoesNotContain("NewMethod", after, StringComparison.Ordinal);
        Assert.Matches(@"static \w+ Accumulate\(", after);
        window.Close();
    }

    [AvaloniaFact]
    public void Generate_lists_what_the_server_can_write_for_the_type_and_nothing_else()
    {
        MainWindow window = project.Open("Point.cs");
        PlaceAfter(window.EditorArea.CodeEditor, "public int X");
        Ui.Run(window, AppActions.Generate);
        Ui.WaitFor(() => window.Assist.Offered.Count > 0, "what can be generated");
        Assert.Contains("Generate Equals and GetHashCode", window.Assist.Offered);
        Assert.All(window.Assist.Offered, title => Assert.StartsWith("Generate", title, StringComparison.Ordinal));
        Ui.Press(window, "ESCAPE");
        window.Close();
    }

    [AvaloniaFact]
    public void Refactor_this_lists_only_the_refactorings_at_the_selection()
    {
        MainWindow window = project.Open("Loop.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        string text = editor.Document.Snapshot.ToString();
        int from = text.IndexOf("for (", StringComparison.Ordinal);
        editor.Selections = SelectionSet.Single(new Selection(from, text.IndexOf("        }\n        return", StringComparison.Ordinal) + "        }".Length));
        Ui.Run(window, AppActions.RefactorThis);
        Ui.WaitFor(() => window.Assist.Offered.Count > 0, "the refactorings");
        IReadOnlyList<string> offered = window.Assist.Offered;
        Assert.Contains("Extract method", offered);
        Assert.DoesNotContain(offered, t => t.StartsWith("Suppress", StringComparison.Ordinal));
        Ui.Press(window, "ESCAPE");
        window.Close();
    }

    [AvaloniaFact]
    public void The_status_bar_path_names_the_file_and_the_member_the_caret_is_in()
    {
        MainWindow window = project.Open("Program.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "Helper.Tw");
        Ui.WaitFor(() => window.Breadcrumbs.Count == 3, "the member the caret is in");
        Assert.Equal(["Program.cs", "Program", "Main()"], window.Breadcrumbs);
        // Out of the method, only the type is left.
        editor.GoTo(new TextPosition(1, 0));
        Ui.Settle();
        Assert.Equal(["Program.cs", "Program"], window.Breadcrumbs);
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

    [AvaloniaFact]
    public void A_literal_argument_shows_its_parameters_name_before_it_and_the_text_after_makes_room()
    {
        MainWindow window = project.Open("Program.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        int argument = editor.Document.Snapshot.ToString().IndexOf("Twice(2)", StringComparison.Ordinal) + "Twice(".Length;
        Ui.WaitFor(() => editor.InlayHints.Any(h => h.Label == "value:"), "the parameter name hint");
        Assert.Equal(argument, Assert.Single(editor.InlayHints).Offset);

        // The argument stands a hint's room past the parenthesis; the caret at it stands after the hint.
        double Step(int offset) => editor.PointOf(offset + 1).X - editor.PointOf(offset).X;
        double character = Step(argument);
        Assert.True(Step(argument - 1) > character * 4, $"{Step(argument - 1)} against a character's {character}");

        // Off in Settings, the line stands as typed.
        editor.ShowInlayHints = false;
        Assert.Equal(character, Step(argument - 1), 3);
        window.Close();
    }

    [AvaloniaFact]
    public void Extend_selection_grows_through_the_code_around_the_caret_and_shrink_takes_it_back()
    {
        MainWindow window = project.Open("Loop.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "            to");
        string Selected() => editor.Document.Snapshot.GetText(editor.Selections.Primary.Start, editor.Selections.Primary.Length);
        List<string> grown = [];
        for (int i = 0; i < 6; i++)
        {
            Ui.Run(window, AppActions.ExtendSelection);
            grown.Add(Selected());
        }
        Assert.Equal("total", grown[0]);
        Assert.Equal("total += i * 2", grown[1]);
        Assert.Equal("total += i * 2;", grown[2]);
        Assert.StartsWith("{", grown[3], StringComparison.Ordinal);
        Assert.StartsWith("for (", grown[4], StringComparison.Ordinal);
        // The method's statements, without the braces around them.
        Assert.StartsWith("int total = 0;", grown[5], StringComparison.Ordinal);
        Assert.EndsWith("return total;", grown[5], StringComparison.Ordinal);

        Ui.Run(window, AppActions.ShrinkSelection);
        Ui.Run(window, AppActions.ShrinkSelection);
        Assert.Equal(grown[3], Selected());
        for (int i = 0; i < 4; i++)
        {
            Ui.Run(window, AppActions.ShrinkSelection);
        }
        Assert.True(editor.Selections.Primary.IsEmpty);
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
            File.WriteAllText(PathOf("Point.cs"), "class Point\n{\n    public int X { get; }\n    public int Y { get; }\n}\n");
            File.WriteAllText(PathOf("Loop.cs"), "static class Loop\n{\n    public static int Sum()\n    {\n        int total = 0;\n        for (int i = 0; i < 10; i++)\n        {\n            total += i * 2;\n        }\n        return total;\n    }\n}\n");
            File.WriteAllText(PathOf("Messy.cs"), "using System.Text;\nusing System;\nusing System.Collections.Generic;\n\nstatic class Messy\n{\n    public static int Count()\n    {\n          List<string> names = [];\n        Console.WriteLine(names.Count);\n      return names.Count;\n    }\n}\n");
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
