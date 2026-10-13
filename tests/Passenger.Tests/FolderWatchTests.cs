using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Explorer;
using Passenger.App.Workspace;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>Changes another program makes in the folder, seen at once as Rider's fsnotifier sees them: the Explorer,
/// Go to File and the open files follow, an open file with unsaved edits asks which version to keep, a project added on
/// disk joins the Solution view and the run configurations, and the solution the folder's files now pick is the one
/// the Explorer and the language server both open. Linux's way of watching, a watch for each kept folder from a pool
/// with a limit, is chosen here on any system, since only the kernel's own running out is Linux's alone.</summary>
public sealed class FolderWatchTests : IDisposable
{
    private readonly string folder = PathNames.Canonical(Directory.CreateTempSubdirectory("passenger-watch-").FullName);

    public void Dispose() => TestFolders.Delete(folder);

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

    [AvaloniaFact]
    public void Lines_changed_on_disk_leave_breakpoints_bookmarks_and_the_caret_with_their_own_lines()
    {
        string path = Path.Combine(folder, "Long.cs");
        File.WriteAllText(path, string.Concat(Enumerable.Range(1, 60).Select(i => $"// line {i}\n")));
        MainWindow window = Open();
        window.OpenFile(path);
        Ui.Settle();
        string open = window.EditorArea.Active!.Path;
        window.Debugger.Breakpoints.Toggle(open, 19);
        window.Debugger.Breakpoints.Toggle(open, 39);
        window.Bookmarks.Toggle(open, 19);
        window.Bookmarks.Toggle(open, 39);
        Passenger.Editor.CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new Passenger.Text.TextPosition(29, 4));

        // A checkout changes line 5 (adding one after it) and line 50, around everything the person placed.
        File.WriteAllText(path, string.Concat(Enumerable.Range(1, 60).Select(i =>
            i == 5 ? "// line 5, changed\n// and one more\n" : i == 50 ? "// line 50, changed\n" : $"// line {i}\n")));
        Ui.WaitFor(() => Text(window).Contains("line 50, changed", StringComparison.Ordinal), "the disk's text");
        Assert.Equal([20, 40], window.Debugger.Breakpoints.In(open).Select(b => b.Line));
        Assert.Equal([20, 40], window.Bookmarks.LinesIn(open));
        Assert.Equal(new Passenger.Text.TextPosition(30, 4), editor.CaretPosition);
        window.Close();
    }

    [AvaloniaFact]
    public void A_BOM_added_on_disk_or_a_conversion_to_UTF_8_survives_the_next_save_without_a_question()
    {
        string path = Path.Combine(folder, "One.cs");
        File.WriteAllText(path, "class One { }\n");
        File.WriteAllBytes(Path.Combine(folder, "Two.cs"), Encoding.Latin1.GetBytes("// Café\n"));
        MainWindow window = Open();
        window.OpenFile("One.cs");
        Ui.Settle();
        List<string> asked = [];
        window.AskConflict = document =>
        {
            asked.Add(document.Name);
            return Task.FromResult(false);
        };
        Type(window, "public ");

        // dotnet format with charset = utf-8-bom: the same text, a BOM before it, under unsaved edits.
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("class One { }\n")]);
        Ui.WaitFor(() => EncodingShown(window) == "UTF-8 BOM", "the status bar to show the BOM added on disk");
        window.Perform(AppActions.Save);
        Assert.Equal([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("public class One { }\n")], File.ReadAllBytes(path));

        // A Latin-1 file converted to UTF-8 elsewhere is saved as UTF-8 from then on.
        window.OpenFile("Two.cs");
        Ui.Settle();
        Assert.Equal("ISO-8859-1", EncodingShown(window));
        File.WriteAllBytes(Path.Combine(folder, "Two.cs"), Encoding.UTF8.GetBytes("// Café\n"));
        Ui.WaitFor(() => EncodingShown(window) == "UTF-8", "the status bar to show the file converted on disk");
        Type(window, "// señor ");
        window.Perform(AppActions.Save);
        Assert.Equal(Encoding.UTF8.GetBytes("// señor // Café\n"), File.ReadAllBytes(Path.Combine(folder, "Two.cs")));
        Assert.Empty(asked);
        window.Close();
    }

    [AvaloniaFact]
    public void Watching_each_folder_as_on_Linux_leaves_build_output_dependencies_and_gits_objects_unwatched_and_follows_new_folders()
    {
        Write("src/App/App.cs", "class App { }\n");
        Write("src/App/bin/Debug/App.dll", "");
        Write("src/App/obj/project.assets.json", "{}\n");
        Write("node_modules/left-pad/index.js", "");
        Write("artifacts/bin/App/debug/App.dll", "");
        Write("packages/Some.Package/Some.Package.nupkg", "");
        Write("TestResults/run.trx", "");
        Write(".vs/state", "");
        Git("init", "-q", "-b", "main");
        Git("-c", "user.email=tests@passenger.invalid", "-c", "user.name=Passenger Tests", "commit", "-q", "--allow-empty", "-m", "First");
        MainWindow window = Open(FolderWatching.Inotify);
        window.OpenFile("src/App/App.cs");
        Ui.Landed(() => window.Vcs.Branch == "main", "the branch");

        string[] watched = Watched(window);
        foreach (string kept in new[] { ".", ".git", ".git/refs", ".git/refs/heads", "src", "src/App" })
        {
            Assert.True(watched.Contains(kept), $"{kept} is not watched, so what other programs change in it goes unseen");
        }
        Assert.All(watched, path => Assert.True(path is "." or ".git" or "src" or "src/App" || path.StartsWith(".git/refs", StringComparison.Ordinal),
            $"{path} is watched, spending one of the user's inotify watches on a folder nothing in the IDE looks at"));

        // Each kept folder's own watch is what hears of its files, Git's HEAD among them.
        File.WriteAllText(Path.Combine(folder, "src", "App", "App.cs"), "class App { int changed; }\n");
        Ui.WaitFor(() => Text(window) == "class App { int changed; }\n", "the open file deep in the folder to take the text on disk");
        Git("switch", "-q", "-c", "side");
        Ui.WaitFor(() => window.Vcs.Branch == "side", "the branch checked out elsewhere to show");

        // A folder made later is watched with the kept folders in it, before a file is made there.
        Directory.CreateDirectory(Path.Combine(folder, "src", "Feature", "Deep"));
        Directory.CreateDirectory(Path.Combine(folder, "src", "Feature", "obj"));
        Ui.WaitFor(() => Watched(window).Contains("src/Feature/Deep"), "the folders made to be watched");
        Ui.Settle();
        Assert.DoesNotContain("src/Feature/obj", Watched(window));
        string deep = Path.Combine("src", "Feature", "Deep", "Deep.cs");
        Assert.DoesNotContain(deep, window.FileIndex.Files);
        Write("src/Feature/Deep/Deep.cs", "class Deep { }\n");
        Ui.WaitFor(() => window.FileIndex.Files.Contains(deep), "the file made in the new folder in Go to File, which only that folder's watch hears of");

        // Windows moves or deletes a watched folder only once its watch lets go; there one watch covers the tree anyway.
        if (!OperatingSystem.IsWindows())
        {
            // A folder renamed is watched under its new name, its old name's watches let go of.
            Directory.Move(Path.Combine(folder, "src", "Feature"), Path.Combine(folder, "src", "Moved"));
            Ui.WaitFor(() => Watched(window).Contains("src/Moved/Deep") && !Watched(window).Any(p => p.StartsWith("src/Feature", StringComparison.Ordinal)),
                "the renamed folders to be watched under their new names alone");
            Ui.Settle();
            string moved = Path.Combine("src", "Moved", "Deep", "Moved.cs");
            Write("src/Moved/Deep/Moved.cs", "class Moved { }\n");
            Ui.WaitFor(() => window.FileIndex.Files.Contains(moved), "the file made in the renamed folder in Go to File");

            Directory.Delete(Path.Combine(folder, "src", "Moved"), recursive: true);
            Ui.WaitFor(() => !Watched(window).Any(p => p.StartsWith("src/Moved", StringComparison.Ordinal)), "the deleted folders' watches to be let go of");
            Ui.WaitFor(() => !window.FileIndex.Files.Contains(moved), "the deleted file to leave Go to File");
        }
        window.Close();
    }

    [AvaloniaFact]
    public void Running_out_of_inotify_watches_is_one_ide_error_saying_how_to_raise_the_limit()
    {
        string settings = Directory.CreateTempSubdirectory("passenger-settings-").FullName;
        string? before = Environment.GetEnvironmentVariable("PASSENGER_SETTINGS");
        Environment.SetEnvironmentVariable("PASSENGER_SETTINGS", settings);
        try
        {
            Write("One/One.cs", "class One { }\n");
            Write("Two/Two.cs", "class Two { }\n");
            MainWindow window = Open(FolderWatching.Inotify);
            IReadOnlyDictionary<string, FileSystemWatcher> watches = window.FolderWatcher!.Watches;

            // What .NET's watcher raises on Linux when inotify_add_watch fails with ENOSPC, from each folder's own watcher.
            RaiseError(watches[Path.Combine(folder, "One")], OutOfWatches());
            RaiseError(watches[Path.Combine(folder, "Two")], OutOfWatches());
            Ui.WaitFor(() => window.Status?.Contains("fs.inotify.max_user_watches", StringComparison.Ordinal) == true,
                "the status bar to say the inotify watches ran out");
            Ui.Settle();
            IdeError error = Assert.Single(IdeErrors.Errors);
            Assert.StartsWith("Changes made outside Passenger in some folders will go unseen", error.Message, StringComparison.Ordinal);
            Assert.Contains("sudo sysctl --system", error.Message, StringComparison.Ordinal);
            Assert.Contains("fs.inotify.max_user_watches", Ui.IdeLog(), StringComparison.Ordinal);
            Assert.Equal(["."], Watched(window));

            // The folders still watched still say what changes in them.
            Write("Three.cs", "class Three { }\n");
            Ui.WaitFor(() => window.FileIndex.Files.Contains("Three.cs"), "a file made in a folder still watched in Go to File");
            // Reported on purpose: the check that fails a test over a caught error is not to see it.
            IdeErrors.Clear();
            window.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PASSENGER_SETTINGS", before);
            TestFolders.Delete(settings);
        }
    }

    private static IOException OutOfWatches() =>
        new("The configured user limit (8192) on the number of inotify watches has been reached, or the operating system failed to allocate a required resource.", 28);

    /// <summary>A watcher's error raised as the system's watcher raises it, which no kernel but Linux's gives for this.</summary>
    private static void RaiseError(FileSystemWatcher watcher, Exception error) =>
        typeof(FileSystemWatcher).GetMethod("OnError", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(watcher, [new ErrorEventArgs(error)]);

    /// <summary>The folders watched, from the open folder, with forward slashes.</summary>
    private string[] Watched(MainWindow window) =>
        [.. window.FolderWatcher!.Watches.Keys.Select(p => Path.GetRelativePath(folder, p).Replace('\\', '/')).Order(StringComparer.Ordinal)];

    private void Git(params string[] arguments)
    {
        using Process process = Process.Start(new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {error}");
    }

    private static string? EncodingShown(MainWindow window) => Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.TextBlock>(window, "EncodingText")!.Text;

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

    [AvaloniaFact]
    public void A_folders_sln_and_slnx_listing_different_projects_give_the_explorer_and_the_language_server_the_same_one()
    {
        // As dotnet sln migrate leaves a folder, with a project added to the .slnx since.
        Write("App/App.csproj", Project);
        Write("App/Program.cs", "System.Console.WriteLine(\"app\");\n");
        Write("Extra/Extra.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        Write("Extra/Helper.cs", "public static class Helper\n{\n    public static string Name => \"extra\";\n}\n");
        Write("Extra/Use.cs", "public static class Use\n{\n    public static int Count()\n    {\n        int count = Helper.Name;\n        return count;\n    }\n}\n");
        Write("Foo.sln", ClassicSolution);
        Write("Foo.slnx", "<Solution>\n  <Project Path=\"App/App.csproj\" />\n  <Project Path=\"Extra/Extra.csproj\" />\n</Solution>\n");
        Restore.Run(Path.Combine(folder, "Foo.slnx"));
        string slnx = Path.Combine(folder, "Foo.slnx");
        string sln = Path.Combine(folder, "Foo.sln");
        MainWindow window = Open();
        Assert.Equal(slnx, window.Solution?.Path);
        Assert.Equal(slnx, window.CSharpService?.Session?.Solution);
        Assert.Contains(window.ExplorerView.Tree.Rows, r => r.Kind == NodeKind.Project && r.Name == "Extra");
        // Helper is known only with Extra's project loaded: without it the error would be an unknown name, not a conversion.
        window.OpenFile("Extra/Use.cs");
        Ui.WaitFor(() => Codes(window).Contains("CS0029"), "the language server to load Extra, which the Explorer shows");

        // The .slnx deleted, both read the .sln, which has no Extra.
        File.Delete(slnx);
        Ui.WaitFor(() => window.Solution?.Path == sln, "the Explorer to show the .sln");
        Assert.DoesNotContain(window.ExplorerView.Tree.Rows, r => r.Kind == NodeKind.Project && r.Name == "Extra");
        Ui.WaitFor(() => window.CSharpService?.Session?.Solution == sln, "the language server to open the .sln the Explorer shows");
        window.Close();
    }

    private static IEnumerable<string?> Codes(MainWindow window) =>
        window.CSharpService?.For(window.EditorArea.Active!)?.Diagnostics.Select(d => d.Code?.ToString()) ?? [];

    /// <summary>A Visual Studio solution naming App alone.</summary>
    private const string ClassicSolution = """

        Microsoft Visual Studio Solution File, Format Version 12.00
        # Visual Studio Version 17
        VisualStudioVersion = 17.0.31903.59
        MinimumVisualStudioVersion = 10.0.40219.1
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "App\App.csproj", "{6F1A3C0E-1B2D-4C5E-9F70-1A2B3C4D5E6F}"
        EndProject
        Global
            GlobalSection(SolutionConfigurationPlatforms) = preSolution
                Debug|Any CPU = Debug|Any CPU
                Release|Any CPU = Release|Any CPU
            EndGlobalSection
            GlobalSection(ProjectConfigurationPlatforms) = postSolution
                {6F1A3C0E-1B2D-4C5E-9F70-1A2B3C4D5E6F}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                {6F1A3C0E-1B2D-4C5E-9F70-1A2B3C4D5E6F}.Debug|Any CPU.Build.0 = Debug|Any CPU
                {6F1A3C0E-1B2D-4C5E-9F70-1A2B3C4D5E6F}.Release|Any CPU.ActiveCfg = Release|Any CPU
                {6F1A3C0E-1B2D-4C5E-9F70-1A2B3C4D5E6F}.Release|Any CPU.Build.0 = Release|Any CPU
            EndGlobalSection
        EndGlobal

        """;

    private const string Project = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n";

    private void Write(string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private MainWindow Open(FolderWatching? watching = null)
    {
        MainWindow window = new() { Width = 1200, Height = 800, Watching = watching ?? FolderWatching.Platform };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    private static string Text(MainWindow window) => window.EditorArea.CodeEditor.Document.Snapshot.ToString();
}
