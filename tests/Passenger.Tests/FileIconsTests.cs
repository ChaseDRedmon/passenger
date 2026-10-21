using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Explorer;
using Passenger.App.Settings;

namespace Passenger.Tests;

/// <summary>Settings' File icons: Passenger's own icons by default, or the file-icons package's, a shape and colour of
/// its own for each language and tool, in the Explorer and the editor's tabs as the setting changes, and back.</summary>
public sealed class FileIconsTests : IDisposable
{
    private readonly string folder = TestFolders.Create("file-icons-");

    public FileIconsTests()
    {
        Write("Program.cs", "System.Console.WriteLine(\"Shop\");\n");
        Write("Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        Write("package.json", "{}\n");
        Write("Dockerfile", "FROM scratch\n");
        Write(".gitignore", "bin/\n");
        Write("README.md", "# Shop\n");
        Write("appsettings.json", "{}\n");
        Write("build.ps1", "Write-Host build\n");
        Write("logo.png", "");
        Write("app.test.ts", "");
        Write("notes.unknown", "");
        Write(".github/dependabot.yml", "version: 2\n");
        Write("src/Order.cs", "class Order;\n");
    }

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void File_icons_draws_each_kind_of_file_in_a_shape_and_colour_of_its_own_and_Default_brings_back_Passengers_own()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        try
        {
            window.OpenFolder(folder);
            window.OpenFile(Path.Combine(folder, "Program.cs"));
            Ui.Settle();
            Assert.Same(Resource("Icon.Code"), Row(window, "Program.cs").Icon);
            Assert.Same(Resource("Icon.Document"), Row(window, "package.json").Icon);

            window.ApplySettings(window.Settings with { FileIcons = FileIconTheme.FileIcons });
            Ui.Settle();
            (string Name, string Shape, string? Colour)[] expected =
            [
                ("Program.cs", "CSharp", "Blue"),
                ("Shop.csproj", "Stack", "Blue"),
                ("package.json", "Npm", "Red"),
                ("Dockerfile", "Docker", "Blue"),
                (".gitignore", "Git", "Red"),
                ("README.md", "Book", "Blue"),
                ("appsettings.json", "Json", "Cyan"),
                ("build.ps1", "PowerShell", "Blue"),
                ("logo.png", "Image", "Orange"),
                ("app.test.ts", "TestTypeScript", "Blue"),
                ("notes.unknown", "File", null),
                (".github", "GitHub", null),
            ];
            foreach ((string name, string shape, string? colour) in expected)
            {
                ExplorerNode row = Row(window, name);
                Assert.True(ReferenceEquals(Resource($"FileIcon.{shape}"), row.Icon), $"Under File Icons {name} is not drawn as file-icons draws it, its {shape}");
                Assert.True(ReferenceEquals(Resource(colour is null ? "Passenger.File.Plain" : $"Passenger.FileIcon.{colour}"), row.IconBrush),
                    $"Under File Icons {name} is not in file-icons' {colour ?? "plain"} colour");
            }
            Assert.True(ReferenceEquals(Resource("Icon.Explorer"), Row(window, "src").Icon), "Under File Icons a plain folder lost the folder icon");
            Assert.True(TabIcons(window).Any(i => ReferenceEquals(i.Data, Resource("FileIcon.CSharp")) && ReferenceEquals(i.Foreground, Resource("Passenger.FileIcon.Blue"))),
                "The open C# file's tab kept Passenger's own icon after File Icons was chosen");

            window.ApplySettings(window.Settings with { FileIcons = FileIconTheme.Default });
            Ui.Settle();
            Assert.True(ReferenceEquals(Resource("Icon.Code"), Row(window, "Program.cs").Icon), "Default did not bring back Passenger's own icon for C#");
            Assert.True(ReferenceEquals(Resource("Icon.Document"), Row(window, "package.json").Icon), "Default did not bring back Passenger's own icon for a JSON file");
            Assert.True(TabIcons(window).Any(i => ReferenceEquals(i.Data, Resource("Icon.Code"))), "The open C# file's tab kept file-icons' icon after Default was chosen");
        }
        finally
        {
            // The icon set in force is the process's; the next test starts on Passenger's own.
            window.ApplySettings(new AppSettings());
            window.Close();
        }
    }

    private static IEnumerable<PathIcon> TabIcons(MainWindow window) => window.EditorArea.GetVisualDescendants().OfType<PathIcon>();

    private static ExplorerNode Row(MainWindow window, string name) =>
        window.ExplorerView.Tree.Rows.FirstOrDefault(r => r.Name == name)
        ?? throw new InvalidOperationException($"The Explorer shows no {name}: [{string.Join(", ", window.ExplorerView.Tree.Rows.Select(r => r.Name))}]");

    private static object Resource(string key)
    {
        Application app = Application.Current!;
        return app.TryFindResource(key, app.ActualThemeVariant, out object? value) && value is not null ? value : throw new InvalidOperationException($"The theme has no {key}");
    }

    private void Write(string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
