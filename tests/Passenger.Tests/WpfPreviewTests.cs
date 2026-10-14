using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Passenger.App;
using Passenger.App.Workspace;
using Passenger.App.Xaml;
using Passenger.Editor;
using Passenger.Text;
using static Passenger.Tests.XamlTests;

namespace Passenger.Tests;

/// <summary>WPF's previewer through the window, on Windows: <see cref="WpfXamlTests"/>' <c>dotnet new wpf</c> app (App.xaml's
/// merged brushes and heading style, a <c>d:DataContext</c> view model, a button template drawing its background) built and
/// previewed: the window with App.xaml's resources, an edit followed, broken XAML's error with its line over the last frame,
/// a user control at its design size with its design-time data, an element chosen by a click or the caret. Elsewhere they
/// skip, as WPF draws only on Windows (<see cref="WpfXamlTests"/> checks what the pane says there).</summary>
public sealed class WpfPreviewTests : IClassFixture<WpfPreviewTests.Gallery>
{
    private const string OnlyOnWindows = "WPF draws only on Windows, so its previewer runs only there";

    private static readonly Color Accent = Color.Parse("#3574F0");

    private readonly Gallery app;

    public WpfPreviewTests(Gallery app)
    {
        this.app = app;
        if (OperatingSystem.IsWindows())
        {
            app.Reset();
        }
    }

    [AvaloniaFact]
    public void A_window_previews_with_app_xamls_resources_follows_an_edit_and_shows_why_broken_xaml_did_not_load_with_its_line()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), OnlyOnWindows);
        MainWindow window = app.Open(app.PathOf("MainWindow.xaml"));
        CodeEditor editor = window.EditorArea.CodeEditor;
        OpenDocument document = window.EditorArea.Active!;
        XamlPreview preview = Previewed(window, document);
        Assert.Null(preview.Error);
        Assert.Equal("Live", preview.Status);
        XamlPreviewPane pane = window.PreviewPaneOf(document)!;
        Color At(Point point) => Ui.Pixel(window, pane.Frame.TranslatePoint(point, window) ?? throw new InvalidOperationException("The preview's frame is not on the window"));

        // The window's button style draws the button's background, App.xaml's merged Accent: its resources are the app's.
        PreviewElement button = ElementOf(preview, editor, "<Button x:Name=\"Go\"");
        Assert.True(button.Box.Width > 100, $"The button is drawn {button.Box.Width} wide, not across the window");
        Assert.Equal(Accent, At(new Point(button.Box.X + 24, button.Box.Center.Y)));

        // Below the panel's children, where its own background shows once the edit gives it one.
        PreviewElement panel = ElementOf(preview, editor, "<StackPanel Margin=\"12\"");
        Point below = new(panel.Box.Center.X, panel.Box.Bottom - 20);
        Color orange = Color.Parse("#E2A33F");
        Assert.NotEqual(orange, At(below));
        PlaceAfter(editor, "<StackPanel");
        window.KeyTextInput(" Background=\"#E2A33F\"");
        Ui.WaitFor(() => At(below) == orange, "the preview drawing the panel's new background once typing paused", TimeSpan.FromSeconds(30));

        // XAML that does not load leaves the last frame up, the previewer's reason over it with its line.
        Replace(editor, "</StackPanel>", "</StackPane>");
        int line = editor.Document.Snapshot.GetLineFromOffset(editor.Document.Snapshot.ToString().IndexOf("</StackPane>", StringComparison.Ordinal)) + 1;
        Ui.WaitFor(() => preview.Error is not null, "the previewer saying why the XAML did not load", TimeSpan.FromSeconds(30));
        Assert.Contains($"(line {line}, column ", preview.Error, StringComparison.Ordinal);
        Assert.Contains("StackPane", preview.Error, StringComparison.Ordinal);
        Assert.Equal(preview.Error, pane.ErrorShown);
        Assert.Equal(orange, At(below));
        Replace(editor, "</StackPane>", "</StackPanel>");
        Ui.WaitFor(() => preview.Error is null && pane.ErrorShown is null, "the error gone once the XAML loads again", TimeSpan.FromSeconds(30));
        window.Close();
    }

    [AvaloniaFact]
    public void A_user_control_previews_at_its_design_size_with_its_design_time_data_context()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), OnlyOnWindows);
        string card = app.Write("Card.xaml", Card);
        MainWindow window = app.Open(card);
        CodeEditor editor = window.EditorArea.CodeEditor;
        OpenDocument document = window.EditorArea.Active!;
        XamlPreview preview = Previewed(window, document);
        Assert.Null(preview.Error);
        Assert.Equal(new Size(320, 120), preview.FrameSize);
        Assert.Equal(new PixelSize(320, 120), preview.Frame!.PixelSize);
        XamlPreviewPane pane = window.PreviewPaneOf(document)!;
        Assert.Equal(Accent, Ui.Pixel(window, pane.Frame.TranslatePoint(new Point(310, 110), window)!.Value));

        // A design-time creatable instance is the view model itself, its values shown.
        PreviewElement greeting = ElementOf(preview, editor, "<TextBlock Text=\"{Binding Greeting}\"");
        PreviewElement owner = ElementOf(preview, editor, "<TextBlock Text=\"{Binding Owner.Name}\"");
        Assert.True(greeting.Box.Width > 10, $"The greeting is drawn {greeting.Box.Width} wide: the view model's \"Hello\" is not shown");
        Assert.True(owner.Box.Width > 10, $"The owner's name is drawn {owner.Box.Width} wide: the view model's \"Ada\" is not shown");

        // One that is not is a stand-in of its shape, its values the defaults: the bindings resolve, and show nothing.
        Replace(editor, "IsDesignTimeCreatable=True", "IsDesignTimeCreatable=False");
        Ui.WaitFor(() => preview.Elements.Any(e => TagOf(editor, e).Contains("{Binding Greeting}", StringComparison.Ordinal) && e.Box.Width == 0),
            "the greeting drawn empty from the stand-in", TimeSpan.FromSeconds(30));
        Assert.Null(preview.Error);
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_an_element_in_the_preview_selects_its_start_tag_and_the_caret_outlines_its_element()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), OnlyOnWindows);
        MainWindow window = app.Open(app.PathOf("MainWindow.xaml"));
        CodeEditor editor = window.EditorArea.CodeEditor;
        OpenDocument document = window.EditorArea.Active!;
        XamlPreview preview = Previewed(window, document);
        XamlPreviewPane pane = window.PreviewPaneOf(document)!;

        PreviewElement button = ElementOf(preview, editor, "<Button x:Name=\"Go\"");
        Point at = pane.Frame.TranslatePoint(new Point(button.Box.X + 24, button.Box.Center.Y), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
        string text = editor.Document.Snapshot.ToString();
        int start = text.IndexOf("<Button x:Name=\"Go\"", StringComparison.Ordinal);
        int end = text.IndexOf("/>", start, StringComparison.Ordinal) + 2;
        Assert.Equal(new Selection(end, start), editor.Selections.Primary with { PreferredX = double.NaN });
        Assert.Equal(button.Box, pane.Outlined);

        // The caret in another element's tag outlines that one, where the frame draws it.
        PlaceAfter(editor, "<TextBlock Style=\"{StaticResource Heading}\" Te");
        Ui.Settle();
        PreviewElement heading = ElementOf(preview, editor, "<TextBlock Style=\"{StaticResource Heading}\"");
        Assert.Equal(heading.Box, pane.Outlined);
        Assert.True(pane.Outline.IsVisible, "The outline is not shown");
        Point drawn = pane.Outline.TranslatePoint(default, window)!.Value;
        Point expected = pane.Frame.TranslatePoint(heading.Box.TopLeft, window)!.Value;
        Assert.True(Math.Abs(drawn.X - expected.X) < 1 && Math.Abs(drawn.Y - expected.Y) < 1, $"The outline is drawn at {drawn}, the heading at {expected}");

        // In the window's resources, which draw nothing of their own, the window itself is outlined.
        PlaceAfter(editor, "<Window.Resources>");
        Ui.Settle();
        Assert.Equal(ElementOf(preview, editor, "<Window x:Class").Box, pane.Outlined);
        window.Close();
    }

    /// <summary>The project built, XAML Preview turned on through Find Action, and its first frame drawn.</summary>
    private static XamlPreview Previewed(MainWindow window, OpenDocument document)
    {
        Ui.Run(window, AppActions.Build);
        Ui.Settle();
        Ui.Run(window, AppActions.FindAction);
        Passenger.App.Navigation.ActionPopup popup = Assert.IsType<Passenger.App.Navigation.ActionPopup>(window.Popup);
        popup.Query = "xaml preview";
        Ui.Settle();
        Assert.Equal(AppActions.XamlPreview, popup.Rows[0].Id);
        Ui.Press(window, "ENTER");
        Ui.Settle();
        XamlPreview preview = window.PreviewOf(document) ?? throw new InvalidOperationException("XAML Preview showed no preview beside the file");
        Ui.WaitFor(() => preview.Frame is not null || preview.Error is not null, $"the previewer's first frame (it says: {preview.Status})", TimeSpan.FromSeconds(90));
        Ui.Settle();
        Assert.True(preview.Frame is not null, $"The previewer drew nothing: {preview.Error}");
        return preview;
    }

    /// <summary>The drawn element whose start tag begins with a stretch of the file.</summary>
    private static PreviewElement ElementOf(XamlPreview preview, CodeEditor editor, string tag)
    {
        int at = editor.Document.Snapshot.ToString().IndexOf(tag, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{tag}' is not in the file");
        return preview.Elements.FirstOrDefault(e => e.Tag.Start == at)
            ?? throw new InvalidOperationException($"The preview drew no element for {tag}; it drew: {string.Join(", ", preview.Elements.Select(e => TagOf(editor, e)))}");
    }

    private static string TagOf(CodeEditor editor, PreviewElement element)
    {
        string text = editor.Document.Snapshot.ToString();
        return element.Tag.End <= text.Length ? text[element.Tag.Start..element.Tag.End] : string.Empty;
    }

    /// <summary>A card of the view model's: its design size and a creatable design-time instance, App.xaml's brush behind.</summary>
    private const string Card = """
        <UserControl x:Class="Gallery.Card"
                     xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                     xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                     xmlns:vm="clr-namespace:Gallery.ViewModels"
                     mc:Ignorable="d"
                     d:DesignWidth="320" d:DesignHeight="120"
                     d:DataContext="{d:DesignInstance vm:MainViewModel, IsDesignTimeCreatable=True}">
            <Border Background="{StaticResource Accent}" Padding="8">
                <StackPanel>
                    <TextBlock Text="{Binding Greeting}" HorizontalAlignment="Left" Foreground="White" />
                    <TextBlock Text="{Binding Owner.Name}" HorizontalAlignment="Left" Foreground="White" />
                </StackPanel>
            </Border>
        </UserControl>

        """;

    /// <summary><see cref="WpfXamlTests.Gallery"/>'s app, made and restored only where it is previewed.</summary>
    public sealed class Gallery : SharedProject
    {
        public Gallery()
            : base("wpfpreview-")
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }
            TestFolders.Prepare(() => WpfXamlTests.Gallery.Make(Folder), Folder);
            Prepare();
        }
    }
}
