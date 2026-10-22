using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Passenger.App.Containers;

/// <summary>What the Containers window shows of a container's secrets, in the Details tab and in Inspect's JSON, which is
/// written to disk only so: the value of an environment variable whose name says it is one (<see cref="ComposeCheck.Secret"/>,
/// the rule a compose file's hints follow) as the same dots whatever its length; in any other value, a URL's password
/// (<c>postgres://app:••••••@db</c>) and a connection string's part so named (<c>Password=••••••</c>). An empty value
/// hides nothing and shows as it is.</summary>
public static partial class ContainerSecrets
{
    public const string Masked = "••••••";

    /// <summary>A variable's value as it is shown.</summary>
    public static string Shown(string name, string value)
    {
        if (value.Length == 0)
        {
            return value;
        }
        if (ComposeCheck.Secret(name))
        {
            return Masked;
        }
        string bare = UrlPassword().Replace(value, $"${{head}}{Masked}@");
        return ConnectionPart().Replace(bare, part => ComposeCheck.Secret(part.Groups["name"].Value.Trim()) && part.Groups["value"].Length > 0
            ? $"{part.Groups["head"].Value}{Masked}" : part.Value);
    }

    /// <summary>An environment entry as the engine lists it ("NAME=value"), its secrets masked.</summary>
    public static string Entry(string entry)
    {
        int equals = entry.IndexOf('=', StringComparison.Ordinal);
        return equals <= 0 ? entry : $"{entry[..(equals + 1)]}{Shown(entry[..equals], entry[(equals + 1)..])}";
    }

    /// <summary>Masks, in place, every environment list in what <c>docker inspect</c> said: each <c>Env</c> array of
    /// "NAME=value" strings anywhere in it (a container's and an image's Config, an older image's ContainerConfig).</summary>
    public static void Mask(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject holder:
                foreach ((string name, JsonNode? value) in holder.ToList())
                {
                    if (name == "Env" && value is JsonArray entries)
                    {
                        for (int i = 0; i < entries.Count; i++)
                        {
                            if (entries[i] is JsonValue one && one.TryGetValue(out string? entry))
                            {
                                entries[i] = Entry(entry);
                            }
                        }
                    }
                    else
                    {
                        Mask(value);
                    }
                }
                break;
            case JsonArray items:
                foreach (JsonNode? item in items)
                {
                    Mask(item);
                }
                break;
        }
    }

    [GeneratedRegex(@"(?<head>[A-Za-z][A-Za-z0-9+.\-]*://[^\s/@:]*:)[^\s@]*@")]
    private static partial Regex UrlPassword();

    /// <summary>A connection string's part, "name=value" between semicolons.</summary>
    [GeneratedRegex(@"(?<head>(?:^|;)(?<name>[^=;]+)=)(?<value>[^;]*)")]
    private static partial Regex ConnectionPart();
}
