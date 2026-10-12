using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Passenger.App;
using Passenger.App.Mcp;
using Passenger.Editor;

namespace Passenger.Tests;

/// <summary>Passenger as an agent's IDE, over the Model Context Protocol with the SDK's own client: an
/// agent that starts Passenger with no window (<c>--mcp</c>) and works a project through it, and one
/// that joins the window a person is using (<c>--mcp-attach</c>) and changes what they see.</summary>
[Collection(Debugging.Helpers.DebugSession.Collection)]
public sealed class McpTests : IDisposable
{
    private static readonly string Passenger = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Passenger.exe" : "Passenger");

    private readonly string folder = Directory.CreateTempSubdirectory("passenger-mcp-").FullName;
    private readonly List<string> errors = [];

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

    public void Dispose() => TestFolders.Delete(folder);

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
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        string program = Path.Combine(window.Folder!, "Program.cs");
        window.OpenFile(program);
        Ui.Settle();
        McpHost.Listen(window);
        string socket = Path.Combine(McpHost.SocketFolder, $"{Environment.ProcessId}.sock");

        // The agent's calls run on the UI thread, so the test lets it run while they are answered.
        Task<string?> agent = Task.Run(async () =>
        {
            await using McpClient client = await Start(["--mcp-attach", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
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
        Assert.False(File.Exists(socket));
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

    private Task<McpClient> Start(IList<string> arguments) => McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "passenger",
        Command = Passenger,
        Arguments = arguments,
        StandardErrorLines = line =>
        {
            lock (errors)
            {
                errors.Add(line);
            }
        },
    }), cancellationToken: Cancel);

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
