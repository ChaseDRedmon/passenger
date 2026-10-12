using System.Text;
using System.Text.Json;
using Passenger.App.Workspace;

namespace Passenger.App.Run;

/// <summary>A launch profile from a project's <c>Properties/launchSettings.json</c>, as <c>dotnet run</c>
/// and Rider read one: the arguments, working directory and environment a run of the project gets.
/// Only profiles that run the project itself (<c>"commandName": "Project"</c>) are runnable here.</summary>
public sealed record LaunchProfile(
    string Name,
    string? CommandLineArgs,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    string? ApplicationUrl)
{
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string SettingsPath(ProjectModel project) => Path.Combine(project.Folder, "Properties", "launchSettings.json");

    /// <summary>The project's runnable profiles in the file's order; none when it has no settings or
    /// they do not read.</summary>
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
                if (value.ValueKind != JsonValueKind.Object || Text(value, "commandName") != "Project")
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
                found.Add(new LaunchProfile(profile.Name, Text(value, "commandLineArgs"), Text(value, "workingDirectory"), environment, Text(value, "applicationUrl")));
            }
            return found;
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            return [];
        }

        static string? Text(JsonElement value, string key) =>
            value.TryGetProperty(key, out JsonElement found) && found.ValueKind == JsonValueKind.String ? found.GetString() : null;
    }

    /// <summary>The folder the profile runs in: its own, relative to the project and with Visual Studio's
    /// <c>$(ProjectDir)</c> expanded, or the project's folder, where <c>dotnet run</c> and Visual Studio run it.</summary>
    public string Folder(ProjectModel project)
    {
        if (string.IsNullOrWhiteSpace(WorkingDirectory))
        {
            return project.Folder;
        }
        string expanded = WorkingDirectory.Replace("$(ProjectDir)", project.Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        return Path.GetFullPath(Path.Combine(project.Folder, expanded));
    }

    /// <summary>The environment the program gets: the profile's variables, with its application URL as
    /// ASP.NET Core's <c>ASPNETCORE_URLS</c>, as <c>dotnet run</c> passes it.</summary>
    public IReadOnlyDictionary<string, string> Environment()
    {
        Dictionary<string, string> environment = new(EnvironmentVariables);
        if (!string.IsNullOrWhiteSpace(ApplicationUrl) && !environment.ContainsKey("ASPNETCORE_URLS"))
        {
            environment["ASPNETCORE_URLS"] = ApplicationUrl;
        }
        return environment;
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
