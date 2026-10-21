using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Explorer;
using Passenger.App.Settings;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Dockerfiles and compose files through the window, on a solution of an ASP.NET Core app using Npgsql, a worker
/// and a library: a Dockerfile completes and is checked by the Dockerfile server, a compose file by the YAML server with
/// the Compose specification's schema, and Go to Declaration in a compose file goes where Passenger finds each name leads;
/// Add > Docker Support writes a project's Dockerfile and the context's .dockerignore, or sets the SDK's container
/// properties, and Add > Docker Compose the solution's compose.yaml, each one command Undo takes back. The servers' tests
/// skip, saying why, without a Node.js that runs them; the folder is never restored (its nuget.config names no feed that
/// is there), so nothing reaches nuget.org.</summary>
public sealed class DockerFilesTests : IClassFixture<DockerFilesTests.Shop>
{
    private readonly Shop app;

    public DockerFilesTests(Shop app) => this.app = app;

    [AvaloniaFact]
    public void A_dockerfile_completes_an_instruction_and_marks_one_docker_does_not_have()
    {
        Shop.SkipWithoutNode();
        MainWindow window = app.Open("ops/api.Dockerfile");
        try
        {
            CodeEditor editor = window.EditorArea.CodeEditor;
            OpenDocument document = window.EditorArea.Active!;
            Assert.Equal("Dockerfile", document.Language.Name);

            // A FROM's image goes to its registry's page; a stage it names, to the stage.
            PlaceAfter(editor, "FROM mcr.microsoft.com/dotnet/asp");
            Ui.Press(window, Ui.Shortcut(AppActions.GoToDeclaration));
            Ui.WaitFor(() => window.Browsed.Count > 0, "the aspnet image's page opened");
            Assert.Equal("https://mcr.microsoft.com/artifact/mar/dotnet/aspnet", window.Browsed[^1].AbsoluteUri);
            PlaceAfter(editor, "FROM ba");
            Ui.Press(window, Ui.Shortcut(AppActions.GoToDeclaration));
            Ui.WaitFor(() => editor.CaretPosition == new TextPosition(0, 45), $"the caret at the base stage's name, not {editor.CaretPosition}");
            Assert.Single(window.Browsed);

            editor.Selections = SelectionSet.Single(editor.Document.Snapshot.Length);
            TypeEach(window, "WORK");
            Ui.WaitFor(() => window.Assist.CompletionLabels.Any(l => l.StartsWith("WORKDIR", StringComparison.Ordinal)), "the Dockerfile server's instructions as one is typed");
            Ui.Press(window, "ESCAPE");

            editor.ApplyEdits([new TextEdit(0, editor.Document.Snapshot.Length, Shop.OpsDockerfile + "FROMM scratch\n")]);
            Ui.WaitFor(() => document.Markers.Any(m => m.Kind == MarkerKind.Error), "the error under the instruction Docker has not");
            Marker error = document.Markers.First(m => m.Kind == MarkerKind.Error);
            Assert.Equal("FROMM", editor.Document.Snapshot.GetText(error.Range.Start, error.Range.Length));
            Assert.Contains("Unknown instruction: FROMM", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
            app.Reset();
        }
    }

    /// <summary>Settings' Complete image tags from Docker Hub, on at first: after FROM image: the Dockerfile server offers the
    /// image's tags, asked of Docker Hub (a stand-in on loopback here); turned off on the Docker page, beside Remote JSON
    /// Schemas, the server starts again and asks Docker Hub nothing, offering no tags.</summary>
    [AvaloniaFact]
    public void A_dockerfile_completes_an_images_tags_from_docker_hub_and_asks_it_nothing_once_settings_turn_that_off()
    {
        Shop.SkipWithoutNode();
        using StandInHub hub = new();
        MainWindow window = app.Open("ops/api.Dockerfile", hub.Place);
        try
        {
            CodeEditor editor = window.EditorArea.CodeEditor;
            Assert.True(window.Settings.DockerHubTags, "Complete image tags from Docker Hub is off in a fresh Passenger");
            editor.ApplyEdits([new TextEdit(0, editor.Document.Snapshot.Length, "FROM postgres:1\n" + Shop.OpsDockerfile)]);
            PlaceAfter(editor, "FROM postgres:1");
            Ui.Run(window, AppActions.Complete);
            Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("17-alpine"), $"postgres's tags from Docker Hub (asked: {string.Join(", ", hub.Asked)})");
            Assert.Equal(["16", "17", "17-alpine"], window.Assist.CompletionLabels.Order(StringComparer.Ordinal));
            Assert.Equal([TokenAsked, TagsAsked], hub.Asked);
            Ui.Press(window, "ESCAPE");

            SettingsDialog dialog = DialogButtonsTests.Check(window.ShowSettings());
            dialog.Query = "docker hub";
            Ui.Settle();
            Assert.Equal([SettingsDialog.DockerPage], dialog.Pages);
            Assert.Equal([HubTags], dialog.Shown);
            CheckBox asks = Editor<CheckBox>(dialog, HubTags);
            Assert.True(asks.IsChecked == true, "Settings shows Complete image tags from Docker Hub off while it is on");
            Click(dialog, asks);
            dialog.Ok();
            Ui.Settle();
            Assert.False(window.Settings.DockerHubTags, "Clearing Complete image tags from Docker Hub left it on");

            // The server, started again, answers (an instruction's completion) and offers no tags, asking Docker Hub nothing.
            editor.Selections = SelectionSet.Single(editor.Document.Snapshot.Length);
            TypeEach(window, "WORK");
            Ui.WaitFor(() => window.Assist.CompletionLabels.Any(l => l.StartsWith("WORKDIR", StringComparison.Ordinal)), "the Dockerfile server started again answering");
            Ui.Press(window, "ESCAPE");
            PlaceAfter(editor, "FROM postgres:1");
            Ui.Run(window, AppActions.Complete);
            Ui.Settle();
            Assert.True(window.Assist.CompletionLabels.Count == 0,
                $"With Complete image tags from Docker Hub off, FROM postgres:1 still offered [{string.Join(", ", window.Assist.CompletionLabels)}]");
            Assert.True(hub.Asked.Count == 2, $"Docker Hub was asked with Complete image tags from Docker Hub off: {string.Join(", ", hub.Asked.Skip(2))}");
        }
        finally
        {
            window.Close();
            app.Reset();
        }
    }

    private const string HubTags = "Complete image tags from Docker Hub";
    private const string TokenAsked = "GET /auth.docker.io/token?service=registry.docker.io&scope=repository:library/postgres:pull";
    private const string TagsAsked = "GET /registry-1.docker.io/v2/library/postgres/tags/list";

    [AvaloniaFact]
    public void A_compose_file_completes_a_services_keys_by_the_compose_schema_marks_a_key_it_does_not_have_and_knows_composes_tags()
    {
        Shop.SkipWithoutNode(yaml: true);
        MainWindow window = app.Open("ops/compose.yaml");
        try
        {
            CodeEditor editor = window.EditorArea.CodeEditor;
            OpenDocument document = window.EditorArea.Active!;
            PlaceAfter(editor, "    image: postgres:17");
            Ui.Press(window, "ENTER");
            TypeEach(window, "heal");
            Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("healthcheck"), "the Compose schema's keys of a service as one is typed");
            Ui.Press(window, "ESCAPE");

            editor.ApplyEdits([new TextEdit(0, editor.Document.Snapshot.Length, Shop.ComposeFile.Replace("    ports: !reset []", "    bogus: 1\n    ports: !reset []", StringComparison.Ordinal))]);
            Ui.WaitFor(() => document.Markers.Any(m => m.Kind == MarkerKind.Error && m.Message.Contains("bogus", StringComparison.Ordinal)),
                "the error under the key a service does not have");
            Marker error = document.Markers.First(m => m.Kind == MarkerKind.Error && m.Message.Contains("bogus", StringComparison.Ordinal));
            Assert.Equal("bogus", editor.Document.Snapshot.GetText(error.Range.Start, error.Range.Length));
            Assert.Contains("Property bogus is not allowed", error.Message, StringComparison.Ordinal);
            // Compose's own tags are no errors: !reset's line is unmarked.
            int reset = editor.Document.Snapshot.ToString().IndexOf("!reset", StringComparison.Ordinal);
            Assert.DoesNotContain(document.Markers, m => m.Kind is MarkerKind.Error or MarkerKind.Warning
                && editor.Document.Snapshot.GetLineFromOffset(m.Range.Start) == editor.Document.Snapshot.GetLineFromOffset(reset));
        }
        finally
        {
            window.Close();
            app.Reset();
        }
    }

    [AvaloniaFact]
    public void Go_to_declaration_in_a_compose_file_leads_to_the_dockerfile_and_project_services_declarations_variables_env_files_and_registry_pages()
    {
        Shop.SkipWithoutNode(yaml: true);
        MainWindow window = app.Open("ops/compose.yaml");
        try
        {
            string compose = app.PathOf("ops/compose.yaml");
            Ui.WaitFor(() => window.Session.Workspace?.Projects.Any(p => p.Name == "Api") == true, "the solution's projects");

            // The Structure window shows the YAML server's outline: the services and what declares them.
            Ui.Run(window, AppActions.ShowStructure);
            Ui.WaitFor(() => window.StructureView.Rows.Any(r => r.Name == "depends_on"), "the compose file's outline in the Structure window");
            Assert.Equal(["services", "api", "build"], window.StructureView.Rows.Take(3).Select(r => r.Name));

            // A build: its Dockerfile, and the project the Dockerfile's entry point runs, to choose from.
            GoFrom(window, compose, "dockerfile: ops/api.Dock");
            Ui.WaitFor(() => window.Assist.Offered.Count == 2, "the build's Dockerfile and project offered");
            Assert.Equal(["api.Dockerfile", "Api.csproj"], window.Assist.Offered);
            Ui.Press(window, "ESCAPE");

            // A service a service names, however it names it.
            Arrives(window, compose, "depends_on:\n      d", compose, new TextPosition(19, 2));
            Arrives(window, compose, "links: [ca", compose, new TextPosition(25, 2));
            Arrives(window, compose, "volumes_from:\n      - cac", compose, new TextPosition(25, 2));
            Arrives(window, compose, "network_mode: service:d", compose, new TextPosition(19, 2));
            Arrives(window, compose, "service: ba", app.PathOf("ops/common.yaml"), new TextPosition(1, 2));

            // A named volume, network and secret: their declarations at the file's foot.
            Arrives(window, compose, "      - da", compose, new TextPosition(31, 2));
            Arrives(window, compose, "      - back", compose, new TextPosition(33, 2));
            Arrives(window, compose, "      - tok", compose, new TextPosition(35, 2));

            // A variable: its line in the .env beside the file; an env_file: its file.
            Arrives(window, compose, "$DB_PASS", app.PathOf("ops/.env"), new TextPosition(1, 0));
            Arrives(window, compose, "${REGIS", app.PathOf("ops/.env"), new TextPosition(0, 0));
            Arrives(window, compose, "      - api.e", app.PathOf("ops/api.env"), new TextPosition(0, 0));

            // An image: its page on Docker Hub, in the browser.
            GoFrom(window, compose, "image: postgres:1");
            Ui.WaitFor(() => window.Browsed.Count > 0, "postgres's page opened");
            Assert.Equal("https://hub.docker.com/_/postgres", window.Browsed[^1].AbsoluteUri);
        }
        finally
        {
            window.Close();
            app.Reset();
        }
    }

    [AvaloniaFact]
    public void Add_docker_support_writes_the_projects_dockerfile_and_the_contexts_dockerignore_and_undo_takes_both_away()
    {
        MainWindow window = app.Open(null);
        try
        {
            AwaitEvaluated(window);
            RightClick(window, Row(window, NodeKind.Project, "Api"));
            ChooseFromMenu(window, "Add", "Docker Support...");
            DockerSupportDialog dialog = DialogButtonsTests.Check(Assert.Single(window.OwnedWindows.OfType<DockerSupportDialog>()));
            Assert.Equal(DockerSupportKind.Dockerfile, dialog.Chosen);
            dialog.Accept();
            Ui.Settle();

            string dockerfile = app.PathOf("Api/Dockerfile");
            string ignore = app.PathOf(".dockerignore");
            Assert.Equal(Shop.Dockerfile, File.ReadAllText(dockerfile));
            Assert.Equal(DockerFiles.Ignore, File.ReadAllText(ignore));
            Assert.Equal(dockerfile, window.EditorArea.Active?.Path);

            // The .dockerignore comments with #, as a .env does.
            window.OpenFile(ignore);
            Ui.Settle();
            Assert.Equal(("Ignore", "#"), (window.EditorArea.Active!.Language.Name, window.EditorArea.Active.Language.LineComment));
            Assert.Equal("#", Language.ForPath(app.PathOf("ops/.env")).LineComment);

            // Undo, from the Solution view, takes both away together, asked first.
            List<string> asked = Answer(window);
            window.ExplorerView.Reveal(app.PathOf("Api/Api.csproj"));
            Ui.Settle();
            Choose(window, Row(window, NodeKind.Project, "Api"));
            Ui.Run(window, EditorActions.Undo);
            Assert.Equal(["Undo"], asked);
            Assert.False(File.Exists(dockerfile), $"Undo left the Dockerfile: {window.Status}");
            Assert.False(File.Exists(ignore), $"Undo left the .dockerignore: {window.Status}");
        }
        finally
        {
            window.Close();
            app.Reset();
        }
    }

    [AvaloniaFact]
    public void Add_docker_support_by_the_sdk_sets_a_workers_container_properties_and_leaves_the_rest_of_its_project_file_as_it_was()
    {
        MainWindow window = app.Open(null);
        try
        {
            AwaitEvaluated(window);
            string worker = app.PathOf("Worker/Worker.csproj");
            RightClick(window, Row(window, NodeKind.Project, "Worker"));
            ChooseFromMenu(window, "Add", "Docker Support...");
            DockerSupportDialog dialog = DialogButtonsTests.Check(Assert.Single(window.OwnedWindows.OfType<DockerSupportDialog>()));
            dialog.Chosen = DockerSupportKind.Sdk;
            dialog.Accept();
            Ui.Settle();

            Assert.Equal(Shop.Worker.Replace("    <Nullable>enable</Nullable>\r\n", "    <Nullable>enable</Nullable>\r\n"
                + "    <EnableSdkContainerSupport>true</EnableSdkContainerSupport>\r\n    <ContainerRepository>worker</ContainerRepository>\r\n", StringComparison.Ordinal),
                File.ReadAllText(worker));
            Assert.False(File.Exists(app.PathOf("Worker/Dockerfile")), "The SDK's container support wrote a Dockerfile too");

            // Set again, the properties are where they were: nothing is added twice.
            Assert.Null(window.AddDockerSupport(worker, DockerSupportKind.Sdk));
            Assert.Equal(1, File.ReadAllText(worker).Split("<ContainerRepository>").Length - 1);
        }
        finally
        {
            window.Close();
            app.Reset();
        }
    }

    [AvaloniaFact]
    public void Add_docker_compose_runs_an_app_using_npgsql_beside_postgres_it_waits_for_with_its_connection_string_and_lists_the_file_in_the_solution()
    {
        MainWindow window = app.Open(null);
        try
        {
            AwaitEvaluated(window);
            RightClick(window, Row(window, NodeKind.Solution, "Shop"));
            ChooseFromMenu(window, "Add", "Docker Compose...");
            DockerComposeDialog dialog = DialogButtonsTests.Check(Assert.Single(window.OwnedWindows.OfType<DockerComposeDialog>()));
            Assert.Equal([("Api", "no Dockerfile: Docker Support adds one"), ("Worker", "no Dockerfile: Docker Support adds one")],
                dialog.Projects.Select(p => (p.Name, p.Detail)));
            ComposeChoice postgres = Assert.Single(dialog.Services);
            Assert.Equal(("postgres", "postgres:17, for Api"), (postgres.Name, postgres.Detail));
            dialog.Projects.Single(p => p.Name == "Worker").IsChosen = false;
            dialog.Close(true);
            Ui.Settle();

            string compose = app.PathOf("compose.yaml");
            Assert.Equal(Shop.Compose, File.ReadAllText(compose));
            Assert.Equal(Shop.Dockerfile, File.ReadAllText(app.PathOf("Api/Dockerfile")));
            Assert.Contains("<Folder Name=\"/Solution Items/\">\r\n    <File Path=\"compose.yaml\" />\r\n  </Folder>", File.ReadAllText(app.PathOf("Shop.slnx")), StringComparison.Ordinal);
            Assert.Equal(compose, window.EditorArea.Active?.Path);

            // Undo takes the compose file, the Dockerfile and the solution's entry back.
            List<string> asked = Answer(window);
            Choose(window, Row(window, NodeKind.Solution, "Shop"));
            Ui.Run(window, EditorActions.Undo);
            Assert.Equal(["Undo"], asked);
            Assert.False(File.Exists(compose), $"Undo left compose.yaml: {window.Status}");
            Assert.False(File.Exists(app.PathOf("Api/Dockerfile")), "Undo left Api's Dockerfile");
            Assert.Equal(Shop.Solution, File.ReadAllText(app.PathOf("Shop.slnx")));
        }
        finally
        {
            window.Close();
            app.Reset();
        }
    }

    /// <summary>Go to Declaration from just after a stretch of the file, which is opened (again) first.</summary>
    private static void GoFrom(MainWindow window, string file, string after)
    {
        window.OpenFile(file);
        Ui.Settle();
        PlaceAfter(window.EditorArea.CodeEditor, after);
        Ui.Press(window, Ui.Shortcut(AppActions.GoToDeclaration));
    }

    private static void Arrives(MainWindow window, string file, string after, string at, TextPosition position)
    {
        GoFrom(window, file, after);
        Ui.WaitFor(() => window.EditorArea.Active?.Path == at && window.EditorArea.CodeEditor.CaretPosition == position,
            $"Go to Declaration after '{after}' to go to {Path.GetFileName(at)} {position.Line + 1}:{position.Column + 1}; it is at "
            + $"{Path.GetFileName(window.EditorArea.Active?.Path)} {window.EditorArea.CodeEditor.CaretPosition.Line + 1}:{window.EditorArea.CodeEditor.CaretPosition.Column + 1} ({window.Status})");
    }

    private static void AwaitEvaluated(MainWindow window) =>
        Ui.WaitFor(() => window.Session.Workspace?.Projects is { Count: 3 } projects && projects.All(p => p.IsEvaluated), "the solution's projects evaluated", TimeSpan.FromMinutes(2));

    private static void TypeEach(MainWindow window, string text)
    {
        foreach (char c in text)
        {
            window.KeyTextInput(c.ToString());
        }
    }

    private static void PlaceAfter(CodeEditor editor, string text)
    {
        int at = editor.Document.Snapshot.ToString().IndexOf(text, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{text}' is not in the file");
        editor.Selections = SelectionSet.Single(at + text.Length);
    }

    private static T Editor<T>(SettingsDialog dialog, string label) where T : Control =>
        ((Visual)dialog.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == label).Parent!).GetVisualDescendants().OfType<T>().First();

    private static void Click(Window window, Control control)
    {
        Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
    }

    private static List<string> Answer(MainWindow window)
    {
        List<string> asked = [];
        window.Confirm = (title, _, _) =>
        {
            asked.Add(title);
            return Task.FromResult(true);
        };
        return asked;
    }

    private static ExplorerNode Row(MainWindow window, NodeKind kind, string name) =>
        window.ExplorerView.Tree.Rows.FirstOrDefault(r => r.Kind == kind && r.Name == name)
        ?? throw new InvalidOperationException($"The Explorer shows no {kind} {name}: [{string.Join(", ", window.ExplorerView.Tree.Rows.Select(r => r.Name))}]");

    private static Control Container(MainWindow window, ExplorerNode row)
    {
        ListBox rows = window.ExplorerView.GetVisualDescendants().OfType<ListBox>().First(l => ReferenceEquals(l.ItemsSource, window.ExplorerView.Tree.Rows));
        rows.ScrollIntoView(row);
        Ui.Settle();
        return rows.ContainerFromItem(row) ?? throw new InvalidOperationException($"The Explorer does not show {row.Name}");
    }

    private static void RightClick(MainWindow window, ExplorerNode row)
    {
        Control item = Container(window, row);
        Point at = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Ui.Settle();
    }

    private static void Choose(MainWindow window, ExplorerNode row)
    {
        Control item = Container(window, row);
        Point at = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
    }

    /// <summary>Chooses an item of a submenu of the Explorer's open menu, as clicks on them would.</summary>
    private static void ChooseFromMenu(MainWindow window, string submenu, string item)
    {
        ContextMenu menu = window.ExplorerView.OpenMenu ?? throw new InvalidOperationException("No menu opened on the row");
        MenuItem parent = menu.Items.OfType<MenuItem>().FirstOrDefault(i => (string?)i.Header == submenu)
            ?? throw new InvalidOperationException($"The row's menu has no {submenu}: [{string.Join(", ", menu.Items.OfType<MenuItem>().Select(i => i.Header))}]");
        MenuItem chosen = parent.Items.OfType<MenuItem>().FirstOrDefault(i => (string?)i.Header == item)
            ?? throw new InvalidOperationException($"{submenu} has no {item}: [{string.Join(", ", parent.Items.OfType<MenuItem>().Select(i => i.Header))}]");
        chosen.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();
        Ui.Settle();
    }

    /// <summary>Docker Hub on a loopback port of its own, each request under its host's name as the Dockerfile server's
    /// preload sends it there: a token from auth.docker.io, and postgres's tags from registry-1.docker.io for that token;
    /// every request kept by its method and path.</summary>
    private sealed class StandInHub : IDisposable
    {
        private const string Token = "stand-in";

        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource end = new();
        private readonly List<string> asked = [];
        private readonly Task accepting;

        public StandInHub()
        {
            listener.Start();
            Place = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
            accepting = Task.Run(AcceptAsync);
        }

        public Uri Place { get; }

        public IReadOnlyList<string> Asked
        {
            get
            {
                lock (asked)
                {
                    return [.. asked];
                }
            }
        }

        public void Dispose()
        {
            end.Cancel();
            listener.Stop();
            accepting.Wait(TimeSpan.FromSeconds(10));
            end.Dispose();
        }

        private async Task AcceptAsync()
        {
            List<Task> answering = [];
            try
            {
                while (true)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync(end.Token);
                    answering.Add(Task.Run(() => AnswerAsync(client)));
                }
            }
            catch (Exception stopped) when (stopped is OperationCanceledException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                // Disposed.
            }
            await Task.WhenAll(answering);
        }

        private async Task AnswerAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    StringBuilder head = new();
                    byte[] buffer = new byte[4096];
                    while (!head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        int read = await stream.ReadAsync(buffer, end.Token);
                        if (read == 0)
                        {
                            return;
                        }
                        head.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    }
                    string[] lines = head.ToString().Split("\r\n");
                    string request = lines[0][..lines[0].LastIndexOf(' ')];
                    lock (asked)
                    {
                        asked.Add(request);
                    }
                    string path = request[(request.IndexOf(' ') + 1)..];
                    bool bearer = lines.Any(l => l.Equals($"Authorization: Bearer {Token}", StringComparison.OrdinalIgnoreCase));
                    (int status, string body) = path switch
                    {
                        _ when path.StartsWith("/auth.docker.io/token?", StringComparison.Ordinal) => (200, $"{{\"token\":\"{Token}\"}}"),
                        "/registry-1.docker.io/v2/library/postgres/tags/list" when bearer => (200, "{\"name\":\"library/postgres\",\"tags\":[\"16\",\"17\",\"17-alpine\",\"latest\"]}"),
                        _ => (404, "{}"),
                    };
                    byte[] content = Encoding.UTF8.GetBytes(body);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: application/json\r\n"
                        + $"Content-Length: {content.Length}\r\nConnection: close\r\n\r\n"), end.Token);
                    await stream.WriteAsync(content, end.Token);
                }
                catch (Exception gone) when (gone is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    // The server gave up on it, or the test ended.
                }
            }
        }
    }

    /// <summary>A solution of an ASP.NET Core app (Api, using Npgsql, with launch profiles and a connection string), a worker
    /// and a library the app references, with a compose file of its own under ops/ (its .env, env_file, a file it extends,
    /// and a Dockerfile); never restored. The tests share it, each putting back what it changed.</summary>
    public sealed class Shop : IDisposable
    {
        public const string Solution = "<Solution>\r\n  <Project Path=\"Api/Api.csproj\" />\r\n  <Project Path=\"Lib/Lib.csproj\" />\r\n  <Project Path=\"Worker/Worker.csproj\" />\r\n</Solution>\r\n";

        public const string Api = "<Project Sdk=\"Microsoft.NET.Sdk.Web\">\r\n\r\n  <PropertyGroup>\r\n    <TargetFramework>net10.0</TargetFramework>\r\n  </PropertyGroup>\r\n\r\n"
            + "  <ItemGroup>\r\n    <PackageReference Include=\"Npgsql\" Version=\"9.0.3\" />\r\n    <ProjectReference Include=\"..\\Lib\\Lib.csproj\" />\r\n  </ItemGroup>\r\n\r\n</Project>\r\n";

        public const string Worker = "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n\r\n  <PropertyGroup>\r\n    <OutputType>Exe</OutputType>\r\n    <TargetFramework>net10.0</TargetFramework>\r\n"
            + "    <Nullable>enable</Nullable>\r\n  </PropertyGroup>\r\n\r\n</Project>\r\n";

        public const string Lib = "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n\r\n  <PropertyGroup>\r\n    <TargetFramework>net10.0</TargetFramework>\r\n  </PropertyGroup>\r\n\r\n</Project>\r\n";

        public const string LaunchSettings = "{\n  \"profiles\": {\n    \"http\": {\n      \"commandName\": \"Project\",\n      \"applicationUrl\": \"http://localhost:5080\"\n    },\n"
            + "    \"https\": {\n      \"commandName\": \"Project\",\n      \"applicationUrl\": \"https://localhost:7080;http://localhost:5080\"\n    }\n  }\n}\n";

        public const string AppSettings = "{\n  \"ConnectionStrings\": {\n    \"Shop\": \"Host=localhost;Database=shop;Username=postgres;Password=postgres\"\n  }\n}\n";

        /// <summary>No feed that is there: a restore of the folder fails at once, offline.</summary>
        public const string NuGetConfig = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<configuration>\n  <packageSources>\n    <clear />\n    <add key=\"none\" value=\"./no-packages\" />\n  </packageSources>\n</configuration>\n";

        public const string ComposeFile = "services:\n  api:\n    build:\n      context: ..\n      dockerfile: ops/api.Dockerfile\n    image: ${REGISTRY}/shop/api:latest\n"
            + "    depends_on:\n      db:\n        condition: service_healthy\n    env_file:\n      - api.env\n    environment:\n"
            + "      ConnectionStrings__Shop: \"Host=db;Password=$DB_PASSWORD\"\n    networks:\n      - backend\n    volumes:\n      - data:/var/data\n"
            + "    secrets:\n      - token\n  db:\n    image: postgres:17\n    ports: !reset []\n    links: [cache]\n    volumes_from:\n      - cache\n"
            + "  cache:\n    extends:\n      file: common.yaml\n      service: base\n    network_mode: service:db\nvolumes:\n  data: {}\nnetworks:\n  backend: {}\n"
            + "secrets:\n  token:\n    file: ./token.txt\n";

        public const string OpsDockerfile = "FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base\nWORKDIR /app\nEXPOSE 8080\n\nFROM mcr.microsoft.com/dotnet/sdk:10.0 AS build\n"
            + "WORKDIR /src\nCOPY . .\nRUN dotnet publish \"Api/Api.csproj\" -c Release -o /app/publish\n\nFROM base AS final\nWORKDIR /app\n"
            + "COPY --from=build /app/publish .\nENTRYPOINT [\"dotnet\", \"Api.dll\"]\n";

        /// <summary>What Add > Docker Support writes for Api.</summary>
        public const string Dockerfile = "# Built from the solution's folder: docker build -f Api/Dockerfile .\nFROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base\nUSER $APP_UID\n"
            + "WORKDIR /app\nENV ASPNETCORE_HTTP_PORTS=5080\nEXPOSE 5080\n\nFROM mcr.microsoft.com/dotnet/sdk:10.0 AS build\nARG BUILD_CONFIGURATION=Release\nWORKDIR /src\n"
            + "COPY [\"nuget.config\", \".\"]\nCOPY [\"Api/Api.csproj\", \"Api/\"]\nCOPY [\"Lib/Lib.csproj\", \"Lib/\"]\nRUN dotnet restore \"./Api/Api.csproj\"\nCOPY . .\n"
            + "WORKDIR \"/src/Api\"\nRUN dotnet build \"./Api.csproj\" -c $BUILD_CONFIGURATION -o /app/build\n\nFROM build AS publish\nARG BUILD_CONFIGURATION=Release\n"
            + "RUN dotnet publish \"./Api.csproj\" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false\n\nFROM base AS final\nWORKDIR /app\n"
            + "COPY --from=publish /app/publish .\nENTRYPOINT [\"dotnet\", \"Api.dll\"]\n";

        /// <summary>What Add > Docker Compose writes for Api alone.</summary>
        public const string Compose = "services:\n  api:\n    build:\n      context: .\n      dockerfile: Api/Dockerfile\n    image: api\n    ports:\n      - \"5080:5080\"\n"
            + "    environment:\n      ConnectionStrings__Shop: \"Host=postgres;Port=5432;Database=shop;Username=postgres;Password=${POSTGRES_PASSWORD:-postgres}\"\n"
            + "    depends_on:\n      postgres:\n        condition: service_healthy\n  postgres:\n    image: postgres:17\n    environment:\n"
            + "      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-postgres}\n    volumes:\n      - postgres-data:/var/lib/postgresql/data\n    healthcheck:\n"
            + "      test: [\"CMD-SHELL\", \"pg_isready -U postgres\"]\n      interval: 5s\n      timeout: 5s\n      retries: 10\n\nvolumes:\n  postgres-data:\n";

        private static readonly (string Path, string Text)[] Files =
        [
            ("Shop.slnx", Solution),
            ("nuget.config", NuGetConfig),
            ("Api/Api.csproj", Api),
            ("Api/Program.cs", "var app = WebApplication.Create(args);\napp.MapGet(\"/\", () => \"Shop\");\napp.Run();\n"),
            ("Api/Properties/launchSettings.json", LaunchSettings),
            ("Api/appsettings.json", AppSettings),
            ("Lib/Lib.csproj", Lib),
            ("Lib/Prices.cs", "namespace Lib;\n\npublic static class Prices\n{\n}\n"),
            ("Worker/Worker.csproj", Worker),
            ("Worker/Program.cs", "System.Console.WriteLine(\"Working\");\n"),
            ("ops/compose.yaml", ComposeFile),
            ("ops/.env", "REGISTRY=ghcr.io/shop\nDB_PASSWORD=secret\n"),
            ("ops/api.env", "ASPNETCORE_ENVIRONMENT=Development\n"),
            ("ops/common.yaml", "services:\n  base:\n    image: redis:7\n"),
            ("ops/token.txt", "dev-token\n"),
            ("ops/api.Dockerfile", OpsDockerfile),
        ];

        /// <summary>What the tests' Adds write, taken away again.</summary>
        private static readonly string[] Made = ["Api/Dockerfile", "Worker/Dockerfile", "Lib/Dockerfile", ".dockerignore", "compose.yaml"];

        public Shop()
        {
            Folder = TestFolders.Create("Shop");
            Reset();
        }

        public string Folder { get; }

        /// <summary>A file's path as the IDE names it: canonical, its '/' Windows' '\' there, so it compares with an open file's.</summary>
        public string PathOf(string relative) => Path.GetFullPath(Path.Combine(PathNames.Canonical(Folder), relative));

        public void Reset()
        {
            foreach ((string path, string text) in Files)
            {
                string full = Path.Combine(Folder, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
            }
            foreach (string made in Made)
            {
                File.Delete(Path.Combine(Folder, made));
            }
        }

        /// <summary>A window on the solution, with a file open when one is named, its Dockerfile server asking
        /// <paramref name="dockerHub"/> for image tags when one is given.</summary>
        public MainWindow Open(string? file, Uri? dockerHub = null)
        {
            MainWindow window = new() { Width = 1200, Height = 800, Autosaves = false, DockerHub = dockerHub };
            window.Show();
            window.OpenFolder(Folder);
            if (file is not null)
            {
                window.OpenFile(PathOf(file));
            }
            Ui.Settle();
            return window;
        }

        /// <summary>Skips a test that needs the Dockerfile or YAML server where there is no Node.js to run it, or (for the
        /// YAML server, <paramref name="yaml"/>) one older than it needs.</summary>
        public static void SkipWithoutNode(bool yaml = false)
        {
            WebLanguagesTests.Shop.SkipWithoutNode();
            if (yaml)
            {
                Passenger.App.Web.NodeRuntime node = Task.Run(() => Passenger.App.Web.NodeRuntime.FindAsync(null)).GetAwaiter().GetResult();
                string? old = node.Problem ?? node.TooOldFor(Path.Combine(AppContext.BaseDirectory, "web", "node_modules", "yaml-language-server", "package.json"), "the YAML language server");
                Assert.SkipWhen(old is not null, $"The YAML language server cannot run here: {old}");
            }
        }

        public void Dispose() => TestFolders.Delete(Folder);
    }
}
