using Passenger.App;
using Passenger.App.CSharp;

namespace Passenger.Tests;

/// <summary>A small project a test class's windows share, restored once, and the warm language server they share on it:
/// each window takes up the server the last one left running (<see cref="WarmServer"/>) rather than start its own. Each
/// test starts on the project as written (<see cref="Reset"/>, from its class's constructor): what a test changed is put
/// back and what it added removed, and a server that saw any change, even one undone since, or that a failed test left
/// (<see cref="OpenWindows"/>), is ended, so the next window starts one that loads the files as written.</summary>
public abstract class SharedProject : IDisposable
{
    /// <summary>The project's own files as written, by path, outside what restores and builds write (bin and obj).</summary>
    private readonly Dictionary<string, byte[]> written = new(StringComparer.Ordinal);
    private readonly HashSet<string> folders = new(StringComparer.Ordinal);

    /// <summary>When each of the project's own files and folders was last written as the shared server knows them: a file
    /// written and put back, or a file added and removed (its folder's time), shows here as no content does.</summary>
    private Dictionary<string, DateTime> stamps = [];

    /// <summary>A test wrote a file of its own to the project since the last reset.</summary>
    private bool changed;

    protected SharedProject(string name) => Folder = TestFolders.Create(name);

    public string Folder { get; }

    /// <summary>The server the class's windows share.</summary>
    public WarmServer Server { get; } = new();

    public string PathOf(string name) => Path.Combine(Folder, name);

    /// <summary>Restores the project, once its files are written, and takes them as the ones every test starts on.</summary>
    protected void Prepare()
    {
        TestFolders.Prepare(() =>
        {
            Restore.Run(Folder);
            folders.UnionWith(Directory.EnumerateDirectories(Folder, "*", SearchOption.AllDirectories).Where(Own));
            foreach (string file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories).Where(Own))
            {
                written[file] = File.ReadAllBytes(file);
            }
            stamps = Stamps();
        }, Folder);
    }

    /// <summary>A window on the project, which never saves what its test typed, with a file open once C# is ready: on the
    /// server the class's last window left, or one of its own when there is none to take up.</summary>
    public MainWindow Open(string file, Action<MainWindow>? before = null)
    {
        MainWindow window = new() { Width = 1200, Height = 800, Autosaves = false, WarmServer = Server };
        window.Show();
        window.OpenFolder(Folder);
        window.OpenFile(file);
        before?.Invoke(window);
        Ui.Settle();
        Assert.True(window.CSharpService?.IsReady, window.CSharpService?.State);
        return window;
    }

    /// <summary>Writes a file a test needs that the project does not have: the next window starts a server of its own, which
    /// loads it as a server started on the folder would, rather than one that would only hear of it through its file
    /// watcher; and before the next test the file goes, with that server.</summary>
    public string Write(string name, string text)
    {
        Drop();
        changed = true;
        string path = PathOf(name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>The project as written for the test about to run: what a test before it changed on disk written back, what
    /// it added removed, and the shared server ended if any of the files changed since it last loaded them.</summary>
    public void Reset()
    {
        bool touched = changed;
        foreach (string file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories).Where(Own).ToList())
        {
            if (!written.ContainsKey(file))
            {
                File.Delete(file);
                touched = true;
            }
        }
        foreach (string folder in Directory.EnumerateDirectories(Folder, "*", SearchOption.AllDirectories).Where(Own).OrderByDescending(f => f.Length).ToList())
        {
            if (!folders.Contains(folder))
            {
                Directory.Delete(folder, recursive: true);
                touched = true;
            }
        }
        foreach ((string file, byte[] content) in written)
        {
            if (!File.Exists(file) || !File.ReadAllBytes(file).AsSpan().SequenceEqual(content))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, content);
                touched = true;
            }
        }
        touched |= !Stamps().OrderBy(s => s.Key, StringComparer.Ordinal).SequenceEqual(stamps.OrderBy(s => s.Key, StringComparer.Ordinal));
        if (touched)
        {
            Drop();
            stamps = Stamps();
            changed = false;
        }
    }

    /// <summary>Ends the shared server and removes the project; a fixture that holds more (a stand-in feed) ends it too.</summary>
    public virtual void Dispose()
    {
        Drop();
        TestFolders.Delete(Folder);
    }

    /// <summary>Ends the shared server, waited for off the UI thread, whose continuations it needs none of.</summary>
    private void Drop() => Task.Run(Server.Drop).GetAwaiter().GetResult();

    private Dictionary<string, DateTime> Stamps()
    {
        Dictionary<string, DateTime> now = new(StringComparer.Ordinal) { [Folder] = Directory.GetLastWriteTimeUtc(Folder) };
        foreach (string folder in folders)
        {
            now[folder] = Directory.GetLastWriteTimeUtc(folder);
        }
        foreach (string file in written.Keys)
        {
            now[file] = File.GetLastWriteTimeUtc(file);
        }
        return now;
    }

    /// <summary>Whether a path is the project's own, not under what a restore or a build writes, nor a repository's .git,
    /// which git writes as a window only reads it (status refreshing the index): a class whose tests change the repository
    /// puts it back itself.</summary>
    private bool Own(string path) =>
        !Path.GetRelativePath(Folder, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or ".git");
}
