using System.Text.Json;
using System.Text.Json.Serialization;

namespace Passenger.App.Settings;

/// <summary>What a person has set in Settings: the theme, the editor's and the terminal's font, the shell. Kept as JSON
/// beside the layout (only by the window a person uses: a headless IDE starts on the defaults and keeps nothing); a
/// value the file lacks takes its default, and a file that does not read leaves the defaults standing.</summary>
public sealed record AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public ThemeChoice Theme { get; init; } = ThemeChoice.System;
    public double EditorFontSize { get; init; } = 13;
    public double EditorLineSpacing { get; init; } = 1.2;
    public bool Ligatures { get; init; }
    public bool ShowLineNumbers { get; init; } = true;
    public double TerminalFontSize { get; init; } = 13;

    /// <summary>The person's shortcuts over the keymap's, by action, in keymap notation ("meta shift K"); an empty list
    /// unbinds the action.</summary>
    public Dictionary<string, string[]> Shortcuts { get; init; } = [];

    /// <summary>The terminal's shell, or null for the user's own login shell.</summary>
    public string? TerminalShell { get; init; }

    /// <summary>The file they are kept in: PASSENGER_SETTINGS names the folder, else the user's local application data.</summary>
    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(Docking.DockLayout.Path)!, "settings.json");

    public static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new() : new();
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
