using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Client;

namespace Passenger.Tests;

/// <summary>An agent debugging in containers on a real engine: debug_start on a compose configuration debugs each .NET
/// service in a session of its own, answering with each one's state, and its container_action on a debugged service goes
/// through the run; debug_processes lists a program in a container Passenger did not start, and debug_attach attaches to
/// it there. Skipped, saying why, where no Linux engine runs.</summary>
[Collection(Alone.Name)]
public sealed class McpContainerDebuggingTests : McpRig
{
    /// <summary>debug_start of a compose configuration debugs each service; container_action's restart of a debugged one
    /// ends its session, restarts it held and attaches again, so it stops in Main again, and its stop ends the session
    /// before the container goes; then a program in a container Passenger did not start is listed and attached to.</summary>
    [Fact]
    public async Task An_agents_debug_start_debugs_each_service_of_a_compose_configuration_its_container_action_goes_through_the_run_and_debug_attach_reaches_a_program_in_a_container()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        CancellationToken test = TestContext.Current.CancellationToken;
        await Task.Run(() => ContainerPrograms.Publish($"linux-{ContainerPrograms.Processor()}"), test);
        (string shop, string project) = ContainerRunsTests.Shop("agentdebug");
        DeleteAtEnd(shop);
        // A second Compose configuration whose files run web too, so a resource no run has names two.
        File.WriteAllText(Path.Combine(shop, "Web", "compose.yaml"),
            $"name: {project}web\nservices:\n  web:\n    build: {{ context: .., dockerfile: Web/Dockerfile }}\n    ports: [\"8080\"]\n");
        await using McpClient client = await Start(["--mcp", shop], languageServer: false);
        foreach (string app in (string[])["Web", "Api"])
        {
            await Call(client, "breakpoint_set", new() { ["path"] = $"{app}/Program.cs", ["line"] = 1 });
        }
        string? attached = null;
        try
        {
            // No run has web and the chosen configuration is a project's: the two Compose configurations whose files have it
            // are named rather than one guessed, and nothing starts.
            JsonElement opened = await Call(client, "workspace");
            Assert.False(opened.GetProperty("chosenConfiguration").GetString()!.StartsWith("Compose:", StringComparison.Ordinal), $"The chosen configuration: {opened}");
            Assert.False(opened.TryGetProperty("containerRun", out _), $"workspace listed a container run before any: {opened}");
            ModelContextProtocol.Protocol.CallToolResult which = await Result(client, "run", new() { ["resource"] = "web" });
            Assert.True(which.IsError == true && Text(which).Contains("web is a service of 2 configurations", StringComparison.Ordinal)
                && Text(which).Contains("Compose: compose.yaml", StringComparison.Ordinal) && Text(which).Contains("Compose: Web/compose.yaml", StringComparison.Ordinal),
                $"run of a service two configurations have answered: {Text(which)}");
            Assert.True(Docker.NoneLeft(project), "A refused run started containers");

            JsonElement stop = await Call(client, "debug_start", new() { ["configuration"] = "Compose: compose.yaml", ["waitSeconds"] = 120 });
            Assert.True(stop.TryGetProperty("sessions", out JsonElement sessions), $"debug_start of two services answered no sessions: {stop}");
            string[] each = [.. sessions.EnumerateArray().Select(s => s.GetString()!)];
            Assert.True(each.Any(s => s.StartsWith("web:", StringComparison.Ordinal)) && each.Any(s => s.StartsWith("api:", StringComparison.Ordinal)),
                $"debug_start answered: {stop}");
            Assert.Equal("paused", stop.GetProperty("state").GetString());
            Assert.Contains("Program.cs:1:", stop.GetProperty("location").GetString(), StringComparison.Ordinal);

            // workspace lists the run beside the AppHosts, each service as a resource: the .NET ones held for the debugger, each
            // debugged in its session, and the cache as Compose runs it.
            JsonElement run = (await Call(client, "workspace")).GetProperty("containerRun");
            Assert.Equal(("Compose: compose.yaml", project, "debug"),
                (run.GetProperty("configuration").GetString(), run.GetProperty("project").GetString(), run.GetProperty("how").GetString()));
            Dictionary<string, JsonElement> services = run.GetProperty("resources").EnumerateArray().ToDictionary(r => r.GetProperty("name").GetString()!);
            foreach (string service in (string[])["web", "api"])
            {
                JsonElement it = services[service];
                Assert.True((it.GetProperty("type").GetString(), it.GetProperty("state").GetString(), it.GetProperty("held").GetString(), it.GetProperty("session").GetString(),
                    it.GetProperty("target").GetString()) == ("Project", "running", "debug", service, service)
                    && it.GetProperty("debugger").GetString() is "paused" or "running", $"workspace listed {service} as {it}");
            }
            Assert.True((services["cache"].GetProperty("type").GetString(), services["cache"].GetProperty("state").GetString(), services["cache"].TryGetProperty("held", out _))
                == ("Container", "running", false), $"workspace listed the cache as {services["cache"]}");

            // Each let go from Main's breakpoint once it has stopped there.
            foreach (string service in (string[])["web", "api"])
            {
                JsonElement paused = default;
                Assert.True(await Waiting.UntilAsync(async () => (paused = await Call(client, "debug_state", new() { ["session"] = service })).GetProperty("state").GetString() == "paused",
                    TimeSpan.FromMinutes(1), test), $"{service} did not stop in Main: {paused}");
                await Call(client, "debug_continue", new() { ["session"] = service, ["waitSeconds"] = 2 });
            }
            string before = await Task.Run(() => ContainerRunsTests.Started(project, "web"), test);

            // Restart through the run: web held again and attached again, so Main's breakpoint stops it once more.
            Assert.Equal($"{project}/web: restart done", Text(await Call(client, "container_action", new() { ["container"] = $"{project}/web", ["action"] = "restart" })));
            Assert.NotEqual(before, await Task.Run(() => ContainerRunsTests.Started(project, "web"), test));
            JsonElement again = default;
            Assert.True(await Waiting.UntilAsync(async () => (again = await Call(client, "debug_state", new() { ["session"] = "web" })).GetProperty("state").GetString() == "paused",
                TimeSpan.FromMinutes(1), test), $"web, restarted through the run, did not stop in Main again: {again}");
            Assert.Contains("Program.cs:1:", again.GetProperty("location").GetString(), StringComparison.Ordinal);

            // An agent's resource names a compose service as an Aspire resource, through the run of services: run lets web's
            // session go, its program running on, and debug_attach attaches to it again as it runs.
            JsonElement unheld = await Call(client, "run", new() { ["resource"] = "web" });
            Assert.Contains("runs without the debugger", unheld.GetProperty("status").GetString(), StringComparison.Ordinal);
            JsonElement letGo = (await Call(client, "workspace")).GetProperty("containerRun").GetProperty("resources").EnumerateArray().Single(r => r.GetProperty("name").GetString() == "web");
            Assert.False(letGo.TryGetProperty("held", out _) || letGo.TryGetProperty("session", out _), $"web, run without the debugger, is listed as {letGo}");
            JsonElement reattached = await Call(client, "debug_attach", new() { ["resource"] = "web", ["waitSeconds"] = 2 });
            Assert.True(reattached.GetProperty("state").GetString() is "running" or "paused", $"debug_attach of the compose service web answered: {reattached}");

            // Stop through the run: api's session ends before its container goes, which an adapter losing it would make an error.
            Assert.Equal($"{project}/api: stop done", Text(await Call(client, "container_action", new() { ["container"] = $"{project}/api", ["action"] = "stop" })));
            Assert.Equal("exited", ContainerRunsTests.States(project)["api"]);
            JsonElement state = await Call(client, "debug_state");
            Assert.True(!state.TryGetProperty("sessions", out JsonElement live) || !live.EnumerateArray().Any(s => s.GetString()!.StartsWith("api:", StringComparison.Ordinal)
                && !s.GetString()!.Contains("ended", StringComparison.Ordinal)), $"api is still debugged after its container stopped: {state}");
            Assert.Equal("Stopped web", Text(await Call(client, "stop", new() { ["resource"] = "web" })));
            Assert.Equal("exited", ContainerRunsTests.States(project)["web"]);

            Assert.Equal("Stopped", Text(await Call(client, "stop")));
            Assert.True(await Waiting.UntilAsync(() => Docker.NoneLeft(project), TimeSpan.FromMinutes(2),
                TestContext.Current.CancellationToken), "stop left the configuration's containers behind");

            // With no run, a resource starts the configuration that has it, now the chosen one, as the gutter's icon starts a
            // service: web with what it depends on; then debug_start of api, which that run does not start, ends it and starts
            // api's own, stopped in Main.
            JsonElement started = await Call(client, "run", new() { ["resource"] = "web" });
            string up = started.GetProperty("status").GetString()!;
            Assert.True(started.GetProperty("stillRunning").GetBoolean() && up.Contains("Running Compose: compose.yaml: web", StringComparison.Ordinal)
                && up.Contains("\n  web: running", StringComparison.Ordinal), $"run of web with no run under way answered: {started}");
            Assert.False(ContainerRunsTests.States(project).ContainsKey("api"), "run of web started api too");
            JsonElement apiStop = await Call(client, "debug_start", new() { ["resource"] = "api", ["waitSeconds"] = 120 });
            Assert.True(apiStop.GetProperty("state").GetString() == "paused" && apiStop.GetProperty("location").GetString()!.Contains("Program.cs:1:", StringComparison.Ordinal),
                $"debug_start of api with no run under way that starts it answered: {apiStop}");
            Dictionary<string, string> states = ContainerRunsTests.States(project);
            Assert.True(states.GetValueOrDefault("api") == "running" && !states.ContainsKey("web"), $"debug_start of api left these: {string.Join(", ", states)}");
            Assert.Equal("Stopped", Text(await Call(client, "stop")));
            Assert.True(await Waiting.UntilAsync(() => Docker.NoneLeft(project), TimeSpan.FromMinutes(2),
                TestContext.Current.CancellationToken), "stop left the service's run's containers behind");

            // A program in a container Passenger did not start: listed by its container, attached to there.
            string output = Path.Combine(shop, "Web", "bin", "Debug", TestProjects.Framework);
            attached = $"ptest{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}agentattach";
            Docker.Run(["run", "-d", .. Docker.AnyPids, "--name", attached, "--label", Docker.RunLabel, "-v", $"{output}:/app:ro", "mcr.microsoft.com/dotnet/aspnet:10.0",
                "dotnet", "/app/Web.dll"]);
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

    /// <summary>An agent's run never replaces the person's own containers of the project, refusing with why; a compose
    /// configuration's debug_start an agent gives up on as its held services come up, and a run it stops as it comes up,
    /// take the run down rather than leaving it up for no one; and a run up as a --mcp IDE's input ends goes with the IDE.</summary>
    [Fact]
    public async Task A_compose_start_an_agent_gives_up_on_or_stops_comes_down_and_a_run_up_goes_with_the_ides_end()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        CancellationToken test = TestContext.Current.CancellationToken;
        await Task.Run(() => ContainerPrograms.Publish($"linux-{ContainerPrograms.Processor()}"), test);
        (string shop, string project) = ContainerRunsTests.Shop("agentgone");
        DeleteAtEnd(shop);
        bool Up() => Docker.Said("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}") is { Length: > 0 };
        McpClient client = await Start(["--mcp", shop], languageServer: false);
        try
        {
            // The person's own cache, from their own compose up: an agent's run refuses rather than replace it, asking no one.
            File.WriteAllText(Path.Combine(shop, "theirs.yaml"), $"services:\n  cache:\n    labels: [\"{Docker.RunLabel}\"]\n");
            Docker.Run("compose", "-f", Path.Combine(shop, "compose.yaml"), "-f", Path.Combine(shop, "theirs.yaml"), "up", "-d", "cache");
            string theirs = ContainerRunsTests.Container(project, "cache");
            ModelContextProtocol.Protocol.CallToolResult refused = await Result(client, "run", new() { ["configuration"] = "Compose: compose.yaml" });
            Assert.True(refused.IsError == true && Text(refused).Contains($"would replace {project}-cache-1 of {project}, which Passenger did not make", StringComparison.Ordinal),
                $"run answered: {Text(refused)}");
            Assert.Equal(theirs, ContainerRunsTests.Container(project, "cache"));
            Docker.Run("rm", "-f", theirs);

            // Stopped as its services come up: up ends and nothing of it is left.
            Task<ModelContextProtocol.Protocol.CallToolResult> running = Result(client, "run", new() { ["configuration"] = "Compose: compose.yaml" });
            Assert.True(await Waiting.UntilAsync(() => Up() || running.IsCompleted, TimeSpan.FromMinutes(5), test), "run never brought a container up");
            Assert.False(running.IsCompleted, $"run answered before it could be stopped: {(running.IsCompleted ? Text(await running) : null)}");
            Assert.Equal("Stopped", Text(await Call(client, "stop")));
            ModelContextProtocol.Protocol.CallToolResult stopped = await running;
            Assert.True(stopped.IsError == true && Text(stopped).Contains("was stopped as it started", StringComparison.Ordinal), $"The run stopped as it came up answered: {Text(stopped)}");
            Assert.True(await Waiting.UntilAsync(() => !Up(), TimeSpan.FromMinutes(2), test), $"The run stopped as it came up left its containers up: "
                + $"{Docker.Run("ps", "-a", "--filter", $"label=com.docker.compose.project={project}", "--format", "{{.Names}} {{.Status}} {{.Labels}}")}\n{LogTail()}");

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
            string log = Path.Combine(OwnState, "logs", "passenger.log");
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
