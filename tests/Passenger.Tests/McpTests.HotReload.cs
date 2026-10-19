using System.Text.Json;
using ModelContextProtocol.Client;

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
}
