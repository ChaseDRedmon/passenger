using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Passenger.Editor;
using Passenger.Text;
using Xunit.v3;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(Passenger.Tests.TestApp))]
[assembly: Passenger.Tests.WindowTestsStartIdle]

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

/// <summary>A window test starts with no background work held. Each test has a dispatcher of its own, and work a test
/// left running (one that failed before its window settled, or closed it mid-parse) ends on that dispatcher, which
/// never runs again: held for ever, it would fail every window test after it on <see cref="Ui.Settle"/>'s limit.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class WindowTestsStartIdleAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (methodUnderTest.GetCustomAttribute<AvaloniaFactAttribute>() is not null || methodUnderTest.GetCustomAttribute<AvaloniaTheoryAttribute>() is not null)
        {
            BackgroundWork.Forget();
        }
    }
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

    /// <summary>Adds a caret where an alt+shift click on that offset would, there being none there, as the primary.</summary>
    public void AddCaret(int offset) => Editor.Selections = Editor.Selections.Add(Selection.At(offset));
}

/// <summary>A test's temporary folders, deleted as Windows lets them be: git writes its objects read-only, which Windows
/// refuses to delete, and a folder a process still runs in (a git status the closed window started, a debugged program
/// being killed) cannot go until it has ended, so both are waited out for a while. One already gone is no error.</summary>
public static class TestFolders
{
    public static void Delete(string path)
    {
        // A settings folder's log lines still queued would make its logs folder again once it was gone.
        Passenger.App.IdeErrors.Flush();
        DateTime until = DateTime.UtcNow.AddSeconds(30);
        while (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception error) when ((error is IOException or UnauthorizedAccessException) && DateTime.UtcNow < until)
            {
                Writable(path);
                Thread.Sleep(100);
            }
        }
    }

    private static void Writable(string path)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                FileAttributes attributes = File.GetAttributes(file);
                if (attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
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

    /// <summary>A key that types, pressed as a keyboard presses it: the key, carrying the character the layout gives it
    /// ("ł" for a Polish AltGr+L, which Windows sends as Ctrl+Alt+L), then that character as text unless the key was taken
    /// as a shortcut, which keeps the platform from typing it.</summary>
    public static void Type(TopLevel window, string keys, string text)
    {
        Keystroke stroke = Keystroke.Parse(keys.Replace('+', ' '));
        RawInputModifiers modifiers = Raw(stroke.Modifiers);
        bool taken = false;
        EventHandler<KeyEventArgs> watch = (_, e) => taken = e.Handled;
        window.AddHandler(InputElement.KeyDownEvent, watch, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        window.KeyPress(stroke.Key, modifiers, PhysicalKey.None, text);
        window.RemoveHandler(InputElement.KeyDownEvent, watch);
        if (!taken)
        {
            window.KeyTextInput(text);
        }
        window.KeyRelease(stroke.Key, modifiers, PhysicalKey.None, text);
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
        throw new TimeoutException($"Background work never finished.{ServerLogTail()}");
    }

    /// <summary>The end of the newest language server's log, which says where a project load that never ended stopped. The
    /// server writes it line by line, so unlike the IDE's own log there is no queue to flush first. Newest by when it was
    /// begun: a Windows directory listing dates a file still open by its last close, so a server still running looks older
    /// than the one shut down for it.</summary>
    private static string ServerLogTail()
    {
        DirectoryInfo logs = new(Passenger.App.CSharp.CSharpService.LogFolder);
        if (!logs.Exists || logs.EnumerateFiles("server-*.log").OrderByDescending(f => f.CreationTimeUtc).FirstOrDefault() is not { } newest)
        {
            return string.Empty;
        }
        string[] lines = Shared(newest.FullName).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string begun = newest.CreationTime.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
        return $"\nThe newest language server's log ({newest.Name}, begun {begun}) {(lines.Length == 0 ? "is empty" : "ends:")}\n{string.Join('\n', lines.TakeLast(25))}";
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

    /// <summary>The IDE's log as a person opening it would find it: what was queued for it written first, and read sharing
    /// the file with the log's own thread, which may be writing to it (Windows refuses a reader that does not share).</summary>
    public static string IdeLog()
    {
        Passenger.App.IdeErrors.Flush();
        return Shared(Passenger.App.IdeErrors.LogPath);
    }

    /// <summary>A file's text read alongside a writer that has it open, or nothing when there is no such file (yet, or
    /// any longer: a log is moved aside as it rotates).</summary>
    public static string Shared(string path)
    {
        try
        {
            using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(file);
            return reader.ReadToEnd();
        }
        catch (Exception missing) when (missing is FileNotFoundException or DirectoryNotFoundException)
        {
            return string.Empty;
        }
    }

    /// <summary>A pixel of the window as the compositor last drew it, at a point in its coordinates: what a person sees,
    /// which a fresh render of every control would not show if one of them was never asked to draw again.</summary>
    public static Avalonia.Media.Color Pixel(TopLevel window, Point at)
    {
        using Avalonia.Media.Imaging.WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The window has drawn no frame.");
        byte[] pixel = new byte[4];
        System.Runtime.InteropServices.GCHandle pinned = System.Runtime.InteropServices.GCHandle.Alloc(pixel, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            PixelPoint point = PixelPoint.FromPoint(at, window.RenderScaling);
            frame.CopyPixels(new PixelRect(point.X, point.Y, 1, 1), pinned.AddrOfPinnedObject(), 4, 4);
        }
        finally
        {
            pinned.Free();
        }
        return frame.Format == Avalonia.Platform.PixelFormat.Rgba8888
            ? Avalonia.Media.Color.FromRgb(pixel[0], pixel[1], pixel[2])
            : Avalonia.Media.Color.FromRgb(pixel[2], pixel[1], pixel[0]);
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

/// <summary>How long a key waits for the window's thread: work posted at input's priority, as the platform's keys wait
/// for the thread between jobs, the longest wait kept until <see cref="Stop"/>. Each is posted as soon as the last is
/// answered, from a thread of its own, so a loaded machine that is slow to wake a timer or the thread pool still
/// takes as many looks as the window answers.</summary>
public sealed class InputWait : IDisposable
{
    private readonly SemaphoreSlim answered = new(0);
    private volatile bool stopped;
    private int posted;

    public InputWait()
    {
        Thread poster = new(() =>
        {
            while (!stopped)
            {
                long at = Stopwatch.GetTimestamp();
                Interlocked.Increment(ref posted);
                Dispatcher.UIThread.Post(() => Answer(Stopwatch.GetElapsedTime(at)), DispatcherPriority.Input);
                while (!stopped && !answered.Wait(100))
                {
                }
            }
        })
        { IsBackground = true, Name = "Input wait" };
        poster.Start();
    }

    public TimeSpan Longest { get; private set; }

    public int Answered { get; private set; }

    public int Posted => Volatile.Read(ref posted);

    /// <summary>Stops measuring, on the window's thread, so a wait while it is not running never counts.</summary>
    public void Stop() => stopped = true;

    public void Dispose() => Stop();

    private void Answer(TimeSpan waited)
    {
        if (!stopped)
        {
            Answered++;
            Longest = waited > Longest ? waited : Longest;
        }
        answered.Release();
    }
}

/// <summary>Programs the IDE started for a test, by id, as a process list would show them.</summary>
public static class Processes
{
    /// <summary>The id a program wrote to a file once it was running, the window's loop running meanwhile (not settling,
    /// which a build under way would not let it do).</summary>
    public static int WrittenId(string file, string what)
    {
        DateTime until = DateTime.UtcNow.AddMinutes(2);
        int id;
        while ((id = ReadId(file)) <= 0)
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"Waited 2 minutes for {what}");
            }
            Passenger.App.Screenshot.Pump(TimeSpan.FromMilliseconds(50));
        }
        return id;
    }

    /// <summary>The id a program wrote to a file once it was running, for a test with no window of its own.</summary>
    public static async Task<int> WrittenIdAsync(string file, string what)
    {
        DateTime until = DateTime.UtcNow.AddMinutes(2);
        int id;
        while ((id = ReadId(file)) <= 0)
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"Waited 2 minutes for {what}");
            }
            await Task.Delay(100);
        }
        return id;
    }

    /// <summary>Every process a process started, and every one those started, as the system lists them now.</summary>
    public static IReadOnlyList<int> Descendants(int id)
    {
        ILookup<int, int> children = ParentsOfAll().ToLookup(p => p.Parent, p => p.Child);
        List<int> found = [];
        Queue<int> next = new([id]);
        while (next.TryDequeue(out int parent))
        {
            foreach (int child in children[parent].Where(c => c != parent && c != id && !found.Contains(c) && StartedAfter(c, parent)))
            {
                found.Add(child);
                next.Enqueue(child);
            }
        }
        return found;

        // Windows keeps a gone parent's id in its children and gives the id to new processes: not their children.
        static bool StartedAfter(int child, int parent)
        {
            try
            {
                using System.Diagnostics.Process later = System.Diagnostics.Process.GetProcessById(child);
                using System.Diagnostics.Process earlier = System.Diagnostics.Process.GetProcessById(parent);
                return later.StartTime >= earlier.StartTime;
            }
            catch (Exception gone) when (gone is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }

    /// <summary>A process as a failure names it: its id and its command line (its name on Windows).</summary>
    public static string Describe(int id)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(id);
                return $"{id} {process.ProcessName}";
            }
            using System.Diagnostics.Process ps = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ps", ["-o", "command=", "-p", id.ToString(System.Globalization.CultureInfo.InvariantCulture)]) { RedirectStandardOutput = true })!;
            string command = ps.StandardOutput.ReadToEnd().Trim();
            ps.WaitForExit();
            return $"{id} {command}";
        }
        catch (Exception gone) when (gone is ArgumentException or InvalidOperationException)
        {
            return $"{id} (gone)";
        }
    }

    /// <summary>Each process and its parent: ps's columns on macOS and Linux, Win32_Process's on Windows, which has no ps.</summary>
    private static IEnumerable<(int Child, int Parent)> ParentsOfAll()
    {
        System.Diagnostics.ProcessStartInfo list = OperatingSystem.IsWindows()
            ? new("powershell", ["-NoProfile", "-Command", "Get-CimInstance Win32_Process | ForEach-Object { \"$($_.ProcessId) $($_.ParentProcessId)\" }"])
            : new("ps", ["-A", "-o", "pid=,ppid="]);
        list.RedirectStandardOutput = true;
        string text;
        using (System.Diagnostics.Process ps = System.Diagnostics.Process.Start(list)!)
        {
            text = ps.StandardOutput.ReadToEnd();
            ps.WaitForExit();
        }
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 2 && int.TryParse(words[0], out int child) && int.TryParse(words[1], out int parent))
            {
                yield return (child, parent);
            }
        }
    }

    /// <summary>The id in a file, or 0 before there is one. The writer may still have it open, which Windows refuses a reader.</summary>
    private static int ReadId(string file)
    {
        try
        {
            return File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out int id) ? id : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    public static bool IsRunning(int id)
    {
        if (id <= 0)
        {
            return false;
        }
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Whether a process has gone within a few seconds, the time a killed one may take to leave the list.</summary>
    public static bool Gone(int id)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(15);
        while (IsRunning(id) && DateTime.UtcNow < until)
        {
            Thread.Sleep(100);
        }
        return !IsRunning(id);
    }

    /// <summary>Ends what a failed test left running, its children with it.</summary>
    public static void Kill(int id)
    {
        try
        {
            if (IsRunning(id))
            {
                using System.Diagnostics.Process left = System.Diagnostics.Process.GetProcessById(id);
                left.Kill(entireProcessTree: true);
            }
        }
        catch (Exception gone) when (gone is ArgumentException or InvalidOperationException)
        {
        }
    }

    /// <summary>Stops a process where it stands, answering nothing, as a deadlocked one would: SIGSTOP on macOS and
    /// Linux, and on Windows every thread suspended (NtSuspendProcess), which a kill ends as surely. Signalled, not by
    /// running kill: on macOS a child ending while another is stopped sets .NET reaping in a spin that holds Process's lock.</summary>
    public static void Suspend(int id, string what)
    {
        if (OperatingSystem.IsWindows())
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(id);
            int status = NtSuspendProcess(process.Handle);
            Assert.True(status == 0, $"{what} could not be suspended (NTSTATUS 0x{status:X8})");
            return;
        }
        Assert.True(Signal(id, OperatingSystem.IsMacOS() ? 17 : 19) == 0, $"{what} could not be stopped (errno {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()})");
    }

    [System.Runtime.InteropServices.DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr process);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Signal(int process, int signal);
}

/// <summary>Where a process's threads wait, as macOS's sample sees them (it may read any of the user's processes): each
/// thread with a frame naming one of the markers, from the first such frame to its last. On CI, where no one can attach
/// a debugger to what hangs, this names the call that never returned. Empty on other systems.</summary>
public static partial class NativeStacks
{
    public static string Describe(string what, int processId, params string[] markers)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return string.Empty;
        }
        string file = Path.Combine(Path.GetTempPath(), $"passenger-sample-{Guid.NewGuid():N}.txt");
        try
        {
            using System.Diagnostics.Process sample = System.Diagnostics.Process.Start(new ProcessStartInfo("/usr/bin/sample", [processId.ToString(System.Globalization.CultureInfo.InvariantCulture), "1", "-file", file])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            Task<string> said = sample.StandardOutput.ReadToEndAsync();
            Task<string> complained = sample.StandardError.ReadToEndAsync();
            if (!sample.WaitForExit(TimeSpan.FromMinutes(1)))
            {
                sample.Kill();
                return $"\n{what}: sample did not finish in a minute";
            }
            if (!File.Exists(file))
            {
                return $"\n{what}: sample could not look ({said.Result.Trim()} {complained.Result.Trim()})";
            }
            List<string> threads = [];
            string? thread = null;
            List<string> frames = [];
            foreach (string line in File.ReadLines(file).SkipWhile(l => !l.StartsWith("Call graph:", StringComparison.Ordinal)).Skip(1).TakeWhile(l => l.Trim().Length > 0))
            {
                if (ThreadLine().Match(line) is { Success: true } header)
                {
                    Keep();
                    thread = header.Groups[1].Value.Trim();
                }
                else if (FrameLine().Match(line) is { Success: true } frame)
                {
                    // A C++ frame's parameter list says nothing its name does not.
                    string name = frame.Groups[1].Value;
                    frames.Add($"{(name.IndexOf('(') is > 0 and int open ? name[..open] : name)} ({frame.Groups[2].Value})");
                }
            }
            Keep();
            return threads.Count == 0 ? $"\n{what}: no thread had a frame in {string.Join(" or ", markers)}" : $"\n{what}:\n{string.Join('\n', threads)}";

            void Keep()
            {
                int first = frames.FindIndex(f => markers.Any(m => f.Contains(m, StringComparison.Ordinal)));
                if (thread is not null && first >= 0)
                {
                    threads.Add($"  {thread}\n{string.Join('\n', frames.Skip(first).TakeLast(24).Select(f => $"    {f}"))}");
                }
                frames.Clear();
            }
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return $"\n{what}: sample failed: {error.Message}";
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>A child of a process by its program's name, from the process list (macOS and Linux); null when there is
    /// none, or elsewhere.</summary>
    public static int? ChildNamed(int parent, string name)
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }
        using System.Diagnostics.Process ps = System.Diagnostics.Process.Start(new ProcessStartInfo("ps", ["-A", "-o", "pid=,ppid=,comm="])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        string listed = ps.StandardOutput.ReadToEnd();
        ps.WaitForExit();
        foreach (string line in listed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] columns = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length == 3 && columns[1] == parent.ToString(System.Globalization.CultureInfo.InvariantCulture)
                && Path.GetFileName(columns[2]) == name && int.TryParse(columns[0], out int child))
            {
                return child;
            }
        }
        return null;
    }

    // "    881 Thread_57752918   DispatchQueue_1: com.apple.main-thread  (serial)"
    [System.Text.RegularExpressions.GeneratedRegex(@"^\s+\d+\s+(Thread_\d+.*)$")]
    private static partial System.Text.RegularExpressions.Regex ThreadLine();

    // "    +   881 main  (in ReadsInputConsoleApp) + 184  [0x1024fa124]"
    [System.Text.RegularExpressions.GeneratedRegex(@"^[\s+!:|]*\d+\s+(.+?)\s+\(in ([^)]+)\)")]
    private static partial System.Text.RegularExpressions.Regex FrameLine();
}
