using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Passenger.App;
using Passenger.App.Vcs;
using Passenger.App.Welcome;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>Rider's recent solutions and Welcome screen, in the run's own state folder: the window a person uses keeps
/// each folder it opens, and a start that names nothing reopens the last unless Settings says not to. The Welcome screen
/// lists them newest first, narrowed as a search is typed, opened by Up, Down and Enter, removed for good by Delete; one
/// gone from disk is marked and refused with why. Clone Repository clones and opens, says why it could not, and stops
/// when canceled; File, Recent Solutions opens one with its recent files.</summary>
public sealed class WelcomeTests : IDisposable
{
    private readonly string settings = TestFolders.Create("settings-");
    private readonly string alpha = TestFolders.Create("alpha-");
    private readonly string beta = TestFolders.Create("beta-");

    public void Dispose()
    {
        TestFolders.Delete(settings);
        TestFolders.Delete(alpha);
        TestFolders.Delete(beta);
    }

    [AvaloniaFact]
    public void A_start_that_names_nothing_reopens_the_folder_opened_last_as_it_was_left()
    {
        using IDisposable theme = TestsTheme();
        File.WriteAllText(Path.Combine(alpha, "One.cs"), "class One { }\n");
        File.WriteAllText(Path.Combine(beta, "Two.cs"), "class Two { }\n");
        MainWindow first = Started(LaunchOptions.Empty);
        Assert.True(first.Folder is null && Shown(first), "A first start with nothing to reopen did not show the Welcome screen");
        first.OpenFolder(alpha);
        first.OpenFile("One.cs");
        first.OpenFolder(beta);
        first.OpenFile("Two.cs");
        Ui.Settle();
        first.Close();

        MainWindow second = Started(LaunchOptions.Empty);
        Assert.True(second.Folder == PathNames.Canonical(beta), $"The start reopened {second.Folder ?? "nothing"} rather than {beta}, opened last");
        Assert.Equal("Two.cs", second.EditorArea.Active?.Name);
        Assert.False(Shown(second), "The Welcome screen shows over the folder reopened");

        // A folder named on the command line is opened in its place.
        second.Close();
        MainWindow third = Started(LaunchOptions.Empty with { Folder = alpha });
        Assert.Equal(PathNames.Canonical(alpha), third.Folder);
        third.Close();
    }

    [AvaloniaFact]
    public void With_reopening_off_the_Welcome_screen_lists_the_recent_ones_newest_first_narrowed_by_a_search_and_opens_one_by_its_keys()
    {
        using IDisposable theme = TestsTheme();
        MainWindow first = Started(LaunchOptions.Empty);
        first.OpenFolder(alpha);
        first.OpenFolder(beta);
        first.ApplySettings(first.Settings with { ReopenProjectsOnStartup = false });
        first.Close();

        MainWindow window = Started(LaunchOptions.Empty);
        Assert.True(window.Folder is null, $"Reopening is off, yet the start opened {window.Folder}");
        Assert.True(Shown(window), "Nothing is open, and the Welcome screen is not shown");
        Assert.Equal([Name(beta), Name(alpha)], window.Welcome.Rows.Select(r => r.Name));
        Assert.Equal([RecentRow.Home(PathNames.Canonical(beta)), RecentRow.Home(PathNames.Canonical(alpha))], window.Welcome.Rows.Select(r => r.Shown));

        // The search has the keys as the screen opens.
        Type(window, "alpha");
        Assert.Equal([Name(alpha)], window.Welcome.Rows.Select(r => r.Name));
        Ui.Press(window, "ESCAPE");
        Assert.Equal(2, window.Welcome.Rows.Count);
        Ui.Press(window, "UP");
        Assert.Equal(Name(beta), window.Welcome.Selected?.Name);
        Ui.Press(window, "DOWN");
        Ui.Press(window, "ENTER");
        Assert.True(window.Folder == PathNames.Canonical(alpha), $"Down and Enter on {Name(alpha)} opened {window.Folder ?? "nothing"}");
        window.Close();
    }

    [AvaloniaFact]
    public void One_removed_from_the_Welcome_screen_is_gone_for_good_and_one_deleted_on_disk_is_marked_and_says_so_when_opened()
    {
        using IDisposable theme = TestsTheme();
        string gamma = TestFolders.Create("gamma-");
        MainWindow first = Started(LaunchOptions.Empty);
        first.OpenFolder(alpha);
        first.OpenFolder(beta);
        first.ApplySettings(first.Settings with { ReopenProjectsOnStartup = false });
        first.Close();

        // Another window opened on the same state before the removal, which it writes over after it.
        MainWindow window = Started(LaunchOptions.Empty);
        MainWindow other = Started(LaunchOptions.Empty);
        RecentRow removed = window.Welcome.Rows.Single(r => r.Name == Name(alpha));
        Click(window, window.Welcome.RowControl(removed)!);
        Ui.Press(window, "DELETE");
        Assert.Equal([Name(beta)], window.Welcome.Rows.Select(r => r.Name));
        window.Close();
        other.OpenFolder(gamma);
        other.Close();

        MainWindow next = Started(LaunchOptions.Empty);
        Assert.True(next.Welcome.Rows.Select(r => r.Name).SequenceEqual([Name(gamma), Name(beta)]),
            $"{Name(alpha)} was removed, yet the next start lists [{string.Join(", ", next.Welcome.Rows.Select(r => r.Name))}]");
        next.Close();

        string gone = PathNames.Canonical(beta);
        TestFolders.Delete(beta);
        MainWindow last = Started(LaunchOptions.Empty);
        RecentRow missing = last.Welcome.Rows.Single(r => r.Path == gone);
        Assert.True(missing.Missing, $"{gone} is gone from disk, and the Welcome screen does not mark it");
        Assert.False(last.Welcome.Rows.Single(r => r.Name == Name(gamma)).Missing);
        Ui.Press(last, "DOWN");
        Ui.Press(last, "ENTER");
        Assert.Null(last.Folder);
        Assert.Contains($"{RecentRow.Home(gone)} no longer exists", last.Welcome.Notice);
        Assert.Contains("no longer exists", last.Status);
        last.Close();
    }

    [AvaloniaFact]
    public void File_Recent_Solutions_lists_the_others_and_opens_the_one_chosen_with_its_recent_files()
    {
        using IDisposable theme = TestsTheme();
        File.WriteAllText(Path.Combine(alpha, "One.cs"), "class One { }\n");
        File.WriteAllText(Path.Combine(alpha, "Three.cs"), "class Three { }\n");
        MainWindow window = Started(LaunchOptions.Empty);
        window.OpenFolder(alpha);
        window.OpenFile("One.cs");
        window.OpenFile("Three.cs");
        window.OpenFolder(beta);
        Ui.Settle();

        NativeMenu recent = Ui.Submenu(Ui.Submenu(window.MenuBar, "File"), MainMenu.RecentSolutions);
        NativeMenuItem[] items = [.. recent.Items.OfType<NativeMenuItem>()];
        Assert.Equal([$"{Name(alpha)} – {RecentRow.Home(PathNames.Canonical(alpha))}"], items.Select(i => i.Header));
        items[0].Command!.Execute(null);
        Ui.Settle();
        Assert.Equal(PathNames.Canonical(alpha), window.Folder);

        // The folder's own recent files came back with it, the one shown first.
        Passenger.App.Navigation.SymbolPopup files = window.ShowRecentFiles();
        Ui.WaitFor(() => files.Rows.Count > 0, "the recent files");
        Assert.Equal("Three.cs", window.EditorArea.Active?.Name);
        Assert.Equal(["One.cs"], files.Rows.Select(r => r.Name));
        window.Close();
    }

    [AvaloniaFact]
    public void Clone_Repository_clones_into_the_folder_chosen_and_opens_the_clone()
    {
        using IDisposable theme = TestsTheme();
        string source = TestFolders.Create("source-");
        File.WriteAllText(Path.Combine(source, "Readme.md"), "# Shared\n");
        TestGit.Init(source);
        string remotes = TestFolders.Create("remotes-");
        string bare = Path.Combine(remotes, "shared.git");
        TestGit.Run(remotes, "clone", "--bare", "-q", source, bare);
        string clones = TestFolders.Create("clones-");
        MainWindow window = Started(LaunchOptions.Empty);

        Assert.True(window.Perform(AppActions.CloneRepository));
        Ui.Settle();
        CloneDialog dialog = Assert.Single(window.OwnedWindows.OfType<CloneDialog>());
        dialog.Folder = clones;
        // The URL has the keys as the dialog opens, and Enter clones.
        Type(dialog, bare);
        Assert.Equal(Path.Combine(clones, "shared"), dialog.Destination);
        Ui.Press(dialog, "ENTER");
        Ui.Settle();

        string cloned = PathNames.Canonical(Path.Combine(clones, "shared"));
        Assert.True(dialog.Problem is null, $"The clone failed: {dialog.Problem}");
        Assert.False(dialog.IsVisible, "The dialog stayed open over the clone made");
        Assert.True(window.Folder == cloned, $"The clone was made, but the window opened {window.Folder ?? "nothing"} rather than it");
        Assert.True(File.Exists(Path.Combine(cloned, "Readme.md")), "The clone does not hold the repository's file");
        Ui.WaitFor(() => window.Vcs.Repository is not null, "the clone's repository");
        Assert.Equal(cloned, window.RecentSolutions[0].Path);
        window.Close();
    }

    [AvaloniaFact]
    public void Clone_Repository_says_why_a_URL_could_not_be_cloned_and_leaves_nothing_behind()
    {
        using IDisposable theme = TestsTheme();
        string clones = TestFolders.Create("clones-");
        MainWindow window = Started(LaunchOptions.Empty);
        CloneDialog dialog = window.ShowCloneRepository();
        Ui.Settle();
        dialog.Folder = clones;

        Ui.Press(dialog, "ENTER");
        Assert.Equal("A repository URL is needed", dialog.Problem);

        string missing = Path.Combine(clones, "missing.git");
        Type(dialog, missing);
        Ui.Press(dialog, "ENTER");
        Ui.Settle();
        Assert.True(dialog.Problem?.Contains("does not exist", StringComparison.Ordinal) == true,
            $"Cloning {missing}, which is not there, was to say it does not exist; it says: {dialog.Problem ?? "nothing"}");
        Assert.True(dialog.IsVisible, "The dialog closed on a clone that failed, taking the URL to correct with it");
        Assert.Null(window.Folder);
        Assert.Empty(Directory.EnumerateFileSystemEntries(clones));
        dialog.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void Cancel_stops_a_clone_under_way_and_removes_what_it_made()
    {
        using IDisposable theme = TestsTheme();
        string clones = TestFolders.Create("clones-");
        // A server that takes git's request and never answers it, as one far away and stuck would.
        using TcpListener server = new(IPAddress.Loopback, 0);
        server.Start();
        List<TcpClient> asked = [];
        Task<TcpClient> accepted = server.AcceptTcpClientAsync(TestContext.Current.CancellationToken).AsTask();
        int? git = null;
        EventHandler<ChildStart> started = (_, child) => git = child.Command.Contains(" clone --progress ", StringComparison.Ordinal) ? child.ProcessId : git;
        ChildProcesses.Started += started;
        MainWindow window = Started(LaunchOptions.Empty);
        try
        {
            CloneDialog dialog = window.ShowCloneRepository();
            Ui.Settle();
            dialog.Folder = clones;
            Type(dialog, $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/stuck.git");
            // Pressed without waiting for the clone, which waits for the server.
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            dialog.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Ui.PumpUntil(() => accepted.IsCompleted, "git to ask the server for the repository");
            asked.Add(accepted.Result);
            Assert.True(dialog.IsCloning, "git asked the server, but the dialog does not say it is cloning");
            Assert.True(Directory.Exists(Path.Combine(clones, "stuck")), "git asked the server before making the clone's folder");
            int[] running = [git!.Value, .. Processes.Descendants(git.Value)];

            // Escape is Cancel; the dialog it closes takes no key's release.
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Ui.Settle();
            Assert.False(dialog.IsVisible, "Cancel left the dialog open");
            Assert.Null(window.Folder);
            Assert.All(running, id => Assert.True(Processes.Gone(id), $"Cancel left git running: {Processes.Describe(id)}"));
            Assert.False(Directory.Exists(Path.Combine(clones, "stuck")), "Cancel left the folder the clone had begun");
        }
        finally
        {
            ChildProcesses.Started -= started;
            foreach (TcpClient client in asked)
            {
                client.Dispose();
            }
            window.Close();
        }
    }

    /// <summary>The window a person starts, as the app makes it: its layout and settings kept in the test's state folder,
    /// then the command line taken.</summary>
    private MainWindow Started(LaunchOptions options)
    {
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = settings };
        window.Show();
        window.KeepLayout();
        window.KeepSettings();
        window.Start(options);
        Ui.Settle();
        return window;
    }

    private static string Name(string folder) => Path.GetFileName(folder);

    /// <summary>Whether the window shows its Welcome screen: in it, and not hidden.</summary>
    private static bool Shown(MainWindow window) => TopLevel.GetTopLevel(window.Welcome) == window && window.Welcome.IsEffectivelyVisible;

    /// <summary>Text typed where the keys are.</summary>
    private static void Type(TopLevel window, string text)
    {
        window.KeyTextInput(text);
        Ui.Settle();
    }

    private static void Click(TopLevel window, Control control)
    {
        Point at = control.TranslatePoint(new Point(12, 12), window) ?? throw new InvalidOperationException("The control is not in the window");
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
    }

    /// <summary>Puts the tests' theme back once a window that keeps its settings has put the person's in force.</summary>
    private static IDisposable TestsTheme() => new ThemeBack(Avalonia.Application.Current!.RequestedThemeVariant);

    private sealed class ThemeBack(Avalonia.Styling.ThemeVariant? theme) : IDisposable
    {
        public void Dispose() => Avalonia.Application.Current!.RequestedThemeVariant = theme;
    }
}
