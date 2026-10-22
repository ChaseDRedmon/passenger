using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;
using Passenger.Roslyn.Extension;
using Passenger.App.Run;

namespace Passenger.Tests;

public sealed class AspireCodeHelpTests : IClassFixture<AspireCodeHelpTests.Project>
{
    private readonly Project project;

    public AspireCodeHelpTests(Project project)
    {
        this.project = project;
        project.Reset();
    }

    [AvaloniaFact]
    public void AddProject_navigates_to_the_real_project_but_a_resource_identifier_to_its_declaration()
    {
        MainWindow window = project.Open(project.PathOf("Host/Program.cs"));
        Ui.PlaceAfter(window.EditorArea.CodeEditor, "AddProject<Projects.Ap");
        Ui.Run(window, AppActions.GoToDeclaration);
        Ui.WaitFor(() => window.EditorArea.Active?.Name == "Api.csproj", "AddProject to open Api.csproj, not generated metadata");
        Assert.True(PathNames.Same(PathNames.Canonical(project.PathOf("Api/Api.csproj")), window.EditorArea.Active!.Path));

        window.OpenFile(project.PathOf("Host/Program.cs"));
        Ui.Settle();
        Ui.PlaceAfter(window.EditorArea.CodeEditor, "WithReference(connec");
        Ui.Run(window, AppActions.GoToDeclaration);
        Ui.Settle();
        Assert.Equal("Program.cs", window.EditorArea.Active!.Name);
        CodeEditor editor = window.EditorArea.CodeEditor;
        Assert.Contains("var connected =", editor.Document.Snapshot.GetLineText(editor.CaretPosition.Line), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Connection_completion_offers_only_connected_resources_and_a_connection_warning_offers_a_native_reference_fix_undo_takes_back()
    {
        string host = File.ReadAllText(project.PathOf("Host/Program.cs"));
        string service = File.ReadAllText(project.PathOf("Api/Program.cs"));
        MainWindow window = project.Open(project.PathOf("Api/Program.cs"));
        window.Confirm = (_, _, _) => Task.FromResult(true);
        WaitForMissing(window, "api", "db");

        // Completion of a connection's name offers only the resources the AppHost connects the service to.
        Ui.PlaceAfter(window.EditorArea.CodeEditor, "GetConnectionString(\"");
        Ui.Run(window, AppActions.Complete);
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("connected"), "the connected database name to be offered");
        Assert.DoesNotContain("db", window.Assist.CompletionLabels);
        Assert.DoesNotContain("worker", window.Assist.CompletionLabels);
        Ui.Press(window, "ESCAPE");

        Ui.PlaceAfter(window.EditorArea.CodeEditor, "GetConnectionString(\"d");
        Ui.Run(window, AppActions.QuickFix);
        Ui.WaitFor(() => window.Assist.Offered.Contains("Add WithReference(db) to api"), "the native reference fix to be offered");
        // Another file of the solution changed while the fix is offered, which the server says by asking for diagnostics again.
        TaskCompletionSource refreshed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler heard = (_, _) => refreshed.TrySetResult();
        Passenger.Lsp.LanguageServer server = window.CSharpService!.Session!.Server;
        server.DiagnosticsRefreshRequested += heard;
        File.WriteAllText(project.PathOf("OtherHost/Program.cs"), "System.Console.WriteLine(\"Still no graph\");\n");
        Ui.WaitFor(() => refreshed.Task.IsCompleted, "the language server to ask for diagnostics again once another file changed");
        server.DiagnosticsRefreshRequested -= heard;
        Choose(window, "Add WithReference(db) to api");
        // Its refusals all name the Aspire reference, which its success does not.
        Ui.WaitFor(() => File.ReadAllText(project.PathOf("Host/Program.cs")).Contains("api.WithReference(db);", StringComparison.Ordinal)
            || window.Status?.Contains("Aspire reference", StringComparison.Ordinal) == true, "the native fix to add the missing AppHost edge, or say why not");
        Assert.True(File.ReadAllText(project.PathOf("Host/Program.cs")).Contains("api.WithReference(db);", StringComparison.Ordinal),
            $"The native fix chosen after another file changed added nothing to the AppHost: {window.Status}");
        Ui.WaitFor(() => !HasMissing(window), "the service warning to clear after adding its reference");
        Assert.Equal(service, window.EditorArea.CodeEditor.Document.Snapshot.ToString());
        Ui.Run(window, AppActions.FocusExplorer);
        Ui.Run(window, EditorActions.Undo);
        Ui.WaitFor(() => File.ReadAllText(project.PathOf("Host/Program.cs")) == host, "global undo to restore the AppHost");
        Assert.Equal(service, File.ReadAllText(project.PathOf("Api/Program.cs")));
        window.OpenFile(project.PathOf("Api/Program.cs"));
        Ui.Settle();
        Assert.Equal(service, window.EditorArea.CodeEditor.Document.Snapshot.ToString());
        WaitForMissing(window, "api", "db");
        window.Close();
    }

    [AvaloniaFact]
    public void A_direct_HttpClient_address_has_a_warning_but_a_stale_fix_does_not_overwrite_the_AppHost()
    {
        MainWindow window = project.Open(project.PathOf("Worker/Program.cs"));
        WaitForMissing(window, "worker", "api");
        Ui.PlaceAfter(window.EditorArea.CodeEditor, "https+http://ap");
        Ui.Run(window, AppActions.QuickFix);
        Ui.WaitFor(() => window.Assist.Offered.Contains("Add WithReference(api) to worker"), "the service-discovery reference fix");
        string changed = File.ReadAllText(project.PathOf("Host/Program.cs")).Replace("builder.Build().Run();",
            "worker.WithReference(api);\nbuilder.Build().Run();\n// Changed after the fix was offered.\n", StringComparison.Ordinal);
        File.WriteAllText(project.PathOf("Host/Program.cs"), changed);
        Ui.WaitFor(() => !HasMissing(window), "the editor to observe the changed AppHost before accepting the stale offer");
        Choose(window, "Add WithReference(api) to worker");
        Ui.Settle();
        Assert.Equal(changed, File.ReadAllText(project.PathOf("Host/Program.cs")));
        Assert.Contains("changed", window.Status!, StringComparison.OrdinalIgnoreCase);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_ambiguous_AppHost_or_unknown_graph_mutation_does_not_accuse_the_service(bool mutation)
    {
        if (mutation)
        {
            project.Write("Host/Program.cs", Project.Host.Replace("builder.Build().Run();", "builder.Services.Clear();\nbuilder.Build().Run();", StringComparison.Ordinal));
        }
        else
        {
            project.Write("OtherHost/Program.cs", Project.Host);
        }
        MainWindow window = project.Open(project.PathOf("Api/Program.cs"));
        Ui.Settle();
        Assert.False(HasMissing(window), "An unproven graph must not produce a missing-reference warning");
        Assert.DoesNotContain(window.EditorArea.Active!.Markers, m => m.Message.Contains("does not reference", StringComparison.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public void Configuration_resources_share_the_semantic_graph_for_generic_and_reordered_named_constant_AddProject_calls()
    {
        project.Write("Host/Program.cs", Project.Host.Replace("var worker = builder.AddProject<Projects.Worker>(\"worker\");", """
            const string WorkerName = "worker";
            const string WorkerProject = "../Worker/Worker.csproj";
            builder.AddProject(projectPath: WorkerProject, name: WorkerName).WithReference(api);
            """, StringComparison.Ordinal));
        MainWindow window = project.Open(project.PathOf("Host/Program.cs"));
        try
        {
            Ui.WaitFor(() => window.Configurations.Any(configuration => PathNames.Same(configuration.Project, PathNames.Canonical(project.PathOf("Host/Host.csproj")))),
                "the semantic AppHost configuration");
            RunConfiguration host = window.Configurations.First(configuration => PathNames.Same(configuration.Project, PathNames.Canonical(project.PathOf("Host/Host.csproj"))));
            Assert.True(window.SelectConfiguration(host.Name));
            Ui.Run(window, AppActions.FindAction);
            window.KeyTextInput("Edit Configurations");
            Ui.Press(window, "ENTER");
            RunConfigurationsDialog dialog = Assert.IsType<RunConfigurationsDialog>(window.RunConfigurationsDialog);
            Ui.WaitFor(() => ResourceRows(dialog).Length == 2, "generic and unassigned named-argument project resources");
            Assert.Equal(["api", "worker"], ResourceRows(dialog).Select(row => row.Children.OfType<TextBlock>().First().Text).Order(StringComparer.Ordinal));
            Assert.Contains(ResourceRows(dialog), row => row.Children.OfType<TextBlock>().Any(text => text.Text == "Api"));
            Assert.Contains(ResourceRows(dialog), row => row.Children.OfType<TextBlock>().Any(text => text.Text == "Worker"));
            Ui.Press(dialog, "ESCAPE");
        }
        finally
        {
            window.Close();
        }

        static Grid[] ResourceRows(RunConfigurationsDialog dialog) => dialog.GetVisualDescendants().OfType<Grid>()
            .Where(row => row.Children.OfType<ComboBox>().Any(combo => combo.ItemsSource is string[] items && items.SequenceEqual(["Run", "Debug"])))
            .ToArray();
    }

    private static bool HasMissing(MainWindow window) => window.CSharpService!.For(window.EditorArea.Active!)!.Diagnostics
        .Any(d => d.Source == "Passenger.Aspire");

    private static void WaitForMissing(MainWindow window, string service, string resource)
    {
        try
        {
            Ui.WaitFor(() => HasMissing(window), $"the missing {service} to {resource} reference warning");
        }
        catch (TimeoutException timeout)
        {
            Passenger.App.CSharp.CSharpDocument current = window.CSharpService!.For(window.EditorArea.Active!)!;
            string evidence;
            try
            {
                AspireHelp help = Ui.Await(current.Lsp!.ExtensionAsync<AspireHelp>(Handlers.AspireCodeHelp, new AspireHelpRequest(null, Explain: true)),
                    "semantic Aspire failure evidence", TimeSpan.FromSeconds(10));
                evidence = help.Evidence ?? "The semantic handler returned no evidence";
            }
            catch (Exception failed)
            {
                evidence = $"Semantic evidence request failed: {failed}";
            }
            string diagnostics = string.Join("\n", current.Diagnostics.Take(10).Select(d => $"{d.Source}: {d.Message}"));
            throw new TimeoutException($"{timeout.Message}\nStatus: {window.Status}\nDiagnostics:\n{diagnostics}\nAspire semantic evidence:\n{evidence}", timeout);
        }
        Assert.Contains(window.EditorArea.Active!.Markers, m => m.Kind == MarkerKind.Warning
            && m.Message.Contains($"'{service}' does not reference '{resource}'", StringComparison.Ordinal));
    }

    private static void Choose(MainWindow window, string label)
    {
        Ui.WaitFor(() => window.Assist.Offered.Contains(label), $"the native '{label}' action");
        int index = window.Assist.Offered.ToList().IndexOf(label);
        for (int i = 0; i < index; i++)
        {
            Ui.Press(window, "DOWN");
        }
        Ui.Press(window, "ENTER");
    }

    public sealed class Project : SharedProject
    {
        internal const string Host = """
            using Aspire.Hosting;
            var builder = DistributedApplication.CreateBuilder(args);
            var db = builder.AddConnectionString("db");
            var connected = builder.AddConnectionString("connected");
            var api = builder.AddProject<Projects.Api>("api").WithReference(connected);
            var worker = builder.AddProject<Projects.Worker>("worker");
            builder.Build().Run();
            """;

        public Project() : base("aspire-help-")
        {
            Put("Shop.slnx", "<Solution><Project Path=\"Api/Api.csproj\" /><Project Path=\"Worker/Worker.csproj\" /><Project Path=\"Host/Host.csproj\" /><Project Path=\"OtherHost/OtherHost.csproj\" /></Solution>");
            foreach (string name in new[] { "Api", "Worker" })
            {
                Put($"{name}/{name}.csproj", TestProjects.Web("<ImplicitUsings>enable</ImplicitUsings>"));
            }
            Put("Api/Program.cs", "var builder = WebApplication.CreateBuilder(args);\nvar missing = builder.Configuration.GetConnectionString(\"db\");\nvar known = builder.Configuration.GetConnectionString(\"connected\");\nbuilder.Build().Run();\n");
            Put("Worker/Program.cs", "var builder = WebApplication.CreateBuilder(args);\nvar client = new HttpClient();\nclient.BaseAddress = new Uri(\"https+http://api\");\nbuilder.Build().Run();\n");
            foreach (string name in new[] { "Host", "OtherHost" })
            {
                Put($"{name}/{name}.csproj", $"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <Sdk Name="Aspire.AppHost.Sdk" Version="13.6.2" />
                      <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>{TestProjects.Framework}</TargetFramework><ImplicitUsings>enable</ImplicitUsings><IsAspireHost>true</IsAspireHost><AspireUseCliBundle>false</AspireUseCliBundle></PropertyGroup>
                      <ItemGroup><PackageReference Include="Aspire.Hosting.AppHost" Version="13.6.2" /><ProjectReference Include="../Api/Api.csproj" /><ProjectReference Include="../Worker/Worker.csproj" /></ItemGroup>
                    </Project>
                    """);
            }
            Put("Host/Program.cs", Host);
            Put("OtherHost/Program.cs", "System.Console.WriteLine(\"No graph\");\n");
            // The AppHosts' graph is the starter's, in the package cache the test project's restore filled: never nuget.org.
            AspireApp.UseFeed(Folder);
            Prepare();
        }

        private void Put(string name, string text) => TestFolders.Write(Folder, name, text);
    }
}
