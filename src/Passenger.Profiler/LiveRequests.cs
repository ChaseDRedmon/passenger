using System.Diagnostics.Tracing;
using System.Globalization;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

namespace Passenger.Profiler;

/// <summary>Something a web app told of its requests: each is of one request, known by its activity's root id
/// (<see cref="Trace"/>), the time it was told (UTC).</summary>
public abstract record WebEvent(string Trace, DateTime At);

/// <summary>ASP.NET Core began a request, its own id (<c>HttpContext.TraceIdentifier</c>) in <paramref name="Request"/>.</summary>
public sealed record RequestBegan(string Trace, DateTime At, string Request, string Method, string Path, string Query) : WebEvent(Trace, At);

/// <summary>ASP.NET Core ended a request with its status: told by the request's own id, since a request an exception ended
/// is ended outside its activity.</summary>
public sealed record RequestEnded(string Trace, DateTime At, string Request, int Status) : WebEvent(Trace, At);

/// <summary>A request ended in an exception nothing handled: its words and stack, as the exception writes itself.</summary>
public sealed record RequestFailed(string Trace, DateTime At, string Request, string Error) : WebEvent(Trace, At);

/// <summary>EF Core ran a command for a request: its id, text, time and the rows a non-query changed; or why it failed.</summary>
public sealed record CommandRan(string Trace, DateTime At, string Id, string Text, double Milliseconds, int? Rows, string? Error) : WebEvent(Trace, At);

/// <summary>A command's reader closed after so many reads (EF Core's ReadCount: each Read asked, the last that found no row too).</summary>
public sealed record CommandRead(string Trace, DateTime At, string Id, int Reads) : WebEvent(Trace, At);

/// <summary>A running web app's requests and the database commands each issued, heard live with no change to the app: ASP.NET
/// Core's hosting events and EF Core's command events through the runtime's DiagnosticSource bridge
/// (<c>Microsoft-Diagnostics-DiagnosticSource</c>) over an EventPipe session at the program's own diagnostic port, each told
/// with the root id of the activity it ran under, which ties a command to its request. Nothing is asked of the app while
/// no one listens: the session lasts from <see cref="AttachAsync"/> to <see cref="DisposeAsync"/>.</summary>
public sealed class LiveRequests : IAsyncDisposable
{
    private const string Bridge = "Microsoft-Diagnostics-DiagnosticSource";

    /// <summary>The events asked for and the values each takes from its payload (<c>*Activity</c> is the activity it ran under).</summary>
    internal static readonly string Specs = string.Join("\n",
    [
        // Asked by its name, so ASP.NET Core makes each request's activity for its listeners too.
        "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn:-",
        "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start:-Method=Request.Method;Path=Request.Path;Query=Request.QueryString;Request=TraceIdentifier;Trace=*Activity.RootId",
        "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop:-Status=Response.StatusCode;Request=TraceIdentifier;Trace=*Activity.RootId",
        "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.UnhandledException:-Error=exception;Request=httpContext.TraceIdentifier;Trace=*Activity.RootId",
        "Microsoft.AspNetCore/Microsoft.AspNetCore.Diagnostics.UnhandledException:-Error=exception;Request=httpContext.TraceIdentifier;Trace=*Activity.RootId",
        "Microsoft.EntityFrameworkCore/Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted:-Id=CommandId;Text=Command.CommandText;Ms=Duration.TotalMilliseconds;Result=Result;Trace=*Activity.RootId",
        "Microsoft.EntityFrameworkCore/Microsoft.EntityFrameworkCore.Database.Command.CommandError:-Id=CommandId;Text=Command.CommandText;Ms=Duration.TotalMilliseconds;Error=Exception.Message;Trace=*Activity.RootId",
        "Microsoft.EntityFrameworkCore/Microsoft.EntityFrameworkCore.Database.Command.DataReaderDisposing:-Id=CommandId;Reads=ReadCount;Trace=*Activity.RootId",
    ]);

    private readonly EventPipeSession session;
    private readonly EventPipeEventSource source;

    private LiveRequests(EventPipeSession session, EventPipeEventSource source, Task ended)
    {
        this.session = session;
        this.source = source;
        Ended = ended;
    }

    /// <summary>Done once the stream ends: the program gone, or the session stopped.</summary>
    public Task Ended { get; }

    /// <summary>The providers a session asks: the bridge's events and messages, with <see cref="Specs"/>.</summary>
    internal static EventPipeProvider[] Providers =>
    [
        new EventPipeProvider(Bridge, EventLevel.Verbose, 0x3, new Dictionary<string, string> { ["FilterAndPayloadSpecs"] = Specs }),
        // The runtime's counters once a second, passed over: the stream's reader hands on what it holds as more comes, so a
        // request's last events are heard within a second of it, not only once the app serves another.
        new EventPipeProvider("System.Runtime", EventLevel.Informational, 0, new Dictionary<string, string> { ["EventCounterIntervalSec"] = "1" }),
    ];

    /// <summary>Starts hearing a running program at its diagnostic port, each event told to <paramref name="heard"/> on the
    /// thread that reads the stream. A program gone, or with no port to reach, is a <see cref="ProfilingException"/> saying why.</summary>
    public static async Task<LiveRequests> AttachAsync(int processId, Action<WebEvent> heard, CancellationToken cancel = default)
    {
        EventPipeSession session;
        try
        {
            session = await new DiagnosticsClient(processId).StartEventPipeSessionAsync(Providers, requestRundown: false, circularBufferMB: 64, cancel);
        }
        catch (Exception error) when (error is DiagnosticsClientException or IOException or UnauthorizedAccessException or System.Net.Sockets.SocketException or TimeoutException)
        {
            throw new ProfilingException($"Its requests could not be followed: {error.Message}", error);
        }
        EventPipeEventSource source = new(session.EventStream);
        source.Dynamic.All += data =>
        {
            if (data.ProviderName == Bridge && Read(data) is { } told)
            {
                heard(told);
            }
        };
        // A thread of its own: the stream is read until the program ends or the session is stopped.
        Task ended = Task.Factory.StartNew(() =>
        {
            try
            {
                source.Process();
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException or EndOfStreamException or FormatException)
            {
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return new LiveRequests(session, source, ended);
    }

    /// <summary>One of the bridge's events as what it tells; null for another.</summary>
    internal static WebEvent? Read(TraceEvent data)
    {
        if (data.PayloadByName("EventName") is not string name || data.PayloadByName("Arguments") is not IEnumerable<object> arguments)
        {
            return null;
        }
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (object pair in arguments)
        {
            if (pair is IDictionary<string, object> entry && entry.TryGetValue("Key", out object? key) && key is string k)
            {
                values[k] = entry.TryGetValue("Value", out object? value) ? value?.ToString() ?? string.Empty : string.Empty;
            }
        }
        string trace = values.GetValueOrDefault("Trace") ?? string.Empty;
        string request = values.GetValueOrDefault("Request") ?? string.Empty;
        DateTime at = data.TimeStamp.ToUniversalTime();
        return name switch
        {
            "Microsoft.AspNetCore.Hosting.HttpRequestIn.Start" => new RequestBegan(trace, at, request, values.GetValueOrDefault("Method") ?? string.Empty,
                values.GetValueOrDefault("Path") ?? string.Empty, values.GetValueOrDefault("Query") ?? string.Empty),
            "Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop" => new RequestEnded(trace, at, request, Number(values.GetValueOrDefault("Status")) ?? 0),
            "Microsoft.AspNetCore.Hosting.UnhandledException" or "Microsoft.AspNetCore.Diagnostics.UnhandledException" =>
                new RequestFailed(trace, at, request, values.GetValueOrDefault("Error") ?? string.Empty),
            "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted" or "Microsoft.EntityFrameworkCore.Database.Command.CommandError" =>
                new CommandRan(trace, at, values.GetValueOrDefault("Id") ?? string.Empty, values.GetValueOrDefault("Text") ?? string.Empty,
                    double.TryParse(values.GetValueOrDefault("Ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) ? ms : 0,
                    Number(values.GetValueOrDefault("Result")), values.GetValueOrDefault("Error")),
            "Microsoft.EntityFrameworkCore.Database.Command.DataReaderDisposing" =>
                new CommandRead(trace, at, values.GetValueOrDefault("Id") ?? string.Empty, Number(values.GetValueOrDefault("Reads")) ?? 0),
            _ => null,
        };

        static int? Number(string? text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : null;
    }

    /// <summary>Stops the session and lets go of the program, which runs on as it was.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await session.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception error) when (error is DiagnosticsClientException or IOException or ObjectDisposedException or TimeoutException or ServerNotAvailableException)
        {
            // A program gone has nothing left to stop.
        }
        source.StopProcessing();
        session.Dispose();
        await Ended.WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { }, TaskScheduler.Default);
        source.Dispose();
    }
}
