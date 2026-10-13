using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Passenger.App.Debugging;
using Passenger.App.Docking;
using Passenger.App.Navigation;
using Passenger.App.Settings;
using Passenger.App.Welcome;
using Passenger.App.Workspace;

namespace Passenger.App.State;

/// <summary>The IDE's own state, in one folder: the app's documents at its top (the settings, the layout a folder
/// without its own opens on, the recent solutions), and each folder's under <c>workspaces/&lt;name&gt;-&lt;hash&gt;/</c>,
/// as Rider keeps a project's workspace.xml. Only the window a person uses keeps one; a headless IDE starts on the
/// defaults.</summary>
public sealed class StateStore
{
    private readonly Dictionary<string, ProjectState> projects = new(StringComparer.Ordinal);

    public StateStore(string root)
    {
        Root = root;
        Settings = new(Path.Combine(root, "settings.json"));
        Layout = new(Path.Combine(root, "layout.json"));
        Recent = new(Path.Combine(root, "recentSolutions.json"));
    }

    /// <summary>The folder: PASSENGER_SETTINGS names it, else the user's local application data.</summary>
    public static string DefaultRoot => Environment.GetEnvironmentVariable("PASSENGER_SETTINGS") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Passenger");

    public string Root { get; }

    public StateDocument<AppSettings> Settings { get; }

    /// <summary>The tool windows' layout as it was changed last, which a folder with none of its own opens on.</summary>
    public StateDocument<DockLayout> Layout { get; }

    /// <summary>The folders and solutions opened lately, which the Welcome screen lists and a start reopens.</summary>
    public StateDocument<RecentSolutionsState> Recent { get; }

    /// <summary>A folder's own documents.</summary>
    public ProjectState Project(string folder)
    {
        if (!projects.TryGetValue(folder, out ProjectState? project))
        {
            project = new ProjectState(Root, folder);
            projects[folder] = project;
        }
        return project;
    }

    /// <summary>Writes every change still waiting, now.</summary>
    public void Flush()
    {
        Settings.Flush();
        Layout.Flush();
        Recent.Flush();
        foreach (ProjectState project in projects.Values)
        {
            project.Flush();
        }
    }

    /// <summary>The step from a file written before documents had versions to the first version, which has its shape.</summary>
    public static void Unversioned(JsonObject document)
    {
    }
}

/// <summary>A folder's documents: what its window had open, its layout, breakpoints and bookmarks.</summary>
public sealed class ProjectState
{
    internal ProjectState(string root, string folder)
    {
        Folder = FolderOf(root, folder);
        // Before the store, the workspace was one file named as this folder is.
        Workspace = new(Path.Combine(Folder, "workspace.json"), formerly: Folder + ".json");
        Layout = new(Path.Combine(Folder, "layout.json"));
        Breakpoints = new(Path.Combine(Folder, "breakpoints.json"));
        Bookmarks = new(Path.Combine(Folder, "bookmarks.json"));
    }

    /// <summary>The folder the documents are in.</summary>
    public string Folder { get; }

    /// <summary>Where a store keeps an open folder's own things, <c>workspaces/&lt;name&gt;-&lt;hash&gt;</c>: its
    /// documents, and the profiler's snapshots of its programs, which a window keeps whether or not it keeps state.</summary>
    public static string FolderOf(string root, string folder)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(folder)))[..12];
        return Path.Combine(root, "workspaces", $"{Path.GetFileName(folder)}-{hash}");
    }

    /// <summary>The editors' splits, tabs and carets, the recent files and the projects unloaded.</summary>
    public StateDocument<WorkspaceState> Workspace { get; }

    public StateDocument<DockLayout> Layout { get; }

    public StateDocument<BreakpointsState> Breakpoints { get; }

    public StateDocument<BookmarksState> Bookmarks { get; }

    public void Flush()
    {
        Workspace.Flush();
        Layout.Flush();
        Breakpoints.Flush();
        Bookmarks.Flush();
    }
}
