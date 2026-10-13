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
        Dock.Add(new Docking.ToolWindow("bookmarks", "Bookmarks", "Icon.Bookmark", view, Docking.ToolAnchor.LeftBottom)
        {
            Action = AppActions.ShowBookmarks,
            Menus = [new MenuPlace("Navigate/Bookmarks", 0, 3)],
        });
        AddBookmarkActions();
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

    /// <summary>Toggle Bookmark on the caret's line, and Next and Previous Bookmark from it through every file.</summary>
    private void AddBookmarkActions()
    {
        actions!.Add(new IdeAction(AppActions.ToggleBookmark, "Toggle Bookmark", "Bookmarks", context => bookmarks.Toggle(context.File!.Path, context.Caret.Line))
        {
            WhyNot = NeedsFile,
            Menus = [new MenuPlace("Navigate/Bookmarks", 0, 0)],
        });
        Step(AppActions.NextBookmark, "Next Bookmark", 1);
        Step(AppActions.PreviousBookmark, "Previous Bookmark", -1);

        void Step(string id, string title, int step) => actions.Add(new IdeAction(id, title, "Bookmarks", context =>
        {
            Bookmark next = bookmarks.Next(context.File?.Path, context.File is null ? -1 : context.Caret.Line, step)!;
            NavigateTo(next.Path, new TextPosition(next.Line, 0));
        })
        {
            WhyNot = _ => bookmarks.All.Count == 0 ? "No bookmarks" : null,
            Menus = [new MenuPlace("Navigate/Bookmarks", 0, step > 0 ? 1 : 2)],
        });
    }
}
