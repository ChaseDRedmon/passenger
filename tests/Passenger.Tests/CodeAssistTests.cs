using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Navigation;
using Passenger.App.ToolWindows;
using Passenger.App.Workspace;
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
    public void A_commit_character_types_the_chosen_item_before_it_and_the_items_documentation_shows_beside_the_list()
    {
        MainWindow window = project.Open("Program.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "static void Main()\n    {\n");
        window.KeyTextInput("        System.");
        Ui.Settle();
        window.KeyTextInput("C");
        Ui.Settle();
        window.KeyTextInput("onso");
        Ui.Settle();
        Assert.Equal("Console", window.Assist.CompletionLabels.FirstOrDefault());
        Ui.WaitFor(() => window.Assist.ItemDocumentation?.Contains("standard input", StringComparison.Ordinal) == true, "the chosen item's documentation");

        // The dot commits Console and is typed after it, and lists Console's members in turn.
        window.KeyTextInput(".");
        Ui.Settle();
        Assert.True(editor.Document.Snapshot.ToString().Contains("System.Console.", StringComparison.Ordinal), editor.Document.Snapshot.ToString());
        Ui.WaitFor(() => window.Assist.IsCompletionOpen, "Console's members");
        window.KeyTextInput("WriteL");
        Ui.Settle();
        window.KeyTextInput("(");
        Ui.Settle();
        Assert.Contains("System.Console.WriteLine(", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
        Assert.False(window.Assist.IsCompletionOpen);
        Assert.Null(window.Assist.ItemDocumentation);
        window.Close();
    }

    [AvaloniaFact]
    public void Find_usages_groups_them_by_what_they_do_project_and_file_previews_the_chosen_one_and_keeps_each_search()
    {
        MainWindow window = project.Open("Loop.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "int tot");
        Ui.Run(window, AppActions.FindUsages);
        FindView find = window.Find;
        Ui.WaitFor(() => find.Rows.Count > 0, "the usages in the Find window");
        Assert.True(window.DockHost.IsShown("find"));
        Assert.Equal(["Declaration", "Write access", "Read access"], find.Rows.Where(r => r.Depth == 0).Select(r => r.Label));
        Assert.All(find.Rows.Where(r => r.Depth == 1), r => Assert.Equal("Sample", r.Label));
        Assert.All(find.Rows.Where(r => r.Depth == 2), r => Assert.Equal("Loop.cs", r.Label));
        Assert.Equal(["int total = 0;", "total += i * 2;", "return total;"], find.Rows.Where(r => r.Usage is not null).Select(r => r.Label));

        // The first usage is chosen, its line shown beside the tree with the name marked.
        Assert.Equal("int total = 0;", find.Selected?.Label);
        Assert.Contains("for (int i = 0", find.Preview.Document.Snapshot.ToString(), StringComparison.Ordinal);
        TextRange marked = Assert.Single(find.Preview.Highlights);
        Assert.Equal("total", find.Preview.Document.Snapshot.GetText(marked.Start, marked.Length));

        // Another search has a tab of its own beside the first.
        window.OpenFile(project.PathOf("Program.cs"));
        PlaceAfter(window.EditorArea.CodeEditor, "Helper.Twi");
        Ui.Run(window, AppActions.FindUsages);
        Ui.WaitFor(() => find.Shown == "Usages of Twice", "the second search");
        Assert.Equal(["Usages of total", "Usages of Twice"], find.Searches);
        Assert.Equal(["Declaration", "Invocation"], find.Rows.Where(r => r.Depth == 0).Select(r => r.Label));
        find.ShowSearch("Usages of total");
        Assert.Equal(3, find.Rows.Count(r => r.Usage is not null));

        // Enter goes to the usage chosen.
        find.Selected = find.Rows.Last(r => r.Usage is not null);
        find.FocusRows();
        Ui.Press(window, "ENTER");
        Assert.Equal("Loop.cs", window.EditorArea.Active?.Name);
        Assert.Equal(new TextPosition(9, 15), window.EditorArea.CodeEditor.CaretPosition);
        window.Close();
    }

    [AvaloniaFact]
    public void The_error_stripe_marks_a_problem_in_its_colour_says_what_it_is_and_a_click_goes_there()
    {
        MainWindow window = project.Open("Loop.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        Assert.Equal((0, 0), editor.ProblemCount);
        PlaceAfter(editor, "        return total;");
        window.KeyTextInput(" int wrong = \"text\";");
        Ui.WaitFor(() => editor.ProblemCount.Errors == 1, "the error's diagnostic");
        CodeEditor.StripeMark error = Assert.Single(editor.StripeMarks, m => m.Kind == StripeMarkKind.Error);
        Assert.Contains("convert", error.Message, StringComparison.Ordinal);
        Assert.Contains(editor.StripeMarks, m => m.Kind == StripeMarkKind.Caret);

        Point at = editor.TranslatePoint(editor.StripePoint(error), window)!.Value;

        // A click on it takes the caret to the error's line.
        editor.GoTo(new TextPosition(0, 0));
        Ui.Settle();
        window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        Ui.Settle();
        Assert.Equal(editor.Document.Snapshot.GetLineFromOffset(error.Offset), editor.CaretPosition.Line);

        // Drawn in the error colour on the scrollbar's track, over the thumb.
        Avalonia.Media.Color colour = Ui.Pixel(window, at);
        Assert.True(colour.R > 180 && colour.G < 140 && colour.B < 140, $"The error's mark is {colour}, not the error colour");

        window.Close();
    }

    [AvaloniaFact]
    public void A_right_click_on_a_call_puts_the_caret_there_and_its_menu_renames_it_and_goes_to_an_implementation()
    {
        string helper = File.ReadAllText(project.PathOf("Helper.cs"));
        MainWindow window = project.Open("Program.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        try
        {
            int call = editor.Document.Snapshot.ToString().IndexOf("Twice", StringComparison.Ordinal) + 2;
            RightClick(window, editor, call);
            Assert.InRange(editor.Selections.Primary.Caret, call - 1, call + 1);
            Choose(window, "Refactor", "Rename...");
            window.KeyTextInput("Double");
            Ui.Press(window, "ENTER");
            Ui.Settle();
            Assert.Contains("Helper.Double(", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);

            window.OpenFile(project.PathOf("Shapes.cs"));
            Ui.Settle();
            int sides = editor.Document.Snapshot.ToString().IndexOf("Sides", StringComparison.Ordinal) + 1;
            RightClick(window, editor, sides);
            Choose(window, "Go To", "Go to Implementation");
            Ui.WaitFor(() => editor.CaretPosition.Line == 7, "the implementation in Square");
            Assert.Null(window.OpenContextMenu);
        }
        finally
        {
            File.WriteAllText(project.PathOf("Helper.cs"), helper);
            window.Close();
        }
    }

    private static void RightClick(MainWindow window, CodeEditor editor, int offset)
    {
        Point at = editor.TranslatePoint(editor.PointOf(offset) + new Vector(1, editor.LineHeight / 2), window)!.Value;
        window.MouseDown(at, Avalonia.Input.MouseButton.Right);
        window.MouseUp(at, Avalonia.Input.MouseButton.Right);
        Ui.Settle();
    }

    /// <summary>Chooses a submenu's item in the open context menu, as clicks on them would.</summary>
    private static void Choose(MainWindow window, string submenu, string item)
    {
        Avalonia.Controls.ContextMenu menu = window.OpenContextMenu ?? throw new InvalidOperationException("No context menu is open");
        Avalonia.Controls.MenuItem parent = menu.Items.OfType<Avalonia.Controls.MenuItem>().First(i => (string?)i.Header == submenu);
        Avalonia.Controls.MenuItem chosen = parent.Items.OfType<Avalonia.Controls.MenuItem>().First(i => (string?)i.Header == item);
        Assert.True(chosen.IsEnabled, $"{submenu}, {item} is disabled");
        chosen.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
        menu.Close();
        Ui.Settle();
    }

    [AvaloniaFact]
    public void A_reformat_started_before_switching_tabs_changes_only_its_own_file()
    {
        MainWindow window = project.Open("Messy.cs");
        string helper = File.ReadAllText(project.PathOf("Helper.cs"));
        window.Perform(AppActions.Reformat);
        // Before the server answers, another file is shown.
        window.OpenFile(project.PathOf("Helper.cs"));
        Ui.Settle();
        Passenger.App.Workspace.OpenDocument messy = window.EditorArea.Documents.First(d => d.Name == "Messy.cs");
        Ui.WaitFor(() => messy.Text.Snapshot.ToString().Contains("        List<string> names = [];", StringComparison.Ordinal), "Messy.cs reformatted");
        Assert.Equal("Helper.cs", window.EditorArea.Active!.Name);
        Assert.Equal(helper, window.EditorArea.CodeEditor.Document.Snapshot.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void Code_typed_below_a_comment_holding_line_breaks_only_roslyn_counts_is_squiggled_coloured_and_reformatted_in_place()
    {
        MainWindow window = project.Open("Separators.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        OpenDocument document = window.EditorArea.Active!;
        string before = editor.Document.Snapshot.ToString();
        PlaceAfter(editor, "int total = 1;");
        window.KeyTextInput(" int wrong = \"text\";");

        Ui.WaitFor(() => document.Markers.Any(m => m.Kind == MarkerKind.Error), "the squiggle under the error typed");
        TextSnapshot text = editor.Document.Snapshot;
        Marker error = Assert.Single(document.Markers, m => m.Kind == MarkerKind.Error);
        Assert.Equal("\"text\"", text.GetText(error.Range));
        Assert.Equal(5, text.GetLineFromOffset(error.Range.Start));

        // A use of the local is coloured as one only by the server, whose colours must land on the editor's line.
        int use = text.ToString().IndexOf("return total", StringComparison.Ordinal) + "return ".Length;
        Ui.WaitFor(() => document.Highlighter!.GetSpans(editor.Document.Snapshot, use, 1).Any(s => s.Start == use && s.Kind == TokenKind.Local),
            "the server's colour on the local's use");

        Ui.Run(window, AppActions.Reformat);
        Ui.WaitFor(() => window.Status is "Reformatted" or "Already formatted", "the reformat");
        Assert.Equal(before.Replace("int total = 1;\n          return", "int total = 1; int wrong = \"text\";\n        return", StringComparison.Ordinal),
            editor.Document.Snapshot.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void A_line_typed_while_a_rename_is_out_still_leaves_the_new_name_on_the_symbol()
    {
        string helper = File.ReadAllText(project.PathOf("Helper.cs"));
        MainWindow window = project.Open("Program.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        try
        {
            PlaceAfter(editor, "Helper.Tw");
            Ui.Run(window, AppActions.Rename);
            window.KeyTextInput("Double");
            Ui.Settle();
            // Enter sends the rename; a line is typed above before the answer comes.
            window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
            editor.ApplyEdits([new TextEdit(0, 0, "// typed meanwhile\n")]);
            Ui.Settle();
            string text = editor.Document.Snapshot.ToString();
            Assert.StartsWith("// typed meanwhile\nclass Program", text, StringComparison.Ordinal);
            Assert.Contains("int result = Helper.Double(2);", text, StringComparison.Ordinal);
        }
        finally
        {
            File.WriteAllText(project.PathOf("Helper.cs"), helper);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Opening_the_open_folder_again_through_mcp_keeps_the_open_files_language_server()
    {
        MainWindow window = project.Open("Program.cs");
        Passenger.App.Workspace.OpenDocument program = window.EditorArea.Active!;
        Passenger.App.CSharp.CSharpService server = window.CSharpService!;
        Passenger.App.Mcp.PassengerTools tools = new(window);

        // Agents open the workspace first, often the one already open.
        Task<Passenger.App.Mcp.Workspace> opened = tools.Open(Path.GetDirectoryName(project.PathOf("Program.cs"))!);
        Ui.WaitFor(() => opened.IsCompleted, "the open", TimeSpan.FromSeconds(10));
        Assert.Same(server, window.CSharpService);
        Assert.NotNull(server.For(program));
        Task<IReadOnlyList<Passenger.App.Mcp.Finding>> found = tools.Diagnostics("Program.cs");
        Ui.WaitFor(() => found.IsCompleted, "the diagnostics of the file open before", TimeSpan.FromSeconds(15));
        Assert.True(found.IsCompletedSuccessfully, found.Exception?.Message);
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
    public void Renaming_a_file_to_match_its_type_with_unsaved_edits_leaves_one_file_of_the_new_name_holding_them()
    {
        string foo = project.PathOf("Foo.cs");
        string bar = project.PathOf("Bar.cs");
        File.WriteAllText(foo, "class Bar\n{\n}\n");
        MainWindow window = project.Open("Foo.cs");
        try
        {
            CodeEditor editor = window.EditorArea.CodeEditor;
            OpenDocument document = window.EditorArea.Active!;
            PlaceAfter(editor, "{");
            window.KeyTextInput(" public int Edited; ");
            PlaceAfter(editor, "class B");
            Ui.Run(window, AppActions.QuickFix);
            Ui.WaitFor(() => window.Assist.Offered.Contains("Rename file to Bar.cs"), "the offer to rename the file");
            for (int i = window.Assist.Offered.ToList().IndexOf("Rename file to Bar.cs"); i > 0; i--)
            {
                Ui.Press(window, "DOWN");
            }
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => !File.Exists(foo), "Foo.cs to be renamed");

            Assert.True(File.ReadAllText(bar) == "class Bar\n{ public int Edited; \n}\n", "Bar.cs does not hold the edits made before the rename");
            Assert.True(ReferenceEquals(document, Assert.Single(window.EditorArea.Documents)), "The renamed file's tab did not move to Bar.cs");
            Assert.Equal(PathNames.Canonical(bar), document.Path);
            Assert.Equal("Bar.cs", window.EditorArea.Active?.Name);
            Assert.EndsWith("/Bar.cs", window.CSharpService?.For(document)?.Lsp?.Uri, StringComparison.Ordinal);

            // A later save writes Bar.cs, never Foo.cs back.
            window.KeyTextInput("// ");
            window.EditorArea.SaveAll();
            Assert.False(File.Exists(foo), "Saving the moved file wrote Foo.cs back");
            Assert.StartsWith("class B// ar", File.ReadAllText(bar), StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
            File.Delete(foo);
            File.Delete(bar);
        }
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
        MainWindow window = project.Open("Area.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        int argument = editor.Document.Snapshot.ToString().IndexOf("Of(3, 4)", StringComparison.Ordinal) + "Of(".Length;
        Ui.WaitFor(() => editor.InlayHints.Count == 2, "the parameter name hints");
        Assert.Equal([("width:", argument), ("height:", argument + "3, ".Length)], editor.InlayHints.Select(h => (h.Label, h.Offset)));

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

    [AvaloniaFact]
    public void An_abbreviation_and_tab_become_rider_s_live_template_where_it_belongs()
    {
        MainWindow window = project.Open("Point.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        // In a type's body, prop is a property, its type selected to be typed over.
        PlaceAfter(editor, "public int Y { get; }");
        editor.Focus();
        Ui.Press(window, "ENTER");
        window.KeyTextInput("prop");
        Ui.Settle();
        Ui.Press(window, "TAB");
        Assert.True(editor.IsInSnippet);
        Assert.Contains("    public int Name { get; set; }", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
        Assert.Equal("int", editor.Document.Snapshot.GetText(editor.Selections.Primary.Start, editor.Selections.Primary.Length));
        window.KeyTextInput("string");
        Ui.Press(window, "TAB");
        window.KeyTextInput("Label");
        Ui.Press(window, "TAB");
        Assert.False(editor.IsInSnippet);
        Assert.Contains("    public string Label { get; set; }\n}", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
        window.Close();

        // In a method's body, cw is a Console.WriteLine with the caret in its parentheses.
        window = project.Open("Loop.cs");
        editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "int total = 0;");
        editor.Focus();
        Ui.Press(window, "ENTER");
        window.KeyTextInput("cw");
        Ui.Settle();
        Ui.Press(window, "TAB");
        string text = editor.Document.Snapshot.ToString();
        Assert.Contains("        Console.WriteLine();\n", text, StringComparison.Ordinal);
        Assert.Equal(text.IndexOf("Console.WriteLine(", StringComparison.Ordinal) + "Console.WriteLine(".Length, editor.Selections.Primary.Caret);
        window.Close();
    }

    [AvaloniaFact]
    public void A_postfix_template_after_an_expression_and_a_dot_wraps_it_where_it_fits()
    {
        MainWindow window = project.Open("Loop.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "int total = 0;");
        editor.Focus();
        string Text() => editor.Document.Snapshot.ToString();
        string Selected() => editor.Document.Snapshot.GetText(editor.Selections.Primary.Start, editor.Selections.Primary.Length);

        // .foreach wraps the whole call chain, its variable to be named first.
        Ui.Press(window, "ENTER");
        window.KeyTextInput("Enumerable.Range(0, 3).foreach");
        Ui.Settle();
        Ui.Press(window, "TAB");
        Assert.Contains("        foreach (var item in Enumerable.Range(0, 3))\n        {\n", Text(), StringComparison.Ordinal);
        Assert.Equal("item", Selected());
        Ui.Press(window, "ESCAPE");

        // .var names what it holds, the name selected to be typed over.
        PlaceAfter(editor, "int total = 0;");
        Ui.Press(window, "ENTER");
        window.KeyTextInput("total.var");
        Ui.Settle();
        Ui.Press(window, "TAB");
        Assert.Contains("        var value = total;", Text(), StringComparison.Ordinal);
        window.KeyTextInput("copy");
        Ui.Press(window, "TAB");
        Assert.Contains("        var copy = total;", Text(), StringComparison.Ordinal);

        // In the middle of an expression only an expression's template fits: .par wraps the name before it, .if is not offered.
        PlaceAfter(editor, "var copy = total;");
        Ui.Press(window, "ENTER");
        window.KeyTextInput("int doubled = 2 * total.par");
        Ui.Settle();
        Ui.Press(window, "TAB");
        Assert.Contains("        int doubled = 2 * (total)\n", Text(), StringComparison.Ordinal);
        Ui.Press(window, "ESCAPE");
        window.KeyTextInput(" + total.if");
        Ui.Settle();
        Ui.Press(window, "ESCAPE");
        Ui.Press(window, "TAB");
        Assert.DoesNotContain("if (", Text(), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Call_and_type_hierarchies_show_who_calls_a_method_and_what_derives_from_a_type()
    {
        MainWindow window = project.Open("Helper.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "static int Tw");
        Task<string?> calls = window.ShowCallHierarchyAsync();
        Ui.WaitFor(() => calls.IsCompleted, "the call hierarchy");
        Assert.Null(calls.Result);
        Assert.True(window.DockHost.IsShown("hierarchy"));
        Assert.Equal(2, window.Hierarchy.Rows.Count);
        Assert.Contains("Twice", window.Hierarchy.Rows[0].Node.Item.Name, StringComparison.Ordinal);
        Assert.Contains("Main", window.Hierarchy.Rows[1].Node.Item.Name, StringComparison.Ordinal);
        HierarchyRow caller = window.Hierarchy.Rows[1];
        Assert.Equal(PathNames.Canonical(project.PathOf("Program.cs")), caller.Node.Path);
        Assert.Equal(4, caller.Node.Where.Line);

        // Callees of Twice: none it calls of the project's own.
        Task callees = window.Hierarchy.ChooseAsync(first: false);
        Ui.WaitFor(() => callees.IsCompleted, "the callees");
        Assert.False(window.Hierarchy.IsFirst);
        window.Close();

        window = project.Open("Shapes.cs");
        editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "interface ISh");
        Task<string?> types = window.ShowTypeHierarchyAsync();
        Ui.WaitFor(() => types.IsCompleted, "the type hierarchy");
        Task subtypes = window.Hierarchy.ChooseAsync(first: false);
        Ui.WaitFor(() => subtypes.IsCompleted, "the subtypes");
        Assert.Contains(window.Hierarchy.Rows, r => r.Depth == 1 && r.Node.Item.Name.Contains("Square", StringComparison.Ordinal));
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
            File.WriteAllText(PathOf("Shapes.cs"), "interface IShape\n{\n    int Sides { get; }\n}\n\nclass Square : IShape\n{\n    public int Sides => 4;\n}\n");
            File.WriteAllText(PathOf("Area.cs"), "static class Area\n{\n    public static int Of(int width, int height) => width * height;\n    public static int Room() => Of(3, 4);\n}\n");
            File.WriteAllText(PathOf("Point.cs"), "class Point\n{\n    public int X { get; }\n    public int Y { get; }\n}\n");
            File.WriteAllText(PathOf("Loop.cs"), "static class Loop\n{\n    public static int Sum()\n    {\n        int total = 0;\n        for (int i = 0; i < 10; i++)\n        {\n            total += i * 2;\n        }\n        return total;\n    }\n}\n");
            // Roslyn ends a line at each of these in the comment; the editor's lines end only at '\n'.
            File.WriteAllText(PathOf("Separators.cs"), "static class Separators\n{\n    /* Pasted:\u2028a line separator,\u2029a paragraph separator,\u0085a next line,\ra carriage return. */\n    public static int Value()\n    {\n        int total = 1;\n          return total;\n    }\n}\n");
            File.WriteAllText(PathOf("Messy.cs"), "using System.Text;\nusing System;\nusing System.Collections.Generic;\n\nstatic class Messy\n{\n    public static int Count()\n    {\n          List<string> names = [];\n        Console.WriteLine(names.Count);\n      return names.Count;\n    }\n}\n");
            using Process restore = Process.Start(new ProcessStartInfo("dotnet", ["restore", folder]) { RedirectStandardOutput = true, RedirectStandardError = true })!;
            restore.WaitForExit();
        }

        public string PathOf(string name) => Path.Combine(folder, name);

        /// <summary>A window on the project with a file open, once C# is ready. The tests share the project, so a
        /// window never saves what its test typed.</summary>
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
