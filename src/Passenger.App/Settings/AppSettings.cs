using System.Text.Json.Nodes;
using Passenger.App.State;

namespace Passenger.App.Settings;

/// <summary>What a person has set in Settings: the theme, the editor's and the terminal's font, the shell. Kept in the
/// store's <c>settings.json</c> (only by the window a person uses: a headless IDE starts on the defaults and keeps
/// nothing); a value the file lacks takes its default, and one out of range is brought into it.</summary>
public sealed record AppSettings : IStateDocument<AppSettings>
{
    /// <summary>The smallest font Settings offers; a size read from the file is brought between it and the largest.</summary>
    public const double MinFontSize = 6;

    public const double MaxFontSize = 48;
    public const double MinLineSpacing = 0.8;
    public const double MaxLineSpacing = 3;
    public const int MinIdleSeconds = 1;
    public const int MaxIdleSeconds = 3600;
    public const int MinConsoleBufferKb = 16;
    public const int MaxConsoleBufferKb = 102_400;

    public ThemeChoice Theme { get; init; } = ThemeChoice.System;
    public double EditorFontSize { get; init; } = 13;
    public double EditorLineSpacing { get; init; } = 1.2;
    public bool Ligatures { get; init; }
    public bool ShowLineNumbers { get; init; } = true;

    /// <summary>Rider's rainbow braces: brackets coloured by how deeply they nest.</summary>
    public bool RainbowBraces { get; init; } = true;

    /// <summary>Rider's parameter name hints: an argument's parameter shown before it where the call does not say it.</summary>
    public bool ParameterHints { get; init; } = true;

    /// <summary>Rider's type name hints: the type of a <c>var</c>, a lambda's parameter or a target-typed <c>new</c> shown
    /// after it where the code does not say it.</summary>
    public bool TypeHints { get; init; } = true;
    public double TerminalFontSize { get; init; } = 13;

    /// <summary>Whether Run runs under dotnet watch, so a save applies its edits to the running program (Hot Reload),
    /// and an edit that cannot be applied restarts it.</summary>
    public bool HotReload { get; init; }

    /// <summary>Rider's solution-wide analysis: every file's errors in Problems as code changes, without a build. Off by
    /// default, as in Rider, since it analyses the whole solution; the status bar turns it on and off.</summary>
    public bool SolutionWideAnalysis { get; init; }

    /// <summary>Rider's autosave on frame deactivation: every changed file saved when the focus goes to another program
    /// or to the terminal.</summary>
    public bool SaveOnDeactivation { get; init; } = true;

    /// <summary>Rider's "Save files if the IDE is idle": every changed file saved once no key or click has come for
    /// <see cref="IdleSeconds"/>.</summary>
    public bool SaveWhenIdle { get; init; }

    public int IdleSeconds { get; init; } = 15;

    /// <summary>Rider's "Reopen projects on startup": started with nothing named, the window opens the folder or solution
    /// opened last, rather than the Welcome screen.</summary>
    public bool ReopenProjectsOnStartup { get; init; } = true;

    /// <summary>Rider's Check IDE updates: the person's window asks GitHub for Passenger's latest release as it starts and
    /// once a day, and says when a newer one is out. Off, nothing is asked unless Help, Check for Updates is chosen.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Rider's console cycle buffer size (<c>idea.cycle.buffer.size</c>): how much of a Run, Build or Debug
    /// console's text is kept, in KB, the oldest lines dropped past it.</summary>
    public int ConsoleBufferKb { get; init; } = ToolWindows.OutputView.DefaultLimitKb;

    /// <summary>The person's shortcuts over the keymap's, by action, in keymap notation ("meta shift K"); an empty list
    /// unbinds the action.</summary>
    public Dictionary<string, string[]> Shortcuts { get; init; } = [];

    /// <summary>The terminal's shell, or null for the user's own login shell.</summary>
    public string? TerminalShell { get; init; }

    /// <summary>Rider's Enable MCP Server: agents reach the window through its socket (<c>passenger --mcp-attach</c>).
    /// Off until the person turns it on, since any program running as them could then act with the IDE's
    /// permissions.</summary>
    public bool McpServer { get; init; }

    /// <summary>A client's verbose protocol trace: every message to and from the C# language servers, the debugger and
    /// VSTest written to lsp.log, dap.log and vstest.log in the logs folder (<see cref="IdeLog.Traces"/>).</summary>
    public bool TraceProtocols { get; init; }

    /// <summary>Rider's debug log of its processes: every program the IDE runs in passenger.log with its exit code and
    /// time, not only those that fail or take a while (<see cref="IdeLog.Programs"/>).</summary>
    public bool LogEveryProgram { get; init; }

    public static IReadOnlyList<Action<JsonObject>> Upgrades { get; } = [StateStore.Unversioned];

    /// <summary>A hand-edited or later build's value this build cannot use is brought into range, or back to its default;
    /// a shortcut that does not parse is the keymap's to pass over.</summary>
    public AppSettings Clamped()
    {
        AppSettings defaults = new();
        return this with
        {
            Theme = Enum.IsDefined(Theme) ? Theme : defaults.Theme,
            EditorFontSize = Math.Clamp(EditorFontSize, MinFontSize, MaxFontSize),
            EditorLineSpacing = Math.Clamp(EditorLineSpacing, MinLineSpacing, MaxLineSpacing),
            TerminalFontSize = Math.Clamp(TerminalFontSize, MinFontSize, MaxFontSize),
            IdleSeconds = Math.Clamp(IdleSeconds, MinIdleSeconds, MaxIdleSeconds),
            ConsoleBufferKb = Math.Clamp(ConsoleBufferKb, MinConsoleBufferKb, MaxConsoleBufferKb),
            Shortcuts = (Shortcuts ?? []).Where(s => s.Value is not null).ToDictionary(s => s.Key, s => s.Value.Where(k => k is not null).ToArray()),
            TerminalShell = string.IsNullOrWhiteSpace(TerminalShell) ? null : TerminalShell,
        };
    }
}
