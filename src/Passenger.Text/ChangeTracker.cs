namespace Passenger.Text;

/// <summary>The changes made to a document since a point: a request sent to a language server, whose answer is in the
/// offsets of the text as it was. The answer's edits are carried onto the text as it is now, or refused when the text
/// one of them would change was itself changed meanwhile.</summary>
public sealed class ChangeTracker : IDisposable
{
    private readonly List<IReadOnlyList<TextEdit>> changes = [];

    public ChangeTracker(TextDocument document)
    {
        Document = document;
        Snapshot = document.Snapshot;
        Version = document.Version;
        document.Changed += OnChanged;
    }

    public TextDocument Document { get; }

    /// <summary>The text as it was when the tracking began.</summary>
    public TextSnapshot Snapshot { get; }

    /// <summary>The document's version then, as the language server knew it.</summary>
    public int Version { get; }

    /// <summary>An offset of the text as it was, where it is now.</summary>
    public int MapOffset(int offset)
    {
        foreach (IReadOnlyList<TextEdit> change in changes)
        {
            offset = TextEditing.MapOffset(offset, change, afterInsertions: false);
        }
        return offset;
    }

    /// <summary>Edits in the offsets of the text as it was, in the offsets of the text now; null when the text one of them
    /// replaces was changed meanwhile, so it would no longer replace what it was made for.</summary>
    public IReadOnlyList<TextEdit>? Map(IReadOnlyList<TextEdit> edits)
    {
        List<TextEdit> mapped = [];
        foreach (TextEdit edit in edits)
        {
            int start = edit.Start;
            int end = edit.Start + edit.Length;
            foreach (IReadOnlyList<TextEdit> change in changes)
            {
                foreach (TextEdit made in change)
                {
                    int madeEnd = made.Start + made.Length;
                    bool overlaps = made.Start < end && madeEnd > start;
                    bool inside = made.Length == 0 && made.Start > start && made.Start < end;
                    if (overlaps || inside)
                    {
                        return null;
                    }
                }
                start = TextEditing.MapOffset(start, change, afterInsertions: false);
                end = TextEditing.MapOffset(end, change, afterInsertions: false);
            }
            mapped.Add(new TextEdit(start, Math.Max(0, end - start), edit.Text));
        }
        return mapped;
    }

    public void Dispose() => Document.Changed -= OnChanged;

    private void OnChanged(object? sender, DocumentChangedEventArgs e) => changes.Add(e.Edits);
}
