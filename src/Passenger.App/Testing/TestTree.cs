using System.Collections.ObjectModel;
using Avalonia;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Passenger.App.Testing;

/// <summary>Where a test or a group of them stands, as Rider's Unit Tests window marks them.</summary>
public enum TestState
{
    NotRun,
    Running,
    Passed,
    Failed,
    Skipped,
}

public enum TestNodeKind
{
    Project,
    Namespace,
    Class,
    Method,
}

/// <summary>A row of the Unit Tests window: a project, a namespace, a class or a test method. A group's state is
/// its tests' (failed over running over passed), its duration their sum.</summary>
public sealed class TestNode(TestNodeKind kind, string name, TestNode? parent) : Observable
{
    private readonly List<TestResult> results = [];
    private TestState state;
    private bool isExpanded = kind != TestNodeKind.Method;

    public TestNodeKind Kind { get; } = kind;
    public string Name { get; } = name;
    public TestNode? Parent { get; } = parent;
    public List<TestNode> Children { get; } = [];
    public int Depth => Parent is null ? 0 : Parent.Depth + 1;
    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    /// <summary>A method's test cases: one, or a theory's rows.</summary>
    public List<TestCase> Cases { get; } = [];

    /// <summary>The results of a method's last run, one per case or data row.</summary>
    public IReadOnlyList<TestResult> Results => results;

    public string? Path => Cases.FirstOrDefault(c => !string.IsNullOrEmpty(c.CodeFilePath))?.CodeFilePath ?? Children.Select(c => c.Path).FirstOrDefault(p => p is not null);
    public int Line => Cases.FirstOrDefault(c => c.LineNumber > 0)?.LineNumber ?? Children.Select(c => c.Line).FirstOrDefault(l => l > 0);

    public TestState State
    {
        get => state;
        private set => Set(ref state, value);
    }

    public TimeSpan Duration => Kind == TestNodeKind.Method ? TimeSpan.FromTicks(results.Sum(r => r.Duration.Ticks)) : TimeSpan.FromTicks(Children.Sum(c => c.Duration.Ticks));

    public bool IsExpanded
    {
        get => isExpanded;
        set => Set(ref isExpanded, value);
    }

    public bool HasChildren => Children.Count > 0;

    /// <summary>Every test case under this row.</summary>
    public IEnumerable<TestCase> AllCases => Kind == TestNodeKind.Method ? Cases : Children.SelectMany(c => c.AllCases);

    public IEnumerable<TestNode> Methods => Kind == TestNodeKind.Method ? [this] : Children.SelectMany(c => c.Methods);

    /// <summary>What the details pane shows: each result's outcome, message, stack and output.</summary>
    public string Details => string.Join("\n\n", results.Select(r =>
    {
        string header = results.Count > 1 ? $"{r.DisplayName ?? r.TestCase.DisplayName}: {r.Outcome}" : r.Outcome.ToString();
        string output = string.Join(string.Empty, r.Messages.Select(m => m.Text));
        return string.Join('\n', new[] { header, r.ErrorMessage, r.ErrorStackTrace, output.Length > 0 ? $"Output:\n{output.TrimEnd()}" : null }.Where(t => !string.IsNullOrWhiteSpace(t)));
    }));

    internal void Begin()
    {
        results.Clear();
        State = TestState.Running;
        Raise(nameof(Duration));
        Raise(nameof(Details));
    }

    internal void Add(TestResult result)
    {
        results.Add(result);
        IEnumerable<TestState> states = results.Select(r => Of(r.Outcome));
        State = Combine(results.Count < Cases.Count ? states.Append(TestState.Running) : states);
        Raise(nameof(Duration));
        Raise(nameof(Details));
    }

    /// <summary>A method still running when its run ended was never run: it goes back to not run.</summary>
    internal void End()
    {
        if (Kind == TestNodeKind.Method && State == TestState.Running)
        {
            State = results.Count == 0 ? TestState.NotRun : Combine(results.Select(r => Of(r.Outcome)));
        }
    }

    internal void Recompute()
    {
        if (Kind != TestNodeKind.Method)
        {
            State = Combine(Children.Select(c => c.State));
            Raise(nameof(Duration));
        }
    }

    private static TestState Of(TestOutcome outcome) => outcome switch
    {
        TestOutcome.Passed => TestState.Passed,
        TestOutcome.Failed => TestState.Failed,
        TestOutcome.Skipped => TestState.Skipped,
        _ => TestState.NotRun,
    };

    private static TestState Combine(IEnumerable<TestState> states)
    {
        List<TestState> all = [.. states];
        return all.Count == 0 ? TestState.NotRun
            : all.Contains(TestState.Failed) ? TestState.Failed
            : all.Contains(TestState.Running) ? TestState.Running
            : all.Contains(TestState.Passed) ? (all.Contains(TestState.NotRun) ? TestState.NotRun : TestState.Passed)
            : all.All(s => s == TestState.Skipped) ? TestState.Skipped
            : TestState.NotRun;
    }
}

/// <summary>The Unit Tests window's tree: the tests discovered, by project, namespace, class and method, flattened
/// to the rows that are open so a virtualizing list draws only what is on screen.</summary>
public sealed class TestTree
{
    private readonly Dictionary<Guid, TestNode> byCase = [];

    public ObservableCollection<TestNode> Rows { get; } = [];

    public IReadOnlyList<TestNode> Roots { get; private set; } = [];

    /// <summary>The tree for what discovery found, project by project; what was open stays open.</summary>
    public void Load(IReadOnlyList<(string Project, IReadOnlyList<TestCase> Cases)> projects)
    {
        byCase.Clear();
        List<TestNode> roots = [];
        foreach ((string project, IReadOnlyList<TestCase> cases) in projects)
        {
            TestNode root = new(TestNodeKind.Project, project, null);
            foreach (TestCase test in cases.OrderBy(c => c.FullyQualifiedName, StringComparer.Ordinal))
            {
                (string space, string type, string method) = Split(test.FullyQualifiedName);
                TestNode parent = space.Length == 0 ? root : Child(root, TestNodeKind.Namespace, space);
                TestNode owner = Child(parent, TestNodeKind.Class, type);
                TestNode leaf = Child(owner, TestNodeKind.Method, method);
                leaf.Cases.Add(test);
                byCase[test.Id] = leaf;
            }
            roots.Add(root);
        }
        Roots = roots;
        Flatten();
    }

    public TestNode? Node(TestCase test) => byCase.GetValueOrDefault(test.Id);

    /// <summary>The methods about to run go to running.</summary>
    public void Begin(IEnumerable<TestNode> methods)
    {
        foreach (TestNode method in methods.Distinct())
        {
            method.Begin();
        }
        Recompute();
    }

    public void Apply(TestResult result)
    {
        if (Node(result.TestCase) is { } method)
        {
            method.Add(result);
            for (TestNode? group = method.Parent; group is not null; group = group.Parent)
            {
                group.Recompute();
            }
        }
    }

    public void End()
    {
        foreach (TestNode method in Roots.SelectMany(r => r.Methods))
        {
            method.End();
        }
        Recompute();
    }

    /// <summary>The test methods declared in a file, for the editor's gutter.</summary>
    public IEnumerable<TestNode> MethodsIn(string path) =>
        Roots.SelectMany(r => r.Methods).Where(m => string.Equals(m.Path, path, StringComparison.Ordinal));

    public void Toggle(TestNode node)
    {
        if (!node.HasChildren)
        {
            return;
        }
        node.IsExpanded = !node.IsExpanded;
        Flatten();
    }

    private void Recompute()
    {
        foreach (TestNode root in Roots)
        {
            Recompute(root);
        }

        static void Recompute(TestNode node)
        {
            foreach (TestNode child in node.Children)
            {
                Recompute(child);
            }
            node.Recompute();
        }
    }

    private void Flatten()
    {
        Rows.Clear();
        foreach (TestNode root in Roots)
        {
            Add(root);
        }

        void Add(TestNode node)
        {
            Rows.Add(node);
            if (node.IsExpanded)
            {
                foreach (TestNode child in node.Children)
                {
                    Add(child);
                }
            }
        }
    }

    private static TestNode Child(TestNode parent, TestNodeKind kind, string name)
    {
        if (parent.Children.FirstOrDefault(c => c.Kind == kind && c.Name == name) is { } existing)
        {
            return existing;
        }
        TestNode child = new(kind, name, parent);
        parent.Children.Add(child);
        return child;
    }

    /// <summary>"A.B.Class.Method" as its namespace, class and method; a parameter list (NUnit's cases) stays with
    /// the method.</summary>
    internal static (string Namespace, string Class, string Method) Split(string fullyQualifiedName)
    {
        int parameters = fullyQualifiedName.IndexOf('(');
        string name = parameters >= 0 ? fullyQualifiedName[..parameters] : fullyQualifiedName;
        string rest = parameters >= 0 ? fullyQualifiedName[parameters..] : string.Empty;
        int method = name.LastIndexOf('.');
        if (method < 0)
        {
            return (string.Empty, string.Empty, fullyQualifiedName);
        }
        string type = name[..method];
        int owner = type.LastIndexOf('.');
        return owner < 0 ? (string.Empty, type, name[(method + 1)..] + rest) : (type[..owner], type[(owner + 1)..], name[(method + 1)..] + rest);
    }
}
