using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;

namespace FramesApp;

public static class Program
{
    /// <summary>Slow layouts asked for by the command line ("--frames N"), after which the app ends by itself.</summary>
    public static int Frames { get; private set; }

    /// <summary>Headless, so a test's run puts nothing on the screen. Its window's SlowPanel spins in its measure; "slow N"
    /// typed into its console measures it again N times, a frame each, and "quit" ends it.</summary>
    public static int Main(string[] args)
    {
        if (args is ["--frames", string count])
        {
            Frames = int.Parse(count, CultureInfo.InvariantCulture);
        }
        Thread input = new(Read) { IsBackground = true, Name = "Input" };
        input.Start();
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Measures the window's SlowPanel again, a frame at a time, then ends the app if it is to end.</summary>
    public static void Slow(int times, bool thenQuit)
    {
        int left = times;
        DispatcherTimer.Run(() =>
        {
            if (left-- > 0 && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
            {
                window.Slow.InvalidateMeasure();
                return true;
            }
            if (thenQuit)
            {
                (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
            }
            Console.WriteLine($"measured {times} times");
            return false;
        }, TimeSpan.FromMilliseconds(80));
    }

    private static void Read()
    {
        while (Console.ReadLine() is { } line)
        {
            string[] words = line.Trim().Split(' ');
            if (words is ["slow", string count] && int.TryParse(count, CultureInfo.InvariantCulture, out int times))
            {
                Dispatcher.UIThread.Post(() => Slow(times, thenQuit: false));
            }
            else if (words is ["quit"])
            {
                Dispatcher.UIThread.Post(() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown());
            }
        }
    }
}
