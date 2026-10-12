using System.Net.Sockets;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Passenger.App.Mcp;

/// <summary>Passenger as a Model Context Protocol server, three ways. <c>passenger --mcp [FOLDER]</c> is an
/// IDE with no window, its tools on standard input and output, for an agent to start as its own. The
/// window the user works in listens on a socket only its user can reach (<see cref="Listen"/>), and
/// <c>passenger --mcp-attach [PID]</c> joins an agent's standard input and output to it, so the agent
/// drives the IDE the user is looking at. Each connection is a server of its own over the same tools.</summary>
public static class McpHost
{
    /// <summary>The IDE has no window, only the headless platform's (whose frames a screenshot takes).</summary>
    public static bool IsHeadless { get; private set; }

    /// <summary>Where running IDEs listen: one socket each, named for its process.</summary>
    public static string SocketFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Passenger", "mcp");

    private const string Instructions =
        "Passenger is a C# IDE. These tools act on its open workspace as its user would: open a folder or solution, ask the C# " +
        "language server about code (diagnostics, symbol_info, definition, usages, rename, code_actions), build, run, and debug " +
        "(breakpoint_set, debug_start, debug_step, debug_evaluate, debug_variables), and see or drive the window (screenshot, " +
        "ide_action). Lines and columns count from 1; relative paths are the open folder's. Debugging tools wait for the program " +
        "to stop and say where it is, with the top frame's locals.";

    public static McpServerOptions Options(MainWindow window)
    {
        PassengerTools tools = new(window);
        McpServerPrimitiveCollection<McpServerTool> collection = [];
        foreach (MethodInfo method in typeof(PassengerTools).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null))
        {
            collection.Add(McpServerTool.Create(method, tools));
        }
        return new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "passenger",
                Title = "Passenger",
                Version = typeof(McpHost).Assembly.GetName().Version?.ToString() ?? "0",
            },
            ServerInstructions = Instructions,
            ToolCollection = collection,
        };
    }

    /// <summary><c>--mcp</c>: the IDE with no window, its tools on standard input and output until the
    /// agent closes them.</summary>
    public static int RunStdio(LaunchOptions launch)
    {
        IsHeadless = true;
        Stream input = Console.OpenStandardInput();
        Stream output = Console.OpenStandardOutput();
        // Standard output is the protocol's alone: anything else written to the console goes to stderr.
        Console.SetOut(Console.Error);

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .AfterSetup(builder => Program.ApplyTheme(builder.Instance!, launch.Theme))
            .SetupWithoutStarting();

        MainWindow window = new() { Width = 1600, Height = 1000 };
        window.SetRenderScaling(2);
        window.Show();
        window.Start(launch);

        using CancellationTokenSource done = new();
        McpServer server = McpServer.Create(new StreamServerTransport(input, output, "passenger"), Options(window));
        _ = Task.Run(async () =>
        {
            try
            {
                await server.RunAsync(done.Token);
            }
            finally
            {
                Dispatcher.UIThread.Post(done.Cancel);
            }
        });
        // The UI thread runs the dispatcher's loop, timers and all, until the agent goes.
        Dispatcher.UIThread.MainLoop(done.Token);
        _ = window.Debugger.StopAsync();
        window.Close();
        return 0;
    }

    /// <summary>Listens for agents on this IDE's socket, each connection a server of its own, until
    /// the window closes.</summary>
    public static void Listen(MainWindow window)
    {
        Directory.CreateDirectory(SocketFolder);
        if (!OperatingSystem.IsWindows())
        {
            // The folder is the user's alone: nobody else reaches the IDE through it.
            File.SetUnixFileMode(SocketFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        RemoveStale();
        string path = Path.Combine(SocketFolder, $"{Environment.ProcessId}.sock");
        File.Delete(path);
        Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(4);
        CancellationTokenSource closed = new();
        window.Closed += (_, _) =>
        {
            closed.Cancel();
            listener.Dispose();
            File.Delete(path);
        };
        _ = Task.Run(async () =>
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
                _ = Serve(window, client, closed.Token);
            }
        });
    }

    private static async Task Serve(MainWindow window, Socket client, CancellationToken closed)
    {
        await using NetworkStream stream = new(client, ownsSocket: true);
        McpServerOptions options = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            window.Agents++;
            return Options(window);
        });
        await using McpServer server = McpServer.Create(new StreamServerTransport(stream, stream, "passenger"), options);
        try
        {
            await server.RunAsync(closed);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or SocketException)
        {
        }
        finally
        {
            Dispatcher.UIThread.Post(() => window.Agents--);
        }
    }

    /// <summary><c>--mcp-attach [PID]</c>: joins standard input and output to a running IDE's socket, the
    /// one named or the newest that answers, until either side closes.</summary>
    public static int Attach(int? processId)
    {
        RemoveStale();
        IEnumerable<string> candidates = processId is int pid
            ? [Path.Combine(SocketFolder, $"{pid}.sock")]
            : Directory.Exists(SocketFolder)
                ? Directory.EnumerateFiles(SocketFolder, "*.sock").OrderByDescending(File.GetLastWriteTimeUtc)
                : [];
        foreach (string path in candidates)
        {
            Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.Connect(new UnixDomainSocketEndPoint(path));
            }
            catch (SocketException)
            {
                socket.Dispose();
                continue;
            }
            using NetworkStream stream = new(socket, ownsSocket: true);
            Stream input = Console.OpenStandardInput();
            Stream output = Console.OpenStandardOutput();
            Task.WaitAny(
                Task.Run(() => input.CopyTo(stream)),
                Task.Run(() =>
                {
                    stream.CopyTo(output);
                    output.Flush();
                }));
            return 0;
        }
        Console.Error.WriteLine(processId is int wanted
            ? $"passenger: no Passenger with process {wanted} is listening"
            : "passenger: no running Passenger is listening; start one, or use --mcp FOLDER for one with no window");
        return 1;
    }

    /// <summary>Takes away the sockets of IDEs that are no longer running.</summary>
    private static void RemoveStale()
    {
        if (!Directory.Exists(SocketFolder))
        {
            return;
        }
        foreach (string path in Directory.EnumerateFiles(SocketFolder, "*.sock"))
        {
            if (int.TryParse(Path.GetFileNameWithoutExtension(path), out int pid) && !Running(pid))
            {
                File.Delete(path);
            }
        }

        static bool Running(int pid)
        {
            try
            {
                using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
