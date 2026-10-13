using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.ToolWindows;

namespace Passenger.Tests;

/// <summary>An error inside the IDE is caught, written to the log and shown, never the end of it; a language server
/// that ends on its own, or stops answering, is started again with the open files, as Rider restarts its backend, and one
/// still starting when its folder closes is ended, as is one a window test that failed left loading.</summary>
public sealed class IdeErrorsTests : IDisposable
{
    private readonly string folder = TestFolders.Create("errors-");

    /// <summary>The language servers' logs from before the test, in the log folder the whole run shares.</summary>
    private readonly HashSet<string> earlierServers = [.. AllServerLogs()];

    public void Dispose()
    {
        IdeErrors.Clear();
        TestFolders.Delete(folder);
    }

    [AvaloniaFact]
    public void A_command_that_throws_leaves_the_window_up_with_the_error_in_the_log_and_the_status_bar()
    {
        File.WriteAllText(Path.Combine(folder, "One.cs"), "class One { }\n");
        File.WriteAllText(Path.Combine(folder, "Two.cs"), "class Two { }\n");
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile("One.cs");
        window.OpenFile("Two.cs");
        Ui.Settle();
        IdeErrors.Clear();
        bool thrown = false;
        window.EditorArea.ActiveChanged += (_, _) =>
        {
            if (!thrown)
            {
                thrown = true;
                throw new InvalidOperationException("Next Tab went wrong");
            }
        };

        Ui.Run(window, AppActions.NextTab);
        // And one thrown from work posted to the UI thread, as a click's handler runs.
        Dispatcher.UIThread.Post(() => throw new FormatException("A posted job went wrong"));
        Ui.Settle();

        Assert.True(window.IsVisible);
        string log = Ui.IdeLog();
        Assert.Contains("Next Tab went wrong", log);
        Assert.Contains("A posted job went wrong", log);
        Assert.Contains(IdeErrors.Errors, e => e.Kind == "InvalidOperationException" && e.Message == "Next Tab went wrong");
        // Once only, though the posted job's task, faulted with it and never awaited, reaches the finalizer.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Ui.Settle();
        IdeError posted = Assert.Single(IdeErrors.Errors, e => e.Kind == "FormatException" && e.Message == "A posted job went wrong");
        Assert.True(posted.Count == 1, $"One exception reported twice (the dispatcher's, then its task's) was counted {posted.Count} times");
        Assert.Equal("IDE error: A posted job went wrong", window.Status);

        // The window still works: the next key does what it says.
        Assert.Equal("One.cs", window.EditorArea.Active!.Name);
        Ui.Run(window, AppActions.NextTab);
        Assert.Equal("Two.cs", window.EditorArea.Active!.Name);
        window.ShowErrors();
        Ui.Settle();
        Assert.IsType<ErrorsPopup>(window.Popup);
        // Thrown on purpose: the check that fails a test over a caught error is not to see these.
        IdeErrors.Clear();
        window.Close();
    }

    [AvaloniaFact]
    public void The_same_error_again_and_again_is_one_row_with_its_count_and_a_log_rotated_under_its_size_cap()
    {
        long limit = IdeErrors.LogLimit;
        IdeErrors.LogLimit = 16 * 1024;
        try
        {
            MainWindow window = new() { Width = 1200, Height = 800 };
            window.Show();
            Ui.Settle();
            IdeErrors.Clear();

            // As a timer whose every tick throws would, three thousand times over.
            for (int i = 0; i < 3000; i++)
            {
                Dispatcher.UIThread.Post(Tick);
            }
            Ui.Settle();

            Assert.True(IdeErrors.Errors.Count == 1, $"One error thrown again and again was listed {IdeErrors.Errors.Count} times");
            Assert.Equal(3000, IdeErrors.Errors[0].Count);
            Assert.Equal("1", window.FindControl<TextBlock>("ErrorsCount")!.Text);
            ErrorsPopup popup = window.ShowErrors();
            Ui.Settle();
            ListBoxItem row = Assert.Single(popup.GetVisualDescendants().OfType<ListBoxItem>());
            string?[] shown = [.. row.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)];
            Assert.Contains(shown, t => t?.EndsWith("  InvalidOperationException: The timer went wrong", StringComparison.Ordinal) == true);
            Assert.Contains("3000 times", shown);
            Assert.Contains(popup.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Occurred 3000 times, from ", StringComparison.Ordinal) == true);

            // The log's own thread writes the last repeat unasked, a line of its own: the details are the first time's.
            Ui.WaitFor(() => Ui.Shared(IdeErrors.LogPath).Contains("Error, occurred 3000 times: InvalidOperationException: The timer went wrong", StringComparison.Ordinal),
                "the log's thread to write the last repeat");
            IdeErrors.Flush();
            string[] logs = Directory.GetFiles(IdeErrors.LogFolder, "passenger*.log");
            Assert.All(logs, log => Assert.True(new FileInfo(log).Length <= IdeErrors.LogLimit,
                $"{Path.GetFileName(log)} grew to {new FileInfo(log).Length} bytes, past the log's limit of {IdeErrors.LogLimit}"));
            // Rider's idea.log rotation: twelve kept beside the current one, the oldest gone.
            Assert.Equal(
                Enumerable.Range(1, IdeErrors.Rotations).Select(n => $"passenger.{n}.log").Append("passenger.log").Order(StringComparer.Ordinal),
                logs.Select(log => Path.GetFileName(log)).Order(StringComparer.Ordinal));
            int traces = logs.Sum(log => Ui.Shared(log).Split($"{nameof(IdeErrorsTests)}.{nameof(Tick)}()").Length - 1);
            Assert.True(traces <= 1, $"The stack trace is in the log {traces} times: a repeat wrote it again rather than a line");
            IdeErrors.Clear();
            window.Close();
        }
        finally
        {
            IdeErrors.LogLimit = limit;
        }
    }

    [AvaloniaFact]
    public void Past_a_hundred_different_errors_the_rest_are_only_in_the_log_and_the_count_says_there_are_more()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        Ui.Settle();
        IdeErrors.Clear();

        for (int i = 0; i < IdeErrors.Limit + 20; i++)
        {
            string message = $"Error number {i}";
            Dispatcher.UIThread.Post(() => throw new InvalidOperationException(message));
        }
        Ui.Settle();

        Assert.True(IdeErrors.Errors.Count == IdeErrors.Limit, $"{IdeErrors.Errors.Count} errors were listed, not the limit of {IdeErrors.Limit}");
        Assert.Equal("Error number 0", IdeErrors.Errors[0].Message);
        Assert.Equal(20, IdeErrors.Unlisted);
        Assert.Equal($"{IdeErrors.Limit}+", window.FindControl<TextBlock>("ErrorsCount")!.Text);
        Assert.Contains($"Error number {IdeErrors.Limit + 19}", Ui.IdeLog(), StringComparison.Ordinal);
        ErrorsPopup popup = window.ShowErrors();
        Ui.Settle();
        Assert.Contains(popup.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("as are 20 more", StringComparison.Ordinal) == true);
        IdeErrors.Clear();
        window.Close();
    }

    [AvaloniaFact]
    public void A_window_test_in_which_the_ide_caught_an_error_fails_naming_it()
    {
        MainWindow window = new() { Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.Post(() => throw new ArgumentException("A posted job went wrong in a test"));
        Ui.Settle();
        FailOnIdeErrorsAttribute check = new();
        System.Reflection.MethodInfo self = typeof(IdeErrorsTests).GetMethod(nameof(A_window_test_in_which_the_ide_caught_an_error_fails_naming_it))!;
        InvalidOperationException failed = Assert.Throws<InvalidOperationException>(() => check.After(self, null!));
        Assert.Contains("ArgumentException: A posted job went wrong in a test", failed.Message, StringComparison.Ordinal);
        Assert.Empty(IdeErrors.Errors);
        window.Close();
    }

    [AvaloniaFact]
    public void A_language_server_that_ends_is_started_again_each_time_with_the_open_file_and_a_log_of_its_own_beside_the_ides()
    {
        MainWindow window = OpenProgram();
        List<int> ended = [];
        foreach (string time in (string[])["once", "a second time"])
        {
            int server = window.CSharpService!.Session!.Server.ProcessId;
            Process.GetProcessById(server).Kill();
            Ui.WaitFor(() => window.CSharpService.Session is { IsLoaded: true } session && session.Server.ProcessId != server,
                $"the language server to be started again after it ended {time} ({window.CSharpService.State})");
            Assert.Contains($"process {server}) ended", Ui.IdeLog());
            ended.Add(server);
        }

        // Each server's log is its own, in the folder Help, Show Log opens, and let go once the server is gone.
        foreach (int server in ended)
        {
            string[] logs = [.. ServerLogs().Where(log => ServerOf(log) == server)];
            Assert.True(logs.Length == 1, $"The server that ended (process {server}) left {logs.Length} logs of its own beside the IDE's, in {IdeErrors.LogFolder}");
            Ui.WaitFor(() => LetGo(logs[0]), $"the ended server's log {Path.GetFileName(logs[0])} to be closed, which until then can be neither moved nor deleted");
        }
        TypeAnErrorAndSeeItReported(window);
        window.Close();
    }

    [AvaloniaFact]
    public void Opening_another_folder_while_csharp_starts_leaves_only_the_new_folders_language_server_running()
    {
        string other = TestFolders.Create("other-");
        WriteProgram(folder);
        WriteProgram(other);
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        // Its server has begun once its log has, named for its process; the window's loop runs, never waiting for the load.
        Ui.PumpUntil(() => ServerLogs().Length > 0, $"a language server to begin for the first folder ({window.CSharpService?.State})", TimeSpan.FromMinutes(1));
        int first = ServerOf(Assert.Single(ServerLogs()));
        Assert.True(window.CSharpService?.Session is null, "The first folder's language server answered before the second folder opened, so it was not still starting");

        window.OpenFolder(other);
        Ui.Settle();

        Assert.True(window.CSharpService?.IsReady, window.CSharpService?.State);
        int second = window.CSharpService!.Session!.Server.ProcessId;
        Assert.True(Processes.Gone(first), $"The first folder's language server (process {first}) still runs, loading a folder no longer open");
        int[] running = [.. ServerLogs().Select(ServerOf).Where(Processes.IsRunning)];
        Assert.True(running.SequenceEqual([second]), $"Language servers running: {string.Join(", ", running.Select(Processes.Describe))}; only the second folder's, {second}, should be");
        window.Close();
    }

    [AvaloniaFact]
    public void A_window_test_that_throws_right_after_opening_a_project_leaves_no_language_server_and_no_work_for_the_next()
    {
        WriteProgram(folder);
        int server = 0;
        void FailingTest()
        {
            MainWindow window = new() { Width = 1200, Height = 800 };
            window.Show();
            window.OpenFolder(folder);
            window.OpenFile("Program.cs");
            Ui.PumpUntil(() => window.CSharpService?.Session is not null, $"the language server to start on the project ({window.CSharpService?.State})");
            server = window.CSharpService!.Session!.Server.ProcessId;
            throw new InvalidOperationException("The test failed with its project still loading, before its own close");
        }
        Assert.Throws<InvalidOperationException>(FailingTest);
        Assert.True(Processes.IsRunning(server), "The language server had ended before the test failed, so there was none for the failure to leave");

        // What runs after every window test, however it ended.
        new TestsLeaveNothingBehindAttribute().After(
            typeof(IdeErrorsTests).GetMethod(nameof(A_window_test_that_throws_right_after_opening_a_project_leaves_no_language_server_and_no_work_for_the_next))!,
            (Xunit.v3.IXunitTest)TestContext.Current.Test!);

        Assert.True(Processes.Gone(server), $"The language server (process {server}) still runs after the window test that opened it failed, loading a folder for nobody into the next test");
        Assert.True(Editor.BackgroundWork.Pending == 0, $"{Editor.BackgroundWork.Pending} pieces of the failed test's work were still held for the next test to wait out");
    }

    [AvaloniaFact]
    public void A_language_server_that_stops_answering_is_reported_not_responding_and_started_again()
    {
        MainWindow window = OpenProgram();
        int first = window.CSharpService!.Session!.Server.ProcessId;

        Processes.Suspend(first, "The language server");
        bool replaced = false;
        try
        {
            // Find Usages waits on a server that will never answer, as on one deadlocked: without the watch, for ever.
            string text = window.EditorArea.CodeEditor.Document.Snapshot.ToString();
            window.EditorArea.CodeEditor.Selections = Text.SelectionSet.Single(text.IndexOf("Main", StringComparison.Ordinal) + 1);
            Ui.Run(window, AppActions.FindUsages);
            Ui.WaitFor(() => window.CSharpService.Session is { IsLoaded: true } session && session.Server.ProcessId != first, "a new language server");
            replaced = true;
        }
        finally
        {
            if (!replaced)
            {
                End(first);
            }
        }
        // Said as the server is replaced, then by Find Usages, which it never answered, once its connection is gone.
        Assert.Equal("Find Usages failed: the language server stopped responding, so it was ended", window.Status);
        Assert.Contains($"process {first}) is not responding: textDocument/references waited", Ui.IdeLog());
        Ui.WaitFor(() => !Running(first), "the server that stopped answering to be ended, not left behind");

        TypeAnErrorAndSeeItReported(window);
        window.Close();
    }

    [AvaloniaFact]
    public void The_hierarchy_window_shown_before_the_language_server_started_again_still_opens_a_row_after_it()
    {
        MainWindow window = OpenProgram(Calls);
        Place(window, "static void Ot");
        Task<string?> shown = window.ShowCallHierarchyAsync();
        Ui.WaitFor(() => shown.IsCompleted, "the call hierarchy");
        Assert.Null(shown.Result);
        HierarchyRow caller = Assert.Single(window.Hierarchy.Rows, r => r.Depth == 1);
        Assert.Contains("Helper", caller.Node.Item.Name, StringComparison.Ordinal);

        int server = window.CSharpService!.Session!.Server.ProcessId;
        Process.GetProcessById(server).Kill();
        Ui.WaitFor(() => window.CSharpService.Session is { IsLoaded: true } session && session.Server.ProcessId != server,
            $"the language server to be started again ({window.CSharpService.State})");

        // Its callers are asked of the server running now: the one the window was shown with is gone.
        Task opening = window.Hierarchy.ExpandAsync(caller);
        Ui.WaitFor(() => opening.IsCompleted, "the caller's row to open");
        Assert.True(opening.IsCompletedSuccessfully, $"Opening a row of the Hierarchy window after the language server started again failed: {opening.Exception?.InnerException}");
        Assert.True(caller.IsExpanded, "The caller's row did not open");
        Assert.All(caller.Children!, c => Assert.Contains("Main", c.Item.Name, StringComparison.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public void Call_hierarchy_asked_while_the_language_server_starts_again_says_why_it_cannot_and_works_once_it_is_back()
    {
        MainWindow window = OpenProgram(Calls);
        Place(window, "static void Ot");
        int first = window.CSharpService!.Session!.Server.ProcessId;

        // Asked of a server that stops before it answers, as one that crashes or is ended for hanging does.
        Processes.Suspend(first, "The language server");
        Task<string?> asked;
        try
        {
            asked = window.ShowCallHierarchyAsync();
            Screenshot.Pump(TimeSpan.FromMilliseconds(200));
            Assert.False(asked.IsCompleted, "Call Hierarchy answered from a stopped server");
        }
        finally
        {
            End(first);
        }
        Ui.PumpUntil(() => asked.IsCompleted, "Call Hierarchy to give up on the server that ended");
        Assert.Equal("Call Hierarchy failed: the language server stopped before it answered", asked.Result);

        // Asked while the next one starts: there is no server to ask.
        Ui.PumpUntil(() => window.CSharpService.Session is null, "the ended server to be let go");
        Task<string?> starting = window.ShowCallHierarchyAsync();
        Assert.True(starting.IsCompleted, "Call Hierarchy waited for a server that is not there");
        Assert.StartsWith("Call Hierarchy needs the C# language server: ", starting.Result, StringComparison.Ordinal);
        Assert.Equal(starting.Result, window.Status);

        Ui.WaitFor(() => window.CSharpService.Session is { IsLoaded: true }, $"the language server to be started again ({window.CSharpService.State})");
        Task<string?> again = window.ShowCallHierarchyAsync();
        Ui.WaitFor(() => again.IsCompleted, "the call hierarchy from the new server");
        Assert.Null(again.Result);
        Assert.Contains(window.Hierarchy.Rows, r => r.Depth == 1 && r.Node.Item.Name.Contains("Helper", StringComparison.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public void A_rename_that_cannot_write_a_file_it_changes_says_so_in_the_status_bar()
    {
        string library = Path.Combine(folder, "Library");
        Directory.CreateDirectory(library);
        File.WriteAllText(Path.Combine(library, "Helper.cs"), "static class Helper\n{\n    public static int Twice(int value) => value * 2;\n}\n");
        MainWindow window = OpenProgram("class Program\n{\n    static void Main()\n    {\n        int result = Helper.Twice(2);\n    }\n}\n");
        Place(window, "Helper.Tw");
        using (Unwritable(Path.Combine(library, "Helper.cs")))
        {
            Ui.Run(window, AppActions.Rename);
            Ui.Settle();
            window.KeyTextInput("Double");
            Ui.Press(window, "ENTER");
            Ui.Settle();
            Assert.StartsWith("Rename failed: Helper.cs could not be written", window.Status, StringComparison.Ordinal);
        }
        Assert.Contains("Twice", File.ReadAllText(Path.Combine(library, "Helper.cs")), StringComparison.Ordinal);
        window.Close();
    }

    /// <summary>Three methods, each calling the next.</summary>
    private const string Calls = "class Program\n{\n    static void Main()\n    {\n        Helper();\n    }\n\n    static void Helper()\n    {\n        Other();\n    }\n\n    static void Other()\n    {\n    }\n}\n";

    /// <summary>Puts the caret just after some text of the file shown.</summary>
    private static void Place(MainWindow window, string after)
    {
        int at = window.EditorArea.CodeEditor.Document.Snapshot.ToString().IndexOf(after, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{after}' is not in the file");
        window.EditorArea.CodeEditor.Selections = Text.SelectionSet.Single(at + after.Length);
    }

    /// <summary>A file no save can replace while this is held: its folder made read-only on macOS and Linux, and on Windows
    /// the file held open for reading only, which refuses both the move over it and a write in place.</summary>
    private static IDisposable Unwritable(string file)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        string parent = Path.GetDirectoryName(file)!;
        UnixFileMode mode = File.GetUnixFileMode(parent);
        File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return new Restored(() =>
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(parent, mode);
            }
        });
    }

    private sealed class Restored(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    /// <summary>A restored console project with its Program.cs open, the language server ready on it.</summary>
    private MainWindow OpenProgram(string? program = null)
    {
        WriteProgram(folder, program);
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile("Program.cs");
        Ui.Settle();
        Assert.True(window.CSharpService?.IsReady, window.CSharpService?.State);
        return window;
    }

    /// <summary>A console project in a folder, restored.</summary>
    private static void WriteProgram(string at, string? program = null)
    {
        File.WriteAllText(Path.Combine(at, "Sample.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(at, "Program.cs"), program ?? "class Program\n{\n    static void Main()\n    {\n    }\n}\n");
        Restore.Run(at);
    }

    /// <summary>The language servers' logs this test's IDE wrote, one to each server it started, where Help, Show Log finds
    /// them: in the folder of the IDE's own log.</summary>
    private string[] ServerLogs() => [.. AllServerLogs().Where(log => !earlierServers.Contains(log))];

    private static string[] AllServerLogs() =>
        Directory.Exists(IdeErrors.LogFolder) ? Directory.GetFiles(IdeErrors.LogFolder, "server-*.log", SearchOption.AllDirectories) : [];

    /// <summary>The server a log is of: its process, the last part of the log's name.</summary>
    private static int ServerOf(string log) =>
        int.Parse(Path.GetFileNameWithoutExtension(log).Split('-')[^1], System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Whether nothing holds a file open any longer: only then does it open unshared, on Windows and macOS alike.</summary>
    private static bool LetGo(string path)
    {
        try
        {
            using FileStream only = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>The file open before is open in the new server: an error typed into it is reported.</summary>
    private static void TypeAnErrorAndSeeItReported(MainWindow window)
    {
        string text = window.EditorArea.CodeEditor.Document.Snapshot.ToString();
        window.EditorArea.CodeEditor.Selections = Text.SelectionSet.Single(text.IndexOf("    }", StringComparison.Ordinal));
        window.KeyTextInput("int number = \"text\";");
        Ui.WaitFor(() => window.EditorArea.Active!.Markers.Any(m => m.Kind == Editor.MarkerKind.Error), "the new server's diagnostics");
    }

    /// <summary>Kills a server the IDE never replaced, so a failed test leaves none stopped behind.</summary>
    private static void End(int id)
    {
        if (!OperatingSystem.IsWindows())
        {
            Signal(id, 9);
            return;
        }
        try
        {
            using Process process = Process.GetProcessById(id);
            process.Kill();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
        }
    }

    private static bool Running(int id)
    {
        try
        {
            using Process process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Signal(int process, int signal);

    private static void Tick() => throw new InvalidOperationException("The timer went wrong");
}
