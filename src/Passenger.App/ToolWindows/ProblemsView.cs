using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.App.Run;

namespace Passenger.App.ToolWindows;

/// <summary>The build's problems, errors first: a severity icon, the message, and where (file, line,
/// column, project). A double-click or Enter goes there.</summary>
public sealed class ProblemsView : DockPanel
{
    private readonly ListBox list;
    private readonly TextBlock empty;

    public ProblemsView()
    {
        list = new ListBox
        {
            Classes = { "tree" },
            ItemsSource = Problems,
            ItemTemplate = new FuncDataTemplate<Problem>((problem, _) => Row(problem), supportsRecycling: false),
        };
        empty = new TextBlock { Text = "No problems", Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Children.Add(new Panel { Children = { empty, list } });
        list.DoubleTapped += (_, _) => Choose();
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Choose();
                e.Handled = true;
            }
        };
        Problems.CollectionChanged += (_, _) => empty.IsVisible = Problems.Count == 0;
    }

    /// <summary>A problem chosen to go to.</summary>
    public event EventHandler<Problem>? Chosen;

    public ObservableCollection<Problem> Problems { get; } = [];

    public void Show(IEnumerable<Problem> problems)
    {
        Problems.Clear();
        foreach (Problem problem in problems.OrderBy(p => p.Severity).ThenBy(p => p.Path).ThenBy(p => p.Position.Line))
        {
            Problems.Add(problem);
        }
    }

    private void Choose()
    {
        if (list.SelectedItem is Problem problem)
        {
            Chosen?.Invoke(this, problem);
        }
    }

    private static Control Row(Problem? problem)
    {
        if (problem is null)
        {
            return new TextBlock();
        }
        PathIcon icon = new() { Margin = new Thickness(0, 0, 8, 0) };
        string colour = problem.Severity == ProblemSeverity.Error ? "Passenger.Editor.Error" : "Passenger.Editor.Warning";
        icon.Bind(PathIcon.DataProperty, icon.GetResourceObservable(problem.Severity == ProblemSeverity.Error ? "Icon.Error" : "Icon.Problems"));
        icon.Bind(PathIcon.ForegroundProperty, icon.GetResourceObservable(colour));
        TextBlock where = new() { Text = $"{problem.Where}  {problem.Code}", FontSize = 12, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        where.Bind(TextBlock.ForegroundProperty, where.GetResourceObservable("Passenger.TextMuted"));
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 24,
            Children =
            {
                icon,
                new TextBlock { Text = problem.Message, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 900 },
                where,
            },
        };
    }
}
