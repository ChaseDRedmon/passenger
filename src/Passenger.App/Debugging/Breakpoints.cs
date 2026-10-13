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

/// <summary>A line breakpoint (lines from zero).</summary>
public sealed class LineBreakpoint(string path, int line) : Breakpoint
{
    public string Path { get; internal set; } = path;
    public int Line { get; internal set; } = line;

    public override string Location => $"{System.IO.Path.GetFileName(Path)}:{Line + 1}";
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

/// <summary>A breakpoint as kept: its file, its line from zero and what it does, but nothing a debugger said of it.</summary>
public sealed record KeptBreakpoint(string Path, int Line)
{
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

/// <summary>Every breakpoint: the line breakpoints by file, and the CLR exception breakpoints. A breakpoint in an open file
/// moves with the text around it, as Rider's do, so an edit above it leaves it on its line; one whose line is deleted lands
/// where the deletion did, and two on one line become one. Muted, none of them stops the program, as Rider's Mute
/// Breakpoints has it, though each keeps whether it is on.</summary>
public sealed class BreakpointStore
{
    private readonly Dictionary<string, List<LineBreakpoint>> byPath = new(StringComparer.Ordinal);
    private readonly List<ExceptionBreakpoint> exceptions = [new(null)];

    /// <summary>A file's breakpoints changed: added, removed, moved, muted, or the debugger said something.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>The exception breakpoints changed: added, removed, muted, what one does, or what the debugger said.</summary>
    public event EventHandler? ExceptionsChanged;

    public IEnumerable<string> Files => byPath.Where(p => p.Value.Count > 0).Select(p => p.Key);

    /// <summary>Every line breakpoint.</summary>
    public IEnumerable<LineBreakpoint> All => byPath.Values.SelectMany(b => b);

    /// <summary>The exception breakpoints: "Any exception" first, then the types in the order they were added.</summary>
    public IReadOnlyList<ExceptionBreakpoint> Exceptions => exceptions;

    /// <summary>Rider's Mute Breakpoints: none stops the program, nor logs, until they are unmuted.</summary>
    public bool Muted { get; private set; }

    public IReadOnlyList<LineBreakpoint> In(string path) => byPath.TryGetValue(path, out List<LineBreakpoint>? found) ? found : [];

    /// <summary>Every breakpoint as the folder keeps it.</summary>
    public BreakpointsState Capture() => new()
    {
        Breakpoints = [.. All.Select(b => new KeptBreakpoint(b.Path, b.Line)
        {
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
            foreach (KeptBreakpoint breakpoint in file.Where(b => list.All(there => there.Line != b.Line)))
            {
                list.Add(new LineBreakpoint(breakpoint.Path, breakpoint.Line)
                {
                    Enabled = breakpoint.Enabled,
                    Suspend = breakpoint.Suspend,
                    Condition = breakpoint.Condition,
                    HitCondition = breakpoint.HitCondition,
                    LogMessage = breakpoint.LogMessage,
                });
            }
            list.Sort((a, b) => a.Line.CompareTo(b.Line));
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
    }

    /// <summary>Sets a breakpoint on a line, or removes the one there.</summary>
    public void Toggle(string path, int line)
    {
        List<LineBreakpoint> list = For(path);
        if (list.FirstOrDefault(b => b.Line == line) is { } existing)
        {
            list.Remove(existing);
        }
        else
        {
            list.Add(new LineBreakpoint(path, line));
            list.Sort((a, b) => a.Line.CompareTo(b.Line));
        }
        Changed?.Invoke(this, path);
    }

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
        list.Sort((a, b) => a.Line.CompareTo(b.Line));
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
            List<LineBreakpoint> unique = [.. list.GroupBy(b => b.Line).Select(g => g.First()).OrderBy(b => b.Line)];
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
