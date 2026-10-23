using System.Net.Http;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Endpoints;

namespace Passenger.Tests;

/// <summary>The Requests window: a web app Run runs, followed while the window shows with no change to the app, lists each
/// request it serves with its status and the route of the folder's code it reached, and one that failed opens where its
/// exception was thrown.</summary>
public sealed class LiveRequestsTests
{
    [AvaloniaFact]
    public void The_requests_window_lists_the_running_apps_requests_with_their_routes_and_opens_their_handlers()
    {
        using HeldPort port = new();
        string folder = TestFolders.Create("requests-");
        TestFolders.Write(folder, "Shop.csproj", TestProjects.Web("<ImplicitUsings>enable</ImplicitUsings>"));
        TestFolders.Write(folder, "Properties/launchSettings.json",
            $"{{\n  \"profiles\": {{\n    \"http\": {{ \"commandName\": \"Project\", \"applicationUrl\": \"http://127.0.0.1:{port.Port}\" }}\n  }}\n}}\n");
        TestFolders.Write(folder, "Program.cs", """
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            WebApplication app = builder.Build();

            app.MapGet("/orders/{id:int}", (int id) => $"order {id}");
            app.MapGet("/boom", Fail);
            app.Run();

            static string Fail() => throw new InvalidOperationException("No orders today");
            """);
        Restore.Run(folder);
        MainWindow window = Ui.OpenFolder(folder);
        try
        {
            window.ShowToolWindow(ToolWindowIds.Requests);
            Ui.WaitFor(() => window.Endpoints.Routes.Count == 2 || window.Session.Routes.All.Count == 2, "the app's two routes, read from its code", TimeSpan.FromMinutes(3));
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
                Assert.Equal("order 7", Task.Run(() => client.GetStringAsync($"http://127.0.0.1:{port.Port}/orders/7")).GetAwaiter().GetResult());
                HttpResponseMessage failed = Task.Run(() => client.GetAsync($"http://127.0.0.1:{port.Port}/boom")).GetAwaiter().GetResult();
                Assert.Equal(500, (int)failed.StatusCode);
            }
            try
            {
                Ui.WaitFor(() => window.RequestLog.Requests.Count(r => r.Status is not null) == 2, "both requests, listed as they ended", TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                throw new Xunit.Sdk.XunitException($"The requests were not both listed as ended: [{string.Join("; ", window.RequestLog.Requests.Select(r => $"{r.Method} {r.Path} trace '{r.Trace}' status {r.Status}"))}], "
                    + $"the window says: {window.Requests.Following}");
            }
            LiveRequest order = window.RequestLog.Requests.Single(r => r.Path == "/orders/7");
            Assert.Equal(("GET", 200, "GET /orders/{id:int}"), (order.Method, order.Status, order.Route is { } route ? RouteRequests.Title(route) : null));
            LiveRequest boom = window.RequestLog.Requests.Single(r => r.Path == "/boom");
            Assert.Equal(500, boom.Status);
            Assert.Contains("No orders today", boom.Error, StringComparison.Ordinal);

            // A request opens at its handler; one that failed where its exception was thrown.
            window.Requests.Open(order);
            Ui.WaitFor(() => window.EditorArea.Active?.Path.EndsWith("Program.cs", StringComparison.Ordinal) == true
                && window.EditorArea.CodeEditor.CaretPosition.Line == 3, "the order's handler, opened");
            window.Requests.Open(boom);
            Ui.WaitFor(() => window.EditorArea.CodeEditor.CaretPosition.Line == 7, "the line that threw, opened");
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.Runner.IsRunning, "the shop to stop");
            window.Close();
        }
    }
}
