using Passenger.App.Workspace;
using Passenger.Text;

namespace Passenger.App;

/// <summary>The window's Find in Files and Replace in Files, over the folder's index and the open files' own text.</summary>
public partial class MainWindow
{
    /// <summary>Find in Files (Ctrl+Shift+F) or, with replace, Replace in Files (Ctrl+Shift+H), seeded with the
    /// selection's word as Rider seeds it.</summary>
    public Navigation.FindInFilesPopup? ShowFindInFiles(bool replace)
    {
        if (folder is null)
        {
            return null;
        }
        Navigation.FindInFilesPopup popup = new(SearchFiles, TextOf, replace, SelectedWord());
        popup.Done += (_, match) =>
        {
            ClosePopup();
            if (match is not null)
            {
                NavigateTo(match.Path, new TextPosition(match.Line, match.Column));
                if (Editors.Active?.Path == match.Path)
                {
                    int at = Editors.CodeEditor.Document.Snapshot.GetOffset(new TextPosition(match.Line, match.Column));
                    Editors.CodeEditor.Select(new TextRange(at, match.Length));
                }
            }
        };
        popup.ReplaceRequested += async (_, asked) =>
        {
            int files = asked.Matches.Select(m => m.Path).Distinct().Count();
            string what = $"{asked.Matches.Count} match{(asked.Matches.Count == 1 ? "" : "es")} in {files} file{(files == 1 ? "" : "s")}";
            if (await Confirm("Replace in Files", $"Replace {what} with '{asked.Replacement}'? An open file's change can be undone there; a closed file is written.", "Replace All"))
            {
                ReplaceInFiles(asked.Query, asked.Replacement, [.. asked.Matches.Select(m => m.Path).Distinct()]);
                StatusText.Text = $"Replaced {what}";
                await popup.SearchAsync();
            }
        };
        ShowPopup(popup);
        return popup;
    }

    /// <summary>Replaces a query's matches in these files: an open one as one undoable edit, a closed one written in
    /// its own encoding.</summary>
    public void ReplaceInFiles(SearchQuery query, string replacement, IReadOnlyList<string> paths)
    {
        foreach (string path in paths)
        {
            if (Editors.Documents.FirstOrDefault(d => d.Path == path) is { } open)
            {
                string before = open.Text.Snapshot.ToString();
                string after = FileSearch.ReplaceAll(before, query, replacement);
                if (after != before)
                {
                    open.Text.SetText(after, open.Selections);
                }
            }
            else if (FileText.Load(path) is { } file)
            {
                string after = FileSearch.ReplaceAll(file.Text, query, replacement);
                if (after != file.Text)
                {
                    FileText.Save(path, after, file.Encoding, file.HasBom);
                }
            }
        }
    }

    private Task<IReadOnlyList<SearchMatch>> SearchFiles(SearchQuery query, CancellationToken cancellation)
    {
        Dictionary<string, TextSnapshot> open = Editors.Documents.ToDictionary(d => d.Path, d => d.Text.Snapshot);
        return FileSearch.FindAsync(index.Root ?? folder!, index.Files, query, open, cancellation);
    }

    /// <summary>A file's text for the preview: the editor's when it is open.</summary>
    private TextSnapshot? TextOf(string path) =>
        Editors.Documents.FirstOrDefault(d => d.Path == path)?.Text.Snapshot
        ?? (FileSearch.Read(path) is { } text ? TextSnapshot.From(text) : null);
}
