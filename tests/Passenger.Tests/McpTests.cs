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

    public void Dispose() => Directory.Delete(folder, recursive: true);

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
