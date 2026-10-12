using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Passenger.App.Editors;

namespace Passenger.App.Workspace;

/// <summary>What a folder's window had open, kept per folder in the settings folder as Rider keeps its workspace.xml:
/// the editors' splits, tabs and carets, and the recent files, so opening the folder again brings them back. Only the
/// window a person uses keeps it, as with the layout.</summary>
public sealed record WorkspaceState
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public EditorLayout? Editors { get; init; }

    /// <summary>The files opened lately, the latest first, for Recent Files and Go to File.</summary>
    public List<string> RecentFiles { get; init; } = [];

    /// <summary>The projects unloaded in the Solution view, by path.</summary>
    public List<string> UnloadedProjects { get; init; } = [];

    /// <summary>The folder's file: its name and a hash of its path, under the settings folder's <c>workspaces</c>.</summary>
    public static string PathFor(string folder)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(folder)))[..12];
        return Path.Combine(Path.GetDirectoryName(Docking.DockLayout.Path)!, "workspaces", $"{Path.GetFileName(folder)}-{hash}.json");
    }

    /// <summary>The folder's kept state, or null when there is none or it does not read.</summary>
    public static WorkspaceState? Load(string folder)
    {
        string path = PathFor(folder);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(path), Options) : null;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string folder)
    {
        string path = PathFor(folder);
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
