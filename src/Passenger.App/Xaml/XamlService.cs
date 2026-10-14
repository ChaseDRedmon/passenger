using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;
using Passenger.Xaml;
using Passenger.Xaml.Projects;

namespace Passenger.App.Xaml;

/// <summary>XAML for a folder's session: each project with XAML Passenger reads (an Avalonia or WPF project) loaded the first time one of
/// its files opens, wholly off the UI thread (its project file and restore read, its assemblies and C#), and kept current
/// as its C# and XAML change (typed in an open file, or written on disk); every open XAML file given a
/// <see cref="XamlDocument"/>. A UI-thread object: the loads and reads run on the thread pool, each project's builder
/// taking its own lock.</summary>
public sealed class XamlService : IDisposable
{
    /// <summary>How long typing in a C# file pauses before the XAML that names its types reads it again.</summary>
    private static readonly TimeSpan SourceSettle = TimeSpan.FromMilliseconds(500);

    /// <summary>The folder of the <c>dotnet</c> that builds, whose packs hold the framework's reference assemblies; found
    /// once, off the UI thread, as finding it may wait for the login shell's environment.</summary>
    private static readonly Lazy<string?> DotnetRoot = new(() =>
    {
        try
        {
            return Path.GetDirectoryName(Dotnet.Host());
        }
        catch (FileNotFoundException)
        {
            // No SDK: the framework's reference assemblies are not read, the packages' still are.
            return null;
        }
    });

    private readonly Dictionary<string, XamlLoading> projects = new(StringComparer.Ordinal);
    private readonly Dictionary<OpenDocument, XamlDocument> documents = [];
    private readonly Dictionary<OpenDocument, (EventHandler<DocumentChangedEventArgs> Handler, Debounce Wait)> sources = [];
    private bool disposed;

    /// <summary>A project's types or XAML files were read again; raised on the UI thread.</summary>
    public event EventHandler<XamlProject>? ProjectChanged;

    /// <summary>Something to say in the status bar (a project's XAML still loading).</summary>
    public event EventHandler<string>? Message;

    /// <summary>Where a library's own files come from (a theme's XAML for go to declaration): its Source Link, or a
    /// stand-in of a test's.</summary>
    public ILibraryFiles LibraryFiles { get; set; } = new SourceLink();

    /// <summary>Whether a path is a XAML file Passenger reads, by its extension.</summary>
    public static bool IsXaml(string path) => Path.GetExtension(path).ToLowerInvariant() is ".axaml" or ".paml" or ".xaml";

    /// <summary>An open file's XAML, null for a file that is not XAML of a C# project.</summary>
    public XamlDocument? For(OpenDocument document) => documents.GetValueOrDefault(document);

    public IReadOnlyCollection<XamlDocument> Documents => documents.Values;

    /// <summary>Gives an open XAML file of a C# project its share of the project's XAML, and follows an open C# file as it
    /// is typed, so the XAML naming its types sees them.</summary>
    public void Attach(OpenDocument document)
    {
        UiThread.Check();
        if (disposed || documents.ContainsKey(document) || sources.ContainsKey(document))
        {
            return;
        }
        if (IsXaml(document.Path) && !Paths.IsUnder(document.Path, LibrarySources.Folder))
        {
            if (ProjectOf(document.Path) is { } loading)
            {
                documents[document] = new XamlDocument(this, document, loading);
            }
            return;
        }
        if (document.Language.Name == "C#")
        {
            // Followed whether or not its project's XAML has loaded yet: the project it is in is found once typing pauses.
            Debounce wait = new(SourceSettle, () => Loaded(document.Path) is { } owner ? SourceTypedAsync(owner, document) : Task.CompletedTask);
            EventHandler<DocumentChangedEventArgs> handler = (_, _) => wait.Restart();
            document.Text.Changed += handler;
            document.Parsed += OnParsed;
            sources[document] = (handler, wait);
        }
    }

    public void Detach(OpenDocument document)
    {
        UiThread.Check();
        if (documents.Remove(document, out XamlDocument? xaml))
        {
            xaml.Dispose();
        }
        if (sources.Remove(document, out (EventHandler<DocumentChangedEventArgs> Handler, Debounce Wait) source))
        {
            document.Text.Changed -= source.Handler;
            document.Parsed -= OnParsed;
            source.Wait.Stop();
        }
    }

    /// <summary>Files changed on disk: a project's C# or XAML read again where it changed, and a project file or restore
    /// (its assets) loading the whole project again.</summary>
    public void FilesChanged(IEnumerable<string> paths)
    {
        UiThread.Check();
        foreach (IGrouping<XamlLoading, string> changed in paths.Select(p => (Path: p, Owner: Loaded(p))).Where(p => p.Owner is not null)
            .GroupBy(p => p.Owner!, p => p.Path))
        {
            XamlLoading loading = changed.Key;
            List<string> files = [.. changed];
            if (files.Any(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f) == "project.assets.json"))
            {
                Reload(loading);
                continue;
            }
            HashSet<string> open = [.. documents.Keys.Select(d => d.Path)];
            _ = UpdateAsync(loading, builder =>
            {
                XamlProject? made = null;
                foreach (string file in files)
                {
                    if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        made = builder.SourceChanged(file, null);
                    }
                    else if (builder.Owns(file) && !open.Contains(file))
                    {
                        made = builder.XamlChanged(file, null);
                    }
                }
                return made;
            });
        }
    }

    /// <summary>An open XAML file's resources, includes or names changed as it was typed: the project's other open files
    /// are checked again against them.</summary>
    internal void Summarised(XamlDocument changed)
    {
        foreach (XamlDocument document in documents.Values.Where(d => d != changed && d.Loading.ProjectPath == changed.Loading.ProjectPath))
        {
            document.ProjectChanged();
        }
    }

    /// <summary>A build ended: each open XAML file takes the XAML compiler's problems in it, in place of the last build's.</summary>
    public void Built(IReadOnlyList<Run.Problem> problems)
    {
        UiThread.Check();
        foreach (XamlDocument document in documents.Values)
        {
            document.Built(problems.Where(p => p.Path is { } path && PathNames.Same(path, document.Document.Path)));
        }
    }

    /// <summary>A C# file parsed: its colours (Color.Parse, FromRgb and the rest) found off the UI thread, and its
    /// swatches shown when the text is still the one parsed.</summary>
    private async void OnParsed(object? sender, Languages.CSharp.ParsedText parsed)
    {
        if (sender is not OpenDocument document)
        {
            return;
        }
        try
        {
            IReadOnlyList<Passenger.Xaml.Features.ColourUse> colours = await BackgroundWork.Run(() => Passenger.Xaml.Features.Colours.InCSharp(parsed.Root));
            if (!disposed && document.Text.Snapshot == parsed.Text && (colours.Count > 0 || document.ColourMarks.Count > 0))
            {
                document.ColourMarks = Marks(parsed.Text, colours);
            }
        }
        catch (Exception error)
        {
            IdeErrors.Report(error);
        }
    }

    /// <summary>The gutter's swatches for colours found in a text: a line's first.</summary>
    internal static IReadOnlyList<ColourMark> Marks(TextSnapshot text, IReadOnlyList<Passenger.Xaml.Features.ColourUse> colours) =>
        [.. colours.Where(c => c.Offset <= text.Length).Select(c => new ColourMark(text.GetLineFromOffset(c.Offset),
            Avalonia.Media.Color.FromArgb(c.Colour.A, c.Colour.R, c.Colour.G, c.Colour.B), c))];

    /// <summary>The load of the project a XAML file is in, begun if it has not been; null for a file in no C# project.
    /// Whether the project's XAML is one Passenger reads is learned off the UI thread, as the load begins.</summary>
    private XamlLoading? ProjectOf(string path)
    {
        string? project = ProjectFile(path);
        if (project is null)
        {
            return null;
        }
        if (projects.TryGetValue(project, out XamlLoading? loading))
        {
            return loading;
        }
        Dictionary<string, TextSnapshot> texts = OpenTexts();
        Task<XamlProjectBuilder?> builder = BackgroundWork.Run(() => XamlProjectBuilder.For(project, DotnetRoot.Value));
        loading = new XamlLoading(project, builder, LoadAsync(builder, texts));
        projects[project] = loading;
        _ = AnnounceAsync(loading);
        return loading;
    }

    private static async Task<XamlProject?> LoadAsync(Task<XamlProjectBuilder?> made, Dictionary<string, TextSnapshot> texts)
    {
        if (await made is not { } builder)
        {
            return null;
        }
        return await BackgroundWork.Run(() => builder.Load(p => texts.TryGetValue(p, out TextSnapshot? text) ? text.ToString() : null));
    }

    /// <summary>The IDE's log learns once a project's XAML has loaded, and of a load that failed.</summary>
    private async Task AnnounceAsync(XamlLoading loading)
    {
        try
        {
            if (await loading.Loaded is { } project && !disposed)
            {
                IdeErrors.Write($"XAML for {Path.GetFileName(loading.ProjectPath)} read: {project.Types.All.Count()} types, {project.Files.Count} XAML files");
                ProjectChanged?.Invoke(this, project);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            IdeErrors.Report(error);
        }
    }

    /// <summary>The project already loading or loaded that a file belongs to.</summary>
    private XamlLoading? Loaded(string path) =>
        projects.Values.Where(l => Paths.IsUnder(path, l.Folder)).OrderByDescending(l => l.Folder.Length).FirstOrDefault();

    private void Reload(XamlLoading loading)
    {
        Dictionary<string, TextSnapshot> texts = OpenTexts();
        XamlLoading again = loading with { Loaded = LoadAsync(loading.Builder, texts) };
        projects[loading.ProjectPath] = again;
        foreach (XamlDocument document in documents.Values.Where(d => d.Loading.ProjectPath == loading.ProjectPath))
        {
            document.Follow(again);
        }
        _ = AnnounceAsync(again);
    }

    private Dictionary<string, TextSnapshot> OpenTexts()
    {
        Dictionary<string, TextSnapshot> texts = new(StringComparer.Ordinal);
        foreach (OpenDocument document in documents.Keys.Concat(sources.Keys))
        {
            texts[document.Path] = document.Text.Snapshot;
        }
        return texts;
    }

    /// <summary>A C# file typed in: its types read again from its text as it stands.</summary>
    private Task SourceTypedAsync(XamlLoading loading, OpenDocument document)
    {
        string path = document.Path;
        TextSnapshot text = document.Text.Snapshot;
        return UpdateAsync(loading, builder => builder.SourceChanged(path, text.ToString()));
    }

    /// <summary>A change made to a loaded project off the UI thread; its open XAML files read against the result.</summary>
    private async Task UpdateAsync(XamlLoading loading, Func<XamlProjectBuilder, XamlProject?> change)
    {
        try
        {
            if (await loading.Loaded is null || await loading.Builder is not { } builder)
            {
                return;
            }
            XamlProject? made = await BackgroundWork.Run(() => change(builder));
            if (made is null || disposed)
            {
                return;
            }
            foreach (XamlDocument document in documents.Values.Where(d => d.Loading.ProjectPath == loading.ProjectPath))
            {
                document.ProjectChanged();
            }
            ProjectChanged?.Invoke(this, made);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            IdeErrors.Report(error);
        }
    }

    /// <summary>The XAML file whose <c>x:Class</c> is a class, in the project of a C# file of it: an open one's text first,
    /// else the files on disk; null for none.</summary>
    public string? ClassFile(string className, string near)
    {
        UiThread.Check();
        foreach (XamlDocument document in documents.Values)
        {
            if (document.Document.Text.Snapshot.ToString().Contains($"Class=\"{className}\"", StringComparison.Ordinal))
            {
                return document.Document.Path;
            }
        }
        if (ProjectFile(near) is not { } project)
        {
            return null;
        }
        Stack<string> waiting = new([Path.GetDirectoryName(project)!]);
        while (waiting.Count > 0)
        {
            string folder = waiting.Pop();
            try
            {
                foreach (string file in Directory.EnumerateFiles(folder).Where(IsXaml))
                {
                    if (File.ReadAllText(file).Contains($"Class=\"{className}\"", StringComparison.Ordinal))
                    {
                        return PathNames.Canonical(file);
                    }
                }
                foreach (string sub in Directory.EnumerateDirectories(folder))
                {
                    if (Path.GetFileName(sub) is not ("bin" or "obj") && !Path.GetFileName(sub).StartsWith('.'))
                    {
                        waiting.Push(sub);
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be read holds no XAML to go to.
            }
        }
        return null;
    }

    /// <summary>A file's project: the nearest folder at or above it with a C# project file.</summary>
    private static string? ProjectFile(string path)
    {
        for (DirectoryInfo? at = new FileInfo(path).Directory; at is not null; at = at.Parent)
        {
            try
            {
                string? found = at.EnumerateFiles("*.csproj").Select(f => f.FullName).Order(StringComparer.Ordinal).FirstOrDefault();
                if (found is not null)
                {
                    return found;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>Says something in the status bar.</summary>
    internal void Say(string message) => Message?.Invoke(this, message);

    /// <summary>Lets go of the open files and every project read, as the folder's session ends.</summary>
    public void Dispose()
    {
        disposed = true;
        foreach (OpenDocument document in documents.Keys.Concat(sources.Keys).ToList())
        {
            Detach(document);
        }
        projects.Clear();
    }
}

/// <summary>One project's XAML being read: its project file and folder at once, its builder and its first read once the
/// thread pool has made them (null for a project whose XAML Passenger does not read).</summary>
public sealed record XamlLoading(string ProjectPath, Task<XamlProjectBuilder?> Builder, Task<XamlProject?> Loaded)
{
    public string Folder => Path.GetDirectoryName(ProjectPath)!;
}
