using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;

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

    /// <summary>One of a theory's data rows (or NUnit's cases), found by a runner or made by its run.</summary>
    Row,
}

/// <summary>A row of the Unit Tests window: a project, a target framework, a namespace, a class, a test method or one of
/// its data rows. A group's state is its methods' (failed over running over not run over passed), its duration their
/// sum, both kept up to date by each method's change rather than summed again; a method's state is all its results', a
/// data row's its own.</summary>
public sealed class TestNode : Observable
{
    private readonly List<TestOutcome> results = [];

    /// <summary>A group's methods by state, or a method's results by state, indexed by <see cref="TestState"/>.</summary>
    private readonly int[] counts = new int[5];
    private TestState state;
    private long ticks;
    private bool isExpanded;

    /// <summary>How many outcomes the method's run is waiting for.</summary>
    private int expected;

    public TestNode(TestNodeKind kind, string name, TestNode? parent)
    {
        Kind = kind;
        Name = name;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
        isExpanded = kind is not (TestNodeKind.Method or TestNodeKind.Row);
    }

    public TestNodeKind Kind { get; }
    public string Name { get; }
    public TestNode? Parent { get; }
    public List<TestNode> Children { get; } = [];
    public int Depth { get; }
    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    /// <summary>A method's tests as a runner found them, which a run runs: the method, or each of its data rows; none
    /// until a runner has looked.</summary>
    public List<TestItem> Items { get; } = [];

    /// <summary>A method as its code declares it now: where it is, while it is typed.</summary>
    public TestItem? FromCode { get; internal set; }

    /// <summary>A data row's test; null for a row its method's run made.</summary>
    public TestItem? Item { get; internal set; }

    /// <summary>A project row's project file, as its tests' <see cref="TestItem.Project"/> names it.</summary>
    public string? Project { get; internal set; }

    /// <summary>Taken out of the tree by a load: the method was renamed or removed.</summary>
    public bool Gone { get; private set; }

    /// <summary>The results of a method's last run, one per test or data row; a data row's own.</summary>
    public IReadOnlyList<TestOutcome> Results => results;

    /// <summary>A method's full name, Namespace.Class.Method, nested types after a '+'.</summary>
    public string? FullName => FromCode?.Method ?? Items.FirstOrDefault()?.Method ?? Item?.Method;

    public string? Path => Located?.Path ?? (Kind == TestNodeKind.Row ? Parent?.Path : Children.Select(c => c.Path).FirstOrDefault(p => p is not null));

    public int Line => Located?.Line ?? (Kind == TestNodeKind.Row ? Parent?.Line ?? 0 : Children.Select(c => c.Line).FirstOrDefault(l => l > 0));

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

    /// <summary>The methods under this row, or the method itself; none under a data row.</summary>
    public IEnumerable<TestNode> Methods => Kind switch
    {
        TestNodeKind.Method => [this],
        TestNodeKind.Row => [],
        _ => Children.SelectMany(c => c.Methods),
    };

    /// <summary>What the details pane shows: each result's outcome, message, stack and output.</summary>
    public string Details => string.Join("\n\n", results.Select(r =>
    {
        string header = results.Count > 1 ? $"{r.Row ?? r.DisplayName ?? r.Test.DisplayName}: {r.State}" : r.State.ToString();
        return string.Join('\n', new[] { header, r.Message, r.StackTrace, r.Output is { } output ? $"Output:\n{output.TrimEnd()}" : null }.Where(t => !string.IsNullOrWhiteSpace(t)));
    }));

    /// <summary>Where the code or a runner says it is: the code's, which follows typing, over the last build's.</summary>
    private TestItem? Located => FromCode ?? Item ?? Items.FirstOrDefault(i => i.Path is not null);

    /// <summary>A method or row about to run: its results go, and it is running until as many outcomes as it runs
    /// tests have come.</summary>
    internal void Begin(int tests)
    {
        results.Clear();
        Array.Clear(counts);
        AddTicks(-ticks);
        expected = tests;
        State = TestState.Running;
        Raise(nameof(Details));
    }

    internal void Add(TestOutcome outcome)
    {
        results.Add(outcome);
        counts[(int)outcome.State]++;
        AddTicks(outcome.Duration.Ticks);
        State = Combine(counts, running: results.Count < expected);
        Raise(nameof(Details));
    }

    /// <summary>A method or row still running when its run ended was never run: it goes back to not run.</summary>
    internal void End()
    {
        if (State == TestState.Running)
        {
            State = Combine(counts, running: false);
        }
    }

    internal void Leave() => Gone = true;

    internal void RaiseShape() => Raise(nameof(HasChildren));

    /// <summary>A group's counts and duration made again from its methods, once, when the tree is loaded (a method found
    /// again may have more or fewer tests).</summary>
    internal void Recount()
    {
        if (Kind is TestNodeKind.Method or TestNodeKind.Row)
        {
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

    /// <summary>A method's time is its groups' too; a data row's is already its method's.</summary>
    private void AddTicks(long delta)
    {
        if (delta == 0)
        {
            return;
        }
        for (TestNode? node = this; node is not null; node = Kind == TestNodeKind.Row ? null : node.Parent)
        {
            node.ticks += delta;
            node.Raise(nameof(Duration));
        }
    }

    /// <summary>Failed over running over not run over passed; skipped only when all were.</summary>
    private static TestState Combine(int[] counts, bool running) =>
        counts[(int)TestState.Failed] > 0 ? TestState.Failed
        : running || counts[(int)TestState.Running] > 0 ? TestState.Running
        : counts[(int)TestState.NotRun] > 0 ? TestState.NotRun
        : counts[(int)TestState.Passed] > 0 ? TestState.Passed
        : counts[(int)TestState.Skipped] > 0 ? TestState.Skipped
        : TestState.NotRun;
}

/// <summary>The tests a runner found in one of a project's assemblies, built for one target framework.</summary>
public sealed record TestSource(string Framework, IReadOnlyList<TestItem> Tests);

/// <summary>A test project's tests: those its code declares, and those a runner found in each of its assemblies. The same
/// parts make an equal value, which the tree takes for a project unchanged.</summary>
public sealed record ProjectTests(string Project, string Name, CodeTests Code, IReadOnlyList<TestSource> Discovered);

/// <summary>The Unit Tests window's tree: the tests by project, target framework, namespace, class, method and data row,
/// flattened to the rows that are open so a virtualizing list draws only what is on screen. The code's tests and a
/// runner's merge on their method's id, so a run's results land on the rows the code showed, and a test the code
/// declares stays whatever a run reported.</summary>
public sealed class TestTree
{
    /// <summary>Each project's tree as last loaded, with what it was loaded from and its rows by their tests' keys.</summary>
    private Dictionary<string, Built> projects = new(StringComparer.Ordinal);

    /// <summary>The methods and rows the run under way began, which its end ends.</summary>
    private List<TestNode> running = [];

    private string emptyText = string.Empty;

    public TestRows Rows { get; } = [];

    /// <summary>Tests were found, or a run began or ended: what shows their state follows.</summary>
    public event EventHandler? Updated;

    /// <summary>Results came in during a run, of which there may be thousands: worth following a moment later.</summary>
    public event EventHandler? ResultApplied;

    public IReadOnlyList<TestNode> Roots { get; private set; } = [];

    /// <summary>What the window says while the tree has no rows: why there are none yet.</summary>
    public string EmptyText
    {
        get => emptyText;
        set
        {
            if (emptyText != value)
            {
                emptyText = value;
                EmptyTextChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? EmptyTextChanged;

    /// <summary>The tree for the test projects' tests. A project loaded from the same parts as before is kept whole; in
    /// another a row with the same place and name is the same row, so what was open or closed stays so, a chosen row
    /// stays chosen, and a test keeps its last results until it runs again.</summary>
    public void Load(IReadOnlyList<ProjectTests> tests)
    {
        Dictionary<string, Built> next = new(StringComparer.Ordinal);
        List<TestNode> roots = [];
        foreach (ProjectTests project in tests)
        {
            if (next.ContainsKey(project.Project))
            {
                continue;
            }
            Built? was = projects.GetValueOrDefault(project.Project);
            Built built = was is not null && was.Input == project ? was : Build(project, was);
            next[project.Project] = built;
            if (built.Root is { } root)
            {
                roots.Add(root);
            }
        }
        foreach ((string path, Built gone) in projects)
        {
            if (!next.ContainsKey(path) && gone.Root is { } root)
            {
                Leave(root);
            }
        }
        projects = next;
        Roots = roots;
        Rows.Show([.. roots.SelectMany(r => Visible(r, includeSelf: true))]);
        Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The row a test is shown by: its data row's, else its method's.</summary>
    public TestNode? Node(TestItem test) =>
        projects.TryGetValue(test.Project, out Built? built) ? built.ByKey.GetValueOrDefault(test.Key) : null;

    /// <summary>The tests about to run go to running, each method waiting for as many outcomes as it runs tests; the
    /// rows a method's last run made wait to be made again.</summary>
    public void Begin(IReadOnlyList<TestItem> tests)
    {
        Dictionary<TestNode, int> methods = new(ReferenceEqualityComparer.Instance);
        List<TestNode> rows = [];
        foreach (TestItem test in tests)
        {
            if (Node(test) is not { } node)
            {
                continue;
            }
            TestNode method = node.Kind == TestNodeKind.Row ? node.Parent! : node;
            methods[method] = methods.GetValueOrDefault(method) + 1;
            rows.AddRange(node.Kind == TestNodeKind.Row ? [node] : node.Children.Where(c => c.Item is null));
        }
        foreach ((TestNode method, int count) in methods)
        {
            method.Begin(count);
        }
        foreach (TestNode row in rows)
        {
            row.Begin(1);
        }
        running = [.. methods.Keys, .. rows];
        Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Outcomes as they come, each changing its row, its method and the groups above them, nothing else. A
    /// result naming a data row its test does not have (a theory whose rows its run makes) is that row's, made now.</summary>
    public void Apply(IEnumerable<TestOutcome> outcomes)
    {
        bool any = false;
        bool reshaped = false;
        foreach (TestOutcome outcome in outcomes)
        {
            if (Node(outcome.Test) is not { } node)
            {
                continue;
            }
            TestNode method = node.Kind == TestNodeKind.Row ? node.Parent! : node;
            TestNode? row = node.Kind == TestNodeKind.Row ? node : outcome.Row is { } name ? MadeRow(method, name, ref reshaped) : null;
            row?.Add(outcome);
            method.Add(outcome);
            any = true;
        }
        if (reshaped)
        {
            Rows.Show([.. Roots.SelectMany(r => Visible(r, includeSelf: true))]);
        }
        if (any)
        {
            ResultApplied?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The run is over: what it began and never heard of goes back to not run, and a row a method's run made
    /// before and did not make again goes.</summary>
    public void End()
    {
        bool reshaped = false;
        foreach (TestNode node in running)
        {
            if (node is { Kind: TestNodeKind.Row, Item: null, Results.Count: 0 } stale && stale.Parent is { Results.Count: > 0 } method)
            {
                method.Children.Remove(stale);
                stale.Leave();
                method.RaiseShape();
                reshaped |= method.IsExpanded;
            }
            node.End();
        }
        running = [];
        if (reshaped)
        {
            Rows.Show([.. Roots.SelectMany(r => Visible(r, includeSelf: true))]);
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

    /// <summary>A project's tree from its parts, the rows of its tree before taken again where they are the same. A test
    /// a runner found in a file whose code no longer declares its method was renamed or removed since the build, and is
    /// left out; one in a file the code was not read from (an inherited test's base, a test of an attribute the code's
    /// reading does not know) stays.</summary>
    private static Built Build(ProjectTests project, Built? was)
    {
        Dictionary<string, TestNode> before = new(StringComparer.Ordinal);
        if (was?.Root is { } oldRoot)
        {
            Index(oldRoot, string.Empty);
        }
        Dictionary<string, TestNode> made = new(StringComparer.Ordinal);
        Dictionary<TestNode, string> keys = new(ReferenceEqualityComparer.Instance);
        Dictionary<TestNode, List<TestNode>> runRows = new(ReferenceEqualityComparer.Instance);
        HashSet<TestNode> hadChildren = new(ReferenceEqualityComparer.Instance);
        Dictionary<string, TestNode> byKey = new(StringComparer.Ordinal);
        TestNode root = Node(null, TestNodeKind.Project, project.Name, $"{(int)TestNodeKind.Project}:{project.Project}");
        root.Project = project.Project;
        List<(TestNode Top, IReadOnlyList<TestItem> Found)> tops = [];
        if (project.Discovered.Count > 1)
        {
            tops.AddRange(project.Discovered.Select(s => (Node(root, TestNodeKind.Framework, s.Framework), s.Tests)));
        }
        else
        {
            tops.Add((root, project.Discovered.Count == 1 ? project.Discovered[0].Tests : []));
        }
        foreach ((TestNode top, IReadOnlyList<TestItem> found) in tops)
        {
            IEnumerable<TestItem> kept = found.Where(t => t.Path is null || !project.Code.MethodsByFile.TryGetValue(t.Path, out IReadOnlySet<string>? declared)
                || declared.Contains(t.Method[(t.Method.LastIndexOf('.') + 1)..]));
            // A nested type ("Outer+Inner") after its outer type's methods: '/' sorts after '.', where '+' sorts before.
            foreach (TestItem test in kept.Concat(project.Code.Tests).OrderBy(t => t.Method.Replace('+', '/'), StringComparer.Ordinal).ThenBy(t => t.Row, StringComparer.Ordinal))
            {
                (string space, string type, string name) = Split(test.Method);
                TestNode parent = space.Length == 0 ? top : Node(top, TestNodeKind.Namespace, space);
                TestNode method = Node(Node(parent, TestNodeKind.Class, type), TestNodeKind.Method, name);
                if (test.Framework is null)
                {
                    method.FromCode ??= test;
                    continue;
                }
                method.Items.Add(test);
                if (test.Row is { } row)
                {
                    TestNode data = Node(method, TestNodeKind.Row, row);
                    data.Item = test;
                    byKey[test.Key] = data;
                }
                else
                {
                    byKey[test.Key] = method;
                }
            }
        }
        // The rows a run made stay under their method until it runs again, unless a runner has found the row since.
        HashSet<TestNode> restored = new(ReferenceEqualityComparer.Instance);
        foreach ((TestNode method, List<TestNode> rows) in runRows)
        {
            foreach (TestNode row in rows.Where(r => r.Item is null && !method.Children.Contains(r)))
            {
                method.Children.Add(row);
                restored.Add(row);
            }
        }
        foreach (TestNode left in before.Values.Where(n => !restored.Contains(n)))
        {
            left.Leave();
        }
        foreach (TestNode node in made.Values.Where(n => n.HasChildren != hadChildren.Contains(n)))
        {
            node.RaiseShape();
        }
        root.Recount();
        return new Built(project, root.HasChildren ? root : null, byKey);

        void Index(TestNode node, string parentKey)
        {
            string key = node.Parent is null ? $"{(int)node.Kind}:{project.Project}" : $"{parentKey}/{(int)node.Kind}:{node.Name}";
            before[key] = node;
            foreach (TestNode child in node.Children)
            {
                Index(child, key);
            }
        }

        // The node the key names: the one made already in this load, else the one before it, emptied, else a new one.
        TestNode Node(TestNode? parent, TestNodeKind kind, string name, string? rootKey = null)
        {
            string key = rootKey ?? $"{keys[parent!]}/{(int)kind}:{name}";
            if (made.TryGetValue(key, out TestNode? node))
            {
                return node;
            }
            if (!before.Remove(key, out node) || node.Parent != parent)
            {
                node = new TestNode(kind, name, parent);
            }
            else if (node.HasChildren)
            {
                hadChildren.Add(node);
                if (kind == TestNodeKind.Method && node.Children.Any(c => c.Item is null))
                {
                    runRows[node] = [.. node.Children.Where(c => c.Item is null)];
                }
            }
            node.Children.Clear();
            node.Items.Clear();
            node.FromCode = null;
            node.Item = null;
            made[key] = node;
            keys[node] = key;
            parent?.Children.Add(node);
            return node;
        }
    }

    /// <summary>A row made by a method's run: the one made before by that name, else a new one, under the method.</summary>
    private static TestNode MadeRow(TestNode method, string name, ref bool reshaped)
    {
        foreach (TestNode child in method.Children)
        {
            if (child.Name == name)
            {
                return child;
            }
        }
        TestNode row = new(TestNodeKind.Row, name, method);
        method.Children.Add(row);
        method.RaiseShape();
        reshaped |= method.IsExpanded;
        return row;
    }

    private static void Leave(TestNode node)
    {
        node.Leave();
        foreach (TestNode child in node.Children)
        {
            Leave(child);
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

    /// <summary>"A.B.Class.Method" as its namespace, class and method.</summary>
    internal static (string Namespace, string Class, string Method) Split(string method)
    {
        int last = method.LastIndexOf('.');
        if (last < 0)
        {
            return (string.Empty, string.Empty, method);
        }
        string type = method[..last];
        int owner = type.LastIndexOf('.');
        return owner < 0 ? (string.Empty, type, method[(last + 1)..]) : (type[..owner], type[(owner + 1)..], method[(last + 1)..]);
    }

    private sealed record Built(ProjectTests Input, TestNode? Root, Dictionary<string, TestNode> ByKey);
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
