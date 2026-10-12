using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Passenger.App.Packages;

/// <summary>A package as a search lists it: its id, its newest version, what it says it is, who made it and how much it
/// is used.</summary>
public sealed record PackageSummary(string Id, string Version, string Description, string Authors, long Downloads);

/// <summary>Where packages are found: nuget.org, or a folder of .nupkg files.</summary>
public interface IPackageFeed
{
    string Name { get; }

    /// <summary>The packages a query finds, the most used first.</summary>
    Task<IReadOnlyList<PackageSummary>> SearchAsync(string query, bool prerelease, CancellationToken cancel);

    /// <summary>A package's versions that can be installed, newest first: none its owner unlisted.</summary>
    Task<IReadOnlyList<string>> VersionsAsync(string id, bool prerelease, CancellationToken cancel);
}

/// <summary>nuget.org through its v3 protocol, as the NuGet client finds it: the service index names the search and
/// the registrations, which say of each version whether it is listed (the flat container lists unlisted ones too).</summary>
public sealed class NuGetOrgFeed(HttpClient http) : IPackageFeed
{
    private const string Index = "https://api.nuget.org/v3/index.json";
    private (string Search, string Registrations)? services;

    public string Name => "nuget.org";

    public async Task<IReadOnlyList<PackageSummary>> SearchAsync(string query, bool prerelease, CancellationToken cancel)
    {
        (string search, _) = await ServicesAsync(cancel);
        string url = $"{search}?q={Uri.EscapeDataString(query)}&take=30&prerelease={(prerelease ? "true" : "false")}&semVerLevel=2.0.0";
        SearchResponse? found = await http.GetFromJsonAsync<SearchResponse>(url, Json, cancel);
        return [.. (found?.Data ?? []).Select(p => new PackageSummary(p.Id, p.Version, p.Description ?? string.Empty, string.Join(", ", p.Authors ?? []), p.TotalDownloads))];
    }

    public async Task<IReadOnlyList<string>> VersionsAsync(string id, bool prerelease, CancellationToken cancel)
    {
        (_, string registrations) = await ServicesAsync(cancel);
        if (await GetAsync<Registration>($"{registrations}{id.ToLowerInvariant()}/index.json", cancel) is not { } registration)
        {
            return [];
        }
        // A package with many versions has its pages by reference, each fetched on its own.
        RegistrationPage[] pages = await Task.WhenAll((registration.Items ?? []).Select(async page =>
            page.Items is null ? await GetAsync<RegistrationPage>(page.Id, cancel) ?? page : page));
        return [.. pages.SelectMany(p => p.Items ?? []).Select(leaf => leaf.CatalogEntry)
            .Where(entry => entry.Listed != false && (prerelease || !VersionOrder.IsPrerelease(entry.Version)))
            .Select(entry => entry.Version)
            .OrderByDescending(v => v, VersionOrder.Instance)];
    }

    /// <summary>A JSON answer, or null where there is none (a package nuget.org does not have).</summary>
    private async Task<T?> GetAsync<T>(string url, CancellationToken cancel) where T : class
    {
        using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        Stream body = await response.Content.ReadAsStreamAsync(cancel);
        // The SemVer 2 registrations are gzipped whatever was asked for; a decompressing handler takes the header away.
        bool gzipped = response.Content.Headers.ContentEncoding.Contains("gzip", StringComparer.OrdinalIgnoreCase);
        await using Stream json = gzipped ? new GZipStream(body, CompressionMode.Decompress) : body;
        return await JsonSerializer.DeserializeAsync<T>(json, Json, cancel);
    }

    private async Task<(string Search, string Registrations)> ServicesAsync(CancellationToken cancel)
    {
        if (services is { } known)
        {
            return known;
        }
        ServiceIndex? index = await http.GetFromJsonAsync<ServiceIndex>(Index, Json, cancel);
        string? Find(string type) => index?.Resources?.FirstOrDefault(r => r.Type == type)?.Id;
        string search = Find("SearchQueryService/3.5.0") ?? Find("SearchQueryService") ?? throw new InvalidOperationException("nuget.org names no search service");
        // 3.6.0 is the hive that has packages with SemVer 2 versions, which the earlier ones leave out.
        string registrations = Find("RegistrationsBaseUrl/3.6.0") ?? throw new InvalidOperationException("nuget.org names no SemVer 2 registrations");
        services = (search, registrations.EndsWith('/') ? registrations : registrations + "/");
        return services.Value;
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private sealed record ServiceIndex(IReadOnlyList<ServiceResource>? Resources);

    private sealed record ServiceResource([property: JsonPropertyName("@id")] string Id, [property: JsonPropertyName("@type")] string Type);

    private sealed record SearchResponse(IReadOnlyList<SearchHit>? Data);

    private sealed record SearchHit(string Id, string Version, string? Description, IReadOnlyList<string>? Authors, long TotalDownloads);

    private sealed record Registration(IReadOnlyList<RegistrationPage>? Items);

    private sealed record RegistrationPage([property: JsonPropertyName("@id")] string Id, IReadOnlyList<RegistrationLeaf>? Items);

    private sealed record RegistrationLeaf(CatalogEntry CatalogEntry);

    /// <summary>A version as its registration has it; one with no listed flag is listed.</summary>
    private sealed record CatalogEntry(string Version, bool? Listed);
}

/// <summary>A folder of .nupkg files, the kind a nuget.config can name as a source: each package's id, version,
/// description and authors read from its .nuspec.</summary>
public sealed class FolderFeed(string folder) : IPackageFeed
{
    public string Name => folder;

    public Task<IReadOnlyList<PackageSummary>> SearchAsync(string query, bool prerelease, CancellationToken cancel) => Task.Run(() =>
        (IReadOnlyList<PackageSummary>)[.. Read(prerelease)
            .Where(p => query.Trim().Length == 0 || p.Id.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.Version, VersionOrder.Instance).First())], cancel);

    public Task<IReadOnlyList<string>> VersionsAsync(string id, bool prerelease, CancellationToken cancel) => Task.Run(() =>
        (IReadOnlyList<string>)[.. Read(prerelease).Where(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Select(p => p.Version).OrderByDescending(v => v, VersionOrder.Instance)], cancel);

    private IEnumerable<PackageSummary> Read(bool prerelease)
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }
        foreach (string file in Directory.EnumerateFiles(folder, "*.nupkg"))
        {
            PackageSummary? package = null;
            try
            {
                using ZipArchive zip = ZipFile.OpenRead(file);
                if (zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !e.FullName.Contains('/', StringComparison.Ordinal)) is { } spec)
                {
                    using Stream stream = spec.Open();
                    XElement metadata = XDocument.Load(stream).Root!.Elements().First(e => e.Name.LocalName == "metadata");
                    string Field(string name) => metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? string.Empty;
                    package = new PackageSummary(Field("id"), Field("version"), Field("description"), Field("authors"), 0);
                }
            }
            catch (Exception error) when (error is InvalidDataException or IOException or System.Xml.XmlException or InvalidOperationException)
            {
                // A file that is not a package is passed over.
            }
            if (package is not null && (prerelease || !VersionOrder.IsPrerelease(package.Version)))
            {
                yield return package;
            }
        }
    }
}

/// <summary>NuGet's order of versions, SemVer 2's precedence: by their numbers (1.0 is 1.0.0.0), a prerelease before
/// its release, and prerelease labels dot by dot, numbers by value and below words, words as NuGet compares them,
/// ignoring case. Build metadata after a + takes no part.</summary>
public sealed class VersionOrder : IComparer<string>
{
    public static readonly VersionOrder Instance = new();

    public static bool IsPrerelease(string version) => Release(version).Contains('-', StringComparison.Ordinal);

    public int Compare(string? x, string? y)
    {
        (long[] a, string[]? aLabel) = Parse(x ?? string.Empty);
        (long[] b, string[]? bLabel) = Parse(y ?? string.Empty);
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return a[i].CompareTo(b[i]);
            }
        }
        if (aLabel is null || bLabel is null)
        {
            // A release is above its prereleases.
            return (aLabel is null).CompareTo(bLabel is null);
        }
        for (int i = 0; i < Math.Min(aLabel.Length, bLabel.Length); i++)
        {
            int order = CompareIdentifiers(aLabel[i], bLabel[i]);
            if (order != 0)
            {
                return order;
            }
        }
        return aLabel.Length.CompareTo(bLabel.Length);
    }

    private static int CompareIdentifiers(string a, string b)
    {
        bool aNumber = a.Length > 0 && a.All(char.IsAsciiDigit);
        bool bNumber = b.Length > 0 && b.All(char.IsAsciiDigit);
        if (aNumber && bNumber)
        {
            // Digits of any length, compared by value: the longer number without leading zeros is the larger.
            string aDigits = a.TrimStart('0');
            string bDigits = b.TrimStart('0');
            return aDigits.Length != bDigits.Length ? aDigits.Length.CompareTo(bDigits.Length) : string.CompareOrdinal(aDigits, bDigits);
        }
        if (aNumber != bNumber)
        {
            return aNumber ? -1 : 1;
        }
        return StringComparer.OrdinalIgnoreCase.Compare(a, b);
    }

    private static string Release(string version) => version.Split('+', 2)[0];

    /// <summary>A version's four numbers, those it leaves out 0, and its prerelease label's identifiers, null for a release.</summary>
    private static (long[] Numbers, string[]? Label) Parse(string version)
    {
        string[] parts = Release(version.Trim()).Split('-', 2);
        long[] numbers = new long[4];
        string[] given = parts[0].Split('.');
        for (int i = 0; i < Math.Min(given.Length, numbers.Length); i++)
        {
            numbers[i] = long.TryParse(given[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long number) ? number : 0;
        }
        return (numbers, parts.Length > 1 ? parts[1].Split('.') : null);
    }
}
