using System.Collections.ObjectModel;
using Avalonia;
using Passenger.Dap;

namespace Passenger.App.Debugging;

/// <summary>A row of the Variables view: a local, a member, an element, a watch or an evaluated
/// result. One with a reference opens into its children, asked of the debugger the first time.</summary>
public sealed class VariableNode(string name, string value, string? type, int reference, int depth, VariableRole role) : Observable
{
    private bool isExpanded;
    private bool isEditing;

    /// <summary>The row this one opened from, if any.</summary>
    public VariableNode? Parent { get; init; }

    /// <summary>What the debugger says names the value in an expression, where its name does not: a call's value
    /// ("Count() returned") is $ReturnValue.</summary>
    public string? EvaluateName { get; init; }

    /// <summary>A value a call of the last step returned, listed at the top of the frame's values as Rider lists them.</summary>
    public bool IsReturnValue => Depth == 0 && Role == VariableRole.Value && EvaluateName?.StartsWith("$ReturnValue", StringComparison.Ordinal) == true;

    /// <summary>The exception the program stopped at, $exception.</summary>
    public bool IsException => Depth == 0 && Role == VariableRole.Value && Name == "$exception";

    /// <summary>The C# that reaches this value from the frame, for Set Value: a local's name, a member after its
    /// object's expression, an element's index after its array's. Null for a row no expression reaches (a
    /// scope, the static members, a result).</summary>
    public string? Expression
    {
        get
        {
            if (Role == VariableRole.Watch)
            {
                return Name;
            }
            if (Role is VariableRole.Scope or VariableRole.Result or VariableRole.Error)
            {
                return null;
            }
            if (Parent is null || Parent.Role == VariableRole.Scope)
            {
                return EvaluateName ?? Name;
            }
            // The raw view lists the members of the object above it.
            string? owner = Parent.Name switch
            {
                "Static members" or "Results" => null,
                "Raw View" => Parent.Parent?.Expression,
                _ => Parent.Expression,
            };
            return owner is null ? null : Name.StartsWith('[') ? $"{owner}{Name}" : $"{owner}.{Name}";
        }
    }

    /// <summary>Its value is being typed over, in its row (Set Value, F2).</summary>
    public bool IsEditing
    {
        get => isEditing;
        set => Set(ref isEditing, value);
    }

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

    public static VariableNode From(DapVariable variable, int depth, VariableNode? parent = null) =>
        new(variable.Name, variable.Value, variable.Type, variable.VariablesReference, depth,
            variable.PresentationHint?.Attributes?.Contains("failedEvaluation") == true ? VariableRole.Error : VariableRole.Value)
        {
            Parent = parent,
            EvaluateName = variable.EvaluateName is { Length: > 0 } named && named != variable.Name ? named : null,
        };
}

public enum VariableRole
{
    Value,
    Watch,
    Result,
    Error,

    /// <summary>A scope of the frame (its locals), when it has more than one.</summary>
    Scope,
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
                node.Children = [.. (await Load!(node.Reference)).Select(v => VariableNode.From(v, node.Depth + 1, node))];
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
