using System.Collections;

namespace Passenger.Profiler;

/// <summary>An object as an object set lists it: where it is, its type and its own bytes.</summary>
public sealed record HeapObject(ulong Address, string Type, long Bytes);

/// <summary>A set of a snapshot's objects, as dotMemory opens one: a type's, or a comparison's new, dead or survived objects
/// of a type. Building one takes a while on a large heap: call it off the UI thread.</summary>
public sealed class ObjectSet
{
    private readonly int[] members;

    internal ObjectSet(MemorySnapshot snapshot, string title, int[] members)
    {
        Snapshot = snapshot;
        Title = title;
        HeapGraph graph = snapshot.Graph;
        // The largest first, as dotMemory lists instances.
        Array.Sort(members, (a, b) => graph.Size[b].CompareTo(graph.Size[a]) is var c and not 0 ? c : a.CompareTo(b));
        this.members = members;
        foreach (int member in members)
        {
            Bytes += graph.Size[member];
        }
        Objects = new Listing(graph, members);
    }

    public MemorySnapshot Snapshot { get; }

    /// <summary>What dotMemory calls it: "New objects of the type Item".</summary>
    public string Title { get; }

    public int Count => members.Length;

    public long Bytes { get; }

    /// <summary>The objects, the largest first.</summary>
    public IReadOnlyList<HeapObject> Objects { get; }

    /// <summary>The set's objects, made as a list asks for them: a million records at once would be the heap again.</summary>
    private sealed class Listing(HeapGraph graph, int[] members) : IReadOnlyList<HeapObject>, IList
    {
        public HeapObject this[int index]
        {
            get
            {
                int at = members[index];
                return new HeapObject(graph.Address[at], graph.Types[graph.Type[at]].Name, graph.Size[at]);
            }
        }

        public int Count => members.Length;

        object? IList.this[int index]
        {
            get => this[index];
            set => throw new NotSupportedException();
        }

        bool IList.IsFixedSize => true;

        bool IList.IsReadOnly => true;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        public IEnumerator<HeapObject> GetEnumerator()
        {
            for (int i = 0; i < members.Length; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        int IList.Add(object? value) => throw new NotSupportedException();

        void IList.Clear() => throw new NotSupportedException();

        bool IList.Contains(object? value) => ((IList)this).IndexOf(value) >= 0;

        int IList.IndexOf(object? value)
        {
            if (value is HeapObject wanted && graph.IndexOf(wanted.Address) is var at and >= 0)
            {
                return Array.IndexOf(members, at);
            }
            return -1;
        }

        void IList.Insert(int index, object? value) => throw new NotSupportedException();

        void IList.Remove(object? value) => throw new NotSupportedException();

        void IList.RemoveAt(int index) => throw new NotSupportedException();

        void ICollection.CopyTo(Array array, int index)
        {
            for (int i = 0; i < members.Length; i++)
            {
                array.SetValue(this[i], index + i);
            }
        }
    }
}
