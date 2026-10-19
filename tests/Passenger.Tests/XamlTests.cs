using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Run;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Avalonia XAML through the window, on an app made by <c>dotnet new avalonia.app</c> with a view model, a theme
/// palette and an asset of its own, and on Passenger's own XAML: what completion offers as a file is typed, read by
/// Passenger's own XAML from the project's referenced assemblies and its C#.</summary>
public sealed class XamlTests : IClassFixture<XamlTests.Gallery>
{
    private readonly Gallery app;

    public XamlTests(Gallery app)
    {
        this.app = app;
        app.Reset();
    }

    [AvaloniaFact]
    public void Typing_a_tag_offers_the_controls_then_a_controls_properties_and_then_the_values_of_the_one_chosen()
    {
        MainWindow window = app.Open(app.PathOf("MainWindow.axaml"));
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "<StackPanel Margin=\"12\" Spacing=\"8\">");
        Ui.Press(window, "ENTER");
        window.KeyTextInput("<");
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("Button"), "Avalonia's controls offered after a tag's '<'");
        Assert.Contains("Border", window.Assist.CompletionLabels);
        window.KeyTextInput("Butt");
        Ui.Settle();
        Assert.Equal("Button", window.Assist.CompletionLabels.FirstOrDefault());
        Ui.Press(window, "ENTER");
        Assert.EndsWith("<Button|", CaretLine(editor), StringComparison.Ordinal);

        window.KeyTextInput(" ");
        window.KeyTextInput("C");
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("Click"), "a Button's events among its attributes");
        Assert.Contains("Command", window.Assist.CompletionLabels);
        Assert.Contains("Canvas.Left", window.Assist.CompletionLabels);
        Ui.Press(window, "ESCAPE");
        Ui.Press(window, "BACK_SPACE");
        window.KeyTextInput("H");
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("HorizontalAlignment"), "a Button's properties, its bases' included");
        window.KeyTextInput("orizontalA");
        Ui.Settle();
        Assert.Equal("HorizontalAlignment", window.Assist.CompletionLabels.FirstOrDefault());
        // The property goes in with its quotes, the caret between them, and its values are offered there at once.
        Ui.Press(window, "ENTER");
        Assert.EndsWith("<Button HorizontalAlignment=\"|\"", CaretLine(editor), StringComparison.Ordinal);
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("Center"), "the enum's members offered inside the new value's quotes");
        Assert.Equal(["Center", "Left", "Right", "Stretch"], window.Assist.CompletionLabels.Order());
        window.KeyTextInput("Ce");
        Ui.Settle();
        Ui.Press(window, "ENTER");
        Assert.EndsWith("<Button HorizontalAlignment=\"Center|\"", CaretLine(editor), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void A_value_chosen_with_nothing_typed_from_the_list_its_property_opened_leaves_the_caret_after_it_to_type_on()
    {
        MainWindow window = app.Open(app.PathOf("MainWindow.axaml"));
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "<StackPanel Margin=\"12\" Spacing=\"8\">");
        Ui.Press(window, "ENTER");
        window.KeyTextInput("<");
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("Button"), "Avalonia's controls offered after a tag's '<'");
        window.KeyTextInput("Butt");
        Ui.Settle();
        Ui.Press(window, "ENTER");
        window.KeyTextInput(" ");
        window.KeyTextInput("V");
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("VerticalAlignment"), "a Button's properties");
        window.KeyTextInput("erticalA");
        Ui.Settle();
        Ui.Press(window, "ENTER");
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("Center"), "the enum's members offered inside the new value's quotes");
        Assert.EndsWith("<Button VerticalAlignment=\"|\"", CaretLine(editor), StringComparison.Ordinal);

        // Chosen at once, with nothing typed to replace: the caret goes after the value, so the quote typed next steps over
        // the closing one and the next attribute follows, rather than both landing in front of the value.
        string first = window.Assist.CompletionLabels[0];
        Ui.Press(window, "ENTER");
        Assert.True(CaretLine(editor).EndsWith($"<Button VerticalAlignment=\"{first}|\"", StringComparison.Ordinal),
            $"The value chosen with nothing typed left the caret before it, where typing on goes in front of it: {CaretLine(editor)}");
        window.KeyTextInput("\"");
        window.KeyTextInput(" ");
        window.KeyTextInput("H");
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("HorizontalAlignment"), "a Button's properties after the value");
        Assert.EndsWith($"<Button VerticalAlignment=\"{first}\" H|", CaretLine(editor), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void A_resource_reference_offers_the_keys_the_app_merges_in_and_a_binding_its_data_types_properties_through_a_path()
    {
        MainWindow window = app.Open(app.PathOf("MainWindow.axaml"));
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "<Button Name=\"Go\" Content=\"Go\"");
        window.KeyTextInput(" Foreground=");
        window.KeyTextInput("\"");
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("Red"), "the named colours a brush takes");
        window.KeyTextInput("{");
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("StaticResource"), "the markup extensions after a value's brace");
        Assert.Contains("Binding", window.Assist.CompletionLabels);
        Assert.Contains("x:Static", window.Assist.CompletionLabels);
        window.KeyTextInput("StaticRes");
        Ui.Settle();
        Ui.Press(window, "ENTER");
        window.KeyTextInput(" ");
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("Accent"), "the palette's keys, merged in by App.axaml");
        Assert.Contains("Brand", window.Assist.CompletionLabels);
        Assert.Contains("Gallery.Gap", window.Assist.CompletionLabels);

        PlaceAfter(editor, "Text=\"{Binding ");
        Ui.Run(window, AppActions.Complete);
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("Greeting"), "the view model's properties, x:DataType's type's");
        Assert.Contains("Owner", window.Assist.CompletionLabels);
        Assert.Contains("Mode=", window.Assist.CompletionLabels);
        Ui.Press(window, "ESCAPE");
        int greeting = editor.Document.Snapshot.ToString().IndexOf("{Binding Greeting}", StringComparison.Ordinal) + "{Binding ".Length;
        editor.ApplyEdits([new TextEdit(greeting, "Greeting".Length, "Owner.")]);
        editor.Selections = SelectionSet.Single(greeting + "Owner.".Length);
        Ui.Run(window, AppActions.Complete);
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("Name"), "the path's next step's properties, Person's after Owner.");
        Assert.Contains("Age", window.Assist.CompletionLabels);
        Assert.DoesNotContain("Greeting", window.Assist.CompletionLabels);
        window.Close();
    }

    [AvaloniaFact]
    public void Passengers_own_main_window_completes_its_theme_tokens_and_its_style_classes()
    {
        MainWindow window = new() { Width = 1200, Height = 800, Autosaves = false };
        window.Show();
        window.OpenFile(Path.Combine(Repository, "src", "Passenger.App", "MainWindow.axaml"));
        Ui.Settle();
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "Background=\"{DynamicResource Passenger.");
        Ui.Run(window, AppActions.Complete);
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("Passenger.Chrome"), "Tokens.axaml's keys, which App.axaml merges in");
        Assert.Contains("Passenger.Editor.Background", window.Assist.CompletionLabels);
        Ui.Press(window, "ESCAPE");
        PlaceAfter(editor, "<PathIcon Data=\"{StaticResource Icon.");
        Ui.Run(window, AppActions.Complete);
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("Icon.Menu"), "Icons.axaml's geometries");
        Ui.Press(window, "ESCAPE");

        PlaceAfter(editor, "<Button Name=\"MenuButton\" Classes=\"");
        Ui.Run(window, AppActions.Complete);
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("tool"), "the style classes Shell.axaml styles");
        Assert.Contains("widget", window.Assist.CompletionLabels);
        Ui.Press(window, "ESCAPE");

        PlaceAfter(editor, "<editors:");
        Ui.Run(window, AppActions.Complete);
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("EditorArea"), "Passenger's own controls, from its C#, under their prefix");
        window.Close();
    }

    [AvaloniaFact]
    public void A_misspelt_property_enum_value_resource_and_binding_path_are_marked_as_typed_listed_in_problems_and_go_once_put_right()
    {
        MainWindow window = app.Open(app.PathOf("MainWindow.axaml"));
        CodeEditor editor = window.EditorArea.CodeEditor;
        OpenDocument document = window.EditorArea.Active!;
        Assert.Empty(document.Markers);
        PlaceAfter(editor, "<Button Name=\"Go\" Content=\"Go\"");
        window.KeyTextInput(" Foregrund=\"Red\" HorizontalAlignment=\"Centre\"");
        Replace(editor, "{Binding Greeting}", "{Binding Greting}");
        Replace(editor, "{StaticResource Accent}", "{StaticResource Acent}");
        Ui.WaitFor(() => document.Markers.Count(m => m.Kind == MarkerKind.Error) == 4, "the four mistakes marked as errors once typing paused");
        Assert.Equal("Cannot resolve property 'Foregrund' on Button", MarkerOn(editor, document, "Foregrund").Message);
        Assert.Equal("'Centre' is not a value of HorizontalAlignment", MarkerOn(editor, document, "Centre").Message);
        Assert.Equal("Cannot resolve property 'Greting' on MainViewModel", MarkerOn(editor, document, "Greting").Message);
        Assert.Equal("Resource 'Acent' is not found", MarkerOn(editor, document, "Acent").Message);
        Ui.WaitFor(() => window.BottomPanel.Problems.FileProblems.Count == 4, "Problems listing the file's four errors");
        Assert.All(window.BottomPanel.Problems.FileProblems, p => Assert.Equal(ProblemSeverity.Error, p.Severity));

        Replace(editor, "Foregrund", "Foreground");
        Replace(editor, "Greting", "Greeting");
        Ui.WaitFor(() => document.Markers.Count == 2, "the two put right no longer marked");
        Assert.DoesNotContain(document.Markers, m => m.Message.Contains("Foregrund", StringComparison.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public void A_package_restored_while_its_xaml_is_open_has_its_types_read_once_the_restore_lands()
    {
        string folder = PathNames.Canonical(TestFolders.Create("xaml-restore-"));
        try
        {
            TestFolders.Prepare(() =>
            {
                AvaloniaApp.Create(folder, "Fresh");
                File.WriteAllText(Path.Combine(folder, "MainWindow.axaml"), """
                    <Window xmlns="https://github.com/avaloniaui"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                            xmlns:h="using:Avalonia.Headless"
                            x:Class="Fresh.MainWindow"
                            Title="Fresh">
                        <Window.Resources>
                            <h:AvaloniaHeadlessPlatformOptions x:Key="Headless" />
                        </Window.Resources>
                        <TextBlock Text="Welcome" />
                    </Window>

                    """);
                Restore.Run(folder);
            }, folder);
            MainWindow window = new() { Width = 1200, Height = 800, Autosaves = false };
            window.Show();
            window.OpenFolder(folder);
            window.OpenFile(Path.Combine(folder, "MainWindow.axaml"));
            OpenDocument document = window.EditorArea.Active!;
            Ui.WaitFor(() => document.Markers.Any(m => m.Message.Contains("AvaloniaHeadlessPlatformOptions", StringComparison.Ordinal)),
                "the type of a package the app does not reference marked");

            // The package added (here where the project file does not name it, so only its restore says so) and restored, by
            // the language server or a terminal's dotnet restore: the restore's assets are what the XAML reads it from.
            File.WriteAllText(Path.Combine(folder, "Directory.Build.props"),
                $"<Project>\n  <ItemGroup>\n    <PackageReference Include=\"Avalonia.Headless\" Version=\"{AvaloniaApp.Version}\" />\n  </ItemGroup>\n</Project>\n");
            Ui.Settle();
            Restore.Run(folder);
            Ui.WaitFor(() => document.Markers.Count == 0, "the package's type resolved once its restore landed", TimeSpan.FromSeconds(30));
            window.Close();
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }

    [AvaloniaFact]
    public void An_x_class_no_csharp_declares_is_marked_as_avalonias_compiler_refuses_it()
    {
        // Unlike WPF's MarkupCompile, Avalonia's compiler makes no class: it says "Unable to find type" (AVLN2000).
        string badge = app.Write("Badge.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Gallery.Badge">
                <TextBlock Text="New" />
            </UserControl>

            """);
        MainWindow window = app.Open(badge);
        CodeEditor editor = window.EditorArea.CodeEditor;
        OpenDocument document = window.EditorArea.Active!;
        Ui.WaitFor(() => document.Markers.Count > 0, "the class nothing declares marked");
        Marker marked = MarkerOn(editor, document, "Gallery.Badge");
        Assert.Equal(MarkerKind.Error, marked.Kind);
        Assert.Equal("Cannot resolve class 'Gallery.Badge'", marked.Message);
        window.Close();
    }

    [AvaloniaFact]
    public void After_a_build_the_xaml_compilers_error_is_marked_on_its_line_and_listed_with_its_code()
    {
        MainWindow window = app.Open(app.PathOf("MainWindow.axaml"));
        CodeEditor editor = window.EditorArea.CodeEditor;
        OpenDocument document = window.EditorArea.Active!;
        // A number the compiler reads: checked by the build alone.
        PlaceAfter(editor, "<TextBlock Name=\"Title\"");
        window.KeyTextInput(" Width=\"wide\"");
        Ui.Settle();
        Assert.Empty(document.Markers);
        Ui.Run(window, AppActions.Build);
        Ui.Settle();
        Problem built = Assert.Single(window.BottomPanel.Problems.Problems, p => p.Code == "AVLN1000");
        Assert.Equal(PathNames.Canonical(app.PathOf("MainWindow.axaml")), built.Path);
        Assert.Equal(editor.Document.Snapshot.GetLineFromOffset(editor.Document.Snapshot.ToString().IndexOf("Width=\"wide\"", StringComparison.Ordinal)), built.Position.Line);
        Marker marked = MarkerOn(editor, document, "Width");
        Assert.Equal(MarkerKind.Error, marked.Kind);
        Assert.Equal("AVLN1000: The input string 'wide' was not in a correct format.", marked.Message);

        // The line edited, the build's word on it is the build's, no longer this text's.
        Replace(editor, "\"wide\"", "\"120\"");
        Ui.Settle();
        Assert.Empty(document.Markers);
        window.Close();
    }

    [AvaloniaFact]
    public void Passengers_own_xaml_which_compiles_is_marked_with_no_problem()
    {
        MainWindow window = new() { Width = 1200, Height = 800, Autosaves = false };
        window.Show();
        string[] files = [.. Directory.EnumerateFiles(Path.Combine(Repository, "src", "Passenger.App"), "*.axaml", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        Assert.True(files.Length >= 9, $"Passenger.App has only {files.Length} XAML files");
        foreach (string file in files)
        {
            window.OpenFile(file);
            Ui.Settle();
            OpenDocument document = window.EditorArea.Active!;
            Assert.True(window.XamlService.For(document)?.IsLoaded, $"{Path.GetFileName(file)} was not read as XAML");
            Assert.True(document.Markers.Count == 0,
                $"{Path.GetFileName(file)}, which compiles, is marked: {string.Join("; ", document.Markers.Select(m => $"{document.Text.Snapshot.GetPosition(m.Range.Start).Line + 1}: {m.Message}"))}");
        }
        window.Close();
    }

    [AvaloniaFact]
    public void Go_to_declaration_from_xaml_opens_a_controls_class_a_resources_definition_and_a_bound_property()
    {
        MainWindow window = app.Open(app.PathOf("MainWindow.axaml"));
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "x:Class=\"Gallery.Main");
        Ui.Run(window, AppActions.GoToDeclaration);
        Ui.Settle();
        Assert.Equal("MainWindow.axaml.cs", window.EditorArea.Active?.Name);
        Assert.Contains("public partial class |MainWindow", CaretLine(window.EditorArea.CodeEditor), StringComparison.Ordinal);

        window.OpenFile(app.PathOf("MainWindow.axaml"));
        Ui.Settle();
        editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "<Butt");
        Ui.Run(window, AppActions.QuickDocumentation);
        Ui.WaitFor(() => window.Assist.HoverText is not null, "the card describing the tag's type");
        Assert.Equal("class Avalonia.Controls.Button : ContentControl", window.Assist.HoverText);
        Ui.Press(window, "ESCAPE");
        Ui.Run(window, AppActions.GoToDeclaration);
        Ui.WaitFor(() => window.Assist.Offered.Count == 2, "the class and its control theme to choose from");
        Ui.Press(window, "ENTER");
        Ui.Settle();
        // Avalonia's own Button, decompiled from the package's assembly, read-only, at the class.
        OpenDocument library = window.EditorArea.Active!;
        Assert.Equal("Avalonia.Controls.Button.cs", library.Name);
        Assert.True(library.IsReadOnly, "the library's code opened editable");
        Assert.Contains("class |Button", CaretLine(window.EditorArea.CodeEditor), StringComparison.Ordinal);

        window.OpenFile(app.PathOf("MainWindow.axaml"));
        Ui.Settle();
        editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "{StaticResource Acc");
        Ui.Run(window, AppActions.GoToDeclaration);
        // A key the theme's dark and light dictionaries both define: a place to choose for each.
        Ui.WaitFor(() => window.Assist.Offered.Count == 2, "Accent's dark and light definitions to choose from");
        Assert.Equal(["Accent (Dark) in Palette.axaml", "Accent (Light) in Palette.axaml"], window.Assist.Offered);
        Ui.Press(window, "DOWN");
        Ui.Press(window, "ENTER");
        Ui.Settle();
        Assert.Equal("Palette.axaml", window.EditorArea.Active?.Name);
        Assert.Contains("<SolidColorBrush x:Key=\"|Accent\" Color=\"#3574F0\" />", CaretLine(window.EditorArea.CodeEditor), StringComparison.Ordinal);

        window.OpenFile(app.PathOf("MainWindow.axaml"));
        Ui.Settle();
        editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "{Binding Gree");
        Ui.Run(window, AppActions.GoToDeclaration);
        Ui.Settle();
        Assert.Equal("MainViewModel.cs", window.EditorArea.Active?.Name);
        Assert.Contains("public string |Greeting", CaretLine(window.EditorArea.CodeEditor), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void An_elements_name_renames_with_its_code_behinds_uses_and_go_to_declaration_from_the_code_lands_on_it()
    {
        MainWindow window = app.Open(app.PathOf("MainWindow.axaml.cs"));
        CodeEditor code = window.EditorArea.CodeEditor;
        // The field Avalonia's generator makes for the button leads to its name in the XAML.
        PlaceAfter(code, "        G");
        Ui.Run(window, AppActions.GoToDeclaration);
        Ui.Settle();
        Assert.Equal("MainWindow.axaml", window.EditorArea.Active?.Name);
        CodeEditor xaml = window.EditorArea.CodeEditor;
        Assert.Contains("<Button Name=\"|Go\"", CaretLine(xaml), StringComparison.Ordinal);

        Ui.Run(window, AppActions.FindUsages);
        Ui.Settle();
        Ui.WaitFor(() => window.Find.Rows.Count > 0, "the usages in the Find window");
        IReadOnlyList<string> found = [.. window.Find.Rows.Where(r => r.Usage is not null).Select(r => r.Label)];
        Assert.Contains(found, f => f.Contains("Go.Content = \"Start\";", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("{Binding #Go.Content}", StringComparison.Ordinal));

        Ui.Run(window, AppActions.Rename);
        Ui.WaitFor(() => window.Assist.Renaming == "Go", "the rename box on the button's name");
        window.KeyTextInput("Start");
        Ui.Press(window, "ENTER");
        OpenDocument behind = window.EditorArea.Documents.First(d => d.Name == "MainWindow.axaml.cs");
        Ui.WaitFor(() => behind.Text.Snapshot.ToString().Contains("Start.Content = \"Start\";", StringComparison.Ordinal), "the code-behind's use renamed with it");
        string renamed = window.EditorArea.Documents.First(d => d.Name == "MainWindow.axaml").Text.Snapshot.ToString();
        Assert.Contains("<Button Name=\"Start\"", renamed, StringComparison.Ordinal);
        Assert.Contains("{Binding #Start.Content}", renamed, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"Go\"", renamed, StringComparison.Ordinal);
        // One command, undone whole, asked first as Rider asks of a change to more than one file.
        List<string> asked = [];
        window.Confirm = (title, text, _) =>
        {
            asked.Add(text);
            return Task.FromResult(true);
        };
        Ui.Run(window, EditorActions.Undo);
        Ui.Settle();
        Assert.Equal(["Undo Rename Go to Start? It changes MainWindow.axaml.cs and MainWindow.axaml."], asked);
        Assert.Contains("Go.Content = \"Start\";", behind.Text.Snapshot.ToString(), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void A_key_one_theme_variant_lacks_is_warned_of_where_defined_and_used_and_the_fix_adds_it_to_that_variant()
    {
        MainWindow window = app.Open(app.PathOf("MainWindow.axaml"));
        OpenDocument view = window.EditorArea.Active!;
        PlaceAfter(window.EditorArea.CodeEditor, "<Button Name=\"Go\" Content=\"Go\"");
        window.KeyTextInput(" Foreground=\"{DynamicResource Danger}\"");
        window.OpenFile(app.PathOf("Styles/Palette.axaml"));
        Ui.Settle();
        CodeEditor palette = window.EditorArea.CodeEditor;
        OpenDocument document = window.EditorArea.Active!;
        PlaceAfter(palette, "<SolidColorBrush x:Key=\"Muted\" Color=\"#868A91\" />");
        window.KeyTextInput("\n            <SolidColorBrush x:Key=\"Danger\" Color=\"#E55765\" />");
        Ui.WaitFor(() => document.Markers.Any(m => m.Kind == MarkerKind.Warning), "the warning on the key only Dark defines");
        Assert.Equal("'Danger' is defined for Dark but not for Light", MarkerOn(palette, document, "Danger").Message);
        Ui.WaitFor(() => view.Markers.Any(m => m.Kind == MarkerKind.Warning), "the warning on the window's use of it, from the palette as typed");
        Marker used = Assert.Single(view.Markers);
        Assert.Equal("'Danger' is not defined for Light", used.Message);

        Ui.Run(window, AppActions.QuickFix);
        Ui.WaitFor(() => window.Assist.Offered.Count > 0, "the fix offered on the key's line");
        Assert.Equal(["Add 'Danger' to Light"], window.Assist.Offered);
        Ui.Press(window, "ENTER");
        Ui.Settle();
        Assert.Contains("<SolidColorBrush x:Key=\"Muted\" Color=\"#6C707E\" />\n            <SolidColorBrush x:Key=\"Danger\" Color=\"#E55765\" />\n        </ResourceDictionary>",
            palette.Document.Snapshot.ToString(), StringComparison.Ordinal);
        Ui.WaitFor(() => document.Markers.Count == 0 && view.Markers.Count == 0, "both warnings gone once Light has the key too");
        window.Close();
    }

    [AvaloniaFact]
    public void An_avares_path_completes_from_the_projects_assets_is_warned_of_when_missing_and_goes_to_its_file()
    {
        MainWindow window = app.Open(app.PathOf("MainWindow.axaml"));
        CodeEditor editor = window.EditorArea.CodeEditor;
        OpenDocument document = window.EditorArea.Active!;
        PlaceAfter(editor, "<StackPanel Margin=\"12\" Spacing=\"8\">");
        window.KeyTextInput("\n        <Image Source=");
        window.KeyTextInput("\"");
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("avares://Gallery/Assets/logo.png"), "the project's asset as an avares:// path");
        Assert.Contains("avares://Gallery/Styles/Palette.axaml", window.Assist.CompletionLabels);
        window.KeyTextInput("logo");
        Ui.Settle();
        Assert.Equal("avares://Gallery/Assets/logo.png", window.Assist.CompletionLabels.FirstOrDefault());
        Ui.Press(window, "ENTER");
        Assert.EndsWith("<Image Source=\"avares://Gallery/Assets/logo.png|\"", CaretLine(editor), StringComparison.Ordinal);
        Ui.Press(window, "END");
        window.KeyTextInput(" />");
        Ui.Settle();
        Assert.Empty(document.Markers);

        Replace(editor, "logo.png", "logo2.png");
        Ui.WaitFor(() => document.Markers.Count == 1, "the warning on the asset that is not there");
        Assert.Equal("'avares://Gallery/Assets/logo2.png' is not found", document.Markers[0].Message);
        Assert.Equal(MarkerKind.Warning, document.Markers[0].Kind);

        window.OpenFile(app.PathOf("App.axaml"));
        Ui.Settle();
        PlaceAfter(window.EditorArea.CodeEditor, "Source=\"avares://Gallery/Sty");
        Ui.Run(window, AppActions.GoToDeclaration);
        Ui.Settle();
        Assert.Equal("Palette.axaml", window.EditorArea.Active?.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void Colours_show_as_gutter_swatches_and_one_chosen_in_the_picker_is_written_back_as_it_was_written()
    {
        MainWindow window = app.Open(app.PathOf("MainWindow.axaml"));
        CodeEditor editor = window.EditorArea.CodeEditor;
        OpenDocument view = window.EditorArea.Active!;
        int button = editor.Document.Snapshot.GetLineFromOffset(editor.Document.Snapshot.ToString().IndexOf("<Button Name=\"Go\"", StringComparison.Ordinal));
        // The button's background names the palette's Accent: its swatch is the dark theme's, which the window shows.
        Ui.WaitFor(() => view.ColourMarks.Any(m => m.Line == button), "the swatch of the brush the button's background names");
        Assert.Equal(Color.Parse("#3D7BF5"), view.ColourMarks.Single(m => m.Line == button).Colour);
        Point swatch = editor.TranslatePoint(editor.GutterColumnPoint(GutterColumn.Marks, button), window)!.Value;
        Assert.Equal(Color.Parse("#3D7BF5"), Ui.Pixel(window, swatch));

        window.MouseDown(swatch, MouseButton.Left);
        window.MouseUp(swatch, MouseButton.Left);
        Ui.Settle();
        Assert.Equal("#3D7BF5", window.PickingColour);
        window.KeyTextInput("#E2A33F");
        Ui.Settle();
        Assert.Equal("#E2A33F", window.PickingColour);
        Ui.Press(window, "ENTER");
        Ui.Settle();
        Assert.Null(window.PickingColour);
        // Written where the brush is defined, in the palette's dark dictionary; the light one keeps its own.
        string palette = File.ReadAllText(app.PathOf("Styles/Palette.axaml"));
        Assert.Contains("<SolidColorBrush x:Key=\"Accent\" Color=\"#E2A33F\" />", palette, StringComparison.Ordinal);
        Assert.Contains("<SolidColorBrush x:Key=\"Accent\" Color=\"#3574F0\" />", palette, StringComparison.Ordinal);
        Ui.WaitFor(() => view.ColourMarks.SingleOrDefault(m => m.Line == button).Colour == Color.Parse("#E2A33F"), "the swatch following the palette");

        // C#'s colours too, written back in the form they were written.
        window.OpenFile(app.PathOf("MainWindow.axaml.cs"));
        Ui.Settle();
        CodeEditor code = window.EditorArea.CodeEditor;
        OpenDocument behind = window.EditorArea.Active!;
        PlaceAfter(code, "Go.Content = \"Start\";");
        window.KeyTextInput("\n        Go.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x33, 0x66, 0x99));");
        int line = code.CaretPosition.Line;
        Ui.WaitFor(() => behind.ColourMarks.Any(m => m.Line == line), "the swatch of the colour FromRgb makes");
        Assert.Equal(Color.Parse("#336699"), behind.ColourMarks.Single(m => m.Line == line).Colour);
        Point made = code.TranslatePoint(code.GutterColumnPoint(GutterColumn.Marks, line), window)!.Value;
        window.MouseDown(made, MouseButton.Left);
        window.MouseUp(made, MouseButton.Left);
        Ui.Settle();
        window.KeyTextInput("#AA0000");
        Ui.Press(window, "ENTER");
        Ui.Settle();
        Assert.Contains("new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(170, 0, 0));", code.Document.Snapshot.GetLineText(line), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Go_to_declaration_reaches_the_themes_own_xaml_for_a_control_a_template_part_and_a_theme_resource()
    {
        MainWindow window = app.Open(app.PathOf("MainWindow.axaml"));
        // Fluent's sources as its Source Link gives them, stood in: FluentControls.xaml merges Button.xaml in.
        ThemeSources sources = new(TestFolders.Create("xaml-theme-"));
        window.XamlService.LibraryFiles = sources;
        CodeEditor editor = window.EditorArea.CodeEditor;
        PlaceAfter(editor, "<Butt");
        Ui.Run(window, AppActions.GoToDeclaration);
        Ui.WaitFor(() => window.Assist.Offered.Count == 2, "the class and its control theme to choose from");
        Assert.Equal(["Avalonia.Controls.Button", "Button's control theme"], window.Assist.Offered);
        Ui.Press(window, "DOWN");
        Ui.Press(window, "ENTER");
        Ui.Settle();
        OpenDocument theme = window.EditorArea.Active!;
        Assert.Equal("Button.xaml", theme.Name);
        Assert.True(theme.IsReadOnly, "the theme's XAML opened editable");
        Assert.Contains("<|ControlTheme x:Key=\"{x:Type Button}\" TargetType=\"Button\">", CaretLine(window.EditorArea.CodeEditor), StringComparison.Ordinal);
        Assert.Equal([("Avalonia.Themes.Fluent", "/Controls/FluentControls.xaml"), ("Avalonia.Themes.Fluent", "/Controls/Button.xaml")], sources.Asked);

        window.OpenFile(app.PathOf("App.axaml"));
        Ui.Settle();
        PlaceAfter(window.EditorArea.CodeEditor, "<FluentTheme />");
        window.KeyTextInput("\n        <Style Selector=\"Button /template/ ContentPresenter#PART_ContentPresenter\">\n            <Setter Property=\"Background\" Value=\"{DynamicResource ButtonBackground}\" />\n        </Style>");
        PlaceAfter(window.EditorArea.CodeEditor, "#PART_Con");
        Ui.Run(window, AppActions.GoToDeclaration);
        Ui.Settle();
        Assert.Equal("Button.xaml", window.EditorArea.Active?.Name);
        Assert.Contains("<ContentPresenter x:Name=\"|PART_ContentPresenter\"", CaretLine(window.EditorArea.CodeEditor), StringComparison.Ordinal);

        window.OpenFile(app.PathOf("App.axaml"));
        Ui.Settle();
        PlaceAfter(window.EditorArea.CodeEditor, "{DynamicResource ButtonBack");
        Ui.Run(window, AppActions.GoToDeclaration);
        Ui.Settle();
        Assert.Equal("Button.xaml", window.EditorArea.Active?.Name);
        Assert.Contains("x:Key=\"|ButtonBackground\"", CaretLine(window.EditorArea.CodeEditor), StringComparison.Ordinal);
        window.Close();
    }

    /// <summary>A theme library's files as its Source Link would give them, written into a folder of the test's; what was
    /// asked for, in order.</summary>
    private sealed class ThemeSources(string folder) : Passenger.App.Xaml.ILibraryFiles
    {
        public List<(string Assembly, string Path)> Asked { get; } = [];

        public Task<string?> FileAsync(string assembly, string assemblyName, string path, IProgress<string>? why, CancellationToken cancel)
        {
            Asked.Add((assemblyName, path));
            string? text = path switch
            {
                "/Controls/FluentControls.xaml" => "<ResourceDictionary xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n"
                    + "  <ResourceDictionary.MergedDictionaries>\n    <MergeResourceInclude Source=\"Button.xaml\" />\n  </ResourceDictionary.MergedDictionaries>\n</ResourceDictionary>\n",
                "/Controls/Button.xaml" => "<ResourceDictionary xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n"
                    + "  <StaticResource x:Key=\"ButtonBackground\" ResourceKey=\"ControlFillColorDefaultBrush\" />\n"
                    + "  <ControlTheme x:Key=\"{x:Type Button}\" TargetType=\"Button\">\n    <Setter Property=\"Template\">\n      <ControlTemplate>\n"
                    + "        <ContentPresenter x:Name=\"PART_ContentPresenter\" Content=\"{TemplateBinding Content}\" />\n      </ControlTemplate>\n    </Setter>\n"
                    + "  </ControlTheme>\n</ResourceDictionary>\n",
                _ => null,
            };
            if (text is null)
            {
                why?.Report($"{path} is not in the stand-in");
                return Task.FromResult<string?>(null);
            }
            // Kept as Source Link's files are, under a MetadataAsSource folder, which the editor shows read-only.
            string local = Path.Combine(folder, "MetadataAsSource", "SourceLink", assemblyName, path.TrimStart('/'));
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            File.WriteAllText(local, text);
            return Task.FromResult<string?>(local);
        }
    }

    /// <summary>The one marker over a stretch of the text.</summary>
    [AvaloniaFact]
    public void The_preview_draws_a_window_from_the_last_build_follows_its_text_as_it_is_typed_and_draws_styles_as_their_PreviewWith()
    {
        MainWindow window = app.Open(app.PathOf("MainWindow.axaml"));
        CodeEditor editor = window.EditorArea.CodeEditor;
        OpenDocument document = window.EditorArea.Active!;
        Ui.Run(window, AppActions.Build);
        Ui.Settle();
        Ui.Run(window, AppActions.FindAction);
        Passenger.App.Navigation.ActionPopup popup = Assert.IsType<Passenger.App.Navigation.ActionPopup>(window.Popup);
        popup.Query = "xaml preview";
        Ui.Settle();
        Assert.Equal(AppActions.XamlPreview, popup.Rows[0].Id);
        Ui.Press(window, "ENTER");
        Ui.Settle();
        Passenger.App.Xaml.XamlPreview preview = window.PreviewOf(document) ?? throw new InvalidOperationException("XAML Preview showed no preview beside the file");
        Ui.WaitFor(() => preview.Frame is not null, "the previewer's first frame", TimeSpan.FromSeconds(60));
        Ui.Settle();
        Assert.Null(preview.Error);
        // Drawn at the size the window's design-time width and height give it.
        Assert.Equal(new PixelSize(800, 450), preview.Frame!.PixelSize);
        Avalonia.Controls.Image frame = window.PreviewPaneOf(document)!.Frame;
        // A point of the panel below its children, where its background shows.
        Color Below() => Ui.Pixel(window, frame.TranslatePoint(new Point(400, 300), window) ?? throw new InvalidOperationException("The preview's frame is not on the window"));
        Color orange = Color.Parse("#E2A33F");
        Assert.NotEqual(orange, Below());

        PlaceAfter(editor, "<StackPanel");
        window.KeyTextInput(" Background=\"#E2A33F\"");
        Ui.WaitFor(() => Below() == orange, "the preview drawing the panel's new background once typing paused", TimeSpan.FromSeconds(30));

        // XAML that does not load leaves the last frame up, the host's reason over it.
        Replace(editor, "</StackPanel>", "</StackPane>");
        Ui.WaitFor(() => preview.Error is not null, "the previewer saying why the XAML did not load", TimeSpan.FromSeconds(30));
        Assert.Contains("line 14", preview.Error, StringComparison.Ordinal);
        Assert.Equal(orange, Below());
        Replace(editor, "</StackPane>", "</StackPanel>");
        Ui.WaitFor(() => preview.Error is null, "the error gone once the XAML loads again", TimeSpan.FromSeconds(30));

        // Styles preview as their Design.PreviewWith: the file shown is the one previewed.
        string styles = app.PathOf("Styles/Buttons.axaml");
        File.WriteAllText(styles, Gallery.Buttons);
        window.OpenFile(styles);
        Ui.Settle();
        OpenDocument shown = window.EditorArea.Active!;
        Passenger.App.Xaml.XamlPreview styled = window.PreviewOf(shown) ?? throw new InvalidOperationException("The styles opened while previewing showed no preview");
        Ui.WaitFor(() => styled.Frame is not null, "the styles' preview", TimeSpan.FromSeconds(60));
        Avalonia.Controls.Image button = window.PreviewPaneOf(shown)!.Frame;
        Ui.WaitFor(() => button.TranslatePoint(new Point(24, styled.FrameSize.Height / 2), window) is { } inside && Ui.Pixel(window, inside) == orange,
            "the button Design.PreviewWith holds drawn in the style's colour", TimeSpan.FromSeconds(30));
        window.Close();
    }

    internal static Marker MarkerOn(CodeEditor editor, OpenDocument document, string stretch)
    {
        string text = editor.Document.Snapshot.ToString();
        return Assert.Single(document.Markers, m => m.Range.End <= text.Length && text[m.Range.Start..m.Range.End] == stretch);
    }

    internal static void Replace(CodeEditor editor, string what, string with)
    {
        int at = editor.Document.Snapshot.ToString().IndexOf(what, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{what}' is not in the file");
        editor.ApplyEdits([new TextEdit(at, what.Length, with)]);
    }

    /// <summary>The repository the tests were built from.</summary>
    internal static string Repository
    {
        get
        {
            for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
            {
                if (File.Exists(Path.Combine(at.FullName, "Passenger.slnx")))
                {
                    return at.FullName;
                }
            }
            throw new DirectoryNotFoundException($"No folder above {AppContext.BaseDirectory} holds Passenger.slnx");
        }
    }

    /// <summary>The caret's line with a bar where the caret is.</summary>
    internal static string CaretLine(CodeEditor editor) =>
        editor.Document.Snapshot.GetLineText(editor.CaretPosition.Line).Insert(editor.CaretPosition.Column, "|");

    internal static void PlaceAfter(CodeEditor editor, string text)
    {
        int at = editor.Document.Snapshot.ToString().IndexOf(text, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{text}' is not in the file");
        editor.Selections = SelectionSet.Single(at + text.Length);
    }

    /// <summary>The app: Avalonia's template with a view model its window binds through <c>x:DataType</c>, a palette of
    /// brushes in a theme dictionary merged in by App.axaml, and an asset.</summary>
    public sealed class Gallery : SharedProject
    {
        public Gallery()
            : base("xaml-")
        {
            TestFolders.Prepare(() =>
            {
                AvaloniaApp.Create(Folder, "Gallery");
                Directory.CreateDirectory(PathOf("ViewModels"));
                Directory.CreateDirectory(PathOf("Styles"));
                Directory.CreateDirectory(PathOf("Assets"));
                File.WriteAllText(PathOf("ViewModels/MainViewModel.cs"), MainViewModel);
                File.WriteAllText(PathOf("Styles/Palette.axaml"), Palette);
                File.WriteAllBytes(PathOf("Assets/logo.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
                File.WriteAllText(PathOf("App.axaml"), AppXaml);
                File.WriteAllText(PathOf("MainWindow.axaml"), Window);
                File.WriteAllText(PathOf("MainWindow.axaml.cs"), CodeBehind);
            }, Folder);
            Prepare();
        }

        public const string MainViewModel = """
            namespace Gallery.ViewModels;

            public class MainViewModel
            {
                public string Greeting { get; set; } = "Hello";

                public Person Owner { get; } = new();

                public int Count { get; set; }
            }

            public class Person
            {
                public string Name { get; set; } = "Ada";

                public int Age { get; set; }
            }

            """;

        public const string Palette = """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ResourceDictionary.ThemeDictionaries>
                    <ResourceDictionary x:Key="Dark">
                        <SolidColorBrush x:Key="Accent" Color="#3D7BF5" />
                        <SolidColorBrush x:Key="Muted" Color="#868A91" />
                    </ResourceDictionary>
                    <ResourceDictionary x:Key="Light">
                        <SolidColorBrush x:Key="Accent" Color="#3574F0" />
                        <SolidColorBrush x:Key="Muted" Color="#6C707E" />
                    </ResourceDictionary>
                </ResourceDictionary.ThemeDictionaries>
                <Color x:Key="Brand">#E2A33F</Color>
                <Thickness x:Key="Gallery.Gap">8</Thickness>
            </ResourceDictionary>

            """;

        public const string AppXaml = """
            <Application xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Gallery.App"
                         RequestedThemeVariant="Default">
                <Application.Resources>
                    <ResourceDictionary>
                        <ResourceDictionary.MergedDictionaries>
                            <ResourceInclude Source="avares://Gallery/Styles/Palette.axaml" />
                        </ResourceDictionary.MergedDictionaries>
                    </ResourceDictionary>
                </Application.Resources>

                <Application.Styles>
                    <FluentTheme />
                </Application.Styles>
            </Application>

            """;

        /// <summary>Styles that preview as the button their Design.PreviewWith holds, in the style's colour.</summary>
        public const string Buttons = """
            <Styles xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Design.PreviewWith>
                    <Border Padding="20">
                        <Button Classes="warm" Content="Warm" />
                    </Border>
                </Design.PreviewWith>
                <Style Selector="Button.warm">
                    <Setter Property="Background" Value="#E2A33F" />
                </Style>
            </Styles>

            """;

        /// <summary>The window's code-behind, using the field Avalonia's generator makes for the button named Go.</summary>
        public const string CodeBehind = """
            using Avalonia.Controls;

            namespace Gallery;

            public partial class MainWindow : Window
            {
                public MainWindow()
                {
                    InitializeComponent();
                    Go.Content = "Start";
                }
            }

            """;

        public const string Window = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                    xmlns:vm="using:Gallery.ViewModels"
                    mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="450"
                    x:Class="Gallery.MainWindow"
                    x:DataType="vm:MainViewModel"
                    Title="Gallery">
                <StackPanel Margin="12" Spacing="8">
                    <TextBlock Name="Title" Text="{Binding Greeting}" />
                    <Button Name="Go" Content="Go" Background="{StaticResource Accent}" />
                    <TextBlock Text="{Binding #Go.Content}" />
                </StackPanel>
            </Window>

            """;
    }
}
