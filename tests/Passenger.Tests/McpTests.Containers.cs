using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Passenger.App;
using Passenger.App.Containers;
using Passenger.App.Mcp;

namespace Passenger.Tests;

/// <summary>An agent's container tools on a real engine: the engine's containers listed, a log read, a container stopped
/// and started as the window's buttons do it, a command run in one only once the person allows it, and what a crashed
/// Passenger left removed as the next one starts. Each skips, saying why, where no Linux engine runs.</summary>
public sealed partial class McpTests
{
    [Fact]
    public async Task An_agent_lists_the_engines_containers_reads_a_log_and_stops_and_starts_one()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        string name = Docker.Container("agent", "echo agent-hello; trap 'exit 0' TERM; while true; do sleep 1; done");
        await using McpClient client = await Start(["--mcp", folder]);

        JsonElement listed = await Call(client, "containers");
        Assert.Equal("Ready", listed.GetProperty("status").GetString());
        JsonElement mine = listed.GetProperty("containers").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);
        Assert.Equal("running", mine.GetProperty("state").GetString());
        Assert.Equal(Docker.Image, mine.GetProperty("image").GetString());

        Assert.Contains("agent-hello", Text(await Call(client, "container_logs", new() { ["container"] = name })), StringComparison.Ordinal);

        Assert.Equal($"{name}: stop done", Text(await Call(client, "container_action", new() { ["container"] = name, ["action"] = "stop" })));
        Assert.Equal("exited", Docker.State(name));
        Assert.Equal($"{name}: start done", Text(await Call(client, "container_action", new() { ["container"] = name, ["action"] = "start" })));
        Assert.Equal("running", Docker.State(name));

        CallToolResult unknown = await Result(client, "container_action", new() { ["container"] = $"{name}-not", ["action"] = "stop" });
        Assert.True(unknown.IsError == true && Text(unknown).Contains("containers lists them", StringComparison.Ordinal), $"A stop of no such container answered: {Text(unknown)}");
    }

    [AvaloniaFact]
    public void An_agents_command_in_a_container_waits_for_the_person_to_allow_it()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        string name = Docker.Container("exec", "sleep 600");
        string notes = Plain("mcp-ctr-");
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(notes);
        Ui.Settle();
        McpClient client = Await(Task.Run(() => Start(["--mcp-attach", ProcessId])), "the agent attaching");
        try
        {
            Task<JsonElement> running = Task.Run(() => Call(client, "container_exec", new() { ["container"] = name, ["command"] = "echo $((6*7)) > /tmp/answer; cat /tmp/answer" }));
            Press(Admission(window, "the window to ask whether the agent may work there"), AgentAdmissionDialog.Allow);
            ConfirmDialog asked = Question(window, "the question for the agent's command in the container");
            Assert.Contains("Allow container_exec?", Texts(asked));
            // Nothing ran before the person answered.
            Assert.False(Docker.Run("exec", name, "sh", "-c", "test -e /tmp/answer && echo there || echo absent") == "there", "The command ran before the person allowed it");
            Press(asked, "Allow");
            JsonElement ran = Await(running, "the command in the container");
            Assert.Equal(0, ran.GetProperty("exitCode").GetInt32());
            Assert.Equal("42\n", ran.GetProperty("output").GetString());
        }
        finally
        {
            Gone(client);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Containers_a_passenger_that_is_gone_left_are_removed_as_the_next_one_starts()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(Plain("mcp-left-"));
        Ui.Settle();
        // The labels a folder's containers carry, which also record that this state labelled something: a start then looks.
        string[] labels = [.. window.Session.Containers.Labels.Where(word => word != "--label")];
        ContainerLabels.Flush();
        string state = labels.Single(l => l.StartsWith(ContainerLabels.State, StringComparison.Ordinal));
        // One a crashed Passenger of this state left, and one of this living process, which stays.
        string left = Docker.Container("left", "sleep 600", state, $"{ContainerLabels.Process}={ContainerLabels.Of(int.MaxValue - 7, 1)}");
        string living = Docker.Container("living", "sleep 600", labels);
        McpClient client = Await(Task.Run(() => Start(["--mcp", folder], environment: new() { ["PASSENGER_SETTINGS"] = TestRun.Settings })), "Passenger starting");
        try
        {
            Ui.WaitFor(() => Docker.State(left) is null, $"{left}, labelled with this state and a process that is gone, removed as Passenger started",
                TimeSpan.FromSeconds(60));
            Assert.Equal("running", Docker.State(living));
        }
        finally
        {
            Gone(client);
            window.Close();
        }
    }
}
