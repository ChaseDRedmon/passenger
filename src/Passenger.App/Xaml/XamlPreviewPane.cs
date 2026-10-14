using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Passenger.App.Xaml;

/// <summary>The design view beside a XAML file, as Rider's Avalonia previewer shows it: a header naming it and what it is
/// doing, the host's error over the last frame when the XAML did not load, and the frame at its own size on the
/// editor's background, shrunk to fit when it is larger than the pane.</summary>
public sealed class XamlPreviewPane : DockPanel
{
    private readonly XamlPreview preview;
    private readonly TextBlock status;
    private readonly TextBlock error;
    private readonly Border errorBar;
    private readonly Image image;
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
        // An edge, so a frame of the editor's own colour still shows where the design ends.
        edge = new Border { Child = image, BorderThickness = new Thickness(1), Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
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

    /// <summary>The window moved to a screen of another scale: the host draws at that one.</summary>
    private void Rescaled(object? sender, EventArgs e) => preview.Scaling = ((TopLevel)sender!).RenderScaling;

    /// <summary>The frame shown, for tests to find on the window.</summary>
    public Image Frame => image;

    private void Show()
    {
        status.Text = preview.Status;
        image.Source = preview.Frame;
        image.Width = preview.FrameSize.Width;
        image.Height = preview.FrameSize.Height;
        edge.IsVisible = preview.Frame is not null;
        waiting.Text = preview.Status;
        waiting.IsVisible = preview.Frame is null;
        error.Text = preview.Error;
        errorBar.IsVisible = preview.Error is not null;
    }
}
