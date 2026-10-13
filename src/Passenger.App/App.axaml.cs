using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

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
            MainWindow window = new() { AgentsAllowed = Program.Options.ListenForAgents };
            // The person's own window opens on the layout and settings they left (agents let in or not); headless ones
            // keep the defaults.
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
            };
            desktop.MainWindow = window;
            if (OperatingSystem.IsMacOS())
            {
                NativeMenu.SetMenu(this, window.ApplicationMenu());
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
