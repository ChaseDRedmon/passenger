using System.ComponentModel;
using ModelContextProtocol.Server;
using Passenger.App.Packages;
using Passenger.App.Vcs;
using Passenger.App.Workspace;
using Passenger.Terminal;

namespace Passenger.App.Mcp;

/// <summary>The daily loop's tools: Git as the Commit and Git windows drive it, NuGet as its window does, and the IDE's
/// terminal, each through the window's own code so the agent and the person see one state.</summary>
public sealed partial class PassengerTools
{
    // Version control

    [McpServerTool(Name = "vcs_status", Title = "Changed files", ReadOnly = true), Description(
        "The branch and the files changed since the last commit (modified, added, deleted, renamed, unversioned, conflicted), as the Commit window lists them.")]
    public Task<VcsStatus> VcsStatus() => OnUi(async () =>
    {
        VcsController vcs = window.Vcs;
        await vcs.RefreshAsync();
        if (vcs.Repository is null)
        {
            throw new InvalidOperationException("The folder is not in a Git repository");
        }
        return new VcsStatus(vcs.Branch, [.. vcs.Changes.Select(c => new VcsChange(Relative(c.Path), c.Kind.ToString().ToLowerInvariant(), c.From is { } from ? Relative(from) : null))]);
    });

    [McpServerTool(Name = "vcs_diff", Title = "A file's changes", ReadOnly = true), Description(
        "What changed in a file since the last commit, as a unified diff: the editor's text when the file is open (unsaved changes included), else the file on disk.")]
    public Task<string> VcsDiff(string path) => OnUi(async () =>
    {
        string full = Resolve(path);
        string head = await window.Vcs.HeadTextAsync(full) ?? string.Empty;
        string now = window.EditorArea.Documents.FirstOrDefault(d => d.Path == full)?.Text.Snapshot.ToString()
            ?? (File.Exists(full) ? File.ReadAllText(full) : string.Empty);
        string diff = LineDiff.Unified(head, now, Relative(full).Replace('\\', '/'));
        return diff.Length == 0 ? "No changes" : diff;
    });

    [McpServerTool(Name = "vcs_commit", Title = "Commit", Destructive = true), Description(
        "Commits files with a message, as the Commit window does (every file saved first): the paths given, else every changed file that is not unversioned. " +
        "amend changes the last commit; push pushes after. Returns the new commit.")]
    public Task<string> VcsCommit(string message, IReadOnlyList<string>? paths = null, bool amend = false, bool push = false) => OnUi(async () =>
    {
        VcsController vcs = window.Vcs;
        await vcs.RefreshAsync();
        IReadOnlyList<FileChange> chosen = paths is null
            ? [.. vcs.Changes.Where(c => c.Kind is not ChangeKind.Unversioned)]
            : [.. paths.Select(Resolve).Select(p => vcs.Changes.FirstOrDefault(c => c.Path == p) ?? throw new ArgumentException($"{Relative(p)} has no changes to commit"))];
        if (!await vcs.CommitAsync(chosen, message, amend, push))
        {
            throw new InvalidOperationException(window.Status ?? "The commit failed");
        }
        CommitInfo head = (await vcs.Repository!.LogAsync(1))[0];
        return $"{head.Short} {head.Subject}" + (push ? $"; {window.Status}" : string.Empty);
    });

    [McpServerTool(Name = "vcs_log", Title = "The history", ReadOnly = true), Description(
        "The newest commits of every branch, as the Git window's Log lists them, with their branches and tags.")]
    public Task<IReadOnlyList<VcsCommit>> VcsLog(int count = 20) => OnUi(async () =>
    {
        Git repository = window.Vcs.Repository ?? throw new InvalidOperationException("The folder is not in a Git repository");
        return (IReadOnlyList<VcsCommit>)[.. (await repository.LogAsync(Math.Clamp(count, 1, 500))).Select(c =>
            new VcsCommit(c.Short, c.Subject, c.Author, c.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), c.Refs))];
    });

    [McpServerTool(Name = "vcs_checkout", Title = "Check out a branch", Destructive = true), Description(
        "Checks out a branch (with create, a new one from the head), as the branch menu does; open files take what is on disk after.")]
    public Task<string> VcsCheckout(string branch, bool create = false) => OnUi(async () =>
    {
        VcsController vcs = window.Vcs;
        if (create)
        {
            if (await vcs.NewBranchAsync(branch) is { } failed)
            {
                throw new InvalidOperationException(failed);
            }
        }
        else if (!await vcs.CheckoutAsync(branch))
        {
            throw new InvalidOperationException(window.Status ?? $"Could not check out {branch}");
        }
        return $"On {vcs.Branch}";
    });

    // Packages

    [McpServerTool(Name = "nuget_search", Title = "Search NuGet", ReadOnly = true), Description(
        "Packages on the NuGet window's feed (nuget.org) a query finds, with their newest versions and downloads.")]
    public Task<IReadOnlyList<PackageSummary>> NuGetSearch(string query, bool prerelease = false) => OnUi(async () =>
        await window.NuGet.Feed.SearchAsync(query, prerelease, CancellationToken.None));

    [McpServerTool(Name = "nuget_install", Title = "Install a package", Destructive = true), Description(
        "Adds a package to a project (its name or .csproj path) at a version (the newest when none is given), or moves it to that version, " +
        "through dotnet add package as the NuGet window does.")]
    public Task<string> NuGetInstall(string project, string id, string? version = null) => OnUi(async () =>
    {
        ProjectModel chosen = Project(project);
        string at = version ?? (await window.NuGet.Feed.VersionsAsync(id, prerelease: false, CancellationToken.None)).FirstOrDefault()
            ?? throw new ArgumentException($"{window.NuGet.Feed.Name} has no package {id}");
        PackageManager manager = new();
        List<string> said = [];
        manager.Line += (_, line) => said.Add(line);
        bool done = await manager.InstallAsync(chosen.Path, id, at);
        window.ReloadSolution();
        return done ? $"{id} {at} is in {chosen.Name}" : throw new InvalidOperationException(string.Join('\n', said.TakeLast(15)));
    });

    [McpServerTool(Name = "nuget_remove", Title = "Remove a package", Destructive = true), Description("Takes a package out of a project, through dotnet remove package.")]
    public Task<string> NuGetRemove(string project, string id) => OnUi(async () =>
    {
        ProjectModel chosen = Project(project);
        PackageManager manager = new();
        List<string> said = [];
        manager.Line += (_, line) => said.Add(line);
        bool done = await manager.RemoveAsync(chosen.Path, id);
        window.ReloadSolution();
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
        [Description("With seconds: a memory snapshot too, the heap's types by size, taken just before the CPU snapshot")] bool memory = false) => OnUi(async () =>
    {
        Choose(configuration);
        if (await window.ProfileAsync(arguments) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }
        Passenger.Profiler.ProfilingSession running = window.Profiling!;
        IReadOnlyList<HeapTypeInfo>? heap = null;
        if (seconds is { } limit)
        {
            await Until(() => running.HasExited, TimeSpan.FromSeconds(Math.Clamp(limit, 1, 3600)));
            if (memory && !running.HasExited && await window.MemorySnapshotAsync() is { } taken)
            {
                heap = [.. taken.Types.Take(Math.Clamp(top, 1, 200)).Select(t => new HeapTypeInfo(t.Name, t.Count, t.Bytes))];
            }
        }
        else
        {
            await Until(() => running.HasExited, TimeSpan.FromMinutes(10));
        }
        Passenger.Profiler.Snapshot snapshot = await window.SnapshotAsync() ?? window.ProfilerView.Snapshot
            ?? throw new InvalidOperationException(window.Status ?? "The run left no snapshot");
        List<ProfileFrame> path = [];
        if (snapshot.Threads.FirstOrDefault() is { } busiest)
        {
            path.Add(new ProfileFrame(busiest.Name, busiest.Total, busiest.Self));
            for (Passenger.Profiler.CallNode at = busiest; at.Children.Count > 0 && path.Count < 80;)
            {
                at = at.Ordered[0];
                path.Add(new ProfileFrame(at.Name, at.Total, at.Self));
            }
        }
        return new ProfileInfo(
            Math.Round(snapshot.Duration.TotalMilliseconds),
            snapshot.Samples,
            snapshot.Threads.Count,
            [.. snapshot.HotSpots.Take(Math.Clamp(top, 1, 200)).Select(h => new ProfileFrame(h.Name, h.Total, h.Self))],
            path,
            snapshot.TracePath,
            heap);
    });

    // The terminal

    [McpServerTool(Name = "terminal_run", Title = "Run in the terminal", Destructive = true), Description(
        "Types a command into the IDE's Terminal window (opening it, and its shell, if need be) and presses Enter, as the person would, then waits " +
        "until the output has been quiet for a moment (or waitSeconds) and returns what the command printed. The person sees it run.")]
    public Task<string> TerminalRun(string command, int waitSeconds = 15) => OnUi(async () =>
    {
        TerminalSession shell = await Terminal();
        int before = shell.Screen.Scrollback.Count + shell.Screen.CursorRow;
        int version = shell.Screen.Version;
        shell.Write(command + "\r");
        // Quiet: no new output for half a second, after some came.
        DateTime until = DateTime.UtcNow.AddSeconds(Math.Clamp(waitSeconds, 1, 600));
        DateTime last = DateTime.UtcNow;
        bool answered = false;
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(100);
            if (shell.Screen.Version != version)
            {
                version = shell.Screen.Version;
                last = DateTime.UtcNow;
                answered = true;
            }
            else if (answered && DateTime.UtcNow - last > TimeSpan.FromMilliseconds(500))
            {
                break;
            }
        }
        TerminalScreen screen = shell.Screen;
        int end = screen.Scrollback.Count + screen.CursorRow;
        IEnumerable<string> lines = Enumerable.Range(Math.Max(0, before), Math.Max(0, end - before + 1)).Select(i => screen.LineAt(i).Text());
        return string.Join('\n', lines).TrimEnd();
    });

    [McpServerTool(Name = "terminal_screen", Title = "What the terminal shows", ReadOnly = true), Description(
        "The IDE terminal's screen as it stands (a full-screen program's included), with the last lines of the scrollback above it.")]
    public Task<string> TerminalScreen(int scrollback = 20) => OnUi(() =>
    {
        if (window.TerminalPanel.Active is not { } shell)
        {
            throw new InvalidOperationException("The terminal has no shell running");
        }
        TerminalScreen screen = shell.Screen;
        IEnumerable<string> above = screen.Scrollback.Skip(Math.Max(0, screen.Scrollback.Count - Math.Max(0, scrollback))).Select(l => l.Text());
        return Task.FromResult(string.Join('\n', above.Concat(screen.Lines.Select(l => l.Text()))).TrimEnd());
    });

    private async Task<TerminalSession> Terminal()
    {
        if (window.TerminalPanel.Active is { IsRunning: true } running)
        {
            return running;
        }
        window.ShowToolWindow("terminal");
        TerminalSession shell = window.TerminalPanel.Active is { IsRunning: true } started ? started : await window.TerminalPanel.NewSessionAsync();
        // The shell's first prompt, before anything is typed at it.
        await Until(() => shell.Screen.Version > 0, TimeSpan.FromSeconds(10));
        await Task.Delay(300);
        return shell;
    }

    private ProjectModel Project(string project)
    {
        IReadOnlyList<ProjectModel> projects = window.Projects;
        string full = Resolve(project);
        return projects.FirstOrDefault(p => p.Path == full || p.Name.Equals(project, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No project {project}; the folder has {string.Join(", ", projects.Select(p => p.Name))}");
    }
}

public sealed record VcsStatus(string? Branch, IReadOnlyList<VcsChange> Changes);

public sealed record VcsChange(string Path, string Kind, string? From);

public sealed record VcsCommit(string Hash, string Subject, string Author, string Date, IReadOnlyList<string> Refs);

/// <summary>A frame of a profile: a method, the milliseconds with it on the stack, and those in it alone.</summary>
public sealed record ProfileFrame(string Method, int TotalMs, int SelfMs);

public sealed record ProfileInfo(double DurationMs, int Samples, int Threads, IReadOnlyList<ProfileFrame> HotSpots, IReadOnlyList<ProfileFrame> HotPath, string Trace, IReadOnlyList<HeapTypeInfo>? Heap = null);

/// <summary>A type on the heap of a memory snapshot: its live objects and their bytes.</summary>
public sealed record HeapTypeInfo(string Type, int Count, long Bytes);
