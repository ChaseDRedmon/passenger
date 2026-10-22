using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using Passenger.App;

namespace Passenger.Tests;

/// <summary>An agent's Hot Reload, as Apply Hot Reload Changes is a person's: the edits it made in the editor applied to the
/// program it debugs, and an edit the runtime cannot take named at its line, the program restarted for it only when asked.</summary>
public sealed partial class McpTests
{
    [Fact]
    public async Task An_agent_applies_hot_reload_changes_to_the_program_it_debugs_and_is_told_each_rude_edit_at_its_line_and_whether_it_restarted()
    {
        File.WriteAllText(Path.Combine(folder, "Helper.cs"),
            "static class Helper\n{\n    static int calls;\n\n    public static int Twice(int value)\n    {\n        calls++;\n        return value * 2;\n    }\n}\n");
        await using McpClient client = await Start(["--mcp", folder]);
        await Call(client, "breakpoint_set", new() { ["path"] = "Program.cs", ["line"] = 4 });
        Assert.Equal("paused", (await Call(client, "debug_start")).GetProperty("state").GetString());

        // An edit to a method's body, made while the program is paused, is applied at once and runs from the next call.
        await Call(client, "open_file", new() { ["path"] = "Helper.cs", ["line"] = 8, ["column"] = 25 });
        await Call(client, "type", new() { ["text"] = " + 1" });
        JsonElement applied = await Call(client, "hot_reload");
        Assert.True(applied.GetProperty("result").GetString() == "applied" && applied.GetProperty("methods").GetInt32() == 1
            && applied.GetProperty("files").EnumerateArray().Select(f => f.GetString()).SequenceEqual(["Helper.cs"]) && !applied.GetProperty("restartNeeded").GetBoolean(),
            $"The edit to Twice's body was not applied: {applied}");
        Assert.Equal("3", Local(await Call(client, "debug_continue"), "total"));

        // A field's type the runtime cannot change: named at its line, nothing applied, and with restart false the program
        // goes on in the code it has, still paused where it was.
        await Call(client, "open_file", new() { ["path"] = "Helper.cs", ["line"] = 3, ["column"] = 12 });
        await Call(client, "type", new() { ["text"] = "u" });
        JsonElement rude = await Call(client, "hot_reload", new() { ["restart"] = false });
        Assert.True(rude.GetProperty("result").GetString() == "restartNeeded" && rude.GetProperty("restartNeeded").GetBoolean() && !rude.GetProperty("restarted").GetBoolean(),
            $"Changing a field's type did not say a restart is needed, or restarted unasked: {rude}");
        JsonElement edit = Assert.Single(rude.GetProperty("edits").EnumerateArray(), e => e.GetProperty("severity").GetString() == "error");
        Assert.True(edit.GetProperty("path").GetString() == "Helper.cs" && edit.GetProperty("line").GetInt32() == 3
            && edit.GetProperty("code").GetString()!.StartsWith("ENC", StringComparison.Ordinal), $"The rude edit is not named at its line: {edit}");
        JsonElement still = await Call(client, "debug_state");
        Assert.True(still.GetProperty("state").GetString() == "paused" && Local(still, "total") == "3", $"The program did not stay where it was: {still}");

        // Asked to, it restarts the program with the edits built, which stops at the breakpoint again and runs them.
        JsonElement restarted = await Call(client, "hot_reload", new() { ["restart"] = true });
        Assert.True(restarted.GetProperty("restarted").GetBoolean() && restarted.GetProperty("debugger").GetProperty("state").GetString() == "paused"
            && restarted.GetProperty("debugger").GetProperty("location").GetString()!.StartsWith("Program.cs:4:", StringComparison.Ordinal),
            $"The restart for the rude edit did not stop at the breakpoint again: {restarted}");
        Assert.Equal("3", Local(await Call(client, "debug_continue"), "total"));
        await Call(client, "stop");
    }

    /// <summary>An agent's hot_reload on the person's window is the window's own Apply Hot Reload Changes: it shows as that
    /// action's work while it applies, and its outcome is the window's last apply, where the tool decided again for itself
    /// what to apply and restarted in its own order, unseen.</summary>
    [AvaloniaFact]
    public void An_agents_hot_reload_on_the_persons_window_is_its_apply_hot_reload_changes_shown_as_its_work()
    {
        File.WriteAllText(Path.Combine(folder, "Helper.cs"),
            "static class Helper\n{\n    public static int Twice(int value)\n    {\n        return value * 2;\n    }\n}\n");
        MainWindow window = AgentsWindow(folder);
        Ui.WaitFor(() => window.CSharpService is { IsReady: true }, $"the language server to load the project ({window.CSharpService?.State})", TimeSpan.FromMinutes(3));
        List<string> worked = [];
        window.Actions.Work.Changed += (_, change) =>
        {
            if (change.Work.Id == AppActions.ApplyHotReloadChanges && !change.Ended)
            {
                worked.Add(change.Work.Stage);
            }
        };
        McpClient? client = null;
        try
        {
            window.OpenFile(Path.Combine(folder, "Program.cs"));
            window.EditorArea.CodeEditor.GoTo(new global::Passenger.Text.TextPosition(3, 4));
            Ui.Run(window, AppActions.ToggleBreakpoint);
            Ui.Run(window, AppActions.Debug);
            Ui.WaitFor(() => window.Debugger.Session?.State == global::Passenger.App.Debugging.DebugState.Paused && window.Debugger.Stop is not null,
                $"the program to stop at its breakpoint{Ui.BuildErrors(window)}");
            window.OpenFile(Path.Combine(folder, "Helper.cs"));
            Typing.Replace(window, "value * 2", "value * 2 + 1");

            client = Await(Task.Run(() => Start(["--mcp-attach", ProcessId])), "the agent attaching");
            Task<JsonElement> applying = Task.Run(() => Call(client, "hot_reload"));
            Press(Question(window, "the question for the agent's hot_reload"), "Allow");
            JsonElement applied = Await(applying, "the agent's hot_reload");
            Assert.True(applied.GetProperty("result").GetString() == "applied" && applied.GetProperty("methods").GetInt32() == 1, $"The agent's edit was not applied: {applied}");
            Assert.True(worked.Contains("Applying Hot Reload changes…"), $"The agent's apply did not show as Apply Hot Reload Changes' work; it showed: {string.Join(" | ", worked)}");
            Assert.Equal(global::Passenger.App.Debugging.HotReloadResult.Applied, window.Debugger.LastApplied?.Result);
        }
        finally
        {
            if (window.Debugger.IsDebugging)
            {
                window.Perform(AppActions.StopAll);
                Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
            }
            if (client is not null)
            {
                Await(Task.Run(async () =>
                {
                    await client.DisposeAsync();
                    return true;
                }), "the agent going");
            }
            window.Close();
        }
    }

    /// <summary>Run with Hot Reload (Settings' Hot Reload, or run's hotReload, since the headless IDE keeps the default settings)
    /// runs the program under dotnet watch, which takes saved edits: hot_reload saves them and answers with what dotnet watch
    /// said of them, and run_output shows the program running them.</summary>
    [Fact]
    public async Task An_agent_applies_hot_reload_changes_to_what_run_runs_under_dotnet_watch_and_is_told_what_dotnet_watch_made_of_them()
    {
        File.WriteAllText(Path.Combine(folder, "Program.cs"),
            "while (true)\n{\n    Console.WriteLine(Talk.Say());\n    Thread.Sleep(200);\n}\n\nstatic class Talk\n{\n    public static string Say() => \"before the edit\";\n}\n");
        await using McpClient client = await Start(["--mcp", folder]);
        // Never built, as on CI, where run comes first: the output folder the design-time build made goes, for dotnet watch's to make.
        Assert.Contains((await Call(client, "find_symbol", new() { ["query"] = "Talk" })).EnumerateArray(), s => s.GetProperty("path").GetString() == "Program.cs");
        TestFolders.Delete(Path.Combine(folder, "bin"));
        Assert.True((await Call(client, "run", new() { ["waitSeconds"] = 0, ["hotReload"] = true })).GetProperty("stillRunning").GetBoolean());
        // dotnet watch watches once it has loaded the project, which is after the program has started.
        string output = string.Empty;
        await Waiting.UntilAsync(async () =>
        {
            output = (await Call(client, "run_output", new() { ["lines"] = 100000 })).GetString()!;
            return output.Contains("before the edit", StringComparison.Ordinal) && output.Contains("Waiting for changes", StringComparison.Ordinal);
        }, TimeSpan.FromMinutes(2), Cancel, TimeSpan.FromMilliseconds(500));
        Assert.True(output.Contains("Waiting for changes", StringComparison.Ordinal), $"dotnet watch never watched the program:\n{output[..Math.Min(output.Length, 3000)]}");

        await Call(client, "open_file", new() { ["path"] = "Program.cs", ["line"] = 9, ["column"] = 51 });
        await Call(client, "type", new() { ["text"] = ", edited" });
        JsonElement applied = await Call(client, "hot_reload");
        Assert.True(applied.GetProperty("result").GetString() == "applied" && applied.GetProperty("said").GetString()!.Contains("dotnet watch applied the changes", StringComparison.Ordinal),
            $"dotnet watch did not apply the saved edit: {applied}\n{(await Call(client, "run_output")).GetString()}");
        await Waiting.UntilAsync(async () =>
        {
            output = (await Call(client, "run_output")).GetString()!;
            return output.Contains("before the edit, edited", StringComparison.Ordinal);
        }, TimeSpan.FromSeconds(30), Cancel, TimeSpan.FromMilliseconds(500));
        Assert.True(output.Contains("before the edit, edited", StringComparison.Ordinal), $"The running program never ran the edit:\n{output[Math.Max(0, output.Length - 3000)..]}");
        await Call(client, "stop");
    }
}
