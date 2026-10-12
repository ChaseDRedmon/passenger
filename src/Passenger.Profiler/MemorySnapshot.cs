using System.Diagnostics;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Runtime;

namespace Passenger.Profiler;

/// <summary>A type on the heap: how many objects of it live, and the bytes they take themselves (shallow, as dotMemory's
/// first view has them).</summary>
public sealed record HeapType(string Name, int Count, long Bytes);

/// <summary>What a program's heap held at a moment, as dotMemory's snapshot: every live object's type, counted and
/// sized. The runtime writes a dump of the heap through its diagnostic port (createdump, so no debugger attaches) and
/// ClrMD reads it; the dump, as large as the heap, is deleted once read.</summary>
public sealed class MemorySnapshot
{
    private MemorySnapshot(IReadOnlyList<HeapType> types, long bytes, int objects, TimeSpan took)
    {
        Types = types;
        Bytes = bytes;
        Objects = objects;
        Took = took;
    }

    /// <summary>The types, the most bytes first.</summary>
    public IReadOnlyList<HeapType> Types { get; }

    /// <summary>The heap's live objects together, in bytes.</summary>
    public long Bytes { get; }

    public int Objects { get; }

    /// <summary>How long the dump and its reading took, the program held for the dump's part.</summary>
    public TimeSpan Took { get; }

    /// <summary>Takes a snapshot of a running .NET program's heap.</summary>
    public static Task<MemorySnapshot> TakeAsync(int processId, CancellationToken cancellation = default) => Task.Run(() =>
    {
        Stopwatch clock = Stopwatch.StartNew();
        string dump = Path.Combine(Path.GetTempPath(), $"passenger-heap-{processId}-{Guid.NewGuid().ToString("N")[..8]}.dmp");
        try
        {
            new DiagnosticsClient(processId).WriteDump(DumpType.WithHeap, dump, logDumpGeneration: false);
            cancellation.ThrowIfCancellationRequested();
            using DataTarget target = DataTarget.LoadDump(dump);
            if (target.ClrVersions.Length == 0)
            {
                throw new InvalidOperationException("The dump has no .NET runtime in it");
            }
            using ClrRuntime runtime = target.ClrVersions[0].CreateRuntime();
            Dictionary<string, (int Count, long Bytes)> types = new(StringComparer.Ordinal);
            long bytes = 0;
            int objects = 0;
            foreach (ClrObject item in runtime.Heap.EnumerateObjects())
            {
                if (item.Type is not { } type || item.IsFree)
                {
                    continue;
                }
                long size = (long)item.Size;
                string name = type.Name ?? "(unnamed)";
                (int Count, long Bytes) so = types.GetValueOrDefault(name);
                types[name] = (so.Count + 1, so.Bytes + size);
                bytes += size;
                objects++;
            }
            return new MemorySnapshot([.. types.Select(t => new HeapType(t.Key, t.Value.Count, t.Value.Bytes)).OrderByDescending(t => t.Bytes)], bytes, objects, clock.Elapsed);
        }
        finally
        {
            try
            {
                File.Delete(dump);
            }
            catch (IOException)
            {
            }
        }
    }, cancellation);
}
