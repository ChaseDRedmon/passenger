using Avalonia;
using Avalonia.Headless;

namespace AgentApp;

public static class Program
{
    /// <summary>Headless, so a test's run puts nothing on the screen: an agent sees it through the pictures it draws, and
    /// clicks and types into it through its window's own input.</summary>
    public static int Main(string[] args) => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHarfBuzz()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .StartWithClassicDesktopLifetime(args);
}
