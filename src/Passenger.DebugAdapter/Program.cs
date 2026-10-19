using Passenger.Dap;
using Passenger.DebugAdapter;

// Passenger's .NET debug adapter: the Debug Adapter Protocol on standard input and output, until the
// client disconnects or goes away. Takes netcoredbg's flags, so a client written for it runs this:
//   Passenger.DebugAdapter --interpreter=vscode [--engineLogging=FILE] [--symbolCache=FOLDER]
// A flag it does not know is fatal, so a typo never runs something other than what was asked.

string? logPath = null;
string? symbolCache = null;
foreach (string arg in args)
{
    if (arg is "--help" or "-h")
    {
        Console.WriteLine("Passenger.DebugAdapter --interpreter=vscode [--engineLogging=FILE] [--symbolCache=FOLDER]");
        Console.WriteLine("  The Debug Adapter Protocol on standard input and output; --engineLogging writes the engine's log to FILE.");
        Console.WriteLine("  --symbolCache keeps the symbols decompiled for assemblies that ship none in FOLDER, pruned as the adapter");
        Console.WriteLine("  starts; without it their frames have no source.");
        return 0;
    }
    else if (arg.StartsWith("--symbolCache=", StringComparison.Ordinal))
    {
        if (arg.Length == "--symbolCache=".Length)
        {
            Console.Error.WriteLine("--symbolCache needs a folder");
            return 2;
        }
        symbolCache = arg["--symbolCache=".Length..];
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

// How long an evaluation may run before it is stopped, shortened for the tests' run; a value that is no whole number of
// milliseconds above 0 is fatal, as a flag's is.
if (Environment.GetEnvironmentVariable(Passenger.Debugger.Extensions.EvaluationTimeoutVariable) is { Length: > 0 } timeout)
{
    if (!int.TryParse(timeout, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int milliseconds) || milliseconds <= 0)
    {
        Console.Error.WriteLine($"{Passenger.Debugger.Extensions.EvaluationTimeoutVariable} must be a whole number of milliseconds above 0, not '{timeout}'");
        return 2;
    }
    Passenger.Debugger.Extensions.EvaluationTimeout = TimeSpan.FromMilliseconds(milliseconds);
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
// No program the adapter launches may hold the protocol: on macOS and Linux it is given descriptors 0, 1 and 2 as its own.
(Stream input, Stream output) = Passenger.Debugger.SuspendedLaunch.DetachProtocol();
DapConnection connection = new(input, output, Log);
DebugAdapter adapter = new(connection, Log, symbolCache is null ? null : new Passenger.Debugger.Decompilation.SymbolCache(symbolCache, Log));
TaskCompletionSource disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
adapter.ShutdownRequested += () => disconnected.TrySetResult();
connection.Start();
await Task.WhenAny(disconnected.Task, connection.Completion);
// A client that closed its end without disconnecting leaves the debuggee to be let go here.
await adapter.AbortAsync();
Log("Exiting");
logWriter?.Dispose();
// A read of standard input cannot be cancelled, and the client may hold its end open: exit rather
// than wait on either.
Environment.Exit(0);
return 0;
