using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Passenger.Editor;

namespace Passenger.App.Settings;

/// <summary>Rider's Settings: the pages down the left under a search box, the chosen page's options on the right,
/// OK, Cancel and Apply below. A search keeps the pages with an option whose words match and shows only those options,
/// as Rider's search narrows the tree. The options edit a copy, which Apply and OK hand back.</summary>
public sealed class SettingsDialog : Window
{
    public const string Appearance = "Appearance";
    public const string SystemPage = "System Settings";
    public const string UpdatesPage = "Updates";
    public const string EditorPage = "Editor";
    public const string CodeFoldingPage = "Code Folding";
    public const string LiveTemplatesPage = "Live Templates";
    public const string PostfixPage = "Postfix Completion";
    public const string TerminalPage = "Terminal";
    public const string BuildPage = "Build";
    public const string RunPage = "Run";
    public const string KeymapPage = "Keymap";
    public const string McpPage = "MCP Server";
    public const string DiscordPage = "Discord";
    public const string DiagnosticsPage = "Diagnostics";

    /// <summary>Rider's names for <see cref="BuildResultsMode"/>'s choices, in its order.</summary>
    public static readonly string[] BuildResultsChoices = ["Always", "Only if errors or warnings received", "Only if errors received", "Never"];

    /// <summary>The names of <see cref="DiscordShown"/>'s choices, in its order.</summary>
    public static readonly string[] DiscordChoices = ["Passenger only", "The solution", "The solution and the file"];

    private static readonly string[] Order = [Appearance, SystemPage, UpdatesPage, EditorPage, CodeFoldingPage, PostfixPage, LiveTemplatesPage, BuildPage, RunPage,
        TerminalPage, McpPage, DiscordPage, DiagnosticsPage, KeymapPage];

    private readonly Action<AppSettings> apply;

    /// <summary>The window's actions, which the keymap page lists.</summary>
    private readonly ActionRegistry actions;
    private readonly List<Option> options = [];
    private readonly TextBox search;
    private readonly ListBox pages;
    private readonly TextBlock heading;
    private readonly StackPanel body;
    private readonly TextBlock nothing;
    private readonly Dictionary<string, TextBlock> shortcutTexts = [];
    private TextBlock? keymapNote;
    private string? capturing;

    public SettingsDialog(AppSettings current, ActionRegistry actions, Action<AppSettings> apply)
    {
        this.apply = apply;
        this.actions = actions;
        Working = current;
        Title = "Settings";
        Width = 980;
        Height = 700;
        MinWidth = 640;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Bind(BackgroundProperty, this.GetResourceObservable("Passenger.Chrome"));

        AddOptions(actions.Keymap);
        search = new TextBox { Classes = { "search" }, PlaceholderText = "Search settings", Margin = new Thickness(10, 10, 10, 6) };
        search.TextChanged += (_, _) => Filter();
        pages = new ListBox { Classes = { "tree" } };
        pages.SelectionChanged += (_, _) => ShowPage();
        heading = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 14) };
        body = new StackPanel { Spacing = 12 };
        nothing = new TextBlock { Classes = { "muted" }, Text = "Nothing matches", IsVisible = false, Margin = new Thickness(12) };

        Button ok = new() { Content = "OK", IsDefault = true, Classes = { "primary" }, MinWidth = 80 };
        Button cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        Button applyButton = new() { Content = "Apply", MinWidth = 80 };
        ok.Click += (_, _) => Ok();
        cancel.Click += (_, _) => Close(false);
        applyButton.Click += (_, _) => apply(Working);

        DockPanel left = new() { Width = 240 };
        DockPanel.SetDock(search, Dock.Top);
        left.Children.Add(search);
        left.Children.Add(new Panel { Children = { pages, nothing } });
        Border leftPane = new() { BorderThickness = new Thickness(0, 0, 1, 0), Child = left };
        leftPane.Bind(Border.BorderBrushProperty, leftPane.GetResourceObservable("Passenger.Divider"));
        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(16, 10),
            Children = { cancel, applyButton, ok },
        };
        Border bottom = new() { BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons };
        bottom.Bind(Border.BorderBrushProperty, bottom.GetResourceObservable("Passenger.Divider"));
        DockPanel right = new() { Margin = new Thickness(20, 16, 20, 0) };
        DockPanel.SetDock(heading, Dock.Top);
        right.Children.Add(heading);
        right.Children.Add(new ScrollViewer { Content = body });
        Border editorPane = new() { Child = right };
        editorPane.Bind(Border.BackgroundProperty, editorPane.GetResourceObservable("Passenger.Editor.Background"));

        DockPanel root = new();
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(leftPane, Dock.Left);
        root.Children.Add(bottom);
        root.Children.Add(leftPane);
        root.Children.Add(editorPane);
        Content = root;
        Filter();
        Opened += (_, _) => Dispatcher.UIThread.Post(() => search.Focus(), DispatcherPriority.Loaded);
        AddHandler(KeyDownEvent, OnCaptureKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>The settings as the options now stand.</summary>
    public AppSettings Working { get; private set; }

    /// <summary>The Live Templates page.</summary>
    public LiveTemplatesPane Templates { get; private set; } = null!;

    /// <summary>The Postfix Completion page.</summary>
    public PostfixTemplatesPane Postfix { get; private set; } = null!;

    public string Query
    {
        get => search.Text ?? string.Empty;
        set => search.Text = value;
    }

    /// <summary>The pages listed, after the search.</summary>
    public IReadOnlyList<string> Pages => pages.ItemsSource as IReadOnlyList<string> ?? [];

    public string? Page
    {
        get => pages.SelectedItem as string;
        set => pages.SelectedItem = value;
    }

    /// <summary>The options the chosen page shows now, by their labels.</summary>
    public IReadOnlyList<string> Shown => [.. options.Where(o => o.Page == Page && o.Row.IsVisible).Select(o => o.Label)];

    /// <summary>OK: the settings applied, and the dialog closed.</summary>
    public void Ok()
    {
        apply(Working);
        Close(true);
    }

    /// <summary>Changes one setting, as its option does.</summary>
    public void Change(Func<AppSettings, AppSettings> change) => Working = change(Working);

    private void Filter()
    {
        string[] words = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (Option option in options)
        {
            option.Row.IsVisible = words.All(w => option.Words.Contains(w, StringComparison.OrdinalIgnoreCase));
        }
        string? was = Page;
        string[] listed = [.. Order.Where(p => options.Any(o => o.Page == p && o.Row.IsVisible))];
        pages.ItemsSource = listed;
        nothing.IsVisible = listed.Length == 0;
        pages.SelectedItem = listed.Contains(was) ? was : listed.FirstOrDefault();
        ShowPage();
    }

    private void ShowPage()
    {
        body.Children.Clear();
        heading.Text = Page ?? string.Empty;
        foreach (Option option in options.Where(o => o.Page == Page))
        {
            body.Children.Add(option.Row);
        }
    }

    private void AddOptions(Keymap keymap)
    {
        ComboBox theme = new() { ItemsSource = new[] { "System", "Dark", "Light" }, SelectedIndex = Working.Theme switch { ThemeChoice.Dark => 1, ThemeChoice.Light => 2, _ => 0 }, Width = 160 };
        theme.SelectionChanged += (_, _) => Change(s => s with { Theme = theme.SelectedIndex switch { 1 => ThemeChoice.Dark, 2 => ThemeChoice.Light, _ => ThemeChoice.System } });
        Add(Appearance, "Theme", theme, "colour color dark light");

        Add(SystemPage, "Reopen projects on startup", Check(Working.ReopenProjectsOnStartup, v => Change(s => s with { ReopenProjectsOnStartup = v })),
            "reopen last solution project folder startup start launch welcome screen recent");
        Add(SystemPage, "Save when leaving the IDE", Check(Working.SaveOnDeactivation, v => Change(s => s with { SaveOnDeactivation = v })),
            "autosave files switching to a different application or the built-in terminal focus frame deactivation");
        NumericUpDown seconds = Number(Working.IdleSeconds, AppSettings.MinIdleSeconds, AppSettings.MaxIdleSeconds, 1, v => Change(s => s with { IdleSeconds = (int)v }));
        seconds.IsEnabled = Working.SaveWhenIdle;
        CheckBox idle = Check(Working.SaveWhenIdle, v =>
        {
            Change(s => s with { SaveWhenIdle = v });
            seconds.IsEnabled = v;
        });
        TextBlock unit = new() { Text = "seconds", VerticalAlignment = VerticalAlignment.Center };
        Add(SystemPage, "Save when idle for", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { idle, seconds, unit } },
            "autosave files idle inactive seconds");

        // Rider's System Settings, Updates; what is sent is said under it.
        Add(UpdatesPage, "Check for updates", Check(Working.CheckForUpdates, v => Change(s => s with { CheckForUpdates = v })),
            "updates check new version release newer download github automatic install restart",
            "Passenger asks GitHub for its latest release when it starts and once a day. When a newer one is out, an installed "
            + "Passenger downloads it, checks it against the SHA-256 the release gives, and installs it when it restarts; a "
            + "copy built from source says in the status bar that it is out. Nothing is sent but the requests, which name "
            + "Passenger and its version.");

        Add(EditorPage, "Font size", Number(Working.EditorFontSize, AppSettings.MinFontSize, AppSettings.MaxFontSize, 1, v => Change(s => s with { EditorFontSize = v })), "font size text");
        Add(EditorPage, "Line spacing", Number(Working.EditorLineSpacing, AppSettings.MinLineSpacing, AppSettings.MaxLineSpacing, 0.1, v => Change(s => s with { EditorLineSpacing = v })), "font line height");
        Add(EditorPage, "Enable ligatures", Check(Working.Ligatures, v => Change(s => s with { Ligatures = v })), "font arrows");
        Add(EditorPage, "Show line numbers", Check(Working.ShowLineNumbers, v => Change(s => s with { ShowLineNumbers = v })), "gutter appearance");
        Add(EditorPage, "Rainbow braces", Check(Working.RainbowBraces, v => Change(s => s with { RainbowBraces = v })), "brackets parentheses colour color nesting");
        Add(EditorPage, "Solution-wide analysis", Check(Working.SolutionWideAnalysis, v => Change(s => s with { SolutionWideAnalysis = v })),
            "errors problems whole solution analysis inspections without build");
        Add(EditorPage, "Show parameter name hints", Check(Working.ParameterHints, v => Change(s => s with { ParameterHints = v })), "inlay hints parameter names arguments");
        Add(EditorPage, "Show type name hints", Check(Working.TypeHints, v => Change(s => s with { TypeHints = v })), "inlay hints type names var lambda parameters new inferred");
        // Rider's Editor, General: its Soft Wraps, Appearance and Font Size options, by its names.
        TextBox masks = new() { Classes = { "search" }, Text = Working.SoftWrapFiles, PlaceholderText = "No files", Width = 260, IsEnabled = Working.SoftWrap };
        ToolTip.SetTip(masks, "Use * and ? as wildcards and ; to separate patterns");
        masks.TextChanged += (_, _) => Change(s => s with { SoftWrapFiles = masks.Text ?? string.Empty });
        CheckBox wrapFiles = Check(Working.SoftWrap, v =>
        {
            Change(s => s with { SoftWrap = v });
            masks.IsEnabled = v;
        });
        Add(EditorPage, "Soft-wrap these files", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { wrapFiles, masks } },
            "soft wrap wrapping long lines width markdown text files masks");
        Add(EditorPage, "Use the original line's indent for wrapped fragments", Check(Working.SoftWrapIndent, v => Change(s => s with { SoftWrapIndent = v })),
            "soft wrap wrapped indent indentation fragments");
        Add(EditorPage, "Show whitespaces", Check(Working.ShowWhitespaces, v => Change(s => s with { ShowWhitespaces = v })),
            "whitespace spaces tabs dots arrows invisible characters appearance");
        Add(EditorPage, OperatingSystem.IsMacOS() ? "Change font size with Command+Mouse Wheel" : "Change font size with Ctrl+Mouse Wheel",
            Check(Working.WheelZoom, v => Change(s => s with { WheelZoom = v })), "zoom font size mouse wheel scroll ctrl control command cmd");

        // Rider's Editor, General, Code Folding: its Fold by default switches for what Passenger folds, by its names (its
        // General's, .NET's and XML's).
        const string Folding = "fold by default folding collapse collapsed open opening region";
        Add(CodeFoldingPage, "File header", Check(Working.FoldFileHeader, v => Change(s => s with { FoldFileHeader = v })),
            $"{Folding} comments licence license copyright top",
            "Fold by default: what folds as a file opens. A file keeps the folds it was closed with, after a restart too.");
        Add(CodeFoldingPage, "Imports", Check(Working.FoldImports, v => Change(s => s with { FoldImports = v })), $"{Folding} usings using directives");
        Add(CodeFoldingPage, "Documentation comments", Check(Working.FoldDocComments, v => Change(s => s with { FoldDocComments = v })),
            $"{Folding} doc xml summary comments");
        Add(CodeFoldingPage, "Method bodies", Check(Working.FoldMethodBodies, v => Change(s => s with { FoldMethodBodies = v })),
            $"{Folding} members bodies accessors properties functions");
        Add(CodeFoldingPage, "Preprocessor regions", Check(Working.FoldPreprocessorRegions, v => Change(s => s with { FoldPreprocessorRegions = v })),
            $"{Folding} #region endregion directives .net");
        Add(CodeFoldingPage, "Line and block comments", Check(Working.FoldComments, v => Change(s => s with { FoldComments = v })),
            $"{Folding} block comments .net");
        Add(CodeFoldingPage, "XML tags", Check(Working.FoldXmlTags, v => Change(s => s with { FoldXmlTags = v })),
            $"{Folding} xml elements tags project csproj props axaml xaml");

        // Rider's Editor, General, Postfix Completion and Editor, Live Templates, C#: each a page of its own, found by its
        // templates' words too.
        Postfix = new PostfixTemplatesPane(Working, Change);
        AddPage(PostfixPage, Postfix, "postfix templates completion expression dot autopopup "
            + string.Join(' ', Languages.CSharp.PostfixTemplates.All.Select(t => $"{t.Name} {t.Description} {t.Example}")));
        Templates = new LiveTemplatesPane(TemplateChange.Applied(Working.LiveTemplates), list => Change(s => s with { LiveTemplates = TemplateChange.Of(list) }));
        AddPage(LiveTemplatesPage, Templates, "live templates c# shortcut abbreviation snippet expand tab surround with generation availability mnemonic variables macros "
            + string.Join(' ', CSharp.LiveTemplates.BuiltIn.Select(t => $"{t.Abbreviation} {t.Description}")));

        // Rider's Toolset and Build, Presentation, by its names: the implicit build's option only while the explicit's is on.
        CheckBox implicitly = Check(Working.OpenBuildWindowOnImplicitBuild, v => Change(s => s with { OpenBuildWindowOnImplicitBuild = v }));
        implicitly.IsEnabled = Working.OpenBuildWindowOnBuild;
        CheckBox explicitly = Check(Working.OpenBuildWindowOnBuild, v =>
        {
            Change(s => s with { OpenBuildWindowOnBuild = v });
            implicitly.IsEnabled = v;
        });
        Add(BuildPage, "Tool Window: when build is explicitly invoked, open the build progress view in a tool window", explicitly,
            "build tool window open progress view start explicitly invoked solution rebuild clean toolset presentation");
        Add(BuildPage, "Tool Window: open the build progress view even when the build is invoked implicitly (i.e. Run/Debug)", implicitly,
            "build tool window open progress view start implicitly run debug profile before launch toolset presentation");
        ComboBox results = new() { ItemsSource = BuildResultsChoices, SelectedIndex = (int)Working.ShowBuildResults, Width = 260 };
        results.SelectionChanged += (_, _) => Change(s => s with { ShowBuildResults = (BuildResultsMode)Math.Max(0, results.SelectedIndex) });
        Add(BuildPage, "Showing build results after build completed", results,
            "build tool window results after completed finished failed errors warnings always never toolset presentation");

        Add(RunPage, "Hot Reload on save", Check(Working.HotReload, v => Change(s => s with { HotReload = v })), "dotnet watch apply changes edit continue running");
        NumericUpDown buffer = Number(Working.ConsoleBufferKb, AppSettings.MinConsoleBufferKb, AppSettings.MaxConsoleBufferKb, 64, v => Change(s => s with { ConsoleBufferKb = (int)v }));
        Add(RunPage, "Console buffer size", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { buffer, new TextBlock { Text = "KB", VerticalAlignment = VerticalAlignment.Center } } },
            "console cycle buffer size output limit lines run debug build kb");

        Add(TerminalPage, "Font size", Number(Working.TerminalFontSize, AppSettings.MinFontSize, AppSettings.MaxFontSize, 1, v => Change(s => s with { TerminalFontSize = v })), "font size shell");
        (string shell, _) = Passenger.Terminal.TerminalSession.DefaultShell();
        TextBox path = new() { Classes = { "search" }, Text = Working.TerminalShell ?? string.Empty, PlaceholderText = shell, Width = 320 };
        path.TextChanged += (_, _) => Change(s => s with { TerminalShell = string.IsNullOrWhiteSpace(path.Text) ? null : path.Text.Trim() });
        Add(TerminalPage, "Shell path", path, "shell zsh bash powershell program");

        // What turning it on allows is said under it, as Rider's consent to its MCP server says.
        Add(McpPage, "Enable MCP Server", Check(Working.McpServer, v => Change(s => s with { McpServer = v })),
            "mcp model context protocol agents ai claude codex attach socket",
            "Agents started with passenger --mcp-attach can use this window: read and change files, build, run and debug, use Git, "
            + "NuGet and the terminal. Each agent asks before its first use of a tool that changes something.");

        // The JetBrains Discord plugins' options at their defaults (AppSettings says whose), changed only while it is on.
        ComboBox shows = new() { ItemsSource = DiscordChoices, SelectedIndex = (int)Working.DiscordShows, Width = 240, IsEnabled = Working.ShowInDiscord };
        shows.SelectionChanged += (_, _) => Change(s => s with { DiscordShows = (DiscordShown)Math.Max(0, shows.SelectedIndex) });
        NumericUpDown idleMinutes = Number(Working.DiscordIdleMinutes, AppSettings.MinDiscordIdleMinutes, AppSettings.MaxDiscordIdleMinutes, 1,
            v => Change(s => s with { DiscordIdleMinutes = (int)v }));
        idleMinutes.IsEnabled = Working.ShowInDiscord && Working.DiscordIdle;
        CheckBox whenIdle = Check(Working.DiscordIdle, v =>
        {
            Change(s => s with { DiscordIdle = v });
            idleMinutes.IsEnabled = Working.ShowInDiscord && v;
        });
        whenIdle.IsEnabled = Working.ShowInDiscord;
        CheckBox discord = Check(Working.ShowInDiscord, v =>
        {
            Change(s => s with { ShowInDiscord = v });
            shows.IsEnabled = v;
            whenIdle.IsEnabled = v;
            idleMinutes.IsEnabled = v && Working.DiscordIdle;
        });
        const string Discord = "discord rich presence activity status playing";
        Add(DiscordPage, "Show activity in Discord", discord, $"{Discord} friends share game",
            "Discord shows Playing Passenger with its icon, what is chosen below and how long the solution has been open, to "
            + "whoever can see your activity. Passenger tells the Discord app on this computer alone.");
        Add(DiscordPage, "Show", shows, $"{Discord} solution project file name details visibility privacy");
        Add(DiscordPage, "Only Passenger when idle for", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { whenIdle, idleMinutes, new TextBlock { Text = "minutes", VerticalAlignment = VerticalAlignment.Center } },
        }, $"{Discord} idle inactive away timeout minutes");

        // Rider's Help, Diagnostic Tools: what is written where is said under each, the folder being Help, Show Log's.
        Add(DiagnosticsPage, "Trace protocol messages", Check(Working.TraceProtocols, v => Change(s => s with { TraceProtocols = v })),
            "trace log lsp dap json-rpc language server debugger adapter vstest tests messages verbose",
            "Every message to and from the C# language server, the debugger and the test runner is written to lsp.log, dap.log "
            + "and vstest.log in the logs folder (Help, Show Log).");
        Add(DiagnosticsPage, "Log every program started", Check(Working.LogEveryProgram, v => Change(s => s with { LogEveryProgram = v })),
            "debug log processes programs commands git dotnet exit code time",
            "Each program the IDE runs (git, dotnet, the servers) is written to passenger.log with its exit code and time. "
            + "Otherwise only those that fail or take a second or more are.");

        keymapNote = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        keymapNote.Bind(TextBlock.ForegroundProperty, keymapNote.GetResourceObservable("Passenger.TextMuted"));
        Add(KeymapPage, $"{keymap.Name} keymap", keymapNote, "keymap shortcut binding keys");
        foreach (IdeAction action in actions.All.OrderBy(a => a.Title, StringComparer.Ordinal))
        {
            TextBlock keys = new() { VerticalAlignment = VerticalAlignment.Center, Width = 200, TextTrimming = TextTrimming.CharacterEllipsis };
            keys.Bind(TextBlock.ForegroundProperty, keys.GetResourceObservable("Passenger.TextMuted"));
            shortcutTexts[action.Id] = keys;
            Button add = new() { Content = "Add...", Padding = new Thickness(8, 2) };
            add.Click += (_, _) => Capture(action.Id);
            Button remove = new() { Content = "Remove", Padding = new Thickness(8, 2) };
            remove.Click += (_, _) => Rebind(action.Id);
            Button reset = new() { Content = "Reset", Padding = new Thickness(8, 2) };
            reset.Click += (_, _) => ResetShortcuts(action.Id);
            StackPanel editor = new() { Orientation = Orientation.Horizontal, Spacing = 6, Children = { keys, add, remove, reset } };
            Add(KeymapPage, action.Title, editor, $"{action.Group} {actions.Shortcut(action.Id)} keymap shortcut");
        }
        ShowShortcuts();
    }

    /// <summary>Gives an action exactly these shortcuts (keymap notation, "meta shift K"); none unbinds it. A shortcut
    /// another action had is taken from it, and the page says so.</summary>
    public void Rebind(string action, params string[] shortcuts)
    {
        Keymap before = AppActions.Keymap(Working.Shortcuts);
        List<string> taken = [];
        foreach (string text in shortcuts)
        {
            if (before.ActionFor(Shortcut.Parse(text)) is { } other && other != action)
            {
                taken.Add($"{Shortcut.Parse(text).Display()} was {actions[other]?.Title ?? other}'s");
                // The other action keeps its remaining shortcuts as its own.
                Working = Working with { Shortcuts = new(Working.Shortcuts) { [other] = [.. before.ShortcutsFor(other).Where(s => s != Shortcut.Parse(text)).Select(s => s.Notation())] } };
            }
        }
        Working = Working with { Shortcuts = new(Working.Shortcuts) { [action] = shortcuts } };
        ShowShortcuts();
        keymapNote!.Text = taken.Count > 0 ? string.Join("; ", taken) + "." : string.Empty;
        keymapNote.IsVisible = taken.Count > 0;
    }

    /// <summary>Puts an action's shortcuts back as the keymap has them.</summary>
    public void ResetShortcuts(string action)
    {
        Dictionary<string, string[]> shortcuts = new(Working.Shortcuts);
        shortcuts.Remove(action);
        Working = Working with { Shortcuts = shortcuts };
        ShowShortcuts();
    }

    /// <summary>The next keys pressed become one more shortcut of an action, as Rider's Add Keyboard Shortcut does.</summary>
    public void Capture(string action)
    {
        capturing = action;
        shortcutTexts[action].Text = "Press the keys...";
    }

    private void ShowShortcuts()
    {
        Keymap current = AppActions.Keymap(Working.Shortcuts);
        foreach ((string action, TextBlock text) in shortcutTexts)
        {
            IReadOnlyList<Shortcut> keys = current.ShortcutsFor(action);
            text.Text = keys.Count == 0 ? "No shortcut" : string.Join(", ", keys.Select(k => k.Display()));
        }
    }

    /// <summary>Seen before the focused control: the keys being captured are a shortcut, not typing.</summary>
    private void OnCaptureKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (capturing is { } action && e.Key is not (Avalonia.Input.Key.LeftShift or Avalonia.Input.Key.RightShift or Avalonia.Input.Key.LeftCtrl
            or Avalonia.Input.Key.RightCtrl or Avalonia.Input.Key.LeftAlt or Avalonia.Input.Key.RightAlt or Avalonia.Input.Key.LWin or Avalonia.Input.Key.RWin))
        {
            capturing = null;
            e.Handled = true;
            if (e.Key == Avalonia.Input.Key.Escape && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
            {
                ShowShortcuts();
                return;
            }
            Shortcut pressed = new(Keystroke.From(e));
            string[] keep = [.. AppActions.Keymap(Working.Shortcuts).ShortcutsFor(action).Select(s => s.Notation())];
            Rebind(action, [.. keep, pressed.Notation()]);
        }
    }

    /// <summary>A page that is one control of its own (the live templates' list and editor), the whole of its width, found
    /// by a search for any of its words.</summary>
    private void AddPage(string page, Control content, string keywords) => options.Add(new Option(page, page, content, $"{page} {keywords}"));

    /// <summary>An option's row: its label, what edits it, and a note under the editor when it needs saying what it
    /// does.</summary>
    private void Add(string page, string label, Control editor, string keywords, string? note = null)
    {
        TextBlock name = new() { Text = label, Width = 220, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Control row = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, editor } };
        if (note is not null)
        {
            TextBlock said = new() { Text = note, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Margin = new Thickness(name.Width, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            said.Bind(TextBlock.ForegroundProperty, said.GetResourceObservable("Passenger.TextMuted"));
            row = new StackPanel { Spacing = 4, Children = { row, said } };
        }
        options.Add(new Option(page, label, row, $"{page} {label} {keywords}"));
    }

    private static NumericUpDown Number(double value, double min, double max, double step, Action<double> changed)
    {
        NumericUpDown number = new() { Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)step, Width = 140, FormatString = step < 1 ? "0.0" : "0" };
        number.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } next)
            {
                changed((double)next);
            }
        };
        return number;
    }

    private static CheckBox Check(bool value, Action<bool> changed)
    {
        CheckBox box = new() { IsChecked = value };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    private sealed record Option(string Page, string Label, Control Row, string Words);
}
