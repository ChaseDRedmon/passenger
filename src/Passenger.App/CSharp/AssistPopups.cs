using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Passenger.App.CSharp;

/// <summary>A card over the editor: hover information, a diagnostic's message, a signature. Code is
/// set in the editor's font, prose in the interface's, a problem in its severity's colour.</summary>
internal sealed class InfoCard : Border
{
    // Styled as the Border it is, so the theme's Border.popup reaches it.
    protected override Type StyleKeyOverride => typeof(Border);

    private readonly StackPanel parts = new() { Spacing = 6 };

    public InfoCard()
    {
        Classes.Add("popup");
        Padding = new Thickness(10, 8);
        MaxWidth = 640;
        Child = parts;
        IsHitTestVisible = false;
    }

    public bool IsEmpty => parts.Children.Count == 0;

    /// <summary>What the card says, a line a part.</summary>
    public string Text => string.Join('\n', parts.Children.OfType<TextBlock>().Select(t => t.Text));

    public void Clear() => parts.Children.Clear();

    public void AddCode(string code) => parts.Children.Add(new TextBlock
    {
        Text = code,
        FontFamily = Fonts.Mono,
        FontSize = 12.5,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("Passenger.Text"),
    });

    public void AddText(string text, IBrush? colour = null) => parts.Children.Add(new TextBlock
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12.5,
        Foreground = colour ?? Brush("Passenger.TextMuted"),
    });

    /// <summary>Hover markdown, as Roslyn writes it: fenced code blocks and prose between them.</summary>
    public void AddMarkdown(string markdown)
    {
        bool code = false;
        List<string> block = [];
        foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                Flush(block, code);
                code = !code;
                continue;
            }
            block.Add(code ? raw : Plain(raw));
        }
        Flush(block, code);
    }

    private void Flush(List<string> block, bool code)
    {
        string text = string.Join('\n', block).Trim('\n', ' ');
        block.Clear();
        if (text.Length == 0)
        {
            return;
        }
        if (code)
        {
            AddCode(text);
        }
        else
        {
            AddText(text);
        }
    }

    /// <summary>Markdown's emphasis and escapes taken out, leaving the words.</summary>
    private static string Plain(string line) => line
        .Replace("**", string.Empty)
        .Replace("__", string.Empty)
        .Replace("`", string.Empty)
        .Replace("\\", string.Empty)
        .Replace("&nbsp;", " ")
        .Replace("&lt;", "<")
        .Replace("&gt;", ">")
        .Replace("&amp;", "&");

    internal static IBrush Brush(string key) =>
        Application.Current is { } app && app.TryFindResource(key, app.ActualThemeVariant, out object? value) && value is IBrush brush ? brush : Brushes.Gray;
}

/// <summary>One completion row: the badge for its kind (a coloured letter, as Rider's are), its
/// label and the detail the server gives (a type, a namespace).</summary>
internal sealed record CompletionRow(Lsp.CompletionItem Item, string Letter, IBrush Badge, string Detail)
{
    public string Label => Item.Label + (Item.LabelDetails?.Detail ?? string.Empty);
}

/// <summary>The completion list: rows under the caret, the selected one highlighted, ten at a time.</summary>
internal sealed class CompletionPopup : Border
{
    // Styled as the Border it is, so the theme's Border.popup reaches it.
    protected override Type StyleKeyOverride => typeof(Border);

    public CompletionPopup()
    {
        Classes.Add("popup");
        MinWidth = 280;
        MaxWidth = 620;
        Padding = new Thickness(0, 4);
        List = new ListBox
        {
            Classes = { "tree" },
            MaxHeight = 26 * 10,
            ItemTemplate = new FuncDataTemplate<CompletionRow>((row, _) => Row(row), supportsRecycling: false),
        };
        Child = List;
    }

    public ListBox List { get; }

    public CompletionRow? Selected => List.SelectedItem as CompletionRow;

    public IReadOnlyList<CompletionRow> Rows => List.ItemsSource as IReadOnlyList<CompletionRow> ?? [];

    public void Show(IReadOnlyList<CompletionRow> rows)
    {
        List.ItemsSource = rows;
        List.SelectedIndex = rows.Count > 0 ? 0 : -1;
        if (rows.Count > 0)
        {
            List.ScrollIntoView(0);
        }
    }

    public void Move(int step)
    {
        int count = List.ItemCount;
        if (count == 0)
        {
            return;
        }
        List.SelectedIndex = Math.Clamp(List.SelectedIndex + step, 0, count - 1);
        List.ScrollIntoView(List.SelectedIndex);
    }

    private static Control Row(CompletionRow? row)
    {
        if (row is null)
        {
            return new TextBlock();
        }
        return new DockPanel
        {
            Height = 24,
            Children =
            {
                new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(3),
                    Background = row.Badge,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = row.Letter,
                        FontSize = 10,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.Black,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
                new TextBlock
                {
                    [DockPanel.DockProperty] = Dock.Right,
                    Text = row.Detail,
                    Foreground = InfoCard.Brush("Passenger.TextMuted"),
                    FontSize = 12,
                    Margin = new Thickness(12, 0, 4, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 200,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new TextBlock
                {
                    Text = row.Label,
                    FontFamily = Fonts.Mono,
                    FontSize = 12.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };
    }
}

/// <summary>A titled list to choose from (fixes, definitions, usages, surround templates): Up and Down, Enter to choose,
/// a choice's digit to choose it at once, Escape or a click away to dismiss.</summary>
internal sealed class ChoicePopup : Border
{
    // Styled as the Border it is, so the theme's Border.popup reaches it.
    protected override Type StyleKeyOverride => typeof(Border);

    private readonly ListBox list;

    /// <summary>What is offered, by its words.</summary>
    public IReadOnlyList<string> Titles { get; }

    public ChoicePopup(string title, IReadOnlyList<Choice> choices)
    {
        Titles = [.. choices.Select(c => c.Text)];
        bool numbered = choices.Any(c => c.Mnemonic is not null);
        Classes.Add("popup");
        MinWidth = 320;
        MaxWidth = 760;
        Padding = new Thickness(0, 6);
        list = new ListBox
        {
            Classes = { "tree" },
            MaxHeight = 26 * 14,
            ItemsSource = choices,
            SelectedIndex = choices.Count > 0 ? 0 : -1,
            ItemTemplate = new FuncDataTemplate<Choice>((choice, _) => new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Height = 24,
                Children =
                {
                    // Rider's digit before the choices that have one, a column of its own when any does.
                    new TextBlock
                    {
                        Text = choice?.Mnemonic?.ToString(),
                        Foreground = InfoCard.Brush("Passenger.TextMuted"),
                        FontFamily = Fonts.Mono,
                        Width = 10,
                        IsVisible = numbered,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock { Text = choice?.Text, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock
                    {
                        Text = choice?.Detail,
                        Foreground = InfoCard.Brush("Passenger.TextMuted"),
                        FontFamily = choice?.DetailIsCode == true ? Fonts.Mono : FontFamily.Default,
                        FontSize = 12,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 460,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            }, supportsRecycling: false),
        };
        Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = title, Classes = { "paneTitle" }, Margin = new Thickness(12, 2, 12, 2) },
                list,
            },
        };
        list.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        list.DoubleTapped += (_, _) => Choose();
        Dispatcher.UIThread.Post(() =>
        {
            list.Focus();
            if (list.ContainerFromIndex(Math.Max(0, list.SelectedIndex)) is Control first)
            {
                first.Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>The choice made, or null when dismissed.</summary>
    public event EventHandler<Choice?>? Done;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Choose();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Done?.Invoke(this, null);
            e.Handled = true;
        }
        else if (Digit(e.Key) is { } digit && list.ItemsSource is IEnumerable<Choice> choices && choices.FirstOrDefault(c => c.Mnemonic == digit) is { } marked)
        {
            Done?.Invoke(this, marked);
            e.Handled = true;
        }
    }

    private static char? Digit(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => (char)('0' + (key - Key.D0)),
        >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (key - Key.NumPad0)),
        _ => null,
    };

    private void Choose()
    {
        if (list.SelectedItem is Choice choice)
        {
            Done?.Invoke(this, choice);
        }
    }
}

/// <summary>One thing to choose: what it says, a quieter detail beside it, what it stands for, and the digit that
/// chooses it, if it has one.</summary>
internal sealed record Choice(string Text, string Detail, object Value, bool DetailIsCode = false, char? Mnemonic = null);

/// <summary>Rider's inline rename: the name in a box over itself, all of it selected; Enter renames,
/// Escape leaves it.</summary>
internal sealed class RenameBox : Border
{
    // Styled as the Border it is, so the theme's Border.popup reaches it.
    protected override Type StyleKeyOverride => typeof(Border);

    private readonly TextBox name;

    public RenameBox(string current)
    {
        Classes.Add("popup");
        Padding = new Thickness(8, 6);
        name = new TextBox { Text = current, Classes = { "search" }, MinWidth = 220, FontFamily = Fonts.Mono };
        Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "Rename", Classes = { "muted" }, FontSize = 11 },
                name,
            },
        };
        name.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Dispatcher.UIThread.Post(() =>
        {
            name.Focus();
            name.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>The new name, or null when cancelled.</summary>
    public event EventHandler<string?>? Done;

    /// <summary>The name as the box has it now.</summary>
    public string Text => name.Text ?? string.Empty;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string text = name.Text?.Trim() ?? string.Empty;
            Done?.Invoke(this, text.Length > 0 ? text : null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Done?.Invoke(this, null);
            e.Handled = true;
        }
    }
}

internal static class Fonts
{
    public static readonly FontFamily Mono = new("avares://Passenger.Editor/Fonts#JetBrains Mono");
}
