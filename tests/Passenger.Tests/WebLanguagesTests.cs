using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>JavaScript, TypeScript, HTML, CSS and JSON through the window, on an ASP.NET Core app (<c>dotnet new blazor</c>)
/// with a script under wwwroot, TypeScript under a tsconfig.json, a stylesheet, a package.json and a component with its
/// own &lt;style&gt; and &lt;script&gt;: each gets completion, a problem, go to declaration and rename from the language
/// servers Passenger carries under web/ (TypeScript's, and VS Code's for HTML, CSS and JSON), run on the machine's
/// Node.js, a Razor file's through Razor's own forwarding to the HTML server. Without a Node.js the files keep their
/// colours and say why. Each test opens its own window, so its own servers; a test that cannot find Node.js skips,
/// saying so.</summary>
public sealed class WebLanguagesTests : IClassFixture<WebLanguagesTests.Shop>
{
    private readonly Shop app;

    public WebLanguagesTests(Shop app) => this.app = app;

    [AvaloniaFact]
    public void A_script_completes_marks_a_syntax_error_goes_to_a_functions_declaration_and_renames_it_everywhere_it_is_called()
    {
        Shop.SkipWithoutNode(typescript: true);
        MainWindow window = app.Open("wwwroot/js/site.js");
        try
        {
            CodeEditor editor = window.EditorArea.CodeEditor;
            OpenDocument document = window.EditorArea.Active!;
            Ui.WaitFor(() => window.LanguageShown?.StartsWith("JavaScript · TypeScript ", StringComparison.Ordinal) == true,
                "the status bar to name the TypeScript the script is read by");
            Assert.EndsWith("(Passenger's)", window.LanguageShown, StringComparison.Ordinal);

            PlaceAfter(editor, "console.log(message);");
            Ui.Press(window, "ENTER");
            window.KeyTextInput("console");
            window.KeyTextInput(".");
            Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("log"), "console's members after its dot");
            window.KeyTextInput("war");
            Ui.WaitFor(() => window.Assist.CompletionLabels.FirstOrDefault() == "warn", "the list narrowed to warn");
            Ui.Press(window, "ENTER");
            Assert.Equal("console.warn", editor.Document.Snapshot.GetLineText(editor.CaretPosition.Line));

            editor.ApplyEdits([new TextEdit(editor.Document.Snapshot.Length, 0, "\nlet broken = ;\n")]);
            Ui.WaitFor(() => document.Markers.Any(m => m.Kind == MarkerKind.Error), "the squiggle under the statement that is not JavaScript");
            Marker error = document.Markers.First(m => m.Kind == MarkerKind.Error);
            Assert.StartsWith("TS1109: Expression expected", error.Message, StringComparison.Ordinal);
            Assert.Equal(";", editor.Document.Snapshot.GetText(error.Range.Start, Math.Max(1, error.Range.Length)));

            PlaceAfter(editor, "const message = gre");
            Ui.Press(window, Ui.Shortcut(AppActions.GoToDeclaration));
            Ui.WaitFor(() => editor.CaretPosition.Line == 1, "the caret at the function's declaration");
            Assert.Equal(new TextPosition(1, "function ".Length), editor.CaretPosition);

            Ui.Run(window, AppActions.Rename);
            Ui.WaitFor(() => window.Assist.Renaming == "greet", "the rename box on the function");
            window.KeyTextInput("welcome");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => editor.Document.Snapshot.ToString().Contains("const message = welcome(\"Ada\");", StringComparison.Ordinal),
                "the call renamed with the function");
            Assert.Contains("function welcome(name)", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
            app.Reset();
        }
    }

    [AvaloniaFact]
    public void TypeScript_under_a_tsconfig_completes_a_numbers_members_marks_a_type_error_goes_to_a_declaration_in_another_file_and_renames_across_files()
    {
        Shop.SkipWithoutNode(typescript: true);
        MainWindow window = app.Open("Scripts/items.ts");
        try
        {
            OpenDocument items = window.EditorArea.Active!;
            window.OpenFile(app.PathOf("Scripts/cart.ts"));
            Ui.Settle();
            CodeEditor editor = window.EditorArea.CodeEditor;
            OpenDocument cart = window.EditorArea.Active!;
            Ui.WaitFor(() => cart.Markers.Any(m => m.Kind == MarkerKind.Error), "the squiggle under the number given to a string");
            Marker error = Assert.Single(cart.Markers, m => m.Kind == MarkerKind.Error);
            Assert.Equal("label", editor.Document.Snapshot.GetText(error.Range.Start, error.Range.Length));
            Assert.StartsWith("TS2322: Type 'number' is not assignable to type 'string'", error.Message, StringComparison.Ordinal);

            PlaceAfter(editor, "console.log(label);");
            Ui.Press(window, "ENTER");
            window.KeyTextInput("sum");
            window.KeyTextInput(".");
            Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("toFixed"), "a number's members after its dot");
            window.KeyTextInput("toF");
            Ui.WaitFor(() => window.Assist.CompletionLabels.FirstOrDefault() == "toFixed", "the list narrowed to toFixed");
            Ui.Press(window, "ESCAPE");

            PlaceAfter(editor, "const sum: number = tot");
            Ui.Press(window, Ui.Shortcut(AppActions.GoToDeclaration));
            Ui.WaitFor(() => window.EditorArea.Active == items, "items.ts opened at the function cart.ts imports");
            Assert.Equal(new TextPosition(5, "export function ".Length), window.EditorArea.CodeEditor.CaretPosition);

            Ui.Run(window, AppActions.Rename);
            Ui.WaitFor(() => window.Assist.Renaming == "total", "the rename box on the function");
            window.KeyTextInput("sumOf");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => cart.Text.Snapshot.ToString().Contains("const sum: number = sumOf(items);", StringComparison.Ordinal),
                "the importing file's call renamed with the function");
            Assert.Contains("import { Item, sumOf } from \"./items\";", cart.Text.Snapshot.ToString(), StringComparison.Ordinal);
            Assert.Contains("export function sumOf(items: Item[]): number", items.Text.Snapshot.ToString(), StringComparison.Ordinal);

            CodeEditor shown = window.EditorArea.CodeEditor;
            shown.ApplyEdits([new TextEdit(shown.Document.Snapshot.Length, 0, "export const  spaced=sumOf( []);\n")]);
            Ui.Run(window, AppActions.Reformat);
            Ui.WaitFor(() => shown.Document.Snapshot.ToString().Contains("export const spaced = sumOf([]);", StringComparison.Ordinal),
                "the statement laid out by TypeScript's formatter");

            PlaceAfter(shown, "export function sumO");
            Ui.Run(window, AppActions.FindUsages);
            Passenger.App.ToolWindows.FindView find = window.Find;
            Ui.WaitFor(() => find.Rows.Count(r => r.Usage is not null) >= 4, "the function's usages, in both files, in the Find window");
            string[] usages = [.. find.Rows.Where(r => r.Usage is not null).Select(r => r.Label)];
            Assert.Contains("const sum: number = sumOf(items);", usages);
            Assert.Contains("import { Item, sumOf } from \"./items\";", usages);
        }
        finally
        {
            window.Close();
            app.Reset();
        }
    }

    [AvaloniaFact]
    public void A_stylesheet_completes_a_property_warns_of_an_unknown_one_shows_its_colours_goes_to_a_custom_propertys_declaration_and_renames_it()
    {
        Shop.SkipWithoutNode();
        MainWindow window = app.Open("wwwroot/css/site.css");
        try
        {
            CodeEditor editor = window.EditorArea.CodeEditor;
            OpenDocument document = window.EditorArea.Active!;
            Ui.WaitFor(() => document.ColourMarks.Count == 2, "a swatch beside each line that writes a colour");
            Assert.Equal([1, 6], document.ColourMarks.Select(m => m.Line));
            Assert.Equal(Avalonia.Media.Color.FromRgb(0x1b, 0x6e, 0xc2), document.ColourMarks[0].Colour);

            PlaceAfter(editor, "background-color: #ffffff;");
            Ui.Press(window, "ENTER");
            TypeEach(window, "bord");
            Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("border-radius"), "CSS's properties as one is typed");
            window.KeyTextInput("er-ra");
            Ui.WaitFor(() => window.Assist.CompletionLabels.FirstOrDefault() == "border-radius", "the list narrowed to border-radius");
            Ui.Press(window, "ENTER");
            int line = editor.CaretPosition.Line;
            Assert.StartsWith("    border-radius: ", editor.Document.Snapshot.GetLineText(line), StringComparison.Ordinal);
            window.KeyTextInput("4px");
            Assert.Equal("    border-radius: 4px;", editor.Document.Snapshot.GetLineText(line));

            // A value chosen from the list its colon opened, with nothing typed, goes in with the caret after it.
            Ui.Press(window, "END");
            Ui.Press(window, "ENTER");
            window.KeyTextInput("display");
            window.KeyTextInput(":");
            Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("flex"), "display's values after its colon");
            for (int down = window.Assist.CompletionLabels.ToList().IndexOf("flex"); down > 0; down--)
            {
                Ui.Press(window, "DOWN");
            }
            Ui.Press(window, "ENTER");
            window.KeyTextInput(";");
            Assert.Equal("    display:flex;", editor.Document.Snapshot.GetLineText(editor.CaretPosition.Line));

            PlaceAfter(editor, "border-radius: 4px;");
            editor.ApplyEdits([new TextEdit(editor.Selections.Primary.Caret, 0, "\n    colr: red;")]);
            Ui.WaitFor(() => document.Markers.Any(m => m.Kind == MarkerKind.Warning), "the warning under the property CSS has not");
            Marker warning = Assert.Single(document.Markers, m => m.Kind == MarkerKind.Warning);
            Assert.Equal("colr", editor.Document.Snapshot.GetText(warning.Range.Start, warning.Range.Length));
            Assert.Contains("Unknown property: 'colr'", warning.Message, StringComparison.Ordinal);

            PlaceAfter(editor, "color: var(--acc");
            Ui.Press(window, Ui.Shortcut(AppActions.GoToDeclaration));
            Ui.WaitFor(() => editor.CaretPosition.Line == 1, "the caret at the custom property's declaration");
            Assert.Equal(new TextPosition(1, 4), editor.CaretPosition);

            Ui.Run(window, AppActions.Rename);
            Ui.WaitFor(() => window.Assist.Renaming == "--accent", "the rename box on the whole custom property's name");
            window.KeyTextInput("--brand");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => editor.Document.Snapshot.ToString().Contains("color: var(--brand);", StringComparison.Ordinal), "its use renamed with it");
            Assert.Contains("--brand: #1b6ec2;", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);

            editor.ApplyEdits([new TextEdit(editor.Document.Snapshot.Length, 0, ".card{margin:0;padding:4px;}\n")]);
            Ui.Run(window, AppActions.Reformat);
            Ui.WaitFor(() => editor.Document.Snapshot.ToString().Contains(".card {\n    margin: 0;\n    padding: 4px;\n}", StringComparison.Ordinal),
                "the rule laid out by CSS's formatter, in the file's own indent");
        }
        finally
        {
            window.Close();
            app.Reset();
        }
    }

    [AvaloniaFact]
    public void Package_json_completes_and_checks_by_its_schema_goes_from_a_dependency_to_its_package_and_renames_a_script_with_its_runs()
    {
        Shop.SkipWithoutNode();
        MainWindow window = app.Open("package.json");
        try
        {
            CodeEditor editor = window.EditorArea.CodeEditor;
            OpenDocument document = window.EditorArea.Active!;
            int value = editor.Document.Snapshot.ToString().IndexOf("\"private\": true", StringComparison.Ordinal) + "\"private\": ".Length;
            editor.ApplyEdits([new TextEdit(value, "true".Length, "\"yes\"")]);
            Ui.WaitFor(() => document.Markers.Any(m => m.Kind == MarkerKind.Warning), "the warning where the value is not what the schema says");
            Marker warning = Assert.Single(document.Markers, m => m.Kind == MarkerKind.Warning);
            Assert.Equal("\"yes\"", editor.Document.Snapshot.GetText(warning.Range.Start, warning.Range.Length));
            Assert.Contains("Incorrect type. Expected \"boolean\"", warning.Message, StringComparison.Ordinal);

            PlaceAfter(editor, "\"version\": \"1.0.0\",");
            Ui.Press(window, "ENTER");
            window.KeyTextInput("\"");
            Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("description"), "package.json's properties, from its schema");
            window.KeyTextInput("descr");
            Ui.WaitFor(() => window.Assist.CompletionLabels.FirstOrDefault() == "description", "the list narrowed to description");
            Ui.Press(window, "ENTER");
            Assert.StartsWith("  \"description\": \"", editor.Document.Snapshot.GetLineText(editor.CaretPosition.Line), StringComparison.Ordinal);

            PlaceAfter(editor, "\"left-p");
            Ui.Press(window, Ui.Shortcut(AppActions.GoToDeclaration));
            Ui.WaitFor(() => window.EditorArea.Active?.Path == app.PathOf("node_modules/left-pad/package.json"), "the installed package's own package.json");

            window.OpenFile(app.PathOf("package.json"));
            Ui.Settle();
            editor = window.EditorArea.CodeEditor;
            PlaceAfter(editor, "\"bui");
            Ui.Run(window, AppActions.Rename);
            Ui.WaitFor(() => window.Assist.Renaming == "build", "the rename box on the script's name");
            window.KeyTextInput("compile");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => editor.Document.Snapshot.ToString().Contains("\"compile\": \"tsc -p .\"", StringComparison.Ordinal), "the script renamed");
            Assert.Contains("\"watch\": \"npm run compile -- --watch\"", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
            app.Reset();
        }
    }

    [AvaloniaFact]
    public void A_components_style_and_script_complete_check_go_to_declaration_and_rename_through_the_HTML_server()
    {
        Shop.SkipWithoutNode();
        MainWindow window = app.Open("Components/Pages/Styled.razor", csharp: true);
        try
        {
            CodeEditor editor = window.EditorArea.CodeEditor;
            OpenDocument document = window.EditorArea.Active!;

            PlaceAfter(editor, "color: #1b6ec2;");
            Ui.Press(window, "ENTER");
            TypeEach(window, "bord");
            Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("border-radius"), "CSS's properties in the component's <style>");
            Ui.Press(window, "ESCAPE");
            int typed = editor.Selections.Primary.Caret;
            editor.ApplyEdits([new TextEdit(typed - "bord".Length, "bord".Length, "border: 1px solid;\n        colr: red;")]);
            Ui.WaitFor(() => document.Markers.Any(m => m.Message.Contains("Unknown property: 'colr'", StringComparison.Ordinal)),
                "the warning under the property CSS has not, in the component's <style>");
            Marker warning = document.Markers.First(m => m.Message.Contains("colr", StringComparison.Ordinal));
            Assert.Equal("colr", editor.Document.Snapshot.GetText(warning.Range.Start, warning.Range.Length));

            int call = editor.Document.Snapshot.ToString().IndexOf("    shout(\"hi\")", StringComparison.Ordinal) + 6;
            editor.Selections = SelectionSet.Single(call);
            Ui.Press(window, Ui.Shortcut(AppActions.GoToDeclaration));
            int declared = editor.Document.Snapshot.ToString().IndexOf("function shout", StringComparison.Ordinal) + "function ".Length;
            Ui.WaitFor(() => editor.Selections.Primary.Caret == declared, "the caret at the script's function");

            Ui.Run(window, AppActions.Rename);
            Ui.WaitFor(() => window.Assist.Renaming == "shout", "the rename box on the script's function");
            window.KeyTextInput("yell");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => editor.Document.Snapshot.ToString().Contains("    yell(\"hi\");", StringComparison.Ordinal), "the call renamed with the function");
            Assert.Contains("function yell(text)", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);

            PlaceAfter(editor, "        return text");
            window.KeyTextInput(".");
            Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("toUpperCase"), "a string's members in the component's <script>");
        }
        finally
        {
            window.Close();
            app.Reset();
        }
    }

    [AvaloniaFact]
    public void Without_a_Node_js_a_script_keeps_its_colours_and_its_banner_and_the_status_bar_say_why()
    {
        string missing = Path.Combine(TestRun.Root, "no-node-here", OperatingSystem.IsWindows() ? "node.exe" : "node");
        MainWindow window = new() { Width = 1200, Height = 800, Autosaves = false };
        window.Show();
        try
        {
            window.ApplySettings(window.Settings with { NodeInterpreter = missing });
            window.OpenFolder(app.Folder);
            window.OpenFile(app.PathOf("wwwroot/js/site.js"));
            Ui.Settle();
            CodeEditor editor = window.EditorArea.CodeEditor;
            Ui.WaitFor(() => window.EditorArea.ActiveGroup.BannerText is not null, "the banner over the script");
            Assert.Equal($"JavaScript has its colours only: there is no Node.js at {missing}, which Settings | Languages & Frameworks | Node.js names.",
                window.EditorArea.ActiveGroup.BannerText);
            Assert.Equal("JavaScript · no Node.js", window.LanguageShown);
            Assert.Equal(TokenKind.Keyword, KindOf(editor, "function"));

            Ui.Run(window, AppActions.Rename);
            Assert.Equal($"Rename needs the TypeScript language server: there is no Node.js at {missing}, which Settings | Languages & Frameworks | Node.js names", window.Status);

            // A Node.js named that runs it, the server starts on it and the banner goes.
            if (Programs.Node() is { } node && Shop.TooOld() is null)
            {
                window.ApplySettings(window.Settings with { NodeInterpreter = node });
                Ui.WaitFor(() => window.EditorArea.ActiveGroup.BannerText is null, "the banner to go once a Node.js is named");
                Ui.WaitFor(() => window.LanguageShown?.StartsWith("JavaScript · TypeScript ", StringComparison.Ordinal) == true, "the script read by TypeScript");
            }
        }
        finally
        {
            window.ApplySettings(new Passenger.App.Settings.AppSettings());
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_project_with_its_own_typescript_is_read_by_it()
    {
        Shop.SkipWithoutNode(typescript: true);
        string folder = TestFolders.Create("own-typescript-");
        CopyFolder(Path.Combine(AppContext.BaseDirectory, "web", "node_modules", "typescript"), Path.Combine(folder, "node_modules", "typescript"));
        File.WriteAllText(Path.Combine(folder, "tsconfig.json"), "{ \"compilerOptions\": { \"strict\": true } }\n");
        File.WriteAllText(Path.Combine(folder, "main.ts"), "const n: number = 1;\n");
        MainWindow window = new() { Width = 1200, Height = 800, Autosaves = false };
        window.Show();
        try
        {
            window.OpenFolder(folder);
            window.OpenFile(Path.Combine(PathNames.Canonical(folder), "main.ts"));
            Ui.Settle();
            Ui.WaitFor(() => window.LanguageShown?.StartsWith("TypeScript · TypeScript ", StringComparison.Ordinal) == true, "the status bar to name the TypeScript in use");
            Assert.DoesNotContain("Passenger's", window.LanguageShown, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    /// <summary>Text typed a key at a time, as a person types it: a name's first letter is what opens completion.</summary>
    private static void TypeEach(MainWindow window, string text)
    {
        foreach (char c in text)
        {
            window.KeyTextInput(c.ToString());
        }
    }

    private static void PlaceAfter(CodeEditor editor, string text)
    {
        int at = editor.Document.Snapshot.ToString().IndexOf(text, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{text}' is not in the file");
        editor.Selections = SelectionSet.Single(at + text.Length);
    }

    /// <summary>The one kind a stretch of the text is coloured as, its spaces aside; null when its parts differ.</summary>
    private static TokenKind? KindOf(CodeEditor editor, string stretch)
    {
        TextSnapshot text = editor.Document.Snapshot;
        int start = text.ToString().IndexOf(stretch, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{stretch}' is not in the text");
        TokenKind?[] kinds = new TokenKind?[stretch.Length];
        Array.Fill(kinds, TokenKind.Text);
        foreach (SyntaxSpan span in editor.Highlighter!.GetSpans(text, start, stretch.Length))
        {
            for (int i = Math.Max(span.Start, start); i < Math.Min(span.End, start + stretch.Length); i++)
            {
                kinds[i - start] = span.Kind;
            }
        }
        TokenKind?[] seen = [.. kinds.Where((_, i) => !char.IsWhiteSpace(stretch[i])).Distinct()];
        return seen.Length == 1 ? seen[0] : null;
    }

    /// <summary>An ASP.NET Core app from <c>dotnet new blazor</c> with the files a web project carries beside its C#:
    /// wwwroot/js/site.js, TypeScript under Scripts/ with its tsconfig.json, wwwroot/css/site.css, a package.json with a
    /// dependency installed in node_modules, and a component with a &lt;style&gt; and a &lt;script&gt;; restored once for
    /// every test, never built. The tests share it and put back what they changed.</summary>
    public sealed class Shop : IDisposable
    {
        public const string Script = "// Greets whoever opens the page.\nfunction greet(name) {\n    return \"Hello, \" + name;\n}\n\nconst message = greet(\"Ada\");\nconsole.log(message);\n";

        public const string Items = "export interface Item {\n    name: string;\n    price: number;\n}\n\nexport function total(items: Item[]): number {\n"
            + "    return items.reduce((sum, item) => sum + item.price, 0);\n}\n";

        public const string Cart = "import { Item, total } from \"./items\";\n\nconst items: Item[] = [{ name: \"tea\", price: 3 }];\nconst sum: number = total(items);\n"
            + "const label: string = sum;\nconsole.log(label);\n";

        public const string Config = "{\n  // The scripts compile to wwwroot/js.\n  \"compilerOptions\": {\n    \"target\": \"ES2022\",\n    \"module\": \"ES2022\",\n"
            + "    \"strict\": true,\n    \"outDir\": \"wwwroot/js/out\"\n  },\n  \"include\": [\"Scripts/**/*.ts\"]\n}\n";

        public const string Style = ":root {\n    --accent: #1b6ec2;\n}\n\n.btn-primary {\n    color: var(--accent);\n    background-color: #ffffff;\n}\n";

        public const string Package = "{\n  \"name\": \"shop-client\",\n  \"version\": \"1.0.0\",\n  \"private\": true,\n  \"scripts\": {\n"
            + "    \"build\": \"tsc -p .\",\n    \"watch\": \"npm run build -- --watch\"\n  },\n  \"devDependencies\": {\n    \"left-pad\": \"^1.3.0\"\n  }\n}\n";

        public const string Component = "@page \"/styled\"\n\n<h1 class=\"title\">Styled</h1>\n\n<style>\n    .title {\n        color: #1b6ec2;\n    }\n</style>\n\n"
            + "<script>\n    function shout(text) {\n        return text.toUpperCase();\n    }\n    shout(\"hi\");\n</script>\n";

        private static readonly (string Path, string Text)[] Files =
        [
            ("wwwroot/js/site.js", Script),
            ("Scripts/items.ts", Items),
            ("Scripts/cart.ts", Cart),
            ("tsconfig.json", Config),
            ("wwwroot/css/site.css", Style),
            ("package.json", Package),
            ("node_modules/left-pad/package.json", "{\n  \"name\": \"left-pad\",\n  \"version\": \"1.3.0\",\n  \"main\": \"index.js\"\n}\n"),
            ("node_modules/left-pad/index.js", "module.exports = (text, length) => text.padStart(length);\n"),
            ("Components/Pages/Styled.razor", Component),
        ];

        public Shop()
        {
            Folder = TestFolders.Create("web-");
            TestFolders.Prepare(() =>
            {
                BlazorApp.Create(Folder);
                Reset();
                Restore.Run(Folder);
            }, Folder);
        }

        public string Folder { get; }

        public string PathOf(string relative) => Path.Combine(PathNames.Canonical(Folder), relative);

        /// <summary>Every file as written, whatever a test saved.</summary>
        public void Reset()
        {
            foreach ((string path, string text) in Files)
            {
                string full = Path.Combine(Folder, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
            }
        }

        /// <summary>A window on the app with a file open, its server started; with <paramref name="csharp"/>, once C# is
        /// ready (a Razor file's HTML comes through it).</summary>
        public MainWindow Open(string file, bool csharp = false)
        {
            MainWindow window = new() { Width = 1200, Height = 800, Autosaves = false };
            window.Show();
            window.OpenFolder(Folder);
            window.OpenFile(PathOf(file));
            Ui.Settle();
            if (csharp)
            {
                Assert.True(window.CSharpService?.IsReady, window.CSharpService?.State);
            }
            return window;
        }

        /// <summary>Skips a test that needs the web language servers where there is no Node.js to run them, or, for one that
        /// needs the TypeScript server (<paramref name="typescript"/>), one older than it needs.</summary>
        public static void SkipWithoutNode(bool typescript = false)
        {
            Assert.SkipWhen(Programs.Node() is null, "Node.js is not on the PATH, and the web language servers run on it");
            if (typescript)
            {
                string? old = TooOld();
                Assert.SkipWhen(old is not null, $"The TypeScript language server cannot run here: {old}");
            }
        }

        /// <summary>Why the PATH's Node.js cannot run the TypeScript server, or null when it can.</summary>
        public static string? TooOld()
        {
            Passenger.App.Web.NodeRuntime node = Task.Run(() => Passenger.App.Web.NodeRuntime.FindAsync(null)).GetAwaiter().GetResult();
            return node.Problem ?? node.TooOldFor(Path.Combine(AppContext.BaseDirectory, "web", "node_modules", "typescript-language-server", "package.json"), "the TypeScript language server");
        }

        public void Dispose() => TestFolders.Delete(Folder);
    }
}
