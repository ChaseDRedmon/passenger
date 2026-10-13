using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Explorer;
using Passenger.App.Navigation;
using Passenger.App.Workspace;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>Find in Files' scope and file mask as Rider's: the solution, one project (a project nested in its folder
/// left to itself), a directory with or without its subdirectories, or a named scope; a mask of wildcards with commas,
/// a ! leaving files out; the Solution view's Edit menu opening Find and Replace in Files on a row's folder, project or
/// file; and the scope and mask coming back the next time, and after a restart.</summary>
public sealed class FindInFilesScopeTests : IDisposable
{
    private const string Project = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n";

    private readonly string folder = TestFolders.Create("scope-");
    private readonly string settings = TestFolders.Create("settings-");

    public FindInFilesScopeTests()
    {
        Write("Shop.slnx", "<Solution>\n  <Project Path=\"App/App.csproj\" />\n  <Project Path=\"App/Plugins/Plugins.csproj\" />\n  <Project Path=\"Lib/Lib.csproj\" />\n</Solution>\n");
        Write("App/App.csproj", Project);
        Write("App/Main.cs", "// the total of the app\nclass Main { int total; }\n");
        Write("App/Models/Order.cs", "class Order { decimal total; }\n");
        Write("App/Models/Lines/Line.cs", "class Line { decimal total; }\n");
        Write("App/Models/notes.md", "the total, in words\n");
        Write("App/Plugins/Plugins.csproj", Project);
        Write("App/Plugins/Plugin.cs", "class Plugin { int total; }\n");
        Write("Lib/Lib.csproj", Project);
        Write("Lib/Money.cs", "record Money(decimal Total);\n");
        Write("Lib/Money.g.cs", "// generated: total\n");
        Write("docs/total.md", "total\n");
    }

    public void Dispose()
    {
        TestFolders.Delete(folder);
        TestFolders.Delete(settings);
    }

    [AvaloniaFact]
    public void A_search_in_one_project_finds_only_its_files_and_leaves_a_project_nested_in_it_to_itself()
    {
        MainWindow window = Open();
        window.OpenFile(Full("App/Main.cs"));
        Ui.Settle();
        FindInFilesPopup popup = Find(window);
        Search(popup, "total");
        Assert.Equal(["Line.cs", "Main.cs", "Money.cs", "Money.g.cs", "notes.md", "Order.cs", "Plugin.cs", "total.md"], Found(popup));

        // Alt+P: the project of the file in the editor, as Rider preselects it.
        Ui.Press(window, "alt P");
        Assert.Equal((SearchScopeKind.Project, Full("App/App.csproj")), (popup.ScopeKind, popup.Project));
        string[] app = Found(popup);
        Assert.True(app.SequenceEqual(["Line.cs", "Main.cs", "notes.md", "Order.cs"]),
            $"Searching the App project found [{string.Join(", ", app)}]: another project's files, or those of the project nested in its folder");

        popup.Project = Full("Lib/Lib.csproj");
        Ui.Settle();
        Assert.Equal(["Money.cs", "Money.g.cs"], Found(popup));
        Assert.Equal("2 matches in 2 files", popup.Status);

        // Alt+S: a named scope, the open files.
        Ui.Press(window, "alt S");
        Assert.Equal("Open Files", popup.NamedScope);
        Assert.Equal(["Main.cs"], Found(popup));
        window.Close();
    }

    [AvaloniaFact]
    public void The_Solution_views_Edit_menu_finds_in_a_folder_a_project_or_a_file_and_nowhere_else()
    {
        MainWindow window = Open();
        FindInFilesPopup popup = FromMenu(window, Reveal(window, "App/Models"), "Find in Files...");
        Assert.Equal((SearchScopeKind.Directory, Full("App/Models"), true), (popup.ScopeKind, popup.Directory, popup.Recursive));
        Search(popup, "total");
        string[] models = Found(popup);
        Assert.True(models.SequenceEqual(["Line.cs", "notes.md", "Order.cs"]), $"Find in Files on the Models folder found [{string.Join(", ", models)}]");

        // Alt+Y: the folder's own files, not its subfolders'.
        Ui.Press(window, "alt Y");
        Assert.False(popup.Recursive);
        Assert.Equal(["notes.md", "Order.cs"], Found(popup));
        Ui.Press(window, "ESCAPE");

        popup = FromMenu(window, window.ExplorerView.Tree.Rows.First(r => r.Kind == NodeKind.Project && r.Name == "Lib"), "Find in Files...");
        Assert.Equal((SearchScopeKind.Project, Full("Lib/Lib.csproj")), (popup.ScopeKind, popup.Project));
        Search(popup, "total");
        Assert.Equal(["Money.cs", "Money.g.cs"], Found(popup));
        Ui.Press(window, "ESCAPE");

        // A file is a scope of its own, named after it as Rider names a selection.
        popup = FromMenu(window, Reveal(window, "Lib/Money.g.cs"), "Find in Files...");
        Assert.Equal((SearchScopeKind.Scope, "Money.g.cs"), (popup.ScopeKind, popup.NamedScope));
        Search(popup, "total");
        Assert.Equal(["Money.g.cs"], Found(popup));
        Ui.Press(window, "ESCAPE");

        // The shortcut with the Solution view's row chosen and the keys there searches that row, as Rider's does.
        Reveal(window, "App/App.csproj");
        window.ExplorerView.FocusRows();
        Ui.Press(window, Ui.Shortcut(AppActions.FindInFiles));
        popup = Assert.IsType<FindInFilesPopup>(window.Popup);
        Assert.Equal((SearchScopeKind.Project, Full("App/App.csproj")), (popup.ScopeKind, popup.Project));
        window.Close();
    }

    [AvaloniaFact]
    public void A_file_mask_leaves_out_other_files_and_one_after_a_bang_leaves_out_what_it_matches()
    {
        MainWindow window = Open();
        FindInFilesPopup popup = Find(window);
        Search(popup, "total");
        Assert.Equal(8, Found(popup).Length);

        // Alt+K turns the mask on, on the C# files.
        Ui.Press(window, "alt K");
        Assert.Equal("*.cs", popup.FileMask);
        string[] code = Found(popup);
        Assert.True(code.SequenceEqual(["Line.cs", "Main.cs", "Money.cs", "Money.g.cs", "Order.cs", "Plugin.cs"]), $"The mask *.cs let through [{string.Join(", ", code)}]");

        popup.FileMask = "*.cs, !*.g.cs";
        Ui.Settle();
        Assert.DoesNotContain("Money.g.cs", Found(popup));
        Assert.Equal(5, Found(popup).Length);

        // Exclusions alone leave everything else in, and the mask takes a name in any case.
        popup.FileMask = "!*.cs";
        Ui.Settle();
        Assert.Equal(["notes.md", "total.md"], Found(popup));
        popup.FileMask = "*.MD,Plugin.?s";
        Ui.Settle();
        Assert.Equal(["notes.md", "Plugin.cs", "total.md"], Found(popup));

        popup.FileMask = " ";
        Ui.Settle();
        Assert.Empty(popup.Matches);
        Assert.Equal("Empty file mask", popup.Status);

        // Alt+K again turns it off: every file.
        Ui.Press(window, "alt K");
        Assert.Null(popup.FileMask);
        Assert.Equal(8, Found(popup).Length);
        window.Close();
    }

    [AvaloniaFact]
    public void Replace_in_files_from_a_folders_menu_changes_only_the_files_under_it()
    {
        MainWindow window = Open();
        window.Confirm = (_, _, _) => Task.FromResult(true);
        FindInFilesPopup popup = FromMenu(window, Reveal(window, "App/Models"), "Replace in Files...");
        Assert.Equal(SearchScopeKind.Directory, popup.ScopeKind);
        popup.Words = true;
        Search(popup, "total");
        popup.Replacement = "sum";
        popup.RequestReplace();
        Ui.WaitFor(() => window.Status?.StartsWith("Replaced", StringComparison.Ordinal) == true, "the replacement");

        Assert.Equal("Replaced 3 matches in 3 files", window.Status);
        Assert.Equal("class Order { decimal sum; }\n", File.ReadAllText(Full("App/Models/Order.cs")));
        Assert.Equal("class Line { decimal sum; }\n", File.ReadAllText(Full("App/Models/Lines/Line.cs")));
        Assert.Equal("the sum, in words\n", File.ReadAllText(Full("App/Models/notes.md")));
        foreach ((string file, string text) in new[]
        {
            ("App/Main.cs", "// the total of the app\nclass Main { int total; }\n"),
            ("App/Plugins/Plugin.cs", "class Plugin { int total; }\n"),
            ("Lib/Money.g.cs", "// generated: total\n"),
            ("docs/total.md", "total\n"),
        })
        {
            Assert.True(File.ReadAllText(Full(file)) == text, $"Replace in Files on the Models folder changed {file}, outside it");
        }
        window.Close();
    }

    [AvaloniaFact]
    public void The_scope_and_the_mask_come_back_when_find_opens_again_and_after_a_restart()
    {
        MainWindow first = Keeping(folder);
        FindInFilesPopup popup = Find(first);
        Ui.Press(first, "alt D");
        popup.Directory = Full("Lib");
        Ui.Press(first, "alt Y");
        Ui.Press(first, "alt K");
        popup.FileMask = "*.cs, !*.g.cs";
        Ui.Settle();
        Ui.Press(first, "ESCAPE");

        popup = Find(first);
        Assert.Equal((SearchScopeKind.Directory, Full("Lib"), false, "*.cs, !*.g.cs"), (popup.ScopeKind, popup.Directory, popup.Recursive, popup.FileMask));
        Search(popup, "total");
        Assert.Equal(["Money.cs"], Found(popup));
        Ui.Press(first, "ESCAPE");
        first.Close();

        MainWindow second = Keeping(folder);
        popup = Find(second);
        Assert.True((popup.ScopeKind, popup.Directory, popup.Recursive, popup.FileMask) == (SearchScopeKind.Directory, Full("Lib"), false, "*.cs, !*.g.cs"),
            $"After a restart Find in Files opened on {popup.ScopeKind} {popup.Directory} (recursive {popup.Recursive}) with the mask {popup.FileMask ?? "off"}");
        Ui.Press(second, "ESCAPE");

        // Another folder has its own scope, and the mask, as Rider's, is every folder's.
        string other = TestFolders.Create("other-");
        File.WriteAllText(Path.Combine(other, "One.cs"), "class One { }\n");
        second.OpenFolder(other);
        Ui.Settle();
        popup = Find(second);
        Assert.Equal((SearchScopeKind.Solution, "*.cs, !*.g.cs"), (popup.ScopeKind, popup.FileMask));
        Ui.Press(second, "ESCAPE");
        second.Close();
    }

    /// <summary>Find in Files as its shortcut opens it, where the keys are.</summary>
    private static FindInFilesPopup Find(MainWindow window)
    {
        Ui.Press(window, Ui.Shortcut(AppActions.FindInFiles));
        return Assert.IsType<FindInFilesPopup>(window.Popup);
    }

    private static void Search(FindInFilesPopup popup, string text)
    {
        popup.Text = text;
        Ui.Settle();
        Ui.WaitFor(() => popup.Searching.IsCompleted, $"the search for {text}");
    }

    /// <summary>The names of the files the matches are in, each once, in order.</summary>
    private static string[] Found(FindInFilesPopup popup)
    {
        Ui.WaitFor(() => popup.Searching.IsCompleted, "the search");
        return [.. popup.Matches.Select(m => m.Name).Distinct().Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The Solution view's row of a file or folder, opened down to and chosen.</summary>
    private ExplorerNode Reveal(MainWindow window, string relative)
    {
        window.ExplorerView.Reveal(Full(relative));
        Ui.Settle();
        return window.ExplorerView.Selected ?? throw new InvalidOperationException($"The Solution view shows no {relative}");
    }

    /// <summary>A row's right-click menu, Edit, and an item of it, as clicks on them would; the popup it opens.</summary>
    private static FindInFilesPopup FromMenu(MainWindow window, ExplorerNode row, string item)
    {
        ListBox rows = window.ExplorerView.GetVisualDescendants().OfType<ListBox>().First(l => ReferenceEquals(l.ItemsSource, window.ExplorerView.Tree.Rows));
        rows.ScrollIntoView(row);
        Ui.Settle();
        Control shown = rows.ContainerFromItem(row) ?? throw new InvalidOperationException($"The Solution view does not show {row.Name}");
        Point at = shown.TranslatePoint(new Point(shown.Bounds.Width / 2, shown.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Ui.Settle();
        ContextMenu menu = window.ExplorerView.OpenMenu ?? throw new InvalidOperationException($"No menu opened on {row.Name}");
        MenuItem edit = menu.Items.OfType<MenuItem>().FirstOrDefault(i => (string?)i.Header == "Edit")
            ?? throw new InvalidOperationException($"{row.Name}'s menu has no Edit: [{string.Join(", ", menu.Items.OfType<MenuItem>().Select(i => i.Header))}]");
        MenuItem chosen = edit.Items.OfType<MenuItem>().First(i => (string?)i.Header == item);
        chosen.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();
        Ui.Settle();
        return Assert.IsType<FindInFilesPopup>(window.Popup);
    }

    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    /// <summary>The window a person uses, as the app makes it: one that keeps its state, here in the test's own folder.</summary>
    private MainWindow Keeping(string open)
    {
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = settings };
        window.Show();
        window.KeepLayout();
        window.OpenFolder(open);
        Ui.Settle();
        return window;
    }

    private string Full(string relative) => PathNames.Canonical(Path.Combine(folder, relative));

    private void Write(string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
