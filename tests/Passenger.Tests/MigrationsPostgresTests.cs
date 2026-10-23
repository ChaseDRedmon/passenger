using System.Globalization;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Run;

namespace Passenger.Tests;

/// <summary>The Migrations window against the database a compose run uses: a web app's Postgres, which the app reaches by
/// its service's name inside the run, updated by dotnet ef at the port Compose published it on, as this machine reaches it.
/// Skipped, saying why, without a Linux engine or the Postgres image, which it never pulls.</summary>
public sealed class MigrationsPostgresTests
{
    private const string Postgres = "postgres:17-alpine";

    [AvaloniaFact]
    public void A_compose_runs_Postgres_is_updated_at_the_port_Compose_published_it_on()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        Assert.SkipWhen(Docker.Said("image", "inspect", Postgres) is null, $"The engine lacks {Postgres}, which this test never pulls: pull it to run the test");
        string project = $"ptest{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}efpg";
        string folder = Make(project);
        MainWindow window = ContainerRunsTests.Open(folder);
        try
        {
            RunConfiguration compose = window.Configurations.First(c => c.Kind == RunKind.Compose);
            Assert.True(window.SelectConfiguration(compose.Name));
            string? refused = Ui.Await(window.RunAsync(), "the compose run to come up", TimeSpan.FromMinutes(5));
            Assert.True(refused is null, $"{refused}{Ui.BuildErrors(window)}\n{ContainerRunsTests.Run(window)}");

            // Refreshed with the run up, its plan read and the project built, dotnet ef reaches the run's Postgres where
            // Compose published it (once the window's own listing, which may have begun as the run came up, has ended): a new
            // database, whose history table EF looks for and does not find, has no migration applied.
            window.ShowToolWindow(ToolWindowIds.Migrations);
            Ui.WaitFor(() => window.Migrations.Busy is null && window.Perform(AppActions.MigrationsRefresh), "Refresh Migrations to run");
            Ui.WaitFor(() => window.Migrations.Listed && window.Migrations.Busy is null && window.Migrations.Database is not null,
                () => $"the migrations listed against the run's Postgres ({Said(window)})", TimeSpan.FromMinutes(2));
            Assert.Equal("db at localhost:" + Published(project), window.Migrations.Database!.Split(',')[0]);

            window.Migrations.ChooseContext("ShopContext");
            Ui.Click(window.MigrationsView.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Single(b => b.Name == AppActions.AddMigration), settle: false);
            Ui.WaitFor(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(d => d.IsVisible), "Add Migration's question");
            ConfirmDialog named = window.OwnedWindows.OfType<ConfirmDialog>().Single(d => d.IsVisible);
            named.KeyTextInput("Initial");
            Ui.ClickButton(named, "Add");
            Ui.WaitFor(() => window.Migrations.Busy is null && window.Migrations.Migrations.Any(m => m.Name == "Initial"), () => $"Initial added ({Said(window)})",
                TimeSpan.FromMinutes(2));

            Assert.True(window.Perform(AppActions.UpdateDatabase), "Update Database did not run");
            Ui.WaitFor(() => window.Migrations.Busy is null && window.Migrations.Migrations is [{ Applied: true }], () => $"Initial applied to the run's Postgres ({Said(window)})",
                TimeSpan.FromMinutes(2));
            string applied = Docker.Run("compose", "-p", project, "exec", "-T", "db", "psql", "-U", "postgres", "-d", "shop", "-tAc", "select \"MigrationId\" from \"__EFMigrationsHistory\"");
            Assert.EndsWith("_Initial", applied.Trim(), StringComparison.Ordinal);
            Assert.DoesNotContain("Password=postgres", window.BottomPanel.Build.Document.Snapshot.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Docker.Stopped(window, project, "Stop to take the shop down", "com.docker.compose.service=web");
            window.Close();
        }
        Docker.TakenDown(project, "the folder's end to take Postgres down");
    }

    /// <summary>What the window says as a wait gives up: the status bar, what the Migrations window is doing and shows
    /// missing, and the Build window's last lines, where dotnet ef's own words are.</summary>
    private static string Said(MainWindow window) =>
        $"{window.Status}; under way: {window.Migrations.Busy ?? "nothing"}; building: {window.Builds.IsBuilding}; gaps: {string.Join(" | ", window.MigrationsView.GapsShown)}"
        + $"\nthe Build window ends:\n{string.Join('\n', window.BottomPanel.Build.Document.Snapshot.ToString().Split('\n').TakeLast(15))}";

    /// <summary>The port Compose published Postgres on, on this machine.</summary>
    private static string Published(string project) => Docker.Run("compose", "-p", project, "port", "db", "5432").Trim().Split(':')[^1];

    /// <summary>A solution of one web app on Npgsql's provider, its context registered with the connection string Shop, and a
    /// compose.yaml running it beside Postgres, which it reaches as db; the local tool and the packages from the run's feed.</summary>
    private static string Make(string project)
    {
        string folder = TestFolders.Create("efpg");
        ShopApp.Feed();
        AvaloniaApp.UseFeed(folder);
        string npgsql = AvaloniaApp.Metadata("NpgsqlEfVersion");
        TestFolders.Write(folder, "Shop.slnx", "<Solution>\n  <Project Path=\"Web/Web.csproj\" />\n</Solution>\n");
        TestFolders.Write(folder, "Web/Web.csproj", TestProjects.Web("<ImplicitUsings>enable</ImplicitUsings>", "<Nullable>enable</Nullable>").With($"""
              <ItemGroup>
                <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="{npgsql}" />
                <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="{ShopApp.Version}" PrivateAssets="all" />
              </ItemGroup>

            """));
        TestFolders.Write(folder, "Web/Program.cs", """
            using Microsoft.EntityFrameworkCore;

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.Services.AddDbContext<ShopContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("Shop")));
            WebApplication app = builder.Build();
            app.MapGet("/", () => "shop");
            app.Run();

            public sealed class Product
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            public sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
            {
                public DbSet<Product> Products => Set<Product>();
            }

            """);
        TestFolders.Write(folder, "Web/Dockerfile", """
            FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
            USER $APP_UID
            WORKDIR /app
            EXPOSE 8080

            FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
            WORKDIR /src
            COPY . .
            RUN dotnet publish "Web/Web.csproj" -c Release -o /app/publish

            FROM base AS final
            WORKDIR /app
            COPY --from=build /app/publish .
            ENTRYPOINT ["dotnet", "Web.dll"]

            """);
        TestFolders.Write(folder, "compose.yaml", $$"""
            name: {{project}}
            services:
              web:
                build: { context: ., dockerfile: Web/Dockerfile }
                ports: ["8080"]
                environment:
                  ConnectionStrings__Shop: "Host=db;Port=5432;Database=shop;Username=postgres;Password=postgres"
                depends_on: { db: { condition: service_healthy } }
              db:
                image: {{Postgres}}
                environment: { POSTGRES_PASSWORD: postgres, POSTGRES_DB: shop }
                ports: ["5432"]
                healthcheck: { test: ["CMD-SHELL", "pg_isready -U postgres"], interval: 1s, retries: 60 }

            """);
        TestFolders.Write(folder, "dotnet-tools.json", $$"""
            {
              "version": 1,
              "isRoot": true,
              "tools": {
                "dotnet-ef": { "version": "{{ShopApp.Version}}", "commands": [ "dotnet-ef" ], "rollForward": false }
              }
            }

            """);
        Restore.Run(Path.Combine(folder, "Web", "Web.csproj"));
        return Passenger.Core.PathNames.Canonical(folder);
    }
}
