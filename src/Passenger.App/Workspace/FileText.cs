using System.Text;

namespace Passenger.App.Workspace;

/// <summary>A file's text and how it was stored, so saving writes it back the same way: the same
/// encoding, a byte-order mark only if it had one, and its own line endings (kept by the document).</summary>
public sealed record FileText(string Text, Encoding Encoding, bool HasBom)
{
    /// <summary>How much of a file is checked for a NUL byte before it is called binary.</summary>
    private const int BinaryProbe = 8000;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public string EncodingName => Encoding switch
    {
        UTF8Encoding => HasBom ? "UTF-8 BOM" : "UTF-8",
        UnicodeEncoding { CodePage: 1201 } => "UTF-16 BE",
        UnicodeEncoding => "UTF-16",
        _ => Encoding.WebName.ToUpperInvariant(),
    };

    /// <summary>Reads a file as text, or returns null for one that looks binary. Without a BOM it is
    /// UTF-8 if it decodes as UTF-8 and Latin-1 otherwise, which reads every byte.</summary>
    public static FileText? Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new FileText(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true), true);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return new FileText(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode, true);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return new FileText(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode, true);
        }
        if (bytes.AsSpan(0, Math.Min(bytes.Length, BinaryProbe)).Contains((byte)0))
        {
            return null;
        }
        try
        {
            return new FileText(StrictUtf8.GetString(bytes), new UTF8Encoding(false), false);
        }
        catch (DecoderFallbackException)
        {
            return new FileText(Encoding.Latin1.GetString(bytes), Encoding.Latin1, false);
        }
    }

    /// <summary>Writes through a temporary file beside the target, so a failed write never leaves a
    /// half-written file where the old one was. A symlink is written through to its target, and the
    /// file keeps its permissions (a script stays executable). A file deleted since it was opened (a
    /// git clean, another program) is written anew, its folder too.</summary>
    /// <summary>A file about to be written by a save, so a folder watcher takes it for the change it is rather than a
    /// new file (a save writes a temporary file beside it and moves it over).</summary>
    public static event Action<string>? Writing;

    /// <summary>The temporary file a save writes first, which nothing but the save should see.</summary>
    public const string SaveSuffix = ".passenger-save";

    public static void Save(string path, string text, Encoding encoding, bool bom)
    {
        Writing?.Invoke(path);
        bool there = File.Exists(path) || new FileInfo(path).LinkTarget is not null;
        string target = there ? File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path : path;
        if (!there)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        }
        byte[] preamble = bom ? encoding.GetPreamble() : [];
        byte[] body = encoding.GetBytes(text);
        string temporary = target + SaveSuffix;
        try
        {
            using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(preamble);
                stream.Write(body);
                stream.Flush(flushToDisk: true);
            }
            if (!OperatingSystem.IsWindows() && File.Exists(target))
            {
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(target));
            }
            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            // A failed save leaves the old file as it was, and nothing beside it.
            File.Delete(temporary);
            throw;
        }
    }
}
