using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Languages.CSharp;

/// <summary>Colours a C# document as it is typed. Roslyn's text follows the document change by
/// change, so each parse is incremental and runs on a worker thread; until it lands, the colours
/// already known are shifted through the edit (a word being typed keeps its colour), so typing never
/// flashes plain. Only the newest parse is kept, and a parse is never queued behind another.</summary>
public sealed class CSharpHighlighter : IHighlighter, IDisposable
{
    /// <summary>After this many edits Roslyn's text is rebuilt from the document, so the chain of
    /// changes it keeps for incremental parsing never grows without end.</summary>
    private const int RebaseAfter = 2000;

    /// <summary>The symbols a Debug build defines, so <c>#if DEBUG</c> code is coloured as code until
    /// the project system says which configuration is active.</summary>
    private static readonly CSharpParseOptions Options = new(LanguageVersion.Preview, DocumentationMode.Parse, preprocessorSymbols: ["DEBUG", "TRACE", "NET", "NETCOREAPP"]);

    private readonly TextDocument document;
    private readonly SynchronizationContext? ui = SynchronizationContext.Current;
    private SourceText text;
    private SyntaxTree? tree;
    private SyntaxSpan[] spans = [];
    private int version;
    private int edits;
    private bool parsing;
    private bool disposed;

    public CSharpHighlighter(TextDocument document)
    {
        this.document = document;
        text = SourceText.From(document.Snapshot.ToString());
        document.Changed += OnChanged;
        Parse();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<SyntaxSpan> GetSpans(TextSnapshot snapshot, int start, int length)
    {
        SyntaxSpan[] current = spans;
        int low = 0;
        int high = current.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (current[middle].End <= start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        int end = low;
        while (end < current.Length && current[end].Start < start + length)
        {
            end++;
        }
        return new ArraySegment<SyntaxSpan>(current, low, end - low);
    }

    public void Dispose()
    {
        disposed = true;
        document.Changed -= OnChanged;
    }

    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (++edits >= RebaseAfter)
        {
            edits = 0;
            text = SourceText.From(e.After.ToString());
        }
        else
        {
            text = text.WithChanges(e.Edits.Select(edit => new TextChange(new TextSpan(edit.Start, edit.Length), edit.Text)));
        }
        spans = Shift(spans, e.Edits);
        version++;
        Parse();
    }

    private void Parse()
    {
        if (parsing || disposed)
        {
            return;
        }
        parsing = true;
        SourceText parsedText = text;
        SyntaxTree? previous = tree;
        int parsedVersion = version;
        BackgroundWork.Begin();
        Task.Run(() =>
        {
            SyntaxTree parsed = previous is null ? CSharpSyntaxTree.ParseText(parsedText, Options) : previous.WithChangedText(parsedText);
            return (parsed, CSharpClassifier.Classify(parsed.GetRoot(), CancellationToken.None));
        }).ContinueWith(done => OnUi(() =>
        {
            try
            {
                parsing = false;
                if (done.IsCompletedSuccessfully)
                {
                    // Even a stale tree is kept: the next parse grows incrementally from it.
                    tree = done.Result.parsed;
                    if (parsedVersion == version)
                    {
                        spans = done.Result.Item2;
                        Changed?.Invoke(this, EventArgs.Empty);
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

    /// <summary>Carries colours through edits (sorted, in the old text's coordinates). A span the
    /// edit lands inside grows or shrinks with it; one an edit crosses is dropped until the next parse
    /// brings it back.</summary>
    private static SyntaxSpan[] Shift(SyntaxSpan[] spans, IReadOnlyList<TextEdit> edits)
    {
        List<SyntaxSpan> shifted = new(spans.Length);
        int e = 0;
        int delta = 0;
        foreach (SyntaxSpan span in spans)
        {
            while (e < edits.Count && edits[e].End <= span.Start)
            {
                delta += edits[e].Text.Length - edits[e].Length;
                e++;
            }
            if (e >= edits.Count || edits[e].Start > span.End || (edits[e].Start == span.End && edits[e].Length > 0))
            {
                shifted.Add(span with { Start = span.Start + delta });
                continue;
            }
            TextEdit edit = edits[e];
            bool alone = e + 1 >= edits.Count || edits[e + 1].Start > span.End;
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
        return [.. shifted];
    }
}
