using System.Text.Json.Nodes;
using Passenger.App.State;

namespace Passenger.App.Containers;

/// <summary>The store's <c>containers.json</c>: whether Passenger has ever labelled something an engine made for this
/// state folder, without which a start runs no docker command to look for what a crash left.</summary>
public sealed record ContainersState : IStateDocument<ContainersState>
{
    public bool Labelled { get; init; }

    public static IReadOnlyList<Action<JsonObject>> Upgrades { get; } = [StateStore.Unversioned];

    public ContainersState Clamped() => this;
}
