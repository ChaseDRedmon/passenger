using Avalonia.Threading;
using Passenger.App.Navigation;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Languages.CSharp;
using Passenger.Text;
using A = Passenger.App.AppActions;

namespace Passenger.App;

/// <summary>Rider's places, from the navigation history: Last and Next Edit Location step through where the files were
/// edited, Recent Locations lists the places left and edited with their lines of code, and Recently Changed Files (Recent
/// Files with Show edited only) the files edited.</summary>
public partial class MainWindow
{
    /// <summary>How many places Recent Locations lists, Rider's own (ide.max.recent.locations).</summary>
    private const int RecentLocationsShown = 25;

    /// <summary>The lines Recent Locations shows either side of a place, Rider's (recent.locations.lines.before.and.after).</summary>
    private const int LinesAround = 2;

    private OpenDocument? shownLast;
    private bool editNoted;

    /// <summary>Whether Recent Locations last showed only the places edited, which it opens on again, as Rider's does.</summary>
    private bool recentEditedOnly;

    private void AddPlaces()
    {
        // A file left for another is a place visited, where its caret was left.
        Editors.ActiveChanged += (_, _) =>
        {
            if (shownLast is { } left && left != Editors.Active)
            {
                TextSnapshot text = left.Text.Snapshot;
                int caret = Math.Min(left.Selections.Primary.Caret, text.Length);
                session.Navigation.Visited(new HistoryPlace(left.Path, caret, text.GetLineFromOffset(caret)));
            }
            shownLast = Editors.Active;
        };
        Editors.Opened += (_, document) => document.Text.Changed += (_, _) => NoteEdit(document);
        Add(A.LastEditLocation, "Last Edit Location", "Navigate", _ => StepEdits(-1), _ => session.Navigation.CanStepEdits(-1) ? null : NoEditBefore, ("Navigate", 3, 4));
        Add(A.NextEditLocation, "Next Edit Location", "Navigate", _ => StepEdits(1), _ => session.Navigation.CanStepEdits(1) ? null : NoEditAfter, ("Navigate", 3, 5));
        Add(A.RecentChangedFiles, "Recently Changed Files", "IDE", _ => ShowRecentFiles(editedOnly: true), menu: ("View", 2, 1));
        Add(A.RecentLocations, "Recent Locations", "IDE", _ => ShowRecentLocations(), menu: ("View", 2, 2));
    }

    private const string NoEditBefore = "No earlier edit location";
    private const string NoEditAfter = "No later edit location";

    /// <summary>An edit to the file shown keeps the caret's place once the edit has put it there, as IntelliJ keeps the
    /// place a command changed. A reload from disk, or what a save does to the text, leaves the file saved and keeps none.</summary>
    private void NoteEdit(OpenDocument document)
    {
        if (editNoted || Editors.Active != document)
        {
            return;
        }
        editNoted = true;
        Dispatcher.UIThread.Post(() =>
        {
            editNoted = false;
            if (Editors.Active == document && document.IsModified && Here() is { } here)
            {
                session.Navigation.Edited(here);
            }
        });
    }

    /// <summary>Last Edit Location (a step of -1) or Next Edit Location (1): the caret goes to the place edited before or
    /// after the one it went to last, Back coming back from there.</summary>
    private void StepEdits(int step)
    {
        if (session.Navigation.StepEdits(step, Here(), path => Editors.Documents.Any(d => d.Path == path) || File.Exists(path)) is not { } place)
        {
            StatusText.Text = step < 0 ? NoEditBefore : NoEditAfter;
            return;
        }
        GoToPlace(place);
    }

    /// <summary>Goes to a place the history kept, by its offset in the file as it is now, remembered for Back.</summary>
    private void GoToPlace(HistoryPlace place)
    {
        if (TextOf(place.Path) is { } text)
        {
            NavigateTo(place.Path, text.GetPosition(Math.Min(place.Offset, text.Length)));
        }
    }

    /// <summary>Rider's Recent Locations: the places left and edited, each with its lines of code. Its own keys again
    /// while it is open turn Show edited only, as Rider's do.</summary>
    public RecentLocationsPopup ShowRecentLocations()
    {
        if (popups.Open is RecentLocationsPopup open)
        {
            open.EditedOnly = !open.EditedOnly;
            return open;
        }
        Dictionary<string, (TextSnapshot Text, IHighlighter? Colours)> files = [];
        List<IDisposable> made = [];
        HistoryPlace? here = Here();
        RecentLocationsPopup popup = new(editedOnly => [.. session.Navigation.Recent(editedOnly, here).Select(place => Location(place, files, made)).OfType<RecentLocation>().Take(RecentLocationsShown)],
            recentEditedOnly);
        popup.Done += (_, place) =>
        {
            popups.Close();
            if (place is not null)
            {
                GoToPlace(place);
            }
        };
        popup.Forgot += (_, place) => session.Navigation.Forget(place);
        // The colours made for closed files go with the popup.
        popup.DetachedFromVisualTree += (_, _) =>
        {
            recentEditedOnly = popup.EditedOnly;
            foreach (IDisposable colours in made)
            {
                colours.Dispose();
            }
            made.Clear();
        };
        popups.Show(popup);
        return popup;
    }

    /// <summary>A place as Recent Locations shows it: its line and those either side, coloured as the file is (an open
    /// file's own colours, or a parse of a closed one's text, made once for each file), and what it is in; null for a
    /// file that can no longer be read.</summary>
    private RecentLocation? Location(HistoryPlace place, Dictionary<string, (TextSnapshot Text, IHighlighter? Colours)> files, List<IDisposable> made)
    {
        OpenDocument? open = Editors.Documents.FirstOrDefault(d => d.Path == place.Path);
        if (!files.TryGetValue(place.Path, out (TextSnapshot Text, IHighlighter? Colours) file))
        {
            if (open is not null)
            {
                file = (open.Text.Snapshot, open.Highlighter);
            }
            else if (TextOf(place.Path) is { } read)
            {
                TextDocument closed = new(read.ToString());
                IHighlighter? colours = Languages.Highlighters.For(Workspace.Language.ForPath(place.Path).Name, closed);
                if (colours is IDisposable disposable)
                {
                    made.Add(disposable);
                }
                file = (closed.Snapshot, colours);
            }
            else
            {
                return null;
            }
            files[place.Path] = file;
        }
        TextSnapshot text = file.Text;
        int offset = Math.Min(place.Offset, text.Length);
        int line = text.GetLineFromOffset(offset);
        int first = Math.Max(0, line - LinesAround);
        int last = Math.Min(text.LineCount - 1, line + LinesAround);
        int start = text.GetLineStart(first);
        int end = text.GetLine(last).Start + text.GetLine(last).Length;
        string? member = open?.Syntax is { } syntax ? CSharp.Declarations.PathAt(syntax.Current, offset) : null;
        return new RecentLocation(place with { Offset = offset, Line = line }, Path.GetFileName(place.Path), member, first, text.GetText(start, end - start),
            file.Colours is { } whole ? new FragmentColours(whole, text, start) : null);
    }

    /// <summary>A file's colours over a stretch of it, shifted to the stretch's own offsets, as Recent Locations draws a
    /// place's lines apart from the file.</summary>
    private sealed class FragmentColours(IHighlighter whole, TextSnapshot text, int start) : IHighlighter
    {
        public event EventHandler? Changed
        {
            add => whole.Changed += value;
            remove => whole.Changed -= value;
        }

        public IReadOnlyList<SyntaxSpan> GetSpans(TextSnapshot fragment, int from, int length)
        {
            List<SyntaxSpan> spans = [];
            foreach (SyntaxSpan span in whole.GetSpans(text, start + from, Math.Max(0, Math.Min(length, text.Length - start - from))))
            {
                int spanStart = Math.Max(span.Start - start, 0);
                int spanEnd = Math.Min(span.End - start, fragment.Length);
                if (spanEnd > spanStart)
                {
                    spans.Add(new SyntaxSpan(spanStart, spanEnd - spanStart, span.Kind));
                }
            }
            return spans;
        }
    }
}
