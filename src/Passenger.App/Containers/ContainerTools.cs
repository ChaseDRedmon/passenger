using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using Passenger.App.Updates;
using Passenger.Core;

namespace Passenger.App.Containers;

/// <summary>A container's system as the tools need it: its processor (docker's name, amd64 or arm64) and C library.</summary>
public sealed record ContainerSystem(string Architecture, bool Musl)
{
    /// <summary>The tools' runtime identifier for it (linux-musl-arm64), or null for a processor none are made for.</summary>
    public string? Rid => Architecture switch
    {
        "amd64" => Musl ? "linux-musl-x64" : "linux-x64",
        "arm64" => Musl ? "linux-musl-arm64" : "linux-arm64",
        _ => null,
    };
}

/// <summary>Where Passenger's container tools come from for this window: folders that may hold a copy already (beside the
/// app, a dev build's artifacts/container-tools, the state's cache), the release of this version to download the rest
/// from, and the cache it downloads into.</summary>
public sealed record ContainerToolsSource(IReadOnlyList<string> Local, ReleaseFeed Releases, string Cache)
{
    /// <summary>Beside the app (container-tools/RID), a dev build's artifacts/container-tools/RID (found up from the app's
    /// folder), and the state's cache/container-tools/VERSION/RID, downloaded from <paramref name="releases"/>.</summary>
    public static ContainerToolsSource Default(ReleaseFeed releases) => new(
        [Path.Combine(AppContext.BaseDirectory, "container-tools"), .. DevBuild() is { } dev ? [dev] : Array.Empty<string>()],
        releases, Path.Combine(State.StateStore.DefaultRoot, "cache", "container-tools"));

    /// <summary>artifacts/container-tools of the build the app runs from (artifacts/bin/Passenger.App/debug), where
    /// <c>just container-tools</c> puts them.</summary>
    private static string? DevBuild()
    {
        for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (at.Name == "artifacts")
            {
                return Path.Combine(at.FullName, "container-tools");
            }
        }
        return null;
    }
}

/// <summary>Passenger's container tools on this side (src/Passenger.ContainerTools runs inside): a copy for a container's
/// system found on this machine or downloaded once (its own version's release, checked against that release's
/// SHA256SUMS, into the state's cache through a temporary folder renamed), a container's system read once per image,
/// and the tools put into a container: mounted at <see cref="Mounted"/> where Passenger starts it (<see cref="MountFor"/>),
/// else copied to <see cref="Copied"/>. Any thread: nothing here touches the window.</summary>
public static class ContainerTools
{
    public const string Executable = "passenger-tools";

    /// <summary>Where the tools are mounted, read-only, into a container Passenger starts for debugging or profiling.</summary>
    public const string Mounted = "/passenger/tools";

    /// <summary>Where they are copied into a container Passenger did not start (or whose engine cannot see this machine's files).</summary>
    public const string Copied = "/tmp/passenger-tools";

    /// <summary>Where the adapter in a container keeps the symbols it decompiles.</summary>
    public const string SymbolCache = "/tmp/passenger-symbols";

    /// <summary>The release's archive of the tools for a system, by the fixed name the release workflow gives it.</summary>
    public static string Asset(string rid) => $"Passenger-container-tools-{rid}.tar.gz";

    /// <summary>A <c>-v</c> value (or a Compose service's <c>volumes:</c> entry) that mounts the tools' folder on this machine
    /// read-only at <see cref="Mounted"/>: what a container run configuration adds to a container it starts for Debug.</summary>
    public static string MountFor(string folder) => $"{folder}:{Mounted}:ro";

    /// <summary>Each image's system, read once (by the image's id).</summary>
    private static readonly ConcurrentDictionary<string, ContainerSystem> Systems = new(StringComparer.Ordinal);

    /// <summary>The containers the tools were copied into in this process, by id: where they are.</summary>
    private static readonly ConcurrentDictionary<string, string> CopiedInto = new(StringComparer.Ordinal);

    /// <summary>The tools' folder on this machine for a system, its executable inside: a copy found, else downloaded.
    /// <paramref name="say"/> tells of a download's progress. Throws <see cref="ContainerToolsException"/> saying why there
    /// are none.</summary>
    public static async Task<string> FolderAsync(ContainerToolsSource source, string rid, Action<string>? say, CancellationToken cancel)
    {
        foreach (string local in source.Local)
        {
            string folder = Path.Combine(local, rid);
            if (File.Exists(Path.Combine(folder, Executable)))
            {
                return folder;
            }
        }
        string version = Product.Version;
        string cached = Path.Combine(source.Cache, version, rid);
        if (File.Exists(Path.Combine(cached, Executable)))
        {
            return cached;
        }
        return await DownloadAsync(source, version, rid, cached, say, cancel).ConfigureAwait(false);
    }

    /// <summary>The tools of a release downloaded, checked and unpacked into <paramref name="cached"/>.</summary>
    private static async Task<string> DownloadAsync(ContainerToolsSource source, string version, string rid, string cached, Action<string>? say, CancellationToken cancel)
    {
        string tag = $"v{version}";
        Uri release = new(source.Releases.Downloads, $"{tag}/");
        string asset = Asset(rid);
        string parent = Path.GetDirectoryName(cached)!;
        Directory.CreateDirectory(parent);
        string archive = Path.Combine(parent, $".{asset}.{Guid.NewGuid():N}");
        string unpacking = Path.Combine(parent, $".{rid}.{Guid.NewGuid():N}");
        try
        {
            say?.Invoke($"Downloading Passenger's container tools for {rid}");
            string sums = await Releases.ReadTextAsync(new ReleaseFile("SHA256SUMS", new Uri(release, "SHA256SUMS"), 0), version, cancel).ConfigureAwait(false);
            string? expected = sums.Split('\n').Select(line => line.Trim().Split(' ', 2, StringSplitOptions.TrimEntries))
                .FirstOrDefault(parts => parts.Length == 2 && parts[1].TrimStart('*') == asset)?[0].ToLowerInvariant();
            if (expected is null)
            {
                throw new ContainerToolsException($"The {tag} release's SHA256SUMS lists no {asset}");
            }
            string got = await Releases.DownloadAsync(new ReleaseFile(asset, new Uri(release, asset), 0), archive, version,
                percent => say?.Invoke($"Downloading Passenger's container tools for {rid}: {percent}%"), cancel).ConfigureAwait(false);
            if (got != expected)
            {
                IdeLog.Write(IdeLog.Debugger, $"{asset} of {tag} has SHA-256 {got}, where SHA256SUMS says {expected}: refused");
                throw new ContainerToolsException($"The container tools downloaded for {rid} are not what the {tag} release's SHA256SUMS says they are, and were not used");
            }
            Directory.CreateDirectory(unpacking);
            await using (FileStream file = File.OpenRead(archive))
            await using (GZipStream unzipped = new(file, CompressionMode.Decompress))
            {
                await TarFile.ExtractToDirectoryAsync(unzipped, unpacking, overwriteFiles: false, cancel).ConfigureAwait(false);
            }
            if (!File.Exists(Path.Combine(unpacking, Executable)))
            {
                throw new ContainerToolsException($"{asset} of {tag} holds no {Executable}");
            }
            if (Directory.Exists(cached))
            {
                Directory.Delete(cached, recursive: true);
            }
            Directory.Move(unpacking, cached);
            return cached;
        }
        catch (Exception failed) when (failed is UpdateFailedException or UpdateRefusedException)
        {
            throw new ContainerToolsException($"Passenger's container tools for {rid} could not be downloaded ({failed.Message})"
                + (Product.Version.StartsWith("0.1.", StringComparison.Ordinal) || !File.Exists(Path.Combine(AppContext.BaseDirectory, "LICENSE.txt"))
                    ? $"; a build of your own makes them with just container-tools {rid}" : string.Empty));
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new ContainerToolsException($"Passenger's container tools for {rid} could not be unpacked: {failed.Message}");
        }
        finally
        {
            File.Delete(archive);
            if (Directory.Exists(unpacking))
            {
                Directory.Delete(unpacking, recursive: true);
            }
        }
    }

    /// <summary>The engine's own processor, docker's name for it (amd64, arm64).</summary>
    public static async Task<string> EngineArchitectureAsync(ContainerEngine engine, CancellationToken cancel)
    {
        ChildResult result = await engine.RunAsync(["version", "--format", "{{.Server.Arch}}"], cancel: cancel).ConfigureAwait(false);
        return result.Succeeded ? result.Output.Trim() : throw new ContainerToolsException($"The container engine did not say its processor: {ContainerEngine.Said(result)}");
    }

    /// <summary>A container's system: its image's processor, and musl when its /lib holds musl's loader (read by docker cp,
    /// which needs nothing in the image and works on a container created and never started). Once per image.</summary>
    public static async Task<ContainerSystem> SystemOfContainerAsync(ContainerEngine engine, string container, CancellationToken cancel)
    {
        JsonElement inspected = await engine.JsonAsync(["inspect", "--type", "container", container], cancel).ConfigureAwait(false);
        string image = ContainerEngine.Text(inspected[0], "Image") ?? throw new ContainerToolsException($"docker inspect named no image of {container}");
        if (Systems.TryGetValue(image, out ContainerSystem? known))
        {
            return known;
        }
        JsonElement described = await engine.JsonAsync(["image", "inspect", image], cancel).ConfigureAwait(false);
        string architecture = ContainerEngine.Text(described[0], "Architecture") ?? "amd64";
        string loader = architecture == "arm64" ? "aarch64" : architecture == "amd64" ? "x86_64" : architecture;
        string probe = Path.Combine(Path.GetTempPath(), $"passenger-libc-{Guid.NewGuid():N}");
        try
        {
            ChildResult copied = await engine.RunAsync(["cp", $"{container}:/lib/ld-musl-{loader}.so.1", probe], cancel: cancel).ConfigureAwait(false);
            ContainerSystem system = new(architecture, copied.Succeeded);
            Systems[image] = system;
            return system;
        }
        finally
        {
            File.Delete(probe);
        }
    }

    /// <summary>An image's system, read from a container created from it (never started, labelled as all Passenger makes,
    /// removed at once): for a container Passenger is about to start with the tools mounted.</summary>
    public static async Task<ContainerSystem> SystemOfImageAsync(ContainerEngine engine, string image, IReadOnlyList<string> labels, CancellationToken cancel)
    {
        JsonElement described = await engine.JsonAsync(["image", "inspect", image], cancel).ConfigureAwait(false);
        if (ContainerEngine.Text(described[0], "Id") is { } id && Systems.TryGetValue(id, out ContainerSystem? known))
        {
            return known;
        }
        ChildResult created = await engine.RunAsync(["create", .. labels, image], cancel: cancel).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            throw new ContainerToolsException($"Could not read the system of image {image}: {ContainerEngine.Said(created)}");
        }
        string container = created.Output.Trim();
        try
        {
            return await SystemOfContainerAsync(engine, container, cancel).ConfigureAwait(false);
        }
        finally
        {
            await engine.RunAsync(["rm", "-f", container], cancel: CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Where the tools' executable is in a container now: mounted where Passenger started it, or copied in by this
    /// process; null when it has none.</summary>
    public static async Task<string?> InContainerAsync(ContainerEngine engine, string container, CancellationToken cancel)
    {
        foreach (string folder in (string[])[Mounted, CopiedInto.GetValueOrDefault(container) ?? Copied])
        {
            string executable = $"{folder}/{Executable}";
            ChildResult said = await engine.RunAsync(["exec", .. Unheld, container, executable, "version"], cancel: cancel).ConfigureAwait(false);
            if (said.Succeeded && said.Output.Trim() == Product.Version)
            {
                return executable;
            }
        }
        return null;
    }

    /// <summary>Copies the tools into a container (<see cref="Copied"/>), and gives where their executable is there.</summary>
    public static async Task<string> CopyIntoAsync(ContainerEngine engine, string container, string folder, CancellationToken cancel)
    {
        // docker cp makes the folder and keeps each file's mode, the executable's among them (Windows' CLI marks every file executable).
        ChildResult copied = await engine.RunAsync(["cp", Path.Combine(folder, "."), $"{container}:{Copied}"], TimeSpan.FromMinutes(2), cancel).ConfigureAwait(false);
        if (!copied.Succeeded)
        {
            throw new ContainerToolsException($"Could not copy Passenger's container tools into {container}: {ContainerEngine.Said(copied)}");
        }
        CopiedInto[container] = Copied;
        return $"{Copied}/{Executable}";
    }

    /// <summary>The tools' <c>processes</c> in a container: its .NET programs, by their ids there.</summary>
    public static async Task<IReadOnlyList<DotnetProcess>> ProcessesAsync(ContainerEngine engine, string container, string executable, CancellationToken cancel)
    {
        ChildResult listed = await engine.RunAsync(["exec", .. Unheld, container, executable, "processes"], cancel: cancel).ConfigureAwait(false);
        if (!listed.Succeeded)
        {
            throw new ContainerToolsException($"Passenger's tools could not list the programs in {container}: {ContainerEngine.Said(listed)}");
        }
        using JsonDocument document = JsonDocument.Parse(listed.Output);
        return [.. document.RootElement.EnumerateArray().Select(p => new DotnetProcess(
            p.GetProperty("id").GetInt32(), p.GetProperty("name").GetString() ?? string.Empty, p.GetProperty("commandLine").GetString() ?? string.Empty,
            p.GetProperty("user").GetString() ?? string.Empty, true)
        {
            Container = container,
            HotReload = p.TryGetProperty("hotReload", out JsonElement hot) && hot.ValueKind == JsonValueKind.True,
            Note = p.TryGetProperty("optimized", out JsonElement optimized) && optimized.ValueKind == JsonValueKind.True ? ReleaseBuild : null,
            WhyNot = p.TryGetProperty("whyNot", out JsonElement why) ? why.GetString() : null,
        })];
    }

    /// <summary>What a person should know of a Release build: Just My Code finds none of its code the user's.</summary>
    public const string ReleaseBuild = "A Release build: optimized, so under Just My Code none of its code is yours and no breakpoint in it stops";

    /// <summary>docker exec runs the tools in the container's environment, which a run held for the debugger sets for its
    /// program: the tools' own runtime would hold at its start too, waiting for a debugger never coming, or load the
    /// program's startup hooks. Cleared for every command of theirs.</summary>
    private static readonly string[] Unheld =
        ["-e", "DOTNET_DefaultDiagnosticPortSuspend=0", "-e", "DOTNET_DiagnosticPorts=", "-e", "DOTNET_STARTUP_HOOKS=", "-e", "DOTNET_MODIFIABLE_ASSEMBLIES="];

    /// <summary>The adapter's variable for how long an evaluation may run (Passenger.Debugger's).</summary>
    private const string EvaluationLimit = "PASSENGER_EVAL_TIMEOUT_MS";

    /// <summary>The adapter's command in a container, over docker exec's standard input and output: the IDE's evaluation
    /// limit given it as the IDE's own adapter has it.</summary>
    public static IReadOnlyList<string> AdapterCommand(string container, string executable, IReadOnlyList<string> adapterArguments)
    {
        List<string> words = ["exec", "-i", .. Unheld];
        if (Environment.GetEnvironmentVariable(EvaluationLimit) is { Length: > 0 } limit)
        {
            words.AddRange(["-e", $"{EvaluationLimit}={limit}"]);
        }
        return [.. words, container, executable, "dap", .. adapterArguments];
    }

    /// <summary>A mode of the tools in a container, over docker exec's standard input and output: the profiler's
    /// <c>trace</c>, <c>heap</c> and <c>sources</c>.</summary>
    public static IReadOnlyList<string> ToolsCommand(string container, string executable, IReadOnlyList<string> words) =>
        ["exec", "-i", .. Unheld, container, executable, .. words];
}

/// <summary>Passenger's container tools could not be had or used: no copy and no download, a checksum that did not match, a
/// container they could not be copied into. Its message says so in a line.</summary>
public sealed class ContainerToolsException(string message) : Exception(message);
