using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Passenger.App;

namespace Passenger.Tests;

/// <summary>An error inside the IDE is caught, written to the log and shown, never the end of it; a language server
/// that ends on its own, or stops answering, is started again with the open files, as Rider restarts its backend.</summary>
public sealed class IdeErrorsTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("passenger-errors-").FullName;
    private readonly string settings = Directory.CreateTempSubdirectory("passenger-settings-").FullName;
    private readonly string? before = Environment.GetEnvironmentVariable("PASSENGER_SETTINGS");

    public IdeErrorsTests() => Environment.SetEnvironmentVariable("PASSENGER_SETTINGS", settings);

    public void Dispose()
    {
        IdeErrors.Clear();
        Environment.SetEnvironmentVariable("PASSENGER_SETTINGS", before);
        TestFolders.Delete(folder);
        TestFolders.Delete(settings);
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
    public void A_language_server_that_ends_is_started_again_and_the_open_file_is_in_the_new_one()
    {
        MainWindow window = OpenProgram();
        int first = window.CSharpService!.Session!.Server.ProcessId;

        Process.GetProcessById(first).Kill();
        Ui.WaitFor(() => window.CSharpService.Session is { IsLoaded: true } session && session.Server.ProcessId != first, "a new language server");
        Assert.Contains($"process {first}) ended", Ui.IdeLog());

        TypeAnErrorAndSeeItReported(window);
        window.Close();
    }

    [AvaloniaFact]
    public void A_language_server_that_stops_answering_is_reported_not_responding_and_started_again()
    {
        MainWindow window = OpenProgram();
        int first = window.CSharpService!.Session!.Server.ProcessId;

        Suspend(first);
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
        Assert.Equal("C# server not responding; starting it again", window.Status);
        Assert.Contains($"process {first}) is not responding: textDocument/references waited", Ui.IdeLog());
        Ui.WaitFor(() => !Running(first), "the server that stopped answering to be ended, not left behind");

        TypeAnErrorAndSeeItReported(window);
        window.Close();
    }

    /// <summary>A restored console project with its Program.cs open, the language server ready on it.</summary>
    private MainWindow OpenProgram()
    {
        File.WriteAllText(Path.Combine(folder, "Sample.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(folder, "Program.cs"), "class Program\n{\n    static void Main()\n    {\n    }\n}\n");
        using (Process restore = Process.Start(new ProcessStartInfo("dotnet", ["restore", folder]) { RedirectStandardOutput = true, RedirectStandardError = true })!)
        {
            restore.WaitForExit();
        }
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile("Program.cs");
        Ui.Settle();
        Assert.True(window.CSharpService?.IsReady, window.CSharpService?.State);
        return window;
    }

    /// <summary>The file open before is open in the new server: an error typed into it is reported.</summary>
    private static void TypeAnErrorAndSeeItReported(MainWindow window)
    {
        string text = window.EditorArea.CodeEditor.Document.Snapshot.ToString();
        window.EditorArea.CodeEditor.Selections = Text.SelectionSet.Single(text.IndexOf("    }", StringComparison.Ordinal));
        window.KeyTextInput("int number = \"text\";");
        Ui.WaitFor(() => window.EditorArea.Active!.Markers.Any(m => m.Kind == Editor.MarkerKind.Error), "the new server's diagnostics");
    }

    /// <summary>Stops a process where it stands, answering nothing, as a deadlocked server would: SIGSTOP on macOS and
    /// Linux, and on Windows every thread suspended (NtSuspendProcess), which a kill ends as surely. Signalled, not by
    /// running kill: on macOS a child ending while another is stopped sets .NET reaping in a spin that holds Process's lock.</summary>
    private static void Suspend(int id)
    {
        if (OperatingSystem.IsWindows())
        {
            using Process process = Process.GetProcessById(id);
            int status = NtSuspendProcess(process.Handle);
            Assert.True(status == 0, $"The language server could not be suspended (NTSTATUS 0x{status:X8})");
            return;
        }
        Assert.True(Signal(id, OperatingSystem.IsMacOS() ? 17 : 19) == 0, $"The language server could not be stopped (errno {Marshal.GetLastPInvokeError()})");
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

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr process);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Signal(int process, int signal);

    private static void Tick() => throw new InvalidOperationException("The timer went wrong");
}
