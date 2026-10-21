using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Passenger.Tests;

/// <summary>An agent's profile of a program in a container (attach with container): recorded there, its hot method named at
/// its line from the PDB in the container, and the heap's types from a memory snapshot taken there. Skips, saying why,
/// where no Linux engine runs.</summary>
public sealed partial class McpTests
{
    [Fact]
    public async Task An_agent_profiles_a_program_in_a_container_its_hot_method_at_its_line_and_its_heap()
    {
        ContainerProfilingTests.Spins spins = await Task.Run(() => ContainerProfilingTests.Spins.Shared);
        Assert.SkipWhen(spins.WhyNot is not null, spins.WhyNot ?? string.Empty);
        string container = await Task.Run(() => spins.Start("agent"));
        await using McpClient client = await Start(["--mcp", spins.Folder]);

        CallToolResult unattached = await Result(client, "profile", new() { ["container"] = container, ["seconds"] = 1 });
        Assert.True(unattached.IsError == true && Text(unattached).Contains("attach", StringComparison.Ordinal), $"A container's profile without attach answered: {Text(unattached)}");
        CallToolResult nowhere = await Result(client, "profile", new() { ["attach"] = true, ["container"] = $"{container}-not", ["seconds"] = 1 });
        Assert.True(nowhere.IsError == true && Text(nowhere).Contains("debug_processes", StringComparison.Ordinal), $"A profile in no such container answered: {Text(nowhere)}");

        JsonElement profiled = await Call(client, "profile", new() { ["attach"] = true, ["container"] = container, ["seconds"] = 2, ["memory"] = true });
        Assert.True(profiled.GetProperty("samples").GetInt32() > 0, $"The profile in the container sampled nothing: {profiled.GetRawText()}");
        JsonElement spin = Assert.Single(profiled.GetProperty("hotSpots").EnumerateArray(), h => h.GetProperty("method").GetString()!.StartsWith("Hot.Spin", StringComparison.Ordinal));
        Assert.True(spin.TryGetProperty("source", out JsonElement source) && source.GetString() == $"Program.cs:{ContainerProfilingTests.Spins.SpinLine}",
            $"The hot method in the container is not named at its line: {spin}");
        Assert.Contains(profiled.GetProperty("heap").EnumerateArray(), t => t.GetProperty("type").GetString() == "Keepsake");
        Assert.Equal("running", Docker.State(container));
    }
}
