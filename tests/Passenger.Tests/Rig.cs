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
[assembly: Passenger.Tests.TestsLeaveNothingBehind]
[assembly: AssemblyFixture(typeof(Passenger.Tests.TestRun))]

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

/// <summary>What the whole run shares, set before anything reads it and never changed: a folder of the run's own in the
/// system's temporary folder, holding the IDE's state (<c>PASSENGER_SETTINGS</c>: its log, the language servers' logs,
/// the agents' sockets, the profiler's snapshots), every test's folders and the shells' history, and an empty Git
/// configuration in place of the person's and the system's, which every git the tests and the IDE run inherits. So a run
/// never writes to the person's own state, log or history, an agent of theirs never attaches to a test's window, and a
/// person's commit signing or hooks never reach a test's commit. The folder goes when the run ends; one a run that was
/// killed left goes when the next begins.</summary>
public sealed class TestRun : IDisposable
{
    private const string Prefix = "passenger-run-";

    /// <summary>The run's folder, named for the test process.</summary>
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), Prefix + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The IDE's state folder for the whole run.</summary>
    public static string Settings => Path.Combine(Root, "settings");

    /// <summary>Where the run's windows look for Discord (<c>PASSENGER_DISCORD_IPC</c>): a place nothing listens.</summary>
    public static string DiscordNowhere => OperatingSystem.IsWindows()
        ? $"{Prefix}{Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)}-discord-ipc"
        : Path.Combine(Root, "discord-ipc");

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Begin()
    {
        // Before anything is started, which inherits it.
        Nice.Normal();
        foreach (string left in Directory.EnumerateDirectories(Path.GetTempPath(), Prefix + "*"))
        {
            // A run still going has its process; one killed has none, and only an id reused since keeps its folder.
            if (int.TryParse(Path.GetFileName(left)[Prefix.Length..], out int id) && (id == Environment.ProcessId || !Processes.IsRunning(id)))
            {
                TestFolders.Delete(left);
            }
        }
        Directory.CreateDirectory(Settings);
        string gitConfig = Path.Combine(Root, "gitconfig");
        File.WriteAllText(gitConfig, string.Empty);
        Environment.SetEnvironmentVariable("PASSENGER_SETTINGS", Settings);
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", gitConfig);
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        // Configuration given on git's command line through the environment, which would come before the file's.
        Environment.SetEnvironmentVariable("GIT_CONFIG_PARAMETERS", null);
        Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", null);
        // The commands the terminals' shells run go into the run's history, not the person's: sh takes HISTFILE as given,
        // and zsh takes its startup files, and from them its history, from ZDOTDIR.
        Environment.SetEnvironmentVariable("HISTFILE", Path.Combine(Root, "history"));
        Environment.SetEnvironmentVariable("ZDOTDIR", Directory.CreateDirectory(Path.Combine(Root, "zsh")).FullName);
        NuGetHome();
        // No test reaches GitHub: a window or a Passenger started that checks for updates without a stand-in of its own
        // asks a loopback port nothing listens on.
        Environment.SetEnvironmentVariable(Passenger.App.Updates.ReleaseFeed.Variable, "http://127.0.0.1:9/repos/Zaneris/passenger/releases/latest");
        // Nor the person's Discord: a window without a stand-in of its own looks where nothing listens.
        Environment.SetEnvironmentVariable(Passenger.App.Discord.DiscordIpc.Variable, DiscordNowhere);
        // A run stopped before its end (Ctrl+C) leaves nothing either.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => End();
    }

    /// <summary>On macOS and Linux, NuGet's home for the run, which NuGet takes from DOTNET_CLI_HOME: its user nuget.config,
    /// its credential providers and its HTTP cache are the run's, so no test reads the person's feeds or credentials or
    /// writes their nuget.config, nor reads the machine's. The run's nuget.config names nuget.org and the person's package
    /// cache, as theirs did by default, so a restore downloads nothing new, and a folder's own nuget.config still says
    /// otherwise. dotnet's first run in that home leaves the certificate store and PATH alone.</summary>
    private static void NuGetHome()
    {
        if (OperatingSystem.IsWindows())
        {
            // NuGet reads the user's nuget.config from %APPDATA% there; each test's folder clears its feeds instead.
            return;
        }
        string personal = Environment.GetEnvironmentVariable("DOTNET_CLI_HOME") is { Length: > 0 } given ? given : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string home = Directory.CreateDirectory(Path.Combine(Root, "home")).FullName;
        string settings = Directory.CreateDirectory(Path.Combine(home, ".nuget", "NuGet")).FullName;
        string cache = System.Security.SecurityElement.Escape(Path.Combine(personal, ".nuget", "packages"));
        File.WriteAllText(Path.Combine(settings, "NuGet.Config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
              </packageSources>
              <config>
                <add key="globalPackagesFolder" value="{cache}" />
              </config>
            </configuration>
            """);
        Environment.SetEnvironmentVariable("DOTNET_CLI_HOME", home);
        Environment.SetEnvironmentVariable("NUGET_COMMON_APPLICATION_DATA", Directory.CreateDirectory(Path.Combine(Root, "machine")).FullName);
        Environment.SetEnvironmentVariable("NUGET_NETCORE_PLUGIN_PATHS", null);
        Environment.SetEnvironmentVariable("NUGET_PLUGIN_PATHS", null);
        Environment.SetEnvironmentVariable("DOTNET_GENERATE_ASPNET_CERTIFICATE", "false");
        Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", "false");
        Environment.SetEnvironmentVariable("DOTNET_NOLOGO", "1");
    }

    /// <summary>The run's end, once every test is done (xunit disposes the assembly's fixture then).</summary>
    public void Dispose()
    {
        End();
        // Once more after the log's own last flush, which it hooked after the run began, and so runs before this.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => End();
    }

    private static void End()
    {
        try
        {
            TestFolders.Delete(Root);
        }
        catch (Exception held) when (held is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"The tests' folder {Root} could not be removed: {held.Message}");
        }
    }
}

/// <summary>The run's nice value on macOS and Linux. MSBuild keeps no idle node for the next build at any nice value but 0,
/// and GitHub's macOS runner starts everything at -10: the run is brought to 0 as it begins, which lowers its priority and
/// so needs no privilege, and what it starts inherits that. A run started above 0 (by zsh's &amp;, at 5) could come down
/// only with privilege, so a test that needs a node kept skips there, saying why.</summary>
public static partial class Nice
{
    private const int PrioProcess = 0;

    /// <summary>The run's nice value now; 0 on Windows, which has none.</summary>
    public static int Value
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return 0;
            }
            // -1 is a nice value as well as the failure: only errno tells them apart.
            System.Runtime.InteropServices.Marshal.SetLastPInvokeError(0);
            int nice = GetPriority(PrioProcess, 0);
            return nice == -1 && System.Runtime.InteropServices.Marshal.GetLastPInvokeError() != 0 ? 0 : nice;
        }
    }

    /// <summary>Brings the run up to 0 from below. On Linux a nice value is each thread's, so each thread there is now.</summary>
    internal static void Normal()
    {
        if (OperatingSystem.IsWindows() || Value >= 0)
        {
            return;
        }
        IEnumerable<int> threads = OperatingSystem.IsLinux() && Directory.Exists("/proc/self/task")
            ? Directory.EnumerateDirectories("/proc/self/task").Select(task => int.Parse(Path.GetFileName(task), System.Globalization.CultureInfo.InvariantCulture))
            : [0];
        foreach (int thread in threads)
        {
            SetPriority(PrioProcess, thread, 0);
        }
    }

    /// <summary>Skips a test that needs MSBuild to keep a node for the next build, when the run is at a nice value at which
    /// it keeps none.</summary>
    public static void SkipUnlessMSBuildKeepsNodes() => Assert.SkipWhen(Value != 0,
        $"The tests run at nice {Value}, at which MSBuild keeps no idle node for the next build, and only a privileged process could bring them to 0");

    [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "getpriority", SetLastError = true)]
    private static partial int GetPriority(int which, int who);

    [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "setpriority", SetLastError = true)]
    private static partial int SetPriority(int which, int who, int priority);
}

/// <summary>A test leaves nothing behind for the next, however it ends. A window test starts with no background work held:
/// each test has a dispatcher of its own, and work a test left running (one that failed before its window settled, or
/// closed it mid-parse) ends on that dispatcher, which never runs again, and held for ever would fail every window test
/// after it on <see cref="Ui.Settle"/>'s limit. And after a window test, every window it left open is closed (one that
/// failed before its own close) and what those windows started (a language server, a build, a debugger) is waited out,
/// then the folders the test made as it ran are removed, the windows that used them gone first.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class TestsLeaveNothingBehindAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (IsWindowTest(methodUnderTest))
        {
            BackgroundWork.Forget();
            OpenWindows.Watch();
        }
        TestFolders.Begin(test.UniqueID);
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        try
        {
            if (IsWindowTest(methodUnderTest) && !OpenWindows.CloseAll())
            {
                TestContext.Current.SendDiagnosticMessage($"{test.TestDisplayName} left {BackgroundWork.Pending} pieces of background work held after its windows closed");
            }
        }
        finally
        {
            TestFolders.End(test.UniqueID);
        }
    }

    public static bool IsWindowTest(MethodInfo method) =>
        method.GetCustomAttribute<AvaloniaFactAttribute>() is not null || method.GetCustomAttribute<AvaloniaTheoryAttribute>() is not null;
}

/// <summary>The windows open now, as a test leaves them, for <see cref="TestsLeaveNothingBehindAttribute"/> to close.</summary>
public static class OpenWindows
{
    private static readonly List<Window> Open = [];
    private static bool watching;

    /// <summary>The longest the work of windows closed after a test may take to land, past the end their own limit gives
    /// what they started.</summary>
    private static readonly TimeSpan Landing = TimeSpan.FromSeconds(30);

    public static void Watch()
    {
        if (!watching)
        {
            watching = true;
            Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Open.Add(window));
            Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => Open.Remove(window));
        }
        Open.Clear();
    }

    /// <summary>Closes every window still open, the last opened first, waits for what each started to end (its own limit
    /// kills what will not), then for their background work to land, which a dispatcher that never runs again could not
    /// let go. Whether it all landed: work that never does, the next window test lets go of as it starts.</summary>
    public static bool CloseAll()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException("Windows left open can only be closed on their own thread");
        }
        List<Task> ending = [];
        while (Open.Count > 0)
        {
            Window last = Open[^1];
            Open.RemoveAt(Open.Count - 1);
            if (last.IsVisible)
            {
                last.Close();
                if (last is Passenger.App.MainWindow { Ended: { } ended })
                {
                    ending.Add(ended);
                }
            }
        }
        if (ending.Count == 0 && BackgroundWork.Pending == 0)
        {
            return true;
        }
        DateTime until = DateTime.UtcNow + Passenger.App.MainWindow.EndLimit + Landing;
        while (!(ending.All(e => e.IsCompleted) && BackgroundWork.Pending == 0) && DateTime.UtcNow < until)
        {
            Passenger.App.Screenshot.Pump(TimeSpan.FromMilliseconds(20));
        }
        return BackgroundWork.Pending == 0;
    }
}

/// <summary>A code editor alone in a window, driven the way a person drives it: keys by Rider's
/// macOS keymap (so a test reads the same on every platform), typed text, and clicks.</summary>
public sealed class EditorRig
{
    public EditorRig(string text)
    {
        // The one document a test makes: the editor's, which the test then drives as a person does.
#pragma warning disable RS0030
        Document = new TextDocument(text);
#pragma warning restore RS0030
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

/// <summary>A test's temporary folders, in the run's own (<see cref="TestRun.Root"/>), deleted as Windows lets them be: git
/// writes its objects read-only, which Windows refuses to delete, and a folder a process still runs in (a git status the
/// closed window started, a debugged program being killed) cannot go until it has ended, so both are waited out for a
/// while. One already gone is no error.</summary>
public static class TestFolders
{
    /// <summary>The folders each test running now made as it ran, by the test's id, from its start (its class's are made
    /// before then, and go with its class's Dispose).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<string>> Made = new(StringComparer.Ordinal);

    /// <summary>A new, empty folder of the test's own, its name starting with <paramref name="name"/>: one made as a test
    /// runs is removed when it ends, however it ends, after its windows (<see cref="TestsLeaveNothingBehindAttribute"/>).</summary>
    public static string Create(string name)
    {
        string path;
        do
        {
            path = Path.Combine(TestRun.Root, name + Guid.NewGuid().ToString("N")[..8]);
        }
        while (Directory.Exists(path));
        Directory.CreateDirectory(path);
        if (TestContext.Current.Test?.UniqueID is { } test && Made.TryGetValue(test, out List<string>? made))
        {
            lock (made)
            {
                made.Add(path);
            }
        }
        return path;
    }

    /// <summary>Gets a test class's folders ready (a restore, a repository), removing them if that fails: a class whose
    /// constructor throws is never disposed.</summary>
    public static void Prepare(Action prepare, params string[] paths)
    {
        try
        {
            prepare();
        }
        catch
        {
            foreach (string path in paths)
            {
                Delete(path);
            }
            throw;
        }
    }

    public static void Begin(string test) => Made[test] = [];

    public static void End(string test)
    {
        if (!Made.TryRemove(test, out List<string>? made))
        {
            return;
        }
        List<Exception> held = [];
        foreach (string path in made)
        {
            try
            {
                Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                held.Add(error);
            }
        }
        if (held.Count > 0)
        {
            throw new IOException($"The test's folders could not all be removed, so the next run finds them: {string.Join("; ", held.Select(e => e.Message))}", held[0]);
        }
    }

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
        bool shown = window.IsVisible;
        window.KeyPress(stroke.Key, modifiers, PhysicalKey.None, null);
        // A press that closed its window (a dialog's Enter, done at once) leaves the release nowhere to go.
        if (!shown || window.IsVisible)
        {
            window.KeyRelease(stroke.Key, modifiers, PhysicalKey.None, null);
        }
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

    /// <summary>Waits for a task the window started (a run, a debug session's start) as <see cref="WaitFor"/> waits, and
    /// gives its answer.</summary>
    public static T Await<T>(Task<T> task, string what, TimeSpan? limit = null)
    {
        WaitFor(() => task.IsCompleted, what, limit);
        return task.GetAwaiter().GetResult();
    }

    /// <summary>Lets the window's background work land, as a screenshot does, then finds what it shows, with no wait of
    /// the test's own: what that work brings (Git's answers, a search's) must be work the window counts.</summary>
    public static void Landed(Func<bool> shown, string what)
    {
        Settle();
        Assert.True(shown(), $"Once its background work had landed the window still lacked {what}");
    }

    /// <summary>Waits for something to become true while background work is still under way (a git made slow, an answer
    /// held back), running the dispatcher's loop but never waiting for that work to land as <see cref="WaitFor"/> does.</summary>
    public static void PumpUntil(Func<bool> condition, string what, TimeSpan? limit = null)
    {
        DateTime until = DateTime.UtcNow + (limit ?? TimeSpan.FromSeconds(90));
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"Waited {(limit ?? TimeSpan.FromSeconds(90)).TotalSeconds} s for {what}");
            }
            Passenger.App.Screenshot.Pump(TimeSpan.FromMilliseconds(20));
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

    /// <summary>A submenu of the menu bar as the platform opens it: asked to bring its items up to date first.</summary>
    public static NativeMenu Submenu(NativeMenu parent, string title)
    {
        NativeMenu menu = parent.Items.OfType<NativeMenuItem>().First(i => i.Header == title).Menu!;
        Type bridge = menu.GetType().GetInterfaces().First(i => i.GetMethod("RaiseNeedsUpdate") is not null);
        bridge.GetMethod("RaiseNeedsUpdate")!.Invoke(menu, null);
        return menu;
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

/// <summary>A test's restore, run as the IDE runs the .NET command line (<see cref="Passenger.Core.ChildProcesses.DotnetCommand"/>):
/// what MSBuild leaves of it is the tests' own and ends with them, and no variable of dotnet test's holds its output open.</summary>
public static class Restore
{
    /// <summary>The longest a restore may take: nuget.org slow, a cache cold.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromMinutes(5);

    /// <summary>The restore, which fails the test at once naming what it means and quoting what dotnet said: a project
    /// not restored (offline, nuget.org down) would show up later as C# unavailable, far from the cause.</summary>
    public static async Task<Passenger.Core.ChildResult> RunAsync(string target)
    {
        Passenger.Core.ChildResult restored;
        try
        {
            restored = await Passenger.Core.ChildProcesses.RunAsync(Passenger.Core.ChildProcesses.DotnetCommand(["restore", target]), limit: Limit);
        }
        catch (TimeoutException hung)
        {
            throw new TimeoutException($"The test's project did not restore, so C# cannot load it: {hung.Message}", hung);
        }
        Assert.True(restored.Succeeded,
            $"The test's project did not restore, so C# cannot load it: dotnet restore {target} exited with code {restored.ExitCode}:\n{restored.Output}{restored.Error}");
        return restored;
    }

    /// <summary>The restore, waited for off the UI thread, whose continuations would otherwise wait on it.</summary>
    public static Passenger.Core.ChildResult Run(string target) => Task.Run(() => RunAsync(target)).GetAwaiter().GetResult();
}

/// <summary>Git as the tests' fixtures run it: as the IDE runs it (<see cref="Passenger.Core.ChildProcesses"/>, so none of a
/// hook's variables points it at another repository, and with the run's empty configuration), its output read, within a
/// limit, failing the test naming the command and what git said when it fails.</summary>
public static class TestGit
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(1);

    public static string Run(string folder, params string[] arguments)
    {
        Passenger.Core.ChildResult result;
        try
        {
            result = Task.Run(() => Passenger.Core.ChildProcesses.RunAsync(Passenger.Core.ChildProcesses.Command("git", arguments, folder), limit: Limit)).GetAwaiter().GetResult();
        }
        catch (TimeoutException hung)
        {
            throw new TimeoutException($"The test's repository is not as it was to be made: {hung.Message}", hung);
        }
        Assert.True(result.Succeeded,
            $"The test's repository is not as it was to be made: git {string.Join(' ', arguments)} in {folder} exited with code {result.ExitCode}:\n{result.Error}{result.Output}");
        return result.Output;
    }

    /// <summary>A new repository on main, with an author of its own, its files kept as written (not as Git for Windows'
    /// autocrlf would convert them) and its first commit of what the folder holds.</summary>
    public static void Init(string folder)
    {
        Run(folder, "init", "-q", "-b", "main");
        Run(folder, "config", "user.email", "tests@passenger.invalid");
        Run(folder, "config", "user.name", "Passenger Tests");
        Run(folder, "config", "core.autocrlf", "false");
        Run(folder, "add", "-A");
        Run(folder, "commit", "-q", "-m", "First");
    }
}

/// <summary>Programs the IDE started for a test, by id, as a process list would show them.</summary>
public static partial class Processes
{
    /// <summary>The id a program wrote to a file once it was running, the window's loop running meanwhile (not settling,
    /// which a build under way would not let it do).</summary>
    public static int WrittenId(string file, string what) => WrittenId([file], what);

    /// <summary>The id the first of several programs to run wrote to its file.</summary>
    public static int WrittenId(IReadOnlyList<string> files, string what)
    {
        DateTime until = DateTime.UtcNow.AddMinutes(2);
        int id;
        while ((id = files.Select(ReadId).FirstOrDefault(i => i > 0)) <= 0)
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

    /// <summary>Each process now by id, as ps lists it on macOS and Linux: its id, parent, nice value and command line, for a
    /// failure to say what ran and at what priority. Empty on Windows, which has no ps.</summary>
    public static IReadOnlyDictionary<int, string> Listed() =>
        OperatingSystem.IsWindows() ? new Dictionary<int, string>() : ById(Ps("-A", "-o", "pid=,ppid=,nice=,command="));

    /// <summary>Each MSBuild worker node now by id, with the MSBuild variables of its environment (its handshake's salt among
    /// them): ps shows a process's environment to its user on macOS, /proc on Linux. Empty on Windows.</summary>
    public static IReadOnlyDictionary<int, string> MSBuildNodes()
    {
        Dictionary<int, string> nodes = [];
        if (OperatingSystem.IsWindows())
        {
            return nodes;
        }
        foreach ((int id, string line) in ById(Ps(OperatingSystem.IsMacOS() ? ["-A", "-E", "-o", "pid=,command="] : ["-A", "-o", "pid=,command="])))
        {
            if (line.Contains("/nodemode:1", StringComparison.Ordinal))
            {
                nodes[id] = string.Join(' ', MSBuildVariable().Matches(OperatingSystem.IsMacOS() ? line : Environ(id)).Select(m => m.Value));
            }
        }
        return nodes;

        static string Environ(int id)
        {
            try
            {
                return File.ReadAllText($"/proc/{id}/environ").Replace('\0', ' ');
            }
            catch (Exception gone) when (gone is IOException or UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>The worker nodes MSBuild counts now as a build ends, and the most it keeps for the next build, one and a half
    /// a processor: the MSBuild executable's (those of MSBuild's server, which a terminal's dotnet build uses), not the
    /// .NET host's that a build Passenger starts runs in. While it counts more, a build ending keeps none of its nodes.
    /// None counted on Windows, which has no ps.</summary>
    public static (int Counted, int Most) MSBuildNodesCounted()
    {
        int counted = Listed().Values.Count(line => line.Split(' ', 5, StringSplitOptions.RemoveEmptyEntries) is [_, _, _, string program, string rest]
            && program.EndsWith("/MSBuild", StringComparison.Ordinal) && rest.Contains("/nodemode:1", StringComparison.Ordinal));
        return (counted, Math.Max(1, Environment.ProcessorCount * 3 / 2));
    }

    private static string[] Ps(params string[] arguments)
    {
        using System.Diagnostics.Process ps = System.Diagnostics.Process.Start(new ProcessStartInfo("ps", arguments) { RedirectStandardOutput = true })!;
        string text = ps.StandardOutput.ReadToEnd();
        ps.WaitForExit();
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static Dictionary<int, string> ById(IEnumerable<string> lines)
    {
        Dictionary<int, string> found = [];
        foreach (string line in lines)
        {
            if (line.Split(' ', 2) is [string first, _] && int.TryParse(first, out int id))
            {
                found[id] = line;
            }
        }
        return found;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\bMSBUILD[A-Z_]*=\S*")]
    private static partial System.Text.RegularExpressions.Regex MSBuildVariable();

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
    public static int ReadId(string file)
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

    /// <summary>Whether a process has gone within a few seconds, the time a killed one may take to leave the list, or within
    /// the time given.</summary>
    public static bool Gone(int id, TimeSpan? within = null)
    {
        DateTime until = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(15));
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
        catch (Exception gone) when (gone is ArgumentException or InvalidOperationException or AggregateException or System.ComponentModel.Win32Exception)
        {
            // Gone, or ending: Windows refuses to end a process of the tree that is (ChildProcesses.Kill).
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
        string file = Path.Combine(TestRun.Root, $"sample-{Guid.NewGuid():N}.txt");
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
