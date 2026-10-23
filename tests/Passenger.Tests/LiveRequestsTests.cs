using System.Net.Http;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Endpoints;

namespace Passenger.Tests;

/// <summary>The Requests window: a web app on EF Core and SQLite that Run runs, followed while the window shows with no
/// change to the app, lists each request it serves with its status, the route of the folder's code it reached and the SQL
/// commands it issued, a command run again and again for one request marked (an N+1); a request opens at its handler, one
/// that failed where its exception was thrown.</summary>
public sealed class LiveRequestsTests
{
    private const string Program = """
        using Microsoft.EntityFrameworkCore;

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.AddDbContext<Shop>(options => options.UseSqlite("Data Source=" + Path.Combine(AppContext.BaseDirectory, "shop.db")).EnableSensitiveDataLogging());
        WebApplication app = builder.Build();
        using (IServiceScope scope = app.Services.CreateScope())
        {
            Shop made = scope.ServiceProvider.GetRequiredService<Shop>();
            made.Database.EnsureDeleted();
            made.Database.EnsureCreated();
            made.Orders.AddRange(new Order { Customer = "Ada" }, new Order { Customer = "Grace" }, new Order { Customer = "Linus" });
            made.SaveChanges();
        }

        app.MapGet("/orders/{id:int}", (int id, Shop db) => db.Orders.Single(o => o.Id == id).Customer);
        app.MapGet("/orders", (Shop db) => string.Join(", ", db.Orders.ToList().Select(o => $"{o.Customer}: {db.Lines.Count(l => l.OrderId == o.Id)}")));
        app.MapGet("/sign-in/{name}", (string name, Shop db) =>
        {
            string password = name + "-secret";
            return db.Orders.Any(o => o.Customer == password) ? "in" : "out";
        });
        app.MapGet("/boom", Fail);
        app.Run();

        static string Fail() => throw new InvalidOperationException("No orders today");

        public class Shop(DbContextOptions<Shop> options) : DbContext(options)
        {
            public DbSet<Order> Orders => Set<Order>();

            public DbSet<Line> Lines => Set<Line>();
        }

        public class Order
        {
            public int Id { get; set; }

            public string Customer { get; set; } = "";
        }

        public class Line
        {
            public int Id { get; set; }

            public int OrderId { get; set; }
        }
        """;

    [AvaloniaFact]
    public void The_requests_window_lists_the_running_apps_requests_with_their_routes_and_commands_and_opens_their_handlers()
    {
        using HeldPort port = new();
        string folder = TestFolders.Create("requests-");
        WriteShop(folder, port.Port);
        MainWindow window = Ui.OpenFolder(folder);
        try
        {
            window.ShowToolWindow(ToolWindowIds.Requests);
            Ui.WaitFor(() => window.Session.Routes.All.Count == 4, "the app's four routes, read from its code", TimeSpan.FromMinutes(3));
            port.LetGo(Path.Combine(folder, "gate"));
            Assert.True(window.SelectConfiguration("Shop: http"), $"No configuration Shop: http among {string.Join(", ", window.Configurations.Select(c => c.Name))}");
            Ui.Launched(window, window.RunAsync(), "the shop to start", TimeSpan.FromMinutes(2));
            Ui.WaitFor(() => window.BottomPanel.Run.Document.Snapshot.ToString().Contains("Now listening on:", StringComparison.Ordinal), "the shop to listen",
                TimeSpan.FromMinutes(2));
            try
            {
                Ui.WaitFor(() => window.Requests.Following.StartsWith("Following", StringComparison.Ordinal) && window.Requests.Following.Contains("process", StringComparison.Ordinal),
                    "the Requests window to follow the shop", TimeSpan.FromMinutes(1));
            }
            catch (TimeoutException)
            {
                throw new Xunit.Sdk.XunitException($"The Requests window did not follow the running shop; it says: {window.Requests.Following}");
            }

            using (HttpClient client = new())
            {
                Assert.Equal("Grace", Get(client, $"http://127.0.0.1:{port.Port}/orders/2"));
                Assert.Equal("Ada: 0, Grace: 0, Linus: 0", Get(client, $"http://127.0.0.1:{port.Port}/orders"));
                Assert.Equal("out", Get(client, $"http://127.0.0.1:{port.Port}/sign-in/ada"));
                HttpResponseMessage failed = Task.Run(() => client.GetAsync($"http://127.0.0.1:{port.Port}/boom")).GetAwaiter().GetResult();
                Assert.Equal(500, (int)failed.StatusCode);
            }
            try
            {
                Ui.WaitFor(() => window.RequestLog.Requests.Count(r => r.Status is not null) == 4 && window.RequestLog.Requests.Sum(r => r.Commands.Count) == 6,
                    "the four requests, listed as they ended, with their six commands", TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                throw new Xunit.Sdk.XunitException($"The requests were not all listed: [{string.Join("; ", window.RequestLog.Requests.Select(r => $"{r.Method} {r.Path} status {r.Status}, {r.Commands.Count} commands"))}], "
                    + $"the window says: {window.Requests.Following}");
            }

            LiveRequest one = window.RequestLog.Requests.Single(r => r.Path == "/orders/2");
            Assert.Equal(("GET", 200, "GET /orders/{id:int}"), (one.Method, one.Status, one.Route is { } route ? RouteRequests.Title(route) : null));
            LiveCommand read = Assert.Single(one.Commands);
            Assert.Contains("FROM \"Orders\"", read.Text, StringComparison.Ordinal);
            Assert.Equal(2, read.Reads);
            // The app logs its parameters' values (EF's sensitive data logging): shown, but a secret's never in the clear.
            Ui.WaitFor(() => read.Parameters is not null, "the command's parameters, from EF Core's log of it");
            Assert.True(System.Text.RegularExpressions.Regex.IsMatch(read.Parameters!, @"^@\w*id\w*='2'"), $"{read.Parameters}; the console: {string.Join(" | ", window.BottomPanel.Run.Document.Snapshot.ToString().Split('\n').Where(l => l.Contains("Parameters", StringComparison.Ordinal)))}");
            LiveCommand signIn = Assert.Single(window.RequestLog.Requests.Single(r => r.Path == "/sign-in/ada").Commands);
            Ui.WaitFor(() => signIn.Parameters is not null, "the sign-in's parameters");
            Assert.Matches(@"^@\w*password\w*='" + Passenger.App.Containers.ContainerSecrets.Masked + "'", signIn.Parameters);
            Assert.DoesNotContain("ada-secret", signIn.Parameters, StringComparison.Ordinal);
            Assert.DoesNotContain("ada-secret", window.Requests.Rows.OfType<RequestsView.CommandRow>().Select(r => r.Command.Text + r.Command.Parameters).Aggregate(string.Empty, string.Concat), StringComparison.Ordinal);
            Assert.False(one.HasRepeats, "A request that ran one command was marked as an N+1");

            // The orders' list counts each order's lines with a command of its own: the same text three times, an N+1.
            LiveRequest all = window.RequestLog.Requests.Single(r => r.Path == "/orders");
            Assert.Equal(4, all.Commands.Count);
            Assert.True(all.HasRepeats, "The request that ran one command for each order was not marked as an N+1");
            Assert.Equal([false, true, true, true], all.Commands.Select(c => c.Repeated));
            Assert.Equal(3, all.Commands[1].Runs);
            // Listed newest first, each request followed by its commands.
            Assert.Equal(["/boom", "/sign-in/ada", "/orders", "/orders/2"], window.Requests.Rows.OfType<LiveRequest>().Select(r => r.Path));

            LiveRequest boom = window.RequestLog.Requests.Single(r => r.Path == "/boom");
            Assert.Equal(500, boom.Status);
            Assert.Contains("No orders today", boom.Error, StringComparison.Ordinal);

            // A request opens at its handler; one that failed where its exception was thrown; a command's text in a tab.
            window.Requests.Open(one);
            Ui.WaitFor(() => window.EditorArea.Active?.Path.EndsWith("Program.cs", StringComparison.Ordinal) == true
                && window.EditorArea.CodeEditor.CaretPosition.Line == LineOf("app.MapGet(\"/orders/{id:int}\""), "the order's handler, opened");
            window.Requests.Open(boom);
            Ui.WaitFor(() => window.EditorArea.CodeEditor.CaretPosition.Line == LineOf("static string Fail()"), "the line that threw, opened");
            window.Requests.Open(window.Requests.Rows.OfType<RequestsView.CommandRow>().First(r => r.Request == one));
            Ui.WaitFor(() => window.EditorArea.Active?.Path.EndsWith(".sql", StringComparison.Ordinal) == true, "the command's text, opened in a tab");
            Assert.Equal($"-- {read.Parameters}\n{read.Text}", window.EditorArea.CodeEditor.Document.Snapshot.ToString());
            Assert.True(window.EditorArea.CodeEditor.IsReadOnly, "A command's text opened as something to edit");
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.Runner.IsRunning, "the shop to stop");
            window.Close();
        }
    }

    /// <summary>The shop written into a folder and restored from the run's local feed, its launch profile serving at the port
    /// given: this class's and an agent's test of the requests (McpRequestsTests).</summary>
    internal static void WriteShop(string folder, int port)
    {
        AvaloniaApp.UseFeed(folder);
        TestFolders.Write(folder, "Shop.csproj", TestProjects.Web("<ImplicitUsings>enable</ImplicitUsings>", "<Nullable>enable</Nullable>")
            .With($"  <ItemGroup>\n    <PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"{ShopApp.Version}\" />\n  </ItemGroup>\n"));
        TestFolders.Write(folder, "Properties/launchSettings.json",
            $"{{\n  \"profiles\": {{\n    \"http\": {{ \"commandName\": \"Project\", \"applicationUrl\": \"http://127.0.0.1:{port}\" }}\n  }}\n}}\n");
        TestFolders.Write(folder, "Program.cs", Program);
        Restore.Run(folder);
    }

    internal static string Get(HttpClient client, string url) => Task.Run(() => client.GetStringAsync(url)).GetAwaiter().GetResult();

    private static int LineOf(string text) => Array.FindIndex(Program.Split('\n'), l => l.Contains(text, StringComparison.Ordinal));
}
