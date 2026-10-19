using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Passenger.App;

/// <summary>Fluent's own keys as Passenger's tokens, for every theme at once: each key FluentKeys.axaml names answers
/// with the token it names, looked up in the application's resources as asked (Dark or Light, under whichever theme is
/// laid), so a theme supplies only tokens and a control left to Fluent never shows another theme's colours or Fluent's
/// greys. In App.axaml's resources; a key it does not name stays Fluent's.</summary>
public sealed class FluentKeys : ResourceProvider
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Table = new(Read);

    /// <summary>Every Fluent key mapped, and the token each reads.</summary>
    public static IReadOnlyDictionary<string, string> Tokens => Table.Value;

    public override bool HasResources => true;

    public override bool TryGetResource(object key, ThemeVariant? theme, out object? value)
    {
        // The token is asked of the whole application, so the theme laid last (Harbour over Passenger's) answers.
        if (key is string name && Tokens.TryGetValue(name, out string? token) && Owner is { } owner && owner.TryGetResource(token, theme, out value))
        {
            return true;
        }
        value = null;
        return false;
    }

    private static Dictionary<string, string> Read()
    {
        ResourceDictionary table = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://Passenger/Theme/FluentKeys.axaml"));
        return table.ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.Ordinal);
    }
}
