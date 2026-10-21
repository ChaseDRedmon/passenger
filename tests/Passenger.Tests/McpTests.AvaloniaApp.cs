using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Passenger.App;
using Passenger.App.Settings;
using Passenger.Core;
using Debuggees = Passenger.Tests.Debugging.Helpers.Debuggees;

namespace Passenger.Tests;

/// <summary>The Avalonia app McpTests' agents run and drive: tests/Debuggees/AgentApp (a counter's button, a name box that
/// greets on Enter, a binding and a misspelt one, a drop-down and a list to scroll), written once for the class with a project file on
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
        JsonElement built = await Call(client, "build");
        Assert.True(built.GetProperty("succeeded").GetBoolean(), $"The app did not build: {built}");
        // A build that succeeded is answered in a few words: MSBuild's lines are for one that did not.
        Assert.False(built.TryGetProperty("outputTail", out _), $"A build that succeeded was answered with MSBuild's output: {built}");
        // An app never ends by itself: run waits for its UI rather than for the whole of waitSeconds, and says it is up.
        long began = Stopwatch.GetTimestamp();
        JsonElement ran = await Call(client, "run");
        Assert.True(ran.GetProperty("stillRunning").GetBoolean(), $"The app ended as it started: {ran}");
        Assert.True(ran.GetProperty("status").GetString()!.Contains("its UI up", StringComparison.Ordinal) && Stopwatch.GetElapsedTime(began) < TimeSpan.FromSeconds(25),
            $"run waited {Stopwatch.GetElapsedTime(began).TotalSeconds:0} s for an app that never ends, and said: {ran}");

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

        // A drop-down's list opens as a window of its own over the app's, where the agent picks from it.
        await Call(client, "app_click", new() { ["control"] = "#Colour" });
        JsonElement popup = Assert.Single((await Call(client, "app_windows")).GetProperty("windows").EnumerateArray(), w => w.TryGetProperty("owner", out _));
        Assert.Equal(("PopupRoot", main.GetProperty("id").GetString()), (popup.GetProperty("type").GetString(), popup.GetProperty("owner").GetString()));
        string picked = Text(await Call(client, "app_click", new() { ["control"] = "ComboBoxItem \"Blue\"" }));
        string colour = Text(await Call(client, "app_tree", new() { ["filter"] = "#Colour" }));
        Assert.True(colour.Contains("ComboBox#Colour \"Blue\"", StringComparison.Ordinal), $"Blue, clicked in the drop-down ({picked}), is not chosen: {colour}");

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
        CallToolResult gone = null!;
        await Waiting.UntilAsync(async () => (gone = await Result(client, "app_tree")).IsError == true, TimeSpan.FromSeconds(10), Cancel, TimeSpan.FromMilliseconds(200));
        Assert.True(gone.IsError, "The tree of an app that has ended was answered");
        Assert.Contains("AgentApp has ended", Text(gone), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_agent_debugging_an_avalonia_app_that_clicks_into_a_breakpoint_is_told_at_once_where_it_stopped()
    {
        await using McpClient client = await Start(["--mcp", appFolder]);
        // An app never stops by itself: debug_start comes back once its UI is up, not after the whole of waitSeconds.
        Stopwatch launching = Stopwatch.StartNew();
        JsonElement started = await Call(client, "debug_start");
        Assert.True(started.GetProperty("status").GetString()!.Contains("its UI up", StringComparison.Ordinal) && launching.Elapsed < TimeSpan.FromSeconds(50),
            $"debug_start waited {launching.Elapsed.TotalSeconds:0} s for an app that never stops, and said: {started}");
        Assert.Single((await Call(client, "app_windows")).GetProperty("windows").EnumerateArray());

        // Set while the app runs, the breakpoint in the button's handler is there for the click straight after it, and the
        // click says where it stopped the app, whose paused UI could not say what the click did.
        await Call(client, "breakpoint_set", new() { ["path"] = "MainWindow.axaml.cs", ["line"] = 23 });
        Stopwatch asked = Stopwatch.StartNew();
        string clicked = Text(await Call(client, "app_click", new() { ["control"] = "#Count" }));
        Assert.True(asked.Elapsed < TimeSpan.FromSeconds(8), $"The click that stopped at a breakpoint took {asked.Elapsed} to say so: {clicked}");
        Assert.Contains("and that paused the program (breakpoint) at MainWindow.axaml.cs:23:", clicked, StringComparison.Ordinal);
        JsonElement stopped = await Call(client, "debug_state");
        Assert.Equal("paused", stopped.GetProperty("state").GetString());
        // Avalonia's and the inspector's frames under the handler are one line of their modules, not a line each.
        string[] frames = [.. stopped.GetProperty("frames").EnumerateArray().Select(f => f.GetString()!)];
        Assert.True(frames[0].Contains("MainWindow.axaml.cs:23", StringComparison.Ordinal) && frames.Length < 8
            && frames.Any(f => f.Contains(" frames outside the folder's code (", StringComparison.Ordinal) && f.Contains("Avalonia.", StringComparison.Ordinal)),
            $"The stop's frames, for an agent:\n{string.Join('\n', frames)}");

        // Let go, the click finishes.
        await Call(client, "breakpoint_remove", new() { ["path"] = "MainWindow.axaml.cs", ["line"] = 23 });
        await Call(client, "debug_continue", new() { ["waitSeconds"] = 1 });
        Assert.Contains("\"Clicked once\"", Text(await Call(client, "app_tree", new() { ["control"] = "#Counter" })), StringComparison.Ordinal);
        await Call(client, "stop");
    }

    /// <summary>What the Live Visual Tree and the editor give a person debugging the app, given an agent: a failed binding
    /// stopping it with XAML Binding Errors on, and running on with it off; a button's properties with where each value came
    /// from and its styles with why each applies or not; Break When Value Changes on the counter's text, which a click stops
    /// where the code-behind sets it; and the binding values the window's XAML shows at that stop.</summary>
    [Fact]
    public async Task An_agent_debugging_an_avalonia_app_reads_a_controls_sources_and_styles_breaks_where_a_property_changes_and_reads_the_xamls_binding_values()
    {
        await using McpClient client = await Start(["--mcp", appFolder]);
        JsonElement on = Assert.Single((await Call(client, "breakpoint_set", new() { ["bindingErrors"] = true })).EnumerateArray(), b => b.TryGetProperty("bindingErrors", out _));
        Assert.True(on.GetProperty("enabled").GetBoolean(), $"XAML Binding Errors is not on: {on}");
        JsonElement failed = await Call(client, "debug_start");
        Assert.True(failed.GetProperty("state").GetString() == "paused"
            && failed.GetProperty("locals").EnumerateArray().Any(l => l.GetProperty("name").GetString() == "$bindingError" && l.GetProperty("value").GetString()!.Contains("Nmae", StringComparison.Ordinal)),
            $"The misspelt binding did not stop the app with XAML Binding Errors on: {failed}");
        JsonElement off = Assert.Single((await Call(client, "breakpoint_remove", new() { ["bindingErrors"] = true })).EnumerateArray(), b => b.TryGetProperty("bindingErrors", out _));
        Assert.False(off.GetProperty("enabled").GetBoolean(), $"XAML Binding Errors is still on: {off}");
        Assert.Equal("running", (await Call(client, "debug_continue", new() { ["waitSeconds"] = 2 })).GetProperty("state").GetString());

        // The button as the Live Visual Tree's details show it: its XAML, its content set there, and the styles of its theme.
        JsonElement button = await Call(client, "app_details", new() { ["control"] = "#Count" });
        Assert.Equal("Button", button.GetProperty("type").GetString());
        Assert.StartsWith("MainWindow.axaml:8:", button.GetProperty("xaml").GetString(), StringComparison.Ordinal);
        JsonElement content = Assert.Single(button.GetProperty("properties").EnumerateArray(), p => p.GetProperty("name").GetString() == "Content");
        Assert.Equal(("\"Count\"", "Local"), (content.GetProperty("value").GetString(), content.GetProperty("source").GetString()));
        JsonElement[] styles = [.. button.GetProperty("styles").EnumerateArray()];
        Assert.True(styles.Any(s => s.GetProperty("applies").GetBoolean()) && styles.Any(s => !s.GetProperty("applies").GetBoolean()
            && s.GetProperty("whyNot").GetString() == "`:pressed` is not active on it"), $"The button's styles do not say which apply and why not: {button.GetProperty("styles")}");
        Assert.DoesNotContain(button.GetProperty("properties").EnumerateArray(), p => p.GetProperty("source").GetString() == "Default");

        // Break When Value Changes on the counter's text: the click stops where the code-behind sets it.
        JsonElement watched = Assert.Single((await Call(client, "breakpoint_set", new() { ["control"] = "#Counter", ["property"] = "Text" })).EnumerateArray());
        Assert.Equal("Text on TextBlock#Counter", watched.GetProperty("property").GetString());
        Assert.True((await Call(client, "app_details", new() { ["control"] = "#Counter", ["properties"] = "Text" })).GetProperty("properties").EnumerateArray()
            .Any(p => p.GetProperty("name").GetString() == "Text" && p.TryGetProperty("breakpoint", out JsonElement marked) && marked.GetBoolean()),
            "The counter's details do not mark its watched text");
        string clicked = Text(await Call(client, "app_click", new() { ["control"] = "#Count" }));
        Assert.True(clicked.Contains("and that paused the program", StringComparison.Ordinal) && clicked.Contains("at MainWindow.axaml.cs:24:", StringComparison.Ordinal),
            $"The click did not stop where the code-behind sets the watched text: {clicked}");

        // At the stop, the window's XAML open in the editor shows each binding's value after it, and the misspelt one as failed.
        await Call(client, "open_file", new() { ["path"] = "MainWindow.axaml" });
        JsonElement stop = await Call(client, "debug_state");
        string[] values = stop.TryGetProperty("bindingValues", out JsonElement shown) ? [.. shown.EnumerateArray().Select(v => v.GetString()!)] : [];
        Assert.True(values.Contains("MainWindow.axaml:12: Text: \"Ada\"") && values.Any(v => v.StartsWith("MainWindow.axaml:11: Text: binding failed at Nmae: ", StringComparison.Ordinal)),
            $"The binding values at the stop are not those the editor shows: {stop}");

        // Removed by the id the breakpoint names its element by (the paused app answers no query), the click finishes.
        await Call(client, "breakpoint_remove", new() { ["control"] = watched.GetProperty("control").GetString(), ["property"] = "Text" });
        Assert.DoesNotContain((await Call(client, "breakpoints")).EnumerateArray(), b => b.TryGetProperty("property", out _));
        await Call(client, "debug_continue", new() { ["waitSeconds"] = 1 });
        Assert.Contains("\"Clicked once\"", Text(await Call(client, "app_tree", new() { ["control"] = "#Counter" })), StringComparison.Ordinal);
        await Call(client, "stop");
    }

    /// <summary>Profiled until it ends by itself once its window has been drawn ("--quit-once-drawn"), not for a time that
    /// a loaded machine's app may not have drawn in; and an app that draws nothing says so, its frames' count 0 and why.</summary>
    [Fact]
    public async Task An_agent_profiling_an_avalonia_app_reads_its_frames_heaviest_controls_by_their_lines_in_the_folders_xaml()
    {
        await using McpClient client = await Start(["--mcp", appFolder]);
        JsonElement undrawn = (await Call(client, "profile", new() { ["arguments"] = "--quit-at-once" })).GetProperty("frames");
        Assert.True(undrawn.GetProperty("count").GetInt32() == 0 && undrawn.GetProperty("why").GetString() is { Length: > 0 },
            $"The profile of an Avalonia app that drew nothing did not say why it has no frames: {undrawn}");

        JsonElement profiled = await Call(client, "profile", new() { ["arguments"] = "--quit-once-drawn" });
        JsonElement frames = profiled.GetProperty("frames");
        Assert.True(frames.GetProperty("count").GetInt32() > 0, $"The profile of an Avalonia app has no frames: {frames}");
        string[] places = [.. frames.GetProperty("heaviest").EnumerateArray().Select(c => c.TryGetProperty("xaml", out JsonElement at) ? at.GetString() : null).OfType<string>()];
        Assert.NotEmpty(places);
        Assert.All(places, place => Assert.True(place.StartsWith("MainWindow.axaml:", StringComparison.Ordinal),
            $"A control's XAML place is {place}, not the folder's MainWindow.axaml and a line, as every other path a tool gives"));
        // The Frames page's layout table, by control type with each type's controls, and the longest frame's own breakdown.
        JsonElement[] types = [.. frames.GetProperty("types").EnumerateArray()];
        Assert.Contains(types, t => t.GetProperty("type").GetString() == "TextBlock" && t.GetProperty("controlCount").GetInt32() >= 4
            && t.GetProperty("controls").EnumerateArray().Any(c => c.GetProperty("control").GetString()!.StartsWith("TextBlock#", StringComparison.Ordinal)));
        Assert.All(types, t => Assert.True(t.GetProperty("measures").GetInt32() + t.GetProperty("arranges").GetInt32() > 0, $"A type with no layout is listed: {t}"));
        JsonElement longest = frames.GetProperty("longest");
        Assert.True(longest.GetProperty("durationMs").GetDouble() == frames.GetProperty("longestMs").GetDouble(),
            $"The longest frame is not the one the frames say: {frames}");
    }

    /// <summary>On the person's window an agent asks before its first click or key in their app or in Passenger itself, and
    /// once allowed both still never answers the questions Passenger asks the person for it, nor reaches Settings. Passenger's
    /// own windows are inspected once a process, so this is the run's one test of them in the test process itself.</summary>
    [AvaloniaFact]
    public void An_agent_on_the_persons_window_is_asked_before_it_first_clicks_or_presses_keys_in_their_avalonia_app_or_in_passenger()
    {
        MainWindow window = AgentsWindow(appFolder);
        McpClient? client = null;
        SettingsDialog? shown = null;
        try
        {
            Ui.Launched(window, window.RunAsync(), "the app to build and start", TimeSpan.FromMinutes(4));
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

            // Passenger's own window, the person's: its run configurations' menu opened by name, and closed by a key, asked for
            // first, the menu keeping the keys though the question came between.
            TextBlock project = window.FindControl<TextBlock>("ProjectName")!;
            // The tree names an element by its type, #name, any .classes, then its text.
            Assert.Matches($"TextBlock#ProjectName(\\.[\\w-]+)* \"{System.Text.RegularExpressions.Regex.Escape(project.Text!)}\"",
                Text(Await(Task.Run(() => Call(agent, "app_tree", new() { ["app"] = "ide", ["filter"] = "#ProjectName" })), "Passenger's tree")));
            Await(Task.Run(() => Call(agent, "app_click", new() { ["app"] = "ide", ["control"] = "#RunConfigButton" })), "the click on Passenger's run configurations");
            Dictionary<string, object?> menu = new() { ["app"] = "ide", ["filter"] = "MenuItem \"AgentApp\"" };
            Assert.Contains("MenuItem \"AgentApp\"", Text(Await(Task.Run(() => Call(agent, "app_tree", menu)), "the run configurations' menu")), StringComparison.Ordinal);
            Task<JsonElement> escaping = Task.Run(() => Call(agent, "app_key", new() { ["app"] = "ide", ["keys"] = "Escape" }));
            Press(Question(window, "the question for the agent's keys"), "Allow");
            string escaped = Text(Await(escaping, "the Escape"));
            string after = Text(Await(Task.Run(() => Call(agent, "app_tree", menu)), "the menu closed"));
            Assert.True(after.StartsWith("Nothing in Passenger matches", StringComparison.Ordinal), $"Escape ({escaped}) left the menu open: {after}");

            // Allowed both, it never answers the question its own build waits on: its click and Enter on that question's Allow
            // are refused, and the question stands for the person.
            Task<CallToolResult> building = Task.Run(() => Result(agent, "build"));
            ConfirmDialog question = Question(window, "the question for the agent's build");
            CallToolResult clicked = Await(Task.Run(() => Result(agent, "app_click", new() { ["app"] = "ide", ["window"] = "ConfirmDialog", ["control"] = "Button \"Allow\"" })),
                "the agent's click on the question's Allow");
            Assert.True(clicked.IsError == true && Text(clicked).Contains("only they answer it", StringComparison.Ordinal),
                $"The agent's click on the question its own build waits on was answered: {Text(clicked)}");
            CallToolResult entered = Await(Task.Run(() => Result(agent, "app_key", new() { ["app"] = "ide", ["window"] = "ConfirmDialog", ["keys"] = "Enter" })),
                "the agent's Enter in the question");
            Assert.True(entered.IsError == true && Text(entered).Contains("only they answer it", StringComparison.Ordinal),
                $"The agent's Enter in the question its own build waits on was answered: {Text(entered)}");
            Assert.True(question.IsVisible && !building.IsCompleted, "The question the agent's build waits on was answered by the agent's own input");
            Press(question, "Deny");
            Assert.Contains("did not allow build", Text(Await(building, "the build the person denied")), StringComparison.Ordinal);

            // Nor does its input reach Settings, where the person says what agents may do.
            shown = window.ShowSettings();
            Ui.Settle();
            CallToolResult set = Await(Task.Run(() => Result(agent, "app_click", new() { ["app"] = "ide", ["window"] = "SettingsDialog", ["x"] = 20, ["y"] = 20 })),
                "the agent's click in Settings");
            Assert.True(set.IsError == true && Text(set).Contains("Settings is the person's alone", StringComparison.Ordinal),
                $"The agent's click in Settings was answered: {Text(set)}");
        }
        finally
        {
            shown?.Close();
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
