using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Migrations;

namespace Passenger.Tests;

/// <summary>The Migrations window over a SQLite project's context, as a person uses it: its migrations listed with which
/// the database has, Add, Update (to the latest and back to one), Script into a tab; and what keeps dotnet ef from working
/// (no tool, a tool of another version, no Design package) found before the command, fixed in place, and the command then
/// made. EF Core and dotnet-ef come from the package cache through the run's local feed, never nuget.org.</summary>
public sealed class MigrationsTests
{
    /// <summary>The longest a build and a dotnet ef command take together on a developer's machine, scaled by the run.</summary>
    private static readonly TimeSpan Slow = TimeSpan.FromMinutes(2);

    [AvaloniaFact]
    public void Migrations_are_added_applied_rolled_back_and_scripted_against_the_runs_database()
    {
        string folder = ShopApp.Make(design: true, tool: ShopApp.Version);
        MainWindow window = Ui.OpenFolder(folder, languageServer: false);
        try
        {
            window.ShowToolWindow(ToolWindowIds.Migrations);
            Ui.WaitFor(() => window.Migrations.Listed, $"the shop's migrations listed (status: {window.Status}; gaps: {Gaps(window)})", Slow);
            Assert.True(window.Migrations.Migrations.Count == 0, $"A context with no migration listed {window.Migrations.Migrations.Count}");
            Assert.Equal("ShopContext", window.Migrations.Context);

            Add(window, "Initial");
            Assert.Equal([("Initial", (bool?)false)], Rows(window));

            Tool(window, AppActions.UpdateDatabase);
            Ui.WaitFor(() => Rows(window) is [(_, true)], $"Initial applied to the database ({window.Status})", Slow);

            // A model change and its migration: pending until the database is updated again.
            ShopApp.AddPrice(folder);
            Add(window, "AddPrice");
            Assert.Equal([("Initial", (bool?)true), ("AddPrice", false)], Rows(window));
            Tool(window, AppActions.UpdateDatabase);
            Ui.WaitFor(() => Rows(window) is [(_, true), (_, true)], $"both applied ({window.Status})", Slow);

            // Back to Initial: rolling AddPrice back asks first, naming it.
            ListBox list = window.MigrationsView.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "MigrationsList");
            Ui.RightClick(Assert.IsType<ListBoxItem>(list.ContainerFromItem(window.Migrations.Migrations.Single(m => m.Name == "Initial"))));
            Ui.Choose(list.ContextMenu, "Update Database to Initial");
            ChoiceDialog asked = Asked<ChoiceDialog>(window, "the roll back's question");
            Assert.Contains("AddPrice", string.Join(' ', asked.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)), StringComparison.Ordinal);
            Ui.ClickButton(asked, "Roll Back");
            Ui.WaitFor(() => Rows(window) is [(_, true), (_, false)], $"AddPrice rolled back ({window.Status})", Slow);

            // A script of every migration, into a tab of its own.
            Tool(window, AppActions.ScriptMigrations);
            MigrationScriptDialog script = Asked<MigrationScriptDialog>(window, "Script Migrations' question");
            Ui.ClickButton(script, "Script");
            Ui.WaitFor(() => window.EditorArea.Active?.Path.EndsWith(".sql", StringComparison.Ordinal) == true, $"the script open in a tab ({window.Status})", Slow);
            string sql = window.EditorArea.Active!.Text.Snapshot.ToString();
            Assert.Contains("CREATE TABLE \"Products\"", sql, StringComparison.Ordinal);
            Assert.Contains("\"Price\"", sql, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_missing_tool_and_Design_package_are_found_before_the_command_fixed_in_place_and_the_command_made()
    {
        string folder = ShopApp.Make(design: false, tool: null);
        MainWindow window = Ui.OpenFolder(folder, languageServer: false);
        try
        {
            window.ShowToolWindow(ToolWindowIds.Migrations);
            Ui.WaitFor(() => Gaps(window).Contains("dotnet ef is not installed here", StringComparison.Ordinal)
                && Gaps(window).Contains("lacks Microsoft.EntityFrameworkCore.Design", StringComparison.Ordinal), $"both gaps shown before any command ({Gaps(window)})");
            Assert.False(window.Migrations.Listed, "The window ran dotnet ef with the tool missing");

            Tool(window, AppActions.AddMigration);
            Name(window, "Initial");
            ChoiceDialog tool = Asked<ChoiceDialog>(window, "the missing tool's question");
            Ui.ClickButton(tool, $"Pin the Local dotnet-ef to {ShopApp.Version}");
            ChoiceDialog design = Asked<ChoiceDialog>(window, "the missing Design package's question", Slow);
            Ui.ClickButton(design, $"Add Microsoft.EntityFrameworkCore.Design {ShopApp.Version}");
            Ui.WaitFor(() => Directory.Exists(Path.Combine(folder, "Migrations")) && Directory.GetFiles(Path.Combine(folder, "Migrations"), "*_Initial.cs").Length == 1,
                $"the migration made once both were fixed ({window.Status}; gaps: {Gaps(window)})", Slow);

            JsonObject manifest = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "dotnet-tools.json")))!;
            Assert.Equal(ShopApp.Version, manifest["tools"]!["dotnet-ef"]!["version"]!.GetValue<string>());
            string project = File.ReadAllText(Path.Combine(folder, "Shop.csproj"));
            Assert.Contains($"<PackageReference Include=\"Microsoft.EntityFrameworkCore.Design\" Version=\"{ShopApp.Version}\"", project, StringComparison.Ordinal);
            Assert.Contains("<PrivateAssets>all</PrivateAssets>", project, StringComparison.Ordinal);
            Ui.WaitFor(() => Gaps(window).Length == 0, $"no gap left ({Gaps(window)})");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_tool_of_another_version_is_said_with_both_versions_and_pinned_to_the_runtimes_before_the_command()
    {
        string folder = ShopApp.Make(design: true, tool: ShopApp.Older);
        MainWindow window = Ui.OpenFolder(folder, languageServer: false);
        try
        {
            window.ShowToolWindow(ToolWindowIds.Migrations);
            string said = $"The local dotnet-ef {ShopApp.Older} does not match Shop's EF Core {ShopApp.Version}";
            Ui.WaitFor(() => Gaps(window).Contains(said, StringComparison.Ordinal), $"the mismatch said with both versions ({Gaps(window)})", Slow);

            Tool(window, AppActions.AddMigration);
            Name(window, "Initial");
            ChoiceDialog asked = Asked<ChoiceDialog>(window, "the mismatched tool's question", Slow);
            Ui.ClickButton(asked, $"Pin the Local dotnet-ef to {ShopApp.Version}");
            Ui.WaitFor(() => window.Migrations.Migrations.Any(m => m.Name == "Initial"), $"the migration made with the pinned tool ({window.Status})", Slow);
            JsonObject manifest = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "dotnet-tools.json")))!;
            Assert.Equal(ShopApp.Version, manifest["tools"]!["dotnet-ef"]!["version"]!.GetValue<string>());
            Assert.DoesNotContain("does not match", Gaps(window), StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    private static string Gaps(MainWindow window) => string.Join(" | ", window.MigrationsView.GapsShown);

    private static IReadOnlyList<(string Name, bool? Applied)> Rows(MainWindow window) => [.. window.Migrations.Migrations.Select(m => (m.Name, m.Applied))];

    /// <summary>Add Migration… from the window's button, its name typed into its question, waited for until it is listed.</summary>
    private static void Add(MainWindow window, string name)
    {
        Tool(window, AppActions.AddMigration);
        Name(window, name);
        Ui.WaitFor(() => window.Migrations.Busy is null && window.Migrations.Migrations.Any(m => m.Name == name), $"{name} added and listed ({window.Status})", Slow);
    }

    private static void Name(MainWindow window, string name)
    {
        ConfirmDialog dialog = Asked<ConfirmDialog>(window, "Add Migration's question");
        dialog.KeyTextInput(name);
        Ui.ClickButton(dialog, "Add");
    }

    /// <summary>A click on one of the Migrations window's buttons, by the action it runs.</summary>
    private static void Tool(MainWindow window, string action)
    {
        Button button = window.MigrationsView.GetVisualDescendants().OfType<Button>().Single(b => b.Name == action);
        Ui.Click(button, settle: false);
    }

    private static T Asked<T>(MainWindow window, string what, TimeSpan? limit = null)
        where T : Window
    {
        Ui.WaitFor(() => window.OwnedWindows.OfType<T>().Any(d => d.IsVisible), $"{what} (status: {window.Status}; gaps: {Gaps(window)})", limit);
        return DialogButtonsTests.Check(window.OwnedWindows.OfType<T>().Single(d => d.IsVisible));
    }
}

/// <summary>A console app with a SQLite context, as a test of migrations writes it: EF Core's SQLite provider at the
/// version the tests pin, the Design package or not, the local tool at a version or none, restored from the run's local
/// feed, which holds the tool and the Design package copied from the package cache.</summary>
internal static class ShopApp
{
    public static string Version { get; } = AvaloniaApp.Metadata("EfCoreVersion");

    public static string Older { get; } = AvaloniaApp.Metadata("EfToolOlderVersion");

    public static string Make(bool design, string? tool)
    {
        string folder = TestFolders.Create("shop");
        Feed();
        AvaloniaApp.UseFeed(folder);
        string packages = $"    <PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"{Version}\" />\n"
            + (design ? $"    <PackageReference Include=\"Microsoft.EntityFrameworkCore.Design\" Version=\"{Version}\" PrivateAssets=\"all\" />\n" : string.Empty);
        TestFolders.Write(folder, "Shop.csproj", TestProjects.Console("<Nullable>enable</Nullable>", "<ImplicitUsings>enable</ImplicitUsings>")
            .With($"  <ItemGroup>\n{packages}  </ItemGroup>\n"));
        TestFolders.Write(folder, "Program.cs", """
            using Microsoft.EntityFrameworkCore;

            using ShopContext context = new();
            Console.WriteLine(context.Products.Count());

            public sealed class Product
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            public sealed class ShopContext : DbContext
            {
                public DbSet<Product> Products => Set<Product>();

                protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=shop.db");
            }

            """);
        if (tool is not null)
        {
            TestFolders.Write(folder, "dotnet-tools.json", $$"""
                {
                  "version": 1,
                  "isRoot": true,
                  "tools": {
                    "dotnet-ef": {
                      "version": "{{tool}}",
                      "commands": [ "dotnet-ef" ],
                      "rollForward": false
                    }
                  }
                }

                """);
        }
        Restore.Run(Path.Combine(folder, "Shop.csproj"));
        return folder;
    }

    /// <summary>The model changed: a product has a price, which no migration covers yet.</summary>
    public static void AddPrice(string folder)
    {
        string program = Path.Combine(folder, "Program.cs");
        File.WriteAllText(program, File.ReadAllText(program).Replace("    public string Name { get; set; } = \"\";\n", "    public string Name { get; set; } = \"\";\n    public decimal Price { get; set; }\n", StringComparison.Ordinal));
    }

    /// <summary>The tool at both versions and the Design package, copied from the package cache into the run's local feed.</summary>
    private static void Feed()
    {
        string cache = AvaloniaApp.Metadata("NuGetPackageRoot");
        foreach ((string id, string version) in new[] { ("dotnet-ef", Version), ("dotnet-ef", Older), ("microsoft.entityframeworkcore.design", Version) })
        {
            string package = Path.Combine(cache, id, version, $"{id}.{version}.nupkg");
            Assert.True(File.Exists(package), $"{id} {version} is not in the package cache at {package}: restore the tests first");
            string fed = Path.Combine(AvaloniaApp.Feed, Path.GetFileName(package));
            if (!File.Exists(fed))
            {
                File.Copy(package, fed, overwrite: true);
            }
        }
    }
}
