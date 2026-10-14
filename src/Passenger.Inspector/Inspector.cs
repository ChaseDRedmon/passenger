using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Passenger.Inspector;

/// <summary>The inspector's work on the app's UI thread: the windows and their visual trees, an element looked at closely,
/// Select Element and the outline over the element chosen, a word to Passenger as controls come and go, and for an agent
/// the windows' pictures and input to them (<see cref="Pictures"/>, <see cref="Driver"/>). Elements are named by ids that
/// last as long as the element does.</summary>
internal sealed class Inspector
{
    /// <summary>How long the app's controls may come and go before Passenger is told its tree changed, once.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    private readonly Connection connection;
    private readonly ConditionalWeakTable<Visual, object> ids = new();
    private readonly Dictionary<long, WeakReference<Visual>> byId = [];
    private readonly List<IDisposable> handlers = [];
    private readonly Picker picker;
    private readonly Driver driver;
    private long next;
    private bool told;

    /// <summary>Whether Passenger has asked for the tree since it was last told of a change, so it is told only once.</summary>
    private bool watched;

    public Inspector(Connection connection)
    {
        Dispatcher.UIThread.VerifyAccess();
        this.connection = connection;
        picker = new Picker(this, connection);
        driver = new Driver(this);
        handlers.Add(Control.LoadedEvent.AddClassHandler(typeof(Control), (sender, _) => Changed(sender)));
        handlers.Add(Control.UnloadedEvent.AddClassHandler(typeof(Control), (sender, _) => Changed(sender)));
        handlers.Add(Window.WindowOpenedEvent.AddClassHandler(typeof(Window), (sender, _) => Changed(sender)));
        handlers.Add(Window.WindowClosedEvent.AddClassHandler(typeof(Window), (sender, _) => Changed(sender)));
    }

    public Reply Handle(Request request)
    {
        try
        {
            return request.Method switch
            {
                Wire.Tree => new Reply { Id = request.Id, Tree = Tree(request) },
                Wire.Windows => new Reply { Id = request.Id, Windows = Windows() },
                Wire.Details => Find(request.Element) is { } visual
                    ? new Reply { Id = request.Id, Details = new Details(this).Of(visual) }
                    : Gone(request),
                Wire.Pick => Done(request, () => picker.Choose(request.On == true)),
                Wire.Highlight => Done(request, () => picker.Outline(Find(request.Element))),
                Wire.Capture => Find(request.Element) is { } shown
                    ? new Reply { Id = request.Id, Picture = Pictures.Take(shown, request) }
                    : Gone(request),
                Wire.Input => new Reply { Id = request.Id, Done = driver.Do(request.Input ?? throw new Refusal("No input was given")) },
                _ => new Reply { Id = request.Id, Error = $"The inspector does not know '{request.Method}'" },
            };
        }
        catch (Refusal refused)
        {
            return new Reply { Id = request.Id, Error = refused.Message };
        }
        catch (Exception error)
        {
            return new Reply { Id = request.Id, Error = $"{error.GetType().Name}: {error.Message}" };
        }
    }

    /// <summary>Passenger has gone: nothing of the inspector's is left in the app's windows.</summary>
    public void Stop()
    {
        picker.Choose(false);
        picker.Outline(null);
        foreach (IDisposable handler in handlers)
        {
            handler.Dispose();
        }
    }

    private static Reply Gone(Request request) => new() { Id = request.Id, Error = "That element is no longer in the app" };

    private static Reply Done(Request request, Action action)
    {
        action();
        return new Reply { Id = request.Id };
    }

    /// <summary>The id Passenger knows a visual by, the same for as long as it lives.</summary>
    public long IdOf(Visual visual)
    {
        if (ids.TryGetValue(visual, out object? known))
        {
            return (long)known;
        }
        long id = ++next;
        ids.Add(visual, id);
        if (byId.Count > 4096 && byId.Count % 4096 == 0)
        {
            foreach (long dead in byId.Where(e => !e.Value.TryGetTarget(out _)).Select(e => e.Key).ToList())
            {
                byId.Remove(dead);
            }
        }
        byId[id] = new WeakReference<Visual>(visual);
        return id;
    }

    public Visual? Find(long? id) => id is long known && byId.TryGetValue(known, out WeakReference<Visual>? held) && held.TryGetTarget(out Visual? visual) ? visual : null;

    /// <summary>Whether a visual is the inspector's own (Select Element's outline), which the tree leaves out.</summary>
    public bool IsOwn(Visual visual) => picker.IsOwn(visual);

    private AppTree Tree(Request request)
    {
        watched = true;
        told = false;
        Look? look = request.Layout == true || request.Properties is { Count: > 0 }
            ? new Look(request.Layout == true, request.Properties ?? [], Shown.Focused())
            : null;
        return new AppTree([.. TopLevels().Select(top => Node(top, top, look, new Rect(top.ClientSize)))]);
    }

    /// <summary>What an agent's tree carries beyond each element's identity: layout and text, and property values.</summary>
    private sealed record Look(bool Layout, IReadOnlyList<string> Properties, IInputElement? Focused);

    /// <summary>The app's top levels as an agent lists them.</summary>
    private List<AppWindow> Windows() => [.. TopLevels().Select(top => new AppWindow(
        IdOf(top),
        TypeName(top.GetType()),
        top.Name,
        (top as Window)?.Title,
        top.ClientSize.Width,
        top.ClientSize.Height,
        top.RenderScaling,
        top is WindowBase { IsActive: true },
        top.IsVisible,
        top is PopupRoot { ParentTopLevel: { } parent } ? IdOf(parent) : null))];

    /// <summary>The app's top levels, each once: its desktop lifetime's windows or its single view's, then every other one
    /// Avalonia is drawing (a popup's, or every window of an app with no lifetime, as Passenger's own screenshot has).</summary>
    public IReadOnlyList<TopLevel> TopLevels()
    {
        List<TopLevel> tops = [];
        switch (Application.Current?.ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                tops.AddRange(desktop.Windows);
                break;
            case ISingleViewApplicationLifetime { MainView: { } view } when TopLevel.GetTopLevel(view) is { } single:
                tops.Add(single);
                break;
        }
        foreach (TopLevel top in Drawn())
        {
            if (!tops.Contains(top))
            {
                tops.Add(top);
            }
        }
        return tops;
    }

    /// <summary>The top levels Avalonia's media context draws: every window and popup shown, which is no public list.</summary>
    internal static List<TopLevel> Drawn()
    {
        try
        {
            Type? context = typeof(Visual).Assembly.GetType("Avalonia.Media.MediaContext");
            object? instance = context?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            return context?.GetField("_topLevels", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance) is IDictionary drawn
                ? [.. drawn.Keys.OfType<TopLevel>()]
                : [];
        }
        catch (Exception error) when (error is TargetInvocationException or MemberAccessException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>A visual and what it holds; with an agent's look, also where it is and whether what clips it (its window, a
    /// scroll viewer) shows any of it, <paramref name="clip"/> being what its ancestors leave shown.</summary>
    private Element Node(Visual visual, TopLevel top, Look? look, Rect clip)
    {
        Rect? box = look is { Layout: true } ? Shown.RectIn(visual, top) : null;
        Rect inside = box is { } own && visual.ClipToBounds ? clip.Intersect(own) : clip;
        List<Element> children = [];
        foreach (Visual child in visual.GetVisualChildren())
        {
            // An adorner layer holding nothing of the app's: the one the outline of the element chosen made.
            if (!IsOwn(child) && !(child is AdornerLayer layer && layer.Children.All(c => IsOwn(c))))
            {
                children.Add(Node(child, top, look, inside));
            }
        }
        StyledElement? styled = visual as StyledElement;
        Element node = new(
            IdOf(visual),
            TypeName(visual.GetType()),
            styled?.Name,
            styled is null ? [] : [.. styled.Classes.Where(c => !c.StartsWith(':'))],
            styled?.TemplatedParent is not null,
            Details.Place(visual) is not null,
            visual.IsVisible,
            children);
        if (look is null)
        {
            return node;
        }
        if (look.Layout)
        {
            Rect shown = box?.Intersect(clip) ?? default;
            node = node with
            {
                Bounds = box is { } at ? Shown.Boxed(at) : null,
                Text = Shown.TextOf(visual),
                Disabled = visual is InputElement { IsEffectivelyEnabled: false } ? true : null,
                Focused = look.Focused == visual ? true : null,
                OutOfView = box is { Width: > 0, Height: > 0 } && (shown.Width <= 0 || shown.Height <= 0) ? true : null,
            };
        }
        return look.Properties.Count == 0 ? node : node with { Values = Shown.Values(visual, look.Properties) };
    }

    /// <summary>A visual as an agent's answer names it: its type and #name (or the text it shows), and the control whose
    /// template made it.</summary>
    public static string Named(Visual visual) =>
        visual is StyledElement { TemplatedParent: Visual parent } ? $"{Shown.Called(visual)} in {Shown.Called(parent)}'s template" : Shown.Called(visual);

    /// <summary>A type as the tree names it: no namespace, no generic arity.</summary>
    public static string TypeName(Type type)
    {
        string name = type.Name;
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }

    /// <summary>A control came or went: Passenger, if it has the tree, hears once that it changed, a moment after the last.</summary>
    private void Changed(object? sender)
    {
        if ((sender is Visual visual && IsOwn(visual)) || !watched || told)
        {
            return;
        }
        told = true;
        DispatcherTimer.RunOnce(() =>
        {
            watched = false;
            connection.Send(new Reply { Event = Wire.TreeChanged });
        }, Settle);
    }
}

/// <summary>Why the inspector will not do what it was asked (the element is disabled, covered, gone), in words an agent
/// reads as they are.</summary>
internal sealed class Refusal(string message) : Exception(message);
