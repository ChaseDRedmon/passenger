using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Passenger.Editor;

namespace Passenger.App;

/// <summary>Renders the main window to a PNG and exits (<c>--screenshot PATH</c>), after opening what
/// the command line names and taking its steps. It runs on Avalonia's headless platform with real Skia
/// drawing, so nothing appears on screen and nothing takes the focus. The picture is the compositor's
/// own frame, the path the desktop draws through, not a re-render into a bitmap (which once drew text
/// differently), so it shows what a person would see minus the system's window chrome.</summary>
public static class Screenshot
{
    /// <summary>How long to wait for background work (colouring, indexing) before capturing anyway.</summary>
    private static readonly TimeSpan SettleLimit = TimeSpan.FromSeconds(90);

    public static int Run(LaunchOptions launch, ScreenshotOptions options)
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .AfterSetup(builder => Program.ApplyTheme(builder.Instance!, launch.Theme))
            .SetupWithoutStarting();

        MainWindow window = new() { Width = options.Size.Width, Height = options.Size.Height, Autosaves = false };
        window.SetRenderScaling(options.Scale);
        bool settled = Prepare(window, launch, options.Steps);

        // A diff or a dialog the steps opened is what the person would be looking at.
        WriteableBitmap frame = window.Frontmost.CaptureRenderedFrame()
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
        // A program or a build the steps started ends with the window, as it does with the person's.
        window.Close();
        window.Ended?.Wait();
        if (!settled)
        {
            // The picture is kept to show what was still going on, but it is not the settled window.
            Console.Error.WriteLine($"screenshot: {options.Path} taken with background work still pending after {SettleLimit.TotalSeconds:0} s");
            return 1;
        }
        Console.WriteLine($"screenshot: {options.Path} after {elapsed:F0} ms");
        return 0;
    }

    /// <summary>Shows the window, opens what the command line names and takes the steps, the background work each
    /// starts landed before the next: the window as its picture is taken, with no wait of its own. False when work was
    /// still pending at the limit.</summary>
    public static bool Prepare(MainWindow window, LaunchOptions launch, IReadOnlyList<ScriptStep> steps)
    {
        window.Show();
        bool settled = Settle();
        window.Start(launch);
        settled &= Settle();
        foreach (ScriptStep step in steps)
        {
            Take(window, step);
            settled &= Settle();
        }
        return settled;
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
            case ScriptStep.Wait wait:
                Pump(wait.For);
                break;
            case ScriptStep.Hover hover:
                window.MouseMove(hover.At, RawInputModifiers.None);
                // Past the editor's hover delay, and the card's answer.
                Take(window, new ScriptStep.Wait(TimeSpan.FromMilliseconds(1500)));
                break;
            case ScriptStep.Drag drag:
                window.MouseDown(drag.From, MouseButton.Left, RawInputModifiers.None);
                for (int i = 1; i <= 10; i++)
                {
                    window.MouseMove(drag.From + ((drag.To - drag.From) * (i / 10.0)), RawInputModifiers.LeftMouseButton);
                }
                if (drag.Release)
                {
                    window.MouseUp(drag.To, MouseButton.Left, RawInputModifiers.None);
                }
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

    /// <summary>Runs the dispatcher's loop for a while: its timers (the editor's hover, a caret's blink)
    /// fire only inside a running frame, which running the queued jobs alone never is.</summary>
    public static void Pump(TimeSpan duration)
    {
        DispatcherFrame frame = new();
        DispatcherTimer.RunOnce(() => frame.Continue = false, duration, DispatcherPriority.Send);
        Dispatcher.UIThread.PushFrame(frame);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
    }

    /// <summary>Lets queued UI work, rendering and background work finish, so the picture shows the
    /// settled state rather than a frame mid-way; false when work was still pending at the limit.</summary>
    private static bool Settle()
    {
        Stopwatch waited = Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            if (BackgroundWork.Pending == 0)
            {
                return true;
            }
            Thread.Sleep(5);
        }
        while (waited.Elapsed < SettleLimit);
        return false;
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
