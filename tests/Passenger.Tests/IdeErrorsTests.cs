using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Passenger.App;

namespace Passenger.Tests;

/// <summary>An error inside the IDE is caught, written to the log and shown, never the end of it; a language server
/// that ends on its own is started again with the open files, as Rider restarts its backend.</summary>
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
        string log = File.ReadAllText(IdeErrors.LogPath);
        Assert.Contains("Next Tab went wrong", log);
        Assert.Contains("A posted job went wrong", log);
        Assert.Contains(IdeErrors.Errors, e => e.Kind == "InvalidOperationException" && e.Message == "Next Tab went wrong");
        // Once only, though the posted job's task, faulted with it and never awaited, reaches the finalizer.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Ui.Settle();
        Assert.Single(IdeErrors.Errors, e => e.Kind == "FormatException" && e.Message == "A posted job went wrong");
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
        int first = window.CSharpService!.Session!.Server.ProcessId;

        Process.GetProcessById(first).Kill();
        Ui.WaitFor(() => window.CSharpService.Session is { IsLoaded: true } session && session.Server.ProcessId != first, "a new language server");
        Assert.Contains($"process {first}) ended", File.ReadAllText(IdeErrors.LogPath));

        // The file open before is open in the new server: an error typed into it is reported.
        string text = window.EditorArea.CodeEditor.Document.Snapshot.ToString();
        window.EditorArea.CodeEditor.Selections = Text.SelectionSet.Single(text.IndexOf("    }", StringComparison.Ordinal));
        window.KeyTextInput("int number = \"text\";");
        Ui.WaitFor(() => window.EditorArea.Active!.Markers.Any(m => m.Kind == Editor.MarkerKind.Error), "the new server's diagnostics");
        window.Close();
    }
}
