using System.Text.Json.Nodes;
using Passenger.App.State;
using Passenger.Text;

namespace Passenger.App.Debugging;

/// <summary>A line breakpoint (lines from zero). Its <see cref="Status"/> is what the running
/// debugger last said of it, and goes back to unknown when the session ends.</summary>
public sealed class LineBreakpoint(string path, int line)
{
    public string Path { get; internal set; } = path;
    public int Line { get; internal set; } = line;
    public bool Enabled { get; internal set; } = true;
    public string? Condition { get; internal set; }
    public string? HitCondition { get; internal set; }

    /// <summary>Whether a hit stops the program. One that does not is a tracepoint: it logs its message, or
    /// Rider's "Breakpoint reached", and lets the program go on.</summary>
    public bool Suspend { get; internal set; } = true;

    /// <summary>A message written to the Debug window's console on each hit, its {expressions} evaluated there.</summary>
    public string? LogMessage { get; internal set; }

    public BreakpointStatus Status { get; internal set; }

    /// <summary>Its file and line as the Breakpoints window and the popup name it: "Program.cs:7".</summary>
    public string Location => $"{System.IO.Path.GetFileName(Path)}:{Line + 1}";

    /// <summary>The debugger's word on a breakpoint that will not be hit ("no symbols loaded").</summary>
    public string? Message { get; internal set; }
}

public enum BreakpointStatus
{
    /// <summary>No debugger has said, or none is running.</summary>
    Unknown,
    Bound,

    /// <summary>Refused: it will not stop the program (a hit count the debugger cannot read, a line written since the
    /// build), and its <see cref="LineBreakpoint.Message"/> says why.</summary>
    Invalid,
}

/// <summary>A folder's breakpoints as the store keeps them, as Rider keeps them with the project.</summary>
public sealed record BreakpointsState : IStateDocument<BreakpointsState>
{
    public List<KeptBreakpoint> Breakpoints { get; init; } = [];

    public static IReadOnlyList<Action<JsonObject>> Upgrades { get; } = [StateStore.Unversioned];

    /// <summary>One without a file is left out, and a line before the first is the first.</summary>
    public BreakpointsState Clamped() => this with
    {
        Breakpoints = [.. (Breakpoints ?? []).Where(b => b is { Path.Length: > 0 }).Select(b => b with { Line = Math.Max(0, b.Line) })],
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

/// <summary>Every line breakpoint, by file. A breakpoint in an open file moves with the text around
/// it, as Rider's do, so an edit above it leaves it on its line; one whose line is deleted lands where
/// the deletion did, and two on one line become one.</summary>
public sealed class BreakpointStore
{
    private readonly Dictionary<string, List<LineBreakpoint>> byPath = new(StringComparer.Ordinal);

    /// <summary>A file's breakpoints changed: added, removed, moved, or the debugger said something.</summary>
    public event EventHandler<string>? Changed;

    public IEnumerable<string> Files => byPath.Where(p => p.Value.Count > 0).Select(p => p.Key);

    public IEnumerable<LineBreakpoint> All => byPath.Values.SelectMany(b => b);

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
    };

    /// <summary>Sets the breakpoints a folder kept, beside those there are; a line with one already keeps it.</summary>
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
    }

    /// <summary>Takes every breakpoint away, as another folder opens: each folder's are its own.</summary>
    public void Clear()
    {
        foreach (string path in Files.ToList())
        {
            byPath[path].Clear();
            Changed?.Invoke(this, path);
        }
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

    public void SetCondition(LineBreakpoint breakpoint, string? condition, string? hitCondition) =>
        Update(breakpoint, breakpoint.Enabled, breakpoint.Suspend, condition, hitCondition, breakpoint.LogMessage);

    /// <summary>Changes what a breakpoint does, as its popup or the Breakpoints window does; blank text is none.</summary>
    public void Update(LineBreakpoint breakpoint, bool enabled, bool suspend, string? condition, string? hitCondition, string? logMessage)
    {
        breakpoint.Enabled = enabled;
        breakpoint.Suspend = suspend;
        breakpoint.Condition = string.IsNullOrWhiteSpace(condition) ? null : condition.Trim();
        breakpoint.HitCondition = string.IsNullOrWhiteSpace(hitCondition) ? null : hitCondition.Trim();
        breakpoint.LogMessage = string.IsNullOrWhiteSpace(logMessage) ? null : logMessage;
        Changed?.Invoke(this, breakpoint.Path);
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

    public void Remove(LineBreakpoint breakpoint)
    {
        if (For(breakpoint.Path).Remove(breakpoint))
        {
            Changed?.Invoke(this, breakpoint.Path);
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
    }

    /// <summary>Carries the breakpoints of a file, or of every file in a folder, to where it moved: the debugger hears
    /// that the old path has none and the new one has them.</summary>
    public void Move(string from, string to)
    {
        foreach (string path in byPath.Keys.Where(p => Workspace.PathNames.Within(p, from)).ToList())
        {
            string moved = Workspace.PathNames.Moved(path, from, to);
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
