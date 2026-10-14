using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;

namespace BindingErrorsApp;

public static class Program
{
    /// <summary>The UI's dispatcher, once the app has made it: nothing is posted before.</summary>
    public static Dispatcher? Ui { get; set; }

    /// <summary>Headless, so a test's run puts nothing on the screen. A line "rebind" on its standard input gives the window
    /// a new view model, binding its caption (and failing) again, as something a person did in it would.</summary>
    public static int Main(string[] args)
    {
        Thread input = new(Input) { IsBackground = true, Name = "Input" };
        input.Start();
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .StartWithClassicDesktopLifetime(args);
    }

    private static void Input()
    {
        while (Console.ReadLine() is { } line)
        {
            if (line.Trim() != "rebind")
            {
                continue;
            }
            Ui?.Post(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
                {
                    window.DataContext = new MainViewModel();
                    Console.WriteLine("Bound again");
                }
            });
        }
    }
}
