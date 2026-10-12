using System.Globalization;
using Avalonia;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App;

/// <summary>The command line: <c>passenger [PATH] [--file F]... [--caret L:C] [--theme T]</c>, a
/// screenshot with the steps to take before it, or the Model Context Protocol server: <c>--mcp</c> (an IDE
/// with no window, on standard input and output), <c>--mcp-attach [PID] [FOLDER]</c> (a window's, the one over the
/// folder first), and <c>--no-mcp</c> (a window that never lets agents in). A flag it does not know, or a value it
/// cannot read, is fatal rather than ignored, so a typo never runs something other than what was asked.</summary>
public sealed record LaunchOptions(string? Folder, IReadOnlyList<string> Files, TextPosition? Caret, ThemeChoice? Theme, ScreenshotOptions? Screenshot)
{
    public McpMode Mcp { get; init; }

    /// <summary>The process <c>--mcp-attach</c> names, or null for any Passenger's windows.</summary>
    public int? AttachTo { get; init; }

    /// <summary>Whether the window may listen for agents on its socket once its Enable MCP Server setting is on
    /// (<c>--no-mcp</c> says never).</summary>
    public bool ListenForAgents { get; init; } = true;

    public static LaunchOptions Empty { get; } = new(null, [], null, null, null);

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        string? folder = null;
        List<string> files = [];
        TextPosition? caret = null;
        ThemeChoice? theme = null;
        string? shot = null;
        LogicalSize size = new(1600, 1000);
        double scale = 2;
        Rect? crop = null;
        List<ScriptStep> steps = [];
        McpMode mcp = McpMode.None;
        int? attachTo = null;
        bool listen = true;
        for (int i = 0; i < args.Count; i++)
        {
            string flag = args[i];
            switch (flag)
            {
                case "--mcp":
                    mcp = McpMode.Stdio;
                    break;
                case "--mcp-attach":
                    mcp = McpMode.Attach;
                    if (i + 1 < args.Count && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
                    {
                        attachTo = pid;
                        i++;
                    }
                    break;
                case "--no-mcp":
                    listen = false;
                    break;
                case "--file":
                    files.Add(Value(args, ref i, flag));
                    break;
                case "--caret":
                    caret = ParseCaret(Value(args, ref i, flag));
                    break;
                case "--theme":
                    theme = ParseTheme(Value(args, ref i, flag));
                    break;
                case "--screenshot":
                    shot = Value(args, ref i, flag);
                    break;
                case "--size":
                    double[] wh = Numbers(Value(args, ref i, flag), 'x', 2, flag);
                    size = new LogicalSize(wh[0], wh[1]);
                    break;
                case "--scale":
                    scale = Numbers(Value(args, ref i, flag), ',', 1, flag)[0];
                    break;
                case "--crop":
                    double[] box = Numbers(Value(args, ref i, flag), ',', 4, flag);
                    crop = new Rect(box[0], box[1], box[2], box[3]);
                    break;
                case "--type":
                    steps.Add(new ScriptStep.Type(Value(args, ref i, flag)));
                    break;
                case "--key":
                    steps.Add(new ScriptStep.Key(ParseKey(Value(args, ref i, flag))));
                    break;
                case "--wait":
                    steps.Add(new ScriptStep.Wait(TimeSpan.FromMilliseconds(Numbers(Value(args, ref i, flag), ',', 1, flag)[0])));
                    break;
                case "--hover":
                    double[] at = Numbers(Value(args, ref i, flag), ',', 2, flag);
                    steps.Add(new ScriptStep.Hover(new Point(at[0], at[1])));
                    break;
                case "--click" or "--double-click":
                    double[] xy = Numbers(Value(args, ref i, flag), ',', 2, flag);
                    steps.Add(new ScriptStep.Click(new Point(xy[0], xy[1]), flag == "--click" ? 1 : 2));
                    break;
                case "--drag" or "--drag-hold":
                    double[] line = Numbers(Value(args, ref i, flag), ',', 4, flag);
                    steps.Add(new ScriptStep.Drag(new Point(line[0], line[1]), new Point(line[2], line[3]), Release: flag == "--drag"));
                    break;
                case { } path when !path.StartsWith('-') && folder is null && files.Count == 0:
                    if (Directory.Exists(path))
                    {
                        folder = Path.GetFullPath(path);
                    }
                    else if (File.Exists(path))
                    {
                        folder = Path.GetDirectoryName(Path.GetFullPath(path));
                        files.Add(Path.GetFullPath(path));
                    }
                    else
                    {
                        throw new FormatException($"'{path}' is neither a folder nor a file");
                    }
                    break;
                default:
                    throw new FormatException($"unknown flag '{flag}'");
            }
        }
        if (shot is null && (steps.Count > 0 || crop is not null))
        {
            throw new FormatException("--type, --key, --click, --hover, --wait and --crop are steps of a --screenshot");
        }
        if (mcp != McpMode.None && shot is not null)
        {
            throw new FormatException("--mcp and --mcp-attach do not take a --screenshot");
        }
        ScreenshotOptions? screenshot = shot is null ? null : new ScreenshotOptions(shot, size, scale, steps, crop);
        return new LaunchOptions(folder, files, caret, theme, screenshot) { Mcp = mcp, AttachTo = attachTo, ListenForAgents = listen };
    }

    private static string Value(IReadOnlyList<string> args, ref int i, string flag)
    {
        if (i + 1 >= args.Count)
        {
            throw new FormatException($"{flag} needs a value");
        }
        i++;
        return args[i];
    }

    private static double[] Numbers(string text, char separator, int count, string flag)
    {
        string[] parts = text.Split(separator);
        double[] values = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || values[i] < 0)
            {
                throw new FormatException($"{flag} takes {count} non-negative numbers, not '{text}'");
            }
        }
        if (values.Length != count || (count <= 2 && values.Any(v => v == 0)))
        {
            throw new FormatException($"{flag} takes {count} numbers split by '{separator}', not '{text}'");
        }
        return values;
    }

    /// <summary>"12:5" is line 12, column 5, as the status bar shows them (from one).</summary>
    private static TextPosition ParseCaret(string text)
    {
        string[] parts = text.Split(':');
        if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], out int line) || line < 1)
        {
            throw new FormatException($"--caret takes LINE or LINE:COLUMN from 1, not '{text}'");
        }
        int column = 1;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out column) || column < 1))
        {
            throw new FormatException($"--caret takes LINE or LINE:COLUMN from 1, not '{text}'");
        }
        return new TextPosition(line - 1, column - 1);
    }

    /// <summary>"cmd+shift+L" or Rider's own "meta shift L".</summary>
    private static Keystroke ParseKey(string text)
    {
        string spaced = text.Replace('+', ' ');
        return Keystroke.Parse(spaced);
    }

    private static ThemeChoice ParseTheme(string text) => text switch
    {
        "dark" => ThemeChoice.Dark,
        "light" => ThemeChoice.Light,
        "system" => ThemeChoice.System,
        _ => throw new FormatException($"--theme takes dark, light or system, not '{text}'"),
    };
}

/// <summary>A screenshot of the main window: its logical size, pixels per logical unit, the steps to
/// take first and the part of the window to keep (logical units), or all of it.</summary>
public sealed record ScreenshotOptions(string Path, LogicalSize Size, double Scale, IReadOnlyList<ScriptStep> Steps, Rect? Crop);

public readonly record struct LogicalSize(double Width, double Height);

/// <summary>One thing a scripted screenshot does before it captures, through the window's real input
/// so it exercises exactly what a person's keys and clicks would.</summary>
public abstract record ScriptStep
{
    public sealed record Type(string Text) : ScriptStep;

    public sealed record Key(Keystroke Stroke) : ScriptStep;

    public sealed record Click(Point At, int Count) : ScriptStep;

    /// <summary>The left button pressed at one point and moved to another, let go there unless the capture is to show
    /// the drag itself (a tool window over its drop place).</summary>
    public sealed record Drag(Point From, Point To, bool Release) : ScriptStep;

    /// <summary>The pointer moved to a point and left to rest there, long enough for a hover card.</summary>
    public sealed record Hover(Point At) : ScriptStep;

    /// <summary>Time for what the screenshot does not wait on by itself: a program running.</summary>
    public sealed record Wait(TimeSpan For) : ScriptStep;
}

/// <summary>How the command line asks for the Model Context Protocol server.</summary>
public enum McpMode
{
    None,

    /// <summary>An IDE with no window, its tools on standard input and output.</summary>
    Stdio,

    /// <summary>Standard input and output joined to a running IDE's socket.</summary>
    Attach,
}

/// <summary>A theme forced from the command line over the app's own (<c>App.axaml</c>).</summary>
public enum ThemeChoice
{
    Dark,
    Light,
    System,
}
