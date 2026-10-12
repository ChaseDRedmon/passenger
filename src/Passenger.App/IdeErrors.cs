using Avalonia.Logging;
using Avalonia.Threading;

namespace Passenger.App;

/// <summary>Rider's IDE Internal Errors: an exception that escapes on the UI thread or from a task nobody awaited is
/// written to the log in the settings folder (<c>logs/passenger.log</c>, Rider's idea.log) and shown in the status
/// bar, and the IDE keeps running. Avalonia's own warnings and errors go to the same log.</summary>
public static class IdeErrors
{
    private static readonly object Gate = new();
    private static readonly List<IdeError> Reported = [];
    private static bool installed;

    /// <summary>The exceptions reported already: one the dispatcher caught also faults its operation's task, which nobody
    /// observes, so it comes round again as an unobserved task's.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Exception, object> Seen = [];
    private static Dispatcher? hooked;

    /// <summary>An error was reported; raised on the UI thread.</summary>
    public static event EventHandler<IdeError>? Raised;

    /// <summary>The folder Help, Show Log opens: the settings folder's <c>logs</c>.</summary>
    public static string LogFolder => Path.Combine(Path.GetDirectoryName(Docking.DockLayout.Path)!, "logs");

    public static string LogPath => Path.Combine(LogFolder, "passenger.log");

    /// <summary>The errors reported since the last Clear, the oldest first.</summary>
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
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write($"Fatal: {e.ExceptionObject}");
        Logger.Sink = new Sink();
    }

    /// <summary>Logs an error and shows it, from any thread.</summary>
    public static void Report(Exception error)
    {
        lock (Gate)
        {
            if (!Seen.TryAdd(error, Gate))
            {
                return;
            }
        }
        IdeError reported = new(DateTime.Now, error.GetType().Name, error.Message, error.ToString());
        Write($"Error: {reported.Details}");
        lock (Gate)
        {
            Reported.Add(reported);
        }
        Dispatcher.UIThread.Post(() => Raised?.Invoke(null, reported));
    }

    /// <summary>Forgets the errors shown (Rider's Clear All); the log keeps them.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Reported.Clear();
        }
    }

    /// <summary>A line in the log, for what is worth finding later (a server restarted) but not an error.</summary>
    public static void Write(string line)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {line}{Environment.NewLine}");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

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

/// <summary>One error the IDE caught: when, its kind and message, and the whole exception for its details.</summary>
public sealed record IdeError(DateTime At, string Kind, string Message, string Details);
