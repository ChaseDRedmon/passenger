using System.Diagnostics;
using Microsoft.Diagnostics.Runtime;

namespace Passenger.Profiler;

/// <summary>A type on the heap.</summary>
internal sealed class HeapTypeInfo(string name, ulong methodTable)
{
    public string Name { get; } = name;
    public ulong MethodTable { get; } = methodTable;
}

/// <summary>What a dump's heap held, as dotMemory's snapshot: every object reachable from a GC root, in address order, with
/// its references. Read with ClrMD; arrays rather than objects, since a heap has millions.</summary>
internal sealed class HeapGraph
{
    public required ulong[] Address { get; init; }
    public required int[] Type { get; init; }
    public required long[] Size { get; init; }

    /// <summary>The references, each object's from <c>EdgeStart[i]</c> to <c>EdgeStart[i + 1]</c>: the object referred to.</summary>
    public required int[] EdgeStart { get; init; }
    public required int[] EdgeTarget { get; init; }

    public required HeapTypeInfo[] Types { get; init; }

    /// <summary>How long the dump's walk took, and finding what of it is reachable.</summary>
    public TimeSpan Walked { get; private set; }
    public TimeSpan Analysed { get; private set; }

    public int Count => Address.Length;

    /// <summary>The object at an address, or -1.</summary>
    public int IndexOf(ulong address) => Array.BinarySearch(Address, address) is var at and >= 0 ? at : -1;

    /// <summary>Reads a dump of a .NET program's heap. Throws <see cref="HeapUnreadableException"/> when the dump caught the
    /// GC mid-collection, which another dump will not.</summary>
    public static HeapGraph Read(string dump, CancellationToken cancel = default)
    {
        Stopwatch clock = Stopwatch.StartNew();
        using DataTarget target = DataTarget.LoadDump(dump);
        if (target.ClrVersions.Length == 0)
        {
            throw new InvalidOperationException("The dump has no .NET runtime in it");
        }
        using ClrRuntime runtime = target.ClrVersions[0].CreateRuntime();
        ClrHeap heap = runtime.Heap;
        if (!heap.CanWalkHeap)
        {
            throw new HeapUnreadableException();
        }
        Reading reading = new(heap);
        reading.Walk(cancel);
        TimeSpan walked = clock.Elapsed;
        HeapGraph graph = reading.Analyse(cancel);
        graph.Walked = walked;
        graph.Analysed = clock.Elapsed - walked;
        return graph;
    }

    /// <summary>A dump's walk: the objects and references as found, then what the roots reach.</summary>
    private sealed class Reading(ClrHeap heap)
    {
        private readonly Dictionary<ulong, int> typeOf = [];
        private readonly List<HeapTypeInfo> types = [];
        private readonly Growing<ulong> addresses = new();
        private readonly Growing<int> objectTypes = new();
        private readonly Growing<long> sizes = new();
        private readonly Growing<int> edgeStarts = new();
        private readonly Growing<ulong> edgeTargets = new();
        private ulong[] sorted = [];
        private int[] order = [];

        public void Walk(CancellationToken cancel)
        {
            foreach (ClrObject item in heap.EnumerateObjects())
            {
                if (item.IsFree || item.Type is not { } type)
                {
                    continue;
                }
                addresses.Add(item.Address);
                objectTypes.Add(TypeIndex(type));
                sizes.Add((long)item.Size);
                edgeStarts.Add(edgeTargets.Count);
                if (type.ContainsPointers)
                {
                    foreach (ulong reference in item.EnumerateReferenceAddresses(carefully: false, considerDependantHandles: true))
                    {
                        edgeTargets.Add(reference);
                    }
                }
                if ((addresses.Count & 0xFFFF) == 0)
                {
                    cancel.ThrowIfCancellationRequested();
                }
            }
            edgeStarts.Add(edgeTargets.Count);
            // Regions need not come in address order; what the rest looks up by address must.
            sorted = addresses.ToArray();
            order = [.. Enumerable.Range(0, sorted.Length)];
            bool ascending = true;
            for (int i = 1; i < sorted.Length && ascending; i++)
            {
                ascending = sorted[i] > sorted[i - 1];
            }
            if (!ascending)
            {
                Array.Sort(sorted, order);
            }
        }

        private int TypeIndex(ClrType type)
        {
            if (!typeOf.TryGetValue(type.MethodTable, out int index))
            {
                index = types.Count;
                typeOf[type.MethodTable] = index;
                types.Add(new HeapTypeInfo(type.Name ?? "(unnamed)", type.MethodTable));
            }
            return index;
        }

        private int Find(ulong address)
        {
            int at = Array.BinarySearch(sorted, address);
            return at >= 0 ? order[at] : -1;
        }

        /// <summary>The objects the roots reach, breadth first, kept in address order with their references.</summary>
        public HeapGraph Analyse(CancellationToken cancel)
        {
            int count = addresses.Count;
            int[] starts = edgeStarts.ToArray();
            ulong[] targets = edgeTargets.ToArray();
            int[] targetIndex = new int[targets.Length];
            for (int e = 0; e < targets.Length; e++)
            {
                targetIndex[e] = Find(targets[e]);
            }
            bool[] reached = new bool[count];
            int[] queue = new int[count];
            int tail = 0;
            foreach (ClrRoot root in heap.EnumerateRoots())
            {
                if (Find(root.Object.Address) is var at and >= 0 && !reached[at])
                {
                    reached[at] = true;
                    queue[tail++] = at;
                }
            }
            for (int head = 0; head < tail; head++)
            {
                int from = queue[head];
                for (int e = starts[from]; e < starts[from + 1]; e++)
                {
                    int to = targetIndex[e];
                    if (to >= 0 && !reached[to])
                    {
                        reached[to] = true;
                        queue[tail++] = to;
                    }
                }
                if ((head & 0xFFFF) == 0)
                {
                    cancel.ThrowIfCancellationRequested();
                }
            }

            // Only what is reachable is kept, in address order, as dotMemory's snapshot after its collection.
            int[] kept = new int[count];
            Array.Fill(kept, -1);
            int live = 0;
            int[] liveOld = new int[tail];
            foreach (int old in order)
            {
                if (reached[old])
                {
                    kept[old] = live;
                    liveOld[live++] = old;
                }
            }
            ulong[] allAddresses = addresses.ToArray();
            int[] allTypes = objectTypes.ToArray();
            long[] allSizes = sizes.ToArray();
            ulong[] address = new ulong[live];
            int[] type = new int[live];
            long[] size = new long[live];
            int[] edgeStart = new int[live + 1];
            List<int> edgeTarget = [];
            for (int i = 0; i < live; i++)
            {
                int old = liveOld[i];
                address[i] = allAddresses[old];
                type[i] = allTypes[old];
                size[i] = allSizes[old];
                edgeStart[i] = edgeTarget.Count;
                for (int e = starts[old]; e < starts[old + 1]; e++)
                {
                    if (targetIndex[e] >= 0)
                    {
                        edgeTarget.Add(kept[targetIndex[e]]);
                    }
                }
            }
            edgeStart[live] = edgeTarget.Count;
            return new HeapGraph
            {
                Address = address,
                Type = type,
                Size = size,
                EdgeStart = edgeStart,
                EdgeTarget = [.. edgeTarget],
                Types = [.. types],
            };
        }
    }

    /// <summary>A growing array that never holds more than twice what it needs.</summary>
    private sealed class Growing<T>
    {
        private T[] items = new T[1024];

        public int Count { get; private set; }

        public void Add(T item)
        {
            if (Count == items.Length)
            {
                Array.Resize(ref items, items.Length * 2);
            }
            items[Count++] = item;
        }

        public T[] ToArray() => items[..Count];
    }
}

/// <summary>A dump that caught the GC mid-collection, whose heap cannot be walked.</summary>
public sealed class HeapUnreadableException() : Exception("The dump caught the program mid-collection");
