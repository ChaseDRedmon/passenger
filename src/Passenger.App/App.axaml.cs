using Avalonia;
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
            MainWindow window = new();
            // The person's own window opens on the layout they left; headless ones keep the defaults.
            window.KeepLayout();
            window.KeepSettings();
            // A theme named on the command line is the run's, over the one kept.
            Program.ApplyTheme(this, Program.Options.Theme);
            window.Opened += (_, _) =>
            {
                window.Start(Program.Options);
                if (Program.Options.ListenForAgents)
                {
                    Mcp.McpHost.Listen(window);
                }
            };
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
