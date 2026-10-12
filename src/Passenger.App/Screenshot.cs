using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Passenger.App;

/// <summary>Renders the main window to a PNG and exits (<c>--screenshot PATH</c>), after opening what
/// the command line names and taking its steps. It runs on Avalonia's headless platform with real Skia
/// drawing, so nothing appears on screen and nothing takes the focus. The picture is the compositor's
/// own frame, the path the desktop draws through, not a re-render into a bitmap (which once drew text
/// differently), so it shows what a person would see minus the system's window chrome.</summary>
internal static class Screenshot
{
    /// <summary>How long to wait for background work (colouring, indexing) before capturing anyway.</summary>
    private static readonly TimeSpan SettleLimit = TimeSpan.FromSeconds(10);

    public static int Run(LaunchOptions launch, ScreenshotOptions options)
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .AfterSetup(builder => Program.ApplyTheme(builder.Instance!, launch.Theme))
            .SetupWithoutStarting();

        MainWindow window = new() { Width = options.Size.Width, Height = options.Size.Height };
        window.SetRenderScaling(options.Scale);
        window.Show();
        Settle();
        window.Start(launch);
        Settle();
        foreach (ScriptStep step in options.Steps)
        {
            Take(window, step);
            Settle();
        }

        WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The headless window rendered no frame.");
        using (frame)
        {
            if (options.Crop is { } crop)
            {
                using WriteableBitmap part = Crop(frame, PixelRect.FromRect(crop, options.Scale));
                part.Save(options.Path, PngBitmapEncoderOptions.Default);
            }
            else
            {
                frame.Save(options.Path, PngBitmapEncoderOptions.Default);
            }
        }

        double elapsed = Stopwatch.GetElapsedTime(Program.StartedAt).TotalMilliseconds;
        Console.WriteLine($"screenshot: {options.Path} after {elapsed:F0} ms");
        window.Close();
        return 0;
    }

    private static void Take(MainWindow window, ScriptStep step)
    {
        switch (step)
        {
            case ScriptStep.Type type:
                window.KeyTextInput(type.Text);
                break;
            case ScriptStep.Key key:
                RawInputModifiers modifiers = Raw(key.Stroke.Modifiers);
                window.KeyPress(key.Stroke.Key, modifiers, PhysicalKey.None, null);
                window.KeyRelease(key.Stroke.Key, modifiers, PhysicalKey.None, null);
                break;
            case ScriptStep.Click click:
                for (int i = 0; i < click.Count; i++)
                {
                    window.MouseDown(click.At, MouseButton.Left, RawInputModifiers.None);
                    window.MouseUp(click.At, MouseButton.Left, RawInputModifiers.None);
                }
                break;
        }
    }

    /// <summary>Lets queued UI work, rendering and background work finish, so the picture shows the
    /// settled state rather than a frame mid-way.</summary>
    private static void Settle()
    {
        Stopwatch waited = Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            if (BackgroundWork.Pending == 0)
            {
                break;
            }
            Thread.Sleep(5);
        }
        while (waited.Elapsed < SettleLimit);
    }

    /// <summary>A copy of one region of a frame, clamped to the frame.</summary>
    private static WriteableBitmap Crop(WriteableBitmap frame, PixelRect region)
    {
        PixelRect bounds = region.Intersect(new PixelRect(frame.PixelSize));
        WriteableBitmap part = new(bounds.Size, frame.Dpi, frame.Format, AlphaFormat.Premul);
        using ILockedFramebuffer target = part.Lock();
        frame.CopyPixels(bounds, target.Address, target.RowBytes * bounds.Height, target.RowBytes);
        return part;
    }

    private static RawInputModifiers Raw(KeyModifiers modifiers)
    {
        RawInputModifiers raw = RawInputModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            raw |= RawInputModifiers.Shift;
        }
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            raw |= RawInputModifiers.Control;
        }
        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            raw |= RawInputModifiers.Alt;
        }
        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            raw |= RawInputModifiers.Meta;
        }
        return raw;
    }
}

/// <summary>Counts background work still to land on the UI thread, so a screenshot can wait for it.
/// Anything that runs off the UI thread and changes what is drawn enters here.</summary>
public static class BackgroundWork
{
    private static int pending;

    public static int Pending => Volatile.Read(ref pending);

    public static void Begin() => Interlocked.Increment(ref pending);

    public static void End() => Interlocked.Decrement(ref pending);
}
