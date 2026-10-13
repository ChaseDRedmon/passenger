using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Passenger.App.Packages;
using Passenger.App.Updates;
using Passenger.Editor;

namespace Passenger.App;

/// <summary>Rider's check for updates, until Passenger installs them: the person's window asks for the latest release
/// as it starts and once a day while Settings' Check for updates is on, and says in the status bar, once per version,
/// that a newer one is out, its link opening this system's download; Help, Check for Updates (the application menu's on
/// macOS, as Rider's) asks whenever a person wants. Every answer is logged; only one a person asked for is said.</summary>
public partial class MainWindow
{
    /// <summary>Rider's words when there is nothing newer.</summary>
    public const string UpToDate = "Passenger is up to date";

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromDays(1);

    /// <summary>Ends a check under way as the window closes, so a feed that never answers holds nothing after it.</summary>
    private readonly CancellationTokenSource updatesEnd = new();
    private DispatcherTimer? updateTimer;
    private bool watchesUpdates;

    /// <summary>The release announced, for a window that keeps no state; the person's keeps it in the store.</summary>
    private string? announcedHere;

    /// <summary>Where this window asks for the latest release: GitHub's, or the one PASSENGER_RELEASES names; a test
    /// names a stand-in of its own, with a timeout it can wait out.</summary>
    public ReleaseFeed Releases { get; init; } = ReleaseFeed.Default;

    /// <summary>The newer release the status bar shows, its version and what its link opens, or null while it shows
    /// none.</summary>
    public LatestRelease? UpdateShown { get; private set; }

    private void AddUpdates()
    {
        actions!.Add(new IdeAction(AppActions.CheckForUpdates, "Check for Updates...", "Help",
            context => _ = CheckForUpdatesAsync(asked: true))
        {
            Menus = OperatingSystem.IsMacOS() ? [] : [new MenuPlace("Help", 3, 0)],
        });
        Closed += (_, _) =>
        {
            updateTimer?.Stop();
            updatesEnd.Cancel();
        };
    }

    /// <summary>The person's window asks for the latest release now and once a day after, while its setting is on; a
    /// headless window (a test's, an agent's, a screenshot's) asks only when Check for Updates is chosen.</summary>
    public void WatchForUpdates()
    {
        watchesUpdates = true;
        WatchUpdates(settings.CheckForUpdates);
    }

    /// <summary>Starts the checks, the first now, as the setting comes on, and stops them as it goes off.</summary>
    private void WatchUpdates(bool on)
    {
        if (!watchesUpdates || on == (updateTimer is not null))
        {
            return;
        }
        if (!on)
        {
            updateTimer!.Stop();
            updateTimer = null;
            return;
        }
        updateTimer = new DispatcherTimer { Interval = UpdateInterval };
        updateTimer.Tick += (_, _) => _ = CheckForUpdatesAsync(asked: false);
        updateTimer.Start();
        _ = CheckForUpdatesAsync(asked: false);
    }

    /// <summary>Asks for the latest release, off the UI thread. A newer one is shown when a person asked or it is newer
    /// than the one announced last; a person who asked is told the rest too, while a check of the window's own only logs
    /// it.</summary>
    private async Task CheckForUpdatesAsync(bool asked)
    {
        if (asked)
        {
            StatusText.Text = "Checking for updates...";
        }
        ReleaseFeed feed = Releases;
        CancellationToken end = updatesEnd.Token;
        ReleaseAnswer answer = await BackgroundWork.Run(() => Updates.Releases.AskAsync(feed, Product.Version, end));
        if (end.IsCancellationRequested)
        {
            return;
        }
        IdeErrors.Write($"Updates: {answer.Said}");
        switch (answer)
        {
            case ReleaseAnswer.Newer { Release: var release }:
                bool told = Announced is { } before && VersionOrder.Instance.Compare(release.Version, before) <= 0;
                if (asked || !told)
                {
                    ShowUpdate(release);
                }
                if (!told)
                {
                    Announced = release.Version;
                }
                if (asked)
                {
                    StatusText.Text = $"Passenger {release.Version} is available";
                }
                break;
            case ReleaseAnswer.Failed { Reason: var reason } when asked:
                StatusText.Text = $"Could not check for updates: {reason}";
                break;
            case ReleaseAnswer.Current when asked:
                StatusText.Text = UpToDate;
                break;
        }
    }

    /// <summary>The newest version announced: the store's in the window a person uses, so a restart does not say it
    /// again.</summary>
    private string? Announced
    {
        get => keepsSettings ? state!.Updates.Value.Announced : announcedHere;
        set
        {
            announcedHere = value;
            if (keepsSettings)
            {
                state!.Updates.Set(state.Updates.Value with { Announced = value });
            }
        }
    }

    private void ShowUpdate(LatestRelease release)
    {
        UpdateShown = release;
        UpdateText.Text = $"Passenger {release.Version} available";
        string opens = release.Asset is { } asset ? $"Download {asset}" : $"Open the release page of {release.Tag}";
        ToolTip.SetTip(UpdateButton, $"{opens}\n{release.Link}");
        UpdateButton.IsVisible = true;
    }

    /// <summary>Opens the link in the person's browser through the system's own launcher, as a link anywhere opens: the
    /// browser is the person's, never ended with the IDE (and a test's headless platform opens nothing).</summary>
    private void OnUpdateClicked(object? sender, RoutedEventArgs e)
    {
        if (UpdateShown is { } shown)
        {
            _ = Launcher.LaunchUriAsync(shown.Link);
        }
    }
}
