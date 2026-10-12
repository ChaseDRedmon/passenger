using Passenger.Text;

namespace Passenger.App.Navigation;

/// <summary>A place the caret was: its file, its offset in the text and that offset's line.</summary>
public sealed record HistoryPlace(string Path, int Offset, int Line);

/// <summary>Rider's Back and Forward. A navigation (go to definition, a usage, a bookmark) puts the place it left behind
/// and forgets what was ahead; Back puts the caret's place ahead. The caret moved by a key or a click changes neither,
/// so Forward still goes where Back came from. Each way keeps at most <see cref="Limit"/> places, and the places in an
/// open file move with its text.</summary>
public sealed class NavigationHistory
{
    /// <summary>How many places each way, IntelliJ's own limit (editor.navigation.history.stack.size).</summary>
    public const int Limit = 150;

    /// <summary>Two places in a file fewer lines apart than this are one, as IntelliJ's TextEditorState merges them.</summary>
    private const int NearLines = 4;

    private readonly List<HistoryPlace> behind = [];
    private readonly List<HistoryPlace> ahead = [];

    /// <summary>A navigation left a place: it goes behind, taking the place of the last there when near it.</summary>
    public void Left(HistoryPlace from)
    {
        PutBehind(from);
        ahead.Clear();
    }

    /// <summary>Where Back goes from the caret's place, which goes ahead; null with nothing behind. A place whose file
    /// is gone is passed over.</summary>
    public HistoryPlace? Back(HistoryPlace? here, Func<string, bool> exists)
    {
        if (Take(behind, exists) is not { } target)
        {
            return null;
        }
        if (here is not null)
        {
            Keep(ahead, here);
        }
        return target;
    }

    /// <summary>Where Forward goes from the caret's place, which goes behind; null with nothing ahead. A place near the
    /// caret's is passed over while there is another, as IntelliJ's is.</summary>
    public HistoryPlace? Forward(HistoryPlace? here, Func<string, bool> exists)
    {
        HistoryPlace? target = Take(ahead, exists);
        while (target is not null && here is not null && Near(here, target) && ahead.Count > 0)
        {
            target = Take(ahead, exists) ?? target;
        }
        if (target is null)
        {
            return null;
        }
        if (here is not null && (here.Path != target.Path || here.Line != target.Line))
        {
            PutBehind(here);
        }
        return target;
    }

    /// <summary>Carries a file's places through an edit to it. Text typed at a place goes before it, so a place stays on
    /// the text it was at, as a bookmark stays on its line.</summary>
    public void Follow(string path, DocumentChangedEventArgs change)
    {
        Carry(behind);
        Carry(ahead);

        void Carry(List<HistoryPlace> places)
        {
            for (int i = 0; i < places.Count; i++)
            {
                if (places[i].Path != path)
                {
                    continue;
                }
                int mapped = TextEditing.MapOffset(Math.Min(places[i].Offset, change.Before.Length), change.Edits, afterInsertions: true);
                int offset = Math.Min(mapped, change.After.Length);
                places[i] = places[i] with { Offset = offset, Line = change.After.GetLineFromOffset(offset) };
            }
        }
    }

    /// <summary>Carries the places in a file, or in every file of a folder, to where it moved.</summary>
    public void Move(string from, string to)
    {
        foreach (List<HistoryPlace> places in new[] { behind, ahead })
        {
            for (int i = 0; i < places.Count; i++)
            {
                places[i] = places[i] with { Path = Workspace.PathNames.Moved(places[i].Path, from, to) };
            }
        }
    }

    /// <summary>Forgets every place, as another folder opens: each folder's are its own.</summary>
    public void Clear()
    {
        behind.Clear();
        ahead.Clear();
    }

    private void PutBehind(HistoryPlace place)
    {
        if (behind.Count > 0 && Near(behind[^1], place))
        {
            behind.RemoveAt(behind.Count - 1);
        }
        Keep(behind, place);
    }

    private static bool Near(HistoryPlace one, HistoryPlace other) => one.Path == other.Path && Math.Abs(one.Line - other.Line) < NearLines;

    private static void Keep(List<HistoryPlace> places, HistoryPlace place)
    {
        places.Add(place);
        if (places.Count > Limit)
        {
            places.RemoveAt(0);
        }
    }

    private static HistoryPlace? Take(List<HistoryPlace> places, Func<string, bool> exists)
    {
        while (places.Count > 0)
        {
            HistoryPlace last = places[^1];
            places.RemoveAt(places.Count - 1);
            if (exists(last.Path))
            {
                return last;
            }
        }
        return null;
    }
}
