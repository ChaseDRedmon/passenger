using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Explorer;
using Passenger.App.Workspace;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>Solution folders as Rider's Solution view shows and edits them: an icon of their own, the files they list,
/// folders before projects by name at every level, and New Solution Folder, Rename, Existing Project, Existing Item and
/// Remove from their menus and keys, and a project's Rename, each changing the .sln or .slnx on disk and nothing else in
/// it, which still builds.</summary>
public sealed class SolutionFoldersTests : IDisposable
{
    private const string FolderType = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";

    private const string Library = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n";

    private const string Executable = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n";

    /// <summary>As Visual Studio writes a .sln: a byte order mark, a blank first line and CRLF; the folders declared after
    /// the project at the top, the Solution Items' files out of order.</summary>
    private const string Sln = "﻿\r\nMicrosoft Visual Studio Solution File, Format Version 12.00\r\n# Visual Studio Version 17\r\nVisualStudioVersion = 17.10.35013.160\r\nMinimumVisualStudioVersion = 10.0.40219.1\r\n"
        + "Project(\"{9A19103F-16F7-4668-BE54-9A1E7A4F7556}\") = \"Shop.Web\", \"src\\Shop.Web\\Shop.Web.csproj\", \"{A1000000-0000-0000-0000-000000000001}\"\r\nEndProject\r\n"
        + "Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"Solution Items\", \"Solution Items\", \"{B1000000-0000-0000-0000-000000000001}\"\r\n"
        + "\tProjectSection(SolutionItems) = preProject\r\n\t\tREADME.md = README.md\r\n\t\t.editorconfig = .editorconfig\r\n\tEndProjectSection\r\nEndProject\r\n"
        + "Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"src\", \"src\", \"{B1000000-0000-0000-0000-000000000002}\"\r\nEndProject\r\n"
        + "Project(\"{9A19103F-16F7-4668-BE54-9A1E7A4F7556}\") = \"Shop.Core\", \"src\\Shop.Core\\Shop.Core.csproj\", \"{A1000000-0000-0000-0000-000000000002}\"\r\nEndProject\r\n"
        + "Global\r\n\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n\t\tDebug|Any CPU = Debug|Any CPU\r\n\t\tRelease|Any CPU = Release|Any CPU\r\n\tEndGlobalSection\r\n"
        + "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n"
        + "\t\t{A1000000-0000-0000-0000-000000000001}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n\t\t{A1000000-0000-0000-0000-000000000001}.Debug|Any CPU.Build.0 = Debug|Any CPU\r\n"
        + "\t\t{A1000000-0000-0000-0000-000000000001}.Release|Any CPU.ActiveCfg = Release|Any CPU\r\n\t\t{A1000000-0000-0000-0000-000000000001}.Release|Any CPU.Build.0 = Release|Any CPU\r\n"
        + "\t\t{A1000000-0000-0000-0000-000000000002}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n\t\t{A1000000-0000-0000-0000-000000000002}.Debug|Any CPU.Build.0 = Debug|Any CPU\r\n"
        + "\t\t{A1000000-0000-0000-0000-000000000002}.Release|Any CPU.ActiveCfg = Release|Any CPU\r\n\t\t{A1000000-0000-0000-0000-000000000002}.Release|Any CPU.Build.0 = Release|Any CPU\r\n"
        + "\tEndGlobalSection\r\n\tGlobalSection(SolutionProperties) = preSolution\r\n\t\tHideSolutionNode = FALSE\r\n\tEndGlobalSection\r\n"
        + "\tGlobalSection(NestedProjects) = preSolution\r\n\t\t{A1000000-0000-0000-0000-000000000002} = {B1000000-0000-0000-0000-000000000002}\r\n\tEndGlobalSection\r\n"
        + "\tGlobalSection(ExtensibilityGlobals) = postSolution\r\n\t\tSolutionGuid = {C1000000-0000-0000-0000-000000000001}\r\n\tEndGlobalSection\r\nEndGlobal\r\n";

    private const string Slnx = "<Solution>\n  <Project Path=\"src/Shop.Web/Shop.Web.csproj\" />\n"
        + "  <Folder Name=\"/Solution Items/\">\n    <File Path=\"README.md\" />\n    <File Path=\".editorconfig\" />\n  </Folder>\n"
        + "  <Folder Name=\"/src/\">\n    <Project Path=\"src/Shop.Core/Shop.Core.csproj\" />\n  </Folder>\n</Solution>\n";

    private readonly string folder = PathNames.Canonical(TestFolders.Create("solution-folders-"));

    public SolutionFoldersTests()
    {
        Write("src/Shop.Web/Shop.Web.csproj", Executable);
        Write("src/Shop.Web/Program.cs", "System.Console.WriteLine(\"Shop\");\n");
        Write("src/Shop.Core/Shop.Core.csproj", Library);
        Write("src/Shop.Core/Order.cs", "namespace Shop.Core;\n\npublic class Order\n{\n}\n");
        Write("tools/Importer/Importer.csproj", Executable);
        Write("tools/Importer/Program.cs", "System.Console.WriteLine(\"Import\");\n");
        Write("docs/notes.md", "# Notes\n");
        Write("README.md", "# Shop\n");
        Write(".editorconfig", "root = true\n");
    }

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaTheory]
    [InlineData("sln")]
    [InlineData("slnx")]
    public void A_solution_folder_has_its_own_icon_lists_its_files_and_sorts_before_the_projects_by_name(string format)
    {
        string solution = WriteSolution(format);
        MainWindow window = Open();
        ExplorerTree tree = window.ExplorerView.Tree;
        ExplorerNode items = Row(window, NodeKind.SolutionFolder, "Solution Items");
        Assert.True(ReferenceEquals(Resource("Icon.SolutionFolder"), items.Icon), "A closed solution folder is drawn as a folder on disk is, so it passes for one");
        Assert.NotSame(Resource("Passenger.File.Folder"), items.IconBrush);
        Assert.Same(Resource("Passenger.File.SolutionFolder"), items.IconBrush);

        // Opened from the keys, as a person opens one: Right on the chosen row.
        Choose(window, items);
        Ui.Press(window, "RIGHT");
        Choose(window, Row(window, NodeKind.SolutionFolder, "src"));
        Ui.Press(window, "RIGHT");
        Assert.True(ReferenceEquals(Resource("Icon.SolutionFolderOpen"), items.Icon), "An open solution folder still shows the closed one's icon");
        // The solution declares the project first and README.md before .editorconfig; Rider shows folders, then projects,
        // then files, each by name.
        Assert.Equal(
            ["Shop", "  Solution Items", "    .editorconfig", "    README.md", "  src", "    Shop.Core", "  Shop.Web"],
            tree.Rows.Select(r => new string(' ', r.Depth * 2) + r.Name));
        Assert.Equal(
            [NodeKind.Solution, NodeKind.SolutionFolder, NodeKind.File, NodeKind.File, NodeKind.SolutionFolder, NodeKind.Project, NodeKind.Project],
            tree.Rows.Select(r => r.Kind));

        // A file a solution folder lists opens on a double-click, as any file does.
        DoubleClick(window, Row(window, NodeKind.File, "README.md"));
        Assert.Equal(PathNames.Canonical(Path.Combine(folder, "README.md")), window.EditorArea.Active?.Path);

        // Remove on one of them takes it out of the solution folder; the file stays on disk.
        List<string> asked = Answer(window);
        Choose(window, Row(window, NodeKind.File, ".editorconfig"));
        Ui.Press(window, "BACK_SPACE");
        Assert.Equal(["Remove Item"], asked);
        Assert.DoesNotContain(tree.Rows, r => r.Name == ".editorconfig");
        Assert.True(File.Exists(Path.Combine(folder, ".editorconfig")), "Removing a solution item deleted its file");
        string left = File.ReadAllText(solution);
        Assert.DoesNotContain(".editorconfig", left, StringComparison.Ordinal);
        Assert.Contains("README.md", left, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("sln")]
    [InlineData("slnx")]
    public void A_solution_folder_added_renamed_given_a_project_and_a_file_and_removed_changes_only_its_own_lines(string format)
    {
        bool slnx = format == "slnx";
        string solution = WriteSolution(format);
        byte[] original = File.ReadAllBytes(solution);
        MainWindow window = Open();
        List<string> asked = Answer(window);
        IReadOnlyList<string> pick = [];
        window.PickFiles = (_, _) => Task.FromResult(pick);
        ExplorerTree tree = window.ExplorerView.Tree;
        // The folder watcher hears of each change to the file and reads the solution again, making new rows: that lands
        // before the next step finds one.
        int heard = 0;
        window.FolderWatcher!.Changed += (_, changes) => heard += changes.Paths.Contains(PathNames.Canonical(solution)) ? 1 : 0;
        void Edit(Action edit)
        {
            int before = heard;
            edit();
            Ui.WaitFor(() => heard > before, "the folder watcher to hear of the change to the solution");
            Ui.Settle();
        }

        // New Solution Folder, from the solution's own menu.
        RightClick(window, Row(window, NodeKind.Solution, "Shop"));
        ChooseFromMenu(window, "Add", "New Solution Folder...");
        Edit(() => Name(window, "Tools"));
        ExplorerNode tools = Row(window, NodeKind.SolutionFolder, "Tools");
        Assert.Equal(1, tools.Depth);
        Assert.Same(tools, window.ExplorerView.Selected);
        string text = Read(solution, "a solution folder added");
        if (slnx)
        {
            Assert.Contains("    <Project Path=\"src/Shop.Core/Shop.Core.csproj\" />\n  </Folder>\n  <Folder Name=\"/Tools/\" />\n</Solution>\n", text, StringComparison.Ordinal);
        }
        else
        {
            Assert.Matches(new Regex($"^Project\\(\"{Regex.Escape(FolderType)}\"\\) = \"Tools\", \"Tools\", \"\\{{[0-9A-F-]{{36}}\\}}\"\r$", RegexOptions.Multiline), text);
        }
        Builds(solution, "a solution folder was added");

        // Rename, by its key.
        Choose(window, Row(window, NodeKind.SolutionFolder, "Tools"));
        Ui.Run(window, AppActions.Rename);
        Edit(() => Name(window, "Utilities"));
        Assert.DoesNotContain(tree.Rows, r => r.Name == "Tools");
        // Each change reads the solution again, so the folder's row is a new one each time.
        ExplorerNode Utilities() => Row(window, NodeKind.SolutionFolder, "Utilities");
        Assert.Same(Utilities(), window.ExplorerView.Selected);
        text = Read(solution, "the solution folder renamed");
        Assert.DoesNotContain("Tools", text, StringComparison.Ordinal);
        Assert.Contains(slnx ? "<Folder Name=\"/Utilities/\" />" : "\"Utilities\", \"Utilities\"", text, StringComparison.Ordinal);
        Builds(solution, "a solution folder was renamed");

        // Existing Project, from its menu opened by the menu key.
        pick = [Path.Combine(folder, "tools", "Importer", "Importer.csproj")];
        Choose(window, Utilities());
        Ui.Run(window, AppActions.ShowContextMenu);
        Edit(() => ChooseFromMenu(window, "Add", "Existing Project..."));
        Assert.True(Utilities().IsExpanded, "The folder a project was added to stayed closed, hiding it");
        Assert.Equal(2, Row(window, NodeKind.Project, "Importer").Depth);
        text = Read(solution, "a project added to the solution folder");
        string? importer = null;
        if (slnx)
        {
            Assert.Contains("  <Folder Name=\"/Utilities/\">\n    <Project Path=\"tools/Importer/Importer.csproj\" />\n  </Folder>\n", text, StringComparison.Ordinal);
        }
        else
        {
            importer = Assert.Single(Regex.Matches(text, "\"Importer\", \"tools\\\\Importer\\\\Importer.csproj\", \"(\\{[0-9A-F-]{36}\\})\"\r\n")).Groups[1].Value;
            // A project the configurations do not name is skipped when the solution builds.
            Assert.Contains($"\t\t{importer}.Debug|Any CPU.Build.0 = Debug|Any CPU\r\n", text, StringComparison.Ordinal);
            Assert.Contains($"\t\t{importer}.Release|Any CPU.Build.0 = Release|Any CPU\r\n", text, StringComparison.Ordinal);
            Assert.Contains($"\t\t{importer} = {Id(text, "Utilities")}\r\n", text, StringComparison.Ordinal);
        }
        Builds(solution, "a project was added to a solution folder");
        Assert.True(File.Exists(Path.Combine(folder, "tools", "Importer", "bin", "Debug", "net10.0", "Importer.dll")),
            "The project added to the solution folder was not built with the solution");

        // Existing Item, and a folder inside it.
        pick = [Path.Combine(folder, "docs", "notes.md")];
        RightClick(window, Utilities());
        Edit(() => ChooseFromMenu(window, "Add", "Existing Item..."));
        Assert.Equal(PathNames.Canonical(Path.Combine(folder, "docs", "notes.md")), window.ExplorerView.Selected?.Path);
        RightClick(window, Utilities());
        ChooseFromMenu(window, "Add", "New Solution Folder...");
        Edit(() => Name(window, "Scripts"));
        Assert.Equal(
            ["  Utilities", "    Scripts", "    Importer", "    notes.md"],
            tree.Rows.SkipWhile(r => r.Name != "Utilities").TakeWhile((r, i) => i == 0 || r.Depth > 1).Select(r => new string(' ', r.Depth * 2) + r.Name));
        text = Read(solution, "a file and a folder added to the solution folder");
        if (slnx)
        {
            Assert.Contains("  <Folder Name=\"/Utilities/\">\n    <File Path=\"docs/notes.md\" />\n    <Project Path=\"tools/Importer/Importer.csproj\" />\n  </Folder>\n  <Folder Name=\"/Utilities/Scripts/\" />\n</Solution>\n", text, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("\"Utilities\", \"Utilities\", \"" + Id(text, "Utilities") + "\"\r\n\tProjectSection(SolutionItems) = preProject\r\n\t\tdocs\\notes.md = docs\\notes.md\r\n\tEndProjectSection\r\nEndProject\r\n", text, StringComparison.Ordinal);
            Assert.Contains($"\t\t{Id(text, "Scripts")} = {Id(text, "Utilities")}\r\n", text, StringComparison.Ordinal);
        }
        Builds(solution, "a file and a folder were added to a solution folder");

        // Remove, by its key, once the person agrees to what goes with it.
        Choose(window, Utilities());
        Edit(() => Ui.Press(window, "BACK_SPACE"));
        Assert.Equal(["Remove Solution Folder"], asked);
        Assert.DoesNotContain(tree.Rows, r => r.Name is "Utilities" or "Importer" or "notes.md" or "Scripts");
        Assert.True(File.Exists(Path.Combine(folder, "tools", "Importer", "Importer.csproj")), "Removing the solution folder deleted its project's files");
        Assert.True(File.Exists(Path.Combine(folder, "docs", "notes.md")), "Removing the solution folder deleted its file");
        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(solution)),
            $"With the folder removed, the solution file is not as it was before it was added:\n{Read(solution, "the solution folder removed")}");
        Builds(solution, "the solution folder was removed");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("sln")]
    [InlineData("slnx")]
    public void A_project_renamed_takes_its_solution_entry_and_the_references_to_it_along_so_the_solution_builds_and_undo_and_redo_take_it_all(string format)
    {
        // Shop.Web uses Shop.Core's Order through a reference.
        string web = Path.Combine(folder, "src", "Shop.Web", "Shop.Web.csproj");
        Write("src/Shop.Web/Shop.Web.csproj", Executable.Replace("</Project>", "  <ItemGroup>\n    <ProjectReference Include=\"..\\Shop.Core\\Shop.Core.csproj\" />\n  </ItemGroup>\n</Project>", StringComparison.Ordinal));
        Write("src/Shop.Web/Program.cs", "System.Console.WriteLine(new Shop.Core.Order());\n");
        string solution = WriteSolution(format);
        byte[] original = File.ReadAllBytes(solution);
        string referencing = File.ReadAllText(web);
        string projects = Path.Combine(folder, "src", "Shop.Core");
        MainWindow window = Open();
        List<string> asked = Answer(window);
        int heard = 0;
        window.FolderWatcher!.Changed += (_, changes) => heard += changes.Paths.Contains(PathNames.Canonical(solution)) ? 1 : 0;
        void Edit(Action edit)
        {
            int before = heard;
            edit();
            Ui.WaitFor(() => heard > before, "the folder watcher to hear of the change to the solution");
            Ui.Settle();
        }
        IEnumerable<string> ProjectFiles() => Directory.EnumerateFiles(projects, "*.csproj").Select(Path.GetFileName).Order(StringComparer.Ordinal)!;

        // Rename's keys on the project in its solution folder: the name box holds its name, and a new one renames its file.
        Choose(window, Row(window, NodeKind.SolutionFolder, "src"));
        Ui.Press(window, "RIGHT");
        Choose(window, Row(window, NodeKind.Project, "Shop.Core"));
        Ui.Run(window, AppActions.Rename);
        Passenger.App.Vcs.NamePopup popup = Assert.IsType<Passenger.App.Vcs.NamePopup>(window.Popup);
        Assert.Equal("Shop.Core", popup.Text);
        popup.Text = string.Empty;
        Edit(() => Name(window, "Shop.Domain"));
        Assert.Equal(["Shop.Domain.csproj"], ProjectFiles());
        string renamed = Read(solution, "a project was renamed");
        Assert.Equal(format == "slnx"
            ? Slnx.Replace("src/Shop.Core/Shop.Core.csproj", "src/Shop.Core/Shop.Domain.csproj", StringComparison.Ordinal)
            : Sln[1..].Replace("\"Shop.Core\", \"src\\Shop.Core\\Shop.Core.csproj\"", "\"Shop.Domain\", \"src\\Shop.Core\\Shop.Domain.csproj\"", StringComparison.Ordinal),
            renamed);
        Assert.Equal(referencing.Replace("Shop.Core.csproj", "Shop.Domain.csproj", StringComparison.Ordinal), File.ReadAllText(web));
        Assert.Contains(window.ExplorerView.Tree.Rows, r => r.Kind == NodeKind.Project && r.Name == "Shop.Domain");
        BuildsApart(solution, "a project was renamed");
        // The language server's restore of the project renamed has the solution read again, its rows made anew.
        Quiet(window);

        // Undo in the Solution view takes all of it back, asked first.
        Choose(window, Row(window, NodeKind.Project, "Shop.Domain"));
        Edit(() => Ui.Run(window, Passenger.Editor.EditorActions.Undo));
        Assert.Equal(["Undo"], asked);
        Assert.True(ProjectFiles().SequenceEqual(["Shop.Core.csproj"]), $"Undo left [{string.Join(", ", ProjectFiles())}]: {window.Status}");
        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(solution)), $"Undo did not put the solution back as it was:\n{Read(solution, "the rename was undone")}");
        Assert.Equal(referencing, File.ReadAllText(web));
        Quiet(window);

        // Redo makes all of it again: one project file, the solution and the reference naming it, and a build.
        Choose(window, Row(window, NodeKind.Project, "Shop.Core"));
        Edit(() => Ui.Run(window, Passenger.Editor.EditorActions.Redo));
        Assert.Equal(["Undo", "Redo"], asked);
        Assert.True(ProjectFiles().SequenceEqual(["Shop.Domain.csproj"]), $"Redo left [{string.Join(", ", ProjectFiles())}]: {window.Status}");
        Assert.Equal(renamed, Read(solution, "the rename was redone"));
        Assert.Equal(referencing.Replace("Shop.Core.csproj", "Shop.Domain.csproj", StringComparison.Ordinal), File.ReadAllText(web));
        BuildsApart(solution, "the project's rename was redone");

        // A file a solution folder lists, renamed where the folder shows it on disk, is listed under its new name.
        Edit(() => Assert.Null(window.Rename(PathNames.Canonical(Path.Combine(folder, "README.md")), "Shop.md")));
        Assert.Equal(renamed.Replace(format == "slnx" ? "<File Path=\"README.md\" />" : "\t\tREADME.md = README.md", format == "slnx" ? "<File Path=\"Shop.md\" />" : "\t\tShop.md = Shop.md", StringComparison.Ordinal),
            Read(solution, "a file it lists was renamed"));
        window.Close();
    }

    [AvaloniaFact]
    public void A_solution_folder_added_to_a_Windows_1252_sln_keeps_every_other_byte_and_Local_History_keeps_the_sln_as_it_was()
    {
        // A .sln an older Visual Studio wrote with no byte order mark, a comment in the system's code page, Windows-1252.
        string solution = Path.Combine(folder, "Shop.sln");
        string comment = "# Visual Studio Version 17 \u0096 Café\r\n";
        byte[] original = Encoding.Latin1.GetBytes(Sln[1..].Replace("# Visual Studio Version 17\r\n", comment, StringComparison.Ordinal));
        File.WriteAllBytes(solution, original);
        MainWindow window = Open();
        Answer(window);
        bool heard = false;
        window.FolderWatcher!.Changed += (_, changes) => heard |= changes.Paths.Contains(PathNames.Canonical(solution));

        RightClick(window, Row(window, NodeKind.Solution, "Shop"));
        ChooseFromMenu(window, "Add", "New Solution Folder...");
        Name(window, "Tools");
        Ui.WaitFor(() => heard, "the folder watcher to hear of the change to the solution");
        Ui.Settle();

        string written = Encoding.Latin1.GetString(File.ReadAllBytes(solution));
        Assert.True(written.Contains(comment, StringComparison.Ordinal), $"Adding a solution folder rewrote the .sln's Windows-1252 comment:\n{written}");
        string without = Regex.Replace(written, $"Project\\(\"{Regex.Escape(FolderType)}\"\\) = \"Tools\", \"Tools\", \"\\{{[0-9A-F-]{{36}}\\}}\"\r\nEndProject\r\n", string.Empty);
        Assert.Equal(Encoding.Latin1.GetString(original), without);

        // Local History has the .sln as it was before, as it has every file an IDE write replaces.
        string path = PathNames.Canonical(solution);
        Ui.WaitFor(() => window.LocalHistory!.Snapshot().Events.Any(e => e.Path == path && e.Before is { } hash && window.LocalHistory.Content(hash) is { } before && before.AsSpan().SequenceEqual(original)),
            "Local History to keep the .sln as it was before the solution folder went in");
        window.Close();
    }

    [AvaloniaFact]
    public void Removing_a_solution_folder_names_the_projects_folders_and_files_that_go_with_it()
    {
        string solution = WriteSolution("slnx");
        File.WriteAllText(solution, Slnx.Replace("  <Folder Name=\"/src/\">", "  <Folder Name=\"/src/tools/\">\n    <File Path=\"docs/notes.md\" />\n  </Folder>\n  <Folder Name=\"/src/\">", StringComparison.Ordinal));
        MainWindow window = Open();
        List<string> questions = [];
        window.Confirm = (_, question, _) =>
        {
            questions.Add(question);
            return Task.FromResult(false);
        };
        Choose(window, Row(window, NodeKind.SolutionFolder, "src"));
        Ui.Press(window, "BACK_SPACE");
        Assert.Equal(["Remove the solution folder src from Shop, and with it the project Shop.Core, the solution folder tools and the file notes.md? Their files stay on disk."], questions);
        Assert.Contains("/src/", File.ReadAllText(solution), StringComparison.Ordinal);
        Assert.Contains(window.ExplorerView.Tree.Rows, r => r.Name == "src");
        window.Close();
    }

    [AvaloniaFact]
    public void Build_selected_projects_on_a_solution_folder_builds_the_projects_under_it_and_no_other()
    {
        WriteSolution("sln");
        MainWindow window = Open();
        RightClick(window, Row(window, NodeKind.SolutionFolder, "src"));
        MenuItem build = window.ExplorerView.OpenMenu!.Items.OfType<MenuItem>().First(i => (string?)i.Header == "Build Selected Projects");
        build.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        window.ExplorerView.OpenMenu?.Close();
        Ui.Settle();
        Assert.False(window.IsBuilding);
        Assert.True(File.Exists(Path.Combine(folder, "src", "Shop.Core", "bin", "Debug", "net10.0", "Shop.Core.dll")), $"The project in the solution folder was not built: {Ui.Said(window)}");
        Assert.False(File.Exists(Path.Combine(folder, "src", "Shop.Web", "bin", "Debug", "net10.0", "Shop.Web.dll")),
            "Building the solution folder's projects built Shop.Web, which is outside it, too");
        window.Close();
    }

    private string WriteSolution(string format)
    {
        string solution = Path.Combine(folder, $"Shop.{format}");
        File.WriteAllBytes(solution, new UTF8Encoding(false).GetBytes(format == "slnx" ? Slnx : Sln));
        Restore.Run(solution);
        return solution;
    }

    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    /// <summary>dotnet build of the solution as it is on disk now, which fails the test naming what was done to it.</summary>
    private static void Builds(string solution, string after)
    {
        ChildResult built = Task.Run(() => ChildProcesses.RunAsync(ChildProcesses.DotnetCommand(["build", solution]), limit: Restore.Limit)).GetAwaiter().GetResult();
        Assert.True(built.Succeeded, $"Once {after}, dotnet build no longer builds the solution:\n{built.Output}{built.Error}");
    }

    /// <summary>Waits until the folder watcher has heard nothing for a while and what it heard has landed.</summary>
    private static void Quiet(MainWindow window)
    {
        DateTime last = DateTime.UtcNow;
        void Seen(object? sender, FolderChanges changes) => last = DateTime.UtcNow;
        window.FolderWatcher!.Changed += Seen;
        try
        {
            Ui.WaitFor(() => DateTime.UtcNow - last > TimeSpan.FromSeconds(1.5), "the folder watcher to hear the last of what was written");
        }
        finally
        {
            window.FolderWatcher!.Changed -= Seen;
        }
    }

    /// <summary>dotnet build of a copy of the folder as it is on disk now, its build output left out: a project renamed is
    /// restored anew, which the window's language server does in the folder itself meanwhile, and two restores of one
    /// project at once fail.</summary>
    private void BuildsApart(string solution, string after)
    {
        string copy = PathNames.Canonical(TestFolders.Create("solution-copy-"));
        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(folder, file);
            if (!relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(copy, relative))!);
                File.Copy(file, Path.Combine(copy, relative));
            }
        }
        try
        {
            Builds(Path.Combine(copy, Path.GetFileName(solution)), after);
        }
        finally
        {
            TestFolders.Delete(copy);
        }
    }

    /// <summary>The solution file's text, which must keep its byte order mark and line endings whatever was changed.</summary>
    private static string Read(string solution, string after)
    {
        byte[] bytes = File.ReadAllBytes(solution);
        string text = Encoding.UTF8.GetString(bytes);
        if (Path.GetExtension(solution) == ".sln")
        {
            Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), $"Once {after}, the .sln lost its byte order mark");
            Assert.False(Regex.IsMatch(text, "[^\r]\n"), $"Once {after}, the .sln has lines that no longer end in CRLF, so every line shows as changed:\n{text}");
            return text[1..];
        }
        return text;
    }

    /// <summary>The id a .sln gives the folder of a name.</summary>
    private static string Id(string text, string name) =>
        Regex.Match(text, $"\\) = \"{name}\", \"{name}\", \"(\\{{[0-9A-F-]{{36}}\\}})\"").Groups[1].Value;

    private static ExplorerNode Row(MainWindow window, NodeKind kind, string name) =>
        window.ExplorerView.Tree.Rows.FirstOrDefault(r => r.Kind == kind && r.Name == name)
        ?? throw new InvalidOperationException($"The Explorer shows no {kind} {name}: [{string.Join(", ", window.ExplorerView.Tree.Rows.Select(r => r.Name))}]");

    private static object Resource(string key)
    {
        Application app = Application.Current!;
        return app.TryFindResource(key, app.ActualThemeVariant, out object? value) && value is not null ? value : throw new InvalidOperationException($"The theme has no {key}");
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

    /// <summary>Types a name into the name box open and presses Enter, as a person does.</summary>
    private static void Name(MainWindow window, string name)
    {
        Assert.IsType<Passenger.App.Vcs.NamePopup>(window.Popup);
        window.KeyTextInput(name);
        Ui.Press(window, "ENTER");
        Assert.False(window.IsPopupOpen, $"The name {name} was refused: {(window.Popup as Passenger.App.Vcs.NamePopup)?.Problem}");
    }

    private static Point Centre(MainWindow window, ExplorerNode row)
    {
        ListBox rows = window.ExplorerView.GetVisualDescendants().OfType<ListBox>().First(l => ReferenceEquals(l.ItemsSource, window.ExplorerView.Tree.Rows));
        rows.ScrollIntoView(row);
        Ui.Settle();
        Control item = rows.ContainerFromItem(row) ?? throw new InvalidOperationException($"The Explorer does not show {row.Name}");
        return item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
    }

    /// <summary>Clicks a row of the Explorer, as a person chooses one before pressing a key.</summary>
    private static void Choose(MainWindow window, ExplorerNode row)
    {
        Point at = Centre(window, row);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
        Assert.Equal([row], window.ExplorerView.SelectedNodes);
    }

    private static void DoubleClick(MainWindow window, ExplorerNode row)
    {
        Point at = Centre(window, row);
        for (int i = 0; i < 2; i++)
        {
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
        }
        Ui.Settle();
    }

    private static void RightClick(MainWindow window, ExplorerNode row)
    {
        Point at = Centre(window, row);
        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Ui.Settle();
        Assert.True(window.ExplorerView.SelectedNodes.SequenceEqual([row]),
            $"A right-click on {row.Name} left [{string.Join(", ", window.ExplorerView.SelectedNodes.Select(n => n.Name))}] chosen, so its menu is another row's");
    }

    /// <summary>Chooses an item of a submenu of the Explorer's open menu, as clicks on them would.</summary>
    private static void ChooseFromMenu(MainWindow window, string submenu, string item)
    {
        ContextMenu menu = window.ExplorerView.OpenMenu ?? throw new InvalidOperationException("No menu opened on the row");
        MenuItem parent = menu.Items.OfType<MenuItem>().FirstOrDefault(i => (string?)i.Header == submenu)
            ?? throw new InvalidOperationException($"The row's menu has no {submenu}: [{string.Join(", ", menu.Items.OfType<MenuItem>().Select(i => i.Header))}]");
        MenuItem chosen = parent.Items.OfType<MenuItem>().FirstOrDefault(i => (string?)i.Header == item)
            ?? throw new InvalidOperationException($"{submenu} has no {item}: [{string.Join(", ", parent.Items.OfType<MenuItem>().Select(i => i.Header))}]");
        chosen.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();
        Ui.Settle();
    }

    private void Write(string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
