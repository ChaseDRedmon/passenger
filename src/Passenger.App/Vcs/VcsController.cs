using Avalonia.Threading;
using Passenger.App.Workspace;
using Passenger.Editor;

namespace Passenger.App.Vcs;

/// <summary>The open folder's Git working tree as the window shows it: the branch, the changed files, each open file's
/// change bars against the last commit, and committing, pushing, switching branches and rolling back, after which the
/// open files take what is on disk again.</summary>
public sealed class VcsController
{
    /// <summary>How long typing pauses before a file's change bars are worked out again.</summary>
    private static readonly TimeSpan MarksDelay = TimeSpan.FromMilliseconds(120);

    private readonly Dictionary<OpenDocument, Tracked> documents = [];
    private readonly Dictionary<string, Task<string?>> heads = [];
    private Git? git;
    private int opened;

    /// <summary>The working tree the open folder is in, or null when it is in none.</summary>
    public Git? Repository => git;

    /// <summary>The branch checked out, or null on a detached head or outside a working tree.</summary>
    public string? Branch { get; private set; }

    /// <summary>The changed and new files, as the last status read them.</summary>
    public IReadOnlyList<FileChange> Changes { get; private set; } = [];

    /// <summary>Raised when the branch or the changed files have been read again.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised after the whole tree was read again, as after a commit, a checkout or an update, when the
    /// history may have moved too (a save alone changes only the files).</summary>
    public event EventHandler? Refreshed;

    /// <summary>What happened, for the status bar: a commit made, a push refused and git's reason.</summary>
    public event EventHandler<string>? Message;

    /// <summary>Run before a commit, so what is committed is what the editor shows (Rider saves every file first).</summary>
    public Action? BeforeCommit { get; set; }

    /// <summary>Finds the folder's working tree and reads it; a folder in none leaves the window without Git.</summary>
    public async Task OpenAsync(string folder)
    {
        int generation = ++opened;
        Git? found = await Git.OpenAsync(folder);
        if (generation != opened)
        {
            return;
        }
        git = found;
        heads.Clear();
        await RefreshAsync();
    }

    /// <summary>Makes the folder a working tree (Rider's Create Git Repository).</summary>
    public async Task<bool> InitAsync(string folder)
    {
        GitResult init = await Git.InitAsync(folder);
        if (!init.Succeeded)
        {
            Message?.Invoke(this, init.Message);
            return false;
        }
        await OpenAsync(folder);
        return git is not null;
    }

    /// <summary>Reads the branch and the changed files again, and every open file's change bars.</summary>
    public async Task RefreshAsync()
    {
        if (git is not { } repository)
        {
            Branch = null;
            Changes = [];
            Changed?.Invoke(this, EventArgs.Empty);
            Refreshed?.Invoke(this, EventArgs.Empty);
            return;
        }
        heads.Clear();
        try
        {
            Branch = await repository.BranchAsync();
            Changes = await repository.StatusAsync();
        }
        catch (InvalidOperationException failed)
        {
            Message?.Invoke(this, failed.Message);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        Refreshed?.Invoke(this, EventArgs.Empty);
        foreach (OpenDocument document in documents.Keys)
        {
            _ = MarkAsync(document);
        }
    }

    /// <summary>Starts showing a file's change bars, worked out again as it is edited.</summary>
    public void Attach(OpenDocument document)
    {
        if (documents.ContainsKey(document))
        {
            return;
        }
        DispatcherTimer timer = new() { Interval = MarksDelay };
        Tracked tracked = new(timer);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = MarkAsync(document);
        };
        tracked.OnChanged = (_, _) =>
        {
            timer.Stop();
            timer.Start();
        };
        tracked.OnSaved = (_, _) => _ = RefreshStatusAsync();
        document.Text.Changed += tracked.OnChanged;
        document.Saved += tracked.OnSaved;
        documents[document] = tracked;
        _ = MarkAsync(document);
    }

    public void Detach(OpenDocument document)
    {
        if (documents.Remove(document, out Tracked? tracked))
        {
            tracked.Timer.Stop();
            document.Text.Changed -= tracked.OnChanged;
            document.Saved -= tracked.OnSaved;
        }
    }

    /// <summary>Commits the chosen files with a message, then pushes when asked, as Rider's Commit and Commit and
    /// Push do. Whether the commit was made.</summary>
    public async Task<bool> CommitAsync(IReadOnlyList<FileChange> chosen, string message, bool amend, bool push)
    {
        if (git is not { } repository)
        {
            return false;
        }
        if (chosen.Count == 0 && !amend)
        {
            Message?.Invoke(this, "No files are chosen to commit");
            return false;
        }
        if (message.Trim().Length == 0)
        {
            Message?.Invoke(this, "The commit message is empty");
            return false;
        }
        BeforeCommit?.Invoke();
        string[] paths = [.. chosen.SelectMany(c => c.From is { } from ? new[] { c.Path, from } : [c.Path])];
        GitResult commit = await repository.CommitAsync(paths, message, amend);
        if (!commit.Succeeded)
        {
            Message?.Invoke(this, commit.Message);
            await RefreshAsync();
            return false;
        }
        int count = chosen.Count;
        string committed = count == 1 ? "1 file committed" : $"{count} files committed";
        Message?.Invoke(this, $"{committed}: {FirstLine(message)}");
        await RefreshAsync();
        if (push)
        {
            await PushAsync();
        }
        return true;
    }

    public async Task<bool> PushAsync()
    {
        if (git is not { } repository)
        {
            return false;
        }
        Message?.Invoke(this, "Pushing...");
        GitResult pushed = await repository.PushAsync();
        Message?.Invoke(this, pushed.Succeeded ? $"Pushed {Branch}" : $"Push failed: {pushed.Message}");
        return pushed.Succeeded;
    }

    /// <summary>Rider's Update Project: the upstream's commits brought in, then the open files and Git read again.</summary>
    public async Task<bool> UpdateAsync()
    {
        if (git is not { } repository)
        {
            return false;
        }
        Message?.Invoke(this, "Updating...");
        GitResult pulled = await repository.PullAsync();
        ReloadOpenFiles();
        await RefreshAsync();
        Message?.Invoke(this, pulled.Succeeded ? Summary(pulled.Output, "Updated") : $"Update failed: {pulled.Message}");
        return pulled.Succeeded;
    }

    public async Task<bool> FetchAsync()
    {
        if (git is not { } repository)
        {
            return false;
        }
        Message?.Invoke(this, "Fetching...");
        GitResult fetched = await repository.FetchAsync();
        await RefreshAsync();
        Message?.Invoke(this, fetched.Succeeded ? "Fetched" : $"Fetch failed: {fetched.Message}");
        return fetched.Succeeded;
    }

    private static string Summary(string output, string fallback) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() is { Length: > 0 } last ? $"{fallback}: {last}" : fallback;

    /// <summary>The last commit's message, which Amend starts from.</summary>
    public async Task<string?> LastMessageAsync() => git is { } repository ? await repository.LastMessageAsync() : null;

    public async Task<bool> CheckoutAsync(string branch) =>
        (await SwitchAsync(repository => repository.CheckoutAsync(branch), $"Checked out {branch}")).Succeeded;

    /// <summary>The Git log's commit actions, as Rider's: each saves first, and the open files follow what it did.</summary>
    public async Task<bool> CheckoutRevisionAsync(string hash) =>
        (await SwitchAsync(repository => repository.CheckoutRevisionAsync(hash), $"Checked out {Short(hash)}")).Succeeded;

    public async Task<string?> NewBranchAtAsync(string name, string hash)
    {
        GitResult created = await SwitchAsync(repository => repository.NewBranchAtAsync(name, hash), $"Created and checked out {name} at {Short(hash)}");
        return created.Succeeded ? null : created.Message;
    }

    public async Task<bool> CherryPickAsync(string hash) =>
        (await SwitchAsync(repository => repository.CherryPickAsync(hash), $"Cherry-picked {Short(hash)}")).Succeeded;

    public async Task<bool> RevertAsync(string hash) =>
        (await SwitchAsync(repository => repository.RevertAsync(hash), $"Reverted {Short(hash)}")).Succeeded;

    public async Task<bool> ResetAsync(string hash, ResetMode mode) =>
        (await SwitchAsync(repository => repository.ResetAsync(hash, mode), $"Reset {Branch ?? "HEAD"} to {Short(hash)} ({mode.ToString().ToLowerInvariant()})")).Succeeded;

    private static string Short(string hash) => hash.Length > 8 ? hash[..8] : hash;

    /// <summary>Creates a branch from the head and checks it out; null when it worked, else git's reason.</summary>
    public async Task<string?> NewBranchAsync(string name)
    {
        GitResult created = await SwitchAsync(repository => repository.NewBranchAsync(name), $"Created and checked out {name}");
        return created.Succeeded ? null : created.Message;
    }

    /// <summary>Rider's Stash Changes: every uncommitted change, new files too, put away under a message; the open files
    /// take the head's text again. Null when it worked, else git's reason.</summary>
    public async Task<string?> StashAsync(string message)
    {
        GitResult stashed = await SwitchAsync(repository => repository.StashAsync(message.Trim()), "Stashed the changes");
        return stashed.Succeeded ? null : stashed.Message;
    }

    public async Task<IReadOnlyList<StashInfo>> StashesAsync() => git is { } repository ? await repository.StashesAsync() : [];

    /// <summary>Rider's Unstash Changes: a stash's changes back in the files, the stash dropped after with
    /// <paramref name="pop"/>.</summary>
    public async Task<bool> UnstashAsync(StashInfo stash, bool pop) =>
        (await SwitchAsync(repository => repository.UnstashAsync(stash.Index, pop), pop ? $"Unstashed {stash.Message}" : $"Applied {stash.Message}")).Succeeded;

    public async Task<bool> DropStashAsync(StashInfo stash) =>
        (await SwitchAsync(repository => repository.DropStashAsync(stash.Index), $"Dropped {stash.Message}")).Succeeded;

    /// <summary>Puts files back as the last commit has them; the open ones take their text from disk again.</summary>
    public async Task<bool> RollbackAsync(IReadOnlyList<FileChange> changes)
    {
        if (git is not { } repository || changes.Count == 0)
        {
            return false;
        }
        GitResult rolled = await repository.RollbackAsync(changes);
        ReloadOpenFiles();
        await RefreshAsync();
        Message?.Invoke(this, rolled.Succeeded ? $"Rolled back {Files(changes.Count)}" : rolled.Message);
        return rolled.Succeeded;
    }

    /// <summary>The open files take their text from disk again where something else changed it and they have no
    /// unsaved changes.</summary>
    public void ReloadOpenFiles()
    {
        foreach (OpenDocument document in documents.Keys)
        {
            document.Reload();
        }
    }

    private async Task<GitResult> SwitchAsync(Func<Git, Task<GitResult>> change, string done)
    {
        if (git is not { } repository)
        {
            return new GitResult(1, string.Empty, "The folder is not in a Git repository");
        }
        GitResult switched = await change(repository);
        if (switched.Succeeded)
        {
            ReloadOpenFiles();
        }
        await RefreshAsync();
        Message?.Invoke(this, switched.Succeeded ? done : switched.Message);
        return switched;
    }

    private async Task RefreshStatusAsync()
    {
        if (git is not { } repository)
        {
            return;
        }
        try
        {
            Changes = await repository.StatusAsync();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidOperationException failed)
        {
            Message?.Invoke(this, failed.Message);
        }
    }

    /// <summary>A file's text as the last commit has it, or null when it has none (new, or outside the tree).</summary>
    public Task<string?> HeadTextAsync(string path)
    {
        if (git is not { } repository || repository.Relative(path).StartsWith("..", StringComparison.Ordinal))
        {
            return Task.FromResult<string?>(null);
        }
        if (!heads.TryGetValue(path, out Task<string?>? head))
        {
            head = repository.HeadTextAsync(path);
            heads[path] = head;
        }
        return head;
    }

    /// <summary>Works out a file's change bars from the last commit's text against the editor's, off the window's
    /// thread; a newer edit's answer wins.</summary>
    private async Task MarkAsync(OpenDocument document)
    {
        if (!documents.TryGetValue(document, out Tracked? tracked))
        {
            return;
        }
        int generation = ++tracked.Generation;
        string? before = await HeadTextAsync(document.Path);
        string after = document.Text.Snapshot.ToString();
        IReadOnlyList<ChangeMark> marks = before is null ? [] : await Task.Run(() => LineDiff.Marks(before, after));
        if (generation == tracked.Generation && documents.ContainsKey(document))
        {
            document.ChangeMarks = marks;
        }
    }

    private static string FirstLine(string message) => message.Trim().Split('\n')[0].Trim();

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";

    private sealed class Tracked(DispatcherTimer timer)
    {
        public DispatcherTimer Timer { get; } = timer;
        public int Generation { get; set; }
        public EventHandler<Passenger.Text.DocumentChangedEventArgs>? OnChanged { get; set; }
        public EventHandler? OnSaved { get; set; }
    }
}
