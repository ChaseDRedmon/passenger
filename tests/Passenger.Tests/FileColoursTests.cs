using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Passenger.App;
using Passenger.Editor;
using Passenger.Languages.TextMate;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>The files beside the C# in colour, as Rider colours them: a project file, settings with comments, a CI
/// workflow, a README and a script each open with their tokens in their kinds' colours, in either theme; typing keeps
/// the colours right however far a comment or a code fence it opens or closes reaches; Comment with Line Comment and
/// with Block Comment write each language's own comments; a tag's name lights up with its pair's; and a key in a long
/// file costs the lines it changed.</summary>
public sealed class FileColoursTests : IDisposable
{
    private const string Project = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project Sdk="Microsoft.NET.Sdk">
          <!-- The build: an executable,
               with its analyzers. -->
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <Description>Shapes &amp; their areas</Description>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Serilog" Version="4.0.0" />
            <None Update="appsettings.json">
              <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
            </None>
          </ItemGroup>
        </Project>

        """;

    private const string Settings = """
        {
          // Warning and up, unless a category says otherwise.
          "Logging": {
            "LogLevel": {
              "Default": "Warning"
            }
          },
          /* The hosts it answers on. */
          "AllowedHosts": "*",
          "Retries": 3,
          "UseCache": true
        }

        """;

    private const string Workflow = """
        # Builds and tests every push.
        name: CI
        on:
          push:
            branches: [ main ]
        jobs:
          build:
            runs-on: ubuntu-latest
            steps:
              - uses: actions/setup-dotnet@v4
                with:
                  dotnet-version: '10.0.x'
              - name: Test
                run: dotnet test -c Release
                timeout-minutes: 30

        """;

    private const string Readme = """
        # Shapes

        A library of **shapes** and their *areas*.

        ## Getting started

        Build it with `dotnet build`, from the [.NET SDK](https://dot.net/download).

        ```bash
        dotnet test --filter "Category!=Slow"
        ```

        > Areas are in square units.

        """;

    private const string Script = """
        #!/bin/sh
        # Packs every project.
        set -eu
        output="artifacts/packages"
        for project in src/*/*.csproj; do
            if [ -f "$project" ]; then
                dotnet pack "$project" -o "$output"
            fi
        done

        """;

    private readonly string folder = TestFolders.Create("colours-");

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void A_project_file_settings_a_workflow_a_readme_and_a_script_each_show_their_tokens_in_Riders_colours()
    {
        MainWindow window = Window();
        CodeEditor editor = Open(window, "Shapes.csproj", Project);
        Assert.Equal(TokenKind.Tag, KindOf(editor, "<Project"));
        Assert.Equal(TokenKind.AttributeName, KindOf(editor, "Sdk"));
        Assert.Equal(TokenKind.AttributeValue, KindOf(editor, "\"Microsoft.NET.Sdk\""));
        Assert.Equal(TokenKind.Comment, KindOf(editor, "with its analyzers. -->"));
        Assert.Equal(TokenKind.Tag, KindOf(editor, "</OutputType>"));
        Assert.Equal(TokenKind.Text, KindOf(editor, "Exe<"[..3]));
        Assert.Equal(TokenKind.Entity, KindOf(editor, "&amp;"));

        editor = Open(window, "appsettings.json", Settings);
        Assert.Equal(TokenKind.Comment, KindOf(editor, "// Warning and up, unless a category says otherwise."));
        Assert.Equal(TokenKind.Comment, KindOf(editor, "/* The hosts it answers on. */"));
        Assert.Equal(TokenKind.JsonKey, KindOf(editor, "\"Logging\""));
        Assert.Equal(TokenKind.String, KindOf(editor, "\"Warning\""));
        Assert.Equal(TokenKind.Number, KindOf(editor, "3"));
        Assert.Equal(TokenKind.Keyword, KindOf(editor, "true"));

        editor = Open(window, Path.Combine(".github", "workflows", "ci.yml"), Workflow);
        Assert.Equal(TokenKind.Comment, KindOf(editor, "# Builds and tests every push."));
        Assert.Equal(TokenKind.YamlKey, KindOf(editor, "runs-on"));
        // A workflow's first key reads as YAML 1.1's boolean, but it is a key, as Rider colours it.
        Assert.Equal(TokenKind.YamlKey, KindOf(editor, "on:"[..2]));
        Assert.Equal(TokenKind.Text, KindOf(editor, "ubuntu-latest"));
        Assert.Equal(TokenKind.Text, KindOf(editor, "30"));
        Assert.Equal(TokenKind.String, KindOf(editor, "'10.0.x'"));
        Assert.Equal(TokenKind.Text, KindOf(editor, "dotnet test -c Release"));

        editor = Open(window, "README.md", Readme);
        Assert.Equal(TokenKind.Keyword, KindOf(editor, "##"));
        Assert.Equal(TokenKind.Heading, KindOf(editor, "Getting started"));
        Assert.Equal(TokenKind.Bold, KindOf(editor, "shapes"));
        Assert.Equal(TokenKind.Italic, KindOf(editor, "areas"));
        Assert.Equal(TokenKind.Code, KindOf(editor, "`dotnet build`"));
        Assert.Equal(TokenKind.Link, KindOf(editor, ".NET SDK"));
        Assert.Equal(TokenKind.LinkDestination, KindOf(editor, "https://dot.net/download"));
        Assert.Equal(TokenKind.Quote, KindOf(editor, "Areas are in square units."));
        // A fenced block in a language with a grammar is coloured as that language.
        Assert.Equal(TokenKind.String, KindOf(editor, "\"Category!=Slow\""));

        editor = Open(window, "pack.sh", Script);
        Assert.Equal(TokenKind.Comment, KindOf(editor, "# Packs every project."));
        Assert.Equal(TokenKind.Keyword, KindOf(editor, "for"));
        Assert.Equal(TokenKind.Keyword, KindOf(editor, "then"));
        Assert.Equal(TokenKind.String, KindOf(editor, "\"$project\""));
        Assert.Equal(TokenKind.Text, KindOf(editor, "dotnet pack"));
        window.Close();
    }

    [AvaloniaFact]
    public void A_web_page_its_style_and_script_and_a_Dockerfile_show_their_tokens_too()
    {
        MainWindow window = Window();
        CodeEditor editor = Open(window, "index.html", "<!DOCTYPE html>\n<html lang=\"en\">\n<!-- The list. -->\n<ul class=\"list\">&amp;</ul>\n<script>\n    const shapes = [\"circle\"];\n</script>\n</html>\n");
        Assert.Equal(TokenKind.Tag, KindOf(editor, "<ul"));
        Assert.Equal(TokenKind.AttributeName, KindOf(editor, "class"));
        Assert.Equal(TokenKind.AttributeValue, KindOf(editor, "\"list\""));
        Assert.Equal(TokenKind.Comment, KindOf(editor, "<!-- The list. -->"));
        Assert.Equal(TokenKind.Entity, KindOf(editor, "&amp;"));
        Assert.Equal(TokenKind.Keyword, KindOf(editor, "const"));
        Assert.Equal(TokenKind.String, KindOf(editor, "\"circle\""));

        editor = Open(window, "site.css", "/* The page. */\nbody > .list {\n    margin: 0 auto 12px;\n    color: var(--accent);\n}\n");
        Assert.Equal(TokenKind.Comment, KindOf(editor, "/* The page. */"));
        Assert.Equal(TokenKind.Tag, KindOf(editor, ".list"));
        Assert.Equal(TokenKind.Keyword, KindOf(editor, "margin"));
        Assert.Equal(TokenKind.Number, KindOf(editor, "12px"));
        Assert.Equal(TokenKind.Method, KindOf(editor, "var"));

        editor = Open(window, "shapes.ts", "// Areas.\nexport class Circle implements Shape {\n    area(): number {\n        return Math.PI * this.radius ** 2;\n    }\n}\n");
        Assert.Equal(TokenKind.Keyword, KindOf(editor, "export"));
        Assert.Equal(TokenKind.Class, KindOf(editor, "Circle"));
        Assert.Equal(TokenKind.Method, KindOf(editor, "area"));
        Assert.Equal(TokenKind.Field, KindOf(editor, "radius"));
        Assert.Equal(TokenKind.Number, KindOf(editor, "2"));

        editor = Open(window, "list.js", "function total(shapes) {\n    return shapes.length; // all of them\n}\n");
        Assert.Equal(TokenKind.Keyword, KindOf(editor, "function"));
        Assert.Equal(TokenKind.Method, KindOf(editor, "total"));
        Assert.Equal(TokenKind.Comment, KindOf(editor, "// all of them"));

        editor = Open(window, "Dockerfile", "# Two stages.\nFROM mcr.microsoft.com/dotnet/sdk:10.0 AS build\nRUN dotnet publish -o /app \"Shapes.csproj\"\n");
        Assert.Equal(TokenKind.Comment, KindOf(editor, "# Two stages."));
        Assert.Equal(TokenKind.Keyword, KindOf(editor, "FROM"));
        Assert.Equal(TokenKind.Keyword, KindOf(editor, "RUN"));
        Assert.Equal(TokenKind.String, KindOf(editor, "\"Shapes.csproj\""));
        window.Close();
    }

    [AvaloniaFact]
    public void Each_kind_is_drawn_in_its_own_colour_in_the_dark_theme_and_the_light()
    {
        MainWindow window = Window();
        (string File, string Text, string Token, TokenKind Kind)[] shown =
        [
            ("Shapes.csproj", Project, "PropertyGroup", TokenKind.Tag),
            ("Shapes.csproj", Project, "Include", TokenKind.AttributeName),
            ("Shapes.csproj", Project, "PreserveNewest</", TokenKind.Text),
            ("appsettings.json", Settings, "\"AllowedHosts\"", TokenKind.JsonKey),
            ("ci.yml", Workflow, "branches", TokenKind.YamlKey),
            ("README.md", Readme, "Getting started", TokenKind.Heading),
            ("README.md", Readme, "`dotnet build`", TokenKind.Code),
        ];
        foreach (ThemeVariant theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            window.RequestedThemeVariant = theme;
            foreach ((string file, string text, string token, TokenKind kind) in shown)
            {
                CodeEditor editor = Open(window, file, text);
                string named = kind == TokenKind.Text ? "Passenger.Editor.Text" : $"Passenger.Syntax.{kind}";
                Color expected = Token(theme, named);
                Color ink = Ink(window, editor, text.IndexOf(token, StringComparison.Ordinal), token.TrimEnd('<', '/').Length);
                Color plain = Token(theme, "Passenger.Editor.Text");
                Assert.True(kind == TokenKind.Text || Distance(ink, expected) < Distance(ink, plain),
                    $"In the {theme} theme {token} in {file} is drawn {ink}, nearer the text's {plain} than its {kind}'s {expected}");
                Assert.True(Distance(ink, expected) < 90, $"In the {theme} theme {token} in {file} is drawn {ink}, not its {kind}'s {expected}");
            }
        }
        window.Close();
    }

    [AvaloniaFact]
    public void Typing_keeps_colours_right_after_an_XML_comment_and_a_Markdown_code_fence_open_and_close()
    {
        MainWindow window = Window();
        CodeEditor editor = Open(window, "Shapes.csproj", Project);
        editor.Focus();

        // A word typed in a tag's name keeps the tag's colour at once, before the grammar has read the line again.
        int name = Project.IndexOf("OutputType", StringComparison.Ordinal) + "Output".Length;
        editor.Selections = SelectionSet.Single(name);
        window.KeyTextInput("X");
        Assert.Equal(TokenKind.Tag, KindOf(editor, "OutputXType", settle: false));
        Ui.Settle();
        Assert.Equal(TokenKind.Tag, KindOf(editor, "<OutputXType>"));

        // A comment opened above the property group reaches down to the end of the file, then closed after it, ends there.
        int group = editor.Document.Snapshot.ToString().IndexOf("  <PropertyGroup>", StringComparison.Ordinal);
        editor.Selections = SelectionSet.Single(group);
        window.KeyTextInput("<!--");
        Ui.Settle();
        Assert.Equal(TokenKind.Comment, KindOf(editor, "<None Update"));
        Assert.Equal(TokenKind.Comment, KindOf(editor, "</Project>"));
        int end = editor.Document.Snapshot.ToString().IndexOf("</PropertyGroup>", StringComparison.Ordinal) + "</PropertyGroup>".Length;
        editor.Selections = SelectionSet.Single(end);
        window.KeyTextInput("-->");
        Ui.Settle();
        Assert.Equal(TokenKind.Comment, KindOf(editor, "<Description>"));
        Assert.Equal(TokenKind.Tag, KindOf(editor, "<None Update="[..5]));
        Assert.Equal(TokenKind.AttributeValue, KindOf(editor, "\"Serilog\""));
        Assert.Equal(TokenKind.Tag, KindOf(editor, "</Project>"));
        Assert.Equal(Colours(editor), Colours(Open(window, "Fresh.csproj", editor.Document.Snapshot.ToString())));

        // A code fence opened in a README makes code of what follows; closing it gives the rest its own colours again.
        editor = Open(window, "README.md", Readme);
        editor.Focus();
        int heading = Readme.IndexOf("## Getting started", StringComparison.Ordinal);
        editor.Selections = SelectionSet.Single(heading);
        window.KeyTextInput("```");
        Ui.Press(window, "ENTER");
        Ui.Settle();
        Assert.Equal(TokenKind.Code, KindOf(editor, "## Getting started"));
        Assert.Equal(TokenKind.Code, KindOf(editor, "`dotnet build`"));
        int afterHeading = editor.Document.Snapshot.ToString().IndexOf("## Getting started", StringComparison.Ordinal) + "## Getting started".Length;
        editor.Selections = SelectionSet.Single(afterHeading);
        Ui.Press(window, "ENTER");
        window.KeyTextInput("```");
        Ui.Settle();
        Assert.Equal(TokenKind.Code, KindOf(editor, "## Getting started"));
        Assert.Equal(TokenKind.Link, KindOf(editor, ".NET SDK"));
        Assert.Equal(TokenKind.Quote, KindOf(editor, "Areas are in square units."));
        Assert.Equal(Colours(editor), Colours(Open(window, "Fresh.md", editor.Document.Snapshot.ToString())));
        window.Close();
    }

    [AvaloniaFact]
    public void Comment_with_line_comment_writes_each_languages_own_comment_and_block_comment_wraps_XML_in_its_own()
    {
        MainWindow window = Window();
        CodeEditor editor = Open(window, "ci.yml", Workflow);
        editor.Focus();
        int build = Workflow.IndexOf("  build:", StringComparison.Ordinal);
        editor.Selections = SelectionSet.Single(new Selection(build, Workflow.IndexOf("runs-on: ubuntu-latest", StringComparison.Ordinal) + 3));
        Ui.Run(window, EditorActions.ToggleLineComment);
        Assert.Contains("  # build:\n  #   runs-on: ubuntu-latest\n", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
        Assert.Equal(TokenKind.Comment, KindOf(editor, "#   runs-on: ubuntu-latest"));
        Ui.Run(window, EditorActions.ToggleLineComment);
        Assert.Equal(Workflow, editor.Document.Snapshot.ToString());

        editor = Open(window, "appsettings.json", Settings);
        editor.Focus();
        editor.Selections = SelectionSet.Single(Settings.IndexOf("\"Retries\"", StringComparison.Ordinal));
        Ui.Run(window, EditorActions.ToggleLineComment);
        Assert.Contains("  // \"Retries\": 3,\n", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);

        // XML has no line comment: each line is wrapped in its block comment, and the toggle takes it off again.
        editor = Open(window, "Shapes.csproj", Project);
        editor.Focus();
        int reference = Project.IndexOf("<PackageReference", StringComparison.Ordinal);
        editor.Selections = SelectionSet.Single(reference + 3);
        Ui.Run(window, EditorActions.ToggleLineComment);
        Assert.Contains("    <!--<PackageReference Include=\"Serilog\" Version=\"4.0.0\" />-->\n", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
        Assert.Equal(TokenKind.Comment, KindOf(editor, "<!--<PackageReference Include=\"Serilog\" Version=\"4.0.0\" />-->"));
        Ui.Run(window, EditorActions.ToggleLineComment);
        Assert.Equal(Project, editor.Document.Snapshot.ToString());

        // Block Comment wraps the selection in <!-- -->, and takes the comment off the selection it is.
        int output = Project.IndexOf("<OutputType>Exe</OutputType>", StringComparison.Ordinal);
        editor.Selections = SelectionSet.Single(new Selection(output, output + "<OutputType>Exe</OutputType>".Length));
        Ui.Run(window, EditorActions.ToggleBlockComment);
        Assert.Contains("    <!--<OutputType>Exe</OutputType>-->\n", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
        Assert.Equal("<!--<OutputType>Exe</OutputType>-->", editor.Document.Snapshot.GetText(editor.Selections.Primary.Range));
        Ui.Settle();
        Assert.Equal(TokenKind.Comment, KindOf(editor, "<!--<OutputType>Exe</OutputType>-->"));
        Ui.Run(window, EditorActions.ToggleBlockComment);
        Assert.Equal(Project, editor.Document.Snapshot.ToString());

        // With nothing selected it puts an empty comment at the caret, the caret inside, and again takes it off.
        int text = Project.IndexOf("Shapes &amp;", StringComparison.Ordinal);
        editor.Selections = SelectionSet.Single(text);
        Ui.Run(window, EditorActions.ToggleBlockComment);
        Assert.Contains("<Description><!---->Shapes", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
        Assert.Equal(text + "<!--".Length, editor.Selections.Primary.Caret);
        window.KeyTextInput("note");
        Ui.Run(window, EditorActions.ToggleBlockComment);
        Assert.Contains("<Description>noteShapes", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void A_tag_name_at_the_caret_lights_up_with_its_pairs_and_brackets_pair_only_outside_strings()
    {
        MainWindow window = Window();
        CodeEditor editor = Open(window, "Shapes.csproj", Project);
        editor.Focus();
        int start = Project.IndexOf("<ItemGroup>", StringComparison.Ordinal) + 1;
        int end = Project.IndexOf("</ItemGroup>", StringComparison.Ordinal) + 2;

        // In the start tag's name, at its start or end, the end tag's name lights up with it, and the other way round.
        Assert.Equal((start, end), PairAt(editor, start + 4));
        Assert.Equal((start, end), PairAt(editor, start));
        Assert.Equal((end, start), PairAt(editor, end + "ItemGroup".Length));
        Assert.Equal("ItemGroup", editor.Document.Snapshot.GetText(editor.MatchedBraces!.Value.Matching));
        Color lit = Token(window.ActualThemeVariant, "Passenger.Editor.MatchedBrace");
        Assert.Equal(lit, Ui.Pixel(window, InBox(window, editor, start + 1)));
        Assert.Equal(lit, Ui.Pixel(window, InBox(window, editor, end + 1)));

        // An element pairs with its own end; a tag that closes itself, a comment's words and a value pair with nothing.
        int none = Project.IndexOf("<None", StringComparison.Ordinal) + 1;
        Assert.Equal((none, Project.IndexOf("</None>", StringComparison.Ordinal) + 2), PairAt(editor, none + 2));
        Assert.Null(PairAt(editor, Project.IndexOf("PackageReference", StringComparison.Ordinal) + 3));
        Assert.Null(PairAt(editor, Project.IndexOf("build:", StringComparison.Ordinal) + 2));
        Assert.Null(PairAt(editor, Project.IndexOf("Serilog", StringComparison.Ordinal) + 2));

        // Move Caret to Matching Brace goes from a tag's name to its pair's, and back.
        editor.Selections = SelectionSet.Single(start + 4);
        Ui.Run(window, EditorActions.MatchBrace);
        Assert.Equal(end, editor.Selections.Primary.Caret);
        Ui.Run(window, EditorActions.MatchBrace);
        Assert.Equal(start, editor.Selections.Primary.Caret);

        // JSON's brackets pair across lines; one inside a string pairs with nothing.
        const string json = "{\n  \"a\": [1, 2],\n  \"b\": \"{ [ not code\",\n  \"c\": { \"d\": null }\n}\n";
        editor = Open(window, "data.json", json);
        Assert.Equal((0, json.LastIndexOf('}')), PairAt(editor, 0));
        Assert.Equal((json.IndexOf('['), json.IndexOf(']')), PairAt(editor, json.IndexOf('[')));
        Assert.Null(PairAt(editor, json.IndexOf("{ [", StringComparison.Ordinal) + 1));
        Assert.Null(PairAt(editor, json.IndexOf("{ [", StringComparison.Ordinal) + 3));
        window.Close();
    }

    /// <summary>A key in a file of 20,000 lines of XML, or 5,000 of Markdown, has the grammar read the line it changed,
    /// off the UI thread, and the UI thread do what a key in a short file of the language costs it; a comment opened at the
    /// top is read to the end of the file and lands whole.</summary>
    [AvaloniaFact]
    public void A_key_in_twenty_thousand_lines_of_XML_or_five_thousand_of_Markdown_reads_the_line_it_changed()
    {
        StringBuilder xml = new("<Project Sdk=\"Microsoft.NET.Sdk\">\n");
        for (int i = 0; i < 1000; i++)
        {
            xml.Append(System.Globalization.CultureInfo.InvariantCulture, $"  <!-- Group {i}:\n       its items. -->\n  <ItemGroup Condition=\"'$(Group)' == '{i}' &gt; 0\">\n");
            for (int j = 0; j < 10; j++)
            {
                xml.Append(System.Globalization.CultureInfo.InvariantCulture, $"    <Compile Include=\"File{i}_{j}.cs\" Label=\"a > b\" />\n");
            }
            xml.Append(System.Globalization.CultureInfo.InvariantCulture, $"    <PackageReference Include=\"Package{i}\"\n                      Version=\"1.0.{i}\" />\n");
            xml.Append("    <None Update=\"x.json\">\n      <CopyToOutputDirectory>Always</CopyToOutputDirectory>\n    </None>\n  </ItemGroup>\n\n");
        }
        string large = xml.Append("</Project>\n").ToString();
        Assert.True(large.Count(c => c == '\n') > 20_000);
        StringBuilder md = new("# Notes\n\n");
        for (int i = 0; md.ToString().Count(c => c == '\n') < 5000; i++)
        {
            md.Append(System.Globalization.CultureInfo.InvariantCulture, $"## Section {i}\n\nSome **bold** and *italic* text with `code` and a [link](https://example.com/{i}).\n\n");
            md.Append("- one item\n- another item\n\n```json\n{ \"a\": 1 }\n```\n\n> quoted\n\n");
        }
        string notes = md.ToString();
        MainWindow window = Window();

        foreach ((string file, string text, string at, string small, string smallAt) in new[]
        {
            ("Large.props", large, "File500_3.cs", "<Project>\n  <ItemGroup>\n    <Compile Include=\"File0.cs\" />\n  </ItemGroup>\n</Project>\n", "File0.cs"),
            ("Notes.md", notes, "Section 250", "# Notes\n\n## Section 1\n\nSome **bold** text.\n", "Section 1"),
        })
        {
            (double keyLong, double readLong, double lines, TextMateHighlighter colours) = Keys(window, file, text, at);
            (double keyShort, _, _, _) = Keys(window, "Small" + Path.GetExtension(file), small, smallAt);
            Assert.True(lines <= 2, $"A key in {file} had the grammar read {lines:F1} lines");
            Assert.True(keyLong <= Math.Max(4 * keyShort, keyShort + 2), $"A key in {file} took the UI thread {keyLong:F2} ms, against {keyShort:F2} ms in a short file");
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{file}: {text.Count(c => c == '\n'):N0} lines; a key {keyLong:F2} ms on the UI thread ({keyShort:F2} ms in a short file), "
                + $"{readLong:F2} ms off it reading {lines:F1} lines; opening it read {colours.Worked.Lines:N0} lines");
        }

        // A comment opened at the top of the XML reaches its last line once the pass lands, read in one key's passes.
        CodeEditor editor = window.EditorArea.CodeEditor;
        Open(window, "Large.props", large);
        TextMateHighlighter highlighter = (TextMateHighlighter)window.EditorArea.Active!.Highlighter!;
        editor.Selections = SelectionSet.Single(large.IndexOf('\n') + 1);
        editor.Focus();
        (long before, _, TimeSpan time) = highlighter.Worked;
        window.KeyTextInput("<!--");
        Ui.Settle();
        Assert.Equal(TokenKind.Comment, KindOf(editor, "</Project>"));
        (long after, _, TimeSpan took) = highlighter.Worked;
        TestContext.Current.TestOutputHelper?.WriteLine($"Opening a comment at the top: {after - before:N0} lines read in {(took - time).TotalMilliseconds:F0} ms");
        window.Close();
    }

    /// <summary>A file opened in the window and typed in at a place: the median time a key takes the UI thread, the time
    /// the grammar took for it off the UI thread, and the lines it read for it.</summary>
    private (double Key, double Read, double Lines, TextMateHighlighter Colours) Keys(MainWindow window, string file, string text, string at)
    {
        CodeEditor editor = Open(window, file, text);
        TextMateHighlighter colours = (TextMateHighlighter)window.EditorArea.Active!.Highlighter!;
        editor.Selections = SelectionSet.Single(text.IndexOf(at, StringComparison.Ordinal) + 2);
        editor.Focus();
        Ui.Settle();
        (long lines, int passes, TimeSpan time) = colours.Worked;
        List<double> keys = [];
        const int Count = 12;
        for (int i = 0; i < Count; i++)
        {
            long began = Stopwatch.GetTimestamp();
            window.KeyTextInput(i % 2 == 0 ? "x" : "y");
            Dispatcher(window);
            keys.Add(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
            Ui.Settle();
        }
        (long linesAfter, int passesAfter, TimeSpan timeAfter) = colours.Worked;
        Assert.True(passesAfter - passes >= Count, $"{Count} keys in {file} made {passesAfter - passes} passes");
        keys.Sort();
        return (keys[Count / 2], (timeAfter - time).TotalMilliseconds / Count, (linesAfter - lines) / (double)Count, colours);
    }

    /// <summary>What the key left for the UI thread to do (its frame, drawn with the colours carried), done.</summary>
    private static void Dispatcher(MainWindow window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        _ = window;
    }

    private MainWindow Window()
    {
        MainWindow window = new() { Width = 1200, Height = 800, Autosaves = false };
        window.Show();
        return window;
    }

    /// <summary>A file written to the folder and opened, with no folder opened, so no language server starts; once its
    /// colours have landed.</summary>
    private CodeEditor Open(MainWindow window, string name, string text)
    {
        string path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path) || File.ReadAllText(path) != text)
        {
            File.WriteAllText(path, text);
        }
        Assert.Null(window.OpenFile(path));
        Ui.Settle();
        Assert.Null((window.EditorArea.Active!.Highlighter as TextMateHighlighter)?.Failure);
        return window.EditorArea.CodeEditor;
    }

    /// <summary>The kind every character of a stretch of the editor's text is coloured as (its first occurrence), or
    /// null when they are not all one.</summary>
    private static TokenKind? KindOf(CodeEditor editor, string stretch, bool settle = true)
    {
        if (settle)
        {
            Ui.Settle();
        }
        TextSnapshot text = editor.Document.Snapshot;
        int start = text.ToString().IndexOf(stretch, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{stretch}' is not in the text");
        IReadOnlyList<SyntaxSpan> spans = editor.Highlighter!.GetSpans(text, start, stretch.Length);
        TokenKind?[] kinds = new TokenKind?[stretch.Length];
        Array.Fill(kinds, TokenKind.Text);
        foreach (SyntaxSpan span in spans)
        {
            for (int i = Math.Max(span.Start, start); i < Math.Min(span.End, start + stretch.Length); i++)
            {
                kinds[i - start] = span.Kind;
            }
        }
        // Spaces take no colour a person could see.
        TokenKind?[] seen = [.. kinds.Where((_, i) => !char.IsWhiteSpace(stretch[i])).Distinct()];
        return seen.Length == 1 ? seen[0] : null;
    }

    /// <summary>Every span of the editor's text, as text, to compare with the same text opened afresh.</summary>
    private static string Colours(CodeEditor editor)
    {
        Ui.Settle();
        TextSnapshot text = editor.Document.Snapshot;
        return string.Join(", ", editor.Highlighter!.GetSpans(text, 0, text.Length).Select(s => $"{s.Kind} {text.GetText(s.Start, s.Length)}"));
    }

    private static (int Touched, int Matching)? PairAt(CodeEditor editor, int caret)
    {
        editor.Selections = SelectionSet.Single(caret);
        Ui.Settle();
        return editor.MatchedBraces is { } pair ? (pair.Touched.Start, pair.Matching.Start) : null;
    }

    /// <summary>A point inside the box drawn behind the character at an offset: at its top, above the glyph.</summary>
    private static Point InBox(MainWindow window, CodeEditor editor, int offset) =>
        editor.TranslatePoint(new Point(editor.PointOf(offset + 1).X - 2, editor.PointOf(offset).Y + 1), window)!.Value;

    /// <summary>The colour of the most ink in a stretch of text as drawn: of every pixel in its boxes, the one farthest
    /// from the editor's background, which is where a glyph's stroke covers the pixel whole.</summary>
    private static Color Ink(MainWindow window, CodeEditor editor, int start, int length)
    {
        Ui.Settle();
        Color background = Token(window.ActualThemeVariant, "Passenger.Editor.Background");
        Point from = editor.TranslatePoint(editor.PointOf(start), window)!.Value;
        Point to = editor.TranslatePoint(editor.PointOf(start + length), window)!.Value;
        using Avalonia.Media.Imaging.WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window has drawn no frame.");
        PixelRect box = new PixelRect(PixelPoint.FromPoint(from, window.RenderScaling), PixelPoint.FromPoint(to + new Point(0, editor.LineHeight), window.RenderScaling));
        byte[] pixels = new byte[box.Width * box.Height * 4];
        System.Runtime.InteropServices.GCHandle pinned = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(box, pinned.AddrOfPinnedObject(), pixels.Length, box.Width * 4);
        }
        finally
        {
            pinned.Free();
        }
        bool rgba = frame.Format == Avalonia.Platform.PixelFormat.Rgba8888;
        Color inkiest = background;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            Color pixel = rgba ? Color.FromRgb(pixels[i], pixels[i + 1], pixels[i + 2]) : Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]);
            if (Distance(pixel, background) > Distance(inkiest, background))
            {
                inkiest = pixel;
            }
        }
        return inkiest;
    }

    private static double Distance(Color a, Color b) =>
        Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));

    private static Color Token(ThemeVariant theme, string key) =>
        Application.Current!.TryFindResource(key, theme, out object? brush) && brush is ISolidColorBrush solid
            ? solid.Color
            : throw new InvalidOperationException($"The {theme} theme has no {key}");
}
