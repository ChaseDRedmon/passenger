using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Passenger.Tests;

/// <summary>An agent on a compose file and a Dockerfile: their servers' outline and problems, Passenger's own declarations
/// in the compose file (a service's, a build's Dockerfile), an image's registry page said, and the YAML server's layout,
/// saved. Skipped, as DockerFilesTests' are, without a Node.js that runs the YAML server.</summary>
public sealed partial class McpTests
{
    [Fact]
    public async Task An_agent_reads_a_compose_files_declarations_and_problems_and_formats_it_through_the_yaml_server()
    {
        DockerFilesTests.Shop.SkipWithoutNode(yaml: true);
        const string Compose = "services:\n  web:\n    image:   nginx:1.27\n    ports: [ \"8080:80\" ]\n    depends_on:   [api]\n  api:\n    build: .\n";
        string compose = Made("mcpcompose-", made =>
        {
            File.WriteAllText(Path.Combine(made, "compose.yaml"), Compose);
            File.WriteAllText(Path.Combine(made, "Dockerfile"), "FROM alpine:3.20\nFROMM nothing\nENTRYPOINT [\"sh\"]\n");
        });
        await using McpClient client = await Start(["--mcp", compose]);

        // Passenger's declarations: the service depends_on names, and the build's Dockerfile.
        JsonElement service = Assert.Single((await Call(client, "definition", At(Compose, "compose.yaml", "[ap"))).EnumerateArray());
        Assert.Equal(("compose.yaml", 6, 3), (service.GetProperty("path").GetString(), service.GetProperty("line").GetInt32(), service.GetProperty("column").GetInt32()));
        JsonElement dockerfile = Assert.Single((await Call(client, "definition", At(Compose, "compose.yaml", "build: ."))).EnumerateArray());
        Assert.Equal(("Dockerfile", 1), (dockerfile.GetProperty("path").GetString(), dockerfile.GetProperty("line").GetInt32()));
        CallToolResult image = await Result(client, "definition", At(Compose, "compose.yaml", "image:   ngi"));
        Assert.True(image.IsError == true && Text(image).Contains("https://hub.docker.com/_/nginx", StringComparison.Ordinal), $"definition on an image answered: {Text(image)}");

        // The YAML server's outline, and the Dockerfile server's problem.
        string outline = (await Call(client, "outline", new() { ["path"] = "compose.yaml" })).GetString()!;
        Assert.True(outline.Contains("web (", StringComparison.Ordinal) && outline.Contains("depends_on (", StringComparison.Ordinal), $"The compose file's outline is not its server's:\n{outline}");
        JsonElement problem = Assert.Single((await Call(client, "diagnostics", new() { ["path"] = "Dockerfile" })).EnumerateArray());
        Assert.Contains("Unknown instruction: FROMM", problem.GetProperty("message").GetString(), StringComparison.Ordinal);

        // The YAML server's layout, saved.
        Assert.StartsWith("Reformatted", (await Call(client, "format", new() { ["path"] = "compose.yaml" })).GetString(), StringComparison.Ordinal);
        Assert.Equal("services:\n  web:\n    image: nginx:1.27\n    ports: [\"8080:80\"]\n    depends_on: [api]\n  api:\n    build: .\n", File.ReadAllText(Path.Combine(compose, "compose.yaml")));
    }
}
