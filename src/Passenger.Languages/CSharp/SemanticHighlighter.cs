using Passenger.Editor;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.Languages.CSharp;

/// <summary>The language server's colours over the parser's: what only the compiler knows (that a
/// name is a struct, a field, a local, an extension method) replaces the syntax's guess wherever the
/// server says something. Tokens are asked for once edits settle and whenever the server says the
/// code moved, decoded against the snapshot they were asked for, and carried through the edits made
/// since, so they never lag a keystroke behind the text.</summary>
public sealed class SemanticHighlighter : IHighlighter, IDisposable
{
    /// <summary>How long typing must pause before new tokens are asked for.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    private readonly IHighlighter syntax;
    private readonly LspDocument document;
    private readonly TokenKind?[] kinds;
    private readonly SynchronizationContext? ui = SynchronizationContext.Current;
    private readonly List<IReadOnlyList<Passenger.Text.TextEdit>> since = [];
    private SyntaxSpan[] tokens = [];
    private CancellationTokenSource? pending;
    private int asked;
    private bool disposed;

    public SemanticHighlighter(IHighlighter syntax, LspDocument document, SemanticTokensLegend legend)
    {
        this.syntax = syntax;
        this.document = document;
        kinds = legend.TokenTypes.Select(Kind).ToArray();
        syntax.Changed += OnSyntaxChanged;
        document.Text.Changed += OnTextChanged;
        Refresh();
    }

    public event EventHandler? Changed;

    /// <summary>Asks for fresh tokens now: the server's view moved (projects loaded, a refresh).</summary>
    public void Refresh() => Request(TimeSpan.Zero);

    public IReadOnlyList<SyntaxSpan> GetSpans(TextSnapshot snapshot, int start, int length)
    {
        IReadOnlyList<SyntaxSpan> parsed = syntax.GetSpans(snapshot, start, length);
        List<SyntaxSpan> semantic = Visible(start, start + length);
        if (semantic.Count == 0)
        {
            return parsed;
        }
        List<SyntaxSpan> merged = new(parsed.Count + semantic.Count);
        int s = 0;
        foreach (SyntaxSpan span in parsed)
        {
            while (s < semantic.Count && semantic[s].End <= span.Start)
            {
                s++;
            }
            bool covered = s < semantic.Count && semantic[s].Start < span.End;
            if (!covered)
            {
                merged.Add(span);
            }
        }
        merged.AddRange(semantic);
        merged.Sort((a, b) => a.Start.CompareTo(b.Start));
        return merged;
    }

    /// <summary>The parser's colours this layers over, which stand alone again once the server is gone.</summary>
    public IHighlighter Syntax => syntax;

    public void Dispose()
    {
        disposed = true;
        pending?.Cancel();
        syntax.Changed -= OnSyntaxChanged;
        document.Text.Changed -= OnTextChanged;
    }

    private void OnSyntaxChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnTextChanged(object? sender, DocumentChangedEventArgs e)
    {
        since.Add(e.Edits);
        Request(Settle);
    }

    private void Request(TimeSpan delay)
    {
        if (disposed)
        {
            return;
        }
        pending?.Cancel();
        CancellationTokenSource cancel = new();
        pending = cancel;
        int request = ++asked;
        BackgroundWork.Begin();
        _ = Fetch(delay, request, cancel.Token);
    }

    private async Task Fetch(TimeSpan delay, int request, CancellationToken cancel)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancel);
            }
            TextSnapshot snapshot = document.Text.Snapshot;
            int editsBefore = since.Count;
            (SemanticTokens? result, _) = await document.SemanticTokensAsync(cancel);
            if (result is null || cancel.IsCancellationRequested)
            {
                return;
            }
            SyntaxSpan[] decoded = Decode(result.Data, snapshot);
            OnUi(() =>
            {
                if (request != asked || disposed)
                {
                    return;
                }
                // Edits made while the request was out are carried over the new tokens.
                since.RemoveRange(0, Math.Min(editsBefore, since.Count));
                tokens = decoded;
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (OperationCanceledException)
        {
            // Asked again, or let go.
        }
        catch (LanguageServerFailed)
        {
            // The parser's colours stand until the server answers, or a server started again does.
        }
        finally
        {
            BackgroundWork.End();
        }
    }

    /// <summary>The protocol's relative encoding (each token as line and column deltas, length, type,
    /// modifiers) turned into offsets in the snapshot it was asked for.</summary>
    private SyntaxSpan[] Decode(int[] data, TextSnapshot snapshot)
    {
        ServerLines lines = ServerLines.Of(snapshot);
        List<SyntaxSpan> spans = new(data.Length / 5);
        int line = 0;
        int column = 0;
        for (int i = 0; i + 4 < data.Length; i += 5)
        {
            line += data[i];
            column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            int type = data[i + 3];
            if (line >= lines.LineCount || type >= kinds.Length || kinds[type] is not { } kind)
            {
                continue;
            }
            int start = lines.OffsetOf(line, column);
            spans.Add(new SyntaxSpan(start, data[i + 2], kind));
        }
        return [.. spans];
    }

    /// <summary>The tokens over a range of the current text: those near it in the tokens' own text,
    /// carried through every edit since.</summary>
    private List<SyntaxSpan> Visible(int start, int end)
    {
        if (tokens.Length == 0)
        {
            return [];
        }
        int from = start;
        int to = end;
        for (int i = since.Count - 1; i >= 0; i--)
        {
            from = Back(since[i], from);
            to = Back(since[i], to) + 1;
        }
        int low = 0;
        int high = tokens.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (tokens[middle].End <= from - 1)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        List<SyntaxSpan> spans = [];
        for (int i = low; i < tokens.Length && tokens[i].Start <= to; i++)
        {
            spans.Add(tokens[i]);
        }
        foreach (IReadOnlyList<Passenger.Text.TextEdit> batch in since)
        {
            spans = Forward(spans, batch);
        }
        return spans;
    }

    private static int Back(IReadOnlyList<Passenger.Text.TextEdit> batch, int offset)
    {
        int shift = 0;
        foreach (Passenger.Text.TextEdit edit in batch)
        {
            int start = edit.Start + shift;
            if (offset < start)
            {
                break;
            }
            if (offset <= start + edit.Text.Length)
            {
                return edit.Start;
            }
            shift += edit.Text.Length - edit.Length;
        }
        return offset - shift;
    }

    /// <summary>A token an edit lands inside grows or shrinks with it; one an edit crosses is dropped
    /// until fresh tokens arrive.</summary>
    private static List<SyntaxSpan> Forward(List<SyntaxSpan> spans, IReadOnlyList<Passenger.Text.TextEdit> batch)
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
            Passenger.Text.TextEdit edit = batch[e];
            bool alone = e + 1 >= batch.Count || batch[e + 1].Start > span.End;
            if (alone && edit.Length == 0 && edit.Start > span.Start && edit.Text.All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                shifted.Add(new SyntaxSpan(span.Start + delta, span.Length + edit.Text.Length, span.Kind));
                delta += edit.Text.Length;
                e++;
            }
        }
        return shifted;
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

    /// <summary>Roslyn's token types, standard and its own, as the editor's kinds; operators and
    /// punctuation are left to the syntax.</summary>
    private static TokenKind? Kind(string type) => type switch
    {
        "namespace" => TokenKind.Namespace,
        "class" or "recordClass" or "recordClassName" or "className" or "record" => TokenKind.Class,
        "struct" or "recordStruct" or "recordStructName" or "structName" => TokenKind.Struct,
        "interface" or "interfaceName" => TokenKind.Interface,
        "enum" or "enumName" => TokenKind.Enum,
        "delegate" or "delegateName" => TokenKind.Delegate,
        "typeParameter" or "typeParameterName" => TokenKind.TypeParameter,
        "method" or "methodName" or "extensionMethod" or "extensionMethodName" or "function" => TokenKind.Method,
        "property" or "propertyName" => TokenKind.Property,
        "event" or "eventName" => TokenKind.Event,
        "field" or "fieldName" => TokenKind.Field,
        "constant" or "constantName" => TokenKind.Constant,
        "enumMember" or "enumMemberName" => TokenKind.EnumMember,
        "variable" or "local" or "localName" => TokenKind.Local,
        "parameter" or "parameterName" => TokenKind.Parameter,
        "label" or "labelName" => TokenKind.Label,
        "keyword" => TokenKind.Keyword,
        "controlKeyword" => TokenKind.ControlKeyword,
        "string" or "stringVerbatim" => TokenKind.String,
        "stringEscapeCharacter" => TokenKind.StringEscape,
        "number" => TokenKind.Number,
        "comment" => TokenKind.Comment,
        "excludedCode" => TokenKind.Excluded,
        "preprocessorKeyword" or "preprocessorText" => TokenKind.Preprocessor,
        _ when type.StartsWith("xmlDocComment", StringComparison.Ordinal) => TokenKind.DocComment,
        _ => null,
    };
}
