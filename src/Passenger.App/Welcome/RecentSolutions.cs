using System.Text.Json.Nodes;
using Passenger.App.State;

namespace Passenger.App.Welcome;

/// <summary>The folders and solutions opened lately, as Rider's recentSolutions.xml keeps them, by path (a solution file,
/// or a folder without one, canonical): what the Welcome screen and File, Recent Solutions list, and the one reopened on
/// start. Kept by path rather than as a list, so two windows' openings and removals merge entry by entry.</summary>
public sealed record RecentSolutionsState : IStateDocument<RecentSolutionsState>
{
    /// <summary>How many are kept, the oldest dropped past it, as IntelliJ's recent projects are.</summary>
    public const int Limit = 50;

    public Dictionary<string, RecentSolution> Solutions { get; init; } = [];

    public static IReadOnlyList<Action<JsonObject>> Upgrades { get; } = [StateStore.Unversioned];

    /// <summary>The newest first, each with its path.</summary>
    public IReadOnlyList<(string Path, RecentSolution Entry)> Newest() =>
        [.. Solutions.OrderByDescending(s => s.Value.Opened).ThenBy(s => s.Key, StringComparer.Ordinal).Select(s => (s.Key, s.Value))];

    /// <summary>With a path opened now, newest of all even on a clock that has not moved since the last.</summary>
    public RecentSolutionsState Opened(string path, string name, DateTimeOffset now)
    {
        DateTimeOffset latest = Solutions.Values.Select(s => s.Opened).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
        Dictionary<string, RecentSolution> next = new(Solutions)
        {
            [path] = new RecentSolution { Name = name, Opened = now > latest ? now : latest.AddTicks(1) },
        };
        return this with { Solutions = Kept(next) };
    }

    public RecentSolutionsState Without(string path)
    {
        Dictionary<string, RecentSolution> next = new(Solutions);
        next.Remove(path);
        return this with { Solutions = next };
    }

    /// <summary>An entry without a path is left out, a name missing is the path's own, and only the newest are kept.</summary>
    public RecentSolutionsState Clamped() => this with
    {
        Solutions = Kept((Solutions ?? []).Where(s => !string.IsNullOrEmpty(s.Key) && s.Value is not null)
            .ToDictionary(s => s.Key, s => s.Value with { Name = string.IsNullOrEmpty(s.Value.Name) ? Path.GetFileName(s.Key.TrimEnd('/', '\\')) : s.Value.Name })),
    };

    private static Dictionary<string, RecentSolution> Kept(Dictionary<string, RecentSolution> all) =>
        all.Count <= Limit ? all : all.OrderByDescending(s => s.Value.Opened).Take(Limit).ToDictionary(s => s.Key, s => s.Value);
}

/// <summary>One recent folder or solution: its name as the window showed it, and when it was last opened.</summary>
public sealed record RecentSolution
{
    public string Name { get; init; } = string.Empty;

    public DateTimeOffset Opened { get; init; }
}

/// <summary>A recent folder or solution as a list shows it: where it is, its name, its path with the home folder as ~,
/// and whether it is gone from disk.</summary>
public sealed record RecentRow(string Path, string Name, string Shown, bool Missing)
{
    public static RecentRow Of(string path, RecentSolution entry) =>
        new(path, entry.Name, Home(path), !Directory.Exists(path) && !File.Exists(path));

    /// <summary>A path under the home folder as IntelliJ shows it, from ~.</summary>
    public static string Home(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && Core.PathNames.IsUnder(path, home) ? "~" + path[home.TrimEnd('/', '\\').Length..] : path;
    }
}
