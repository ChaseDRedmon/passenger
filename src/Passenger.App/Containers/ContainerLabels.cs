using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Passenger.Core;

namespace Passenger.App.Containers;

/// <summary>The labels on everything Passenger has an engine make (containers, networks): the state folder it keeps state
/// in, by a hash of it, and the process that made it, by its id and start time. No child holds a container, so
/// <see cref="ChildProcesses"/>' lifeline cannot end one: Passenger ends them by these labels instead, with the folder that
/// started them (<see cref="FolderContainers"/>), on macOS and Linux as the process ends however it ends (the watchdog's
/// last command, <see cref="ChildProcesses.AtIdeEnd"/>), and at the next start, which removes those of this state whose
/// process is gone (<see cref="RemoveLeftoversAsync"/>).</summary>
public static class ContainerLabels
{
    public const string State = "net.passenger-ide.state";
    public const string Process = "net.passenger-ide.process";
    public const string Folder = "net.passenger-ide.folder";

    private static int toldWatchdog;

    /// <summary>The state folder's label value: the first 16 hex digits of a SHA-256 of its path.</summary>
    public static string StateValue => Hash(StateStoreRoot());

    /// <summary>This process's label value: its id and start time, so a process given the same id later is another.</summary>
    public static string ProcessValue { get; } = Of(Environment.ProcessId, StartOf(System.Diagnostics.Process.GetCurrentProcess()));

    /// <summary>A process's label value, for a process by its id and start time (Unix seconds).</summary>
    public static string Of(int processId, long started) => string.Create(CultureInfo.InvariantCulture, $"{processId}-{started}");

    /// <summary>A folder's label value: the first 16 hex digits of a SHA-256 of its canonical path.</summary>
    public static string FolderValue(string folder) => Hash(folder);

    /// <summary>The <c>--label</c> words for something made for a folder (or none), and, the first time, the watchdog told
    /// to remove what this process made once it has ended.</summary>
    public static IReadOnlyList<string> Arguments(ContainerEngine engine, string? folder)
    {
        if (Interlocked.Exchange(ref toldWatchdog, 1) == 0 && engine.Cli is { } cli)
        {
            ChildProcesses.AtIdeEnd(EndCommand(cli));
        }
        List<string> words = ["--label", $"{State}={StateValue}", "--label", $"{Process}={ProcessValue}"];
        if (folder is not null)
        {
            words.AddRange(["--label", $"{Folder}={FolderValue(folder)}"]);
        }
        return words;
    }

    /// <summary>The shell command that removes this process's containers and networks, for the watchdog.</summary>
    private static string EndCommand(string cli)
    {
        string docker = $"'{cli.Replace("'", "'\\''", StringComparison.Ordinal)}'";
        string label = $"'label={Process}={ProcessValue}'";
        return $"{docker} rm -f $({docker} ps -aq --filter {label}); {docker} network rm $({docker} network ls -q --filter {label})";
    }

    private static string StateStoreRoot()
    {
        string root = global::Passenger.App.State.StateStore.DefaultRoot;
        try
        {
            return PathNames.Canonical(root);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return root;
        }
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    private static long StartOf(System.Diagnostics.Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();
        }
        catch (Exception unknown) when (unknown is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return 0;
        }
    }

    /// <summary>Whether the process a label names is gone: none has its id, or the one that has started at another time.</summary>
    public static bool IsGone(string label)
    {
        string[] parts = label.Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int id)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long started))
        {
            return false;
        }
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(id);
            long now = StartOf(process);
            return now != 0 && started != 0 && Math.Abs(now - started) > 2;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    /// <summary>Removes the containers and networks labelled with this state whose process is gone: what a crash left, or
    /// a process the watchdog could not clean up after (Windows). Off the UI thread; quiet when there is no engine.</summary>
    public static async Task<IReadOnlyList<string>> RemoveLeftoversAsync(ContainerEngine engine, CancellationToken cancel = default)
    {
        List<string> removed = [];
        if (await Task.Run(() => engine.Cli, cancel) is null)
        {
            return removed;
        }
        string filter = $"label={State}={StateValue}";
        try
        {
            IReadOnlyList<JsonElement> containers = await engine.JsonLinesAsync(["ps", "-a", "--filter", filter, "--format", "json"], cancel);
            string[] gone = [.. containers.Select(ContainerInfo.Read).Where(c => c.Labels.GetValueOrDefault(Process) is { } made && IsGone(made)).Select(c => c.Id)];
            if (gone.Length > 0)
            {
                await engine.RunAsync(["rm", "-f", .. gone], ContainerEngine.AskLimit * 3, cancel);
                removed.AddRange(gone);
            }
            IReadOnlyList<JsonElement> networks = await engine.JsonLinesAsync(["network", "ls", "--filter", filter, "--format", "json"], cancel);
            string[] left = [.. networks.Where(n => ContainerInfo.ReadLabels(ContainerEngine.Text(n, "Labels")).GetValueOrDefault(Process) is { } made && IsGone(made))
                .Select(n => ContainerEngine.Text(n, "ID")).OfType<string>()];
            if (left.Length > 0)
            {
                await engine.RunAsync(["network", "rm", .. left], ContainerEngine.AskLimit, cancel);
                removed.AddRange(left);
            }
            if (removed.Count > 0)
            {
                IdeErrors.Write($"Containers: removed {removed.Count} container{(removed.Count == 1 ? string.Empty : "s")} and networks an earlier Passenger left");
            }
        }
        catch (Exception failed) when (failed is ContainerCommandException or TimeoutException or JsonException or System.ComponentModel.Win32Exception)
        {
            // No engine running: what is left is removed at a start that finds one.
        }
        return removed;
    }
}
