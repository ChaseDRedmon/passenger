using System.Text;
using Avalonia.Logging;
using Avalonia.Threading;

namespace Passenger.App;

/// <summary>Rider's IDE Internal Errors: an exception that escapes on the UI thread or from a task nobody awaited is
/// written to the log in the settings folder (<c>logs/passenger.log</c>, Rider's idea.log) and shown in the status
/// bar, and the IDE keeps running. Repeats of one error are one entry with its count, the list holds Rider's hundred,
/// and the log is written by a thread of its own and rotated by size. Avalonia's warnings and errors go to the log.</summary>
public static class IdeErrors
{
    /// <summary>The different errors listed at most, as Rider's MessagePool: past it, they are only in the log.</summary>
    public const int Limit = 100;

    /// <summary>The rotated logs kept beside the current one, <c>passenger.1.log</c> the newest (Rider's idea.log.count).</summary>
    public const int Rotations = 12;

    private static readonly object Gate = new();
    private static readonly List<IdeError> Reported = [];

    /// <summary>Each listed error's place in <see cref="Reported"/>, by the whole exception's text.</summary>
    private static readonly Dictionary<string, int> Places = new(StringComparer.Ordinal);
    private static int unlisted;
    private static bool installed;

    /// <summary>The exceptions reported already: one the dispatcher caught also faults its operation's task, which nobody
    /// observes, so it comes round again as an unobserved task's.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Exception, object> Seen = [];
    private static Dispatcher? hooked;

    /// <summary>Lines waiting for the log's thread; taken under <see cref="Writing"/> so they reach the file in order.</summary>
    private static readonly List<LogLine> Queued = [];
    private static readonly object Writing = new();
    private static Thread? writer;

    /// <summary>How long the log's thread rests after a write, so the lines of a burst share one opening of the file.</summary>
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(250);

    /// <summary>An error was reported, a repeat with its count so far; raised on the UI thread.</summary>
    public static event EventHandler<IdeError>? Raised;

    /// <summary>The folder Help, Show Log opens: the state store's <c>logs</c>.</summary>
    public static string LogFolder => Path.Combine(State.StateStore.DefaultRoot, "logs");

    public static string LogPath => Path.Combine(LogFolder, "passenger.log");

    /// <summary>The size past which the log is rotated, Rider's idea.log.limit: no log file grows beyond it.</summary>
    public static long LogLimit { get; set; } = 10_000_000;

    /// <summary>The errors reported since the last Clear, each once with how many times it came, the oldest first.</summary>
    public static IReadOnlyList<IdeError> Errors
    {
        get
        {
            lock (Gate)
            {
                return [.. Reported];
            }
        }
    }

    /// <summary>The errors since the last Clear that came once the list was full: they are only in the log.</summary>
    public static int Unlisted
    {
        get
        {
            lock (Gate)
            {
                return unlisted;
            }
        }
    }

    /// <summary>Catches what would end the IDE: on the UI thread's dispatcher (each window makes sure of it, since a
    /// headless session can bring its own), and once per process for tasks nobody awaited and Avalonia's log.</summary>
    public static void Install()
    {
        if (hooked != Dispatcher.UIThread)
        {
            hooked = Dispatcher.UIThread;
            hooked.UnhandledException += (_, e) =>
            {
                Report(e.Exception);
                e.Handled = true;
            };
        }
        if (installed)
        {
            return;
        }
        installed = true;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report(e.Exception.InnerExceptions.Count == 1 ? e.Exception.InnerException! : e.Exception);
            e.SetObserved();
        };
        // Past saving: an exception off the UI thread still ends the process, but leaves its reason in the log.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Write($"Fatal: {e.ExceptionObject}");
            Flush();
        };
        Logger.Sink = new Sink();
        Core.ChildProcesses.Ended += (_, end) =>
        {
            if (end.Notable)
            {
                Write(end.ToString());
            }
        };
    }

    /// <summary>Logs an error and shows it, from any thread. A repeat counts against the error listed already and is
    /// one line in the log, its details being there from the first time.</summary>
    public static void Report(Exception error)
    {
        string details = error.ToString();
        IdeError reported;
        lock (Gate)
        {
            if (!Seen.TryAdd(error, Gate))
            {
                return;
            }
            DateTime now = DateTime.Now;
            if (Places.TryGetValue(details, out int place))
            {
                reported = Reported[place] with { Count = Reported[place].Count + 1, Last = now };
                Reported[place] = reported;
            }
            else
            {
                reported = new IdeError(now, error.GetType().Name, error.Message, details);
                if (Reported.Count < Limit)
                {
                    Places[details] = Reported.Count;
                    Reported.Add(reported);
                }
                else
                {
                    unlisted++;
                }
            }
        }
        Write(reported.Count == 1 ? $"Error: {details}" : $"Error, occurred {reported.Count} times: {reported.Kind}: {reported.Message}");
        Dispatcher.UIThread.Post(() => Raised?.Invoke(null, reported));
    }

    /// <summary>Forgets the errors shown (Rider's Clear All); the log keeps them.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Reported.Clear();
            Places.Clear();
            unlisted = 0;
        }
    }

    /// <summary>A line in the log, for what is worth finding later (a server restarted) but not an error. It is queued
    /// from any thread and written by the log's own, so the UI thread never waits on the disk.</summary>
    public static void Write(string line)
    {
        // The folder is the one in force now: a test's settings folder changes under a running process.
        LogLine queued = new(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {line}{Environment.NewLine}");
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

    /// <summary>Writes every line queued, now, on the calling thread: before the process ends, and before the log is read.</summary>
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
            int start = 0;
            while (start < lines.Count)
            {
                int end = start + 1;
                while (end < lines.Count && lines[end].Path == lines[start].Path)
                {
                    end++;
                }
                Append(lines[start].Path, lines, start, end);
                start = end;
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
    private static void Append(string path, List<LogLine> lines, int start, int end)
    {
        FileStream? file = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            file = Open(path);
            long length = file.Length;
            bool rotates = true;
            for (int i = start; i < end; i++)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(lines[i].Text);
                if (rotates && length > 0 && length + bytes.Length > LogLimit)
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

    /// <summary>Avalonia's warnings and errors, in the IDE's log rather than a trace nobody reads.</summary>
    private sealed class Sink : ILogSink
    {
        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Write($"Avalonia {level} [{area}] {messageTemplate}");

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
            Write($"Avalonia {level} [{area}] {messageTemplate} {string.Join(", ", propertyValues)}");
    }
}

/// <summary>One error the IDE caught: when it first came, its kind and message, the whole exception for its details,
/// and how many times it came since the list was cleared, the last when.</summary>
public sealed record IdeError(DateTime At, string Kind, string Message, string Details)
{
    public int Count { get; init; } = 1;

    public DateTime Last { get; init; } = At;
}
