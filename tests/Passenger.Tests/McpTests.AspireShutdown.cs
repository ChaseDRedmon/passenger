using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using ModelContextProtocol.Client;

namespace Passenger.Tests;

public sealed partial class McpTests
{
    [Fact(Skip = AspireApp.MissingCliReason, SkipType = typeof(AspireApp), SkipWhen = nameof(AspireApp.IsUnavailable))]
    public async Task Ending_a_windowless_Passengers_input_stops_the_official_starter_tree_and_Dcp_listener()
    {
        string starter = TestFolders.Create("mcp-aspire-shutdown-");
        madeFolders.Add(starter);
        string hostPid = Path.Combine(starter, "apphost.pid");
        string apiPid = Path.Combine(starter, "api.pid");
        string webPid = Path.Combine(starter, "web.pid");
        string endpointFile = Path.Combine(starter, "execution-endpoint.txt");
        TestFolders.Prepare(() =>
        {
            AspireApp.Create(starter, AspireApp.Name);
            RecordPid("AppHost", "AppHost.cs", hostPid);
            RecordPid("ApiService", "Program.cs", apiPid);
            RecordPid("Web", "Program.cs", webPid);
            string host = Path.Combine(starter, AspireApp.Name + ".AppHost", "AppHost.cs");
            string text = File.ReadAllText(host);
            File.WriteAllText(host, $"File.WriteAllText({JsonSerializer.Serialize(endpointFile)}, Environment.GetEnvironmentVariable(\"DEBUG_SESSION_PORT\") ?? string.Empty);\n" + text);
            Restore.Run(starter);
        }, starter);

        await using McpClient client = await Start(["--mcp", starter], environment: new()
        {
            ["DEBUG_SESSION_SERVER_CERTIFICATE"] = "not-a-certificate",
        });
        string configuration = $"Aspire: {AspireApp.Name}.AppHost: http";
        Assert.True(await Waiting.UntilAsync(async () =>
            (await Call(client, "workspace")).GetProperty("runConfigurations").EnumerateArray().Any(item => item.GetString() == configuration),
            TimeSpan.FromMinutes(2), Cancel), $"The official starter's HTTP configuration was not discovered.{Said()}");
        await Call(client, "run", new() { ["configuration"] = configuration, ["resource"] = "apiservice" });
        JsonElement app = default;
        Assert.True(await Waiting.UntilAsync(async () =>
        {
            app = Assert.Single((await Call(client, "workspace")).GetProperty("aspireApps").EnumerateArray());
            return new[] { "apiservice", "webfrontend" }.All(name => app.GetProperty("resources").EnumerateArray().Count(resource =>
                resource.GetProperty("displayName").GetString() == name && resource.GetProperty("state").GetString() == "Running") == 1);
        }, TimeSpan.FromMinutes(1), Cancel), $"The official starter services did not reach Running.{Said()}");
        int hostId = await Processes.WrittenIdAsync(hostPid, "the real AppHost PID");
        int apiId = await Processes.WrittenIdAsync(apiPid, "the real API PID");
        int webId = await Processes.WrittenIdAsync(webPid, "the real frontend PID");
        Assert.Equal(hostId, app.GetProperty("processId").GetInt32());
        Assert.All(new[] { hostId, apiId, webId }, id => Assert.True(Processes.IsRunning(id), $"The official starter process {id} never remained alive"));
        string hostProject = Path.Combine(starter, AspireApp.Name + ".AppHost", AspireApp.Name + ".AppHost.csproj");
        JsonElement api = Assert.Single(app.GetProperty("resources").EnumerateArray(), resource => resource.GetProperty("displayName").GetString() == "apiservice"
            && resource.GetProperty("state").GetString() == "Running");
        string target = api.GetProperty("target").GetString()!;
        Assert.Equal(hostProject + "::" + api.GetProperty("name").GetString(), target);
        Assert.Contains(app.GetProperty("resources").EnumerateArray(), resource => resource.GetProperty("displayName").GetString() == "webfrontend"
            && resource.GetProperty("state").GetString() == "Running");

        foreach (string missing in new[] { "not-a-resource", hostProject + "::not-a-resource" })
        {
            ModelContextProtocol.Protocol.CallToolResult refused = await Result(client, "stop", new() { ["resource"] = missing });
            Assert.True(refused.IsError == true && Text(refused).Contains("No owned Aspire resource", StringComparison.Ordinal));
        }
        JsonElement attached = await Call(client, "debug_attach", new() { ["resource"] = target, ["waitSeconds"] = 1 });
        Assert.True(attached.GetProperty("state").GetString() is "running" or "paused", attached.ToString());
        Assert.Equal(apiId, await Processes.WrittenIdAsync(apiPid, "the attached API PID"));
        Assert.True(Processes.IsRunning(hostId) && Processes.IsRunning(webId));

        await Call(client, "run", new() { ["resource"] = target });
        await Until(() => !Processes.IsRunning(apiId) && ReadPid(apiPid) != apiId, "Run Without Debugger to restart only the API");
        apiId = await Processes.WrittenIdAsync(apiPid, "the run API PID");
        Assert.True(Processes.IsRunning(hostId) && Processes.IsRunning(webId));
        JsonElement restarted = await Call(client, "debug_start", new() { ["resource"] = target, ["waitSeconds"] = 1 });
        Assert.True(restarted.GetProperty("state").GetString() is "running" or "paused", restarted.ToString());
        await Until(() => !Processes.IsRunning(apiId) && ReadPid(apiPid) != apiId, "Debug to restart only the API");
        apiId = await Processes.WrittenIdAsync(apiPid, "the debug API PID");
        Assert.True(Processes.IsRunning(hostId) && Processes.IsRunning(webId));

        JsonElement profile = await Call(client, "profile", new() { ["resource"] = target, ["seconds"] = 1 });
        Assert.True(profile.GetProperty("durationMs").GetDouble() > 0, profile.ToString());
        Assert.Equal(apiId, await Processes.WrittenIdAsync(apiPid, "the profiled API PID"));
        Assert.True(Processes.IsRunning(hostId) && Processes.IsRunning(webId));

        api = Assert.Single(Assert.Single((await Call(client, "workspace")).GetProperty("aspireApps").EnumerateArray())
            .GetProperty("resources").EnumerateArray(), resource => resource.GetProperty("displayName").GetString() == "apiservice");
        Uri apiUrl = new(Assert.Single(api.GetProperty("urls").EnumerateArray(), url => url.GetProperty("url").GetString()!.StartsWith("http:", StringComparison.Ordinal))
            .GetProperty("url").GetString()!);
        using (HttpClient http = new())
        {
            await http.GetStringAsync(new Uri(apiUrl, "/weatherforecast"), Cancel);
        }
        Assert.True(await Waiting.UntilAsync(async () =>
        {
            JsonElement logs = await Call(client, "aspire_telemetry", new() { ["resource"] = target, ["limit"] = 1 });
            Assert.InRange(logs.GetProperty("logs").GetArrayLength(), 0, 1);
            return logs.GetProperty("logs").GetArrayLength() == 1;
        }, TimeSpan.FromMinutes(1), Cancel), "Native HTTP telemetry did not return a bounded log snapshot");
        JsonElement traces = await Call(client, "aspire_telemetry", new() { ["resource"] = target, ["kind"] = "traces", ["limit"] = 1 });
        Assert.InRange(traces.GetProperty("traces").GetArrayLength(), 0, 1);

        await Call(client, "stop", new() { ["resource"] = target });
        await Until(() => !Processes.IsRunning(apiId), "resource Stop to end only the API");
        Assert.True(Processes.IsRunning(hostId) && Processes.IsRunning(webId));
        await Call(client, "run", new() { ["resource"] = target });
        await Until(() => ReadPid(apiPid) != apiId, "the API to restart before testing EOF cleanup");
        apiId = await Processes.WrittenIdAsync(apiPid, "the restarted API PID");
        await Until(() => File.Exists(endpointFile) && new FileInfo(endpointFile).Length > 0, "the owned DCP endpoint written by the real AppHost");
        string address = File.ReadAllText(endpointFile).Trim();
        Assert.StartsWith("localhost:", address, StringComparison.Ordinal);
        int port = int.Parse(address["localhost:".Length..], CultureInfo.InvariantCulture);
        using (TcpClient listener = new())
        {
            await listener.ConnectAsync(IPAddress.Loopback, port, Cancel);
        }
        Process passenger;
        lock (passengers)
        {
            passenger = Assert.Single(passengers);
        }
        int[] owned = [.. Processes.Descendants(passenger.Id), hostId, apiId, webId];
        Assert.Contains(hostId, Processes.Descendants(passenger.Id));
        EndInput(passenger);
        await Until(() => passenger.HasExited && owned.All(id => !Processes.IsRunning(id)),
            "EOF to end windowless Passenger, both services, AppHost, DCP, dashboard and the remaining owned child tree without a dispatcher pump");
        using TcpClient closed = new();
        await Assert.ThrowsAsync<SocketException>(async () => await closed.ConnectAsync(IPAddress.Loopback, port, Cancel));

        void RecordPid(string project, string source, string marker)
        {
            string path = Path.Combine(starter, AspireApp.Name + "." + project, source);
            string text = File.ReadAllText(path);
            string statement = $"File.WriteAllText({JsonSerializer.Serialize(marker)}, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));\n";
            if (text.StartsWith("using ", StringComparison.Ordinal))
            {
                int builder = text.IndexOf("var builder =", StringComparison.Ordinal);
                Assert.True(builder >= 0, $"The official {project} source has no application builder");
                text = text.Insert(builder, statement);
            }
            else
            {
                text = statement + text;
            }
            File.WriteAllText(path, text);
        }

        static int ReadPid(string path) => File.Exists(path) && int.TryParse(File.ReadAllText(path), CultureInfo.InvariantCulture, out int id) ? id : 0;
    }
}
