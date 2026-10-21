using System.Text.RegularExpressions;
using Avalonia.Controls;
using Passenger.App.Run;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App;

/// <summary>Something a run configuration runs from a line of an open file: a Compose configuration's service on its line
/// of a compose file the configuration reads, or a Dockerfile configuration's image on its Dockerfile's final FROM.</summary>
public sealed record RunTarget(int Line, RunConfiguration Configuration, string? Service);

/// <summary>The gutter's run icons beside what the folder's container configurations run: each service of a compose
/// file a Compose configuration reads, and the final FROM of a Dockerfile a Dockerfile configuration builds, only in files
/// a configuration uses. A click opens the menu that runs it (and Debug, greyed until containers are debugged). They are
/// read from the file's text as typed, once typing pauses, and again as the configurations change.</summary>
public partial class MainWindow
{
    private Debounce? runMarksLater;

    private void AddRunMarks()
    {
        runMarksLater = new Debounce(TimeSpan.FromMilliseconds(300), () =>
        {
            ShowRunMarks();
            return Task.CompletedTask;
        });
        Editors.Opened += (_, document) =>
        {
            document.Text.Changed += (_, _) =>
            {
                if (document.RunMarks.Count > 0 || IsRunFile(document.Path))
                {
                    runMarksLater.Restart();
                }
            };
            ShowRunMarks(document);
        };
    }

    /// <summary>Whether a file is one a run icon may stand in: a compose file or a Dockerfile.</summary>
    private static bool IsRunFile(string path) => Web.ComposeDeclarations.IsCompose(path) || Web.WebLanguages.IsDockerfile(Path.GetFileName(path));

    private void ShowRunMarks()
    {
        foreach (OpenDocument document in Editors.Documents)
        {
            ShowRunMarks(document);
        }
    }

    private void ShowRunMarks(OpenDocument document)
    {
        RunMark[] marks = [.. RunTargets(document).Select(t => t.Line).Distinct().Select(l => new RunMark(l))];
        if (!marks.SequenceEqual(document.RunMarks))
        {
            document.RunMarks = marks;
        }
    }

    /// <summary>What the folder's configurations run from an open file's lines.</summary>
    public IReadOnlyList<RunTarget> RunTargets(OpenDocument document)
    {
        string path = document.Path;
        if (!IsRunFile(path))
        {
            return [];
        }
        TextSnapshot text = document.Text.Snapshot;
        List<RunTarget> targets = [];
        foreach (RunConfiguration configuration in Configurations)
        {
            if (configuration.Kind == RunKind.Compose && configuration.ComposeFiles.Any(f => Core.PathNames.Same(f, path)))
            {
                targets.AddRange(ServiceLines(text).Select(s => new RunTarget(s.Line, configuration, s.Service)));
            }
            else if (configuration.Kind == RunKind.Dockerfile && DockerfileOf(configuration) is { } dockerfile && Core.PathNames.Same(dockerfile, path)
                && FinalFrom(text) is int from)
            {
                targets.Add(new RunTarget(from, configuration, null));
            }
        }
        return targets;
    }

    /// <summary>The Dockerfile a Dockerfile configuration builds: its own, else its project's beside it.</summary>
    private string? DockerfileOf(RunConfiguration configuration) => configuration.Dockerfile.Length > 0 ? configuration.Dockerfile
        : Projects.FirstOrDefault(p => Core.PathNames.Same(p.Path, configuration.Project)) is { } project
            ? Containers.ContainerConfigurations.Dockerfile(project, Containers.ContainerConfigurations.Text(project.Path) ?? string.Empty)
            : null;

    /// <summary>Each service's line of a compose file: the keys one level under the top-level <c>services:</c>.</summary>
    private static IEnumerable<(int Line, string Service)> ServiceLines(TextSnapshot text)
    {
        int indent = -1;
        bool inServices = false;
        for (int line = 0; line < text.LineCount; line++)
        {
            string row = text.GetLineText(line);
            if (row.Trim().Length == 0 || row.TrimStart().StartsWith('#'))
            {
                continue;
            }
            int at = row.Length - row.TrimStart().Length;
            if (at == 0)
            {
                inServices = TopKey().Match(row) is { Success: true } top && top.Groups["key"].Value == "services";
                indent = -1;
                continue;
            }
            if (!inServices)
            {
                continue;
            }
            indent = indent < 0 ? at : indent;
            if (at == indent && ServiceKey().Match(row) is { Success: true } key)
            {
                yield return (line, key.Groups["key"].Value);
            }
        }
    }

    /// <summary>A Dockerfile's final FROM, the image it makes.</summary>
    private static int? FinalFrom(TextSnapshot text)
    {
        int? last = null;
        for (int line = 0; line < text.LineCount; line++)
        {
            if (From().IsMatch(text.GetLineText(line)))
            {
                last = line;
            }
        }
        return last;
    }

    /// <summary>The run icon's menu: Run of each configuration that runs the line (one service of a Compose
    /// configuration, or a Dockerfile's), and Debug, greyed with why while containers are not debugged.</summary>
    private void ShowRunMarkMenu(CodeEditor editor, OpenDocument document, int line)
    {
        RunTarget[] here = [.. RunTargets(document).Where(t => t.Line == line)];
        if (here.Length == 0)
        {
            return;
        }
        MenuFlyout menu = new();
        foreach (RunTarget target in here)
        {
            string what = target.Service ?? target.Configuration.Name;
            string from = here.Length > 1 && target.Service is not null ? $" ({target.Configuration.Name})" : string.Empty;
            MenuItem run = new() { Header = $"Run '{what}'{from}" };
            run.Click += (_, _) => RunTarget(target);
            MenuItem debug = new() { Header = $"Debug '{what}'{from}", IsEnabled = false };
            ToolTip.SetTip(debug, LaunchPlan.InContainers(target.Configuration.Name));
            ToolTip.SetShowOnDisabled(debug, true);
            menu.Items.Add(run);
            menu.Items.Add(debug);
        }
        RunMarkMenu = menu;
        menu.Placement = PlacementMode.Pointer;
        menu.ShowAt(editor, showAtPointer: true);
    }

    /// <summary>The run icon's menu last opened, for a test to choose from.</summary>
    public MenuFlyout? RunMarkMenu { get; private set; }

    /// <summary>Runs what a run icon stands beside: a Compose configuration's one service (with what it depends on), or a
    /// Dockerfile configuration; what stopped it, if anything, is in the status bar.</summary>
    public Task<string?> RunTarget(RunTarget target) => buildRun.RunContainersAsync(target.Service is { } service
        ? target.Configuration.Copied() with { Name = $"{target.Configuration.Name}: {service}", Services = [service], LaunchService = service }
        : target.Configuration);

    [GeneratedRegex(@"^(?<key>[A-Za-z0-9_.-]+)\s*:")]
    private static partial Regex TopKey();

    [GeneratedRegex(@"^\s+[""']?(?<key>[A-Za-z0-9_.-]+)[""']?\s*:(?:\s|$)")]
    private static partial Regex ServiceKey();

    [GeneratedRegex(@"^\s*FROM\s", RegexOptions.IgnoreCase)]
    private static partial Regex From();
}
