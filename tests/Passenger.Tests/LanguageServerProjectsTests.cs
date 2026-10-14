using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Passenger.App;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.Tests;

/// <summary>The language server's projects. It loads them from what Passenger's project host built of them at design time,
/// as its own build host would have built them, so it answers across a project reference; a folder opened again has them
/// loaded from those builds as kept, none built again while nothing they read changed. A file of a multi-targeted project
/// is answered for in the target framework chosen in the editor's switcher, as Rider's.</summary>
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

    [AvaloniaFact]
    public void A_file_of_a_multi_targeted_project_is_answered_for_in_the_target_framework_chosen_in_the_editor()
    {
        string greeter = Path.Combine(folder, "Lib", "Greeter.cs");
        MainWindow window = Keeping();
        window.OpenFile(greeter);
        OpenDocument document = window.EditorArea.Active!;
        CodeEditor editor = window.EditorArea.CodeEditor;
        Ui.WaitFor(() => window.CSharpService?.IsReady == true && document.ProjectContexts.Count == 2,
            "the server to say Greeter.cs is compiled for both of Lib's target frameworks", TimeSpan.FromMinutes(2));
        Assert.Equal(["Lib (net10.0)", "Lib (net11.0)"], document.ProjectContexts.Order(StringComparer.Ordinal));
        Assert.Equal(document.ProjectContext![5..^1], editor.ContextLabel);

        // Only net11.0 compiles the member that names what does not exist.
        Choose(window, editor, "Lib (net11.0)");
        Assert.Equal("net11.0", editor.ContextLabel);
        Ui.WaitFor(() => Missing(window, document), "the error on Missing, which only net11.0 compiles");
        Choose(window, editor, "Lib (net10.0)");
        Assert.Equal("net10.0", editor.ContextLabel);
        Ui.WaitFor(() => !Missing(window, document) && window.CSharpService!.For(document)!.Diagnostics.Count == 0,
            "the error on Missing to go, net10.0 compiling none of it");
        window.Close();
    }

    /// <summary>Clicks the editor's switcher and the framework in the menu it opens.</summary>
    private static void Choose(MainWindow window, CodeEditor editor, string context)
    {
        Rect bounds = editor.ContextSwitcherBounds;
        Assert.True(bounds.Width > 0, "The editor shows no target framework switcher for a file compiled for two");
        Point at = editor.TranslatePoint(bounds.Center, window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
        ContextMenu menu = Assert.IsType<ContextMenu>(window.OpenContextMenu);
        MenuItem item = menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == context);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();
        Ui.Settle();
    }

    [AvaloniaFact]
    public void Leaving_a_folder_whose_projects_are_still_being_built_lets_the_next_folders_builds_start_at_once()
    {
        // A project whose design-time build waits three minutes, as a large solution's round goes on for minutes.
        string slow = PathNames.Canonical(TestFolders.Create("slow-"));
        string quick = PathNames.Canonical(TestFolders.Create("quick-"));
        try
        {
            Write(slow, "Slow/Slow.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net11.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
            Write(slow, "Slow/Slow.cs", "class Slow\n{\n}\n");
            Write(slow, "Directory.Build.targets", """
                <Project>
                  <Target Name="Wait" BeforeTargets="CoreCompile" Condition="'$(DesignTimeBuild)' == 'true'">
                    <Exec Condition="'$(OS)' != 'Windows_NT'" Command="echo begun &gt; built.started; sleep 180" />
                    <Exec Condition="'$(OS)' == 'Windows_NT'" Command="powershell -NoProfile -Command &quot;Set-Content -Path built.started -Value begun; Start-Sleep -Seconds 180&quot;" />
                  </Target>
                </Project>
                """);
            Write(quick, "Quick/Quick.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net11.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
            Write(quick, "Quick/Quick.cs", "class Quick\n{\n}\n");
            Restore.Run(Path.Combine(slow, "Slow", "Slow.csproj"));
            Restore.Run(Path.Combine(quick, "Quick", "Quick.csproj"));

            MainWindow window = new() { Width = 1200, Height = 800 };
            window.Show();
            window.OpenFolder(slow);
            // Pumped, not settled: the language server's requests about the open file wait for its load, as long as the build.
            Ui.PumpUntil(() => File.Exists(Path.Combine(slow, "Slow", "built.started")), "the slow project's design-time build to begin", TimeSpan.FromMinutes(2));

            // The host builds one round at a time: the slow folder's must end as it is left, or the next waits minutes.
            window.OpenFolder(quick);
            Ui.PumpUntil(() => window.Session.DesignTimeBuilds.Builds == 1,
                "the next folder's project built at design time, which waits behind the round of the folder left unless that round ended",
                TimeSpan.FromSeconds(60));
            window.Close();
        }
        finally
        {
            TestFolders.Delete(slow);
            TestFolders.Delete(quick);
        }
    }

    private static bool Missing(MainWindow window, OpenDocument document) =>
        window.CSharpService?.For(document)?.Diagnostics.Any(d => d.Code?.ToString() == "CS0103" && d.Message.Contains("Missing", StringComparison.Ordinal)) == true;

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
