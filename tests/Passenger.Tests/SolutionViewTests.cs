using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Docking;
using Passenger.App.Explorer;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.App.Vcs;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Terminal;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>The Solution view's file commands as Rider's: Cut, Copy and Paste by their keys and its Edit menu, and drags,
/// move or copy files and folders into another folder or another project's, an open file's tab, undo and edits going with
/// it; Add, Reference... adds and takes out a project reference, which the Dependencies, the build and the language server
/// follow, leaving the rest of the project file as it was; Add, Class/Interface... writes each of Rider's kinds of type;
/// Open In, Terminal starts a shell in the folder; and Tools' Compare shows two files' differences.</summary>
public sealed class SolutionViewTests : IDisposable
{
    private const string Library = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n";

    /// <summary>As Visual Studio writes one: a byte order mark, CRLF, and a blank line between its groups.</summary>
    private const string Executable = "﻿<Project Sdk=\"Microsoft.NET.Sdk\">\r\n\r\n  <PropertyGroup>\r\n    <OutputType>Exe</OutputType>\r\n    <TargetFramework>net10.0</TargetFramework>\r\n  </PropertyGroup>\r\n\r\n</Project>\r\n";

    private const string Order = "namespace Shop.App.Models;\n\npublic class Order\n{\n    public int Total;\n}\n";

    private readonly string folder = PathNames.Canonical(TestFolders.Create("solution-view-"));
    private readonly string trash = TestFolders.Create("trash-");

    public SolutionViewTests()
    {
        Write("Shop.App/Shop.App.csproj", Executable);
        Write("Shop.App/Program.cs", "System.Console.WriteLine(\"Shop\");\n");
        Write("Shop.App/Models/Order.cs", Order);
        Write("Shop.App/Models/Lines/Line.cs", "namespace Shop.App.Models.Lines;\n\npublic class Line\n{\n}\n");
        Write("Shop.Core/Shop.Core.csproj", Library);
        Write("Shop.Core/Catalog/Product.cs", "namespace Shop.Core.Catalog;\n\npublic class Product\n{\n}\n");
        Write("Shop.slnx", "<Solution>\n  <Project Path=\"Shop.App/Shop.App.csproj\" />\n  <Project Path=\"Shop.Core/Shop.Core.csproj\" />\n</Solution>\n");
        Trash.Folder = trash;
    }

    public void Dispose()
    {
        Trash.Folder = null;
        TestFolders.Delete(folder);
        TestFolders.Delete(trash);
    }

    [AvaloniaFact]
    public void A_file_cut_and_pasted_into_another_projects_folder_moves_there_with_its_open_tab_its_undo_and_its_edit()
    {
        string order = Path.Combine(folder, "Shop.App", "Models", "Order.cs");
        MainWindow window = Open();
        window.Autosaves = false;
        window.OpenFile(order);
        OpenDocument document = Assert.Single(window.EditorArea.Documents);
        window.EditorArea.CodeEditor.GoTo(new TextPosition(4, 21));
        window.KeyTextInput(" // the sum");
        Ui.Settle();

        // Cut by its key on the file's row: the row dims until it is pasted.
        Choose(window, Show(window, order));
        Ui.Run(window, EditorActions.Cut);
        Assert.True(Show(window, order).IsCut, "The file cut is not dimmed, so nothing shows it waits to be pasted");
        Assert.Equal(order, Ui.Clipboard(window));

        // Paste by its key on a folder of the other project.
        string catalog = Path.Combine(folder, "Shop.Core", "Catalog");
        Choose(window, Show(window, catalog));
        Ui.Run(window, EditorActions.Paste);
        string moved = Path.Combine(catalog, "Order.cs");
        Assert.Equal("Moved Order.cs to Catalog", window.Status);
        Assert.False(File.Exists(order), "The file cut and pasted is still where it was");
        Assert.Equal(Order.Replace("Total;", "Total; // the sum", StringComparison.Ordinal), File.ReadAllText(moved));
        Assert.True(ReferenceEquals(document, Assert.Single(window.EditorArea.Documents)), "The moved file's tab was closed and opened again, losing its undo");
        Assert.Equal(moved, document.Path);
        Assert.Equal("Order.cs", window.EditorArea.Active?.Name);
        ExplorerNode row = Assert.Single(window.ExplorerView.Tree.Rows, r => r.Path == moved);
        Assert.Same(row, window.ExplorerView.Selected);
        Assert.False(row.IsCut);
        Assert.DoesNotContain(window.ExplorerView.Tree.Rows, r => r.Path == order);

        // A cut is pasted once.
        Ui.Run(window, EditorActions.Paste);
        Assert.StartsWith("Nothing to paste", window.Status, StringComparison.Ordinal);

        // Undo still reaches the edit made before the move.
        window.EditorArea.CodeEditor.Focus();
        Ui.Run(window, EditorActions.Undo);
        Assert.Equal(Order, document.Text.Snapshot.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void A_folder_copied_and_pasted_copies_all_it_holds_and_a_copy_into_its_own_folder_is_named_as_Riders_Duplicate_asks()
    {
        string models = Path.Combine(folder, "Shop.App", "Models");
        MainWindow window = Open();

        // Copy from the folder's Edit menu, Paste by its key on the other project.
        RightClick(window, Show(window, models));
        ChooseFromMenu(window, "Edit", "Copy");
        Assert.False(Show(window, models).IsCut, "A copied folder is dimmed as a cut one is");
        Choose(window, Row(window, NodeKind.Project, "Shop.Core"));
        Ui.Run(window, EditorActions.Paste);
        string copy = Path.Combine(folder, "Shop.Core", "Models");
        Assert.Equal("Copied Models to Shop.Core", window.Status);
        Assert.Equal(Order, File.ReadAllText(Path.Combine(copy, "Order.cs")));
        Assert.True(File.Exists(Path.Combine(copy, "Lines", "Line.cs")), "The folder inside the folder copied was left behind");
        Assert.Equal(Order, File.ReadAllText(Path.Combine(models, "Order.cs")));
        Assert.Same(Row(window, NodeKind.Folder, "Models", copy), window.ExplorerView.Selected);

        // Pasted again where it came from, the copy needs a name of its own.
        RightClick(window, Row(window, NodeKind.Project, "Shop.App"));
        ChooseFromMenu(window, "Edit", "Paste");
        NamePopup popup = Assert.IsType<NamePopup>(window.Popup);
        Assert.Equal("Models", popup.Text);
        Ui.Press(window, "ENTER");
        Assert.Equal("Models is already there", popup.Problem);
        popup.Text = string.Empty;
        window.KeyTextInput("Archive");
        Ui.Press(window, "ENTER");
        Assert.False(window.IsPopupOpen, $"The copy's name was refused: {popup.Problem}");
        Assert.Equal(Order, File.ReadAllText(Path.Combine(folder, "Shop.App", "Archive", "Order.cs")));
        Assert.True(File.Exists(Path.Combine(folder, "Shop.App", "Archive", "Lines", "Line.cs")), "The duplicate left the folder inside behind");

        // Pasted onto a project that has a folder of that name, it asks before replacing it.
        List<string> asked = [];
        window.Confirm = (title, _, _) =>
        {
            asked.Add(title);
            return Task.FromResult(false);
        };
        File.WriteAllText(Path.Combine(copy, "Order.cs"), "changed\n");
        Choose(window, Row(window, NodeKind.Project, "Shop.Core"));
        Ui.Run(window, EditorActions.Paste);
        Assert.Equal(["Files Already Exist"], asked);
        Assert.Equal("changed\n", File.ReadAllText(Path.Combine(copy, "Order.cs")));
        window.Close();
    }

    [AvaloniaFact]
    public void A_file_dragged_onto_another_projects_folder_moves_there_and_with_the_copy_key_held_is_copied()
    {
        string order = Path.Combine(folder, "Shop.App", "Models", "Order.cs");
        string catalog = Path.Combine(folder, "Shop.Core", "Catalog");
        MainWindow window = Open();
        window.OpenFile(order);
        Ui.Settle();
        ExplorerNode file = Show(window, order);
        ExplorerNode target = Show(window, catalog);
        DropOverlay overlay = window.ExplorerView.GetVisualDescendants().OfType<DropOverlay>().Single();

        // Held over the folder, the folder and what shows under it light up.
        Point from = Centre(window, file);
        Point to = Centre(window, target);
        window.MouseDown(from, MouseButton.Left);
        Move(window, from, to);
        Assert.True(overlay.IsVisible, "Nothing lit up under the file dragged over the folder");
        window.MouseUp(to, MouseButton.Left);
        Ui.Settle();
        Assert.False(overlay.IsVisible, "The drop place stayed lit after the drop");
        string moved = Path.Combine(catalog, "Order.cs");
        Assert.Equal("Moved Order.cs to Catalog", window.Status);
        Assert.False(File.Exists(order), "The file dragged away is still where it was");
        Assert.Equal(Order, File.ReadAllText(moved));
        Assert.Equal(moved, Assert.Single(window.EditorArea.Documents).Path);

        // Dropped onto a file, it goes into that file's folder; with the copy key held it is copied.
        string program = Path.Combine(folder, "Shop.App", "Program.cs");
        from = Centre(window, Show(window, moved));
        to = Centre(window, Show(window, program));
        window.MouseDown(from, MouseButton.Left);
        Move(window, from, to);
        window.MouseUp(to, MouseButton.Left, OperatingSystem.IsMacOS() ? RawInputModifiers.Alt : RawInputModifiers.Control);
        Ui.Settle();
        Assert.Equal("Copied Order.cs to Shop.App", window.Status);
        Assert.Equal(Order, File.ReadAllText(Path.Combine(folder, "Shop.App", "Order.cs")));
        Assert.True(File.Exists(moved), "The file copied by a drag left where it was");

        // A folder never goes inside itself: nothing lights up there, and nothing moves.
        string models = Path.Combine(folder, "Shop.App", "Models");
        ExplorerNode lines = Show(window, Path.Combine(models, "Lines"));
        from = Centre(window, Show(window, models));
        to = Centre(window, lines);
        window.MouseDown(from, MouseButton.Left);
        Move(window, from, to);
        Assert.False(overlay.IsVisible, "A folder dragged over a folder inside it lit that up as a place to drop it");
        window.MouseUp(to, MouseButton.Left);
        Ui.Settle();
        Assert.True(Directory.Exists(Path.Combine(models, "Lines")), "The folder went inside itself");
        window.Close();
    }

    [AvaloniaFact]
    public void Add_reference_adds_one_the_build_and_language_server_follow_and_remove_takes_it_out_leaving_the_project_file_as_it_was()
    {
        string solution = Path.Combine(folder, "Shop.slnx");
        string app = Path.Combine(folder, "Shop.App", "Shop.App.csproj");
        string core = Path.Combine(folder, "Shop.Core", "Shop.Core.csproj");
        Write("Shop.Core/Order.cs", "namespace Shop.Core;\n\npublic class Order\n{\n}\n");
        Write("Shop.App/Program.cs", "System.Console.WriteLine(new Shop.Core.Order());\n");
        Restore.Run(solution);
        byte[] original = File.ReadAllBytes(app);
        MainWindow window = Open();
        window.OpenFile(Path.Combine(folder, "Shop.App", "Program.cs"));
        ProblemsView problems = window.BottomPanel.Problems;
        Ui.WaitFor(() => problems.FileProblems.Any(p => p.Severity == ProblemSeverity.Error), "the language server's error for the type of a project not referenced", TimeSpan.FromMinutes(2));

        // Add, Reference... on the project: the other project offered, checked with Space, OK with Enter.
        RightClick(window, Row(window, NodeKind.Project, "Shop.App"));
        ChooseFromMenu(window, "Add", "Reference...");
        AddReferenceDialog dialog = Assert.Single(window.OwnedWindows.OfType<AddReferenceDialog>());
        ReferenceChoice offered = Assert.Single(dialog.Choices);
        Assert.Equal(("Shop.Core", core, false), (offered.Name, offered.Path, offered.IsChosen));
        Ui.Press(dialog, "SPACE");
        Assert.True(offered.IsChosen, "Space did not check the project chosen");
        // Enter is OK; the dialog it closes takes no key's release.
        Heard(window, app, () => dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null));
        Assert.Empty(window.OwnedWindows.OfType<AddReferenceDialog>());
        Assert.Equal("Added a reference to Shop.Core to Shop.App", window.Status);
        string added = Encoding.UTF8.GetString(File.ReadAllBytes(app));
        Assert.Equal(
            "﻿<Project Sdk=\"Microsoft.NET.Sdk\">\r\n\r\n  <PropertyGroup>\r\n    <OutputType>Exe</OutputType>\r\n    <TargetFramework>net10.0</TargetFramework>\r\n  </PropertyGroup>\r\n\r\n"
                + "  <ItemGroup>\r\n    <ProjectReference Include=\"..\\Shop.Core\\Shop.Core.csproj\" />\r\n  </ItemGroup>\r\n\r\n</Project>\r\n",
            added);

        // The project's Dependencies list it, the language server finds the type, and the solution builds.
        ExplorerNode dependencies = Dependencies(window, "Shop.App");
        Choose(window, dependencies);
        Ui.Press(window, "RIGHT");
        ExplorerNode reference = Assert.Single(window.ExplorerView.Tree.Rows, r => r.Kind == NodeKind.ProjectReference);
        Assert.Equal(("Shop.Core", core), (reference.Name, reference.Referenced));
        Ui.WaitFor(() => !problems.FileProblems.Any(p => p.Severity == ProblemSeverity.Error), "the language server to find the type once the project references it", TimeSpan.FromMinutes(2));
        Builds(solution, "a reference was added");
        // What the build writes (a restore's .props among it) has the solution read again: once the folder is quiet.
        Quiet(window);

        // Remove on the reference, by its key once asked: the project file is as it was, byte for byte.
        List<string> asked = Answer(window);
        Choose(window, Assert.Single(window.ExplorerView.Tree.Rows, r => r.Kind == NodeKind.ProjectReference));
        Heard(window, app, () => Ui.Press(window, OperatingSystem.IsMacOS() ? "BACK_SPACE" : "DELETE"));
        Assert.Equal(["Remove Reference"], asked);
        Assert.Equal("Removed the reference to Shop.Core from Shop.App", window.Status);
        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(app)),
            $"With the reference removed, the project file is not as it was before it was added:\n{Encoding.UTF8.GetString(File.ReadAllBytes(app))}");
        Assert.DoesNotContain(window.ExplorerView.Tree.Rows, r => r.Kind == NodeKind.ProjectReference);
        Ui.WaitFor(() => problems.FileProblems.Any(p => p.Severity == ProblemSeverity.Error), "the language server's error once the reference is gone", TimeSpan.FromMinutes(2));

        // The dialog, from the Dependencies' own menu, offers what references the project back as nothing to check.
        Heard(window, core, () => Assert.Null(window.SetReferences(core, [app])));
        RightClick(window, Dependencies(window, "Shop.App"));
        ChooseFromMenu(window, null, "Add Reference...");
        dialog = Assert.Single(window.OwnedWindows.OfType<AddReferenceDialog>());
        Assert.False(Assert.Single(dialog.Choices).CanChoose, "A project that references this one was offered, which would make a cycle");
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Ui.Settle();
        Assert.False(dialog.IsVisible, "Escape left the dialog open");
        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(app)), "Cancelling the dialog changed the project file");
        window.Close();
    }

    [AvaloniaFact]
    public void Add_class_interface_writes_each_of_Riders_kinds_of_type_in_its_folders_namespace()
    {
        string models = Path.Combine(folder, "Shop.App", "Models");
        MainWindow window = Open();
        string[] declarations =
        [
            "public class Invoice\n{\n}\n",
            "public interface IInvoice\n{\n}\n",
            "public record InvoiceLine();\n",
            "public struct Money\n{\n}\n",
            "public enum Status\n{\n}\n",
        ];
        string[] names = ["Invoice", "IInvoice", "InvoiceLine", "Money", "Status"];
        for (int kind = 0; kind < names.Length; kind++)
        {
            RightClick(window, Show(window, models));
            ChooseFromMenu(window, "Add", "Class/Interface...");
            NamePopup popup = Assert.IsType<NamePopup>(window.Popup);
            Assert.Equal("Class", popup.Kind);
            for (int down = 0; down < kind; down++)
            {
                Ui.Press(window, "DOWN");
            }
            Assert.Equal(MainWindow.TypeKinds[kind], popup.Kind);
            window.KeyTextInput(names[kind]);
            Ui.Press(window, "ENTER");
            Assert.False(window.IsPopupOpen, $"The {popup.Kind} {names[kind]} was refused: {popup.Problem}");
            string written = Path.Combine(models, names[kind] + ".cs");
            Assert.Equal($"namespace Shop.App.Models;\n\n{declarations[kind]}", File.ReadAllText(written));
            Assert.Equal(written, window.EditorArea.Active?.Path);
        }
        Assert.Equal(["Class", "Interface", "Record", "Struct", "Enum"], MainWindow.TypeKinds);
        window.Close();
    }

    [AvaloniaFact]
    public void Open_in_terminal_starts_a_shell_in_the_folder_in_a_tab_of_its_own()
    {
        if (OperatingSystem.IsWindows())
        {
            // sh's pwd is Unix's.
            return;
        }
        string catalog = Path.Combine(folder, "Shop.Core", "Catalog");
        MainWindow window = Open();
        window.TerminalPanel.Shell = ("/bin/sh", []);
        RightClick(window, Show(window, catalog));
        ChooseFromMenu(window, "Open In", "Terminal");
        Ui.WaitFor(() => window.TerminalPanel.Active is { IsRunning: true }, "the shell to start");
        Ui.WaitFor(() => window.TerminalPanel.View.IsFocused, "the keys to go to the terminal");
        Ui.WaitFor(() => window.TerminalPanel.Active!.Screen.CursorColumn > 0, "the shell's prompt");
        Assert.Single(window.TerminalPanel.Tabs);
        window.KeyTextInput("pwd -P");
        Ui.Press(window, "ENTER");
        TerminalSession shell = window.TerminalPanel.Active!;
        // Read across the rows, as a long path wraps.
        Ui.WaitFor(() => string.Concat(shell.Screen.Lines.Select(l => l.Text())).Contains(catalog, StringComparison.Ordinal), $"pwd to print {catalog}", TimeSpan.FromSeconds(30));
        window.Close();
    }

    [AvaloniaFact]
    public void Compare_with_and_compare_files_show_two_files_differences()
    {
        Write("Shop.App/Notes/a.txt", "one\ntwo\nthree\n");
        Write("Shop.App/Notes/b.txt", "one\n2\nthree\nfour\n");
        string a = Path.Combine(folder, "Shop.App", "Notes", "a.txt");
        string b = Path.Combine(folder, "Shop.App", "Notes", "b.txt");
        MainWindow window = Open();
        window.PickFiles = (_, _) => Task.FromResult<IReadOnlyList<string>>([b]);

        // Compare With... by its key: the file chosen on the left, the one picked on the right.
        Choose(window, Show(window, a));
        Ui.Press(window, OperatingSystem.IsMacOS() ? "meta D" : "control D");
        Ui.Landed(() => window.Frontmost is DiffWindow, "the diff window");
        DiffView view = ((DiffWindow)window.Frontmost).View;
        Assert.Equal("one\ntwo\nthree\n", view.Left.Document.Snapshot.ToString());
        Assert.Equal("one\n2\nthree\nfour\n", view.Right.Document.Snapshot.ToString());
        Assert.Equal([new DiffHunk(1, 1, 1, 1), new DiffHunk(3, 0, 3, 1)], view.Hunks);
        Assert.Equal("a.txt vs b.txt", window.Frontmost.Title);
        window.Frontmost.Close();
        Ui.Settle();

        // Compare Files on the two chosen together, from Tools.
        ExplorerNode second = Show(window, b);
        Choose(window, Show(window, a));
        Point at = Centre(window, second);
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.Shift);
        Ui.Settle();
        Assert.Equal(2, window.ExplorerView.SelectedNodes.Count);
        window.ExplorerView.Rows().RaiseEvent(new ContextRequestedEventArgs());
        Ui.Settle();
        ChooseFromMenu(window, "Tools", "Compare Files");
        Ui.Landed(() => window.Frontmost is DiffWindow, "the diff window of the two files chosen");
        view = ((DiffWindow)window.Frontmost).View;
        Assert.Equal("one\ntwo\nthree\n", view.Left.Document.Snapshot.ToString());
        Assert.Equal("one\n2\nthree\nfour\n", view.Right.Document.Snapshot.ToString());
        window.Frontmost.Close();
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

    /// <summary>Opens the Explorer's folders down to a file or folder, as a person does to get to it, and gives its row.</summary>
    private static ExplorerNode Show(MainWindow window, string path)
    {
        window.ExplorerView.Reveal(PathNames.Canonical(path));
        Ui.Settle();
        return window.ExplorerView.Tree.Rows.FirstOrDefault(r => r.Path == PathNames.Canonical(path))
            ?? throw new InvalidOperationException($"The Explorer does not show {path}: [{string.Join(", ", window.ExplorerView.Tree.Rows.Select(r => r.Name))}]");
    }

    private static ExplorerNode Row(MainWindow window, NodeKind kind, string name, string? path = null) =>
        window.ExplorerView.Tree.Rows.FirstOrDefault(r => r.Kind == kind && r.Name == name && (path is null || r.Path == path))
        ?? throw new InvalidOperationException($"The Explorer shows no {kind} {name}: [{string.Join(", ", window.ExplorerView.Tree.Rows.Select(r => r.Name))}]");

    /// <summary>A project's Dependencies row, the project opened to show it.</summary>
    private static ExplorerNode Dependencies(MainWindow window, string project)
    {
        ExplorerNode row = Row(window, NodeKind.Project, project);
        if (!row.IsExpanded)
        {
            Choose(window, row);
            Ui.Press(window, "RIGHT");
        }
        return window.ExplorerView.Tree.Rows.First(r => r.Kind == NodeKind.Dependencies && r.Owner == row.Path);
    }

    private static Point Centre(MainWindow window, ExplorerNode row)
    {
        ListBox rows = window.ExplorerView.Rows();
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

    private static void RightClick(MainWindow window, ExplorerNode row)
    {
        Point at = Centre(window, row);
        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Ui.Settle();
    }

    /// <summary>A drag's moves from one point to another, in steps, the button held, as a hand moves a mouse.</summary>
    private static void Move(MainWindow window, Point from, Point to)
    {
        for (int step = 1; step <= 10; step++)
        {
            window.MouseMove(from + ((to - from) * (step / 10.0)), RawInputModifiers.LeftMouseButton);
        }
        Ui.Settle();
    }

    /// <summary>Chooses an item of the Explorer's open menu, in one of its submenus when one is named, as clicks would.</summary>
    private static void ChooseFromMenu(MainWindow window, string? submenu, string item)
    {
        ContextMenu menu = window.ExplorerView.OpenMenu ?? throw new InvalidOperationException("No menu opened on the row");
        IEnumerable<MenuItem> items = menu.Items.OfType<MenuItem>();
        if (submenu is not null)
        {
            MenuItem parent = items.FirstOrDefault(i => (string?)i.Header == submenu)
                ?? throw new InvalidOperationException($"The row's menu has no {submenu}: [{string.Join(", ", items.Select(i => i.Header))}]");
            items = parent.Items.OfType<MenuItem>();
        }
        MenuItem chosen = items.FirstOrDefault(i => (string?)i.Header == item)
            ?? throw new InvalidOperationException($"{submenu ?? "The menu"} has no {item}: [{string.Join(", ", items.Select(i => i.Header))}]");
        Assert.True(chosen.IsEnabled, $"{item} is disabled");
        chosen.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();
        Ui.Settle();
    }

    /// <summary>Does something that writes a file the folder watcher reads the solution again for (a project file, a
    /// restore's .props), and waits for it to hear of it: the rows are made again then, and one found before is gone.</summary>
    private static void Heard(MainWindow window, string path, Action change)
    {
        int heard = 0;
        void Count(object? sender, FolderChanges changes) => heard += changes.Paths.Contains(PathNames.Canonical(path)) ? 1 : 0;
        window.FolderWatcher!.Changed += Count;
        try
        {
            change();
            Ui.WaitFor(() => heard > 0, $"the folder watcher to hear of {Path.GetFileName(path)} written");
            Ui.Settle();
        }
        finally
        {
            window.FolderWatcher!.Changed -= Count;
        }
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

    /// <summary>dotnet build of the solution as it is on disk now, which fails the test naming what was done to it.</summary>
    private static void Builds(string solution, string after)
    {
        ChildResult built = Task.Run(() => ChildProcesses.RunAsync(ChildProcesses.DotnetCommand(["build", solution]), limit: Restore.Limit)).GetAwaiter().GetResult();
        Assert.True(built.Succeeded, $"Once {after}, dotnet build no longer builds the solution:\n{built.Output}{built.Error}");
    }

    private void Write(string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}

file static class ExplorerRows
{
    /// <summary>The Explorer's list of rows.</summary>
    public static ListBox Rows(this ExplorerView explorer) =>
        explorer.GetVisualDescendants().OfType<ListBox>().First(l => ReferenceEquals(l.ItemsSource, explorer.Tree.Rows));
}
