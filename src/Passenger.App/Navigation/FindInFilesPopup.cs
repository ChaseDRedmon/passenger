using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Navigation;

/// <summary>Rider's Find in Files (and Replace in Files): the query with Match case, Words and Regex (Alt+C, Alt+O,
/// Alt+X), the matches as they are found with each one's file and line, the chosen one's file previewed below; Enter
/// or a double-click goes to it, Escape closes. In Replace mode a replacement and Replace All.</summary>
public sealed class FindInFilesPopup : UserControl
{
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(250);

    private readonly Func<SearchQuery, CancellationToken, Task<IReadOnlyList<SearchMatch>>> search;
    private readonly Func<string, TextSnapshot?> textOf;
    private readonly TextBox query;
    private readonly TextBox? replacement;
    private readonly ToggleButton matchCase;
    private readonly ToggleButton words;
    private readonly ToggleButton regex;
    private readonly ListBox results;
    private readonly TextBlock status;
    private readonly CodeEditor preview;
    private readonly DispatcherTimer typing;
    private CancellationTokenSource? asking;
    private IReadOnlyList<SearchMatch> matches = [];
    private Task? searching;

    public FindInFilesPopup(Func<SearchQuery, CancellationToken, Task<IReadOnlyList<SearchMatch>>> search, Func<string, TextSnapshot?> textOf, bool replace, string? initial = null)
    {
        this.search = search;
        this.textOf = textOf;
        Width = 820;
        query = new TextBox { Classes = { "search" }, PlaceholderText = "Text to find", Text = initial ?? string.Empty };
        matchCase = Option("Cc", "Match case (Alt+C)");
        words = Option("W", "Words (Alt+O)");
        regex = Option(".*", "Regex (Alt+X)");
        DockPanel find = new() { Margin = new Thickness(10, 0, 10, 6) };
        StackPanel toggles = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(6, 0, 0, 0), Children = { matchCase, words, regex } };
        DockPanel.SetDock(toggles, Dock.Right);
        find.Children.Add(toggles);
        find.Children.Add(query);
        StackPanel top = new() { Children = { new TextBlock { Classes = { "paneTitle" }, Text = replace ? "Replace in Files" : "Find in Files", Margin = new Thickness(12, 10, 12, 6) }, find } };
        if (replace)
        {
            replacement = new TextBox { Classes = { "search" }, PlaceholderText = "Replacement" };
            Button all = new() { Content = "Replace All", Classes = { "primary" }, Padding = new Thickness(12, 4), Margin = new Thickness(6, 0, 0, 0) };
            all.Click += (_, _) => RequestReplace();
            DockPanel row = new() { Margin = new Thickness(10, 0, 10, 6) };
            DockPanel.SetDock(all, Dock.Right);
            row.Children.Add(all);
            row.Children.Add(replacement);
            top.Children.Add(row);
        }
        status = new TextBlock { Classes = { "muted" }, FontSize = 12, Margin = new Thickness(12, 0, 12, 6) };
        top.Children.Add(status);
        results = new ListBox
        {
            Classes = { "tree" },
            Height = 26 * 10,
            ItemTemplate = new FuncDataTemplate<SearchMatch>((match, _) => match is null ? new TextBlock() : Row(match), supportsRecycling: false),
        };
        results.SelectionChanged += (_, _) => ShowPreview();
        results.DoubleTapped += (_, _) => Choose();
        preview = new CodeEditor { IsReadOnly = true, ShowErrorStripe = false, Height = 180, Focusable = false };
        Border previewFrame = new() { BorderThickness = new Thickness(0, 1, 0, 0), Child = preview };
        previewFrame.Bind(Border.BorderBrushProperty, previewFrame.GetResourceObservable("Passenger.Divider"));
        Content = new Border
        {
            Classes = { "popup" },
            Child = new StackPanel { Children = { top, results, previewFrame } },
        };
        typing = new DispatcherTimer { Interval = Pause };
        typing.Tick += (_, _) =>
        {
            typing.Stop();
            searching = Refresh();
        };
        query.TextChanged += (_, _) => Retype();
        foreach (ToggleButton option in new[] { matchCase, words, regex })
        {
            option.IsCheckedChanged += (_, _) => Retype();
        }
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Dispatcher.UIThread.Post(() =>
        {
            query.Focus();
            query.SelectAll();
        }, DispatcherPriority.Loaded);
        if (query.Text is { Length: > 0 })
        {
            searching = Refresh();
        }
    }

    /// <summary>A match to go to, or null when dismissed.</summary>
    public event EventHandler<SearchMatch?>? Done;

    /// <summary>Replace All pressed: the query, its replacement, and the files it matched in.</summary>
    public event EventHandler<(SearchQuery Query, string Replacement, IReadOnlyList<SearchMatch> Matches)>? ReplaceRequested;

    public IReadOnlyList<SearchMatch> Matches => matches;

    public string Status => status.Text ?? string.Empty;

    public SearchQuery Query => new(query.Text ?? string.Empty, matchCase.IsChecked == true, words.IsChecked == true, regex.IsChecked == true);

    /// <summary>The matches' number as Rider says it: a thousand and more past the limit.</summary>
    private static string Count(int found) =>
        found >= FileSearch.MostMatches ? $"{FileSearch.MostMatches}+ matches" : found == 1 ? "1 match" : $"{found} matches";

    public string Text
    {
        get => query.Text ?? string.Empty;
        set => query.Text = value;
    }

    public string Replacement
    {
        get => replacement?.Text ?? string.Empty;
        set
        {
            if (replacement is not null)
            {
                replacement.Text = value;
            }
        }
    }

    public bool MatchCase
    {
        get => matchCase.IsChecked == true;
        set => matchCase.IsChecked = value;
    }

    public bool Words
    {
        get => words.IsChecked == true;
        set => words.IsChecked = value;
    }

    public bool Regex
    {
        get => regex.IsChecked == true;
        set => regex.IsChecked = value;
    }

    /// <summary>Searches now, as a pause in typing does.</summary>
    public Task SearchAsync()
    {
        typing.Stop();
        searching = Refresh();
        return searching;
    }

    /// <summary>The search under way, if any (for a test to wait on).</summary>
    public Task Searching => searching ?? Task.CompletedTask;

    public void RequestReplace()
    {
        if (matches.Count > 0)
        {
            ReplaceRequested?.Invoke(this, (Query, Replacement, matches));
        }
    }

    private void Retype()
    {
        typing.Stop();
        typing.Start();
    }

    private async Task Refresh()
    {
        asking?.Cancel();
        CancellationTokenSource cancel = asking = new();
        SearchQuery asked = Query;
        if (asked.Text.Length == 0)
        {
            Show([], string.Empty);
            return;
        }
        if (FileSearch.Problem(asked) is { } problem)
        {
            Show([], problem);
            return;
        }
        status.Text = "Searching...";
        try
        {
            IReadOnlyList<SearchMatch> found = await search(asked, cancel.Token);
            if (cancel.IsCancellationRequested)
            {
                return;
            }
            int files = found.Select(m => m.Path).Distinct().Count();
            Show(found, found.Count == 0 ? "Nothing found" : $"{Count(found.Count)} in {files} file{(files == 1 ? "" : "s")}");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Show(IReadOnlyList<SearchMatch> found, string said)
    {
        matches = found;
        results.ItemsSource = found;
        results.SelectedIndex = found.Count > 0 ? 0 : -1;
        status.Text = said;
        ShowPreview();
    }

    private void ShowPreview()
    {
        if (results.SelectedItem is not SearchMatch match || textOf(match.Path) is not { } text)
        {
            preview.Show(new TextDocument(), SelectionSet.Single(0), default);
            return;
        }
        TextDocument shown = new(text.ToString());
        preview.Show(shown, SelectionSet.Single(0), default);
        (preview.Highlighter as IDisposable)?.Dispose();
        preview.Highlighter = match.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? new Languages.CSharp.CSharpHighlighter(shown) : null;
        if (match.Line < shown.Snapshot.LineCount)
        {
            int at = shown.Snapshot.GetLineStart(match.Line) + match.Column;
            preview.SetHighlights([new TextRange(at, match.Length)], 0);
            preview.GoTo(new TextPosition(match.Line, match.Column));
        }
    }

    private void Choose()
    {
        if (results.SelectedItem is SearchMatch match)
        {
            Done?.Invoke(this, match);
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.C or Key.O or Key.X)
        {
            ToggleButton option = e.Key == Key.C ? matchCase : e.Key == Key.O ? words : regex;
            option.IsChecked = option.IsChecked != true;
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Escape:
                Done?.Invoke(this, null);
                e.Handled = true;
                break;
            case Key.Enter when replacement?.IsFocused != true:
                Choose();
                e.Handled = true;
                break;
            case Key.Down or Key.Up when matches.Count > 0:
                results.SelectedIndex = Math.Clamp(results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, matches.Count - 1);
                results.ScrollIntoView(results.SelectedIndex);
                e.Handled = true;
                break;
        }
    }

    private static ToggleButton Option(string label, string tip)
    {
        ToggleButton button = new() { Classes = { "option" }, Content = label, FontSize = 11 };
        ToolTip.SetTip(button, tip);
        return button;
    }

    /// <summary>A match's line, its leading space trimmed and the match itself bold, and where it is at the right.</summary>
    private static Control Row(SearchMatch match)
    {
        string line = match.LineText;
        int indent = line.Length - line.TrimStart().Length;
        int start = Math.Clamp(match.Column - indent, 0, Math.Max(0, line.Length - indent));
        string trimmed = line[indent..];
        int length = Math.Min(match.Length, trimmed.Length - start);
        TextBlock text = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = CSharp.Fonts.Mono, FontSize = 12 };
        text.Inlines = [];
        text.Inlines.Add(new Avalonia.Controls.Documents.Run(trimmed[..start]));
        text.Inlines.Add(new Avalonia.Controls.Documents.Run(trimmed.Substring(start, Math.Max(0, length))) { FontWeight = FontWeight.Bold });
        text.Inlines.Add(new Avalonia.Controls.Documents.Run(trimmed[Math.Min(trimmed.Length, start + Math.Max(0, length))..]));
        TextBlock where = new() { Text = $"{match.Name} {match.Line + 1}", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0), FontSize = 12 };
        where.Bind(TextBlock.ForegroundProperty, where.GetResourceObservable("Passenger.TextMuted"));
        DockPanel row = new() { Height = 24, Margin = new Thickness(8, 0) };
        DockPanel.SetDock(where, Dock.Right);
        row.Children.Add(where);
        row.Children.Add(text);
        ToolTip.SetTip(row, match.Path);
        return row;
    }
}
