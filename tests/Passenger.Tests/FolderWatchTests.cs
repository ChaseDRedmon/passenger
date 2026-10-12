using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Workspace;

namespace Passenger.Tests;

/// <summary>Changes another program makes in the folder, seen at once as Rider's fsnotifier sees them: the Explorer,
/// Go to File and the open files follow, an open file with unsaved edits asks which version to keep, and a project
/// added on disk joins the Solution view and the run configurations.</summary>
public sealed class FolderWatchTests : IDisposable
{
    private readonly string folder = PathNames.Canonical(Directory.CreateTempSubdirectory("passenger-watch-").FullName);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [AvaloniaFact]
    public void Files_created_changed_and_deleted_elsewhere_reach_the_explorer_go_to_file_and_the_open_tabs()
    {
        File.WriteAllText(Path.Combine(folder, "One.cs"), "class One { }\n");
        MainWindow window = Open();
        window.OpenFile("One.cs");
        Ui.Settle();

        File.WriteAllText(Path.Combine(folder, "Two.cs"), "class Two { }\n");
        Ui.WaitFor(() => window.ExplorerView.Tree.Rows.Any(r => r.Name == "Two.cs"), "the new file in the Explorer");
        Ui.WaitFor(() => window.FileIndex.Files.Contains("Two.cs"), "the new file in Go to File");

        File.WriteAllText(Path.Combine(folder, "One.cs"), "class One { int changed; }\n");
        Ui.WaitFor(() => Text(window) == "class One { int changed; }\n", "the open file to take the text on disk");
        Assert.False(window.EditorArea.Active!.IsModified);

        File.Delete(Path.Combine(folder, "One.cs"));
        Ui.WaitFor(() => window.EditorArea.Documents.Count == 0, "the deleted file's tab to close");
        Ui.WaitFor(() => !window.ExplorerView.Tree.Rows.Any(r => r.Name == "One.cs"), "the deleted file to leave the Explorer");
        Assert.False(File.Exists(Path.Combine(folder, "One.cs")), "Closing the deleted file's tab wrote it back");
        window.Close();
    }

    [AvaloniaFact]
    public void A_file_with_unsaved_edits_changed_on_disk_asks_which_to_keep()
    {
        string path = Path.Combine(folder, "One.cs");
        File.WriteAllText(path, "class One { }\n");
        MainWindow window = Open();
        window.OpenFile("One.cs");
        Ui.Settle();
        List<string> asked = [];
        bool loadDisk = false;
        window.AskConflict = document =>
        {
            asked.Add(document.Name);
            return Task.FromResult(loadDisk);
        };
        Type(window, "public ");

        // Keep Memory Changes: the edits are written over the disk's.
        File.WriteAllText(path, "class One { int disk; }\n");
        Ui.WaitFor(() => asked.Count == 1, "the File Cache Conflict question");
        Assert.Equal("One.cs", asked[0]);
        Assert.Equal("public class One { }\n", Text(window));
        Assert.Equal("public class One { }\n", File.ReadAllText(path));
        Assert.False(window.EditorArea.Active!.IsModified);

        // Load File System Changes: the disk's text replaces the edits.
        Type(window, "sealed ");
        loadDisk = true;
        File.WriteAllText(path, "class One { int again; }\n");
        Ui.WaitFor(() => asked.Count == 2, "the question again for the next change");
        Ui.WaitFor(() => Text(window) == "class One { int again; }\n", "the disk's text");
        Assert.False(window.EditorArea.Active!.IsModified);
        window.Close();
    }

    [AvaloniaFact]
    public void While_the_conflict_question_is_open_nothing_is_saved_over_the_disks_version()
    {
        string path = Path.Combine(folder, "One.cs");
        File.WriteAllText(path, "class One { }\n");
        MainWindow window = Open();
        window.OpenFile("One.cs");
        Ui.Settle();
        window.ApplySettings(window.Settings with { SaveWhenIdle = true, IdleSeconds = 1 });
        Type(window, "public ");

        File.WriteAllText(path, "class One { int disk; }\n");
        Ui.WaitFor(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(), "the File Cache Conflict dialog");
        // The dialog has the focus and the idle wait goes by: neither writes over the disk's version, nor does Save.
        Passenger.App.Screenshot.Pump(TimeSpan.FromSeconds(1.5));
        window.Perform(AppActions.Save);
        Ui.Settle();
        Assert.Equal("class One { int disk; }\n", File.ReadAllText(path));
        Assert.Single(window.OwnedWindows.OfType<ConfirmDialog>());

        ConfirmDialog dialog = window.OwnedWindows.OfType<ConfirmDialog>().Single();
        Avalonia.Controls.Button load = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog)
            .OfType<Avalonia.Controls.Button>().First(b => (string?)b.Content == "Load File System Changes");
        load.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Ui.WaitFor(() => Text(window) == "class One { int disk; }\n", "the disk's text in the editor");
        Assert.False(window.EditorArea.Active!.IsModified);
        window.Close();
    }

    [AvaloniaFact]
    public void A_file_with_unsaved_edits_deleted_on_disk_stays_open_and_saving_writes_it_back()
    {
        string path = Path.Combine(folder, "Gone", "One.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "class One { }\n");
        MainWindow window = Open();
        window.OpenFile(path);
        Ui.Settle();
        Type(window, "public ");

        // Deleted with its folder (a git clean), the file keeps its tab and its edits.
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        Ui.WaitFor(() => !window.ExplorerView.Tree.Rows.Any(r => r.Name == "Gone"), "the deletion to be seen");
        Assert.Equal("public class One { }\n", Text(window));

        window.Perform(AppActions.Save);
        Assert.Equal("public class One { }\n", File.ReadAllText(path));
        Assert.False(window.EditorArea.Active!.IsModified);
        window.Close();
        Assert.False(window.IsVisible, "The window would not close");
    }

    private static void Type(MainWindow window, string text)
    {
        window.EditorArea.CodeEditor.Selections = Passenger.Text.SelectionSet.Single(0);
        window.KeyTextInput(text);
        Ui.Settle();
    }

    [AvaloniaFact]
    public void A_project_added_to_the_solution_on_disk_joins_the_solution_view_and_the_run_configurations()
    {
        Write("App/App.csproj", Project);
        Write("Shop.slnx", "<Solution>\n  <Project Path=\"App/App.csproj\" />\n</Solution>\n");
        MainWindow window = Open();
        Assert.Equal(["App"], window.Configurations.Select(c => c.Name));

        Write("Tool/Tool.csproj", Project);
        Write("Shop.slnx", "<Solution>\n  <Project Path=\"App/App.csproj\" />\n  <Project Path=\"Tool/Tool.csproj\" />\n</Solution>\n");
        Ui.WaitFor(() => window.Configurations.Any(c => c.Name == "Tool"), "the new project's run configuration");
        Assert.Equal(2, window.Solution!.Projects.Count());
        Assert.True(window.ExplorerView.Tree.IsSolutionView);
        Ui.WaitFor(() => window.ExplorerView.Tree.Rows.Any(r => r.Name == "Tool"), "the new project in the Solution view");
        window.Close();
    }

    private const string Project = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n";

    private void Write(string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    private static string Text(MainWindow window) => window.EditorArea.CodeEditor.Document.Snapshot.ToString();
}
