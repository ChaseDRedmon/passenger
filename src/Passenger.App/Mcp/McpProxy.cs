using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Passenger.Core;

namespace Passenger.App.Mcp;

/// <summary><c>--mcp-attach [PID] [FOLDER]</c>: the agent's server for its whole session, standing in for a person's
/// window. It answers initialize and tools/list itself, from the build's own tools (<see cref="McpHost.ToolMethods"/>),
/// and joins a window only when a tool is called: the innermost whose folder holds FOLDER (else the agent's working
/// folder), never one on an unrelated folder; with a PID, that process's windows alone, the newest when none holds it.
/// With no window to join it has the running Passenger open the folder (<see cref="AppSocket"/>), else starts Passenger
/// on it, and waits for that folder's window, which lets the agent in or asks the person first. A window that closes is
/// joined again, or another started, at the next call. Resources and prompts are none, here as in the window.</summary>
public sealed class McpProxy
{
    /// <summary>The test run's: <c>headless</c> has a call with no window start this build headless, as its own child, in
    /// the state PASSENGER_SETTINGS names, never the person's installed Passenger.</summary>
    public const string StartVariable = "PASSENGER_START";

    /// <summary>How long a call waits for a Passenger started or asked to open the folder to show its window.</summary>
    private static readonly TimeSpan StartLimit = TimeSpan.FromMinutes(2);

    private readonly int? processId;
    private readonly string here;
    private readonly SemaphoreSlim joining = new(1, 1);
    private McpClient? joined;

    /// <summary>The socket <see cref="joined"/> talks over.</summary>
    private Socket? joinedSocket;

    /// <summary>The headless Passenger a test run's call started, which ends with this one.</summary>
    private Process? headless;

    private McpProxy(int? processId, string here)
    {
        this.processId = processId;
        this.here = here;
    }

    /// <summary>Fails a start whose <see cref="StartVariable"/> is not one this build knows, as a malformed flag does.</summary>
    public static void CheckVariable()
    {
        if (Environment.GetEnvironmentVariable(StartVariable) is { Length: > 0 } how && how != "headless")
        {
            throw new FormatException($"{StartVariable} takes headless (the test run's), not '{how}'");
        }
    }

    /// <summary>Serves the agent on standard input and output until it goes, then lets go of the window it joined.</summary>
    public static int Run(int? processId, string? folder)
    {
        Stream input = Console.OpenStandardInput();
        Stream output = Console.OpenStandardOutput();
        // Standard output is the protocol's alone: anything else written to the console goes to stderr.
        Console.SetOut(Console.Error);
        McpProxy proxy = new(processId, PathNames.Canonical(folder ?? Environment.CurrentDirectory));
        McpServerPrimitiveCollection<McpServerTool> tools = [];
        foreach (MethodInfo method in McpHost.ToolMethods())
        {
            // Never called: each call goes on to the window's own tool of the name.
            tools.Add(new ForwardedTool(McpServerTool.Create(method, _ => throw new InvalidOperationException("The proxy runs no tool itself"), McpHost.ToolCreateOptions()), proxy));
        }
        McpServerOptions options = new()
        {
            ServerInfo = McpHost.ServerInfo,
            ServerInstructions = McpHost.Instructions,
            ToolCollection = tools,
        };
        using CancellationTokenSource gone = new();
        StreamServerTransport transport = new(input, output, "passenger");
        McpServer server = McpServer.Create(transport, options);
        Task running = server.RunAsync(gone.Token);
        // The server waits out the calls still going when the agent goes, so its going is watched for here.
        Task.WaitAny(running, transport.MessageReader.Completion);
        gone.Cancel();
        proxy.Leave();
        try
        {
            running.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // What the agent's going cut short.
        }
        return 0;
    }

    /// <summary>One call of the agent's, on the window it is joined to.</summary>
    private async Task<CallToolResult> CallAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancel)
    {
        string tool = request.Params?.Name ?? "a tool";
        McpClient window;
        try
        {
            window = await JoinAsync(request.Server.ClientInfo, cancel);
        }
        catch (NotJoined why)
        {
            return Refused(why.Message);
        }
        RequestId id = new($"agent-{Guid.NewGuid():N}");
        Task<CallToolResult> answer = window.SendRequestAsync<CallToolRequestParams, CallToolResult>(RequestMethods.ToolsCall, request.Params!, requestId: id).AsTask();
        try
        {
            return await answer.WaitAsync(cancel);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            // The agent gave up: the window is told, as the protocol has it, and stops waiting on the call.
            try
            {
                await window.SendNotificationAsync(NotificationMethods.CancelledNotification, new CancelledNotificationParams { RequestId = id });
            }
            catch (Exception error) when (error is IOException or McpException or ObjectDisposedException or InvalidOperationException)
            {
                // The window went too.
            }
            throw;
        }
        catch (Exception error) when (window.Completion.IsCompleted && error is not OperationCanceledException)
        {
            return Refused($"The Passenger window closed before it answered {tool}; a later call joins one again.");
        }
    }

    /// <summary>The window the agent works in: the one joined, while it stays, else one over the folder, which a running
    /// Passenger is asked to open or a new one started on, waited for.</summary>
    private async Task<McpClient> JoinAsync(Implementation? agent, CancellationToken cancel)
    {
        await joining.WaitAsync(cancel);
        try
        {
            if (joined is { Completion.IsCompleted: false } live)
            {
                // A window that closed has closed its end already, which the session may not have read yet: it ends soon after.
                if (Closed(joinedSocket))
                {
                    await Task.WhenAny(live.Completion, Task.Delay(TimeSpan.FromSeconds(1), cancel));
                }
                if (!live.Completion.IsCompleted)
                {
                    return live;
                }
            }
            if (joined is not null)
            {
                await Quietly(joined);
                joined = null;
            }
            NetworkStream stream = Connect() ?? await StartAsync(cancel);
            try
            {
                // The agent's own name, which the window shows the person when it asks.
                joined = await McpClient.CreateAsync(new StreamClientTransport(stream, stream),
                    new McpClientOptions { ClientInfo = agent ?? new Implementation { Name = "An agent", Version = "0" } }, cancellationToken: cancel);
                joinedSocket = stream.Socket;
            }
            catch (Exception error) when (error is IOException or McpException or InvalidOperationException
                || (error is OperationCanceledException && !cancel.IsCancellationRequested))
            {
                await stream.DisposeAsync();
                throw new NotJoined($"The Passenger window on {here} closed as the agent joined it; a later call tries again.");
            }
            return joined;
        }
        finally
        {
            joining.Release();
        }
    }

    /// <summary>Whether the window's end of a connection looks closed, readable with nothing to read; only a hint, since
    /// the session's own reader may have taken what was there.</summary>
    private static bool Closed(Socket? socket)
    {
        try
        {
            return socket is null || (socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
        }
        catch (Exception error) when (error is SocketException or ObjectDisposedException)
        {
            return true;
        }
    }

    /// <summary>A connection to the window to join, or null when no window of Passenger's is over the folder.</summary>
    private NetworkStream? Connect()
    {
        McpHost.RemoveStale();
        foreach (string path in Windows())
        {
            Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.Connect(new UnixDomainSocketEndPoint(path));
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
        }
        return null;
    }

    /// <summary>The windows' sockets an agent here may join, the best first: those whose folder holds the agent's, the
    /// innermost first, then the newest; with a process named, any of its windows, the newest after those.</summary>
    private IEnumerable<string> Windows()
    {
        if (!Directory.Exists(McpHost.SocketFolder))
        {
            return [];
        }
        return Directory.EnumerateFiles(McpHost.SocketFolder, "*.sock")
            .Where(path => processId is not int pid || McpHost.ProcessOf(path) == pid)
            .Select(path => (Path: path, Holding: McpHost.Holding(McpHost.WindowFolder(path), here)))
            .Where(socket => processId is not null || socket.Holding >= 0)
            .OrderByDescending(socket => socket.Holding)
            .ThenByDescending(socket => File.GetLastWriteTimeUtc(socket.Path))
            .Select(socket => socket.Path)
            .ToList();
    }

    /// <summary>Has a Passenger open the folder, the running one's or one started on it, and joins its window once it
    /// listens.</summary>
    private async Task<NetworkStream> StartAsync(CancellationToken cancel)
    {
        if (processId is int pid)
        {
            throw new NotJoined($"Passenger {pid} has no window an agent can join. Open {here} in it, or attach without a process id to have Passenger open it.");
        }
        if (!Directory.Exists(here))
        {
            throw new NotJoined($"{here} is no folder for Passenger to open.");
        }
        // An agent started in no folder of the person's (Claude Desktop's is the disk's root) has no project to open.
        if (PathNames.Same(here, Path.GetPathRoot(here) ?? here) || PathNames.Same(here, PathNames.Canonical(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))))
        {
            throw new NotJoined($"The agent works in {here}, which is no project folder for Passenger to open. Give --mcp-attach the folder to work in "
                + "(--mcp-attach FOLDER), or start the agent in it.");
        }
        if (await AppSocket.AskAsync(here, cancel) is null)
        {
            await LaunchAsync(cancel);
        }
        long began = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(began) < StartLimit)
        {
            if (Connect() is { } stream)
            {
                return stream;
            }
            if (headless is { HasExited: true } ended)
            {
                throw new NotJoined($"The Passenger started on {here} ended with exit code {ended.ExitCode} before its window opened.");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancel);
        }
        throw new NotJoined($"No Passenger window opened on {here} within {StartLimit.TotalSeconds:0} s.");
    }

    /// <summary>Starts Passenger on the folder: the installed app this is, through LaunchServices on macOS (which hands it
    /// to a Passenger already running there), else apart from this process, which it outlives; the test run's headless,
    /// as a child.</summary>
    private async Task LaunchAsync(CancellationToken cancel)
    {
        string self = Environment.ProcessPath ?? throw new NotJoined("Passenger cannot tell where it is installed, to start it.");
        if (Environment.GetEnvironmentVariable(StartVariable) == "headless")
        {
            ProcessStartInfo start = ChildProcesses.Command(self, ["--headless", here], here);
            // Its input kept: its end, with this process, ends it; its output is never the agent's protocol.
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            headless = ChildProcesses.Start(start);
            _ = headless.StandardOutput.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
            return;
        }
        if (OperatingSystem.IsMacOS() && Bundle(self) is { } app)
        {
            ChildResult opened = await ChildProcesses.RunAsync(ChildProcesses.Command("/usr/bin/open", ["-a", app, here]), limit: TimeSpan.FromSeconds(30), cancel: cancel);
            if (!opened.Succeeded)
            {
                throw new NotJoined($"LaunchServices did not start Passenger: {opened.Error.Trim()}");
            }
            return;
        }
        try
        {
            ChildProcesses.StartApart(ChildProcesses.Command(self, [here], here));
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new NotJoined($"Passenger did not start: {error.Message}");
        }
    }

    /// <summary>The app bundle an executable is inside (<c>X.app/Contents/MacOS/Passenger</c>), or null.</summary>
    private static string? Bundle(string executable)
    {
        string? macOS = Path.GetDirectoryName(executable);
        string? contents = macOS is null ? null : Path.GetDirectoryName(macOS);
        string? app = contents is null ? null : Path.GetDirectoryName(contents);
        return app is not null && app.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(macOS) == "MacOS" ? app : null;
    }

    /// <summary>The agent went: the window it joined sees it go.</summary>
    private void Leave()
    {
        if (joined is { } window)
        {
            Quietly(window).Wait(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task Quietly(McpClient window)
    {
        try
        {
            await window.DisposeAsync();
        }
        catch (Exception error) when (error is IOException or McpException or ObjectDisposedException or OperationCanceledException)
        {
            // Gone already.
        }
    }

    private static CallToolResult Refused(string why) => new() { IsError = true, Content = [new TextContentBlock { Text = why }] };

    /// <summary>Why the agent's call found no window to go on to.</summary>
    private sealed class NotJoined(string message) : Exception(message);

    /// <summary>A tool as the build defines it, listed by the proxy itself, its calls going on to the window's.</summary>
    private sealed class ForwardedTool(McpServerTool tool, McpProxy proxy) : DelegatingMcpServerTool(tool)
    {
        public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default) =>
            new(proxy.CallAsync(request, cancellationToken));
    }
}
