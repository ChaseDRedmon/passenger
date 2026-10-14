using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace FramesApp;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new();
            desktop.MainWindow = window;
            if (Program.Frames > 0)
            {
                window.Opened += (_, _) => Program.Slow(Program.Frames, thenQuit: true);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
