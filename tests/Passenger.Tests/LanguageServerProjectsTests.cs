using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>The language server's projects. It loads them from what Passenger's project host built of them at design time,
/// as its own build host would have built them, so it answers across a project reference; a folder opened again has them
/// loaded from those builds as kept, none built again while nothing they read changed.</summary>
public sealed class LanguageServerProjectsTests : IClassFixture<RestoredFolders>, IDisposable
{
    private readonly string folder;
    private readonly string settings = TestFolders.Create("settings-");

    public LanguageServerProjectsTests(RestoredFolders folders)
    {
        string made = string.Empty;
        TestFolders.Prepare(() => made = folders.Take("builds-", root =>
        {
            Write(root, "Shapes.slnx", "<Solution>\n  <Project Path=\"App/App.csproj\" />\n  <Project Path=\"Lib/Lib.csproj\" />\n</Solution>\n");
            Write(root, "Lib/Lib.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFrameworks>net10.0;net11.0</TargetFrameworks>\n  </PropertyGroup>\n</Project>\n");
            Write(root, "Lib/Greeter.cs", """
                namespace Lib;

                public static class Greeter
                {
                    public static string Hello() => "Hello";
                #if NET11_0_OR_GREATER
                    public static int Newer() => Missing;
                #endif
                }

                """);
            Write(root, "App/App.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net11.0</TargetFramework>\n  </PropertyGroup>\n  <ItemGroup>\n    <ProjectReference Include=\"../Lib/Lib.csproj\" />\n  </ItemGroup>\n</Project>\n");
            Write(root, "App/Program.cs", "System.Console.WriteLine(Lib.Greeter.Hello() + Lib.Greeter.Absent());\n");
            // Restored at its real path: MSBuild resolves a reference from there, which would restore Lib twice at once.
            return PathNames.Canonical(Path.Combine(root, "Shapes.slnx"));
        }), settings);
        folder = PathNames.Canonical(made);
    }

    public void Dispose()
    {
        TestFolders.Delete(folder);
        TestFolders.Delete(settings);
    }

    [AvaloniaFact]
    public void A_folder_opened_again_has_the_language_server_load_its_projects_from_the_design_time_builds_kept()
    {
        string program = Path.Combine(folder, "App", "Program.cs");
        MainWindow first = Keeping();
        first.OpenFile(program);
        // Greeter is Lib's: the server took App's reference to it from the host's build.
        WaitForAbsent(first, program);
        Assert.True(first.CSharpService!.Session!.Fed, "The language server built the projects itself rather than load the project host's builds");
        Assert.Equal(2, first.Session.DesignTimeBuilds.Builds);
        first.Close();

        MainWindow second = Keeping();
        second.OpenFile(program);
        WaitForAbsent(second, program);
        Assert.True(second.CSharpService!.Session!.Fed, "Opened again, the language server built the projects itself rather than load the builds kept");
        Assert.True(second.Session.DesignTimeBuilds.Builds == 0,
            $"Opened again with nothing changed, the folder had {second.Session.DesignTimeBuilds.Builds} project(s) built again rather than loaded from the builds kept");
        second.Close();
    }

    private MainWindow Keeping()
    {
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = settings };
        window.Show();
        window.KeepLayout();
        window.OpenFolder(Path.Combine(folder, "Shapes.slnx"));
        Ui.Settle();
        return window;
    }

    /// <summary>Waits for the server's error on the member Greeter lacks, which it can give only knowing Lib's Greeter.</summary>
    private static void WaitForAbsent(MainWindow window, string program) =>
        Ui.WaitFor(() => window.EditorArea.Documents.FirstOrDefault(d => d.Path == program) is { } open && window.CSharpService?.For(open)?.Diagnostics.Any(d => d.Code?.ToString() == "CS0117" && d.Message.Contains("Absent", StringComparison.Ordinal)) == true,
            "the server's error on Greeter.Absent, which it gives only knowing Lib's Greeter", TimeSpan.FromMinutes(2));

    private static void Write(string root, string relative, string text)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
