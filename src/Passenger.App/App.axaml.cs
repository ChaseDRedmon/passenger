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
            window.Opened += (_, _) => window.Start(Program.Options);
            desktop.MainWindow = window;
            if (OperatingSystem.IsMacOS())
            {
                // The application menu's own items, as Rider's: Settings (Cmd+,) beside About and Quit.
                NativeMenuItem settings = new("Settings...") { Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.OemComma, Avalonia.Input.KeyModifiers.Meta) };
                settings.Click += (_, _) => window.Perform(AppActions.ShowSettings);
                NativeMenu.SetMenu(this, new NativeMenu { Items = { settings } });
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
