using System.Text.Json.Nodes;
using Passenger.App.State;
using Passenger.Core;
using Passenger.Dap;
using Passenger.Text;

namespace Passenger.App.Debugging;

/// <summary>What every breakpoint has, as Rider's Breakpoints window edits it: on or off, whether it stops, a condition, a
/// hit count and a message to log. Its <see cref="Status"/> is what the running debugger last said of it, and goes back to
/// unknown when the session ends.</summary>
public abstract class Breakpoint
{
    public bool Enabled { get; internal set; } = true;
    public string? Condition { get; internal set; }
    public string? HitCondition { get; internal set; }

    /// <summary>Whether a hit stops the program. One that does not is a tracepoint: it logs its message, or
    /// Rider's "Breakpoint reached", and lets the program go on.</summary>
    public bool Suspend { get; internal set; } = true;

    /// <summary>A message written to the Debug window's console on each hit, its {expressions} evaluated there.</summary>
    public string? LogMessage { get; internal set; }

    public BreakpointStatus Status { get; internal set; }

    /// <summary>The debugger's word on a breakpoint that will not be hit ("no symbols loaded").</summary>
    public string? Message { get; internal set; }

    /// <summary>How the Breakpoints window and the popup name it: "Program.cs:7", "Any exception".</summary>
    public abstract string Location { get; }
}

/// <summary>A line breakpoint (lines from zero); in Avalonia XAML, on one attribute of the line when it names one
/// (<see cref="XamlBreakpoints"/>), as Rider's inline breakpoints are on one lambda of theirs.</summary>
public sealed class LineBreakpoint(string path, int line, string? attribute = null) : Breakpoint
{
    public string Path { get; internal set; } = path;
    public int Line { get; internal set; } = line;

    /// <summary>The XAML attribute it is on, by its name as written, or null for its whole line.</summary>
    public string? Attribute { get; } = attribute;

    public override string Location => $"{System.IO.Path.GetFileName(Path)}:{Line + 1}{(Attribute is { } on ? $" {on}" : string.Empty)}";
}

/// <summary>Rider's CLR exception breakpoint: an exception type by its full name, or Rider's "Any exception", which every
/// folder has and none removes, and the scenarios it stops in. Rider's "Any exception" stops on what the user's code
/// leaves unhandled; a breakpoint on a type added since stops wherever it is thrown.</summary>
public sealed class ExceptionBreakpoint(string? type) : Breakpoint
{
    /// <summary>Rider's name for the breakpoint on every exception.</summary>
    public const string AnyException = "Any exception";

    /// <summary>The scenarios of Rider's "Any exception": thrown anywhere, and handled outside the user's code or not at all.</summary>
    public const ExceptionScenarios AnyExceptionScenarios = ExceptionScenarios.Thrown | ExceptionScenarios.HandledInExternalCode | ExceptionScenarios.Unhandled;

    /// <summary>The type's full name, or null for every exception.</summary>
    public string? Type { get; } = type;

    public bool IsAny => Type is null;

    public ExceptionScenarios Scenarios { get; internal set; } = type is null ? AnyExceptionScenarios : ExceptionScenarios.All;

    public override string Location => Type ?? AnyException;
}

/// <summary>Rider's data breakpoint (Set Data Breakpoint on a member in Variables): a field, an auto-property or an array
/// element of one object, watched for a write that changes it. It lasts as long as its session, as Rider's do, the object
/// being that session's alone, so no folder keeps it.</summary>
public sealed class DataBreakpoint(string dataId, string location) : Breakpoint
{
    /// <summary>What the debugger named the data watched by.</summary>
    public string DataId { get; } = dataId;

    /// <summary>The member, and the object it is of with its type: "count on counter (Counter)", Rider's words.</summary>
    public override string Location { get; } = location;
}

/// <summary>A property breakpoint, the Live Visual Tree's Break When Value Changes: an Avalonia property of one element of
/// the app being debugged, which stops the app in the code that changes it (a style's, a binding's, code-behind's, an
/// animation's), as a data breakpoint does for a field. It lasts as long as its session, as data breakpoints do, the
/// element being that run's alone. Its condition is C# over the change: <c>newValue</c>, <c>oldValue</c>, <c>control</c>.</summary>
public sealed class PropertyBreakpoint(int id, long element, string described, string property) : Breakpoint
{
    /// <summary>Its number in the session, which the inspector in the app and the debugger's condition name it by.</summary>
    public int Id { get; } = id;

    /// <summary>The element's id in the Live Visual Tree.</summary>
    public long Element { get; } = element;

    /// <summary>The property, by the name the element's details list it under (Owner.Name when attached).</summary>
    public string Property { get; } = property;

    /// <summary>"Background on Button#Save".</summary>
    public override string Location { get; } = $"{property} on {described}";
}

public enum BreakpointStatus
{
    /// <summary>No debugger has said, or none is running.</summary>
    Unknown,
    Bound,

    /// <summary>Refused: it will not stop the program (a hit count the debugger cannot read, a line written since the
    /// build), and its <see cref="Breakpoint.Message"/> says why.</summary>
    Invalid,
}

/// <summary>A folder's breakpoints as the store keeps them, as Rider keeps them with the project.</summary>
public sealed record BreakpointsState : IStateDocument<BreakpointsState>
{
    public List<KeptBreakpoint> Breakpoints { get; init; } = [];

    /// <summary>The CLR exception breakpoints, "Any exception" (no type) among them; none kept is "Any exception" as Rider has it.</summary>
    public List<KeptExceptionBreakpoint>? Exceptions { get; init; }

    public static IReadOnlyList<Action<JsonObject>> Upgrades { get; } = [StateStore.Unversioned];

    /// <summary>One without a file is left out, and a line before the first is the first; an exception type kept twice is
    /// kept once.</summary>
    public BreakpointsState Clamped() => this with
    {
        Breakpoints = [.. (Breakpoints ?? []).Where(b => b is { Path.Length: > 0 }).Select(b => b with { Line = Math.Max(0, b.Line) })],
        Exceptions = Exceptions is null ? null : [.. Exceptions.Where(e => e is not null && e.Type is null or { Length: > 0 }).DistinctBy(e => e.Type ?? string.Empty)],
    };
}

/// <summary>A breakpoint as kept: its file, its line from zero (and XAML attribute) and what it does, but nothing a
/// debugger said of it.</summary>
public sealed record KeptBreakpoint(string Path, int Line)
{
    public string? Attribute { get; init; }

    public bool Enabled { get; init; } = true;
    public bool Suspend { get; init; } = true;
    public string? Condition { get; init; }
    public string? HitCondition { get; init; }
    public string? LogMessage { get; init; }
}

/// <summary>An exception breakpoint as kept: its type (none for "Any exception"), what it does and Rider's "Suspend If".</summary>
public sealed record KeptExceptionBreakpoint(string? Type)
{
    public bool Enabled { get; init; } = true;
    public bool Suspend { get; init; } = true;
    public string? Condition { get; init; }
    public string? HitCondition { get; init; }
    public string? LogMessage { get; init; }
    public bool ThrownInUserCode { get; init; } = true;
    public bool ThrownInExternalCode { get; init; } = true;
    public bool HandledInUserCode { get; init; } = true;
    public bool HandledInExternalCode { get; init; } = true;
    public bool Unhandled { get; init; } = true;
}

/// <summary>Every breakpoint: the line breakpoints by file, the CLR exception breakpoints and the session's data breakpoints.
/// A breakpoint in an open file moves with the text around it, as Rider's do, so an edit above it leaves it on its line;
/// one whose line is deleted lands where the deletion did, and two on one line become one. Muted, none of them stops the
/// program, as Rider's Mute Breakpoints has it, though each keeps whether it is on.</summary>
public sealed class BreakpointStore
{
    private readonly Dictionary<string, List<LineBreakpoint>> byPath = new(StringComparer.Ordinal);
    private readonly List<ExceptionBreakpoint> exceptions = [new(null)];
    private readonly List<DataBreakpoint> data = [];
    private readonly List<PropertyBreakpoint> properties = [];
    private int lastProperty;

    /// <summary>A file's breakpoints changed: added, removed, moved, muted, or the debugger said something.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>The property breakpoints changed: added, removed, muted, what one does, or what the debugger said.</summary>
    public event EventHandler? PropertiesChanged;

    /// <summary>The property breakpoints of the session, in the order they were set.</summary>
    public IReadOnlyList<PropertyBreakpoint> Properties => properties;

    /// <summary>The exception breakpoints changed: added, removed, muted, what one does, or what the debugger said.</summary>
    public event EventHandler? ExceptionsChanged;

    /// <summary>The data breakpoints changed: added, removed, muted, what one does, or what the debugger said.</summary>
    public event EventHandler? DataChanged;

    public IEnumerable<string> Files => byPath.Where(p => p.Value.Count > 0).Select(p => p.Key);

    /// <summary>Every line breakpoint.</summary>
    public IEnumerable<LineBreakpoint> All => byPath.Values.SelectMany(b => b);

    /// <summary>The exception breakpoints: "Any exception" first, then the types in the order they were added.</summary>
    public IReadOnlyList<ExceptionBreakpoint> Exceptions => exceptions;

    /// <summary>The data breakpoints of the session, in the order they were set.</summary>
    public IReadOnlyList<DataBreakpoint> Data => data;

    /// <summary>Rider's Mute Breakpoints: none stops the program, nor logs, until they are unmuted.</summary>
    public bool Muted { get; private set; }

    public IReadOnlyList<LineBreakpoint> In(string path) => byPath.TryGetValue(path, out List<LineBreakpoint>? found) ? found : [];

    /// <summary>Every breakpoint as the folder keeps it.</summary>
    public BreakpointsState Capture() => new()
    {
        Breakpoints = [.. All.Select(b => new KeptBreakpoint(b.Path, b.Line)
        {
            Attribute = b.Attribute,
            Enabled = b.Enabled,
            Suspend = b.Suspend,
            Condition = b.Condition,
            HitCondition = b.HitCondition,
            LogMessage = b.LogMessage,
        })],
        Exceptions = [.. exceptions.Select(e => new KeptExceptionBreakpoint(e.Type)
        {
            Enabled = e.Enabled,
            Suspend = e.Suspend,
            Condition = e.Condition,
            HitCondition = e.HitCondition,
            LogMessage = e.LogMessage,
            ThrownInUserCode = e.Scenarios.HasFlag(ExceptionScenarios.ThrownInUserCode),
            ThrownInExternalCode = e.Scenarios.HasFlag(ExceptionScenarios.ThrownInExternalCode),
            HandledInUserCode = e.Scenarios.HasFlag(ExceptionScenarios.HandledInUserCode),
            HandledInExternalCode = e.Scenarios.HasFlag(ExceptionScenarios.HandledInExternalCode),
            Unhandled = e.Scenarios.HasFlag(ExceptionScenarios.Unhandled),
        })],
    };

    /// <summary>Sets the breakpoints a folder kept, beside those there are; a line with one already keeps it. The exception
    /// breakpoints are the folder's own.</summary>
    public void Restore(BreakpointsState kept)
    {
        foreach (IGrouping<string, KeptBreakpoint> file in kept.Breakpoints.GroupBy(b => b.Path))
        {
            List<LineBreakpoint> list = For(file.Key);
            foreach (KeptBreakpoint breakpoint in file.Where(b => list.All(there => there.Line != b.Line || there.Attribute != b.Attribute)))
            {
                list.Add(new LineBreakpoint(breakpoint.Path, breakpoint.Line, breakpoint.Attribute)
                {
                    Enabled = breakpoint.Enabled,
                    Suspend = breakpoint.Suspend,
                    Condition = breakpoint.Condition,
                    HitCondition = breakpoint.HitCondition,
                    LogMessage = breakpoint.LogMessage,
                });
            }
            Sort(list);
            Changed?.Invoke(this, file.Key);
        }
        if (kept.Exceptions is { } keptExceptions)
        {
            exceptions.Clear();
            exceptions.Add(new ExceptionBreakpoint(null));
            foreach (KeptExceptionBreakpoint breakpoint in keptExceptions)
            {
                ExceptionBreakpoint restored = breakpoint.Type is null ? exceptions[0] : new ExceptionBreakpoint(breakpoint.Type);
                restored.Enabled = breakpoint.Enabled;
                restored.Suspend = breakpoint.Suspend;
                restored.Condition = breakpoint.Condition;
                restored.HitCondition = breakpoint.HitCondition;
                restored.LogMessage = breakpoint.LogMessage;
                restored.Scenarios = (breakpoint.ThrownInUserCode ? ExceptionScenarios.ThrownInUserCode : 0)
                    | (breakpoint.ThrownInExternalCode ? ExceptionScenarios.ThrownInExternalCode : 0)
                    | (breakpoint.HandledInUserCode ? ExceptionScenarios.HandledInUserCode : 0)
                    | (breakpoint.HandledInExternalCode ? ExceptionScenarios.HandledInExternalCode : 0)
                    | (breakpoint.Unhandled ? ExceptionScenarios.Unhandled : 0);
                if (!restored.IsAny)
                {
                    exceptions.Add(restored);
                }
            }
            ExceptionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Takes every breakpoint away, as another folder opens: each folder's are its own, "Any exception" as Rider
    /// sets it until the folder says otherwise, and none muted.</summary>
    public void Clear()
    {
        Muted = false;
        foreach (string path in Files.ToList())
        {
            byPath[path].Clear();
            Changed?.Invoke(this, path);
        }
        exceptions.Clear();
        exceptions.Add(new ExceptionBreakpoint(null));
        ExceptionsChanged?.Invoke(this, EventArgs.Empty);
        ClearData();
    }

    /// <summary>Mutes every breakpoint, or unmutes them, as Rider's Mute Breakpoints toggles.</summary>
    public void SetMuted(bool muted)
    {
        if (Muted == muted)
        {
            return;
        }
        Muted = muted;
        foreach (string path in Files.ToList())
        {
            Changed?.Invoke(this, path);
        }
        ExceptionsChanged?.Invoke(this, EventArgs.Empty);
        DataChanged?.Invoke(this, EventArgs.Empty);
        PropertiesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The data breakpoint on what the debugger names by a data id, set as Rider sets one, to stop at every write
    /// that changes it; the one there is already when there is.</summary>
    public DataBreakpoint AddData(string dataId, string location)
    {
        if (FindData(dataId) is { } existing)
        {
            return existing;
        }
        DataBreakpoint added = new(dataId, location);
        data.Add(added);
        DataChanged?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>The data breakpoint on what the debugger names by a data id, if one is set.</summary>
    public DataBreakpoint? FindData(string dataId) => data.FirstOrDefault(d => d.DataId == dataId);

    /// <summary>Lets the data and property breakpoints go, as their session ends.</summary>
    public void ClearData()
    {
        if (data.Count > 0)
        {
            data.Clear();
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        if (properties.Count > 0)
        {
            properties.Clear();
            PropertiesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The property breakpoint on a property of an element of the app (by its Live Visual Tree id), set to stop at
    /// every change; the one there is already when there is.</summary>
    public PropertyBreakpoint AddProperty(long element, string described, string property)
    {
        if (FindProperty(element, property) is { } existing)
        {
            return existing;
        }
        PropertyBreakpoint added = new(++lastProperty, element, described, property);
        properties.Add(added);
        PropertiesChanged?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>The property breakpoint on a property of an element, if one is set.</summary>
    public PropertyBreakpoint? FindProperty(long element, string property) =>
        properties.FirstOrDefault(p => p.Element == element && p.Property == property);

    /// <summary>Records what the debugger said of the property breakpoints; those it was not asked of (off, or muted) go
    /// back to unknown.</summary>
    public void ReportProperties(IReadOnlyList<(PropertyBreakpoint Breakpoint, BreakpointStatus Status, string? Message)> reports)
    {
        foreach (PropertyBreakpoint breakpoint in properties)
        {
            (breakpoint.Status, breakpoint.Message) = reports.FirstOrDefault(r => ReferenceEquals(r.Breakpoint, breakpoint)) is { Breakpoint: not null } said
                ? (said.Status, said.Message)
                : (BreakpointStatus.Unknown, null);
        }
        PropertiesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets a breakpoint on a line, or removes those there (its XAML attributes' too, as a click in Rider's gutter
    /// takes a line's inline breakpoints away).</summary>
    public void Toggle(string path, int line)
    {
        List<LineBreakpoint> list = For(path);
        if (list.RemoveAll(b => b.Line == line) == 0)
        {
            list.Add(new LineBreakpoint(path, line));
            Sort(list);
        }
        Changed?.Invoke(this, path);
    }

    /// <summary>Sets a breakpoint on a XAML attribute of a line, or removes the one there; the line's own and its other
    /// attributes' stay.</summary>
    public void ToggleAttribute(string path, int line, string attribute)
    {
        List<LineBreakpoint> list = For(path);
        if (list.FirstOrDefault(b => b.Line == line && b.Attribute == attribute) is { } existing)
        {
            list.Remove(existing);
        }
        else
        {
            list.Add(new LineBreakpoint(path, line, attribute));
            Sort(list);
        }
        Changed?.Invoke(this, path);
    }

    /// <summary>The breakpoint on a line, or on a XAML attribute of it, set if there is none.</summary>
    public LineBreakpoint Set(string path, int line, string? attribute = null)
    {
        List<LineBreakpoint> list = For(path);
        if (list.FirstOrDefault(b => b.Line == line && b.Attribute == attribute) is { } existing)
        {
            return existing;
        }
        LineBreakpoint added = new(path, line, attribute);
        list.Add(added);
        Sort(list);
        Changed?.Invoke(this, path);
        return added;
    }

    /// <summary>A file's breakpoints in order: by line, a line's own before its attributes'.</summary>
    private static void Sort(List<LineBreakpoint> list) =>
        list.Sort((a, b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : (a.Attribute is null ? 0 : 1).CompareTo(b.Attribute is null ? 0 : 1));

    /// <summary>Turns a line's breakpoint off or back on, keeping it; sets one if there is none.</summary>
    public void ToggleEnabled(string path, int line)
    {
        if (In(path).FirstOrDefault(b => b.Line == line) is { } existing)
        {
            existing.Enabled = !existing.Enabled;
            Changed?.Invoke(this, path);
        }
        else
        {
            Toggle(path, line);
        }
    }

    /// <summary>The breakpoint on an exception type (a full name, or "Any exception"), set as Rider sets a new one, to stop
    /// wherever the exception is thrown; the one there is already when there is.</summary>
    public ExceptionBreakpoint AddException(string type)
    {
        string name = type.Trim();
        if (FindException(name) is { } existing)
        {
            return existing;
        }
        ExceptionBreakpoint added = new(name);
        exceptions.Add(added);
        ExceptionsChanged?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>The breakpoint on an exception type by its full name, or "Any exception".</summary>
    public ExceptionBreakpoint? FindException(string type) => type.Trim() == ExceptionBreakpoint.AnyException
        ? exceptions[0]
        : exceptions.FirstOrDefault(e => string.Equals(e.Type, type.Trim(), StringComparison.Ordinal));

    /// <summary>Which of Rider's scenarios an exception breakpoint stops in.</summary>
    public void SetScenarios(ExceptionBreakpoint breakpoint, ExceptionScenarios scenarios)
    {
        if (breakpoint.Scenarios != scenarios)
        {
            breakpoint.Scenarios = scenarios;
            ExceptionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetCondition(LineBreakpoint breakpoint, string? condition, string? hitCondition) =>
        Update(breakpoint, breakpoint.Enabled, breakpoint.Suspend, condition, hitCondition, breakpoint.LogMessage);

    /// <summary>Changes what a breakpoint does, as its popup or the Breakpoints window does; blank text is none.</summary>
    public void Update(Breakpoint breakpoint, bool enabled, bool suspend, string? condition, string? hitCondition, string? logMessage)
    {
        breakpoint.Enabled = enabled;
        breakpoint.Suspend = suspend;
        breakpoint.Condition = string.IsNullOrWhiteSpace(condition) ? null : condition.Trim();
        breakpoint.HitCondition = string.IsNullOrWhiteSpace(hitCondition) ? null : hitCondition.Trim();
        breakpoint.LogMessage = string.IsNullOrWhiteSpace(logMessage) ? null : logMessage;
        Raise(breakpoint);
    }

    /// <summary>Sets a breakpoint that does not stop, as Shift+click in the gutter does in Rider; or removes the
    /// one on that line.</summary>
    public void ToggleTracepoint(string path, int line)
    {
        if (In(path).Any(b => b.Line == line))
        {
            Toggle(path, line);
            return;
        }
        LineBreakpoint tracepoint = new(path, line) { Suspend = false };
        List<LineBreakpoint> list = For(path);
        list.Add(tracepoint);
        Sort(list);
        Changed?.Invoke(this, path);
    }

    /// <summary>Removes a breakpoint; "Any exception" stays, as in Rider, turned off by its box instead.</summary>
    public void Remove(Breakpoint breakpoint)
    {
        if (breakpoint is LineBreakpoint line && For(line.Path).Remove(line))
        {
            Changed?.Invoke(this, line.Path);
        }
        else if (breakpoint is ExceptionBreakpoint { IsAny: false } exception && exceptions.Remove(exception))
        {
            ExceptionsChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (breakpoint is DataBreakpoint watched && data.Remove(watched))
        {
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (breakpoint is PropertyBreakpoint property && properties.Remove(property))
        {
            PropertiesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Records what the debugger said of a file's breakpoints, in the order they were sent.</summary>
    public void Report(string path, IReadOnlyList<(BreakpointStatus Status, string? Message, int? Line)> reports, IReadOnlyList<LineBreakpoint> sent)
    {
        for (int i = 0; i < Math.Min(reports.Count, sent.Count); i++)
        {
            sent[i].Status = reports[i].Status;
            sent[i].Message = reports[i].Message;
        }
        Changed?.Invoke(this, path);
    }

    /// <summary>Records what the debugger said of the exception breakpoints sent, in the order they were sent; those not
    /// sent (off, or muted) go back to unknown.</summary>
    public void ReportExceptions(IReadOnlyList<(BreakpointStatus Status, string? Message)> reports, IReadOnlyList<ExceptionBreakpoint> sent)
    {
        foreach (ExceptionBreakpoint breakpoint in exceptions)
        {
            int at = IndexOf(sent, breakpoint);
            (breakpoint.Status, breakpoint.Message) = at >= 0 && at < reports.Count ? reports[at] : (BreakpointStatus.Unknown, null);
        }
        ExceptionsChanged?.Invoke(this, EventArgs.Empty);

        static int IndexOf(IReadOnlyList<ExceptionBreakpoint> list, ExceptionBreakpoint item)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], item))
                {
                    return i;
                }
            }
            return -1;
        }
    }

    /// <summary>Records what the debugger said of the data breakpoints sent, in the order they were sent; those not sent (off,
    /// or muted) go back to unknown.</summary>
    public void ReportData(IReadOnlyList<(BreakpointStatus Status, string? Message)> reports, IReadOnlyList<DataBreakpoint> sent)
    {
        foreach (DataBreakpoint breakpoint in data)
        {
            int at = -1;
            for (int i = 0; i < sent.Count; i++)
            {
                if (ReferenceEquals(sent[i], breakpoint))
                {
                    at = i;
                    break;
                }
            }
            (breakpoint.Status, breakpoint.Message) = at >= 0 && at < reports.Count ? reports[at] : (BreakpointStatus.Unknown, null);
        }
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forgets what the last debugger said, when its session ends.</summary>
    public void ResetStatus()
    {
        foreach ((string path, List<LineBreakpoint> list) in byPath)
        {
            if (list.Any(b => b.Status != BreakpointStatus.Unknown))
            {
                foreach (LineBreakpoint breakpoint in list)
                {
                    breakpoint.Status = BreakpointStatus.Unknown;
                    breakpoint.Message = null;
                }
                Changed?.Invoke(this, path);
            }
        }
        if (exceptions.Any(e => e.Status != BreakpointStatus.Unknown))
        {
            foreach (ExceptionBreakpoint breakpoint in exceptions)
            {
                breakpoint.Status = BreakpointStatus.Unknown;
                breakpoint.Message = null;
            }
            ExceptionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Carries the breakpoints of a file, or of every file in a folder, to where it moved: the debugger hears
    /// that the old path has none and the new one has them.</summary>
    public void Move(string from, string to)
    {
        foreach (string path in byPath.Keys.Where(p => PathNames.Within(p, from)).ToList())
        {
            string moved = PathNames.Moved(path, from, to);
            List<LineBreakpoint> list = byPath[path];
            byPath.Remove(path);
            foreach (LineBreakpoint breakpoint in list)
            {
                breakpoint.Path = moved;
            }
            byPath[moved] = list;
            Changed?.Invoke(this, path);
            Changed?.Invoke(this, moved);
        }
    }

    /// <summary>Carries a file's breakpoints through an edit to it.</summary>
    public void Follow(string path, DocumentChangedEventArgs change)
    {
        if (!byPath.TryGetValue(path, out List<LineBreakpoint>? list) || list.Count == 0)
        {
            return;
        }
        bool moved = false;
        foreach (LineBreakpoint breakpoint in list)
        {
            int line = TextEditing.MapLine(change.Before, change.Edits, change.After, breakpoint.Line);
            if (line != breakpoint.Line)
            {
                breakpoint.Line = line;
                moved = true;
            }
        }
        if (moved)
        {
            List<LineBreakpoint> unique = [.. list.GroupBy(b => (b.Line, b.Attribute)).Select(g => g.First())];
            Sort(unique);
            list.Clear();
            list.AddRange(unique);
            Changed?.Invoke(this, path);
        }
    }

    private void Raise(Breakpoint breakpoint)
    {
        if (breakpoint is LineBreakpoint line)
        {
            Changed?.Invoke(this, line.Path);
        }
        else if (breakpoint is DataBreakpoint)
        {
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (breakpoint is PropertyBreakpoint)
        {
            PropertiesChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ExceptionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private List<LineBreakpoint> For(string path)
    {
        if (!byPath.TryGetValue(path, out List<LineBreakpoint>? list))
        {
            list = [];
            byPath[path] = list;
        }
        return list;
    }
}
