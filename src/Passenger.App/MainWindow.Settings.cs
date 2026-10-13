using Avalonia.Interactivity;
using Avalonia.Styling;
using Passenger.App.Settings;
using Passenger.Editor;

namespace Passenger.App;

/// <summary>The window's settings: applied the moment they change (the theme, every editor, the terminal), and kept
/// only by the window a person uses.</summary>
public partial class MainWindow
{
    private AppSettings settings = new();
    private bool keepsSettings;

    /// <summary>The settings in force.</summary>
    public AppSettings Settings => settings;

    private void AddSettings() => Editors.ForEachEditor(editor => ApplyTo(editor, settings));

    /// <summary>The window a person uses opens on the settings they left (in its <see cref="StateFolder"/>) and keeps
    /// each change; headless ones keep the defaults.</summary>
    public void KeepSettings()
    {
        AppSettings kept = Store().Settings.Value;
        keepsSettings = true;
        ApplySettings(kept, theme: true);
    }

    /// <summary>Puts settings in force, and keeps them when this window keeps its settings. The theme changes only when
    /// its setting does (or the person's settings are first read), so one named on the command line stands.</summary>
    public void ApplySettings(AppSettings next) => ApplySettings(next, theme: false);

    private void ApplySettings(AppSettings next, bool theme)
    {
        AppSettings before = settings;
        settings = next;
        if (Avalonia.Application.Current is { } app && (theme || next.Theme != before.Theme))
        {
            app.RequestedThemeVariant = next.Theme switch
            {
                ThemeChoice.Dark => ThemeVariant.Dark,
                ThemeChoice.Light => ThemeVariant.Light,
                _ => ThemeVariant.Default,
            };
        }
        AppActions.UserShortcuts = next.Shortcuts;
        keys.Keymap = AppActions.Keymap();
        keys.Reset();
        if (menuBar is not null)
        {
            BuildMenuBar();
        }
        foreach (Editors.EditorGroup group in Editors.Groups)
        {
            ApplyTo(group.Editor, next);
        }
        terminal!.View.FontSize = next.TerminalFontSize;
        foreach (ToolWindows.OutputView console in (ToolWindows.OutputView[])[Bottom.Run, Bottom.Build, Bottom.Debug.Console])
        {
            console.Limit = next.ConsoleBufferKb * 1024;
        }
        if (next.TerminalShell is { } shell && Run.CommandLine.Split(shell) is { Count: > 0 } parts)
        {
            terminal.Shell = (parts[0], [.. parts.Skip(1)]);
        }
        else if (before.TerminalShell is not null)
        {
            terminal.Shell = null;
        }
        ApplyAnalysis();
        ListenForAgents(next.McpServer);
        if (keepsSettings)
        {
            state!.Settings.Set(next);
        }
    }

    /// <summary>Settings (Ctrl+Alt+S, Cmd+, on the Mac), over this window until OK or Cancel.</summary>
    public SettingsDialog ShowSettings()
    {
        SettingsDialog dialog = new(settings, keys.Keymap, ApplySettings);
        _ = dialog.ShowDialog(this);
        return dialog;
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e) => ShowSettings();

    /// <summary>The toolbar's magnifier: Search Everywhere, as its shortcut opens it.</summary>
    private void OnSearchClicked(object? sender, RoutedEventArgs e) => RunAppAction(AppActions.GoToEverything);

    private static void ApplyTo(CodeEditor editor, AppSettings with)
    {
        editor.FontSize = with.EditorFontSize;
        editor.LineSpacing = with.EditorLineSpacing;
        editor.Ligatures = with.Ligatures;
        editor.ShowLineNumbers = with.ShowLineNumbers;
        editor.ShowInlayHints = with.ParameterHints;
        editor.RainbowBraces = with.RainbowBraces;
        editor.InvalidateVisual();
    }
}
