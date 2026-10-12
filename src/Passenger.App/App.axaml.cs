using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Passenger.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // The screenshot mode has no lifetime and makes its own window.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new();
            window.Opened += (_, _) => window.Start(Program.Options);
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
