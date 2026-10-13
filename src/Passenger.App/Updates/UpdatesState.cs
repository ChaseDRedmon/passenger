using System.Text.Json.Nodes;
using Passenger.App.State;

namespace Passenger.App.Updates;

/// <summary>What the update check keeps: the newest release's version it has said is out, so each newer version is
/// announced once, however often Passenger starts while it stays the latest.</summary>
public sealed record UpdatesState : IStateDocument<UpdatesState>
{
    public string? Announced { get; init; }

    public static IReadOnlyList<Action<JsonObject>> Upgrades { get; } = [StateStore.Unversioned];

    public UpdatesState Clamped() => this with { Announced = string.IsNullOrWhiteSpace(Announced) ? null : Announced.Trim() };
}
