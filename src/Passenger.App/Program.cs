using System.Diagnostics;
using Avalonia;
using Avalonia.Styling;

namespace Passenger.App;

internal static class Program
{
    /// <summary>When Main began, so a screenshot can say how long its first frame took. Set as Main's
    /// first act, since a static initializer would run only when something first read it.</summary>
    public static long StartedAt { get; private set; }

    /// <summary>What the command line asked to open, for the window the desktop lifetime makes.</summary>
    public static LaunchOptions Options { get; private set; } = LaunchOptions.Empty;

    [STAThread]
    public static int Main(string[] args)
    {
        StartedAt = Stopwatch.GetTimestamp();
        LaunchOptions options;
        try
        {
            options = LaunchOptions.Parse(args);
        }
        catch (FormatException error)
        {
            Console.Error.WriteLine($"passenger: {error.Message}");
            return 2;
        }

        Options = options;
        Lsp.ShellEnvironment.Load();
        switch (options.Mcp)
        {
            case McpMode.Attach:
                return Mcp.McpHost.Attach(options.AttachTo);
            case McpMode.Stdio:
                return Mcp.McpHost.RunStdio(options);
        }
        if (options.Screenshot is { } screenshot)
        {
            return Screenshot.Run(options, screenshot);
        }
        return BuildAvaloniaApp()
            .AfterSetup(builder => ApplyTheme(builder.Instance!, options.Theme))
            .StartWithClassicDesktopLifetime(args);
    }

    public static void ApplyTheme(Application app, ThemeChoice? theme)
    {
        if (theme is { } choice)
        {
            app.RequestedThemeVariant = choice switch
            {
                ThemeChoice.Dark => ThemeVariant.Dark,
                ThemeChoice.Light => ThemeVariant.Light,
                _ => ThemeVariant.Default,
            };
        }
    }

    /// <summary>The XAML previewer calls this by name, so it stays public and parameterless. Avalonia's log goes to the
    /// IDE's own (<see cref="IdeErrors"/>), which the app installs.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
}
