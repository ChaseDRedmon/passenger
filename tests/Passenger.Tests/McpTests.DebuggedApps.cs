using System.Text.Json;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using Passenger.App;

namespace Passenger.Tests;

/// <summary>An agent on a window debugging two Avalonia apps at once (<see cref="DebuggedAppsTests"/>' pair): its app tools act
/// on the app of the Debug tab it names, by the name debug_state lists, else on the chosen tab's; the failed bindings it
/// reads name the program each failed in.</summary>
public sealed partial class McpTests
{
    [AvaloniaFact]
    public void An_agents_app_tools_read_the_app_of_the_debug_tab_it_names_and_the_chosen_tabs_by_default()
    {
        string apps = TestFolders.Create("apps-");
        madeFolders.Add(apps);
        Restore.Run(DebuggedAppsTests.WriteTo(apps));
        MainWindow window = AgentsWindow(apps);
        DebuggedAppsTests.Debug(window, "BindingErrorsApp", "Caption");
        DebuggedAppsTests.Debug(window, "LiveTreeApp", "Delete");
        McpClient client = Await(Task.Run(() => Start(["--mcp-attach", ProcessId])), "the agent attaching");
        try
        {
            string chosen = Text(Unasked(window, client, "app_tree"));
            Assert.True(chosen.Contains("Delete", StringComparison.Ordinal) && !chosen.Contains("Caption", StringComparison.Ordinal),
                $"The agent's app_tree naming no app was not the chosen tab's, LiveTreeApp's: {chosen}");
            string named = Text(Unasked(window, client, "app_tree", new() { ["app"] = "BindingErrorsApp" }));
            Assert.True(named.Contains("Caption", StringComparison.Ordinal) && !named.Contains("Delete", StringComparison.Ordinal),
                $"The agent's app_tree naming BindingErrorsApp was not that app's: {named}");
            JsonElement failures = Unasked(window, client, "app_binding_errors");
            Assert.True(failures.EnumerateArray().Any(f => f.GetProperty("app").GetString() == "BindingErrorsApp" && f.GetProperty("path").GetString() == "Nmae"),
                $"The first app's failed binding was not listed under its name: {failures}");
            Assert.Equal("Stopped", Unasked(window, client, "stop").GetString());
            WaitFor(() => !window.Debugger.IsDebugging, "both apps to end");
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
}
