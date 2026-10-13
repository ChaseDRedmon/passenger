using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Passenger.Core;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App;

/// <summary>The status bar's path, as Rider's new UI has it: the active file's folders and name, then the type and
/// member the caret is in, from the language server's outline (asked again as editing pauses); a click on a member goes
/// to it, on the file or a folder shows it in the Explorer.</summary>
public partial class MainWindow
{
    private readonly StackPanel breadcrumbs = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 18, 0) };
    private IReadOnlyList<DocumentSymbol> outline = [];
    private string? outlined;
    private DispatcherTimer? outlineAgain;
    private int outlining;

    /// <summary>The path shown, part by part.</summary>
    public IReadOnlyList<string> Breadcrumbs => [.. breadcrumbs.Children.OfType<Button>().Select(b => b.Content as string ?? string.Empty)];

    private void AddBreadcrumbs()
    {
        BreadcrumbHost.Children.Add(breadcrumbs);
        outlineAgain = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        outlineAgain.Tick += (_, _) =>
        {
            outlineAgain.Stop();
            _ = OutlineAsync();
        };
        Editors.ActiveChanged += (_, _) =>
        {
            outline = [];
            outlined = null;
            ShowBreadcrumbs();
            outlineAgain.Stop();
            outlineAgain.Start();
        };
        Editors.CaretMoved += (_, _) => ShowBreadcrumbs();
        Editors.Opened += (_, document) => document.Text.Changed += (_, _) =>
        {
            if (Editors.Active == document)
            {
                outlineAgain.Stop();
                outlineAgain.Start();
            }
        };
    }

    private async Task OutlineAsync()
    {
        int asked = ++outlining;
        if (Editors.Active is not { } document || csharp?.For(document)?.Lsp is not { } lsp)
        {
            return;
        }
        try
        {
            IReadOnlyList<DocumentSymbol> found = await lsp.OutlineAsync();
            if (asked == outlining && Editors.Active == document)
            {
                outline = found;
                outlined = document.Path;
                ShowBreadcrumbs();
            }
        }
        catch (LanguageServerFailed)
        {
            // The path keeps the file's own parts; the next edit asks again.
        }
    }

    private void ShowBreadcrumbs()
    {
        breadcrumbs.Children.Clear();
        if (Editors.Active is not { } document)
        {
            return;
        }
        string relative = folder is { } root && PathNames.IsUnder(document.Path, root)
            ? Path.GetRelativePath(root, document.Path)
            : Path.GetFileName(document.Path);
        string[] parts = relative.Split(Path.DirectorySeparatorChar);
        // The last two folders: a deep path would push the rest of the status bar off.
        for (int i = Math.Max(0, parts.Length - 3); i < parts.Length - 1; i++)
        {
            string at = Path.Combine([folder ?? string.Empty, .. parts[..(i + 1)]]);
            Crumb(parts[i], () => Explorer.Reveal(at), muted: true);
        }
        Crumb(parts[^1], () => Explorer.Reveal(document.Path), muted: false);
        if (outlined != document.Path)
        {
            return;
        }
        ServerLines lines = ServerLines.Of(document.Text.Snapshot);
        int line = lines.PositionOf(Editors.CodeEditor.Selections.Primary.Caret).Line;
        IReadOnlyList<DocumentSymbol> level = outline;
        while (level.FirstOrDefault(s => s.Range.Start.Line <= line && line <= s.Range.End.Line) is { } symbol)
        {
            // The namespace is the file's own context; the path names what is in it.
            if (symbol.Kind != 3)
            {
                string name = symbol.Name.Split(" : ")[0];
                TextPosition target = lines.Text.GetPosition(lines.OffsetOf(symbol.SelectionRange.Start));
                Crumb(name, () => NavigateTo(document.Path, target), muted: false);
            }
            level = symbol.Children ?? [];
        }
    }

    private void Crumb(string text, Action open, bool muted)
    {
        if (breadcrumbs.Children.Count > 0)
        {
            TextBlock between = new() { Text = "›", Classes = { "status" }, Margin = new Avalonia.Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center };
            breadcrumbs.Children.Add(between);
        }
        Button crumb = new() { Content = text, Classes = { "crumb" }, Opacity = muted ? 0.75 : 1 };
        crumb.Click += (_, _) => open();
        breadcrumbs.Children.Add(crumb);
    }
}
