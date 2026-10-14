using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace Passenger.WpfPreview;

/// <summary>What a file's root is drawn on. A window is drawn as Visual Studio's designer draws one, never shown: its
/// title bar and its client area at the size its content gets as it runs (its Width and Height, else d:DesignWidth and
/// d:DesignHeight, less the system's frame), its content, resources, names, data context and text properties moved onto
/// it; a page likewise without the title bar. Any other element is drawn as itself on the window colour, at its own size,
/// else its design size, else the size its content asks.</summary>
internal sealed class Surface
{
    /// <summary>A window or page whose size says nothing, at the size the project's template gives a new window.</summary>
    private static readonly Size Unsized = new(800, 450);

    private Surface(FrameworkElement root, object loaded)
    {
        Root = root;
        Loaded = loaded;
    }

    /// <summary>What is laid out and drawn.</summary>
    public FrameworkElement Root { get; }

    /// <summary>The object the file made.</summary>
    public object Loaded { get; }

    /// <summary>The surface for a file's root; null when it draws nothing (a style, a flow document).</summary>
    public static Surface? For(object root, LooseXaml loose) => root switch
    {
        Window window => new Surface(OfWindow(window, loose), window),
        Page page => new Surface(OfPage(page, loose), page),
        FrameworkElement element => new Surface(OfElement(element, element.Width, element.Height, loose), element),
        UIElement element => new Surface(OfElement(element, double.NaN, double.NaN, loose), element),
        _ => null,
    };

    private static FrameworkElement OfWindow(Window window, LooseXaml loose)
    {
        Border client = Client(window, window.Content, window.ContentTemplate, window.ContentTemplateSelector);
        client.Background = window.Background;
        client.BorderBrush = window.BorderBrush;
        client.BorderThickness = window.BorderThickness;
        bool titled = window.WindowStyle != WindowStyle.None;
        Thickness frame = titled ? SystemParameters.WindowNonClientFrameThickness : default;
        double caption = window.WindowStyle == WindowStyle.ToolWindow ? SystemParameters.SmallCaptionHeight : Math.Max(0, frame.Top - frame.Bottom);
        SizeToContent fits = window.SizeToContent;
        double width = fits.HasFlag(SizeToContent.Width) ? double.NaN : Given(window.Width) ?? loose.DesignWidth ?? Unsized.Width;
        double height = fits.HasFlag(SizeToContent.Height) ? double.NaN : Given(window.Height) ?? loose.DesignHeight ?? Unsized.Height;
        client.Width = double.IsNaN(width) ? double.NaN : Math.Max(0, width - frame.Left - frame.Right);
        client.Height = double.IsNaN(height) ? double.NaN : Math.Max(0, height - frame.Top - frame.Bottom);
        DockPanel surface = new() { LastChildFill = true, Style = Unstyled<DockPanel>() };
        if (titled)
        {
            Border bar = new()
            {
                Height = caption,
                Style = Unstyled<Border>(),
                Background = SystemColors.ControlLightLightBrush,
                BorderBrush = SystemColors.ControlLightBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new TextBlock
                {
                    Text = window.Title,
                    Style = Unstyled<TextBlock>(),
                    Margin = new Thickness(10, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontFamily = SystemFonts.CaptionFontFamily,
                    FontSize = SystemFonts.CaptionFontSize,
                    Foreground = SystemColors.ControlTextBrush,
                },
            };
            DockPanel.SetDock(bar, Dock.Top);
            surface.Children.Add(bar);
        }
        surface.Children.Add(client);
        return surface;
    }

    private static FrameworkElement OfPage(Page page, LooseXaml loose)
    {
        Border client = Client(page, page.Content, null, null);
        client.Background = page.Background ?? SystemColors.WindowBrush;
        client.Width = Given(page.Width) ?? loose.DesignWidth ?? Unsized.Width;
        client.Height = Given(page.Height) ?? loose.DesignHeight ?? Unsized.Height;
        return client;
    }

    private static FrameworkElement OfElement(UIElement element, double width, double height, LooseXaml loose) => new Border
    {
        Style = Unstyled<Border>(),
        Background = SystemColors.WindowBrush,
        Width = double.IsNaN(width) && loose.DesignWidth is { } designWidth ? designWidth : double.NaN,
        Height = double.IsNaN(height) && loose.DesignHeight is { } designHeight ? designHeight : double.NaN,
        Child = element,
    };

    /// <summary>A border holding what a window or page holds, its content moved there with all its content reads from it:
    /// its resources, the names it registered, its data context and the text properties its content inherits.</summary>
    private static Border Client(FrameworkElement owner, object? content, DataTemplate? template, DataTemplateSelector? selector)
    {
        switch (owner)
        {
            case ContentControl control:
                control.Content = null;
                break;
            case Page page:
                page.Content = null;
                break;
        }
        ResourceDictionary resources = owner.Resources;
        owner.Resources = new ResourceDictionary();
        UIElement shown = content as UIElement ?? new ContentPresenter { Content = content, ContentTemplate = template, ContentTemplateSelector = selector };
        Border client = new()
        {
            Style = Unstyled<Border>(),
            Resources = resources,
            Child = new AdornerDecorator { Child = shown },
            FlowDirection = owner.FlowDirection,
            Language = owner.Language,
        };
        NameScope.SetNameScope(client, NameScope.GetNameScope(owner));
        if (owner.DataContext is { } data)
        {
            client.DataContext = data;
        }
        else if (BindingOperations.GetBindingBase(owner, FrameworkElement.DataContextProperty) is { } binding)
        {
            client.SetBinding(FrameworkElement.DataContextProperty, binding);
        }
        foreach (DependencyProperty inherited in Inherited)
        {
            client.SetValue(inherited, owner.GetValue(inherited));
        }
        return client;
    }

    /// <summary>The text properties a window's or page's content inherits from it: a control's are text elements' own,
    /// which a border passes down as attached ones.</summary>
    private static readonly DependencyProperty[] Inherited =
    [
        TextElement.ForegroundProperty, TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.FontStyleProperty,
        TextElement.FontWeightProperty, TextElement.FontStretchProperty,
    ];

    private static double? Given(double size) => double.IsNaN(size) || double.IsInfinity(size) ? null : size;

    /// <summary>A style of its own, so the app's implicit styles leave the previewer's own parts as they are.</summary>
    private static Style Unstyled<T>() => new(typeof(T));
}
