using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Passenger.App.Workspace;

/// <summary>A decoded image or local font owned by its <see cref="OpenDocument"/> and shared by its tabs. Each group
/// borrows it for a view of its own; the document releases it after its last view or hold closes.</summary>
public sealed class AssetPreview : IDisposable
{
    /// <summary>The most encoded data kept while decoding an image or a font.</summary>
    private const long ImageBytes = 64 * 1024 * 1024;
    private const long FontBytes = 32 * 1024 * 1024;

    /// <summary>The most pixels an image may request, checked before its pixel buffer is allocated.</summary>
    private const long ImagePixels = 32 * 1024 * 1024;

    private readonly IFontCollection? fonts;

    private AssetPreview(Bitmap bitmap)
    {
        Bitmap = bitmap;
    }

    private AssetPreview(GlyphTypeface typeface, IFontCollection fonts)
    {
        Typeface = typeface;
        this.fonts = fonts;
    }

    /// <summary>The decoded image, or null for a font; borrowed by views, never disposed by them.</summary>
    public Bitmap? Bitmap { get; }

    /// <summary>The file's own glyph face, or null for an image, without installed-font substitution.</summary>
    public GlyphTypeface? Typeface { get; }

    /// <summary>Whether a path names an image or font type shown as a read-only preview instead of text.</summary>
    public static bool Supports(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".ico" or ".ttf" or ".otf";

    /// <summary>Reads an image or font into a bounded snapshot and decodes it, rejecting empty, oversized or invalid
    /// content with an <see cref="IOException"/>. Images show the codec's first frame; font collections are refused.
    /// The caller owns the result until its document closes.</summary>
    public static AssetPreview Load(string path)
    {
        bool font = System.IO.Path.GetExtension(path).ToLowerInvariant() is ".ttf" or ".otf";
        using FileStream file = File.OpenRead(path);
        long length = file.Length;
        long limit = font ? FontBytes : ImageBytes;
        if (length == 0 || length > limit)
        {
            throw new IOException($"The {(font ? "font" : "image")} must be non-empty and no larger than {limit / (1024 * 1024)} MB.");
        }
        // A growing file must not feed the decoder more than the length whose limit was checked.
        byte[] bytes = new byte[(int)length];
        file.ReadExactly(bytes);
        if (font && bytes.AsSpan().StartsWith("ttcf"u8))
        {
            throw new IOException("Font collections are not supported.");
        }
        using MemoryStream stream = new(bytes, writable: false);
        try
        {
            return font ? LoadFont(stream) : LoadImage(stream);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new IOException($"The {(font ? "font" : "image")} could not be decoded: {error.Message}", error);
        }
    }

    /// <summary>Loads a face in a collection private to this asset, so opening it changes no installed-font lookup.</summary>
    private static AssetPreview LoadFont(Stream stream)
    {
        LocalFontCollection collection = new();
        try
        {
            if (!collection.TryAddGlyphTypeface(stream, out GlyphTypeface? typeface))
            {
                throw new IOException("The local font could not be decoded.");
            }
            return new AssetPreview(typeface, collection);
        }
        catch
        {
            ((IDisposable)collection).Dispose();
            throw;
        }
    }

    /// <summary>Decodes into the bitmap the views draw, without a second full-size pixel buffer. A partial decode is
    /// refused before the bitmap is handed to the document.</summary>
    private static AssetPreview LoadImage(Stream stream)
    {
        using SKCodec codec = SKCodec.Create(stream) ?? throw new IOException("The image could not be decoded.");
        SKImageInfo info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > ImagePixels)
        {
            throw new IOException("The image must contain no more than 32 million pixels.");
        }
        WriteableBitmap bitmap = new(new PixelSize(info.Width, info.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        try
        {
            using (ILockedFramebuffer frame = bitmap.Lock())
            {
                SKImageInfo pixels = new(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                if (codec.GetPixels(pixels, frame.Address, frame.RowBytes, SKCodecOptions.Default) != SKCodecResult.Success)
                {
                    throw new IOException("The image pixel data is incomplete or invalid.");
                }
            }
            return new AssetPreview(bitmap);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Releases the image or font once every view has let go of its glyph runs.</summary>
    public void Dispose()
    {
        Bitmap?.Dispose();
        fonts?.Dispose();
    }

    /// <summary>A collection used only for its public stream-loading API, never registered with the font manager.</summary>
    private sealed class LocalFontCollection : FontCollectionBase
    {
        public override Uri Key { get; } = new("fonts:preview");
    }
}
