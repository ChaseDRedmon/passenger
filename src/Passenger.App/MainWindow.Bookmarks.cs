using Passenger.App.Navigation;
using Passenger.App.Workspace;
using Passenger.Text;

namespace Passenger.App;

/// <summary>The window's bookmarks: Toggle Bookmark on the caret's line, Next and Previous across files, the Bookmarks
/// window, and every open file's ribbons kept to the store.</summary>
public partial class MainWindow
{
    private readonly BookmarkStore bookmarks = new();

    public BookmarkStore Bookmarks => bookmarks;

    private void AddBookmarks()
    {
        ToolWindows.BookmarksView view = new(bookmarks, (path, line) =>
            Editors.Documents.FirstOrDefault(d => d.Path == path) is { } open && line < open.Text.Snapshot.LineCount ? open.Text.Snapshot.GetLineText(line) : string.Empty);
        view.GoToRequested += (_, bookmark) => NavigateTo(bookmark.Path, new TextPosition(bookmark.Line, 0));
        Dock.Add(new Docking.ToolWindow("bookmarks", "Bookmarks", "Icon.Bookmark", view, Docking.ToolAnchor.LeftBottom) { Action = AppActions.ShowBookmarks });
        Editors.Opened += (_, document) =>
        {
            document.Bookmarks = bookmarks.LinesIn(document.Path);
            document.Text.Changed += (_, change) => bookmarks.Follow(document.Path, change);
        };
        bookmarks.Changed += (_, path) =>
        {
            foreach (OpenDocument document in Editors.Documents.Where(d => d.Path == path))
            {
                document.Bookmarks = bookmarks.LinesIn(path);
            }
        };
    }

    private void RunBookmarkAction(string action)
    {
        switch (action)
        {
            case AppActions.ToggleBookmark when Editors.Active is { } active:
                bookmarks.Toggle(active.Path, Editors.CodeEditor.CaretPosition.Line);
                break;
            case AppActions.NextBookmark or AppActions.PreviousBookmark:
                if (bookmarks.Next(Editors.Active?.Path, Editors.Active is null ? -1 : Editors.CodeEditor.CaretPosition.Line, action == AppActions.NextBookmark ? 1 : -1) is { } next)
                {
                    NavigateTo(next.Path, new TextPosition(next.Line, 0));
                }
                else
                {
                    StatusText.Text = "No bookmarks";
                }
                break;
            case AppActions.ShowBookmarks:
                ShowToolWindow("bookmarks");
                break;
        }
    }
}
