using Passenger.Dap;
using Passenger.DebugAdapter;

// Passenger's .NET debug adapter: the Debug Adapter Protocol on standard input and output, until the
// client disconnects or goes away. Takes netcoredbg's flags, so a client written for it runs this:
//   Passenger.DebugAdapter --interpreter=vscode [--engineLogging=FILE]
// A flag it does not know is fatal, so a typo never runs something other than what was asked.

string? logPath = null;
foreach (string arg in args)
{
    if (arg is "--help" or "-h")
    {
        Console.WriteLine("Passenger.DebugAdapter --interpreter=vscode [--engineLogging=FILE]");
        Console.WriteLine("  The Debug Adapter Protocol on standard input and output; --engineLogging writes the engine's log to FILE.");
        return 0;
    }
    else if (arg.StartsWith("--interpreter=", StringComparison.Ordinal))
    {
        if (arg != "--interpreter=vscode")
        {
            Console.Error.WriteLine($"Only --interpreter=vscode (the Debug Adapter Protocol) is supported, not '{arg}'");
            return 2;
        }
    }
    else if (arg.StartsWith("--engineLogging=", StringComparison.Ordinal) && arg.Length > "--engineLogging=".Length)
    {
        logPath = arg["--engineLogging=".Length..];
    }
    else
    {
        Console.Error.WriteLine($"Unknown flag '{arg}'; --help lists them");
        return 2;
    }
}

StreamWriter? logWriter = null;
if (logPath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
    logWriter = new StreamWriter(logPath, append: true) { AutoFlush = true };
}
Lock logGate = new();
void Log(string message)
{
    if (logWriter is not null)
    {
        lock (logGate)
        {
            logWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
        }
    }
}

Log("Starting");
// On macOS and Linux a launched program is given descriptors 0, 1 and 2 as its own, so the protocol moves off them.
(Stream input, Stream output) = OperatingSystem.IsWindows()
    ? (Console.OpenStandardInput(), Console.OpenStandardOutput())
    : Passenger.Debugger.SuspendedLaunch.DetachProtocol();
DapConnection connection = new(input, output, Log);
DebugAdapter adapter = new(connection, Log);
TaskCompletionSource disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
adapter.ShutdownRequested += () => disconnected.TrySetResult();
connection.Start();
await Task.WhenAny(disconnected.Task, connection.Completion);
// A client that closed its end without disconnecting leaves the debuggee to be let go here.
adapter.Abort();
Log("Exiting");
logWriter?.Dispose();
// A read of standard input cannot be cancelled, and the client may hold its end open: exit rather
// than wait on either.
Environment.Exit(0);
return 0;
