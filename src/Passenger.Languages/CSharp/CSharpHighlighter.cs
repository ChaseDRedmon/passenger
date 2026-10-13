using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Languages.CSharp;

/// <summary>Colours a C# document as it is typed, and is the one parse of it that everything else reading its syntax
/// shares (<see cref="Current"/>, <see cref="Parsed"/>). Roslyn reads the document's own snapshots, told the
/// range each edit changed, so each parse is incremental and runs on a worker thread. Nothing is classified ahead:
/// the editor asks for the lines it draws, and they are classified from the newest tree there and
/// then (Roslyn's trees are immutable and safe to read from any thread). Edits made since that tree
/// was parsed are mapped back to find the tree's text and forward again to place its colours, so a
/// word being typed keeps its colour and typing never flashes plain. A keystroke costs the lines on
/// screen, whatever the size of the file.</summary>
public sealed class CSharpHighlighter : IHighlighter, IBraceMatcher, IDisposable
{
    /// <summary>The symbols a Debug build defines, so <c>#if DEBUG</c> code is coloured as code until
    /// the project system says which configuration is active.</summary>
    private static readonly CSharpParseOptions Options = new(LanguageVersion.Preview, DocumentationMode.Parse, preprocessorSymbols: ["DEBUG", "TRACE", "NET", "NETCOREAPP"]);

    private readonly TextDocument document;
    private readonly SynchronizationContext? ui = SynchronizationContext.Current;
    private readonly List<(int Version, IReadOnlyList<TextEdit> Edits)> sinceTree = [];
    private SnapshotText text;
    private SyntaxTree? tree;
    private TextSnapshot? treeText;

    /// <summary>What folds in <see cref="tree"/>, which the next parse's folds are found from.</summary>
    private FoldRegions? treeFolds;
    private long parseBytes;
    private int parses;
    private int version;
    private bool parsing;
    private bool disposed;

    /// <summary>The parse under way, which a caller asking for the syntax before the first has landed waits for.</summary>
    private Task<(SyntaxTree Tree, FoldRegions Folds)>? running;

    /// <summary>The syntax given for <see cref="currentVersion"/> of the text.</summary>
    private ParsedText? current;
    private int currentVersion = -1;

    /// <summary>The spans given for each range asked of the newest text: a frame asks for each run of lines between
    /// folds, and a caret's line on its own.</summary>
    private readonly Dictionary<(int Start, int Length), SyntaxSpan[]> cached = [];
    private int cachedVersion = -1;

    /// <summary>The ranges asked of the newest text, which the next parse classifies ahead.</summary>
    private readonly List<(int Start, int Length)> asked = [];

    public CSharpHighlighter(TextDocument document)
    {
        this.document = document;
        text = new SnapshotText(document.Snapshot);
        document.Changed += OnChanged;
        Parse();
    }

    public event EventHandler? Changed;

    /// <summary>What folds in the text (<see cref="FoldRegions"/>), from the newest parse of the text as it stands.</summary>
    public IReadOnlyList<FoldRegion> Folds { get; private set; } = [];

    /// <summary>A parse of the text as it stands found its folds.</summary>
    public event EventHandler? FoldsChanged;

    /// <summary>What the parses of this file allocated on their worker, its folds and the lines on screen classified ahead
    /// included, and how many there were: what typing costs off the UI thread, for a test to count rather than time.</summary>
    public (long Bytes, int Parses) Worked => (Interlocked.Read(ref parseBytes), Volatile.Read(ref parses));

    /// <summary>A parse of the text as it stands landed: its syntax, for work on it off the UI thread. Raised on the UI
    /// thread, once typing has paused long enough for a parse to catch up with it.</summary>
    public event EventHandler<ParsedText>? Parsed;

    /// <summary>The syntax of the text as it stands, for whatever else reads it (live templates' scopes, postfix
    /// templates, Extend Selection, the tests a file declares, inline values): the newest parse, carried through the
    /// edits made since by Roslyn's incremental parse, which reads again only around them, so nothing parses the whole
    /// file on the UI thread. Made once for each version of the text, on the UI thread, where the text changes.</summary>
    public ParsedText Current
    {
        get
        {
            if (current is null || currentVersion != version)
            {
                current = tree is not null && sinceTree.Count == 0 ? new ParsedText(tree, treeText!) : new ParsedText(Carried(), text.Snapshot);
                currentVersion = version;
            }
            return current;
        }
    }

    /// <summary>The newest parse when it is of the text as it stands, else null until one is (<see cref="Parsed"/>).</summary>
    public ParsedText? Newest => tree is not null && sinceTree.Count == 0 ? Current : null;

    public IReadOnlyList<SyntaxSpan> GetSpans(TextSnapshot snapshot, int start, int length)
    {
        if (tree is null)
        {
            return [];
        }
        if (cachedVersion != version)
        {
            cached.Clear();
            asked.Clear();
            cachedVersion = version;
        }
        if (cached.TryGetValue((start, length), out SyntaxSpan[]? known))
        {
            return known;
        }
        if (asked.Count < 64)
        {
            asked.Add((start, length));
        }
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
            Forward(spans, batch);
        }
        SyntaxSpan[] result = [.. spans];
        if (cached.Count >= 256)
        {
            cached.Clear();
        }
        cached[(start, length)] = result;
        return result;
    }

    /// <summary>A bracket and its pair (<see cref="BracePairs"/>), read from <see cref="Current"/> once the first parse has
    /// landed, so a frame never waits for one.</summary>
    public BracePair? PairAt(TextSnapshot snapshot, int offset) =>
        tree is not null && ReferenceEquals(snapshot, document.Snapshot) ? BracePairs.At(Current.Root, offset) : null;

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
        FoldRegions? previousFolds = treeFolds;
        int parsedVersion = version;
        (int Start, int Length)[] warm = [.. asked];
        BackgroundWork.Begin();
        Task<(SyntaxTree Tree, FoldRegions Folds)> parse = Task.Run(() =>
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            SyntaxTree parsed = previous is null ? CSharpSyntaxTree.ParseText(parsedText, Options) : previous.WithChangedText(parsedText);
            // Classifying the lines on screen here builds the tree's nodes there (Roslyn makes them on
            // first touch, and a list of thousands of members makes arrays of thousands), so the UI
            // thread finds them made.
            SyntaxNode root = parsed.GetRoot();
            int end = root.FullSpan.End;
            foreach ((int start, int length) in warm)
            {
                CSharpClassifier.Classify(root, TextSpan.FromBounds(Math.Min(start, end), Math.Min(start + length, end)));
            }
            FoldRegions folds = FoldRegions.Of(new ParsedText(parsed, parsedText.Snapshot), previous is null ? null : previousFolds);
            Interlocked.Add(ref parseBytes, GC.GetAllocatedBytesForCurrentThread() - allocated);
            Interlocked.Increment(ref parses);
            return (Tree: parsed, Folds: folds);
        });
        running = parse;
        parse.ContinueWith(done => OnUi(() =>
        {
            try
            {
                parsing = false;
                if (done.IsCompletedSuccessfully && !disposed)
                {
                    tree = done.Result.Tree;
                    treeText = parsedText.Snapshot;
                    treeFolds = done.Result.Folds;
                    parsedText.ForgetHistory();
                    sinceTree.RemoveAll(batch => batch.Version <= parsedVersion);
                    cached.Clear();
                    Changed?.Invoke(this, EventArgs.Empty);
                    if (parsedVersion == version)
                    {
                        Folds = done.Result.Folds.All;
                        FoldsChanged?.Invoke(this, EventArgs.Empty);
                        Parsed?.Invoke(this, Current);
                    }
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

    /// <summary>The newest tree carried to the text as it stands. Asked before the first parse has landed, that parse is
    /// waited for rather than made twice.</summary>
    private SyntaxTree Carried()
    {
        SyntaxTree? basis = tree;
        if (basis is null && running is { } first)
        {
            ((IAsyncResult)first).AsyncWaitHandle.WaitOne();
            basis = first.IsCompletedSuccessfully ? first.Result.Tree : null;
        }
        return basis is null ? CSharpSyntaxTree.ParseText(text, Options) : basis.WithChangedText(text);
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
        for (int i = 0; i < batch.Count; i++)
        {
            TextEdit edit = batch[i];
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

    /// <summary>Carries colours through a batch of edits (sorted, in the old text's coordinates), in place. A
    /// span an edit lands inside grows or shrinks with it; one an edit crosses is dropped until the
    /// next parse brings it back.</summary>
    private static void Forward(List<SyntaxSpan> spans, IReadOnlyList<TextEdit> batch)
    {
        int kept = 0;
        int e = 0;
        int delta = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            SyntaxSpan span = spans[i];
            while (e < batch.Count && batch[e].End <= span.Start)
            {
                delta += batch[e].Text.Length - batch[e].Length;
                e++;
            }
            if (e >= batch.Count || batch[e].Start > span.End || (batch[e].Start == span.End && batch[e].Length > 0))
            {
                spans[kept++] = span with { Start = span.Start + delta };
                continue;
            }
            TextEdit edit = batch[e];
            bool alone = e + 1 >= batch.Count || batch[e + 1].Start > span.End;
            if (alone && edit.Length == 0 && edit.Start > span.Start)
            {
                spans[kept++] = new SyntaxSpan(span.Start + delta, span.Length + edit.Text.Length, span.Kind);
                delta += edit.Text.Length;
                e++;
            }
            else if (alone && edit.Text.Length == 0 && edit.Start >= span.Start && edit.End <= span.End)
            {
                if (span.Length > edit.Length)
                {
                    spans[kept++] = new SyntaxSpan(span.Start + delta, span.Length - edit.Length, span.Kind);
                }
                delta -= edit.Length;
                e++;
            }
        }
        spans.RemoveRange(kept, spans.Count - kept);
    }
}
