using System.Text.Json.Nodes;
using Passenger.App.Editors;
using Passenger.App.State;

namespace Passenger.App.Workspace;

/// <summary>What a folder's window had open, kept in the folder's place in the store as Rider keeps its workspace.xml:
/// the editors' splits, tabs and carets, the recent files and the projects unloaded, so opening the folder again
/// brings them back. Only the window a person uses keeps it, as with the layout.</summary>
public sealed record WorkspaceState : IStateDocument<WorkspaceState>
{
    public EditorLayout? Editors { get; init; }

    /// <summary>The files opened lately, the latest first, for Recent Files and Go to File.</summary>
    public List<string> RecentFiles { get; init; } = [];

    /// <summary>The projects unloaded in the Solution view, by path.</summary>
    public List<string> UnloadedProjects { get; init; } = [];

    public static IReadOnlyList<Action<JsonObject>> Upgrades { get; } = [StateStore.Unversioned];

    /// <summary>A tab or a path without its file is left out; the editors bring the rest (a caret, a split's share)
    /// into range as they open them.</summary>
    public WorkspaceState Clamped() => this with
    {
        Editors = Editors is { } editors ? Clamp(editors) : null,
        RecentFiles = [.. (RecentFiles ?? []).Where(f => !string.IsNullOrEmpty(f))],
        UnloadedProjects = [.. (UnloadedProjects ?? []).Where(p => !string.IsNullOrEmpty(p))],
    };

    private static EditorLayout Clamp(EditorLayout layout) => layout with
    {
        First = layout.First is { } first ? Clamp(first) : null,
        Second = layout.Second is { } second ? Clamp(second) : null,
        Tabs = layout.Tabs is { } tabs ? [.. tabs.Where(t => t is { Path.Length: > 0 })] : null,
    };
}
