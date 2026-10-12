using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Passenger.Profiler;

/// <summary>A node of the call tree: a method called along one path from a thread's first frame, with how many samples
/// found the thread in it (Total) and in it and no deeper (Self).</summary>
public sealed class CallNode(string name, string module, CallNode? parent)
{
    private readonly Dictionary<string, CallNode> children = [];

    public string Name { get; internal set; } = name;
    public string Module { get; } = module;
    public CallNode? Parent { get; } = parent;
    public int Total { get; internal set; }
    public int Self { get; internal set; }
    public IReadOnlyCollection<CallNode> Children => children.Values;

    /// <summary>The children, the heaviest first, as the call tree lists them.</summary>
    public IReadOnlyList<CallNode> Ordered => [.. children.Values.OrderByDescending(c => c.Total).ThenBy(c => c.Name, StringComparer.Ordinal)];

    internal CallNode Child(string method, string module)
    {
        if (!children.TryGetValue(method, out CallNode? child))
        {
            children[method] = child = new CallNode(method, module, this);
        }
        return child;
    }
}

/// <summary>A method's share of the run, all threads together: samples in it and no deeper (Self), and samples with it
/// anywhere on the stack, counted once however deep it recurses (Total).</summary>
public sealed record HotSpot(string Name, string Module, int Self, int Total);

/// <summary>What a profiling run found: per thread, the call tree of its samples; the hot spots across them all; and
/// how long it ran. A sample is a thread caught at one stack, once a millisecond, so a count reads as milliseconds.</summary>
public sealed class Snapshot
{
    /// <summary>EventPipe's sampler: every managed thread's stack, each millisecond.</summary>
    public const string SampleProvider = "Microsoft-DotNETCore-SampleProfiler";

    private Snapshot(CallNode all, IReadOnlyList<HotSpot> hotSpots, TimeSpan duration, int samples, string trace)
    {
        All = all;
        Threads = all.Ordered;
        HotSpots = hotSpots;
        Duration = duration;
        Samples = samples;
        TracePath = trace;
    }

    /// <summary>Every thread under one root, whose total is every sample: what the flame graph spans.</summary>
    public CallNode All { get; }

    /// <summary>A root per thread (named for it), the heaviest first.</summary>
    public IReadOnlyList<CallNode> Threads { get; }

    /// <summary>The methods by their own samples, the heaviest first.</summary>
    public IReadOnlyList<HotSpot> HotSpots { get; }

    public TimeSpan Duration { get; }

    public int Samples { get; }

    /// <summary>The .nettrace file the run was written to, which other tools (PerfView, speedscope) read too.</summary>
    public string TracePath { get; }

    /// <summary>Whether a program's entry point is among a thread's first frames (below native ones, the runtime's
    /// CallEntryPoint, or a Main, top-level statements' &lt;Main&gt;$ included).</summary>
    private static bool RunsEntryPoint(CallNode node, int depth) =>
        depth > 0 && node.Children.Any(c =>
            c.Name.StartsWith("System.Environment.CallEntryPoint", StringComparison.Ordinal)
            || c.Name.Contains(".Main(", StringComparison.Ordinal)
            || c.Name.Contains(".<Main>$(", StringComparison.Ordinal)
            || RunsEntryPoint(c, depth - 1));

    /// <summary>A method as dotTrace writes it: its parameters by their types' own names ("Main(String[])"), not IL's
    /// ("Main(class System.String[])").</summary>
    public static string Pretty(string method)
    {
        int open = method.IndexOf('(', StringComparison.Ordinal);
        if (open < 0 || !method.EndsWith(')'))
        {
            return method;
        }
        string[] parameters = method[(open + 1)..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IEnumerable<string> types = parameters.Select(p =>
        {
            string type = p.Replace("value class ", string.Empty, StringComparison.Ordinal).Replace("class ", string.Empty, StringComparison.Ordinal);
            int generic = type.IndexOfAny(['<', '`', '[', '&', '*']);
            string head = generic < 0 ? type : type[..generic];
            string tail = generic < 0 ? string.Empty : type[generic..];
            int dot = head.LastIndexOf('.');
            return (dot < 0 ? head : head[(dot + 1)..]) + tail;
        });
        return $"{method[..open]}({string.Join(", ", types)})";
    }

    /// <summary>Reads a .nettrace file EventPipe wrote: the samples' stacks, with the methods named from the run's own
    /// method load events.</summary>
    public static Snapshot Read(string nettrace)
    {
        string etlx = TraceLog.CreateFromEventPipeDataFile(nettrace, null, new TraceLogOptions { ContinueOnError = true });
        try
        {
            using TraceLog log = new(etlx);
            Dictionary<int, CallNode> threads = [];
            CallNode all = new("All threads", string.Empty, null);
            Dictionary<string, (string Module, int Self, int Total)> spots = new(StringComparer.Ordinal);
            int samples = 0;
            double first = double.MaxValue;
            double last = 0;
            foreach (TraceEvent data in log.Events)
            {
                if (data.ProviderName != SampleProvider || data.CallStack() is not { } stack)
                {
                    continue;
                }
                samples++;
                first = Math.Min(first, data.TimeStampRelativeMSec);
                last = Math.Max(last, data.TimeStampRelativeMSec);
                List<(string Method, string Module)> frames = [];
                for (TraceCallStack? frame = stack; frame is not null; frame = frame.Caller)
                {
                    TraceCodeAddress address = frame.CodeAddress;
                    string method = address.FullMethodName is { Length: > 0 } named ? Pretty(named) : $"[{(address.ModuleName is { Length: > 0 } module ? module : "native")}]";
                    // Runs of unnamed native frames are one frame: what native code called is not the program's.
                    if (frames.Count > 0 && frames[^1].Method == method && method.StartsWith('['))
                    {
                        continue;
                    }
                    // The sampler stops a busy thread at its loop's GC poll; the time is the loop's, not the poll's.
                    if (frames.Count == 0 && method.Contains("<PollGC>", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    frames.Add((method, address.ModuleName ?? string.Empty));
                }
                int threadId = data.ThreadID;
                if (!threads.TryGetValue(threadId, out CallNode? node))
                {
                    TraceThread? thread = data.Thread();
                    string name = thread?.ThreadInfo is { Length: > 0 } info ? $"Thread {threadId} ({info})" : $"Thread {threadId}";
                    threads[threadId] = node = all.Child(name, string.Empty);
                }
                all.Total++;
                node.Total++;
                // From the thread's first frame to where it was caught.
                for (int i = frames.Count - 1; i >= 0; i--)
                {
                    node = node.Child(frames[i].Method, frames[i].Module);
                    node.Total++;
                }
                node.Self++;
                HashSet<string> counted = [];
                for (int i = 0; i < frames.Count; i++)
                {
                    (string method, string module) = frames[i];
                    (string Module, int Self, int Total) spot = spots.GetValueOrDefault(method, (module, 0, 0));
                    spots[method] = (spot.Module, spot.Self + (i == 0 ? 1 : 0), spot.Total + (counted.Add(method) ? 1 : 0));
                }
            }
            // The thread the program's entry point runs on is its main thread, as dotTrace names it.
            foreach (CallNode thread in all.Children)
            {
                if (RunsEntryPoint(thread, 4))
                {
                    thread.Name = $"Main thread ({thread.Name})";
                }
            }
            IReadOnlyList<HotSpot> hot = [.. spots.Select(s => new HotSpot(s.Key, s.Value.Module, s.Value.Self, s.Value.Total))
                .Where(s => s.Self > 0)
                .OrderByDescending(s => s.Self)
                .ThenBy(s => s.Name, StringComparer.Ordinal)];
            TimeSpan duration = samples == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(last - first);
            return new Snapshot(all, hot, duration, samples, nettrace);
        }
        finally
        {
            try
            {
                File.Delete(etlx);
            }
            catch (IOException)
            {
            }
        }
    }
}
