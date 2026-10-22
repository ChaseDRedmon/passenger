using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input.Platform;
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
        // First, as Passenger's own fixes are, and beside the server's actions there, not in their place.
        Assert.Equal("Add WithReference(db) to api", window.Assist.Offered[0]);
        Assert.True(window.Assist.Offered.Count > 1, $"Only the Aspire fix was offered, without the language server's actions: {string.Join("; ", window.Assist.Offered)}");
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

    /// <summary>A file no AppHost involves pays nothing for the AppHost in the folder: a key typed in it and its pause allocate
    /// on the UI thread under 8 bytes a line more with the open AppHost 5,000 lines longer, where each pause asked the server
    /// for every AppHost's resources, hashed every open file's text and pulled every open file's diagnostics again; the
    /// AppHost's run icons stand through it, read again only as its own text changes; and the file's completion, context
    /// actions and declarations are the server's, Passenger's extension never asked.</summary>
    [AvaloniaFact]
    public void A_typing_pause_in_a_file_no_AppHost_involves_costs_nothing_of_Aspire()
    {
        const int Notes = 5000;
        MainWindow window = project.Open(project.PathOf("Host/Program.cs"));
        try
        {
            Passenger.App.Workspace.OpenDocument host = window.EditorArea.Active!;
            int[] declared = [.. host.Text.Snapshot.ToString().Split('\n').Select((text, line) => (text, line))
                .Where(l => l.text.Contains("AddProject", StringComparison.Ordinal)).Select(l => l.line)];
            Ui.WaitFor(() => host.RunMarks.Select(m => m.Line).SequenceEqual(declared), "the AppHost's run icons beside its AddProject lines");
            window.OpenFile(project.PathOf("Plain/Notes.cs"));
            Ui.Settle();
            IReadOnlyList<RunMark> marks = host.RunMarks;
            long few = UiBytesPerPause(window);
            Assert.True(ReferenceEquals(marks, host.RunMarks), "Keys in a file no AppHost involves made the AppHost's run icons again");

            // The AppHost made long, its icons read again for its own text.
            window.OpenFile(project.PathOf("Host/Program.cs"));
            CodeEditor editor = window.EditorArea.CodeEditor;
            editor.Selections = SelectionSet.Single(editor.Document.Snapshot.Length);
            Task copied = window.Clipboard!.SetTextAsync(string.Concat(Enumerable.Range(0, Notes).Select(i => $"\n// Note {i} of what the AppHost runs, which makes its text long.")));
            Ui.WaitFor(() => copied.IsCompleted, "the clipboard to take the notes");
            Ui.Press(window, Ui.Shortcut(EditorActions.Paste));
            Ui.WaitFor(() => host.Text.Snapshot.LineCount > Notes && !ReferenceEquals(marks, host.RunMarks) && host.RunMarks.Select(m => m.Line).SequenceEqual(declared),
                "the AppHost's run icons read again for its own text");
            window.OpenFile(project.PathOf("Plain/Notes.cs"));
            Ui.Settle();
            marks = host.RunMarks;
            long many = UiBytesPerPause(window);
            Assert.True(ReferenceEquals(marks, host.RunMarks), "Keys in a file no AppHost involves made the long AppHost's run icons again");
            long each = (many - few) / Notes;
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"A typing pause in a plain file allocated {many:N0} bytes on the UI thread with the open AppHost {Notes:N0} lines longer, {few:N0} without: {each} bytes a line.");
            Assert.True(each < 8, $"A typing pause in a file no AppHost involves allocated {many / 1024:N0} KB on the UI thread with the open AppHost {Notes:N0} lines longer, {few / 1024:N0} KB without: {each} bytes for each of its lines, which the pause read");

            // The server's own completion, fixes and declarations, with no word to Passenger's extension.
            editor = window.EditorArea.CodeEditor;
            Passenger.Lsp.LanguageServer server = window.CSharpService!.Session!.Server;
            Task asked = server.RefuseNext(ExtensionMessage, 1);
            try
            {
                Ui.PlaceAfter(editor, "return ");
                Ui.Run(window, AppActions.Complete);
                Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("value"), "the server's completion in a file no AppHost involves");
                Ui.Press(window, "ESCAPE");
                Ui.PlaceAfter(editor, "Twi");
                Ui.Run(window, AppActions.QuickFix);
                Ui.WaitFor(() => window.Assist.Offered.Contains("Generate method 'Twice'"), "the server's fix in a file no AppHost involves");
                Ui.Press(window, "ESCAPE");
                Ui.PlaceAfter(editor, "Twice(val");
                Ui.Run(window, AppActions.GoToDeclaration);
                Ui.WaitFor(() => editor.Document.Snapshot.GetLineText(editor.CaretPosition.Line).Contains("int value", StringComparison.Ordinal),
                    "Go to Declaration to reach the local in a file no AppHost involves");
                Assert.False(asked.IsCompleted, "Completion, Show Context Actions or Go to Declaration in a file no AppHost involves asked Passenger's extension for Aspire's help");
            }
            finally
            {
                _ = server.RefuseNext(ExtensionMessage, 0);
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>How Passenger's extension is asked about a document.</summary>
    private const string ExtensionMessage = "textDocument/_vs_dipatchExtensionMessage";

    /// <summary>What a key typed in the plain file's comment and its pause allocate on the UI thread, the least of a few.</summary>
    private static long UiBytesPerPause(MainWindow window)
    {
        CodeEditor editor = window.EditorArea.CodeEditor;
        Ui.PlaceAfter(editor, "// Typed here:");
        editor.Focus();
        Ui.Settle();
        long least = long.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            window.KeyTextInput("a");
            Ui.Settle();
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        return least;
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
            Put("Shop.slnx", "<Solution><Project Path=\"Api/Api.csproj\" /><Project Path=\"Worker/Worker.csproj\" /><Project Path=\"Host/Host.csproj\" /><Project Path=\"OtherHost/OtherHost.csproj\" /><Project Path=\"Plain/Plain.csproj\" /></Solution>");
            // A project no AppHost references, whose files Aspire's help leaves alone.
            Put("Plain/Plain.csproj", TestProjects.Library());
            Put("Plain/Notes.cs", "namespace Plain;\n\npublic static class Notes\n{\n    // Typed here:\n    public static int Count()\n    {\n        int value = 1;\n        return Twice(value);\n    }\n}\n");
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
            Prepare(byRealPath: true);
        }

        private void Put(string name, string text) => TestFolders.Write(Folder, name, text);
    }
}
