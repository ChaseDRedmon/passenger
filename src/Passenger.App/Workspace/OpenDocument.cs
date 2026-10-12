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
    private IReadOnlyList<TestMark> testMarks = [];
    private IReadOnlyList<ChangeMark> changeMarks = [];
    private IReadOnlyList<string?> gutterAnnotations = [];

    public OpenDocument(string path, FileText file)
    {
        Path = path;
        Text = new TextDocument(file.Text);
        Encoding = file.Encoding;
        HasBom = file.HasBom;
        EncodingName = file.EncodingName;
        Language = Language.ForPath(path);
        highlighter = Language.Name == "C#" ? new CSharpHighlighter(Text) : null;
        Text.Changed += (_, _) => Raise(nameof(IsModified));
    }

    /// <summary>Raised after a save, so the language server hears of it.</summary>
    public event EventHandler? Saved;

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

    /// <summary>Text per line at the gutter's left (Git blame's), empty for none.</summary>
    public IReadOnlyList<string?> GutterAnnotations
    {
        get => gutterAnnotations;
        set => Set(ref gutterAnnotations, value);
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
    public Encoding Encoding { get; }
    public bool HasBom { get; }
    public string EncodingName { get; }
    public Language Language { get; }
    public bool IsModified => Text.IsModified;
    public Avalonia.Media.Geometry Icon => Explorer.FileIcons.ForFile(Path);
    public Avalonia.Media.IBrush IconBrush => Explorer.FileIcons.BrushForFile(Path);

    public SelectionSet Selections { get; set; } = SelectionSet.Single(0);
    public Vector Scroll { get; set; }

    /// <summary>Takes the file's text from disk again when something else changed it there (a checkout, a rollback),
    /// as one undoable edit over only what differs, so carets before it stay put; never over unsaved changes.
    /// Whether it changed anything.</summary>
    public bool Reload()
    {
        if (IsModified || FileText.Load(Path) is not { } file)
        {
            return false;
        }
        string now = Text.Snapshot.ToString();
        string disk = file.Text;
        if (now == disk)
        {
            return false;
        }
        int start = 0;
        while (start < now.Length && start < disk.Length && now[start] == disk[start])
        {
            start++;
        }
        int end = 0;
        while (end < now.Length - start && end < disk.Length - start && now[now.Length - 1 - end] == disk[disk.Length - 1 - end])
        {
            end++;
        }
        SelectionSet before = Selections;
        Text.Edit([new TextEdit(start, now.Length - start - end, disk[start..(disk.Length - end)])], before, before.Clamp(disk.Length));
        Text.BreakUndoGroup();
        Text.MarkSaved();
        Raise(nameof(IsModified));
        return true;
    }

    public void Save()
    {
        FileText.Save(Path, Text.Snapshot.ToString(), Encoding, HasBom);
        Text.MarkSaved();
        Raise(nameof(IsModified));
        Saved?.Invoke(this, EventArgs.Empty);
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
