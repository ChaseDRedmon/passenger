using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace Passenger.App;

/// <summary>The visual directions offered for #223 before one becomes Passenger's look: PASSENGER_DIRECTION names one,
/// whose tokens and styles (Theme/Directions) are laid over Tokens.axaml and Shell.axaml, so its pictures are the app
/// itself. For the choosing only: the one chosen moves into the theme and this goes.</summary>
internal static class Directions
{
    public const string Variable = "PASSENGER_DIRECTION";

    /// <summary>Each direction's letter, as the variable takes it, and the name of its two files.</summary>
    private static readonly IReadOnlyDictionary<string, string> Known = new Dictionary<string, string>();

    /// <summary>The direction asked for, or null for the look as it is; one the variable names that does not exist is
    /// fatal, never the look as it is.</summary>
    public static string? Chosen()
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 } asked)
        {
            return null;
        }
        return Known.TryGetValue(asked.ToLowerInvariant(), out string? name) ? name
            : throw new FormatException($"{Variable} takes one of the directions ({string.Join(", ", Known.Keys)}), not '{asked}'");
    }

    /// <summary>Lays the chosen direction over the theme, after the theme's own dictionaries and styles so it wins.</summary>
    public static void Apply(Application app)
    {
        if (Chosen() is not { } name)
        {
            return;
        }
        Uri root = new("avares://Passenger/");
        app.Resources.MergedDictionaries.Add(new ResourceInclude(root) { Source = new Uri(root, $"Theme/Directions/{name}.Tokens.axaml") });
        app.Styles.Add(new StyleInclude(root) { Source = new Uri(root, $"Theme/Directions/{name}.Styles.axaml") });
    }
}
