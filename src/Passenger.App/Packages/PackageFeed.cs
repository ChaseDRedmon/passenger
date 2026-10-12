using System.IO.Compression;
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

    /// <summary>A package's versions, newest first.</summary>
    Task<IReadOnlyList<string>> VersionsAsync(string id, bool prerelease, CancellationToken cancel);
}

/// <summary>nuget.org through its v3 protocol, as the NuGet client finds it: the service index names the search and
/// the flat container, which lists a package's versions.</summary>
public sealed class NuGetOrgFeed(HttpClient http) : IPackageFeed
{
    private const string Index = "https://api.nuget.org/v3/index.json";
    private (string Search, string Container)? services;

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
        (_, string container) = await ServicesAsync(cancel);
        VersionsResponse? listed = await http.GetFromJsonAsync<VersionsResponse>($"{container}{id.ToLowerInvariant()}/index.json", Json, cancel);
        return [.. (listed?.Versions ?? []).Where(v => prerelease || !v.Contains('-', StringComparison.Ordinal)).Reverse()];
    }

    private async Task<(string Search, string Container)> ServicesAsync(CancellationToken cancel)
    {
        if (services is { } known)
        {
            return known;
        }
        ServiceIndex? index = await http.GetFromJsonAsync<ServiceIndex>(Index, Json, cancel);
        string? Find(string type) => index?.Resources?.FirstOrDefault(r => r.Type == type)?.Id;
        string search = Find("SearchQueryService/3.5.0") ?? Find("SearchQueryService") ?? throw new InvalidOperationException("nuget.org names no search service");
        string container = Find("PackageBaseAddress/3.0.0") ?? throw new InvalidOperationException("nuget.org names no package base address");
        services = (search, container.EndsWith('/') ? container : container + "/");
        return services.Value;
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private sealed record ServiceIndex(IReadOnlyList<ServiceResource>? Resources);

    private sealed record ServiceResource([property: JsonPropertyName("@id")] string Id, [property: JsonPropertyName("@type")] string Type);

    private sealed record SearchResponse(IReadOnlyList<SearchHit>? Data);

    private sealed record SearchHit(string Id, string Version, string? Description, IReadOnlyList<string>? Authors, long TotalDownloads);

    private sealed record VersionsResponse(IReadOnlyList<string>? Versions);
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
            if (package is not null && (prerelease || !package.Version.Contains('-', StringComparison.Ordinal)))
            {
                yield return package;
            }
        }
    }
}

/// <summary>NuGet's order of versions: by their numbers, a prerelease before its release.</summary>
public sealed class VersionOrder : IComparer<string>
{
    public static readonly VersionOrder Instance = new();

    public int Compare(string? x, string? y)
    {
        (Version a, string? aTag) = Parse(x ?? string.Empty);
        (Version b, string? bTag) = Parse(y ?? string.Empty);
        int numbers = a.CompareTo(b);
        if (numbers != 0)
        {
            return numbers;
        }
        return (aTag, bTag) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => string.CompareOrdinal(aTag, bTag),
        };
    }

    private static (Version, string?) Parse(string text)
    {
        string[] parts = text.Split('+')[0].Split('-', 2);
        string numbers = parts[0].Contains('.', StringComparison.Ordinal) ? parts[0] : parts[0] + ".0";
        return (Version.TryParse(numbers, out Version? number) ? number : new Version(0, 0), parts.Length > 1 ? parts[1] : null);
    }
}
