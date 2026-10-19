using System.Reflection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Passenger.App;

namespace Passenger.Tests;

/// <summary>An agent's view of the IDE's actions: every one the IDE declares, listed with why it cannot run now, and run by
/// its id as its shortcut would.</summary>
public sealed partial class McpTests
{
    [Fact]
    public async Task An_agent_lists_every_action_the_ide_declares_and_one_it_runs_says_why_when_it_cannot()
    {
        await using McpClient client = await Start(["--mcp", folder]);
        string[] listed = [.. (await Call(client, "ide_actions")).EnumerateArray().Select(a => a.GetString()!)];
        HashSet<string> ids = [.. listed.Select(line => line[..line.IndexOf(": ", StringComparison.Ordinal)])];
        // Every id the IDE declares, the MVP's newest among them (Hot Reload, the Live Visual Tree's, the overlays, XAML's).
        string[] declared = [.. typeof(AppActions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!)];
        string[] missing = [.. declared.Where(id => !ids.Contains(id))];
        Assert.True(missing.Length == 0, $"ide_actions leaves out {string.Join(", ", missing)}");

        // One that cannot run here says why, there and when it is run.
        string hotReload = Assert.Single(listed, l => l.StartsWith($"{AppActions.ApplyHotReloadChanges}: ", StringComparison.Ordinal));
        Assert.Contains(" - cannot run: Nothing runs", hotReload, StringComparison.Ordinal);
        CallToolResult refused = await Result(client, "ide_action", new() { ["action"] = AppActions.ApplyHotReloadChanges });
        Assert.True(refused.IsError == true && Text(refused).Contains($"{AppActions.ApplyHotReloadChanges} did not run: Nothing runs", StringComparison.Ordinal),
            $"Apply Hot Reload Changes with nothing running answered: {Text(refused)}");
        string selectElement = Assert.Single(listed, l => l.StartsWith($"{AppActions.SelectElement}: ", StringComparison.Ordinal));
        Assert.Contains("cannot run: No Avalonia or WPF app is being debugged", selectElement, StringComparison.Ordinal);

        // One that can, runs: the Live Visual Tree shown, as its action shows it.
        Assert.Equal($"Ran {AppActions.ShowLiveTree}", (await Call(client, "ide_action", new() { ["action"] = AppActions.ShowLiveTree })).GetString());
    }
}
