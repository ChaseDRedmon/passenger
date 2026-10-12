using Passenger.Text;

namespace Passenger.App.Debugging;

/// <summary>A line breakpoint (lines from zero). Its <see cref="Status"/> is what the running
/// debugger last said of it, and goes back to unknown when the session ends.</summary>
public sealed class LineBreakpoint(string path, int line)
{
    public string Path { get; } = path;
    public int Line { get; internal set; } = line;
    public bool Enabled { get; internal set; } = true;
    public string? Condition { get; internal set; }
    public string? HitCondition { get; internal set; }
    public BreakpointStatus Status { get; internal set; }

    /// <summary>The debugger's word on a breakpoint that will not be hit ("no symbols loaded").</summary>
    public string? Message { get; internal set; }
}

public enum BreakpointStatus
{
    /// <summary>No debugger has said, or none is running.</summary>
    Unknown,
    Bound,
    Invalid,
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

    public void SetCondition(LineBreakpoint breakpoint, string? condition, string? hitCondition)
    {
        breakpoint.Condition = string.IsNullOrWhiteSpace(condition) ? null : condition;
        breakpoint.HitCondition = string.IsNullOrWhiteSpace(hitCondition) ? null : hitCondition;
        Changed?.Invoke(this, breakpoint.Path);
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
            int start = change.Before.GetLineStart(Math.Min(breakpoint.Line, change.Before.LineCount - 1));
            // A breakpoint goes with its line's first character, which text inserted before it pushes
            // along: a line broken at its very start takes the breakpoint down with the code.
            int mapped = TextEditing.MapOffset(start, change.Edits, afterInsertions: true);
            int line = change.After.GetLineFromOffset(Math.Min(mapped, change.After.Length));
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
