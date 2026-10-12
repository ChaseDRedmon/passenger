using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Passenger.Text;

namespace Passenger.Editor;

public sealed partial class CodeEditor
{
    private readonly LineCache lines = new();
    private readonly Dictionary<TokenKind, TextRunProperties> runProperties = [];
    private (TextRunProperties Current, TextRunProperties Other) numberRuns;
    private double measuredWidth;

    public override void Render(DrawingContext context)
    {
        Size size = Bounds.Size;
        double gutter = GutterWidth;
        double left = TextLeft - scroll.X;
        double lineHeight = metrics.LineHeight;
        TextSnapshot text = document.Snapshot;

        context.FillRectangle(palette.Background, new Rect(size));

        int first = Math.Clamp((int)(scroll.Y / lineHeight), 0, text.LineCount - 1);
        int last = Math.Clamp((int)((scroll.Y + size.Height) / lineHeight), 0, text.LineCount - 1);
        int rangeStart = text.GetLineStart(first);
        int rangeEnd = text.GetLine(last).EndIncludingBreak;
        IReadOnlyList<SyntaxSpan> spans = highlighter?.GetSpans(text, rangeStart, rangeEnd - rangeStart) ?? [];
        int spanIndex = 0;
        HashSet<int> caretLines = selections.Items.Select(s => text.GetLineFromOffset(s.Caret)).ToHashSet();
        bool focused = IsFocused;

        using (context.PushClip(new Rect(gutter, 0, Math.Max(0, size.Width - gutter), size.Height)))
        {
            for (int line = first; line <= last; line++)
            {
                LineSpan span = text.GetLine(line);
                double y = line * lineHeight - scroll.Y;
                string content = text.GetText(span.Start, span.Length);
                TextLine layout = Layout(content, span, spans, ref spanIndex);
                widestLine = Math.Max(widestLine, layout.WidthIncludingTrailingWhitespace);

                if (caretLines.Contains(line) && selections.Items.All(s => s.IsEmpty))
                {
                    context.FillRectangle(palette.CurrentLine, new Rect(gutter, y, size.Width - gutter, lineHeight));
                }
                DrawSelections(context, span, layout, left, y, size.Width, focused);
                DrawHighlights(context, span, layout, left, y);
                layout.Draw(context, new Point(left, y + (lineHeight - layout.Height) / 2));
                DrawMarkers(context, span, layout, left, y);
            }

            if (focused && caretOn)
            {
                foreach (Selection s in selections.Items)
                {
                    int line = text.GetLineFromOffset(s.Caret);
                    if (line < first || line > last)
                    {
                        continue;
                    }
                    double x = left + XOf(text, s.Caret);
                    double y = line * lineHeight - scroll.Y;
                    context.FillRectangle(palette.Caret, new Rect(Math.Round(x), y, 2, lineHeight));
                }
            }
            DrawPreedit(context, text, left);
        }

        DrawGutter(context, text, first, last, caretLines, gutter, size.Height);
        lines.EndFrame();
        if (widestLine > measuredWidth)
        {
            // A longer line came into view: the horizontal extent grows, outside the render pass.
            measuredWidth = widestLine;
            Dispatcher.UIThread.Post(UpdateScrollBars);
        }
    }

    /// <summary>A line's x position of an offset, measured on its own formatted line.</summary>
    private double XOf(TextSnapshot text, int offset)
    {
        LineSpan span = text.GetLine(text.GetLineFromOffset(offset));
        TextLine layout = PlainLayout(text.GetText(span.Start, span.Length));
        return layout.GetDistanceFromCharacterHit(new CharacterHit(Math.Min(offset, span.End) - span.Start));
    }

    /// <summary>The offset nearest to x on a line.</summary>
    private int OffsetAtX(TextSnapshot text, int line, double x)
    {
        LineSpan span = text.GetLine(Math.Clamp(line, 0, text.LineCount - 1));
        string content = text.GetText(span.Start, span.Length);
        TextLine layout = PlainLayout(content);
        CharacterHit hit = layout.GetCharacterHitFromDistance(x);
        int column = Math.Clamp(hit.FirstCharacterIndex + hit.TrailingLength, 0, content.Length);
        if (column > 0 && column < content.Length && char.IsLowSurrogate(content[column]))
        {
            column--;
        }
        return span.Start + column;
    }

    private void DrawSelections(DrawingContext context, LineSpan span, TextLine layout, double left, double y, double width, bool focused)
    {
        IBrush brush = focused ? palette.Selection : palette.SelectionInactive;
        foreach (Selection s in selections.Items)
        {
            if (s.IsEmpty || s.End < span.Start || s.Start > span.EndIncludingBreak || (s.End == span.Start && span.Length > 0))
            {
                continue;
            }
            int from = Math.Max(s.Start, span.Start) - span.Start;
            int to = Math.Min(s.End, span.End) - span.Start;
            double x1 = left + layout.GetDistanceFromCharacterHit(new CharacterHit(from));
            // A selection that runs on through the line break fills to the edge, as Rider draws it.
            double x2 = s.End > span.End ? width : left + layout.GetDistanceFromCharacterHit(new CharacterHit(to));
            if (x2 > x1)
            {
                context.FillRectangle(brush, new Rect(x1, y, x2 - x1, metrics.LineHeight));
            }
        }
    }

    private void DrawHighlights(DrawingContext context, LineSpan span, TextLine layout, double left, double y)
    {
        IReadOnlyList<TextRange> ranges = Highlights;
        int low = 0;
        int high = ranges.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (ranges[middle].End <= span.Start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        for (int i = low; i < ranges.Count && ranges[i].Start <= span.End; i++)
        {
            int from = Math.Max(ranges[i].Start, span.Start) - span.Start;
            int to = Math.Min(ranges[i].End, span.End) - span.Start;
            double x1 = left + layout.GetDistanceFromCharacterHit(new CharacterHit(from));
            double x2 = left + layout.GetDistanceFromCharacterHit(new CharacterHit(to));
            if (x2 > x1)
            {
                Rect box = new(x1, y + 1, x2 - x1, metrics.LineHeight - 2);
                if (i == CurrentHighlight)
                {
                    // The current match stands out from the rest, outlined in the caret's colour.
                    context.DrawRectangle(palette.SearchCurrent, new Pen(palette.Caret, 1), box, 2, 2);
                }
                else
                {
                    context.FillRectangle(palette.SearchMatch, box, 2);
                }
            }
        }
    }

    /// <summary>Squiggles under errors, warnings and suggestions; unnecessary code is faded by laying
    /// the background over it, half see-through.</summary>
    private void DrawMarkers(DrawingContext context, LineSpan span, TextLine layout, double left, double y)
    {
        foreach (Marker marker in Markers)
        {
            if (marker.Range.Start > span.End)
            {
                break;
            }
            if (marker.Range.End < span.Start)
            {
                continue;
            }
            int from = Math.Max(marker.Range.Start, span.Start) - span.Start;
            int to = Math.Min(marker.Range.End, span.End) - span.Start;
            double x1 = left + layout.GetDistanceFromCharacterHit(new CharacterHit(from));
            double x2 = left + layout.GetDistanceFromCharacterHit(new CharacterHit(to));
            if (x2 - x1 < metrics.CharWidth / 2)
            {
                // A zero-width diagnostic (a missing semicolon) still needs something to see.
                x2 = x1 + metrics.CharWidth;
            }
            if (marker.Kind == MarkerKind.Unnecessary)
            {
                context.FillRectangle(palette.Faded, new Rect(x1, y, x2 - x1, metrics.LineHeight));
                continue;
            }
            IBrush brush = marker.Kind switch
            {
                MarkerKind.Error => palette.Error,
                MarkerKind.Warning => palette.Warning,
                _ => palette.Info,
            };
            DrawWave(context, brush, x1, x2, y + metrics.LineHeight - 3);
        }
    }

    private static void DrawWave(DrawingContext context, IBrush brush, double x1, double x2, double y)
    {
        const double Step = 2.5;
        StreamGeometry wave = new();
        using (StreamGeometryContext path = wave.Open())
        {
            path.BeginFigure(new Point(x1, y), false);
            bool up = true;
            for (double x = x1 + Step; x < x2 + Step; x += Step)
            {
                path.LineTo(new Point(Math.Min(x, x2), up ? y - 2 : y));
                up = !up;
            }
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(brush, 1), wave);
    }

    private void DrawGutter(DrawingContext context, TextSnapshot text, int first, int last, HashSet<int> caretLines, double gutter, double height)
    {
        context.FillRectangle(palette.Gutter, new Rect(0, 0, gutter, height));
        double right = gutter - GutterPadRight;
        for (int line = first; line <= last; line++)
        {
            bool current = caretLines.Contains(line);
            string label = (line + 1).ToString();
            TextLine number = lines.Get(new LineKey(label, current ? -1 : -2), () =>
                Format(label, [], current ? numberRuns.Current : numberRuns.Other));
            double y = line * metrics.LineHeight - scroll.Y + (metrics.LineHeight - number.Height) / 2;
            number.Draw(context, new Point(right - number.WidthIncludingTrailingWhitespace, y));
        }
    }

    /// <summary>A visible line formatted with its colours, from the cache when its text and spans
    /// are unchanged.</summary>
    private TextLine Layout(string content, LineSpan span, IReadOnlyList<SyntaxSpan> spans, ref int spanIndex)
    {
        while (spanIndex < spans.Count && spans[spanIndex].End <= span.Start)
        {
            spanIndex++;
        }
        List<LineRun> runs = [];
        for (int i = spanIndex; i < spans.Count && spans[i].Start < span.End; i++)
        {
            SyntaxSpan s = spans[i];
            int start = Math.Max(s.Start, span.Start) - span.Start;
            int end = Math.Min(s.End, span.End) - span.Start;
            if (end > start && s.Kind != TokenKind.Text)
            {
                runs.Add(new LineRun(start, end - start, s.Kind));
            }
        }
        LineRun[] array = [.. runs];
        return lines.Get(new LineKey(content, Hash(array), array), () => Format(content, array, null));
    }

    private TextLine PlainLayout(string content) => lines.Get(new LineKey(content, 0, []), () => Format(content, [], null));

    private TextLine Format(string content, LineRun[] runs, TextRunProperties? only)
    {
        TextRunProperties plain = only ?? Run(TokenKind.Text);
        ITextSource source = runs.Length == 0 ? new SingleRun(content, plain) : new ColouredRuns(content, runs, plain, Run);
        return TextFormatter.Current.FormatLine(source, 0, double.PositiveInfinity, new LineProperties(plain, metrics.CharWidth * Indent.TabWidth))!;
    }

    /// <summary>The font and colour a token kind is drawn in, made once per theme.</summary>
    private TextRunProperties Run(TokenKind kind)
    {
        if (!runProperties.TryGetValue(kind, out TextRunProperties? props))
        {
            runProperties[kind] = props = RunWith(palette.Syntax(kind));
        }
        return props;
    }

    private static readonly FontFeatureCollection NoLigatures =
        [new FontFeature { Tag = "calt", Value = 0 }, new FontFeature { Tag = "liga", Value = 0 }];

    private GenericTextRunProperties RunWith(IBrush brush) =>
        new(Typeface, FontSize, foregroundBrush: brush, fontFeatures: ligatures ? null : NoLigatures);

    private static int Hash(LineRun[] runs)
    {
        HashCode hash = new();
        foreach (LineRun run in runs)
        {
            hash.Add(run);
        }
        return hash.ToHashCode() | 1;
    }

    /// <summary>A coloured stretch of one line, in its columns.</summary>
    private readonly record struct LineRun(int Start, int Length, TokenKind Kind);

    /// <summary>A formatted line's identity: its text, and its colouring as a hash over the runs that
    /// is checked against the runs themselves, so two colourings can never share a line.</summary>
    private readonly struct LineKey(string text, int style, LineRun[]? runs = null) : IEquatable<LineKey>
    {
        private readonly LineRun[] runs = runs ?? [];

        public string Text { get; } = text;
        public int Style { get; } = style;

        public bool Equals(LineKey other) => Style == other.Style && Text == other.Text && runs.AsSpan().SequenceEqual(other.runs);
        public override bool Equals(object? obj) => obj is LineKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Text, Style);
    }

    /// <summary>Formatted lines drawn in the last frame. A frame keeps the ones it draws again and
    /// disposes the rest, so the cache never outgrows the screen.</summary>
    private sealed class LineCache
    {
        private Dictionary<LineKey, TextLine> previous = [];
        private Dictionary<LineKey, TextLine> current = [];

        public TextLine Get(LineKey key, Func<TextLine> format)
        {
            if (current.TryGetValue(key, out TextLine? line))
            {
                return line;
            }
            if (previous.Remove(key, out line))
            {
                current[key] = line;
                return line;
            }
            line = format();
            current[key] = line;
            return line;
        }

        public void EndFrame()
        {
            foreach (TextLine stale in previous.Values)
            {
                stale.Dispose();
            }
            previous.Clear();
            (previous, current) = (current, previous);
        }

        public void Clear()
        {
            foreach (TextLine line in previous.Values.Concat(current.Values))
            {
                line.Dispose();
            }
            previous.Clear();
            current.Clear();
        }
    }

    private sealed class SingleRun(string text, TextRunProperties props) : ITextSource
    {
        public TextRun GetTextRun(int textSourceIndex) =>
            textSourceIndex >= text.Length ? new TextEndOfParagraph() : new TextCharacters(text.AsMemory(textSourceIndex), props);
    }

    private sealed class ColouredRuns(string text, LineRun[] runs, TextRunProperties plain, Func<TokenKind, TextRunProperties> colour) : ITextSource
    {
        public TextRun GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex >= text.Length)
            {
                return new TextEndOfParagraph();
            }
            foreach (LineRun run in runs)
            {
                if (textSourceIndex < run.Start)
                {
                    return new TextCharacters(text.AsMemory(textSourceIndex, run.Start - textSourceIndex), plain);
                }
                if (textSourceIndex < run.Start + run.Length)
                {
                    return new TextCharacters(text.AsMemory(textSourceIndex, run.Start + run.Length - textSourceIndex), colour(run.Kind));
                }
            }
            return new TextCharacters(text.AsMemory(textSourceIndex), plain);
        }
    }

    /// <summary>One unwrapped line whose tab stops are every <paramref name="tab"/> pixels.</summary>
    private sealed class LineProperties(TextRunProperties props, double tab) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => 0;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => props;
        public override TextWrapping TextWrapping => TextWrapping.NoWrap;
        public override double Indent => 0;
        public override double DefaultIncrementalTab => tab;
    }

    /// <summary>Every brush the editor paints with, from the theme's <c>Passenger.Editor.*</c> and
    /// <c>Passenger.Syntax.*</c> resources, or a dark default for one the theme leaves out.</summary>
    private sealed class Palette
    {
        private readonly Dictionary<TokenKind, IBrush> syntax = [];

        public static Palette Default { get; } = new();

        public IBrush Background { get; private init; } = Brush("#1E1F22");
        public IBrush Gutter { get; private init; } = Brush("#1E1F22");
        public IBrush Text { get; private init; } = Brush("#BCBEC4");
        public IBrush LineNumber { get; private init; } = Brush("#4B5059");
        public IBrush LineNumberCurrent { get; private init; } = Brush("#A1A3AB");
        public IBrush CurrentLine { get; private init; } = Brush("#26282E");
        public IBrush Selection { get; private init; } = Brush("#214283");
        public IBrush SelectionInactive { get; private init; } = Brush("#2C3341");
        public IBrush Caret { get; private init; } = Brush("#CED0D6");
        public IBrush Preedit { get; private init; } = Brush("#CED0D6");
        public IBrush SearchMatch { get; private init; } = Brush("#2F4A36");
        public IBrush Error { get; private init; } = Brush("#F75464");
        public IBrush Warning { get; private init; } = Brush("#E5A336");
        public IBrush Info { get; private init; } = Brush("#8A8F97");
        public IBrush Faded { get; private init; } = new SolidColorBrush(Color.Parse("#1E1F22"), 0.5).ToImmutable();
        public IBrush SearchCurrent { get; private init; } = Brush("#4F9A5F");

        public IBrush Syntax(TokenKind kind) => syntax.TryGetValue(kind, out IBrush? brush) ? brush : Text;

        public static Palette From(Control control)
        {
            Palette defaults = Default;
            Palette palette = new()
            {
                Background = Find(control, "Passenger.Editor.Background") ?? defaults.Background,
                Gutter = Find(control, "Passenger.Editor.Gutter") ?? defaults.Gutter,
                Text = Find(control, "Passenger.Editor.Text") ?? defaults.Text,
                LineNumber = Find(control, "Passenger.Editor.LineNumber") ?? defaults.LineNumber,
                LineNumberCurrent = Find(control, "Passenger.Editor.LineNumberCurrent") ?? defaults.LineNumberCurrent,
                CurrentLine = Find(control, "Passenger.Editor.CurrentLine") ?? defaults.CurrentLine,
                Selection = Find(control, "Passenger.Editor.Selection") ?? defaults.Selection,
                SelectionInactive = Find(control, "Passenger.Editor.SelectionInactive") ?? defaults.SelectionInactive,
                Caret = Find(control, "Passenger.Editor.Caret") ?? defaults.Caret,
                Preedit = Find(control, "Passenger.Editor.Text") ?? defaults.Preedit,
                SearchMatch = Find(control, "Passenger.Editor.SearchMatch") ?? defaults.SearchMatch,
                Error = Find(control, "Passenger.Editor.Error") ?? defaults.Error,
                Warning = Find(control, "Passenger.Editor.Warning") ?? defaults.Warning,
                Info = Find(control, "Passenger.Editor.Info") ?? defaults.Info,
                Faded = Fade(Find(control, "Passenger.Editor.Background")) ?? defaults.Faded,
                SearchCurrent = Find(control, "Passenger.Editor.SearchCurrent") ?? defaults.SearchCurrent,
            };
            foreach (TokenKind kind in Enum.GetValues<TokenKind>())
            {
                if (Find(control, $"Passenger.Syntax.{kind}") is { } brush)
                {
                    palette.syntax[kind] = brush;
                }
            }
            return palette;
        }

        private static IBrush? Fade(IBrush? background) =>
            background is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.5).ToImmutable() : null;

        private static IBrush? Find(Control control, string key) =>
            control.TryFindResource(key, control.ActualThemeVariant, out object? value) ? value as IBrush : null;

        private static IBrush Brush(string colour) => new SolidColorBrush(Color.Parse(colour)).ToImmutable();
    }
}
