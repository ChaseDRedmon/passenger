using System.ComponentModel;
using ModelContextProtocol.Server;
using Passenger.App.Packages;
using Passenger.App.Vcs;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Terminal;
using Passenger.Text;

namespace Passenger.App.Mcp;

/// <summary>The daily loop's tools: Git as the Commit and Git windows drive it, NuGet as its window does, and the IDE's
/// terminal, each through the window's own code so the agent and the person see one state.</summary>
public sealed partial class PassengerTools
{
    // Version control

    [McpServerTool(Name = "vcs_status", Title = "Changed files", ReadOnly = true), Description(
        "The branch and the files changed since the last commit (modified, added, deleted, renamed, unversioned, conflicted), as the Commit window lists them. " +
        "While a merge, a rebase, an unstash or a cherry-pick has stopped for conflicts, conflicts says what is under way, the branch yours and theirs are " +
        "(during a rebase yours is the branch being rebased) and, for each file in conflict, whether each side modified or deleted it, as the Conflicts dialog lists them; " +
        "vcs_resolve resolves them.")]
    public Task<VcsStatus> VcsStatus(CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        VcsController vcs = window.Vcs;
        await vcs.Opened;
        await vcs.RefreshAsync();
        if (vcs.Repository is null)
        {
            throw new InvalidOperationException("The folder is not in a Git repository");
        }
        ConflictSides sides = vcs.Sides;
        VcsConflicts? conflicts = vcs.Conflicts.Count == 0 ? null : new VcsConflicts(sides.Description, sides.Yours, sides.Theirs,
            [.. vcs.Conflicts.Select(c => new VcsConflict(Relative(c.Path), sides.YoursHas(c) ? "modified" : "deleted", sides.TheirsHas(c) ? "modified" : "deleted"))]);
        return new VcsStatus(vcs.Branch, [.. vcs.Changes.Select(c => new VcsChange(Relative(c.Path), c.Kind.ToString().ToLowerInvariant(), c.From is { } from ? Relative(from) : null))], conflicts);
    });

    [McpServerTool(Name = "vcs_resolve", Title = "Resolve a conflict", Destructive = true), Description(
        "Resolves a file in conflict (vcs_status lists them) as the Conflicts dialog does: take yours or theirs, that side's version whole " +
        "(Accept Yours, Accept Theirs; during a rebase yours is the branch being rebased; a side that deleted the file deletes it), or give text, " +
        "the merged file to write, as the merge window's Apply Changes writes its result. The file is then marked resolved. Once none is left, " +
        "vcs_commit finishes a merge, a cherry-pick or a revert, and the Continue Rebase action (ide_action) a rebase.")]
    public Task<string> VcsResolve(string path, [Description("yours or theirs; or leave it out and give text")] string? take = null,
        [Description("The file's merged text, in place of take")] string? text = null, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        VcsController vcs = window.Vcs;
        await vcs.Opened;
        await vcs.RefreshAsync();
        string full = Resolve(path);
        GitConflict conflict = vcs.Conflicts.FirstOrDefault(c => c.Path == full)
            ?? throw new ArgumentException(vcs.Conflicts.Count == 0 ? "No file is in conflict" : $"{Relative(full)} is not in conflict; {string.Join(", ", vcs.Conflicts.Select(c => Relative(c.Path)))} are");
        VcsOutcome done = (take, text) switch
        {
            ("yours" or "theirs", null) => await vcs.AcceptAsync([conflict], yours: take == "yours"),
            (null, { } merged) => await vcs.ResolveAsync(full, merged),
            _ => throw new ArgumentException("Give take (yours or theirs) or text, the merged file, not both"),
        };
        if (done.Problem is { } failed)
        {
            throw new InvalidOperationException(failed);
        }
        int left = vcs.Conflicts.Count;
        return left == 0 ? $"{Relative(full)} resolved; no conflicts are left" : $"{Relative(full)} resolved; {left} {(left == 1 ? "file is" : "files are")} still in conflict";
    });

    [McpServerTool(Name = "vcs_diff", Title = "A file's changes", ReadOnly = true), Description(
        "What changed in a file since the last commit, as a unified diff: the editor's text when the file is open (unsaved changes included), else the file on disk.")]
    public Task<string> VcsDiff(string path, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        string full = Resolve(path);
        string name = Relative(full).Replace('\\', '/');
        await window.Vcs.Opened;
        string head = await window.Vcs.HeadTextAsync(full) ?? string.Empty;
        TextSnapshot? open = Editors.Documents.FirstOrDefault(d => d.Path == full)?.Text.Snapshot;
        // Reading and comparing a large file would hold the person's window: the thread pool does both.
        string diff = await BackgroundWork.Run(() => LineDiff.Unified(head, open?.ToString() ?? (File.Exists(full) ? File.ReadAllText(full) : string.Empty), name), cancel);
        return diff.Length == 0 ? "No changes" : diff;
    });

    [McpServerTool(Name = "vcs_commit", Title = "Commit", Destructive = true), Description(
        "Commits files with a message, as the Commit window does (every file saved first): the paths given, else every changed file that is not unversioned. " +
        "amend changes the last commit; push pushes after. Returns the new commit, and how the push went; a push the remote refuses is an error " +
        "naming git's reason, the commit made all the same.")]
    public Task<string> VcsCommit(string message, IReadOnlyList<string>? paths = null, bool amend = false, bool push = false, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        VcsController vcs = window.Vcs;
        await vcs.Opened;
        await vcs.RefreshAsync();
        IReadOnlyList<FileChange> chosen = paths is null
            ? [.. vcs.Changes.Where(c => c.Kind is not ChangeKind.Unversioned)]
            : [.. paths.Select(Resolve).Select(p => vcs.Changes.FirstOrDefault(c => c.Path == p) ?? throw new ArgumentException($"{Relative(p)} has no changes to commit"))];
        CommitResult result = await vcs.CommitAsync(chosen, message, amend, push);
        if (!result.Commit.Succeeded)
        {
            throw new InvalidOperationException(result.Commit.Message);
        }
        string made = result.Made is { } head ? $"{head.Short} {head.Subject}" : result.Commit.Message;
        return result.Push switch
        {
            null => made,
            { Succeeded: true } pushed => $"{made}; {pushed.Message}",
            { } refused => throw new InvalidOperationException($"Committed {made}, but the push did not go: {refused.Message}"),
        };
    });

    [McpServerTool(Name = "vcs_log", Title = "The history", ReadOnly = true), Description(
        "The newest commits of every branch, as the Git window's Log lists them, with their branches and tags. With a path, Show History's " +
        "instead: the commits from HEAD that changed that file (followed through its renames) or folder, each with what it did there (the " +
        "file under the path it had then); with startLine and endLine too, Show History for Selection's: the commits that changed those " +
        "lines of the file as the editor has it.")]
    public Task<IReadOnlyList<VcsCommit>> VcsLog(int count = 20, string? path = null,
        [Description("The first line of the file's to follow, from 1")] int? startLine = null,
        [Description("The last line, from 1")] int? endLine = null, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        await window.Vcs.Opened;
        Git repository = window.Vcs.Repository ?? throw new InvalidOperationException("The folder is not in a Git repository");
        int most = Math.Clamp(count, 1, 500);
        (int, int)? lines = (startLine, endLine) switch
        {
            (null, null) => null,
            (int first, int last) when path is not null && first >= 1 && last >= first => (first - 1, last - 1),
            _ => throw new ArgumentException("startLine and endLine come together, with a path, from 1 and the last at or after the first"),
        };
        if (path is null)
        {
            return (IReadOnlyList<VcsCommit>)[.. (await repository.LogAsync(most)).Select(c => Commit(c, null))];
        }
        HistoryScope scope = await window.HistoryScopeAsync(Resolve(path), lines);
        return [.. (await repository.HistoryAsync(scope, most)).Select(h => Commit(h.Commit, h.Changes))];

        VcsCommit Commit(CommitInfo commit, IReadOnlyList<FileChange>? changes) => new(
            commit.Short,
            commit.Subject,
            commit.Author,
            commit.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            commit.Refs,
            changes?.Select(c => new VcsChange(Relative(c.Path), c.Kind.ToString().ToLowerInvariant(), c.From is { } from ? Relative(from) : null)).ToList());
    });

    [McpServerTool(Name = "vcs_checkout", Title = "Check out a branch", Destructive = true), Description(
        "Checks out a branch (with create, a new one from the head), as the branch menu does; open files take what is on disk after.")]
    public Task<string> VcsCheckout(string branch, bool create = false, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        VcsController vcs = window.Vcs;
        await vcs.Opened;
        VcsOutcome switched = create ? await vcs.NewBranchAsync(branch) : await vcs.CheckoutAsync(branch);
        if (switched.Problem is { } failed)
        {
            throw new InvalidOperationException(failed);
        }
        return $"On {vcs.Branch}";
    });

    [McpServerTool(Name = "vcs_stash", Title = "Stash or unstash changes", Destructive = true), Description(
        "Rider's Stash Changes and Unstash Changes. action: list (the stashes, the newest first, by index), push (every "
        + "uncommitted change, new files too, put away under message, open files saved first), pop or apply (stash index's "
        + "changes back in the files; pop drops it after), drop (throw stash index away).")]
    public Task<IReadOnlyList<StashSummary>> VcsStash(string action = "list", string message = "", int index = 0, CancellationToken cancel = default) => OnUi<IReadOnlyList<StashSummary>>(cancel, async () =>
    {
        VcsController vcs = window.Vcs;
        await vcs.Opened;
        if (action != "list")
        {
            IReadOnlyList<StashInfo> before = await vcs.StashesAsync();
            StashInfo? stash = before.FirstOrDefault(s => s.Index == index);
            if (action != "push" && stash is null)
            {
                throw new InvalidOperationException($"No stash {index}; there are {before.Count}");
            }
            Editors.SaveAll();
            VcsOutcome done = action switch
            {
                "push" => await vcs.StashAsync(message),
                "pop" or "apply" => await vcs.UnstashAsync(stash!, action == "pop"),
                "drop" => await vcs.DropStashAsync(stash!),
                _ => throw new ArgumentException($"No stash action '{action}'; there are list, push, pop, apply and drop", nameof(action)),
            };
            if (done.Problem is { } failed)
            {
                throw new InvalidOperationException(failed);
            }
        }
        return [.. (await vcs.StashesAsync()).Select(s => new StashSummary(s.Index, s.Message, s.Time))];
    });

    // Packages

    [McpServerTool(Name = "nuget_search", Title = "Search NuGet", ReadOnly = true), Description(
        "Packages a query finds on the feeds of the folder's nuget.config files, as the NuGet window searches them: every enabled feed, or the " +
        "one named, with each package's newest version, its downloads and the feeds that have it. A feed that does not answer fails the search " +
        "only when no feed does.")]
    public Task<IReadOnlyList<PackageSummary>> NuGetSearch(string query, bool prerelease = false,
        [Description("A feed's name, as the NuGet window's Sources tab lists it; every enabled feed when none is given.")] string? feed = null,
        CancellationToken cancel = default) => OnUi(cancel, async () =>
        await (await window.NuGet.FeedAsync(feed)).SearchAsync(query, prerelease, cancel));

    [McpServerTool(Name = "nuget_install", Title = "Install a package", Destructive = true), Description(
        "Adds a package to a project (its name or .csproj path) at a version (the newest on the folder's enabled feeds when none is given), or " +
        "moves it to that version, through dotnet add package as the NuGet window does, its restore using the feeds and credentials of the " +
        "folder's nuget.config files.")]
    public Task<string> NuGetInstall(string project, string id, string? version = null, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        ProjectModel chosen = Project(project);
        IPackageFeed feeds = await window.NuGet.FeedAsync(null);
        string at = version ?? (await feeds.VersionsAsync(id, prerelease: false, cancel)).FirstOrDefault()
            ?? throw new ArgumentException($"{feeds.Name} has no package {id}");
        PackageManager manager = new();
        List<string> said = [];
        manager.Line += (_, line) => said.Add(line);
        bool done = await manager.InstallAsync(chosen.Path, id, at);
        Session.ReadAgain();
        return done ? $"{id} {at} is in {chosen.Name}" : throw new InvalidOperationException(string.Join('\n', said.TakeLast(15)));
    });

    [McpServerTool(Name = "nuget_remove", Title = "Remove a package", Destructive = true), Description("Takes a package out of a project, through dotnet remove package.")]
    public Task<string> NuGetRemove(string project, string id, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        ProjectModel chosen = Project(project);
        PackageManager manager = new();
        List<string> said = [];
        manager.Line += (_, line) => said.Add(line);
        bool done = await manager.RemoveAsync(chosen.Path, id);
        Session.ReadAgain();
        return done ? $"{id} is out of {chosen.Name}" : throw new InvalidOperationException(string.Join('\n', said.TakeLast(15)));
    });

    // The profiler

    [McpServerTool(Name = "profile", Title = "Profile a configuration", Destructive = true), Description(
        "Builds and runs a run configuration (the chosen one if none is named) under the sampling profiler from its first instruction, as " +
        "Profile does, until it ends by itself or, with seconds, for that long (then the snapshot is taken and the program goes on). " +
        "Returns the run's length, the hot spots by their own time, and the hot path of the busiest thread; the Performance Profiler window " +
        "shows the whole snapshot.")]
    public Task<ProfileInfo> Profile(
        string? configuration = null,
        [Description(Arguments)] string? arguments = null,
        [Description("How long to profile before the snapshot; the program's whole run when not given")] int? seconds = null,
        [Description("How many hot spots to return")] int top = 25,
        [Description("With seconds: a memory snapshot too, the heap's types by size, taken just before the CPU snapshot")] bool memory = false, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        Choose(configuration);
        if (await window.ProfileAsync(arguments).WaitAsync(cancel) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }
        Passenger.Profiler.ProfilingSession running = Session.Profiling!;
        IReadOnlyList<HeapTypeInfo>? heap = null;
        if (seconds is { } limit)
        {
            await Until(() => running.HasExited, TimeSpan.FromSeconds(Math.Clamp(limit, 1, 3600)), cancel);
            if (memory && !running.HasExited && await window.MemorySnapshotAsync() is { } taken)
            {
                heap = [.. taken.Types.Take(Math.Clamp(top, 1, 200)).Select(t => new HeapTypeInfo(t.Name, t.Count, t.Bytes))];
            }
        }
        else
        {
            await Until(() => running.HasExited, TimeSpan.FromMinutes(10), cancel);
        }
        SnapshotRead read = await window.SnapshotAsync();
        Passenger.Profiler.Snapshot snapshot = read.Snapshot ?? throw new InvalidOperationException(read.Message);
        List<ProfileFrame> path = [];
        if (snapshot.Threads.FirstOrDefault() is { } busiest)
        {
            path.Add(Frame(busiest.Name, busiest.Total, busiest.Self));
            for (Passenger.Profiler.CallNode at = busiest; at.Children.Count > 0 && path.Count < 80;)
            {
                at = at.Ordered[0];
                path.Add(Frame(at.Name, at.Total, at.Self));
            }
        }
        return new ProfileInfo(
            Math.Round(snapshot.Duration.TotalMilliseconds),
            snapshot.Samples,
            snapshot.Threads.Count,
            [.. snapshot.HotSpots.Take(Math.Clamp(top, 1, 200)).Select(h => Frame(h.Name, h.Total, h.Self))],
            path,
            snapshot.TracePath,
            heap);

        ProfileFrame Frame(string method, int total, int self) =>
            new(method, (int)Math.Round(snapshot.Milliseconds(total)), (int)Math.Round(snapshot.Milliseconds(self)));
    });

    // The terminal

    [McpServerTool(Name = "terminal_run", Title = "Run in the terminal", Destructive = true), Description(
        "Types a command into the IDE's Terminal window (opening it, and its shell, if need be) and presses Enter, as the person would, then waits " +
        "until the shell shows its prompt again, or the output has been quiet for two seconds (or waitSeconds), and returns what the command " +
        "printed. The person sees it run. While the tab shown runs a program (a run configuration that uses the terminal), the line goes " +
        "to that program, as typed.")]
    public Task<string> TerminalRun(string command, int waitSeconds = 15, CancellationToken cancel = default) => OnUi(cancel, async () =>
    {
        TerminalSession shell = await Terminal(cancel);
        TerminalScreen screen = shell.Screen;
        // By its number, which stays the line's own as the scrollback's oldest drop past its limit.
        long before = screen.ScreenTop + screen.CursorRow;
        string prompt = Prompt(screen);
        int version = screen.Version;
        shell.Write(command + "\r");
        DateTime until = DateTime.UtcNow.AddSeconds(Math.Clamp(waitSeconds, 1, 600));
        DateTime last = DateTime.UtcNow;
        bool answered = false;
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(100, cancel);
            if (screen.Version != version)
            {
                version = screen.Version;
                last = DateTime.UtcNow;
                answered = true;
                continue;
            }
            // Its prompt back below it ends a command; one that leaves another prompt (a cd) or none (a server) ends once quiet.
            bool prompted = prompt.Length > 0 && screen.ScreenTop + screen.CursorRow > before && Prompt(screen) == prompt;
            if (answered && DateTime.UtcNow - last > (prompted ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromSeconds(2)))
            {
                break;
            }
        }
        long end = screen.ScreenTop + screen.CursorRow;
        List<string> lines = [];
        for (long line = Math.Max(before, screen.FirstLine); line <= end; line++)
        {
            lines.Add(screen.LineAt(line).Text());
        }
        return string.Join('\n', lines).TrimEnd();
    });

    [McpServerTool(Name = "terminal_screen", Title = "What the terminal shows", ReadOnly = true), Description(
        "The IDE terminal's screen as it stands in the tab shown (a full-screen program's included, and a run configuration's program), " +
        "with the last lines of the scrollback above it.")]
    public Task<string> TerminalScreen(int scrollback = 20, CancellationToken cancel = default) => OnUi(cancel, () =>
    {
        if (window.TerminalPanel.Active is not { } shell)
        {
            throw new InvalidOperationException("The terminal has no shell running");
        }
        TerminalScreen screen = shell.Screen;
        IEnumerable<string> above = screen.Scrollback.Skip(Math.Max(0, screen.Scrollback.Count - Math.Max(0, scrollback))).Select(l => l.Text());
        return Task.FromResult(string.Join('\n', above.Concat(screen.Lines.Select(l => l.Text()))).TrimEnd());
    });

    private async Task<TerminalSession> Terminal(CancellationToken cancel)
    {
        if (window.TerminalPanel.Active is { IsRunning: true } running)
        {
            return running;
        }
        window.ShowToolWindow("terminal");
        TerminalSession shell = window.TerminalPanel.Active is { IsRunning: true } started ? started : await window.TerminalPanel.NewSessionAsync();
        // Its first prompt before anything is typed: output alone is not it, since ConPTY clears the screen before the shell starts.
        DateTime limit = DateTime.UtcNow.AddSeconds(20);
        DateTime changed = DateTime.UtcNow;
        int version = -1;
        while (DateTime.UtcNow < limit && (Prompt(shell.Screen).Length == 0 || DateTime.UtcNow - changed < TimeSpan.FromMilliseconds(300)))
        {
            if (shell.Screen.Version != version)
            {
                version = shell.Screen.Version;
                changed = DateTime.UtcNow;
            }
            await Task.Delay(50, cancel);
        }
        return shell;
    }

    /// <summary>The text before the cursor on its line, which at a shell waiting for a command is its prompt.</summary>
    private static string Prompt(TerminalScreen screen) =>
        screen.IsAlternate ? string.Empty : screen.Lines[screen.CursorRow].Text(0, screen.CursorColumn);

    private ProjectModel Project(string project)
    {
        IReadOnlyList<ProjectModel> projects = Session.Projects;
        string full = Resolve(project);
        return projects.FirstOrDefault(p => p.Path == full || p.Name.Equals(project, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No project {project}; the folder has {string.Join(", ", projects.Select(p => p.Name))}");
    }
}

public sealed record VcsStatus(string? Branch, IReadOnlyList<VcsChange> Changes, VcsConflicts? Conflicts = null);

/// <summary>What stopped for conflicts, the branches yours and theirs are, and the files in conflict.</summary>
public sealed record VcsConflicts(string Operation, string? Yours, string? Theirs, IReadOnlyList<VcsConflict> Files);

/// <summary>A file in conflict, and whether yours and theirs modified or deleted it.</summary>
public sealed record VcsConflict(string Path, string Yours, string Theirs);

public sealed record VcsChange(string Path, string Kind, string? From);

/// <summary>A commit as vcs_log lists it; with a path, what it did there.</summary>
public sealed record VcsCommit(string Hash, string Subject, string Author, string Date, IReadOnlyList<string> Refs, IReadOnlyList<VcsChange>? Changes = null);

/// <summary>A frame of a profile: a method, the milliseconds with it on the stack, and those in it alone.</summary>
public sealed record ProfileFrame(string Method, int TotalMs, int SelfMs);

public sealed record ProfileInfo(double DurationMs, int Samples, int Threads, IReadOnlyList<ProfileFrame> HotSpots, IReadOnlyList<ProfileFrame> HotPath, string Trace, IReadOnlyList<HeapTypeInfo>? Heap = null);

/// <summary>A type on the heap of a memory snapshot: its live objects and their bytes.</summary>
public sealed record HeapTypeInfo(string Type, int Count, long Bytes);

/// <summary>A stash as the vcs_stash tool lists it.</summary>
public sealed record StashSummary(int Index, string Message, DateTimeOffset Time);
