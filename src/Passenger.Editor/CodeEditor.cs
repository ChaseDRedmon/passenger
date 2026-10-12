using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Passenger.Text;

namespace Passenger.Editor;

/// <summary>The code editor: draws a <see cref="TextDocument"/> with a line-number gutter, carets and
/// selections, and turns keys, typing and the mouse into edits. Lines are formatted only when they
/// are on screen and kept while their text and colours stay the same, so a keystroke costs the one
/// line it changed.</summary>
public sealed partial class CodeEditor : Control
{
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<CodeEditor, double>(nameof(FontSize), 13);

    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<CodeEditor, double>(nameof(LineSpacing), 1.2);

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<CodeEditor, FontFamily>(nameof(FontFamily), new FontFamily("avares://Passenger.Editor/Fonts#JetBrains Mono"));

    /// <summary>Space between the gutter's edge and the line numbers, and after them.</summary>
    private const double GutterPadLeft = 14;
    private const double GutterPadRight = 22;
    private const double TextPadLeft = 6;

    private readonly ScrollBar vertical = new() { Orientation = Orientation.Vertical, AllowAutoHide = true };
    private readonly ScrollBar horizontal = new() { Orientation = Orientation.Horizontal, AllowAutoHide = true };
    private readonly DispatcherTimer blink;
    private TextDocument document = new();
    private SelectionSet selections = SelectionSet.Single(0);
    private IHighlighter? highlighter;
    private Vector scroll;
    private double widestLine;
    private bool caretOn = true;
    private bool updatingBars;
    private bool editing;
    private int? pendingGoTo;
    private bool ligatures;
    private Metrics metrics;
    private Palette palette;
    private (TextRunProperties Value, TextRunProperties Exception) annotationRuns;
    private TextRunProperties? questionRun;

    public CodeEditor()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        VisualChildren.Add(vertical);
        VisualChildren.Add(horizontal);
        LogicalChildren.Add(vertical);
        LogicalChildren.Add(horizontal);
        vertical.PropertyChanged += OnScrollBarChanged;
        horizontal.PropertyChanged += OnScrollBarChanged;
        blink = new DispatcherTimer(TimeSpan.FromMilliseconds(530), DispatcherPriority.Background, (_, _) =>
        {
            caretOn = !caretOn;
            InvalidateVisual();
        });
        document.Changed += OnDocumentChanged;
        ActualThemeVariantChanged += (_, _) => RefreshStyle();
        AddHandler(TextInputMethodClientRequestedEvent, (_, e) => e.Client = InputMethod);
        metrics = Metrics.Measure(Typeface, FontSize, LineSpacing);
        palette = Palette.Default;
        numberRuns = (
            RunWith(palette.LineNumberCurrent),
            RunWith(palette.LineNumber));
        annotationRuns = (ItalicRunWith(palette.InlineValue), ItalicRunWith(palette.Error));
    }

    /// <summary>Raised when the view scrolls, so a diff's other side can follow.</summary>
    public event EventHandler? ScrollChanged;

    /// <summary>Raised when any caret or selection changes, by an edit or a move.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Cmd+click (Ctrl+click on Windows) at an offset: the host's go-to-definition.</summary>
    public event EventHandler<int>? NavigateRequested;

    /// <summary>The pointer has rested on the text: the offset under it and where it is, for a hover
    /// card. Raised once per rest; moving off clears it (<see cref="HoverEnded"/>).</summary>
    public event EventHandler<HoverEventArgs>? Hovered;

    public event EventHandler? HoverEnded;

    /// <summary>Text arrived from the keyboard and was typed into the document, so completion and
    /// signature help can decide whether to open.</summary>
    public event EventHandler<string>? Typed;

    /// <summary>The markers drawn over the text (sorted), from the language service.</summary>
    public IReadOnlyList<Marker> Markers { get; private set; } = [];

    public void SetMarkers(IReadOnlyList<Marker> markers)
    {
        Markers = [.. markers.OrderBy(m => m.Range.Start)];
        InvalidateVisual();
    }

    /// <summary>The marks in the gutter (breakpoints, the execution point), from the host.</summary>
    public IReadOnlyList<LineMark> LineMarks { get; private set; } = [];

    public void SetLineMarks(IReadOnlyList<LineMark> marks)
    {
        LineMarks = marks;
        InvalidateVisual();
    }

    /// <summary>The lines changed since the last commit, as bars against the code, from the host.</summary>
    public IReadOnlyList<ChangeMark> ChangeMarks { get; private set; } = [];

    public void SetChangeMarks(IReadOnlyList<ChangeMark> marks)
    {
        ChangeMarks = marks;
        InvalidateVisual();
    }

    /// <summary>The bookmarked lines, drawn as ribbons at the line numbers' left.</summary>
    public IReadOnlyList<int> Bookmarks { get; private set; } = [];

    public void SetBookmarks(IReadOnlyList<int> lines)
    {
        Bookmarks = lines;
        InvalidateVisual();
    }

    /// <summary>Text per line in a column at the gutter's left (Git blame's author and date), null for a line with none;
    /// empty for no column.</summary>
    public IReadOnlyList<string?> GutterAnnotations { get; private set; } = [];

    private double annotationWidth;

    public void SetGutterAnnotations(IReadOnlyList<string?> perLine)
    {
        GutterAnnotations = perLine;
        int longest = perLine.Count == 0 ? 0 : perLine.Max(l => l?.Length ?? 0);
        annotationWidth = longest == 0 ? 0 : (longest * metrics.CharWidth) + 18;
        InvalidateArrange();
        InvalidateVisual();
    }

    /// <summary>Whether a point (in this control) is on the annotation column.</summary>
    public bool IsInAnnotationColumn(Point at) => annotationWidth > 0 && at.X < annotationWidth;

    /// <summary>Whether the change marks tint whole lines, as a diff's two sides show them, rather than draw bars in
    /// the gutter; a mark with no lines is then a rule between two.</summary>
    public bool ChangeBackgrounds { get; set; }

    /// <summary>Whether a point (in this control) is on the change bars' strip at the gutter's edge.</summary>
    public bool IsInChangeColumn(Point at) => ShowLineNumbers && at.X >= GutterWidth - FoldWidth - 8 && at.X < GutterWidth - FoldWidth;

    /// <summary>The tests' run icons in the gutter, from the host.</summary>
    public IReadOnlyList<TestMark> TestMarks { get; private set; } = [];

    public void SetTestMarks(IReadOnlyList<TestMark> marks)
    {
        TestMarks = marks;
        InvalidateVisual();
    }

    /// <summary>Whether a point (in the editor's coordinates) is in the gutter's column of run icons and the
    /// execution arrow, right of the line numbers.</summary>
    public bool IsInMarkColumn(Point at) => ShowLineNumbers && at.X >= GutterWidth - FoldWidth - GutterPadRight && at.X < GutterWidth - FoldWidth;

    /// <summary>Text after lines' code (the debugger's inline values, an exception), from the host.</summary>
    public IReadOnlyList<LineAnnotation> LineAnnotations { get; private set; } = [];

    public void SetLineAnnotations(IReadOnlyList<LineAnnotation> annotations)
    {
        LineAnnotations = annotations;
        InvalidateVisual();
    }

    /// <summary>A click on a line's number (from zero): the host's breakpoint toggle. With a handler,
    /// a click there no longer selects the line, as in Rider with breakpoints over line numbers.</summary>
    public event EventHandler<GutterClick>? GutterClicked;

    /// <summary>The execution arrow was dragged to another line (from zero), Rider's way to Set Next Statement.
    /// With a handler, a press on the arrow drags it; let go where it was, it is the gutter click it began as.</summary>
    public event EventHandler<int>? ExecutionPointDragged;

    /// <summary>Where a line's number is drawn, in the editor's coordinates: what a popup about its breakpoint
    /// is placed beside.</summary>
    public Point GutterPoint(int line) => new(GutterWidth, TopOf(line) + metrics.LineHeight - scroll.Y);

    /// <summary>The middle of one of the gutter's columns at a line, in the editor's coordinates: where a click on the
    /// execution arrow or a test's icon, a change bar or a fold's chevron lands.</summary>
    public Point GutterColumnPoint(GutterColumn column, int line)
    {
        double right = GutterWidth - FoldWidth;
        double x = column switch
        {
            GutterColumn.Folding => GutterWidth - (FoldWidth / 2),
            GutterColumn.Changes => right - 4,
            _ => right - (GutterPadRight / 2),
        };
        return new Point(x, TopOf(line) - scroll.Y + (metrics.LineHeight / 2));
    }

    /// <summary>The markers at an offset, for a tooltip.</summary>
    public IEnumerable<Marker> MarkersAt(int offset) =>
        Markers.Where(m => m.Range.Start <= offset && (offset < m.Range.End || (m.Range.Length == 0 && offset == m.Range.Start)));

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public double LineSpacing
    {
        get => GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public IndentOptions Indent { get; set; } = new();

    /// <summary>Whether the font may join characters into ligatures (=> drawn as one arrow). Off, as
    /// in Rider, so every character stays the one typed.</summary>
    public bool Ligatures
    {
        get => ligatures;
        set
        {
            ligatures = value;
            RefreshStyle();
        }
    }

    public WordStop WordStop { get; set; } = OperatingSystem.IsMacOS() ? WordStop.End : WordStop.Start;

    /// <summary>What a line comment starts with in this document's language, or null for none.</summary>
    public string? LineCommentPrefix { get; set; } = "//";

    public bool IsReadOnly { get; set; }

    /// <summary>Whether the gutter shows line numbers; a console's output goes without.</summary>
    public bool ShowLineNumbers { get; set; } = true;

    /// <summary>Scrolls so the last line sits at the bottom of the view, as output follows its end.</summary>
    public void ScrollToEnd()
    {
        double bottom = RowCount * metrics.LineHeight - Viewport.Height + horizontal.DesiredSize.Height;
        ScrollTo(new Vector(0, Math.Max(0, bottom)));
    }

    /// <summary>When set, the editor resolves its own key presses; an IDE leaves it null and sends
    /// actions in through <see cref="TryExecute"/> from one window-wide keymap.</summary>
    public Keymap? Keymap
    {
        get => ownKeys?.Keymap;
        set => ownKeys = value is null ? null : new KeymapDispatcher(value);
    }

    public TextDocument Document
    {
        get => document;
        set
        {
            document.Changed -= OnDocumentChanged;
            document = value;
            document.Changed += OnDocumentChanged;
            selections = SelectionSet.Single(0);
            scroll = default;
            widestLine = 0;
            measuredWidth = 0;
            lines.Clear();
            InvalidateArrange();
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IHighlighter? Highlighter
    {
        get => highlighter;
        set
        {
            if (highlighter is not null)
            {
                highlighter.Changed -= OnHighlighterChanged;
            }
            highlighter = value;
            if (highlighter is not null)
            {
                highlighter.Changed += OnHighlighterChanged;
            }
            InvalidateVisual();
        }
    }

    public SelectionSet Selections
    {
        get => selections;
        set => SetSelections(value, reveal: true);
    }

    /// <summary>The primary caret's line and column, both from zero.</summary>
    public TextPosition CaretPosition => document.Snapshot.GetPosition(selections.Primary.Caret);

    public Vector ScrollOffset => scroll;

    public double LineHeight => metrics.LineHeight;

    private Typeface Typeface => new(FontFamily);

    private double GutterWidth => ShowLineNumbers
        ? annotationWidth + GutterPadLeft + Math.Max(2, Digits(document.Snapshot.LineCount)) * metrics.CharWidth + GutterPadRight + FoldWidth
        : 4;

    private double TextLeft => GutterWidth + TextPadLeft;

    private Size Viewport => new(Math.Max(0, Bounds.Width - GutterWidth), Bounds.Height);

    /// <summary>Puts the primary caret at a line and column (from zero) and scrolls it into the middle
    /// of the view, as opening a file at a position does.</summary>
    public void GoTo(TextPosition position)
    {
        int offset = document.Snapshot.GetOffset(position);
        SetSelections(SelectionSet.Single(offset), reveal: false);
        if (Bounds.Height <= 0)
        {
            // Not laid out yet, so there is no view to centre in; the first arrange does it.
            pendingGoTo = offset;
            return;
        }
        double y = TopOf(document.Snapshot.GetLineFromOffset(offset));
        ScrollTo(new Vector(scroll.X, y - Viewport.Height / 3));
        Reveal(offset);
    }

    /// <summary>Shows a document with the carets and scroll it was left at, as switching back to a
    /// tab does.</summary>
    public void Show(TextDocument shown, SelectionSet carets, Vector offset)
    {
        Document = shown;
        selections = carets.Clamp(shown.Snapshot.Length);
        scroll = offset;
        UpdateScrollBars();
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ranges a search found (sorted), drawn behind the text, the current one stronger. The
    /// search owns them and sets them again when the text changes.</summary>
    public IReadOnlyList<TextRange> Highlights { get; private set; } = [];

    public int CurrentHighlight { get; private set; } = -1;

    public void SetHighlights(IReadOnlyList<TextRange> ranges, int current)
    {
        Highlights = ranges;
        CurrentHighlight = current;
        InvalidateVisual();
    }

    /// <summary>Selects a range and scrolls it into view, as finding it does.</summary>
    public void Select(TextRange range)
    {
        SetSelections(SelectionSet.Single(new Selection(range.Start, range.End)), reveal: true);
    }

    /// <summary>Where an offset's caret would stand, as the top of its line, in this control's
    /// coordinates: what a popup anchors to, and where a click lands on that offset.</summary>
    public Point PointOf(int offset)
    {
        TextSnapshot text = document.Snapshot;
        int line = text.GetLineFromOffset(offset);
        return new Point(TextLeft - scroll.X + XOf(text, offset), TopOf(line) - scroll.Y);
    }

    public void ScrollTo(Vector offset)
    {
        double maxY = Math.Max(0, (RowCount - 1) * metrics.LineHeight);
        double maxX = Math.Max(0, widestLine + TextPadLeft + metrics.CharWidth * 4 - Viewport.Width);
        Vector clamped = new(Math.Clamp(offset.X, 0, maxX), Math.Clamp(offset.Y, 0, maxY));
        if (clamped == scroll)
        {
            return;
        }
        scroll = clamped;
        UpdateScrollBars();
        InvalidateVisual();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        vertical.Measure(availableSize);
        horizontal.Measure(availableSize);
        double width = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double barWidth = vertical.DesiredSize.Width;
        double barHeight = horizontal.DesiredSize.Height;
        vertical.Arrange(new Rect(finalSize.Width - barWidth, 0, barWidth, Math.Max(0, finalSize.Height - barHeight)));
        horizontal.Arrange(new Rect(GutterWidth, finalSize.Height - barHeight, Math.Max(0, finalSize.Width - GutterWidth - barWidth), barHeight));
        UpdateScrollBars();
        if (pendingGoTo is { } offset)
        {
            pendingGoTo = null;
            Dispatcher.UIThread.Post(() => GoTo(document.Snapshot.GetPosition(Math.Min(offset, document.Snapshot.Length))));
        }
        return finalSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FontSizeProperty || change.Property == LineSpacingProperty || change.Property == FontFamilyProperty)
        {
            metrics = Metrics.Measure(Typeface, FontSize, LineSpacing);
            RefreshStyle();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshStyle();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        RestartBlink();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        blink.Stop();
        ownKeys?.Reset();
        document.BreakUndoGroup();
        InvalidateVisual();
    }

    private void SetSelections(SelectionSet value, bool reveal)
    {
        SelectionSet clamped = value.Clamp(document.Snapshot.Length);
        UnfoldAroundCarets(clamped);
        bool changed = !clamped.SameAs(selections) || clamped.PrimaryIndex != selections.PrimaryIndex;
        selections = clamped;
        if (reveal)
        {
            Reveal(selections.Primary.Caret);
        }
        RestartBlink();
        InvalidateVisual();
        if (changed)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Scrolls just enough to show <paramref name="offset"/>, keeping a few columns of room
    /// beside it.</summary>
    private void Reveal(int offset)
    {
        if (Bounds.Height <= 0)
        {
            return;
        }
        TextSnapshot text = document.Snapshot;
        int line = text.GetLineFromOffset(offset);
        double top = TopOf(line);
        double y = scroll.Y;
        Size viewport = Viewport;
        double usable = viewport.Height - horizontal.DesiredSize.Height;
        if (top < y)
        {
            y = top;
        }
        else if (top + metrics.LineHeight > y + usable)
        {
            y = top + metrics.LineHeight - usable;
        }
        double x = scroll.X;
        double caretX = XOf(text, offset);
        double room = metrics.CharWidth * 4;
        if (caretX - room < x)
        {
            x = Math.Max(0, caretX - room);
        }
        else if (caretX + room > x + viewport.Width - TextPadLeft - vertical.DesiredSize.Width)
        {
            x = caretX + room - (viewport.Width - TextPadLeft - vertical.DesiredSize.Width);
        }
        widestLine = Math.Max(widestLine, caretX);
        ScrollTo(new Vector(x, y));
    }

    private void RestartBlink()
    {
        caretOn = true;
        if (IsFocused)
        {
            blink.Stop();
            blink.Start();
        }
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (!editing)
        {
            selections = TextEditing.MapThrough(selections, e.Edits).Clamp(e.After.Length);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        InvalidateArrange();
        InvalidateVisual();
    }

    private void OnHighlighterChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(InvalidateVisual);

    private void OnScrollBarChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (updatingBars || e.Property != RangeBase.ValueProperty)
        {
            return;
        }
        ScrollTo(sender == vertical ? new Vector(scroll.X, vertical.Value) : new Vector(horizontal.Value, scroll.Y));
    }

    private void UpdateScrollBars()
    {
        updatingBars = true;
        Size viewport = Viewport;
        double maxY = Math.Max(0, (RowCount - 1) * metrics.LineHeight);
        vertical.Maximum = maxY;
        vertical.ViewportSize = viewport.Height;
        vertical.LargeChange = viewport.Height;
        vertical.SmallChange = metrics.LineHeight;
        vertical.Value = scroll.Y;
        vertical.IsVisible = RowCount * metrics.LineHeight > viewport.Height;
        double maxX = Math.Max(0, widestLine + TextPadLeft + metrics.CharWidth * 4 - viewport.Width);
        horizontal.Maximum = maxX;
        horizontal.ViewportSize = viewport.Width;
        horizontal.LargeChange = viewport.Width;
        horizontal.SmallChange = metrics.CharWidth * 4;
        horizontal.Value = scroll.X;
        horizontal.IsVisible = maxX > 0;
        updatingBars = false;
    }

    private void RefreshStyle()
    {
        palette = Palette.From(this);
        questionRun = null;
        hintRun = null;
        runProperties.Clear();
        numberRuns = (
            RunWith(palette.LineNumberCurrent),
            RunWith(palette.LineNumber));
        annotationRuns = (ItalicRunWith(palette.InlineValue), ItalicRunWith(palette.Error));
        lines.Clear();
        InvalidateArrange();
        InvalidateVisual();
    }

    private static int Digits(int value) => value < 10 ? 1 : (int)Math.Floor(Math.Log10(value)) + 1;

    /// <summary>The font's measures at a size: the height of a line and the width of a character,
    /// both whole pixels so lines and columns land on the pixel grid.</summary>
    private readonly record struct Metrics(double LineHeight, double CharWidth, double FontSize)
    {
        public static Metrics Measure(Typeface typeface, double fontSize, double spacing)
        {
            GenericTextRunProperties props = new(typeface, fontSize);
            using TextLine probe = TextFormatter.Current.FormatLine(
                new SingleRun("MMMMMMMMMM", props), 0, double.PositiveInfinity, new LineProperties(props, 4))!;
            return new Metrics(Math.Ceiling(probe.Height * spacing), probe.WidthIncludingTrailingWhitespace / 10, fontSize);
        }
    }
}
