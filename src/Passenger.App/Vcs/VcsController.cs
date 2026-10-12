using Avalonia.Threading;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Vcs;

/// <summary>The open folder's Git working tree as the window shows it: the branch, the changed files, each open file's
/// change bars against the last commit, and committing, pushing, switching branches and rolling back, after which the
/// open files take what is on disk again. A UI-thread object.</summary>
public sealed class VcsController
{
    /// <summary>How long typing pauses before a file's change bars are worked out again.</summary>
    private static readonly TimeSpan MarksDelay = TimeSpan.FromMilliseconds(120);

    private const string Busy = "Git is still busy with the last change";

    private readonly Dictionary<OpenDocument, Tracked> documents = [];

    /// <summary>Files' texts in the commit <see cref="textsOf"/> (null before the first commit), which a read sets once
    /// it has found HEAD since the folder opened (<see cref="textsRead"/>). They are kept until HEAD names another, so a
    /// refresh that finds it where it was reads none of them again.</summary>
    private readonly Dictionary<string, Task<string?>> heads = [];
    private string? textsOf;
    private bool textsRead;
    private readonly List<(string From, string To)> moves = [];
    private Git? git;
    private int opened;

    /// <summary>Reads go one at a time, so an older read never lands after a newer one (a save's status after the
    /// commit's); those asked for while one runs are one read when it ends, answered together, reading the history
    /// too when any of them asked for it.</summary>
    private bool reading;
    private TaskCompletionSource? asked;
    private bool historyAsked;

    /// <summary>The working tree the open folder is in, or null when it is in none.</summary>
    public Git? Repository => git;

    /// <summary>The branch checked out, or null on a detached head or outside a working tree.</summary>
    public string? Branch { get; private set; }

    /// <summary>The changed and new files, as the last status read them.</summary>
    public IReadOnlyList<FileChange> Changes { get; private set; } = [];

    /// <summary>Raised when the branch or the changed files have been read again.</summary>
    public event EventHandler? Changed;

    /// <summary>How many times git has been asked for last-commit texts: once for all the open files together when HEAD
    /// moves, once for each file looked at since, and never by a refresh that finds HEAD where it was.</summary>
    public int HeadReads { get; private set; }

    /// <summary>How many times an open file's text has been compared with its last commit's: once after each pause in
    /// typing and each new last-commit text, never again for a popup, a rollback or a diff of the text its bars are of.</summary>
    public int Comparisons { get; private set; }

    /// <summary>The most memory one comparison has taken, in bytes allocated on its thread: a few times the file's text,
    /// however much of it differs.</summary>
    public long LargestComparison { get; private set; }

    /// <summary>Raised after the whole tree was read again, as after a commit, a checkout or an update, when the
    /// history may have moved too (a save alone changes only the files).</summary>
    public event EventHandler? Refreshed;

    /// <summary>The history moved (a commit, a checkout, a fetch, a reset), so the log is read again; raised after Refreshed
    /// when it did, never for a save.</summary>
    public event EventHandler? HistoryChanged;

    private string? knownHistory;

    /// <summary>What happened, for the status bar: a commit made, a push refused and git's reason.</summary>
    public event EventHandler<string>? Message;

    /// <summary>A commit, a rollback, a stash or a change of branch is running. Another is refused until it ends, since
    /// git's index lock would fail it halfway (a double-click on Commit).</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Raised when <see cref="IsBusy"/> changes, for the buttons that start one.</summary>
    public event EventHandler? BusyChanged;

    /// <summary>Saves every open file, run before anything git does to the files (a commit, a checkout, a stash, a
    /// rollback, an update), as Rider saves first: what is committed is what the editor shows, and nothing unsaved is
    /// left behind for the next save to write over what git put there.</summary>
    public Action? SaveFirst { get; set; }

    /// <summary>Finds the folder's working tree and reads it; a folder in none leaves the window without Git.</summary>
    public async Task OpenAsync(string folder)
    {
        UiThread.Check();
        int generation = ++opened;
        Git? found = await Git.OpenAsync(folder);
        if (generation != opened)
        {
            return;
        }
        git = found;
        heads.Clear();
        textsOf = null;
        textsRead = false;
        moves.Clear();
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

    /// <summary>Reads the branch, the changed files and the history again, and when HEAD has moved every open file's
    /// last-commit text and change bars; done once a read begun after the call has landed.</summary>
    public Task RefreshAsync()
    {
        UiThread.Check();
        historyAsked = true;
        return ReadAsync();
    }

    /// <summary>Asks for a read: the next one to start, which starts now unless one is running.</summary>
    private Task ReadAsync()
    {
        TaskCompletionSource next = asked ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!reading)
        {
            _ = ReadInTurnAsync();
        }
        return next.Task;
    }

    /// <summary>Runs the reads asked for, one after another, until none is.</summary>
    private async Task ReadInTurnAsync()
    {
        reading = true;
        try
        {
            while (asked is { } answered)
            {
                asked = null;
                bool history = historyAsked;
                historyAsked = false;
                try
                {
                    await ReadOnceAsync(history);
                    answered.SetResult();
                }
                catch (Exception failed)
                {
                    answered.SetException(failed);
                }
            }
        }
        finally
        {
            reading = false;
        }
    }

    /// <summary>One read: the status (the branch, HEAD and the files), with the refs when the history is wanted too.</summary>
    private async Task ReadOnceAsync(bool history)
    {
        if (git is not { } repository)
        {
            Branch = null;
            Changes = [];
            if (!textsRead)
            {
                ReadTexts(null, null);
            }
            Changed?.Invoke(this, EventArgs.Empty);
            if (history)
            {
                Refreshed?.Invoke(this, EventArgs.Empty);
                if (knownHistory is not null)
                {
                    knownHistory = null;
                    HistoryChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            return;
        }
        GitStatus? status = null;
        string? refs = null;
        string? failure = null;
        try
        {
            status = await repository.StatusAsync();
            refs = history ? await repository.RefsAsync() : null;
        }
        catch (InvalidOperationException failed)
        {
            failure = failed.Message;
        }
        if (git != repository)
        {
            // Another folder was opened meanwhile: this is the last one's.
            return;
        }
        if (failure is not null)
        {
            Message?.Invoke(this, failure);
        }
        if (status is not null)
        {
            Branch = status.Branch;
            Changes = status.Changes;
            if (!textsRead || status.Head != textsOf)
            {
                ReadTexts(repository, status.Head);
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (!history)
        {
            return;
        }
        Refreshed?.Invoke(this, EventArgs.Empty);
        string? now = status is not null && refs is not null ? refs + status.Head : null;
        if (now is not null && now != knownHistory)
        {
            knownHistory = now;
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>HEAD names another commit (or the tree is another): every open file's text in it, read by one git, and
    /// their change bars against it.</summary>
    private void ReadTexts(Git? repository, string? head)
    {
        textsRead = true;
        textsOf = head;
        heads.Clear();
        if (repository is not null && head is not null)
        {
            string[] paths = [.. documents.Keys.Select(d => d.Path).Distinct().Where(repository.IsInside)];
            if (paths.Length > 0)
            {
                HeadReads++;
                Task<IReadOnlyDictionary<string, string?>> texts = repository.TextsAtAsync(head, [.. paths.SelectMany(p => new[] { p, Origin(p) })]);
                foreach (string path in paths)
                {
                    heads[path] = TextOfAsync(texts, path, Origin(path));
                }
            }
        }
        foreach (OpenDocument document in documents.Keys)
        {
            _ = MarkAsync(document);
        }

        static async Task<string?> TextOfAsync(Task<IReadOnlyDictionary<string, string?>> texts, string path, string origin)
        {
            IReadOnlyDictionary<string, string?> read = await texts;
            return read.GetValueOrDefault(path) ?? read.GetValueOrDefault(origin);
        }
    }

    /// <summary>Starts showing a file's change bars, worked out again as it is edited.</summary>
    public void Attach(OpenDocument document)
    {
        UiThread.Check();
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
        tracked.OnSaved = (_, _) => _ = ReadAsync();
        document.Text.Changed += tracked.OnChanged;
        document.Saved += tracked.OnSaved;
        documents[document] = tracked;
        _ = MarkAsync(document);
    }

    public void Detach(OpenDocument document)
    {
        UiThread.Check();
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
        return await OneAtATimeAsync(async () =>
        {
            SaveFirst?.Invoke();
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
        }, false);
    }

    public async Task<bool> PushAsync()
    {
        UiThread.Check();
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
        UiThread.Check();
        if (git is not { } repository)
        {
            return false;
        }
        SaveFirst?.Invoke();
        Message?.Invoke(this, "Updating...");
        GitResult pulled = await repository.PullAsync();
        ReloadOpenFiles();
        await RefreshAsync();
        Message?.Invoke(this, pulled.Succeeded ? Summary(pulled.Output, "Updated") : $"Update failed: {pulled.Message}");
        return pulled.Succeeded;
    }

    public async Task<bool> FetchAsync()
    {
        UiThread.Check();
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
        return await OneAtATimeAsync(async () =>
        {
            SaveFirst?.Invoke();
            GitResult rolled = await repository.RollbackAsync(changes);
            ReloadOpenFiles();
            await RefreshAsync();
            Message?.Invoke(this, rolled.Succeeded ? $"Rolled back {Files(changes.Count)}" : rolled.Message);
            return rolled.Succeeded;
        }, false);
    }

    /// <summary>Runs a change to the working tree unless another is still running, which it refuses with
    /// <paramref name="refused"/>.</summary>
    private async Task<T> OneAtATimeAsync<T>(Func<Task<T>> change, T refused)
    {
        UiThread.Check();
        if (IsBusy)
        {
            Message?.Invoke(this, Busy);
            return refused;
        }
        IsBusy = true;
        BusyChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            return await change();
        }
        finally
        {
            IsBusy = false;
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The open files take their text from disk again where something else changed it and they have no
    /// unsaved changes.</summary>
    public void ReloadOpenFiles()
    {
        UiThread.Check();
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
        return await OneAtATimeAsync(async () =>
        {
            SaveFirst?.Invoke();
            GitResult switched = await change(repository);
            if (switched.Succeeded)
            {
                ReloadOpenFiles();
            }
            await RefreshAsync();
            Message?.Invoke(this, switched.Succeeded ? done : switched.Message);
            return switched;
        }, new GitResult(1, string.Empty, Busy));
    }

    /// <summary>A file's text as the last commit has it, or null when it has none (new, or outside the tree). A file
    /// opened since HEAD last moved is read on its own, in the commit the others were read in.</summary>
    public Task<string?> HeadTextAsync(string path)
    {
        UiThread.Check();
        if (git is not { } repository || !repository.IsInside(path) || (textsRead && textsOf is null))
        {
            return Task.FromResult<string?>(null);
        }
        if (!heads.TryGetValue(path, out Task<string?>? head))
        {
            HeadReads++;
            string revision = textsOf ?? "HEAD";
            string origin = Origin(path);
            head = origin == path ? repository.TextAtAsync(revision, path) : TextOrOriginAsync(repository, revision, path, origin);
            heads[path] = head;
        }
        return head;

        static async Task<string?> TextOrOriginAsync(Git repository, string revision, string path, string origin) =>
            await repository.TextAtAsync(revision, path) ?? await repository.TextAtAsync(revision, origin);
    }

    /// <summary>A file or folder moved here (a rename, a code fix): until a commit has its files at their new paths,
    /// their change bars, diff and rollback compare with what the last commit has where they were, as Rider's do.</summary>
    public void Moved(string from, string to)
    {
        moves.Add((from, to));
        heads.Clear();
    }

    /// <summary>Where a path was before every move since the folder opened.</summary>
    private string Origin(string path)
    {
        for (int i = moves.Count - 1; i >= 0; i--)
        {
            path = PathNames.Moved(path, moves[i].To, moves[i].From);
        }
        return path;
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
        using (BackgroundWork.Scope())
        {
            FileDiff? diff = await DiffAsync(document);
            if (generation == tracked.Generation && documents.ContainsKey(document))
            {
                document.ChangeMarks = diff?.Marks ?? [];
            }
        }
    }

    /// <summary>An open file's text as it is now compared with the last commit's, or null when that has no such file.
    /// The comparison its change bars came from, or are coming from, when they are of this text, so what acts on a bar
    /// costs nothing more; else a new one, off the window's thread.</summary>
    public async Task<FileDiff?> DiffAsync(OpenDocument document)
    {
        UiThread.Check();
        if (await HeadTextAsync(document.Path) is not { } head)
        {
            return null;
        }
        TextSnapshot text = document.Text.Snapshot;
        documents.TryGetValue(document, out Tracked? tracked);
        if (tracked?.Last is { } last && ReferenceEquals(last.Text, text) && ReferenceEquals(last.Head, head))
        {
            return await last.Diff;
        }
        // The last commit's lines are split once while its text stays the same.
        FileDiff? earlier = tracked?.Last is { Diff.IsCompletedSuccessfully: true } done && ReferenceEquals(done.Head, head) ? done.Diff.Result : null;
        Task<FileDiff> diff = CompareAsync(head, earlier, text);
        if (tracked is not null)
        {
            tracked.Last = new Compared(head, text, diff);
        }
        return await diff;
    }

    private async Task<FileDiff> CompareAsync(string head, FileDiff? earlier, TextSnapshot text)
    {
        Comparisons++;
        (FileDiff diff, long bytes) = await Task.Run(() =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            FileDiff compared = earlier?.Compare(text) ?? FileDiff.Compare(head, text);
            return (compared, GC.GetAllocatedBytesForCurrentThread() - before);
        });
        LargestComparison = Math.Max(LargestComparison, bytes);
        return diff;
    }

    private static string FirstLine(string message) => message.Trim().Split('\n')[0].Trim();

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";

    private sealed class Tracked(DispatcherTimer timer)
    {
        public DispatcherTimer Timer { get; } = timer;
        public int Generation { get; set; }
        public Compared? Last { get; set; }
        public EventHandler<Passenger.Text.DocumentChangedEventArgs>? OnChanged { get; set; }
        public EventHandler? OnSaved { get; set; }
    }

    /// <summary>The newest comparison of a file, running or done, and the texts it is between.</summary>
    private sealed record Compared(string Head, TextSnapshot Text, Task<FileDiff> Diff);
}
