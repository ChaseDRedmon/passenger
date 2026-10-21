using System.Buffers.Binary;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Editors;
using Passenger.App.Workspace;
using Passenger.Core;
using SkiaSharp;

namespace Passenger.Tests;

/// <summary>Local images and fonts through ordinary file opening, read-only actions and the editor tab lifecycle.</summary>
public sealed class AssetPreviewTests : IDisposable
{
    private readonly string folder = TestFolders.Create("assets-");
    private readonly string settings = TestFolders.Create("asset-settings-");

    /// <inheritdoc/>
    public void Dispose()
    {
        TestFolders.Delete(folder);
        TestFolders.Delete(settings);
    }

    [AvaloniaTheory]
    [InlineData(".png", 64, 32)]
    [InlineData(".PNG", 64, 32)]
    [InlineData(".jpg", 64, 32)]
    [InlineData(".jpeg", 64, 32)]
    [InlineData(".webp", 64, 32)]
    [InlineData(".bmp", 1, 1)]
    [InlineData(".gif", 1, 1)]
    [InlineData(".ico", 64, 32)]
    public void Opening_a_common_image_shows_its_pixels_and_dimensions_in_a_read_only_tab(string extension, int width, int height)
    {
        string file = "picture" + extension;
        File.WriteAllBytes(Path.Combine(folder, file), ImageBytes(extension));
        MainWindow window = Open();
        Assert.Null(window.OpenFile(file));
        Ui.Settle();

        OpenDocument document = Assert.IsType<OpenDocument>(window.EditorArea.Active);
        Assert.True(document.IsReadOnly);
        Assert.False(window.EditorArea.CodeEditor.IsVisible);
        Assert.Empty(document.Text.Snapshot.ToString());
        Assert.Equal(new PixelSize(width, height), document.Asset!.Bitmap!.PixelSize);
        AssetPreviewControl preview = Preview(window.EditorArea.ActiveGroup);
        Image image = Assert.Single(preview.GetVisualDescendants().OfType<Image>());
        Assert.Same(document.Asset.Bitmap, image.Source);
        Assert.True(image.IsEffectivelyVisible && image.Bounds.Width > 0 && image.Bounds.Height > 0, "The decoded image has no visible room");
        Assert.Equal(Stretch.Uniform, image.Stretch);
        Assert.Contains(preview.GetVisualDescendants().OfType<TextBlock>(), text => text.Text!.Contains($"{width} × {height} pixels", StringComparison.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public void An_image_preview_preserves_its_colours_and_transparent_pixels_show_the_editor_background()
    {
        // Sample broad colour regions rather than the interpolated edge between two enlarged pixels.
        using SKBitmap bitmap = new(64, 32);
        bitmap.Erase(SKColors.Transparent);
        using SKCanvas canvas = new(bitmap);
        using SKPaint paint = new() { Color = SKColors.Red };
        canvas.DrawRect(0, 0, 32, 32, paint);
        using SKImage source = SKImage.FromBitmap(bitmap);
        using SKData encoded = source.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(folder, "transparent.png"), encoded.ToArray());
        MainWindow window = Open();
        Assert.Null(window.OpenFile("transparent.png"));
        Ui.Settle();

        AssetPreviewControl preview = Preview(window.EditorArea.ActiveGroup);
        Image image = Assert.Single(preview.GetVisualDescendants().OfType<Image>());
        Point red = image.TranslatePoint(new Point(image.Bounds.Width / 4, image.Bounds.Height / 2), window)!.Value;
        Point clear = image.TranslatePoint(new Point(image.Bounds.Width * 3 / 4, image.Bounds.Height / 2), window)!.Value;
        Point ground = preview.TranslatePoint(new Point(1, preview.Bounds.Height / 2), window)!.Value;
        Assert.Equal(Colors.Red, Ui.Pixel(window, red));
        Assert.Equal(Ui.Pixel(window, ground), Ui.Pixel(window, clear));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(".ttf", "JetBrainsMono-Regular.ttf", FontStyle.Normal)]
    [InlineData(".otf", "JetBrainsMono-Italic.ttf", FontStyle.Italic)]
    public void A_local_font_shows_its_actual_face_letters_digits_and_every_ASCII_symbol(string extension, string source, FontStyle style)
    {
        string file = "uninstalled-face" + extension;
        CopyFont(file, source);
        MainWindow window = Open();
        Assert.Null(window.OpenFile(file));
        Ui.Settle();

        GlyphTypeface face = window.EditorArea.Active!.Asset!.Typeface!;
        Assert.Equal("JetBrains Mono", face.FamilyName);
        Assert.Equal(style, face.Style);
        AssetPreviewControl preview = Preview(window.EditorArea.ActiveGroup);
        string[] samples = ["ABCDEFGHIJKLMNOPQRSTUVWXYZ", "abcdefghijklmnopqrstuvwxyz", "0123456789", "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"];
        foreach (string sample in samples)
        {
            Control shown = Assert.Single(preview.GetVisualDescendants().OfType<Control>(), control => AutomationProperties.GetName(control) == sample);
            Assert.True(shown.IsEffectivelyVisible && shown.Bounds.Width > 100 && shown.Bounds.Height > 16, $"The font sample '{sample}' has no visible glyph area");
        }
        Assert.Contains(preview.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "All printable ASCII glyphs are present.");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Typing_pasting_saving_and_encoding_actions_never_write_a_preview_or_the_text_tab_it_replaced()
    {
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "Keep these notes.\n");
        byte[] original = ImageBytes(".png");
        File.WriteAllBytes(Path.Combine(folder, "picture.png"), original);
        CopyFont("face.ttf");
        byte[] font = File.ReadAllBytes(Path.Combine(folder, "face.ttf"));
        MainWindow window = Open();
        window.OpenFile("notes.txt");
        Ui.Settle();
        window.KeyTextInput("Changed: ");
        Ui.Settle();

        foreach (string file in new[] { "picture.png", "face.ttf" })
        {
            Assert.Null(window.OpenFile(file));
            Ui.Settle();
            Assert.True(Preview(window.EditorArea.ActiveGroup).Focus());
            window.KeyTextInput("Do not write this");
            await window.Clipboard!.SetTextAsync("Do not paste this");
            Ui.Press(window, Ui.Shortcut(Passenger.Editor.EditorActions.Paste));
            Ui.Press(window, Ui.Shortcut(AppActions.Save));
            Assert.False(window.Perform(AppActions.FileEncoding), "A binary preview offered text encoding conversion");
            Assert.False(window.EditorArea.Active!.IsModified);
        }
        Ui.Run(window, AppActions.SaveAll);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(folder, "picture.png")));
        Assert.Equal(font, File.ReadAllBytes(Path.Combine(folder, "face.ttf")));
        window.OpenFile("notes.txt");
        Ui.Settle();
        Assert.True(window.EditorArea.CodeEditor.IsVisible);
        Assert.Equal("Changed: Keep these notes.\n", window.EditorArea.CodeEditor.Document.Snapshot.ToString());
        Assert.Equal("Changed: Keep these notes.\n", File.ReadAllText(Path.Combine(folder, "notes.txt")));
        window.Close();
    }

    [AvaloniaFact]
    public void Preview_tabs_split_move_close_reopen_and_return_with_the_workspace()
    {
        File.WriteAllBytes(Path.Combine(folder, "picture.png"), ImageBytes(".png"));
        CopyFont("face.ttf");
        MainWindow first = Open();
        first.OpenFile("picture.png");
        Ui.Settle();
        EditorArea area = first.EditorArea;
        EditorGroup left = area.ActiveGroup;
        AssetPreviewControl original = Preview(left);
        Assert.True(first.Perform(AppActions.SplitRight));
        Ui.Settle();
        EditorGroup right = area.ActiveGroup;
        Assert.NotSame(original, Preview(right));
        Assert.Same(left.Active!.Asset, right.Active!.Asset);
        Ui.Run(first, AppActions.CloseTab);
        Assert.Single(area.Groups);
        Assert.Same(original, Preview(left));
        Ui.Run(first, AppActions.CloseTab);
        Assert.Empty(area.Documents);
        Assert.True(first.Perform(AppActions.ReopenClosedTab));
        Ui.Settle();
        Assert.Equal("picture.png", area.Active!.Name);
        Assert.NotSame(original, Preview(area.ActiveGroup));
        Assert.True(first.Perform(AppActions.SplitDown));
        Ui.Settle();
        first.OpenFile("face.ttf");
        Ui.Settle();
        area.Move(area.ActiveGroup, area.Active!, left);
        Ui.Settle();
        Assert.NotNull(Preview(left));
        first.Close();

        MainWindow second = Open();
        Assert.Equal(2, second.EditorArea.Groups.Count);
        Assert.Equal(2, second.EditorArea.Documents.Count);
        foreach (EditorGroup group in second.EditorArea.Groups)
        {
            Assert.NotNull(Preview(group));
            Assert.False(group.Editor.IsVisible);
        }
        Assert.Contains(second.EditorArea.Documents, document => document.Asset?.Typeface?.FamilyName == "JetBrains Mono");
        second.Close();
    }

    [AvaloniaFact]
    public void Reloading_an_image_updates_both_splits_but_invalid_pixel_data_keeps_the_previous_picture_and_explains_why()
    {
        string path = Path.Combine(folder, "picture.png");
        byte[] complete = ImageBytes(".png");
        File.WriteAllBytes(path, complete);
        MainWindow window = Open();
        window.OpenFile("picture.png");
        Ui.Settle();
        Assert.True(window.Perform(AppActions.SplitRight));
        Ui.Settle();
        AssetPreview good = window.EditorArea.Active!.Asset!;

        File.WriteAllBytes(path, complete[..(complete.Length / 2)]);
        window.FollowWritten([path]);
        Ui.Settle();
        Assert.Same(good, window.EditorArea.Active.Asset);
        Assert.Contains("Preview reload failed", window.EditorArea.ActiveGroup.BannerText!, StringComparison.Ordinal);
        File.WriteAllBytes(path, ImageBytes(".png", width: 16, height: 8));
        window.FollowWritten([path]);
        Ui.Settle();
        foreach (EditorGroup group in window.EditorArea.Groups)
        {
            Image shown = Assert.Single(Preview(group).GetVisualDescendants().OfType<Image>());
            Assert.Same(group.Active!.Asset!.Bitmap, shown.Source);
            Assert.Equal(new PixelSize(16, 8), group.Active.Asset.Bitmap!.PixelSize);
            Assert.Null(group.Active.Notice);
        }
        string moved = Path.Combine(folder, "renamed.png");
        Assert.Null(window.EditorArea.MoveFile(path, moved));
        Assert.Equal("renamed.png", window.EditorArea.Active.Name);
        Assert.NotNull(Preview(window.EditorArea.ActiveGroup));
        string binary = Path.Combine(folder, "renamed.bin");
        Assert.Null(window.EditorArea.MoveFile(moved, binary));
        Assert.Null(window.EditorArea.Borrow(binary));
        Assert.NotNull(Preview(window.EditorArea.ActiveGroup));
        window.Close();
    }

    [AvaloniaFact]
    public void Invalid_empty_missing_unsupported_and_oversized_assets_fail_without_opening_binary_text()
    {
        File.WriteAllBytes(Path.Combine(folder, "broken.png"), ImageBytes(".png")[..50]);
        File.WriteAllBytes(Path.Combine(folder, "broken.ttf"), "not a font"u8.ToArray());
        File.WriteAllBytes(Path.Combine(folder, "empty.otf"), []);
        File.WriteAllBytes(Path.Combine(folder, "unknown.bin"), [0, 1, 2, 3]);
        using (FileStream large = File.Create(Path.Combine(folder, "large.ttf")))
        {
            large.SetLength((32 * 1024 * 1024) + 1);
        }
        using (FileStream large = File.Create(Path.Combine(folder, "large.png")))
        {
            large.SetLength((64 * 1024 * 1024) + 1);
        }
        byte[] huge = ImageBytes(".bmp");
        BinaryPrimitives.WriteInt32LittleEndian(huge.AsSpan(18), 10000);
        BinaryPrimitives.WriteInt32LittleEndian(huge.AsSpan(22), 10000);
        File.WriteAllBytes(Path.Combine(folder, "huge.bmp"), huge);
        MainWindow window = Open();
        foreach (string file in new[] { "broken.png", "broken.ttf", "empty.otf", "unknown.bin", "missing.png", "large.ttf", "large.png", "huge.bmp" })
        {
            string? error = window.OpenFile(file);
            Ui.Settle();
            Assert.False(string.IsNullOrEmpty(error), $"{file} was accepted without an error");
            Assert.Empty(window.EditorArea.Documents);
        }
        Assert.Contains("32 million pixels", window.OpenFile("huge.bmp")!, StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(folder, "vector.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" />");
        Assert.Null(window.OpenFile("vector.svg"));
        Ui.Settle();
        Assert.Null(window.EditorArea.Active!.Asset);
        Assert.True(window.EditorArea.CodeEditor.IsVisible);
        window.Close();
    }

    /// <summary>A preview's tab holds an empty stand-in for text: nothing that reads or changes a file's text may take it for
    /// the image's, or an agent's replace would claim to have changed a picture, and a bookmark would mark a line it lacks.</summary>
    [AvaloniaFact]
    public void A_preview_has_no_text_for_the_status_bar_an_agent_find_and_replace_in_files_or_a_bookmark()
    {
        string picture = Path.Combine(folder, "picture.png");
        byte[] original = ImageBytes(".png");
        File.WriteAllBytes(picture, original);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "Keep these notes.\n");
        MainWindow window = Open();
        window.OpenFile("notes.txt");
        Ui.Settle();
        Assert.True(window.FindControl<Button>("EncodingButton")!.IsVisible, "A text file's encoding is not shown");
        window.OpenFile("picture.png");
        Ui.Settle();

        Assert.False(window.FindControl<Button>("EncodingButton")!.IsVisible, "An image's preview shows a text encoding");
        Assert.False(window.FindControl<Button>("LineSeparatorButton")!.IsVisible, "An image's preview shows a line separator");
        Assert.False(window.Perform(AppActions.ToggleBookmark), "A bookmark was put on a line of an image");
        Assert.False(window.Perform(AppActions.ToggleBreakpoint), "A breakpoint was put on a line of an image");
        Assert.Empty(window.Bookmarks.All);

        Passenger.App.Mcp.PassengerTools agent = new(window);
        foreach (Task asked in new Task[] { agent.EditorText("picture.png"), agent.Edit("picture.png", "x", "y") })
        {
            Ui.WaitFor(() => asked.IsCompleted, "the agent's answer");
            Assert.True(asked.IsFaulted, "An agent read or edited an image's preview as text");
            Assert.Contains("picture.png is an image, shown as a read-only preview", asked.Exception!.InnerException!.Message, StringComparison.Ordinal);
        }
        // A pattern that matches an empty line finds the text file's lines, never the image's empty stand-in.
        Task<Passenger.App.Mcp.FindInFilesResult> finding = agent.FindInFiles("^", regex: true);
        Ui.WaitFor(() => finding.IsCompleted, "find_in_files' answer");
        Assert.DoesNotContain(finding.Result.Matches, m => m.Path == "picture.png");
        Assert.Contains(finding.Result.Matches, m => m.Path == "notes.txt");
        Task<string> replacing = agent.ReplaceInFiles("^", "// ", regex: true);
        Ui.WaitFor(() => replacing.IsCompleted, "replace_in_files' answer");
        Assert.DoesNotContain("picture.png", replacing.Result, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(picture));
        Assert.Empty(window.EditorArea.Active!.Text.Snapshot.ToString());
        window.Close();
    }

    /// <summary>A folder renamed elsewhere (mv, git mv, Finder) takes its open files with it: an image's tab follows it as
    /// a text file's does, known by its bytes, rather than closing as if it had been deleted.</summary>
    [AvaloniaFact]
    public void An_open_preview_follows_its_folder_renamed_elsewhere()
    {
        string old = Path.Combine(folder, "art", "Old");
        string renamed = Path.Combine(folder, "art", "New");
        Directory.CreateDirectory(old);
        File.WriteAllBytes(Path.Combine(old, "picture.png"), ImageBytes(".png"));
        MainWindow window = Open();
        window.OpenFile(Path.Combine(old, "picture.png"));
        Ui.Settle();

        Directory.Move(old, renamed);
        Ui.WaitFor(() => window.EditorArea.Documents.Count == 1 && PathNames.IsUnder(window.EditorArea.Documents[0].Path, PathNames.Canonical(renamed)),
            "the image's tab to follow its folder to its new name");
        Assert.Equal("picture.png", window.EditorArea.Active!.Name);
        Assert.NotNull(Preview(window.EditorArea.ActiveGroup));
        window.Close();
        Assert.False(Directory.Exists(old), "The folder renamed elsewhere was made again under its old name");
    }

    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = settings };
        window.Show();
        window.KeepLayout();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    /// <summary>The bundled face copied to a new name, so the preview cannot depend on an installed family.</summary>
    private void CopyFont(string file, string source = "JetBrainsMono-Regular.ttf") =>
        File.Copy(Path.Combine(XamlTests.Repository, "src", "Passenger.Editor", "Fonts", source), Path.Combine(folder, file));

    private static AssetPreviewControl Preview(EditorGroup group) => Assert.Single(group.GetVisualDescendants().OfType<AssetPreviewControl>());

    /// <summary>Small real raster files without external fixtures; GIF and BMP use minimal headers, ICO carries a PNG.</summary>
    private static byte[] ImageBytes(string extension, int width = 64, int height = 32)
    {
        if (extension == ".gif")
        {
            return Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
        }
        if (extension == ".bmp")
        {
            byte[] bytes = new byte[58];
            bytes[0] = (byte)'B';
            bytes[1] = (byte)'M';
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), 1);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 24);
            bytes[54] = 200;
            return bytes;
        }
        using SKBitmap bitmap = new(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using SKImage image = SKImage.FromBitmap(bitmap);
        SKEncodedImageFormat format = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png,
        };
        using SKData encoded = image.Encode(format, 90);
        byte[] raster = encoded.ToArray();
        if (extension != ".ico")
        {
            return raster;
        }
        byte[] icon = new byte[22 + raster.Length];
        BinaryPrimitives.WriteInt16LittleEndian(icon.AsSpan(2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(icon.AsSpan(4), 1);
        icon[6] = (byte)width;
        icon[7] = (byte)height;
        BinaryPrimitives.WriteInt16LittleEndian(icon.AsSpan(10), 1);
        BinaryPrimitives.WriteInt16LittleEndian(icon.AsSpan(12), 32);
        BinaryPrimitives.WriteInt32LittleEndian(icon.AsSpan(14), raster.Length);
        BinaryPrimitives.WriteInt32LittleEndian(icon.AsSpan(18), 22);
        raster.CopyTo(icon, 22);
        return icon;
    }
}
