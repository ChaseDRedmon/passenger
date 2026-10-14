using System.Text;
using Passenger.Dap;
using Passenger.Lsp;

namespace Passenger.App;

/// <summary>The IDE's logs folder, Rider's log directory, which Help, Show Log opens: <c>passenger.log</c> (Rider's
/// idea.log), each C# language server's own log under <c>roslyn/</c>, <c>dap.log</c> for the debugger, <c>vstest.log</c>
/// for the test runner, and with <see cref="Traces"/> on <c>lsp.log</c>. A line is queued from any thread and written by
/// the log's own thread, so no caller waits on the disk; each file is rotated by size, a dozen kept. The process's, as
/// Rider's idea.log is: every window writes the same files, a line naming the folder where that matters (Opened, a
/// language server's start), and each language server writes a log of its own, named for its process.</summary>
public static class IdeLog
{
    /// <summary>The IDE's own log: errors, what the servers and programs it runs said went wrong, startup times.</summary>
    public const string Ide = "passenger";

    /// <summary>The debugger's: each session, the adapter's own words, and with <see cref="Traces"/> its messages.</summary>
    public const string Debugger = "dap";

    /// <summary>The test runners': what VSTest and Microsoft.Testing.Platform's test hosts warned of or failed with, and
    /// with <see cref="Traces"/> their messages.</summary>
    public const string Tests = "vstest";

    /// <summary>The C# language servers' messages, written only with <see cref="Traces"/> on.</summary>
    public const string LspTrace = "lsp";

    /// <summary>The rotated logs kept beside the current one, <c>passenger.1.log</c> the newest (Rider's idea.log.count).</summary>
    public const int Rotations = 12;

    /// <summary>Lines waiting for the log's thread; taken under <see cref="Writing"/> so they reach the file in order.</summary>
    private static readonly List<LogLine> Queued = [];
    private static readonly object Writing = new();
    private static Thread? writer;
    private static volatile bool traces;

    /// <summary>How long the log's thread rests after a write, so the lines of a burst share one opening of the file.</summary>
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(250);

    /// <summary>The state store's <c>logs</c>, in force now: a test's settings folder changes under a running process.</summary>
    public static string Folder => Path.Combine(State.StateStore.DefaultRoot, "logs");

    /// <summary>The size past which a log is rotated, Rider's idea.log.limit: no log file grows beyond it.</summary>
    public static long Limit { get; set; } = 10_000_000;

    /// <summary>Settings' Trace protocol messages: every message to and from the C# language servers (lsp.log), the debug
    /// adapter (dap.log), VSTest and the Testing Platform's test hosts (vstest.log) is written as it goes, as a client's
    /// verbose trace is.</summary>
    public static bool Traces
    {
        get => traces;
        set
        {
            traces = value;
            LanguageServer.Trace = value ? (server, message) => Write(LspTrace, $"[server {server}] {message}") : null;
            DapConnection.Trace = value ? (adapter, message) => Write(Debugger, $"[{adapter}] {message}") : null;
        }
    }

    /// <summary>Settings' Log every program started, Rider's debug log of its processes: each program the IDE runs, its exit
    /// code and time, in passenger.log. Off, only those that fail or take a second are, since git's quick reads come every
    /// few seconds and would bury the rest.</summary>
    public static bool Programs { get; set; }

    public static string PathOf(string log) => Path.Combine(Folder, $"{log}.log");

    /// <summary>A line in one of the logs, from any thread.</summary>
    public static void Write(string log, string line)
    {
        LogLine queued = new(PathOf(log), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {line}{Environment.NewLine}");
        lock (Queued)
        {
            Queued.Add(queued);
            if (writer is null)
            {
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
                writer = new Thread(WriteQueued) { IsBackground = true, Name = "Passenger log" };
                writer.Start();
            }
            Monitor.Pulse(Queued);
        }
    }

    /// <summary>Writes every line queued, now, on the calling thread: before the process ends, and before a log is read.</summary>
    public static void Flush()
    {
        lock (Writing)
        {
            List<LogLine> lines;
            lock (Queued)
            {
                if (Queued.Count == 0)
                {
                    return;
                }
                lines = [.. Queued];
                Queued.Clear();
            }
            foreach (IGrouping<string, LogLine> file in lines.GroupBy(l => l.Path, StringComparer.Ordinal))
            {
                Append(file.Key, [.. file]);
            }
        }
    }

    private static void WriteQueued()
    {
        while (true)
        {
            lock (Queued)
            {
                while (Queued.Count == 0)
                {
                    Monitor.Wait(Queued);
                }
            }
            Flush();
            Thread.Sleep(Pause);
        }
    }

    /// <summary>Appends lines to one log, rotating it first whenever the next line would take it past the limit.</summary>
    private static void Append(string path, List<LogLine> lines)
    {
        FileStream? file = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            file = Open(path);
            long length = file.Length;
            bool rotates = true;
            foreach (LogLine line in lines)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(line.Text);
                if (rotates && length > 0 && length + bytes.Length > Limit)
                {
                    file.Dispose();
                    // One that cannot be moved (Windows, a reader not sharing deletion) is written on rather than lost.
                    rotates = Rotate(path);
                    file = Open(path);
                    length = file.Length;
                }
                file.Write(bytes);
                length += bytes.Length;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            file?.Dispose();
        }
    }

    private static FileStream Open(string path) => new(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>Rider's RollingFileHandler: the oldest goes, each moves up one, and the log becomes <c>passenger.1.log</c>.</summary>
    private static bool Rotate(string path)
    {
        try
        {
            File.Delete(Rotated(path, Rotations));
            for (int n = Rotations - 1; n >= 1; n--)
            {
                if (File.Exists(Rotated(path, n)))
                {
                    File.Move(Rotated(path, n), Rotated(path, n + 1));
                }
            }
            File.Move(path, Rotated(path, 1));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A rotated log's path: <c>passenger.3.log</c> beside <c>passenger.log</c>.</summary>
    private static string Rotated(string path, int number) =>
        Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}.{number}{Path.GetExtension(path)}");

    private sealed record LogLine(string Path, string Text);
}
