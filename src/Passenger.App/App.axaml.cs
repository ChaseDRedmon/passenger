using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace Passenger.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        IdeErrors.Install();
        // The screenshot mode has no lifetime and makes its own window.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Only the person's window updates Passenger, and only a copy its installer or disk image put in place.
            Updates.Updater? updater = Updates.Updater.OfThisCopy(State.StateStore.DefaultRoot, out string notUpdating);
            MainWindow window = new() { AgentsAllowed = Program.Options.ListenForAgents, SelfUpdate = updater };
            // The person's own windows open on the layout and settings they left (agents let in or not), and every window
            // opened from this one is made as it is (PassengerApp); headless ones keep the defaults.
            window.KeepLayout();
            window.KeepSettings();
            // A theme named on the command line is the run's, over the one kept.
            Program.ApplyTheme(this, Program.Options.Theme);
            window.Opened += (_, _) =>
            {
                // The first of a start's phases in the log, as Rider's idea.log begins with its build and runtime.
                IdeErrors.Write($"Passenger {Product.Version} ({Product.Commit ?? "no commit"}) on {RuntimeInformation.OSDescription} "
                    + $"({Product.Runtime}): window shown {Stopwatch.GetElapsedTime(Program.StartedAt).TotalSeconds:0.00} s after launch");
                window.Start(Program.Options);
                // The process asks for the latest release once, in this window, which hands the watch on as it closes.
                IdeErrors.Write(updater is null ? $"Updates: this copy only says when a newer release is out: {notUpdating}"
                    : $"Updates: this copy installs updates, in {updater.Installed}");
                window.WatchForUpdates();
                // The person's windows show their activity in Discord, the one in front, while Settings says so.
                window.ShowActivityInDiscord();
            };
            desktop.MainWindow = window;
            if (OperatingSystem.IsMacOS())
            {
                NativeMenu.SetMenu(this, window.ApplicationMenu());
            }
            // macOS hands the running Passenger what the Finder or the Dock's icon opens with it, rather than starting
            // another (Info.plist's document types): a solution, a project or a folder.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += OnActivated;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (e is FileActivatedEventArgs { Files: { Count: > 0 } files })
        {
            string[] paths = [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()];
            IdeErrors.Write($"Opened by the system: {string.Join(", ", paths)}");
            UiThread.Post(() => PassengerApp.OpenFromSystem(paths));
        }
    }
}
