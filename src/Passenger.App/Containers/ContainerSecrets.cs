using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Passenger.Core;

namespace Passenger.App.Containers;

/// <summary>Passenger's one rule for what is a secret, and its one mask: a value whose name says it is one
/// (<see cref="Secret"/>) shows as the same dots whatever its length; in any other value, a URL's password
/// (<c>postgres://app:••••••@db</c>) and a connection string's part so named (<c>Password=••••••</c>). The Containers
/// window's Details tab and Inspect's JSON (written to disk only so) follow it, and so do a compose file's hints. An empty
/// value hides nothing and shows as it is.</summary>
public static partial class ContainerSecrets
{
    public const string Masked = "••••••";

    /// <summary>What makes a name a secret's anywhere in it, its case, underscores, dashes and dots aside (DBPASSWORD,
    /// SECRETKEY, ConnectionStrings__Shop).</summary>
    private static readonly string[] Anywhere = ["PASSWORD", "PASSWD", "SECRET", "TOKEN", "CREDENTIAL", "CONNECTIONSTRING"];

    /// <summary>What makes a name a secret's as a word of it (DB_PASS, MYSQL_PW, DB_PWD, API_KEY), or its plural.</summary>
    private static readonly HashSet<string> Whole = new(StringComparer.Ordinal) { "PASS", "PW", "PWD", "KEY" };

    /// <summary>The words that end in KEY and name no key (a word ending in KEY otherwise is one: APIKEY, ACCESSKEY).</summary>
    private static readonly HashSet<string> Ordinary = new(StringComparer.Ordinal)
    {
        "MONKEY", "DONKEY", "TURKEY", "JOCKEY", "HOCKEY", "WHISKEY", "LACKEY", "FLUNKEY", "MALARKEY", "HOTKEY", "TURNKEY",
    };

    /// <summary>The shell's working folders, paths a compose file's volumes are written with, whose PWD is no password's.</summary>
    private static readonly HashSet<string> Folders = new(StringComparer.Ordinal) { "PWD", "OLDPWD" };

    /// <summary>Whether a name's value is a secret: it holds one of <see cref="Anywhere"/>, or a word of it (between
    /// underscores, dashes or dots, or where its case turns, as apiKey's Key) is one of <see cref="Whole"/> or its plural,
    /// or ends in KEY but for <see cref="Ordinary"/>'s words; so DBPASSWORD, APIKEY and DB_PASS are and MONKEY is not,
    /// nor the shell's own PWD and OLDPWD (<see cref="Folders"/>).</summary>
    public static bool Secret(string name)
    {
        if (Folders.Contains(name))
        {
            return false;
        }
        string squeezed = name.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (Anywhere.Any(s => squeezed.Contains(s, StringComparison.Ordinal)))
        {
            return true;
        }
        foreach (string word in Words().Split(name).Where(w => w.Length > 0).Select(w => w.ToUpperInvariant()))
        {
            string single = word.Length > 1 && word[^1] == 'S' ? word[..^1] : word;
            if (Whole.Contains(word) || Whole.Contains(single)
                || (word.EndsWith("KEY", StringComparison.Ordinal) && !Ordinary.Contains(word))
                || (single.EndsWith("KEY", StringComparison.Ordinal) && !Ordinary.Contains(single)))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>A variable's value as it is shown.</summary>
    public static string Shown(string name, string value) => value.Length > 0 && Secret(name) ? Masked : InValue(value);

    /// <summary>A value whose name says nothing, as it is shown: a URL's password and a connection string's part named as a
    /// secret's masked, the rest as it is.</summary>
    public static string InValue(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }
        string bare = UrlPassword().Replace(value, $"${{head}}{Masked}@");
        return ConnectionPart().Replace(bare, part => Secret(part.Groups["name"].Value.Trim()) && part.Groups["value"].Length > 0
            ? $"{part.Groups["head"].Value}{Masked}" : part.Value);
    }

    /// <summary>Whether what a line has before and after a place makes it a URL's password (<c>postgres://user:</c> before,
    /// <c>@db</c> after), as a compose file's <c>${PGPASS}</c> there is.</summary>
    public static bool AtUrlPassword(string before, string after) => UserBefore().IsMatch(before) && HostAfter().IsMatch(after);

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

    /// <summary>Where Inspect writes its copies, under a state folder.</summary>
    public static string InspectCopies(string stateFolder) => Path.Combine(stateFolder, "cache", "containers", "inspect");

    /// <summary>The state folders whose Inspect copies this process has looked through.</summary>
    private static readonly HashSet<string> Looked = new(StringComparer.Ordinal);

    /// <summary>Inspect's copies an earlier Passenger wrote before it masked them, deleted, once a process for each state
    /// folder: each copy whose masking would change it (a secret written whole), so a copy written masked, which masking
    /// leaves as it is, stays. Reads the disk, so off the UI thread. The paths deleted, canonical; none the second time.</summary>
    public static IReadOnlyList<string> ForgetUnmasked(string stateFolder)
    {
        lock (Looked)
        {
            if (!Looked.Add(PathNames.Canonical(stateFolder)))
            {
                return [];
            }
        }
        List<string> deleted = [];
        string folder = InspectCopies(stateFolder);
        if (!Directory.Exists(folder))
        {
            return deleted;
        }
        string[] copies;
        try
        {
            copies = Directory.GetFiles(folder, "*.json");
        }
        catch (Exception unlisted) when (unlisted is IOException or UnauthorizedAccessException)
        {
            return deleted;
        }
        foreach (string copy in copies)
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(copy)) is not { } read)
                {
                    continue;
                }
                string written = read.ToJsonString();
                Mask(read);
                if (read.ToJsonString() != written)
                {
                    string path = PathNames.Canonical(copy);
                    File.Delete(copy);
                    deleted.Add(path);
                }
            }
            catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or JsonException)
            {
                // A copy that cannot be read or deleted now is looked at again by the next process.
                IdeErrors.Write($"Inspect's copy {copy} could not be looked through for unmasked secrets: {unread.Message}");
            }
        }
        return deleted;
    }

    [GeneratedRegex(@"[_.\-]+|(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex Words();

    [GeneratedRegex(@"(?<head>[A-Za-z][A-Za-z0-9+.\-]*://[^\s/@:]*:)[^\s@]*@")]
    private static partial Regex UrlPassword();

    /// <summary>A connection string's part, "name=value" between semicolons.</summary>
    [GeneratedRegex(@"(?<head>(?:^|;)(?<name>[^=;]+)=)(?<value>[^;]*)")]
    private static partial Regex ConnectionPart();

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+.\-]*://[^\s/@]*:[^\s/@]*$")]
    private static partial Regex UserBefore();

    [GeneratedRegex(@"^[^\s/@]*@")]
    private static partial Regex HostAfter();
}
