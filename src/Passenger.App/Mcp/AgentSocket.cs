using System.Net.Sockets;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Passenger.App.Mcp;

/// <summary>A window's socket for agents, open while the window may let them in at all (not <c>--no-mcp</c>): in <see
/// cref="McpHost.SocketFolder"/>, which only the person's own processes reach, named for the process and the window so two
/// windows of one Passenger never take each other's, with the window's folder written beside it for <c>--mcp-attach</c>
/// to choose by. A connection is served from the start, but each of its tool calls waits until it is let in: by Enable
/// MCP Server, by the folder's Always, or by the person's Allow (<see cref="AgentConnection.AdmitAsync"/>), so a process
/// that connects learns only the build's own tool list until the person says so. Closing it ends every connection.</summary>
public sealed class AgentSocket : IDisposable
{
    /// <summary>The sockets the process has opened, every window's, which number each one's name apart.</summary>
    private static int opened;

    private readonly MainWindow window;
    private readonly Socket listener;
    private readonly CancellationTokenSource closed = new();

    /// <summary>Cancelled, and made anew, when the person shuts out the agents connected (Enable MCP Server turned off).</summary>
    private CancellationTokenSource letIn = new();

    /// <summary>The agents' connections, closed at once when they are shut out or the window closes, so an agent's next
    /// call finds the window gone rather than sending into its end.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Socket, byte> clients = new();

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
        // The folder is the user's alone: nobody else reaches the IDE through it.
        string folder = McpHost.OwnSocketFolder();
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

    /// <summary>Ends every agent's connection to the window; each is asked to be let in again at its next call.</summary>
    public void ShutOut()
    {
        Interlocked.Exchange(ref letIn, new CancellationTokenSource()).Cancel();
        Hangup();
    }

    public void Dispose()
    {
        if (closed.IsCancellationRequested)
        {
            return;
        }
        closed.Cancel();
        listener.Dispose();
        Hangup();
        File.Delete(Path);
        File.Delete(McpHost.FolderFile(Path));
    }

    private void Hangup()
    {
        foreach (Socket client in clients.Keys)
        {
            try
            {
                client.Shutdown(SocketShutdown.Both);
            }
            catch (Exception error) when (error is SocketException or ObjectDisposedException)
            {
                // Gone already.
            }
        }
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
        using CancellationTokenSource gone = CancellationTokenSource.CreateLinkedTokenSource(closed.Token, letIn.Token);
        AgentConnection connection = new(gone.Token, attached: true);
        clients[client] = 0;
        try
        {
            await using NetworkStream stream = new(client, ownsSocket: true);
            McpServerOptions options = await UiThread.InvokeAsync(() => McpHost.Options(window, connection));
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
            clients.TryRemove(client, out _);
            UiThread.Post(() => connection.Left(window));
        }
    }
}

/// <summary>What the person said to an agent asking to work in the window.</summary>
public enum Admission
{
    Allowed,
    NotAllowed,

    /// <summary>Nobody answered within the window's limit, or the agent went first.</summary>
    Unanswered,
}

/// <summary>One agent's connection: its calls end when it does; on a person's window (attached) it is let in before its
/// first call goes on, and a tool that changes something waits the first time for the person to allow it. UI thread
/// only, but for <see cref="Gone"/>.</summary>
public sealed class AgentConnection(CancellationToken gone, bool attached)
{
    private readonly Dictionary<string, Task<bool>> answers = new(StringComparer.Ordinal);

    /// <summary>The question whether the agent may work in the window, while the person is asked it.</summary>
    private Task<Admission>? asking;

    /// <summary>Cancelled when the agent goes, or the window stops letting agents in.</summary>
    public CancellationToken Gone { get; } = gone;

    /// <summary>A person's window, rather than the agent's own IDE.</summary>
    public bool Attached { get; } = attached;

    /// <summary>The agent as its client names itself (initialize's clientInfo: its title, else its name), once it has
    /// called a tool.</summary>
    public string Agent { get; private set; } = "An agent";

    /// <summary>Let into the window, and so counted in its status bar until it goes.</summary>
    public bool Admitted { get; private set; }

    /// <summary>Takes the agent's name from its client's own words.</summary>
    internal void Named(Implementation? client)
    {
        if (client is not null && (client.Title is { Length: > 0 } title ? title : client.Name) is { Length: > 0 } name)
        {
            Agent = name;
        }
    }

    /// <summary>Lets the agent into a person's window before its call goes on, or says why not: at once while Enable MCP
    /// Server is on, the folder's Always holds or nobody is at the window; else the person is asked, once for every call
    /// waiting, for at most the window's limit. A no, or no answer, is the call's answer, and a later call asks again.</summary>
    internal async Task<string?> AdmitAsync(MainWindow window, CancellationToken cancel)
    {
        UiThread.Check();
        if (!Attached || Admitted)
        {
            return null;
        }
        if (window.LetsAgentsIn)
        {
            Admit(window);
            return null;
        }
        // A no that came after its calls gave up is no answer to this one, which asks again.
        if (asking is { IsCompletedSuccessfully: true } past && past.Result != Admission.Allowed)
        {
            asking = null;
        }
        asking ??= window.AskToLetInAsync(Agent, Gone);
        Task<Admission> answer = asking;
        Admission said = await answer.WaitAsync(cancel);
        if (said == Admission.Allowed)
        {
            Admit(window);
            return null;
        }
        if (asking == answer)
        {
            asking = null;
        }
        string where = window.Folder is { } folder ? $"the Passenger window on {folder}" : "the Passenger window";
        return said == Admission.NotAllowed
            ? $"The person at {where} did not let {Agent} work there. Ask them, or do without it; a later call asks them again."
            : $"Nobody at {where} answered within {window.AgentAnswerLimit.TotalSeconds:0} s whether {Agent} may work there. A later call asks again.";
    }

    /// <summary>The connection ended: the window stops counting it.</summary>
    internal void Left(MainWindow window)
    {
        UiThread.Check();
        if (Admitted)
        {
            Admitted = false;
            window.Agents--;
        }
    }

    /// <summary>Whether the person lets this agent use a tool: asked the first time, and again after a no; never on a
    /// window nobody is at (<see cref="MainWindow.AgentsUnattended"/>), whose agent uses them all.</summary>
    internal async Task<bool> AllowAsync(MainWindow window, Tool tool, IDictionary<string, System.Text.Json.JsonElement>? arguments, CancellationToken cancel)
    {
        UiThread.Check();
        if (window.AgentsUnattended)
        {
            return true;
        }
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

    private void Admit(MainWindow window)
    {
        if (Admitted || Gone.IsCancellationRequested)
        {
            return;
        }
        Admitted = true;
        window.Agents++;
    }

    private static string Shown(System.Text.Json.JsonElement value) =>
        value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
}

/// <summary>A tool as one connection reaches it: its call cancelled with the connection, and on a person's window held
/// until the agent is let in, then first allowed by them when it changes something (not read-only nor declared harmless),
/// and named in the status bar while it runs.</summary>
internal sealed class ConnectedTool(McpServerTool tool, MainWindow window, AgentConnection connection) : DelegatingMcpServerTool(tool)
{
    public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.Gone);
        if (!connection.Attached)
        {
            return await base.InvokeAsync(request, call.Token);
        }
        // Read-only or not, no call goes on before the agent is let into the window.
        string? refused = await UiThread.InvokeAsync(() =>
        {
            connection.Named(request.Server.ClientInfo);
            return connection.AdmitAsync(window, call.Token);
        });
        if (refused is not null)
        {
            call.Token.ThrowIfCancellationRequested();
            return Refused(refused);
        }
        Tool shown = ProtocolTool;
        bool readOnly = shown.Annotations is { ReadOnlyHint: true };
        if (!readOnly && shown.Annotations is not { DestructiveHint: false }
            && !await UiThread.InvokeAsync(() => connection.AllowAsync(window, shown, request.Params?.Arguments, call.Token)))
        {
            call.Token.ThrowIfCancellationRequested();
            return Refused($"The person at this Passenger window did not allow {shown.Name}. Ask them, or do without it.");
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

    private static CallToolResult Refused(string why) => new() { IsError = true, Content = [new TextContentBlock { Text = why }] };
}
