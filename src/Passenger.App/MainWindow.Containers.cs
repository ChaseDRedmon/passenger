using System.Text.Json;
using Passenger.App.Containers;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App;

/// <summary>The window's Containers: its tool window over the person's container engine, the actions on the row chosen
/// there (Start, Stop, Restart, Pause, Remove, Scale, Pull, Prune, Inspect, Open Shell), what the status bar and Problems
/// say of the engine when it cannot serve, and the engine's check before a run of tests that use Testcontainers.</summary>
public partial class MainWindow
{
    private ContainersView? containersView;

    /// <summary>The container engine the window asks: the person's, or a test's stand-in.</summary>
    public ContainerEngine Engine { get; init; } = ContainerEngine.Shared;

    /// <summary>The Containers tool window.</summary>
    public ContainersView ContainersWindow => containersView!;

    private void AddContainers()
    {
        ContainersView view = new(new ContainersParts(() => Engine, work, Say, Browse, () => StateFolder));
        containersView = view;
        view.ActionRequested += (_, action) => Request(action);
        view.LinkClicked += (_, link) => OpenLink(link);
        view.StateRead += (_, state) => ShowEngine(state);
        view.Files.Opened += (_, copy) => OpenFile(copy);
        view.Files.Problem += (_, problem) => status.Text = problem;
        Dock.Add(new Docking.ToolWindow("containers", "Containers", "Icon.Containers", view, Docking.ToolAnchor.BottomLeft)
        {
            Action = AppActions.ShowContainers,
            Menus = [new MenuPlace("Tools", 0, 4)],
            Chosen = () => view.Chosen,
        });
        ActionButtons.ShowMarked(view, actions!);
        string? OneContainer(ActionContext _) => view.Chosen?.Container is null ? "No container is chosen in the Containers window" : null;
        foreach ((string id, string title, string action) in (ReadOnlySpan<(string, string, string)>)[
            (AppActions.ContainerStart, "Start", "start"),
            (AppActions.ContainerStop, "Stop", "stop"),
            (AppActions.ContainerRestart, "Restart", "restart"),
            (AppActions.ContainerPause, "Pause or Unpause", "pause"),
            (AppActions.ContainerRemove, "Remove", "remove"),
            (AppActions.ContainerPull, "Pull", "pull")])
        {
            actions!.Add(new IdeAction(id, title, "Containers", context => _ = view.ActAsync(id, action, view.Chosen!))
            {
                WhyNot = _ => ContainerCommands.For(action, view.Chosen) is { Words: null } refused ? refused.WhyNot ?? $"{view.Chosen?.Name} cannot {action}" : null,
                Works = id,
            });
        }
        actions!.Add(new IdeAction(AppActions.ContainersRefresh, "Refresh", "Containers", context => _ = view.RefreshAsync()));
        actions.Add(new IdeAction(AppActions.ContainerScale, "Scale Service…", "Containers", context => _ = ScaleAsync(view.Chosen!))
        {
            WhyNot = _ => view.Chosen is { Kind: ContainerRowKind.Service, Project: not null } ? null : "No Compose service is chosen in the Containers window",
            Works = AppActions.ContainerScale,
        });
        actions.Add(new IdeAction(AppActions.ContainerPrune, "Prune…", "Containers", context => _ = PruneAsync(view.Chosen))
        {
            WhyNot = _ => Engine.Known is { IsReady: true } ? null : "The container engine is not running",
            Works = AppActions.ContainerPrune,
        });
        actions.Add(new IdeAction(AppActions.ContainerInspect, "Inspect", "Containers", context => _ = InspectAsync(view.Chosen!))
        {
            WhyNot = _ => view.Chosen is { Kind: not ContainerRowKind.Group and not ContainerRowKind.Project } ? null : "No container, image, volume or network is chosen in the Containers window",
        });
        actions.Add(new IdeAction(AppActions.ContainerShell, "Open Shell", "Containers", context => _ = OpenShellAsync(view.Chosen!.Container!)) { WhyNot = OneContainer, Words = ["docker exec", "Attach"] });
    }

    /// <summary>The window's folder opened: when it holds a Compose file or a Dockerfile, the engine is asked once, off the
    /// UI thread, so a missing or stopped one is said before anything needs it.</summary>
    private void CheckEngineFor(string? folder)
    {
        string[] names = ["compose.yaml", "compose.yml", "docker-compose.yml", "docker-compose.yaml", "Dockerfile"];
        if (folder is null || !names.Any(name => File.Exists(Path.Combine(folder, name))))
        {
            return;
        }
        ProjectSession asking = session;
        ContainerEngine engine = Engine;
        _ = ShowOnceRead();

        async Task ShowOnceRead()
        {
            EngineState state = await BackgroundWork.Run(() => engine.StateAsync());
            if (session == asking)
            {
                ShowEngine(state);
            }
        }
    }

    /// <summary>The engine's state, said in the status bar while it cannot serve (with where a click starts it, or shows
    /// what to do) and listed in Problems' Containers tab, which empties once it can.</summary>
    private void ShowEngine(EngineState state)
    {
        UiThread.Check();
        if (state.Problem is not { } problem)
        {
            Bottom.Problems.ShowContainers([]);
            return;
        }
        Bottom.Problems.ShowContainers([new Problem(ProblemSeverity.Warning, problem, null, null, new TextPosition(0, 0), state.Context ?? "Docker")]);
        status.Say(problem, ProblemSeverity.Warning, EnginePlace(state));
    }

    /// <summary>Where a click on the engine's problem goes: Docker Desktop started, where it is installed and the engine is
    /// not running; otherwise Problems' Containers tab, which says what to do.</summary>
    private StatusPlace EnginePlace(EngineState state) =>
        state.Status == EngineStatus.NotRunning && EngineState.DesktopApp is { } desktop
            ? new StatusPlace("start Docker Desktop", () => StartDesktop(desktop))
            : StatusPlace.Problems(Dock, Bottom.Problems, ProblemsTab.Containers);

    /// <summary>Starts Docker Desktop, which outlives the IDE: through LaunchServices on macOS, as a child that ends
    /// nothing of Passenger's on Windows.</summary>
    private void StartDesktop(string desktop)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                _ = ChildProcesses.RunAsync(ChildProcesses.Command("/usr/bin/open", ["-a", desktop]), limit: TimeSpan.FromSeconds(30));
            }
            else
            {
                ChildProcesses.StartApart(ChildProcesses.Command(desktop, []));
            }
            status.Text = "Starting Docker Desktop…";
        }
        catch (Exception failed) when (failed is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            status.Text = $"Docker Desktop did not start: {failed.Message}";
        }
    }

    /// <summary>A session's test runner asks the window's engine before a run of tests that use Testcontainers.</summary>
    private void BindContainers(ProjectSession next)
    {
        next.Tests.ContainerEngine = async cancel =>
        {
            EngineState state = await Engine.StateAsync(again: true, cancel);
            UiThread.Post(() =>
            {
                if (session == next)
                {
                    ShowEngine(state);
                }
            });
            return state.Status is EngineStatus.Ready or EngineStatus.NoCompose ? (null, state.Endpoint) : (state.Problem, null);
        };
        CheckEngineFor(next.Folder);
    }

    /// <summary>Prune, asked first: what the chosen heading (or the chosen object's kind) gathers that nothing uses.</summary>
    private async Task PruneAsync(ContainerRow? chosen)
    {
        string kind = chosen?.Object?.Kind ?? (chosen?.Kind == ContainerRowKind.Group ? chosen.Label : "container");
        (string what, IReadOnlyList<string> words) = ContainerCommands.Prune(kind);
        ConfirmDialog asked = new("Prune", $"Remove {what}? This cannot be undone.", "Prune");
        Asked = asked;
        if (!await asked.ShowDialog<bool>(this))
        {
            return;
        }
        ContainerRow row = chosen ?? new ContainerRow(ContainerRowKind.Group, 0, "group:Containers", "Containers");
        await containersView!.ActAsync(AppActions.ContainerPrune, "prune", row, words);
    }

    /// <summary>The question Prune asked last, for the tests.</summary>
    public ConfirmDialog? Asked { get; private set; }

    /// <summary>Scale: how many containers a Compose service runs, asked for, then <c>docker compose up --scale</c>.</summary>
    private async Task ScaleAsync(ContainerRow service)
    {
        string? count = await AskNameAsync($"Containers of {service.Name}", service.Containers.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            text => int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) && n is >= 0 and <= 100
                ? null : "A number from 0 to 100");
        if (count is null || service.Project is not { } project)
        {
            return;
        }
        await containersView!.ActAsync(AppActions.ContainerScale, "scale", service,
            [.. project.Arguments, "up", "-d", "--no-recreate", "--scale", $"{service.Service}={count}", service.Service!]);
    }

    /// <summary>Inspect: what the engine says of a container, image, volume or network, as JSON in a read-only tab.</summary>
    private async Task InspectAsync(ContainerRow row)
    {
        IReadOnlyList<string> words = row.Container is { } container ? ["inspect", container.Id]
            : row.Object is { Kind: "volume" } volume ? ["volume", "inspect", volume.Name]
            : row.Object is { Kind: "network" } network ? ["network", "inspect", network.Id]
            : row.Object is { } image ? ["image", "inspect", image.Id] : [];
        if (words.Count == 0)
        {
            return;
        }
        ContainerEngine engine = Engine;
        string copy = Path.Combine(StateFolder, "cache", "containers", "inspect", $"{string.Concat(row.Name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_'))}.json");
        string? problem = await BackgroundWork.Run(async () =>
        {
            try
            {
                JsonElement said = await engine.JsonAsync(words);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                await File.WriteAllTextAsync(copy, JsonSerializer.Serialize(said, new JsonSerializerOptions { WriteIndented = true }) + "\n");
                return null;
            }
            catch (Exception failed) when (failed is ContainerCommandException or TimeoutException or JsonException or IOException)
            {
                return failed.Message;
            }
        });
        if (problem is not null)
        {
            status.Text = $"{row.Name} could not be inspected: {problem}";
            return;
        }
        OpenFile(copy);
    }

    /// <summary>Open Shell: <c>docker exec -it … sh</c> in a Terminal tab of the container's name; an image with no shell
    /// is said, rather than a tab that ends at once.</summary>
    public async Task<TerminalRun?> OpenShellAsync(ContainerInfo container)
    {
        ContainerEngine engine = Engine;
        string? problem = await BackgroundWork.Run(async () =>
        {
            try
            {
                ChildResult tried = await engine.RunAsync(["exec", container.Id, "sh", "-c", "exit 0"]);
                string said = ContainerEngine.Said(tried);
                return tried.Succeeded ? null
                    : said.Contains("executable file not found", StringComparison.Ordinal) || said.Contains("no such file", StringComparison.OrdinalIgnoreCase)
                    ? $"{container.Image} has no shell (sh) to open in {container.Name}"
                    : said.Contains("is not running", StringComparison.Ordinal) ? $"Start {container.Name} to open a shell in it" : said;
            }
            catch (Exception failed) when (failed is TimeoutException or FileNotFoundException or System.ComponentModel.Win32Exception)
            {
                return failed.Message;
            }
        });
        if (problem is not null)
        {
            status.Text = problem;
            return null;
        }
        try
        {
            TerminalRun run = await terminal!.RunAsync($"{container.Name} (shell)", engine.Command(["exec", "-it", container.Id, "sh"]));
            terminal.FocusView();
            return run;
        }
        catch (LaunchException failed)
        {
            status.Text = failed.Message;
            return null;
        }
    }
}
