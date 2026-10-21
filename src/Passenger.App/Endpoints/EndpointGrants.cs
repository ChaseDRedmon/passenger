using System.Text.Json.Nodes;
using Passenger.App.State;

namespace Passenger.App.Endpoints;

/// <summary>What the folder's requests may take from the person's computer, each to one host: an environment variable
/// or a file outside the folder ("localhost:5000 env API_TOKEN"), as Always for this folder kept it.</summary>
public sealed record EndpointsState : IStateDocument<EndpointsState>
{
    public List<string> Allowed { get; init; } = [];

    public static IReadOnlyList<Action<JsonObject>> Upgrades { get; } = [StateStore.Unversioned];

    public static IReadOnlyList<string> Sets { get; } = [nameof(Allowed)];

    public EndpointsState Clamped() => this with { Allowed = [.. (Allowed ?? []).Where(a => a is not null).Distinct()] };
}

/// <summary>The open folder's answers to the question a request asks before it takes something from the person's
/// computer to a host: an Allow lasts while the folder is open, and Always for this folder is kept in the folder's state
/// when the window keeps state. A Don't allow is never kept, so the next send asks again.</summary>
public sealed class EndpointGrants
{
    private readonly HashSet<string> allowed = new(StringComparer.Ordinal);
    private StateDocument<EndpointsState>? kept;

    /// <summary>The folder's documents from now on: what it allowed always, allowed again.</summary>
    public void Keep(StateDocument<EndpointsState> state)
    {
        kept = state;
        allowed.UnionWith(state.Value.Allowed);
    }

    /// <summary>The reads of a call the folder has not allowed to its host, which the person is asked about.</summary>
    public IReadOnlyList<EndpointRead> Unallowed(EndpointCall call) => [.. call.Reads.Where(r => !allowed.Contains(r.Key(call.Host)))];

    /// <summary>Allows a call's reads to its host while the folder is open, and from now on when <paramref name="always"/>.</summary>
    public void Allow(EndpointCall call, bool always)
    {
        UiThread.Check();
        string[] keys = [.. call.Reads.Select(r => r.Key(call.Host))];
        allowed.UnionWith(keys);
        if (always && kept is not null)
        {
            EndpointsState now = kept.Value;
            kept.Set(now with { Allowed = [.. now.Allowed.Union(keys, StringComparer.Ordinal)] });
        }
    }
}
