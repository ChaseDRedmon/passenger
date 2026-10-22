using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Passenger.App;
using Passenger.App.Run;
using Passenger.Core;

namespace Passenger.Tests;

public sealed partial class McpTests
{
    [Fact]
    public async Task Aspire_tools_are_discoverable_and_refuse_to_control_an_unowned_app()
    {
        await using McpClient client = await Start(["--mcp", folder]);
        IList<McpClientTool> tools = await Exchange("Listing Aspire tools", TimeSpan.FromMinutes(1),
            cancel => client.ListToolsAsync(cancellationToken: cancel).AsTask());
        foreach (string name in new[] { "run", "debug_start", "debug_attach", "profile", "stop", "aspire_telemetry" })
        {
            McpClientTool tool = Assert.Single(tools, tool => tool.Name == name);
            Assert.True(tool.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("resource", out _));
        }
        Assert.Equal("aspire_telemetry", Assert.Single(tools, tool => tool.Name.StartsWith("aspire_", StringComparison.Ordinal)).Name);
        int listed = tools.Sum(tool => JsonSerializer.Serialize(tool.ProtocolTool, ModelContextProtocol.McpJsonUtilities.DefaultOptions).Length);
        Assert.True(listed < 60_000, $"The tools an agent reads as each session starts take {listed} bytes");

        foreach (string name in new[] { "stop", "debug_attach", "aspire_telemetry" })
        {
            CallToolResult unowned = await Result(client, name,
                new() { ["resource"] = Path.Combine(folder, "Sum.csproj") + "::missing" });
            Assert.True(unowned.IsError == true && Text(unowned).Contains("No owned Aspire resource", StringComparison.Ordinal),
                $"{name} accepted an unowned resource: {Text(unowned)}");
        }
        CallToolResult ordinary = await Result(client, "run", new() { ["resource"] = "missing" });
        Assert.True(ordinary.IsError == true, $"An ordinary configuration accepted an Aspire target: {Text(ordinary)}");
        Assert.Equal("Stopped", (await Call(client, "stop")).GetString());
        JsonElement workspace = await Call(client, "workspace");
        Assert.Equal(PathNames.Canonical(folder), workspace.GetProperty("folder").GetString());
        Assert.Empty(workspace.GetProperty("aspireApps").EnumerateArray());
    }

    [Fact(Skip = AspireApp.MissingCliReason, SkipType = typeof(AspireApp), SkipWhen = nameof(AspireApp.IsUnavailable))]
    public async Task An_agent_must_qualify_duplicate_official_starter_resource_names_with_the_owned_AppHost()
    {
        using AspireApp project = new();
        await using McpClient client = await Start(["--mcp", project.Folder]);
        string first = $"Aspire: {AspireApp.Name}.AppHost: http";
        string second = $"Aspire: {AspireApp.CompanionName}.AppHost: http";
        Assert.True(await Waiting.UntilAsync(async () =>
        {
            string?[] names = (await Call(client, "workspace")).GetProperty("runConfigurations").EnumerateArray().Select(item => item.GetString()).ToArray();
            return names.Contains(first) && names.Contains(second);
        }, TimeSpan.FromMinutes(2), Cancel), $"Both official AppHosts were not discovered.{Said()}");
        await Call(client, "run", new() { ["configuration"] = first, ["resource"] = project.Host + "::apiservice" });
        await Call(client, "run", new() { ["configuration"] = second, ["resource"] = project.CompanionHost + "::apiservice" });
        JsonElement[] apps = [];
        Assert.True(await Waiting.UntilAsync(async () =>
        {
            apps = (await Call(client, "workspace")).GetProperty("aspireApps").EnumerateArray().ToArray();
            return apps.Length == 2 && apps.All(app => app.GetProperty("resources").EnumerateArray().Count(resource =>
                resource.GetProperty("displayName").GetString() == "apiservice" && resource.GetProperty("state").GetString() == "Running") == 1);
        }, TimeSpan.FromMinutes(1), Cancel), $"Both owned API resources did not reach Running.{Said()}");
        Assert.Equal(2, apps.Length);
        JsonElement[] resources = apps.Select(app => Assert.Single(app.GetProperty("resources").EnumerateArray(), resource => resource.GetProperty("displayName").GetString() == "apiservice"
            && resource.GetProperty("state").GetString() == "Running")).ToArray();
        Assert.Equal(resources[0].GetProperty("displayName").GetString(), resources[1].GetProperty("displayName").GetString());
        Assert.NotEqual(resources[0].GetProperty("target").GetString(), resources[1].GetProperty("target").GetString());
        foreach (string tool in new[] { "stop", "debug_attach", "aspire_telemetry" })
        {
            CallToolResult refused = await Result(client, tool, new() { ["resource"] = "apiservice" });
            Assert.True(refused.IsError == true && Text(refused).Contains("ambiguous", StringComparison.Ordinal),
                $"{tool} accepted an ambiguous target: {Text(refused)}");
        }
        foreach (JsonElement app in apps)
        {
            JsonElement resource = Assert.Single(app.GetProperty("resources").EnumerateArray(), item => item.GetProperty("displayName").GetString() == "apiservice"
                && item.GetProperty("state").GetString() == "Running");
            string target = resource.GetProperty("target").GetString()!;
            Assert.Equal(app.GetProperty("appHostPath").GetString() + "::" + resource.GetProperty("name").GetString(), target);
            if (resource.GetProperty("name").GetString() != "apiservice")
            {
                CallToolResult refused = await Result(client, "aspire_telemetry", new() { ["resource"] = app.GetProperty("appHostPath").GetString() + "::apiservice" });
                Assert.True(refused.IsError == true, "A running qualified target accepted the cold declaration alias");
            }
            JsonElement logs = await Call(client, "aspire_telemetry", new() { ["resource"] = target, ["limit"] = 1 });
            Assert.InRange(logs.GetProperty("logs").GetArrayLength(), 0, 1);
        }
    }

    [AvaloniaFact]
    public void Canceling_an_agents_Aspire_start_ends_the_starting_process_and_its_execution_listener()
    {
        Write("Sum.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        string pidFile = Path.Combine(folder, "aspire-starting.pid");
        string endpointFile = Path.Combine(folder, "aspire-starting.endpoint");
        Write("Program.cs", $"File.WriteAllText({JsonSerializer.Serialize(endpointFile)}, Environment.GetEnvironmentVariable(\"DEBUG_SESSION_PORT\"));\nFile.WriteAllText({JsonSerializer.Serialize(pidFile)}, Environment.ProcessId.ToString());\nConsole.WriteLine(\"starting\");\nawait Task.Delay(Timeout.Infinite);\n");
        MainWindow window = AgentsWindow(folder);
        window.SetConfigurations([new RunConfiguration
        {
            Name = "Pending Aspire",
            Project = Path.Combine(folder, "Sum.csproj"),
            Aspire = true,
        }], "Pending Aspire");
        McpClient client = Await(Task.Run(() => Start(["--mcp-attach", ProcessId])), "the Aspire agent attaching");
        Process? started = null;
        try
        {
            Ui.WaitFor(() => window.Configurations.Any(configuration => configuration.Aspire), "the explicit Aspire run configuration");
            string configuration = window.Configurations.First(candidate => candidate.Aspire).Name;
            using CancellationTokenSource giveUp = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
            Task<CallToolResult> starting = Task.Run(() => CallGivingUp(client, "run",
                new() { ["configuration"] = configuration }, giveUp.Token));
            Window permission = Question(window, "permission for the Aspire startup");
            Button allow = permission.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Allow"));
            Point at = allow.TranslatePoint(new Point(allow.Bounds.Width / 2, allow.Bounds.Height / 2), permission)!.Value;
            permission.MouseDown(at, MouseButton.Left);
            permission.MouseUp(at, MouseButton.Left);
            Ui.PumpUntil(() => File.Exists(pidFile) || starting.IsCompleted, "the starting AppHost process", TimeSpan.FromMinutes(5));
            Assert.True(File.Exists(pidFile), $"The AppHost process did not start: {(starting.IsCompletedSuccessfully ? Text(starting.Result) : window.Status)}{Said()}");
            int processId = Processes.WrittenId(pidFile, "the starting AppHost PID");
            started = Process.GetProcessById(processId);
            ProcessRunner runner = Assert.Single(window.AspireRuntime.Apps).Runner;
            Ui.PumpUntil(() => runner.Shown().ToString().Contains("starting", StringComparison.Ordinal), "the AppHost's startup console marker");
            int port = new Uri("http://" + File.ReadAllText(endpointFile)).Port;
            Task<string?> reading = Task.Run(async () => (await Call(client, "run_output")).GetString());
            Ui.PumpUntil(() => reading.IsCompleted, "reading the starting AppHost's console");
            Assert.Contains("starting", reading.GetAwaiter().GetResult(), StringComparison.Ordinal);

            giveUp.Cancel();
            Ui.PumpUntil(() => starting.IsCompleted && window.AspireRuntime.Apps.Count == 0 && started.HasExited,
                "canceling the start to roll back the AppHost and listener", TimeSpan.FromMinutes(1));
            Assert.ThrowsAny<OperationCanceledException>(() => starting.GetAwaiter().GetResult());
            Assert.False(Await(Task.Run(async () =>
            {
                using TcpClient socket = new();
                try
                {
                    await socket.ConnectAsync(IPAddress.Loopback, port, Cancel);
                    return true;
                }
                catch (SocketException)
                {
                    return false;
                }
            }), "checking the canceled execution listener"), "The canceled Aspire startup left its DCP listener running");
            Assert.False(window.IsRunning, "The canceled Aspire startup remained a running IDE configuration");
        }
        finally
        {
            Gone(client);
            window.Close();
            started?.Dispose();
        }
    }
}
