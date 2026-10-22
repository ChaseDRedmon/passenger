using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Client;

namespace Passenger.Tests;

/// <summary>An agent debugging in containers on a real engine: debug_start on a compose configuration debugs each .NET
/// service in a session of its own, answering with each one's state; debug_processes lists a program in a container
/// Passenger did not start, and debug_attach attaches to it there. Skipped, saying why, where no Linux engine runs.</summary>
public sealed partial class McpTests
{
    [Fact]
    public async Task An_agents_debug_start_debugs_each_service_of_a_compose_configuration_and_debug_attach_reaches_a_program_in_a_container()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        ContainerPrograms.Publish($"linux-{(Docker.Ask(["version", "--format", "{{.Server.Arch}}"]).Trim() == "arm64" ? "arm64" : "x64")}");
        (string shop, string project) = ContainerRunsTests.Shop("agentdebug");
        madeFolders.Add(shop);
        await using McpClient client = await Start(["--mcp", shop]);
        foreach (string app in (string[])["Web", "Api"])
        {
            await Call(client, "breakpoint_set", new() { ["path"] = $"{app}/Program.cs", ["line"] = 1 });
        }
        string? attached = null;
        try
        {
            JsonElement stop = await Call(client, "debug_start", new() { ["configuration"] = "Compose: compose.yaml", ["waitSeconds"] = 120 });
            Assert.True(stop.TryGetProperty("sessions", out JsonElement sessions), $"debug_start of two services answered no sessions: {stop}");
            string[] each = [.. sessions.EnumerateArray().Select(s => s.GetString()!)];
            Assert.True(each.Any(s => s.StartsWith("web:", StringComparison.Ordinal)) && each.Any(s => s.StartsWith("api:", StringComparison.Ordinal)),
                $"debug_start answered: {stop}");
            Assert.Equal("paused", stop.GetProperty("state").GetString());
            Assert.Contains("Program.cs:1:", stop.GetProperty("location").GetString(), StringComparison.Ordinal);
            Assert.Equal("Stopped", Text(await Call(client, "stop")));
            Assert.True(await Waiting.UntilAsync(() => Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Length == 0, TimeSpan.FromMinutes(2),
                TestContext.Current.CancellationToken), "stop left the configuration's containers behind");

            // A program in a container Passenger did not start: listed by its container, attached to there.
            string output = Path.Combine(shop, "Web", "bin", "Debug", "net10.0");
            attached = $"ptest{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}agentattach";
            Docker.Run("run", "-d", "--name", attached, "--label", Docker.RunLabel, "-v", $"{output}:/app:ro", "mcr.microsoft.com/dotnet/aspnet:10.0", "dotnet", "/app/Web.dll");
            JsonElement listed = default;
            Assert.True(await Waiting.UntilAsync(async () => (listed = await Call(client, "debug_processes")).EnumerateArray()
                .Any(p => p.TryGetProperty("container", out JsonElement c) && c.GetString() == attached), TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken),
                $"debug_processes never listed the program in {attached}: {listed}");
            JsonElement at = await Call(client, "debug_attach", new() { ["program"] = "Web", ["container"] = attached, ["waitSeconds"] = 2 });
            Assert.True(at.GetProperty("state").GetString() is "running" or "paused", $"debug_attach in the container answered: {at}");
            await Call(client, "debug_detach");
            Assert.Equal("running", Docker.State(attached));
        }
        finally
        {
            if (attached is not null)
            {
                Docker.Run("rm", "-f", attached);
            }
            await Call(client, "stop");
            // The headless IDE's state is not the run's, whose end removes only its own images: these go here.
            foreach (string image in Docker.Run("images", "-q", "--filter", $"label=com.docker.compose.project={project}").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Docker.Run("rmi", "-f", image);
            }
        }
    }

    /// <summary>A compose configuration's debug_start an agent gives up on as its held services come up takes the run down
    /// rather than leaving it up for no one; and a run up as a --mcp IDE's input ends goes with the IDE.</summary>
    [Fact]
    public async Task A_compose_start_an_agent_gives_up_on_comes_down_and_a_run_up_goes_with_the_ides_end()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        CancellationToken test = TestContext.Current.CancellationToken;
        await Task.Run(() => ContainerPrograms.Publish($"linux-{ContainerPrograms.Processor()}"), test);
        (string shop, string project) = ContainerRunsTests.Shop("agentgone");
        madeFolders.Add(shop);
        bool Up() => Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Length > 0;
        McpClient client = await Start(["--mcp", shop]);
        try
        {
            using CancellationTokenSource giveUp = new();
            Task<ModelContextProtocol.Protocol.CallToolResult> starting = CallGivingUp(client, "debug_start",
                new() { ["configuration"] = "Compose: compose.yaml", ["waitSeconds"] = 120 }, giveUp.Token);
            // Given up once Compose has made its services' containers, held at their start, the debugger attaching.
            Assert.True(await Waiting.UntilAsync(() => Up() || starting.IsCompleted, TimeSpan.FromMinutes(5), test), "debug_start never brought a container up");
            Assert.False(starting.IsCompleted, "debug_start answered before it could be given up on");
            await giveUp.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
            Assert.True(await Waiting.UntilAsync(() => !Up(), TimeSpan.FromMinutes(2), test), $"The start the agent gave up on left its containers up: "
                + $"{Docker.Run("ps", "-a", "--filter", $"label=com.docker.compose.project={project}", "--format", "{{.Names}} {{.Status}} {{.Labels}}")}\n{LogTail()}");

            // A run up as the IDE's input ends goes with it.
            await Call(client, "run", new() { ["configuration"] = "Compose: compose.yaml" });
            Assert.True(Up(), "run answered with no container up");
        }
        finally
        {
            await client.DisposeAsync();
        }
        bool gone = await Waiting.UntilAsync(() => !Up(), TimeSpan.FromMinutes(2), test);
        foreach (string left in Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Docker.Run("rm", "-f", left);
        }
        // The headless IDE's state is not the run's, whose end removes only its own images: these go here.
        foreach (string image in Docker.Run("images", "-q", "--filter", $"label=com.docker.compose.project={project}").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Docker.Run("rmi", "-f", image);
        }
        Assert.True(gone, $"The --mcp IDE's end left its run's containers up: {LogTail()}");

        // The headless IDE's log, whose last lines say how its start and end went.
        string LogTail()
        {
            string log = Path.Combine(settings, "logs", "passenger.log");
            if (!File.Exists(log))
            {
                return "(no log)";
            }
            using FileStream read = new(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            string text = new StreamReader(read).ReadToEnd();
            return text[Math.Max(0, text.Length - 3000)..];
        }
    }
}
