using Avalonia;
using Avalonia.Headless;

namespace Greenhouse.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // The previewer calls this. GREENHOUSE_HEADLESS draws the window off-screen, as Passenger's site pictures run it.
    public static AppBuilder BuildAvaloniaApp() => Environment.GetEnvironmentVariable("GREENHOUSE_HEADLESS") == "1"
        ? AppBuilder.Configure<App>().UseSkia().UseHarfBuzz().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        : AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
