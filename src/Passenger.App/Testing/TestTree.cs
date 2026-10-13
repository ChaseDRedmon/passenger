using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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

    /// <summary>One of a multi-targeted project's target frameworks, as Rider shows each under its project.</summary>
    Framework,
    Namespace,
    Class,
    Method,
}

/// <summary>A row of the Unit Tests window: a project, a target framework, a namespace, a class or a test method. A
/// group's state is its methods' (failed over running over not run over passed), its duration their sum, both kept up
/// to date by each method's change rather than summed again.</summary>
public sealed class TestNode : Observable
{
    private readonly List<TestResult> results = [];

    /// <summary>A group's methods by state, or a method's results by state, indexed by <see cref="TestState"/>.</summary>
    private readonly int[] counts = new int[5];
    private TestState state;
    private long ticks;
    private bool isExpanded;

    public TestNode(TestNodeKind kind, string name, TestNode? parent)
    {
        Kind = kind;
        Name = name;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
        isExpanded = kind != TestNodeKind.Method;
    }

    public TestNodeKind Kind { get; }
    public string Name { get; }
    public TestNode? Parent { get; }
    public List<TestNode> Children { get; } = [];
    public int Depth { get; }
    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    /// <summary>A method's test cases: one, or a theory's rows.</summary>
    public List<TestCase> Cases { get; } = [];

    /// <summary>The results of a method's last run, one per case or data row.</summary>
    public IReadOnlyList<TestResult> Results => results;

    /// <summary>What the row says: a theory counts its cases.</summary>
    public string Label => Kind == TestNodeKind.Method && Cases.Count > 1 ? $"{Name} ({Cases.Count} cases)" : Name;

    public string? Path => Cases.FirstOrDefault(c => !string.IsNullOrEmpty(c.CodeFilePath))?.CodeFilePath ?? Children.Select(c => c.Path).FirstOrDefault(p => p is not null);
    public int Line => Cases.FirstOrDefault(c => c.LineNumber > 0)?.LineNumber ?? Children.Select(c => c.Line).FirstOrDefault(l => l > 0);

    public TestState State
    {
        get => state;
        private set
        {
            TestState was = state;
            if (Set(ref state, value) && Kind == TestNodeKind.Method)
            {
                for (TestNode? group = Parent; group is not null; group = group.Parent)
                {
                    group.Shift(was, value);
                }
            }
        }
    }

    public TimeSpan Duration => TimeSpan.FromTicks(ticks);

    public bool IsExpanded
    {
        get => isExpanded;
        internal set => Set(ref isExpanded, value);
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
        Array.Clear(counts);
        AddTicks(-ticks);
        State = TestState.Running;
        Raise(nameof(Details));
    }

    internal void Add(TestResult result)
    {
        results.Add(result);
        counts[(int)Of(result.Outcome)]++;
        AddTicks(result.Duration.Ticks);
        State = Combine(counts, running: results.Count < Cases.Count);
        Raise(nameof(Details));
    }

    /// <summary>A method still running when its run ended was never run: it goes back to not run.</summary>
    internal void End()
    {
        if (Kind == TestNodeKind.Method && State == TestState.Running)
        {
            State = Combine(counts, running: false);
        }
    }

    /// <summary>A group's counts and duration made again from its methods, once, when the tree is loaded (a method found
    /// again may have more or fewer cases).</summary>
    internal void Recount()
    {
        if (Kind == TestNodeKind.Method)
        {
            Raise(nameof(Label));
            return;
        }
        Array.Clear(counts);
        long sum = 0;
        foreach (TestNode child in Children)
        {
            child.Recount();
            sum += child.ticks;
            if (child.Kind == TestNodeKind.Method)
            {
                counts[(int)child.state]++;
            }
            else
            {
                for (int each = 0; each < counts.Length; each++)
                {
                    counts[each] += child.counts[each];
                }
            }
        }
        if (sum != ticks)
        {
            ticks = sum;
            Raise(nameof(Duration));
        }
        State = Combine(counts, running: false);
    }

    /// <summary>One of the group's methods went from one state to another.</summary>
    private void Shift(TestState from, TestState to)
    {
        counts[(int)from]--;
        counts[(int)to]++;
        State = Combine(counts, running: false);
    }

    private void AddTicks(long delta)
    {
        if (delta == 0)
        {
            return;
        }
        for (TestNode? node = this; node is not null; node = node.Parent)
        {
            node.ticks += delta;
            node.Raise(nameof(Duration));
        }
    }

    private static TestState Of(TestOutcome outcome) => outcome switch
    {
        TestOutcome.Passed => TestState.Passed,
        TestOutcome.Failed => TestState.Failed,
        TestOutcome.Skipped => TestState.Skipped,
        _ => TestState.NotRun,
    };

    /// <summary>Failed over running over not run over passed; skipped only when all were.</summary>
    private static TestState Combine(int[] counts, bool running) =>
        counts[(int)TestState.Failed] > 0 ? TestState.Failed
        : running || counts[(int)TestState.Running] > 0 ? TestState.Running
        : counts[(int)TestState.NotRun] > 0 ? TestState.NotRun
        : counts[(int)TestState.Passed] > 0 ? TestState.Passed
        : counts[(int)TestState.Skipped] > 0 ? TestState.Skipped
        : TestState.NotRun;
}

/// <summary>The tests of one assembly as discovery found them: its project, and its target framework when the project
/// has several.</summary>
public sealed record TestSource(string Project, string? Framework, IReadOnlyList<TestCase> Cases);

/// <summary>The Unit Tests window's tree: the tests discovered, by project, target framework, namespace, class and method,
/// flattened to the rows that are open so a virtualizing list draws only what is on screen.</summary>
public sealed class TestTree
{
    private readonly Dictionary<(Guid, string), TestNode> byCase = [];

    public TestRows Rows { get; } = [];

    /// <summary>Tests were found, or a run began or ended: what shows their state follows.</summary>
    public event EventHandler? Updated;

    /// <summary>Results came in during a run, of which there may be thousands: worth following a moment later.</summary>
    public event EventHandler? ResultApplied;

    public IReadOnlyList<TestNode> Roots { get; private set; } = [];

    /// <summary>The tree for what discovery found. A row found before by the same name is the same row, so what was open
    /// or closed stays so, a chosen row stays chosen, and a method keeps its last results until it runs again.</summary>
    public void Load(IReadOnlyList<TestSource> sources)
    {
        Dictionary<string, TestNode> before = [];
        foreach (TestNode root in Roots)
        {
            Index(root, string.Empty);
        }
        byCase.Clear();
        Dictionary<string, TestNode> built = [];
        Dictionary<TestNode, string> keys = new(ReferenceEqualityComparer.Instance);
        List<TestNode> roots = [];
        HashSet<string> multiTargeted = [.. sources.GroupBy(s => s.Project).Where(g => g.Count(s => s.Framework is not null) > 1).Select(g => g.Key)];
        foreach (TestSource source in sources)
        {
            TestNode root = Node(null, TestNodeKind.Project, source.Project);
            if (!roots.Contains(root))
            {
                roots.Add(root);
            }
            TestNode top = source.Framework is { } framework && multiTargeted.Contains(source.Project) ? Node(root, TestNodeKind.Framework, framework) : root;
            foreach (TestCase test in source.Cases.OrderBy(c => c.FullyQualifiedName, StringComparer.Ordinal))
            {
                (string space, string type, string method) = Split(test.FullyQualifiedName);
                TestNode parent = space.Length == 0 ? top : Node(top, TestNodeKind.Namespace, space);
                TestNode owner = Node(parent, TestNodeKind.Class, type);
                TestNode leaf = Node(owner, TestNodeKind.Method, method);
                leaf.Cases.Add(test);
                byCase[(test.Id, test.Source)] = leaf;
            }
        }
        Roots = roots;
        foreach (TestNode root in roots)
        {
            root.Recount();
        }
        Rows.Show([.. roots.SelectMany(r => Visible(r, includeSelf: true))]);
        Updated?.Invoke(this, EventArgs.Empty);

        void Index(TestNode node, string parentKey)
        {
            string key = $"{parentKey}/{(int)node.Kind}:{node.Name}";
            before[key] = node;
            foreach (TestNode child in node.Children)
            {
                Index(child, key);
            }
        }

        // The node the key names: the one made already in this load, else the one before it, emptied, else a new one.
        TestNode Node(TestNode? parent, TestNodeKind kind, string name)
        {
            string key = $"{(parent is null ? string.Empty : keys[parent])}/{(int)kind}:{name}";
            if (built.TryGetValue(key, out TestNode? made))
            {
                return made;
            }
            if (!before.Remove(key, out made) || made.Parent != parent)
            {
                made = new TestNode(kind, name, parent);
            }
            made.Children.Clear();
            made.Cases.Clear();
            built[key] = made;
            keys[made] = key;
            parent?.Children.Add(made);
            return made;
        }
    }

    public TestNode? Node(TestCase test) => byCase.GetValueOrDefault((test.Id, test.Source));

    /// <summary>The methods about to run go to running.</summary>
    public void Begin(IEnumerable<TestNode> methods)
    {
        foreach (TestNode method in methods.Distinct())
        {
            method.Begin();
        }
        Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Results as they come, each changing its method and the groups above it, nothing else.</summary>
    public void Apply(IEnumerable<TestResult> results)
    {
        bool any = false;
        foreach (TestResult result in results)
        {
            if (Node(result.TestCase) is { } method)
            {
                method.Add(result);
                any = true;
            }
        }
        if (any)
        {
            ResultApplied?.Invoke(this, EventArgs.Empty);
        }
    }

    public void End()
    {
        foreach (TestNode method in Roots.SelectMany(r => r.Methods))
        {
            method.End();
        }
        Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens or closes a row: only its rows below come or go, so the rows chosen stay chosen.</summary>
    public void Toggle(TestNode node)
    {
        if (!node.HasChildren)
        {
            return;
        }
        int at = Rows.IndexOf(node);
        node.IsExpanded = !node.IsExpanded;
        if (at < 0)
        {
            return;
        }
        if (node.IsExpanded)
        {
            Rows.InsertRange(at + 1, [.. Visible(node, includeSelf: false)]);
        }
        else
        {
            int end = at + 1;
            while (end < Rows.Count && Rows[end].Depth > node.Depth)
            {
                end++;
            }
            Rows.RemoveRange(at + 1, end - at - 1);
        }
    }

    /// <summary>Opens the rows that hold a node, so it is one of the rows shown.</summary>
    public void Reveal(TestNode node)
    {
        Stack<TestNode> closed = new();
        for (TestNode? above = node.Parent; above is not null; above = above.Parent)
        {
            if (!above.IsExpanded)
            {
                closed.Push(above);
            }
        }
        while (closed.TryPop(out TestNode? opening))
        {
            Toggle(opening);
        }
    }

    private static IEnumerable<TestNode> Visible(TestNode node, bool includeSelf)
    {
        if (includeSelf)
        {
            yield return node;
        }
        if (!node.IsExpanded)
        {
            yield break;
        }
        foreach (TestNode child in node.Children)
        {
            foreach (TestNode shown in Visible(child, includeSelf: true))
            {
                yield return shown;
            }
        }
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

/// <summary>The rows shown, changed a run of rows at a time: a list draws, and keeps its selection over, one change for
/// a class opened or closed rather than one per row.</summary>
public sealed class TestRows : ObservableCollection<TestNode>
{
    public void InsertRange(int index, IEnumerable<TestNode> rows)
    {
        List<TestNode> added = [.. rows];
        if (added.Count == 0)
        {
            return;
        }
        CheckReentrancy();
        ((List<TestNode>)Items).InsertRange(index, added);
        Changed(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, added, index));
    }

    public void RemoveRange(int index, int count)
    {
        if (count == 0)
        {
            return;
        }
        CheckReentrancy();
        List<TestNode> removed = ((List<TestNode>)Items).GetRange(index, count);
        ((List<TestNode>)Items).RemoveRange(index, count);
        Changed(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
    }

    /// <summary>Shows these rows: the runs of rows that went are taken out and the runs that came put in, so the rows that
    /// stay are the same rows, chosen if they were; rows that moved are shown again whole.</summary>
    public void Show(IReadOnlyList<TestNode> wanted)
    {
        HashSet<TestNode> keep = new(wanted, ReferenceEqualityComparer.Instance);
        HashSet<TestNode> staying = new(Items.Where(keep.Contains), ReferenceEqualityComparer.Instance);
        if (!Items.Where(keep.Contains).SequenceEqual(wanted.Where(staying.Contains)))
        {
            CheckReentrancy();
            Items.Clear();
            ((List<TestNode>)Items).AddRange(wanted);
            Changed(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            return;
        }
        for (int at = Count - 1; at >= 0;)
        {
            if (keep.Contains(this[at]))
            {
                at--;
                continue;
            }
            int end = at;
            while (at >= 0 && !keep.Contains(this[at]))
            {
                at--;
            }
            RemoveRange(at + 1, end - at);
        }
        // What stays is now in the order wanted, so each run of new rows goes in where the next row staying is.
        for (int at = 0; at < wanted.Count;)
        {
            int end = at;
            while (end < wanted.Count && !staying.Contains(wanted[end]))
            {
                end++;
            }
            InsertRange(at, wanted.Skip(at).Take(end - at));
            at = end + 1;
        }
    }

    private void Changed(NotifyCollectionChangedEventArgs change)
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(change);
    }
}
