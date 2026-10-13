using Avalonia.Input;
using Passenger.Editor;

namespace Passenger.App;

/// <summary>A window's actions, each added once by the feature it belongs to, and the keymap that binds them: what every
/// list of actions reads (Find Action, the menus, Settings' keymap, an agent's ide_actions) and what every way of running
/// one goes through (its keys, a menu item, Find Action, ide_action).</summary>
public sealed class ActionRegistry(Func<ActionContext> context, Func<Keymap> keymap)
{
    private readonly Dictionary<string, IdeAction> byId = new(StringComparer.Ordinal);
    private IReadOnlyList<IdeAction>? sorted;

    /// <summary>Adds an action; an id added twice is a mistake in the code, never a choice between two.</summary>
    public void Add(IdeAction action)
    {
        if (!byId.TryAdd(action.Id, action))
        {
            throw new InvalidOperationException($"The action {action.Id} is added twice");
        }
        sorted = null;
    }

    public IdeAction? this[string id] => byId.GetValueOrDefault(id);

    /// <summary>Every action, in the order Find Action lists them before anything is typed: by group, then title.</summary>
    public IReadOnlyList<IdeAction> All => sorted ??=
        [.. byId.Values.OrderBy(a => a.Group, StringComparer.Ordinal).ThenBy(a => a.Title, StringComparer.Ordinal)];

    /// <summary>The window's keymap, the person's shortcuts over the platform's.</summary>
    public Keymap Keymap => keymap();

    /// <summary>Where an action asked for now runs: what the window shows and what has its keys.</summary>
    public ActionContext Context() => context();

    /// <summary>An action's first shortcut as Rider shows it ("⌘K, W"), or null with none.</summary>
    public string? Shortcut(string id) => Keymap.ShortcutsFor(id) is { Count: > 0 } keys ? keys[0].Display() : null;

    /// <summary>An action's one-keystroke shortcut as a menu shows it; none for a chord, which a menu cannot show.</summary>
    public KeyGesture? Gesture(string id) =>
        Keymap.ShortcutsFor(id).FirstOrDefault(s => s.Second is null) is { } shortcut ? new KeyGesture(shortcut.First.Key, shortcut.First.Modifiers) : null;

    /// <summary>The actions whose title, group or id has every word typed, the titles that start with the query first.</summary>
    public IReadOnlyList<IdeAction> Find(string query)
    {
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return All;
        }
        return [.. All
            .Where(a => words.All(w => a.Title.Contains(w, StringComparison.OrdinalIgnoreCase) || a.Group.Contains(w, StringComparison.OrdinalIgnoreCase) || a.Id.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(a => a.Title.StartsWith(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 0 : words.All(w => a.Title.Contains(w, StringComparison.OrdinalIgnoreCase)) ? 1 : 2)
            .ThenBy(a => a.Title.Length)];
    }

    /// <summary>Null when the action can run in this context, else why not: no action has the id, or what it needs is
    /// not there.</summary>
    public string? WhyNot(string id, ActionContext context) =>
        byId.TryGetValue(id, out IdeAction? action) ? action.WhyNot(context) : $"No action is called {id}";

    /// <summary>Runs an action where it is asked for now; null once it has run, else why it could not.</summary>
    public string? Run(string id) => Run(id, Context());

    /// <summary>Runs an action in a context made before something else took the keys (a menu, a popup); null once it has
    /// run, else why it could not.</summary>
    public string? Run(string id, ActionContext context)
    {
        if (WhyNot(id, context) is { } why)
        {
            return why;
        }
        byId[id].Run(context);
        return null;
    }
}
