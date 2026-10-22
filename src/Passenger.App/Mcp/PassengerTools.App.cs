using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Passenger.App.Debugging;
using Passenger.App.LiveTree;
using Passenger.Core;
using Passenger.Inspector;

namespace Passenger.App.Mcp;

/// <summary>The Avalonia or WPF app Passenger runs or debugs, as an agent tests it: its windows, pictures the app draws of
/// them, its visual tree, clicks, typing, keys and scrolling given as a person's are, and its failed bindings. All through the
/// inspector Run and Debug launch an Avalonia 12 app or a WPF app with (<see cref="LiveApp"/>), the Live Visual Tree's, or for
/// Passenger's own windows the same inspector loaded into Passenger. A WPF app's clicks go through UI Automation and its keys
/// through WPF's input manager (src/Passenger.Inspector.Wpf/Driver.cs).</summary>
public sealed partial class PassengerTools
{
    // Said once a parameter for each app_ tool; the element query's syntax once, in app_tree's description.
    private const string AppChoice = "run, debug or ide (Passenger's own windows); by default the one running";

    private const string ControlQuery = "@N or a query, as app_tree describes";

    private const string WindowChoice = "@N from app_windows, its type or \"title\"; by default the first";

    private const string HeldKeys = "Held: ctrl, shift, alt, cmd (meta), primary (Cmd on macOS, Ctrl elsewhere)";

    /// <summary>How long a tool waits for an app launched a moment ago to show its UI.</summary>
    private static readonly TimeSpan AppUiLimit = TimeSpan.FromSeconds(60);

    [McpServerTool(Name = "app_windows", Title = "The app's windows", ReadOnly = true), Description(
        "The windows of the Avalonia or WPF app Run or Debug started with Passenger's inspector in it (an Avalonia 12 app, or " +
        "a WPF app on Windows; one still starting is waited for), or Passenger's own with app ide: each with its id (@N, which " +
        "the other app_ tools take), type, title, size in units, pixels a unit, whether it is active and shown, and a popup's " +
        "(a menu's, a drop-down's) owner.")]
    public Task<AppWindows> AppWindowsList([Description(AppChoice)] string? app = null, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        LiveApp live = await InspectedAsync(app, cancel);
        IReadOnlyList<AppWindow> windows = await live.WindowsAsync();
        return new AppWindows(live.Name, live.Framework, live.Avalonia, live.ProcessId, [.. windows.Select(w => new AppWindowInfo(
            Id(w.Id), w.Type, w.Name, w.Title, Math.Round(w.Width, 2), Math.Round(w.Height, 2), w.Scaling, w.Active, w.Visible, w.Owner is long owner ? Id(owner) : null))]);
    });

    [McpServerTool(Name = "app_screenshot", Title = "A picture of the app", ReadOnly = true), Description(
        "A PNG of one of the app's windows (its open popups drawn over it) as the app draws it, so hidden, off-screen or " +
        "headless alike; or of an element's part of it, or an area (x, y, width, height in the window's units). At the " +
        "window's scaling by default, at most 1600 pixels a side; scale gives pixels a unit. Its text says how its pixels map " +
        "to the window's units, which app_click's x and y take.")]
    public Task<CallToolResult> AppScreenshot(
        [Description(AppChoice)] string? app = null,
        [Description(WindowChoice)] string? window = null,
        [Description(ControlQuery + "; the picture is its part of its window")] string? control = null,
        [Description("Pixels a unit, up to 8")] double? scale = null,
        int? x = null, int? y = null, int? width = null, int? height = null,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        LiveApp live = await InspectedAsync(app, cancel);
        (List<AppNode> nodes, AppNode chosenWindow) = await TreeOf(live, window, cancel);
        AppNode shown = control is { Length: > 0 } query ? AppQuery.One(Within(nodes, window is null ? null : chosenWindow), query) : chosenWindow;
        Box? area = x is null && y is null && width is null && height is null ? null : new Box(x ?? 0, y ?? 0, width ?? 400, height ?? 300);
        if (scale is <= 0 or > 8)
        {
            throw new ArgumentException("scale is pixels a unit, more than 0 and at most 8");
        }
        Picture picture = await live.CaptureAsync(shown.Element.Id, scale, area);
        string what = shown.Parent is null ? Titled(shown.Window) : $"{AppQuery.Short(shown.Element)} in {Titled(shown.Window)}";
        string text = string.Create(CultureInfo.InvariantCulture,
            $"{what}: {picture.Width}x{picture.Height} pixels of the window's area {picture.Area.X:0.#},{picture.Area.Y:0.#} {picture.Area.Width:0.#}x{picture.Area.Height:0.#}, " +
            $"{picture.Scale:0.##} pixels a unit, so the pixel px,py is the window's point {picture.Area.X:0.#}+px/{picture.Scale:0.##}, {picture.Area.Y:0.#}+py/{picture.Scale:0.##}");
        return new CallToolResult { Content = [ImageContentBlock.FromBytes(picture.Png, "image/png"), new TextContentBlock { Text = text }] };
    });

    [McpServerTool(Name = "app_tree", Title = "The app's visual tree", ReadOnly = true), Description(
        "The app's visual tree (the Live Visual Tree's), a level a line: each element's type, #name, .classes, \"text\" it " +
        "shows, @id, box in its window's units (x,y widthxheight), and [hidden], [out of view], [disabled], [focused]; with " +
        "properties, their values ({Name=value}). A template's parts are left out (what they hold comes up a level) unless " +
        "templates is true, and what a hidden element holds is counted unless hidden is true. control starts the tree at an " +
        "element; filter lists only the elements a query finds, each with where it is. Every window, unless one is named.\n" +
        "Every app_ tool names an element (control, filter) by @N, an id shown here, or by a query: a type, #name, .class and " +
        "\"text\" it shows (Button#Save, \"Save\", TextBlock.title), steps joined by / for one inside another (#Panel/Button), " +
        "[n] for the nth match; a template's part only by its #name or @N.")]
    public Task<string> AppTree(
        [Description(AppChoice)] string? app = null,
        [Description("@N from app_windows, its type or \"title\"; every window by default")] string? window = null,
        [Description("Where the tree starts: @N or a query")] string? control = null,
        [Description("A query for the elements to list, in place of the tree")] string? filter = null,
        [Description("Levels below where it starts")] int? depth = null,
        [Description("Properties to show the values of where an element has them (IsEnabled, Foreground, Grid.Row)")] IReadOnlyList<string>? properties = null,
        bool templates = false,
        bool hidden = false,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        LiveApp live = await InspectedAsync(app, cancel);
        AppTree tree = await live.AgentTreeAsync(properties ?? []);
        List<AppNode> nodes = AppQuery.Flatten(tree.Roots);
        if (nodes.Count == 0)
        {
            return $"{live.Name} has no window open";
        }
        List<AppNode> scope = window is { Length: > 0 } ? Within(nodes, Window(nodes, window, live)) : nodes;
        List<AppNode> roots = control is { Length: > 0 } query ? [AppQuery.One(scope, query)] : [.. scope.Where(n => n.Parent is null)];
        if (filter is { Length: > 0 } wanted)
        {
            List<AppNode> under = roots.Count == 1 && roots[0].Parent is not null ? Within(scope, roots[0]) : scope;
            List<AppNode> found = AppQuery.Find(under, wanted, templates);
            if (found.Count == 0)
            {
                return $"Nothing in {live.Name} matches {wanted}";
            }
            return string.Join('\n', found.Take(AppQuery.MostLines).Select(AppQuery.Where))
                + (found.Count > AppQuery.MostLines ? $"\n... and {found.Count - AppQuery.MostLines} more" : string.Empty);
        }
        return AppQuery.Write(roots, depth, templates, hidden);
    });

    [McpServerTool(Name = "app_details", Title = "An element's properties and styles", ReadOnly = true), Description(
        "One element looked at closely, as the Live Visual Tree's details show it, Avalonia or WPF: its type, classes and " +
        "pseudo-classes, its XAML's place (or why none), the template it is a part of, its layout (bounds, desired size, " +
        "margin, border, padding, alignment); each property not at its default (every one with defaults) with its value, type " +
        "and source (Local, Style, Theme, Template, Inherited, Animation, Current, Direct, Default) and what that is (a " +
        "style's selector, the ancestor) with its place; and every style that could apply (selector, kind, origin, place), " +
        "whether it applies and why not, and whether each setter wins, is overridden (by what) or is inactive. breakpoint_set " +
        "with control and property breaks when one changes.")]
    public Task<AppElementDetails> AppDetails(
        [Description(ControlQuery)] string control,
        [Description(AppChoice)] string? app = null,
        [Description(WindowChoice)] string? window = null,
        [Description("Only the properties whose names contain this")] string? properties = null,
        [Description("Properties at their defaults too")] bool defaults = false,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        LiveApp live = await InspectedAsync(app, cancel);
        (List<AppNode> nodes, AppNode chosenWindow) = await TreeOf(live, window, cancel);
        AppNode element = AppQuery.One(Within(nodes, window is null ? null : chosenWindow), control);
        ElementDetails details = AppPlaces.InEditor(await live.DetailsAsync(element.Element.Id), Editors);
        string typed = properties?.Trim() ?? string.Empty;
        BreakpointStore breakpoints = Session.Debug.Breakpoints;
        bool watchable = live == Session.LiveApp;
        LayoutInfo layout = details.Layout;
        return new AppElementDetails(
            AppQuery.Short(element.Element),
            details.Type,
            details.FullType,
            details.Name,
            details.Classes,
            details.PseudoClasses,
            details.Source is { } source ? Placed(source) : null,
            details.Source is null ? details.NoSource : null,
            details.TemplatedParent,
            new AppLayout(Boxed(layout.Bounds), layout.InWindow is { } inWindow ? Boxed(inWindow) : null,
                string.Create(CultureInfo.InvariantCulture, $"{layout.DesiredWidth:0.##}x{layout.DesiredHeight:0.##}"),
                Sides(layout.Margin), layout.Border is { } border ? Sides(border) : null, layout.Padding is { } padding ? Sides(padding) : null,
                $"{layout.HorizontalAlignment}, {layout.VerticalAlignment}"),
            [.. details.Properties
                .Where(p => (defaults || p.Source != ValueSources.Default) && (typed.Length == 0 || p.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)))
                .Select(p => new AppProperty(p.Name, p.Value, p.Type, p.Source, p.Detail, p.Place is { } at ? Placed(at) : null,
                    watchable && breakpoints.FindProperty(details.Id, p.Name) is not null ? true : null))],
            [.. details.Styles.Select(s => new AppStyle(s.Selector, s.Kind, s.Origin, s.Place is { } at ? Placed(at) : null, s.Applies, s.WhyNot,
                [.. s.Setters.Select(setter => new AppSetter(setter.Property, setter.Value, setter.State, AppPlaces.OverriddenBy(setter), setter.Place is { } placed ? Placed(placed) : null))]))]);

        static string Boxed(Box box) => string.Create(CultureInfo.InvariantCulture, $"{box.X:0.##},{box.Y:0.##} {box.Width:0.##}x{box.Height:0.##}");

        static string Sides(Edges edges) => string.Create(CultureInfo.InvariantCulture, $"{edges.Left:0.##},{edges.Top:0.##},{edges.Right:0.##},{edges.Bottom:0.##}");
    });

    /// <summary>A place in the app's XAML as every other tool gives one: the folder's path, the line (none for a root the build
    /// recorded by its file alone) and the column, the editor's (<see cref="AppPlaces"/>).</summary>
    private string Placed(SourcePlace place)
    {
        string file = Relative(PathNames.Canonical(place.File));
        return place.Line > 0 ? string.Create(CultureInfo.InvariantCulture, $"{file}:{place.Line}:{place.Column}") : file;
    }

    [McpServerTool(Name = "app_click", Title = "Click in the app", Destructive = true), Description(
        "Clicks in the app as a mouse would, through the window's own input (handlers, commands and focus all run): at an " +
        "element's centre (or x,y from its top left), or at x,y in a window. The window is not brought forward and the real " +
        "pointer does not move. An element hidden, disabled, out of view or covered is refused, saying which. Says what took " +
        "the click, what it shows now and what has the focus.")]
    public Task<string> AppClick(
        [Description(AppChoice)] string? app = null,
        [Description(ControlQuery)] string? control = null,
        double? x = null,
        double? y = null,
        [Description(WindowChoice)] string? window = null,
        [Description("left, right or middle")] string button = "left",
        int count = 1,
        [Description(HeldKeys)] IReadOnlyList<string>? modifiers = null,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (LiveApp live, AppNode? element, AppNode target) = await Target(app, control, window, needsPoint: true, x, y, cancel);
        (InputDone? sent, string? paused) = await SendAsync(live, new InputAction(InputKinds.Click)
        {
            Window = target.Window.Id,
            Element = element?.Element.Id,
            X = x,
            Y = y,
            Button = button,
            Count = count,
            Modifiers = Modifiers(modifiers),
        }, cancel);
        string clicked = count switch
        {
            2 => "Double-clicked",
            >= 3 => "Triple-clicked",
            _ => button == "left" ? "Clicked" : $"{char.ToUpperInvariant(button[0])}{button[1..]}-clicked",
        };
        if (sent is not { } done)
        {
            return $"{clicked} {Named(element)}, and {paused}";
        }
        return $"{clicked} {Aimed(element, done)}{Took(element, done, "click")}{Said(done)}";
    });

    [McpServerTool(Name = "app_type", Title = "Type into the app", Destructive = true), Description(
        "Types text into the app as a keyboard would, a key at a time (a newline is Enter, a tab Tab): into an element, given " +
        "the focus first (a text box types at its end), else into what has the focus. With replace, Select All goes first. " +
        "Says what it shows now.")]
    public Task<string> AppType(
        string text,
        [Description(AppChoice)] string? app = null,
        [Description(ControlQuery)] string? control = null,
        bool replace = false,
        [Description(WindowChoice)] string? window = null,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (LiveApp live, AppNode? element, AppNode target) = await Target(app, control, window, needsPoint: false, null, null, cancel);
        (InputDone? sent, string? paused) = await SendAsync(live, new InputAction(InputKinds.Type)
        {
            Window = target.Window.Id,
            Element = element?.Element.Id,
            Text = text,
            Replace = replace,
        }, cancel);
        if (sent is not { } done)
        {
            return $"Typed into {Named(element)}, and {paused}";
        }
        return $"Typed {text.Length} character{(text.Length == 1 ? "" : "s")} into {done.TargetName ?? "the window, nothing having the focus"}{Said(done)}";
    });

    [McpServerTool(Name = "app_key", Title = "Press keys in the app", Destructive = true), Description(
        "Presses keys in the app as a keyboard would, chord after chord (\"Enter\", \"Tab Tab Space\", \"ctrl+A Delete\", " +
        "\"primary+S\"): an element is given the focus first, else they go to what has it. Keys by Avalonia's names, which " +
        "WPF's share (Enter, Escape, Back, Delete, Left, PageDown, F5, A, D1 or 1); modifiers ctrl, shift, alt, cmd (meta), " +
        "primary (Cmd on macOS, Ctrl elsewhere). A printable key types its character when nothing takes the key; key bindings " +
        "and commands run.")]
    public Task<string> AppKey(
        [Description("Chords split by spaces, a chord's keys joined by +")] string keys,
        [Description(AppChoice)] string? app = null,
        [Description(ControlQuery)] string? control = null,
        [Description(WindowChoice)] string? window = null,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        IReadOnlyList<Chord> chords = Chords(keys);
        (LiveApp live, AppNode? element, AppNode target) = await Target(app, control, window, needsPoint: false, null, null, cancel);
        (InputDone? sent, string? paused) = await SendAsync(live, new InputAction(InputKinds.Key)
        {
            Window = target.Window.Id,
            Element = element?.Element.Id,
            Keys = chords,
        }, cancel);
        if (sent is not { } done)
        {
            return $"Pressed {keys.Trim()} in {Named(element)}, and {paused}";
        }
        return $"Pressed {keys.Trim()} in {done.TargetName ?? "the window, nothing having the focus"}{Said(done)}";
    });

    [McpServerTool(Name = "app_scroll", Title = "Scroll in the app", Destructive = true), Description(
        "Turns the mouse wheel over an element (its centre) or at x,y in a window, as a person scrolls: dy notches down, dx " +
        "right (3 down when neither is given). Says where the scroll viewer under it is now.")]
    public Task<string> AppScroll(
        [Description(AppChoice)] string? app = null,
        [Description(ControlQuery)] string? control = null,
        [Description("Notches down; negative up")] double? dy = null,
        [Description("Notches right; negative left")] double? dx = null,
        double? x = null,
        double? y = null,
        [Description(WindowChoice)] string? window = null,
        [Description(HeldKeys)] IReadOnlyList<string>? modifiers = null,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        (LiveApp live, AppNode? element, AppNode target) = await Target(app, control, window, needsPoint: true, x, y, cancel);
        double down = dy ?? (dx is null ? 3 : 0);
        double across = dx ?? 0;
        (InputDone? sent, string? paused) = await SendAsync(live, new InputAction(InputKinds.Scroll)
        {
            Window = target.Window.Id,
            Element = element?.Element.Id,
            X = x,
            Y = y,
            DeltaX = across,
            DeltaY = down,
            Modifiers = Modifiers(modifiers),
        }, cancel);
        if (sent is not { } done)
        {
            return $"Scrolled over {Named(element)}, and {paused}";
        }
        string turned = string.Join(" and ", new[] { Notches(down, "down", "up"), Notches(across, "right", "left") }.Where(n => n.Length > 0));
        string scrolled = $"Scrolled {turned} over {Aimed(element, done)}{Took(element, done, "wheel")}";
        return done.Shows is { } where ? $"{scrolled}: {where}" : $"{scrolled}, which no scroll viewer holds";

        static string Notches(double notches, string ahead, string back) => notches == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(notches):0.#} notch{(Math.Abs(notches) == 1 ? "" : "es")} {(notches < 0 ? back : ahead)}");
    });

    [McpServerTool(Name = "app_binding_errors", Title = "The app's failed bindings", ReadOnly = true), Description(
        "The bindings that failed in the Avalonia or WPF apps Run and Debug started last (or the one named), each once: its " +
        "control, property, path, the framework's message (and the nearest members when its source lacks the one named), its " +
        "place in the XAML and how many times it failed. An app that ended keeps its list until the next of its kind starts. " +
        "Failures past the first 1000 an app lists are counted in one last entry of level Untold.")]
    public Task<IReadOnlyList<AppBindingError>> AppBindingErrors([Description(AppChoice)] string? app = null, CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        IEnumerable<LiveApp> apps = app switch
        {
            null => new[] { Session.RunApp, Session.LiveApp }.OfType<LiveApp>(),
            "run" => [Session.RunApp ?? throw new InvalidOperationException("Run has started no Avalonia or WPF app in this folder")],
            "debug" => [Session.LiveApp ?? throw new InvalidOperationException("Debug has started no Avalonia or WPF app in this folder")],
            "ide" => [LiveApp.Own()],
            _ => throw new ArgumentException($"No app '{app}': name run, debug or ide"),
        };
        List<LiveApp> chosen = [.. apps];
        if (chosen.Count == 0)
        {
            throw new InvalidOperationException("No Avalonia or WPF app has been run or debugged in this folder; run or debug_start one");
        }
        return Task.FromResult<IReadOnlyList<AppBindingError>>([.. chosen.SelectMany(live =>
        {
            string which = live == Session.LiveApp ? "debug" : live == Session.RunApp ? "run" : "ide";
            IEnumerable<AppBindingError> listed = live.Bindings.Select(failure =>
            {
                BindingProblem placed = Builds.Placed(live, failure);
                Passenger.App.Run.Problem problem = placed.Problem;
                return new AppBindingError(which, failure.Level, failure.Control, failure.Property, failure.Path, BindingErrors.Message(failure),
                    problem.Path is { } file ? $"{Relative(file)}:{problem.Position.Line + 1}:{problem.Position.Column + 1}" : null, failure.Count, failure.Nearest);
            });
            return live.Untold.Bindings == 0 ? listed : listed.Concat(BindingErrors.Untold(live).Take(1).Select(untold =>
                new AppBindingError(which, "Untold", string.Empty, string.Empty, string.Empty, $"{untold.Said}: {untold.Why}", null, live.Untold.Bindings, [])));
        })]);
    });

    /// <summary>The app a tool acts on, once its UI is up: the one named, or the one running (Run's or Debug's), refusing in
    /// words that say what to do when none is, two are, it is paused, refused by the inspector, or ended.</summary>
    private async Task<LiveApp> InspectedAsync(string? app, CancellationToken cancel)
    {
        LiveApp chosen = app switch
        {
            null => Running(),
            "run" => Session.RunApp ?? throw new InvalidOperationException(Builds.IsRunning
                ? $"What Run started is not {Inspectable}, or Hot Reload runs it"
                : "Run has started no Avalonia or WPF app; run starts one"),
            "debug" => Session.LiveApp ?? throw new InvalidOperationException(Session.Debug.IsDebugging
                ? $"What is being debugged is not {Inspectable}"
                : "Debug has started no Avalonia or WPF app; debug_start starts one"),
            "ide" => LiveApp.Own(),
            _ => throw new ArgumentException($"No app '{app}': name run, debug or ide"),
        };
        if (chosen == Session.LiveApp && Session.Debug.Session is { State: DebugState.Paused })
        {
            throw new InvalidOperationException($"{chosen.Name} is paused in the debugger, and its UI answers nothing until it runs: debug_continue");
        }
        await Until(() => chosen.State != LiveAppState.Waiting, AppUiLimit, cancel);
        return chosen.State switch
        {
            LiveAppState.Connected => chosen,
            LiveAppState.Waiting => throw new TimeoutException($"{chosen.Name} has not shown its UI in {AppUiLimit.TotalSeconds:0} s"),
            LiveAppState.Refused => throw new InvalidOperationException(chosen.Problem ?? $"{chosen.Name} cannot be inspected"),
            _ => throw new InvalidOperationException($"{chosen.Name} has ended"),
        };
    }

    /// <summary>The app running, Run's or Debug's, when it is one.</summary>
    private LiveApp Running()
    {
        List<LiveApp> live = [.. new[] { Session.RunApp, Session.LiveApp }.OfType<LiveApp>().Where(a => a.State is LiveAppState.Waiting or LiveAppState.Connected)];
        return live switch
        {
            [LiveApp only] => only,
            [LiveApp run, LiveApp debug] => throw new ArgumentException($"Run's {run.Name} and Debug's {debug.Name} are both running: name one with app (run or debug)"),
            _ => throw new InvalidOperationException(WhyNoneRuns()),
        };
    }

    /// <summary>Why no app is there to inspect. The app of a Run or Debug still winding down has ended once its inspector
    /// has gone, though its runner has not yet heard the process end (on Windows the socket closes first).</summary>
    private string WhyNoneRuns()
    {
        LiveApp[] apps = [.. new[] { Session.LiveApp, Session.RunApp }.OfType<LiveApp>()];
        if (apps.FirstOrDefault(a => a.State == LiveAppState.Refused) is { } refused)
        {
            return refused.Problem ?? $"{refused.Name} cannot be inspected";
        }
        LiveApp? launched = (Session.Debug.IsDebugging ? Session.LiveApp : null) ?? (Builds.IsRunning ? Session.RunApp : null);
        if (launched is { State: LiveAppState.Ended })
        {
            return Ended(launched);
        }
        if (Builds.IsRunning || Session.Debug.IsDebugging)
        {
            return $"What is running is not {Inspectable}, or Hot Reload runs it";
        }
        return apps.FirstOrDefault() is { } ended
            ? Ended(ended)
            : "No Avalonia or WPF app is running: run or debug_start one (Avalonia 12, or WPF on Windows), or name app ide for Passenger's own windows";

        static string Ended(LiveApp app) => $"{app.Name} has ended: run or debug_start starts it again, or name app ide for Passenger's own windows";
    }

    /// <summary>What Passenger inspects, as a refusal says it.</summary>
    private const string Inspectable = "an app Passenger inspects (an Avalonia 12 app, Avalonia.Base.dll beside it, or a WPF app on Windows)";

    /// <summary>The app's tree, flattened, and the window named (else the app's first, or for Passenger's own the window this
    /// agent works in).</summary>
    private async Task<(List<AppNode> Nodes, AppNode Window)> TreeOf(LiveApp live, string? window, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        List<AppNode> nodes = AppQuery.Flatten((await live.AgentTreeAsync([])).Roots);
        if (nodes.Count == 0)
        {
            throw new InvalidOperationException($"{live.Name} has no window open");
        }
        return (nodes, Window(nodes, window, live));
    }

    private AppNode Window(List<AppNode> nodes, string? named, LiveApp live)
    {
        List<AppNode> windows = [.. nodes.Where(n => n.Parent is null)];
        if (named is not { Length: > 0 })
        {
            // Passenger's own: the window the agent works in, whose title is its own.
            return live.Plan is null && windows.FirstOrDefault(w => w.Element.Text == window.Title) is { } own ? own : windows[0];
        }
        return AppQuery.Find(windows, named).FirstOrDefault(n => n.Parent is null)
            ?? throw new ArgumentException($"No window of {live.Name} is {named}: {string.Join("; ", windows.Select(w => Titled(w.Element)))}");
    }

    /// <summary>A window's elements, the window first.</summary>
    private static List<AppNode> Within(List<AppNode> nodes, AppNode? root) =>
        root is null ? nodes : [.. nodes.Where(n => n == root || n.IsInside(root))];

    /// <summary>What an input acts on: its app, the element named (null for a point, or for whatever has the focus) and the
    /// node whose window it is in.</summary>
    private async Task<(LiveApp Live, AppNode? Element, AppNode Window)> Target(string? app, string? control, string? window, bool needsPoint,
        double? x, double? y, CancellationToken cancel)
    {
        LiveApp live = await InspectedAsync(app, cancel);
        (List<AppNode> nodes, AppNode chosen) = await TreeOf(live, window, cancel);
        if (control is { Length: > 0 } query)
        {
            AppNode element = AppQuery.One(Within(nodes, window is null ? null : chosen), query);
            return (live, element, element);
        }
        if (needsPoint && (x is null || y is null))
        {
            throw new ArgumentException("Name a control, or give x and y in the window");
        }
        return (live, null, chosen);
    }

    private static List<string>? Modifiers(IReadOnlyList<string>? names) => names is null ? null : [.. names.Select(Modifier)];

    /// <summary>A modifier's name as the inspector takes it, or an error naming those there are.</summary>
    private static string Modifier(string name) => name.Trim().ToLowerInvariant() switch
    {
        "ctrl" or "control" => "Control",
        "shift" => "Shift",
        "alt" or "option" or "opt" => "Alt",
        "cmd" or "command" or "meta" or "win" or "super" => "Meta",
        "primary" or "cmdorctrl" => "Primary",
        _ => throw new ArgumentException($"No modifier '{name}': there are ctrl, shift, alt, cmd (meta) and primary"),
    };

    /// <summary>Chords as an agent writes them, "ctrl+shift+S Enter", as Avalonia's keys and modifiers.</summary>
    private static List<Chord> Chords(string keys)
    {
        List<Chord> chords = [];
        foreach (string written in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // The plus key is a chord's last + when it ends in two, or the whole chord.
            string[] parts = written == "+" ? ["+"] : written.EndsWith("++", StringComparison.Ordinal) ? [.. written[..^2].Split('+'), "+"] : written.Split('+');
            List<string> held = [.. parts[..^1].Select(Modifier)];
            chords.Add(new Chord(KeyName(parts[^1]), held));
        }
        return chords.Count > 0 ? chords : throw new ArgumentException("Name the keys to press");
    }

    /// <summary>A key as Avalonia names it, from what an agent writes: Enter, esc, 1, a, PageDown, F5, +.</summary>
    private static string KeyName(string key)
    {
        string name = key.ToLowerInvariant() switch
        {
            "esc" => "Escape",
            "return" => "Enter",
            "backspace" => "Back",
            "del" => "Delete",
            "ins" => "Insert",
            "pgup" => "PageUp",
            "pgdn" => "PageDown",
            "up" => "Up",
            "down" => "Down",
            "left" => "Left",
            "right" => "Right",
            "space" or " " => "Space",
            "+" or "plus" => "OemPlus",
            "-" or "minus" => "OemMinus",
            "," or "comma" => "OemComma",
            "." or "period" => "OemPeriod",
            _ when key.Length == 1 && char.IsAsciiDigit(key[0]) => "D" + key,
            _ => key,
        };
        return Enum.TryParse(name, ignoreCase: true, out Avalonia.Input.Key parsed) && !int.TryParse(name, out _) && parsed != Avalonia.Input.Key.None
            ? parsed.ToString()
            : throw new ArgumentException($"No key '{key}': keys are Avalonia's names (Enter, Escape, Back, Delete, Left, PageDown, F5, A, 1)");
    }

    private static string Id(long id) => "@" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>A window as an answer names it: its type, @id and title.</summary>
    private static string Titled(Element window) => window.Text is { Length: > 0 } title ? $"{AppQuery.Short(window)} \"{title}\"" : AppQuery.Short(window);

    /// <summary>Where a pointer input went, as its answer says it.</summary>
    /// <summary>Input into the app, and what it did; or, when it ran the debugged app into a breakpoint, where that paused it,
    /// said at once rather than the answer waited out, which the app's paused UI gives only once it runs again.</summary>
    private async Task<(InputDone? Done, string? Paused)> SendAsync(LiveApp live, InputAction input, CancellationToken cancel)
    {
        if (live != Session.LiveApp)
        {
            return (await live.InputAsync(input), null);
        }
        using CancellationTokenSource giveUp = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        Task<InputDone> sent = live.InputAsync(input, giveUp.Token);
        await Until(() => sent.IsCompleted || Session.Debug.Session is { State: DebugState.Paused }, LiveApp.AnswerLimit, cancel);
        if (sent.IsCompleted || Session.Debug.Session is not { State: DebugState.Paused })
        {
            return (await sent, null);
        }
        giveUp.Cancel();
        try
        {
            await sent;
        }
        catch (OperationCanceledException)
        {
            // Given up on: its answer comes once the program runs again, to no one.
        }
        await Until(() => Session.Debug.Stop is not null, TimeSpan.FromSeconds(5), cancel);
        DebugSnapshot stop = await DebugSnapshotAsync();
        return (null, $"that paused the program{(stop.Reason is { } why ? $" ({why})" : string.Empty)}{(stop.Location is { } at ? $" at {at}" : string.Empty)} before it was done: "
            + "debug_state shows where, debug_continue lets it go on");
    }

    /// <summary>The element an input went to as the agent named it, or the window.</summary>
    private static string Named(AppNode? element) => element is null ? "the window" : AppQuery.Short(element.Element);

    private static string Aimed(AppNode? element, InputDone done)
    {
        string at = done.X is double x && done.Y is double y ? string.Create(CultureInfo.InvariantCulture, $" at {x:0.#},{y:0.#}") : string.Empty;
        return element is null ? $"window {Id(done.Window)}{at}" : $"{AppQuery.Short(element.Element)}{at} in window {Id(done.Window)}";
    }

    /// <summary>What took a pointer's input, when it is not the element named: what is at a point, or an element behind it.</summary>
    private static string Took(AppNode? element, InputDone done, string what) =>
        done.TargetName is { } name && done.Target != element?.Element.Id ? $"; the {what} went to {name}" : string.Empty;

    private static string Said(InputDone done) =>
        (done.Shows is { } shows ? $"; it shows \"{shows}\"" : string.Empty) + (done.Focused is { } focused ? $"; {focused} has the focus" : "; nothing has the focus");
}

/// <summary>An app's windows, with what the app is: its run configuration's name, its framework ("Avalonia 12.1.4", "WPF
/// 10.0.1"), its Avalonia's version (null for WPF) and its process.</summary>
public sealed record AppWindows(string App, string Framework, string? Avalonia, int? ProcessId, IReadOnlyList<AppWindowInfo> Windows);

public sealed record AppWindowInfo(string Id, string Type, string? Name, string? Title, double Width, double Height, double Scaling, bool Active, bool Visible, string? Owner);

/// <summary>A failed binding as an agent reads it: the app (run, debug or ide), Avalonia's level, the control, its property and
/// the binding's path, the message, its place in the XAML (path:line:column), how many times, and the nearest members.</summary>
public sealed record AppBindingError(string App, string Level, string Control, string Property, string Path, string Message, string? Place, int Count,
    IReadOnlyList<string> Nearest);

/// <summary>An element as app_details reads it, as the Live Visual Tree's details show it: what it is, where its XAML is (path:line:column)
/// or why none, the template it is a part of, its layout, its properties with their sources and its styles with why each applies or not.</summary>
public sealed record AppElementDetails(string Element, string Type, string FullType, string? Name, IReadOnlyList<string> Classes, IReadOnlyList<string> PseudoClasses,
    string? Xaml, string? NoXaml, string? TemplatedParent, AppLayout Layout, IReadOnlyList<AppProperty> Properties, IReadOnlyList<AppStyle> Styles);

/// <summary>Where an element is laid out: its bounds in its parent and in its window (x,y wxh), its desired size, its margin, border and
/// padding (left,top,right,bottom; none when it has no such property) and its alignment.</summary>
public sealed record AppLayout(string Bounds, string? InWindow, string Desired, string Margin, string? Border, string? Padding, string Alignment);

/// <summary>A property's value and type, its source (<see cref="ValueSources"/>) and what that is, where it is written, and whether a
/// property breakpoint (Break When Value Changes) is on it.</summary>
public sealed record AppProperty(string Name, string Value, string Type, string Source, string? Detail, string? Place, bool? Breakpoint);

/// <summary>A style that could apply to an element: its selector, kind, origin and place, whether it applies and why not, its setters.</summary>
public sealed record AppStyle(string Selector, string Kind, string Origin, string? Place, bool Applies, string? WhyNot, IReadOnlyList<AppSetter> Setters);

/// <summary>A style's setter: the property and value, whether it wins, is overridden (by what) or inactive, and where it is written.</summary>
public sealed record AppSetter(string Property, string Value, string State, string? By, string? Place);
