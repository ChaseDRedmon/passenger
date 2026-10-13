using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Rider's edits beyond completion and Refactor This, each through its own keys in the window on a real project
/// with its language server: Surround With's templates, Complete Current Statement, Optimize Imports, Safe Delete,
/// Introduce Field and Parameter, and Encapsulate Field.</summary>
public sealed class CodeEditsTests : IClassFixture<CodeEditsTests.Project>
{
    private readonly Project project;

    public CodeEditsTests(Project project) => this.project = project;

    private const string TwoLines = "total += 1;\n        total += 2;";

    /// <summary>Rider's statement templates around two statements, in the order Surround With offers them: what each
    /// writes, what it leaves chosen to type over first, and where its caret ends once its variables are filled in.</summary>
    private static readonly (string Template, string Written, string First)[] StatementTemplates =
    [
        ("if", "        if (true)\n        {\n            total += 1;\n            total += 2;\n        }\n", "true"),
        ("while", "        while (true)\n        {\n            total += 1;\n            total += 2;\n        }\n", "true"),
        ("for", "        for (int i = 0; i < length; i++)\n        {\n            total += 1;\n            total += 2;\n        }\n", "i"),
        ("do", "        do\n        {\n            total += 1;\n            total += 2;\n        } while (true);\n", "true"),
        ("lock", "        lock (this)\n        {\n            total += 1;\n            total += 2;\n        }\n", "this"),
        ("{}", "        {\n            total += 1;\n            total += 2;\n        }\n", ""),
        ("try", "        try\n        {\n            total += 1;\n            total += 2;\n        }\n        catch (Exception e)\n        {\n            Console.WriteLine(e);\n            throw;\n        }\n", "Exception"),
        ("tryf", "        try\n        {\n            total += 1;\n            total += 2;\n        }\n        finally\n        {\n            \n        }\n", ""),
        ("#if", "        #if true\n        total += 1;\n        total += 2;\n        #endif\n", "true"),
        ("#region", "        #region MyRegion\n        total += 1;\n        total += 2;\n        #endregion\n", "MyRegion"),
        ("foreach", "        foreach (var item in collection)\n        {\n            total += 1;\n            total += 2;\n        }\n", "item"),
        ("using", "        using (resource)\n        {\n            total += 1;\n            total += 2;\n        }\n", "resource"),
    ];

    [AvaloniaFact]
    public void Surround_with_wraps_the_statements_chosen_in_each_of_riders_templates_and_leaves_the_caret_where_rider_does()
    {
        MainWindow window = project.Open("Sum.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        string before = editor.Document.Snapshot.ToString();
        foreach ((string template, string written, string first) in StatementTemplates)
        {
            Choose(editor, TwoLines);
            editor.Focus();
            Ui.Run(window, AppActions.SurroundWith);
            Assert.Equal(["if", "while", "for", "do", "lock", "()", "{}", "try", "tryf", "#if", "#region", "foreach", "using"], window.Assist.Offered);
            Pick(window, template);
            string text = editor.Document.Snapshot.ToString();
            Assert.True(text.Contains("        int total = 0;\n" + written + "        return total;\n", StringComparison.Ordinal),
                $"Surround With {template} did not wrap the two statements as Rider writes it:\n{text}");
            Assert.True(first == Selected(editor), $"Surround With {template} chose '{Selected(editor)}' to type over first, not '{first}'");

            // Its variables in turn, then its end: after the code wrapped, in finally's body, or catch's body chosen.
            for (int field = 0; field < 3 && editor.IsInSnippet; field++)
            {
                Ui.Press(window, "TAB");
            }
            Assert.False(editor.IsInSnippet, $"{template} was still being filled in after three Tabs");
            Assert.Equal(template == "try" ? "Console.WriteLine(e);\n            throw;" : string.Empty, Selected(editor));
            int expected = template switch
            {
                "try" => text.IndexOf("throw;", StringComparison.Ordinal) + "throw;".Length,
                "tryf" => text.IndexOf("finally\n        {\n            ", StringComparison.Ordinal) + "finally\n        {\n            ".Length,
                _ => text.IndexOf("total += 2;", StringComparison.Ordinal) + "total += 2;".Length,
            };
            Assert.True(expected == editor.Selections.Primary.Caret,
                $"After {template} the caret is at {editor.CaretPosition}, not where Rider leaves it:\n{text.Insert(editor.Selections.Primary.Caret, "|")}");

            // One undo takes the whole template back.
            Ui.Run(window, EditorActions.Undo);
            Assert.True(before == editor.Document.Snapshot.ToString(), $"Undo after {template} left:\n{editor.Document.Snapshot}");
        }
        window.Close();
    }

    [AvaloniaFact]
    public void Surround_with_offers_a_cast_and_parentheses_around_an_expression_and_a_namespace_around_types()
    {
        MainWindow window = project.Open("Sum.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        Choose(editor, "return total", skip: "return ".Length);
        editor.Focus();
        Ui.Run(window, AppActions.SurroundWith);
        Assert.Equal(["()", "{}", "cast"], window.Assist.Offered);
        // Its digit chooses it, as in Rider: 0 is the cast, its type to be typed first.
        Ui.Press(window, "0");
        Assert.Contains("        return (()total);\n", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
        window.KeyTextInput("long");
        Ui.Press(window, "TAB");
        string text = editor.Document.Snapshot.ToString();
        Assert.Contains("        return ((long)total);\n", text, StringComparison.Ordinal);
        Assert.Equal(text.IndexOf("((long)total)", StringComparison.Ordinal) + "((long)total)".Length, editor.Selections.Primary.Caret);

        // 6 is parentheses, the caret after what they wrap.
        Choose(editor, "total += 1", skip: 0);
        Ui.Run(window, AppActions.SurroundWith);
        Ui.Press(window, "6");
        text = editor.Document.Snapshot.ToString();
        Assert.Contains("        (total += 1);\n", text, StringComparison.Ordinal);
        Assert.Equal(text.IndexOf("(total += 1)", StringComparison.Ordinal) + "(total += 1".Length, editor.Selections.Primary.Caret);
        window.Close();

        window = project.Open("Two.cs");
        editor = window.EditorArea.CodeEditor;
        editor.Selections = SelectionSet.Single(new Selection(0, editor.Document.Snapshot.Length));
        editor.Focus();
        Ui.Run(window, AppActions.SurroundWith);
        Assert.Contains("namespace", window.Assist.Offered);
        Pick(window, "namespace");
        Assert.Equal("namespace MyNamespace\n{\n    class First\n    {\n    }\n\n    class Second\n    {\n    }\n}\n", editor.Document.Snapshot.ToString());
        Assert.Equal("MyNamespace", Selected(editor));
        window.Close();
    }

    [AvaloniaFact]
    public void Complete_current_statement_closes_an_if_a_call_a_declaration_and_a_method_and_puts_the_caret_where_the_next_code_goes()
    {
        MainWindow window = project.Open("Sum.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        string Text() => editor.Document.Snapshot.ToString();
        int After(string code) => Text().IndexOf(code, StringComparison.Ordinal) + code.Length;

        // An if's condition: its parenthesis closed, braces, and the caret in its body.
        PlaceAfter(editor, "int total = 0;");
        editor.Focus();
        Ui.Press(window, "ENTER");
        window.KeyTextInput("if (total > 0");
        Ui.Run(window, AppActions.CompleteStatement);
        Assert.Contains("        int total = 0;\n        if (total > 0)\n        {\n            \n        }\n        total += 1;\n", Text(), StringComparison.Ordinal);
        Assert.Equal(After("if (total > 0)\n        {\n            "), editor.Selections.Primary.Caret);

        // A call: its parenthesis, its semicolon, and the caret on a line of its own after it.
        window.KeyTextInput("Report(total");
        Ui.Run(window, AppActions.CompleteStatement);
        Assert.Contains("        {\n            Report(total);\n            \n        }\n", Text(), StringComparison.Ordinal);
        Assert.Equal(After("Report(total);\n            "), editor.Selections.Primary.Caret);

        // A declaration: its semicolon, and the next line.
        window.KeyTextInput("int doubled = total * 2");
        Ui.Run(window, AppActions.CompleteStatement);
        Assert.Contains("            Report(total);\n            int doubled = total * 2;\n            \n        }\n", Text(), StringComparison.Ordinal);
        Assert.Equal(After("int doubled = total * 2;\n            "), editor.Selections.Primary.Caret);

        // A method's signature: its parameters closed and a body to write in.
        PlaceAfter(editor, "        return total;\n    }");
        Ui.Press(window, "ENTER");
        Ui.Press(window, "ENTER");
        window.KeyTextInput("static void Report(int value");
        Ui.Run(window, AppActions.CompleteStatement);
        Assert.True(Text().EndsWith("\n    static void Report(int value)\n    {\n        \n    }\n}\n", StringComparison.Ordinal),
            $"The method's signature was not completed as Rider completes it:\n{Text().Insert(editor.Selections.Primary.Caret, "|")}");
        Assert.Equal(After("static void Report(int value)\n    {\n        "), editor.Selections.Primary.Caret);
        window.Close();
    }

    [AvaloniaFact]
    public void Optimize_imports_removes_the_usings_nothing_needs_and_sorts_the_rest()
    {
        MainWindow window = project.Open("Usings.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.Focus();
        Ui.Run(window, AppActions.OptimizeUsings);
        Ui.WaitFor(() => window.Status?.StartsWith("Optimize Imports", StringComparison.Ordinal) == true || window.Status == "The usings are already optimized",
            "Optimize Imports to finish");
        Assert.Equal("Optimize Imports removed unused usings and sorted the rest", window.Status);
        string text = editor.Document.Snapshot.ToString();
        Assert.StartsWith("using System;\nusing System.Collections.Generic;\n\nstatic class Usings\n", text, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Safe_delete_deletes_an_unused_method_at_once_and_lists_a_used_ones_usages_before_deleting_it_when_told_to()
    {
        MainWindow window = project.Open("Calc.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        string Text() => editor.Document.Snapshot.ToString();

        // Nothing uses it: it goes, its doc comment and the blank line before it with it.
        PlaceAfter(editor, "int Unu");
        editor.Focus();
        Ui.Run(window, AppActions.SafeDelete);
        Ui.WaitFor(() => !Text().Contains("Unused", StringComparison.Ordinal), "the unused method to be deleted");
        Assert.Equal("static class Calc\n{\n    public static int Twice(int value) => value * 2;\n\n    public static int Four() => Twice(2);\n}\n", Text());
        Assert.Equal("Deleted the method 'Unused'", window.Status);
        Assert.Empty(window.OwnedWindows.OfType<ConfirmDialog>());

        // Four and Main call Twice: Safe Delete lists both calls and deletes nothing until told to.
        PlaceAfter(editor, "int Twi");
        Ui.Run(window, AppActions.SafeDelete);
        ConfirmDialog asked = Asked(window);
        string[] shown = [.. asked.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? string.Empty)];
        Assert.Contains("The method 'Twice' has 2 usages that would no longer compile once it is deleted:", shown);
        Assert.Contains("Calc.cs:5", shown);
        Assert.Contains("public static int Four() => Twice(2);", shown);
        Assert.Contains("Program.cs:5", shown);
        Click(asked, "Cancel");
        Assert.Contains("Twice(int value)", Text(), StringComparison.Ordinal);
        Assert.Equal("Safe Delete left the method 'Twice' where it is", window.Status);

        // From a usage, the method it calls; Delete Anyway deletes it, the call left to fix.
        PlaceAfter(editor, "=> Twi");
        Ui.Run(window, AppActions.SafeDelete);
        Click(Asked(window), "Delete Anyway");
        Ui.WaitFor(() => !Text().Contains("Twice(int", StringComparison.Ordinal), "the used method to be deleted anyway");
        Assert.Equal("static class Calc\n{\n    public static int Four() => Twice(2);\n}\n", Text());
        Assert.Equal("Deleted the method 'Twice', leaving 2 usages to fix", window.Status);
        window.Close();
    }

    [AvaloniaFact]
    public void Introduce_field_makes_a_constant_a_field_of_the_type_and_says_why_not_for_anything_else()
    {
        MainWindow window = project.Open("Scale.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        string Text() => editor.Document.Snapshot.ToString();
        editor.Focus();

        // Roslyn makes no field of what is not a constant, and the status bar says so.
        Choose(editor, "value * 1024", skip: 0, length: "value".Length);
        Ui.Run(window, AppActions.IntroduceField);
        Ui.WaitFor(() => window.Status?.StartsWith("Introduce Field", StringComparison.Ordinal) == true, "Introduce Field to answer");
        Assert.Equal("Introduce Field is not available here: the C# language server makes a field only of a constant expression", window.Status);

        // Of the constant, a const field: this occurrence or every one, never a local.
        Choose(editor, "1024", skip: 0);
        Ui.Run(window, AppActions.IntroduceField);
        Ui.WaitFor(() => window.Assist.Offered.Count > 0, "Introduce Field's choices");
        Assert.All(window.Assist.Offered, offered => Assert.DoesNotContain("local", offered, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Introduce constant for '1024'", window.Assist.Offered);
        PickTitled(window, "Introduce constant for '1024'");
        Ui.WaitFor(() => Text().Contains("const int", StringComparison.Ordinal), "the constant field to be made");
        Assert.Matches(@"\n    private const int \w+ = 1024;\n", Text());
        Assert.DoesNotContain("value * 1024", Text(), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Introduce_parameter_makes_an_expression_a_parameter_and_passes_it_at_every_call()
    {
        MainWindow window = project.Open("Scale.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        string Text() => editor.Document.Snapshot.ToString();
        Choose(editor, "1024", skip: 0);
        editor.Focus();
        Ui.Run(window, AppActions.IntroduceParameter);
        Ui.WaitFor(() => window.Assist.Offered.Count > 0, "Introduce Parameter's choices");
        Assert.All(window.Assist.Offered, offered => Assert.StartsWith("Introduce parameter for", offered, StringComparison.Ordinal));
        PickTitled(window, "Introduce parameter for '1024'");
        Ui.WaitFor(() => window.Assist.Offered.Any(o => o.Contains("call sites", StringComparison.Ordinal)), "how the parameter is to be passed");
        PickTitled(window, window.Assist.Offered.First(o => o.Contains("call sites", StringComparison.Ordinal)));
        Ui.WaitFor(() => Text().Contains("Kilo(2, 1024)", StringComparison.Ordinal), "the call to pass the new parameter");
        Assert.Matches(@"public static int Kilo\(int value, int \w+\)", Text());
        Assert.DoesNotContain("value * 1024", Text(), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Encapsulate_field_offers_rider_s_choice_of_using_the_property_and_makes_it()
    {
        MainWindow window = project.Open("Counter.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        string Text() => editor.Document.Snapshot.ToString();
        PlaceAfter(editor, "public int cou");
        editor.Focus();
        Ui.Run(window, AppActions.EncapsulateField);
        Ui.WaitFor(() => window.Assist.Offered.Count > 0, "Encapsulate Field's choices");
        Assert.Equal(["Encapsulate field: 'count' (and use property)", "Encapsulate field: 'count' (but still use field)"], window.Assist.Offered);
        PickTitled(window, "Encapsulate field: 'count' (and use property)");
        Ui.WaitFor(() => Text().Contains("public int Count", StringComparison.Ordinal), "the property to be made");
        Assert.Contains("private int count;", Text(), StringComparison.Ordinal);
        Assert.Contains("public void Add() => Count++;", Text(), StringComparison.Ordinal);
        window.Close();
    }

    /// <summary>Chooses code in the editor: <paramref name="length"/> characters (all of <paramref name="code"/> when not
    /// given) from <paramref name="skip"/> into its first occurrence.</summary>
    private static void Choose(CodeEditor editor, string code, int skip = 0, int? length = null)
    {
        int at = editor.Document.Snapshot.ToString().IndexOf(code, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{code}' is not in the file");
        editor.Selections = SelectionSet.Single(new Selection(at + skip, at + skip + (length ?? code.Length - skip)));
    }

    private static void PlaceAfter(CodeEditor editor, string text)
    {
        int at = editor.Document.Snapshot.ToString().IndexOf(text, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{text}' is not in the file");
        editor.Selections = SelectionSet.Single(at + text.Length);
    }

    private static string Selected(CodeEditor editor) =>
        editor.Document.Snapshot.GetText(editor.Selections.Primary.Start, editor.Selections.Primary.Length);

    /// <summary>Chooses a surround template by its digit when it has one, as Rider's list does, else by the arrow keys.</summary>
    private static void Pick(MainWindow window, string template)
    {
        string digit = template switch
        {
            "if" => "1",
            "while" => "2",
            "for" => "3",
            "do" => "4",
            "lock" => "5",
            "()" => "6",
            "{}" => "7",
            "try" => "8",
            "tryf" => "9",
            "cast" => "0",
            _ => string.Empty,
        };
        if (digit.Length > 0)
        {
            Ui.Press(window, digit);
            return;
        }
        PickTitled(window, template);
    }

    /// <summary>Chooses an item of the open list by its words, with the keys a person would press.</summary>
    private static void PickTitled(MainWindow window, string item)
    {
        int at = window.Assist.Offered.ToList().IndexOf(item);
        Assert.True(at >= 0, $"{item} is not offered: {string.Join("; ", window.Assist.Offered)}");
        Ui.Settle();
        for (int i = 0; i < at; i++)
        {
            Ui.Press(window, "DOWN");
        }
        Ui.Press(window, "ENTER");
    }

    private static ConfirmDialog Asked(MainWindow window)
    {
        Ui.WaitFor(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(), "Safe Delete to list the usages");
        return window.OwnedWindows.OfType<ConfirmDialog>().Single();
    }

    private static void Click(ConfirmDialog dialog, string button)
    {
        dialog.GetVisualDescendants().OfType<Button>().First(b => (string?)b.Content == button)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Ui.Settle();
    }

    /// <summary>A small console project on disk, restored once for every test; a window never saves what its test typed.</summary>
    public sealed class Project : IDisposable
    {
        private readonly string folder = TestFolders.Create("edits-");

        public Project()
        {
            File.WriteAllText(PathOf("Edits.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
            File.WriteAllText(PathOf("Program.cs"), "class Program\n{\n    static void Main()\n    {\n        int four = Calc.Twice(2) + Calc.Four() + Sum.Of() + Scale.Two();\n    }\n}\n");
            File.WriteAllText(PathOf("Sum.cs"), "static class Sum\n{\n    public static int Of()\n    {\n        int total = 0;\n        total += 1;\n        total += 2;\n        return total;\n    }\n}\n");
            File.WriteAllText(PathOf("Two.cs"), "class First\n{\n}\n\nclass Second\n{\n}\n");
            File.WriteAllText(PathOf("Usings.cs"), "using System.Text;\nusing System.Collections.Generic;\nusing System;\n\nstatic class Usings\n{\n    public static int Count()\n    {\n"
                + "        List<string> names = [];\n        Console.WriteLine(names.Count);\n        return names.Count;\n    }\n}\n");
            File.WriteAllText(PathOf("Calc.cs"), "static class Calc\n{\n    public static int Twice(int value) => value * 2;\n\n    /// <summary>Never called.</summary>\n"
                + "    public static int Unused(int value) => value + 1;\n\n    public static int Four() => Twice(2);\n}\n");
            File.WriteAllText(PathOf("Scale.cs"), "static class Scale\n{\n    public static int Kilo(int value)\n    {\n        return value * 1024;\n    }\n\n    public static int Two() => Kilo(2);\n}\n");
            File.WriteAllText(PathOf("Counter.cs"), "class Counter\n{\n    public int count;\n\n    public void Add() => count++;\n}\n");
            TestFolders.Prepare(() => Restore.Run(folder), folder);
        }

        public string PathOf(string name) => Path.Combine(folder, name);

        /// <summary>A window on the project with a file open, once C# is ready.</summary>
        public MainWindow Open(string file)
        {
            MainWindow window = new() { Width = 1200, Height = 800, Autosaves = false };
            window.Show();
            window.OpenFolder(folder);
            window.OpenFile(file);
            Ui.Settle();
            Assert.True(window.CSharpService?.IsReady, window.CSharpService?.State);
            return window;
        }

        public void Dispose() => TestFolders.Delete(folder);
    }
}
