using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Passenger.App;

/// <summary>Renders the main window to a PNG and exits (<c>--screenshot PATH</c>). It runs on
/// Avalonia's headless platform with real Skia drawing, so nothing appears on screen and nothing takes
/// the focus, and the picture is the one the desktop draws, minus the system's window chrome.</summary>
internal static class Screenshot
{
    public static int Run(ScreenshotOptions options, ThemeChoice? theme)
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .AfterSetup(builder => Program.ApplyTheme(builder.Instance!, theme))
            .SetupWithoutStarting();

        MainWindow window = new() { Width = options.Size.Width, Height = options.Size.Height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();

        PixelSize pixels = PixelSize.FromSize(window.ClientSize, options.Scale);
        double dpi = 96 * options.Scale;
        using RenderTargetBitmap bitmap = new(pixels, new Vector(dpi, dpi));
        bitmap.Render(window);
        bitmap.Save(options.Path, PngBitmapEncoderOptions.Default);

        double elapsed = Stopwatch.GetElapsedTime(Program.StartedAt).TotalMilliseconds;
        Console.WriteLine($"screenshot: {options.Path} ({pixels.Width}x{pixels.Height}) after {elapsed:F0} ms");
        window.Close();
        return 0;
    }
}
