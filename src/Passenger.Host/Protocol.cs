using System.Text.Json;
using System.Text.Json.Serialization;

namespace Passenger.Host;

// The project host's protocol, one definition for both ends (the app compiles this file too): one JSON object a line
// each way over the host's standard input and output. The app asks (HostRequest); the host answers each request with
// partial results as they come, then with its result or why it failed (HostReply, by the request's id). The host's
// first line, id 0, says which SDK's MSBuild it loaded, or why it loaded none.

public sealed record HostRequest(int Id, string Method, JsonElement Params);

public sealed record HostReply
{
    public int Id { get; init; }

    public JsonElement? Partial { get; init; }

    public JsonElement? Result { get; init; }

    public string? Error { get; init; }
}

public static class HostMethods
{
    /// <summary><see cref="SdkParams"/> to <see cref="SdkAnswer"/>: the SDK a folder's projects would load with.</summary>
    public const string Sdk = "sdk";

    /// <summary><see cref="EvaluateParams"/>: an <see cref="EvaluatedProject"/> partial per project as each is evaluated,
    /// then <see cref="EvaluateDone"/>.</summary>
    public const string Evaluate = "evaluate";
}

public static class HostJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>The SDK the host loaded: its folder (MSBuild's) and version.</summary>
public sealed record HostReady(string Sdk, string Version);

public sealed record SdkParams(string Folder);

/// <summary>The SDK a folder picks (its global.json, else the newest), or why none.</summary>
public sealed record SdkAnswer(string? Sdk, string? Problem);

/// <summary>Projects to evaluate, with the global properties they are evaluated with (the solution's).</summary>
public sealed record EvaluateParams(IReadOnlyList<string> Projects, IReadOnlyDictionary<string, string> Properties);

public sealed record EvaluateDone(int Evaluated, double Seconds);

/// <summary>One project as MSBuild evaluates it, every target framework of it: what the Solution view shows and what
/// knows when it changed. A project that does not evaluate has its <see cref="Problem"/> and nothing else.</summary>
public sealed record EvaluatedProject
{
    public required string Path { get; init; }

    /// <summary>False for a multi-targeted project known so far by its outer evaluation alone, which its frameworks'
    /// follow (the same project again, complete): the outer one is a fraction of the cost and shows the same files.</summary>
    public bool Complete { get; init; } = true;

    /// <summary>Why it did not evaluate: MSBuild's message, with the file and line it names.</summary>
    public string? Problem { get; init; }

    public string? AssemblyName { get; init; }

    public string? OutputType { get; init; }

    public IReadOnlyList<string> TargetFrameworks { get; init; } = [];

    /// <summary>IsTestProject as evaluated (a test SDK's props set it).</summary>
    public bool IsTestProject { get; init; }

    /// <summary>Its packages and project references, a target framework each, or one with none for a project with one.</summary>
    public IReadOnlyList<FrameworkDependencies> Dependencies { get; init; } = [];

    /// <summary>The files the project holds, every framework's, each once.</summary>
    public IReadOnlyList<EvaluatedItem> Items { get; init; } = [];

    /// <summary>The project file and the files it imports outside the SDK and the package folders (which change only
    /// with their versions, and so with these), as they were when it was evaluated.</summary>
    public IReadOnlyList<Stamp> Inputs { get; init; } = [];

    /// <summary>The folders its items' wildcards walked, as they were: one whose files came or went changed.</summary>
    public IReadOnlyList<Stamp> Folders { get; init; } = [];
}

public sealed record FrameworkDependencies(string? Framework, IReadOnlyList<PackageDependency> Packages, IReadOnlyList<string> Projects);

/// <summary>A package reference, its version as restore takes it: its own, its VersionOverride, or the central one.</summary>
public sealed record PackageDependency(string Name, string? Version, bool Implicit);

/// <summary>A file of the project: its item type, full path, its Link (where it shows when it lives elsewhere) and the file
/// it depends on, relative to its own folder, as MSBuild's DependentUpon is.</summary>
public sealed record EvaluatedItem(string Type, string Path, string? Link = null, string? DependentUpon = null);

/// <summary>A file's last write (UTC ticks) and length, 0 and -1 for one not there; for a folder, the hash of its names and
/// how many (<see cref="Stamps.Folder"/>).</summary>
public sealed record Stamp(string Path, long Ticks, long Length, bool Folder = false);
