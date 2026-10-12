using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Languages.CSharp;

/// <summary>Colours a C# document as it is typed. Roslyn reads the document's own snapshots, told the
/// range each edit changed, so each parse is incremental and runs on a worker thread. Nothing is classified ahead:
/// the editor asks for the lines it draws, and they are classified from the newest tree there and
/// then (Roslyn's trees are immutable and safe to read from any thread). Edits made since that tree
/// was parsed are mapped back to find the tree's text and forward again to place its colours, so a
/// word being typed keeps its colour and typing never flashes plain. A keystroke costs the lines on
/// screen, whatever the size of the file.</summary>
public sealed class CSharpHighlighter : IHighlighter, IDisposable
{
    /// <summary>The symbols a Debug build defines, so <c>#if DEBUG</c> code is coloured as code until
    /// the project system says which configuration is active.</summary>
    private static readonly CSharpParseOptions Options = new(LanguageVersion.Preview, DocumentationMode.Parse, preprocessorSymbols: ["DEBUG", "TRACE", "NET", "NETCOREAPP"]);

    private readonly TextDocument document;
    private readonly SynchronizationContext? ui = SynchronizationContext.Current;
    private readonly List<(int Version, IReadOnlyList<TextEdit> Edits)> sinceTree = [];
    private SnapshotText text;
    private SyntaxTree? tree;
    private int version;
    private bool parsing;
    private bool disposed;
    private (int Version, int Start, int Length, SyntaxSpan[] Spans) cached = (-1, 0, 0, []);
    private (int Start, int Length) lastAsked;

    public CSharpHighlighter(TextDocument document)
    {
        this.document = document;
        text = new SnapshotText(document.Snapshot);
        document.Changed += OnChanged;
        Parse();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<SyntaxSpan> GetSpans(TextSnapshot snapshot, int start, int length)
    {
        if (tree is null)
        {
            return [];
        }
        if (cached.Version == version && cached.Start == start && cached.Length == length)
        {
            return cached.Spans;
        }
        lastAsked = (start, length);
        // The range in the tree's text: the visible range carried back through the edits since.
        int from = start;
        int to = start + length;
        for (int i = sinceTree.Count - 1; i >= 0; i--)
        {
            from = Back(sinceTree[i].Edits, from, toEnd: false);
            to = Back(sinceTree[i].Edits, to, toEnd: true);
        }
        SyntaxNode root = tree.GetRoot();
        // One character to the left, so a word being typed at its end is found and its colour
        // carried over the new letters.
        from = sinceTree.Count > 0 ? from - 1 : from;
        TextSpan range = TextSpan.FromBounds(Math.Clamp(from, 0, root.FullSpan.End), Math.Clamp(to, 0, root.FullSpan.End));
        List<SyntaxSpan> spans = CSharpClassifier.Classify(root, range);
        foreach ((_, IReadOnlyList<TextEdit> batch) in sinceTree)
        {
            spans = Forward(spans, batch);
        }
        SyntaxSpan[] result = [.. spans];
        cached = (version, start, length, result);
        return result;
    }

    public void Dispose()
    {
        disposed = true;
        document.Changed -= OnChanged;
    }

    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        text = text.With(e.After, e.Edits);
        version++;
        sinceTree.Add((version, e.Edits));
        Parse();
    }

    private void Parse()
    {
        if (parsing || disposed)
        {
            return;
        }
        parsing = true;
        SnapshotText parsedText = text;
        SyntaxTree? previous = tree;
        int parsedVersion = version;
        (int Start, int Length) warm = lastAsked;
        BackgroundWork.Begin();
        Task.Run(() =>
        {
            SyntaxTree parsed = previous is null ? CSharpSyntaxTree.ParseText(parsedText, Options) : previous.WithChangedText(parsedText);
            // Classifying the lines on screen here builds the tree's nodes there (Roslyn makes them on
            // first touch, and a list of thousands of members makes arrays of thousands), so the UI
            // thread finds them made.
            SyntaxNode root = parsed.GetRoot();
            int end = root.FullSpan.End;
            CSharpClassifier.Classify(root, TextSpan.FromBounds(Math.Min(warm.Start, end), Math.Min(warm.Start + warm.Length, end)));
            return parsed;
        }).ContinueWith(done => OnUi(() =>
        {
            try
            {
                parsing = false;
                if (done.IsCompletedSuccessfully && !disposed)
                {
                    tree = done.Result;
                    parsedText.ForgetHistory();
                    sinceTree.RemoveAll(batch => batch.Version <= parsedVersion);
                    cached = (-1, 0, 0, []);
                    Changed?.Invoke(this, EventArgs.Empty);
                }
                if (parsedVersion != version)
                {
                    Parse();
                }
            }
            finally
            {
                BackgroundWork.End();
            }
        }), TaskScheduler.Default);
    }

    private void OnUi(Action action)
    {
        if (ui is null)
        {
            action();
        }
        else
        {
            ui.Post(_ => action(), null);
        }
    }

    /// <summary>An offset after <paramref name="batch"/> as it was before it. Inside inserted text it
    /// lands at the insertion's start, or past the replaced text for the end of a range.</summary>
    private static int Back(IReadOnlyList<TextEdit> batch, int offset, bool toEnd)
    {
        int shift = 0;
        foreach (TextEdit edit in batch)
        {
            int start = edit.Start + shift;
            int end = start + edit.Text.Length;
            if (offset < start)
            {
                break;
            }
            if (offset <= end)
            {
                return toEnd ? edit.End : edit.Start;
            }
            shift += edit.Text.Length - edit.Length;
        }
        return offset - shift;
    }

    /// <summary>Carries colours through a batch of edits (sorted, in the old text's coordinates). A
    /// span an edit lands inside grows or shrinks with it; one an edit crosses is dropped until the
    /// next parse brings it back.</summary>
    private static List<SyntaxSpan> Forward(List<SyntaxSpan> spans, IReadOnlyList<TextEdit> batch)
    {
        List<SyntaxSpan> shifted = new(spans.Count);
        int e = 0;
        int delta = 0;
        foreach (SyntaxSpan span in spans)
        {
            while (e < batch.Count && batch[e].End <= span.Start)
            {
                delta += batch[e].Text.Length - batch[e].Length;
                e++;
            }
            if (e >= batch.Count || batch[e].Start > span.End || (batch[e].Start == span.End && batch[e].Length > 0))
            {
                shifted.Add(span with { Start = span.Start + delta });
                continue;
            }
            TextEdit edit = batch[e];
            bool alone = e + 1 >= batch.Count || batch[e + 1].Start > span.End;
            if (alone && edit.Length == 0 && edit.Start > span.Start)
            {
                shifted.Add(new SyntaxSpan(span.Start + delta, span.Length + edit.Text.Length, span.Kind));
                delta += edit.Text.Length;
                e++;
            }
            else if (alone && edit.Text.Length == 0 && edit.Start >= span.Start && edit.End <= span.End)
            {
                if (span.Length > edit.Length)
                {
                    shifted.Add(new SyntaxSpan(span.Start + delta, span.Length - edit.Length, span.Kind));
                }
                delta -= edit.Length;
                e++;
            }
        }
        return shifted;
    }
}
