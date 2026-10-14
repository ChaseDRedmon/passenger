using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Discord;
using Passenger.App.Settings;

namespace Passenger.Tests;

/// <summary>Discord shows Playing Passenger as the JetBrains IDEs' Discord plugins have it: the solution, the file as
/// Settings says, Passenger's icon and button and the time since the solution opened; only Passenger once the person is
/// idle; nothing once the setting goes off, the solution closes or Passenger quits. With no Discord one line says so, and
/// Discord is found when it starts. A stand-in listens in each test's own place, and the run points
/// <c>PASSENGER_DISCORD_IPC</c> where nothing listens, so no window reaches the person's Discord.</summary>
[Collection(Alone.Name)]
public sealed class DiscordTests : IDisposable
{
    private const string Show = "Show activity in Discord";
    private const string Idle = "Only Passenger when idle for";

    private readonly string folder = TestFolders.Create("dc-");
    private readonly string settings = TestFolders.Create("dc-settings-");
    private readonly string solution;
    private readonly DiscordStandIn discord;

    public DiscordTests()
    {
        // A short folder: a Unix socket's path is at most 103 bytes.
        discord = new DiscordStandIn(folder);
        solution = Directory.CreateDirectory(Path.Combine(folder, "Shop")).FullName;
        File.WriteAllText(Path.Combine(solution, "One.cs"), "class One { }\n");
        File.WriteAllText(Path.Combine(solution, "Two.cs"), "class Two { }\n");
    }

    public void Dispose()
    {
        discord.Dispose();
        TestFolders.Delete(folder);
        TestFolders.Delete(settings);
    }

    [AvaloniaFact]
    public void Turning_it_on_shows_Passenger_and_the_solution_in_Discord_and_turning_it_off_clears_it()
    {
        discord.Start();
        DateTimeOffset before = DateTimeOffset.UtcNow;
        MainWindow window = Started();
        DateTimeOffset after = DateTimeOffset.UtcNow;
        Wait(TimeSpan.FromMilliseconds(500));
        Assert.True(discord.Connections == 0, $"Show activity in Discord is off by default, yet the window reached Discord {discord.Connections} times");

        SetShown(window, true);
        JsonObject activity = WaitForActivity(a => a?["details"] is not null, "an activity naming the solution");
        Frame handshake = discord.Frames[0];
        Assert.True(handshake.Op == 0 && handshake.Json.ToJsonString() == """{"v":1,"client_id":"1559714715847098468"}""",
            $"The first frame was not the handshake naming Passenger's Discord application: op {handshake.Op}, {handshake.Json.ToJsonString()}");
        Frame command = discord.Frames.Last(f => f.Op == 1);
        Assert.Equal("SET_ACTIVITY", (string?)command.Json["cmd"]);
        Assert.True((int?)command.Json["args"]?["pid"] == Environment.ProcessId, $"The activity names process {command.Json["args"]?["pid"]}, not Passenger's own, {Environment.ProcessId}");
        Assert.Equal("Shop", (string?)activity["details"]);
        Assert.True(activity["state"] is null, $"With no file open the activity still names one: {activity["state"]}");
        Assert.Equal("passenger-1024", (string?)activity["assets"]?["large_image"]);
        Assert.Equal("Passenger", (string?)activity["assets"]?["large_text"]);
        Assert.True(activity["assets"]!.AsObject().Count == 2, $"The activity carries more art than Passenger's icon: {activity["assets"]!.ToJsonString()}");
        Assert.Equal("""[{"label":"Get Passenger","url":"https://passenger-ide.net"}]""", activity["buttons"]?.ToJsonString());
        long start = (long)activity["timestamps"]!["start"]!;
        Assert.True(start >= before.ToUnixTimeMilliseconds() && start <= after.ToUnixTimeMilliseconds(),
            $"The activity's time starts at {DateTimeOffset.FromUnixTimeMilliseconds(start):O}, not when the solution opened, between {before:O} and {after:O}");
        string[] keys = [.. activity.Select(p => p.Key).Order(StringComparer.Ordinal)];
        Assert.True(keys.SequenceEqual(["assets", "buttons", "details", "timestamps"]), $"The activity carries more than it was to: {string.Join(", ", keys)}");

        // Off, the activity is cleared and Discord let go of; on again, the time is still the solution's.
        SetShown(window, false);
        Ui.WaitFor(() => discord.Ended == 1, "Discord to be let go of once the setting went off");
        Frame last = discord.Frames.Last();
        Assert.True(last.Op == 1 && (string?)last.Json["cmd"] == "SET_ACTIVITY" && last.Json["args"]?["activity"] is null && (int?)last.Json["args"]?["pid"] == Environment.ProcessId,
            $"The setting went off without the activity cleared first: the last frame was {last.Json.ToJsonString()}");
        SetShown(window, true);
        JsonObject again = WaitForActivity(a => a is not null, "the activity again once the setting came back on", connection: 2);
        Assert.True((long)again["timestamps"]!["start"]! == start, "Turned off and on, the activity's time began again rather than counting from when the solution opened");
        window.Close();
    }

    [AvaloniaFact]
    public void Switching_files_names_the_file_only_while_the_solution_and_the_file_are_chosen()
    {
        discord.Start();
        MainWindow window = Started();
        SetShown(window, true);
        window.OpenFile(Path.Combine(solution, "One.cs"));
        WaitForActivity(a => (string?)a?["state"] == "Editing One.cs", "the activity to name One.cs, the file open");
        window.OpenFile(Path.Combine(solution, "Two.cs"));
        WaitForActivity(a => (string?)a?["state"] == "Editing Two.cs", "the activity to follow the switch to Two.cs");
        window.OpenFile(Path.Combine(solution, "One.cs"));
        JsonObject one = WaitForActivity(a => (string?)a?["state"] == "Editing One.cs", "the activity to follow the switch back to One.cs");
        Assert.Equal("Shop", (string?)one["details"]);

        // The solution alone: no file, whichever is shown.
        Choose(window, "The solution");
        WaitForActivity(a => a?["details"] is not null && a["state"] is null, "the activity to name the solution alone");
        int sent = Activities().Count;
        window.OpenFile(Path.Combine(solution, "Two.cs"));
        Ui.Settle();
        window.OpenFile(Path.Combine(solution, "One.cs"));
        Wait(TimeSpan.FromMilliseconds(500));
        Assert.True(Activities().Skip(sent).All(a => a?["state"] is null), "With the solution alone chosen, switching files still named one in Discord");

        // Passenger only: neither, though the time since the solution opened stays.
        Choose(window, "Passenger only");
        JsonObject only = WaitForActivity(a => a is not null && a["details"] is null, "the activity to name nothing but Passenger");
        Assert.True(only["state"] is null && only["timestamps"] is not null && (string?)only["assets"]?["large_image"] == "passenger-1024",
            $"Passenger only showed {only.ToJsonString()}");
        window.Close();
    }

    [AvaloniaFact]
    public void Of_two_solution_windows_the_one_in_front_alone_shows_its_activity_and_the_other_takes_over_as_it_closes()
    {
        discord.Start();
        string bank = Directory.CreateDirectory(Path.Combine(folder, "Bank")).FullName;
        File.WriteAllText(Path.Combine(bank, "Three.cs"), "class Three { }\n");
        MainWindow window = Started();
        SetShown(window, true);
        WaitForActivity(a => (string?)a?["details"] == "Shop", "the first window's solution");

        // Another solution in a window of its own, in front: its activity, through the one connection.
        window.ApplySettings(window.Settings with { OpenProjectIn = OpenProjectIn.NewWindow });
        Assert.Null(window.Open(bank));
        Ui.Settle();
        MainWindow second = Assert.Single(PassengerApp.Windows, w => w != window);
        WaitForActivity(a => (string?)a?["details"] == "Bank", "the solution of the window in front");
        PassengerApp.BringForward(window);
        WaitForActivity(a => (string?)a?["details"] == "Shop", "the first window's solution once it is in front again");
        Assert.True(discord.Connections == 1, $"Two windows reached Discord {discord.Connections} times, not once between them");

        // The one in front closing, the other's activity shows, still on the one connection; the last closing clears it.
        window.Close();
        WaitForActivity(a => (string?)a?["details"] == "Bank", "the remaining window's solution once the one in front closed");
        Assert.Equal(0, discord.Ended);
        second.Close();
        Ui.WaitFor(() => discord.Ended == 1, "Discord to be let go of as the last window closed", TimeSpan.FromSeconds(3));
        Assert.True(Activities().Last() is null, "Passenger quit without clearing its activity");
    }

    [AvaloniaFact]
    public void Closing_the_solution_or_Passenger_clears_the_activity()
    {
        discord.Start();
        MainWindow window = Started();
        SetShown(window, true);
        long first = (long)WaitForActivity(a => a is not null, "the activity")["timestamps"]!["start"]!;
        Assert.Null(window.CloseSolution());
        Ui.WaitFor(() => discord.Ended == 1, "Discord to be let go of once the solution closed");
        Assert.True(Activities().Last() is null, "The solution closed, yet the activity was never cleared");

        // Opened again, it is shown again, timed from then, and quitting clears it before Passenger lets go.
        Wait(TimeSpan.FromMilliseconds(50));
        window.OpenFolder(solution);
        JsonObject again = WaitForActivity(a => (string?)a?["details"] == "Shop", "the activity once the solution opened again", connection: 2);
        Assert.True((long)again["timestamps"]!["start"]! > first, "The solution opened again, yet its time counts from when it first opened");
        window.Close();
        Ui.WaitFor(() => discord.Ended == 2, "Discord to be let go of as Passenger quit", TimeSpan.FromSeconds(3));
        Assert.True(Activities().Last() is null, "Passenger quit without clearing its activity");
    }

    [AvaloniaFact]
    public void Without_Discord_one_line_is_logged_and_Passenger_finds_Discord_when_it_starts_and_again_when_it_comes_back()
    {
        MainWindow window = Started();
        SetShown(window, true);
        Ui.PumpUntil(() => Discords().Length > 0, "a line saying Discord is not running", TimeSpan.FromSeconds(10));
        // Ten looks or so: the test's window looks for Discord every 200 ms.
        Wait(TimeSpan.FromSeconds(2));
        Assert.True(Discords().Length == 1, $"With no Discord the log has more than one line about it:\n{string.Join('\n', Discords())}");
        Assert.Contains($"Discord: not running (nothing at {discord.Base}-0)", Discords()[0], StringComparison.Ordinal);

        discord.Start();
        WaitForActivity(a => (string?)a?["details"] == "Shop", "the activity once Discord started");

        // Discord quits: one line, then it is found again when it comes back, the activity with it.
        int said = Discords().Length;
        discord.Stop();
        Wait(TimeSpan.FromSeconds(2));
        string[] since = [.. Discords().Skip(said)];
        Assert.Contains(since, line => line.Contains($"Discord: {discord.Base}-0 closed the connection; looking for it again", StringComparison.Ordinal));
        Assert.True(since.Length == 1, $"Discord went away and the log says more than one line about it:\n{string.Join('\n', since)}");
        int connected = discord.Connections;
        discord.Start();
        WaitForActivity(a => (string?)a?["details"] == "Shop", "the activity once Discord came back", connection: connected + 1);
        window.Close();
    }

    [AvaloniaFact]
    public void A_Discord_that_never_answers_never_slows_the_window()
    {
        discord.Start(answers: false);
        MainWindow window = Started();
        SetShown(window, true);
        Ui.PumpUntil(() => discord.Connections > 0, "the window to reach the Discord that never answers", TimeSpan.FromSeconds(10));
        Stopwatch switching = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++)
        {
            window.OpenFile(Path.Combine(solution, i % 2 == 0 ? "One.cs" : "Two.cs"));
            Ui.Settle();
        }
        Assert.True(switching.Elapsed < TimeSpan.FromSeconds(5), $"Ten file switches took {switching.Elapsed.TotalSeconds:0.0} s with Discord not answering");
        Ui.PumpUntil(() => discord.Connections > 1, "the window to try again after the handshake went unanswered", TimeSpan.FromSeconds(10));
        Assert.True(Discords().Length == 1 && Discords()[0].Contains("did not answer the handshake", StringComparison.Ordinal),
            $"A Discord that never answers was said other than once:\n{string.Join('\n', Discords())}");
        Stopwatch closing = Stopwatch.StartNew();
        window.Close();
        Assert.True(closing.Elapsed < TimeSpan.FromSeconds(1), $"Closing the window waited {closing.Elapsed.TotalSeconds:0.0} s, Discord not answering");
        Ui.WaitFor(() => discord.Ended == discord.Connections, "Passenger to let go of the Discord that never answers as it quit", TimeSpan.FromSeconds(3));
    }

    [AvaloniaFact]
    public void After_the_idle_time_only_Passenger_shows_until_the_person_is_back()
    {
        discord.Start();
        MainWindow window = Started(idle: TimeSpan.FromMilliseconds(800));
        window.OpenFile(Path.Combine(solution, "One.cs"));
        SetShown(window, true);
        WaitForActivity(a => (string?)a?["state"] == "Editing One.cs", "the activity naming the file");
        JsonObject idle = WaitForActivity(a => a is not null && a["details"] is null, "only Passenger once the person was idle");
        string[] keys = [.. idle.Select(p => p.Key).Order(StringComparer.Ordinal)];
        Assert.True(keys.SequenceEqual(["assets", "buttons"]), $"Idle, the activity names more than Passenger: {idle.ToJsonString()}");
        Ui.Press(window, "right");
        WaitForActivity(a => (string?)a?["state"] == "Editing One.cs", "the solution and the file once a key was pressed");
        window.Close();
    }

    [AvaloniaFact]
    public void Only_the_persons_window_reaches_Discord_and_a_tests_never_the_persons_own()
    {
        discord.Start();
        // A window no one called the person's (a screenshot's, an agent's), the setting on: Discord hears nothing.
        MainWindow other = new() { Width = 1200, Height = 800, StateFolder = settings, DiscordIpc = discord.Ipc };
        other.Show();
        other.ApplySettings(other.Settings with { ShowInDiscord = true });
        other.OpenFolder(solution);
        Wait(TimeSpan.FromSeconds(1));
        Assert.True(discord.Connections == 0, "A window that is not the person's reached Discord");
        other.Close();

        // The run's windows look where nothing listens, never the system's places, and the person's Discord hears nothing.
        Assert.Equal(TestRun.DiscordNowhere, Environment.GetEnvironmentVariable(DiscordIpc.Variable));
        int before = Ui.IdeLog().Split('\n').Length;
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = settings };
        Assert.True(window.DiscordIpc.Places.SequenceEqual([TestRun.DiscordNowhere]),
            $"A test's window looks for Discord at {string.Join(", ", window.DiscordIpc.Places)}, not only the run's own place");
        window.Show();
        window.ApplySettings(window.Settings with { ShowInDiscord = true });
        window.ShowActivityInDiscord();
        window.OpenFolder(solution);
        string[] said = [];
        Ui.PumpUntil(() => (said = [.. Ui.IdeLog().Split('\n').Skip(before - 1).Where(l => l.Contains("Discord: ", StringComparison.Ordinal))]).Length > 0,
            "a line saying where the window looked for Discord", TimeSpan.FromSeconds(10));
        Assert.True(said.Length == 1 && said[0].Contains($"nothing at {TestRun.DiscordNowhere}-0", StringComparison.Ordinal),
            $"The test's window looked for Discord other than in the run's own place:\n{string.Join('\n', said)}");
        window.Close();
    }

    [AvaloniaFact]
    public void The_Discord_page_has_the_plugins_options_at_their_defaults_greyed_while_off_and_kept()
    {
        MainWindow window = Started();
        SettingsDialog dialog = window.ShowSettings();
        Ui.Settle();
        dialog.Query = "discord";
        Ui.Settle();
        Assert.Equal([SettingsDialog.DiscordPage], dialog.Pages);
        Assert.Equal([Show, "Show", Idle], dialog.Shown);
        CheckBox on = Editor<CheckBox>(dialog, Show);
        ComboBox shows = Editor<ComboBox>(dialog, "Show");
        CheckBox idle = Editor<CheckBox>(dialog, Idle);
        NumericUpDown minutes = Editor<NumericUpDown>(dialog, Idle);
        Assert.True(on.IsChecked == false, "Show activity in Discord is on before the person turned it on");
        Assert.Equal(["Passenger only", "The solution", "The solution and the file"], shows.Items.Cast<string>());
        Assert.Equal("The solution and the file", shows.SelectedItem);
        Assert.True(idle.IsChecked == true && minutes.Value == 5, $"The idle time is not the plugins' 5 minutes, on: {idle.IsChecked}, {minutes.Value}");
        Assert.False(shows.IsEnabled || idle.IsEnabled || minutes.IsEnabled, "Discord's options can be changed while its activity is off");

        Click(dialog, on);
        Assert.True(shows.IsEnabled && idle.IsEnabled && minutes.IsEnabled, "Turning the activity on left its options greyed");
        shows.SelectedIndex = 1;
        minutes.Value = 12;
        dialog.Ok();
        Ui.Settle();
        window.Close();

        MainWindow again = Started();
        Assert.True(again.Settings is { ShowInDiscord: true, DiscordShows: DiscordShown.Solution, DiscordIdleMinutes: 12 },
            $"The Discord options chosen were not kept: {again.Settings.ShowInDiscord}, {again.Settings.DiscordShows}, {again.Settings.DiscordIdleMinutes}");
        again.Close();
    }

    /// <summary>The person's window as the app makes it, its settings kept in the test's state folder, looking for Discord
    /// at the stand-in's place, with the solution open.</summary>
    private MainWindow Started(TimeSpan? idle = null)
    {
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = settings, DiscordIpc = discord.Ipc, DiscordIdleAfter = idle };
        window.Show();
        window.KeepSettings();
        window.ShowActivityInDiscord();
        window.OpenFolder(solution);
        Ui.Settle();
        return window;
    }

    /// <summary>Turns Show activity in Discord on or off as a person does, in Settings.</summary>
    private static void SetShown(MainWindow window, bool on)
    {
        SettingsDialog dialog = window.ShowSettings();
        Ui.Settle();
        dialog.Page = SettingsDialog.DiscordPage;
        Ui.Settle();
        CheckBox box = Editor<CheckBox>(dialog, Show);
        if (box.IsChecked != on)
        {
            Click(dialog, box);
        }
        dialog.Ok();
        Ui.Settle();
    }

    /// <summary>Chooses what the activity shows, in Settings.</summary>
    private static void Choose(MainWindow window, string shown)
    {
        SettingsDialog dialog = window.ShowSettings();
        Ui.Settle();
        dialog.Page = SettingsDialog.DiscordPage;
        Ui.Settle();
        ComboBox shows = Editor<ComboBox>(dialog, "Show");
        shows.SelectedIndex = Array.IndexOf(SettingsDialog.DiscordChoices, shown);
        dialog.Ok();
        Ui.Settle();
    }

    /// <summary>The activity each SET_ACTIVITY gave, in order, null for a clear.</summary>
    private IReadOnlyList<JsonObject?> Activities() =>
        [.. discord.Frames.Where(f => f.Op == 1 && (string?)f.Json["cmd"] == "SET_ACTIVITY").Select(f => f.Json["args"]?["activity"] as JsonObject)];

    /// <summary>Waits for the newest activity on a connection to be as wanted, and gives it.</summary>
    private JsonObject WaitForActivity(Func<JsonObject?, bool> wanted, string what, int connection = 1)
    {
        JsonObject? newest = null;
        Ui.WaitFor(() =>
        {
            Frame? last = discord.Frames.LastOrDefault(f => f.Connection >= connection && f.Op == 1 && (string?)f.Json["cmd"] == "SET_ACTIVITY");
            newest = last?.Json["args"]?["activity"] as JsonObject;
            return last is not null && wanted(newest);
        }, $"{what}; Discord was last sent {Activities().LastOrDefault()?.ToJsonString() ?? "nothing"}", TimeSpan.FromSeconds(15));
        return newest!;
    }

    /// <summary>The log's lines about this test's Discord.</summary>
    private string[] Discords() =>
        [.. Ui.IdeLog().Split('\n').Where(l => l.Contains("Discord: ", StringComparison.Ordinal) && l.Contains(discord.Base, StringComparison.Ordinal))];

    /// <summary>Lets the window run a while, its timers and Passenger's looks for Discord with it.</summary>
    private static void Wait(TimeSpan time)
    {
        Stopwatch waiting = Stopwatch.StartNew();
        Ui.PumpUntil(() => waiting.Elapsed > time, $"{time.TotalSeconds} s to pass");
    }

    /// <summary>What edits an option, beside its label in the page shown.</summary>
    private static T Editor<T>(SettingsDialog dialog, string label) where T : Control =>
        ((Visual)dialog.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == label).Parent!).GetVisualDescendants().OfType<T>().First();

    private static void Click(Window window, Control control)
    {
        Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
    }

    /// <summary>A frame Discord was sent, on the connection numbered from 1.</summary>
    private sealed record Frame(int Connection, int Op, JsonObject Json);

    /// <summary>The Discord app as Passenger finds it, in a test's own place: a Unix socket at <c>discord-ipc-0</c> in the
    /// test's folder, or a pipe of the test's own name on Windows. It answers the handshake with READY and a command with
    /// its answer, as Discord does (or, told not to, never answers), and keeps every frame it is sent.</summary>
    private sealed class DiscordStandIn(string folder) : IDisposable
    {
        private readonly List<Frame> frames = [];
        private readonly List<Stream> clients = [];
        private CancellationTokenSource? listening;
        private Socket? listener;
        private bool answers;
        private int connections;
        private int ended;

        public string Base { get; } = OperatingSystem.IsWindows()
            ? $"passenger-test-{Guid.NewGuid().ToString("N")[..12]}-discord-ipc"
            : Path.Combine(folder, "discord-ipc");

        /// <summary>Where the test's window looks for it, trying again every 200 ms.</summary>
        public DiscordIpc Ipc => new([Base])
        {
            Retry = TimeSpan.FromMilliseconds(200),
            Pause = TimeSpan.FromMilliseconds(100),
            Timeout = TimeSpan.FromSeconds(1),
        };

        public int Connections => Volatile.Read(ref connections);

        /// <summary>Connections that have ended, whichever end closed them.</summary>
        public int Ended => Volatile.Read(ref ended);

        public IReadOnlyList<Frame> Frames
        {
            get
            {
                lock (frames)
                {
                    return [.. frames];
                }
            }
        }

        /// <summary>Discord starts.</summary>
        public void Start(bool answers = true)
        {
            this.answers = answers;
            CancellationTokenSource cancel = new();
            listening = cancel;
            if (OperatingSystem.IsWindows())
            {
                _ = Task.Run(() => AcceptPipesAsync(cancel.Token));
                return;
            }
            string path = Base + "-0";
            File.Delete(path);
            Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(path));
            socket.Listen(4);
            listener = socket;
            _ = Task.Run(() => AcceptSocketsAsync(socket, cancel.Token));
        }

        /// <summary>Discord quits: its socket goes and every connection ends.</summary>
        public void Stop()
        {
            listening?.Cancel();
            listening = null;
            listener?.Dispose();
            listener = null;
            if (!OperatingSystem.IsWindows())
            {
                File.Delete(Base + "-0");
            }
            Stream[] open;
            lock (frames)
            {
                open = [.. clients];
                clients.Clear();
            }
            foreach (Stream client in open)
            {
                client.Dispose();
            }
        }

        public void Dispose() => Stop();

        private async Task AcceptSocketsAsync(Socket socket, CancellationToken cancel)
        {
            try
            {
                while (true)
                {
                    Socket client = await socket.AcceptAsync(cancel);
                    _ = Task.Run(() => ServeAsync(new NetworkStream(client, ownsSocket: true), cancel));
                }
            }
            catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // Stopped.
            }
        }

        /// <summary>Discord's pipe on Windows: a server instance waiting for each next connection.</summary>
        private async Task AcceptPipesAsync(CancellationToken cancel)
        {
            try
            {
                while (true)
                {
                    NamedPipeServerStream pipe = new(Base + "-0", PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    try
                    {
                        await pipe.WaitForConnectionAsync(cancel);
                    }
                    catch
                    {
                        await pipe.DisposeAsync();
                        throw;
                    }
                    _ = Task.Run(() => ServeAsync(pipe, cancel));
                }
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Stopped.
            }
        }

        private async Task ServeAsync(Stream client, CancellationToken cancel)
        {
            int number = Interlocked.Increment(ref connections);
            lock (frames)
            {
                clients.Add(client);
            }
            byte[] head = new byte[8];
            try
            {
                while (true)
                {
                    await client.ReadExactlyAsync(head, cancel);
                    int op = BinaryPrimitives.ReadInt32LittleEndian(head);
                    byte[] body = new byte[BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4))];
                    await client.ReadExactlyAsync(body, cancel);
                    JsonObject json = JsonNode.Parse(body)!.AsObject();
                    lock (frames)
                    {
                        frames.Add(new Frame(number, op, json));
                    }
                    if (!answers)
                    {
                        continue;
                    }
                    if (op == 0)
                    {
                        await SendAsync(client, new JsonObject
                        {
                            ["cmd"] = "DISPATCH",
                            ["evt"] = "READY",
                            ["data"] = new JsonObject { ["v"] = 1, ["user"] = new JsonObject { ["id"] = "1", ["username"] = "stand-in" } },
                            ["nonce"] = null,
                        }, cancel);
                    }
                    else if (op == 1)
                    {
                        await SendAsync(client, new JsonObject { ["cmd"] = (string?)json["cmd"], ["evt"] = null, ["data"] = null, ["nonce"] = (string?)json["nonce"] }, cancel);
                    }
                }
            }
            catch (Exception error) when (error is EndOfStreamException or IOException or OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // The connection ended, from either end.
            }
            finally
            {
                Interlocked.Increment(ref ended);
                lock (frames)
                {
                    clients.Remove(client);
                }
                await client.DisposeAsync();
            }
        }

        private static async Task SendAsync(Stream client, JsonObject answer, CancellationToken cancel)
        {
            byte[] json = Encoding.UTF8.GetBytes(answer.ToJsonString());
            byte[] frame = new byte[8 + json.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame, 1);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), json.Length);
            json.CopyTo(frame, 8);
            await client.WriteAsync(frame, cancel);
            await client.FlushAsync(cancel);
        }
    }
}
