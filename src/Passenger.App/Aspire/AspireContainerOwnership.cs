using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Passenger.App.Containers;
using Passenger.App.State;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.App.Aspire;

/// <summary>The session containers an AppHost's orchestrator (DCP) made for a run of this process's, as the AppHost's
/// authenticated resource stream reports them, followed while the run is up whatever the window shows
/// (<see cref="AspireController"/>). DCP's containers carry none of Passenger's labels, so they are recorded in the state
/// folder with <see cref="ContainerLabels"/>' state, machine and process, and the same removal ends them: the run's Stop
/// and the folder's end (<see cref="TakeDownAsync"/>), the watchdog's last command (<see cref="ContainerLabels.MadeUnlabelled"/>)
/// and the next start's (<see cref="RemoveLeftoversAsync"/>), by <see cref="ContainerLabels.IsLeftover"/>. A Persistent
/// container (the resource's container.lifetime, DCP's <see cref="PersistentLabel"/>) is meant to outlive the AppHost: it is
/// the person's to keep, never recorded nor removed. UI-thread owned but for the leftovers' removal.</summary>
internal sealed class AspireContainerOwnership
{
    /// <summary>DCP's label on what it makes: true on a Persistent container (and its network), which outlives its AppHost.</summary>
    public const string PersistentLabel = "com.microsoft.developer.usvc-dev.persistent";

    private const int MaximumRecords = 1024;
    private const long MaximumBytes = 1024 * 1024;
    private readonly ContainerEngine engine;
    private readonly StateDocument<Manifest> document;
    private readonly string state;
    private readonly CancellationToken closing;
    private readonly Dictionary<string, long> observations = new(StringComparer.Ordinal);

    /// <summary>What this process recorded of each AppHost (by its hash), until its run's take-down.</summary>
    private readonly Dictionary<string, HashSet<string>> made = new(StringComparer.Ordinal);

    /// <summary>Each AppHost's containers as last recorded (their ids, lifetimes and names), by its hash.</summary>
    private readonly Dictionary<string, string> recordedAs = new(StringComparer.Ordinal);

    /// <summary>The containers said to be on another engine than Passenger's (<see cref="Elsewhere"/>), each said once.</summary>
    private readonly HashSet<string> elsewhere = new(StringComparer.Ordinal);
    private Task<Identity?>? observingIdentity;

    public AspireContainerOwnership(string stateFolder, ContainerEngine engine, CancellationToken closing = default)
    {
        this.engine = engine;
        this.closing = closing;
        state = ContainerLabels.StateValueOf(stateFolder);
        document = new(ManifestOf(stateFolder));
    }

    private static string ManifestOf(string stateFolder) => Path.Combine(stateFolder, "aspire-containers", "ownership.json");

    /// <summary>An AppHost's orchestrator made containers (how many) on another engine than the one Passenger reaches: its
    /// own runtime (ASPIRE_CONTAINER_RUNTIME, else its own look) is not Passenger's docker's. On the UI thread.</summary>
    public event EventHandler<(string AppHost, int Containers)>? Elsewhere;

    /// <summary>Call only for a snapshot from the still-owned AppHost's authenticated connection; await on the UI thread.</summary>
    public async Task Observe(string ownedAppHostPath, IReadOnlyList<AspireResource> resources)
    {
        UiThread.Check();
        if (closing.IsCancellationRequested)
        {
            return;
        }
        if (!Path.IsPathFullyQualified(ownedAppHostPath))
        {
            return;
        }
        string appHost = Hash(Path.GetFullPath(ownedAppHostPath));
        // Most batches are a health or state's change: containers already recorded read no manifest again.
        string signature = string.Join('\n', resources.Select(resource => resource.Data)
            .Where(data => string.Equals(String(data, "resourceType"), "Container", StringComparison.OrdinalIgnoreCase))
            .Select(data => $"{String(data, "properties", "container.id")} {String(data, "properties", "container.lifetime")} {String(data, "name")}").Order(StringComparer.Ordinal));
        if (recordedAs.GetValueOrDefault(appHost) == signature)
        {
            return;
        }
        long observation = observations.GetValueOrDefault(appHost) + 1;
        observations[appHost] = observation;
        using IDisposable held = BackgroundWork.Scope();
        List<Entry> entries = [.. (await ReadAsync()).Entries];
        // A later snapshot of the AppHost's, come while the manifest was read, is the one to record.
        if (closing.IsCancellationRequested || observations[appHost] != observation)
        {
            return;
        }
        List<(string Id, string Name, string Uid)> observed = [];
        HashSet<string> withdrawn = new(StringComparer.Ordinal);
        foreach (AspireResource resource in resources)
        {
            JsonElement data = resource.Data;
            string? id = String(data, "properties", "container.id");
            string? name = String(data, "name");
            string? uid = String(data, "uid");
            if (!string.Equals(String(data, "resourceType"), "Container", StringComparison.OrdinalIgnoreCase) || !FullId(id))
            {
                continue;
            }
            if (String(data, "properties", "container.lifetime") != "Session")
            {
                withdrawn.Add(id!);
            }
            else if (observed.Count < MaximumRecords && ContainerName(name) && SafeText(uid, 256))
            {
                observed.Add((id!, name!, uid!));
            }
        }
        if (entries.RemoveAll(entry => IsCurrent(entry, appHost) && withdrawn.Contains(entry.Id)) > 0)
        {
            await SaveAsync(entries);
        }
        if (made.TryGetValue(appHost, out HashSet<string>? recorded) && recorded.Overlaps(withdrawn))
        {
            recorded.ExceptWith(withdrawn);
            ContainerLabels.EndedUnlabelled(withdrawn);
        }
        observed.RemoveAll(item => withdrawn.Contains(item.Id) || entries.Any(entry => IsCurrent(entry, appHost)
            && entry.Id == item.Id && entry.Name == item.Name && entry.Uid == item.Uid));
        if (observed.Count == 0)
        {
            recordedAs[appHost] = signature;
            return;
        }
        Task<Identity?> readingIdentity = observingIdentity is { IsCompleted: false } pending
            ? pending : observingIdentity = Task.Run(() => ReadIdentityAsync(engine, again: false));
        Identity? identity = await readingIdentity;
        if (closing.IsCancellationRequested || observations[appHost] != observation)
        {
            return;
        }
        if (identity is null)
        {
            IdeErrors.Write("Aspire containers: ownership was not recorded because the engine identity could not be pinned");
            return;
        }
        // The orchestrator picks its runtime itself (ASPIRE_CONTAINER_RUNTIME, else its own look): one the engine Passenger
        // reaches does not hold is said, never recorded, since nothing of Passenger's could show, follow or remove it.
        observed.RemoveAll(item => elsewhere.Contains(item.Id));
        string[] asked = [.. observed.Select(item => item.Id)];
        if (asked.Length > 0 && await BackgroundWork.Run(() => ThereAsync(engine, asked, "as the AppHost made them")) is { } there && there.Length < asked.Length)
        {
            string[] away = [.. asked.Except(there)];
            elsewhere.UnionWith(away);
            observed.RemoveAll(item => away.Contains(item.Id));
            Elsewhere?.Invoke(this, (ownedAppHostPath, away.Length));
        }
        entries = [.. (await ReadAsync()).Entries];
        if (closing.IsCancellationRequested || observations[appHost] != observation || observed.Count == 0)
        {
            return;
        }
        List<string> added = [];
        foreach ((string id, string name, string uid) in observed)
        {
            Entry entry = new(id, name, uid, appHost, identity.Fingerprint,
                state, ContainerLabels.MachineValue, ContainerLabels.ProcessValue);
            if (!entries.Contains(entry))
            {
                entries.RemoveAll(known => IsCurrent(known, appHost) && known.Id == id);
                if (entries.Count == MaximumRecords)
                {
                    IdeErrors.Write("Aspire containers: ownership manifest is full; additional containers are left untracked");
                    break;
                }
                entries.Add(entry);
                added.Add(id);
            }
        }
        await SaveAsync(entries);
        recordedAs[appHost] = signature;
        if (added.Count > 0)
        {
            if (!made.TryGetValue(appHost, out recorded))
            {
                made[appHost] = recorded = new(StringComparer.Ordinal);
            }
            recorded.UnionWith(added);
            ContainerLabels.MadeUnlabelled(engine, added);
        }
    }

    /// <summary>Removes the session containers this process recorded of an AppHost's run once <paramref name="after"/> (its
    /// AppHost's end) has come (the run's Stop, the folder's end), as a container run's Stop takes its own down: done once
    /// the engine has answered, or given up after its limit, what fails the IDE's error said with <paramref name="when"/>.
    /// Begun on the UI thread; the rest needs none, so the folder's end can wait for it after the window's loop has stopped.</summary>
    public Task TakeDownAsync(string appHostPath, string when, Task? after = null)
    {
        UiThread.Check();
        string appHost = Hash(Path.GetFullPath(appHostPath));
        // A snapshot still on its way to the UI thread records nothing of a run taken down.
        observations[appHost] = observations.GetValueOrDefault(appHost) + 1;
        recordedAs.Remove(appHost);
        if (!made.Remove(appHost, out HashSet<string>? recorded) || recorded.Count == 0)
        {
            return Task.CompletedTask;
        }
        string[] ids = [.. recorded];
        StateDocument<Manifest> kept = document;
        // Not held: nothing it does is drawn, and the folder's end waits for it once the window's loop may have stopped.
        return Task.Run(async () =>
        {
            if (after is not null)
            {
                // An orchestrator that outlived its AppHost could make one again of a container taken from under a live one.
                await after.ContinueWith(_ => { }, TaskScheduler.Default).ConfigureAwait(false);
            }
            // Those the orchestrator has not removed itself, which an engine's rm of one gone would call a failure.
            if (await ThereAsync(engine, ids, when) is { } there && await FolderContainers.RemoveAsync(engine, there, when))
            {
                Forget(kept, ids);
                ContainerLabels.EndedUnlabelled(ids);
            }
        });
    }

    /// <summary>The containers of those given the engine still holds, by their whole ids; null when it does not answer.</summary>
    private static async Task<string[]?> ThereAsync(ContainerEngine engine, IReadOnlyList<string> ids, string when)
    {
        try
        {
            ChildResult listed = await engine.RunAsync(["ps", "-aq", "--no-trunc", .. ids.SelectMany(id => (string[])["--filter", $"id={id}"])],
                ContainerEngine.AskLimit);
            if (listed.Succeeded)
            {
                return [.. listed.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(ids.Contains)];
            }
            IdeErrors.Write($"Aspire containers: the engine did not list the run's containers {when}: {ContainerEngine.Said(listed)}");
        }
        catch (Exception failed) when (failed is TimeoutException or FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            IdeErrors.Write($"Aspire containers: the engine did not list the run's containers {when}: {failed.Message}");
        }
        return null;
    }

    /// <summary>Removes the containers the state's record says a process of this state and machine that is gone had an
    /// orchestrator make (<see cref="ContainerLabels.IsLeftover"/>), on the engine it recorded them on, each only while it is
    /// still the container recorded (its id and name) and not Persistent; a recorded container the engine no longer holds is
    /// forgotten. No engine command runs when no record is a leftover. Off the UI thread; the ids removed.</summary>
    public static async Task<IReadOnlyList<string>> RemoveLeftoversAsync(string stateFolder, ContainerEngine engine, CancellationToken cancel = default)
    {
        string path = ManifestOf(stateFolder);
        if (!File.Exists(path))
        {
            return [];
        }
        if (new FileInfo(path).Length > MaximumBytes)
        {
            IdeErrors.Write("Aspire containers: the ownership manifest exceeds its size limit; its leftovers are not removed");
            return [];
        }
        StateDocument<Manifest> record = new(path);
        string stateValue = ContainerLabels.StateValueOf(stateFolder);
        Entry[] candidates = [.. record.Peek().Entries.Where(entry => ContainerLabels.IsLeftover(entry.State, entry.Machine, entry.Owner, stateValue))];
        if (candidates.Length == 0)
        {
            return [];
        }
        Identity? identity = await ReadIdentityAsync(engine).ConfigureAwait(false);
        if (identity is null)
        {
            IdeErrors.Write("Aspire containers: leftover cleanup deferred because the engine identity could not be pinned");
            return [];
        }
        foreach (Entry other in candidates.Where(entry => entry.Engine != identity.Fingerprint))
        {
            IdeErrors.Write($"Aspire containers: retained {other.Id}; the current engine identity differs");
        }
        Entry[] here = [.. candidates.Where(entry => entry.Engine == identity.Fingerprint)];
        if (here.Length == 0)
        {
            return [];
        }
        List<Entry> removing = [];
        List<Entry> gone = [];
        try
        {
            // Several filters of one key are any of them to the engine: one list of those still there, labels and all.
            ContainerInfo[] listed = [.. (await engine.JsonLinesAsync([.. identity.Arguments, "ps", "-a", "--no-trunc",
                .. here.SelectMany(entry => (string[])["--filter", $"id={entry.Id}"]), "--format", "json"], cancel).ConfigureAwait(false)).Select(ContainerInfo.Read)];
            // An answer naming a container no entry names does not hold together: what it leaves out is not taken as gone.
            bool together = listed.All(container => here.Any(entry => entry.Id == container.Id));
            foreach (Entry entry in here)
            {
                ContainerInfo? found = listed.FirstOrDefault(container => container.Id == entry.Id);
                if (found is null)
                {
                    if (together)
                    {
                        gone.Add(entry);
                    }
                    else
                    {
                        IdeErrors.Write($"Aspire containers: retained {entry.Id}; the engine listed another container for it");
                    }
                }
                else if (found.Name.TrimStart('/') != entry.Name || found.Labels.GetValueOrDefault(PersistentLabel) == "true")
                {
                    IdeErrors.Write($"Aspire containers: retained {entry.Id}; its name changed or it is Persistent");
                }
                else
                {
                    removing.Add(entry);
                }
            }
            if (removing.Count > 0)
            {
                ChildResult result = await engine.RunAsync([.. identity.Arguments, "rm", "-f", .. removing.Select(entry => entry.Id)], ContainerEngine.AskLimit * 3, cancel)
                    .ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    throw new ContainerCommandException(ContainerEngine.Said(result));
                }
                IdeErrors.Write($"Aspire containers: removed {Plural.Of(removing.Count, "container")} an earlier Passenger's AppHost left");
            }
        }
        catch (Exception error) when (error is IOException or ContainerCommandException or JsonException or TimeoutException
            or System.ComponentModel.Win32Exception)
        {
            IdeErrors.Write($"Aspire containers: cleanup of {Plural.Of(here.Length, "container")} deferred: {error.Message}");
            removing.Clear();
        }
        if (removing.Count + gone.Count > 0)
        {
            Forget(record, [.. removing.Concat(gone).Select(entry => entry.Id)]);
        }
        return [.. removing.Select(entry => entry.Id)];
    }

    /// <summary>Takes containers out of the record as the file holds it now, on any thread.</summary>
    private static void Forget(StateDocument<Manifest> record, IReadOnlyCollection<string> ids)
    {
        try
        {
            record.ChangeNow(kept => new Manifest { Entries = [.. kept.Entries.Where(entry => !ids.Contains(entry.Id))] });
        }
        catch (Exception unwritten) when (unwritten is IOException or UnauthorizedAccessException)
        {
            IdeErrors.Write($"Aspire containers: the ownership manifest could not be written: {unwritten.Message}");
        }
    }

    private bool IsCurrent(Entry entry, string appHost) => entry.Owner == ContainerLabels.ProcessValue && entry.State == state
        && entry.Machine == ContainerLabels.MachineValue && entry.AppHost == appHost;

    private static async Task<Identity?> ReadIdentityAsync(ContainerEngine engine, bool again = true)
    {
        EngineState current = await engine.StateAsync(again: again).ConfigureAwait(false);
        if (current is not { Cli: { Length: > 0 } cli, Endpoint: { Length: > 0 } endpoint }
            || !Path.IsPathFullyQualified(cli) || current.Status is not (EngineStatus.Ready or EngineStatus.NoCompose))
        {
            return null;
        }
        if (Environment.GetEnvironmentVariable("DOCKER_HOST") is { Length: > 0 } host && host != endpoint)
        {
            return null;
        }
        string[] arguments = current.Context is { Length: > 0 } context ? ["--context", context] : ["--host", endpoint];
        if (current.Context is { Length: > 0 } named)
        {
            JsonElement inspected = await engine.JsonAsync(["context", "inspect", named]).ConfigureAwait(false);
            if (inspected.ValueKind != JsonValueKind.Array || inspected.GetArrayLength() != 1
                || ContainerEngine.Text(inspected[0], "Name") != named
                || ContainerEngine.Text(inspected[0], "Endpoints", "docker", "Host") != endpoint)
            {
                return null;
            }
        }
        string environment = string.Join('\n', new[] { "DOCKER_CONFIG", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH", "DOCKER_TLS" }
            .Select(variable => $"{variable}={Environment.GetEnvironmentVariable(variable)}"));
        return new(Hash($"{Path.GetFullPath(cli)}\n{current.Context}\n{endpoint}\n{environment}"), arguments);
    }

    /// <summary>The manifest as the disk has it now (another Passenger over the same state writes it too), read off the UI thread.</summary>
    private Task<Manifest> ReadAsync() => BackgroundWork.Run(() =>
    {
        if (File.Exists(document.Path) && new FileInfo(document.Path).Length > MaximumBytes)
        {
            throw new InvalidDataException("The Aspire container ownership manifest exceeds its size limit; cleanup is disabled.");
        }
        return document.Peek();
    });

    /// <summary>Keeps the entries, written at once off the UI thread (a crash a moment later still leaves the record), merged
    /// over what another Passenger wrote meanwhile.</summary>
    private async Task SaveAsync(List<Entry> entries)
    {
        await document.ValueAsync();
        string folder = Path.GetDirectoryName(document.Path)!;
        await BackgroundWork.Run(() =>
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        });
        document.Set(new Manifest { Entries = entries });
        await BackgroundWork.Run(document.Flush);
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string? String(JsonElement value, params string[] path)
    {
        foreach (string name in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out value))
            {
                return null;
            }
        }
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool FullId(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static bool SafeText(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum && !value.Any(char.IsControl);
    private static bool ContainerName(string? value) => SafeText(value, 255) && char.IsAsciiLetterOrDigit(value![0])
        && value!.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-');
    private static bool OwnerValue(string? value) => value?.Split('-') is [var pid, var start]
        && int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0
        && long.TryParse(start, NumberStyles.None, CultureInfo.InvariantCulture, out long began) && began > 0;

    private sealed record Identity(string Fingerprint, string[] Arguments);
    public sealed record Entry(string Id, string Name, string Uid, string AppHost, string Engine, string State, string Machine, string Owner);

    public sealed class Manifest : IStateDocument<Manifest>
    {
        public static IReadOnlyList<Action<JsonObject>> Upgrades => [];
        public static IReadOnlyList<string> Sets => [nameof(Entries)];
        public List<Entry> Entries { get; init; } = [];
        public Manifest Clamped() => new()
        {
            Entries = [.. (Entries ?? []).Take(MaximumRecords).Where(entry => entry is not null && FullId(entry.Id) && ContainerName(entry.Name)
                && SafeText(entry.Uid, 256) && FullId(entry.AppHost) && FullId(entry.Engine)
                && entry.State is { Length: 16 } && entry.Machine is { Length: 16 } && SafeText(entry.Owner, 64) && OwnerValue(entry.Owner)).Distinct()],
        };
    }
}
