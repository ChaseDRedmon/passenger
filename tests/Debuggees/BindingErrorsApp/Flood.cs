using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Logging;
using Avalonia.Threading;

namespace BindingErrorsApp;

/// <summary>The app's "flood" mode (its first argument): the window gets thousands of rows, each binding a member the view
/// model lacks, which fail as the window opens and again with each new view model it is given, every 50 ms, until "calm" on
/// its standard input stops them and writes how many binding failures Avalonia logged in all, counted by a log sink of the
/// app's own, which the inspector's sink feeds as it feeds any app's.</summary>
public static class Flood
{
    /// <summary>The rows the window gets.</summary>
    public const int Rows = 3000;

    private static readonly Counter Failures = new();
    private static DispatcherTimer? again;

    public static bool On { get; private set; }

    /// <summary>On when the arguments ask for it, the app's sink counting from then on.</summary>
    public static void Start(string[] args)
    {
        On = args.Contains("flood");
        if (On)
        {
            Logger.Sink = Failures;
        }
    }

    /// <summary>The window's rows, failing once it has its view model, and again from its opening on.</summary>
    public static void Fill(Window window)
    {
        if (!On)
        {
            return;
        }
        StackPanel panel = (StackPanel)window.Content!;
        for (int i = 0; i < Rows; i++)
        {
            panel.Children.Add(new TextBlock { Name = $"Flooded{i}", [!TextBlock.TextProperty] = new ReflectionBinding($"Missing{i}") });
        }
        again = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => window.DataContext = new MainViewModel { Name = "Flooded" });
        window.Opened += (_, _) => again.Start();
    }

    /// <summary>No more failing, and how many failed in all.</summary>
    public static void Calm()
    {
        again?.Stop();
        Console.WriteLine($"Calm after {Failures.Count} binding failures");
    }

    private sealed class Counter : ILogSink
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area == LogArea.Binding;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Counted(level, area);

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) => Counted(level, area);

        private void Counted(LogEventLevel level, string area)
        {
            if (IsEnabled(level, area))
            {
                Interlocked.Increment(ref count);
            }
        }
    }
}
