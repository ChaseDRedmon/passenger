using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.ToolWindows;

/// <summary>A program's or a build's output: the code editor, read-only, following the end while the
/// output grows, with Rider's run toolbar down its left side (rerun, stop, clear). What came from standard
/// error is drawn in the error colour, as Rider's console draws it.</summary>
public sealed class OutputView : DockPanel
{
    private readonly Button rerun;
    private readonly Button stop;
    private readonly ErrorOutput errors = new();

    public OutputView()
    {
        Document = new TextDocument();
        Editor = new CodeEditor
        {
            Document = Document,
            IsReadOnly = true,
            LineCommentPrefix = null,
            ShowLineNumbers = false,
            FontSize = 12.5,
            Highlighter = errors,
        };
        rerun = Tool("Icon.Rerun", "Rerun");
        stop = Tool("Icon.Stop", "Stop");
        Button clear = Tool("Icon.Clear", "Clear");
        stop.IsEnabled = false;
        rerun.Click += (_, _) => RerunClicked?.Invoke(this, EventArgs.Empty);
        stop.Click += (_, _) => StopClicked?.Invoke(this, EventArgs.Empty);
        clear.Click += (_, _) => Document.Clear();
        Border toolbar = new()
        {
            [DockProperty] = Dock.Left,
            Width = 36,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new StackPanel { Spacing = 2, Margin = new Thickness(0, 6), HorizontalAlignment = HorizontalAlignment.Center, Children = { rerun, stop, clear } },
        };
        toolbar.Bind(Border.BorderBrushProperty, toolbar.GetResourceObservable("Passenger.Divider"));
        Children.Add(toolbar);
        Children.Add(Editor);
        Document.Changed += (_, change) =>
        {
            // Output only ever grows at the end; anything taken away is a Clear, which takes the colours with it.
            if (change.Edits.Any(e => e.Length > 0))
            {
                errors.Clear();
            }
            Editor.ScrollToEnd();
        };
    }

    public event EventHandler? RerunClicked;
    public event EventHandler? StopClicked;

    public TextDocument Document { get; }
    public CodeEditor Editor { get; }

    /// <summary>Adds a program's output at the end; standard error's in its own colour.</summary>
    public void Append(string text, bool error = false)
    {
        int start = Document.Snapshot.Length;
        Document.Append(text);
        if (error && text.Length > 0)
        {
            errors.Add(start, text.Length);
        }
    }

    public void SetRunning(bool running)
    {
        stop.IsEnabled = running;
        rerun.IsEnabled = !running;
    }

    private static Button Tool(string icon, string tip)
    {
        Button button = new() { Classes = { "tool" }, Content = new PathIcon() };
        ToolTip.SetTip(button, tip);
        ((PathIcon)button.Content).Bind(PathIcon.DataProperty, button.GetResourceObservable(icon));
        return button;
    }

    /// <summary>The stretches of the console that came from standard error, in order.</summary>
    private sealed class ErrorOutput : IHighlighter
    {
        private readonly List<SyntaxSpan> spans = [];

        public event EventHandler? Changed;

        public void Add(int start, int length)
        {
            if (spans.Count > 0 && spans[^1].End == start)
            {
                spans[^1] = spans[^1] with { Length = spans[^1].Length + length };
            }
            else
            {
                spans.Add(new SyntaxSpan(start, length, TokenKind.ErrorOutput));
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            spans.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public IReadOnlyList<SyntaxSpan> GetSpans(TextSnapshot text, int start, int length)
        {
            int end = start + length;
            int first = spans.FindIndex(s => s.End > start);
            if (first < 0)
            {
                return [];
            }
            List<SyntaxSpan> found = [];
            for (int i = first; i < spans.Count && spans[i].Start < end; i++)
            {
                found.Add(spans[i]);
            }
            return found;
        }
    }
}
