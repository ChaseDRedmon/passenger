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
    private string? settingsPath;

    /// <summary>The settings in force.</summary>
    public AppSettings Settings => settings;

    private void AddSettings() => Editors.ForEachEditor(editor => ApplyTo(editor, settings));

    /// <summary>The window a person uses opens on the settings they left and keeps each change (a file can be named
    /// for a test); headless ones keep the defaults.</summary>
    public void KeepSettings(string? path = null)
    {
        settingsPath = path ?? AppSettings.DefaultPath;
        ApplySettings(AppSettings.Load(settingsPath));
    }

    /// <summary>Puts settings in force, and keeps them when this window keeps its settings.</summary>
    public void ApplySettings(AppSettings next)
    {
        AppSettings before = settings;
        settings = next;
        if (Avalonia.Application.Current is { } app)
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
        foreach (Editors.EditorGroup group in Editors.Groups)
        {
            ApplyTo(group.Editor, next);
        }
        terminal!.View.FontSize = next.TerminalFontSize;
        if (next.TerminalShell is { } shell && Run.CommandLine.Split(shell) is { Count: > 0 } parts)
        {
            terminal.Shell = (parts[0], [.. parts.Skip(1)]);
        }
        else if (before.TerminalShell is not null)
        {
            terminal.Shell = null;
        }
        if (settingsPath is not null)
        {
            next.Save(settingsPath);
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
