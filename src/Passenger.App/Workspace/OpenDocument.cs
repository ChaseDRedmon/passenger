using System.Text;
using Avalonia;
using Passenger.Editor;
using Passenger.Languages.CSharp;
using Passenger.Text;

namespace Passenger.App.Workspace;

/// <summary>A file open in a tab: its text, how to write it back, and where its view was left so
/// switching tabs returns to the same carets and scroll.</summary>
public sealed class OpenDocument : Observable
{
    private IHighlighter? highlighter;
    private IReadOnlyList<Marker> markers = [];
    private IReadOnlyList<LineMark> lineMarks = [];
    private IReadOnlyList<LineAnnotation> lineAnnotations = [];
    private IReadOnlyList<InlayHint> inlayHints = [];
    private IReadOnlyList<TestMark> testMarks = [];
    private IReadOnlyList<ChangeMark> changeMarks = [];
    private IReadOnlyList<string?> gutterAnnotations = [];
    private IReadOnlyList<int> bookmarks = [];
    private IReadOnlyList<FoldRegion> foldRegions = [];

    public OpenDocument(string path, FileText file)
    {
        Path = path;
        Text = new TextDocument(file.Text);
        Encoding = file.Encoding;
        HasBom = file.HasBom;
        EncodingName = file.EncodingName;
        Language = Language.ForPath(path);
        highlighter = Language.Name == "C#" ? new CSharpHighlighter(Text) : null;
        if (highlighter is CSharpHighlighter csharp)
        {
            // Each parse's regions keep the folds of the ones they replace, matched by where they start.
            csharp.FoldsChanged += (_, _) =>
            {
                HashSet<int> folded = [.. foldRegions.Where(r => r.Folded).Select(r => r.Start)];
                FoldRegions = [.. csharp.Folds.Select(r => r with { Folded = folded.Contains(r.Start) })];
            };
        }
        Text.Changed += (_, change) =>
        {
            Selections = TextEditing.MapThrough(Selections, change.Edits).Clamp(change.After.Length);
            if (foldRegions.Count > 0)
            {
                FoldRegions = [.. foldRegions.Select(r => r with
                {
                    Start = TextEditing.MapOffset(r.Start, change.Edits, afterInsertions: true),
                    End = TextEditing.MapOffset(r.End, change.Edits, afterInsertions: true),
                })];
            }
            Raise(nameof(IsModified));
        };
    }

    /// <summary>Raised after a save, so the language server hears of it.</summary>
    public event EventHandler? Saved;

    /// <summary>Raised when a save wrote the file as UTF-8 because its own encoding could not hold the text, with what
    /// the status bar says of it.</summary>
    public event EventHandler<string>? Converted;

    /// <summary>Colours from the in-process parser, replaced by the language server's layered over it
    /// once that is running; null for a language with neither.</summary>
    public IHighlighter? Highlighter
    {
        get => highlighter;
        set => Set(ref highlighter, value);
    }

    /// <summary>The language service's markers (diagnostics), in this text's offsets.</summary>
    public IReadOnlyList<Marker> Markers
    {
        get => markers;
        set => Set(ref markers, value);
    }

    /// <summary>The debugger's marks in the gutter: breakpoints and the execution point.</summary>
    public IReadOnlyList<LineMark> LineMarks
    {
        get => lineMarks;
        set => Set(ref lineMarks, value);
    }

    /// <summary>The tests' run icons beside the test methods and classes it declares.</summary>
    public IReadOnlyList<TestMark> TestMarks
    {
        get => testMarks;
        set => Set(ref testMarks, value);
    }

    /// <summary>The change bars: the lines that differ from the last commit.</summary>
    public IReadOnlyList<ChangeMark> ChangeMarks
    {
        get => changeMarks;
        set => Set(ref changeMarks, value);
    }

    /// <summary>What folds in the text and which of it is folded, in this text's offsets.</summary>
    public IReadOnlyList<FoldRegion> FoldRegions
    {
        get => foldRegions;
        set => Set(ref foldRegions, value);
    }

    /// <summary>The bookmarked lines.</summary>
    public IReadOnlyList<int> Bookmarks
    {
        get => bookmarks;
        set => Set(ref bookmarks, value);
    }

    /// <summary>Text per line at the gutter's left (Git blame's), empty for none.</summary>
    public IReadOnlyList<string?> GutterAnnotations
    {
        get => gutterAnnotations;
        set => Set(ref gutterAnnotations, value);
    }

    /// <summary>The language service's words inside lines (parameter names before arguments), in this text's offsets.</summary>
    public IReadOnlyList<InlayHint> InlayHints
    {
        get => inlayHints;
        set => Set(ref inlayHints, value);
    }

    /// <summary>The debugger's text after lines: inline values, the exception it stopped on.</summary>
    public IReadOnlyList<LineAnnotation> LineAnnotations
    {
        get => lineAnnotations;
        set => Set(ref lineAnnotations, value);
    }

    /// <summary>Lets go of everything the open file holds beyond its text, when its tab closes.</summary>
    public void Close() => (Highlighter as IDisposable)?.Dispose();

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>Whether the language server made this file from an assembly's metadata (Go to Declaration on a
    /// library's symbol): Roslyn decompiles it, or takes its source from Source Link or the PDB, into a folder of its
    /// own, and nothing typed there would reach the library.</summary>
    public bool IsDecompiled => Path.Contains($"{System.IO.Path.DirectorySeparatorChar}MetadataAsSource{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>What the bar over a decompiled file says it came from.</summary>
    public string Origin => Path.Contains("SourceLink", StringComparison.OrdinalIgnoreCase) || Path.Contains("Embedded", StringComparison.OrdinalIgnoreCase)
        ? "Source from the library's Source Link or PDB, read-only"
        : "Decompiled code: the language server made this from the assembly, so it is read-only";
    public TextDocument Text { get; }
    /// <summary>How the file is stored, which a save keeps: as it was read, as it is on disk since (a BOM a formatter
    /// added), or UTF-8 once a save needed it.</summary>
    public Encoding Encoding { get; private set; }
    public bool HasBom { get; private set; }
    public string EncodingName { get; private set; }
    public Language Language { get; }
    public bool IsModified => Text.IsModified;
    public Avalonia.Media.Geometry Icon => Explorer.FileIcons.ForFile(Path);
    public Avalonia.Media.IBrush IconBrush => Explorer.FileIcons.BrushForFile(Path);

    /// <summary>The carets: the editor's while the file is shown there, as left otherwise, moved by every change to the text.</summary>
    public SelectionSet Selections { get; set; } = SelectionSet.Single(0);
    public Vector Scroll { get; set; }

    /// <summary>Whether something other than this IDE changed the file's text on disk since it was last loaded or saved.
    /// A change only to how it is stored there (a BOM a formatter added, a conversion to UTF-8) is taken at once, over
    /// unsaved edits too, since they were made over the same text: the next save keeps it, and no question is asked.</summary>
    public bool ChangedOnDisk()
    {
        FileText? file;
        try
        {
            file = File.Exists(Path) ? FileText.Load(Path) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        if (file is null)
        {
            return false;
        }
        if (file.Text != Text.Saved.ToString())
        {
            return true;
        }
        StoreAs(file);
        return false;
    }

    /// <summary>Takes the file's text and encoding from disk again when something else changed them there (a checkout, a
    /// rollback), as one undoable step over only the lines that differ, so what is anchored elsewhere stays put; over unsaved
    /// changes only when asked (File Cache Conflict's Load File System Changes). Whether it changed the text.</summary>
    public bool Reload(bool overUnsaved = false)
    {
        FileText? file;
        try
        {
            file = (IsModified && !overUnsaved) || !File.Exists(Path) ? null : FileText.Load(Path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        if (file is null)
        {
            return false;
        }
        StoreAs(file);
        if (Text.Snapshot.ToString() == file.Text)
        {
            return false;
        }
        Text.SetText(file.Text, Selections);
        Text.MarkSaved();
        Raise(nameof(IsModified));
        return true;
    }

    public void Save()
    {
        (FileText written, string? converted) = FileText.Save(Path, Text.Snapshot.ToString(), Encoding, HasBom);
        StoreAs(written);
        Text.MarkSaved();
        Raise(nameof(IsModified));
        Saved?.Invoke(this, EventArgs.Empty);
        if (converted is not null)
        {
            Converted?.Invoke(this, converted);
        }
    }

    /// <summary>Takes how the file is stored now, unless that is what it has already in all but name (ASCII reads as
    /// UTF-8 but is the same bytes in Latin-1), so the next save writes it the same way.</summary>
    private void StoreAs(FileText file)
    {
        if (file.IsStoredAs(Encoding, HasBom))
        {
            return;
        }
        Encoding = file.Encoding;
        HasBom = file.HasBom;
        EncodingName = file.EncodingName;
        Raise(nameof(EncodingName));
    }
}

/// <summary>What the editor needs to know about a file's language before any language server
/// answers: its name, its line comment and how it indents.</summary>
public sealed record Language(string Name, string? LineComment, int IndentSize = 4)
{
    public static readonly Language PlainText = new("Text", null);

    public static Language ForPath(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" or ".csx" => new Language("C#", "//"),
        ".csproj" or ".props" or ".targets" or ".xml" or ".axaml" or ".xaml" or ".slnx" or ".resx" or ".config" => new Language("XML", null, 2),
        ".json" => new Language("JSON", null, 2),
        ".js" or ".ts" or ".tsx" or ".jsx" or ".mjs" or ".css" or ".rs" or ".go" or ".java" or ".kt" or ".swift" or ".c" or ".h" or ".cpp" => new Language("Code", "//"),
        ".py" or ".sh" or ".yml" or ".yaml" or ".toml" or ".ps1" or ".rb" => new Language("Script", "#"),
        ".md" => new Language("Markdown", null, 2),
        _ when System.IO.Path.GetFileName(path).Equals("justfile", StringComparison.OrdinalIgnoreCase) => new Language("Script", "#"),
        _ => PlainText,
    };
}
