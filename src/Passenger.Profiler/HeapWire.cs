using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Passenger.Profiler;

/// <summary>A memory snapshot as Passenger's container tools send it out of a container (<see cref="InContainer.HeapAsync"/>)
/// and the IDE reads it back (<see cref="ContainerHeap"/>): the heap graph whole as the container read it (its objects,
/// references, roots, shortest paths and retained sizes) and how each earlier snapshot's objects are found in it, so the
/// Memory page shows it, compares it and opens its objects as it does a snapshot of this machine's. A frame is a kind (a
/// snapshot, or why there is none), the wire's version, the body's length and the body; a snapshot's body is deflated,
/// each array a column of 7-bit numbers (addresses as the steps between them, in their order), which deflate shrinks
/// well.</summary>
internal static class HeapWire
{
    /// <summary>Raised when what is written changes; a reader refuses another version rather than misread it.</summary>
    public const byte Version = 1;

    public const byte Snapshot = (byte)'S';

    public const byte Failure = (byte)'F';

    private const int HeaderLength = 10;

    /// <summary>The largest body read, past which the stream is taken for something other than the wire.</summary>
    private const long LargestBody = int.MaxValue - 64;

    public static void WriteFailure(Stream output, string why) => WriteFrame(output, Failure, Encoding.UTF8.GetBytes(why));

    public static void WriteSnapshot(Stream output, MemorySnapshot taken)
    {
        using MemoryStream body = new();
        using (DeflateStream deflated = new(body, CompressionLevel.Fastest, leaveOpen: true))
        using (BinaryWriter writer = new(deflated, Encoding.UTF8, leaveOpen: true))
        {
            Write(writer, taken);
        }
        WriteFrame(output, Snapshot, body.GetBuffer().AsSpan(0, (int)body.Length));
    }

    private static void WriteFrame(Stream output, byte kind, ReadOnlySpan<byte> body)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        header[0] = kind;
        header[1] = Version;
        BinaryPrimitives.WriteInt64LittleEndian(header[2..], body.Length);
        output.Write(header);
        output.Write(body);
    }

    /// <summary>The next frame, or null once the stream has ended between frames. One of another version, or a stream that
    /// ends inside a frame, is an <see cref="InvalidDataException"/>.</summary>
    public static async Task<(byte Kind, byte[] Body)?> ReadFrameAsync(Stream input)
    {
        byte[] header = new byte[HeaderLength];
        int read = await input.ReadAtLeastAsync(header, HeaderLength, throwOnEndOfStream: false).ConfigureAwait(false);
        if (read == 0)
        {
            return null;
        }
        if (read < HeaderLength)
        {
            throw new InvalidDataException("The container tools' answer ended inside a frame");
        }
        if (header[1] != Version)
        {
            throw new InvalidDataException($"The container tools answer in version {header[1]} of the heap's wire, and the IDE reads version {Version}");
        }
        long length = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(2));
        if (length is < 0 or > LargestBody || header[0] is not (Snapshot or Failure))
        {
            throw new InvalidDataException("The container tools' answer is not a heap snapshot");
        }
        byte[] body = new byte[length];
        try
        {
            await input.ReadExactlyAsync(body).ConfigureAwait(false);
        }
        catch (EndOfStreamException ended)
        {
            throw new InvalidDataException("The container tools' answer ended inside a frame", ended);
        }
        return (header[0], body);
    }

    /// <summary>A snapshot's body read back, of a run of this side's (<paramref name="run"/>, what snapshots are compared
    /// within), <paramref name="asked"/> the time from asking for it to its coming, the rest of which its sending took; its
    /// number, and those of the earlier ones it follows, after the run's <paramref name="before"/> (tools started again).</summary>
    public static MemorySnapshot ReadSnapshot(byte[] body, object run, TimeSpan asked, int before = 0)
    {
        using MemoryStream packed = new(body, writable: false);
        using DeflateStream inflated = new(packed, CompressionMode.Decompress);
        using BinaryReader reader = new(inflated, Encoding.UTF8);
        int number = before + reader.ReadInt32();
        DateTime taken = DateTime.FromBinary(reader.ReadInt64()).ToLocalTime();
        TimeSpan took = TimeSpan.FromTicks(reader.ReadInt64());
        string phases = reader.ReadString();
        HeapGraph graph = ReadGraph(reader);
        int earlierCount = reader.Read7BitEncodedInt();
        Dictionary<int, Identity> earlier = new(earlierCount);
        for (int i = 0; i < earlierCount; i++)
        {
            int older = reader.Read7BitEncodedInt();
            string? inexact = reader.ReadBoolean() ? reader.ReadString() : null;
            int[] later = new int[reader.Read7BitEncodedInt()];
            for (int o = 0; o < later.Length; o++)
            {
                later[o] = reader.Read7BitEncodedInt() - 1;
            }
            earlier[before + older] = new Identity(later, inexact);
        }
        TimeSpan sent = asked > took ? asked - took : TimeSpan.Zero;
        return new MemorySnapshot(run, number, taken, graph, took + sent, $"{phases}, sent out of the container {sent.TotalSeconds:0.0} s", earlier);
    }

    private static void Write(BinaryWriter writer, MemorySnapshot taken)
    {
        writer.Write(taken.Number);
        writer.Write(taken.Taken.ToUniversalTime().ToBinary());
        writer.Write(taken.Took.Ticks);
        writer.Write(taken.Phases);
        HeapGraph graph = taken.Graph;
        writer.Write(graph.PointerSize);
        writer.Write(graph.Walked.Ticks);
        writer.Write(graph.Analysed.Ticks);
        writer.Write7BitEncodedInt(graph.Types.Length);
        foreach (HeapTypeInfo type in graph.Types)
        {
            writer.Write(type.Name);
            writer.Write(type.MethodTable);
            writer.Write7BitEncodedInt(type.ComponentSize);
            writer.Write(type.IsVector);
            writer.Write7BitEncodedInt(type.Fields.Count);
            foreach ((int offset, string name) in type.Fields)
            {
                WriteSigned(writer, offset);
                writer.Write(name);
            }
        }
        writer.Write7BitEncodedInt(graph.Roots.Length);
        foreach (GcRoot root in graph.Roots)
        {
            writer.Write((byte)root.Kind);
            writer.Write(root.Name);
        }
        int count = graph.Count;
        writer.Write7BitEncodedInt(count);
        ulong previous = 0;
        foreach (ulong address in graph.Address)
        {
            writer.Write7BitEncodedInt64((long)(address - previous));
            previous = address;
        }
        Column(graph.Type, 0);
        Wide(graph.Size);
        Column(graph.Root, 1);
        Column(graph.Parent, 1);
        foreach (int offset in graph.ParentOffset)
        {
            WriteSigned(writer, offset);
        }
        Column(graph.Depth, 0);
        Wide(graph.Retained);
        Column(graph.Enter, 0);
        Column(graph.Leave, 0);
        for (int i = 0; i < count; i++)
        {
            writer.Write7BitEncodedInt(graph.EdgeStart[i + 1] - graph.EdgeStart[i]);
        }
        Column(graph.EdgeTarget, 0);
        foreach (int offset in graph.EdgeOffset)
        {
            WriteSigned(writer, offset);
        }
        Wide(graph.TypeRetained);
        writer.Write7BitEncodedInt(taken.Earlier.Count);
        foreach ((int older, Identity identity) in taken.Earlier)
        {
            writer.Write7BitEncodedInt(older);
            writer.Write(identity.Inexact is not null);
            if (identity.Inexact is { } inexact)
            {
                writer.Write(inexact);
            }
            Column(identity.Later, 1);
        }

        // Each number shifted by what keeps it from going below 0 (-1 for none, where one is none).
        void Column(int[] values, int shift)
        {
            writer.Write7BitEncodedInt(values.Length);
            foreach (int value in values)
            {
                writer.Write7BitEncodedInt(value + shift);
            }
        }

        void Wide(long[] values)
        {
            writer.Write7BitEncodedInt(values.Length);
            foreach (long value in values)
            {
                writer.Write7BitEncodedInt64(value);
            }
        }
    }

    private static HeapGraph ReadGraph(BinaryReader reader)
    {
        int pointerSize = reader.ReadInt32();
        TimeSpan walked = TimeSpan.FromTicks(reader.ReadInt64());
        TimeSpan analysed = TimeSpan.FromTicks(reader.ReadInt64());
        HeapTypeInfo[] types = new HeapTypeInfo[reader.Read7BitEncodedInt()];
        for (int t = 0; t < types.Length; t++)
        {
            HeapTypeInfo type = new(reader.ReadString(), reader.ReadUInt64(), reader.Read7BitEncodedInt(), reader.ReadBoolean());
            int fields = reader.Read7BitEncodedInt();
            for (int f = 0; f < fields; f++)
            {
                int offset = ReadSigned(reader);
                type.Fields[offset] = reader.ReadString();
            }
            types[t] = type;
        }
        GcRoot[] roots = new GcRoot[reader.Read7BitEncodedInt()];
        for (int r = 0; r < roots.Length; r++)
        {
            roots[r] = new GcRoot((RootKind)reader.ReadByte(), reader.ReadString());
        }
        int count = reader.Read7BitEncodedInt();
        ulong[] address = new ulong[count];
        ulong previous = 0;
        for (int i = 0; i < count; i++)
        {
            previous += (ulong)reader.Read7BitEncodedInt64();
            address[i] = previous;
        }
        int[] objectTypes = Column(reader, 0);
        long[] size = Wide(reader);
        int[] root = Column(reader, 1);
        int[] parent = Column(reader, 1);
        int[] parentOffset = new int[count];
        for (int i = 0; i < count; i++)
        {
            parentOffset[i] = ReadSigned(reader);
        }
        int[] depth = Column(reader, 0);
        long[] retained = Wide(reader);
        int[] enter = Column(reader, 0);
        int[] leave = Column(reader, 0);
        int[] edgeStart = new int[count + 1];
        for (int i = 0; i < count; i++)
        {
            edgeStart[i + 1] = edgeStart[i] + reader.Read7BitEncodedInt();
        }
        int[] edgeTarget = Column(reader, 0);
        int[] edgeOffset = new int[edgeTarget.Length];
        for (int e = 0; e < edgeOffset.Length; e++)
        {
            edgeOffset[e] = ReadSigned(reader);
        }
        long[] typeRetained = Wide(reader);
        if (objectTypes.Length != count || size.Length != count || edgeTarget.Length != edgeStart[count] || typeRetained.Length != types.Length)
        {
            throw new InvalidDataException("The container tools' heap graph does not hold together");
        }
        return new HeapGraph
        {
            Address = address,
            Type = objectTypes,
            Size = size,
            EdgeStart = edgeStart,
            EdgeTarget = edgeTarget,
            EdgeOffset = edgeOffset,
            Types = types,
            Roots = roots,
            Root = root,
            Parent = parent,
            ParentOffset = parentOffset,
            Depth = depth,
            Retained = retained,
            Enter = enter,
            Leave = leave,
            TypeRetained = typeRetained,
            PointerSize = pointerSize,
            Walked = walked,
            Analysed = analysed,
        };

        static int[] Column(BinaryReader reader, int shift)
        {
            int[] values = new int[reader.Read7BitEncodedInt()];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = reader.Read7BitEncodedInt() - shift;
            }
            return values;
        }

        static long[] Wide(BinaryReader reader)
        {
            long[] values = new long[reader.Read7BitEncodedInt()];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = reader.Read7BitEncodedInt64();
            }
            return values;
        }
    }

    /// <summary>A number that may be below 0 (a dependent handle's offset), zigzagged so a small one stays small.</summary>
    private static void WriteSigned(BinaryWriter writer, int value) => writer.Write7BitEncodedInt((value << 1) ^ (value >> 31));

    private static int ReadSigned(BinaryReader reader)
    {
        int zigzag = reader.Read7BitEncodedInt();
        return (int)((uint)zigzag >> 1) ^ -(zigzag & 1);
    }
}
