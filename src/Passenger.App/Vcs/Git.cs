using System.Diagnostics;
using System.Text;
using Passenger.Core;

namespace Passenger.App.Vcs;

/// <summary>How a file differs from the last commit, as Rider's Commit window colours it.</summary>
public enum ChangeKind
{
    Modified,
    Added,
    Deleted,
    Renamed,
    Unversioned,
    Conflicted,
}

/// <summary>A changed file of the working tree: its path (absolute), how it changed, and where it came from if renamed.</summary>
public sealed record FileChange(string Path, ChangeKind Kind, string? From = null)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>A commit as the log lists it: its hash and parents, who made it and when, the branches and tags at it,
/// and its subject.</summary>
public sealed record CommitInfo(string Hash, IReadOnlyList<string> Parents, string Author, string Email, DateTimeOffset Time, IReadOnlyList<string> Refs, string Subject)
{
    public string Short => Hash[..Math.Min(8, Hash.Length)];
}

/// <summary>A line as Git blame has it: the commit that last changed it (all zeros for a change not committed yet),
/// who made it and when, and its subject.</summary>
public sealed record BlameLine(string Hash, string Author, DateTimeOffset Time, string Summary)
{
    public bool IsCommitted => Hash.Any(c => c != '0');

    public string Short => Hash[..Math.Min(8, Hash.Length)];
}

/// <summary>What one status read found, together: the branch checked out (null on a detached head), the commit HEAD
/// names (null before the first), and the changed and new files.</summary>
public sealed record GitStatus(string? Branch, string? Head, IReadOnlyList<FileChange> Changes);

/// <summary>A stash: its place in the list (stash@{Index}), what it says, and when it was made.</summary>
public sealed record StashInfo(int Index, string Message, DateTimeOffset Time);

/// <summary>What `git` says, and whether it said it without failing.</summary>
public sealed record GitResult(int Code, string Output, string Error)
{
    public bool Succeeded => Code == 0;

    /// <summary>What to tell the person when it failed: git's own words.</summary>
    public string Message => (Error.Trim().Length > 0 ? Error : Output).Trim();
}

/// <summary>A Git working tree, through the `git` executable as Rider drives it: its status, branches, a file as the
/// last commit has it, and committing, pushing and switching. Every call runs git in the tree's root, its output read
/// as UTF-8, never through a shell.</summary>
public sealed class Git
{
    private Git(string root) => Root = root;

    /// <summary>The working tree's top folder.</summary>
    public string Root { get; }

    /// <summary>The working tree a folder is in, or null when it is in none (or git is not there).</summary>
    public static async Task<Git?> OpenAsync(string folder)
    {
        GitResult top = await RunAsync(folder, ["rev-parse", "--show-toplevel"]);
        return top.Succeeded && top.Output.Trim() is { Length: > 0 } root
            ? new Git(Workspace.PathNames.Canonical(root))
            : null;
    }

    /// <summary>Makes a folder a working tree, as Rider's Create Git Repository does.</summary>
    public static Task<GitResult> InitAsync(string folder) => RunAsync(folder, ["init"]);

    /// <summary>The branch, HEAD's commit, and the changed and unversioned files (ignored ones left out, renames with
    /// where they came from), in one git so the three agree.</summary>
    public async Task<GitStatus> StatusAsync()
    {
        GitResult status = await RunAsync(Root, ["status", "--porcelain=v2", "-z", "--branch", "--no-ahead-behind", "--untracked-files=all"]);
        if (!status.Succeeded)
        {
            throw new InvalidOperationException(status.Message);
        }
        string? branch = null;
        string? head = null;
        List<FileChange> changes = [];
        string[] entries = status.Output.Split('\0');
        for (int i = 0; i < entries.Length; i++)
        {
            string entry = entries[i];
            if (entry.Length < 2)
            {
                continue;
            }
            switch (entry[0])
            {
                case '#' when entry.StartsWith("# branch.oid ", StringComparison.Ordinal):
                    head = entry[13..] is "(initial)" ? null : entry[13..];
                    break;
                case '#' when entry.StartsWith("# branch.head ", StringComparison.Ordinal):
                    branch = entry[14..] is "(detached)" ? null : entry[14..];
                    break;
                case '1':
                    // "1 XY sub mH mI mW hH hI path"
                    string[] ordinary = entry.Split(' ', 9);
                    changes.Add(new FileChange(Full(ordinary[8]), Kind(ordinary[1])));
                    break;
                case '2':
                    // "2 XY sub mH mI mW hH hI Xscore path", then the path it came from as its own entry.
                    string[] renamed = entry.Split(' ', 10);
                    changes.Add(new FileChange(Full(renamed[9]), ChangeKind.Renamed, Full(entries[++i])));
                    break;
                case 'u':
                    changes.Add(new FileChange(Full(entry.Split(' ', 11)[10]), ChangeKind.Conflicted));
                    break;
                case '?':
                    changes.Add(new FileChange(Full(entry[2..]), ChangeKind.Unversioned));
                    break;
            }
        }
        return new GitStatus(branch, head, changes);

        static ChangeKind Kind(string xy) =>
            xy.Contains('D') ? ChangeKind.Deleted : xy[0] == 'A' ? ChangeKind.Added : ChangeKind.Modified;
    }

    /// <summary>Every ref and the commit each names: with HEAD's commit, what changes when the history does (a commit, a
    /// checkout, a fetch), and not when a file is saved.</summary>
    public async Task<string> RefsAsync()
    {
        GitResult refs = await RunAsync(Root, ["for-each-ref", "--format=%(objectname) %(refname)"]);
        return refs.Output;
    }

    /// <summary>The branch checked out, or null on a detached head.</summary>
    public async Task<string?> BranchAsync()
    {
        GitResult branch = await RunAsync(Root, ["branch", "--show-current"]);
        return branch.Succeeded && branch.Output.Trim() is { Length: > 0 } name ? name : null;
    }

    /// <summary>The local branches, the current one first.</summary>
    public async Task<IReadOnlyList<string>> BranchesAsync()
    {
        GitResult branches = await RunAsync(Root, ["for-each-ref", "--format=%(HEAD)%(refname:short)", "refs/heads"]);
        if (!branches.Succeeded)
        {
            return [];
        }
        List<string> lines = [.. branches.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)];
        return [.. lines.Where(l => l.StartsWith('*')).Select(l => l[1..]), .. lines.Where(l => !l.StartsWith('*')).Select(l => l.TrimStart(' '))];
    }

    /// <summary>The history of every branch, newest first with a branch's commits together (as Rider's log has it),
    /// at most this many.</summary>
    public async Task<IReadOnlyList<CommitInfo>> LogAsync(int most = 2000)
    {
        GitResult log = await RunAsync(Root, ["log", "--all", "--topo-order", $"--max-count={most}", "--format=%H%x1f%P%x1f%an%x1f%ae%x1f%at%x1f%D%x1f%s%x1e"]);
        if (!log.Succeeded)
        {
            // A branch with no commits yet has no history to read.
            return [];
        }
        List<CommitInfo> commits = [];
        foreach (string record in log.Output.Split('\x1e'))
        {
            string[] field = record.Trim('\n').Split('\x1f');
            if (field.Length < 7)
            {
                continue;
            }
            commits.Add(new CommitInfo(
                field[0],
                field[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                field[2],
                field[3],
                DateTimeOffset.FromUnixTimeSeconds(long.Parse(field[4], System.Globalization.CultureInfo.InvariantCulture)),
                [.. field[5].Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(r => r.StartsWith("HEAD -> ", StringComparison.Ordinal) ? r[8..] : r)],
                field[6]));
        }
        return commits;
    }

    /// <summary>A commit's whole message.</summary>
    public async Task<string> MessageAsync(string hash)
    {
        GitResult show = await RunAsync(Root, ["show", "-s", "--format=%B", "--end-of-options", hash]);
        return show.Succeeded ? show.Output.TrimEnd() : string.Empty;
    }

    /// <summary>The files a commit changed against its first parent (all of them for the first commit), renames found.</summary>
    public async Task<IReadOnlyList<FileChange>> CommitFilesAsync(string hash)
    {
        GitResult files = await RunAsync(Root, ["diff-tree", "--no-commit-id", "--name-status", "-r", "-z", "-M", "--root", "--end-of-options", hash]);
        if (!files.Succeeded)
        {
            return [];
        }
        List<FileChange> changes = [];
        string[] parts = files.Output.Split('\0');
        for (int i = 0; i + 1 < parts.Length; i++)
        {
            string status = parts[i];
            if (status.Length == 0)
            {
                continue;
            }
            switch (status[0])
            {
                case 'R' or 'C' when i + 2 < parts.Length:
                    changes.Add(new FileChange(Full(parts[i + 2]), ChangeKind.Renamed, Full(parts[i + 1])));
                    i += 2;
                    break;
                case 'A':
                    changes.Add(new FileChange(Full(parts[++i]), ChangeKind.Added));
                    break;
                case 'D':
                    changes.Add(new FileChange(Full(parts[++i]), ChangeKind.Deleted));
                    break;
                default:
                    changes.Add(new FileChange(Full(parts[++i]), ChangeKind.Modified));
                    break;
            }
        }
        return changes;
    }

    /// <summary>Git blame for a file, line by line, over the text given (the editor's, unsaved changes and all), so a
    /// line typed since is one not committed yet; null for a file Git does not track.</summary>
    public async Task<IReadOnlyList<BlameLine>?> BlameAsync(string path, string text)
    {
        GitResult blame = await RunAsync(Root, ["blame", "--porcelain", "--contents", "-", "--", Relative(path)], text);
        if (!blame.Succeeded)
        {
            return null;
        }
        Dictionary<string, (string Author, DateTimeOffset Time, string Summary)> commits = [];
        List<BlameLine> lines = [];
        string? hash = null;
        (string Author, long Time, string Summary) reading = (string.Empty, 0, string.Empty);
        foreach (string line in blame.Output.Split('\n'))
        {
            if (line.StartsWith('\t'))
            {
                // The line itself: the commit's fields are all read by now.
                if (hash is not null)
                {
                    if (!commits.TryGetValue(hash, out (string Author, DateTimeOffset Time, string Summary) commit))
                    {
                        commits[hash] = commit = (reading.Author, DateTimeOffset.FromUnixTimeSeconds(reading.Time), reading.Summary);
                    }
                    lines.Add(new BlameLine(hash, commit.Author, commit.Time, commit.Summary));
                }
                continue;
            }
            string[] words = line.Split(' ', 2);
            if (words[0].Length == 40 && words[0].All(char.IsAsciiHexDigit))
            {
                hash = words[0];
                reading = (string.Empty, 0, string.Empty);
                continue;
            }
            string value = words.Length > 1 ? words[1] : string.Empty;
            switch (words[0])
            {
                case "author":
                    reading.Author = value;
                    break;
                case "author-time" when long.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out long seconds):
                    reading.Time = seconds;
                    break;
                case "summary":
                    reading.Summary = value;
                    break;
            }
        }
        return lines;
    }

    /// <summary>A file's text at a revision (a hash, a branch, HEAD), or null when it had no such file.</summary>
    public async Task<string?> TextAtAsync(string revision, string path)
    {
        GitResult show = await RunAsync(Root, ["show", "--end-of-options", $"{revision}:{Relative(path)}"]);
        return show.Succeeded ? show.Output : null;
    }

    /// <summary>Files' texts at a revision, read by one git however many there are (cat-file's batch), each null where
    /// the revision has no such file, as <see cref="TextAtAsync"/> would read them one by one.</summary>
    public async Task<IReadOnlyDictionary<string, string?>> TextsAtAsync(string revision, IReadOnlyList<string> paths)
    {
        Dictionary<string, string?> texts = [];
        List<string> batched = [];
        foreach (string path in paths.Distinct())
        {
            if (!IsInside(path))
            {
                // A path outside the tree ends the whole batch ("outside repository").
                texts[path] = null;
            }
            else if (path.Contains('\n') || path.Contains('\r'))
            {
                // The batch takes a name per line.
                texts[path] = await TextAtAsync(revision, path);
            }
            else
            {
                batched.Add(path);
            }
        }
        if (batched.Count == 0)
        {
            return texts;
        }
        byte[] output = await OutputAsync(Root, ["cat-file", "--batch"], string.Concat(batched.Select(p => $"{revision}:{Relative(p)}\n")));
        int at = 0;
        foreach (string path in batched)
        {
            texts[path] = null;
            int end = Array.IndexOf(output, (byte)'\n', at);
            if (end < 0)
            {
                continue;
            }
            // "<object> <type> <size>", the object's bytes and a line break; or "<name> missing".
            string[] header = Encoding.UTF8.GetString(output, at, end - at).Split(' ');
            at = end + 1;
            if (header.Length == 3 && header[0].All(char.IsAsciiHexDigit) && int.TryParse(header[2], System.Globalization.CultureInfo.InvariantCulture, out int size)
                && size <= output.Length - at)
            {
                if (header[1] == "blob")
                {
                    // Read as RunAsync reads git's output: UTF-8, a byte order mark taken as the file's.
                    using StreamReader reader = new(new MemoryStream(output, at, size, writable: false), Encoding.UTF8);
                    texts[path] = reader.ReadToEnd();
                }
                at += size + 1;
            }
        }
        return texts;
    }

    /// <summary>Whether a path is in the working tree, where git has a last commit's text for it.</summary>
    public bool IsInside(string path)
    {
        string relative = Relative(path);
        return !System.IO.Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith("../", StringComparison.Ordinal);
    }

    /// <summary>Rider's Update Project: the branch's upstream merged in (or rebased onto, as the person's pull.rebase
    /// says).</summary>
    public Task<GitResult> PullAsync() => RunAsync(Root, ["pull", "--no-edit"]);

    public Task<GitResult> FetchAsync() => RunAsync(Root, ["fetch", "--all", "--prune"]);

    /// <summary>The last commit's message, or null before the first commit.</summary>
    public async Task<string?> LastMessageAsync()
    {
        GitResult log = await RunAsync(Root, ["log", "-1", "--format=%B"]);
        return log.Succeeded ? log.Output.TrimEnd() : null;
    }

    /// <summary>Commits these files' changes (new ones added first) with a message, as Rider's Commit does; amending
    /// the last commit when asked.</summary>
    public async Task<GitResult> CommitAsync(IReadOnlyList<string> paths, string message, bool amend = false)
    {
        string[] relative = [.. paths.Select(Relative).Distinct()];
        // No paths is an amend of the message alone: "add -A --" with none would add the whole tree.
        if (relative.Length > 0)
        {
            GitResult add = await RunAsync(Root, ["add", "-A", "--", .. relative.Select(Literal)]);
            if (!add.Succeeded)
            {
                return add;
            }
        }
        string file = System.IO.Path.GetTempFileName();
        await File.WriteAllTextAsync(file, message);
        try
        {
            string[] only = relative.Length > 0 ? ["--only", "--", .. relative.Select(Literal)] : [];
            return await RunAsync(Root, ["commit", "-F", file, .. amend ? new[] { "--amend" } : [], .. only]);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Pushes the branch, setting its upstream on the first push.</summary>
    public async Task<GitResult> PushAsync()
    {
        GitResult upstream = await RunAsync(Root, ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"]);
        if (upstream.Succeeded)
        {
            return await RunAsync(Root, ["push"]);
        }
        return await BranchAsync() is { } branch
            ? await RunAsync(Root, ["push", "-u", "origin", branch])
            : new GitResult(1, string.Empty, "A detached head has no branch to push");
    }

    /// <summary>Checks a branch out. What follows --end-of-options (here and wherever a name or a hash comes from outside) is
    /// a name, never an option, whoever gave it: an agent's "--orphan=x" is a branch git does not have, not an order to
    /// empty the working tree.</summary>
    public Task<GitResult> CheckoutAsync(string branch) => RunAsync(Root, ["switch", "--end-of-options", branch]);

    /// <summary>The stashes, the newest first.</summary>
    public async Task<IReadOnlyList<StashInfo>> StashesAsync()
    {
        GitResult list = await RunAsync(Root, ["stash", "list", "--format=%gd%x1f%gs%x1f%ct"]);
        if (!list.Succeeded)
        {
            return [];
        }
        List<StashInfo> stashes = [];
        foreach (string line in list.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('\x1f');
            if (parts.Length == 3 && parts[0].StartsWith("stash@{", StringComparison.Ordinal) && int.TryParse(parts[0][7..^1], out int index)
                && long.TryParse(parts[2], out long seconds))
            {
                stashes.Add(new StashInfo(index, parts[1], DateTimeOffset.FromUnixTimeSeconds(seconds)));
            }
        }
        return stashes;
    }

    /// <summary>Puts the uncommitted changes, new files too, away in a stash; the files go back to the head's.</summary>
    public Task<GitResult> StashAsync(string message) =>
        RunAsync(Root, ["stash", "push", "--include-untracked", .. message.Length > 0 ? new[] { "-m", message } : []]);

    /// <summary>Applies a stash to the files, and with <paramref name="pop"/> drops it once it applied.</summary>
    public Task<GitResult> UnstashAsync(int index, bool pop) => RunAsync(Root, ["stash", pop ? "pop" : "apply", $"stash@{{{index}}}"]);

    public Task<GitResult> DropStashAsync(int index) => RunAsync(Root, ["stash", "drop", $"stash@{{{index}}}"]);

    /// <summary>A new branch from the head, checked out. -c takes the next word as the name, whatever it is, and git
    /// refuses a name that starts with a dash.</summary>
    public Task<GitResult> NewBranchAsync(string name) => RunAsync(Root, ["switch", "-c", name]);

    /// <summary>A branch made at a commit and checked out.</summary>
    public Task<GitResult> NewBranchAtAsync(string name, string hash) => RunAsync(Root, ["switch", "-c", name, "--end-of-options", hash]);

    /// <summary>A commit checked out on its own, with no branch (a detached head), as Rider's Checkout Revision does.</summary>
    public Task<GitResult> CheckoutRevisionAsync(string hash) => RunAsync(Root, ["switch", "--detach", "--end-of-options", hash]);

    public Task<GitResult> CherryPickAsync(string hash) => RunAsync(Root, ["cherry-pick", "--end-of-options", hash]);

    /// <summary>A commit undone by a new one, with git's own message.</summary>
    public Task<GitResult> RevertAsync(string hash) => RunAsync(Root, ["revert", "--no-edit", "--end-of-options", hash]);

    /// <summary>The branch moved to a commit: soft keeps the changes staged, mixed keeps them in the files, hard drops them.</summary>
    public Task<GitResult> ResetAsync(string hash, ResetMode mode) => RunAsync(Root, ["reset", $"--{mode.ToString().ToLowerInvariant()}", "--end-of-options", hash]);

    /// <summary>Puts files back as the last commit has them (Rider's Rollback); new files are deleted.</summary>
    public async Task<GitResult> RollbackAsync(IReadOnlyList<FileChange> changes)
    {
        foreach (FileChange added in changes.Where(c => c.Kind is ChangeKind.Unversioned))
        {
            File.Delete(added.Path);
        }
        string[] tracked = [.. changes.Where(c => c.Kind is not ChangeKind.Unversioned).SelectMany(c => c.From is { } from ? new[] { c.Path, from } : [c.Path]).Select(Relative)];
        if (tracked.Length == 0)
        {
            return new GitResult(0, string.Empty, string.Empty);
        }
        GitResult restore = await RunAsync(Root, ["restore", "--source=HEAD", "--staged", "--worktree", "--", .. tracked.Select(Literal)]);
        return restore;
    }

    public string Relative(string path) => System.IO.Path.GetRelativePath(Root, path).Replace('\\', '/');

    /// <summary>A file's name as git takes it for that file alone, never a pattern: "[ab].cs" is not also "a.cs", nor is
    /// a name starting ":" pathspec magic. (The environment's GIT_LITERAL_PATHSPECS would also stop a stash's own magic.)</summary>
    private static string Literal(string relative) => $":(literal){relative}";

    private string Full(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relative));

    private static async Task<GitResult> RunAsync(string folder, IReadOnlyList<string> arguments, string? input = null)
    {
        if (Start(folder, arguments) is not { } start)
        {
            return new GitResult(127, string.Empty, OperatingSystem.IsMacOS() ? "git is not installed: xcode-select --install installs it" : "git is not installed");
        }
        try
        {
            ChildResult result = await ChildProcesses.RunAsync(start, input);
            return new GitResult(result.ExitCode, result.Output, result.Error);
        }
        catch (System.ComponentModel.Win32Exception missing)
        {
            return new GitResult(127, string.Empty, $"git is not installed: {missing.Message}");
        }
    }

    /// <summary>What git writes, as bytes, for output that counts its lengths in them (cat-file's objects); nothing when
    /// git is not there or fails to start.</summary>
    private static async Task<byte[]> OutputAsync(string folder, IReadOnlyList<string> arguments, string input)
    {
        if (Start(folder, arguments) is not { } start)
        {
            return [];
        }
        // Its input stays open for the names written to it.
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        try
        {
            using Process process = ChildProcesses.Start(start);
            using MemoryStream output = new();
            Task copied = process.StandardOutput.BaseStream.CopyToAsync(output);
            Task<string> error = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
            await process.WaitForExitAsync();
            await copied;
            await error;
            return output.ToArray();
        }
        catch (Exception failed) when (failed is System.ComponentModel.Win32Exception or IOException)
        {
            return [];
        }
    }

    /// <summary>How git is started for every call, or null when it is not installed.</summary>
    private static ProcessStartInfo? Start(string folder, IReadOnlyList<string> arguments)
    {
        if (Programs.Git() is not { } git)
        {
            return null;
        }
        ProcessStartInfo start = ChildProcesses.Command(git, arguments, folder);
        // Git never stops to ask on a terminal: it has none (ChildProcesses), and this keeps it from looking for one.
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        // A status or a diff read in the background never takes the index's lock, so a stash, a commit or a checkout
        // started at the same moment does not fail on it.
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        return start;
    }
}

/// <summary>How Reset Current Branch to Here treats the changes after the commit.</summary>
public enum ResetMode
{
    Soft,
    Mixed,
    Hard,
}
