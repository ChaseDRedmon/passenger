using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Passenger.App.Containers;
using Passenger.App.State;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.App.Aspire;

/// <summary>The session containers an AppHost's orchestrator (DCP) made for a run of this process's, as the AppHost's
/// authenticated resource stream reports them, followed while the run is up whatever the window shows
/// (<see cref="AspireController"/>), with the session network they are on. DCP's containers carry none of Passenger's
/// labels, so they are recorded in the state folder with <see cref="ContainerLabels"/>' state, machine and process, and the
/// same removal ends them: the run's Stop and the folder's end (<see cref="TakeDownAsync"/>), the watchdog's last command
/// (<see cref="ContainerLabels.MadeUnlabelled"/>) and the next start's (<see cref="RemoveLeftoversAsync"/>), by
/// <see cref="ContainerLabels.IsLeftover"/>. A Persistent container (the resource's container.lifetime, DCP's
/// <see cref="PersistentLabel"/>) and the network an AppHost with one keeps are meant to outlive the AppHost: the person's
/// to keep, never recorded nor removed. UI-thread owned but for the leftovers' removal.</summary>
internal sealed class AspireContainerOwnership
{
    /// <summary>DCP's label on what it makes: true on a Persistent container (and its network), which outlives its AppHost.</summary>
    public const string PersistentLabel = "com.microsoft.developer.usvc-dev.persistent";

    /// <summary>DCP's label on what it made, naming the process of its own that made it.</summary>
    private const string CreatorLabel = "com.microsoft.developer.usvc-dev.creatorProcessId";

    private const int MaximumRecords = 1024;
    private const long MaximumBytes = 1024 * 1024;
    private readonly ContainerEngine engine;
    private readonly StateDocument<Manifest> document;
    private readonly string state;
    private readonly CancellationToken closing;
    private readonly Dictionary<string, long> observations = new(StringComparer.Ordinal);

    /// <summary>What this process recorded of each AppHost (by its hash), until its run's take-down.</summary>
    private readonly Dictionary<string, List<Made>> made = new(StringComparer.Ordinal);

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
        if (made.TryGetValue(appHost, out List<Made>? recorded) && recorded.RemoveAll(container => withdrawn.Contains(container.Id)) > 0)
        {
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
        // An engine that does not answer says nothing of where they are: they are recorded as its, their network unknown.
        IReadOnlyList<(string Id, string? Network)> there = asked.Length == 0 ? []
            : await BackgroundWork.Run(() => PlaceAsync(engine, asked)) ?? [.. asked.Select(id => (id, (string?)null))];
        if (there.Count < asked.Length)
        {
            string[] away = [.. asked.Where(id => !there.Any(container => container.Id == id))];
            elsewhere.UnionWith(away);
            Elsewhere?.Invoke(this, (ownedAppHostPath, away.Length));
        }
        observed.RemoveAll(item => !there.Any(container => container.Id == item.Id));
        entries = [.. (await ReadAsync()).Entries];
        if (closing.IsCancellationRequested || observations[appHost] != observation || observed.Count == 0)
        {
            return;
        }
        List<Made> added = [];
        foreach ((string id, string name, string uid) in observed)
        {
            string? network = there.First(container => container.Id == id).Network;
            Entry entry = new(id, name, uid, appHost, identity.Fingerprint, state, ContainerLabels.MachineValue, ContainerLabels.ProcessValue, network);
            if (!entries.Contains(entry))
            {
                entries.RemoveAll(known => IsCurrent(known, appHost) && known.Id == id);
                if (entries.Count == MaximumRecords)
                {
                    IdeErrors.Write("Aspire containers: ownership manifest is full; additional containers are left untracked");
                    break;
                }
                entries.Add(entry);
                added.Add(new Made(id, network));
            }
        }
        await SaveAsync(entries);
        recordedAs[appHost] = signature;
        if (added.Count > 0)
        {
            if (!made.TryGetValue(appHost, out recorded))
            {
                made[appHost] = recorded = [];
            }
            recorded.AddRange(added);
            ContainerLabels.MadeUnlabelled(engine, added.Select(container => container.Id),
                added.Select(container => container.Network).OfType<string>().Distinct(StringComparer.Ordinal));
        }
    }

    /// <summary>Those of the containers asked for the engine holds, each with the session network it is on; null when the
    /// engine does not answer (nothing is then said of them). Off the UI thread.</summary>
    private static async Task<IReadOnlyList<(string Id, string? Network)>?> PlaceAsync(ContainerEngine engine, string[] asked) =>
        await ThereAsync(engine, asked, "as the AppHost made them") is { } here ? await SessionNetworksAsync(engine, here) : null;

    /// <summary>Removes the session containers this process recorded of an AppHost's run once <paramref name="after"/> (its
    /// AppHost's end) has come (the run's Stop, the folder's end), as a container run's Stop takes its own down, then the
    /// session network they were on: done once the engine has answered, or given up after its limit, what fails the IDE's
    /// error said with <paramref name="when"/>. Begun on the UI thread; the rest needs none, so the folder's end can wait
    /// for it after the window's loop has stopped.</summary>
    public Task TakeDownAsync(string appHostPath, string when, Task? after = null)
    {
        UiThread.Check();
        string appHost = Hash(Path.GetFullPath(appHostPath));
        // A snapshot still on its way to the UI thread records nothing of a run taken down.
        observations[appHost] = observations.GetValueOrDefault(appHost) + 1;
        recordedAs.Remove(appHost);
        if (!made.Remove(appHost, out List<Made>? recorded) || recorded.Count == 0)
        {
            return Task.CompletedTask;
        }
        string[] ids = [.. recorded.Select(container => container.Id)];
        string[] networks = [.. recorded.Select(container => container.Network).OfType<string>().Distinct(StringComparer.Ordinal)];
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
            if (await ThereAsync(engine, ids, when) is { } there && await FolderContainers.RemoveAsync(engine, [.. there.Select(container => container.Id)], when)
                && await RemoveNetworksAsync(engine, networks, when))
            {
                Forget(kept, ids);
                ContainerLabels.EndedUnlabelled(ids, networks);
            }
        });
    }

    /// <summary>The containers of those given the engine still holds, by their whole ids, with the networks each is on; null
    /// when it does not answer.</summary>
    private static async Task<(string Id, string[] Networks)[]?> ThereAsync(ContainerEngine engine, IReadOnlyList<string> ids, string when)
    {
        try
        {
            // Several filters of one key are any of them to the engine.
            IReadOnlyList<JsonElement> listed = await engine.JsonLinesAsync(["ps", "-a", "--no-trunc", .. ids.SelectMany(id => (string[])["--filter", $"id={id}"]),
                "--format", "json"], limit: ContainerEngine.AskLimit);
            return [.. listed.Select(row => (Id: ContainerInfo.IdOf(row) ?? string.Empty, Networks: NetworksOf(row))).Where(container => ids.Contains(container.Id))];
        }
        catch (Exception failed) when (failed is ContainerCommandException or JsonException or TimeoutException or FileNotFoundException
            or System.ComponentModel.Win32Exception)
        {
            IdeErrors.Write($"Aspire containers: the engine did not list the run's containers {when}: {failed.Message}");
        }
        return null;
    }

    /// <summary>The networks a row of <c>ps --format json</c> names: Docker's CLI joins them with commas, Podman's own lists them.</summary>
    private static string[] NetworksOf(JsonElement row) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty("Networks", out JsonElement networks) && networks.ValueKind == JsonValueKind.Array
            ? [.. networks.EnumerateArray().Where(network => network.ValueKind == JsonValueKind.String).Select(network => network.GetString()!)]
            : (ContainerEngine.Text(row, "Networks") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Each container with the session network the orchestrator put it on: one of its networks the engine lists as
    /// the orchestrator's and not Persistent (DCP's labels), else none; an AppHost with a Persistent container keeps its
    /// network, which is the person's then. Asked only of a container on a network.</summary>
    private static async Task<IReadOnlyList<(string Id, string? Network)>> SessionNetworksAsync(ContainerEngine engine, IReadOnlyList<(string Id, string[] Networks)> containers)
    {
        string[] named = [.. containers.SelectMany(container => container.Networks).Where(NetworkName).Distinct(StringComparer.Ordinal)];
        HashSet<string> session = new(StringComparer.Ordinal);
        if (named.Length > 0)
        {
            try
            {
                foreach (JsonElement network in await engine.JsonLinesAsync(["network", "ls", "--filter", $"label={PersistentLabel}=false", "--format", "json"],
                    limit: ContainerEngine.AskLimit))
                {
                    if ((ContainerEngine.Text(network, "Name") ?? ContainerEngine.Text(network, "name")) is { } name && named.Contains(name)
                        && ContainerInfo.ReadLabels(network) is var labels && labels.GetValueOrDefault(PersistentLabel) == "false" && labels.ContainsKey(CreatorLabel))
                    {
                        session.Add(name);
                    }
                }
            }
            catch (Exception failed) when (failed is ContainerCommandException or JsonException or TimeoutException or FileNotFoundException
                or System.ComponentModel.Win32Exception)
            {
                IdeErrors.Write($"Aspire containers: the engine did not list the run's networks: {failed.Message}");
            }
        }
        return [.. containers.Select(container => (container.Id, container.Networks.FirstOrDefault(session.Contains)))];
    }

    /// <summary>Removes the session networks given that the engine still lists as the orchestrator's and not Persistent, once
    /// their containers have gone (one another process put a container on stays, its failure said); true once none is left.</summary>
    private static async Task<bool> RemoveNetworksAsync(ContainerEngine engine, IReadOnlyList<string> names, string when, IReadOnlyList<string>? arguments = null)
    {
        if (names.Count == 0)
        {
            return true;
        }
        try
        {
            string[] there = [.. (await engine.JsonLinesAsync([.. arguments ?? [], "network", "ls", "--filter", $"label={PersistentLabel}=false", "--format", "json"],
                    limit: ContainerEngine.AskLimit))
                .Select(network => ContainerEngine.Text(network, "Name") ?? ContainerEngine.Text(network, "name")).OfType<string>().Where(names.Contains)];
            if (there.Length == 0)
            {
                return true;
            }
            ChildResult removed = await engine.RunAsync([.. arguments ?? [], "network", "rm", .. there], ContainerEngine.AskLimit);
            if (!removed.Succeeded)
            {
                IdeErrors.Write($"Aspire containers: the run's network was not removed {when}: {ContainerEngine.Said(removed)}");
            }
            return removed.Succeeded;
        }
        catch (Exception failed) when (failed is ContainerCommandException or JsonException or TimeoutException or FileNotFoundException
            or System.ComponentModel.Win32Exception)
        {
            IdeErrors.Write($"Aspire containers: the run's network was not removed {when}: {failed.Message}");
            return false;
        }
    }

    /// <summary>Removes the containers the state's record says a process of this state and machine that is gone had an
    /// orchestrator make (<see cref="ContainerLabels.IsLeftover"/>), on the engine it recorded them on, each only while it is
    /// still the container recorded (its id and name) and not Persistent, then the session networks they were on; a
    /// recorded container the engine no longer holds is forgotten. No engine command runs when no record is a leftover.
    /// Off the UI thread; the ids removed.</summary>
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
        Entry[] ended = [.. removing.Concat(gone)];
        if (ended.Length > 0)
        {
            await RemoveNetworksAsync(engine, [.. ended.Select(entry => entry.Network).OfType<string>().Distinct(StringComparer.Ordinal)],
                "as an earlier Passenger's AppHost left it", identity.Arguments).ConfigureAwait(false);
            Forget(record, [.. ended.Select(entry => entry.Id)]);
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
    private static bool NetworkName(string? value) => ContainerName(value);
    private static bool OwnerValue(string? value) => value?.Split('-') is [var pid, var start]
        && int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0
        && long.TryParse(start, NumberStyles.None, CultureInfo.InvariantCulture, out long began) && began > 0;

    private sealed record Identity(string Fingerprint, string[] Arguments);

    /// <summary>A container this process recorded, and the session network it is on.</summary>
    private sealed record Made(string Id, string? Network);

    /// <summary>A recorded container: its id, name, resource and AppHost, the engine's fingerprint, the state, machine and
    /// process it was made for, and the session network it was on, written only when it has one, so a record an earlier
    /// Passenger wrote is the same set member as it reads back.</summary>
    public sealed record Entry(string Id, string Name, string Uid, string AppHost, string Engine, string State, string Machine, string Owner,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Network = null);

    public sealed class Manifest : IStateDocument<Manifest>
    {
        public static IReadOnlyList<Action<JsonObject>> Upgrades => [];
        public static IReadOnlyList<string> Sets => [nameof(Entries)];
        public List<Entry> Entries { get; init; } = [];
        public Manifest Clamped() => new()
        {
            Entries = [.. (Entries ?? []).Take(MaximumRecords).Where(entry => entry is not null && FullId(entry.Id) && ContainerName(entry.Name)
                && SafeText(entry.Uid, 256) && FullId(entry.AppHost) && FullId(entry.Engine)
                && entry.State is { Length: 16 } && entry.Machine is { Length: 16 } && SafeText(entry.Owner, 64) && OwnerValue(entry.Owner)
                && (entry.Network is null || NetworkName(entry.Network))).Distinct()],
        };
    }
}
