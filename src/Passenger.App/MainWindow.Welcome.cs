using Passenger.App.Welcome;
using Passenger.Core;

namespace Passenger.App;

/// <summary>The recent folders and solutions: kept in the store's recentSolutions.json by the window a person uses as
/// each opens, listed by the Welcome screen (in the editor area while nothing is open) and File, Recent Solutions, and the
/// last reopened on a start that names nothing, as Rider's Reopen projects on startup. Clone Repository is beside them.</summary>
public partial class MainWindow
{
    private readonly WelcomeView welcome = new();

    /// <summary>The Welcome screen, which the editor area shows until a folder opens.</summary>
    public WelcomeView Welcome => welcome;

    private void AddWelcome()
    {
        Editors.ShowWelcome(welcome);
        welcome.OpenRequested += (_, _) => Request(AppActions.Open);
        welcome.CloneRequested += (_, _) => Request(AppActions.CloneRepository);
        welcome.Chosen += (_, row) => OpenRecent(row.Path);
        welcome.RemoveRequested += (_, row) => ForgetRecent(row.Path);
        actions!.Add(new IdeAction(AppActions.CloneRepository, "Clone Repository...", "Git", _ => ShowCloneRepository())
        {
            Menus = [new MenuPlace("File", 0, 1), new MenuPlace("Git", 4, 0)],
        });
    }

    /// <summary>The recent folders and solutions, the newest first, each marked when it is gone from disk; none in a
    /// window that keeps no state.</summary>
    public IReadOnlyList<RecentRow> RecentSolutions =>
        keepsWorkspace ? [.. state!.Recent.Value.Newest().Select(recent => RecentRow.Of(recent.Path, recent.Entry))] : [];

    /// <summary>Opens a recent folder or solution, as the Welcome screen and File, Recent Solutions do: null once it is
    /// open, else why not (it is gone), which the status bar and the Welcome screen say.</summary>
    public string? OpenRecent(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            string gone = $"{RecentRow.Home(path)} no longer exists: it was moved or deleted, or its drive is not connected";
            StatusText.Text = gone;
            welcome.Say(gone);
            ShowRecent();
            return gone;
        }
        welcome.Say(null);
        OpenFolder(path);
        return null;
    }

    /// <summary>Takes a folder or solution off the recent ones for good.</summary>
    public void ForgetRecent(string path)
    {
        if (keepsWorkspace)
        {
            state!.Recent.Set(state.Recent.Value.Without(path));
            welcome.Say(null);
            ShowRecent();
        }
    }

    /// <summary>Puts the workspace just opened at the top of the recent ones: its solution, or the folder without one, by
    /// the name the toolbar shows.</summary>
    private void Remember()
    {
        if (!keepsWorkspace || folder is null)
        {
            return;
        }
        string path = Solution?.Path ?? folder;
        state!.Recent.Set(state.Recent.Value.Opened(path, Solution?.Name ?? Path.GetFileName(folder), DateTimeOffset.UtcNow));
        ShowRecent();
    }

    private void ShowRecent() => welcome.Show(RecentSolutions);

    /// <summary>Started with nothing named, the window that keeps state opens what was opened last, unless the setting
    /// says not to; one gone from disk is left on the Welcome screen, marked, with why.</summary>
    private void ReopenLast()
    {
        if (!settings.ReopenProjectsOnStartup || RecentSolutions.FirstOrDefault() is not { } last)
        {
            return;
        }
        if (last.Missing)
        {
            string gone = $"{last.Name} was not reopened: {last.Shown} no longer exists";
            StatusText.Text = gone;
            welcome.Say(gone);
            return;
        }
        OpenFolder(last.Path);
    }

    /// <summary>Clone Repository: the dialog over this window, and the clone opened once it is made.</summary>
    public Vcs.CloneDialog ShowCloneRepository()
    {
        Vcs.CloneDialog dialog = new(CloneFolder());
        dialog.Closed += (_, _) =>
        {
            if (dialog.Cloned is { } cloned)
            {
                OpenFolder(cloned);
            }
        };
        _ = dialog.ShowDialog(this);
        return dialog;
    }

    /// <summary>Where a clone goes unless the person says otherwise: beside the folder opened last, else in the home
    /// folder.</summary>
    private string CloneFolder()
    {
        string? last = RecentSolutions.FirstOrDefault()?.Path;
        string? lastFolder = last is null ? null : File.Exists(last) ? Path.GetDirectoryName(Path.GetDirectoryName(last)) : Path.GetDirectoryName(last);
        return lastFolder is not null && Directory.Exists(lastFolder) ? lastFolder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>File, Recent Solutions: each but the one open, newest first, by name and where it is.</summary>
    private IReadOnlyList<(string Title, Action Open)> RecentMenu() =>
        [.. RecentSolutions.Where(r => folder is null || !(PathNames.Same(r.Path, folder) || PathNames.Same(r.Path, Solution?.Path ?? string.Empty)))
            .Select(r => ($"{r.Name} – {r.Shown}", (Action)(() => OpenRecent(r.Path))))];
}
