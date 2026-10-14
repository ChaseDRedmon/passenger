using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Passenger.App.Xaml;

/// <summary>The design view beside a XAML file, as Rider's Avalonia previewer shows it: a header naming it and what it is
/// doing, the previewer's error over the last frame when the XAML did not load, and the frame at its own size on the
/// editor's background, shrunk to fit when it is larger than the pane. Where the previewer says which element is where
/// (WPF's), a click on the frame chooses the element under it and the element at the editor's caret is outlined.</summary>
public sealed class XamlPreviewPane : DockPanel
{
    private readonly XamlPreview preview;
    private readonly TextBlock status;
    private readonly TextBlock error;
    private readonly Border errorBar;
    private readonly Image image;
    private readonly Canvas marks;
    private readonly Border outline;
    private readonly Border edge;
    private readonly TextBlock waiting;
    private TopLevel? shownOn;

    public XamlPreviewPane(XamlPreview preview)
    {
        this.preview = preview;
        status = new TextBlock { Classes = { "muted" }, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Border header = new()
        {
            [DockProperty] = Dock.Top,
            Height = 28,
            Padding = new Thickness(10, 0),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel
            {
                Children =
                {
                    new TextBlock { Text = "Preview", FontWeight = FontWeight.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) },
                    status,
                },
            },
        };
        header.Bind(Border.BackgroundProperty, header.GetResourceObservable("Passenger.Chrome"));
        header.Bind(Border.BorderBrushProperty, header.GetResourceObservable("Passenger.Divider"));
        error = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable("Passenger.Editor.Error"));
        errorBar = new Border { [DockProperty] = Dock.Top, Padding = new Thickness(10, 6), IsVisible = false, Child = error, BorderThickness = new Thickness(0, 0, 0, 1) };
        errorBar.Bind(Border.BackgroundProperty, errorBar.GetResourceObservable("Passenger.Banner"));
        errorBar.Bind(Border.BorderBrushProperty, errorBar.GetResourceObservable("Passenger.Divider"));
        image = new Image { Stretch = Stretch.Fill };
        // The element at the caret, drawn over the frame in the frame's own units, so it shrinks with it.
        outline = new Border { BorderThickness = new Thickness(2), IsVisible = false, IsHitTestVisible = false };
        outline.Bind(Border.BorderBrushProperty, outline.GetResourceObservable("Passenger.Accent"));
        marks = new Canvas { IsHitTestVisible = false, Children = { outline } };
        Panel framed = new() { Children = { image, marks } };
        framed.PointerPressed += OnFramePressed;
        // An edge, so a frame of the editor's own colour still shows where the design ends.
        edge = new Border { Child = framed, BorderThickness = new Thickness(1), Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        edge.Bind(Border.BorderBrushProperty, edge.GetResourceObservable("Passenger.Divider"));
        waiting = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 320, TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // A frame larger than the pane is shrunk to fit it, never one smaller enlarged.
        Panel view = new()
        {
            Children =
            {
                new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = edge },
                waiting,
            },
        };
        Children.Add(header);
        Children.Add(errorBar);
        Children.Add(view);
        this.Bind(BackgroundProperty, this.GetResourceObservable("Passenger.Editor.Background"));
        preview.Changed += (_, _) => Show();
        AttachedToVisualTree += (_, _) =>
        {
            shownOn = TopLevel.GetTopLevel(this);
            if (shownOn is not null)
            {
                preview.Scaling = shownOn.RenderScaling;
                shownOn.ScalingChanged += Rescaled;
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            shownOn?.ScalingChanged -= Rescaled;
            shownOn = null;
        };
        Show();
    }

    /// <summary>An element of the file was clicked in the frame; raised on the UI thread.</summary>
    public event EventHandler<PreviewElement>? ElementChosen;

    /// <summary>The frame shown, for tests to find on the window.</summary>
    public Image Frame => image;

    /// <summary>The outline of the element at the caret, for tests to find on the window.</summary>
    public Control Outline => outline;

    /// <summary>The box outlined, in the frame's units; null while none is.</summary>
    public Rect? Outlined => outline.IsVisible ? new Rect(Canvas.GetLeft(outline), Canvas.GetTop(outline), outline.Width, outline.Height) : null;

    /// <summary>The error shown over the frame, for tests; null while there is none.</summary>
    public string? ErrorShown => errorBar.IsVisible ? error.Text : null;

    /// <summary>Outlines an element's box in the frame, or none.</summary>
    public void ShowOutline(PreviewElement? element)
    {
        if (element is null || preview.Frame is null)
        {
            outline.IsVisible = false;
            return;
        }
        Rect box = element.Box;
        Canvas.SetLeft(outline, box.X);
        Canvas.SetTop(outline, box.Y);
        outline.Width = Math.Max(box.Width, 1);
        outline.Height = Math.Max(box.Height, 1);
        outline.IsVisible = true;
    }

    private void OnFramePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(image).Properties.IsLeftButtonPressed || preview.ElementAt(e.GetPosition(image)) is not { } chosen)
        {
            return;
        }
        e.Handled = true;
        ElementChosen?.Invoke(this, chosen);
    }

    /// <summary>The window moved to a screen of another scale: the previewer draws at that one.</summary>
    private void Rescaled(object? sender, EventArgs e) => preview.Scaling = ((TopLevel)sender!).RenderScaling;

    private void Show()
    {
        status.Text = preview.Status;
        image.Source = preview.Frame;
        image.Width = preview.FrameSize.Width;
        image.Height = preview.FrameSize.Height;
        marks.Width = preview.FrameSize.Width;
        marks.Height = preview.FrameSize.Height;
        edge.IsVisible = preview.Frame is not null;
        waiting.Text = preview.Status;
        waiting.IsVisible = preview.Frame is null;
        error.Text = preview.Error;
        errorBar.IsVisible = preview.Error is not null;
        if (preview.Frame is null)
        {
            outline.IsVisible = false;
        }
    }
}
