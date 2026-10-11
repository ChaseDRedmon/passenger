using System.Globalization;

namespace Passenger.App;

/// <summary>The command line. A flag it does not know, or a value it cannot read, is fatal rather
/// than quietly ignored, so a typo never runs something other than what was asked.</summary>
internal sealed record LaunchOptions(ScreenshotOptions? Screenshot, ThemeChoice? Theme)
{
    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        string? path = null;
        LogicalSize size = new(1600, 1000);
        double scale = 2;
        ThemeChoice? theme = null;
        for (int i = 0; i < args.Count; i++)
        {
            string flag = args[i];
            switch (flag)
            {
                case "--screenshot":
                    path = Value(args, ref i, flag);
                    break;
                case "--size":
                    size = ParseSize(Value(args, ref i, flag));
                    break;
                case "--scale":
                    scale = ParsePositive(Value(args, ref i, flag), flag);
                    break;
                case "--theme":
                    theme = ParseTheme(Value(args, ref i, flag));
                    break;
                default:
                    throw new FormatException($"unknown flag '{flag}'");
            }
        }
        return new LaunchOptions(path is null ? null : new ScreenshotOptions(path, size, scale), theme);
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

    private static LogicalSize ParseSize(string text)
    {
        string[] parts = text.Split('x');
        if (parts.Length != 2)
        {
            throw new FormatException($"--size takes WIDTHxHEIGHT, not '{text}'");
        }
        return new LogicalSize(ParsePositive(parts[0], "--size"), ParsePositive(parts[1], "--size"));
    }

    private static ThemeChoice ParseTheme(string text) => text switch
    {
        "dark" => ThemeChoice.Dark,
        "light" => ThemeChoice.Light,
        "system" => ThemeChoice.System,
        _ => throw new FormatException($"--theme takes dark, light or system, not '{text}'"),
    };

    private static double ParsePositive(string text, string flag)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0)
        {
            throw new FormatException($"{flag} takes a positive number, not '{text}'");
        }
        return value;
    }
}

/// <summary>A screenshot of the main window: its logical size, and the pixels per logical unit.</summary>
internal sealed record ScreenshotOptions(string Path, LogicalSize Size, double Scale);

internal readonly record struct LogicalSize(double Width, double Height);

/// <summary>A theme forced from the command line over the app's own (<c>App.axaml</c>).</summary>
internal enum ThemeChoice
{
    Dark,
    Light,
    System,
}
