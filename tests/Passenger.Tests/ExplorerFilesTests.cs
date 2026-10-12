using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Editors;
using Passenger.App.Explorer;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>The Explorer's file operations as Rider's: New Class writes a class in its folder's namespace and opens it;
/// a rename moves the file or folder and its open tabs with it; a delete goes to the Trash and closes its tab, and a
/// project's leaves the solution; a name already there is refused with why.</summary>
public sealed class ExplorerFilesTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("passenger-files-").FullName;
    private readonly string trash = Directory.CreateTempSubdirectory("passenger-trash-").FullName;

    public ExplorerFilesTests()
    {
        File.WriteAllText(Path.Combine(folder, "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        Directory.CreateDirectory(Path.Combine(folder, "Models"));
        Trash.Folder = trash;
    }

    public void Dispose()
    {
        Trash.Folder = null;
        Directory.Delete(folder, recursive: true);
        Directory.Delete(trash, recursive: true);
    }

    [AvaloniaFact]
    public void New_class_rename_and_delete_change_the_disk_the_tabs_and_the_explorer()
    {
        MainWindow window = Open();
        string models = PathNames.Canonical(Path.Combine(folder, "Models"));

        // New Class: the folder's namespace under the project's, and the file open.
        Assert.Null(window.CreateClass(models, "Order"));
        string order = Path.Combine(models, "Order.cs");
        Assert.Equal("namespace Shop.Models;\n\npublic class Order\n{\n}\n", File.ReadAllText(order));
        Assert.Equal("Order.cs", window.EditorArea.Active?.Name);
        Assert.Contains(window.ExplorerView.Tree.Rows, r => r.Name == "Order.cs");
        Assert.Equal("Order.cs is already there", window.CreateFile(models, "Order.cs", string.Empty));

        // A rename moves the file, and its tab shows the new one.
        Assert.Null(window.Rename(order, "Purchase.cs"));
        string purchase = Path.Combine(models, "Purchase.cs");
        Assert.True(File.Exists(purchase));
        Assert.False(File.Exists(order));
        Assert.Equal(["Purchase.cs"], window.EditorArea.Documents.Select(d => d.Name));

        // A folder's rename takes its open files along.
        Assert.Null(window.Rename(models, "Domain"));
        Assert.Equal(PathNames.Canonical(Path.Combine(folder, "Domain", "Purchase.cs")), Assert.Single(window.EditorArea.Documents).Path);

        // A delete goes to the Trash and closes the tab.
        window.Confirm = (_, _, _) => Task.FromResult(true);
        string moved = Path.Combine(folder, "Domain", "Purchase.cs");
        Task<bool> deleting = window.DeleteAsync(PathNames.Canonical(moved));
        Ui.WaitFor(() => deleting.IsCompleted, "the delete");
        Assert.True(deleting.Result);
        Assert.False(File.Exists(moved));
        Assert.True(File.Exists(Path.Combine(trash, "Purchase.cs")));
        Assert.Empty(window.EditorArea.Documents);
        Assert.DoesNotContain(window.ExplorerView.Tree.Rows, r => r.Name == "Purchase.cs");
        window.Close();
    }

    [AvaloniaFact]
    public void Delete_on_a_project_beside_its_solution_takes_it_out_of_the_solution_and_leaves_the_folder()
    {
        string solution = Path.Combine(folder, "Shop.slnx");
        File.WriteAllText(solution, "<Solution>\n  <Project Path=\"Shop.csproj\" />\n</Solution>\n");
        MainWindow window = Open();
        List<string> asked = Answer(window);

        // The project and the solution share the repository's top folder, which Delete must never offer to trash.
        Choose(window, Assert.Single(window.ExplorerView.Tree.Rows, r => r.Kind == NodeKind.Project));
        Ui.Press(window, "BACK_SPACE");
        Ui.WaitFor(() => Finished(window, "Removed"), "the project's removal");
        Assert.Equal("Removed Shop from Shop", window.Status);
        Assert.DoesNotContain("Shop.csproj", File.ReadAllText(solution), StringComparison.Ordinal);
        Assert.Equal(["Remove Project"], asked);
        Assert.True(File.Exists(Path.Combine(folder, "Shop.csproj")), "The project's file went with it");
        Assert.True(Directory.Exists(Path.Combine(folder, "Models")), "The folder beside the solution went to the Trash");
        Assert.Empty(Directory.EnumerateFileSystemEntries(trash));
        Assert.DoesNotContain(window.ExplorerView.Tree.Rows, r => r.Kind == NodeKind.Project);
        window.Close();
    }

    [AvaloniaFact]
    public void Delete_on_a_project_in_its_own_folder_moves_that_folder_to_the_Trash_only_once_asked_again()
    {
        string library = Directory.CreateDirectory(Path.Combine(folder, "Lib")).FullName;
        File.Copy(Path.Combine(folder, "Shop.csproj"), Path.Combine(library, "Lib.csproj"));
        string solution = Path.Combine(folder, "Shop.slnx");
        File.WriteAllText(solution, "<Solution>\n  <Project Path=\"Shop.csproj\" />\n  <Project Path=\"Lib/Lib.csproj\" />\n</Solution>\n");
        MainWindow window = Open();
        List<string> asked = Answer(window);

        Choose(window, Assert.Single(window.ExplorerView.Tree.Rows, r => r.Kind == NodeKind.Project && r.Name == "Lib"));
        Ui.Press(window, "BACK_SPACE");
        Ui.WaitFor(() => Finished(window, "Moved"), "the project's folder to go");
        Assert.Equal("Moved Lib to the Trash", window.Status);
        Assert.Equal(["Remove Project", "Delete Project Files"], asked);
        Assert.False(Directory.Exists(library), "The project's own folder stayed after the second question was answered");
        Assert.True(File.Exists(Path.Combine(trash, "Lib", "Lib.csproj")), "The project's folder is not in the Trash");
        string left = File.ReadAllText(solution);
        Assert.Contains("Shop.csproj", left, StringComparison.Ordinal);
        Assert.DoesNotContain("Lib.csproj", left, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void A_delete_the_Trash_refuses_keeps_the_open_file_and_its_unsaved_edit()
    {
        string notes = Path.Combine(folder, "Notes.txt");
        File.WriteAllText(notes, "groceries\n");
        MainWindow window = Open();
        window.Autosaves = false;
        window.OpenFile(notes);
        window.EditorArea.CodeEditor.Focus();
        window.KeyTextInput("milk, ");
        Ui.Settle();
        window.Confirm = (_, _, _) => Task.FromResult(true);
        // A Trash that is a file takes nothing, as one on another volume or a locked file would refuse.
        string full = Path.Combine(trash, "full");
        File.WriteAllText(full, string.Empty);
        Trash.Folder = full;

        Choose(window, Assert.Single(window.ExplorerView.Tree.Rows, r => r.Name == "Notes.txt"));
        Ui.Press(window, "BACK_SPACE");
        Assert.StartsWith("Notes.txt could not be deleted", window.Status, StringComparison.Ordinal);
        Assert.True(File.Exists(notes), "The file left although the Trash refused it");
        OpenDocument open = Assert.Single(window.EditorArea.Documents);
        Assert.Equal("milk, groceries\n", open.Text.Snapshot.ToString());
        Assert.True(open.IsModified, "The unsaved edit was marked saved, so it would be lost");
        window.Close();
    }

    [AvaloniaFact]
    public void Renaming_a_folder_with_two_open_files_keeps_the_one_in_front_in_front()
    {
        string models = PathNames.Canonical(Path.Combine(folder, "Models"));
        File.WriteAllText(Path.Combine(models, "Alpha.cs"), "class Alpha { }\n");
        File.WriteAllText(Path.Combine(models, "Beta.cs"), "class Beta { }\n");
        MainWindow window = Open();
        window.OpenFile(Path.Combine(models, "Alpha.cs"));
        window.OpenFile(Path.Combine(models, "Beta.cs"));
        window.OpenFile(Path.Combine(models, "Alpha.cs"));
        Ui.Settle();

        Assert.Null(window.Rename(models, "Domain"));
        Assert.Equal(PathNames.Canonical(Path.Combine(folder, "Domain", "Alpha.cs")), window.EditorArea.Active?.Path);
        Assert.Equal(2, window.EditorArea.Documents.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void A_file_renamed_in_the_explorer_keeps_its_tab_in_both_splits_its_undo_its_bookmark_and_its_breakpoint()
    {
        string models = PathNames.Canonical(Path.Combine(folder, "Models"));
        string order = Path.Combine(models, "Order.cs");
        File.WriteAllText(order, "namespace Shop.Models;\n\npublic class Order\n{\n    public int Total;\n}\n");
        MainWindow window = Open();
        window.Autosaves = false;
        window.OpenFile(order);
        EditorArea area = window.EditorArea;
        OpenDocument document = Assert.Single(area.Documents);
        area.CodeEditor.GoTo(new TextPosition(4, 21));
        window.KeyTextInput(" // the sum");
        area.CodeEditor.GoTo(new TextPosition(4, 0));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        Ui.Run(window, AppActions.ToggleBookmark);
        Assert.True(window.Perform(AppActions.SplitRight));
        Ui.Settle();

        Assert.Null(window.Rename(order, "Purchase.cs"));
        // The folder watcher hears of the move after it, and must not take it for the file deleted.
        Passenger.App.Screenshot.Pump(TimeSpan.FromMilliseconds(500));
        Ui.Settle();
        string purchase = Path.Combine(models, "Purchase.cs");
        Assert.False(File.Exists(order), "Order.cs is still on disk after its rename");
        Assert.Equal("namespace Shop.Models;\n\npublic class Order\n{\n    public int Total; // the sum\n}\n", File.ReadAllText(purchase));
        Assert.True(ReferenceEquals(document, Assert.Single(area.Documents)), "The renamed file was closed and opened again, losing its undo and carets");
        Assert.Equal(purchase, document.Path);
        Assert.True(area.Groups.Count == 2, "The rename closed the file's second split");
        Assert.All(area.Groups, group => Assert.Same(document, Assert.Single(group.Tabs).Document));
        Assert.Equal(["Purchase.cs", "Purchase.cs"], window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("tabName")).Select(t => t.Text));
        Assert.Empty(window.Debugger.Breakpoints.In(order));
        Assert.Equal(4, Assert.Single(window.Debugger.Breakpoints.In(purchase)).Line);
        Assert.Contains(new LineMark(4, LineMarkKind.Breakpoint), area.CodeEditor.LineMarks);
        Assert.Equal(purchase, Assert.Single(window.Bookmarks.All).Path);
        Assert.Equal([4], document.Bookmarks);

        // The breakpoint still follows its line at the new path, and undo reaches back past the rename.
        area.CodeEditor.GoTo(new TextPosition(0, 0));
        area.CodeEditor.Focus();
        Ui.Press(window, "Enter");
        Assert.Equal(5, Assert.Single(window.Debugger.Breakpoints.In(purchase)).Line);
        area.CodeEditor.TryExecute(EditorActions.Undo);
        area.CodeEditor.TryExecute(EditorActions.Undo);
        Ui.Settle();
        Assert.Equal("namespace Shop.Models;\n\npublic class Order\n{\n    public int Total;\n}\n", document.Text.Snapshot.ToString());
        Assert.Equal(4, Assert.Single(window.Debugger.Breakpoints.In(purchase)).Line);
        window.Close();
    }

    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    /// <summary>Agrees to every question the window asks, keeping their titles in order.</summary>
    private static List<string> Answer(MainWindow window)
    {
        List<string> asked = [];
        window.Confirm = (title, _, _) =>
        {
            asked.Add(title);
            return Task.FromResult(true);
        };
        return asked;
    }

    /// <summary>Whether the status bar says the work ended, as it does or with why it could not.</summary>
    private static bool Finished(MainWindow window, string done) =>
        window.Status is { } status && (status.StartsWith(done, StringComparison.Ordinal) || status.Contains("could not", StringComparison.Ordinal));

    /// <summary>Clicks a row of the Explorer, as a person chooses one before pressing a key.</summary>
    private static void Choose(MainWindow window, ExplorerNode row)
    {
        ListBox rows = window.ExplorerView.GetVisualDescendants().OfType<ListBox>().First(l => ReferenceEquals(l.ItemsSource, window.ExplorerView.Tree.Rows));
        Control item = rows.ContainerFromItem(row) ?? throw new InvalidOperationException($"The Explorer does not show {row.Name}");
        Point at = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
        Assert.Equal([row], window.ExplorerView.SelectedNodes);
    }
}
