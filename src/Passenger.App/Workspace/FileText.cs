using System.Text;

namespace Passenger.App.Workspace;

/// <summary>A file's text and how it was stored, so saving writes it back the same way: the same encoding (or UTF-8,
/// when that cannot hold what was typed), a byte-order mark only if it had one, and its own line endings (kept by the
/// document).</summary>
public sealed record FileText(string Text, Encoding Encoding, bool HasBom)
{
    /// <summary>How much of a file is checked for a NUL byte before it is called binary.</summary>
    private const int BinaryProbe = 8000;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public string EncodingName => NameOf(Encoding, HasBom);

    private static string NameOf(Encoding encoding, bool bom) => encoding switch
    {
        UTF8Encoding => bom ? "UTF-8 BOM" : "UTF-8",
        UnicodeEncoding { CodePage: 1201 } => "UTF-16 BE",
        UnicodeEncoding => "UTF-16",
        _ => encoding.WebName.ToUpperInvariant(),
    };

    /// <summary>Reads a file as text, or returns null for one that looks binary. Without a BOM it is
    /// UTF-8 if it decodes as UTF-8 and Latin-1 otherwise, which reads every byte.</summary>
    public static FileText? Load(string path)
    {
        byte[] bytes = Read(path);
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

    /// <summary>A file's bytes, read sharing writes and deletes, so the read is never in another program's way: on Windows
    /// one that shared only reading (File.ReadAllBytes) made git's checkout or rollback of the file, which deletes it
    /// first, fail for that moment, as the folder watcher reads a file again right after each change to it.</summary>
    private static byte[] Read(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using MemoryStream bytes = new();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    /// <summary>A file about to be written by a save, so a folder watcher takes it for the change it is rather than a
    /// new file (a save writes a temporary file beside it and moves it over).</summary>
    public static event Action<string>? Writing;

    /// <summary>The temporary file a save writes first, which nothing but the save should see.</summary>
    public const string SaveSuffix = ".passenger-save";

    /// <summary>Whether a path is a save's temporary file, there beside its file for the moment the save writes: what
    /// reads the folder meanwhile (the folder watcher, Git's status, Go to File's index, the Explorer) passes it over
    /// rather than take it for a new file.</summary>
    public static bool IsSaving(string path) => path.EndsWith(SaveSuffix, StringComparison.Ordinal);

    /// <summary>Writes through a temporary file beside the target, so a failed write never leaves a half-written file
    /// where the old one was; a symlink is written through to its target, the file keeps its permissions, and one
    /// deleted since it was opened is written anew. A text the encoding cannot hold is written as UTF-8 instead, never
    /// with a question mark for each such character; the result is how it was written, with why when it changed.</summary>
    public static (FileText Written, string? Converted) Save(string path, string text, Encoding encoding, bool bom)
    {
        string? unheld = Unheld(encoding, text);
        FileText written = unheld is null ? new FileText(text, encoding, bom) : new FileText(text, new UTF8Encoding(false), false);
        Writing?.Invoke(path);
        bool there = File.Exists(path) || new FileInfo(path).LinkTarget is not null;
        string target = there ? File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path : path;
        if (!there)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        }
        byte[] preamble = written.HasBom ? written.Encoding.GetPreamble() : [];
        byte[] body = written.Encoding.GetBytes(text);
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
            Replace(temporary, target, preamble, body);
        }
        catch
        {
            // A failed save leaves the old file as it was, and nothing beside it.
            File.Delete(temporary);
            throw;
        }
        return (written, unheld is null ? null : $"{Path.GetFileName(path)} was saved as UTF-8: {NameOf(encoding, bom)} cannot hold '{unheld}'");
    }

    /// <summary>Moves the written file over the target. Windows refuses that while another program holds the target
    /// without sharing its deletion (a virus scanner, a build reading it): the move is tried again for a moment, then the
    /// target is written in place, which such a program allows, so no save (nor the one quitting makes) fails for it.</summary>
    private static void Replace(string temporary, string target, byte[] preamble, byte[] body)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, target, overwrite: true);
                return;
            }
            catch (Exception refused) when (OperatingSystem.IsWindows() && refused is IOException or UnauthorizedAccessException && File.Exists(target))
            {
                if (attempt == 3)
                {
                    break;
                }
                Thread.Sleep(50);
            }
        }
        using (FileStream stream = new(target, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            stream.Write(preamble);
            stream.Write(body);
            stream.SetLength(stream.Position);
            stream.Flush(flushToDisk: true);
        }
        File.Delete(temporary);
    }

    /// <summary>The first character of a text that an encoding cannot hold (Latin-1 has no € and no emoji), which its
    /// encoder would write as a question mark; null when it holds them all, as every Unicode encoding does.</summary>
    private static string? Unheld(Encoding encoding, string text)
    {
        if (encoding is UTF8Encoding or UnicodeEncoding or UTF32Encoding)
        {
            return null;
        }
        Encoding strict = (Encoding)encoding.Clone();
        strict.EncoderFallback = EncoderFallback.ExceptionFallback;
        try
        {
            strict.GetByteCount(text);
            return null;
        }
        catch (EncoderFallbackException unheld)
        {
            return unheld.IsUnknownSurrogate() ? new string([unheld.CharUnknownHigh, unheld.CharUnknownLow]) : unheld.CharUnknown.ToString();
        }
    }

    /// <summary>Whether writing this text in that encoding, with a BOM or without, makes the bytes this file has. ASCII
    /// is the same bytes in UTF-8 and Latin-1, so a Latin-1 file left with only ASCII (read as UTF-8) has not changed.</summary>
    public bool IsStoredAs(Encoding encoding, bool bom) =>
        (bom == HasBom && encoding.CodePage == Encoding.CodePage) || Bytes(encoding, bom).AsSpan().SequenceEqual(Bytes(Encoding, HasBom));

    private byte[] Bytes(Encoding encoding, bool bom) => [.. bom ? encoding.GetPreamble() : [], .. encoding.GetBytes(Text)];
}
