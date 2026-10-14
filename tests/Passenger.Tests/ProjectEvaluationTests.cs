using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Explorer;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>The Solution view shows each project as MSBuild evaluates it, in the project host: its conditions, imports
/// (Directory.Build.props, central package management) and wildcards as a build sees them; a project file edited on disk
/// shows anew; and a folder opened again shows its projects from the cache of their last evaluation, evaluating none whose
/// inputs did not change.</summary>
public sealed class ProjectEvaluationTests : IDisposable
{
    /// <summary>A package referenced under a condition that is false, one under a condition the repository's props make true,
    /// one whose version is central; .cs files of a folder removed, and those of a folder outside the project taken in by a
    /// wildcard but one folder of them excluded, each at a link of the project's.</summary>
    private const string Project = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Humanizer.Core" />
            <PackageReference Include="Newtonsoft.Json" Condition="'$(TargetFramework)' == 'net8.0'" />
            <PackageReference Include="Serilog" Condition="'$(ShopLogs)' == 'true'" />
            <Compile Remove="Legacy/**" />
            <Compile Include="../Shared/**/*.cs" Exclude="../Shared/Obsolete/**" Link="Shared/%(RecursiveDir)%(Filename)%(Extension)" />
          </ItemGroup>
        </Project>

        """;

    private readonly string folder = PathNames.Canonical(TestFolders.Create("evaluation-"));

    public ProjectEvaluationTests()
    {
        Write("Shop.slnx", "<Solution>\n  <Project Path=\"App/App.csproj\" />\n</Solution>\n");
        Write("Directory.Build.props", "<Project>\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <ShopLogs>true</ShopLogs>\n  </PropertyGroup>\n</Project>\n");
        Write("Directory.Packages.props", """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Humanizer.Core" Version="2.14.1" />
                <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
                <PackageVersion Include="Serilog" Version="4.0.0" />
              </ItemGroup>
            </Project>

            """);
        Write("App/App.csproj", Project);
        Write("App/Program.cs", "System.Console.WriteLine(\"Shop\");\n");
        Write("App/Legacy/Old.cs", "class Old\n{\n}\n");
        Write("Shared/Helper.cs", "class Helper\n{\n}\n");
        Write("Shared/Text/Words.cs", "class Words\n{\n}\n");
        Write("Shared/Obsolete/Gone.cs", "class Gone\n{\n}\n");
    }

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void A_project_shows_its_files_and_packages_as_MSBuild_evaluates_its_conditions_imports_wildcards_and_central_versions()
    {
        MainWindow window = Open();
        Assert.Equal("net10.0", Row(window, NodeKind.Project, "App").Detail);
        Assert.Equal(["Dependencies", "Shared", "Program.cs"], Children(window, "App"));
        Assert.Equal(["Text", "Helper.cs"], Children(window, "Shared"));
        Assert.Equal(["Words.cs"], Children(window, "Text"));
        Assert.Equal([("net10.0", null), ("Humanizer.Core", "2.14.1"), ("Serilog", "4.0.0")],
            Children(window, "Dependencies").Select(name => (name, Row(window, null, name).Detail)));
        // The linked files open from where they are.
        Assert.Equal(PathNames.Canonical(Path.Combine(folder, "Shared", "Text", "Words.cs")), Row(window, NodeKind.File, "Words.cs").Path);
        window.Close();
    }

    [AvaloniaFact]
    public void A_project_file_edited_on_disk_shows_what_it_says_now()
    {
        MainWindow window = Open();
        Assert.Contains("Serilog", Children(window, "Dependencies"));
        Write("App/App.csproj", Project.Replace("""<Compile Remove="Legacy/**" />""", """<Compile Remove="Program.cs" />""")
            .Replace("""'$(ShopLogs)' == 'true'""", """'$(ShopLogs)' == 'false'"""));
        Ui.WaitFor(() => !window.ExplorerView.Tree.Rows.Any(r => r.Name == "Serilog"), "the Dependencies to drop the package the project's condition now leaves out");
        Assert.Equal(["Dependencies", "Legacy", "Shared"], Children(window, "App"));
        Assert.Equal(["Old.cs"], Children(window, "Legacy"));
        window.Close();
    }

    [AvaloniaFact]
    public void A_folder_opened_again_shows_its_projects_from_their_last_evaluation_before_any_is_evaluated_again()
    {
        string settings = TestFolders.Create("settings-");
        MainWindow first = new() { Width = 1200, Height = 800, StateFolder = settings };
        first.Show();
        first.KeepLayout();
        first.OpenFolder(Path.Combine(folder, "Shop.slnx"));
        Ui.Settle();
        Assert.True(first.Session.Models.Evaluations > 0, "The folder's first opening evaluated nothing");
        first.Close();

        MainWindow second = new() { Width = 1200, Height = 800, StateFolder = settings };
        second.Show();
        second.KeepLayout();
        second.OpenFolder(Path.Combine(folder, "Shop.slnx"));
        // The cache is read off the window's thread, its tree shown as it lands; no project was evaluated for it.
        Ui.Settle();
        Assert.True(second.Session.Models.Evaluations == 0,
            $"Opened again with nothing changed, the folder had {second.Session.Models.Evaluations} project(s) evaluated again rather than shown from the cache");
        Assert.Equal(["Dependencies", "Shared", "Program.cs"], Children(second, "App"));
        Assert.Contains("Serilog", Children(second, "Dependencies"));

        // A file a project's wildcard takes in, made while it was closed, has that project evaluated again as it opens.
        second.Close();
        Write("Shared/Text/Letters.cs", "class Letters\n{\n}\n");
        MainWindow third = new() { Width = 1200, Height = 800, StateFolder = settings };
        third.Show();
        third.KeepLayout();
        third.OpenFolder(Path.Combine(folder, "Shop.slnx"));
        Ui.Settle();
        Assert.Equal(1, third.Session.Models.Evaluations);
        Assert.Equal(["Text", "Helper.cs"], Children(third, "Shared"));
        Assert.Equal(["Letters.cs", "Words.cs"], Children(third, "Text"));
        third.Close();
    }

    [AvaloniaFact]
    public void The_project_of_the_file_shown_and_those_it_references_load_in_the_language_server_before_the_rest()
    {
        Write("Shop.slnx", "<Solution>\n  <Project Path=\"App/App.csproj\" />\n  <Project Path=\"Core/Core.csproj\" />\n  <Project Path=\"Reports/Reports.csproj\" />\n"
            + "  <Project Path=\"Admin/Admin.csproj\" />\n</Solution>\n");
        Write("App/App.csproj", Project.Replace("</ItemGroup>", "  <ProjectReference Include=\"../Core/Core.csproj\" />\n  </ItemGroup>"));
        foreach (string name in new[] { "Core", "Reports", "Admin" })
        {
            Write($"{name}/{name}.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n</Project>\n");
            Write($"{name}/{name}Code.cs", $"class {name}Code\n{{\n}}\n");
        }
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(Path.Combine(folder, "Shop.slnx"));
        window.OpenFile(Path.Combine(folder, "App", "Program.cs"));
        Ui.Settle();
        Assert.True(window.CSharpService?.Session is not null, "No language server started");
        // The order the server took them in, as its log says it finished (or failed) each: unrestored, they load with errors.
        string log = File.ReadAllText(Directory.EnumerateFiles(Passenger.App.CSharp.CSharpService.LogFolder, $"server-*-{window.CSharpService!.Session!.Server.ProcessId}.log").Single());
        int At(string project) => log.IndexOf(Path.Combine(project, project + ".csproj"), StringComparison.Ordinal) is >= 0 and var at ? at
            : throw new InvalidOperationException($"The language server's log says nothing of {project}:\n{log}");
        Assert.True(Math.Max(At("App"), At("Core")) < Math.Min(At("Reports"), At("Admin")),
            $"The language server took up another project before the file shown's, and the one it references:\n{log}");
        window.Close();
    }

    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(Path.Combine(folder, "Shop.slnx"));
        Ui.Settle();
        return window;
    }

    /// <summary>A row of the Explorer, opened, and the names of the rows under it.</summary>
    private static List<string> Children(MainWindow window, string name)
    {
        ExplorerTree tree = window.ExplorerView.Tree;
        ExplorerNode row = Row(window, null, name);
        tree.Expand(row);
        return [.. (row.Children ?? []).Select(c => c.Name)];
    }

    /// <summary>A row by its kind and name, the solution's top opened down to the project's first.</summary>
    private static ExplorerNode Row(MainWindow window, NodeKind? kind, string name)
    {
        ExplorerTree tree = window.ExplorerView.Tree;
        if (tree.Rows.FirstOrDefault(r => r.Kind == NodeKind.Project) is { IsExpanded: false } project)
        {
            tree.Expand(project);
        }
        return tree.Rows.FirstOrDefault(r => (kind is null || r.Kind == kind) && r.Name == name)
            ?? throw new InvalidOperationException($"The Explorer shows no {kind} {name}: [{string.Join(", ", tree.Rows.Select(r => r.Name))}]");
    }

    private void Write(string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
