using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Editors;

/// <summary>A tab of an editor group: a file, and whether it is the one its group shows.</summary>
public sealed class EditorTab(OpenDocument document) : Observable
{
    private bool isActive;

    public OpenDocument Document { get; } = document;

    public bool IsActive
    {
        get => isActive;
        set => Set(ref isActive, value);
    }
}

/// <summary>One editor of a split, as Rider's: its tabs, its find bar and its editor, which shows the active tab's
/// file. A file may be open in two groups at once (Split Right on it), each with its own carets and scroll over the
/// one text.</summary>
public sealed class EditorGroup : DockPanel
{
    private readonly EditorArea area;
    private readonly ObservableCollection<EditorTab> tabs = [];
    private readonly Dictionary<OpenDocument, (SelectionSet Selections, Vector Scroll)> views = [];
    private readonly Border tabBar;
    private readonly Border banner;
    private readonly TextBlock bannerText;
    private EditorTab? active;

    public EditorGroup(EditorArea area)
    {
        this.area = area;
        Editor = new CodeEditor { IsVisible = false };
        Find = new FindBar { [DockProperty] = Dock.Top };
        ItemsControl strip = new()
        {
            ItemsSource = tabs,
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal }),
            ItemTemplate = new FuncDataTemplate<EditorTab>((tab, _) => Tab(tab), supportsRecycling: false),
        };
        tabBar = new Border
        {
            [DockProperty] = Dock.Top,
            Height = 36,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = strip },
        };
        tabBar.Bind(Border.BackgroundProperty, tabBar.GetResourceObservable("Passenger.Chrome"));
        tabBar.Bind(Border.BorderBrushProperty, tabBar.GetResourceObservable("Passenger.Divider"));
        // Rider's editor notification: what a file is when it is not the project's own.
        bannerText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0), FontSize = 12 };
        banner = new Border { [DockProperty] = Dock.Top, Height = 28, IsVisible = false, BorderThickness = new Thickness(0, 0, 0, 1), Child = bannerText };
        banner.Bind(Border.BackgroundProperty, banner.GetResourceObservable("Passenger.Banner"));
        banner.Bind(Border.BorderBrushProperty, banner.GetResourceObservable("Passenger.Divider"));
        Children.Add(tabBar);
        Children.Add(banner);
        Children.Add(Find);
        Children.Add(Editor);
        // The group the person works in is the area's active one.
        Editor.GotFocus += (_, _) => area.SetActiveGroup(this);
        AddHandler(PointerPressedEvent, (_, _) => area.SetActiveGroup(this), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Whether this is the group the person works in; the others mark their file quieter.</summary>
    public bool IsCurrent
    {
        get => isCurrent;
        set
        {
            isCurrent = value;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool isCurrent = true;

    private event EventHandler? CurrentChanged;

    public CodeEditor Editor { get; }
    public FindBar Find { get; }
    public IReadOnlyList<EditorTab> Tabs => tabs;
    public OpenDocument? Active => active?.Document;
    public bool Has(OpenDocument document) => tabs.Any(t => t.Document == document);

    /// <summary>The strip of tabs, where a dragged tab drops to go into this group at a place.</summary>
    public Control TabStrip => tabBar;

    /// <summary>Adds a file's tab after the active one (or at an index), without showing it.</summary>
    public void Add(OpenDocument document, int? index = null)
    {
        if (Has(document))
        {
            return;
        }
        int at = index ?? (active is null ? tabs.Count : tabs.IndexOf(active) + 1);
        tabs.Insert(Math.Clamp(at, 0, tabs.Count), new EditorTab(document));
    }

    /// <summary>Shows a file's tab, keeping the carets and scroll of the one it replaces.</summary>
    public void Show(OpenDocument document)
    {
        Add(document);
        if (active?.Document == document && Editor.IsVisible)
        {
            return;
        }
        Keep();
        if (active is not null)
        {
            active.IsActive = false;
            active.Document.PropertyChanged -= OnDocumentChanged;
        }
        active = tabs.First(t => t.Document == document);
        active.IsActive = true;
        document.PropertyChanged += OnDocumentChanged;
        (SelectionSet selections, Vector scroll) = views.TryGetValue(document, out var view) ? view : (document.Selections, document.Scroll);
        Editor.LineCommentPrefix = document.Language.LineComment;
        Editor.Indent = new IndentOptions(document.Language.IndentSize);
        Editor.Show(document.Text, selections, scroll);
        Editor.Highlighter = document.Highlighter;
        Editor.SetMarkers(document.Markers);
        Editor.SetInlayHints(document.InlayHints);
        Editor.SetLineMarks(document.LineMarks);
        Editor.SetLineAnnotations(document.LineAnnotations);
        Editor.SetTestMarks(document.TestMarks);
        Editor.SetChangeMarks(document.ChangeMarks);
        Editor.SetGutterAnnotations(document.GutterAnnotations);
        Editor.SetBookmarks(document.Bookmarks);
        Editor.IsReadOnly = document.IsDecompiled;
        banner.IsVisible = document.IsDecompiled;
        bannerText.Text = document.Origin;
        Find.Attach(Editor);
        Editor.IsVisible = true;
        tabBar.IsVisible = true;
    }

    /// <summary>Takes a file's tab away; the group shows its neighbour, or nothing when it was the last.</summary>
    public void Remove(OpenDocument document)
    {
        if (tabs.FirstOrDefault(t => t.Document == document) is not { } tab)
        {
            return;
        }
        int index = tabs.IndexOf(tab);
        views.Remove(document);
        tabs.Remove(tab);
        if (tab == active)
        {
            document.PropertyChanged -= OnDocumentChanged;
            active = null;
            if (tabs.Count > 0)
            {
                Show(tabs[Math.Min(index, tabs.Count - 1)].Document);
            }
            else
            {
                Find.Close();
                Editor.IsVisible = false;
                tabBar.IsVisible = false;
                banner.IsVisible = false;
            }
        }
    }

    /// <summary>Moves a tab along the strip.</summary>
    public void Reorder(OpenDocument document, int index)
    {
        if (tabs.FirstOrDefault(t => t.Document == document) is { } tab)
        {
            int from = tabs.IndexOf(tab);
            int to = Math.Clamp(index > from ? index - 1 : index, 0, tabs.Count - 1);
            tabs.Move(from, to);
        }
    }

    /// <summary>Where along the strip a point (in the strip's coordinates) falls, for a dropped tab.</summary>
    public int IndexAt(Point at)
    {
        int index = 0;
        foreach (Control tab in tabBar.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("tab")))
        {
            Point centre = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, 0), tabBar) ?? default;
            if (at.X > centre.X)
            {
                index++;
            }
        }
        return index;
    }

    /// <summary>The carets and scroll of the file shown, kept for when its tab is shown again.</summary>
    public void Keep()
    {
        if (active is not null)
        {
            views[active.Document] = (Editor.Selections, Editor.ScrollOffset);
            active.Document.Selections = Editor.Selections;
            active.Document.Scroll = Editor.ScrollOffset;
        }
    }

    public void Cycle(int step)
    {
        if (active is null || tabs.Count < 2)
        {
            return;
        }
        Show(tabs[(tabs.IndexOf(active) + step + tabs.Count) % tabs.Count].Document);
    }

    private void OnDocumentChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (active?.Document is not { } document || sender != document)
        {
            return;
        }
        switch (e.PropertyName)
        {
            case nameof(OpenDocument.Highlighter):
                Editor.Highlighter = document.Highlighter;
                break;
            case nameof(OpenDocument.LineMarks):
                Editor.SetLineMarks(document.LineMarks);
                break;
            case nameof(OpenDocument.LineAnnotations):
                Editor.SetLineAnnotations(document.LineAnnotations);
                break;
            case nameof(OpenDocument.Markers):
                Editor.SetMarkers(document.Markers);
                break;
            case nameof(OpenDocument.InlayHints):
                Editor.SetInlayHints(document.InlayHints);
                break;
            case nameof(OpenDocument.TestMarks):
                Editor.SetTestMarks(document.TestMarks);
                break;
            case nameof(OpenDocument.ChangeMarks):
                Editor.SetChangeMarks(document.ChangeMarks);
                break;
            case nameof(OpenDocument.GutterAnnotations):
                Editor.SetGutterAnnotations(document.GutterAnnotations);
                break;
            case nameof(OpenDocument.Bookmarks):
                Editor.SetBookmarks(document.Bookmarks);
                break;
        }
    }

    private Control Tab(EditorTab? tab)
    {
        if (tab is null)
        {
            return new TextBlock();
        }
        OpenDocument document = tab.Document;
        PathIcon icon = new() { Data = document.Icon, Foreground = document.IconBrush };
        TextBlock name = new() { Classes = { "tabName" }, Text = document.Name, VerticalAlignment = VerticalAlignment.Center };
        Ellipse modified = new() { Classes = { "modified" }, Width = 7, Height = 7, IsVisible = document.IsModified };
        PathIcon closeGlyph = new() { Classes = { "small" } };
        closeGlyph.Bind(PathIcon.DataProperty, closeGlyph.GetResourceObservable("Icon.Close"));
        Button close = new() { Classes = { "tabClose" }, Content = closeGlyph };
        ToolTip.SetTip(close, "Close");
        close.Click += (_, e) =>
        {
            _ = area.CloseTab(this, document);
            e.Handled = true;
        };
        Border border = new()
        {
            Classes = { "tab" },
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { icon, name, new Panel { Width = 16, Height = 16, Children = { modified, close } } },
            },
        };
        ToolTip.SetTip(border, document.Path);
        border.Classes.Set("active", tab.IsActive);
        border.Classes.Set("elsewhere", !IsCurrent);
        CurrentChanged += (_, _) => border.Classes.Set("elsewhere", !IsCurrent);
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorTab.IsActive))
            {
                border.Classes.Set("active", tab.IsActive);
            }
        };
        document.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OpenDocument.IsModified))
            {
                modified.IsVisible = document.IsModified;
            }
        };
        border.ContextMenu = TabMenu(document);
        border.PointerPressed += (_, e) =>
        {
            PointerPointProperties pressed = e.GetCurrentPoint(this).Properties;
            if (pressed.IsMiddleButtonPressed)
            {
                _ = area.CloseTab(this, document);
                e.Handled = true;
            }
            else if (pressed.IsLeftButtonPressed)
            {
                area.SetActiveGroup(this);
                Show(document);
                area.Activated(this);
                area.BeginTabDrag(this, document, e);
                e.Pointer.Capture(border);
                e.Handled = true;
            }
        };
        border.PointerMoved += (_, e) => area.ContinueTabDrag(e);
        border.PointerReleased += (_, e) =>
        {
            area.EndTabDrag(e);
            e.Pointer.Capture(null);
        };
        return border;
    }

    /// <summary>Rider's tab menu: close it, the others or all, and split or move it.</summary>
    private ContextMenu TabMenu(OpenDocument document)
    {
        ContextMenu menu = new();
        MenuItem Item(string header, Action run, string? action = null)
        {
            MenuItem item = new() { Header = header };
            if (action is not null && AppActions.Keymap().ShortcutsFor(action).FirstOrDefault() is { } keys)
            {
                item.InputGesture = null;
                item.Header = $"{header}    {keys.Display()}";
            }
            item.Click += (_, _) => run();
            return item;
        }
        menu.Items.Add(Item("Close", () => _ = area.CloseTab(this, document), AppActions.CloseTab));
        menu.Items.Add(Item("Close Other Tabs", () => _ = area.CloseOthers(this, document)));
        menu.Items.Add(Item("Close All Tabs", () => _ = area.CloseAllTabs(this)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Split Right", () => area.Split(this, document, Orientation.Horizontal, move: false), AppActions.SplitRight));
        menu.Items.Add(Item("Split Down", () => area.Split(this, document, Orientation.Vertical, move: false), AppActions.SplitDown));
        menu.Items.Add(Item("Move to Opposite Group", () => area.MoveToOpposite(this, document), AppActions.MoveToOppositeGroup));
        return menu;
    }
}
