using System.Text.Json.Serialization;

namespace Passenger.Inspector;

// What Passenger and its inspector inside an Avalonia app say to each other, compiled into both: one JSON object a line
// over the socket Passenger names in the app's environment. Passenger asks (Request) and the app answers each by its id
// (Reply); the app also tells without being asked (a Reply with an Event): hello once its UI runs, treeChanged as controls
// come and go, picked when the person points at one, bindingFailed as a binding fails (from before hello, as the app starts).
// An agent's tools ask the same: the windows, the tree with each element's layout and text, a picture the app draws of a
// window, and input to it as a person's goes.

/// <summary>The names both ends agree on.</summary>
public static class Wire
{
    /// <summary>Bumped by a change either end must know of: a hello of another version is refused, saying so.</summary>
    public const int Version = 2;

    /// <summary>The app's environment: the socket's path, where it connects.</summary>
    public const string SocketVariable = "PASSENGER_INSPECTOR";

    /// <summary>The runtime's list of startup hooks, which loads the inspector before the app's Main.</summary>
    public const string StartupHooks = "DOTNET_STARTUP_HOOKS";

    /// <summary>The Avalonia the inspector is built for; an app on another major version is refused, saying which it has.</summary>
    public const int AvaloniaMajor = 12;

    public const string Tree = "tree";
    public const string Details = "details";
    public const string Pick = "pick";
    public const string Highlight = "highlight";
    public const string Windows = "windows";
    public const string Capture = "capture";
    public const string Input = "input";

    public const string Hello = "hello";
    public const string TreeChanged = "treeChanged";
    public const string Picked = "picked";
    public const string PickEnded = "pickEnded";
    public const string BindingFailed = "bindingFailed";
}

/// <summary>What Passenger asks: the windows' tree, an element's details, Select Element on or off, an element outlined in
/// the app (none to take the outline away), the windows alone, a picture of a window (or of the element's part of it), or
/// input to the app.</summary>
public sealed record Request(int Id, string Method, long? Element = null, bool? On = null)
{
    /// <summary>With Tree: each element's place in its window, its text, and whether it is disabled, focused or out of view.</summary>
    public bool? Layout { get; init; }

    /// <summary>With Tree: the values of these properties (Avalonia's, an attached one as Owner.Name, else the CLR's) on each
    /// element that has them.</summary>
    public IReadOnlyList<string>? Properties { get; init; }

    /// <summary>With Capture: pixels a unit; else the window's own scaling, less for a large picture.</summary>
    public double? Scale { get; init; }

    /// <summary>With Capture: the part of the window to draw, in its units; else the element's part, or all of it.</summary>
    public Box? Area { get; init; }

    public InputAction? Input { get; init; }
}

/// <summary>Input to the app as a person gives it: into its window's own way in from the platform, so hit testing, routing,
/// pointer-over, focus, gestures, key bindings and commands all run as they would. Kind is one of <see cref="InputKinds"/>.
/// The pointer acts at the element's centre (or X,Y from its top left), or at X,Y in the window; typing and keys go to the
/// element, focused first, else to whatever has the focus. Keys are chords: an Avalonia Key's name and its modifiers
/// (Control, Shift, Alt, Meta, or Primary for the platform's command key).</summary>
public sealed record InputAction(string Kind)
{
    public long? Window { get; init; }

    public long? Element { get; init; }

    public double? X { get; init; }

    public double? Y { get; init; }

    /// <summary>left, right or middle.</summary>
    public string? Button { get; init; }

    /// <summary>Clicks in a row: two is a double click.</summary>
    public int? Count { get; init; }

    /// <summary>Held through a click or a scroll.</summary>
    public IReadOnlyList<string>? Modifiers { get; init; }

    public string? Text { get; init; }

    /// <summary>With Type: the platform's Select All first, so the text replaces what the element holds.</summary>
    public bool? Replace { get; init; }

    public IReadOnlyList<Chord>? Keys { get; init; }

    /// <summary>With Scroll: notches of the wheel, right and down positive.</summary>
    public double? DeltaX { get; init; }

    public double? DeltaY { get; init; }
}

public static class InputKinds
{
    public const string Click = "click";
    public const string Type = "type";
    public const string Key = "key";
    public const string Scroll = "scroll";
}

/// <summary>A key and the modifiers held with it.</summary>
public sealed record Chord(string Key, IReadOnlyList<string> Modifiers);

/// <summary>What an input did: its window, where in it the pointer acted, the element there (the one that took it, or the
/// focused one for keys), what that element shows after (its text, a scroll viewer's offset), and what has the focus.</summary>
public sealed record InputDone(long Window, double? X, double? Y, long? Target, string? TargetName, string? Shows, string? Focused);

/// <summary>One of the app's top levels: a window, or a popup's (a menu, a tooltip, a drop-down) with the window it opened
/// from as its owner; its size in units and its pixels a unit.</summary>
public sealed record AppWindow(long Id, string Type, string? Name, string? Title, double Width, double Height, double Scaling, bool Active,
    bool Visible, long? Owner);

/// <summary>A picture the app drew of one of its windows (its popups over it), or a part: a PNG, its size in pixels, the
/// pixels a unit, and the part drawn in the window's units.</summary>
public sealed record Picture(byte[] Png, int Width, int Height, double Scale, Box Area);

/// <summary>An answer to a request (its id, with what it asked for or why not), or something the app tells unasked.</summary>
public sealed record Reply
{
    public int? Id { get; init; }

    public string? Event { get; init; }

    public string? Error { get; init; }

    public Hello? Hello { get; init; }

    public AppTree? Tree { get; init; }

    public ElementDetails? Details { get; init; }

    /// <summary>The element pointed at, with Picked.</summary>
    public long? Element { get; init; }

    /// <summary>A binding that failed, with BindingFailed.</summary>
    public BindingFailure? Binding { get; init; }

    public IReadOnlyList<AppWindow>? Windows { get; init; }

    public Picture? Picture { get; init; }

    public InputDone? Done { get; init; }
}

/// <summary>The app's first word: its process, the Avalonia it runs (its assembly's version), and why the inspector cannot
/// serve it when it cannot (another major version of Avalonia, none at all).</summary>
public sealed record Hello(int Protocol, int ProcessId, string? Avalonia, string? Problem);

/// <summary>A binding that failed in the app, as Avalonia logged it (its Binding area, Warning or Error): the control it is
/// on (Type#Name) and the property it sets, its path and the part of the path that failed, Avalonia's message, the control's
/// XAML element when the app's build recorded it, and for a path naming what its source's type does not have, that type (as
/// Avalonia names it), the member and the type's members nearest it. Id names one failure for the run; it comes again as it
/// repeats, Count being how many times in all.</summary>
public sealed record BindingFailure(int Id, string Level, string Control, string Property, string Path, string? ErrorPoint, string Message,
    SourcePlace? Place, string? Type, string? Member, IReadOnlyList<string> Nearest, int Count);

/// <summary>The app's top-level windows (popups among them), each with the visual tree under it.</summary>
public sealed record AppTree(IReadOnlyList<Element> Roots);

/// <summary>A visual in the tree: its id (the same for as long as it lives), type, name, style classes, whether a template
/// made it, whether the app's XAML has it (Avalonia recorded where), whether it shows, and its visual children; asked with
/// Layout, also where it is in its window, its text, and whether it is disabled or focused, and with Properties their values.</summary>
public sealed record Element(long Id, string Type, string? Name, IReadOnlyList<string> Classes, bool FromTemplate, bool Sourced, bool Visible,
    IReadOnlyList<Element> Children)
{
    /// <summary>Its box in its window's units, transforms included.</summary>
    public Box? Bounds { get; init; }

    /// <summary>What it shows as text: a TextBlock's or TextBox's (a password's masked), a string content's or header's, a
    /// window's title.</summary>
    public string? Text { get; init; }

    /// <summary>True when it takes no input (IsEffectivelyEnabled false).</summary>
    public bool? Disabled { get; init; }

    /// <summary>True for the element with the keyboard focus.</summary>
    public bool? Focused { get; init; }

    /// <summary>True when what clips it (its window, a scroll viewer) shows none of it.</summary>
    public bool? OutOfView { get; init; }

    public IReadOnlyDictionary<string, string>? Values { get; init; }
}

/// <summary>A place in a XAML file, as Avalonia records it under a Debug build: the file's full path, the line and column of
/// the element's start, both from one.</summary>
public sealed record SourcePlace(string File, int Line, int Column);

/// <summary>A visual looked at closely: what it is, where its XAML is (or why there is none), its layout, every property with
/// its value and where the value came from, and the styles that apply to it and those that do not, with why.</summary>
public sealed record ElementDetails(
    long Id,
    string Type,
    string FullType,
    string? Name,
    IReadOnlyList<string> Classes,
    IReadOnlyList<string> PseudoClasses,
    SourcePlace? Source,
    string? NoSource,
    string? TemplatedParent,
    LayoutInfo Layout,
    IReadOnlyList<PropertyValue> Properties,
    IReadOnlyList<StyleReport> Styles);

/// <summary>Where a visual is laid out: its bounds in its parent and in its window, its desired size, margin, border and
/// padding (none when it has no such property), and its alignment.</summary>
public sealed record LayoutInfo(Box Bounds, Box? InWindow, double DesiredWidth, double DesiredHeight, Edges Margin, Edges? Border,
    Edges? Padding, string HorizontalAlignment, string VerticalAlignment);

public sealed record Box(double X, double Y, double Width, double Height);

public sealed record Edges(double Left, double Top, double Right, double Bottom);

/// <summary>An Avalonia property's value on a visual and its source (<see cref="ValueSources"/>), with what that source is
/// (the style's selector, the ancestor it is inherited from) and where it is written when Avalonia knows.</summary>
public sealed record PropertyValue(string Name, string Owner, bool Attached, string Type, string Value, string Source, string? Detail,
    SourcePlace? Place);

/// <summary>The sources a value can have, in the words the window shows.</summary>
public static class ValueSources
{
    public const string Local = "Local";
    public const string Animation = "Animation";
    public const string Style = "Style";
    public const string Theme = "Theme";
    public const string Template = "Template";
    public const string Inherited = "Inherited";
    public const string Default = "Default";

    /// <summary>Set by the control itself (SetCurrentValue), over whatever else gave it a value.</summary>
    public const string Current = "Current";

    /// <summary>A direct property: a field of the control's, which code and bindings set and styles seldom do.</summary>
    public const string Direct = "Direct";
}

/// <summary>A style that could apply to a visual: its selector as Avalonia writes it, what kind it is (<see cref="StyleKinds"/>),
/// where it comes from (a file, a theme), where its selector is written, whether it applies and why not, and its setters.</summary>
public sealed record StyleReport(string Selector, string Kind, string Origin, SourcePlace? Place, bool Applies, string? WhyNot,
    IReadOnlyList<SetterReport> Setters);

public static class StyleKinds
{
    /// <summary>The control's control theme, or one it is based on.</summary>
    public const string Theme = "Theme";

    /// <summary>A style nested in the control's control theme.</summary>
    public const string ThemeStyle = "Theme style";

    /// <summary>A style nested in the control theme of the control whose template made this one.</summary>
    public const string TemplatedParentTheme = "Templated parent's theme";

    public const string Style = "Style";
}

/// <summary>A setter of a style: the property, the value it sets, where it is written, and what became of it
/// (<see cref="SetterStates"/>), with what overrides it.</summary>
public sealed record SetterReport(string Property, string Value, SourcePlace? Place, string State, string? By);

public static class SetterStates
{
    /// <summary>Its value is the property's.</summary>
    public const string Wins = "Wins";

    /// <summary>Something before it in Avalonia's order gives the property its value.</summary>
    public const string Overridden = "Overridden";

    /// <summary>Its style does not apply now.</summary>
    public const string Inactive = "Inactive";
}

/// <summary>How the messages are written: camel case, nulls left out, a tree as deep as an app's visual tree goes (two
/// levels a control, where System.Text.Json stops at 64), generated for both ends (an app may turn reflection off for
/// System.Text.Json).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    MaxDepth = 4096)]
[JsonSerializable(typeof(Request))]
[JsonSerializable(typeof(Reply))]
internal sealed partial class WireJson : JsonSerializerContext
{
}
