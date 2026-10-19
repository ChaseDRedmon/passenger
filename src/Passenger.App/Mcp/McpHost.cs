using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Passenger.App.State;
using Passenger.App.Workspace;
using Passenger.Core;

namespace Passenger.App.Mcp;

/// <summary>Passenger as a Model Context Protocol server, three ways. <c>passenger --mcp [FOLDER]</c> is an IDE with no
/// window, its tools on standard input and output, for an agent to start as its own. Each window of the person's listens
/// on a socket of its own (<see cref="AgentSocket"/>), and <c>passenger --mcp-attach [PID] [FOLDER]</c> is the agent's
/// server for its whole session (<see cref="McpProxy"/>), joining the window over the folder it works in at its first call,
/// so the agent drives the IDE the person is looking at, once Enable MCP Server or the person's Allow lets it in. Each
/// connection is a server of its own over the same tools.</summary>
public static class McpHost
{
    /// <summary>The IDE has no window, only the headless platform's (whose frames a screenshot takes). The process's, as
    /// the platform is: <c>--mcp</c> runs on the headless one alone.</summary>
    public static bool IsHeadless { get; private set; }

    /// <summary>Where windows listen, one socket each: in the IDE's state folder (PASSENGER_SETTINGS moves it), which
    /// only its user can reach.</summary>
    public static string SocketFolder => Path.Combine(StateStore.DefaultRoot, "mcp");

    /// <summary>What an agent is told as it connects: how to begin and find its way, and what a person's window asks.</summary>
    internal const string Instructions =
        "Passenger is a C# IDE (with XAML, MSBuild files and the web languages). These tools act on its open workspace as its user would. " +
        "Lines and columns count from 1; relative paths are the open folder's.\n\n" +
        "Begin with workspace: the folder and solution open, their projects, run configurations and open files (open opens another folder, " +
        "solution or file). Find your way by meaning rather than by text: find_symbol finds a type or member by name, outline lists a file's " +
        "types and members with their lines, and symbol_info, definition, usages, implementations and hierarchy follow the symbol at a line " +
        "and column; find_in_files searches text. editor_text reads a file with its lines numbered; edit changes one as the editor does " +
        "(an open file the editor has not changed also takes what is written on disk); diagnostics checks it.\n\n" +
        "Change code across the solution as the IDE's refactorings do rather than file by file: rename a symbol everywhere, and refactor for " +
        "Change Signature (every call updated), Move, Safe Delete, Extract Interface, Pull Members Up, Push Members Down, Introduce Field or " +
        "Parameter, Encapsulate Field, Optimize Imports and Code Cleanup; refactor with describe true answers what its dialog offers and the " +
        "options to send back, changing nothing. Each change is one step of the IDE's Undo.\n\n" +
        "build, run, tests_list and tests_run build, run and test. To debug: breakpoint_set, then debug_start (or debug_attach), debug_step, " +
        "debug_evaluate, debug_variables and debug_continue, each waiting for the program to stop and saying where, with the top frame's " +
        "locals. An Avalonia or WPF app that run or debug_start starts is looked at and used as a person tests it: app_windows, app_tree, " +
        "app_screenshot, app_click, app_type, app_key, app_scroll, app_binding_errors. Also version control (vcs_status, vcs_diff, " +
        "vcs_commit), NuGet (nuget_search, nuget_install), Problems and Local History (problems, local_history), the settings (settings, " +
        "settings_set) and the window itself (screenshot, ide_action).\n\n" +
        "Attached to a person's Passenger (--mcp-attach), the first call joins the Passenger window open on the agent's folder (the folder " +
        "after --mcp-attach, else the agent's working folder). With none open, it has the running Passenger open that folder, or starts " +
        "Passenger on it, in a window the person sees, and waits for it. Unless the person has turned on Enable MCP Server, or let agents " +
        "into that folder always, the window asks them to let the agent in, and the call waits for their answer (2 minutes at most); a call " +
        "they refuse or leave unanswered says so, and a later one asks again. The person is also asked before the agent first uses each tool " +
        "that changes something (edit, rename, build, run, debug_start, vcs_commit and the like; reading never asks), and before it opens " +
        "another folder in their window. Passenger --mcp FOLDER is an agent's own IDE with no window, which asks nothing.";

    /// <summary>The server as an agent sees it, whichever way it is reached.</summary>
    internal static Implementation ServerInfo => new()
    {
        Name = "passenger",
        Title = "Passenger",
        Version = Product.Version,
    };

    /// <summary>The build's tools, PassengerTools' methods that are one: what every connection and <c>--mcp-attach</c>
    /// list.</summary>
    internal static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(PassengerTools).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

    /// <summary>How a tool's answer is written as JSON: its text as written, quotes, angle brackets and the rest left as
    /// they are rather than as \u0022s, so code in an answer reads as code and costs an agent no more than it should.</summary>
    private static readonly JsonSerializerOptions AnswerJson = new(McpJsonUtilities.DefaultOptions) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>How every connection and <c>--mcp-attach</c> create a tool of the build's: its answer as <see cref="AnswerJson"/>
    /// writes it, its schema the same in both.</summary>
    internal static McpServerToolCreateOptions ToolCreateOptions() => new() { SerializerOptions = AnswerJson };

    /// <summary>The tools as one connection reaches them (<see cref="ConnectedTool"/>).</summary>
    public static McpServerOptions Options(MainWindow window, AgentConnection connection)
    {
        PassengerTools tools = new(window, connection);
        McpServerPrimitiveCollection<McpServerTool> collection = [];
        foreach (MethodInfo method in ToolMethods())
        {
            collection.Add(new ConnectedTool(McpServerTool.Create(method, tools, ToolCreateOptions()), window, connection));
        }
        return new McpServerOptions
        {
            ServerInfo = ServerInfo,
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
            .AfterSetup(builder => Program.ApplyTheme(builder.Instance!, launch))
            .SetupWithoutStarting();

        // The agent's own IDE, which no other agent joins.
        MainWindow window = new() { Width = 1600, Height = 1000, AgentsAllowed = false };
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

    /// <summary><c>--headless [PATH]</c>: a person's window on the headless platform, which a test run's
    /// <c>--mcp-attach</c> starts in place of the installed app (<see cref="McpProxy.StartVariable"/>): it listens for
    /// agents and asks before letting them in, as any window does, until its input ends.</summary>
    public static int RunHeadless(LaunchOptions launch)
    {
        IsHeadless = true;
        Stream input = Console.OpenStandardInput();
        Console.SetOut(Console.Error);
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .AfterSetup(builder => Program.ApplyTheme(builder.Instance!, launch))
            .SetupWithoutStarting();

        MainWindow window = new() { Width = 1600, Height = 1000 };
        window.SetRenderScaling(2);
        window.Show();
        window.Start(launch);

        using CancellationTokenSource done = new();
        // Not held: it waits for the end of the input, which its starter's own end closes.
        _ = Task.Run(() =>
        {
            try
            {
                input.CopyTo(Stream.Null);
            }
            finally
            {
                UiThread.Post(done.Cancel);
            }
        });
        Dispatcher.UIThread.MainLoop(done.Token);
        PassengerApp.Quit();
        PassengerApp.End();
        return 0;
    }

    /// <summary><c>--mcp-attach [PID] [FOLDER]</c>: the agent's server, joining the window over its folder at its first
    /// call (<see cref="McpProxy"/>).</summary>
    public static int Attach(int? processId, string? folder) => McpProxy.Run(processId, folder);

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
    internal static int? ProcessOf(string path) =>
        int.TryParse(Path.GetFileNameWithoutExtension(path).Split('-')[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int pid)
            ? pid
            : null;

    internal static string? WindowFolder(string socket)
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
    internal static int Holding(string? folder, string here)
    {
        if (folder is null)
        {
            return -1;
        }
        return PathNames.Within(here, folder) ? folder.Length : -1;
    }
}
