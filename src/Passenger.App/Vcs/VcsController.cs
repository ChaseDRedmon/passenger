using Passenger.App.Workspace;
using Passenger.Core;
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

    /// <summary>How long typing pauses before an annotated file is blamed again: a git each time, so longer.</summary>
    private static readonly TimeSpan BlameDelay = TimeSpan.FromMilliseconds(500);

    private const string Busy = "Git is still busy with the last change";

    private const string NotInGit = "The folder is not in a Git repository";

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
    private TaskCompletionSource? opening;

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

    /// <summary>The files in conflict, as the last status read them (each also among <see cref="Changes"/>).</summary>
    public IReadOnlyList<GitConflict> Conflicts { get; private set; } = [];

    /// <summary>What the tree is in the middle of (a merge, a rebase), as the last status found it.</summary>
    public MergeState Merge { get; private set; } = MergeState.Idle;

    /// <summary>The conflicts' two sides in the words Rider gives them, for what the tree is in the middle of.</summary>
    public ConflictSides Sides => Merge.Sides(unstashed);

    /// <summary>The stash an unstash with conflicts applied, which names the sides until they are resolved.</summary>
    private string? unstashed;

    /// <summary>Raised when an operation (an update, an unstash, a cherry-pick, a rebase continued) stopped for conflicts,
    /// as Rider then shows its Conflicts dialog.</summary>
    public event EventHandler? ConflictsFound;

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

    /// <summary>What happened, for the status bar: what is under way (a push), and each operation's outcome in the words
    /// it returns them in (a commit made, a push refused and git's reason).</summary>
    public event EventHandler<string>? Message;

    /// <summary>A commit, a rollback, a stash or a change of branch is running. Another is refused until it ends, since
    /// git's index lock would fail it halfway (a double-click on Commit).</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Raised when <see cref="IsBusy"/> changes, for the buttons that start one.</summary>
    public event EventHandler? BusyChanged;

    /// <summary>Done once nothing runs: what follows from an operation's end (the rebase offered to go on once the last
    /// conflict is resolved) waits for it, rather than being refused as busy.</summary>
    public Task Idle => IsBusy ? (idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task : Task.CompletedTask;

    private TaskCompletionSource? idle;

    /// <summary>Saves every open file, run before anything git does to the files (a commit, a checkout, a stash, a
    /// rollback, an update), as Rider saves first: what is committed is what the editor shows, and nothing unsaved is
    /// left behind for the next save to write over what git put there.</summary>
    public Action? SaveFirst { get; set; }

    /// <summary>Names what an operation does to the files, for Local History, by Rider's activity names (Git checkout,
    /// Rollback, Update from VCS): what it changes is kept under that name until the hold is disposed.</summary>
    public Func<string, IDisposable?>? Activity { get; set; }

    /// <summary>Done once the folder last opened is known to be in a working tree or not, so a question asked straight
    /// after a folder opens (an agent's first call) waits for that rather than hearing there is none.</summary>
    public Task Opened => opening?.Task ?? Task.CompletedTask;

    /// <summary>Finds the folder's working tree and reads it; a folder in none leaves the window without Git.</summary>
    public async Task OpenAsync(string folder)
    {
        UiThread.Check();
        int generation = ++opened;
        TaskCompletionSource lookup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        opening = lookup;
        try
        {
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
        }
        finally
        {
            lookup.TrySetResult();
        }
        await RefreshAsync();
    }

    /// <summary>The folder closed: the window is in no working tree, as with a folder outside one, until another opens;
    /// a lookup still under way for the folder is dropped.</summary>
    public Task CloseAsync()
    {
        UiThread.Check();
        opened++;
        git = null;
        heads.Clear();
        textsOf = null;
        textsRead = false;
        moves.Clear();
        return RefreshAsync();
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
            Conflicts = [];
            Merge = MergeState.Idle;
            unstashed = null;
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
        MergeState? merge = null;
        string? refs = null;
        string? failure = null;
        try
        {
            await indexed;
            status = await repository.StatusAsync();
            merge = await repository.MergeStateAsync(status.Branch, status.Head);
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
            Conflicts = status.Conflicts;
            Merge = merge ?? MergeState.Idle;
            if (Conflicts.Count == 0)
            {
                unstashed = null;
            }
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
        Tracked tracked = new(new Debounce(MarksDelay, () => MarkAsync(document)), new Debounce(BlameDelay, () => BlameAsync(document)));
        tracked.OnChanged = (_, _) =>
        {
            // Outside a working tree a file has no bars to move; once one is found, every open file's are worked out.
            if (git is not null)
            {
                tracked.Marks.Restart();
            }
            if (tracked.Annotated)
            {
                tracked.Reblame.Restart();
            }
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
            tracked.Marks.Stop();
            tracked.Reblame.Stop();
            // A blame still running lands nowhere.
            tracked.Blaming++;
            document.Text.Changed -= tracked.OnChanged;
            document.Saved -= tracked.OnSaved;
        }
    }

    /// <summary>The blame an annotated file shows, line by line, or null when it shows none.</summary>
    public IReadOnlyList<BlameLine>? BlameOf(OpenDocument document) => documents.GetValueOrDefault(document)?.Blame;

    /// <summary>Whether a file's annotations are open, its blame shown or on its way.</summary>
    public bool IsAnnotated(OpenDocument document) => documents.GetValueOrDefault(document)?.Annotated == true;

    /// <summary>Annotate with Git Blame: each line named in the gutter by the commit that last changed it, blamed again
    /// after each pause in typing until <see cref="CloseAnnotations"/>. False, with nothing shown, when Git does not
    /// track the file.</summary>
    public async Task<bool> AnnotateAsync(OpenDocument document)
    {
        UiThread.Check();
        if (!documents.TryGetValue(document, out Tracked? tracked))
        {
            return false;
        }
        tracked.Annotated = true;
        return await BlameAsync(document);
    }

    /// <summary>Close Annotations: the gutter's column goes, and a blame still running lands nowhere.</summary>
    public void CloseAnnotations(OpenDocument document)
    {
        UiThread.Check();
        if (documents.TryGetValue(document, out Tracked? tracked))
        {
            Unannotate(document, tracked);
        }
    }

    private static void Unannotate(OpenDocument document, Tracked tracked)
    {
        tracked.Annotated = false;
        tracked.Blame = null;
        tracked.Blaming++;
        tracked.Reblame.Stop();
        document.GutterAnnotations = [];
    }

    /// <summary>Blames the file's text as it is now and shows it, unless annotations closed, another blame began or the
    /// text changed meanwhile (the pause after that edit blames it again). False only when git said it does not track
    /// the file, which closes them.</summary>
    private async Task<bool> BlameAsync(OpenDocument document)
    {
        if (!documents.TryGetValue(document, out Tracked? tracked) || !tracked.Annotated)
        {
            return true;
        }
        int blaming = ++tracked.Blaming;
        TextSnapshot text = document.Text.Snapshot;
        IReadOnlyList<BlameLine>? lines = git is { } repository ? await repository.BlameAsync(document.Path, text.ToString()) : null;
        if (blaming != tracked.Blaming)
        {
            return true;
        }
        if (lines is null)
        {
            Unannotate(document, tracked);
            return false;
        }
        if (!ReferenceEquals(text, document.Text.Snapshot))
        {
            return true;
        }
        tracked.Blame = lines;
        document.GutterAnnotations = Labels(lines);
        return true;
    }

    /// <summary>Each commit's block named at its first line, as Rider draws it; a change not committed yet says so.</summary>
    private static List<string?> Labels(IReadOnlyList<BlameLine> lines)
    {
        List<string?> labels = [];
        for (int i = 0; i < lines.Count; i++)
        {
            BlameLine line = lines[i];
            bool first = i == 0 || lines[i - 1].Hash != line.Hash;
            string author = line.Author.Length > 14 ? line.Author[..14] : line.Author;
            labels.Add(!first ? null : line.IsCommitted ? $"{line.Time.ToLocalTime():yyyy-MM-dd} {author}" : "Not committed yet");
        }
        return labels;
    }

    /// <summary>Commits the chosen files with a message, then pushes when asked, as Rider's Commit and Commit and
    /// Push do: the commit's outcome with the commit made, and the push's once the commit is made and one was asked for.</summary>
    public async Task<CommitResult> CommitAsync(IReadOnlyList<FileChange> chosen, string message, bool amend, bool push)
    {
        UiThread.Check();
        if (git is not { } repository)
        {
            return new CommitResult(Report(false, NotInGit), null, null);
        }
        if (Conflicts.Count > 0)
        {
            return new CommitResult(Report(false, "Resolve conflicts in order to commit"), null, null);
        }
        // A merge's commit finishes it even when it changed no file (every conflict resolved as HEAD had it).
        bool merging = Merge.CommitsWhole && !amend;
        if (chosen.Count == 0 && !amend && !merging)
        {
            return new CommitResult(Report(false, "No files are chosen to commit"), null, null);
        }
        if (message.Trim().Length == 0)
        {
            return new CommitResult(Report(false, "The commit message is empty"), null, null);
        }
        return await OneAtATimeAsync(async () =>
        {
            SaveFirst?.Invoke();
            string[] paths = [.. Paths(chosen)];
            string[] excluded = merging ? [.. Paths(Changes.Where(c => c.Kind is not ChangeKind.Unversioned && !chosen.Contains(c)))] : [];
            GitResult commit = await repository.CommitAsync(paths, message, amend, merging, excluded);
            if (!commit.Succeeded)
            {
                VcsOutcome failed = Report(false, commit.Message);
                await RefreshAsync();
                return new CommitResult(failed, null, null);
            }
            CommitInfo? made = await repository.HeadAsync();
            int count = chosen.Count;
            string committed = count switch
            {
                0 => "Committed",
                1 => "1 file committed",
                _ => $"{count} files committed",
            };
            VcsOutcome done = Report(true, $"{committed}: {FirstLine(message)}");
            await RefreshAsync();
            return new CommitResult(done, made, push ? await PushAsync() : null);
        }, refused => new CommitResult(refused, null, null));

        static IEnumerable<string> Paths(IEnumerable<FileChange> changes) => changes.SelectMany(c => c.From is { } from ? new[] { c.Path, from } : [c.Path]);
    }

    public async Task<VcsOutcome> PushAsync()
    {
        UiThread.Check();
        if (git is not { } repository)
        {
            return Report(false, NotInGit);
        }
        Message?.Invoke(this, "Pushing...");
        GitResult pushed = await repository.PushAsync();
        return Report(pushed.Succeeded, pushed.Succeeded ? $"Pushed {Branch}" : $"Push failed: {pushed.Message}");
    }

    /// <summary>Rider's Update Project: the upstream's commits brought in, then the open files and Git read again.</summary>
    public async Task<VcsOutcome> UpdateAsync()
    {
        UiThread.Check();
        if (git is not { } repository)
        {
            return Report(false, NotInGit);
        }
        if (Conflicts.Count > 0)
        {
            return Report(false, "Unmerged files detected. These conflicts must be resolved before update.");
        }
        SaveFirst?.Invoke();
        Message?.Invoke(this, "Updating...");
        // Named until Git is read again, so the folder watcher has heard of what it changed.
        using IDisposable? named = Activity?.Invoke("Update from VCS");
        GitResult pulled = await repository.PullAsync();
        ReloadOpenFiles();
        await RefreshAsync();
        if (Conflicted(Merge.Operation is MergeOperation.Rebase ? "Rebase stopped due to conflicts" : "Merged with conflicts") is { } stopped)
        {
            return stopped;
        }
        return Report(pulled.Succeeded, pulled.Succeeded ? Summary(pulled.Output, "Updated") : $"Update failed: {pulled.Message}");
    }

    /// <summary>After an operation that can stop for conflicts: when it did, what it came to, told to the status bar
    /// with the files, and the Conflicts dialog asked for; null when it left none.</summary>
    private VcsOutcome? Conflicted(string what)
    {
        if (Conflicts.Count == 0)
        {
            return null;
        }
        VcsOutcome stopped = Report(false, $"{what}: {string.Join(", ", Conflicts.Select(c => git?.Relative(c.Path) ?? c.Name))}");
        ConflictsFound?.Invoke(this, EventArgs.Empty);
        return stopped;
    }

    public async Task<VcsOutcome> FetchAsync()
    {
        UiThread.Check();
        if (git is not { } repository)
        {
            return Report(false, NotInGit);
        }
        Message?.Invoke(this, "Fetching...");
        GitResult fetched = await repository.FetchAsync();
        await RefreshAsync();
        return Report(fetched.Succeeded, fetched.Succeeded ? "Fetched" : $"Fetch failed: {fetched.Message}");
    }

    /// <summary>An operation's outcome, told to the status bar in the words it is returned in.</summary>
    private VcsOutcome Report(bool succeeded, string message)
    {
        Message?.Invoke(this, message);
        return new VcsOutcome(succeeded, message);
    }

    private static string Summary(string output, string fallback) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() is { Length: > 0 } last ? $"{fallback}: {last}" : fallback;

    /// <summary>The last commit's message, which Amend starts from.</summary>
    public async Task<string?> LastMessageAsync() => git is { } repository ? await repository.LastMessageAsync() : null;

    public Task<VcsOutcome> CheckoutAsync(string branch) =>
        SwitchAsync(repository => repository.CheckoutAsync(branch), $"Checked out {branch}", "Git checkout");

    /// <summary>The Git log's commit actions, as Rider's: each saves first, and the open files follow what it did.</summary>
    public Task<VcsOutcome> CheckoutRevisionAsync(string hash) =>
        SwitchAsync(repository => repository.CheckoutRevisionAsync(hash), $"Checked out {Short(hash)}", "Git checkout");

    public Task<VcsOutcome> NewBranchAtAsync(string name, string hash) =>
        SwitchAsync(repository => repository.NewBranchAtAsync(name, hash), $"Created and checked out {name} at {Short(hash)}", "Git checkout");

    public Task<VcsOutcome> CherryPickAsync(string hash) =>
        SwitchAsync(repository => repository.CherryPickAsync(hash), $"Cherry-picked {Short(hash)}", "Git cherry-pick", "Cherry-pick was performed with conflicts");

    public Task<VcsOutcome> RevertAsync(string hash) =>
        SwitchAsync(repository => repository.RevertAsync(hash), $"Reverted {Short(hash)}", "Git revert", "Revert was performed with conflicts");

    public Task<VcsOutcome> ResetAsync(string hash, ResetMode mode) =>
        SwitchAsync(repository => repository.ResetAsync(hash, mode), $"Reset {Branch ?? "HEAD"} to {Short(hash)} ({mode.ToString().ToLowerInvariant()})", "Git reset");

    private static string Short(string hash) => hash.Length > 8 ? hash[..8] : hash;

    /// <summary>Creates a branch from the head and checks it out.</summary>
    public Task<VcsOutcome> NewBranchAsync(string name) =>
        SwitchAsync(repository => repository.NewBranchAsync(name), $"Created and checked out {name}", "Git checkout");

    /// <summary>Rider's Stash Changes: every uncommitted change, new files too, put away under a message; the open files
    /// take the head's text again.</summary>
    public Task<VcsOutcome> StashAsync(string message) =>
        SwitchAsync(repository => repository.StashAsync(message.Trim()), "Stashed the changes", "Git stash");

    public async Task<IReadOnlyList<StashInfo>> StashesAsync() => git is { } repository ? await repository.StashesAsync() : [];

    /// <summary>Rider's Unstash Changes: a stash's changes back in the files, the stash dropped after with
    /// <paramref name="pop"/>.</summary>
    public Task<VcsOutcome> UnstashAsync(StashInfo stash, bool pop)
    {
        // Git keeps no record of an unstash that stopped for conflicts: the stash names their sides until they are resolved.
        if (Conflicts.Count == 0)
        {
            unstashed = $"stash@{{{stash.Index}}}";
        }
        return SwitchAsync(repository => repository.UnstashAsync(stash.Index, pop), pop ? $"Unstashed {stash.Message}" : $"Applied {stash.Message}", "Git unstash",
            "Unstashed with conflicts");
    }

    public Task<VcsOutcome> DropStashAsync(StashInfo stash) =>
        SwitchAsync(repository => repository.DropStashAsync(stash.Index), $"Dropped {stash.Message}", "Git stash");

    /// <summary>Puts files back as the last commit has them; the open ones take their text from disk again.</summary>
    public async Task<VcsOutcome> RollbackAsync(IReadOnlyList<FileChange> changes)
    {
        if (git is not { } repository)
        {
            return Report(false, NotInGit);
        }
        if (changes.Count == 0)
        {
            return Report(false, "No files are chosen to roll back");
        }
        return await OneAtATimeAsync(async () =>
        {
            SaveFirst?.Invoke();
            using IDisposable? named = Activity?.Invoke("Rollback");
            GitResult rolled = await repository.RollbackAsync(changes);
            ReloadOpenFiles();
            await RefreshAsync();
            return Report(rolled.Succeeded, rolled.Succeeded ? $"Rolled back {Files(changes.Count)}" : rolled.Message);
        }, refused => refused);
    }

    /// <summary>Runs a change to the working tree unless another is still running, which it refuses, answering with what
    /// <paramref name="refused"/> makes of the refusal.</summary>
    private async Task<T> OneAtATimeAsync<T>(Func<Task<T>> change, Func<VcsOutcome, T> refused)
    {
        UiThread.Check();
        if (IsBusy)
        {
            return refused(Report(false, Busy));
        }
        IsBusy = true;
        BusyChanged?.Invoke(this, EventArgs.Empty);
        // Held whole, the waits between its gits too (a rollback's retries): the files and the lists follow what it did.
        using IDisposable held = BackgroundWork.Scope();
        try
        {
            return await change();
        }
        finally
        {
            IsBusy = false;
            BusyChanged?.Invoke(this, EventArgs.Empty);
            idle?.TrySetResult();
            idle = null;
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

    /// <summary>Runs a change to the working tree and its files, what it changes kept in Local History under the
    /// <paramref name="activity"/>'s name; one that can stop for conflicts (an unstash, a cherry-pick) says so in
    /// <paramref name="conflicted"/>'s words when it did.</summary>
    private async Task<VcsOutcome> SwitchAsync(Func<Git, Task<GitResult>> change, string done, string activity, string? conflicted = null)
    {
        if (git is not { } repository)
        {
            return Report(false, NotInGit);
        }
        return await OneAtATimeAsync(async () =>
        {
            SaveFirst?.Invoke();
            bool already = Conflicts.Count > 0;
            using IDisposable? named = Activity?.Invoke(activity);
            GitResult switched = await change(repository);
            if (switched.Succeeded)
            {
                ReloadOpenFiles();
            }
            await RefreshAsync();
            // Stopped for conflicts, the files hold them now: they are shown as they are on disk.
            if (!switched.Succeeded && !already && Conflicts.Count > 0)
            {
                ReloadOpenFiles();
            }
            if (conflicted is not null && !already && Conflicted(conflicted) is { } stopped)
            {
                return stopped;
            }
            return Report(switched.Succeeded, switched.Succeeded ? done : switched.Message);
        }, refused => refused);
    }

    /// <summary>Accept Yours or Accept Theirs: conflicted files take that side whole (or go, where it deleted them) and
    /// are marked resolved; the open ones show it.</summary>
    public async Task<VcsOutcome> AcceptAsync(IReadOnlyList<GitConflict> conflicts, bool yours)
    {
        if (git is not { } repository)
        {
            return Report(false, NotInGit);
        }
        if (conflicts.Count == 0)
        {
            return Report(false, "No files are chosen to resolve");
        }
        // Rebasing, yours is the branch being rebased: git's theirs.
        bool theirs = yours == Sides.Swapped;
        return await OneAtATimeAsync(async () =>
        {
            SaveFirst?.Invoke();
            GitResult accepted = await repository.AcceptAsync(conflicts, theirs);
            ReloadOpenFiles();
            await RefreshAsync();
            string names = string.Join(", ", conflicts.Select(c => c.Name));
            return Report(accepted.Succeeded, accepted.Succeeded ? $"{(yours ? "Accept Yours" : "Accept Theirs")}: {names}" : accepted.Message);
        }, refused => refused);
    }

    /// <summary>A conflicted file's three versions as the merge window lays them out: the one both sides came from, and
    /// yours on the left and theirs on the right (empty where a side deleted it), with the words for each.</summary>
    public async Task<MergeTexts?> MergeTextsAsync(string path)
    {
        UiThread.Check();
        if (git is not { } repository)
        {
            return null;
        }
        ConflictSides sides = Sides;
        (string? common, string? ours, string? theirs) = await repository.StagesAsync(path);
        return new MergeTexts(path, common ?? string.Empty, (sides.Swapped ? theirs : ours) ?? string.Empty, (sides.Swapped ? ours : theirs) ?? string.Empty, sides);
    }

    /// <summary>The merge window's Apply: the result written to the file, in the encoding it had, and the file marked
    /// resolved; the open files show it.</summary>
    public async Task<VcsOutcome> ResolveAsync(string path, string text)
    {
        if (git is not { } repository)
        {
            return Report(false, NotInGit);
        }
        return await OneAtATimeAsync(async () =>
        {
            SaveFirst?.Invoke();
            try
            {
                FileText? before = File.Exists(path) ? FileText.Load(path) : null;
                FileText.Save(path, text, before?.Encoding ?? new System.Text.UTF8Encoding(false), before?.HasBom ?? false);
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                return Report(false, refused.Message);
            }
            GitResult marked = await repository.MarkResolvedAsync(path);
            ReloadOpenFiles();
            await RefreshAsync();
            return Report(marked.Succeeded, marked.Succeeded ? $"{Path.GetFileName(path)} resolved" : marked.Message);
        }, refused => refused);
    }

    /// <summary>Rider's Abort Merge and Abort Rebase (and a cherry-pick's or a revert's): the tree back as it was before
    /// the operation began.</summary>
    public Task<VcsOutcome> AbortAsync()
    {
        MergeOperation operation = Merge.Operation;
        if (operation is MergeOperation.None)
        {
            return Task.FromResult(Report(false, "Nothing is under way to abort"));
        }
        string name = operation switch
        {
            MergeOperation.CherryPick => "cherry-pick",
            _ => operation.ToString().ToLowerInvariant(),
        };
        return SwitchAsync(repository => repository.AbortAsync(operation), $"Abort {name} succeeded", $"Git abort {name}");
    }

    /// <summary>Rider's Continue Rebase, once every conflict is resolved.</summary>
    public Task<VcsOutcome> ContinueRebaseAsync()
    {
        if (Merge.Operation is not MergeOperation.Rebase)
        {
            return Task.FromResult(Report(false, "No rebase in progress"));
        }
        if (Conflicts.Count > 0)
        {
            return Task.FromResult(Report(false, "Resolve conflicts in order to continue rebase"));
        }
        return SwitchAsync(repository => repository.ContinueRebaseAsync(), "Rebase successful", "Git rebase", "Rebase stopped due to conflicts");
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
    /// their change bars, diff and rollback compare with what the last commit has where they were, as Rider's do. What
    /// the index has of it moves with it, as git mv moves it, so the status shows a rename; the next read waits for that.</summary>
    public void Moved(string from, string to)
    {
        moves.Add((from, to));
        heads.Clear();
        if (git is { } repository)
        {
            indexed = MoveInIndexAsync(indexed, repository, from, to);
        }
    }

    /// <summary>The index's moves still under way, which a read waits out so it never shows a move half made.</summary>
    private Task indexed = Task.CompletedTask;

    /// <summary>One move made in the index after those before it; it never fails, so the reads waiting on it go on.</summary>
    private async Task MoveInIndexAsync(Task before, Git repository, string from, string to)
    {
        await before;
        try
        {
            GitResult moved = await repository.MoveInIndexAsync(from, to);
            if (!moved.Succeeded)
            {
                Message?.Invoke(this, $"Git did not take {Path.GetFileName(from)}'s move: {moved.Message}");
            }
        }
        catch (Exception error)
        {
            IdeErrors.Report(error);
        }
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
        (FileDiff diff, long bytes) = await BackgroundWork.Run(() =>
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

    /// <summary>An open file as Git follows it: one handler for its edits and one for its saves, and one wait each for its
    /// bars and its blame, all dropped with it.</summary>
    private sealed class Tracked(Debounce marks, Debounce reblame)
    {
        public Debounce Marks { get; } = marks;
        public Debounce Reblame { get; } = reblame;
        public int Generation { get; set; }
        public Compared? Last { get; set; }
        public bool Annotated { get; set; }
        public IReadOnlyList<BlameLine>? Blame { get; set; }

        /// <summary>The newest blame's number: one that lands under another was overtaken, or annotations closed.</summary>
        public int Blaming { get; set; }

        public EventHandler<Passenger.Text.DocumentChangedEventArgs>? OnChanged { get; set; }
        public EventHandler? OnSaved { get; set; }
    }

    /// <summary>The newest comparison of a file, running or done, and the texts it is between.</summary>
    private sealed record Compared(string Head, TextSnapshot Text, Task<FileDiff> Diff);
}

/// <summary>A conflicted file's versions as the merge window shows them: the one both sides came from, yours (left) and
/// theirs (right), and the words for its sides.</summary>
public sealed record MergeTexts(string Path, string Base, string Left, string Right, ConflictSides Sides);

/// <summary>What an operation on the working tree came to: whether it worked, and what the person is told of it (git's own
/// words when it did not), the words the status bar is told as it lands.</summary>
public sealed record VcsOutcome(bool Succeeded, string Message)
{
    /// <summary>Why it did not work, or null when it did.</summary>
    public string? Problem => Succeeded ? null : Message;
}

/// <summary>A commit's outcome, the commit it made, and the push's outcome when one was asked for after it: a push the
/// remote refuses is a failure of its own, with the commit made all the same.</summary>
public sealed record CommitResult(VcsOutcome Commit, CommitInfo? Made, VcsOutcome? Push)
{
    public bool Succeeded => Commit.Succeeded && Push?.Succeeded != false;
}
