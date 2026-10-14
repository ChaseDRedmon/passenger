using Passenger.App.State;
using Passenger.App.Workspace;
using Passenger.Core;

namespace Passenger.App.Run;

/// <summary>A folder's run configurations: those its projects generate, then the person's own (kept with the folder's
/// state, their projects relative to the folder there), and the one chosen in the toolbar, which Run, Debug and Profile
/// launch. A UI-thread object of its <see cref="ProjectSession"/>.</summary>
public sealed class RunConfigurations(string? folder)
{
    private List<RunConfiguration> own = [];
    private StateDocument<RunConfigurationsState>? kept;

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
        Configure(projects, read.Selected ?? Chosen?.Name);
    }

    /// <summary>Makes the list again from the projects loaded now and the folder's own, the chosen one kept while it is there.</summary>
    public void Configure(IReadOnlyList<ProjectModel> projects) => Configure(projects, Chosen?.Name);

    private void Configure(IReadOnlyList<ProjectModel> projects, string? keep)
    {
        All = [.. RunConfiguration.Generate(projects), .. own];
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
        Changed?.Invoke(this, EventArgs.Empty);
        Write();
        return true;
    }

    /// <summary>Takes the folder's own as Edit Configurations leaves them, and the one chosen there.</summary>
    public void Set(IReadOnlyList<RunConfiguration> mine, string? chosen, IReadOnlyList<ProjectModel> projects)
    {
        UiThread.Check();
        own = [.. mine.Select(c => c with { Generated = false, Unsupported = null })];
        Configure(projects, chosen ?? Chosen?.Name);
        Write();
    }

    private void Write() =>
        kept?.Set(new RunConfigurationsState { Configurations = [.. own.Select(ForStore)], Selected = Chosen?.Name });

    /// <summary>A configuration as the store keeps it: its project relative to the folder, so a hand edit reads plainly.</summary>
    private RunConfiguration ForStore(RunConfiguration configuration) => folder is not null && PathNames.IsUnder(configuration.Project, folder)
        ? configuration with { Project = Path.GetRelativePath(folder, configuration.Project).Replace('\\', '/') }
        : configuration;

    private RunConfiguration FromStore(RunConfiguration configuration) =>
        folder is null ? configuration : configuration with { Project = PathNames.Canonical(Path.Combine(folder, configuration.Project)) };
}
