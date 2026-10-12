using System.Text.Json;
using System.Text.Json.Serialization;

namespace Passenger.App.Docking;

/// <summary>Where each tool window was and whether it was open, and the areas' sizes, as the window last stood: kept
/// in the user's settings folder so the next start opens on it. Only the window a person uses keeps it; a headless
/// IDE (a screenshot, an agent's, a test's) starts on the defaults and keeps nothing.</summary>
public sealed record DockLayout
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public Dictionary<string, DockedWindow> Windows { get; init; } = [];
    public double LeftWidth { get; init; }
    public double RightWidth { get; init; }
    public double BottomHeight { get; init; }

    /// <summary>The file it is kept in: PASSENGER_SETTINGS names the folder, else the user's local application data.</summary>
    public static string Path => System.IO.Path.Combine(
        Environment.GetEnvironmentVariable("PASSENGER_SETTINGS") is { Length: > 0 } folder
            ? folder
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Passenger"),
        "layout.json");

    /// <summary>The kept layout, or none when there is none or it does not read (the defaults then stand).</summary>
    public static DockLayout? Load()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize<DockLayout>(File.ReadAllText(Path), Options) : null;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>One tool window's place: its anchor, its order among that anchor's buttons, and whether it was open.</summary>
public sealed record DockedWindow(ToolAnchor Anchor, int Order, bool Shown);
