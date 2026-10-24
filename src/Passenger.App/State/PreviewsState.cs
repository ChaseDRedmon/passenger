using System.Text.Json.Nodes;

namespace Passenger.App.State;

/// <summary>Which kinds of file show their preview beside their editor, in the store's previews.json, as the person left
/// them, every folder alike: a Markdown file's on until turned off.</summary>
public sealed record PreviewsState : IStateDocument<PreviewsState>
{
    public bool Markdown { get; init; } = true;

    public static IReadOnlyList<Action<JsonObject>> Upgrades { get; } = [StateStore.Unversioned];

    public PreviewsState Clamped() => this;
}
