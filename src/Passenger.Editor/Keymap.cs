using Avalonia.Input;

namespace Passenger.Editor;

/// <summary>One key with its modifiers, written the way Rider's keymap files write them
/// ("meta shift L", "control alt SLASH"), so a binding reads the same in both places.</summary>
public readonly record struct Keystroke(Key Key, KeyModifiers Modifiers)
{
    public static Keystroke Parse(string text)
    {
        KeyModifiers modifiers = KeyModifiers.None;
        Key? key = null;
        foreach (string part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "meta" or "cmd":
                    modifiers |= KeyModifiers.Meta;
                    break;
                case "control" or "ctrl":
                    modifiers |= KeyModifiers.Control;
                    break;
                case "alt" or "option":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= KeyModifiers.Shift;
                    break;
                default:
                    key = key is null ? ParseKey(part) : throw new FormatException($"'{text}' names two keys");
                    break;
            }
        }
        return new Keystroke(key ?? throw new FormatException($"'{text}' names no key"), modifiers);
    }

    public static Keystroke From(KeyEventArgs e) => new(e.Key, e.KeyModifiers);

    /// <summary>For menus and tooltips: ⌘⇧L on macOS, Ctrl+Shift+L elsewhere.</summary>
    public string Display()
    {
        string key = Key switch
        {
            Key.OemPeriod => ".",
            Key.OemComma => ",",
            Key.OemQuestion or Key.Divide => "/",
            Key.OemSemicolon => ";",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemQuotes => "'",
            Key.OemPlus => "=",
            Key.OemMinus => "-",
            Key.OemTilde => "`",
            Key.Back => OperatingSystem.IsMacOS() ? "⌫" : "Backspace",
            Key.Return => OperatingSystem.IsMacOS() ? "↩" : "Enter",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.Left => "←",
            Key.Right => "→",
            >= Key.D0 and <= Key.D9 => ((char)('0' + (Key - Key.D0))).ToString(),
            _ => Key.ToString(),
        };
        if (OperatingSystem.IsMacOS())
        {
            string mods = (Modifiers.HasFlag(KeyModifiers.Control) ? "⌃" : "") + (Modifiers.HasFlag(KeyModifiers.Alt) ? "⌥" : "")
                + (Modifiers.HasFlag(KeyModifiers.Shift) ? "⇧" : "") + (Modifiers.HasFlag(KeyModifiers.Meta) ? "⌘" : "");
            return mods + key;
        }
        List<string> parts = [];
        if (Modifiers.HasFlag(KeyModifiers.Control)) { parts.Add("Ctrl"); }
        if (Modifiers.HasFlag(KeyModifiers.Alt)) { parts.Add("Alt"); }
        if (Modifiers.HasFlag(KeyModifiers.Shift)) { parts.Add("Shift"); }
        if (Modifiers.HasFlag(KeyModifiers.Meta)) { parts.Add("Win"); }
        parts.Add(key);
        return string.Join('+', parts);
    }

    /// <summary>The keymap notation <see cref="Parse"/> reads ("meta shift K"), as a user's keymap is kept.</summary>
    public string Notation()
    {
        List<string> parts = [];
        if (Modifiers.HasFlag(KeyModifiers.Meta)) { parts.Add("meta"); }
        if (Modifiers.HasFlag(KeyModifiers.Control)) { parts.Add("control"); }
        if (Modifiers.HasFlag(KeyModifiers.Alt)) { parts.Add("alt"); }
        if (Modifiers.HasFlag(KeyModifiers.Shift)) { parts.Add("shift"); }
        parts.Add(Key switch
        {
            Key.OemQuestion => "SLASH",
            Key.OemPeriod => "PERIOD",
            Key.OemComma => "COMMA",
            Key.OemSemicolon => "SEMICOLON",
            Key.OemOpenBrackets => "OPEN_BRACKET",
            Key.OemCloseBrackets => "CLOSE_BRACKET",
            Key.OemPipe => "BACK_SLASH",
            Key.OemQuotes => "QUOTE",
            Key.OemPlus => "EQUALS",
            Key.OemMinus => "MINUS",
            Key.OemTilde => "BACK_QUOTE",
            Key.Back => "BACK_SPACE",
            Key.Return => "ENTER",
            Key.PageUp => "PAGE_UP",
            Key.PageDown => "PAGE_DOWN",
            >= Key.D0 and <= Key.D9 => ((char)('0' + (Key - Key.D0))).ToString(),
            _ => Key.ToString().ToUpperInvariant(),
        });
        return string.Join(' ', parts);
    }

    private static Key ParseKey(string name) => name.ToUpperInvariant() switch
    {
        "SLASH" or "DIVIDE" => Key.OemQuestion,
        "PERIOD" => Key.OemPeriod,
        "COMMA" => Key.OemComma,
        "SEMICOLON" => Key.OemSemicolon,
        "OPEN_BRACKET" => Key.OemOpenBrackets,
        "CLOSE_BRACKET" => Key.OemCloseBrackets,
        "BACK_SLASH" => Key.OemPipe,
        "QUOTE" => Key.OemQuotes,
        "EQUALS" => Key.OemPlus,
        "MINUS" => Key.OemMinus,
        "BACK_QUOTE" => Key.OemTilde,
        "BACK_SPACE" => Key.Back,
        "ENTER" => Key.Return,
        "PAGE_UP" => Key.PageUp,
        "PAGE_DOWN" => Key.PageDown,
        "ESCAPE" => Key.Escape,
        "SPACE" => Key.Space,
        "TAB" => Key.Tab,
        "DELETE" => Key.Delete,
        "INSERT" => Key.Insert,
        "HOME" => Key.Home,
        "END" => Key.End,
        "UP" => Key.Up,
        "DOWN" => Key.Down,
        "LEFT" => Key.Left,
        "RIGHT" => Key.Right,
        { Length: 1 } single when char.IsAsciiLetter(single[0]) => Enum.Parse<Key>(single),
        { Length: 1 } digit when char.IsAsciiDigit(digit[0]) => Key.D0 + (digit[0] - '0'),
        _ when Enum.TryParse(name, true, out Key parsed) => parsed,
        _ => throw new FormatException($"unknown key '{name}'"),
    };
}

/// <summary>A binding: one keystroke, or two pressed in turn (a chord, like Cmd+K then Cmd+C).</summary>
public sealed record Shortcut(Keystroke First, Keystroke? Second = null)
{
    /// <summary>"meta K , meta C" is a chord; anything else is one keystroke.</summary>
    public static Shortcut Parse(string text)
    {
        string[] halves = text.Split(',', StringSplitOptions.TrimEntries);
        return halves.Length switch
        {
            1 => new Shortcut(Keystroke.Parse(halves[0])),
            2 => new Shortcut(Keystroke.Parse(halves[0]), Keystroke.Parse(halves[1])),
            _ => throw new FormatException($"'{text}' has more than two keystrokes"),
        };
    }

    public string Display() => Second is { } second ? $"{First.Display()}, {second.Display()}" : First.Display();

    public string Notation() => Second is { } second ? $"{First.Notation()} , {second.Notation()}" : First.Notation();
}

/// <summary>Which shortcuts run which action. An action is a name from <see cref="EditorActions"/>
/// or the IDE's own list; a shortcut may run only one action.</summary>
public sealed class Keymap
{
    private readonly Dictionary<string, List<Shortcut>> byAction = [];
    private readonly Dictionary<Shortcut, string> byShortcut = [];

    public Keymap(string name) => Name = name;

    public string Name { get; }

    public Keymap Bind(string action, params string[] shortcuts)
    {
        foreach (string text in shortcuts)
        {
            Shortcut shortcut = Shortcut.Parse(text);
            if (byShortcut.TryGetValue(shortcut, out string? taken) && taken != action)
            {
                throw new InvalidOperationException($"{Name}: {text} is bound to both {taken} and {action}");
            }
            byShortcut[shortcut] = action;
            if (!byAction.TryGetValue(action, out List<Shortcut>? list))
            {
                byAction[action] = list = [];
            }
            list.Add(shortcut);
        }
        return this;
    }

    /// <summary>Gives an action exactly these shortcuts, as a person's keymap does: its own go, and a shortcut another
    /// action had is taken from it (Rider's "remove other assignments").</summary>
    public Keymap Set(string action, IEnumerable<Shortcut> shortcuts)
    {
        if (byAction.TryGetValue(action, out List<Shortcut>? own))
        {
            foreach (Shortcut shortcut in own)
            {
                byShortcut.Remove(shortcut);
            }
            own.Clear();
        }
        else
        {
            byAction[action] = own = [];
        }
        foreach (Shortcut shortcut in shortcuts.Distinct())
        {
            if (byShortcut.TryGetValue(shortcut, out string? taken) && taken != action && byAction.TryGetValue(taken, out List<Shortcut>? theirs))
            {
                theirs.Remove(shortcut);
            }
            byShortcut[shortcut] = action;
            own.Add(shortcut);
        }
        return this;
    }

    /// <summary>Every action something is bound to.</summary>
    public IEnumerable<string> Actions => byAction.Keys;

    public IReadOnlyList<Shortcut> ShortcutsFor(string action) =>
        byAction.TryGetValue(action, out List<Shortcut>? list) ? list : [];

    public string? ActionFor(Shortcut shortcut) => byShortcut.GetValueOrDefault(shortcut);

    public bool StartsChord(Keystroke first) => byShortcut.Keys.Any(s => s.Second is not null && s.First == first);
}

/// <summary>Turns key presses into actions, holding the first half of a chord until the second.</summary>
public sealed class KeymapDispatcher(Keymap keymap)
{
    private Keystroke? pending;

    public Keymap Keymap { get; set; } = keymap;

    /// <summary>The action a key press completes, or null. A press that opens a chord returns null
    /// but is still consumed (<paramref name="consumed"/>). A press with Shift that has no binding of
    /// its own returns its unshifted binding with <paramref name="shifted"/> set, which a movement
    /// takes as "and extend the selection"; anything else should ignore it.</summary>
    public string? Press(KeyEventArgs e, out bool consumed, out bool shifted)
    {
        Keystroke stroke = Keystroke.From(e);
        consumed = false;
        shifted = false;
        if (IsModifierOnly(stroke.Key))
        {
            return null;
        }
        if (pending is { } first)
        {
            pending = null;
            consumed = true;
            return Keymap.ActionFor(new Shortcut(first, stroke));
        }
        if (Keymap.ActionFor(new Shortcut(stroke)) is { } action)
        {
            consumed = true;
            return action;
        }
        if (Keymap.StartsChord(stroke))
        {
            pending = stroke;
            consumed = true;
            return null;
        }
        if (stroke.Modifiers.HasFlag(KeyModifiers.Shift)
            && Keymap.ActionFor(new Shortcut(stroke with { Modifiers = stroke.Modifiers & ~KeyModifiers.Shift })) is { } unshifted)
        {
            shifted = true;
            return unshifted;
        }
        return null;
    }

    public void Reset() => pending = null;

    /// <summary>Whether the first keystroke of a chord has been pressed and the dispatcher waits for its second.</summary>
    public bool IsPending => pending is not null;

    private static bool IsModifierOnly(Key key) => key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
}
