using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.ToolWindows;

/// <summary>A program's or a build's output: the code editor, read-only, following the end while the
/// output grows, with Rider's run toolbar down its left side (rerun, stop, clear).</summary>
public sealed class OutputView : DockPanel
{
    private readonly Button rerun;
    private readonly Button stop;

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
        Document.Changed += (_, _) => Editor.ScrollToEnd();
    }

    public event EventHandler? RerunClicked;
    public event EventHandler? StopClicked;

    public TextDocument Document { get; }
    public CodeEditor Editor { get; }

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
}
