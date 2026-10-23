using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>An agent's Aspire tools on an AppHost its Passenger runs: the stand-in AppHost (<see cref="StandInAppHost"/>), whose
/// launch profile names a stand-in resource service and, as its own address, a stand-in dashboard of the test's, so what the
/// agent gets back is held: a dashboard's answers read whole with their credentials hidden, a dashboard elsewhere never sent
/// the key, and an AppHost whose connection has ended said as such. No Aspire package, dashboard or CLI, so these run on every
/// system.</summary>
[Collection(Beside.Name)]
public sealed class McpAspireStandInTests(StandInAppHost project) : McpRig, IClassFixture<StandInAppHost>
{
    [Fact]
    public async Task An_agents_telemetry_is_the_dashboards_own_answer_with_its_credentials_hidden_and_never_another_resources()
    {
        project.Reset();
        await using AspireInteractionHost resources = await AspireInteractionHost.StartAsync(project.KeyFile);
        resources.PushResource(new Passenger.App.Aspire.Protocol.Resource { Name = "api-owned-instance", DisplayName = "api", Uid = "api", ResourceType = "Project", State = "Running" });
        await using AspireDashboardHost dashboard = await AspireDashboardHost.StartAsync();
        project.Serve(resources, dashboard.Endpoint);
        await using McpClient client = await Start(["--mcp", project.Folder], languageServer: false);
        string host = PathNames.Canonical(project.ProjectFile);
        string api = host + "::api-owned-instance";
        await Running(client, host);
        string key = File.ReadAllText(project.DashboardKeyFile);

        // A log's values read whole and its credentials hidden, whatever attribute or message carries them.
        dashboard.Answer = _ => (200, "application/json", AspireDashboardHost.Wrapped(Logs));
        foreach (Dictionary<string, object?> asked in (Dictionary<string, object?>[])[new() { ["appHost"] = host, ["limit"] = 1 }, new() { ["resource"] = api, ["limit"] = 1 }])
        {
            int before = dashboard.Requests.Length;
            JsonElement answer = await Call(client, "telemetry", asked);
            JsonElement log = Assert.Single(answer.GetProperty("logs").EnumerateArray());
            Assert.Equal("api-owned-instance", log.GetProperty("resourceName").GetString());
            Assert.Equal("42", log.GetProperty("attributes").GetProperty("code.line.number").GetString());
            string text = log.GetRawText();
            Assert.Contains("true", text, StringComparison.Ordinal);
            foreach (string secret in (string[])["private-value", "cHJpdmF0ZQ==", key])
            {
                Assert.DoesNotContain(secret, Text(answer), StringComparison.Ordinal);
            }
            AspireDashboardHost.Asked request = Assert.Single(dashboard.Requests.Skip(before));
            Assert.Equal("/api/telemetry/logs?limit=1" + (asked.ContainsKey("resource") ? "&resource=api-owned-instance" : string.Empty), request.PathAndQuery);
            Assert.Equal(key, request.Key);
        }

        // What the dashboard says of a resource it does not know is an empty snapshot; any other 404 is the agent's error,
        // never the key, and never answered from another resource's or the whole AppHost's telemetry.
        (string Kind, bool Resource, string? TraceId, string? Title, string ContentType, bool Empty)[] cases =
        [
            ("logs", true, null, "Resource not found", "application/problem+json", true),
            ("traces", true, null, "Resource not found", "application/problem+json", true),
            ("logs", true, null, "Resource not found", "application/json", true),
            ("logs", true, null, null, "application/json", true),
            ("traces", true, null, null, "application/json", true),
            ("logs", false, null, "Resource not found", "application/problem+json", false),
            ("logs", true, null, "Not Found", "application/problem+json", false),
            ("traces", true, "11111111111111111111111111111111", "Trace not found", "application/problem+json", false),
            ("logs", false, null, null, "application/json", false),
            ("traces", true, "11111111111111111111111111111111", null, "application/json", false),
        ];
        foreach ((string kind, bool resource, string? traceId, string? title, string contentType, bool empty) in cases)
        {
            string said = $"{kind}, {(resource ? "a resource" : "the AppHost")}{(traceId is null ? string.Empty : ", a trace")}, a 404 titled {title ?? "nothing"} as {contentType}";
            dashboard.Answer = _ => (404, contentType, title is null ? "{}" : JsonSerializer.Serialize(new { title, status = 404 }));
            int before = dashboard.Requests.Length;
            Dictionary<string, object?> asked = new() { ["kind"] = kind, ["limit"] = 1 };
            asked[resource ? "resource" : "appHost"] = resource ? api : host;
            if (traceId is not null)
            {
                asked["traceId"] = traceId;
            }
            CallToolResult answer = await Result(client, "telemetry", asked);
            if (empty)
            {
                Assert.True(answer.IsError != true, $"{said}: {Text(answer)}");
                using JsonDocument snapshot = JsonDocument.Parse(Text(answer));
                Assert.Equal(0, snapshot.RootElement.GetProperty(kind).GetArrayLength());
            }
            else
            {
                Assert.True(answer.IsError == true && Text(answer).Contains("HTTP 404", StringComparison.Ordinal), $"{said}: {Text(answer)}");
                Assert.DoesNotContain(key, Text(answer), StringComparison.Ordinal);
            }
            AspireDashboardHost.Asked request = Assert.Single(dashboard.Requests.Skip(before));
            string path = traceId is null ? kind : $"traces/{traceId}";
            Assert.Equal($"/api/telemetry/{path}?limit=1" + (resource ? "&resource=api-owned-instance" : string.Empty), request.PathAndQuery);
        }
        Assert.Equal("Stopped", (await Call(client, "stop")).GetString());
        Assert.Empty(resources.Errors);
    }

    [Fact]
    public async Task An_agents_telemetry_never_sends_the_key_to_a_dashboard_elsewhere_or_named_with_credentials()
    {
        await using AspireDashboardHost dashboard = await AspireDashboardHost.StartAsync();
        // An address no machine answers at (TEST-NET-1), and the stand-in dashboard's own named with a user, as a launch
        // profile could name either; each run by an IDE of its own, which reads the profile as it opens the folder.
        foreach (Uri elsewhere in (Uri[])[new("http://192.0.2.10:5000"), new($"http://agent@127.0.0.1:{dashboard.Endpoint.Port}")])
        {
            project.Reset();
            await using AspireInteractionHost resources = await AspireInteractionHost.StartAsync(project.KeyFile);
            resources.PushResource(new Passenger.App.Aspire.Protocol.Resource { Name = "api-owned-instance", DisplayName = "api", Uid = "api", ResourceType = "Project", State = "Running" });
            project.Serve(resources, elsewhere);
            await using McpClient client = await Start(["--mcp", project.Folder], languageServer: false);
            string host = PathNames.Canonical(project.ProjectFile);
            await Running(client, host);
            CallToolResult refused = await Result(client, "telemetry", new() { ["appHost"] = host });
            Assert.True(refused.IsError == true && Text(refused).Contains("loopback", StringComparison.Ordinal), $"{elsewhere}: {Text(refused)}");
            Assert.DoesNotContain(File.ReadAllText(project.DashboardKeyFile), Text(refused), StringComparison.Ordinal);
            Assert.Empty(dashboard.Requests);
            Assert.Equal("Stopped", (await Call(client, "stop")).GetString());
            Assert.Empty(resources.Errors);
        }
    }

    [Fact]
    public async Task An_agent_is_told_why_an_AppHost_whose_connection_ended_lists_no_resources_and_the_rest_of_the_workspace_answers()
    {
        project.Reset();
        await using AspireInteractionHost resources = await AspireInteractionHost.StartAsync(project.KeyFile);
        resources.PushResource(new Passenger.App.Aspire.Protocol.Resource { Name = "api-owned-instance", DisplayName = "api", Uid = "api", ResourceType = "Project", State = "Running" });
        project.Serve(resources);
        await using McpClient client = await Start(["--mcp", project.Folder], languageServer: false);
        string host = PathNames.Canonical(project.ProjectFile);
        await Running(client, host);

        // The AppHost's resource service ends its calls, as one that restarts or goes away does, while the AppHost runs on.
        resources.Drop();
        JsonElement workspace = default;
        Assert.True(await Waiting.UntilAsync(async () =>
        {
            workspace = await Call(client, "workspace");
            return workspace.GetProperty("aspireApps").EnumerateArray().Any(app => app.GetProperty("state").GetString() != "Running");
        }, TimeSpan.FromSeconds(30), Cancel), $"The agent was not told the AppHost's connection ended: {workspace}");
        JsonElement ended = Assert.Single(workspace.GetProperty("aspireApps").EnumerateArray());
        Assert.True(ended.GetProperty("state").GetString()!.Contains("failed", StringComparison.Ordinal), $"The agent was not told why: {ended}");
        Assert.Equal(0, ended.GetProperty("resources").GetArrayLength());
        Assert.Contains(workspace.GetProperty("runConfigurations").EnumerateArray(), item => item.GetString() == StandInAppHost.Configuration);
        Assert.Equal("Stopped", (await Call(client, "stop")).GetString());
        Assert.Empty(resources.Errors);
    }

    /// <summary>An agent runs an AppHost resource's advertised commands through container_action, as the Aspire window's menu
    /// does: by the command's name, or restart as Aspire's resource-restart; a confirmation asks no one (the agent's call is its
    /// own, and nobody would answer a headless IDE's question), while one that asks the person for input, one the resource
    /// lacks and one not enabled now are refused saying why, the AppHost never asked; workspace lists the commands it may run
    /// and how the AppHost's run was started.</summary>
    [Fact]
    public async Task An_agent_runs_an_AppHost_resources_commands_through_container_action_and_is_refused_those_it_cannot()
    {
        project.Reset();
        await using AspireInteractionHost resources = await AspireInteractionHost.StartAsync(project.KeyFile);
        Passenger.App.Aspire.Protocol.Resource api = new() { Name = "api-owned-instance", DisplayName = "api", Uid = "api", ResourceType = "Project", State = "Running" };
        api.Commands.Add(new Passenger.App.Aspire.Protocol.ResourceCommand
        {
            Name = "resource-restart", DisplayName = "Restart", State = Passenger.App.Aspire.Protocol.ResourceCommandState.Enabled, ConfirmationMessage = "Restart api?",
        });
        api.Commands.Add(new Passenger.App.Aspire.Protocol.ResourceCommand { Name = "passenger-ping", DisplayName = "Ping", State = Passenger.App.Aspire.Protocol.ResourceCommandState.Enabled });
        api.Commands.Add(new Passenger.App.Aspire.Protocol.ResourceCommand { Name = "resource-start", DisplayName = "Start", State = Passenger.App.Aspire.Protocol.ResourceCommandState.Disabled });
        api.Commands.Add(new Passenger.App.Aspire.Protocol.ResourceCommand
        {
            Name = "passenger-seed", DisplayName = "Seed", State = Passenger.App.Aspire.Protocol.ResourceCommandState.Enabled,
            ArgumentInputs = { new Passenger.App.Aspire.Protocol.InteractionInput { Name = "rows", Label = "Rows", InputType = Passenger.App.Aspire.Protocol.InputType.Number } },
        });
        resources.PushResource(api);
        project.Serve(resources);
        await using McpClient client = await Start(["--mcp", project.Folder], languageServer: false);
        string host = PathNames.Canonical(project.ProjectFile);
        await Running(client, host);

        JsonElement app = Assert.Single((await Call(client, "workspace")).GetProperty("aspireApps").EnumerateArray());
        Assert.Equal("run", app.GetProperty("how").GetString());
        JsonElement listed = Assert.Single(app.GetProperty("resources").EnumerateArray());
        Assert.Equal(["resource-restart", "passenger-ping", "passenger-seed"], listed.GetProperty("commands").EnumerateArray().Select(c => c.GetString()));
        string target = listed.GetProperty("target").GetString()!;

        // By the resource's display name and Aspire's restart, then by its target and the command's own name.
        Assert.Equal("api-owned-instance: resource-restart Succeeded", Text(await Call(client, "container_action", new() { ["container"] = "api", ["action"] = "restart" })));
        Assert.Equal("api-owned-instance: passenger-ping Succeeded", Text(await Call(client, "container_action", new() { ["container"] = target, ["action"] = "passenger-ping" })));
        Assert.Equal(["resource-restart", "passenger-ping"], resources.Commands.Select(command => command.CommandName));
        Assert.All(resources.Commands, command => Assert.Equal("api-owned-instance", command.ResourceName));

        foreach ((string action, string why) in ((string, string)[])[
            ("explode", "its commands are resource-restart, passenger-ping, resource-start, passenger-seed"),
            ("start", "resource-start is not enabled now"),
            ("passenger-seed", "asks for input the person gives")])
        {
            CallToolResult refused = await Result(client, "container_action", new() { ["container"] = target, ["action"] = action });
            Assert.True(refused.IsError == true && Text(refused).Contains(why, StringComparison.Ordinal), $"container_action {action} on the resource answered: {Text(refused)}");
        }
        Assert.Equal(2, resources.Commands.Length);
        Assert.Equal("Stopped", (await Call(client, "stop")).GetString());
        Assert.Empty(resources.Errors);
    }

    /// <summary>The stand-in AppHost run by the agent from its Aspire configuration, up once its Passenger lists it with its
    /// resource from the stand-in resource service.</summary>
    private async Task Running(McpClient client, string host)
    {
        Assert.True(await Waiting.UntilAsync(async () => (await Call(client, "workspace")).GetProperty("runConfigurations").EnumerateArray()
            .Any(item => item.GetString() == StandInAppHost.Configuration), TimeSpan.FromMinutes(2), Cancel),
            $"The stand-in AppHost's Aspire configuration was not found.{Said()}");
        await Call(client, "run", new() { ["configuration"] = StandInAppHost.Configuration });
        Assert.True(await Waiting.UntilAsync(async () => (await Call(client, "workspace")).GetProperty("aspireApps").EnumerateArray()
            .Any(app => PathNames.Same(app.GetProperty("appHostPath").GetString()!, host)
                && app.GetProperty("resources").EnumerateArray().Any(resource => resource.GetProperty("name").GetString() == "api-owned-instance")),
            TimeSpan.FromMinutes(2), Cancel), $"The stand-in AppHost did not come up with its resource.{Said()}");
    }

    /// <summary>One structured log of the API, its body a boolean and its attributes an integer, an authorization header and
    /// an exception's credentials.</summary>
    private const string Logs = """
        {"resourceLogs":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"api"}},{"key":"service.instance.id","value":{"stringValue":"owned-instance"}}]},
        "scopeLogs":[{"logRecords":[{"timeUnixNano":"1760000000000000000","severityText":"Information","body":{"boolValue":true},
        "attributes":[{"key":"code.line.number","value":{"intValue":"42"}},{"key":"http.request.header.authorization","value":{"stringValue":"Bearer private-value"}},
        {"key":"exception.message","value":{"stringValue":"failure: Basic cHJpdmF0ZQ=="}}]}]}]}]}
        """;
}
