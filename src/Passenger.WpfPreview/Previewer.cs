using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xaml;
using System.Xml;
using System.Xml.Linq;

namespace Passenger.WpfPreview;

/// <summary>Draws the file each request brings, on the application's thread: App.xaml's resources made the application's
/// (again only when its text changes), the file loaded as loose XAML (<see cref="LooseXaml"/>), its design-time data
/// context set, its root put on a surface (<see cref="Surface"/>) in a window that is never shown, so it is laid out and
/// loaded as in the app, and the surface rendered to a PNG with each of the file's elements' boxes. Requests that come
/// while one is drawn wait, and only the newest is drawn.</summary>
internal sealed partial class Previewer(Assembly project, TextWriter protocol, Dispatcher dispatcher)
{
    /// <summary>The largest side a frame is drawn at, in pixels.</summary>
    private const int Largest = 8192;

    private const string DesignNamespace = "clr-namespace:Passenger.WpfPreview.Design;assembly=Passenger.WpfPreview";

    private readonly object gate = new();
    private readonly XamlSchemaContext schema = System.Windows.Markup.XamlReader.GetWpfSchemaContext();
    private readonly string localAssembly = project.GetName().Name ?? string.Empty;
    private WpfPreviewRequest? waiting;
    private bool posted;
    private bool drawing;
    private int answering;
    private string? applicationText;
    private string? applicationProblem;
    private Shown? shown;

    /// <summary>A request read: drawn once the application's thread is free, unless a newer one comes first.</summary>
    public void Take(WpfPreviewRequest request)
    {
        lock (gate)
        {
            waiting = request;
            if (posted)
            {
                return;
            }
            posted = true;
        }
        dispatcher.InvokeAsync(Next);
    }

    /// <summary>The project's code failed outside a drawing (a control's handler, later): said as the newest request's
    /// error.</summary>
    public void Failed(Exception error) => Answer(Error(answering, error));

    private void Next()
    {
        WpfPreviewRequest? request;
        lock (gate)
        {
            posted = false;
            if (drawing)
            {
                // Run from the drawing's own wait for the window to settle: drawn once that drawing is done.
                return;
            }
            request = waiting;
            waiting = null;
        }
        if (request is null)
        {
            return;
        }
        drawing = true;
        answering = request.Sequence;
        WpfPreviewReply reply;
        try
        {
            reply = Draw(request);
        }
        finally
        {
            drawing = false;
        }
        Answer(reply);
        lock (gate)
        {
            if (waiting is null || posted)
            {
                return;
            }
            posted = true;
        }
        dispatcher.InvokeAsync(Next);
    }

    private void Answer(WpfPreviewReply reply)
    {
        lock (protocol)
        {
            protocol.WriteLine(JsonSerializer.Serialize(reply, WpfPreviewJson.Options));
        }
    }

    private WpfPreviewReply Draw(WpfPreviewRequest request)
    {
        LetGo();
        try
        {
            if (request.Application is { } application && application.Xaml != applicationText)
            {
                applicationText = application.Xaml;
                applicationProblem = LoadApplication(application);
            }
            else if (request.Application is null && applicationText is not null)
            {
                applicationText = null;
                applicationProblem = null;
                Application.Current.Resources = new ResourceDictionary();
            }
            LooseXaml loose = LooseXaml.Read(request.Xaml, schema, localAssembly);
            if (loose.RootNamespace == LooseXaml.Presentation && loose.RootName == "Application")
            {
                return Nothing(request, "App.xaml defines the application, which has nothing to draw: its resources are what the other files are drawn with");
            }
            if (loose.RootNamespace == LooseXaml.Presentation && loose.RootName == "ResourceDictionary")
            {
                return Nothing(request, "A resource dictionary has nothing to draw");
            }
            Uri baseUri = new(request.BaseUri);
            object root = Load(loose.Text, baseUri);
            List<string> notes = [];
            if (applicationProblem is not null)
            {
                notes.Add(applicationProblem);
            }
            ApplyDesignContexts(root, loose, baseUri, notes);
            if (Surface.For(root, loose) is not { } surface)
            {
                return Nothing(request, $"A {root.GetType().Name} has nothing to draw");
            }
            HwndSource? window = Host(surface.Root);
            shown = new Shown(surface, window);
            Settle();
            if (window is null)
            {
                // Laid out alone: again, at the size its bindings and loading have given it.
                surface.Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                surface.Root.Arrange(new Rect(surface.Root.DesiredSize));
            }
            surface.Root.UpdateLayout();
            Size size = new(surface.Root.ActualWidth, surface.Root.ActualHeight);
            if (size.Width < 1 || size.Height < 1)
            {
                return Nothing(request, "Nothing to draw: the root is 0 × 0 (sized to content with none, and no d:DesignWidth and d:DesignHeight)");
            }
            double scale = Math.Clamp(request.Scale, 0.25, 4);
            int width = (int)Math.Ceiling(size.Width * scale);
            int height = (int)Math.Ceiling(size.Height * scale);
            if (width > Largest || height > Largest)
            {
                return Error(request.Sequence, $"The design is too large to draw: {size.Width:0} × {size.Height:0}");
            }
            return new WpfPreviewReply
            {
                Kind = WpfPreviewKinds.Frame,
                Sequence = request.Sequence,
                Png = Render(surface.Root, width, height, scale),
                Scale = scale,
                Elements = Boxes(surface, baseUri),
                Note = notes.Count == 0 ? null : string.Join("; ", notes),
            };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return Error(request.Sequence, error);
        }
    }

    /// <summary>App.xaml's resources as the application's, read as loose XAML from its <c>Application.Resources</c>; what
    /// kept them from loading, or null.</summary>
    private string? LoadApplication(WpfPreviewSource application)
    {
        try
        {
            LooseXaml loose = LooseXaml.Read(application.Xaml, schema, localAssembly);
            XElement root = XDocument.Parse(loose.Text).Root!;
            string property = root.Name.LocalName + ".Resources";
            if (root.Elements().FirstOrDefault(e => e.Name.LocalName == property) is not { } resources)
            {
                Application.Current.Resources = new ResourceDictionary();
                return null;
            }
            List<XElement> items = [.. resources.Elements()];
            XElement dictionary = items is [{ Name.LocalName: "ResourceDictionary" } only]
                ? new XElement(only)
                : new XElement(XName.Get("ResourceDictionary", LooseXaml.Presentation), items);
            foreach (XAttribute declared in root.Attributes().Where(a => a.IsNamespaceDeclaration || a.Name.LocalName == "Ignorable"))
            {
                if (dictionary.Attribute(declared.Name) is null)
                {
                    dictionary.Add(new XAttribute(declared));
                }
            }
            Application.Current.Resources = (ResourceDictionary)Load(dictionary.ToString(SaveOptions.DisableFormatting), new Uri(application.BaseUri));
            return null;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Application.Current.Resources = new ResourceDictionary();
            (string message, int? line, _) = Describe(error);
            return $"App.xaml's resources did not load: {message}{(line is { } at ? $" (App.xaml, line {at})" : string.Empty)}";
        }
    }

    /// <summary>Loose XAML loaded as WPF loads it, the project's assembly being the one a namespace with no assembly names,
    /// and relative URIs resolving against the file's place in that assembly.</summary>
    private object Load(string text, Uri baseUri)
    {
        XamlXmlReaderSettings settings = new()
        {
            BaseUri = baseUri,
            LocalAssembly = project,
            ProvideLineInfo = true,
            IgnoreUidsOnPropertyElements = true,
        };
        using XmlReader xml = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        using XamlXmlReader reader = new(xml, schema, settings);
        return System.Windows.Markup.XamlReader.Load(reader);
    }

    /// <summary>Each element's <c>d:DataContext</c> as its data context, the designer's prefix taken as the previewer's
    /// (<see cref="Design.DesignInstanceExtension"/>); one that does not load is noted and left out.</summary>
    private void ApplyDesignContexts(object root, LooseXaml loose, Uri baseUri, List<string> notes)
    {
        if (loose.Contexts.Count == 0 || root is not DependencyObject top)
        {
            return;
        }
        List<(FrameworkElement Element, XamlSourceInfo Place)> placed = [];
        foreach (DependencyObject node in Logical(top))
        {
            if (node is FrameworkElement element && VisualDiagnostics.GetXamlSourceInfo(node) is { } place && Ours(place, baseUri))
            {
                placed.Add((element, place));
            }
        }
        foreach (DesignContext context in loose.Contexts)
        {
            FrameworkElement? target = placed.Where(p => p.Place.LineNumber == context.Line && Math.Abs(p.Place.LinePosition - context.Column) <= 1)
                .Select(p => p.Element).FirstOrDefault();
            if (target is null)
            {
                continue;
            }
            try
            {
                XElement snippet = new(XName.Get("FrameworkElement", LooseXaml.Presentation));
                foreach ((string prefix, string uri) in context.Namespaces)
                {
                    if (prefix is "xml" or "xmlns")
                    {
                        continue;
                    }
                    string mapped = uri == LooseXaml.Designer ? DesignNamespace : uri;
                    snippet.Add(prefix.Length == 0 ? new XAttribute("xmlns", mapped) : new XAttribute(XNamespace.Xmlns + prefix, mapped));
                }
                snippet.Add(new XAttribute("DataContext", context.Value));
                FrameworkElement made = (FrameworkElement)Load(snippet.ToString(SaveOptions.DisableFormatting), baseUri);
                if (made.DataContext is { } value)
                {
                    target.DataContext = value;
                }
                else if (BindingOperations.GetBindingBase(made, FrameworkElement.DataContextProperty) is { } binding)
                {
                    target.SetBinding(FrameworkElement.DataContextProperty, binding);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                notes.Add($"d:DataContext on line {context.Line} did not load: {Describe(error).Message}");
            }
        }
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject node)
    {
        yield return node;
        foreach (DependencyObject child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            foreach (DependencyObject below in Logical(child))
            {
                yield return below;
            }
        }
    }

    /// <summary>Whether a place is in the file drawn: not another file's (a user control's own XAML, read from its BAML).</summary>
    private static bool Ours(XamlSourceInfo place, Uri baseUri) => place.SourceUri is null || place.SourceUri == baseUri;

    /// <summary>The surface in a window of its own that is never shown, so it is laid out, styled and loaded as an app's
    /// content is; none when the system gives no such window, and the surface is laid out alone.</summary>
    private static HwndSource? Host(FrameworkElement surface)
    {
        try
        {
            HwndSourceParameters parameters = new("Passenger WPF preview")
            {
                // A popup never shown, off every screen, never activated and never in the taskbar.
                WindowStyle = unchecked((int)0x80000000),
                ExtendedWindowStyle = 0x08000080,
                PositionX = -32000,
                PositionY = -32000,
                Width = 1,
                Height = 1,
            };
            return new HwndSource(parameters) { SizeToContent = SizeToContent.WidthAndHeight, RootVisual = surface };
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            Console.Error.WriteLine($"No window to lay the preview out in: {error.Message}");
            return null;
        }
    }

    /// <summary>Lets the dispatcher run what the surface's layout, bindings and Loaded queued, before it is drawn.</summary>
    private void Settle() => dispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);

    /// <summary>The surface drawn at a scale, as a PNG whose pixels are at 96 DPI (the pane sizes it by the scale).</summary>
    private static byte[] Render(FrameworkElement surface, int width, int height, double scale)
    {
        RenderTargetBitmap rendered = new(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rendered.Render(surface);
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        rendered.CopyPixels(pixels, stride, 0);
        BitmapSource plain = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        PngBitmapEncoder png = new();
        png.Frames.Add(BitmapFrame.Create(plain));
        using MemoryStream bytes = new();
        png.Save(bytes);
        return bytes.ToArray();
    }

    /// <summary>The boxes of the file's own elements as drawn: the root's (a window's or page's is the whole frame), then
    /// every element of the visual tree the file made, parents before children. What a template or another file's XAML
    /// made inside them is theirs.</summary>
    private static List<WpfPreviewBox> Boxes(Surface surface, Uri baseUri)
    {
        List<WpfPreviewBox> boxes = [];
        FrameworkElement root = surface.Root;
        // A window or page is not in the tree drawn: its box is the surface's.
        bool drawnItself = surface.Loaded is Visual drawn && drawn.IsDescendantOf(root);
        if (!drawnItself && VisualDiagnostics.GetXamlSourceInfo(surface.Loaded) is { } top && Ours(top, baseUri))
        {
            boxes.Add(new WpfPreviewBox(top.LineNumber, top.LinePosition, 0, 0, root.ActualWidth, root.ActualHeight));
        }
        Walk(root, root, baseUri, boxes);
        return boxes;
    }

    private static void Walk(Visual node, FrameworkElement root, Uri baseUri, List<WpfPreviewBox> boxes)
    {
        if (node is UIElement { IsVisible: true } element && VisualDiagnostics.GetXamlSourceInfo(node) is { } place && Ours(place, baseUri)
            && node is not FrameworkElement { TemplatedParent: not null })
        {
            Rect box = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
            boxes.Add(new WpfPreviewBox(place.LineNumber, place.LinePosition, box.X, box.Y, box.Width, box.Height));
        }
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is Visual child)
            {
                Walk(child, root, baseUri, boxes);
            }
        }
    }

    /// <summary>The last drawing let go: its window destroyed and its root (a window that was never shown) closed.</summary>
    private void LetGo()
    {
        if (shown is not { } last)
        {
            return;
        }
        shown = null;
        if (last.Window is { } window)
        {
            window.RootVisual = null;
            window.Dispose();
        }
        Close(last.Surface.Loaded);
    }

    private static void Close(object root)
    {
        if (root is Window window)
        {
            try
            {
                window.Close();
            }
            catch (InvalidOperationException error)
            {
                Console.Error.WriteLine($"A preview's window did not close: {error.Message}");
            }
        }
    }

    private static WpfPreviewReply Nothing(WpfPreviewRequest request, string why) =>
        new() { Kind = WpfPreviewKinds.Nothing, Sequence = request.Sequence, Message = why };

    private static WpfPreviewReply Error(int sequence, string message) =>
        new() { Kind = WpfPreviewKinds.Error, Sequence = sequence, Message = message };

    private static WpfPreviewReply Error(int sequence, Exception error)
    {
        (string message, int? line, int? column) = Describe(error);
        return new WpfPreviewReply { Kind = WpfPreviewKinds.Error, Sequence = sequence, Message = message, Line = line, Column = column };
    }

    /// <summary>What an exception says of the XAML, without the place its message repeats, with its line and column, and
    /// the reason inside a parser's "threw an exception".</summary>
    private static (string Message, int? Line, int? Column) Describe(Exception error)
    {
        int? line = null;
        int? column = null;
        switch (error)
        {
            case System.Windows.Markup.XamlParseException parse when parse.LineNumber > 0:
                line = parse.LineNumber;
                column = parse.LinePosition;
                break;
            case XamlException xaml when xaml.LineNumber > 0:
                line = xaml.LineNumber;
                column = xaml.LinePosition;
                break;
            case XmlException xml when xml.LineNumber > 0:
                line = xml.LineNumber;
                column = xml.LinePosition;
                break;
        }
        if (error is TargetInvocationException { InnerException: { } invoked })
        {
            error = invoked;
        }
        string message = Plain(error.Message);
        Exception? inner = error.InnerException;
        while (inner is TargetInvocationException { InnerException: { } deeper })
        {
            inner = deeper;
        }
        if (inner is not null && Plain(inner.Message) is { Length: > 0 } reason && !message.Contains(reason, StringComparison.Ordinal))
        {
            message = $"{message.TrimEnd('.')}: {reason}";
        }
        return (message, line, column);
    }

    /// <summary>A message without the quotes and place WPF's parser wraps it in, or the place an XML reader adds.</summary>
    private static string Plain(string message)
    {
        string first = message.Split('\n')[0].Trim();
        if (WpfPlace().Match(first) is { Success: true } wpf)
        {
            first = wpf.Groups["message"].Value;
        }
        return XmlPlace().Replace(first, string.Empty).Trim();
    }

    [GeneratedRegex(@"^'(?<message>.*)' Line number '\d+' and line position '\d+'\.?$")]
    private static partial Regex WpfPlace();

    [GeneratedRegex(@"\s*Line \d+, position \d+\.?$")]
    private static partial Regex XmlPlace();

    /// <summary>What a drawing holds until the next: its surface and the window it is laid out in.</summary>
    private sealed record Shown(Surface Surface, HwndSource? Window);
}
