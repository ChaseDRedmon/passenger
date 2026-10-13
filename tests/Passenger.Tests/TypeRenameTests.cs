using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Explorer;
using Passenger.App.Vcs;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Rider's rename of a type with its file, both ways, on a real project in a Git working tree: Rename on a class
/// named like its file renames the file too (Also rename files to reflect this change, on until turned off), its tab
/// following, its uses changing and Git seeing a rename; Rename on the file in the Solution view offers to rename the
/// class with it; a file named otherwise keeps its name; and Undo takes the whole rename back at once, asked first, as
/// Rider's undo of a change to more than one file is.</summary>
public sealed class TypeRenameTests : IDisposable
{
    private const string Order = "namespace Shop;\n\npublic class Order\n{\n    public int Total;\n}\n";

    private const string Till = "namespace Shop;\n\npublic static class Till\n{\n    public static Order Open() => new Order();\n\n    public static int Sum(Order order) => order.Total;\n}\n";

    private const string Shapes = "namespace Shop;\n\npublic class Circle\n{\n}\n\npublic class Square\n{\n    public Circle Inside() => new Circle();\n}\n";

    private readonly string folder = PathNames.Canonical(TestFolders.Create("type-rename-"));

    public TypeRenameTests() => TestFolders.Prepare(() =>
    {
        Write("Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        Write(".gitignore", "bin/\nobj/\n");
        Write("Order.cs", Order);
        Write("Till.cs", Till);
        Write("Shapes.cs", Shapes);
        TestGit.Init(folder);
        Restore.Run(folder);
    }, folder);

    public void Dispose() => TestFolders.Delete(folder);

    private string OrderPath => Path.Combine(folder, "Order.cs");

    private string PurchasePath => Path.Combine(folder, "Purchase.cs");

    [AvaloniaFact]
    public void Renaming_a_class_named_like_its_file_renames_the_file_its_tab_follows_its_uses_change_and_Git_sees_a_rename()
    {
        MainWindow window = Open("Order.cs");
        OpenDocument document = window.EditorArea.Active!;
        PlaceAfter(window, "Total;");
        Ui.Run(window, AppActions.ToggleBreakpoint);
        Ui.Run(window, AppActions.ToggleBookmark);

        RenameAt(window, "class Ord", "Purchase");

        Assert.False(File.Exists(OrderPath), $"Order.cs is still there after its class was renamed: {window.Status}");
        Assert.True(File.Exists(PurchasePath), $"Order.cs was not renamed with its class: {window.Status}");
        Assert.True(ReferenceEquals(document, Assert.Single(window.EditorArea.Documents)), "The renamed file's tab was closed and opened again");
        Assert.Equal(PurchasePath, document.Path);
        Assert.Equal("Purchase.cs", window.EditorArea.Active?.Name);
        Assert.Equal(4, Assert.Single(window.Debugger.Breakpoints.In(PurchasePath)).Line);
        Assert.Empty(window.Debugger.Breakpoints.In(OrderPath));
        Assert.Equal(PurchasePath, Assert.Single(window.Bookmarks.All).Path);
        Assert.Equal(Order.Replace("Order", "Purchase", StringComparison.Ordinal), File.ReadAllText(PurchasePath));
        Assert.Equal(Till.Replace("Order", "Purchase", StringComparison.Ordinal), File.ReadAllText(Path.Combine(folder, "Till.cs")));

        // Git sees the file moved, as git mv leaves it, not one file deleted and another new.
        Ui.WaitFor(() => window.Vcs.Changes.Any(c => c.Kind == ChangeKind.Renamed && c.Path == PurchasePath && c.From == OrderPath), "Git to list Order.cs renamed to Purchase.cs");
        string status = Git("status", "--porcelain");
        Assert.True(status.Split('\n').Any(l => l.StartsWith('R') && l.EndsWith("Order.cs -> Purchase.cs", StringComparison.Ordinal)), $"git status shows no rename:\n{status}");
        window.Close();
    }

    [AvaloniaFact]
    public void With_the_file_option_turned_off_only_the_code_is_renamed_and_it_stays_off_for_the_next_rename()
    {
        MainWindow window = Open("Order.cs");
        PlaceAfter(window, "class Ord");
        Ui.Run(window, AppActions.Rename);
        Ui.WaitFor(() => window.Assist.RenameOffersFiles.Count > 0, "the rename to offer Order.cs");
        CheckBox option = Option(window, "Also rename files to reflect this change");
        Assert.True(option.IsChecked, "Renaming a class named like its file does not rename the file unless asked, as Rider's does");
        Click(window, option);
        Assert.False(option.IsChecked, "A click did not clear Also rename files to reflect this change");
        window.KeyTextInput("Purchase");
        Ui.Press(window, "ENTER");
        Ui.Settle();

        Assert.False(File.Exists(PurchasePath), "The file was renamed with the option turned off");
        Assert.Equal("Order.cs", window.EditorArea.Active?.Name);
        Assert.Equal(Order.Replace("class Order", "class Purchase", StringComparison.Ordinal), window.EditorArea.Active?.Text.Snapshot.ToString());
        Assert.Equal(Till.Replace("Order", "Purchase", StringComparison.Ordinal), File.ReadAllText(Path.Combine(folder, "Till.cs")));

        // The option stays as it was left for the next type named like its file, as Rider keeps it.
        window.OpenFile(Path.Combine(folder, "Till.cs"));
        Ui.Settle();
        PlaceAfter(window, "class Ti");
        Ui.Run(window, AppActions.Rename);
        Ui.WaitFor(() => window.Assist.RenameOffersFiles.Count > 0, "the rename to offer Till.cs");
        Assert.False(Option(window, "Also rename files to reflect this change").IsChecked, "The file option turned off came back on");
        Ui.Press(window, "ESCAPE");
        Assert.Null(window.Assist.Renaming);
        window.Close();
    }

    [AvaloniaFact]
    public void Renaming_the_file_in_the_solution_view_offers_to_rename_its_class_which_renames_it_and_its_uses()
    {
        MainWindow window = Open("Till.cs");
        List<(string Title, string Text)> asked = Answer(window);
        Choose(window, OrderPath);
        Ui.Press(window, "F2");
        NamePopup popup = Assert.IsType<NamePopup>(window.Popup);
        Assert.Equal("Order.cs", popup.Text);
        CheckBox option = popup.Option ?? throw new Xunit.Sdk.XunitException("Renaming Order.cs did not offer to rename the class Order");
        Assert.Equal("Rename type 'Order' to match the new file name", option.Content);
        Assert.True(option.IsChecked, "Rider renames the type with its file unless asked not to");
        window.KeyTextInput("Purchase.cs");
        Ui.Press(window, "ENTER");
        Ui.WaitFor(() => !window.IsPopupOpen, $"the rename to finish: {popup.Problem}");

        Assert.False(File.Exists(OrderPath), "Order.cs is still there");
        Assert.Equal(Order.Replace("Order", "Purchase", StringComparison.Ordinal), File.ReadAllText(PurchasePath));
        CodeEditor editor = window.EditorArea.CodeEditor;
        Assert.Equal(Till.Replace("Order", "Purchase", StringComparison.Ordinal), editor.Document.Snapshot.ToString());
        Assert.Contains(window.ExplorerView.Tree.Rows, r => r.Path == PurchasePath);
        Ui.WaitFor(() => window.Vcs.Changes.Any(c => c.Kind == ChangeKind.Renamed && c.Path == PurchasePath), "Git to list Order.cs renamed");

        // Undo in the Solution view takes the file's rename and the class's back together, asked first.
        Choose(window, PurchasePath);
        Ui.Run(window, EditorActions.Undo);
        Assert.Equal([("Undo", "Undo Rename Order to Purchase? It changes Till.cs and Purchase.cs.")], asked);
        Assert.True(File.Exists(OrderPath), $"Undo did not rename Purchase.cs back: {window.Status}");
        Assert.False(File.Exists(PurchasePath), "Undo left Purchase.cs");
        Assert.Equal(Order, File.ReadAllText(OrderPath));
        Assert.Equal(Till, editor.Document.Snapshot.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void Every_part_of_a_partial_class_in_a_file_named_after_it_is_renamed_with_it_and_a_part_named_otherwise_is_not()
    {
        Write("Basket.cs", "namespace Shop;\n\npublic partial class Basket\n{\n    public int Count;\n}\n");
        Write("Basket.Lines.cs", "namespace Shop;\n\npublic partial class Basket\n{\n    public int Lines;\n}\n");
        Write("BasketExtras.cs", "namespace Shop;\n\npublic partial class Basket\n{\n    public int Extras;\n}\n");
        MainWindow window = Open("Basket.Lines.cs");

        IReadOnlyList<string> offered = RenameAt(window, "class Bask", "Cart");

        Assert.Equal(["Basket.Lines.cs", "Basket.cs"], offered.Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(folder, "Cart.cs")), $"Basket.cs was not renamed with its class: {window.Status}");
        Assert.True(File.Exists(Path.Combine(folder, "Cart.Lines.cs")), $"Basket.Lines.cs was not renamed with its class: {window.Status}");
        Assert.Equal("Cart.Lines.cs", window.EditorArea.Active?.Name);
        Assert.Contains("public partial class Cart", File.ReadAllText(Path.Combine(folder, "BasketExtras.cs")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder, "Basket.cs")), "Basket.cs is still there");
        window.Close();
    }

    [AvaloniaFact]
    public void A_file_holding_types_of_other_names_is_renamed_alone_and_keeps_its_name_when_one_of_them_is_renamed()
    {
        string shapes = Path.Combine(folder, "Shapes.cs");
        string figures = Path.Combine(folder, "Figures.cs");
        MainWindow window = Open("Shapes.cs");

        // Its class renamed, the file named otherwise stays as it is: there is no file to offer.
        RenameAt(window, "class Circ", "Ring", offered: false);
        Assert.True(File.Exists(shapes), "Shapes.cs was renamed with a class it is not named after");
        Assert.Contains("public class Ring", window.EditorArea.Active?.Text.Snapshot.ToString(), StringComparison.Ordinal);

        // Renamed in the Solution view, it offers no type to rename and its code stays as it is.
        window.EditorArea.SaveAll();
        Choose(window, shapes);
        Ui.Press(window, "F2");
        NamePopup popup = Assert.IsType<NamePopup>(window.Popup);
        Assert.Null(popup.Option);
        window.KeyTextInput("Figures.cs");
        Ui.Press(window, "ENTER");
        Ui.WaitFor(() => !window.IsPopupOpen, $"the rename to finish: {popup.Problem}");
        Assert.False(File.Exists(shapes), "Shapes.cs is still there");
        Assert.Equal(Shapes.Replace("Circle", "Ring", StringComparison.Ordinal), File.ReadAllText(figures));
        Assert.Equal("Figures.cs", window.EditorArea.Active?.Name);

        // Undo in the Solution view names it back, asked first.
        List<(string Title, string Text)> asked = Answer(window);
        Choose(window, figures);
        Ui.Run(window, EditorActions.Undo);
        Assert.Equal([("Undo", "Undo Rename Shapes.cs to Figures.cs? It changes Figures.cs.")], asked);
        Assert.True(File.Exists(shapes), $"Undo did not name Figures.cs back: {window.Status}");
        Assert.Equal("Shapes.cs", window.EditorArea.Active?.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void Undo_in_the_editor_takes_the_class_and_file_rename_back_whole_once_asked_and_redo_makes_it_again()
    {
        string till = Path.Combine(folder, "Till.cs");
        MainWindow window = Open("Order.cs");
        List<(string Title, string Text)> asked = Answer(window);
        OpenDocument document = window.EditorArea.Active!;
        CodeEditor editor = window.EditorArea.CodeEditor;
        RenameAt(window, "class Ord", "Purchase");
        Assert.True(File.Exists(PurchasePath), window.Status);

        // What was typed after it is the file's own to undo, asking nothing.
        editor.Focus();
        PlaceAfter(window, "Total;");
        window.KeyTextInput(" // kept");
        Ui.Run(window, EditorActions.Undo);
        Assert.Empty(asked);
        Assert.Equal(Order.Replace("Order", "Purchase", StringComparison.Ordinal), document.Text.Snapshot.ToString());

        // Then Undo reaches the rename: asked, and the file, its class and the other file go back together.
        Ui.Run(window, EditorActions.Undo);
        Assert.Equal([("Undo", "Undo Rename Order to Purchase? It changes Purchase.cs and Till.cs.")], asked);
        Assert.True(File.Exists(OrderPath), $"Undo did not rename Purchase.cs back: {window.Status}");
        Assert.False(File.Exists(PurchasePath), "Undo left Purchase.cs");
        Assert.Equal(OrderPath, document.Path);
        Assert.Equal("Order.cs", window.EditorArea.Active?.Name);
        Assert.Equal(Order, document.Text.Snapshot.ToString());
        Assert.Equal(Till, File.ReadAllText(till));
        window.EditorArea.SaveAll();
        _ = window.Vcs.RefreshAsync();
        Ui.WaitFor(() => window.Vcs.Changes.Count == 0, "Git to list nothing changed once the rename was undone");
        Assert.Equal(string.Empty, Git("status", "--porcelain"));

        // Redo makes all of it again, asked as Undo was.
        Ui.Run(window, EditorActions.Redo);
        Assert.Equal(("Redo", "Redo Rename Order to Purchase? It changes Order.cs and Till.cs."), asked[^1]);
        Assert.True(File.Exists(PurchasePath), $"Redo did not rename Order.cs again: {window.Status}");
        Assert.Equal(PurchasePath, document.Path);
        Assert.Equal(Order.Replace("Order", "Purchase", StringComparison.Ordinal), document.Text.Snapshot.ToString());
        Assert.Equal(Till.Replace("Order", "Purchase", StringComparison.Ordinal), File.ReadAllText(till));
        window.Close();
    }

    private MainWindow Open(string file)
    {
        MainWindow window = new() { Width = 1200, Height = 800, Autosaves = false };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile(Path.Combine(folder, file));
        Ui.Settle();
        Assert.True(window.CSharpService?.IsReady, window.CSharpService?.State);
        return window;
    }

    /// <summary>Rename at the end of a piece of the active file, the new name typed into its box and Enter pressed, once
    /// the box has offered the files named after the type (or, when none is to be, once it has had the chance); the
    /// files it offered.</summary>
    private static IReadOnlyList<string> RenameAt(MainWindow window, string after, string name, bool offered = true)
    {
        PlaceAfter(window, after);
        window.EditorArea.CodeEditor.Focus();
        Ui.Run(window, AppActions.Rename);
        Ui.Settle();
        Assert.True(window.Assist.Renaming is not null, "Rename opened no box");
        Assert.True(offered == window.Assist.RenameOffersFiles.Count > 0,
            $"The rename {(offered ? "did not offer" : "offered")} to rename files: [{string.Join(", ", window.Assist.RenameOffersFiles)}]");
        IReadOnlyList<string> files = window.Assist.RenameOffersFiles;
        window.KeyTextInput(name);
        Ui.Press(window, "ENTER");
        Ui.Settle();
        return files;
    }

    private static void PlaceAfter(MainWindow window, string text)
    {
        CodeEditor editor = window.EditorArea.CodeEditor;
        int at = editor.Document.Snapshot.ToString().IndexOf(text, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{text}' is not in the file");
        editor.Selections = SelectionSet.Single(at + text.Length);
    }

    private static CheckBox Option(MainWindow window, string text) =>
        window.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(c => c.IsEffectivelyVisible && (string?)c.Content == text)
        ?? throw new Xunit.Sdk.XunitException($"No check box '{text}' shows");

    private static void Click(MainWindow window, Control control)
    {
        Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
    }

    /// <summary>Clicks a file's row in the Explorer, its folders opened down to it, as a person chooses it before a key.</summary>
    private static void Choose(MainWindow window, string path)
    {
        window.ExplorerView.Reveal(path);
        Ui.Settle();
        ExplorerNode row = window.ExplorerView.Tree.Rows.FirstOrDefault(r => r.Path == path)
            ?? throw new Xunit.Sdk.XunitException($"The Explorer does not show {path}: [{string.Join(", ", window.ExplorerView.Tree.Rows.Select(r => r.Name))}]");
        ListBox rows = window.ExplorerView.GetVisualDescendants().OfType<ListBox>().First(l => ReferenceEquals(l.ItemsSource, window.ExplorerView.Tree.Rows));
        rows.ScrollIntoView(row);
        Ui.Settle();
        Control item = rows.ContainerFromItem(row) ?? throw new Xunit.Sdk.XunitException($"The Explorer does not show {row.Name}");
        Click(window, item);
        Assert.Equal([row], window.ExplorerView.SelectedNodes);
    }

    /// <summary>Agrees to every question the window asks, keeping each one's title and text in order.</summary>
    private static List<(string Title, string Text)> Answer(MainWindow window)
    {
        List<(string, string)> asked = [];
        window.Confirm = (title, text, _) =>
        {
            asked.Add((title, text));
            return Task.FromResult(true);
        };
        return asked;
    }

    private string Git(params string[] arguments) => TestGit.Run(folder, arguments);

    private void Write(string relative, string text) => File.WriteAllText(Path.Combine(folder, relative), text);
}
