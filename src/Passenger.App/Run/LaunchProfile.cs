using System.Text;
using System.Text.Json;
using Passenger.App.Workspace;

namespace Passenger.App.Run;

/// <summary>A launch profile from a project's <c>Properties/launchSettings.json</c>, as <c>dotnet run</c>, Visual Studio
/// and Rider read one: what it launches (its <c>commandName</c>), with what arguments, environment and working directory,
/// and the browser it opens. Passenger launches the two kinds that run a program (<see cref="ProjectCommand"/> and
/// <see cref="ExecutableCommand"/>); every other kind Rider knows is read too, and says why it does not run here.</summary>
public sealed record LaunchProfile(
    string Name,
    string? Command,
    string? ExecutablePath,
    string? CommandLineArgs,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    string? ApplicationUrl,
    bool LaunchBrowser,
    string? LaunchUrl)
{
    /// <summary>The project's own program, built.</summary>
    public const string ProjectCommand = "Project";

    /// <summary>Another program, named by <c>executablePath</c>.</summary>
    public const string ExecutableCommand = "Executable";

    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string SettingsPath(ProjectModel project) => Path.Combine(project.Folder, "Properties", "launchSettings.json");

    /// <summary>Null for a profile Passenger launches, else why it does not, in Rider's terms for each command it knows.</summary>
    public string? Unsupported => Command switch
    {
        null or "" => "its profile names no commandName",
        _ when Is(ProjectCommand) || Is(ExecutableCommand) => null,
        _ when Is("IISExpress") => "an IIS Express profile runs under IIS Express on Windows, which Passenger does not start",
        _ when Is("IIS") => "an IIS profile runs under IIS on Windows, which Passenger does not drive",
        _ when Is("MsixPackage") => "an MSIX package profile runs a deployed Windows package, which Passenger does not deploy",
        _ when Is("DebugRoslynComponent") => "a Roslyn component profile runs the compiler over another project, which Passenger does not",
        _ when Is("Docker") || Is("DockerCompose") || Is("SdkContainer") => "a container profile runs in Docker, which Passenger does not start",
        _ when Is("WSL2") => "a WSL 2 profile runs in Linux under Windows, which Passenger does not start",
        _ => $"Passenger does not know the {Command} command",
    };

    /// <summary>Whether this profile runs the project's own program.</summary>
    public bool RunsProject => Is(ProjectCommand);

    private bool Is(string command) => string.Equals(Command, command, StringComparison.OrdinalIgnoreCase);

    /// <summary>The project's profiles of every kind in the file's order; none when it has no settings or they do not read.</summary>
    public static IReadOnlyList<LaunchProfile> Read(ProjectModel project)
    {
        string path = SettingsPath(project);
        if (!File.Exists(path))
        {
            return [];
        }
        try
        {
            using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(path), Lenient);
            if (!settings.RootElement.TryGetProperty("profiles", out JsonElement profiles) || profiles.ValueKind != JsonValueKind.Object)
            {
                return [];
            }
            List<LaunchProfile> found = [];
            foreach (JsonProperty profile in profiles.EnumerateObject())
            {
                JsonElement value = profile.Value;
                if (value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                Dictionary<string, string> environment = [];
                if (value.TryGetProperty("environmentVariables", out JsonElement variables) && variables.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty variable in variables.EnumerateObject())
                    {
                        environment[variable.Name] = variable.Value.ValueKind == JsonValueKind.String ? variable.Value.GetString()! : variable.Value.GetRawText();
                    }
                }
                bool browser = value.TryGetProperty("launchBrowser", out JsonElement launch) && launch.ValueKind == JsonValueKind.True;
                found.Add(new LaunchProfile(profile.Name, Text(value, "commandName"), Text(value, "executablePath"), Text(value, "commandLineArgs"),
                    Text(value, "workingDirectory"), environment, Text(value, "applicationUrl"), browser, Text(value, "launchUrl")));
            }
            return found;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }

        static string? Text(JsonElement value, string key) =>
            value.TryGetProperty(key, out JsonElement found) && found.ValueKind == JsonValueKind.String ? found.GetString() : null;
    }

    /// <summary>The environment a run of the profile gets: its variables, with its application URL as ASP.NET Core's
    /// <c>ASPNETCORE_URLS</c> unless it sets that itself, as <c>dotnet run</c> passes it.</summary>
    public Dictionary<string, string> Environment()
    {
        Dictionary<string, string> environment = new(EnvironmentVariables);
        if (RunsProject && !string.IsNullOrWhiteSpace(ApplicationUrl) && !environment.ContainsKey("ASPNETCORE_URLS"))
        {
            environment["ASPNETCORE_URLS"] = ApplicationUrl;
        }
        return environment;
    }

    /// <summary>Where the browser opens when the profile asks for one: its launch URL, relative to its first application
    /// URL unless absolute, as Visual Studio and Rider take it; empty with neither.</summary>
    public string BrowserUrl()
    {
        string? root = ApplicationUrl?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(LaunchUrl))
        {
            return root ?? string.Empty;
        }
        if (Uri.TryCreate(LaunchUrl, UriKind.Absolute, out Uri? absolute) && absolute.Scheme is "http" or "https")
        {
            return absolute.ToString();
        }
        return root is not null && Uri.TryCreate(root.TrimEnd('/') + "/", UriKind.Absolute, out Uri? baseUri) && Uri.TryCreate(baseUri, LaunchUrl, out Uri? joined)
            ? joined.ToString()
            : LaunchUrl;
    }
}

/// <summary>A command line as one string, split the way .NET splits one into a program's arguments on
/// every platform: spaces separate, double quotes group, and backslashes escape only the quotes they
/// precede.</summary>
public static class CommandLine
{
    public static IReadOnlyList<string> Split(string? line)
    {
        List<string> arguments = [];
        if (string.IsNullOrWhiteSpace(line))
        {
            return arguments;
        }
        StringBuilder current = new();
        bool quoted = false;
        bool started = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\')
            {
                int run = 0;
                while (i < line.Length && line[i] == '\\')
                {
                    run++;
                    i++;
                }
                if (i < line.Length && line[i] == '"')
                {
                    current.Append('\\', run / 2);
                    if (run % 2 == 1)
                    {
                        current.Append('"');
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else
                {
                    current.Append('\\', run);
                    i--;
                }
                started = true;
            }
            else if (c == '"')
            {
                // Two quotes inside a quoted run are one literal quote.
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
                started = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (started)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    started = false;
                }
            }
            else
            {
                current.Append(c);
                started = true;
            }
        }
        if (started)
        {
            arguments.Add(current.ToString());
        }
        return arguments;
    }
}
