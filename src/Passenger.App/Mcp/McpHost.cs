using System.Net.Sockets;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Passenger.App.State;
using Passenger.App.Workspace;
using Passenger.Core;

namespace Passenger.App.Mcp;

/// <summary>Passenger as a Model Context Protocol server, three ways. <c>passenger --mcp [FOLDER]</c> is an IDE with no
/// window, its tools on standard input and output, for an agent to start as its own. A window the person has turned
/// Enable MCP Server on for listens on a socket of its own (<see cref="AgentSocket"/>), and <c>passenger --mcp-attach
/// [PID]</c> joins an agent's standard input and output to the one over the folder it works in, so the agent drives the
/// IDE the person is looking at. Each connection is a server of its own over the same tools.</summary>
public static class McpHost
{
    /// <summary>The IDE has no window, only the headless platform's (whose frames a screenshot takes). The process's, as
    /// the platform is: <c>--mcp</c> runs on the headless one alone.</summary>
    public static bool IsHeadless { get; private set; }

    /// <summary>Where windows listen, one socket each: in the IDE's state folder (PASSENGER_SETTINGS moves it), which
    /// only its user can reach.</summary>
    public static string SocketFolder => Path.Combine(StateStore.DefaultRoot, "mcp");

    private const string Instructions =
        "Passenger is a C# IDE. These tools act on its open workspace as its user would: open a folder or solution, ask the C# " +
        "language server about code (diagnostics, symbol_info, definition, usages, rename, code_actions), build, run, and debug " +
        "(breakpoint_set, debug_start or debug_attach, debug_step, debug_evaluate, debug_variables), see or drive the window (screenshot, " +
        "ide_action), and look at and drive the Avalonia app it runs or debugs as a person tests it (app_screenshot, app_tree, app_click, " +
        "app_type, app_key, app_scroll, app_binding_errors). Lines and columns count from 1; relative paths are the open folder's. Debugging " +
        "tools wait for the program to stop and say where it is, with the top frame's locals.";

    /// <summary>The tools as one connection reaches them (<see cref="ConnectedTool"/>).</summary>
    public static McpServerOptions Options(MainWindow window, AgentConnection connection)
    {
        PassengerTools tools = new(window);
        McpServerPrimitiveCollection<McpServerTool> collection = [];
        foreach (MethodInfo method in typeof(PassengerTools).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null))
        {
            collection.Add(new ConnectedTool(McpServerTool.Create(method, tools), window, connection));
        }
        return new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "passenger",
                Title = "Passenger",
                Version = Product.Version,
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
        // The agent's own IDE: nobody else is at it to ask.
        McpServer server = McpServer.Create(new StreamServerTransport(input, output, "passenger"), Options(window, new AgentConnection(done.Token, attached: false)));
        // Not held: it serves for as long as the agent stays, and each tool holds what it starts.
        _ = Task.Run(async () =>
        {
            try
            {
                await server.RunAsync(done.Token);
            }
            finally
            {
                UiThread.Post(done.Cancel);
            }
        });
        // The UI thread runs the dispatcher's loop, timers and all, until the agent goes.
        Dispatcher.UIThread.MainLoop(done.Token);
        // What the agent left running (a program, a debugger) ends with the windows, as it does with the person's.
        PassengerApp.Quit();
        PassengerApp.End();
        return 0;
    }

    /// <summary><c>--mcp-attach [PID]</c>: joins standard input and output to a window's socket until either side
    /// closes: the window whose folder holds <paramref name="folder"/> (else the agent's working directory), the
    /// innermost first, then the newest; only that process's windows when one is named.</summary>
    public static int Attach(int? processId, string? folder)
    {
        RemoveStale();
        string here = PathNames.Canonical(folder ?? Environment.CurrentDirectory);
        IEnumerable<string> candidates = (Directory.Exists(SocketFolder) ? Directory.EnumerateFiles(SocketFolder, "*.sock") : [])
            .Where(path => processId is not int pid || ProcessOf(path) == pid)
            .OrderByDescending(path => Holding(WindowFolder(path), here))
            .ThenByDescending(File.GetLastWriteTimeUtc);
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
            // No window in this process: the copies are the whole of it, for as long as either side stays.
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
            ? $"passenger: no window of Passenger {wanted} lets agents in; turn on Settings | MCP Server | Enable MCP Server in it"
            : "passenger: no Passenger window lets agents in; turn on Settings | MCP Server | Enable MCP Server in one, or use --mcp FOLDER for a Passenger of the agent's own");
        return 1;
    }

    /// <summary>The file beside a window's socket that names the folder open in it.</summary>
    internal static string FolderFile(string socket) => Path.ChangeExtension(socket, ".folder");

    /// <summary>Takes away the sockets (and their folder files) of IDEs that are no longer running.</summary>
    internal static void RemoveStale()
    {
        if (!Directory.Exists(SocketFolder))
        {
            return;
        }
        foreach (string path in Directory.EnumerateFiles(SocketFolder))
        {
            if (ProcessOf(path) is int pid && !Running(pid))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                }
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

    /// <summary>The process a socket is named for: <c>PID-WINDOW.sock</c>, or <c>PID.sock</c> from before windows had
    /// their own.</summary>
    private static int? ProcessOf(string path) =>
        int.TryParse(Path.GetFileNameWithoutExtension(path).Split('-')[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int pid)
            ? pid
            : null;

    private static string? WindowFolder(string socket)
    {
        try
        {
            return File.ReadAllText(FolderFile(socket)) is { Length: > 0 } folder ? folder : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>How closely a window's folder holds a place: its length when it does, so the innermost wins, else -1.</summary>
    private static int Holding(string? folder, string here)
    {
        if (folder is null)
        {
            return -1;
        }
        return PathNames.Within(here, folder) ? folder.Length : -1;
    }
}
