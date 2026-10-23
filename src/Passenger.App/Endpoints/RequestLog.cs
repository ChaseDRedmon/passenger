using System.Collections.Concurrent;
using Passenger.Profiler;
using Passenger.Roslyn.Extension;

namespace Passenger.App.Endpoints;

/// <summary>A database command a request issued: its text, time, the rows it read or changed (once known), why it failed,
/// how many times its text ran in its request (a command run again and again for one request is an N+1), and whether it was slow.</summary>
public sealed class LiveCommand(string id, string text, double milliseconds, int? rows, string? error)
{
    /// <summary>A command over this long is marked slow.</summary>
    public const double SlowMilliseconds = 100;

    /// <summary>A text run this often within one request is marked as repeated.</summary>
    public const int RepeatedAt = 3;

    public string Id { get; } = id;
    public string Text { get; } = text;
    public double Milliseconds { get; } = milliseconds;
    /// <summary>The rows a non-query changed.</summary>
    public int? Rows { get; } = rows;

    /// <summary>How many reads its reader made (EF Core's ReadCount: each Read asked, the last that found no row too).</summary>
    public int? Reads { get; internal set; }

    public string? Error { get; } = error;
    public int Runs { get; internal set; } = 1;
    public bool Repeated => Runs >= RepeatedAt;
    public bool Slow => Milliseconds >= SlowMilliseconds;
}

/// <summary>A request the app served, as it told: its method, path and query, when it began, its status and time once it
/// ended, the exception that ended it, the commands it issued, and the route of the folder's code it reached.</summary>
public sealed class LiveRequest(string trace, string method, string path, string query, DateTime began)
{
    public string Trace { get; } = trace;
    public string Method { get; } = method;
    public string Path { get; } = path;
    public string Query { get; } = query;
    public DateTime Began { get; } = began;
    public int? Status { get; internal set; }
    public double? Milliseconds { get; internal set; }
    public string? Error { get; internal set; }
    public List<LiveCommand> Commands { get; } = [];
    public WebRoute? Route { get; internal set; }

    /// <summary>Whether a command of it ran again and again (an N+1).</summary>
    public bool HasRepeats => Commands.Any(c => c.Repeated);
}

/// <summary>The live requests of the app followed, newest last, as many as <see cref="Most"/>: each event the stream thread
/// hears queued and taken on the UI thread a batch at a time, a command tied to its request by its activity's root id.</summary>
public sealed class RequestLog
{
    /// <summary>The most requests kept, the oldest going first.</summary>
    public const int Most = 500;

    /// <summary>The folder of the state's cache a command's text is opened from, read-only.</summary>
    public const string CommandCopies = "requests";

    /// <summary>Whether a file is a command's text copied to open it, which the editor never writes.</summary>
    public static bool IsCommandCopy(string path) =>
        System.IO.Path.GetDirectoryName(path) is { } folder && System.IO.Path.GetFileName(folder) == CommandCopies
        && System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(folder)) == "cache" && path.EndsWith(".sql", StringComparison.Ordinal);

    private readonly ConcurrentQueue<WebEvent> heard = new();
    private readonly List<LiveRequest> requests = [];
    private readonly Dictionary<string, LiveRequest> byTrace = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LiveRequest> byRequest = new(StringComparer.Ordinal);

    /// <summary>A request by its own id, else by its activity's root id.</summary>
    private LiveRequest? Of(string request, string trace) =>
        request.Length > 0 && byRequest.TryGetValue(request, out LiveRequest? found) ? found
        : trace.Length > 0 && byTrace.TryGetValue(trace, out LiveRequest? traced) ? traced
        : null;
    private readonly Func<string, string, WebRoute?> routeOf;
    private int posted;

    /// <param name="routeOf">The route a method and path reach, from the folder's routes.</param>
    public RequestLog(Func<string, string, WebRoute?> routeOf) => this.routeOf = routeOf;

    public IReadOnlyList<LiveRequest> Requests => requests;

    /// <summary>Requests came, ended or issued commands.</summary>
    public event EventHandler? Changed;

    /// <summary>An event heard on the stream's thread, taken on the UI thread with those heard meanwhile.</summary>
    public void Hear(WebEvent told)
    {
        heard.Enqueue(told);
        if (Interlocked.Exchange(ref posted, 1) == 0)
        {
            UiThread.Post(Take);
        }
    }

    /// <summary>Forgets every request, as a new run begins or the person clears the list.</summary>
    public void Clear()
    {
        UiThread.Check();
        requests.Clear();
        byTrace.Clear();
        byRequest.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Take()
    {
        Volatile.Write(ref posted, 0);
        bool changed = false;
        while (heard.TryDequeue(out WebEvent? told))
        {
            changed |= Apply(told);
        }
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool Apply(WebEvent told)
    {
        switch (told)
        {
            case RequestBegan began:
                LiveRequest request = new(began.Trace, began.Method, began.Path, began.Query, began.At) { Route = routeOf(began.Method, began.Path) };
                requests.Add(request);
                if (began.Trace.Length > 0)
                {
                    byTrace[began.Trace] = request;
                }
                if (began.Request.Length > 0)
                {
                    byRequest[began.Request] = request;
                }
                if (requests.Count > Most)
                {
                    byTrace.Remove(requests[0].Trace);
                    foreach (string gone in byRequest.Where(p => p.Value == requests[0]).Select(p => p.Key).ToList())
                    {
                        byRequest.Remove(gone);
                    }
                    requests.RemoveAt(0);
                }
                return true;
            case RequestEnded ended when Of(ended.Request, ended.Trace) is { } done:
                done.Status = ended.Status;
                done.Milliseconds = (ended.At - done.Began).TotalMilliseconds;
                return true;
            case RequestFailed failed when Of(failed.Request, failed.Trace) is { } broke:
                broke.Error = failed.Error;
                return true;
            case CommandRan ran when byTrace.TryGetValue(ran.Trace, out LiveRequest? issuer):
                LiveCommand command = new(ran.Id, ran.Text, ran.Milliseconds, ran.Rows, ran.Error);
                int runs = issuer.Commands.Count(c => c.Text == ran.Text) + 1;
                foreach (LiveCommand same in issuer.Commands.Where(c => c.Text == ran.Text))
                {
                    same.Runs = runs;
                }
                command.Runs = runs;
                issuer.Commands.Add(command);
                return true;
            case CommandRead read when byTrace.TryGetValue(read.Trace, out LiveRequest? reader)
                && reader.Commands.LastOrDefault(c => c.Id == read.Id) is { } readCommand:
                readCommand.Reads = read.Reads;
                return true;
            default:
                return false;
        }
    }
}
