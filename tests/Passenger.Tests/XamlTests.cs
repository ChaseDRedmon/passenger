using Avalonia.Headless;
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

    /// <summary>The one marker over a stretch of the text.</summary>
    private static Marker MarkerOn(CodeEditor editor, OpenDocument document, string stretch)
    {
        string text = editor.Document.Snapshot.ToString();
        return Assert.Single(document.Markers, m => m.Range.End <= text.Length && text[m.Range.Start..m.Range.End] == stretch);
    }

    private static void Replace(CodeEditor editor, string what, string with)
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
    private static string CaretLine(CodeEditor editor) =>
        editor.Document.Snapshot.GetLineText(editor.CaretPosition.Line).Insert(editor.CaretPosition.Column, "|");

    private static void PlaceAfter(CodeEditor editor, string text)
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
