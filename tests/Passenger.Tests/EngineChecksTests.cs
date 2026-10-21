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
/// and listed in Problems; and with no engine running, nothing of it per file. The engine's tests skip, saying why, where
/// no Linux engine runs (no Node.js is needed: the markers stand without the files' servers).</summary>
public sealed class EngineChecksTests
{
    private const string Dockerfile = "FROM busybox:1.37 as build\nRUN echo built > /built\n\nFROM busybox:1.37\nCOPY --from=build /built /built\nCMD [\"cat\", \"/built\"]\n";

    [AvaloniaFact]
    public void A_dockerfile_shows_buildkits_checks_beside_its_servers_and_in_problems_and_a_fix_saved_clears_them()
    {
        SkipWithoutBuildx();
        string folder = Folder(("Dockerfile", Dockerfile));
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
                && t.Text?.Contains("reads each base image's metadata from its registry", StringComparison.Ordinal) == true);
            CheckBox on = ((Visual)dialog.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == BuildKitSetting).Parent!).GetVisualDescendants().OfType<CheckBox>().First();
            Point at = on.TranslatePoint(new Point(on.Bounds.Width / 2, on.Bounds.Height / 2), dialog)!.Value;
            dialog.MouseDown(at, MouseButton.Left);
            dialog.MouseUp(at, MouseButton.Left);
            Ui.Settle();
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
            Assert.Equal((PathOf(folder, "Dockerfile"), 0, casing.Message), (listed.Path, listed.Position.Line, listed.Message));
            problems.Shown = ProblemsTab.Containers;
            Assert.Equal([(0, "Dockerfile", "1 problem"), (1, casing.Message, "Dockerfile:1:1")], problems.Rows.Select(r => (r.Depth, r.Label, r.Detail)));

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
            + "    environment:\n      GREETING: hello $NAME\n      ZOO: $MONKEY\n  db:\n    image: postgres:16\n    environment:\n      POSTGRES_USER: shop\n"
            + "      POSTGRES_PASSWORD: ${DB_PASSWORD:-hunter2}\n";
        string folder = Folder(("compose.yaml", Compose), (".env", "PORT=9090\nNAME=world\nMONKEY=banana\nDB_PASSWORD=s3cret-value\n"),
            ("compose.override.yaml", "services:\n  db:\n    image: postgres:17\n    environment:\n      POSTGRES_USER: shop\n      POSTGRES_PASSWORD: other-s3cret\n"));
        MainWindow window = Open(folder, "compose.yaml");
        try
        {
            OpenDocument document = window.EditorArea.Active!;
            // A secret's value (a password's, as a word of its variable's or key's name says) is masked, the same dots whatever its
            // length; MONKEY has no word KEY.
            string[] expected = ["${TAG:-1.36}|= 1.36", "${PORT:-8080}|= 9090", "$NAME|= world", "$MONKEY|= banana", "postgres:16|compose.override.yaml: postgres:17",
                "${DB_PASSWORD:-hunter2}|= ••••••", "${DB_PASSWORD:-hunter2}|compose.override.yaml: ••••••"];
            Ui.WaitFor(() => Hints(document, expected).SequenceEqual(expected), $"Compose's values after the text, not [{string.Join(", ", Hints(document, expected))}]");
            Assert.DoesNotContain(document.InlayHints, h => h.Label.Contains("s3cret", StringComparison.Ordinal));

            // Typed, the file is read again once typing pauses: a default of a variable the .env leaves unset is its value.
            string text = document.Text.Snapshot.ToString();
            int port = text.IndexOf("${PORT:-8080}", StringComparison.Ordinal);
            window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(port, "${PORT:-8080}".Length, "${HOST_PORT:-8081}")]);
            string[] typed = ["${TAG:-1.36}|= 1.36", "${HOST_PORT:-8081}|= 8081", "$NAME|= world", "$MONKEY|= banana", "postgres:16|compose.override.yaml: postgres:17",
                "${DB_PASSWORD:-hunter2}|= ••••••", "${DB_PASSWORD:-hunter2}|compose.override.yaml: ••••••"];
            Ui.WaitFor(() => Hints(document, typed).SequenceEqual(typed), $"the new variable's default, not [{string.Join(", ", Hints(document, typed))}]");
            Assert.Empty(window.BottomPanel.Problems.ContainerProblems);
        }
        finally
        {
            window.Close();
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
            Assert.Equal((MarkerKind.Error, "depends_on"), (error.Kind, document.Text.Snapshot.GetText(error.Range.Start, error.Range.Length)));
            Problem listed = Assert.Single(window.BottomPanel.Problems.ContainerProblems);
            Assert.Equal((PathOf(folder, "compose.yaml"), new TextPosition(3, 4), Said), (listed.Path, listed.Position, listed.Message));

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
            File.WriteAllText(Path.Combine(folder, name), text);
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
