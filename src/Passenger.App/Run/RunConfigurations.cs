using Passenger.App.State;
using Passenger.App.Workspace;
using Passenger.Core;

namespace Passenger.App.Run;

/// <summary>A folder's run configurations: those its projects generate, then the person's own (kept with the folder's
/// state, their projects relative to the folder there), and the one chosen in the toolbar, which Run, Debug and Profile
/// launch. A UI-thread object of its <see cref="ProjectSession"/>.</summary>
public sealed class RunConfigurations(string? folder, Func<WorkspaceModel?>? workspace = null)
{
    private List<RunConfiguration> own = [];
    private StateDocument<RunConfigurationsState>? kept;

    /// <summary>The projects the list was last made from.</summary>
    private IReadOnlyList<ProjectModel> projects = [];

    /// <summary>Whether the one chosen was the person's choice (in the toolbar, Edit Configurations or the store), which
    /// stays chosen, rather than the first there was, which follows the projects as their evaluations land.</summary>
    private bool picked;

    /// <summary>Every configuration: the generated ones, then the folder's own.</summary>
    public IReadOnlyList<RunConfiguration> All { get; private set; } = [];

    /// <summary>The person's own, in their order.</summary>
    public IReadOnlyList<RunConfiguration> Own => own;

    /// <summary>The configuration chosen in the toolbar.</summary>
    public RunConfiguration? Chosen { get; private set; }

    /// <summary>The list or the one chosen changed, for the toolbar's widget.</summary>
    public event EventHandler? Changed;

    /// <summary>Takes the folder's own and the one chosen from the store, which keeps each change from now; the one chosen
    /// before stays chosen when the store names none.</summary>
    public void Keep(StateDocument<RunConfigurationsState> document, IReadOnlyList<ProjectModel> projects)
    {
        RunConfigurationsState read = document.Value;
        own = [.. read.Configurations.Select(FromStore)];
        kept = document;
        picked |= read.Selected is not null;
        Configure(projects, read.Selected ?? Chosen?.Name);
    }

    /// <summary>Makes the list again from the projects loaded now and the folder's own, the one the person chose kept while
    /// it is there.</summary>
    public void Configure(IReadOnlyList<ProjectModel> projects) => Configure(projects, picked ? Chosen?.Name : null);

    /// <summary>Why a configuration cannot launch here among the projects loaded now, as the toolbar's menu greys it; null
    /// when it can.</summary>
    public string? WhyNotHere(RunConfiguration configuration) => configuration.WhyNotHere(projects);

    private void Configure(IReadOnlyList<ProjectModel> projects, string? keep)
    {
        this.projects = projects;
        All = [.. RunConfiguration.Generate(projects, workspace?.Invoke()), .. own];
        Chosen = All.FirstOrDefault(c => string.Equals(c.Name, keep, StringComparison.OrdinalIgnoreCase)) ?? All.FirstOrDefault();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Chooses a configuration by name, as the toolbar's menu does; false when there is none.</summary>
    public bool Select(string name)
    {
        if (All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } chosen)
        {
            return false;
        }
        Chosen = chosen;
        picked = true;
        Changed?.Invoke(this, EventArgs.Empty);
        Write();
        return true;
    }

    /// <summary>Takes the folder's own as Edit Configurations leaves them, and the one chosen there.</summary>
    public void Set(IReadOnlyList<RunConfiguration> mine, string? chosen, IReadOnlyList<ProjectModel> projects)
    {
        UiThread.Check();
        own = [.. mine.Select(c => c with { Generated = false, Unsupported = null, Origin = null })];
        picked |= chosen is not null;
        Configure(projects, chosen ?? Chosen?.Name);
        Write();
    }

    private void Write() =>
        kept?.Set(new RunConfigurationsState { Configurations = [.. own.Select(ForStore)], Selected = Chosen?.Name });

    /// <summary>A configuration as the store keeps it: its project and every file it names (compose files, env files, a
    /// Dockerfile, a context, the projects chosen for services) relative to the folder, so a hand edit reads plainly.</summary>
    private RunConfiguration ForStore(RunConfiguration configuration) => folder is null ? configuration : configuration with
    {
        Project = Relative(configuration.Project),
        ComposeFiles = [.. configuration.ComposeFiles.Select(Relative)],
        EnvFiles = [.. configuration.EnvFiles.Select(Relative)],
        Dockerfile = Relative(configuration.Dockerfile),
        Context = Relative(configuration.Context),
        ServiceProjects = configuration.ServiceProjects.ToDictionary(p => p.Key, p => Relative(p.Value)),
    };

    private RunConfiguration FromStore(RunConfiguration configuration) => folder is null ? configuration : configuration with
    {
        Project = Full(configuration.Project),
        ComposeFiles = [.. configuration.ComposeFiles.Select(Full)],
        EnvFiles = [.. configuration.EnvFiles.Select(Full)],
        Dockerfile = Full(configuration.Dockerfile),
        Context = Full(configuration.Context),
        ServiceProjects = configuration.ServiceProjects.ToDictionary(p => p.Key, p => Full(p.Value)),
    };

    private string Relative(string path) => folder is not null && path.Length > 0 && (PathNames.IsUnder(path, folder) || PathNames.Same(path, folder))
        ? Path.GetRelativePath(folder, path).Replace('\\', '/')
        : path;

    private string Full(string path) => folder is null || path.Length == 0 ? path : PathNames.Canonical(Path.Combine(folder, path));
}
