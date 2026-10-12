using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Passenger.Editor;
using Passenger.Text;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(Passenger.Tests.TestApp))]

namespace Passenger.Tests;

/// <summary>The real app on the headless platform, drawing with Skia so text measures as it does on
/// a desktop.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Passenger.App.App>()
        .UseSkia()
        .UseHarfBuzz()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>A code editor alone in a window, driven the way a person drives it: keys by Rider's
/// macOS keymap (so a test reads the same on every platform), typed text, and clicks.</summary>
public sealed class EditorRig
{
    public EditorRig(string text)
    {
        Document = new TextDocument(text);
        Editor = new CodeEditor { Keymap = DefaultKeymaps.VisualStudioMac(), Document = Document };
        Window = new Window { Width = 900, Height = 700, Content = Editor };
        Window.Show();
        Editor.Focus();
        Ui.Settle();
    }

    public Window Window { get; }
    public CodeEditor Editor { get; }
    public TextDocument Document { get; }
    public string Text => Document.Snapshot.ToString();
    public int Caret => Editor.Selections.Primary.Caret;

    /// <summary>"meta shift L", "alt shift PERIOD", "ENTER": Rider's own keymap notation.</summary>
    public void Press(string keys)
    {
        Ui.Press(Window, keys);
    }

    /// <summary>Types one character at a time, as a keyboard does, so the editor's typing help
    /// (closing brackets, stepping over them) sees each one.</summary>
    public void Type(string text)
    {
        foreach (char c in text)
        {
            Window.KeyTextInput(c.ToString());
        }
        Ui.Settle();
    }

    /// <summary>Puts the caret where a click on that offset would.</summary>
    public void PlaceCaret(int offset) => Editor.Selections = SelectionSet.Single(offset);
}

public static class Ui
{
    public static void Press(TopLevel window, string keys)
    {
        Keystroke stroke = Keystroke.Parse(keys.Replace('+', ' '));
        RawInputModifiers modifiers = Raw(stroke.Modifiers);
        window.KeyPress(stroke.Key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(stroke.Key, modifiers, PhysicalKey.None, null);
        Settle();
    }

    /// <summary>Runs queued UI work and waits for background work (colouring) to land.</summary>
    public static void Settle()
    {
        // Long enough for the language server to start and load a small project.
        DateTime limit = DateTime.UtcNow.AddSeconds(90);
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            if (BackgroundWork.Pending == 0)
            {
                return;
            }
            Thread.Sleep(2);
        }
        while (DateTime.UtcNow < limit);
        throw new TimeoutException("Background work never finished.");
    }

    /// <summary>Waits for something to become true, running the dispatcher's loop (and so its timers)
    /// meanwhile; fails naming what never happened.</summary>
    public static void WaitFor(Func<bool> condition, string what, TimeSpan? limit = null)
    {
        DateTime until = DateTime.UtcNow + (limit ?? TimeSpan.FromSeconds(90));
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"Waited {(limit ?? TimeSpan.FromSeconds(90)).TotalSeconds} s for {what}");
            }
            Passenger.App.Screenshot.Pump(TimeSpan.FromMilliseconds(20));
            Settle();
        }
    }

    /// <summary>Opens a control's right-click menu for what it has chosen, as the menu key does, and chooses an item,
    /// failing if it is not there or cannot run.</summary>
    public static void ChooseFromMenu(Control owner, string item)
    {
        ContextMenu menu = owner.ContextMenu ?? throw new InvalidOperationException($"{owner.GetType().Name} has no menu");
        owner.RaiseEvent(new ContextRequestedEventArgs());
        Settle();
        MenuItem? chosen = menu.Items.OfType<MenuItem>().FirstOrDefault(i => (string?)i.Header == item);
        if (chosen is null || !chosen.IsEnabled)
        {
            throw new InvalidOperationException($"The menu has no {item} to choose: [{string.Join(", ", menu.Items.OfType<MenuItem>().Select(i => $"{i.Header}{(i.IsEnabled ? "" : " (disabled)")}"))}]");
        }
        chosen.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();
        Settle();
    }

    /// <summary>The clipboard's text, read as a paste would read it.</summary>
    public static string? Clipboard(TopLevel window)
    {
        Task<string?> text = Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!);
        while (!text.IsCompleted)
        {
            Settle();
        }
        return text.Result;
    }

    /// <summary>Presses the platform's first shortcut for an IDE action, both keystrokes of a chord.</summary>
    public static void Run(TopLevel window, string action)
    {
        Shortcut shortcut = Passenger.App.AppActions.Keymap().ShortcutsFor(action)[0];
        Press(window, Notation(shortcut.First));
        if (shortcut.Second is { } second)
        {
            Press(window, Notation(second));
        }
    }

    /// <summary>The first shortcut the platform's keymap gives an IDE action, in keymap notation.</summary>
    public static string Shortcut(string action) => Notation(Passenger.App.AppActions.Keymap().ShortcutsFor(action)[0].First);

    private static string Notation(Keystroke stroke)
    {
        List<string> parts = [];
        if (stroke.Modifiers.HasFlag(KeyModifiers.Meta))
        {
            parts.Add("meta");
        }
        if (stroke.Modifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("control");
        }
        if (stroke.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("alt");
        }
        if (stroke.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("shift");
        }
        parts.Add(stroke.Key.ToString());
        return string.Join(' ', parts);
    }

    public static RawInputModifiers Raw(KeyModifiers modifiers)
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
