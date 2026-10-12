using System.Diagnostics;
using System.Text;

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

    /// <summary>The changed and unversioned files, ignored ones left out, renames with where they came from.</summary>
    public async Task<IReadOnlyList<FileChange>> StatusAsync()
    {
        GitResult status = await RunAsync(Root, ["status", "--porcelain=v2", "-z", "--untracked-files=all"]);
        if (!status.Succeeded)
        {
            throw new InvalidOperationException(status.Message);
        }
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
        return changes;

        static ChangeKind Kind(string xy) =>
            xy.Contains('D') ? ChangeKind.Deleted : xy[0] == 'A' ? ChangeKind.Added : ChangeKind.Modified;
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

    /// <summary>A file's text as the last commit has it; null when the commit has no such file (it is new).</summary>
    public async Task<string?> HeadTextAsync(string path)
    {
        GitResult show = await RunAsync(Root, ["show", $"HEAD:{Relative(path)}"]);
        return show.Succeeded ? show.Output : null;
    }

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
            GitResult add = await RunAsync(Root, ["add", "-A", "--", .. relative]);
            if (!add.Succeeded)
            {
                return add;
            }
        }
        string file = System.IO.Path.GetTempFileName();
        await File.WriteAllTextAsync(file, message);
        try
        {
            string[] only = relative.Length > 0 ? ["--only", "--", .. relative] : [];
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

    public Task<GitResult> CheckoutAsync(string branch) => RunAsync(Root, ["switch", branch]);

    public Task<GitResult> NewBranchAsync(string name) => RunAsync(Root, ["switch", "-c", name]);

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
        GitResult restore = await RunAsync(Root, ["restore", "--source=HEAD", "--staged", "--worktree", "--", .. tracked]);
        return restore;
    }

    public string Relative(string path) => System.IO.Path.GetRelativePath(Root, path).Replace('\\', '/');

    private string Full(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relative));

    private static async Task<GitResult> RunAsync(string folder, IReadOnlyList<string> arguments)
    {
        ProcessStartInfo start = new("git", arguments)
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Git never stops to ask on a terminal it does not have.
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        try
        {
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new GitResult(process.ExitCode, await output, await error);
        }
        catch (System.ComponentModel.Win32Exception missing)
        {
            return new GitResult(127, string.Empty, $"git is not installed: {missing.Message}");
        }
    }
}
