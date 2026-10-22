using System.Text.Json;
using ModelContextProtocol.Client;

namespace Passenger.Tests;

/// <summary>An agent's publish, build's with a project to publish: the project to a folder, as Publish does, answered with
/// where it landed or why it did not.</summary>
public sealed partial class McpTests
{
    [Fact]
    public async Task An_agent_publishes_a_project_and_is_told_the_folder_it_landed_in_or_why_not()
    {
        await using McpClient client = await Start(["--mcp", folder]);
        JsonElement published = await Call(client, "build", new() { ["publish"] = "Sum" });
        Assert.True(published.GetProperty("succeeded").GetBoolean(), $"The publish ended: {published}");
        string landed = published.GetProperty("folder").GetString() ?? string.Empty;
        Assert.True(File.Exists(Path.Combine(landed, "Sum.dll")), $"The publish says it landed in {landed}, which holds no Sum.dll");
        Assert.StartsWith("Published Sum to ", published.GetProperty("outcome").GetString(), StringComparison.Ordinal);

        // A profile the project does not have publishes nothing and says so, never what MSBuild would make of no profile.
        JsonElement missing = await Call(client, "build", new() { ["publish"] = "Sum", ["profile"] = "Nightly" });
        Assert.False(missing.GetProperty("succeeded").GetBoolean(), $"A publish with no such profile ended: {missing}");
        Assert.Contains("has no publish profile Nightly", missing.GetProperty("outcome").GetString(), StringComparison.Ordinal);
    }
}
