using System.Diagnostics;
using System.Formats.Tar;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.App.Containers;

/// <summary>A file or folder inside a container, at its depth in the Files tab.</summary>
public sealed record ContainerFile(string Path, string Name, bool IsFolder, int Depth);

/// <summary>A container's files, browsed lazily: a folder's entries listed as it is opened (<c>docker exec … ls</c>), and a
/// file opened read-only from a copy (<c>docker cp …:path -</c>) under the state's <c>cache/containers</c>, which every
/// editor takes as read-only (<see cref="IsCopy"/>).</summary>
public sealed class ContainerFilesPane : DockPanel
{
    /// <summary>The largest file a copy is made of.</summary>
    private const long Largest = 16 * 1024 * 1024;

    private readonly Func<ContainerEngine> engine;
    private readonly Func<string> stateFolder;
    private readonly ListBox list;
    private readonly TextBlock said = new() { Classes = { "emptyText" } };
    private readonly Dictionary<string, IReadOnlyList<ContainerFile>> listed = new(StringComparer.Ordinal);
    private readonly HashSet<string> open = new(StringComparer.Ordinal) { "/" };
    private ContainerInfo? container;

    public ContainerFilesPane(Func<ContainerEngine> engine, Func<string> stateFolder)
    {
        this.engine = engine;
        this.stateFolder = stateFolder;
        list = new ListBox
        {
            Classes = { "tree" },
            ItemTemplate = new FuncDataTemplate<ContainerFile>((file, _) => file is null ? new Panel() : Row(file), supportsRecycling: false),
        };
        list.DoubleTapped += (_, _) => _ = ActivateAsync(list.SelectedItem as ContainerFile);
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _ = ActivateAsync(list.SelectedItem as ContainerFile);
                e.Handled = true;
            }
        };
        Children.Add(new Panel { Children = { list, said } });
    }

    /// <summary>A file copied out of a container, to open read-only.</summary>
    public event EventHandler<string>? Opened;

    /// <summary>Something to say of a file that could not be opened.</summary>
    public event EventHandler<string>? Problem;

    /// <summary>The entries listed, folders opened among them.</summary>
    public IReadOnlyList<ContainerFile> Entries => list.ItemsSource as IReadOnlyList<ContainerFile> ?? [];

    /// <summary>Whether a path is a container file's copy, which the editor shows read-only.</summary>
    public static bool IsCopy(string path) =>
        path.Contains($"{System.IO.Path.DirectorySeparatorChar}cache{System.IO.Path.DirectorySeparatorChar}containers{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>Lists a container's root, or nothing; the same container again keeps what was opened.</summary>
    public void Show(ContainerRow? row)
    {
        UiThread.Check();
        ContainerInfo? next = row?.Container;
        if (next?.Id == container?.Id)
        {
            container = next;
            return;
        }
        container = next;
        listed.Clear();
        open.Clear();
        open.Add("/");
        list.ItemsSource = null;
        said.IsVisible = next is not null;
        said.Text = "Listing /…";
        if (next is not null)
        {
            _ = ListAsync("/");
        }
    }

    /// <summary>Opens a folder (listing it the first time) or closes it, or opens a file read-only.</summary>
    public async Task ActivateAsync(ContainerFile? file)
    {
        if (file is null)
        {
            return;
        }
        if (!file.IsFolder)
        {
            await OpenAsync(file.Path);
            return;
        }
        if (!open.Remove(file.Path))
        {
            open.Add(file.Path);
            if (!listed.ContainsKey(file.Path))
            {
                await ListAsync(file.Path);
                return;
            }
        }
        Fill();
    }

    private async Task ListAsync(string folder)
    {
        if (container is not { } listing)
        {
            return;
        }
        ContainerEngine docker = engine();
        (IReadOnlyList<ContainerFile>? found, string? problem) = await BackgroundWork.Run(async () =>
        {
            try
            {
                ChildResult result = await docker.RunAsync(["exec", listing.Id, "ls", "-1ApL", folder], ContainerEngine.AskLimit);
                if (!result.Succeeded)
                {
                    string error = ContainerEngine.Said(result);
                    return (null, error.Contains("is not running", StringComparison.Ordinal) ? $"Start {listing.Name} to browse its files"
                        : error.Contains("executable file not found", StringComparison.Ordinal) ? $"{listing.Image} has no ls to list its files" : error);
                }
                int depth = folder == "/" ? 0 : folder.Count(c => c == '/');
                return ((IReadOnlyList<ContainerFile>?)[.. result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(name => name is not ("./" or "../"))
                    .Select(name => new ContainerFile($"{folder.TrimEnd('/')}/{name.TrimEnd('/')}", name.TrimEnd('/'), name.EndsWith('/'), depth))
                    .OrderBy(f => !f.IsFolder).ThenBy(f => f.Name, StringComparer.Ordinal)], (string?)null);
            }
            catch (Exception failed) when (failed is TimeoutException or FileNotFoundException or System.ComponentModel.Win32Exception)
            {
                return (null, failed.Message);
            }
        });
        if (container?.Id != listing.Id)
        {
            return;
        }
        if (found is null)
        {
            open.Remove(folder);
            if (folder == "/")
            {
                said.Text = problem;
                said.IsVisible = true;
            }
            else
            {
                Problem?.Invoke(this, problem!);
            }
            return;
        }
        listed[folder] = found;
        Fill();
    }

    private void Fill()
    {
        object? chosen = list.SelectedItem;
        List<ContainerFile> rows = [];
        Add("/");
        list.ItemsSource = rows;
        list.SelectedItem = chosen is ContainerFile was ? rows.FirstOrDefault(r => r.Path == was.Path) : null;
        said.IsVisible = rows.Count == 0;
        said.Text = "The folder is empty";

        void Add(string folder)
        {
            foreach (ContainerFile file in listed.GetValueOrDefault(folder) ?? [])
            {
                rows.Add(file);
                if (file.IsFolder && open.Contains(file.Path))
                {
                    Add(file.Path);
                }
            }
        }
    }

    /// <summary>Copies a file out of the container under the state's cache and opens it read-only: what the Files tab and a
    /// test open. Null once opened, else why not.</summary>
    public async Task<string?> OpenAsync(string path)
    {
        if (container is not { } from)
        {
            return "No container is chosen";
        }
        ContainerEngine docker = engine();
        string copy = System.IO.Path.Combine([stateFolder(), "cache", "containers", Safe(from.Name), .. path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Safe)]);
        string? problem = await BackgroundWork.Run(() => CopyAsync(docker, from.Id, path, copy));
        if (problem is not null)
        {
            Problem?.Invoke(this, problem);
            return problem;
        }
        Opened?.Invoke(this, copy);
        return null;
    }

    private static async Task<string?> CopyAsync(ContainerEngine docker, string id, string path, string copy)
    {
        ProcessStartInfo start;
        try
        {
            start = docker.Command(["cp", $"{id}:{path}", "-"]);
        }
        catch (FileNotFoundException missing)
        {
            return missing.Message;
        }
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using Process process = ChildProcesses.Start(start);
        Task<string> errors = process.StandardError.ReadToEndAsync();
        using MemoryStream archive = new();
        using CancellationTokenSource limit = new(ContainerEngine.AskLimit);
        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(archive, limit.Token);
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            ChildProcesses.Kill(process);
            return $"Copying {path} out of the container took too long";
        }
        if (process.ExitCode != 0)
        {
            return (await errors).Trim();
        }
        archive.Position = 0;
        using TarReader reader = new(archive);
        while (await reader.GetNextEntryAsync() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            {
                continue;
            }
            if (entry.Length > Largest)
            {
                return $"{path} is larger than 16 MB";
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(copy)!);
            // The state's own copy, never a workspace file: FileText's writes are for what a person edits.
            await using (FileStream written = File.Create(copy))
            {
                if (entry.DataStream is { } data)
                {
                    await data.CopyToAsync(written);
                }
            }
            return null;
        }
        return $"{path} is not a file";
    }

    private static string Safe(string name) => string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) || c == ':' ? '_' : c));

    private Control Row(ContainerFile file)
    {
        PathIcon icon = new() { Width = 14, Height = 14, Margin = new Thickness(0, 0, 6, 0) };
        icon.Bind(PathIcon.DataProperty, icon.GetResourceObservable(file.IsFolder ? open.Contains(file.Path) ? "Icon.FolderOpen" : "Icon.Explorer" : "Icon.DocumentText"));
        icon.Bind(PathIcon.ForegroundProperty, icon.GetResourceObservable("Passenger.TextMuted"));
        TextBlock name = new() { Text = file.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(name, file.Path);
        return new StackPanel { Orientation = Orientation.Horizontal, Height = ListRow.Height, Margin = new Thickness(8 + (file.Depth * 16), 0, 8, 0), Children = { icon, name } };
    }
}
