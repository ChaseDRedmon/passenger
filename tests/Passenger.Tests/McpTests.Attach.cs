using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Passenger.App;
using Passenger.App.Mcp;
using Passenger.App.Settings;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>An agent set up with <c>--mcp-attach</c> before any Passenger runs, as the repository's .mcp.json and the
/// site's steps set one up: its session has the tools from the start, its first call has Passenger open its folder (the
/// run's headless build started on it, or a window of the running Passenger's), the window asks the person before the
/// agent works there, and a window closed is found again at the next call. A window on another folder is never joined,
/// and on a person's window an agent's open of another folder is theirs to allow.</summary>
public sealed partial class McpTests
{
    [Fact]
    public async Task An_agent_set_up_before_Passenger_runs_has_the_tools_and_its_first_call_starts_Passenger_on_its_folder_which_asks()
    {
        // A state of the agent's own, where no window of this run's listens; the run starts Passenger headless (TestRun).
        string state = TestFolders.Create("a");
        madeFolders.Add(state);
        string notes = Plain("mcp-start-");
        await using McpClient client = await Start(["--mcp-attach"], notes, new() { ["PASSENGER_SETTINGS"] = state });

        // Listed at once, with nothing running: the build's own tools, every one PassengerTools has.
        IList<McpClientTool> tools = await Exchange("Listing the tools", TimeSpan.FromMinutes(1), cancel => client.ListToolsAsync(cancellationToken: cancel).AsTask());
        string[] built = [.. typeof(PassengerTools).GetMethods().Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name).OfType<string>().Order(StringComparer.Ordinal)];
        Assert.Equal(built, tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        // Tools alone, as a window serves: no resources or prompts to ask for.
        Assert.True(client.ServerCapabilities.Resources is null && client.ServerCapabilities.Prompts is null, "The agent's session offers resources or prompts no window has");
        // The instructions say how to begin, that a first call may start Passenger where the person sees it, and what it asks.
        foreach (string said in (string[])["Begin with workspace", "refactor with describe true", "in a window the person sees", "the window asks them to let the agent in"])
        {
            Assert.True(client.ServerInstructions?.Contains(said, StringComparison.Ordinal) == true, $"The server's instructions leave out \"{said}\": {client.ServerInstructions}");
        }
        string sockets = Path.Combine(state, "mcp");
        Assert.False(Directory.Exists(sockets) && Directory.GetFiles(sockets, "*.sock").Length > 0, "Listing the tools started a Passenger");

        // Its first call starts Passenger on the agent's folder, whose window asks the person and holds the call meanwhile.
        using CancellationTokenSource giveUp = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        Task<CallToolResult> waiting = CallGivingUp(client, "workspace", new(), giveUp.Token);
        string? window = null;
        await Until(() => (window = Directory.Exists(sockets) ? Directory.GetFiles(sockets, "*.sock").FirstOrDefault(s =>
            File.Exists(Path.ChangeExtension(s, ".folder")) && File.ReadAllText(Path.ChangeExtension(s, ".folder")) == PathNames.Canonical(notes)) : null) is not null,
            "Passenger to start with a window on the agent's folder");
        string log = Path.Combine(state, "logs", "passenger.log");
        await Until(() => File.Exists(log) && Ui.Shared(log).Contains($"Agents: Test Agent asks to work in the window on {PathNames.Canonical(notes)}", StringComparison.Ordinal),
            "the started Passenger's window to ask the person to let Test Agent in");
        Assert.False(waiting.IsCompleted, $"The call was answered before the person answered: {waiting.Status}");

        // The agent gives up and goes: the Passenger it started, the run's child, goes with it.
        giveUp.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        int started = McpHostProcess(window!);
        await client.DisposeAsync();
        await Until(() => !Processes.IsRunning(started), "the Passenger the agent started to end with its session");
    }

    [AvaloniaFact]
    public void An_agents_first_call_has_the_running_Passenger_open_its_folder_in_a_window_of_its_own_which_asks_first()
    {
        string wanted = Plain("mcp-wanted-");
        string unrelated = Plain("mcp-unrelated-");
        MainWindow first = new() { Width = 1200, Height = 800, StateFolder = settings };
        first.Show();
        first.KeepLayout();
        first.KeepSettings();
        first.OpenFolder(unrelated);
        Ui.Settle();
        McpClient client = Await(Task.Run(() => Start(["--mcp-attach", wanted])), "the agent's session starting");
        try
        {
            // The window on another folder is not the agent's: the running Passenger opens its folder in a window of its own.
            Task<CallToolResult> denied = Task.Run(() => Result(client, "workspace"));
            WaitFor(() => PassengerApp.Windows.Count == 2, "the running Passenger to open the agent's folder in a window of its own");
            MainWindow second = PassengerApp.Windows.Single(w => w != first);
            Assert.Equal(PathNames.Canonical(wanted), second.Folder);
            AgentAdmissionDialog asked = Admission(second, "the window to ask whether the agent may work there");
            Assert.Contains("Test Agent wants to work in this window", Texts(asked));
            Assert.Empty(first.OwnedWindows.OfType<AgentAdmissionDialog>());

            // Don't allow answers the call saying so; the session keeps its tools.
            Press(asked, AgentAdmissionDialog.DontAllow);
            CallToolResult no = Await(denied, "the call the person did not allow");
            Assert.True(no.IsError == true && Text(no).Contains("did not let Test Agent work there", StringComparison.Ordinal),
                $"A call the person did not let in was answered: {Text(no)}");
            Assert.True(first.Agents == 0 && second.Agents == 0, "An agent the person did not let in is counted on a window");

            // A later call asks again; allowed always for this folder, it goes through.
            Task<JsonElement> allowed = Task.Run(() => Call(client, "workspace"));
            AgentAdmissionDialog again = Admission(second, "the window to ask again at the next call");
            Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(again).OfType<CheckBox>().Single(c => (string?)c.Content == "Always for this folder").IsChecked = true;
            Press(again, AgentAdmissionDialog.Allow);
            Assert.Equal(second.Folder, Await(allowed, "the call the person allowed").GetProperty("folder").GetString());
            WaitFor(() => second.AgentStatus == "Agent connected", "the status bar to say an agent is on the window");
            Assert.Null(first.AgentStatus);

            // The window closed, the next call has the folder opened again, and the folder's Always lets the agent in unasked.
            second.Close();
            Ui.Settle();
            JsonElement reopened = Await(Task.Run(() => Call(client, "workspace")), "the call after the agent's window closed");
            MainWindow third = PassengerApp.Windows.Single(w => w != first);
            Assert.Equal(third.Folder, reopened.GetProperty("folder").GetString());
            Assert.Empty(third.OwnedWindows.OfType<AgentAdmissionDialog>());

            // Another agent set up in the folder joins the window open on it: no window more.
            McpClient another = Await(Task.Run(() => Start(["--mcp-attach"], wanted)), "a second agent's session starting");
            try
            {
                Assert.Equal(third.Folder, Await(Task.Run(() => Call(another, "workspace")), "the second agent's call").GetProperty("folder").GetString());
                Assert.Equal(2, PassengerApp.Windows.Count);
                WaitFor(() => third.AgentStatus == "2 agents connected", "the window to count both agents");
            }
            finally
            {
                Gone(another);
            }

            // Taken back in Settings: the folder leaves the list, and the agent on its window is asked again at its next call.
            SettingsDialog taking = DialogButtonsTests.Check(third.ShowSettings());
            taking.Page = SettingsDialog.McpPage;
            Ui.Settle();
            Assert.Equal([PathNames.Canonical(wanted)], taking.AgentFolderList.Items.OfType<string>());
            Assert.False(taking.RemoveAgentFolderButton.IsEnabled, "Remove was offered with no folder chosen");
            taking.AgentFolderList.SelectedIndex = 0;
            taking.RemoveAgentFolderButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Empty(taking.AgentFolderList.Items.OfType<string>());
            taking.Ok();
            Ui.Settle();
            Assert.False(third.AlwaysLetsAgentsIn, "The folder taken back still lets agents in unasked");
            Task<CallToolResult> afterwards = Task.Run(() => Result(client, "workspace"));
            Press(Admission(third, "the window to ask again once its folder was taken back"), AgentAdmissionDialog.DontAllow);
            Assert.True(Await(afterwards, "the call after the folder was taken back").IsError, "A call after the folder was taken back went on unasked");
        }
        finally
        {
            Gone(client);
            foreach (MainWindow window in PassengerApp.Windows.Reverse().ToList())
            {
                window.Close();
            }
        }
    }

    /// <summary>Settings' MCP Server page gives each agent its setup for this copy of Passenger, as the person pastes it: the
    /// running program's own path in every command and configuration, Claude Desktop's with the window's folder after
    /// --mcp-attach, the configurations JSON and TOML that read, and each Copy putting its text on the clipboard whole.</summary>
    [AvaloniaFact]
    public void Settings_gives_each_agent_its_setup_for_this_copy_and_copies_it_whole()
    {
        string project = Plain("mcp-setup-");
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = settings };
        window.Show();
        window.OpenFolder(project);
        Ui.Settle();
        SettingsDialog dialog = DialogButtonsTests.Check(window.ShowSettings());
        try
        {
            dialog.Query = "claude";
            Ui.Settle();
            Assert.Equal([SettingsDialog.McpPage], dialog.Pages);
            dialog.Query = string.Empty;
            dialog.Page = SettingsDialog.McpPage;
            Ui.Settle();
            Dictionary<string, string> setups = dialog.AgentSetups.ToDictionary(s => s.Heading, s => s.Text);
            Assert.Equal(["Claude Code", "A repository's .mcp.json", "Codex", "Codex's config.toml", "Claude Desktop"], dialog.AgentSetups.Select(s => s.Heading));

            // The commands name the program running, wherever it is, which an agent's session starts.
            string claude = setups["Claude Code"];
            Assert.StartsWith("claude mcp add passenger -- ", claude, StringComparison.Ordinal);
            Assert.EndsWith(" --mcp-attach", claude, StringComparison.Ordinal);
            string passenger = claude["claude mcp add passenger -- ".Length..^" --mcp-attach".Length];
            Assert.True(File.Exists(passenger) && PathNames.Same(passenger, PathNames.Canonical(passenger)), $"Claude Code's command names no program: {claude}");
            Assert.Equal($"codex mcp add passenger -- {passenger} --mcp-attach", setups["Codex"]);
            Assert.Equal($"[mcp_servers.passenger]\ncommand = {JsonSerializer.Serialize(passenger)}\nargs = [\"--mcp-attach\"]", setups["Codex's config.toml"]);
            foreach ((string heading, string[] arguments) in new (string, string[])[] { ("A repository's .mcp.json", ["--mcp-attach"]), ("Claude Desktop", ["--mcp-attach", PathNames.Canonical(project)]) })
            {
                JsonElement server = JsonDocument.Parse(setups[heading]).RootElement.GetProperty("mcpServers").GetProperty("passenger");
                Assert.Equal(passenger, server.GetProperty("command").GetString());
                Assert.Equal(arguments, server.GetProperty("args").EnumerateArray().Select(a => a.GetString()!));
            }

            // Copy puts the text on the clipboard whole, and says it did until another is copied.
            Button copy = dialog.CopyButtons["Claude Desktop"];
            copy.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Ui.WaitFor(() => Ui.Clipboard(dialog) == setups["Claude Desktop"], "Claude Desktop's configuration on the clipboard");
            Assert.Equal("Copied", copy.Content);
            dialog.CopyButtons["Claude Code"].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Ui.WaitFor(() => Ui.Clipboard(dialog) == claude, "Claude Code's command on the clipboard");
            Assert.Equal(("Copy", "Copied"), (copy.Content, dialog.CopyButtons["Claude Code"].Content));
        }
        finally
        {
            dialog.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void An_agents_open_of_another_folder_is_the_persons_to_allow_on_their_window_and_not_asked_on_the_agents_own()
    {
        string theirs = Plain("mcp-theirs-");
        string elsewhere = Plain("mcp-elsewhere-");
        MainWindow window = AgentsWindow(theirs);
        McpClient client = Await(Task.Run(() => Start(["--mcp-attach", theirs])), "the agent attaching");
        try
        {
            Task<CallToolResult> refused = Task.Run(() => Result(client, "open", new() { ["path"] = elsewhere }));
            ConfirmDialog asked = DialogButtonsTests.Check(Question(window, "the question for the agent's open"));
            Assert.Contains(Texts(asked), t => t.StartsWith("Test Agent wants to open ", StringComparison.Ordinal) && t.EndsWith($" here, replacing {window.Folder}.", StringComparison.Ordinal));
            Press(asked, AgentAdmissionDialog.DontAllow);
            CallToolResult no = Await(refused, "the open the person did not allow");
            Assert.True(no.IsError == true && Text(no).Contains("did not let Test Agent open", StringComparison.Ordinal), $"An open the person did not allow was answered: {Text(no)}");
            Assert.Equal(PathNames.Canonical(theirs), window.Folder);

            Task<JsonElement> opening = Task.Run(() => Call(client, "open", new() { ["path"] = elsewhere }));
            Press(Question(window, "the open asked again"), AgentAdmissionDialog.Allow);
            Assert.Equal(PathNames.Canonical(elsewhere), Await(opening, "the open the person allowed").GetProperty("folder").GetString());
            Assert.Equal(PathNames.Canonical(elsewhere), window.Folder);
        }
        finally
        {
            Gone(client);
            window.Close();
        }

        // The agent's own Passenger has nobody to ask.
        McpClient own = Await(Task.Run(() => Start(["--mcp", theirs])), "the agent's own Passenger starting");
        try
        {
            Assert.Equal(PathNames.Canonical(elsewhere), Await(Task.Run(() => Call(own, "open", new() { ["path"] = elsewhere })), "the open on the agent's own Passenger")
                .GetProperty("folder").GetString());
        }
        finally
        {
            Gone(own);
        }
    }

    /// <summary>A folder of the test's own with one text file, which opens with no language server to wait for.</summary>
    private string Plain(string name)
    {
        string made = TestFolders.Create(name);
        File.WriteAllText(Path.Combine(made, "Notes.txt"), "notes\n");
        madeFolders.Add(made);
        return made;
    }

    /// <summary>The window's question whether an agent may work there, once asked, held to the dialogs' rules.</summary>
    private AgentAdmissionDialog Admission(MainWindow window, string what)
    {
        WaitFor(() => window.OwnedWindows.OfType<AgentAdmissionDialog>().Any(d => d.IsVisible), what);
        return DialogButtonsTests.Check(window.OwnedWindows.OfType<AgentAdmissionDialog>().Single(d => d.IsVisible));
    }

    /// <summary>The agent's session ended, the window's loop running meanwhile.</summary>
    private void Gone(McpClient client) => Await(Task.Run(async () =>
    {
        await client.DisposeAsync();
        return true;
    }), "the agent going");

    /// <summary>The process a window's socket is named for (<c>PID-WINDOW.sock</c>).</summary>
    private static int McpHostProcess(string socket) => int.Parse(Path.GetFileNameWithoutExtension(socket).Split('-')[0], System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Waits off the UI thread for a condition, failing with what Passenger said meanwhile.</summary>
    private async Task Until(Func<bool> condition, string what)
    {
        DateTime end = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        while (!condition())
        {
            if (DateTime.UtcNow > end)
            {
                throw new TimeoutException($"Waited 120 s for {what}.{Said()}");
            }
            await Task.Delay(100, Cancel);
        }
    }
}
