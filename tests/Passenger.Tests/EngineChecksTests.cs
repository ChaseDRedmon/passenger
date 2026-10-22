using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Containers;
using Passenger.App.Run;
using Passenger.App.Settings;
using Passenger.App.ToolWindows;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>What the person's container engine says of a Dockerfile and a compose file, as a person sees it in the window:
/// BuildKit's build checks marked beside the Dockerfile server's and listed by file in Problems' Containers tab, gone once
/// a fix is saved; a compose file's <c>${VAR}</c>s showing what Compose makes of them with the .env beside it, the base
/// file's values its override changes showing the value Compose ends with, and Compose's error marked where it names it
/// and listed in Problems, a name the file does not declare fixed by Passenger's own fixes; and with no engine running,
/// nothing of it per file. The engine's tests skip, saying why, where
/// no Linux engine runs (no Node.js is needed: the markers stand without the files' servers).</summary>
public sealed class EngineChecksTests
{
    private const string Dockerfile = "FROM busybox:1.37 as build\nRUN echo built > /built\n\nFROM busybox:1.37\nCOPY --from=build /built /built\nCMD [\"cat\", \"/built\"]\n";

    /// <summary>A frontend image no registry has: were it pulled, the check would fail, saying only that its host is unknown.</summary>
    private const string Frontend = "passenger.invalid/unknown-frontend:1";

    [AvaloniaFact]
    public void A_dockerfile_shows_buildkits_checks_beside_its_servers_and_in_problems_never_running_the_frontend_it_names_and_a_fix_saved_clears_them()
    {
        SkipWithoutBuildx();
        // Its syntax directive names a frontend image, which BuildKit would pull and run to check it: the engine's own checks it.
        string folder = Folder(("Dockerfile", $"# syntax={Frontend}\n# check=error=false\n{Dockerfile}"));
        MainWindow window = Open(folder, "Dockerfile");
        try
        {
            OpenDocument document = window.EditorArea.Active!;
            TextSnapshot text = document.Text.Snapshot;
            // Off, as every other test has it: no registry is asked, nothing is marked.
            Assert.False(window.Settings.BuildKitChecks, "Check Dockerfiles with BuildKit is on in the tests' run, which reaches registries");
            Assert.DoesNotContain(document.Markers, m => m.Message.StartsWith("BuildKit:", StringComparison.Ordinal));

            // Turned on, on Settings' Docker page, which says what it reads.
            SettingsDialog dialog = DialogButtonsTests.Check(window.ShowSettings());
            dialog.Query = "buildkit";
            Ui.Settle();
            Assert.Equal([SettingsDialog.DockerPage], dialog.Pages);
            Assert.Equal([BuildKitSetting], dialog.Shown);
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible
                && t.Text?.Contains("reads each base image's metadata from its registry", StringComparison.Ordinal) == true
                && t.Text.Contains("never one a # syntax= line names, which is neither pulled nor run", StringComparison.Ordinal));
            CheckBox on = ((Visual)dialog.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == BuildKitSetting).Parent!).GetVisualDescendants().OfType<CheckBox>().First();
            Ui.Click(on);
            dialog.Ok();
            Ui.Settle();
            Assert.True(window.Settings.BuildKitChecks, "Ticking Check Dockerfiles with BuildKit left it off");

            Ui.WaitFor(() => document.Markers.Any(m => m.Message.EndsWith("(FromAsCasing)", StringComparison.Ordinal)), $"BuildKit's FromAsCasing on the first line ({window.Status})");
            Marker casing = document.Markers.Single(m => m.Message.EndsWith("(FromAsCasing)", StringComparison.Ordinal));
            Assert.Equal(MarkerKind.Warning, casing.Kind);
            Assert.Equal("FROM busybox:1.37 as build", text.GetText(casing.Range.Start, casing.Range.Length));
            Assert.Equal("BuildKit: 'as' and 'FROM' keywords' casing do not match (FromAsCasing)", casing.Message);
            Assert.Equal(new Uri("https://docs.docker.com/go/dockerfile/rule/from-as-casing/"), casing.Link);
            Assert.Equal("FromAsCasing", casing.LinkText);

            // Problems' Containers tab lists it under its file, linked to the rule's page.
            ProblemsView problems = window.BottomPanel.Problems;
            Problem listed = Assert.Single(problems.ContainerProblems);
            Assert.Equal((PathOf(folder, "Dockerfile"), 2, casing.Message), (listed.Path, listed.Position.Line, listed.Message));
            problems.Shown = ProblemsTab.Containers;
            Assert.Equal([(0, "Dockerfile", "1 problem"), (1, casing.Message, "Dockerfile:3:1")], problems.Rows.Select(r => (r.Depth, r.Label, r.Detail)));
            Assert.Empty(Docker.Ask(["image", "ls", "-q", Frontend]).Trim());

            // Fixed and saved, the check runs again and finds nothing.
            int casingAt = text.ToString().IndexOf(" as ", StringComparison.Ordinal);
            window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(casingAt, 4, " AS ")]);
            Ui.Run(window, AppActions.Save);
            Ui.WaitFor(() => !document.Markers.Any(m => m.Message.StartsWith("BuildKit:", StringComparison.Ordinal)) && problems.ContainerProblems.Count == 0,
                $"BuildKit's warning gone once the fix was saved: [{string.Join(", ", problems.ContainerProblems.Select(p => p.Message))}]");
            Assert.Empty(problems.Rows);
        }
        finally
        {
            window.Close();
        }
    }

    private const string BuildKitSetting = "Check Dockerfiles with BuildKit";

    [AvaloniaFact]
    public void A_compose_file_shows_what_its_variables_come_to_and_the_values_its_override_changes_as_hints()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        const string Compose = "services:\n  web:\n    image: \"busybox:${TAG:-1.36}\"\n    ports:\n      - \"${PORT:-8080}:80\"  # the host's port\n"
            + "    environment:\n      GREETING: hello $NAME\n      ZOO: $MONKEY\n    volumes:\n      - $PWD/data:/data\n  db:\n    image: postgres:16\n    environment:\n      POSTGRES_USER: shop\n"
            + "      POSTGRES_PASSWORD: ${DB_PASSWORD:-hunter2}\n";
        string folder = Folder(("compose.yaml", Compose), (".env", "PORT=9090\nNAME=world\nMONKEY=banana\nDB_PASSWORD=s3cret-value\n"),
            ("compose.override.yaml", "services:\n  db:\n    image: postgres:17\n    environment:\n      POSTGRES_USER: shop\n      POSTGRES_PASSWORD: other-s3cret\n"));
        MainWindow window = Open(folder, "compose.yaml");
        try
        {
            OpenDocument document = window.EditorArea.Active!;
            // A secret's value (a password's, as a word of its variable's or key's name says) is masked, the same dots whatever its
            // length; MONKEY has no word KEY, and PWD is the compose file's folder, which Compose runs in, never where the IDE was started.
            string folderHint = FolderHint(folder);
            string[] expected = ["${TAG:-1.36}|= 1.36", "${PORT:-8080}|= 9090", "$NAME|= world", "$MONKEY|= banana", folderHint, "postgres:16|compose.override.yaml: postgres:17",
                "${DB_PASSWORD:-hunter2}|= ••••••", "${DB_PASSWORD:-hunter2}|compose.override.yaml: ••••••"];
            Ui.WaitFor(() => Hints(document, expected).SequenceEqual(expected), $"Compose's values after the text, not [{string.Join(", ", Hints(document, expected))}]");
            Assert.DoesNotContain(document.InlayHints, h => h.Label.Contains("s3cret", StringComparison.Ordinal));

            // Typed, the file is read again once typing pauses: a default of a variable the .env leaves unset is its value.
            string text = document.Text.Snapshot.ToString();
            int port = text.IndexOf("${PORT:-8080}", StringComparison.Ordinal);
            window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(port, "${PORT:-8080}".Length, "${HOST_PORT:-8081}")]);
            string[] typed = ["${TAG:-1.36}|= 1.36", "${HOST_PORT:-8081}|= 8081", "$NAME|= world", "$MONKEY|= banana", folderHint, "postgres:16|compose.override.yaml: postgres:17",
                "${DB_PASSWORD:-hunter2}|= ••••••", "${DB_PASSWORD:-hunter2}|compose.override.yaml: ••••••"];
            Ui.WaitFor(() => Hints(document, typed).SequenceEqual(typed), $"the new variable's default, not [{string.Join(", ", Hints(document, typed))}]");
            Assert.Empty(window.BottomPanel.Problems.ContainerProblems);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A secret is known by its variable's name (a word PASS, PW, PWD or KEY, PASSWORD or SECRET anywhere in it, a
    /// name ending in KEY but an ordinary word's), by the key or list item's KEY= it is given to, or by its place as a URL's
    /// password; a value holding such a URL shows it with the password masked.</summary>
    [AvaloniaFact]
    public void A_compose_files_hints_mask_a_secret_by_its_variables_name_the_key_it_is_given_to_and_a_urls_password()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        const string Compose = "services:\n  db:\n    image: postgres:17\n    environment:\n      POSTGRES_PASSWORD: ${DB_PASS}\n      OPENAI: ${APIKEY}\n"
            + "      ADMIN: ${DBPASSWORD}\n      DATABASE_URL: postgres://u:${PGPASS}@db/x\n      CACHE_PASSWORD: ${CACHE_VALUE}\n      BROKER: ${BROKER_URL}\n"
            + "      ZOO: $MONKEY\n  api:\n    image: busybox:1.37\n    environment:\n      - SIGNING_KEY=${SIGNER}\n    volumes:\n      - $PWD/data:/data\n";
        string[] secrets = ["pass-one", "key-two", "pass-three", "pass-four", "value-five", "hunter-six", "signer-seven"];
        string folder = Folder(("compose.yaml", Compose), (".env", $"DB_PASS={secrets[0]}\nAPIKEY={secrets[1]}\nDBPASSWORD={secrets[2]}\nPGPASS={secrets[3]}\n"
            + $"CACHE_VALUE={secrets[4]}\nBROKER_URL=amqp://app:{secrets[5]}@rabbit:5672\nSIGNER={secrets[6]}\nMONKEY=banana\n"));
        MainWindow window = Open(folder, "compose.yaml");
        try
        {
            OpenDocument document = window.EditorArea.Active!;
            string[] expected = ["${DB_PASS}|= ••••••", "${APIKEY}|= ••••••", "${DBPASSWORD}|= ••••••", "${PGPASS}|= ••••••", "${CACHE_VALUE}|= ••••••",
                "${BROKER_URL}|= amqp://app:••••••@rabbit:5672", "$MONKEY|= banana", "${SIGNER}|= ••••••", FolderHint(folder)];
            Ui.WaitFor(() => Hints(document, expected).SequenceEqual(expected), $"each secret masked, not [{string.Join(", ", Hints(document, expected))}]");
            string leaked = string.Join(", ", document.InlayHints.Where(h => secrets.Any(s => h.Label.Contains(s, StringComparison.Ordinal))).Select(h => h.Label));
            Assert.True(leaked.Length == 0, $"A compose file's hints showed secrets: {leaked}");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Compose fetches a Git repository's or an OCI artifact's compose file that an <c>include</c> names, unless
    /// told not to: the check, which reads the file on this computer alone, asks nothing of either's host (a listener on
    /// loopback here, which no connection reaches) and marks nothing for it.</summary>
    [AvaloniaFact]
    public void A_compose_files_remote_includes_are_never_fetched_and_mark_nothing()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        using Listener host = new();
        string folder = Folder(("git/compose.yaml", $"include:\n  - https://127.0.0.1:{host.Port}/shop.git\nservices:\n  web:\n    image: busybox:${{TAG:-1.37}}\n"),
            ("oci/compose.yaml", $"include:\n  - oci://127.0.0.1:{host.Port}/shop/compose:1\nservices:\n  web:\n    image: busybox:${{TAG:-1.37}}\n"));
        MainWindow window = Open(folder, "git/compose.yaml");
        try
        {
            OpenDocument git = window.EditorArea.Active!;
            window.OpenFile(PathOf(folder, "oci/compose.yaml"));
            Ui.Settle();
            OpenDocument oci = window.EditorArea.Active!;
            // Each checked: its variable's value, which Compose made in the same check, is shown.
            string[] expected = ["${TAG:-1.37}|= 1.37"];
            Ui.WaitFor(() => Hints(git, expected).SequenceEqual(expected) && Hints(oci, expected).SequenceEqual(expected), "both files checked");
            Assert.True(host.Connections == 0, $"Checking a compose file reached an included repository's host {host.Connections} times");
            string marked = string.Join(", ", git.Markers.Concat(oci.Markers).Where(m => m.Message.StartsWith("Compose:", StringComparison.Ordinal)).Select(m => m.Message));
            Assert.True(marked.Length == 0, $"A remote include not fetched was marked: {marked}");
            Assert.Empty(window.BottomPanel.Problems.ContainerProblems);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A port on loopback that counts the connections made to it.</summary>
    private sealed class Listener : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly Task accepting;
        private int connections;

        public Listener()
        {
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            accepting = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        using TcpClient client = await listener.AcceptTcpClientAsync();
                        Interlocked.Increment(ref connections);
                    }
                }
                catch (Exception stopped) when (stopped is SocketException or ObjectDisposedException or InvalidOperationException)
                {
                    // Disposed.
                }
            });
        }

        public int Port { get; }

        public int Connections => Volatile.Read(ref connections) + (listener.Pending() ? 1 : 0);

        public void Dispose()
        {
            listener.Stop();
            accepting.Wait(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public void A_compose_files_error_is_marked_where_compose_names_it_and_listed_in_problems_until_it_is_fixed()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        string folder = Folder(("compose.yaml", "services:\n  web:\n    image: busybox:1.37\n    depends_on:\n      - cache\n"));
        MainWindow window = Open(folder, "compose.yaml");
        try
        {
            OpenDocument document = window.EditorArea.Active!;
            const string Said = "Compose: service \"web\" depends on undefined service \"cache\": invalid compose project";
            Ui.WaitFor(() => document.Markers.Any(m => m.Message == Said), $"Compose's error marked, not [{string.Join(", ", document.Markers.Select(m => m.Message))}]");
            Marker error = document.Markers.Single(m => m.Message == Said);
            // On the name of the service that is not there, which its fixes are offered on.
            Assert.Equal((MarkerKind.Error, "cache"), (error.Kind, document.Text.Snapshot.GetText(error.Range.Start, error.Range.Length)));
            Problem listed = Assert.Single(window.BottomPanel.Problems.ContainerProblems);
            Assert.Equal((PathOf(folder, "compose.yaml"), new TextPosition(4, 8), Said), (listed.Path, listed.Position, listed.Message));

            // The service it depends on added (typed, not saved): Compose reads the text as typed once typing pauses.
            CodeEditor editor = window.EditorArea.CodeEditor;
            editor.ApplyEdits([new TextEdit(document.Text.Snapshot.Length, 0, "  cache:\n    image: busybox:1.37\n")]);
            Ui.WaitFor(() => !document.Markers.Any(m => m.Message.StartsWith("Compose:", StringComparison.Ordinal)) && window.BottomPanel.Problems.ContainerProblems.Count == 0,
                "Compose's error gone once the service is there");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A name Compose finds a service uses that the file does not declare is marked where it is written, and Show
    /// Context Actions there offers Passenger's own fixes first: a service depended on that is not there, removed from
    /// depends_on or changed to the service of a near name; a volume or secret declared at the top level, under its key or
    /// a key made for it, as the file indents. Each is one undo step, and the marker goes once Compose reads the text again.</summary>
    [AvaloniaFact]
    public void A_compose_files_undeclared_names_are_fixed_by_changing_removing_or_declaring_them_each_one_undo_step()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        const string Compose = "services:\n  web:\n    image: busybox:1.37\n    depends_on:\n      - dbb\n      - cache\n  db:\n    image: busybox:1.37\n"
            + "  cache:\n    image: busybox:1.37\n";
        string folder = Folder(("compose.yaml", Compose));
        MainWindow window = Open(folder, "compose.yaml");
        try
        {
            OpenDocument document = window.EditorArea.Active!;
            CodeEditor editor = window.EditorArea.CodeEditor;

            // The service it waits for, misspelt: marked on its name, changed to the near one or taken out of depends_on.
            Marker misspelt = Marked(window, document, "dbb");
            Assert.Equal(new TextPosition(4, 8), document.Text.Snapshot.GetPosition(misspelt.Range.Start));
            Fixes(window, misspelt, ["Remove dbb from depends_on", "Change to db"]);
            Ui.Press(window, "DOWN");
            Ui.Press(window, "ENTER");
            string changed = Compose.Replace("- dbb", "- db", StringComparison.Ordinal);
            Assert.Equal(changed, editor.Document.Snapshot.ToString());
            Ui.Run(window, EditorActions.Undo);
            Assert.Equal(Compose, editor.Document.Snapshot.ToString());
            Ui.Run(window, EditorActions.Redo);
            Assert.Equal(changed, editor.Document.Snapshot.ToString());
            Ui.WaitFor(() => !document.Markers.Any(m => m.Message.StartsWith("Compose:", StringComparison.Ordinal)), "Compose's error gone once the name is the service's");

            // One with no near name is only taken out.
            editor.ApplyEdits([new TextEdit(changed.IndexOf("- db", StringComparison.Ordinal) + 2, 2, "queue")]);
            Marker missing = Marked(window, document, "queue");
            Fixes(window, missing, ["Remove queue from depends_on"]);
            Ui.Press(window, "ENTER");
            Assert.Equal(Compose.Replace("      - dbb\n", string.Empty, StringComparison.Ordinal), editor.Document.Snapshot.ToString());
            Ui.WaitFor(() => !document.Markers.Any(m => m.Message.StartsWith("Compose:", StringComparison.Ordinal)), "Compose's error gone once it is taken out");

            // A volume and a secret the file has no key for: each declared at the file's end under a key made for it.
            string before = editor.Document.Snapshot.ToString();
            int db = before.IndexOf("  db:", StringComparison.Ordinal);
            editor.ApplyEdits([new TextEdit(db, 0, "    volumes:\n      - data:/data\n    secrets:\n      - api_key\n")]);
            Fixes(window, Marked(window, document, "data"), ["Declare data at the top level"]);
            Ui.Press(window, "ENTER");
            string declared = editor.Document.Snapshot.ToString();
            Assert.EndsWith("    image: busybox:1.37\nvolumes:\n  data:\n", declared, StringComparison.Ordinal);
            Ui.Run(window, EditorActions.Undo);
            Assert.DoesNotContain("\nvolumes:", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
            Ui.Run(window, EditorActions.Redo);
            Assert.Equal(declared, editor.Document.Snapshot.ToString());
            Fixes(window, Marked(window, document, "api_key"), ["Declare api_key at the top level"]);
            Assert.Equal("under secrets, read from ./api_key.txt", window.Assist.OfferedDetails[0]);
            Ui.Press(window, "ENTER");
            Assert.EndsWith("volumes:\n  data:\nsecrets:\n  api_key:\n    file: ./api_key.txt\n", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
            Ui.WaitFor(() => !document.Markers.Any(m => m.Message.StartsWith("Compose:", StringComparison.Ordinal)) && window.BottomPanel.Problems.ContainerProblems.Count == 0,
                $"every Compose error gone once the volume and secret are declared ({string.Join(", ", document.Markers.Select(m => m.Message))})");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A fix is worked out again on the text as it stands when it is chosen: a line added above while its list
    /// shows still declares the secret where it belongs, under the file's own key and indentation; one the file no longer
    /// needs (declared by hand meanwhile) is refused, saying so, and changes nothing.</summary>
    [AvaloniaFact]
    public void A_compose_fix_is_worked_out_again_as_it_is_chosen_and_refused_once_the_file_no_longer_needs_it()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        const string Compose = "services:\n    web:\n        image: busybox:1.37\n        secrets:\n            - api_key\nsecrets:\n    other:\n        file: ./other.txt\n";
        string folder = Folder(("compose.yaml", Compose));
        MainWindow window = Open(folder, "compose.yaml");
        try
        {
            OpenDocument document = window.EditorArea.Active!;
            CodeEditor editor = window.EditorArea.CodeEditor;
            Fixes(window, Marked(window, document, "api_key"), ["Declare api_key at the top level"]);
            editor.ApplyEdits([new TextEdit(0, 0, "# The shop\n")]);
            Ui.Press(window, "ENTER");
            Assert.Equal("# The shop\n" + Compose + "    api_key:\n        file: ./api_key.txt\n", editor.Document.Snapshot.ToString());

            Ui.Run(window, EditorActions.Undo);
            string asked = editor.Document.Snapshot.ToString();
            Fixes(window, Marked(window, document, "api_key"), ["Declare api_key at the top level"]);
            editor.ApplyEdits([new TextEdit(asked.Length, 0, "    api_key:\n        environment: API_KEY\n")]);
            string byHand = editor.Document.Snapshot.ToString();
            Ui.Press(window, "ENTER");
            Assert.Equal(byHand, editor.Document.Snapshot.ToString());
            Assert.Equal("Declare api_key at the top level was not applied: the file changed, so it no longer fits", window.Status);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>An agent's code_actions on Compose's marker list Passenger's own fixes first, and apply_code_action makes one
    /// on the text as it stands, the file saved. Skipped without a Node.js too, since the agents' code tools serve a compose
    /// file through its YAML server.</summary>
    [AvaloniaFact]
    public void An_agent_lists_a_compose_files_own_fixes_first_and_applies_one_saved()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        DockerFilesTests.Shop.SkipWithoutNode(yaml: true);
        const string Compose = "services:\n  web:\n    image: busybox:1.37\n    networks:\n      - backend\n";
        string folder = Folder(("compose.yaml", Compose));
        MainWindow window = Open(folder, "compose.yaml");
        try
        {
            Marked(window, window.EditorArea.Active!, "backend");
            Passenger.App.Mcp.PassengerTools agent = new(window);
            IReadOnlyList<string> actions = Ui.Await(agent.CodeActions("compose.yaml", 5, 9), "the code actions an agent asks for");
            Assert.Equal("Declare backend at the top level [quickfix]", actions[0]);
            string applied = Ui.Await(agent.ApplyCodeAction("compose.yaml", 5, 9, "Declare backend at the top level"), "the agent's fix made");
            Assert.Equal("Applied 'Declare backend at the top level' to compose.yaml", applied);
            Assert.Equal(Compose + "networks:\n  backend:\n", File.ReadAllText(PathOf(folder, "compose.yaml")));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Compose's marker on a name, once its check has run, with the caret put on it.</summary>
    private static Marker Marked(MainWindow window, OpenDocument document, string name)
    {
        Ui.WaitFor(() => document.Markers.Any(m => m.Message.StartsWith("Compose:", StringComparison.Ordinal) && Under(document, m) == name),
            $"Compose's error marked on {name}, not on [{string.Join(", ", document.Markers.Select(m => $"{Under(document, m)}: {m.Message}"))}]");
        Marker marker = document.Markers.First(m => m.Message.StartsWith("Compose:", StringComparison.Ordinal) && Under(document, m) == name);
        window.EditorArea.CodeEditor.Selections = SelectionSet.Single(marker.Range.Start + 1);
        return marker;
    }

    private static string Under(OpenDocument document, Marker marker) =>
        marker.Range.End <= document.Text.Snapshot.Length ? document.Text.Snapshot.GetText(marker.Range.Start, marker.Range.Length) : string.Empty;

    /// <summary>Show Context Actions on a marker: Passenger's own fixes first, these and no other of its own.</summary>
    private static void Fixes(MainWindow window, Marker marker, string[] expected)
    {
        Assert.Equal(MarkerKind.Error, marker.Kind);
        Ui.Run(window, AppActions.QuickFix);
        Ui.WaitFor(() => window.Assist.Offered.Count > 0, $"the fixes of {marker.Message}");
        string[] own = [.. window.Assist.Offered.Where(o => o.StartsWith("Remove ", StringComparison.Ordinal) || o.StartsWith("Change to ", StringComparison.Ordinal)
            || o.StartsWith("Declare ", StringComparison.Ordinal))];
        Assert.Equal(expected, own);
        Assert.Equal(expected, window.Assist.Offered.Take(expected.Length));
    }

    /// <summary>Without a Node.js, a compose file's language server cannot start, but the engine still checks it: each file
    /// opened after the first waits for no server's checks, and an agent's diagnostics answer the engine's findings with a
    /// word that the server's are missing and why, rather than refusing.</summary>
    [AvaloniaFact]
    public void Without_a_node_js_an_agents_diagnostics_of_a_compose_file_are_the_engines_with_why_the_servers_are_missing()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        const string Broken = "services:\n  web:\n    image: busybox:1.37\n    depends_on:\n      - cache\n";
        string folder = Folder(("compose.yaml", Broken), ("other/compose.yaml", Broken));
        string missing = Path.Combine(TestRun.Root, "no-node-here", OperatingSystem.IsWindows() ? "node.exe" : "node");
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        try
        {
            window.ApplySettings(window.Settings with { NodeInterpreter = missing });
            window.OpenFolder(folder);
            window.OpenFile(PathOf(folder, "compose.yaml"));
            Ui.Settle();
            window.OpenFile(PathOf(folder, Path.Combine("other", "compose.yaml")));
            Ui.Settle();
            OpenDocument second = window.EditorArea.Active!;
            Assert.False(second.IsChecking, "A compose file opened after the YAML server could not start waits for its checks");

            const string Said = "Compose: service \"web\" depends on undefined service \"cache\": invalid compose project";
            IReadOnlyList<Passenger.App.Mcp.Finding> found = Ui.Await(new Passenger.App.Mcp.PassengerTools(window).Diagnostics("other/compose.yaml"), "the diagnostics an agent asks for");
            Assert.Contains(found, f => f.Severity == "error" && f.Message == Said);
            Passenger.App.Mcp.Finding note = Assert.Single(found, f => f.Severity == "info");
            Assert.Equal($"Checking it beyond the container engine needs the YAML language server: there is no Node.js at {missing}, which Settings | Languages & Frameworks | Node.js names",
                note.Message);
        }
        finally
        {
            window.ApplySettings(new AppSettings());
            window.Close();
        }
    }

    [AvaloniaFact]
    public void With_no_engine_running_a_dockerfile_and_a_compose_file_show_nothing_of_it_and_problems_say_only_the_engines_state()
    {
        string folder = Folder(("Dockerfile", Dockerfile), ("compose.yaml", "services:\n  web:\n    image: busybox\n    ports: [\"${PORT:-8080}:80\"]\n"));
        MainWindow window = new() { Width = 1200, Height = 800, Engine = new ContainerEngine(cli: ContainersTests.StandInCli(TestFolders.Create("ctr-cli-"))) };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile(PathOf(folder, "Dockerfile"));
        Ui.Settle();
        try
        {
            OpenDocument dockerfile = window.EditorArea.Active!;
            window.OpenFile(PathOf(folder, "compose.yaml"));
            Ui.Settle();
            OpenDocument compose = window.EditorArea.Active!;
            Ui.WaitFor(() => window.BottomPanel.Problems.ContainerProblems.Count > 0, "the engine's state in Problems");
            Problem engine = Assert.Single(window.BottomPanel.Problems.ContainerProblems);
            Assert.StartsWith("Docker isn't running", engine.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(dockerfile.Markers, m => m.Message.StartsWith("BuildKit:", StringComparison.Ordinal));
            Assert.Empty(compose.InlayHints);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The hint after <c>$PWD</c>: the compose file's folder, which Compose runs in, never where the IDE was started.</summary>
    private static string FolderHint(string folder)
    {
        string pwd = PathNames.Canonical(folder);
        return $"$PWD|= {(pwd.Length > 48 ? pwd[..47] + "…" : pwd)}";
    }

    /// <summary>Each value hint as the stretch of text it follows (the expected one it ends, else its line up to it) and its
    /// label, in order.</summary>
    private static string[] Hints(OpenDocument document, string[] expected)
    {
        string text = document.Text.Snapshot.ToString();
        return [.. document.InlayHints.Where(h => h.Kind == InlayHintKind.Value).Select(h =>
        {
            string before = text[..h.Offset];
            string? known = expected.Select(e => e.Split('|')[0]).FirstOrDefault(e => before.EndsWith(e, StringComparison.Ordinal));
            return $"{known ?? before[(before.LastIndexOf('\n') + 1)..]}|{h.Label}";
        })];
    }

    private static void SkipWithoutBuildx()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        EngineState state = Task.Run(() => ContainerEngine.Shared.StateAsync()).GetAwaiter().GetResult();
        Assert.SkipWhen(state.BuildxVersion is null, "docker buildx, whose build checks a Dockerfile's are, is not installed here");
    }

    /// <summary>A folder of the test's own holding the files given.</summary>
    private static string Folder(params (string Name, string Text)[] files)
    {
        string folder = TestFolders.Create("engine-checks-");
        foreach ((string name, string text) in files)
        {
            string path = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        return folder;
    }

    private static string PathOf(string folder, string name) => Path.Combine(PathNames.Canonical(folder), name);

    private static MainWindow Open(string folder, string file)
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile(PathOf(folder, file));
        Ui.Settle();
        return window;
    }
}
