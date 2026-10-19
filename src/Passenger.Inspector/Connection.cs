using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Passenger.Inspector;

/// <summary>The app's end of the socket Passenger listens on: requests read a line at a time, and replies and events written
/// by a thread of its own, so the app's UI thread never waits on Passenger. Nothing here touches Avalonia.</summary>
internal sealed class Connection : IDisposable
{
    private readonly Socket socket;
    private readonly StreamReader reader;
    private readonly Stream stream;
    private readonly BlockingCollection<Reply> outgoing = new(boundedCapacity: 1024);
    private readonly Thread writer;

    private Connection(Socket socket)
    {
        this.socket = socket;
        stream = new NetworkStream(socket, ownsSocket: true);
        reader = new StreamReader(stream, new UTF8Encoding(false));
        Configure();
        writer = new Thread(Write) { IsBackground = true, Name = "Passenger inspector writer" };
        writer.Start();
    }

    /// <summary>Has System.Text.Json make every type the wire carries ready now, before the app's Main, rather than on its
    /// first use, which it does under one lock for all of them. The writer, stopped there by the debugger as it sent the
    /// failure the app stopped at, held it from the binding values evaluated at that stop (BindingValues.Read), whose abort
    /// then fired inside it once the app went on and kept it held for good: no request was read again.</summary>
    private static void Configure()
    {
        foreach (PropertyInfo property in typeof(WireJson).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (typeof(JsonTypeInfo).IsAssignableFrom(property.PropertyType))
            {
                _ = property.GetValue(WireJson.Default);
            }
        }
    }

    /// <summary>Connects to Passenger's socket, or null when it is not there (Passenger gone, the run not its).</summary>
    public static Connection? Open(string path)
    {
        Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(path));
            return new Connection(socket);
        }
        catch (SocketException)
        {
            socket.Dispose();
            return null;
        }
    }

    /// <summary>Tells Passenger why the inspector cannot serve this app, and goes.</summary>
    public static void Refuse(string path, Version? avalonia, string problem)
    {
        using Connection? connection = Open(path);
        connection?.Send(new Reply { Event = Wire.Hello, Hello = new Hello(Wire.Version, Environment.ProcessId, avalonia?.ToString(), problem) });
        connection?.Drain();
    }

    /// <summary>The next request, or null once Passenger has gone.</summary>
    public Request? Read()
    {
        while (true)
        {
            string? line;
            try
            {
                line = reader.ReadLine();
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                return null;
            }
            if (line is null)
            {
                return null;
            }
            if (line.Length == 0)
            {
                continue;
            }
            try
            {
                if (JsonSerializer.Deserialize(line, WireJson.Default.Request) is { } request)
                {
                    return request;
                }
            }
            catch (JsonException)
            {
                // A line this version cannot read is passed over; Passenger's own version check refuses a mismatch first.
            }
        }
    }

    /// <summary>Queues a reply or an event; from any thread, never waiting on Passenger, and nothing once it has gone.</summary>
    public void Send(Reply reply)
    {
        try
        {
            outgoing.TryAdd(reply);
        }
        catch (InvalidOperationException)
        {
            // Closed as Passenger went, while the app's thread was saying something (a binding failing).
        }
    }

    /// <summary>Waits a moment for what is queued to be written, as the inspector gives up on an app it cannot serve.</summary>
    internal void Drain()
    {
        outgoing.CompleteAdding();
        writer.Join(TimeSpan.FromSeconds(1));
    }

    private void Write()
    {
        try
        {
            foreach (Reply reply in outgoing.GetConsumingEnumerable())
            {
                byte[] line;
                try
                {
                    line = JsonSerializer.SerializeToUtf8Bytes(reply, WireJson.Default.Reply);
                }
                catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
                {
                    // What could not be written is said as the request's failure, rather than ending what the inspector says.
                    line = JsonSerializer.SerializeToUtf8Bytes(new Reply { Id = reply.Id, Event = reply.Event, Error = $"The inspector could not write its answer: {error.Message}" }, WireJson.Default.Reply);
                }
                stream.Write(line);
                stream.WriteByte((byte)'\n');
                stream.Flush();
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
        {
            // Passenger went: the reader sees it too.
        }
    }

    public void Dispose()
    {
        outgoing.CompleteAdding();
        try
        {
            socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }
        reader.Dispose();
    }
}
