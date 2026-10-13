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
/// Path and Reveal; the solution's own folders' are in MainWindow.Solution.cs. The open tabs follow a rename and close
/// with a delete, and the Explorer, the file index and Git are read again after.</summary>
public partial class MainWindow
{
    private void AddFileOperations()
    {
        Explorer.ActionRequested += (_, asked) => _ = ExplorerActionAsync(asked.Action, asked.Node);
        Explorer.Dropped += (_, dropped) => _ = DropAsync(dropped);
        Explorer.CanPaste = () => held is not null;
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
            case ExplorerAction.NewSolutionFolder when node.Kind is NodeKind.Solution or NodeKind.SolutionFolder:
                AskName("New Solution Folder", string.Empty, name => AddSolutionFolder(node.SolutionPath, name));
                break;
            case ExplorerAction.NewProject when node.Kind is NodeKind.Solution or NodeKind.SolutionFolder:
                ShowNewProject(node.SolutionPath);
                break;
            case ExplorerAction.AddExistingProject when node.Kind is NodeKind.Solution or NodeKind.SolutionFolder:
                await AddExistingProjectsAsync(node.SolutionPath);
                break;
            case ExplorerAction.AddExistingItem when node is { Kind: NodeKind.SolutionFolder, SolutionPath: { } holding }:
                await AddExistingItemsAsync(holding);
                break;
            case ExplorerAction.Rename when node is { Kind: NodeKind.SolutionFolder, SolutionPath: { } renaming }:
                AskName("Rename Solution Folder", node.Name, name => RenameSolutionFolder(renaming, name));
                break;
            case ExplorerAction.Delete when node is { Kind: NodeKind.SolutionFolder, SolutionPath: { } removing }:
                await RemoveSolutionFolderAsync(removing);
                break;
            case ExplorerAction.Delete when node is { IsSolutionItem: true, SolutionPath: { } holder } && path is not null:
                await RemoveSolutionItemAsync(holder, path);
                break;
            case ExplorerAction.ShowHistory or ExplorerAction.CompareWithRevision or ExplorerAction.CompareWithBranch:
                await ExplorerGitAsync(action, node);
                break;
            case ExplorerAction.RunTests or ExplorerAction.DebugTests:
                RunProjectTests([.. Explorer.SelectedNodes.Append(node).Distinct().SelectMany(Explorer.Tree.ProjectsOf).Distinct()], node.Name, action == ExplorerAction.DebugTests);
                break;
            case ExplorerAction.FindInFiles or ExplorerAction.ReplaceInFiles:
                ShowFindInFiles(action == ExplorerAction.ReplaceInFiles, ScopeOf(Explorer.SelectedNodes.Contains(node) ? Explorer.SelectedNodes : [node]));
                break;
            case ExplorerAction.NewFile when into is not null:
                AskName("New File", string.Empty, name => CreateFile(into, name, string.Empty));
                break;
            case ExplorerAction.NewFolder when into is not null:
                AskName("New Folder", string.Empty, name => CreateFolder(into, name));
                break;
            case ExplorerAction.NewClass when into is not null:
                AskType(into);
                break;
            case ExplorerAction.Cut or ExplorerAction.Copy:
                await HoldAsync(node, cut: action == ExplorerAction.Cut);
                break;
            case ExplorerAction.Paste:
                await PasteAsync(node);
                break;
            case ExplorerAction.AddReference when (node.Kind == NodeKind.Project ? path : node.Owner) is { } project:
                ShowAddReference(project);
                break;
            case ExplorerAction.Delete when node is { Kind: NodeKind.ProjectReference, Owner: { } owner, Referenced: { } referenced }:
                await RemoveReferenceAsync(owner, referenced);
                break;
            case ExplorerAction.Compare when node is { Kind: NodeKind.File, Path: not null }:
                await CompareAsync(node);
                break;
            case ExplorerAction.OpenInTerminal when path is not null:
                _ = OpenTerminalAt(node.Kind == NodeKind.Folder ? path : Path.GetDirectoryName(path)!);
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
                // A project's or solution folder's menu builds every project chosen with it; the solution's, the solution.
                Build(kind, node.Kind is NodeKind.Project or NodeKind.SolutionFolder ? SelectedProjects() : null);
                break;
        }
    }

    private void AskName(string title, string initial, Func<string, string?> attempt)
    {
        Vcs.NamePopup popup = new(title, initial, name => Task.FromResult(attempt(name)));
        popup.Done += (_, _) => ClosePopup();
        ShowPopup(popup);
    }

    /// <summary>Asks for a name as <see cref="AskName"/> does, answering with the one taken, or null once the popup closes
    /// without one (Escape, a click outside it).</summary>
    private Task<string?> AskNameAsync(string title, string initial, Func<string, string?> refuse)
    {
        TaskCompletionSource<string?> answer = new();
        string? taken = null;
        Vcs.NamePopup popup = new(title, initial, name =>
        {
            string? why = refuse(name);
            taken = why is null ? name : null;
            return Task.FromResult(why);
        });
        popup.Done += (_, _) => ClosePopup();
        popup.DetachedFromVisualTree += (_, _) => answer.TrySetResult(taken);
        ShowPopup(popup);
        return answer.Task;
    }

    /// <summary>The C# types Rider's Class/Interface makes, in the order its popup lists them (its file templates of that
    /// group: Class, Interface, Record, Struct, Enum).</summary>
    public static IReadOnlyList<string> TypeKinds { get; } = ["Class", "Interface", "Record", "Struct", "Enum"];

    /// <summary>Rider's Add, Class/Interface: a name, and the kind of type chosen under it with Up and Down.</summary>
    private void AskType(string into)
    {
        Vcs.NamePopup? popup = null;
        popup = new Vcs.NamePopup("New Class/Interface", string.Empty, name => Task.FromResult(CreateType(into, name, popup?.Kind ?? TypeKinds[0])),
            [.. TypeKinds.Select(kind => (kind, TypeBadge(kind)))]);
        popup.Done += (_, _) => ClosePopup();
        ShowPopup(popup);
    }

    /// <summary>The letter and colour Rider marks a kind of type with, as Go to Symbol shows it.</summary>
    private static Avalonia.Controls.Control TypeBadge(string kind)
    {
        (string letter, string brush) = Navigation.SymbolPopup.Badge(kind switch { "Interface" => 11, "Struct" => 23, "Enum" => 10, _ => 5 });
        return Navigation.SymbolPopup.KindBadge(kind == "Record" ? "R" : letter, brush);
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
        // Made as its .editorconfig asks: its line breaks and its charset.
        EditorConfig conventions = EditorConfig.For(path);
        FileEncoding charset = conventions.Charset ?? FileEncoding.Utf8;
        LineSeparator separator = conventions.EndOfLine ?? LineSeparator.LF;
        FileText.Save(path, LineEndings.In(text, separator.Held()), charset.Encoding, charset.Bom, separator == LineSeparator.CR);
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
    public string? CreateClass(string into, string name) => CreateType(into, name, "Class");

    /// <summary>Rider's Class/Interface: a file of its name holding a type of the kind chosen (<see cref="TypeKinds"/>), as
    /// its file template for that kind writes it, in the namespace its folder gives.</summary>
    public string? CreateType(string into, string name, string kind)
    {
        string type = Path.GetFileNameWithoutExtension(name);
        if (type.Length == 0 || !char.IsLetter(type[0]) && type[0] != '_' || !type.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            return $"{type} is not a C# name";
        }
        string declaration = kind switch
        {
            "Interface" => $"public interface {type}\n{{\n}}\n",
            "Record" => $"public record {type}();\n",
            "Struct" => $"public struct {type}\n{{\n}}\n",
            "Enum" => $"public enum {type}\n{{\n}}\n",
            _ => $"public class {type}\n{{\n}}\n",
        };
        return CreateFile(into, type + ".cs", $"namespace {NamespaceFor(into)};\n\n{declaration}");
    }

    /// <summary>The namespace a folder's code takes: its project's root namespace (its name, "Shop.App"), then the folders
    /// below, each name a C# identifier or dotted ones, as Rider makes them.</summary>
    private string NamespaceFor(string into)
    {
        string full = PathNames.Canonical(into);
        ProjectModel? project = ProjectOf(full);
        string root = project is null ? Path.GetFileName(folder ?? full) : Path.GetFileNameWithoutExtension(project.Path);
        string below = project is null ? string.Empty : Path.GetRelativePath(project.Folder, full);
        IEnumerable<string> parts = [root, .. below == "." ? [] : below.Split(Path.DirectorySeparatorChar)];
        return string.Join('.', parts.SelectMany(p => p.Split('.', StringSplitOptions.RemoveEmptyEntries)).Select(Identifier));

        static string Identifier(string part)
        {
            string name = new([.. part.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')]);
            return char.IsDigit(name[0]) ? "_" + name : name;
        }
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
        if (EditSolution(file => SolutionEdits.RemoveProject(file, project), $"Removed {name} from {solution.Name}") is { } problem)
        {
            StatusText.Text = $"{name} could not be removed: {problem}";
            return false;
        }
        string own = PathNames.Canonical(Path.GetDirectoryName(project)!);
        bool apart = !PathNames.Within(folder, own) && !PathNames.Within(solution.Path, own) && !Projects.Any(p => PathNames.Within(p.Path, own));
        if (apart && await Confirm("Delete Project Files", $"Also move the folder {Path.GetFileName(own)} and everything in it to the Trash?", "Move to Trash"))
        {
            await DeleteAsync(own, asked: true);
        }
        return true;
    }

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
        foreach (OpenDocument document in Editors.Documents.Where(d => PathNames.Within(d.Path, path)).ToList())
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
