using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Settings;
using Passenger.App.Updates;

namespace Passenger.Tests;

/// <summary>Passenger says when a newer release is out, as Rider does: the person's window asks for the latest release as it
/// starts, says once per version in the status bar that a newer one is out with a link to this system's download, and
/// says nothing of the same version, an older one, a pre-release or a draft, nor of a failure, which only the log has;
/// Help, Check for Updates asks whenever a person wants and says so when Passenger is up to date; the setting off, nothing
/// is asked. A stand-in on a loopback port answers for GitHub, which no test reaches.</summary>
public sealed class UpdatesTests : IDisposable
{
    /// <summary>The downloads #56's release workflow publishes with every release, by fixed names.</summary>
    private static readonly string[] Downloads =
    [
        "Passenger-macos-arm64.dmg", "Passenger-macos-x64.dmg", "Passenger-windows-x64.exe", "Passenger-windows-arm64.exe",
        "Passenger-linux-x64.tar.gz", "Passenger-linux-arm64.tar.gz",
    ];

    private readonly string settings = TestFolders.Create("updates-");
    private readonly StandIn github = new();

    public void Dispose()
    {
        github.Dispose();
        TestFolders.Delete(settings);
    }

    [AvaloniaFact]
    public void A_newer_release_is_announced_once_naming_its_version_with_a_link_to_this_systems_download()
    {
        using IDisposable theme = TestsTheme();
        string newer = Bumped(1, 0);
        github.Answer = Release($"v{newer}");
        MainWindow window = Started();
        string? before = window.Status;
        window.WatchForUpdates();
        Ui.Settle();
        Assert.True(github.Requests.Count == 1, $"The window asked for the latest release {github.Requests.Count} times as it started, not once");

        // The status bar names the version, and its link is this system's download from the release.
        Assert.True(Notice(window) == $"Passenger {newer} available", $"A newer release, v{newer}, is out, but the status bar's notice is '{Notice(window)}'");
        string download = $"https://github.com/Zaneris/passenger/releases/download/v{newer}/{ThisSystemsDownload()}";
        Assert.True(window.UpdateShown?.Link.AbsoluteUri == download,
            $"The notice's link opens {window.UpdateShown?.Link}, not this system's download from the release, {download}");
        Assert.Contains(download, ToolTip.GetTip(window.FindControl<Button>("UpdateButton")!) as string ?? string.Empty);
        Assert.True(window.Status == before, $"A check of the window's own said '{window.Status}' in the status bar's message, where it says nothing");
        Assert.Contains(Ui.IdeLog().Split('\n'), line => line.Contains($"Updates: Passenger {newer} is out (v{newer}; this is {Product.Version}): {download}", StringComparison.Ordinal));

        // Nothing is sent but the request: a GET of the release, naming Passenger and its version, and the API's type.
        string[] head = github.Requests[0].Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal($"GET {github.Latest.PathAndQuery} HTTP/1.1", head[0]);
        Assert.Contains($"User-Agent: Passenger/{Product.Version}", head);
        string[] headers = [.. head.Skip(1).Select(h => h.Split(':')[0]).Order(StringComparer.OrdinalIgnoreCase)];
        Assert.True(headers.SequenceEqual(["Accept", "Host", "User-Agent"], StringComparer.OrdinalIgnoreCase),
            $"The request sent more than the request itself: {string.Join(", ", headers)}");
        window.Close();

        // Started again while it is still the latest, Passenger asks again and says nothing.
        window = Started();
        window.WatchForUpdates();
        Ui.Settle();
        Assert.True(github.Requests.Count == 2, "The window started again did not ask for the latest release");
        Assert.True(Notice(window) is null, $"v{newer} was announced already, yet the window started again announces it once more: '{Notice(window)}'");

        // Help, Check for Updates says it whenever a person asks.
        CheckForUpdates(window);
        Assert.True(Notice(window) == $"Passenger {newer} available", "Check for Updates did not show the newer release the window had announced before");
        Assert.Equal($"Passenger {newer} is available", window.Status);
        window.Close();

        // A later release is announced in its turn.
        string later = Bumped(1, 1);
        github.Answer = Release($"v{later}");
        window = Started();
        window.WatchForUpdates();
        Ui.Settle();
        Assert.True(Notice(window) == $"Passenger {later} available", $"v{later} came out after v{newer} was announced, but the window shows '{Notice(window)}'");
        window.Close();
    }

    [AvaloniaFact]
    public void The_same_version_an_older_one_a_pre_release_or_a_draft_says_nothing_and_Check_for_Updates_says_up_to_date()
    {
        using IDisposable theme = TestsTheme();
        github.Answer = Release($"v{Product.Version}");
        MainWindow window = Started();
        string? before = window.Status;
        window.WatchForUpdates();
        Ui.Settle();
        Assert.True(github.Requests.Count == 1, "The window did not ask for the latest release as it started");
        Assert.True(Notice(window) is null && window.Status == before,
            $"The latest release is the running version, yet the window shows '{Notice(window)}' and says '{window.Status}'");

        // Asked for, each of these is nothing newer: Rider's words.
        (string What, StandIn.Reply Answer)[] nothingNewer =
        [
            ("the running version", Release($"v{Product.Version}")),
            ("an older version", Release("v0.0.1")),
            ("a newer pre-release by its tag", Release($"v{Bumped(1, 0)}-beta.1")),
            ("a newer release GitHub marks a pre-release", Release($"v{Bumped(1, 0)}", prerelease: true)),
            ("a newer draft", Release($"v{Bumped(1, 0)}", draft: true)),
            ("no release at all", new StandIn.Reply(404, """{"message":"Not Found"}""")),
        ];
        foreach ((string what, StandIn.Reply answer) in nothingNewer)
        {
            github.Answer = answer;
            int asked = github.Requests.Count;
            CheckForUpdates(window);
            Assert.True(github.Requests.Count == asked + 1, $"Check for Updates did not ask for the latest release with {what} out");
            Assert.True(window.Status == MainWindow.UpToDate, $"With {what} out, Check for Updates says '{window.Status}', not '{MainWindow.UpToDate}'");
            Assert.True(Notice(window) is null, $"With {what} out, the status bar shows '{Notice(window)}'");
        }
        window.Close();
    }

    [AvaloniaFact]
    public void With_Check_for_updates_off_in_Settings_nothing_is_asked_until_it_is_on_again()
    {
        using IDisposable theme = TestsTheme();
        github.Answer = Release($"v{Bumped(1, 0)}");
        MainWindow window = Started();
        Assert.True(window.Settings.CheckForUpdates, "Check for updates is not on by default");

        // Where Rider has it: System Settings, Updates.
        SettingsDialog dialog = window.ShowSettings();
        Ui.Settle();
        dialog.Query = "updates";
        Ui.Settle();
        Assert.Equal(SettingsDialog.UpdatesPage, dialog.Page);
        Assert.Contains("Check for updates", dialog.Shown);
        dialog.Change(s => s with { CheckForUpdates = false });
        dialog.Ok();
        Ui.Settle();
        window.Close();

        window = Started();
        Assert.False(window.Settings.CheckForUpdates, "Check for updates turned off was on again in the next window");
        window.WatchForUpdates();
        Ui.Settle();
        Assert.True(github.Requests.Count == 0, $"With Check for updates off, the window asked for the latest release {github.Requests.Count} time(s)");
        Assert.True(Notice(window) is null, $"With Check for updates off, the status bar shows '{Notice(window)}'");

        // On again, it asks at once.
        window.ApplySettings(window.Settings with { CheckForUpdates = true });
        Ui.Settle();
        Assert.True(github.Requests.Count == 1, "Check for updates turned on again did not ask for the latest release");
        Assert.True(Notice(window) is not null, "Check for updates turned on again did not announce the newer release");
        window.Close();
    }

    [AvaloniaFact]
    public void A_failed_or_unanswered_check_says_nothing_and_is_logged_and_Check_for_Updates_says_why()
    {
        using IDisposable theme = TestsTheme();
        Assert.True(ReleaseFeed.Default.Latest.IsLoopback, $"A test's window would ask {ReleaseFeed.Default.Latest} for the latest release, not a loopback address");

        github.Answer = new StandIn.Reply(500, """{"message":"Server Error"}""");
        MainWindow window = Started();
        string? before = window.Status;
        window.WatchForUpdates();
        Ui.Settle();
        Assert.True(github.Requests.Count == 1, "The window did not ask for the latest release as it started");
        Assert.True(Notice(window) is null && window.Status == before, $"A check that failed shows '{Notice(window)}' and says '{window.Status}'");
        Assert.Contains(Ui.IdeLog().Split('\n'), line => line.Contains($"Updates: could not ask {github.Latest} for the latest release: it answered 500 Internal Server Error", StringComparison.Ordinal));
        window.Close();

        // A server that takes the request and never answers is given up on at the feed's timeout.
        github.Answer = null;
        window = Started();
        window.WatchForUpdates();
        Ui.Settle();
        Assert.True(github.Requests.Count == 2, "The window did not ask for the latest release as it started");
        Assert.True(Notice(window) is null && window.Status == before, $"A check never answered shows '{Notice(window)}' and says '{window.Status}'");
        Assert.Contains(Ui.IdeLog().Split('\n'), line => line.Contains($"Updates: could not ask {github.Latest} for the latest release: no answer in 1 s", StringComparison.Ordinal));

        // Asked for, the failure is said.
        CheckForUpdates(window);
        Assert.Equal("Could not check for updates: no answer in 1 s", window.Status);
        window.Close();
    }

    /// <summary>The person's window as the app makes it, its settings kept in the test's state folder, asking the stand-in.</summary>
    private MainWindow Started()
    {
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = settings, Releases = new ReleaseFeed(github.Latest, TimeSpan.FromSeconds(1)) };
        window.Show();
        window.KeepSettings();
        Ui.Settle();
        return window;
    }

    /// <summary>Help, Check for Updates as a person chooses it: the application menu's, after About, on macOS, as Rider has
    /// it there; elsewhere Help's, before About.</summary>
    private static void CheckForUpdates(MainWindow window)
    {
        NativeMenu help = Ui.Submenu(window.MenuBar, "Help");
        string[] helpItems = [.. help.Items.OfType<NativeMenuItem>().Select(i => i.Header ?? string.Empty)];
        NativeMenuItem item;
        if (OperatingSystem.IsMacOS())
        {
            Assert.DoesNotContain("Check for Updates...", helpItems);
            string[] application = [.. window.ApplicationMenu().Items.Select(i => (i as NativeMenuItem)?.Header ?? "-")];
            Assert.Equal(["About Passenger", "Check for Updates...", "-", "Settings..."], application);
            item = window.ApplicationMenu().Items.OfType<NativeMenuItem>().First(i => i.Header == "Check for Updates...");
        }
        else
        {
            Assert.Equal(["Check for Updates...", "About"], helpItems[^2..]);
            item = help.Items.OfType<NativeMenuItem>().First(i => i.Header == "Check for Updates...");
        }
        item.Command!.Execute(null);
        Ui.Settle();
    }

    /// <summary>The status bar's notice of a newer release, or null while it shows none.</summary>
    private static string? Notice(MainWindow window) =>
        window.FindControl<Button>("UpdateButton")!.IsVisible ? window.FindControl<TextBlock>("UpdateText")!.Text : null;

    /// <summary>This system's download by #56's fixed names: its system and its processor's.</summary>
    private static string ThisSystemsDownload()
    {
        string system = OperatingSystem.IsMacOS() ? "macos-{0}.dmg" : OperatingSystem.IsWindows() ? "windows-{0}.exe" : "linux-{0}.tar.gz";
        string named = "Passenger-" + string.Format(System.Globalization.CultureInfo.InvariantCulture, system, RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
        Assert.Contains(named, Downloads);
        return named;
    }

    /// <summary>A version newer than the running one: its major version and minor ones raised.</summary>
    private static string Bumped(int major, int minor)
    {
        int[] parts = [.. Product.Version.Split('-')[0].Split('.').Select(p => int.Parse(p, System.Globalization.CultureInfo.InvariantCulture))];
        return $"{parts[0] + major}.{minor}.0";
    }

    /// <summary>A release as GitHub's API gives it, with every download #56 publishes.</summary>
    private static StandIn.Reply Release(string tag, bool prerelease = false, bool draft = false)
    {
        JsonArray assets = [];
        foreach (string name in Downloads)
        {
            assets.Add(new JsonObject
            {
                ["name"] = name,
                ["browser_download_url"] = $"https://github.com/Zaneris/passenger/releases/download/{tag}/{name}",
                ["size"] = 1000,
            });
        }
        JsonObject release = new()
        {
            ["tag_name"] = tag,
            ["name"] = $"Passenger {tag}",
            ["html_url"] = $"https://github.com/Zaneris/passenger/releases/tag/{tag}",
            ["draft"] = draft,
            ["prerelease"] = prerelease,
            ["assets"] = assets,
        };
        return new StandIn.Reply(200, release.ToJsonString());
    }

    private static IDisposable TestsTheme() => new ThemeBack(Avalonia.Application.Current!.RequestedThemeVariant);

    private sealed class ThemeBack(Avalonia.Styling.ThemeVariant? theme) : IDisposable
    {
        public void Dispose() => Avalonia.Application.Current!.RequestedThemeVariant = theme;
    }

    /// <summary>GitHub's releases/latest on a loopback port of its own: each request's head kept as it came, and answered
    /// as the test says, or taken and never answered until the client gives up.</summary>
    private sealed class StandIn : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource end = new();
        private readonly List<string> heads = [];
        private readonly Task accepting;
        private Reply? answer;

        public StandIn()
        {
            listener.Start();
            Latest = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/repos/Zaneris/passenger/releases/latest");
            accepting = Task.Run(AcceptAsync);
        }

        public sealed record Reply(int Status, string Body);

        public Uri Latest { get; }

        /// <summary>The answer to each request from now on; null takes it and never answers.</summary>
        public Reply? Answer
        {
            get => Volatile.Read(ref answer);
            set => Volatile.Write(ref answer, value);
        }

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (heads)
                {
                    return [.. heads];
                }
            }
        }

        public void Dispose()
        {
            end.Cancel();
            listener.Stop();
            accepting.Wait(TimeSpan.FromSeconds(10));
            end.Dispose();
        }

        private async Task AcceptAsync()
        {
            List<Task> answering = [];
            try
            {
                while (true)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync(end.Token);
                    answering.Add(Task.Run(() => AnswerAsync(client)));
                }
            }
            catch (Exception stopped) when (stopped is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // Disposed.
            }
            await Task.WhenAll(answering);
        }

        private async Task AnswerAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    byte[] buffer = new byte[16 * 1024];
                    int read = 0;
                    while (Encoding.ASCII.GetString(buffer, 0, read).IndexOf("\r\n\r\n", StringComparison.Ordinal) < 0)
                    {
                        int more = await stream.ReadAsync(buffer.AsMemory(read), end.Token);
                        if (more == 0)
                        {
                            return;
                        }
                        read += more;
                    }
                    lock (heads)
                    {
                        heads.Add(Encoding.ASCII.GetString(buffer, 0, read));
                    }
                    if (Answer is not { } reply)
                    {
                        // Held until the client gives up and closes, or the stand-in goes.
                        while (await stream.ReadAsync(buffer, end.Token) > 0)
                        {
                        }
                        return;
                    }
                    byte[] body = Encoding.UTF8.GetBytes(reply.Body);
                    string reason = reply.Status switch { 200 => "OK", 404 => "Not Found", _ => "Internal Server Error" };
                    byte[] response = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 {reply.Status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(response, end.Token);
                    await stream.WriteAsync(body, end.Token);
                }
                catch (Exception gone) when (gone is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
                {
                    // The client or the stand-in went first.
                }
            }
        }
    }
}
