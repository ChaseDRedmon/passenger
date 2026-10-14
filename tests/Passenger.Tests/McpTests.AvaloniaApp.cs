using System.Buffers.Binary;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Passenger.App;
using Passenger.Core;
using Debuggees = Passenger.Tests.Debugging.Helpers.Debuggees;

namespace Passenger.Tests;

/// <summary>The Avalonia app McpTests' agents run and drive: tests/Debuggees/AgentApp (a counter's button, a name box that
/// greets on Enter, a misspelt binding, a drop-down and a list to scroll), written once for the class with a project file on
/// the tests' own Avalonia and restored. Headless, so nothing of it shows on the screen.</summary>
public sealed class AgentAppFolder : IDisposable
{
    public AgentAppFolder()
    {
        Folder = PathNames.Canonical(TestFolders.Create("agentapp-"));
        TestFolders.Prepare(() =>
        {
            foreach (string file in Directory.GetFiles(Debuggees.Source("AgentApp")))
            {
                File.Copy(file, Path.Combine(Folder, Path.GetFileName(file)));
            }
            File.WriteAllText(Path.Combine(Folder, "AgentApp.csproj"), AvaloniaApp.HeadlessProject());
            Restore.Run(Folder);
        }, Folder);
    }

    public string Folder { get; }

    public void Dispose() => TestFolders.Delete(Folder);
}

/// <summary>Agents testing the Avalonia app Passenger runs as a person would: they look at it through the pictures it draws
/// and its visual tree, click, type, press keys and scroll through its window's own input, read its failed bindings, and
/// find Passenger's own controls by name.</summary>
public sealed partial class McpTests
{
    private readonly string appFolder;

    [Fact]
    public async Task An_agent_runs_an_avalonia_app_sees_it_clicks_its_button_types_into_it_scrolls_it_and_reads_its_failed_bindings()
    {
        await using McpClient client = await Start(["--mcp", appFolder]);
        JsonElement ran = await Call(client, "run", new() { ["waitSeconds"] = 0 });
        Assert.True(ran.GetProperty("stillRunning").GetBoolean(), $"The app ended as it started: {ran}");

        // Its one window, as the app has it: its title, its size in units.
        JsonElement windows = await Call(client, "app_windows");
        JsonElement main = Assert.Single(windows.GetProperty("windows").EnumerateArray());
        Assert.Equal(("Agent App", 480d, 360d), (main.GetProperty("title").GetString(), main.GetProperty("width").GetDouble(), main.GetProperty("height").GetDouble()));

        // A picture the app drew of it, headless as it is, at its own scaling, with what the pixels map to.
        CallToolResult picture = await Result(client, "app_screenshot");
        ImageContentBlock image = Assert.Single(picture.Content.OfType<ImageContentBlock>());
        double scaling = main.GetProperty("scaling").GetDouble();
        Assert.Equal(((int)(480 * scaling), (int)(360 * scaling)), PngSize(image.DecodedData.ToArray()));
        // A window drawn blank (nothing rendered) is a few hundred bytes of PNG; this one has text and controls.
        Assert.True(image.DecodedData.Length > 4000, $"The app's picture is {image.DecodedData.Length} bytes, as a blank one would be");
        Assert.Contains("pixels a unit", Assert.Single(picture.Content.OfType<TextContentBlock>()).Text, StringComparison.Ordinal);

        // The button found in the tree, clicked as a person clicks it, and the text it changes.
        string buttons = Text(await Call(client, "app_tree", new() { ["filter"] = "Button" }));
        Assert.Contains("Button#Count \"Count\"", buttons, StringComparison.Ordinal);
        Assert.Contains("\"Not clicked yet\"", Text(await Call(client, "app_tree", new() { ["control"] = "#Counter" })), StringComparison.Ordinal);
        string clicked = Text(await Call(client, "app_click", new() { ["control"] = "#Count" }));
        Assert.StartsWith("Clicked Button#Count @", clicked, StringComparison.Ordinal);
        Assert.Contains("\"Clicked once\"", Text(await Call(client, "app_tree", new() { ["control"] = "#Counter" })), StringComparison.Ordinal);
        // By what it says, as a person finds it: the button, not the text inside it.
        await Call(client, "app_click", new() { ["control"] = "\"Count\"" });
        Assert.Contains("\"Clicked 2 times\"", Text(await Call(client, "app_tree", new() { ["control"] = "#Counter" })), StringComparison.Ordinal);

        // Typed into the name box and Enter pressed, its handler greets; replace types over what is there.
        Assert.Contains("it shows \"Ada\"", Text(await Call(client, "app_type", new() { ["control"] = "#NameBox", ["text"] = "Ada" })), StringComparison.Ordinal);
        await Call(client, "app_key", new() { ["keys"] = "Enter" });
        Assert.Contains("\"Hello, Ada\"", Text(await Call(client, "app_tree", new() { ["control"] = "#Greeting" })), StringComparison.Ordinal);
        Assert.Contains("it shows \"Grace\"", Text(await Call(client, "app_type", new() { ["control"] = "#NameBox", ["text"] = "Grace", ["replace"] = true })), StringComparison.Ordinal);

        // An item scrolled out of view is refused, saying so; the wheel over the list brings the list along.
        CallToolResult hidden = await Result(client, "app_click", new() { ["control"] = "\"Item 30\"" });
        Assert.True(hidden.IsError, "A click on an item out of view was sent to whatever is there");
        Assert.Contains("scrolled out of view inside ScrollViewer#List", Text(hidden), StringComparison.Ordinal);
        Assert.Contains("ScrollViewer#List at 0,", Text(await Call(client, "app_scroll", new() { ["control"] = "#List", ["dy"] = 3 })), StringComparison.Ordinal);
        string list = Text(await Call(client, "app_tree", new() { ["control"] = "#List", ["properties"] = new[] { "Offset" } }));
        Assert.DoesNotContain("Offset=0, 0", list, StringComparison.Ordinal);
        Assert.EndsWith("[out of view]", list.Split('\n').Single(l => l.Contains("\"Item 1\"", StringComparison.Ordinal)), StringComparison.Ordinal);

        // The misspelt binding, once, at its place in the XAML with the member nearest.
        JsonElement failed = Assert.Single((await Call(client, "app_binding_errors")).EnumerateArray());
        Assert.Equal(("TextBlock#Caption", "Nmae", "Name"), (failed.GetProperty("control").GetString(), failed.GetProperty("path").GetString(),
            failed.GetProperty("nearest").EnumerateArray().Single().GetString()));
        Assert.StartsWith("MainWindow.axaml:", failed.GetProperty("place").GetString(), StringComparison.Ordinal);

        // Passenger's own window is an Avalonia app too: its controls by name.
        Assert.Contains("TextBlock#ProjectName", Text(await Call(client, "app_tree", new() { ["app"] = "ide", ["filter"] = "#ProjectName" })), StringComparison.Ordinal);

        // Once it has ended, the tools say so (a moment after Stop, as the app goes).
        await Call(client, "stop");
        CallToolResult gone = await Result(client, "app_tree");
        for (int i = 0; i < 50 && gone.IsError != true; i++)
        {
            await Task.Delay(200, Cancel);
            gone = await Result(client, "app_tree");
        }
        Assert.True(gone.IsError, "The tree of an app that has ended was answered");
        Assert.Contains("AgentApp has ended", Text(gone), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void An_agent_on_the_persons_window_is_asked_before_it_first_clicks_or_presses_keys_in_their_avalonia_app_or_in_passenger()
    {
        MainWindow window = AgentsWindow(appFolder);
        McpClient? client = null;
        try
        {
            Assert.Null(Ui.Await(window.RunAsync(), "the app to build and start", TimeSpan.FromMinutes(4)));
            client = Await(Task.Run(() => Start(["--mcp-attach", ProcessId])), "the agent attaching");
            McpClient agent = client;

            // Looking asks nothing.
            Assert.Contains("Button#Count", Text(Await(Task.Run(() => Call(agent, "app_tree", new() { ["filter"] = "#Count" })), "the app's tree")), StringComparison.Ordinal);
            Assert.Empty(window.OwnedWindows.OfType<ConfirmDialog>());

            // The first click waits for the person, and lands once they allow it.
            Task<JsonElement> clicking = Task.Run(() => Call(agent, "app_click", new() { ["control"] = "#Count" }));
            ConfirmDialog asked = Question(window, "the question for the agent's click");
            Assert.Contains("Allow app_click?", Texts(asked));
            Press(asked, "Allow");
            Assert.StartsWith("Clicked Button#Count", Text(Await(clicking, "the click")), StringComparison.Ordinal);
            Assert.Contains("\"Clicked once\"", Text(Await(Task.Run(() => Call(agent, "app_tree", new() { ["control"] = "#Counter" })), "the counter")), StringComparison.Ordinal);

            // Passenger's own window, the person's: its run configurations' menu opened by name, and closed by a key, asked for first.
            TextBlock project = window.FindControl<TextBlock>("ProjectName")!;
            Assert.Contains($"TextBlock#ProjectName \"{project.Text}\"", Text(Await(Task.Run(() => Call(agent, "app_tree", new() { ["app"] = "ide", ["filter"] = "#ProjectName" })), "Passenger's tree")),
                StringComparison.Ordinal);
            Await(Task.Run(() => Call(agent, "app_click", new() { ["app"] = "ide", ["control"] = "#RunConfigButton" })), "the click on Passenger's run configurations");
            Dictionary<string, object?> menu = new() { ["app"] = "ide", ["filter"] = "MenuItem \"AgentApp\"" };
            Assert.Contains("MenuItem \"AgentApp\"", Text(Await(Task.Run(() => Call(agent, "app_tree", menu)), "the run configurations' menu")), StringComparison.Ordinal);
            Task<JsonElement> escaping = Task.Run(() => Call(agent, "app_key", new() { ["app"] = "ide", ["keys"] = "Escape", ["control"] = "MenuItem \"AgentApp\"" }));
            Press(Question(window, "the question for the agent's keys"), "Allow");
            string escaped = Text(Await(escaping, "the Escape"));
            string after = Text(Await(Task.Run(() => Call(agent, "app_tree", menu)), "the menu closed"));
            Assert.True(after.StartsWith("Nothing in Passenger matches", StringComparison.Ordinal), $"Escape ({escaped}) left the menu open: {after}");
        }
        finally
        {
            if (client is { } going)
            {
                Await(Task.Run(async () =>
                {
                    await going.DisposeAsync();
                    return true;
                }), "the agent going");
            }
            window.Runner.Stop();
            WaitFor(() => !window.Runner.IsRunning, "the app stopped");
            window.Close();
        }
    }

    /// <summary>What a tool answered in words, or an error's words.</summary>
    private static string Text(JsonElement answer) => answer.ValueKind == JsonValueKind.String ? answer.GetString()! : answer.GetRawText();

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));

    /// <summary>A PNG's width and height, from its header.</summary>
    private static (int Width, int Height) PngSize(byte[] png)
    {
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        return (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }
}
