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

/// <summary>Exact Session containers reported by an owned AppHost's authenticated resource connection. UI-thread owned.
/// Unobserved containers are left untouched: a crash before observation supplies no ownership evidence.</summary>
internal sealed class AspireContainerOwnership
{
    private const int MaximumRecords = 1024;
    private const long MaximumBytes = 1024 * 1024;
    private readonly ContainerEngine engine;
    private readonly StateDocument<Manifest> document;
    private readonly string state;
    private readonly CancellationToken closing;
    private readonly Dictionary<string, long> observations = new(StringComparer.Ordinal);
    private Task<Identity?>? observingIdentity;
    private Task? cleanup;

    public AspireContainerOwnership(string stateFolder, ContainerEngine engine, CancellationToken closing = default)
    {
        this.engine = engine;
        this.closing = closing;
        state = ContainerLabels.StateValueOf(stateFolder);
        document = new(Path.Combine(stateFolder, "aspire-containers", "ownership.json"));
    }

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
        long observation = observations.GetValueOrDefault(appHost) + 1;
        observations[appHost] = observation;
        List<Entry> entries = [.. Read().Entries];
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
            Save(entries);
        }
        observed.RemoveAll(item => withdrawn.Contains(item.Id) || entries.Any(entry => IsCurrent(entry, appHost)
            && entry.Id == item.Id && entry.Name == item.Name && entry.Uid == item.Uid));
        if (observed.Count == 0)
        {
            return;
        }
        using IDisposable held = BackgroundWork.Scope();
        Task<Identity?> readingIdentity = observingIdentity is { IsCompleted: false } pending
            ? pending : observingIdentity = Task.Run(() => ReadIdentityAsync(again: false));
        Identity? identity = await readingIdentity.ConfigureAwait(false);
        if (closing.IsCancellationRequested)
        {
            return;
        }
        try
        {
            await UiThread.InvokeAsync(() =>
            {
                if (closing.IsCancellationRequested || observations[appHost] != observation)
                {
                    return false;
                }
                if (identity is null)
                {
                    IdeErrors.Write("Aspire containers: ownership was not recorded because the engine identity could not be pinned");
                    return false;
                }
                entries = [.. Read().Entries];
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
                    }
                }
                Save(entries);
                return true;
            }).WaitAsync(closing).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (closing.IsCancellationRequested)
        {
        }
    }

    /// <summary>Checks earlier owners once per tracker. No engine command runs when no eligible record exists.</summary>
    public Task CleanupAsync()
    {
        UiThread.Check();
        if (closing.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }
        return cleanup ??= CleanupCoreAsync();
    }

    private async Task CleanupCoreAsync()
    {
        Entry[] candidates = [.. Read().Entries.Where(entry => entry.State == state && entry.Machine == ContainerLabels.MachineValue
            && entry.Owner != ContainerLabels.ProcessValue && ContainerLabels.IsGone(entry.Owner))];
        if (candidates.Length == 0)
        {
            return;
        }
        using IDisposable held = BackgroundWork.Scope();
        List<Entry> removed = await Task.Run(async () =>
        {
            List<Entry> removed = [];
            Identity? identity = await ReadIdentityAsync().ConfigureAwait(false);
            if (identity is null)
            {
                IdeErrors.Write("Aspire containers: leftover cleanup deferred because the engine identity could not be pinned");
                return removed;
            }
            foreach (Entry entry in candidates)
            {
                if (closing.IsCancellationRequested)
                {
                    break;
                }
                if (entry.Engine != identity.Fingerprint)
                {
                    IdeErrors.Write($"Aspire containers: retained {entry.Id}; the current engine identity differs");
                    continue;
                }
                try
                {
                    JsonElement inspected = await engine.JsonAsync([.. identity.Arguments, "container", "inspect", entry.Id]).ConfigureAwait(false);
                    if (inspected.ValueKind != JsonValueKind.Array || inspected.GetArrayLength() != 1
                        || ContainerEngine.Text(inspected[0], "Id") != entry.Id
                        || ContainerEngine.Text(inspected[0], "Name") != "/" + entry.Name)
                    {
                        IdeErrors.Write($"Aspire containers: retained {entry.Id}; its inspected identity or name changed");
                        continue;
                    }
                    if (closing.IsCancellationRequested)
                    {
                        break;
                    }
                    ChildResult result = await engine.RunAsync([.. identity.Arguments, "rm", "-f", entry.Id]).ConfigureAwait(false);
                    if (!result.Succeeded)
                    {
                        throw new ContainerCommandException(ContainerEngine.Said(result));
                    }
                    removed.Add(entry);
                }
                catch (Exception error) when (error is IOException or ContainerCommandException or JsonException or TimeoutException
                    or System.ComponentModel.Win32Exception)
                {
                    IdeErrors.Write($"Aspire containers: cleanup of {entry.Id} deferred: {error.Message}");
                }
            }
            return removed;
        }).ConfigureAwait(false);
        if (removed.Count > 0 && !closing.IsCancellationRequested)
        {
            try
            {
                await UiThread.InvokeAsync(() =>
                {
                    if (!closing.IsCancellationRequested)
                    {
                        Save(Read().Entries.Except(removed).ToList());
                    }
                    return true;
                }).WaitAsync(closing).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (closing.IsCancellationRequested)
            {
            }
        }
    }

    private bool IsCurrent(Entry entry, string appHost) => entry.Owner == ContainerLabels.ProcessValue && entry.State == state
        && entry.Machine == ContainerLabels.MachineValue && entry.AppHost == appHost;

    private async Task<Identity?> ReadIdentityAsync(bool again = true)
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

    private Manifest Read()
    {
        if (File.Exists(document.Path) && new FileInfo(document.Path).Length > MaximumBytes)
        {
            throw new InvalidDataException("The Aspire container ownership manifest exceeds its size limit; cleanup is disabled.");
        }
        document.Reread();
        return document.Value;
    }

    private void Save(List<Entry> entries)
    {
        string folder = Path.GetDirectoryName(document.Path)!;
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
        }
        else
        {
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        document.Set(new Manifest { Entries = entries });
        document.Flush();
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
