using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AgentApp;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // "--quit-at-once": ends before it shows anything, so it draws no frame.
            if (desktop.Args is ["--quit-at-once"])
            {
                Dispatcher.UIThread.Post(() => desktop.Shutdown());
                base.OnFrameworkInitializationCompleted();
                return;
            }
            MainWindow window = new();
            desktop.MainWindow = window;
            // "--quit-once-drawn": ends once its window has been laid out and drawn, for a profile of its frames that waits
            // for no time of its own.
            if (desktop.Args is ["--quit-once-drawn"])
            {
                window.Opened += (_, _) => window.RequestAnimationFrame(_ => window.RequestAnimationFrame(_ => desktop.Shutdown()));
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
