using Avalonia.Input.Platform;
using Passenger.App.Editors;
using Passenger.App.Explorer;
using Passenger.App.Run;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App;

/// <summary>The Explorer's file operations, as Rider's: New File, New Folder and New Class where a row holds things,
/// Rename and Delete (to the Trash) for what is on disk, though a project's Delete takes it out of the solution, Copy
/// Path and Reveal. The open tabs follow a rename and close with a delete, and the Explorer, the file index and Git are
/// read again after.</summary>
public partial class MainWindow
{
    private void AddFileOperations()
    {
        Explorer.ActionRequested += (_, asked) => _ = ExplorerActionAsync(asked.Action, asked.Node);
        Editors.Moved += (_, move) => OnFileMoved(move);
    }

    private async Task ExplorerActionAsync(ExplorerAction action, ExplorerNode node)
    {
        string? path = node.Path;
        // Where something new goes: into a folder or a project's folder, beside a file.
        string? into = node.Kind switch
        {
            NodeKind.Folder => path,
            NodeKind.Project or NodeKind.Solution or NodeKind.File when path is not null => Path.GetDirectoryName(path),
            _ => folder,
        };
        switch (action)
        {
            case ExplorerAction.NewFile when into is not null:
                AskName("New File", string.Empty, name => CreateFile(into, name, string.Empty));
                break;
            case ExplorerAction.NewFolder when into is not null:
                AskName("New Folder", string.Empty, name => CreateFolder(into, name));
                break;
            case ExplorerAction.NewClass when into is not null:
                AskName("New Class", string.Empty, name => CreateClass(into, name));
                break;
            case ExplorerAction.Rename when path is not null:
                AskName("Rename", Path.GetFileName(path), name => Rename(path, name));
                break;
            case ExplorerAction.Delete when path is not null && node.Kind == NodeKind.Project:
                await RemoveProjectAsync(path);
                break;
            case ExplorerAction.Delete when path is not null:
                await DeleteAsync(path);
                break;
            case ExplorerAction.CopyPath when path is not null && Clipboard is { } clipboard:
                await clipboard.SetTextAsync(path);
                StatusText.Text = $"Copied {path}";
                break;
            case ExplorerAction.Reveal when path is not null:
                ChildProcesses.Reveal(path);
                break;
            case ExplorerAction.ManageNuGet:
                ShowToolWindow("nuget");
                break;
            case ExplorerAction.EditProjectFile when path is not null:
                OpenFile(path);
                break;
            case ExplorerAction.Unload or ExplorerAction.Reload when path is not null:
                SetUnloaded(path, action == ExplorerAction.Unload);
                break;
            case ExplorerAction.Build or ExplorerAction.Rebuild or ExplorerAction.Clean:
                BuildKind kind = action switch { ExplorerAction.Rebuild => BuildKind.Rebuild, ExplorerAction.Clean => BuildKind.Clean, _ => BuildKind.Build };
                // A project's menu builds every project chosen with it; the solution's, the solution.
                Build(kind, node.Kind == NodeKind.Project ? SelectedProjects() : null);
                break;
        }
    }

    private void AskName(string title, string initial, Func<string, string?> attempt)
    {
        Vcs.NamePopup popup = new(title, initial, name => Task.FromResult(attempt(name)));
        popup.Done += (_, _) => ClosePopup();
        ShowPopup(popup);
    }

    /// <summary>Makes a file in a folder (folders in its name made too) and opens it; null when it worked, else why not.</summary>
    public string? CreateFile(string into, string name, string text)
    {
        if (Invalid(name) is { } problem)
        {
            return problem;
        }
        string path = Path.GetFullPath(Path.Combine(into, name));
        if (File.Exists(path) || Directory.Exists(path))
        {
            return $"{name} is already there";
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        FilesChanged();
        OpenFile(path);
        Explorer.Reveal(PathNames.Canonical(path));
        return null;
    }

    public string? CreateFolder(string into, string name)
    {
        if (Invalid(name) is { } problem)
        {
            return problem;
        }
        string path = Path.GetFullPath(Path.Combine(into, name));
        if (File.Exists(path) || Directory.Exists(path))
        {
            return $"{name} is already there";
        }
        Directory.CreateDirectory(path);
        FilesChanged();
        Explorer.Reveal(PathNames.Canonical(path));
        return null;
    }

    /// <summary>Rider's New Class: a file of its name holding a class in the namespace its folder gives (the project's
    /// name, then the folders under it).</summary>
    public string? CreateClass(string into, string name)
    {
        string type = Path.GetFileNameWithoutExtension(name);
        if (type.Length == 0 || !char.IsLetter(type[0]) && type[0] != '_' || !type.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            return $"{type} is not a C# name";
        }
        return CreateFile(into, type + ".cs", $"namespace {NamespaceFor(into)};\n\npublic class {type}\n{{\n}}\n");
    }

    /// <summary>The namespace a folder's code takes: its project's root namespace (its name), then the folders below.</summary>
    private string NamespaceFor(string into)
    {
        string full = PathNames.Canonical(into);
        ProjectModel? project = Projects.Where(p => full == p.Folder || full.StartsWith(p.Folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .OrderByDescending(p => p.Folder.Length).FirstOrDefault();
        string root = project is null ? Path.GetFileName(folder ?? full) : Path.GetFileNameWithoutExtension(project.Path);
        string below = project is null ? string.Empty : Path.GetRelativePath(project.Folder, full);
        IEnumerable<string> parts = [root, .. below == "." ? [] : below.Split(Path.DirectorySeparatorChar)];
        return string.Join('.', parts.Select(p => new string([.. p.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')])));
    }

    /// <summary>Renames a file or folder in place. Its open files keep their tabs, splits and undo, saved first, and
    /// its bookmarks and breakpoints go with it.</summary>
    public string? Rename(string path, string name)
    {
        if (Invalid(name) is { } problem)
        {
            return problem;
        }
        string target = Path.Combine(Path.GetDirectoryName(path)!, name);
        if (target == path)
        {
            return null;
        }
        if ((File.Exists(target) || Directory.Exists(target)) && !string.Equals(target, path, StringComparison.OrdinalIgnoreCase))
        {
            return $"{name} is already there";
        }
        if (Editors.MoveFile(path, target) is { } failed)
        {
            return failed;
        }
        Explorer.Reveal(PathNames.Canonical(target));
        return null;
    }

    /// <summary>A file or folder moved, by a rename here or a code fix: what knows a file by its path follows it. The
    /// language server knows one by its URI, so each open file is closed there and opened at the new one.</summary>
    private void OnFileMoved(FileMove move)
    {
        bookmarks.Move(move.From, move.To);
        debug.Breakpoints.Move(move.From, move.To);
        history.Move(move.From, move.To);
        vcs.Moved(move.From, move.To);
        for (int i = 0; i < recent.Count; i++)
        {
            recent[i] = PathNames.Moved(recent[i], move.From, move.To);
        }
        foreach (OpenDocument document in move.Documents)
        {
            csharp?.Detach(document);
            csharp?.Attach(document);
        }
        FilesChanged();
    }

    /// <summary>Rider's Delete on a project: it leaves the solution and its files stay. Moving its folder to the Trash is a
    /// second question, never asked of the open folder, a folder above it, or one holding the solution or another of its
    /// projects.</summary>
    public async Task<bool> RemoveProjectAsync(string project)
    {
        string name = Path.GetFileNameWithoutExtension(project);
        if (Solution is not { } solution || folder is null)
        {
            StatusText.Text = $"{name} is in no solution to remove it from";
            return false;
        }
        if (!await Confirm("Remove Project", $"Remove {name} from the solution? Its folder and files stay on disk.", "Remove"))
        {
            return false;
        }
        string? problem = await DotnetAsync(folder, ["sln", solution.Path, "remove", project]);
        if (problem is not null)
        {
            StatusText.Text = $"{name} could not be removed: {problem}";
            return false;
        }
        ReloadSolution();
        StatusText.Text = $"Removed {name} from {solution.Name}";
        string own = PathNames.Canonical(Path.GetDirectoryName(project)!);
        bool apart = !Within(folder, own) && !Within(solution.Path, own) && !Projects.Any(p => Within(p.Path, own));
        if (apart && await Confirm("Delete Project Files", $"Also move the folder {Path.GetFileName(own)} and everything in it to the Trash?", "Move to Trash"))
        {
            await DeleteAsync(own, asked: true);
        }
        return true;

        // Case is ignored, as macOS and Windows ignore it: erring that way only keeps files.
        static bool Within(string path, string folder) =>
            string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Runs the .NET command line to its end, off the UI thread: null when it succeeded, else what it said.</summary>
    private static Task<string?> DotnetAsync(string folder, IReadOnlyList<string> arguments) => BackgroundWork.Run<string?>(async () =>
    {
        try
        {
            ChildResult result = await ChildProcesses.RunAsync(ChildProcesses.DotnetCommand(arguments, folder));
            return result.Succeeded ? null : $"{result.Error}{result.Output}".Trim();
        }
        catch (Exception failed) when (failed is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return failed.Message;
        }
    });

    /// <summary>Deletes a file or folder to the Trash, once asked (unless it was already); its open files close without
    /// saving once it is gone, and keep their edits when it could not go.</summary>
    public async Task<bool> DeleteAsync(string path, bool asked = false)
    {
        string what = Path.GetFileName(path);
        bool isFolder = Directory.Exists(path);
        if (!asked && !await Confirm("Delete", isFolder ? $"Move the folder {what} and everything in it to the Trash?" : $"Move {what} to the Trash?", "Delete"))
        {
            return false;
        }
        try
        {
            Trash.Move(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"{what} could not be deleted: {error.Message}";
            return false;
        }
        foreach (OpenDocument document in Editors.Documents
            .Where(d => d.Path == path || (isFolder && d.Path.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.Ordinal))).ToList())
        {
            Editors.CloseDeleted(document);
        }
        FilesChanged();
        StatusText.Text = $"Moved {what} to the Trash";
        return true;
    }

    /// <summary>The Explorer, the index Go to File and Find in Files read, and Git's colours, after files changed.</summary>
    private void FilesChanged()
    {
        Explorer.Refresh();
        if (folder is not null)
        {
            index.Build(folder);
        }
        _ = vcs.RefreshAsync();
    }

    private static string? Invalid(string name) =>
        name.Trim().Length == 0 ? "A name is needed"
        : name.IndexOfAny(Path.GetInvalidFileNameChars().Where(c => c is not '/' and not '\\').ToArray()) >= 0 || name.Contains("..", StringComparison.Ordinal) ? $"{name} is not a file name"
        : null;
}

/// <summary>The platform's Trash (the Recycle Bin on Windows), where a deleted file can still be found.</summary>
public static class Trash
{
    /// <summary>A folder to use as the Trash instead of the platform's (tests, which leave the person's alone).</summary>
    public static string? Folder { get; set; }

    public static void Move(string path)
    {
        if (OperatingSystem.IsWindows() && Folder is null)
        {
            if (Directory.Exists(path))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            else
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            return;
        }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string bin = Folder ?? (OperatingSystem.IsMacOS() ? Path.Combine(home, ".Trash") : Path.Combine(home, ".local", "share", "Trash", "files"));
        Directory.CreateDirectory(bin);
        string name = Path.GetFileName(path);
        string target = Path.Combine(bin, name);
        // The Trash keeps what it already holds: a second one of a name takes the time beside it.
        if (File.Exists(target) || Directory.Exists(target))
        {
            target = Path.Combine(bin, $"{Path.GetFileNameWithoutExtension(name)} {DateTime.Now:HH-mm-ss-fff}{Path.GetExtension(name)}");
        }
        if (Directory.Exists(path))
        {
            Directory.Move(path, target);
        }
        else
        {
            File.Move(path, target);
        }
    }
}
