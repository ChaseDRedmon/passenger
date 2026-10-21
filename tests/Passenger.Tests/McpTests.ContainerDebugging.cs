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
        ContainerDebuggingTests.Turns.Publish($"linux-{(Docker.Ask(["version", "--format", "{{.Server.Arch}}"]).Trim() == "arm64" ? "arm64" : "x64")}");
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
}
