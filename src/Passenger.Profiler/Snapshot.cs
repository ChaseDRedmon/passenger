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

/// <summary>A type the program allocated: about how many bytes and objects (the runtime reports an allocation tick
/// each 100 KB or so, naming the type and stack that crossed it), and the stacks that allocated it, the allocating
/// method first and its callers below (dotMemory's back traces), weighed in kilobytes.</summary>
public sealed record AllocatedType(string Name, long Bytes, int Ticks, CallNode BackTraces);

/// <summary>A garbage collection: its number, generation, why, when it began and how long the program was paused.</summary>
public sealed record Collection(int Number, int Generation, string Reason, double AtMs, double PauseMs);

/// <summary>A sample: the thread, when it was taken (milliseconds from the trace's start), and its stack as numbers into
/// the snapshot's table of frames, where it was caught first.</summary>
public readonly record struct Sample(int Thread, double AtMs, int[] Frames);

/// <summary>An allocation tick: when, the type, the bytes it stands for, and the allocating stack, its frame first.</summary>
public readonly record struct AllocationTick(double AtMs, string Type, long Bytes, int[] Frames);

/// <summary>A thread's row of the timeline: its name and when it was sampled.</summary>
public sealed record ThreadLine(int Id, string Name, IReadOnlyList<double> Times);

/// <summary>What a profiling run found, or a stretch of it: per thread, the call tree of its samples; the hot spots across
/// them all; the allocations and collections; and when each thread was sampled, for the timeline. A sample is a thread
/// caught at one stack, once a millisecond, so a count reads as milliseconds. The run's samples are kept, so any
/// interval of it (<see cref="Within"/>) has the same views.</summary>
public sealed class Snapshot
{
    /// <summary>EventPipe's sampler: every managed thread's stack, each millisecond.</summary>
    public const string SampleProvider = "Microsoft-DotNETCore-SampleProfiler";

    /// <summary>The runtime's own events: method loads, which name the frames, and the GC's.</summary>
    public const string RuntimeProvider = "Microsoft-Windows-DotNETRuntime";

    private readonly IReadOnlyList<Sample> samples;
    private readonly IReadOnlyList<(string Method, string Module)> frames;
    private readonly IReadOnlyDictionary<int, string> threadNames;
    private readonly IReadOnlyList<AllocationTick> ticks;
    private readonly IReadOnlyList<Collection> allCollections;

    private Snapshot(IReadOnlyList<Sample> samples, IReadOnlyList<(string Method, string Module)> frames, IReadOnlyDictionary<int, string> threadNames,
        IReadOnlyList<AllocationTick> ticks, IReadOnlyList<Collection> collections, double startMs, double endMs, string trace, Snapshot? whole)
    {
        this.samples = samples;
        this.frames = frames;
        this.threadNames = threadNames;
        this.ticks = ticks;
        allCollections = collections;
        StartMs = startMs;
        EndMs = endMs;
        TracePath = trace;
        Whole = whole ?? this;
        IReadOnlyList<Sample> inside = [.. samples.Where(x => x.AtMs >= startMs && x.AtMs <= endMs)];
        Samples = inside.Count;
        (All, HotSpots, IReadOnlyDictionary<int, CallNode> byId) = Build(inside);
        Threads = All.Ordered;
        Collections = [.. collections.Where(c => c.AtMs >= startMs && c.AtMs <= endMs)];
        Allocations = Allocated([.. ticks.Where(t => t.AtMs >= startMs && t.AtMs <= endMs)]);
        Timeline = [.. inside.GroupBy(x => x.Thread)
            .Select(g => new ThreadLine(g.Key, byId.TryGetValue(g.Key, out CallNode? thread) ? thread.Name : $"Thread {g.Key}", [.. g.Select(x => x.AtMs)]))
            .OrderByDescending(t => t.Times.Count)];
    }

    /// <summary>The whole run this is a stretch of (itself when it is the whole).</summary>
    public Snapshot Whole { get; }

    /// <summary>Where the snapshot begins and ends, in milliseconds from the trace's start.</summary>
    public double StartMs { get; }
    public double EndMs { get; }

    /// <summary>The types allocated, the most bytes first.</summary>
    public IReadOnlyList<AllocatedType> Allocations { get; }

    /// <summary>The garbage collections, in order.</summary>
    public IReadOnlyList<Collection> Collections { get; }

    /// <summary>Every thread under one root, whose total is every sample: what the flame graph spans.</summary>
    public CallNode All { get; }

    /// <summary>A root per thread (named for it), the heaviest first.</summary>
    public IReadOnlyList<CallNode> Threads { get; }

    /// <summary>The methods by their own samples, the heaviest first.</summary>
    public IReadOnlyList<HotSpot> HotSpots { get; }

    /// <summary>Each thread's samples in time, the busiest thread first.</summary>
    public IReadOnlyList<ThreadLine> Timeline { get; }

    public TimeSpan Duration => TimeSpan.FromMilliseconds(Math.Max(0, EndMs - StartMs));

    public int Samples { get; }

    /// <summary>The .nettrace file the run was written to, which other tools (PerfView, speedscope) read too.</summary>
    public string TracePath { get; }

    /// <summary>The same views over a stretch of the run, as selecting an interval on dotTrace's timeline gives.</summary>
    public Snapshot Within(double fromMs, double toMs)
    {
        Snapshot whole = Whole;
        double from = Math.Max(whole.StartMs, Math.Min(fromMs, toMs));
        double to = Math.Min(whole.EndMs, Math.Max(fromMs, toMs));
        return new Snapshot(whole.samples, whole.frames, whole.threadNames, whole.ticks, whole.allCollections, from, to, whole.TracePath, whole);
    }

    /// <summary>When a method was on a sampled stack, first to last, or null when it never was: what the timeline shows
    /// to say where a method ran.</summary>
    public (double From, double To)? Span(string method)
    {
        HashSet<int> matching = [.. Enumerable.Range(0, frames.Count).Where(i => frames[i].Method.Contains(method, StringComparison.Ordinal))];
        double first = double.MaxValue;
        double last = double.MinValue;
        foreach (Sample sample in samples)
        {
            if (sample.AtMs >= StartMs && sample.AtMs <= EndMs && sample.Frames.Any(matching.Contains))
            {
                first = Math.Min(first, sample.AtMs);
                last = Math.Max(last, sample.AtMs);
            }
        }
        return first <= last ? (first, last) : null;
    }

    /// <summary>The call trees (a root per thread under one for all) and hot spots of some samples.</summary>
    private (CallNode All, IReadOnlyList<HotSpot> HotSpots, IReadOnlyDictionary<int, CallNode> Threads) Build(IReadOnlyList<Sample> taken)
    {
        CallNode all = new("All threads", string.Empty, null);
        Dictionary<int, CallNode> threads = [];
        Dictionary<int, (int Self, int Total)> spots = [];
        HashSet<int> counted = [];
        foreach (Sample sample in taken)
        {
            if (!threads.TryGetValue(sample.Thread, out CallNode? node))
            {
                threads[sample.Thread] = node = all.Child(threadNames.GetValueOrDefault(sample.Thread, $"Thread {sample.Thread}"), string.Empty);
            }
            all.Total++;
            node.Total++;
            // From the thread's first frame to where it was caught.
            for (int i = sample.Frames.Length - 1; i >= 0; i--)
            {
                (string method, string module) = frames[sample.Frames[i]];
                node = node.Child(method, module);
                node.Total++;
            }
            node.Self++;
            counted.Clear();
            for (int i = 0; i < sample.Frames.Length; i++)
            {
                int frame = sample.Frames[i];
                (int Self, int Total) spot = spots.GetValueOrDefault(frame);
                spots[frame] = (spot.Self + (i == 0 ? 1 : 0), spot.Total + (counted.Add(frame) ? 1 : 0));
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
        IReadOnlyList<HotSpot> hot = [.. spots.Where(s => s.Value.Self > 0)
            .Select(s => new HotSpot(frames[s.Key].Method, frames[s.Key].Module, s.Value.Self, s.Value.Total))
            .OrderByDescending(s => s.Self)
            .ThenBy(s => s.Name, StringComparer.Ordinal)];
        return (all, hot, threads);
    }

    /// <summary>The types some allocation ticks name, with their back traces.</summary>
    private IReadOnlyList<AllocatedType> Allocated(IReadOnlyList<AllocationTick> taken)
    {
        Dictionary<string, (long Bytes, int Ticks, CallNode Root)> allocated = new(StringComparer.Ordinal);
        foreach (AllocationTick tick in taken)
        {
            (long Bytes, int Ticks, CallNode Root) so = allocated.GetValueOrDefault(tick.Type, (0, 0, new CallNode(tick.Type, string.Empty, null)));
            int kilobytes = (int)Math.Max(1, tick.Bytes / 1024);
            so.Root.Total += kilobytes;
            CallNode at = so.Root;
            foreach (int frame in tick.Frames)
            {
                at = at.Child(frames[frame].Method, frames[frame].Module);
                at.Total += kilobytes;
            }
            at.Self += kilobytes;
            allocated[tick.Type] = (so.Bytes + tick.Bytes, so.Ticks + 1, so.Root);
        }
        return [.. allocated.Select(a => new AllocatedType(a.Key, a.Value.Bytes, a.Value.Ticks, a.Value.Root)).OrderByDescending(a => a.Bytes)];
    }

    /// <summary>A stack's frames from where it was caught to its thread's first, named and numbered into the table:
    /// runs of unnamed native frames as one, and the runtime's GC poll at the leaf (where the sampler stops a busy loop)
    /// left to its caller.</summary>
    private static int[] Frames(TraceCallStack? stack, Dictionary<(string, string), int> table, List<(string Method, string Module)> named)
    {
        List<int> found = [];
        string? previous = null;
        for (TraceCallStack? frame = stack; frame is not null; frame = frame.Caller)
        {
            TraceCodeAddress address = frame.CodeAddress;
            string method = address.FullMethodName is { Length: > 0 } full ? Pretty(full) : $"[{(address.ModuleName is { Length: > 0 } module ? module : "native")}]";
            if (previous == method && method.StartsWith('['))
            {
                continue;
            }
            if (found.Count == 0 && method.Contains("<PollGC>", StringComparison.Ordinal))
            {
                continue;
            }
            (string, string) key = (method, address.ModuleName ?? string.Empty);
            if (!table.TryGetValue(key, out int number))
            {
                table[key] = number = named.Count;
                named.Add(key);
            }
            found.Add(number);
            previous = method;
        }
        return [.. found];
    }

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
    /// method load events, the allocation ticks and the collections.</summary>
    public static Snapshot Read(string nettrace)
    {
        string etlx = TraceLog.CreateFromEventPipeDataFile(nettrace, null, new TraceLogOptions { ContinueOnError = true });
        try
        {
            using TraceLog log = new(etlx);
            Dictionary<(string, string), int> table = [];
            List<(string Method, string Module)> named = [];
            List<Sample> samples = [];
            Dictionary<int, string> threads = [];
            List<AllocationTick> ticks = [];
            List<Collection> collections = [];
            (int Number, int Generation, string Reason, double At)? collecting = null;
            double? suspended = null;
            foreach (TraceEvent data in log.Events)
            {
                if (data.ProviderName == RuntimeProvider)
                {
                    switch (data.EventName)
                    {
                        case "GC/AllocationTick":
                            string type = data.PayloadByName("TypeName") as string is { Length: > 0 } typed ? typed : "(unknown type)";
                            long bytes = Convert.ToInt64(data.PayloadByName("AllocationAmount64") ?? data.PayloadByName("AllocationAmount") ?? 0L, System.Globalization.CultureInfo.InvariantCulture);
                            ticks.Add(new AllocationTick(data.TimeStampRelativeMSec, type, bytes, Frames(data.CallStack(), table, named)));
                            break;
                        case "GC/Start":
                            collecting = (Convert.ToInt32(data.PayloadByName("Count") ?? 0, System.Globalization.CultureInfo.InvariantCulture),
                                Convert.ToInt32(data.PayloadByName("Depth") ?? 0, System.Globalization.CultureInfo.InvariantCulture),
                                data.PayloadByName("Reason")?.ToString() ?? string.Empty,
                                data.TimeStampRelativeMSec);
                            break;
                        case "GC/SuspendEEStart":
                            suspended = data.TimeStampRelativeMSec;
                            break;
                        case "GC/RestartEEStop" when collecting is { } gc:
                            collections.Add(new Collection(gc.Number, gc.Generation, gc.Reason, gc.At, suspended is { } from ? data.TimeStampRelativeMSec - from : 0));
                            collecting = null;
                            suspended = null;
                            break;
                    }
                    continue;
                }
                if (data.ProviderName != SampleProvider || data.CallStack() is not { } stack)
                {
                    continue;
                }
                int threadId = data.ThreadID;
                if (!threads.ContainsKey(threadId))
                {
                    threads[threadId] = data.Thread()?.ThreadInfo is { Length: > 0 } info ? $"Thread {threadId} ({info})" : $"Thread {threadId}";
                }
                samples.Add(new Sample(threadId, data.TimeStampRelativeMSec, Frames(stack, table, named)));
            }
            double first = samples.Count == 0 ? 0 : samples.Min(x => x.AtMs);
            double last = samples.Count == 0 ? 0 : samples.Max(x => x.AtMs);
            return new Snapshot(samples, named, threads, ticks, collections, first, last, nettrace, null);
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
