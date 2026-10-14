using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Passenger.App;
using Passenger.App.CSharp;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Rider's refactorings that ask their options in a dialog, each through its own keys and its dialog's controls on
/// a real project, computed by Passenger's extension in the real language server: Change Signature, Extract Interface,
/// Move to Another File and Move to Namespace. Each leaves a project that builds, and Undo takes each back whole.</summary>
public sealed class RefactoringDialogsTests : IClassFixture<RestoredFolders>, IDisposable
{
    private const string Calc = "namespace Shop;\n\npublic static class Calc\n{\n    public static int Scale(int value, int factor, int unused) => value * factor;\n\n"
        + "    public static int Twice(int value) => Scale(value, 2, 0);\n}\n";

    private const string Till = "namespace Shop;\n\npublic static class Till\n{\n    public static int Total() => Calc.Scale(2, 3, 9) + Calc.Scale(4, factor: 5, unused: 1);\n}\n";

    private const string Order = "namespace Shop;\n\npublic class Order\n{\n    public int Total { get; private set; }\n\n    public void Add(int amount) => Total += Calc.Twice(amount);\n\n"
        + "    public string Describe() => $\"{Total}\";\n}\n";

    private const string OrderBook = "namespace Shop;\n\npublic static class OrderBook\n{\n    public static Order Open() => new Order();\n}\n";

    private const string Report = "using Shop;\n\nnamespace Shop.Reports;\n\npublic static class Report\n{\n    public static string Of(Order order) => order.Describe() + Till.Total();\n}\n";

    private const string Shapes = "using System;\n\nnamespace Shop;\n\npublic class Circle\n{\n    public double Area(double radius) => Math.PI * radius * radius;\n}\n\n"
        + "public class Square\n{\n    public int Side;\n}\n";

    private const string Animal = "namespace Shop;\n\npublic class Animal\n{\n    public string Name = \"\";\n\n    public virtual string Sound() => \"...\";\n}\n";

    private const string Dog = "using System.Text;\n\nnamespace Shop;\n\npublic class Dog : Animal\n{\n    public int Legs => 4;\n\n    public string Describe()\n    {\n"
        + "        StringBuilder text = new();\n        text.Append(Name).Append(' ').Append(Legs);\n        return text.ToString();\n    }\n}\n";

    private const string Cat = "namespace Shop;\n\npublic class Cat : Animal\n{\n    public bool Purrs => true;\n}\n";

    private readonly string folder;

    public RefactoringDialogsTests(RestoredFolders folders) => folder = PathNames.Canonical(folders.Take("refactorings-", made =>
    {
        string project = PathNames.Canonical(made);
        File.WriteAllText(Path.Combine(project, "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <Nullable>enable</Nullable>\n  </PropertyGroup>\n</Project>\n");
        (string Name, string Text)[] files = [("Calc.cs", Calc), ("Till.cs", Till), ("Order.cs", Order), ("OrderBook.cs", OrderBook),
            ("Report.cs", Report), ("Shapes.cs", Shapes), ("Animal.cs", Animal), ("Dog.cs", Dog), ("Cat.cs", Cat)];
        foreach ((string name, string text) in files)
        {
            File.WriteAllText(Path.Combine(project, name), text);
        }
        return project;
    }));

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void Change_signature_adds_removes_and_reorders_parameters_and_every_call_passes_its_arguments_in_the_new_order()
    {
        MainWindow window = Open("Calc.cs");
        List<(string Title, string Text)> asked = Answer(window);
        PlaceAfter(window, "int Sca");
        Ui.Run(window, AppActions.ChangeSignature);
        ChangeSignatureDialog dialog = Shown<ChangeSignatureDialog>(window, "Change Signature");
        Assert.Equal("public static int Scale(int value, int factor, int unused)", dialog.Preview);
        Assert.Equal(["value", "factor", "unused"], dialog.Parameters.Select(p => p.NameBox.Text));

        // unused goes, factor moves before value, and a new offset is passed 0 by every call.
        Click(dialog, dialog.Parameters[2].NameBox);
        Click(dialog, dialog.RemoveButton);
        Click(dialog, dialog.Parameters[1].TypeBox);
        Click(dialog, dialog.UpButton);
        Assert.Equal(["factor", "value"], dialog.Parameters.Select(p => p.NameBox.Text));
        Click(dialog, dialog.Parameters[1].NameBox);
        Click(dialog, dialog.AddButton);
        dialog.KeyTextInput("int");
        Ui.Settle();
        Click(dialog, dialog.Parameters[2].NameBox);
        dialog.KeyTextInput("offset");
        Click(dialog, dialog.Parameters[2].DefaultBox);
        dialog.KeyTextInput("0");
        Ui.Settle();
        Assert.Equal("public static int Scale(int factor, int value, int offset)", dialog.Preview);
        Assert.Equal(string.Empty, dialog.Problem);
        Next(dialog);

        string calc = Calc.Replace("Scale(int value, int factor, int unused)", "Scale(int factor, int value, int offset)", StringComparison.Ordinal)
            .Replace("Scale(value, 2, 0)", "Scale(2, value, 0)", StringComparison.Ordinal);
        string till = Till.Replace("Calc.Scale(2, 3, 9) + Calc.Scale(4, factor: 5, unused: 1)", "Calc.Scale(3, 2, 0) + Calc.Scale(factor: 5, 4, 0)", StringComparison.Ordinal);
        Assert.Equal(calc, window.EditorArea.Active?.Text.Snapshot.ToString());
        Assert.Equal(till, File.ReadAllText(PathOf("Till.cs")));
        Assert.StartsWith("Changed the signature of Calc.Scale in 2 files", window.Status, StringComparison.Ordinal);
        Builds(window, "Change Signature");

        // Undo in the editor takes back the declaration and every call at once, asked first.
        window.EditorArea.CodeEditor.Focus();
        Ui.Run(window, EditorActions.Undo);
        Assert.Equal([("Undo", "Undo Change Signature? It changes Calc.cs and Till.cs.")], asked);
        Assert.Equal(Calc, window.EditorArea.Active?.Text.Snapshot.ToString());
        Assert.Equal(Till, File.ReadAllText(PathOf("Till.cs")));
        window.Close();
    }

    [AvaloniaFact]
    public void Extract_interface_makes_the_interface_of_the_members_chosen_in_a_file_of_its_own_and_the_class_implements_it()
    {
        MainWindow window = Open("Order.cs");
        List<(string Title, string Text)> asked = Answer(window);
        PlaceAfter(window, "class Ord");
        // Rider gives it no keys: it is Refactor, Extract Interface... in the menu.
        Assert.True(window.RunFromMenu(AppActions.ExtractInterface), window.Status);
        ExtractInterfaceDialog dialog = Shown<ExtractInterfaceDialog>(window, "Extract Interface");
        Assert.Equal("IOrder", dialog.NameBox.Text);
        Assert.Equal("IOrder.cs", dialog.FileBox.Text);
        Assert.Equal(["Total: int", "Add(int): void", "Describe(): string"], dialog.Members.Select(m => m.Text));
        Click(dialog, dialog.Members[2].Box);
        Assert.False(dialog.Members[2].Box.IsChecked, "A click did not leave Describe out of the interface");
        Next(dialog);

        string extracted = PathOf("IOrder.cs");
        Assert.True(File.Exists(extracted), $"Extract Interface made no IOrder.cs: {window.Status}");
        Assert.Equal("namespace Shop;\n\npublic interface IOrder\n{\n    int Total { get; }\n\n    void Add(int amount);\n}\n", File.ReadAllText(extracted).ReplaceLineEndings("\n"));
        Assert.Equal(Order.Replace("public class Order", "public class Order : IOrder", StringComparison.Ordinal), window.EditorArea.Active?.Text.Snapshot.ToString());
        Assert.Equal("Extracted the interface IOrder from Order to IOrder.cs", window.Status);
        Assert.Contains(window.ExplorerView.Tree.Rows, r => r.Path == extracted);
        // The language server finds the new file: the class's own shows no error for the interface it now implements.
        Ui.WaitFor(() => window.CSharpService!.For(window.EditorArea.Active!)!.Diagnostics.All(d => d.Severity != 1), "the language server to take IOrder.cs in");
        Builds(window, "Extract Interface");

        window.EditorArea.CodeEditor.Focus();
        Ui.Run(window, EditorActions.Undo);
        Assert.Equal([("Undo", "Undo Extract Interface? It changes Order.cs and IOrder.cs.")], asked);
        Assert.False(File.Exists(extracted), "Undo left IOrder.cs");
        Assert.Equal(Order, window.EditorArea.Active?.Text.Snapshot.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void Move_to_another_file_takes_a_type_out_of_a_file_of_several_with_the_usings_it_needs()
    {
        MainWindow window = Open("Shapes.cs");
        List<(string Title, string Text)> asked = Answer(window);
        PlaceAfter(window, "class Circ");
        Ui.Run(window, AppActions.Move);
        Assert.Equal(["Move to Another File...", "Move to Namespace..."], window.Assist.Offered);
        Ui.Press(window, "ENTER");
        MoveTypeDialog dialog = Shown<MoveTypeDialog>(window, "Move to Another File");
        Assert.Equal("Circle.cs", dialog.TargetBox.Text);
        Next(dialog);

        string moved = PathOf("Circle.cs");
        Assert.True(File.Exists(moved), $"Move to Another File made no Circle.cs: {window.Status}");
        Assert.Equal("using System;\n\nnamespace Shop;\n\npublic class Circle\n{\n    public double Area(double radius) => Math.PI * radius * radius;\n}\n",
            File.ReadAllText(moved).ReplaceLineEndings("\n"));
        Assert.Equal("using System;\n\nnamespace Shop;\n\npublic class Square\n{\n    public int Side;\n}\n", window.EditorArea.Active?.Text.Snapshot.ToString());
        Assert.Equal("Moved class Circle to Circle.cs", window.Status);
        Builds(window, "Move to Another File");

        window.EditorArea.CodeEditor.Focus();
        Ui.Run(window, EditorActions.Undo);
        Assert.Equal([("Undo", "Undo Move to Another File? It changes Shapes.cs and Circle.cs.")], asked);
        Assert.False(File.Exists(moved), "Undo left Circle.cs");
        Assert.Equal(Shapes, window.EditorArea.Active?.Text.Snapshot.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void Move_to_namespace_changes_a_type_s_namespace_and_gives_the_files_that_use_it_a_using_of_the_new_one()
    {
        MainWindow window = Open("Order.cs");
        List<(string Title, string Text)> asked = Answer(window);
        PlaceAfter(window, "class Ord");
        Ui.Run(window, AppActions.Move);
        Ui.Press(window, "DOWN");
        Ui.Press(window, "ENTER");
        MoveTypeDialog dialog = Shown<MoveTypeDialog>(window, "Move to Namespace");
        Assert.Equal("Shop", dialog.TargetBox.Text);
        dialog.TargetBox.Focus();
        dialog.TargetBox.SelectAll();
        dialog.KeyTextInput("Sales");
        Next(dialog);

        // The type's file uses Calc, which stayed in Shop; OrderBook, in Shop, and Report, in Shop.Reports, use Order.
        Assert.Equal("using Shop;\n\n" + Order.Replace("namespace Shop;", "namespace Sales;", StringComparison.Ordinal), window.EditorArea.Active?.Text.Snapshot.ToString());
        Assert.Equal("using Sales;\n\n" + OrderBook, File.ReadAllText(PathOf("OrderBook.cs")));
        Assert.Equal(Report.Replace("using Shop;\n", "using Sales;\nusing Shop;\n", StringComparison.Ordinal), File.ReadAllText(PathOf("Report.cs")));
        Assert.StartsWith("Moved class Order to namespace Sales in 3 files", window.Status, StringComparison.Ordinal);
        Builds(window, "Move to Namespace");

        window.EditorArea.CodeEditor.Focus();
        Ui.Run(window, EditorActions.Undo);
        Assert.Equal([("Undo", "Undo Move to Namespace? It changes Order.cs, OrderBook.cs and Report.cs.")], asked);
        Assert.Equal(Order, window.EditorArea.Active?.Text.Snapshot.ToString());
        Assert.Equal(OrderBook, File.ReadAllText(PathOf("OrderBook.cs")));
        Assert.Equal(Report, File.ReadAllText(PathOf("Report.cs")));
        window.Close();
    }

    [AvaloniaFact]
    public void Pull_members_up_moves_the_members_chosen_into_the_base_class_with_the_usings_they_need()
    {
        MainWindow window = Open("Dog.cs");
        List<(string Title, string Text)> asked = Answer(window);
        PlaceAfter(window, "class Do");
        // Rider gives it no keys: it is Refactor, Pull Members Up... in the menu.
        Assert.True(window.RunFromMenu(AppActions.PullMembersUp), window.Status);
        MembersMoveDialog dialog = Shown<MembersMoveDialog>(window, "Pull Members Up");
        Assert.Equal("Animal", dialog.BaseBox.SelectedItem);
        Assert.Equal(["Legs: int", "Describe(): string"], dialog.Members.Select(m => m.Text));
        Assert.False(dialog.NextButton.IsEnabled, "Next is offered with no member chosen");
        Click(dialog, dialog.Members[0].Moved);
        Click(dialog, dialog.Members[1].Moved);
        Next(dialog);

        Assert.Equal("using System.Text;\n\nnamespace Shop;\n\npublic class Dog : Animal\n{\n}\n", window.EditorArea.Active?.Text.Snapshot.ToString());
        Assert.Equal("using System.Text;\n\nnamespace Shop;\n\npublic class Animal\n{\n    public string Name = \"\";\n\n    public virtual string Sound() => \"...\";\n\n"
            + "    public int Legs => 4;\n\n    public string Describe()\n    {\n        StringBuilder text = new();\n        text.Append(Name).Append(' ').Append(Legs);\n"
            + "        return text.ToString();\n    }\n}\n", File.ReadAllText(PathOf("Animal.cs")).ReplaceLineEndings("\n"));
        Assert.Equal("Pulled 2 members of Dog up to Animal", window.Status);
        Builds(window, "Pull Members Up");

        window.EditorArea.CodeEditor.Focus();
        Ui.Run(window, EditorActions.Undo);
        Assert.Equal([("Undo", "Undo Pull Members Up? It changes Dog.cs and Animal.cs.")], asked);
        Assert.Equal(Dog, window.EditorArea.Active?.Text.Snapshot.ToString());
        Assert.Equal(Animal, File.ReadAllText(PathOf("Animal.cs")));
        window.Close();
    }

    [AvaloniaFact]
    public void Push_members_down_copies_a_member_into_each_inheritor_and_made_abstract_leaves_it_abstract_in_the_base_class()
    {
        MainWindow window = Open("Animal.cs");
        List<(string Title, string Text)> asked = Answer(window);
        PlaceAfter(window, "class Anim");
        Assert.True(window.RunFromMenu(AppActions.PushMembersDown), window.Status);
        MembersMoveDialog dialog = Shown<MembersMoveDialog>(window, "Push Members Down");
        Assert.Equal(["Cat", "Dog"], dialog.Inheritors.Select(i => i.Text).Order(StringComparer.Ordinal));
        Assert.All(dialog.Inheritors, i => Assert.True(i.Box.IsChecked, $"{i.Text} is not chosen at first"));
        Assert.Equal(["Name: string", "Sound(): string"], dialog.Members.Select(m => m.Text));
        Click(dialog, dialog.Members[1].Moved);
        Click(dialog, dialog.Members[1].Abstract);
        Next(dialog);

        Assert.Equal("namespace Shop;\n\npublic abstract class Animal\n{\n    public string Name = \"\";\n\n    public abstract string Sound();\n}\n", window.EditorArea.Active?.Text.Snapshot.ToString());
        Assert.Equal(Dog.Replace("        return text.ToString();\n    }\n}\n", "        return text.ToString();\n    }\n\n    public override string Sound() => \"...\";\n}\n", StringComparison.Ordinal),
            File.ReadAllText(PathOf("Dog.cs")).ReplaceLineEndings("\n"));
        Assert.Equal(Cat.Replace("    public bool Purrs => true;\n", "    public bool Purrs => true;\n\n    public override string Sound() => \"...\";\n", StringComparison.Ordinal),
            File.ReadAllText(PathOf("Cat.cs")).ReplaceLineEndings("\n"));
        Assert.Equal("Pushed 1 member of Animal down to 2 classes", window.Status);
        Builds(window, "Push Members Down");

        window.EditorArea.CodeEditor.Focus();
        Ui.Run(window, EditorActions.Undo);
        Assert.Single(asked);
        Assert.Equal(Animal, window.EditorArea.Active?.Text.Snapshot.ToString());
        Assert.Equal(Dog, File.ReadAllText(PathOf("Dog.cs")));
        Assert.Equal(Cat, File.ReadAllText(PathOf("Cat.cs")));
        window.Close();
    }

    private MainWindow Open(string file)
    {
        MainWindow window = new() { Width = 1200, Height = 800, Autosaves = false };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile(PathOf(file));
        Ui.Settle();
        Assert.True(window.CSharpService?.IsReady, window.CSharpService?.State);
        return window;
    }

    /// <summary>The dialog a refactoring opened, once it shows.</summary>
    private static T Shown<T>(MainWindow window, string what) where T : RefactoringDialog
    {
        Ui.WaitFor(() => window.OwnedWindows.OfType<T>().Any() || window.Status?.Contains(what, StringComparison.Ordinal) == true, $"{what}'s dialog to open");
        return DialogButtonsTests.Check(window.OwnedWindows.OfType<T>().FirstOrDefault() ?? throw new Xunit.Sdk.XunitException($"{what} opened no dialog: {window.Status}"));
    }

    /// <summary>Next clicked, and the dialog waited out: closed once the refactoring is made, failing with what it says
    /// when it stays open.</summary>
    private static void Next(RefactoringDialog dialog)
    {
        Assert.True(dialog.NextButton.IsEnabled, $"Next is disabled: {dialog.Problem}");
        Click(dialog, dialog.NextButton);
        Ui.WaitFor(() => !dialog.IsVisible || dialog.Problem.Length > 0, "the refactoring to be made");
        Assert.False(dialog.IsVisible, $"The dialog stayed open: {dialog.Problem}");
    }

    /// <summary>Saves every file and builds the project with the .NET command line, as a person would check it.</summary>
    private void Builds(MainWindow window, string after)
    {
        Assert.True(window.EditorArea.SaveAll(), "The files could not be saved");
        ChildResult built = Ui.Await(ChildProcesses.RunAsync(ChildProcesses.DotnetCommand(["build", folder, "--no-restore", "-nologo", "-v:q"]), limit: TimeSpan.FromMinutes(3)),
            "the project to build", TimeSpan.FromMinutes(4));
        Assert.True(built.Succeeded, $"The project does not build after {after}:\n{built.Output}{built.Error}");
    }

    private static void PlaceAfter(MainWindow window, string text)
    {
        CodeEditor editor = window.EditorArea.CodeEditor;
        int at = editor.Document.Snapshot.ToString().IndexOf(text, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{text}' is not in the file");
        editor.Selections = SelectionSet.Single(at + text.Length);
        editor.Focus();
    }

    /// <summary>A click on a control of a window, at its middle, as a person's mouse makes it.</summary>
    private static void Click(Window window, Control control)
    {
        Ui.Settle();
        Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new Xunit.Sdk.XunitException($"{control} is not in the dialog");
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
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

    private string PathOf(string name) => Path.Combine(folder, name);
}
