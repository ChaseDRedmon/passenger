using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Passenger.Core;

namespace Passenger.App.Containers;

/// <summary>How the engine stands, as the status bar and the Containers window say it.</summary>
public enum EngineStatus
{
    /// <summary>A Linux engine answers, with Compose v2 beside its CLI.</summary>
    Ready,

    /// <summary>No <c>docker</c> on the PATH nor in Docker Desktop's, OrbStack's or Rancher Desktop's folders.</summary>
    NoCli,

    /// <summary>The CLI is there but its context's engine does not answer.</summary>
    NotRunning,

    /// <summary>Docker Desktop on Windows runs Windows containers, whose images a .NET service's Linux ones are not.</summary>
    WindowsContainers,

    /// <summary>The engine answers, but the CLI has no <c>docker compose</c> (Compose v2).</summary>
    NoCompose,
}

/// <summary>The engine as its CLI last answered: its status and what to do about it, the server's version and system, the
/// context and its endpoint (<c>DOCKER_HOST</c> for a program that asks the engine itself), and Compose's and Buildx's
/// versions.</summary>
public sealed record EngineState(EngineStatus Status, string? Cli, string? Problem, string? ServerVersion, string? ServerOs, string? Context,
    string? Endpoint, string? ComposeVersion, string? BuildxVersion)
{
    public bool IsReady => Status == EngineStatus.Ready;

    /// <summary>Whether Docker Desktop is installed where a click can start it (macOS's app, Windows' executable).</summary>
    public static string? DesktopApp => OperatingSystem.IsMacOS() && Directory.Exists("/Applications/Docker.app") ? "/Applications/Docker.app"
        : OperatingSystem.IsWindows() && File.Exists(WindowsDesktop) ? WindowsDesktop : null;

    private static string WindowsDesktop => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "Docker Desktop.exe");
}

/// <summary>A line of <c>docker events --format json</c>: what kind of object (container, network, volume, image), what
/// happened to it, its id and its attributes (a container's name and labels among them).</summary>
public sealed record ContainerEvent(string Type, string Action, string Id, IReadOnlyDictionary<string, string> Attributes);

/// <summary>The person's container engine, through their <c>docker</c> CLI and its current context, so Docker Desktop,
/// Colima, OrbStack, Podman's Docker-compatible CLI and a remote context work alike; never the Engine API. Every command
/// goes through <see cref="ChildProcesses"/>: a question's JSON answer (<see cref="JsonAsync"/>, <see cref="JsonLinesAsync"/>),
/// a command to stream from (<see cref="Command"/>, for a console or the terminal), and the engine's events while anything
/// follows them (<see cref="Follow"/>). Once per process (<see cref="Shared"/>), but a window may be given another (a
/// test's stand-in CLI). Thread-safe; nothing here touches the UI.</summary>
public sealed class ContainerEngine
{
    /// <summary>How long a question to the engine may take before it is taken as not answering.</summary>
    public static readonly TimeSpan AskLimit = TimeSpan.FromSeconds(20);

    /// <summary>How long a state stays fresh, so the status bar, a test run and the window ask once between them.</summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(5);

    private readonly object gate = new();
    private readonly string? given;
    private readonly bool search;
    private string? found;
    private bool looked;
    private (EngineState State, long At)? last;
    private Task<EngineState>? asking;
    private readonly List<Action<ContainerEvent>> followers = [];
    private Process? events;

    /// <param name="cli">The <c>docker</c> to run; found as a terminal finds it when null and <paramref name="search"/>.</param>
    /// <param name="search">Whether to look for one: false with no <paramref name="cli"/> is an engine with no CLI at all.</param>
    public ContainerEngine(string? cli = null, bool search = true)
    {
        given = cli;
        this.search = search;
    }

    /// <summary>The process's engine: the person's <c>docker</c>.</summary>
    public static ContainerEngine Shared { get; } = new();

    /// <summary>The CLI, found once: the PATH as the login shell has it, then Docker Desktop's, OrbStack's and Rancher
    /// Desktop's own folders; null with none. It may wait for the login shell's environment, so never on the UI thread.</summary>
    public string? Cli
    {
        get
        {
            lock (gate)
            {
                if (!looked)
                {
                    found = given ?? (search ? Find() : null);
                    looked = true;
                }
                return found;
            }
        }
    }

    private static string? Find()
    {
        string name = OperatingSystem.IsWindows() ? "docker.exe" : "docker";
        if (Programs.OnPath(name) is { } onPath)
        {
            return onPath;
        }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        IEnumerable<string> folders = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "resources", "bin"), Path.Combine(home, ".rd", "bin")]
            : ["/usr/local/bin", "/opt/homebrew/bin", Path.Combine(home, ".docker", "bin"), Path.Combine(home, ".orbstack", "bin"), Path.Combine(home, ".rd", "bin"),
                "/Applications/Docker.app/Contents/Resources/bin"];
        return folders.Select(folder => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
    }

    /// <summary>A <c>docker</c> command to start (a stream into a console, a shell in the terminal); a
    /// <see cref="FileNotFoundException"/> with no CLI.</summary>
    public ProcessStartInfo Command(IEnumerable<string> arguments, string? folder = null)
    {
        string cli = Cli ?? throw new FileNotFoundException(NoCliProblem);
        ProcessStartInfo start = ChildProcesses.Command(cli, arguments, folder);
        // What the CLI says the IDE reads; no colours, nor a prompt to sign in.
        start.Environment["NO_COLOR"] = "1";
        start.Environment["DOCKER_CLI_HINTS"] = "false";
        start.Environment["COMPOSE_ANSI"] = "never";
        start.Environment["COMPOSE_STATUS_STDOUT"] = "1";
        return start;
    }

    /// <summary>Runs a command to its end, its output and errors read whole; an exit code among <paramref name="answers"/>
    /// is its answer (a check that found something), not a failure to log.</summary>
    public Task<ChildResult> RunAsync(IEnumerable<string> arguments, TimeSpan? limit = null, CancellationToken cancel = default, string? folder = null, string? input = null,
        IReadOnlyCollection<int>? answers = null) =>
        ChildProcesses.RunAsync(Command(arguments, folder), input, limit ?? AskLimit, cancel, answers);

    /// <summary>A command's output once it ends, or once <paramref name="time"/> has passed, when it is ended and what it
    /// wrote so far is its answer, with exit code 0 (a log followed for a while).</summary>
    public async Task<ChildResult> RunForAsync(IEnumerable<string> arguments, TimeSpan time, CancellationToken cancel = default)
    {
        ProcessStartInfo start = Command(arguments);
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using Process process = ChildProcesses.Start(start);
        Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        bool ended = true;
        try
        {
            await process.WaitForExitAsync(cancel).WaitAsync(time, cancel);
        }
        catch (TimeoutException)
        {
            ended = false;
        }
        catch (OperationCanceledException)
        {
            ChildProcesses.Kill(process);
            throw;
        }
        if (!ended)
        {
            ChildProcesses.Kill(process);
        }
        try
        {
            await Task.WhenAll(output, errors).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        catch (TimeoutException)
        {
            // What it left holding the pipes is the group's, ended with it.
        }
        return new ChildResult(ended ? process.ExitCode : 0, output.IsCompletedSuccessfully ? output.Result : string.Empty,
            errors.IsCompletedSuccessfully ? errors.Result : string.Empty);
    }

    /// <summary>A command's one JSON document (<c>docker inspect</c>, <c>docker version --format json</c>); a
    /// <see cref="ContainerCommandException"/> when it failed.</summary>
    public async Task<JsonElement> JsonAsync(IEnumerable<string> arguments, CancellationToken cancel = default, TimeSpan? limit = null)
    {
        ChildResult result = await RunAsync(arguments, limit, cancel);
        if (!result.Succeeded)
        {
            throw new ContainerCommandException(Said(result));
        }
        using JsonDocument document = JsonDocument.Parse(result.Output);
        return document.RootElement.Clone();
    }

    /// <summary>A command's JSON objects, one a line, as <c>docker ps --format json</c> writes them (or an array, as
    /// <c>docker compose ls --format json</c> does); a <see cref="ContainerCommandException"/> when it failed.</summary>
    public async Task<IReadOnlyList<JsonElement>> JsonLinesAsync(IEnumerable<string> arguments, CancellationToken cancel = default, TimeSpan? limit = null)
    {
        ChildResult result = await RunAsync(arguments, limit, cancel);
        if (!result.Succeeded)
        {
            throw new ContainerCommandException(Said(result));
        }
        return JsonLines(result.Output);
    }

    /// <summary>The JSON objects in a command's output: an array's items, or one object a line, other lines passed over.</summary>
    public static IReadOnlyList<JsonElement> JsonLines(string output)
    {
        List<JsonElement> found = [];
        string trimmed = output.Trim();
        if (trimmed.StartsWith('['))
        {
            using JsonDocument array = JsonDocument.Parse(trimmed);
            found.AddRange(array.RootElement.EnumerateArray().Select(e => e.Clone()));
            return found;
        }
        foreach (string line in output.Split('\n'))
        {
            // docker stats puts a terminal's codes around each line's object, even into a pipe.
            int at = line.IndexOf('{', StringComparison.Ordinal);
            int end = line.LastIndexOf('}');
            if (at < 0 || end < at)
            {
                continue;
            }
            try
            {
                using JsonDocument one = JsonDocument.Parse(line.AsMemory(at, end - at + 1));
                found.Add(one.RootElement.Clone());
            }
            catch (JsonException)
            {
                // A line of the CLI's own words, between its objects.
            }
        }
        return found;
    }

    /// <summary>What a failed command said: its errors, else its output, else its exit code.</summary>
    public static string Said(ChildResult result) =>
        (result.Error.Trim() is { Length: > 0 } error ? error : result.Output.Trim() is { Length: > 0 } output ? output : $"exit code {result.ExitCode}")
            .Replace("\r", string.Empty, StringComparison.Ordinal);

    /// <summary>The engine's state, asked again once the last answer is <see cref="Fresh"/> no more (or at once when
    /// <paramref name="again"/>); one question at a time, whoever asks.</summary>
    public Task<EngineState> StateAsync(bool again = false, CancellationToken cancel = default)
    {
        lock (gate)
        {
            if (!again && last is { } known && Stopwatch.GetElapsedTime(known.At) < Fresh)
            {
                return Task.FromResult(known.State);
            }
            if (asking is { IsCompleted: false } under)
            {
                return under.WaitAsync(cancel);
            }
            asking = Task.Run(AskStateAsync, CancellationToken.None);
            return asking.WaitAsync(cancel);
        }
    }

    /// <summary>The last state known, without asking.</summary>
    public EngineState? Known
    {
        get
        {
            lock (gate)
            {
                return last?.State;
            }
        }
    }

    /// <summary>Raised on any thread when an answer changes the engine's status.</summary>
    public event EventHandler<EngineState>? StateChanged;

    private const string NoCliProblem = "No docker command was found: install Docker Desktop, OrbStack, Colima, Rancher Desktop or Podman";

    private async Task<EngineState> AskStateAsync()
    {
        EngineState state = await Task.Run(ReadStateAsync);
        EngineState? before;
        lock (gate)
        {
            before = last?.State;
            last = (state, Stopwatch.GetTimestamp());
        }
        if (before?.Status != state.Status)
        {
            StateChanged?.Invoke(this, state);
        }
        return state;
    }

    private async Task<EngineState> ReadStateAsync()
    {
        if (Cli is not { } cli)
        {
            return new EngineState(EngineStatus.NoCli, null, NoCliProblem, null, null, null, null, null, null);
        }
        ChildResult version;
        try
        {
            version = await RunAsync(["version", "--format", "json"], AskLimit);
        }
        catch (Exception failed) when (failed is TimeoutException or System.ComponentModel.Win32Exception or IOException)
        {
            return new EngineState(EngineStatus.NotRunning, cli, $"The container engine did not answer: {failed.Message}", null, null, null, null, null, null);
        }
        (string? serverVersion, string? serverOs, string? context) = (null, null, null);
        foreach (JsonElement read in JsonLines(version.Output))
        {
            context = Text(read, "Client", "Context");
            serverVersion = Text(read, "Server", "Version");
            serverOs = Text(read, "Server", "Os");
        }
        string?[] answers = await Task.WhenAll(EndpointAsync(), ComposeAsync(), BuildxAsync());
        (string? endpoint, string? compose, string? buildx) = (answers[0], answers[1], answers[2]);
        if (!version.Succeeded || serverVersion is null)
        {
            string desktop = EngineState.DesktopApp is not null ? "start Docker Desktop" : OperatingSystem.IsLinux()
                ? "start it (sudo systemctl start docker), or another context's (docker context use)" : "start it, or choose another context (docker context use)";
            return new EngineState(EngineStatus.NotRunning, cli, $"Docker isn't running{(context is null ? string.Empty : $" (context {context})")}: {desktop}",
                null, null, context, endpoint, compose, buildx);
        }
        if (string.Equals(serverOs, "windows", StringComparison.OrdinalIgnoreCase))
        {
            return new EngineState(EngineStatus.WindowsContainers, cli, "Docker runs Windows containers: switch Docker Desktop to Linux containers",
                serverVersion, serverOs, context, endpoint, compose, buildx);
        }
        if (compose is null)
        {
            return new EngineState(EngineStatus.NoCompose, cli, "Docker Compose v2 (docker compose) is not installed beside the docker command",
                serverVersion, serverOs, context, endpoint, compose, buildx);
        }
        return new EngineState(EngineStatus.Ready, cli, null, serverVersion, serverOs, context, endpoint, compose, buildx);

        async Task<string?> EndpointAsync()
        {
            try
            {
                ChildResult inspected = await RunAsync(["context", "inspect", "--format", "json"], AskLimit);
                return JsonLines(inspected.Output).Select(c => Text(c, "Endpoints", "docker", "Host")).FirstOrDefault(h => h is not null);
            }
            catch (Exception failed) when (failed is TimeoutException or JsonException)
            {
                return null;
            }
        }

        async Task<string?> ComposeAsync()
        {
            try
            {
                ChildResult said = await RunAsync(["compose", "version", "--format", "json"], AskLimit);
                return said.Succeeded ? JsonLines(said.Output).Select(v => Text(v, "version")).FirstOrDefault() : null;
            }
            catch (Exception failed) when (failed is TimeoutException or JsonException)
            {
                return null;
            }
        }

        async Task<string?> BuildxAsync()
        {
            try
            {
                ChildResult said = await RunAsync(["buildx", "version"], AskLimit);
                return said.Succeeded ? said.Output.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) : null;
            }
            catch (TimeoutException)
            {
                return null;
            }
        }
    }

    /// <summary>A string at a path of properties, or null.</summary>
    public static string? Text(JsonElement element, params string[] path)
    {
        foreach (string name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return null;
            }
        }
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
            _ => null,
        };
    }

    /// <summary>Follows the engine's events for as long as the answer is kept: <c>docker events --format json</c> runs while
    /// anyone follows, each line given to every follower on a worker thread; it ends with the last follower, or by itself
    /// when the engine stops (a follower then hears nothing more until it follows again).</summary>
    public IDisposable Follow(Action<ContainerEvent> follower)
    {
        lock (gate)
        {
            followers.Add(follower);
            if (events is null)
            {
                StartEvents();
            }
        }
        return new Following(this, follower);
    }

    private sealed class Following(ContainerEngine engine, Action<ContainerEvent> follower) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                engine.Unfollow(follower);
            }
        }
    }

    private void Unfollow(Action<ContainerEvent> follower)
    {
        Process? ending = null;
        lock (gate)
        {
            followers.Remove(follower);
            if (followers.Count == 0)
            {
                ending = events;
                events = null;
            }
        }
        if (ending is not null)
        {
            ChildProcesses.Kill(ending);
        }
    }

    private void StartEvents()
    {
        Process process;
        try
        {
            ProcessStartInfo start = Command(["events", "--format", "json"]);
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            process = ChildProcesses.Start(start);
        }
        catch (Exception failed) when (failed is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return;
        }
        events = process;
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                {
                    if (Event(line) is not { } told)
                    {
                        continue;
                    }
                    Action<ContainerEvent>[] now;
                    lock (gate)
                    {
                        now = [.. followers];
                    }
                    foreach (Action<ContainerEvent> follower in now)
                    {
                        follower(told);
                    }
                }
            }
            catch (Exception ended) when (ended is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Ended under its read.
            }
            lock (gate)
            {
                if (events == process)
                {
                    events = null;
                }
            }
            process.Dispose();
        });
    }

    private static ContainerEvent? Event(string line)
    {
        try
        {
            using JsonDocument read = JsonDocument.Parse(line);
            JsonElement root = read.RootElement;
            Dictionary<string, string> attributes = [];
            if (root.TryGetProperty("Actor", out JsonElement actor) && actor.TryGetProperty("Attributes", out JsonElement given) && given.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in given.EnumerateObject())
                {
                    attributes[property.Name] = property.Value.ToString();
                }
            }
            return new ContainerEvent(Text(root, "Type") ?? string.Empty, Text(root, "Action") ?? Text(root, "status") ?? string.Empty,
                Text(root, "Actor", "ID") ?? Text(root, "id") ?? string.Empty, attributes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A command's words as the log and an error give them.</summary>
    public static string Words(IEnumerable<string> arguments)
    {
        StringBuilder words = new("docker");
        foreach (string word in arguments)
        {
            words.Append(' ').Append(word.Contains(' ', StringComparison.Ordinal) ? $"\"{word}\"" : word);
        }
        return words.ToString();
    }
}

/// <summary>A <c>docker</c> command that failed, with what it said.</summary>
public sealed class ContainerCommandException(string message) : Exception(message);
