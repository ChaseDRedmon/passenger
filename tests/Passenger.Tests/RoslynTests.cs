using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>The Roslyn language server Passenger ships, started for a real project on disk and asked
/// what an editor asks it. Slow (the server and the project load take seconds), so each test shares
/// one session.</summary>
public sealed class RoslynTests : IClassFixture<RoslynTests.ProjectSession>
{
    private readonly ProjectSession project;

    public RoslynTests(ProjectSession project) => this.project = project;

    [Fact]
    public async Task Completion_after_a_dot_offers_the_types_members()
    {
        (LspDocument document, TextDocument text) = project.Open("Program.cs", "class Program\n{\n    static void Main()\n    {\n        System.Console.\n    }\n}\n");
        int offset = text.Snapshot.ToString().IndexOf("Console.", StringComparison.Ordinal) + "Console.".Length;
        (CompletionList? list, _) = await document.CompletionAsync(offset, ".", TestContext.Current.CancellationToken);
        Assert.NotNull(list);
        Assert.Contains(list.Items, item => item.Label == "WriteLine");
        document.Dispose();
    }

    [Fact]
    public async Task A_type_error_typed_into_a_file_comes_back_as_a_diagnostic_on_that_line()
    {
        (LspDocument document, TextDocument text) = project.Open("Program.cs", "class Program\n{\n    static void Main()\n    {\n    }\n}\n");
        int at = text.Snapshot.ToString().IndexOf("    }", StringComparison.Ordinal);
        text.Edit([new TextEdit(at, 0, "        int number = \"text\";\n")], SelectionSet.Single(0), SelectionSet.Single(0));
        IReadOnlyList<Diagnostic> found = [];
        for (int attempt = 0; attempt < 40 && !found.Any(d => Code(d) == "CS0029"); attempt++)
        {
            (found, _) = await document.DiagnosticsAsync(TestContext.Current.CancellationToken);
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }
        Diagnostic error = Assert.Single(found, d => Code(d) == "CS0029");
        Assert.Equal(4, error.Range.Start.Line);
        document.Dispose();
    }

    [Fact]
    public async Task Go_to_definition_finds_a_method_in_another_file_of_the_project()
    {
        (LspDocument document, TextDocument text) = project.Open("Program.cs", "class Program\n{\n    static void Main()\n    {\n        Helper.Twice(2);\n    }\n}\n");
        int offset = text.Snapshot.ToString().IndexOf("Twice", StringComparison.Ordinal) + 1;
        IReadOnlyList<LspLocation> found = await document.DefinitionAsync(offset, TestContext.Current.CancellationToken);
        LspLocation location = Assert.Single(found);
        Assert.EndsWith("/Helper.cs", location.Uri, StringComparison.Ordinal);
        Assert.Equal(2, location.Range.Start.Line);
        document.Dispose();
    }

    [Fact]
    public async Task Hover_over_a_method_describes_its_signature()
    {
        (LspDocument document, TextDocument text) = project.Open("Program.cs", "class Program\n{\n    static void Main()\n    {\n        Helper.Twice(2);\n    }\n}\n");
        int offset = text.Snapshot.ToString().IndexOf("Twice", StringComparison.Ordinal) + 1;
        Hover? hover = await document.HoverAsync(offset, TestContext.Current.CancellationToken);
        Assert.NotNull(hover);
        Assert.Contains("int Helper.Twice(int value)", Shapes.Text(hover.Contents), StringComparison.Ordinal);
        document.Dispose();
    }

    private static string? Code(Diagnostic diagnostic) => diagnostic.Code?.ValueKind == System.Text.Json.JsonValueKind.String ? diagnostic.Code.Value.GetString() : diagnostic.Code?.ToString();

    /// <summary>A small console project in a temporary folder, restored, with Roslyn's server open on
    /// it and its projects loaded.</summary>
    public sealed class ProjectSession : IAsyncLifetime
    {
        private readonly string folder = TestFolders.Create("roslyn-");

        public RoslynSession Session { get; private set; } = null!;

        public async ValueTask InitializeAsync()
        {
            File.WriteAllText(Path.Combine(folder, "Sample.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
            File.WriteAllText(Path.Combine(folder, "Program.cs"), "class Program\n{\n    static void Main() { }\n}\n");
            File.WriteAllText(Path.Combine(folder, "Helper.cs"), "static class Helper\n{\n    public static int Twice(int value) => value * 2;\n}\n");
            Assert.Equal(0, (await Restore.RunAsync(folder)).ExitCode);

            TaskCompletionSource loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Session = await RoslynSession.StartAsync(folder, null, [Path.Combine(folder, "Sample.csproj")], Path.Combine(folder, ".logs"));
            Session.Loaded += (_, _) => loaded.TrySetResult();
            if (!Session.IsLoaded)
            {
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(90));
            }
        }

        public (LspDocument Document, TextDocument Text) Open(string name, string text)
        {
            TextDocument document = new(text);
            return (Session.Open(Path.Combine(folder, name), document), document);
        }

        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            TestFolders.Delete(folder);
        }
    }
}
