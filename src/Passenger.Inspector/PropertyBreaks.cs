using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Core;
using Passenger.Dap;

namespace Passenger.Inspector;

/// <summary>Property breakpoints, the Live Visual Tree's Break When Value Changes: an Avalonia property of one element
/// watched for every change Avalonia makes to it, whatever made it (a style, a binding, code, an animation, its template).
/// Each change the breakpoint's hit count lets through calls <see cref="Breaks.Changed"/>, where Passenger's debugger has
/// a function breakpoint per property breakpoint with its condition: the program stops inside the call that set the value,
/// the code that set it below in the stack. Non-user code, so Just My Code shows that code as where it stopped.</summary>
[DebuggerNonUserCode]
internal sealed class PropertyBreaks
{
    private readonly Dictionary<int, Watched> watches = [];

    /// <summary>Watches a property of an element for a breakpoint (its hit count Passenger's own words, "==2", ">3", "%2"),
    /// in place of what the breakpoint watched before.</summary>
    public void Watch(int breakpoint, AvaloniaObject element, string name, string? hitCondition)
    {
        AvaloniaProperty property = Find(element, name) ?? throw new Refusal($"{Inspector.TypeName(element.GetType())} has no property {name}");
        HitCondition? hits = null;
        if (hitCondition is { Length: > 0 })
        {
            hits = HitCondition.TryParse(hitCondition, out HitCondition read, out string? problem) ? read : throw new Refusal(problem);
        }
        Unwatch(breakpoint);
        Watched watch = new(breakpoint, element, property, hits);
        watches[breakpoint] = watch;
        element.PropertyChanged += watch.Changed;
    }

    public void Unwatch(int breakpoint)
    {
        if (watches.Remove(breakpoint, out Watched? watch))
        {
            watch.Element.PropertyChanged -= watch.Changed;
        }
    }

    /// <summary>Passenger has gone: nothing is watched.</summary>
    public void Stop()
    {
        foreach (int breakpoint in watches.Keys.ToList())
        {
            Unwatch(breakpoint);
        }
    }

    /// <summary>An element's Avalonia property by the name the Live Visual Tree lists it under: its own (or one it took
    /// from another type), else an attached one as Owner.Name.</summary>
    private static AvaloniaProperty? Find(AvaloniaObject element, string name)
    {
        int dot = name.LastIndexOf('.');
        return dot < 0
            ? AvaloniaPropertyRegistry.Instance.FindRegistered(element, name)
            : AvaloniaPropertyRegistry.Instance.GetRegisteredAttached(element.GetType())
                .FirstOrDefault(p => p.Name == name[(dot + 1)..] && Inspector.TypeName(p.OwnerType) == name[..dot]);
    }

    [DebuggerNonUserCode]
    private sealed class Watched(int breakpoint, AvaloniaObject element, AvaloniaProperty property, HitCondition? hits)
    {
        private int count;

        public AvaloniaObject Element { get; } = element;

        public void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != property || (hits is { } condition && !condition.IsMet(++count)))
            {
                return;
            }
            string change = $"Previous Value: {Details.Format(e.OldValue)}, New Value: {Details.Format(e.NewValue)}{By(e)}";
            Breaks.Changed(breakpoint, Element, property, e.OldValue, e.NewValue, change);
        }

        /// <summary>What set the value, from the priority Avalonia set it at: a binding (as its XAML writes it), a style
        /// (its selector, where it is written), an animation, the template, inheritance, or the element's local value.</summary>
        private string By(AvaloniaPropertyChangedEventArgs e)
        {
            try
            {
                switch (e.Priority)
                {
                    case BindingPriority.Animation:
                        return ", by an animation";
                    case BindingPriority.Inherited:
                        return ", inherited";
                    case BindingPriority.Template:
                        return ", by its template";
                    case BindingPriority.Style or BindingPriority.StyleTrigger when Element is Visual visual && new Styling(visual).Winner(property) is { } won:
                        return $", by {(won.Source == ValueSources.Theme ? "theme" : "style")} {won.Detail}{(won.Place is { } at ? $" at {Path.GetFileName(at.File)}:{at.Line}" : string.Empty)}";
                    case BindingPriority.Style or BindingPriority.StyleTrigger:
                        return ", by a style";
                    case BindingPriority.LocalValue when BindingOperations.GetBindingExpressionBase(Element, property) is UntypedBindingExpressionBase { Priority: BindingPriority.LocalValue } bound:
                        return $", by its binding {bound.Description}";
                    case BindingPriority.LocalValue:
                        return ", its local value";
                    default:
                        return string.Empty;
                }
            }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                // Read in the middle of Avalonia's own change: what set it goes unsaid rather than stopping the app.
                return string.Empty;
            }
        }
    }
}

/// <summary>Where a property breakpoint stops the program: Passenger's debugger sets a function breakpoint here for each
/// one (<see cref="Wire.Changed"/>), its condition <c>breakpoint == id</c> and the person's own after it, which can read
/// the parameters (<c>newValue</c>, <c>oldValue</c>, <c>control</c>, <c>property</c>); <c>change</c> is what the IDE says of
/// the stop.</summary>
public static class Breaks
{
    [DebuggerNonUserCode]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Changed(int breakpoint, AvaloniaObject control, AvaloniaProperty property, object? oldValue, object? newValue, string change)
    {
        // Held for the debugger, which reads them where the program stops.
        GC.KeepAlive(control);
        GC.KeepAlive(property);
        GC.KeepAlive(oldValue);
        GC.KeepAlive(newValue);
        GC.KeepAlive(change);
        _ = breakpoint;
    }
}
