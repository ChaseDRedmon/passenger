using System.Text.Encodings.Web;
using System.Text.Json;
using Passenger.Core;

namespace Passenger.App.Mcp;

/// <summary>How an agent is given this copy of Passenger as its MCP server, as Settings' MCP Server page shows it: each
/// agent's command or configuration, naming this executable by its real path (inside the app bundle on macOS) with
/// --mcp-attach, and Claude Desktop's with the folder to work in after it, since it starts its servers in no folder.</summary>
public sealed record AgentSetup(string Executable, string? Folder)
{
    /// <summary>What Claude Desktop's configuration names when no folder is open: the person's to fill in.</summary>
    public const string FolderPlaceholder = "FOLDER";

    private static readonly JsonSerializerOptions Strings = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The running copy's setup, with the window's folder.</summary>
    public static AgentSetup Here(string? folder) => new(ThisExecutable(), folder);

    /// <summary>The executable an agent starts: this process's, or the app host beside it when it runs through dotnet.</summary>
    public static string ThisExecutable()
    {
        string? path = Environment.ProcessPath;
        if (path is null || Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            path = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Passenger.exe" : "Passenger");
        }
        return PathNames.Canonical(path);
    }

    /// <summary>Claude Code's command, run in a terminal.</summary>
    public string ClaudeCode => $"claude mcp add passenger -- {Shell(Executable)} --mcp-attach";

    /// <summary>A repository's .mcp.json, which Claude Code reads for everyone working there.</summary>
    public string McpJson => Json(["--mcp-attach"]);

    /// <summary>Codex's command, run in a terminal.</summary>
    public string Codex => $"codex mcp add passenger -- {Shell(Executable)} --mcp-attach";

    /// <summary>Codex's ~/.codex/config.toml entry, which the Codex app shares.</summary>
    public string CodexToml => $"[mcp_servers.passenger]\ncommand = {Quoted(Executable)}\nargs = [\"--mcp-attach\"]";

    /// <summary>Claude Desktop's claude_desktop_config.json entry, with the folder to work in.</summary>
    public string ClaudeDesktop => Json(["--mcp-attach", Folder ?? FolderPlaceholder]);

    private string Json(IReadOnlyList<string> arguments) =>
        "{\n  \"mcpServers\": {\n    \"passenger\": {\n" +
        $"      \"command\": {Quoted(Executable)},\n" +
        $"      \"args\": [{string.Join(", ", arguments.Select(Quoted))}]\n" +
        "    }\n  }\n}";

    /// <summary>A string as JSON and TOML both write one: in double quotes, a backslash or quote escaped.</summary>
    private static string Quoted(string text) => JsonSerializer.Serialize(text, Strings);

    /// <summary>A path as the system's shell takes it: as it is when nothing in it needs quoting, else in quotes (double on
    /// Windows, whose shells take a backslash as it is; single elsewhere).</summary>
    private static string Shell(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
        }
        return path.All(c => char.IsAsciiLetterOrDigit(c) || "/._-+@%~:,".Contains(c, StringComparison.Ordinal)) ? path : $"'{path.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }
}
