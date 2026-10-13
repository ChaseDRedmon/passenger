using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;

namespace Passenger.Tests;

/// <summary>The logs folder Help, Show Log opens holds what a person needs when something goes wrong: why the language
/// server could not load a project (said in the status bar too), its load's progress as it goes, each start's phases with
/// their times, and with the diagnostic settings on the protocols' traffic and every program the IDE started. The logs
/// folder is the whole run's, so what a test looks for there names its own server, adapter or folder.</summary>
public sealed class LogsTests : IDisposable
{
    private readonly string folder = TestFolders.Create("logs-");

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void A_project_naming_a_missing_sdk_says_why_in_the_status_bar_and_the_ides_log()
    {
        File.WriteAllText(Path.Combine(folder, "Bad.csproj"),
            "<Project Sdk=\"Missing.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(folder, "Program.cs"), "class Program\n{\n    static void Main()\n    {\n    }\n}\n");
        MainWindow window = new() { Width = 1400, Height = 800 };
        window.Show();
        List<string> languageShown = [];
        using IDisposable watching = window.FindControl<TextBlock>("LanguageText")!.GetObservable(TextBlock.TextProperty)
            .Subscribe(new Watcher(text => languageShown.Add(text ?? string.Empty)));
        window.OpenFolder(folder);
        window.OpenFile("Program.cs");
        Ui.WaitFor(() => window.CSharpService is { IsReady: true }, $"the language server to finish loading ({window.CSharpService?.State})");
        Ui.Settle();

        const string Reason = "The SDK 'Missing.Sdk' specified could not be found.";
        Assert.True(window.Status?.Contains(Reason, StringComparison.Ordinal) == true,
            $"The status bar says \"{window.Status}\" once the language server has loaded, not why Bad.csproj did not load");
        Assert.StartsWith("Error while loading Bad.csproj: ", window.Status, StringComparison.Ordinal);
        int server = window.CSharpService!.Session!.Server.ProcessId;
        string[] log = Ui.IdeLog().Split('\n');
        Assert.True(log.Any(line => line.Contains($"C# language server (process {server}) error: ", StringComparison.Ordinal) && line.Contains(Reason, StringComparison.Ordinal)),
            $"The IDE's log does not say why Bad.csproj did not load:\n{string.Join('\n', log.Where(line => line.Contains($"(process {server})", StringComparison.Ordinal)))}");
        Assert.Contains(log, line => line.Contains($"C# language server (process {server}) says: There were problems loading project Bad.csproj", StringComparison.Ordinal));
        // The load's progress showed beside the language as it went, and is gone once it is done.
        Assert.True(languageShown.Any(t => t.Contains("Loading 1 project(s)…", StringComparison.Ordinal)),
            $"The status bar never showed the projects loading; it showed: {string.Join(" | ", languageShown.Distinct())}");
        Ui.WaitFor(() => window.FindControl<TextBlock>("LanguageText")!.Text == "C#", "the load's progress to leave the status bar");
        window.Close();
    }

    [AvaloniaFact]
    public void Tracing_writes_the_language_servers_messages_to_lsp_log_and_every_program_to_the_ides_log_until_turned_off()
    {
        File.WriteAllText(Path.Combine(folder, "Sample.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(folder, "Program.cs"), "class Program\n{\n    static void Main()\n    {\n    }\n}\n");
        Restore.Run(folder);
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.ApplySettings(window.Settings with { TraceProtocols = true, LogEveryProgram = true });
        try
        {
            window.OpenFolder(folder);
            window.OpenFile("Program.cs");
            Ui.WaitFor(() => window.CSharpService is { IsReady: true }, $"the language server to load the project ({window.CSharpService?.State})");
            int server = window.CSharpService!.Session!.Server.ProcessId;
            Ui.WaitFor(() => Ui.IdeLog().Contains($"(process {server}) gave an open file its colours", StringComparison.Ordinal), "the first colours to be logged");

            string trace = Logged("lsp.log");
            Assert.True(Traced(trace, $"[server {server}] --> ", "\"method\":\"initialize\""),
                $"lsp.log in the logs folder does not have the initialize the server was sent:\n{Start(trace)}");
            Assert.True(Traced(trace, $"[server {server}] <-- ", "\"method\":\"workspace/projectInitializationComplete\""),
                $"lsp.log does not have the server's word that the project loaded:\n{Start(trace)}");
            Assert.True(Traced(trace, $"[server {server}] --> ", "\"method\":\"textDocument/semanticTokens/full\""),
                $"lsp.log does not have the open file's colours asked for:\n{Start(trace)}");

            // Every program, quick or not, with its command line, as Rider's debug log has its processes.
            string log = Ui.IdeLog();
            Assert.True(log.Contains($"Process {server} started", StringComparison.Ordinal) && log.Contains("Microsoft.CodeAnalysis.LanguageServer.dll --stdio", StringComparison.Ordinal),
                $"The IDE's log does not have the language server's command line:\n{log}");
            // The phases of the folder's opening, each with its time.
            Assert.Contains($"Opened {window.Folder}", log, StringComparison.Ordinal);
            Assert.Matches($@"C# language server \(process {server}\) started for .* in \d+\.\d\d s, loading 1 project", log);
            Assert.Matches($@"C# language server \(process {server}\) loaded 1 project in \d+\.\d\d s", log);

            // Off, nothing more is traced: a change typed now is not in lsp.log.
            window.ApplySettings(window.Settings with { TraceProtocols = false });
            int traced = Logged("lsp.log").Length;
            window.KeyTextInput("// untraced");
            Ui.Settle();
            Screenshot.Pump(TimeSpan.FromMilliseconds(500));
            string after = Logged("lsp.log");
            Assert.True(after.Length == traced, $"lsp.log grew after the trace was turned off:\n{after[traced..]}");
        }
        finally
        {
            window.ApplySettings(window.Settings with { TraceProtocols = false, LogEveryProgram = false });
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Tracing_writes_the_debuggers_messages_to_dap_log_beside_each_sessions_start_and_end()
    {
        File.WriteAllText(Path.Combine(folder, "Sum.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(folder, "Program.cs"), "System.Console.WriteLine(\"total 6\");\n");
        Restore.Run(folder);
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.ApplySettings(window.Settings with { TraceProtocols = true });
        try
        {
            window.OpenFolder(folder);
            window.OpenFile("Program.cs");
            Ui.Settle();
            Ui.Run(window, AppActions.Debug);
            Ui.WaitFor(() => window.Debugger.Session is { State: Passenger.App.Debugging.DebugState.Ended }, "the program to run to its end under the debugger");
            int adapter = window.Debugger.Session!.AdapterProcessId;

            string log = Logged("dap.log");
            Assert.True(log.Contains($"[adapter {adapter}] launch ", StringComparison.Ordinal) && log.Contains($"[adapter {adapter}] Ended debugging", StringComparison.Ordinal),
                $"dap.log in the logs folder does not have the session's start and end:\n{Start(log)}");
            Assert.True(Traced(log, $"[adapter {adapter}] --> ", "\"command\":\"launch\""), $"dap.log does not have the launch the adapter was sent:\n{Start(log)}");
            Assert.True(Traced(log, $"[adapter {adapter}] <-- ", "\"event\":\"exited\""), $"dap.log does not have the adapter's word that the program ended:\n{Start(log)}");
        }
        finally
        {
            window.ApplySettings(window.Settings with { TraceProtocols = false });
            window.Close();
        }
    }

    /// <summary>A log in the logs folder, as a person opening it would find it.</summary>
    private static string Logged(string name)
    {
        IdeErrors.Flush();
        return Ui.Shared(Path.Combine(IdeErrors.LogFolder, name));
    }

    /// <summary>Whether a trace has a line of one server and direction that holds a part of a message.</summary>
    private static bool Traced(string trace, string from, string part) =>
        trace.Split('\n').Any(line => line.Contains(from, StringComparison.Ordinal) && line.Contains(part, StringComparison.Ordinal));

    private static string Start(string text) => text.Length > 2000 ? text[..2000] : text;

    private sealed class Watcher(Action<string?> next) : IObserver<string?>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(string? value) => next(value);
    }
}
