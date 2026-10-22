using System.Text.RegularExpressions;
using Avalonia.Controls;
using Passenger.App.Run;
using Passenger.App.Workspace;
using Passenger.App.Aspire;
using Passenger.Editor;
using Passenger.Roslyn.Extension;
using Passenger.Text;

namespace Passenger.App;

/// <summary>Something a run configuration runs from a line of an open file: a Compose configuration's service on its line
/// of a compose file the configuration reads, or a Dockerfile configuration's image on its Dockerfile's final FROM.</summary>
public sealed record RunTarget(int Line, RunConfiguration Configuration, string? Service, AspireRunResource? Declaration = null);

/// <summary>The gutter's run icons beside what the folder's container configurations run: each service of a compose
/// file a Compose configuration reads, and the final FROM of a Dockerfile a Dockerfile configuration builds, only in files
/// a configuration uses. A click opens the menu that runs or debugs it. They are
/// read from the file's text as typed, once typing pauses, and again as the configurations change.</summary>
public partial class MainWindow
{
    private Debounce? runMarksLater;
    private CancellationTokenSource? readingAspireMarks;
    private readonly Dictionary<string, (int Version, IReadOnlyList<AspireRunResource> Resources)> aspireRunMarks = new(Core.PathNames.Comparer);

    private void AddRunMarks()
    {
        runMarksLater = new Debounce(TimeSpan.FromMilliseconds(300), () =>
        {
            _ = ReadRunMarksAsync();
            return Task.CompletedTask;
        });
        Closed += (_, _) =>
        {
            runMarksLater.Stop();
            readingAspireMarks?.Cancel();
        };
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
            if (IsRunFile(document.Path))
            {
                runMarksLater.Restart();
            }
        };
    }

    /// <summary>Whether a file is one a run icon may stand in: a compose file or a Dockerfile.</summary>
    private bool IsRunFile(string path) => Web.ComposeDeclarations.IsCompose(path) || Web.WebLanguages.IsDockerfile(Path.GetFileName(path))
        || Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase) && Configurations.Any(session.Aspire.IsAspire);

    private async Task ReadRunMarksAsync()
    {
        using IDisposable held = BackgroundWork.Scope();
        readingAspireMarks?.Cancel();
        readingAspireMarks?.Dispose();
        using CancellationTokenSource cancel = new();
        CancellationToken token = cancel.Token;
        readingAspireMarks = cancel;
        ProjectSession current = session;
        int? version = current.CSharp?.AspireVersion;
        aspireRunMarks.Clear();
        ShowRunMarks();
        try
        {
            if (current.IsEnded || version is null || current.CSharp is not { IsReady: true })
            {
                return;
            }
            RunConfiguration[] configurations = [.. Configurations.Where(current.Aspire.IsAspire)
                .DistinctBy(configuration => configuration.Project, Core.PathNames.Comparer)];
            foreach (RunConfiguration configuration in configurations)
            {
                IReadOnlyList<AspireRunResource> resources = await ReadAspireConfigurationResourcesAsync(configuration, token).ConfigureAwait(false);
                bool currentVersion = await UiThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested || session != current || current.IsEnded || current.CSharp?.AspireVersion != version)
                    {
                        return false;
                    }
                    aspireRunMarks[configuration.Project] = (version.Value, resources);
                    ShowRunMarks();
                    return true;
                }).WaitAsync(token).ConfigureAwait(false);
                if (!currentVersion)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or Lsp.LanguageServerFailed)
        {
        }
        finally
        {
            Interlocked.CompareExchange(ref readingAspireMarks, null, cancel);
        }
    }

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
            if (session.Aspire.IsAspire(configuration) && aspireRunMarks.TryGetValue(configuration.Project, out (int Version, IReadOnlyList<AspireRunResource> Resources) known)
                && session.CSharp?.AspireVersion == known.Version)
            {
                string hash = TextHash.Of(text.ToString());
                targets.AddRange(known.Resources.Where(resource => Core.PathNames.Same(resource.Source, path) && resource.SourceHash == hash)
                    .Select(resource => new RunTarget(resource.At.Line, configuration, resource.Name, resource)));
            }
            else if (configuration.Kind == RunKind.Compose && configuration.ComposeFiles.Any(f => Core.PathNames.Same(f, path)))
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
            ? Containers.ContainerConfigurations.Dockerfile(project)
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
    /// configuration, or a Dockerfile's), Debug and Profile of it; for a .NET service of the container run under way, its
    /// actions on that run instead (Debug, Attach, Run without Debugger, Profile), which leave the run up.</summary>
    private void ShowRunMarkMenu(CodeEditor editor, OpenDocument document, int line)
    {
        RunTarget[] here = [.. RunTargets(document).Where(t => t.Line == line)];
        if (here.Length == 0)
        {
            return;
        }
        MenuFlyout menu = new();
        HashSet<string> joined = new(StringComparer.Ordinal);
        foreach (RunTarget target in here)
        {
            string what = target.Service ?? target.Configuration.Name;
            if (!target.Configuration.Aspire && target.Service is { } service && InRunNow(document.Path, service))
            {
                if (joined.Add(service))
                {
                    foreach ((string id, string header) in (ReadOnlySpan<(string, string)>)[(AppActions.ServiceDebug, $"Debug '{service}'"),
                        (AppActions.ServiceAttach, $"Attach to '{service}'"), (AppActions.ServiceRun, $"Run '{service}' without Debugger"),
                        (AppActions.ServiceProfile, $"Profile '{service}'")])
                    {
                        MenuItem item = new() { Header = header };
                        item.Click += (_, _) => _ = ServiceActionAsync(id, service);
                        menu.Items.Add(item);
                    }
                }
                continue;
            }
            string from = here.Length > 1 && target.Service is not null ? $" ({target.Configuration.Name})" : string.Empty;
            MenuItem run = new() { Header = $"Run '{what}'{from}" };
            run.Click += (_, _) => RunTarget(target);
            MenuItem debug = new() { Header = $"Debug '{what}'{from}" };
            debug.Click += (_, _) => RunTarget(target, Containers.ContainerStart.Debug);
            MenuItem profile = new() { Header = $"Profile '{what}'{from}" };
            profile.Click += (_, _) => RunTarget(target, Containers.ContainerStart.Profile);
            menu.Items.Add(run);
            menu.Items.Add(debug);
            menu.Items.Add(profile);
        }
        RunMarkMenu = menu;
        menu.Placement = PlacementMode.Pointer;
        menu.ShowAt(editor, showAtPointer: true);
    }

    /// <summary>The run icon's menu last opened, for a test to choose from.</summary>
    public MenuFlyout? RunMarkMenu { get; private set; }

    /// <summary>Runs (or debugs, or profiles) what a run icon stands beside: a Compose configuration's one service (with what
    /// it depends on, the service alone debugged or profiled), or a Dockerfile configuration; what stopped it, if anything,
    /// is in the status bar.</summary>
    public Task<string?> RunTarget(RunTarget target, Containers.ContainerStart how = Containers.ContainerStart.Run)
    {
        if (target.Configuration.Aspire)
        {
            return RunAspireTargetAsync(target, how);
        }
        RunConfiguration configuration = target.Service is { } service
            ? target.Configuration.Copied() with
            {
                Name = $"{target.Configuration.Name}: {service}",
                Services = [service],
                LaunchService = service,
                ServiceActions = new Dictionary<string, ServiceStart>(target.Configuration.ServiceActions) { [service] = ServiceStart.Debug },
            }
            : target.Configuration;
        return how == Containers.ContainerStart.Profile ? ProfileAsync(configuration) : buildRun.RunContainersAsync(configuration, how);
    }

    private async Task<string?> RunAspireTargetAsync(RunTarget target, Containers.ContainerStart how)
    {
        ProjectSession current = session;
        if (target.Service is not { } name)
        {
            return "No Aspire project resource is selected";
        }
        IReadOnlyList<AspireRunResource> declared = await ReadAspireConfigurationResourcesAsync(target.Configuration, CancellationToken.None);
        if (session != current || current.IsEnded || target.Declaration is not { } original
            || !declared.Any(resource => resource == original)
            || !Editors.Documents.Any(document => Core.PathNames.Same(document.Path, original.Source)
                && TextHash.Of(document.Text.Snapshot.ToString()) == original.SourceHash))
        {
            return "The AppHost source changed; open its gutter action again";
        }
        if (current.Aspire.FindApp(target.Configuration.Project) is { } app)
        {
            IReadOnlyList<AspireResource> resources = await ReadAspireResourcesAsync(app.Configuration.Project);
            if (session != current || current.IsEnded)
            {
                return "The solution was closed";
            }
            AspireResource[] matches = [.. resources.Where(resource => resource.Name == name || resource.DisplayName == name)];
            if (matches.Length != 1)
            {
                return "The selected Aspire resource is unavailable or ambiguous";
            }
            AspireResource resource = matches[0];
            return how switch
            {
                Containers.ContainerStart.Debug => await DebugAspireResourceAsync(app.Configuration.Project, resource.Name),
                Containers.ContainerStart.Profile => await ProfileAspireResourceAsync(app.Configuration.Project, resource.Name),
                _ => await RestartAspireServiceAsync(current, new(app.Configuration.Project, resource), debugging: false),
            };
        }
        if (session != current || current.IsEnded || declared.Count(resource => resource.Name == name) != 1)
        {
            return "The selected AppHost resource changed";
        }
        RunConfiguration configuration = target.Configuration with
        {
            ServiceActions = declared.ToDictionary(resource => resource.Name,
                resource => resource.Name == name && how is Containers.ContainerStart.Debug or Containers.ContainerStart.Profile ? ServiceStart.Debug : ServiceStart.Run,
                StringComparer.Ordinal),
            ServiceProjects = declared.ToDictionary(resource => resource.Name, resource => resource.Project, StringComparer.Ordinal),
            LaunchService = name,
        };
        return how == Containers.ContainerStart.Profile ? await buildRun.ProfileAspireAsync(configuration)
            : await buildRun.RunAspireAsync(configuration, null, AspireRunMode.Run);
    }

    /// <summary>Whether a compose file's service is a .NET service of the container run under way, which reads that file.</summary>
    private bool InRunNow(string path, string service) =>
        buildRun.Containers is { } run && run.Plan.Compose.Any(word => Core.PathNames.Same(word, path)) && buildRun.ServiceOfRun(service).Service is not null;

    [GeneratedRegex(@"^(?<key>[A-Za-z0-9_.-]+)\s*:")]
    private static partial Regex TopKey();

    [GeneratedRegex(@"^\s+[""']?(?<key>[A-Za-z0-9_.-]+)[""']?\s*:(?:\s|$)")]
    private static partial Regex ServiceKey();

    [GeneratedRegex(@"^\s*FROM\s", RegexOptions.IgnoreCase)]
    private static partial Regex From();
}
