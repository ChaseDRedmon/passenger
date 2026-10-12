using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.App.Run;

namespace Passenger.App.ToolWindows;

/// <summary>Which of Problems' tabs: the current file's, the solution's (with solution-wide analysis on), or the last
/// build's.</summary>
public enum ProblemsTab
{
    File,
    Solution,
    Build,
}

/// <summary>A row of Problems: a group (a project or a file, with its count) or a problem, at its depth.</summary>
public sealed record ProblemRow(int Depth, string Label, string? Detail, Problem? Problem);

/// <summary>Rider's Problems window: the current file's problems as they are typed, the whole solution's with
/// solution-wide analysis on (by project, then file), and the last build's apart from both, each a tab with its count.
/// Errors come before warnings; a double-click or Enter goes to a problem.</summary>
public sealed class ProblemsView : DockPanel
{
    private readonly ObservableCollection<ProblemRow> rows = [];
    private readonly ListBox list;
    private readonly TextBlock empty;
    private readonly Dictionary<ProblemsTab, ToggleButton> tabs = [];
    private IReadOnlyList<Problem> file = [];
    private IReadOnlyList<Problem>? solution;
    private string? fileName;

    public ProblemsView()
    {
        StackPanel strip = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (ProblemsTab tab in Enum.GetValues<ProblemsTab>())
        {
            ToggleButton button = new() { Classes = { "panelTab" } };
            button.Click += (_, _) => Shown = tab;
            tabs[tab] = button;
            strip.Children.Add(button);
        }
        Border bar = new() { [DockProperty] = Dock.Top, Height = 32, BorderThickness = new Thickness(0, 0, 0, 1), Child = strip };
        bar.Bind(Border.BorderBrushProperty, bar.GetResourceObservable("Passenger.Divider"));
        Children.Add(bar);
        list = new ListBox
        {
            Classes = { "tree" },
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<ProblemRow>((row, _) => Row(row), supportsRecycling: false),
        };
        empty = new TextBlock { Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Children.Add(new Panel { Children = { empty, list } });
        list.DoubleTapped += (_, _) => Choose();
        list.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Choose();
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ListMenus.Attach(list, () => list.SelectedItem is not ProblemRow { Problem: { } problem } ? [] :
        [
            ListMenus.Item("Go to", "F4", Choose, problem.Path is not null),
            ListMenus.Item("Copy Message", null, () => _ = ListMenus.CopyAsync(this, problem.Message)),
            new Separator(),
            ListMenus.Item("Show Context Actions", null, () => FixRequested?.Invoke(this, problem), problem.Path is not null),
        ]);
        Refresh();
    }

    /// <summary>A problem's fixes asked for (Rider's Alt+Enter on it): the editor goes there and offers them.</summary>
    public event EventHandler<Problem>? FixRequested;

    /// <summary>A problem chosen to go to.</summary>
    public event EventHandler<Problem>? Chosen;

    /// <summary>The last build's problems.</summary>
    public ObservableCollection<Problem> Problems { get; } = [];

    /// <summary>The current file's problems.</summary>
    public IReadOnlyList<Problem> FileProblems => file;

    /// <summary>The solution's problems, or null with solution-wide analysis off.</summary>
    public IReadOnlyList<Problem>? SolutionProblems => solution;

    /// <summary>The rows of the tab shown.</summary>
    public IReadOnlyList<ProblemRow> Rows => rows;

    /// <summary>The tab shown.</summary>
    public ProblemsTab Shown
    {
        get;
        set
        {
            field = value == ProblemsTab.Solution && solution is null ? ProblemsTab.File : value;
            Refresh();
        }
    }

    /// <summary>The last build's problems, in the Build tab, shown.</summary>
    public void Show(IEnumerable<Problem> problems)
    {
        Problems.Clear();
        foreach (Problem problem in Ordered(problems))
        {
            Problems.Add(problem);
        }
        Shown = ProblemsTab.Build;
    }

    /// <summary>The current file's problems, as they are typed.</summary>
    public void ShowFile(string? name, IReadOnlyList<Problem> problems)
    {
        fileName = name;
        file = [.. Ordered(problems)];
        Refresh();
    }

    /// <summary>The solution's problems, or null to take the tab away when solution-wide analysis goes off.</summary>
    public void ShowSolution(IReadOnlyList<Problem>? problems)
    {
        solution = problems is null ? null : [.. Ordered(problems)];
        if (solution is null && Shown == ProblemsTab.Solution)
        {
            Shown = ProblemsTab.File;
            return;
        }
        Refresh();
    }

    private static IEnumerable<Problem> Ordered(IEnumerable<Problem> problems) =>
        problems.OrderBy(p => p.Severity).ThenBy(p => p.Path, StringComparer.Ordinal).ThenBy(p => p.Position.Line).ThenBy(p => p.Position.Column);

    private void Refresh()
    {
        tabs[ProblemsTab.File].Content = Titled(fileName is null ? "File" : $"File: {fileName}", file.Count);
        tabs[ProblemsTab.Solution].Content = Titled("Solution", solution?.Count ?? 0);
        tabs[ProblemsTab.Solution].IsVisible = solution is not null;
        tabs[ProblemsTab.Build].Content = Titled("Build", Problems.Count);
        foreach ((ProblemsTab tab, ToggleButton button) in tabs)
        {
            button.IsChecked = tab == Shown;
        }
        rows.Clear();
        switch (Shown)
        {
            case ProblemsTab.File:
                foreach (Problem problem in file)
                {
                    rows.Add(new ProblemRow(0, problem.Message, problem.Where, problem));
                }
                empty.Text = fileName is null ? "No file is open" : "No problems in this file";
                break;
            case ProblemsTab.Solution:
                Grouped(solution ?? []);
                empty.Text = "No problems in the solution";
                break;
            default:
                Grouped(Problems);
                empty.Text = "No problems from the last build";
                break;
        }
        empty.IsVisible = rows.Count == 0;

        static string Titled(string title, int count) => count == 0 ? title : $"{title}  {count}";
    }

    /// <summary>Problems under their projects and files, as Rider groups the solution's.</summary>
    private void Grouped(IEnumerable<Problem> problems)
    {
        foreach (IGrouping<string, Problem> project in problems.GroupBy(p => p.Project ?? "Solution").OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            rows.Add(new ProblemRow(0, project.Key, Count(project.Count()), null));
            foreach (IGrouping<string, Problem> path in project.GroupBy(p => p.Path ?? string.Empty).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                if (path.Key.Length > 0)
                {
                    rows.Add(new ProblemRow(1, System.IO.Path.GetFileName(path.Key), Count(path.Count()), null));
                }
                foreach (Problem problem in path)
                {
                    rows.Add(new ProblemRow(path.Key.Length > 0 ? 2 : 1, problem.Message, problem.Where, problem));
                }
            }
        }

        static string Count(int count) => count == 1 ? "1 problem" : $"{count} problems";
    }

    private void Choose()
    {
        if (list.SelectedItem is ProblemRow { Problem: { } problem })
        {
            Chosen?.Invoke(this, problem);
        }
    }

    private static Control Row(ProblemRow? row)
    {
        if (row is null)
        {
            return new TextBlock();
        }
        StackPanel panel = new() { Orientation = Orientation.Horizontal, Height = 24, Margin = new Thickness(8 + (row.Depth * 16), 0, 8, 0) };
        TextBlock detail = new() { Text = row.Detail, FontSize = 12, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        detail.Bind(TextBlock.ForegroundProperty, detail.GetResourceObservable("Passenger.TextMuted"));
        if (row.Problem is not { } problem)
        {
            panel.Children.Add(new TextBlock { Text = row.Label, FontWeight = row.Depth == 0 ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(detail);
            return panel;
        }
        PathIcon icon = new() { Margin = new Thickness(0, 0, 8, 0) };
        icon.Bind(PathIcon.DataProperty, icon.GetResourceObservable(problem.Severity == ProblemSeverity.Error ? "Icon.Error" : "Icon.Problems"));
        icon.Bind(PathIcon.ForegroundProperty, icon.GetResourceObservable(problem.Severity == ProblemSeverity.Error ? "Passenger.Editor.Error" : "Passenger.Editor.Warning"));
        detail.Text = problem.Code is { Length: > 0 } code ? $"{row.Detail}  {code}" : row.Detail;
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock { Text = row.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 900 });
        panel.Children.Add(detail);
        return panel;
    }
}
