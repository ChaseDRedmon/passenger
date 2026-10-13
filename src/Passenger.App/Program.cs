using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Velopack;

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
        // First, as Velopack asks; an update never installs as a second Passenger starts, which would end the first.
        if (Installation.Installed)
        {
            Installation.Hooked(VelopackApp.Build().SetArgs(args).SetAutoApplyOnStartup(false)).Run();
        }
        LaunchOptions options;
        try
        {
            options = LaunchOptions.Parse(args);
            _ = Updates.ReleaseFeed.Default;
        }
        catch (FormatException error)
        {
            Console.Error.WriteLine($"passenger: {error.Message}");
            return 2;
        }

        Options = options;
        Core.ShellEnvironment.Load();
        switch (options.Mcp)
        {
            case McpMode.Attach:
                return Mcp.McpHost.Attach(options.AttachTo, options.Folder);
            case McpMode.Stdio:
                return Mcp.McpHost.RunStdio(options);
        }
        if (options.Screenshot is { } screenshot)
        {
            return Apart(() => Screenshot.Run(options, screenshot));
        }
        IClassicDesktopStyleApplicationLifetime? desktop = null;
        int code = BuildAvaloniaApp()
            .AfterSetup(builder => ApplyTheme(builder.Instance!, options.Theme))
            .StartWithClassicDesktopLifetime(args, lifetime => desktop = lifetime);
        // What the window ended (the debugger, Roslyn) has a moment; then an update made ready installs, last of all.
        MainWindow? closed = desktop?.MainWindow as MainWindow;
        closed?.Ended?.Wait();
        closed?.InstallUpdate();
        return code;
    }

    /// <summary>A screenshot keeps nothing of the person's: unless PASSENGER_SETTINGS names a folder, its state and logs
    /// (Roslyn's server's among them) go to a temporary one, removed when it ends, never the person's own log.</summary>
    private static int Apart(Func<int> run)
    {
        if (Environment.GetEnvironmentVariable("PASSENGER_SETTINGS") is { Length: > 0 })
        {
            return run();
        }
        string scratch = Directory.CreateTempSubdirectory("passenger-screenshot-").FullName;
        Environment.SetEnvironmentVariable("PASSENGER_SETTINGS", scratch);
        try
        {
            return run();
        }
        finally
        {
            // The log is written off a queue: what it still holds would make the folder again after it went.
            IdeLog.Flush();
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A log the language server still holds open on Windows: the system's temporary folder is cleared in time.
            }
        }
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
