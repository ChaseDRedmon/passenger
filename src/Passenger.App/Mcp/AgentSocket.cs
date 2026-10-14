using System.Net.Sockets;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Passenger.App.Mcp;

/// <summary>A window's socket for agents, open while the person lets them in: in <see cref="McpHost.SocketFolder"/>,
/// named for the process and the window so two windows of one Passenger never take each other's, with the window's
/// folder written beside it for <c>--mcp-attach</c> to choose by. Closing it ends every agent's connection.</summary>
public sealed class AgentSocket : IDisposable
{
    /// <summary>The sockets the process has opened, every window's, which number each one's name apart.</summary>
    private static int opened;

    private readonly MainWindow window;
    private readonly Socket listener;
    private readonly CancellationTokenSource closed = new();

    private AgentSocket(MainWindow window, string path, Socket listener)
    {
        this.window = window;
        this.listener = listener;
        Path = path;
        // Not held: it listens for as long as the window does.
        _ = Task.Run(AcceptAsync);
    }

    /// <summary>The socket's file.</summary>
    public string Path { get; }

    /// <summary>Listens for the window's agents, or throws saying why it cannot.</summary>
    public static AgentSocket Open(MainWindow window)
    {
        string folder = McpHost.SocketFolder;
        Directory.CreateDirectory(folder);
        if (!OperatingSystem.IsWindows())
        {
            // The folder is the user's alone: nobody else reaches the IDE through it.
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        McpHost.RemoveStale();
        string path = System.IO.Path.Combine(folder, $"{Environment.ProcessId}-{Interlocked.Increment(ref opened)}.sock");
        // Only a dead Passenger that had this process's number leaves one by this name.
        File.Delete(path);
        Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(4);
        }
        catch
        {
            listener.Dispose();
            throw;
        }
        return new AgentSocket(window, path, listener);
    }

    /// <summary>Writes the window's open folder beside the socket, as it opens and changes.</summary>
    public void Describe(string? folder)
    {
        try
        {
            File.WriteAllText(McpHost.FolderFile(Path), folder ?? string.Empty);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Unnamed, the window is still reached: only no longer chosen for its folder.
        }
    }

    public void Dispose()
    {
        if (closed.IsCancellationRequested)
        {
            return;
        }
        closed.Cancel();
        listener.Dispose();
        File.Delete(Path);
        File.Delete(McpHost.FolderFile(Path));
    }

    private async Task AcceptAsync()
    {
        while (!closed.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync(closed.Token);
            }
            catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(Socket client)
    {
        using CancellationTokenSource gone = CancellationTokenSource.CreateLinkedTokenSource(closed.Token);
        AgentConnection connection = new(gone.Token, attached: true);
        bool counted = false;
        try
        {
            await using NetworkStream stream = new(client, ownsSocket: true);
            McpServerOptions options = await UiThread.InvokeAsync(() =>
            {
                window.Agents++;
                counted = true;
                return McpHost.Options(window, connection);
            });
            StreamServerTransport transport = new(stream, stream, "passenger");
            await using McpServer server = McpServer.Create(transport, options);
            Task running = server.RunAsync(gone.Token);
            // The server waits out the calls still running when the agent goes, so its going is watched for here.
            await Task.WhenAny(running, transport.MessageReader.Completion);
            await gone.CancelAsync();
            await running;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or SocketException)
        {
        }
        finally
        {
            if (counted)
            {
                UiThread.Post(() => window.Agents--);
            }
        }
    }
}

/// <summary>One agent's connection: its calls end when it does, and on a person's window (attached) a tool that changes
/// something waits the first time for the person to allow it. UI thread only, but for <see cref="Gone"/>.</summary>
public sealed class AgentConnection(CancellationToken gone, bool attached)
{
    private readonly Dictionary<string, Task<bool>> answers = new(StringComparer.Ordinal);

    /// <summary>Cancelled when the agent goes, or the window stops letting agents in.</summary>
    public CancellationToken Gone { get; } = gone;

    /// <summary>A person's window, rather than the agent's own IDE.</summary>
    public bool Attached { get; } = attached;

    /// <summary>Whether the person lets this agent use a tool: asked the first time, and again after a no.</summary>
    internal async Task<bool> AllowAsync(MainWindow window, Tool tool, IDictionary<string, System.Text.Json.JsonElement>? arguments, CancellationToken cancel)
    {
        UiThread.Check();
        if (!answers.TryGetValue(tool.Name, out Task<bool>? answer))
        {
            answer = window.AllowAgentAsync(tool.Name, tool.Title ?? tool.Name, [.. (arguments ?? new Dictionary<string, System.Text.Json.JsonElement>()).Select(a => (a.Key, Shown(a.Value)))], cancel);
            answers[tool.Name] = answer;
        }
        if (await answer)
        {
            return true;
        }
        if (answers.TryGetValue(tool.Name, out Task<bool>? now) && now == answer)
        {
            answers.Remove(tool.Name);
        }
        return false;
    }

    private static string Shown(System.Text.Json.JsonElement value) =>
        value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
}

/// <summary>A tool as one connection reaches it: its call cancelled with the connection, and on a person's window first
/// allowed by them when it changes something (not read-only nor declared harmless), and named in the status bar while it
/// runs.</summary>
internal sealed class ConnectedTool(McpServerTool tool, MainWindow window, AgentConnection connection) : DelegatingMcpServerTool(tool)
{
    public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.Gone);
        if (!connection.Attached)
        {
            return await base.InvokeAsync(request, call.Token);
        }
        Tool shown = ProtocolTool;
        bool readOnly = shown.Annotations is { ReadOnlyHint: true };
        if (!readOnly && shown.Annotations is not { DestructiveHint: false }
            && !await UiThread.InvokeAsync(() => connection.AllowAsync(window, shown, request.Params?.Arguments, call.Token)))
        {
            call.Token.ThrowIfCancellationRequested();
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"The person at this Passenger window did not allow {shown.Name}. Ask them, or do without it." }],
            };
        }
        if (readOnly)
        {
            return await base.InvokeAsync(request, call.Token);
        }
        string title = shown.Title ?? shown.Name;
        UiThread.Post(() => window.AgentWorkStarted(title));
        try
        {
            return await base.InvokeAsync(request, call.Token);
        }
        finally
        {
            UiThread.Post(() => window.AgentWorkEnded(title));
        }
    }
}
