namespace Passenger.App;

/// <summary>One entry of a menu: an action (titled as it is registered), a submenu, a separator, or a submenu the window
/// lists as it opens (View's Tool Windows, File's Recent Solutions).</summary>
public sealed record MenuEntry(string? Action, string? Title = null, IReadOnlyList<MenuEntry>? Items = null)
{
    public bool IsSeparator => Action is null && Items is null && Title is null;

    /// <summary>A submenu of what the window has as it opens, named by its title.</summary>
    public bool IsListed => Action is null && Items is null && Title is not null;
}

/// <summary>Rider's main menu: File, Edit, View, Navigate, Code, Refactor, Build, Run, Tests, Tools, Git, Window and Help,
/// each made of the actions that name a place in it (<see cref="IdeAction.Menus"/>), in their order, a separator between
/// each run of them. On macOS it is the menu bar (with Settings and Quit in the application menu, as there); elsewhere a
/// menu behind the toolbar's button.</summary>
public static class MainMenu
{
    public const string ToolWindows = "Tool Windows";
    public const string RecentSolutions = "Recent Solutions";

    /// <summary>The menus, in Rider's order.</summary>
    public static IReadOnlyList<string> Titles { get; } = ["File", "Edit", "View", "Navigate", "Code", "Refactor", "Build", "Run", "Tests", "Tools", "Git", "Window", "Help"];

    /// <summary>The submenus the window lists as each opens.</summary>
    private static readonly string[] Listed = [ToolWindows, RecentSolutions];

    /// <summary>The submenus, each where its parent lists it.</summary>
    private static readonly MenuPlace[] Submenus =
    [
        new("View/" + ToolWindows, 0, 0),
        new("View/Active Editor", 3, 0),
        new("File/" + RecentSolutions, 0, 3),
        new("File/Line Separators", 2, 1),
        new("Edit/Find", 3, 0),
        new("Navigate/Bookmarks", 4, 0),
        new("Code/Folding", 3, 0),
        new("Window/Layout Settings", 0, 0),
        new("Window/Editor Tabs", 1, 0),
    ];

    private static readonly MenuEntry Separator = new(null);

    /// <summary>The menus the actions make.</summary>
    public static IReadOnlyList<MenuEntry> Build(IEnumerable<IdeAction> actions)
    {
        List<(MenuPlace Place, string Id)> placed = [.. actions.SelectMany(a => a.Menus.Select(place => (place, a.Id)))];
        return [.. Titles.Select(title => Menu(title, placed))];
    }

    private static MenuEntry Menu(string path, List<(MenuPlace Place, string Id)> placed)
    {
        IEnumerable<(int Group, int Order, MenuEntry Entry)> actions = placed.Where(p => p.Place.Path == path)
            .Select(p => (p.Place.Group, p.Place.Order, new MenuEntry(p.Id)));
        IEnumerable<(int Group, int Order, MenuEntry Entry)> submenus = Submenus.Where(s => s.Path[..Math.Max(0, s.Path.LastIndexOf('/'))] == path)
            .Select(s => (s.Group, s.Order, Listed.Contains(Name(s.Path)) ? new MenuEntry(null, Name(s.Path)) : Menu(s.Path, placed)));
        List<MenuEntry> items = [];
        int? group = null;
        foreach ((int Group, int Order, MenuEntry Entry) item in actions.Concat(submenus).OrderBy(i => i.Group).ThenBy(i => i.Order))
        {
            if (group is { } before && before != item.Group)
            {
                items.Add(Separator);
            }
            items.Add(item.Entry);
            group = item.Group;
        }
        return new MenuEntry(null, Name(path), items);
    }

    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];
}
