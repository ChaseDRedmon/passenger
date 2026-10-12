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
    private bool isActive;

    public OpenDocument(string path, FileText file)
    {
        Path = path;
        Text = new TextDocument(file.Text);
        Encoding = file.Encoding;
        HasBom = file.HasBom;
        EncodingName = file.EncodingName;
        Language = Language.ForPath(path);
        Highlighter = Language.Name == "C#" ? new CSharpHighlighter(Text) : null;
        Text.Changed += (_, _) => Raise(nameof(IsModified));
    }

    /// <summary>Colours from the in-process parser, or null for a language without one yet.</summary>
    public IHighlighter? Highlighter { get; }

    /// <summary>Lets go of everything the open file holds beyond its text, when its tab closes.</summary>
    public void Close() => (Highlighter as IDisposable)?.Dispose();

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public TextDocument Text { get; }
    public Encoding Encoding { get; }
    public bool HasBom { get; }
    public string EncodingName { get; }
    public Language Language { get; }
    public bool IsModified => Text.IsModified;
    public Avalonia.Media.Geometry Icon => Explorer.FileIcons.ForFile(Path);
    public Avalonia.Media.IBrush IconBrush => Explorer.FileIcons.BrushForFile(Path);

    public bool IsActive
    {
        get => isActive;
        set => Set(ref isActive, value);
    }

    public SelectionSet Selections { get; set; } = SelectionSet.Single(0);
    public Vector Scroll { get; set; }

    public void Save()
    {
        FileText.Save(Path, Text.Snapshot.ToString(), Encoding, HasBom);
        Text.MarkSaved();
        Raise(nameof(IsModified));
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
