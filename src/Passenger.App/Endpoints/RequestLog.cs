using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Passenger.App.Containers;
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

    /// <summary>Its parameters as EF Core logged them ("@__id_0='2'"), their values masked by ContainerSecrets' rule, '?' where
    /// the app logs none; null until heard.</summary>
    public string? Parameters { get; internal set; }

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

    /// <summary>EF Core's logs of commands not yet told of, their values masked.</summary>
    internal List<string> Logged { get; } = [];

    public WebRoute? Route { get; internal set; }

    /// <summary>The program that served it, as the window names it.</summary>
    public string From { get; internal set; } = string.Empty;

    /// <summary>The project of the program that served it, when known.</summary>
    public string? Project { get; internal set; }

    /// <summary>Whether a command of it ran again and again (an N+1).</summary>
    public bool HasRepeats => Commands.Any(c => c.Repeated);
}

/// <summary>The live requests of the app followed, newest last, as many as <see cref="Most"/>: each event the stream thread
/// hears queued and taken on the UI thread a batch at a time, a command tied to its request by its activity's root id.</summary>
public sealed class RequestLog
{
    /// <summary>The most requests kept, the oldest going first.</summary>
    public const int Most = 500;

    /// <summary>The folder of the state's cache SQL is opened from, read-only: a command's text, or an EF Core query's SQL the
    /// debugger read.</summary>
    public const string CommandCopies = "requests";

    /// <summary>Whether a file is SQL copied to open it (<see cref="CommandCopies"/>), which the editor never writes.</summary>
    public static bool IsCommandCopy(string path) =>
        System.IO.Path.GetDirectoryName(path) is { } folder && System.IO.Path.GetFileName(folder) == CommandCopies
        && System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(folder)) == "cache" && path.EndsWith(".sql", StringComparison.Ordinal);

    private readonly ConcurrentQueue<(WebEvent Told, string From, string? Project)> heard = new();
    private readonly List<LiveRequest> requests = [];
    private readonly Dictionary<string, LiveRequest> byTrace = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LiveRequest> byRequest = new(StringComparer.Ordinal);

    /// <summary>A request by its own id, else by its activity's root id.</summary>
    private LiveRequest? Of(string request, string trace) =>
        request.Length > 0 && byRequest.TryGetValue(request, out LiveRequest? found) ? found
        : trace.Length > 0 && byTrace.TryGetValue(trace, out LiveRequest? traced) ? traced
        : null;
    private readonly Func<string, string, string?, WebRoute?> routeOf;
    private int posted;

    /// <param name="routeOf">The route a method and path reach in a project (any when null), from the folder's routes.</param>
    public RequestLog(Func<string, string, string?, WebRoute?> routeOf) => this.routeOf = routeOf;

    public IReadOnlyList<LiveRequest> Requests => requests;

    /// <summary>Requests came, ended or issued commands.</summary>
    public event EventHandler? Changed;

    /// <summary>An event heard on the stream's thread, taken on the UI thread with those heard meanwhile, with the program that told it
    /// and its project.</summary>
    public void Hear(WebEvent told, string from, string? project)
    {
        heard.Enqueue((told, from, project));
        if (Interlocked.Exchange(ref posted, 1) == 0)
        {
            UiThread.Post(Take);
        }
    }

    /// <summary>The routes were read again: each request that reached none known yet is looked for again.</summary>
    public void Reroute()
    {
        UiThread.Check();
        bool found = false;
        foreach (LiveRequest request in requests.Where(r => r.Route is null))
        {
            request.Route = routeOf(request.Method, request.Path, request.Project);
            found |= request.Route is not null;
        }
        if (found)
        {
            Changed?.Invoke(this, EventArgs.Empty);
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
        while (heard.TryDequeue(out (WebEvent Told, string From, string? Project) one))
        {
            changed |= Apply(one.Told, one.From, one.Project);
        }
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool Apply(WebEvent told, string from, string? project)
    {
        switch (told)
        {
            case RequestBegan began:
                LiveRequest request = new(began.Trace, began.Method, began.Path, began.Query, began.At) { From = from, Project = project, Route = routeOf(began.Method, began.Path, project) };
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
                // EF Core logs a command before it tells of it: its log, heard first, gives it its parameters.
                if (issuer.Logged.FirstOrDefault(m => m.Contains(ran.Text, StringComparison.Ordinal)) is { } logged)
                {
                    issuer.Logged.Remove(logged);
                    command.Parameters = CommandParameters.Of(logged);
                }
                return true;
            case CommandRead read when byTrace.TryGetValue(read.Trace, out LiveRequest? reader)
                && reader.Commands.LastOrDefault(c => c.Id == read.Id) is { } readCommand:
                readCommand.Reads = read.Reads;
                return true;
            case CommandLogged log when byTrace.TryGetValue(log.Trace, out LiveRequest? logger):
                if (logger.Commands.FirstOrDefault(c => c.Parameters is null && log.Message.Contains(c.Text, StringComparison.Ordinal)) is { } unnamed)
                {
                    unnamed.Parameters = CommandParameters.Of(log.Message);
                    return true;
                }
                // Kept with its values masked until its command comes: what the app logged in the clear is never held.
                logger.Logged.Add(CommandParameters.Masked(log.Message));
                return false;
            default:
                return false;
        }
    }
}

/// <summary>A command's parameters as EF Core's log of it writes them (<c>[Parameters=[@__id_0='2' (DbType = Int32)], …]</c>):
/// each value shown as ContainerSecrets shows a value of that name (a secret's as dots, a URL's password or a connection
/// string's part so named masked in any other), so no secret the app logs is kept or shown in the clear.</summary>
public static partial class CommandParameters
{
    /// <summary>The parameters a log names, masked, as "@name='value'" apart by commas; empty for a command with none.</summary>
    public static string Of(string logged)
    {
        Match list = List().Match(logged);
        return list.Success ? string.Join(", ", One().Matches(Masked(list.Groups["list"].Value)).Select(p => p.Value.Trim())) : string.Empty;
    }

    /// <summary>A log's text with each parameter's value masked as <see cref="Of"/> shows it.</summary>
    public static string Masked(string logged) =>
        One().Replace(logged, p => $"{p.Groups["name"].Value}='{ContainerSecrets.Shown(p.Groups["name"].Value.TrimStart('@', ':', '$'), p.Groups["value"].Value.Replace("''", "'", StringComparison.Ordinal)).Replace("'", "''", StringComparison.Ordinal)}'{p.Groups["rest"].Value}");

    [GeneratedRegex(@"\[Parameters=\[(?<list>.*?)\], CommandType=", RegexOptions.Singleline)]
    private static partial Regex List();

    [GeneratedRegex(@"(?<name>[@:$]\w+)='(?<value>(?:[^']|'')*)'(?<rest>(?: \([^)]*\))?)")]
    private static partial Regex One();
}
