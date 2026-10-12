using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless;
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
/// person lets agents in, asking them before it first changes anything. Every exchange with Passenger has a time limit,
/// and one that runs out or fails ends the test naming it and quoting what Passenger said meanwhile.</summary>
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
        IList<McpClientTool> tools = await Exchange("Listing the tools", TimeSpan.FromMinutes(1), cancel => client.ListToolsAsync(cancellationToken: cancel).AsTask());
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

        CallToolResult picture = await Result(client, "screenshot");
        ImageContentBlock image = Assert.IsType<ImageContentBlock>(Assert.Single(picture.Content));
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], image.DecodedData.Slice(0, 4).ToArray());

        await Call(client, "stop");
        Assert.Equal("not debugging", (await Call(client, "debug_state")).GetProperty("state").GetString());

        // What a tool refuses reaches the agent in words that say what to do instead.
        CallToolResult refused = await Result(client, "debug_continue");
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
        WaitFor(() =>
        {
            shown = Math.Max(shown, window.Agents);
            return agent.IsCompleted;
        }, "the agent's calls", AgentLimit);
        // The status bar says an agent is on the window while it is, and stops saying so once it goes.
        Assert.Equal(1, shown);
        WaitFor(() => window.Agents == 0, "the window to see the agent go");
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
            catch (Exception error) when (error is not (OperationCanceledException or TimeoutException))
            {
                return error;
            }
        }), "the agent trying to attach");
        Assert.True(refused is not null, "An agent reached a window whose MCP server is off");
        WaitFor(() => Errors().Any(l => l.Contains("Enable MCP Server", StringComparison.Ordinal)), "the refused agent told to turn on Enable MCP Server");

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
        WaitFor(() => window.AgentStatus == "Agent connected", "the status bar to say an agent is connected");

        // Turned off again, the agent's connection ends and the socket goes.
        window.ApplySettings(window.Settings with { McpServer = false });
        WaitFor(() => window.AgentStatus is null, "the status bar to say no agent is on the window");
        Assert.False(File.Exists(socket), "The window still listens for agents with its MCP server off");
        Assert.True(Await(Task.Run(async () =>
        {
            try
            {
                await Result(client, "workspace");
                return false;
            }
            catch (Exception error) when (error is not (OperationCanceledException or TimeoutException))
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
            Task<CallToolResult> denied = Task.Run(() => Result(client, "run", run));
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
            Task<CallToolResult> allowed = Task.Run(() => Result(client, "run", run, giveUp));
            Press(Question(window, "the question asked again"), "Allow");
            WaitFor(() => window.Runner.IsRunning && window.AgentStatus == "Agent: Run a configuration", "the agent's run in the status bar");

            // The agent gives up on the call: the window stops waiting for it, and the program, the person's now, runs on.
            giveUp.Cancel();
            WaitFor(() => allowed.IsCompleted, "the call given up");
            WaitFor(() => window.AgentStatus == "Agent connected", "the window to stop waiting on the run the agent gave up on");
            Assert.True(window.Runner.IsRunning, "Giving up on the call stopped the program");
            window.Runner.Stop();
            WaitFor(() => !window.Runner.IsRunning, "the program stopped");

            // Allowed once, run is not asked again; and an agent that goes takes its waits with it.
            Task<CallToolResult> again = Task.Run(() => Result(client, "run", run));
            WaitFor(() => window.Runner.IsRunning || window.OwnedWindows.OfType<ConfirmDialog>().Any(), "the run allowed before");
            Assert.Empty(window.OwnedWindows.OfType<ConfirmDialog>());
            WaitFor(() => window.AgentStatus == "Agent: Run a configuration", "the second run in the status bar");
            Await(Task.Run(async () =>
            {
                await client.DisposeAsync();
                return true;
            }), "the agent going");
            WaitFor(() => window.AgentStatus is null, "the window to stop waiting on the run of the agent that went");
            WaitFor(() => again.IsCompleted, "the call of the agent that went");
            Assert.True(again.Exception is not null || again.IsCanceled, "A call was answered after its agent went");
        }
        finally
        {
            window.Runner.Stop();
            WaitFor(() => !window.Runner.IsRunning, "the program stopped");
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

    [AvaloniaFact]
    public void The_window_takes_keys_while_an_attached_agent_finds_ten_thousand_usages_each_with_its_line()
    {
        UseSettings();
        // Four files calling Helper.Twice 2,500 times each, every call under a long comment: a usage's line read from its
        // file's start every time reads each file 2,500 times over.
        for (int file = 0; file < 4; file++)
        {
            System.Text.StringBuilder text = new($"static class Uses{file}\n{{\n    public static int All()\n    {{\n        int total = 0;\n");
            for (int call = 0; call < 2500; call++)
            {
                text.Append("        // ").Append('=', 100).Append('\n');
                text.Append("        total += Helper.Twice(").Append(call).Append(");\n");
            }
            Write($"Uses{file}.cs", text.Append("        return total;\n    }\n}\n").ToString());
        }
        Write("Notes.txt", string.Empty);
        MainWindow window = AgentsWindow(folder);
        window.OpenFile(Path.Combine(window.Folder!, "Helper.cs"));
        window.OpenFile(Path.Combine(window.Folder!, "Notes.txt"));
        window.EditorArea.CodeEditor.Focus();
        Ui.Settle();
        McpClient client = Await(Task.Run(() => Start(["--mcp-attach", ProcessId])), "the agent attaching");
        try
        {
            // The language server has the folder loaded before the window is timed, which is the server's work, not the agent's.
            Await(Task.Run(() => Call(client, "diagnostics", new() { ["path"] = "Helper.cs" })), "the language server");

            // The person types in another file while the agent asks for every usage of Twice.
            Task<JsonElement> usages = Task.Run(() => Call(client, "usages", new() { ["path"] = "Helper.cs", ["line"] = 3, ["column"] = 23 }));
            int typed = 0;
            TimeSpan longest;
            using (InputWait waits = new())
            {
                // The call's own limit ends this if Passenger never answers.
                while (!usages.IsCompleted)
                {
                    window.KeyTextInput("x");
                    typed++;
                    Screenshot.Pump(TimeSpan.FromMilliseconds(50));
                }
                waits.Stop();
                longest = waits.Longest;
            }
            JsonElement[] found = [.. usages.GetAwaiter().GetResult().EnumerateArray()];
            Ui.Settle();

            // Every usage comes back, each with its own line, and every key the person typed meanwhile landed at once.
            Assert.Equal(10_002, found.Length);
            Assert.All(found, place => Assert.Contains("Twice(", place.GetProperty("text").GetString(), StringComparison.Ordinal));
            Assert.Contains(found, place => place.GetProperty("path").GetString() == "Uses3.cs" && place.GetProperty("line").GetInt32() == 5005
                && place.GetProperty("text").GetString() == "total += Helper.Twice(2499);");
            Assert.Equal(new string('x', typed), window.EditorArea.Active!.Text.Snapshot.ToString());
            Assert.True(longest < TimeSpan.FromMilliseconds(250),
                $"a key waited {longest.TotalMilliseconds:F0} ms for the window while the agent's {found.Length} usages were found");

            // The callers' hierarchy has each caller's line the same way: each file's method at its first call.
            JsonElement callers = Await(Task.Run(() => Call(client, "hierarchy", new() { ["path"] = "Helper.cs", ["line"] = 3, ["column"] = 23 })), "the callers");
            Assert.Equal(["Uses0.cs:7 total += Helper.Twice(0);", "Uses1.cs:7 total += Helper.Twice(0);", "Uses2.cs:7 total += Helper.Twice(0);", "Uses3.cs:7 total += Helper.Twice(0);"],
                callers.EnumerateArray().Select(c => $"{c.GetProperty("path").GetString()}:{c.GetProperty("line").GetInt32()} {c.GetProperty("text").GetString()}")
                    .Where(c => c.StartsWith("Uses", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        }
        finally
        {
            Await(Task.Run(async () =>
            {
                await client.DisposeAsync();
                return true;
            }), "the agent going");
            window.Close();
        }
    }

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
            await using McpClient client = await Start(["--mcp", folder], environment: new()
            {
                ["PATH"] = "/usr/bin:/bin:/usr/sbin:/sbin",
                ["HOME"] = home,
                ["SHELL"] = File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/sh",
                ["TERM"] = null,
                ["ZDOTDIR"] = null,
                ["DOTNET_ROOT"] = null,
            });
            await Call(client, "vcs_status");
            Assert.True(File.Exists(Path.Combine(home, "used")), "Passenger ran a git other than the one its login shell's PATH finds");
        }
        finally
        {
            TestFolders.Delete(home);
        }
    }

    /// <summary>Longer than any one exchange may take, for a wait on an agent's calls, whose own limits fail first and
    /// name the call.</summary>
    private static readonly TimeSpan AgentLimit = TimeSpan.FromMinutes(6);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static string ProcessId => Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The debugger engine's log of what Passenger debugs, which a failed call quotes.</summary>
    private string EngineLog => Path.Combine(settings, "engine.log");

    /// <summary>Passenger started as an agent's MCP server, with the tests' state folder (where windows' sockets are, and
    /// its log), the engine's log and any other change to the environment, its standard error kept.</summary>
    private Task<McpClient> Start(IList<string> arguments, string? workingDirectory = null, Dictionary<string, string?>? environment = null)
    {
        Dictionary<string, string?> variables = new() { ["PASSENGER_SETTINGS"] = settings, ["PASSENGER_ENGINE_LOG"] = EngineLog };
        foreach ((string name, string? value) in environment ?? new())
        {
            variables[name] = value;
        }
        StdioClientTransport transport = new(new StdioClientTransportOptions
        {
            Name = "passenger",
            Command = Passenger,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            EnvironmentVariables = variables,
            StandardErrorLines = line =>
            {
                lock (errors)
                {
                    errors.Add(line);
                }
            },
        });
        return Exchange($"Starting passenger {string.Join(' ', arguments)}", TimeSpan.FromMinutes(1), cancel => McpClient.CreateAsync(transport, cancellationToken: cancel));
    }

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
    private T Await<T>(Task<T> task, string what)
    {
        WaitFor(() => task.IsCompleted, what, AgentLimit);
        return task.GetAwaiter().GetResult();
    }

    /// <summary><see cref="Ui.WaitFor"/>, failing with what Passenger said meanwhile.</summary>
    private void WaitFor(Func<bool> condition, string what, TimeSpan? limit = null)
    {
        try
        {
            Ui.WaitFor(condition, what, limit);
        }
        catch (TimeoutException waited)
        {
            throw new TimeoutException(waited.Message + Said(), waited);
        }
    }

    /// <summary>The window's question to the person, once it is asked.</summary>
    private ConfirmDialog Question(MainWindow window, string what)
    {
        WaitFor(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(), what);
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
    /// what Passenger said meanwhile.</summary>
    private async Task<JsonElement> Call(McpClient client, string tool, Dictionary<string, object?>? arguments = null)
    {
        CallToolResult result = await Result(client, tool, arguments);
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));
        if (result.IsError == true)
        {
            Assert.Fail($"{tool} failed: {text}{Said()}");
        }
        // A tool that answers in words (stop's "Stopped") is a JSON string of them.
        return text.StartsWith('[') || text.StartsWith('{') ? JsonDocument.Parse(text).RootElement.Clone() : JsonSerializer.SerializeToElement(text);
    }

    /// <summary>A tool's whole result, an error's included, within the time the tool may take, or until the test gives up
    /// on it.</summary>
    private Task<CallToolResult> Result(McpClient client, string tool, Dictionary<string, object?>? arguments = null, CancellationTokenSource? giveUp = null) =>
        Exchange($"The {tool} call", Limit(tool), cancel => client.CallToolAsync(tool, arguments, cancellationToken: cancel).AsTask(), giveUp);

    /// <summary>How long a tool may take before the test calls it hung: minutes for one that builds first; for one that asks
    /// the language server, longer than the 120 s Passenger waits for it to load the folder, so its own words come back;
    /// a minute for any other.</summary>
    private static TimeSpan Limit(string tool) => tool switch
    {
        "build" or "debug_start" or "run" or "tests_run" or "profile" or "nuget_install" or "nuget_remove" => TimeSpan.FromMinutes(5),
        "diagnostics" or "symbol_info" or "definition" or "usages" or "hierarchy" or "implementations" or "rename" or "code_actions"
            or "apply_code_action" or "format" or "find_symbol" or "outline" => TimeSpan.FromMinutes(3),
        _ => TimeSpan.FromMinutes(1),
    };

    /// <summary>One exchange with Passenger, given up after a limit: one that runs out of time or fails ends the test naming
    /// it, with what Passenger said meanwhile, so a hang fails saying why rather than stopping the run in silence.</summary>
    private async Task<T> Exchange<T>(string what, TimeSpan limit, Func<CancellationToken, Task<T>> exchange, CancellationTokenSource? giveUp = null)
    {
        CancellationToken given = giveUp?.Token ?? default;
        using CancellationTokenSource timer = CancellationTokenSource.CreateLinkedTokenSource(Cancel, given);
        timer.CancelAfter(limit);
        try
        {
            return await exchange(timer.Token);
        }
        catch (Exception error) when (!Cancel.IsCancellationRequested && !given.IsCancellationRequested
            && (error is TimeoutException || (error is OperationCanceledException && timer.IsCancellationRequested)))
        {
            throw new TimeoutException($"{what} had no answer in {limit.TotalSeconds:0} s.{Said()}", error);
        }
        catch (Exception error) when (!Cancel.IsCancellationRequested && !given.IsCancellationRequested)
        {
            throw new InvalidOperationException($"{what} failed: {error.Message}{Said()}", error);
        }
    }

    /// <summary>What Passenger said before something went wrong: its standard error, and the end of its log and of the
    /// debugger engine's, which on a machine nobody can watch (CI's) are all there is to go by.</summary>
    private string Said()
    {
        string[] written = Errors();
        return $"\nPassenger's standard error:{(written.Length == 0 ? " nothing" : "\n  " + string.Join("\n  ", written.TakeLast(40)))}"
            + Tail("Passenger's log", Path.Combine(settings, "logs", "passenger.log"), 20)
            + Tail("The debugger engine's log", EngineLog, 30);

        static string Tail(string name, string path, int lines)
        {
            try
            {
                // Shared, since its writer may still have it open, which Windows otherwise refuses a reader.
                using StreamReader reader = new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                return $"\n{name} ends:\n  {string.Join("\n  ", reader.ReadToEnd().TrimEnd().Split('\n').TakeLast(lines))}";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }
    }

    private static string? Local(JsonElement stop, string name) =>
        stop.GetProperty("locals").EnumerateArray().FirstOrDefault(l => l.GetProperty("name").GetString() == name) is { ValueKind: JsonValueKind.Object } local
            ? local.GetProperty("value").GetString()
            : null;

    private void Write(string relative, string text) => File.WriteAllText(Path.Combine(folder, relative), text);
}
