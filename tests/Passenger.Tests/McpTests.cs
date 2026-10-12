using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Passenger.App;
using Passenger.App.Settings;
using Passenger.Editor;

namespace Passenger.Tests;

/// <summary>Passenger as an agent's IDE, over the Model Context Protocol with the SDK's own client: an
/// agent that starts Passenger with no window (<c>--mcp</c>) and works a project through it, and one
/// that joins the window a person is using (<c>--mcp-attach</c>) and changes what they see, once the
/// person lets agents in, asking them before it first changes anything.</summary>
[Collection(Debugging.Helpers.DebugSession.Collection)]
public sealed class McpTests : IDisposable
{
    private static readonly string Passenger = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Passenger.exe" : "Passenger");

    private readonly string folder = Directory.CreateTempSubdirectory("passenger-mcp-").FullName;
    private readonly string settings = Directory.CreateTempSubdirectory("passenger-agents-").FullName;
    private readonly List<string> errors = [];
    private string? settingsBefore;

    public McpTests()
    {
        Write("Sum.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        Write("Program.cs",
            "int total = 0;\nfor (int i = 1; i <= 3; i++)\n{\n    total += Helper.Twice(i);\n}\nConsole.WriteLine($\"total {total}\");\nthrow new InvalidOperationException(\"the end\");\n");
        Write("Helper.cs",
            "static class Helper\n{\n    public static int Twice(int value)\n    {\n        int unused = 1;\n        return value * 2;\n    }\n}\n");
        using Process restore = Process.Start(new ProcessStartInfo("dotnet", ["restore", folder]) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        restore.WaitForExit();
    }

    public void Dispose()
    {
        if (settingsBefore is not null)
        {
            Environment.SetEnvironmentVariable("PASSENGER_SETTINGS", settingsBefore.Length == 0 ? null : settingsBefore);
        }
        TestFolders.Delete(folder);
        TestFolders.Delete(settings);
    }

    [Fact]
    public async Task An_agent_with_a_windowless_Passenger_finds_code_reads_its_warnings_and_debugs_it()
    {
        await using McpClient client = await Start(["--mcp", folder]);
        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: Cancel);
        Assert.Contains(tools, t => t.Name == "debug_start");

        JsonElement symbol = Assert.Single((await Call(client, "find_symbol", new() { ["query"] = "Twice" })).EnumerateArray());
        Assert.Equal("Helper.cs", symbol.GetProperty("path").GetString());
        Assert.Equal(3, symbol.GetProperty("line").GetInt32());

        JsonElement warning = Assert.Single((await Call(client, "diagnostics", new() { ["path"] = "Helper.cs" })).EnumerateArray(),
            d => d.GetProperty("code").GetString() == "CS0219");
        Assert.Equal("warning", warning.GetProperty("severity").GetString());
        Assert.Equal(5, warning.GetProperty("line").GetInt32());

        // The breakpoint is the window's own: the stop comes back with where it is and the locals there.
        await Call(client, "breakpoint_set", new() { ["path"] = "Program.cs", ["line"] = 4 });
        JsonElement stop = await Call(client, "debug_start");
        Assert.Equal("paused", stop.GetProperty("state").GetString());
        Assert.StartsWith("Program.cs:4:", stop.GetProperty("location").GetString(), StringComparison.Ordinal);
        Assert.Equal("1", Local(stop, "i"));
        Assert.Equal("0", Local(stop, "total"));
        Assert.Equal("42", (await Call(client, "debug_evaluate", new() { ["expression"] = "Helper.Twice(i) + 40" })).GetProperty("value").GetString());

        stop = await Call(client, "debug_continue");
        Assert.Equal("2", Local(stop, "i"));

        // With the breakpoint gone the program runs to its unhandled exception, which is named.
        await Call(client, "breakpoint_remove", new() { ["path"] = "Program.cs", ["line"] = 4 });
        stop = await Call(client, "debug_continue");
        Assert.Equal("exception", stop.GetProperty("reason").GetString());
        Assert.StartsWith("System.InvalidOperationException: the end", stop.GetProperty("exception").GetString(), StringComparison.Ordinal);
        Assert.Contains("total 12", (await Call(client, "debug_output")).GetString(), StringComparison.Ordinal);

        CallToolResult picture = await client.CallToolAsync("screenshot", cancellationToken: Cancel);
        ImageContentBlock image = Assert.IsType<ImageContentBlock>(Assert.Single(picture.Content));
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], image.DecodedData.Slice(0, 4).ToArray());

        await Call(client, "stop");
        Assert.Equal("not debugging", (await Call(client, "debug_state")).GetProperty("state").GetString());

        // What a tool refuses reaches the agent in words that say what to do instead.
        CallToolResult refused = await client.CallToolAsync("debug_continue", cancellationToken: Cancel);
        Assert.True(refused.IsError);
        Assert.Contains("debug_start starts it", Assert.IsType<TextContentBlock>(Assert.Single(refused.Content)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_agent_reads_and_commits_changes_and_runs_a_command_in_the_ides_terminal()
    {
        string repository = Directory.CreateTempSubdirectory("passenger-mcp-git-").FullName;
        try
        {
            void Git(params string[] arguments)
            {
                using System.Diagnostics.Process git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", arguments) { WorkingDirectory = repository, RedirectStandardOutput = true, RedirectStandardError = true })!;
                git.WaitForExit();
            }
            Git("init", "-q", "-b", "main");
            Git("config", "user.email", "tests@passenger.invalid");
            Git("config", "user.name", "Passenger Tests");
            // Files are as they were written, not as the machine's git would convert them (Git for Windows' autocrlf).
            Git("config", "core.autocrlf", "false");
            File.WriteAllText(Path.Combine(repository, "Notes.txt"), "one\ntwo\nthree\n");
            Git("add", "-A");
            Git("commit", "-q", "-m", "First");
            File.WriteAllText(Path.Combine(repository, "Notes.txt"), "one\n2\nthree\n");
            File.WriteAllText(Path.Combine(repository, "New.txt"), "new\n");

            await using McpClient client = await Start(["--mcp", repository]);
            JsonElement status = await Call(client, "vcs_status");
            Assert.Equal("main", status.GetProperty("branch").GetString());
            Assert.Equal(["Notes.txt modified", "New.txt unversioned"], status.GetProperty("changes").EnumerateArray()
                .Select(c => $"{c.GetProperty("path").GetString()} {c.GetProperty("kind").GetString()}").Order(StringComparer.Ordinal).Reverse());
            string diff = (await Call(client, "vcs_diff", new() { ["path"] = "Notes.txt" })).GetString()!;
            Assert.Contains("-two\n+2\n", diff, StringComparison.Ordinal);

            // With no paths, every change but the new file goes in, as the Commit window has them chosen.
            Assert.EndsWith(" Two as a number", (await Call(client, "vcs_commit", new() { ["message"] = "Two as a number" })).GetString(), StringComparison.Ordinal);
            JsonElement log = await Call(client, "vcs_log", new() { ["count"] = 5 });
            Assert.Equal(["Two as a number", "First"], log.EnumerateArray().Select(c => c.GetProperty("subject").GetString()));
            Assert.Equal(["New.txt"], (await Call(client, "vcs_status")).GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("path").GetString()));

            // The IDE's own terminal runs it in the folder, and its answer comes back.
            string answer = (await Call(client, "terminal_run", new() { ["command"] = "echo $((6*7)) && ls" })).GetString()!;
            Assert.Contains("42", answer, StringComparison.Ordinal);
            Assert.Contains("Notes.txt", answer, StringComparison.Ordinal);
        }
        finally
        {
            TestFolders.Delete(repository);
        }
    }

    [AvaloniaFact]
    public void An_agent_attached_to_the_window_sets_a_breakpoint_the_person_sees()
    {
        UseSettings();
        MainWindow window = AgentsWindow(folder);
        string program = Path.Combine(window.Folder!, "Program.cs");
        window.OpenFile(program);
        Ui.Settle();
        string socket = window.AgentSocket!;

        // The agent's calls run on the UI thread, so the test lets it run while they are answered.
        Task<string?> agent = Task.Run(async () =>
        {
            await using McpClient client = await Start(["--mcp-attach", ProcessId]);
            await Call(client, "breakpoint_set", new() { ["path"] = "Program.cs", ["line"] = 4 });
            return (await Call(client, "workspace")).GetProperty("folder").GetString();
        });
        int shown = 0;
        Ui.WaitFor(() =>
        {
            shown = Math.Max(shown, window.Agents);
            return agent.IsCompleted;
        }, "the agent's calls");
        // The status bar says an agent is on the window while it is, and stops saying so once it goes.
        Assert.Equal(1, shown);
        Ui.WaitFor(() => window.Agents == 0, "the window to see the agent go");
        Assert.Equal(window.Folder, agent.GetAwaiter().GetResult());
        Assert.Equal(3, Assert.Single(window.Debugger.Breakpoints.In(program)).Line);
        Assert.Contains(new LineMark(3, LineMarkKind.Breakpoint), window.EditorArea.CodeEditor.LineMarks);

        // A closed IDE leaves no socket for the next agent to try.
        window.Close();
        Assert.False(File.Exists(socket), "A closed window left its socket for agents to try");
    }

    [AvaloniaFact]
    public void An_agent_is_refused_until_the_person_enables_the_MCP_server_and_shut_out_when_they_turn_it_off()
    {
        UseSettings();
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.KeepSettings();
        window.OpenFolder(folder);
        Ui.Settle();

        // Off as the window opens: no socket, and an agent that tries to attach is told what would let it in.
        Assert.Null(window.AgentSocket);
        Assert.Empty(Sockets());
        Exception? refused = Await(Task.Run(async () =>
        {
            try
            {
                await using McpClient client = await Start(["--mcp-attach", ProcessId]);
                return null;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return error;
            }
        }), "the agent trying to attach");
        Assert.True(refused is not null, "An agent reached a window whose MCP server is off");
        Ui.WaitFor(() => Errors().Any(l => l.Contains("Enable MCP Server", StringComparison.Ordinal)), "the refused agent told to turn on Enable MCP Server");

        // The person turns it on in Settings, under MCP Server, and the agent is in.
        SettingsDialog dialog = window.ShowSettings();
        Ui.Settle();
        dialog.Query = "agents";
        Ui.Settle();
        Assert.Equal([SettingsDialog.McpPage], dialog.Pages);
        Assert.Equal(["Enable MCP Server"], dialog.Shown);
        dialog.Change(s => s with { McpServer = true });
        dialog.Ok();
        Ui.Settle();
        string socket = Assert.Single(Sockets());
        Assert.Equal(socket, window.AgentSocket);
        McpClient client = Await(Task.Run(() => Start(["--mcp-attach", ProcessId])), "the agent attaching");
        Assert.Equal(window.Folder, Await(Task.Run(async () => (await Call(client, "workspace")).GetProperty("folder").GetString()), "the agent's workspace call"));
        Ui.WaitFor(() => window.AgentStatus == "Agent connected", "the status bar to say an agent is connected");

        // Turned off again, the agent's connection ends and the socket goes.
        window.ApplySettings(window.Settings with { McpServer = false });
        Ui.WaitFor(() => window.AgentStatus is null, "the status bar to say no agent is on the window");
        Assert.False(File.Exists(socket), "The window still listens for agents with its MCP server off");
        Assert.True(Await(Task.Run(async () =>
        {
            try
            {
                await client.CallToolAsync("workspace", cancellationToken: Cancel);
                return false;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return true;
            }
        }), "the shut-out agent's call"), "An agent kept using the window after its MCP server was turned off");
        Await(Task.Run(async () =>
        {
            await client.DisposeAsync();
            return true;
        }), "the agent going");
        window.Close();
    }

    [AvaloniaFact]
    public void An_agent_asks_before_its_first_run_and_the_window_stops_waiting_when_it_gives_up_or_goes()
    {
        UseSettings();
        Write("Program.cs", "Console.WriteLine(\"waiting\");\nThread.Sleep(Timeout.Infinite);\n");
        MainWindow window = AgentsWindow(folder);
        McpClient client = Await(Task.Run(() => Start(["--mcp-attach", ProcessId])), "the agent attaching");
        Dictionary<string, object?> run = new() { ["waitSeconds"] = 600 };
        try
        {
            // The first run waits for the person, who sees what it asks for and denies it.
            Task<CallToolResult> denied = Task.Run(() => client.CallToolAsync("run", run, cancellationToken: Cancel).AsTask());
            ConfirmDialog asked = Question(window, "the question for the agent's run");
            string[] said = Texts(asked);
            Assert.Contains("Allow run?", said);
            Assert.Contains(said, t => t.Contains("wants to run a configuration", StringComparison.Ordinal));
            Assert.Contains("waitSeconds: 600", said);
            Press(asked, "Deny");
            CallToolResult no = Await(denied, "the denied run");
            Assert.True(no.IsError, "A run the person denied ran");
            Assert.Contains("did not allow run", Assert.IsType<TextContentBlock>(Assert.Single(no.Content)).Text, StringComparison.Ordinal);
            Assert.False(window.Runner.IsRunning);

            // Asked again, and allowed: it runs, and the status bar says what the agent is doing.
            using CancellationTokenSource giveUp = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
            Task<CallToolResult> allowed = Task.Run(() => client.CallToolAsync("run", run, cancellationToken: giveUp.Token).AsTask());
            Press(Question(window, "the question asked again"), "Allow");
            Ui.WaitFor(() => window.Runner.IsRunning && window.AgentStatus == "Agent: Run a configuration", "the agent's run in the status bar");

            // The agent gives up on the call: the window stops waiting for it, and the program, the person's now, runs on.
            giveUp.Cancel();
            Ui.WaitFor(() => allowed.IsCompleted, "the call given up");
            Ui.WaitFor(() => window.AgentStatus == "Agent connected", "the window to stop waiting on the run the agent gave up on");
            Assert.True(window.Runner.IsRunning, "Giving up on the call stopped the program");
            window.Runner.Stop();
            Ui.WaitFor(() => !window.Runner.IsRunning, "the program stopped");

            // Allowed once, run is not asked again; and an agent that goes takes its waits with it.
            Task<CallToolResult> again = Task.Run(() => client.CallToolAsync("run", run, cancellationToken: Cancel).AsTask());
            Ui.WaitFor(() => window.Runner.IsRunning || window.OwnedWindows.OfType<ConfirmDialog>().Any(), "the run allowed before");
            Assert.Empty(window.OwnedWindows.OfType<ConfirmDialog>());
            Ui.WaitFor(() => window.AgentStatus == "Agent: Run a configuration", "the second run in the status bar");
            Await(Task.Run(async () =>
            {
                await client.DisposeAsync();
                return true;
            }), "the agent going");
            Ui.WaitFor(() => window.AgentStatus is null, "the window to stop waiting on the run of the agent that went");
            Ui.WaitFor(() => again.IsCompleted, "the call of the agent that went");
        }
        finally
        {
            window.Runner.Stop();
            Ui.WaitFor(() => !window.Runner.IsRunning, "the program stopped");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Each_window_has_its_own_socket_and_an_agent_attaches_to_the_one_over_its_folder()
    {
        UseSettings();
        string other = Directory.CreateTempSubdirectory("passenger-mcp-other-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(other, "Notes.txt"), "notes\n");
            Directory.CreateDirectory(Path.Combine(folder, "Inner"));
            MainWindow first = AgentsWindow(folder);
            MainWindow second = AgentsWindow(other);
            string firstSocket = first.AgentSocket!;
            string secondSocket = second.AgentSocket!;
            Assert.NotEqual(firstSocket, secondSocket);
            Assert.Equal(new[] { firstSocket, secondSocket }.Order(StringComparer.Ordinal), Sockets().Order(StringComparer.Ordinal));

            // Started in the first window's folder (deep in it), the agent reaches that window though the second is newer.
            Assert.Equal(first.Folder, AttachedFolder(Path.Combine(folder, "Inner")));
            Assert.Equal(second.Folder, AttachedFolder(other));

            // One window closing takes its own socket, not the other's.
            second.Close();
            Assert.False(File.Exists(secondSocket), "A closed window left its socket");
            Assert.True(File.Exists(firstSocket), "Closing one window took another's socket");
            // From a folder no window has, the agent reaches the window there is.
            Assert.Equal(first.Folder, AttachedFolder(Path.GetTempPath()));
            first.Close();
        }
        finally
        {
            TestFolders.Delete(other);
        }
    }

    /// <summary>The folder of the window an agent started in a folder reaches with <c>--mcp-attach</c>.</summary>
    private string? AttachedFolder(string workingDirectory) => Await(Task.Run(async () =>
    {
        await using McpClient client = await Start(["--mcp-attach"], workingDirectory);
        return (await Call(client, "workspace")).GetProperty("folder").GetString();
    }), $"an agent attaching from {workingDirectory}");

    [Fact]
    public async Task Started_as_the_Finder_starts_it_Passenger_runs_the_git_the_login_shell_finds()
    {
        if (OperatingSystem.IsWindows())
        {
            // A Windows program gets the person's whole environment, PATH included.
            return;
        }
        // A login profile that puts its own git first, as Homebrew's shellenv does; the git notes that it ran.
        string home = Directory.CreateTempSubdirectory("passenger-home-").FullName;
        try
        {
            string tools = Path.Combine(home, "tools");
            Directory.CreateDirectory(tools);
            string git = Path.Combine(tools, "git");
            string real = global::Passenger.Lsp.Programs.Git()!;
            File.WriteAllText(git, $"#!/bin/sh\ntouch \"{Path.Combine(home, "used")}\"\nexec {real} \"$@\"\n");
            File.SetUnixFileMode(git, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(Path.Combine(home, ".zprofile"), $"export PATH=\"{tools}:$PATH\"\n");
            File.WriteAllText(Path.Combine(home, ".profile"), $"export PATH=\"{tools}:$PATH\"\n");
            using (Process init = Process.Start(new ProcessStartInfo("git", ["init", "-q", folder]) { RedirectStandardOutput = true })!)
            {
                init.WaitForExit();
            }

            // launchd's environment: the bare PATH, no terminal, no .NET or zsh settings of the person's.
            await using McpClient client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "passenger",
                Command = Passenger,
                Arguments = ["--mcp", folder],
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["PATH"] = "/usr/bin:/bin:/usr/sbin:/sbin",
                    ["HOME"] = home,
                    ["SHELL"] = File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/sh",
                    ["TERM"] = null,
                    ["ZDOTDIR"] = null,
                    ["DOTNET_ROOT"] = null,
                },
            }), cancellationToken: Cancel);
            await Call(client, "vcs_status");
            Assert.True(File.Exists(Path.Combine(home, "used")), "Passenger ran a git other than the one its login shell's PATH finds");
        }
        finally
        {
            TestFolders.Delete(home);
        }
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static string ProcessId => Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Passenger started as an agent's MCP server, with the tests' state folder, where windows' sockets are.</summary>
    private Task<McpClient> Start(IList<string> arguments, string? workingDirectory = null) => McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "passenger",
        Command = Passenger,
        Arguments = arguments,
        WorkingDirectory = workingDirectory,
        EnvironmentVariables = new Dictionary<string, string?> { ["PASSENGER_SETTINGS"] = settings },
        StandardErrorLines = line =>
        {
            lock (errors)
            {
                errors.Add(line);
            }
        },
    }), cancellationToken: Cancel);

    /// <summary>The window's state, its socket for agents included, in the tests' own folder rather than the person's.</summary>
    private void UseSettings()
    {
        settingsBefore ??= Environment.GetEnvironmentVariable("PASSENGER_SETTINGS") ?? string.Empty;
        Environment.SetEnvironmentVariable("PASSENGER_SETTINGS", settings);
    }

    /// <summary>A window over a folder whose person has let agents in.</summary>
    private static MainWindow AgentsWindow(string at)
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(at);
        Ui.Settle();
        window.ApplySettings(window.Settings with { McpServer = true });
        Assert.True(window.AgentSocket is not null, $"The window does not listen for agents: {window.Status}");
        return window;
    }

    private string[] Sockets()
    {
        string sockets = Path.Combine(settings, "mcp");
        return Directory.Exists(sockets) ? Directory.GetFiles(sockets, "*.sock") : [];
    }

    private string[] Errors()
    {
        lock (errors)
        {
            return [.. errors];
        }
    }

    /// <summary>A task's result once done, the window's loop running meanwhile, since the agent's calls are answered on it.</summary>
    private static T Await<T>(Task<T> task, string what)
    {
        Ui.WaitFor(() => task.IsCompleted, what);
        return task.GetAwaiter().GetResult();
    }

    /// <summary>The window's question to the person, once it is asked.</summary>
    private static ConfirmDialog Question(MainWindow window, string what)
    {
        Ui.WaitFor(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(), what);
        return window.OwnedWindows.OfType<ConfirmDialog>().Single();
    }

    private static string[] Texts(ConfirmDialog dialog) =>
        [.. Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text ?? string.Empty)];

    private static void Press(ConfirmDialog dialog, string button)
    {
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).OfType<Avalonia.Controls.Button>().First(b => (string?)b.Content == button)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Ui.Settle();
    }

    /// <summary>A tool's answer, read as the JSON it is; a tool's error fails the test with its words and
    /// what Passenger wrote to standard error.</summary>
    private async Task<JsonElement> Call(McpClient client, string tool, Dictionary<string, object?>? arguments = null)
    {
        CallToolResult result = await client.CallToolAsync(tool, arguments, cancellationToken: Cancel);
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));
        if (result.IsError == true)
        {
            lock (errors)
            {
                Assert.Fail($"{tool} failed: {text}\n{string.Join('\n', errors.TakeLast(40))}");
            }
        }
        // A tool that answers in words (stop's "Stopped") is a JSON string of them.
        return text.StartsWith('[') || text.StartsWith('{') ? JsonDocument.Parse(text).RootElement.Clone() : JsonSerializer.SerializeToElement(text);
    }

    private static string? Local(JsonElement stop, string name) =>
        stop.GetProperty("locals").EnumerateArray().FirstOrDefault(l => l.GetProperty("name").GetString() == name) is { ValueKind: JsonValueKind.Object } local
            ? local.GetProperty("value").GetString()
            : null;

    private void Write(string relative, string text) => File.WriteAllText(Path.Combine(folder, relative), text);
}
