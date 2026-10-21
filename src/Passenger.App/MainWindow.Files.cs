using Avalonia.Input.Platform;
using Passenger.App.CSharp;
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
        Explorer.NestingAsked += (_, _) => _ = AskNestingAsync();
        Editors.Moved += (_, move) => OnFileMoved(move);
        // The watcher that saw the move reads the Explorer, the index and Git again itself.
        Editors.Followed += (_, move) => session.Followed(move);
    }

    /// <summary>Rider's File Nesting, over the window: its "Show files with the same names as nested" is the setting every
    /// Solution view nests by.</summary>
    private async Task AskNestingAsync()
    {
        FileNestingDialog dialog = new(settings.NestFiles);
        if (await dialog.ShowDialog<bool>(this))
        {
            ApplySettings(settings with { NestFiles = dialog.Nest });
        }
    }

    private async Task ExplorerActionAsync(ExplorerAction action, ExplorerNode node)
    {
        string? path = node.Path;
        // Where something new goes: into a folder or a project's folder, beside a file.
        string? into = node.Kind switch
        {
            NodeKind.Folder => path,
            NodeKind.Project or NodeKind.Solution or NodeKind.File when path is not null => Path.GetDirectoryName(path),
            _ => Folder,
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
            case ExplorerAction.ShowLocalHistory when Target(node, fileOnly: false) is { } shown:
                ShowLocalHistory(shown);
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
            case ExplorerAction.Rename when node.Kind == NodeKind.Project && path is not null:
                AskName("Rename Project", Path.GetFileNameWithoutExtension(path), name => RenameProject(path, name));
                break;
            case ExplorerAction.Rename when path is not null:
                AskRename(path);
                break;
            case ExplorerAction.Undo or ExplorerAction.Redo:
                UndoCommand(action == ExplorerAction.Redo, null);
                break;
            case ExplorerAction.Delete when path is not null && node.Kind == NodeKind.Project:
                await RemoveProjectAsync(path);
                break;
            case ExplorerAction.Delete when path is not null:
                await DeleteAsync(path);
                break;
            case ExplorerAction.CopyPath when path is not null && Clipboard is { } clipboard:
                await clipboard.SetTextAsync(path);
                status.Text = $"Copied {path}";
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
        popup.Done += (_, _) => popups.Close();
        popups.Show(popup);
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
        popup.Done += (_, _) => popups.Close();
        popup.DetachedFromVisualTree += (_, _) => answer.TrySetResult(taken);
        popups.Show(popup);
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
        popup.Done += (_, _) => popups.Close();
        popups.Show(popup);
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
    /// its file template for that kind writes it, in the namespace its folder gives. Folders before the name
    /// ("Shapes/Circle") are made and the type goes in them, where they were dropped and the type made beside them.</summary>
    public string? CreateType(string into, string name, string kind)
    {
        string[] parts = name.Trim().Split('/', '\\');
        string last = parts[^1];
        string type = last.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? last[..^3] : last;
        if (type.Length == 0 || !char.IsLetter(type[0]) && type[0] != '_' || !type.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            return $"{type} is not a C# name";
        }
        if (Invalid(name) is { } problem)
        {
            return problem;
        }
        into = Path.Combine([into, .. parts[..^1].Where(p => p.Length > 0)]);
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
        string root = project is null ? Path.GetFileName(Folder ?? full) : Path.GetFileNameWithoutExtension(project.Path);
        string below = project is null ? string.Empty : Path.GetRelativePath(project.Folder, full);
        IEnumerable<string> parts = [root, .. below == "." ? [] : below.Split(Path.DirectorySeparatorChar)];
        return string.Join('.', parts.SelectMany(p => p.Split('.', StringSplitOptions.RemoveEmptyEntries)).Select(Identifier));

        static string Identifier(string part)
        {
            string name = new([.. part.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')]);
            return char.IsDigit(name[0]) ? "_" + name : name;
        }
    }

    /// <summary>Rider's Rename on a row: a C# file named after a type it declares offers to rename the type too, Rider's
    /// Rename type 'Foo' to match the new file name, checked as it was left (on until turned off).</summary>
    private void AskRename(string path)
    {
        if (TypeRename.NamedAfterFile(Editors, path) is not { } type)
        {
            AskName("Rename", Path.GetFileName(path), name => Rename(path, name));
            return;
        }
        Vcs.NamePopup? popup = null;
        popup = new Vcs.NamePopup("Rename", Path.GetFileName(path), name => RenameWithTypeAsync(path, name, type, popup?.Option?.IsChecked == true),
            option: ($"Rename type '{type.Name}' to match the new file name", assist.RenameSettings.RenameType));
        popup.OptionChanged += (_, on) => assist.RenameSettings = assist.RenameSettings with { RenameType = on };
        popup.Done += (_, _) => popups.Close();
        popups.Show(popup);
    }

    /// <summary>Renames a C# file and, when asked, the type it is named after, as Rider's Rename of a file does: the
    /// language server renames the type everywhere it is used, then the file takes its new name and any other file named
    /// after the type (a partial part's Foo.Designer.cs) the type's, all one command for Undo. Null once renamed, else why
    /// not, in Rider's words where it has them.</summary>
    public async Task<string?> RenameWithTypeAsync(string path, string name, (string Name, int Offset) type, bool renameType)
    {
        string renamed = TypeRename.Stem(name);
        if (!renameType || renamed == type.Name)
        {
            return Rename(path, name);
        }
        if (Invalid(name) is { } problem)
        {
            return problem;
        }
        if (!name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return "The new file name extension is not valid for source code files";
        }
        if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(renamed))
        {
            return "The new file name cannot be used as a valid type name";
        }
        string target = Path.Combine(Path.GetDirectoryName(path)!, name);
        if ((File.Exists(target) || Directory.Exists(target)) && !string.Equals(target, path, StringComparison.OrdinalIgnoreCase))
        {
            return $"{name} is already there";
        }
        CSharpService? csharp = session.CSharp;
        if (csharp?.Session is not { IsLoaded: true } server)
        {
            return csharp?.Unready("Renaming the type") ?? "Renaming the type needs the C# language server, which runs for a folder";
        }
        OpenDocument? open = Editors.Documents.FirstOrDefault(d => d.Path == path);
        // A file not open is shown to the server for the question alone, as Rider renames without opening it.
        Lsp.LspDocument? shown = null;
        BackgroundWork.Begin();
        try
        {
            Lsp.LspDocument document = open is not null ? csharp.For(open)?.Lsp ?? throw new Lsp.LanguageServerFailed("the C# language server has not opened the file yet")
                : shown = server.Open(path, new TextDocument(FileText.Load(path)?.Text ?? string.Empty));
            using Asked asked = WorkspaceEdits.Track(Editors);
            System.Text.Json.JsonElement? edit = await document.RenameAsync(type.Offset, renamed);
            (string Type, IReadOnlyList<string> Files)? named = await TypeRename.NamedAsync(Editors, server.Folder, document, type.Offset);
            shown?.Dispose();
            shown = null;
            if (edit is not { } made)
            {
                return $"{type.Name} can't be renamed";
            }
            if (await WorkspaceEdits.StaleAsync(Editors, paths => server.HeldTextsAsync(paths), made, asked.Since) is { } stale)
            {
                return $"{type.Name} was not renamed: {stale}";
            }
            List<(string From, string To)> moves = [(path, target)];
            if (named is { } parts)
            {
                moves.AddRange(parts.Files.Where(f => f != path).Select(f => (f, TypeRename.Renamed(f, renamed))));
            }
            // The files nested under it go with it too (Form1.resx with Form1.cs), those that are parts of the type once.
            moves.AddRange(WithDependents(path, name).Where(d => !moves.Any(m => m.From == d.From)));
            List<string> dropped = [];
            int files = TypeRename.Apply(Editors, made, moves, $"Rename {type.Name} to {renamed}", asked, dropped,
                recording => FollowMoved([.. moves.Select(m => (m.From, PathNames.Canonical(m.To)))], recording));
            FilesChanged();
            Explorer.Reveal(PathNames.Canonical(target));
            status.Text = $"Renamed {Path.GetFileName(path)} to {name}, and {type.Name} to {renamed} in {files} file{(files == 1 ? "" : "s")}"
                + (dropped.Count > 0 ? $", but not in {string.Join(", ", dropped)}: it was edited meanwhile" : string.Empty);
            return null;
        }
        catch (Lsp.LanguageServerFailed failed)
        {
            return $"{type.Name} could not be renamed: {failed.Reason}";
        }
        catch (WorkspaceEditFailed failed)
        {
            return failed.Message;
        }
        finally
        {
            shown?.Dispose();
            BackgroundWork.End();
        }
    }

    /// <summary>Rider's Rename on a project: its file takes the name, its extension kept, and the solution and the projects
    /// referencing it follow (<see cref="Rename"/>). Null once renamed, else why not.</summary>
    public string? RenameProject(string project, string name)
    {
        string extension = Path.GetExtension(project);
        return Invalid(name) ?? Rename(project, name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? name : name + extension);
    }

    /// <summary>Renames a file or folder in place, and the files nested under a file with it, as Rider's rename of a
    /// project item renames its dependents (Counter.razor.cs going with Counter.razor). Its open files keep their tabs,
    /// splits and undo, saved first, its bookmarks and breakpoints go with it, and what names it follows (projects' items,
    /// the solution, references to a project). Undo in the Solution view renames it all back, as Rider's does.</summary>
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
        List<(string From, string To)> moves = [(path, target), .. WithDependents(path, name)];
        if (moves.FirstOrDefault(m => (File.Exists(m.To) || Directory.Exists(m.To)) && !string.Equals(m.To, m.From, StringComparison.OrdinalIgnoreCase)) is { To: not null } taken)
        {
            return $"{Path.GetFileName(taken.To)} is already there";
        }
        using (CommandHistory.Recording recording = Editors.Commands.Record($"Rename {Path.GetFileName(path)} to {name}"))
        {
            foreach ((string from, string to) in moves)
            {
                if (Editors.MoveFile(from, to) is { } failed)
                {
                    return failed;
                }
            }
            if (FollowMoved([.. moves.Select(m => (m.From, PathNames.Canonical(m.To)))], recording) is { } unfollowed)
            {
                status.Text = unfollowed;
            }
        }
        Explorer.Reveal(PathNames.Canonical(target));
        return null;
    }

    /// <summary>Where the files nested under a file go as it takes a new name: beside it, named after it.</summary>
    private IEnumerable<(string From, string To)> WithDependents(string path, string name) =>
        DependentsOf(path)
            .Select(d => (d, Path.Combine(Path.GetDirectoryName(d)!, FileNesting.Renamed(d, Path.GetFileName(path), name))))
            .Where(d => d.Item1 != d.Item2);

    /// <summary>A file or folder moved, by a rename here or a code fix: what knows a file by its path follows it. The
    /// language server knows one by its URI, so each open file is closed there and opened at the new one.</summary>
    private void OnFileMoved(FileMove move)
    {
        session.Moved(move);
        FilesChanged();
    }

    /// <summary>Rider's Delete on a project: it leaves the solution and its files stay. Moving its folder to the Trash is a
    /// second question, never asked of the open folder, a folder above it, or one holding the solution or another of its
    /// projects.</summary>
    public async Task<bool> RemoveProjectAsync(string project)
    {
        string name = Path.GetFileNameWithoutExtension(project);
        if (Solution is not { } solution || Folder is null)
        {
            status.Text = $"{name} is in no solution to remove it from";
            return false;
        }
        if (!await Confirm("Remove Project", $"Remove {name} from the solution? Its folder and files stay on disk.", "Remove"))
        {
            return false;
        }
        if (EditSolution(file => SolutionEdits.RemoveProject(file, project), $"Removed {name} from {solution.Name}") is { } problem)
        {
            status.Text = $"{name} could not be removed: {problem}";
            return false;
        }
        string own = PathNames.Canonical(Path.GetDirectoryName(project)!);
        bool apart = !PathNames.Within(Folder, own) && !PathNames.Within(solution.Path, own) && !Projects.Any(p => PathNames.Within(p.Path, own));
        if (apart && await Confirm("Delete Project Files", $"Also move the folder {Path.GetFileName(own)} and everything in it to the Trash?", "Move to Trash"))
        {
            await DeleteAsync(own, asked: true);
        }
        return true;
    }

    /// <summary>How long a delete waits for the design-time builds of the projects it takes to end.</summary>
    private static readonly TimeSpan BuildsGoing = TimeSpan.FromSeconds(60);

    /// <summary>Deletes a file or folder to the Trash, once asked (unless it was already); its open files are saved first,
    /// so the Trash and Local History have what the editor showed, and close once it is gone, or keep their tabs when it
    /// could not go. The design-time builds of the projects in it are held off, those running waited out.</summary>
    public async Task<bool> DeleteAsync(string path, bool asked = false)
    {
        string what = Path.GetFileName(path);
        bool isFolder = Directory.Exists(path);
        if (!asked && !await Confirm("Delete", isFolder ? $"Move the folder {what} and everything in it to the Trash?" : $"Move {what} to the Trash?", "Delete"))
        {
            return false;
        }
        foreach (OpenDocument document in Editors.Documents.Where(d => d.IsModified && PathNames.Within(d.Path, path)).ToList())
        {
            if (!Editors.Save(document))
            {
                status.Text = $"{what} could not be deleted: {document.Name} could not be saved first";
                return false;
            }
        }
        History.LocalHistory? keeping = session.LocalHistory;
        History.HistoryDeletion? taken = keeping is null ? null : await keeping.CaptureAsync(path);
        // A design-time build of a project in it, still writing its obj/, would make the folder again once it had gone.
        (IDisposable held, Task built) = session.DesignTimeBuilds.HoldOff(path);
        using IDisposable releasing = held;
        if (!built.IsCompleted)
        {
            status.Text = $"Waiting for the design-time build of {what} to end before it goes to the Trash…";
            try
            {
                await built.WaitAsync(BuildsGoing);
            }
            catch (TimeoutException)
            {
                // Moved anyway: what the build writes after makes only build output there again.
            }
        }
        try
        {
            Trash.Move(path);
            if (taken is not null)
            {
                keeping!.Deleted(taken);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            status.Text = $"{what} could not be deleted: {error.Message}";
            return false;
        }
        foreach (OpenDocument document in Editors.Documents.Where(d => PathNames.Within(d.Path, path)).ToList())
        {
            Editors.CloseDeleted(document);
        }
        FilesChanged();
        status.Text = $"Moved {what} to the Trash";
        return true;
    }

    /// <summary>The Explorer, the index Go to File and Find in Files read, and Git's colours, after files changed.</summary>
    private void FilesChanged()
    {
        Explorer.Refresh();
        // What a project's wildcards take in changed here: its evaluation follows at once, not when the watcher hears of it.
        session.Evaluate();
        if (Folder is { } folder)
        {
            session.Index.Build(folder);
        }
        _ = session.Vcs.RefreshAsync();
    }

    private static string? Invalid(string name) =>
        name.Trim().Length == 0 ? "A name is needed"
        : name.IndexOfAny(Path.GetInvalidFileNameChars().Where(c => c is not '/' and not '\\').ToArray()) >= 0 || name.Contains("..", StringComparison.Ordinal) ? $"{name} is not a file name"
        : null;
}

/// <summary>The platform's Trash (the Recycle Bin on Windows), where a deleted file can still be found.</summary>
public static class Trash
{
    /// <summary>A folder to use as the Trash instead of the platform's (tests, which leave the person's alone). The
    /// process's, as the system's Trash is every window's.</summary>
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
        if (OperatingSystem.IsLinux() && Folder is null)
        {
            MoveToFreedesktopTrash(path);
            return;
        }
        string bin = Folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
        Directory.CreateDirectory(bin);
        string name = Path.GetFileName(path);
        string target = Path.Combine(bin, name);
        // The Trash keeps what it already holds: a second one of a name takes the time beside it.
        if (File.Exists(target) || Directory.Exists(target))
        {
            target = Path.Combine(bin, $"{Path.GetFileNameWithoutExtension(name)} {DateTime.Now:HH-mm-ss-fff}{Path.GetExtension(name)}");
        }
        MoveTo(path, target);
    }

    /// <summary>The freedesktop.org Trash in the person's data folder (~/.local/share/Trash), as Linux's file managers
    /// keep it: the item in files/ under a name of its own, beside a record in info/ of where it was and when it went,
    /// from which they put it back. A folder on another file system is refused, as a move across them cannot be made.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static void MoveToFreedesktopTrash(string path)
    {
        const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        string trash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trash");
        Directory.CreateDirectory(trash, Private);
        string files = Directory.CreateDirectory(Path.Combine(trash, "files"), Private).FullName;
        string info = Directory.CreateDirectory(Path.Combine(trash, "info"), Private).FullName;
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string name = Path.GetFileName(full);
        string where = string.Join('/', full.Split('/').Select(Uri.EscapeDataString));
        for (int copy = 1; ; copy++)
        {
            string kept = copy == 1 ? name : $"{name}.{copy}";
            string target = Path.Combine(files, kept);
            string record = Path.Combine(info, kept + ".trashinfo");
            if (Path.Exists(target))
            {
                continue;
            }
            // Made only when no record has the name, so two programs trashing at once never share one.
            try
            {
                using FileStream claimed = new(record, FileMode.CreateNew, FileAccess.Write);
                using StreamWriter writer = new(claimed);
                writer.Write($"[Trash Info]\nPath={where}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n");
            }
            catch (IOException) when (File.Exists(record))
            {
                continue;
            }
            try
            {
                MoveTo(full, target);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                File.Delete(record);
                throw;
            }
            return;
        }
    }

    private static void MoveTo(string path, string target)
    {
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
