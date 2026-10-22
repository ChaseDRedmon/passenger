using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Passenger.App;
using Passenger.App.Debugging;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>An agent on a window debugging two programs at once: debug_state lists both, a debugging tool that names
/// neither is refused while it cannot tell which is meant, one that names a program acts on it alone, and stop with a
/// program's name ends that one.</summary>
public sealed partial class McpTests
{
    [AvaloniaFact]
    public void An_agent_names_which_of_two_programs_debugged_at_once_it_acts_on_and_is_refused_when_it_names_none_it_can_tell()
    {
        string pair = TestFolders.Create("pair-");
        madeFolders.Add(pair);
        DebugSessionsTests.Pair.WriteTo(pair);
        Restore.Run(pair);
        string counter = Path.Combine(PathNames.Canonical(pair), "Shared", "Counter.cs");
        MainWindow window = AgentsWindow(pair);
        window.Debugger.Breakpoints.Toggle(counter, DebugSessionsTests.Pair.Next);
        DebugSession first = DebugSessionsTests.Debug(window, "First");
        DebugSession second = DebugSessionsTests.Debug(window, "Second");
        McpClient client = Await(Task.Run(() => Start(["--mcp-attach", ProcessId])), "the agent attaching");
        try
        {
            JsonElement state = Unasked(window, client, "debug_state", new() { ["session"] = "First" });
            Assert.Equal(["First: paused", "Second: paused"], state.GetProperty("sessions").EnumerateArray().Select(s => s.GetString()));
            Assert.Equal("First", state.GetProperty("session").GetString());

            // Both paused, a step that names neither cannot be told which to step.
            CallToolResult refused = Await(Task.Run(() => Result(client, "debug_step")), "the agent's unnamed step");
            string why = string.Concat(refused.Content.OfType<TextContentBlock>().Select(t => t.Text));
            Assert.True(refused.IsError == true && why.Contains("First: paused", StringComparison.Ordinal) && why.Contains("Second: paused", StringComparison.Ordinal)
                && why.Contains("session", StringComparison.Ordinal), $"An unnamed step with two programs paused was answered: {why}");

            // Named, the step is that program's alone, and its tab is the window's.
            JsonElement stepped = Unasked(window, client, "debug_step", new() { ["session"] = "First" });
            Assert.True(stepped.GetProperty("session").GetString() == "First" && stepped.GetProperty("location").GetString()!.Contains($"Counter.cs:{DebugSessionsTests.Pair.Next + 2}:", StringComparison.Ordinal),
                $"The agent's step of First was answered: {stepped}");
            Assert.Same(first, window.Debugger.Session);
            Assert.Equal(DebugState.Paused, second.State);
            JsonElement other = Unasked(window, client, "debug_state", new() { ["session"] = "Second" });
            Assert.True(other.GetProperty("location").GetString()!.Contains($"Counter.cs:{DebugSessionsTests.Pair.Next + 1}:", StringComparison.Ordinal),
                $"Stepping First moved Second, whose state is: {other}");

            // stop names the one it ends; the other, alone, is the one an unnamed tool acts on.
            Assert.Equal("Stopped Second", Unasked(window, client, "stop", new() { ["session"] = "Second" }).GetString());
            WaitFor(() => second.State == DebugState.Ended, "the second program to end");
            Assert.Equal(DebugState.Paused, first.State);
            JsonElement went = Unasked(window, client, "debug_continue");
            Assert.True(went.GetProperty("session").GetString() == "First" && went.GetProperty("location").GetString()!.Contains($"Counter.cs:{DebugSessionsTests.Pair.Next + 1}:", StringComparison.Ordinal),
                $"The agent's unnamed continue, with only First debugged, was answered: {went}");
            Assert.Equal("Stopped", Unasked(window, client, "stop").GetString());
            WaitFor(() => !window.Debugger.IsDebugging, "every program to end");
        }
        finally
        {
            Await(Task.Run(async () =>
            {
                await client.DisposeAsync();
                return true;
            }), "the agent going");
            window.Close();
        }
    }

    /// <summary>An agent lets go of the program it names, the one the debugger attached to, which runs on to its end while
    /// the other debugged stays paused; one the debugger started is refused as Detach refuses it, and with both paused one
    /// named by neither cannot be told.</summary>
    [AvaloniaFact]
    public void An_agent_detaches_the_program_it_names_and_is_refused_one_the_debugger_started()
    {
        string pair = TestFolders.Create("pair-");
        madeFolders.Add(pair);
        DebugSessionsTests.Pair.WriteTo(pair);
        Restore.Run(pair);
        string firstMain = Path.Combine(PathNames.Canonical(pair), "First", "Program.cs");
        MainWindow window = AgentsWindow(pair);
        window.Debugger.Breakpoints.Toggle(firstMain, 3);
        window.Debugger.Breakpoints.Toggle(Path.Combine(PathNames.Canonical(pair), "Second", "Program.cs"), 3);
        window.Build(global::Passenger.App.Run.BuildKind.Build);
        WaitFor(() => !window.IsBuilding, "the build");
        DebugSession second = DebugSessionsTests.Debug(window, "Second");
        // First started held for the debugger, as a container's service is, and attached to.
        string built = Path.Combine(pair, "First", "bin", "Debug", "net10.0", OperatingSystem.IsWindows() ? "First.exe" : "First");
        System.Diagnostics.ProcessStartInfo start = ChildProcesses.Command(built, []);
        start.Environment["DOTNET_DefaultDiagnosticPortSuspend"] = "1";
        start.RedirectStandardOutput = true;
        using System.Diagnostics.Process program = ChildProcesses.Start(start);
        Task<string> written = program.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Null(Await(window.Debugger.AttachAsync(program.Id, "First"), "the debugger to attach to First"));
        DebugSession first = window.Debugger.SessionNamed("First")!;
        WaitFor(() => first.State == DebugState.Paused && second.State == DebugState.Paused, "both programs to stop at their breakpoints");
        McpClient client = Await(Task.Run(() => Start(["--mcp-attach", ProcessId])), "the agent attaching");
        try
        {
            CallToolResult unnamed = Await(Task.Run(() => Result(client, "debug_detach")), "the agent's unnamed detach");
            string why = string.Concat(unnamed.Content.OfType<TextContentBlock>().Select(t => t.Text));
            Assert.True(unnamed.IsError == true && why.Contains("First: paused", StringComparison.Ordinal) && why.Contains("Second: paused", StringComparison.Ordinal),
                $"An unnamed detach with two programs paused was answered: {why}");

            CallToolResult started = Await(Task.Run(() => Result(client, "debug_detach", new() { ["session"] = "Second" })), "the agent's detach of Second");
            string refused = string.Concat(started.Content.OfType<TextContentBlock>().Select(t => t.Text));
            Assert.True(started.IsError == true && refused.Contains("The debugger started this program", StringComparison.Ordinal),
                $"The detach of a program the debugger started was answered: {refused}");

            string detached = Unasked(window, client, "debug_detach", new() { ["session"] = "First" }).GetString()!;
            Assert.True(detached.StartsWith("Detached from First, which runs on", StringComparison.Ordinal), $"The detach of First was answered: {detached}");
            Assert.True(program.WaitForExit(TimeSpan.FromSeconds(30).Scaled()), "First, let go, did not run on to its end");
            Assert.Equal("first 3", written.Result.Trim());
            Assert.Equal(DebugState.Paused, second.State);
            Assert.Equal("Stopped", Unasked(window, client, "stop").GetString());
            WaitFor(() => !window.Debugger.IsDebugging, "every program to end");
        }
        finally
        {
            Await(Task.Run(async () =>
            {
                await client.DisposeAsync();
                return true;
            }), "the agent going");
            window.Close();
            if (!program.HasExited)
            {
                Processes.Kill(program.Id);
            }
        }
    }
}
