using System.Collections.ObjectModel;
using Avalonia;
using Passenger.Dap;

namespace Passenger.App.Debugging;

/// <summary>A row of the Variables view: a local, a member, an element, a watch or an evaluated
/// result. One with a reference opens into its children, asked of the debugger the first time.</summary>
public sealed class VariableNode(string name, string value, string? type, int reference, int depth, VariableRole role) : Observable
{
    private bool isExpanded;

    public string Name { get; } = name;
    public string Value { get; } = value;
    public string? Type { get; } = type;
    public int Reference { get; } = reference;
    public int Depth { get; } = depth;
    public VariableRole Role { get; } = role;
    public bool HasChildren => Reference > 0;

    /// <summary>Rider's form: the type in braces before the value, unless the value already says it.</summary>
    public string TypeLabel => Type is { Length: > 0 } t && Value != t && !Value.StartsWith(t, StringComparison.Ordinal) ? $"{{{t}}} " : string.Empty;

    public Thickness Indent => new(Depth * 16, 0, 0, 0);
    public double ChevronAngle => IsExpanded ? 90 : 0;
    public List<VariableNode>? Children { get; set; }

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (Set(ref isExpanded, value))
            {
                Raise(nameof(ChevronAngle));
            }
        }
    }

    public static VariableNode From(DapVariable variable, int depth) =>
        new(variable.Name, variable.Value, variable.Type, variable.VariablesReference, depth,
            variable.PresentationHint?.Attributes?.Contains("failedEvaluation") == true ? VariableRole.Error : VariableRole.Value);
}

public enum VariableRole
{
    Value,
    Watch,
    Result,
    Error,
}

/// <summary>The Variables view's rows, flattened to what is open so a virtualizing list draws only
/// what is on screen; a node's children are fetched when it first opens.</summary>
public sealed class VariablesTree
{
    public ObservableCollection<VariableNode> Rows { get; } = [];

    /// <summary>Asks the debugger for a reference's children: the session's, while one runs.</summary>
    public Func<int, Task<IReadOnlyList<DapVariable>>>? Load { get; set; }

    public void Show(IEnumerable<VariableNode> top)
    {
        Rows.Clear();
        foreach (VariableNode node in top)
        {
            Rows.Add(node);
        }
    }

    public Task Toggle(VariableNode node) => node.IsExpanded ? Collapse(node) : Expand(node);

    public async Task Expand(VariableNode node)
    {
        if (!node.HasChildren || node.IsExpanded || (node.Children is null && Load is null))
        {
            return;
        }
        if (node.Children is null)
        {
            try
            {
                node.Children = [.. (await Load!(node.Reference)).Select(v => VariableNode.From(v, node.Depth + 1))];
            }
            catch (DapException error)
            {
                node.Children = [new VariableNode("error", error.Reason, null, 0, node.Depth + 1, VariableRole.Error)];
            }
        }
        int at = Rows.IndexOf(node);
        if (at < 0)
        {
            return;
        }
        node.IsExpanded = true;
        foreach (VariableNode child in node.Children)
        {
            Rows.Insert(++at, child);
        }
    }

    public Task Collapse(VariableNode node)
    {
        if (!node.IsExpanded)
        {
            return Task.CompletedTask;
        }
        node.IsExpanded = false;
        int at = Rows.IndexOf(node) + 1;
        while (at < Rows.Count && Rows[at].Depth > node.Depth)
        {
            Rows[at].IsExpanded = false;
            Rows.RemoveAt(at);
        }
        return Task.CompletedTask;
    }
}
