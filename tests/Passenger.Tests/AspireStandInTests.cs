using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Aspire;
using Passenger.App.Aspire.Protocol;
using Passenger.Core;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>The Aspire window over an AppHost's resource service, a stand-in the test drives (<see cref="AspireInteractionHost"/>),
/// which a stand-in AppHost names in its launch profile (<see cref="StandInAppHost"/>): the window runs that AppHost as a person
/// runs theirs and speaks to the stand-in with the key it gave it. No Aspire package, dashboard, orchestrator or CLI is needed,
/// so these run on every system.</summary>
public sealed class AspireStandInTests(StandInAppHost project) : IClassFixture<StandInAppHost>
{
    /// <summary>A single-file AppHost has its run configuration as the folder opens, found among the files of the folder's one
    /// walk, and as the folder watcher sees one come, a C# file gain the AppHost SDK's line, or one go; never one under a hidden
    /// folder, which a walk for what projects build passes over.</summary>
    [AvaloniaFact]
    public void Single_file_AppHosts_are_found_as_the_folder_opens_and_followed_as_files_come_change_and_go()
    {
        project.Reset();
        string sdk = $"#:sdk Aspire.AppHost.Sdk@{AspireApp.TemplatesVersion}\nvar builder = DistributedApplication.CreateBuilder(args);\nbuilder.Build().Run();\n";
        TestFolders.Write(project.Folder, "Loose/apphost.cs", sdk);
        TestFolders.Write(project.Folder, ".hidden/apphost.cs", sdk);
        TestFolders.Write(project.Folder, "Later/Notes.cs", "System.Console.WriteLine(\"Not an AppHost yet\");\n");
        string loose = PathNames.Canonical(project.PathOf("Loose/apphost.cs"));
        string notes = PathNames.Canonical(project.PathOf("Later/Notes.cs"));
        MainWindow window = project.Open(null, languageServer: false);
        try
        {
            Ui.WaitFor(() => Found(window, loose), "the single-file AppHost there as the folder opened");
            Assert.DoesNotContain(window.Configurations, c => c.Project.Contains($"{Path.DirectorySeparatorChar}.hidden{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
            Assert.False(Found(window, notes), "A C# file without the AppHost SDK's line was taken for a single-file AppHost");

            TestFolders.Write(project.Folder, "Came/apphost.cs", sdk);
            string came = PathNames.Canonical(project.PathOf("Came/apphost.cs"));
            Ui.WaitFor(() => Found(window, came), "a single-file AppHost made after the folder opened");
            File.WriteAllText(notes, sdk);
            Ui.WaitFor(() => Found(window, notes), "a C# file that gained the AppHost SDK's line");
            File.Delete(loose);
            Ui.WaitFor(() => !Found(window, loose), "a deleted single-file AppHost's configuration to go");
            Assert.True(Found(window, came) && Found(window, notes), "The single-file AppHosts still there lost their configurations as another went");
            Assert.DoesNotContain(window.Configurations, c => c.Project.Contains($"{Path.DirectorySeparatorChar}.hidden{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
        }

        static bool Found(MainWindow window, string file) => window.Configurations.Any(c => c.Aspire && PathNames.Same(c.Project, file));
    }

    [AvaloniaFact]
    public void The_console_drops_overlapping_lines_follows_a_new_generation_links_its_lines_and_ends_its_streams_when_hidden()
    {
        project.Reset();
        AspireInteractionHost backend = Ui.Await(AspireInteractionHost.StartAsync(project.KeyFile), "the stand-in resource service");
        backend.PushResource(new Resource { Name = "apiservice", DisplayName = "apiservice", Uid = "first", ResourceType = "Project", State = "Running" });
        MainWindow window = OpenRunning(backend);
        List<string> started = [];
        EventHandler<ChildStart> observe = (_, child) =>
        {
            lock (started)
            {
                started.Add(child.Command);
            }
        };
        try
        {
            FindAction(window, "Show Aspire Console");
            Ui.WaitFor(() => backend.Consoles.Length == 1, "the console's subscription");
            AspireInteractionHost.ConsoleSubscription old = Assert.Single(backend.Consoles);
            Assert.Equal("apiservice", old.ResourceName);
            old.Push(Line(1), Line(2));
            old.Push(Line(2), Line(3));
            Ui.WaitFor(() => Console(window).Contains("line 3", StringComparison.Ordinal), "both overlapping batches");
            Assert.Equal(1, Console(window).Split('\n').Count(line => line.Trim() == "[apiservice] line 2"));
            Assert.False(old.Closed.Task.IsCompleted);
            backend.PushResource(new Resource { Name = "apiservice", DisplayName = "apiservice", Uid = "second", ResourceType = "Project", State = "Running" });
            Ui.WaitFor(() => old.Closed.Task.IsCompleted && backend.Consoles.Length == 2, "the old console subscription to be ended and replaced");
            AspireInteractionHost.ConsoleSubscription next = backend.Consoles[1];
            next.Push(new ConsoleLogLine { Text = "new generation", LineNumber = 1 });
            old.Push(new ConsoleLogLine { Text = "stale generation", LineNumber = 4 });
            Ui.WaitFor(() => Console(window).Contains("new generation", StringComparison.Ordinal), "the new generation's output");
            Assert.DoesNotContain("stale generation", Console(window), StringComparison.Ordinal);
            Assert.DoesNotContain("line 2", Console(window), StringComparison.Ordinal);
            next.Push(
                new ConsoleLogLine { Text = $"{project.Program}(3,4): error ASPIRETEST: request source", LineNumber = 2 },
                new ConsoleLogLine { Text = "See https://example.com/aspire/help.", LineNumber = 3 });
            Ui.WaitFor(() => Console(window).Contains("https://example.com/aspire/help", StringComparison.Ordinal), "the console's source and web links");
            ClickConsoleLink(window, "example.com");
            Assert.Equal("https://example.com/aspire/help", Assert.Single(window.Browsed).AbsoluteUri);
            ClickConsoleLink(window, project.Program);
            Ui.WaitFor(() => window.EditorArea.Active is { } active && PathNames.Same(PathNames.Canonical(project.Program), active.Path)
                && window.EditorArea.CodeEditor.CaretPosition == new TextPosition(2, 3), "the console's source link to open its file at its line and column");
            Assert.Single(window.Browsed);

            // Hidden, the window follows no console, and asks the CLI for nothing in its place.
            FindAction(window, "Show Aspire Console");
            ChildProcesses.Started += observe;
            Ui.Click(window.Aspire.Console.Editor);
            Ui.Run(window, AppActions.HideToolWindow);
            Ui.WaitFor(() => next.Closed.Task.IsCompleted, "hiding the Aspire window to end its console's subscription");
            Ui.Settle();
            lock (started)
            {
                Assert.DoesNotContain(started, command => command.Contains("aspire", StringComparison.OrdinalIgnoreCase)
                    && (command.Contains("resource", StringComparison.OrdinalIgnoreCase) || command.Contains("telemetry", StringComparison.OrdinalIgnoreCase)));
            }
            Assert.True(window.IsRunning, "Hiding the Aspire window stopped the AppHost");
            Assert.Empty(backend.Errors);
        }
        finally
        {
            ChildProcesses.Started -= observe;
            window.Close();
            Ui.Await(Complete(backend.DisposeAsync().AsTask()), "the stand-in resource service to stop");
        }

        static ConsoleLogLine Line(int number) => new() { Text = $"line {number}", LineNumber = number };
    }

    [AvaloniaFact]
    public void A_resource_update_in_the_background_keeps_the_chosen_parent_and_its_children_open()
    {
        project.Reset();
        AspireInteractionHost backend = Ui.Await(AspireInteractionHost.StartAsync(project.KeyFile), "the stand-in resource service");
        backend.PushResource(new Resource
        {
            Name = "apiservice", DisplayName = "apiservice", Uid = "api", ResourceType = "Project", State = "Running",
            HealthReports = { new HealthReport { Key = "ready", Status = HealthStatus.Healthy } },
        });
        Resource worker = new()
        {
            Name = "worker", DisplayName = "worker", Uid = "worker", ResourceType = "Executable", State = "Running",
            Relationships = { new ResourceRelationship { Type = "Parent", ResourceName = "apiservice" } },
        };
        backend.PushResource(worker);
        MainWindow window = OpenRunning(backend);
        try
        {
            FindAction(window, "Show Aspire Resources");
            Ui.WaitFor(() => window.Aspire.ResourceSnapshots.Count == 2, "the parent and its child");
            TreeViewItem parent = window.Aspire.ResourceList.Items.OfType<TreeViewItem>().Single();
            Assert.Equal("apiservice", Assert.IsType<AspireResource>(parent.Tag).Name);
            Assert.Equal("worker", Assert.IsType<AspireResource>(Assert.Single(parent.Items.OfType<TreeViewItem>()).Tag).Name);
            Ui.Click(parent.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "apiservice"));
            Ui.Press(window, "RIGHT");
            Ui.WaitFor(() => parent.IsExpanded, "Right to open the parent");
            Assert.Equal("apiservice", window.Aspire.SelectedResource?.Name);
            Assert.Contains(parent.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "worker");
            worker.State = "Stopped";
            backend.PushResource(worker);
            Ui.WaitFor(() => window.Aspire.ResourceSnapshots.Any(resource => resource.Name == "worker" && resource.State == "Stopped"),
                "the child's update");
            Assert.Equal("apiservice", window.Aspire.SelectedResource?.Name);
            parent = window.Aspire.ResourceList.Items.OfType<TreeViewItem>().Single();
            Assert.True(parent.IsExpanded, "The child's update closed its parent");
            Assert.Contains(parent.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "Healthy");
            Assert.Empty(backend.Errors);
        }
        finally
        {
            window.Close();
            Ui.Await(Complete(backend.DisposeAsync().AsTask()), "the stand-in resource service to stop");
        }
    }

    [AvaloniaFact]
    public void A_confirmation_cancelled_and_a_form_the_AppHost_updates_send_what_the_dialog_shows()
    {
        project.Reset();
        AspireInteractionHost backend = Ui.Await(AspireInteractionHost.StartAsync(project.KeyFile), "the stand-in resource service");
        MainWindow window = OpenRunning(backend);
        try
        {
            backend.Push(new WatchInteractionsResponseUpdate
            {
                InteractionId = 1,
                Title = "Restart API?",
                Message = "Confirm restart",
                ShowSecondaryButton = true,
                Notification = new InteractionNotification { Intent = MessageIntent.Confirmation },
            });
            AspireInteractionDialog confirmation = Interaction(window, "Restart API?");
            Ui.Click(confirmation.Secondary);
            Ui.WaitFor(() => backend.Answers.Length == 1, "the confirmation's answer");
            WatchInteractionsRequestUpdate canceled = Assert.Single(backend.Answers);
            Assert.Equal(WatchInteractionsRequestUpdate.KindOneofCase.Notification, canceled.KindCase);
            Assert.False(canceled.Notification.Result);
            CompleteInteraction(backend, window, 1);

            backend.Push(Form("before"));
            AspireInteractionDialog form = Interaction(window, "API settings");
            TextBox input = Assert.IsType<TextBox>(form.InputControls["value"]);
            Ui.Click(input);
            Ui.WaitFor(() => backend.Answers.Any(answer => answer.InteractionId == 2
                && answer.KindCase == WatchInteractionsRequestUpdate.KindOneofCase.InputsDialog
                && answer.ResponseUpdate && answer.InputsDialog.InputItems[0].Value == "before"),
                "the form's first value");
            int baseline = backend.Answers.Length;
            AspireInteractionHost.WireBarrier sending = backend.HoldNextAnswer();
            string firstValue = new string('x', 256 * 1024) + "typed while connected";
            ReplaceDuringWork(form, firstValue);
            Ui.PumpUntil(() => sending.Entered.Task.IsCompleted, "the typed value's update to reach the held frame");
            Assert.Equal(baseline, backend.Answers.Length);
            WatchInteractionsResponseUpdate update = Form("server update");
            update.InputsDialog.InputItems[0].ValidationErrors.Add("Choose another value");
            backend.Push(update);
            Ui.PumpUntil(() => form.Problem.Text?.Contains("Choose another value", StringComparison.Ordinal) == true,
                "the AppHost's update while the first answer is still on its way");
            input = Assert.IsType<TextBox>(form.InputControls["value"]);
            Assert.Equal(firstValue, input.Text);
            Ui.Click(input, settle: false);
            ReplaceDuringWork(form, "superseded value");
            ReplaceDuringWork(form, "latest value");
            sending.Release.TrySetResult();
            Ui.WaitFor(() => backend.Answers.Length == baseline + 2, "the first update and the latest value after it");
            WatchInteractionsRequestUpdate[] updates = backend.Answers.Skip(baseline).ToArray();
            Assert.Equal(firstValue, updates[0].InputsDialog.InputItems[0].Value);
            Assert.Equal("latest value", updates[1].InputsDialog.InputItems[0].Value);
            Assert.All(updates, answer =>
            {
                Assert.Equal(2, answer.InteractionId);
                Assert.Equal(WatchInteractionsRequestUpdate.KindOneofCase.InputsDialog, answer.KindCase);
                Assert.True(answer.ResponseUpdate);
            });
            Ui.Click(form.Primary);
            Ui.WaitFor(() => backend.Answers.Length == baseline + 3, "the form's answer");
            Assert.False(backend.Answers[^1].ResponseUpdate);
            Assert.Equal("latest value", backend.Answers[^1].InputsDialog.InputItems[0].Value);
            CompleteInteraction(backend, window, 2);
            Assert.Empty(backend.Errors);
        }
        finally
        {
            window.Close();
            Ui.Await(Complete(backend.DisposeAsync().AsTask()), "the stand-in resource service to stop");
        }
    }

    /// <summary>A terminal prompt, which Passenger cannot give, closed by its button, by Escape and by the window's close, each
    /// answered as cancelled for the request it shows last, the connection and the next prompt going on.</summary>
    [AvaloniaFact]
    public void A_terminal_prompt_closed_any_way_is_answered_cancelled_and_ordinary_prompts_go_on()
    {
        project.Reset();
        AspireInteractionHost backend = Ui.Await(AspireInteractionHost.StartAsync(project.KeyFile), "the stand-in resource service");
        MainWindow window = OpenRunning(backend);
        try
        {
            int id = 900;
            foreach (string close in (string[])["button", "escape", "window"])
            {
                WatchInteractionsResponseUpdate update = new()
                {
                    InteractionId = id,
                    Title = $"Open a terminal ({close})",
                    PrimaryButtonText = "Open terminal",
                    PromptTerminal = new InteractionPromptTerminal { TerminalId = "terminal-before" },
                };
                backend.Push(update);
                AspireInteractionDialog terminal = Interaction(window, update.Title);
                Assert.Equal("Close", terminal.Primary.Content);
                Assert.Contains(terminal.GetVisualDescendants().OfType<TextBlock>(), block =>
                    block.Text?.Contains("Terminal interactions are not supported", StringComparison.Ordinal) == true);
                update.PromptTerminal.TerminalId = "terminal-current";
                update.Title = $"Current terminal request ({close})";
                backend.Push(update);
                Ui.WaitFor(() => terminal.Title?.EndsWith(" · " + update.Title, StringComparison.Ordinal) == true,
                    "the prompt's newer request");
                int answered = backend.Answers.Length;
                switch (close)
                {
                    case "button":
                        Ui.Click(terminal.Primary);
                        break;
                    case "escape":
                        Ui.Press(terminal, "ESCAPE");
                        break;
                    default:
                        // The system's close, which the headless platform draws no button for.
                        terminal.Close();
                        break;
                }
                Ui.WaitFor(() => backend.Answers.Length == answered + 1, $"the terminal prompt closed by its {close} to be answered");
                WatchInteractionsRequestUpdate rejected = backend.Answers[^1];
                Assert.Equal(id, rejected.InteractionId);
                Assert.Equal(WatchInteractionsRequestUpdate.KindOneofCase.PromptTerminal, rejected.KindCase);
                Assert.Equal("terminal-current", rejected.PromptTerminal.TerminalId);
                Assert.True(rejected.PromptTerminal.HasResult);
                Assert.False(rejected.PromptTerminal.Result);
                Assert.False(backend.Disconnected.Task.IsCompleted, $"Closing a terminal prompt by its {close} ended the connection");
                CompleteInteraction(backend, window, id);
                id++;
            }

            backend.Push(new WatchInteractionsResponseUpdate
            {
                InteractionId = id,
                Title = "Continue normally",
                MessageBox = new InteractionMessageBox { Intent = MessageIntent.Confirmation },
            });
            AspireInteractionDialog ordinary = Interaction(window, "Continue normally");
            int before = backend.Answers.Length;
            Ui.Click(ordinary.Primary);
            Ui.WaitFor(() => backend.Answers.Length == before + 1, "the ordinary prompt's answer");
            Assert.Equal(id, backend.Answers[^1].InteractionId);
            Assert.Equal(WatchInteractionsRequestUpdate.KindOneofCase.MessageBox, backend.Answers[^1].KindCase);
            Assert.True(backend.Answers[^1].MessageBox.Result);
            Assert.False(backend.Disconnected.Task.IsCompleted);
            CompleteInteraction(backend, window, id);
            Assert.Empty(backend.Errors);
        }
        finally
        {
            window.Close();
            Ui.Await(Complete(backend.DisposeAsync().AsTask()), "the stand-in resource service to stop");
        }
    }

    [AvaloniaFact]
    public void A_file_input_the_AppHost_replaces_refuses_the_upload_under_way_and_takes_the_file_chosen_again()
    {
        project.Reset();
        AspireInteractionHost backend = Ui.Await(AspireInteractionHost.StartAsync(project.KeyFile), "the stand-in resource service");
        MainWindow window = OpenRunning(backend);
        try
        {
            backend.Push(FileForm());
            AspireInteractionDialog dialog = Interaction(window, "Upload API settings");
            PickUploadFile(window, dialog, project.Program);
            Ui.PumpUntil(() => backend.FirstUpload.Entered.Task.IsCompleted, "the chosen file's upload to reach the held reply");
            UploadFileChunk[] oldUpload = Assert.Single(backend.Uploads);
            Assert.Equal(3, oldUpload[0].InteractionId);
            Assert.Equal("attachment", oldUpload[0].InputName);
            Assert.Equal(Path.GetFileName(project.Program), oldUpload[0].FileName);
            Assert.Equal(File.ReadAllBytes(project.Program), oldUpload.SelectMany(chunk => chunk.Data.ToByteArray()).ToArray());
            WatchInteractionsResponseUpdate update = FileForm();
            update.Message = "The server replaced this file input";
            backend.Push(update);
            Ui.PumpUntil(() => dialog.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == update.Message),
                "the replaced file input while the first upload is held");
            Assert.Same(window, dialog.Owner);
            backend.FirstUpload.Release.TrySetResult();
            Ui.WaitFor(() => dialog.Problem.IsEffectivelyVisible && dialog.Problem.Text!.Contains("Choose the file again", StringComparison.Ordinal),
                "the dialog to refuse the upload made for the input replaced");
            Ui.Click(dialog.Primary);
            Ui.WaitFor(() => backend.Answers.Length == 1, "the answer with no file");
            WatchInteractionsRequestUpdate rejected = Assert.Single(backend.Answers);
            Assert.Equal(string.Empty, Assert.Single(rejected.InputsDialog.InputItems).Value);
            Assert.DoesNotContain("stale-file-id", rejected.ToString(), StringComparison.Ordinal);

            PickUploadFile(window, dialog, project.ProjectFile);
            Ui.WaitFor(() => Equals(((Button)dialog.InputControls["attachment"]).Content, "1 file(s) uploaded"), "the second upload to finish");
            Assert.Equal(2, backend.Uploads.Length);
            Assert.Equal(File.ReadAllBytes(project.ProjectFile), backend.Uploads[1].SelectMany(chunk => chunk.Data.ToByteArray()).ToArray());
            Ui.Click(dialog.Primary);
            Ui.WaitFor(() => backend.Answers.Length == 2, "the answer with the file chosen again");
            Assert.False(backend.Answers[^1].ResponseUpdate);
            string value = Assert.Single(backend.Answers[^1].InputsDialog.InputItems).Value;
            using JsonDocument references = JsonDocument.Parse(value);
            JsonElement reference = Assert.Single(references.RootElement.EnumerateArray());
            Assert.Equal("current-file-id", reference.GetProperty("Id").GetString());
            Assert.Equal(Path.GetFileName(project.ProjectFile), reference.GetProperty("Name").GetString());
            Assert.DoesNotContain("stale-file-id", backend.Answers[^1].ToString(), StringComparison.Ordinal);
            CompleteInteraction(backend, window, 3);
            Assert.Empty(backend.Errors);
        }
        finally
        {
            window.Close();
            Ui.Await(Complete(backend.DisposeAsync().AsTask()), "the stand-in resource service to stop");
        }

        static WatchInteractionsResponseUpdate FileForm() => new()
        {
            InteractionId = 3,
            Title = "Upload API settings",
            InputsDialog = new InteractionInputsDialog
            {
                InputItems = { new InteractionInput { Name = "attachment", Label = "Settings file", InputType = InputType.File } },
            },
        };
    }

    /// <summary>A window on the stand-in AppHost, run from its Aspire configuration as a person runs it, once the window has
    /// connected to the stand-in resource service its launch profile names; no C# language server, which nothing here asks.</summary>
    private MainWindow OpenRunning(AspireInteractionHost backend)
    {
        project.Serve(backend);
        MainWindow window = project.Open(project.Program, languageServer: false);
        try
        {
            Ui.WaitFor(() => window.Configurations.Any(configuration => configuration.Name == StandInAppHost.Configuration),
                "the stand-in AppHost's Aspire configuration");
            Assert.True(window.SelectConfiguration(StandInAppHost.Configuration));
            Ui.Run(window, AppActions.RunProgram);
            Ui.WaitFor(() => backend.Connected.Task.IsCompleted && window.AspireRuntime.Apps.Count == 1,
                $"the window to connect to the stand-in AppHost's resource service ({window.Status})");
            Assert.True(PathNames.Same(PathNames.Canonical(project.ProjectFile), Assert.Single(window.AspireRuntime.Apps).Configuration.Project));
            return window;
        }
        catch
        {
            window.Close();
            Ui.Await(Complete(backend.DisposeAsync().AsTask()), "the stand-in resource service to stop");
            throw;
        }
    }

    private static string Console(MainWindow window) => window.Aspire.Console.Document.Snapshot.ToString();

    /// <summary>Chooses a file for the prompt's file input as a person does in the window's file dialog, which the test
    /// answers for (<see cref="MainWindow.PickFiles"/>): one file, asked for by the input's label.</summary>
    private static void PickUploadFile(MainWindow window, AspireInteractionDialog dialog, string path)
    {
        List<(string Title, bool Several)> asked = [];
        window.PickFiles = (title, _, several) =>
        {
            asked.Add((title, several));
            return Task.FromResult<IReadOnlyList<string>>([path]);
        };
        Ui.Click(dialog.InputControls["attachment"], settle: false);
        Ui.PumpUntil(() => asked.Count == 1, "the file input to ask for a file");
        Assert.Equal(("Settings file", false), asked[0]);
    }

    internal static AspireInteractionDialog Interaction(MainWindow window, string title)
    {
        Ui.WaitFor(() => window.OwnedWindows.OfType<AspireInteractionDialog>().Any(dialog => dialog.IsVisible && dialog.Title?.EndsWith(" · " + title, StringComparison.Ordinal) == true),
            $"the AppHost's {title} prompt");
        AspireInteractionDialog dialog = Assert.Single(window.OwnedWindows.OfType<AspireInteractionDialog>(), dialog => dialog.Title?.EndsWith(" · " + title, StringComparison.Ordinal) == true);
        Assert.Same(window, dialog.Owner);
        return DialogButtonsTests.Check(dialog);
    }

    private static void CompleteInteraction(AspireInteractionHost backend, MainWindow window, int id)
    {
        backend.Push(new WatchInteractionsResponseUpdate { InteractionId = id, Complete = new InteractionComplete() });
        Ui.WaitFor(() => !window.OwnedWindows.OfType<AspireInteractionDialog>().Any(dialog => dialog.InteractionId == id),
            "the prompt the AppHost completed to close");
    }

    /// <summary>Select All (the headless platform's, Ctrl+A, on every system) and typing, pressed without waiting for the window
    /// to settle, since a held answer holds it.</summary>
    private static void ReplaceDuringWork(TopLevel window, string text)
    {
        window.KeyPress(Avalonia.Input.Key.A, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
        window.KeyRelease(Avalonia.Input.Key.A, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
        window.KeyTextInput(text);
    }

    private static void ClickConsoleLink(MainWindow window, string text)
    {
        Passenger.Editor.CodeEditor editor = window.Aspire.Console.Editor;
        int offset = editor.Document.Snapshot.ToString().IndexOf(text, StringComparison.Ordinal);
        Assert.True(offset >= 0, $"The Aspire console has no {text} link");
        int line = editor.Document.Snapshot.GetLineFromOffset(offset);
        editor.ScrollTo(new Vector(0, Math.Max(0, line * editor.LineHeight - editor.Bounds.Height / 3)));
        Ui.Settle();
        Point at = editor.PointOf(offset) + new Vector(2, editor.LineHeight / 2);
        window.MouseMove(editor.TranslatePoint(at, window)!.Value);
        Ui.WaitFor(() => editor.HoveredLink is { } link && link.Range.Start <= offset && offset < link.Range.End,
            $"the console's link for {text} under the pointer");
        Ui.Click(editor, at);
    }

    private static void FindAction(MainWindow window, string title)
    {
        Ui.Run(window, AppActions.FindAction);
        window.KeyTextInput(title);
        Ui.Settle();
        Passenger.App.Navigation.ActionPopup popup = Assert.IsType<Passenger.App.Navigation.ActionPopup>(window.Popup);
        Assert.Equal(title, popup.Rows[0].Title);
        Ui.Press(window, "ENTER");
    }

    private static WatchInteractionsResponseUpdate Form(string value) => new()
    {
        InteractionId = 2,
        Title = "API settings",
        InputsDialog = new InteractionInputsDialog
        {
            InputItems = { new InteractionInput { Name = "value", Label = "Value", InputType = InputType.Text, Value = value, UpdateStateOnChange = true } },
        },
    };

    private static async Task<bool> Complete(Task task)
    {
        await task;
        return true;
    }
}

/// <summary>A stand-in AppHost: a console project the IDE runs as one (it sets IsAspireHost), which writes the resource service
/// key the IDE gave it where its stand-in resource service reads it (<see cref="AspireInteractionHost.KeyFile"/>) and waits to
/// be stopped; its launch profile, written for each test, names that stand-in's address.</summary>
public sealed class StandInAppHost : SharedProject
{
    public const string Name = "StandIn.AppHost";
    public const string Configuration = $"Aspire: {Name}: http";

    public StandInAppHost() : base("aspire-stand-in-")
    {
        // The version the AppHost SDK would set, without which the .NET SDK takes the project for a workload-era AppHost.
        TestFolders.Write(Folder, $"{Name}/{Name}.csproj", TestProjects.Console("<IsAspireHost>true</IsAspireHost>",
            $"<AspireHostingSDKVersion>{AspireApp.TemplatesVersion}</AspireHostingSDKVersion>"));
        TestFolders.Write(Folder, $"{Name}/Program.cs", """
            using System;
            using System.IO;
            using System.Threading;

            string keyFile = Environment.GetEnvironmentVariable("PASSENGER_STAND_IN_KEY")!;
            File.WriteAllText(keyFile + ".part", Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_RESOURCESERVICE_APIKEY"));
            File.Move(keyFile + ".part", keyFile, overwrite: true);
            Console.WriteLine("The stand-in AppHost is up");
            Thread.Sleep(Timeout.Infinite);

            """);
        TestFolders.Write(Folder, $"{Name}/Properties/launchSettings.json", Profile(null));
        TestFolders.Write(Folder, "StandIn.slnx", $"<Solution>\n  <Project Path=\"{Name}/{Name}.csproj\" />\n</Solution>\n");
        Prepare();
    }

    public string ProjectFile => PathOf($"{Name}/{Name}.csproj");

    public string Program => PathOf($"{Name}/Program.cs");

    public string KeyFile => PathOf("resource-service.key");

    /// <summary>The AppHost's launch profile, naming the stand-in resource service's address, as an AppHost's names its own.</summary>
    internal void Serve(AspireInteractionHost backend) => Write($"{Name}/Properties/launchSettings.json", Profile(backend.Endpoint));

    private string Profile(Uri? resourceService)
    {
        Dictionary<string, string> environment = new()
        {
            ["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true",
            ["PASSENGER_STAND_IN_KEY"] = KeyFile,
        };
        if (resourceService is not null)
        {
            environment["ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL"] = resourceService.AbsoluteUri;
        }
        return JsonSerializer.Serialize(new { profiles = new { http = new { commandName = "Project", environmentVariables = environment } } });
    }
}
