using Avalonia.Threading;
using Passenger.App.Editors;
using Passenger.App.ToolWindows;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Dap;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Debugging;

/// <summary>The debugger in the IDE: the breakpoints in the gutter, a session's stops shown where
/// they are (the execution line, the Debug window's frames and variables), the stepping actions, and
/// a value when the pointer rests on an expression while paused. A UI-thread object.</summary>
public sealed class DebugController
{
    private readonly EditorArea editors;
    private readonly DebugView view;
    private readonly List<string> watches = [];
    private readonly Dictionary<OpenDocument, EventHandler<DocumentChangedEventArgs>> following = [];

    /// <summary>Where the program stopped, at its built line: shown where that line's code now is.</summary>
    private (string Path, int Line)? executionPoint;

    /// <summary>The session's built lines, from its start to its end: the files' edits since the build.</summary>
    private BuiltLines? lines;

    private (string Expression, string Value)? lastResult;

    /// <summary>The frame chosen at the stop shown: its variables are the view's, a hover evaluates in it. None while the
    /// program runs, so nothing asks of a frame the debugger has let go.</summary>
    private DapStackFrame? frame;

    /// <summary>The exception the program stopped on, and the frame it was thrown in, shown at that frame's line.</summary>
    private (string Text, int FrameId)? stopException;

    /// <summary>Why the exception the program stopped on could not be described, when it could not.</summary>
    private string? undescribed;

    /// <summary>Counts the stops shown and the runs between them, so what a stop's requests bring back once the program has
    /// gone on is dropped rather than shown over what is now true.</summary>
    private int shown;

    /// <summary>Clears the last stop's frames and values a moment after the program goes on, as Rider's views do: a step
    /// that stops again at once replaces them without the panes going blank between.</summary>
    private readonly DispatcherTimer clearing = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public DebugController(EditorArea editors, DebugView view)
    {
        this.editors = editors;
        this.view = view;
        clearing.Tick += (_, _) =>
        {
            clearing.Stop();
            if (Session is not { State: DebugState.Paused })
            {
                view.ShowFrames([], null, [], null);
                view.Tree.Show([]);
            }
        };
        Breakpoints.Changed += (_, path) => UpdateMarks(path);
        view.ActionRequested += (_, action) => ActionRequested?.Invoke(this, action);
        view.FrameChosen += (_, chosen) => _ = ChooseFrame(chosen, navigate: true);
        view.ThreadChosen += (_, thread) => _ = ChooseThread(thread.Id);
        view.EvaluateRequested += (_, request) => _ = Evaluate(request.Expression, request.Watch);
        view.SetValueRequested += (_, request) => _ = SetValue(request.Node, request.Value);
        view.WatchRemoved += (_, expression) =>
        {
            watches.Remove(expression);
            _ = ShowVariables();
        };
    }

    public BreakpointStore Breakpoints { get; } = new();

    public DebugSession? Session { get; private set; }

    public bool IsDebugging => Session is { State: not DebugState.Ended };

    /// <summary>A toolbar button of the Debug window: an app action for the window to run.</summary>
    public event EventHandler<string>? ActionRequested;

    /// <summary>Something for the status bar.</summary>
    public event EventHandler<string>? Message;

    /// <summary>A place to show: a stop, or a frame chosen.</summary>
    public event EventHandler<(string Path, TextPosition Position)>? Navigate;

    /// <summary>The session started, stopped, ran on or ended.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Launches a program under the debugger (after it is built), its output in the Debug
    /// window's Console.</summary>
    public Task StartAsync(DebugTarget target, string name) =>
        OpenAsync((built, log, output) => DebugSession.LaunchAsync(target, Breakpoints, built, log, output), name, launched: true);

    /// <summary>Attaches the debugger to a running .NET process, its output (if any) in the Console.</summary>
    public Task AttachAsync(int processId, string name) =>
        OpenAsync((built, log, output) => DebugSession.AttachAsync(processId, Breakpoints, built, log, output), name, launched: false);

    /// <summary>Starts a session, its output in the Console; a program it launched reads what is typed there, as in
    /// Rider's Debug console (one attached to has an input of its own).</summary>
    private async Task OpenAsync(Func<BuiltLines, Action<string>, Action<OutputEvent>, Task<DebugSession>> open, string name, bool launched)
    {
        UiThread.Check();
        if (IsDebugging)
        {
            await StopAsync();
        }
        // Not the last session's while this one starts, nor once it could not: a caller asks Session whether it did.
        Session = null;
        view.Clear();
        clearing.Stop();
        executionPoint = null;
        frame = null;
        stopException = null;
        undescribed = null;
        lastResult = null;
        BuiltLines built = new();
        lines = built;
        view.SetState(DebugState.Starting);
        Message?.Invoke(this, $"Debugging {name}");
        try
        {
            ConsoleSink console = view.Console.Sink;
            DebugSession session = await open(built, line => view.Console.Append($"{line}\n"), output =>
            {
                ConsoleStream? stream = output.Category switch
                {
                    "stdout" => ConsoleStream.Output,
                    "stderr" => ConsoleStream.Error,
                    "telemetry" => null,
                    _ => ConsoleStream.System,
                };
                if (stream is { } into)
                {
                    console.Write(output.Output, into);
                }
            });
            Session = session;
            view.Tree.Load = reference => session.VariablesAsync(reference);
            if (launched)
            {
                view.Console.Input = new ConsoleInput(text => Send(session.InputAsync(text)), () => Send(session.CloseInputAsync()));
            }
            session.StateChanged += (_, _) => _ = OnStateChanged(session, name);
            await OnStateChanged(session, name);
        }
        catch (Exception error) when (error is DapException or IOException or InvalidOperationException or TimeoutException)
        {
            lines = null;
            view.Console.Flush();
            view.SetState(DebugState.Ended);
            Message?.Invoke(this, $"Could not debug {name}: {(error is DapException dap ? dap.Reason : error.Message)}");
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Input for the program, which a session that has ended, or an adapter gone, may never take: its end says so.</summary>
    private static void Send(Task sending) =>
        _ = sending.ContinueWith(static sent => sent.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    public async Task StopAsync()
    {
        UiThread.Check();
        if (Session is { } session)
        {
            await session.StopAsync();
        }
    }

    /// <summary>Ends the session's program and adapter as the IDE closes (<see cref="DebugSession.CloseAsync"/>).</summary>
    public Task CloseAsync()
    {
        UiThread.Check();
        return Session?.CloseAsync() ?? Task.CompletedTask;
    }

    /// <summary>Runs one of the debugger's actions (<c>app.debug.*</c>) on the active editor and the
    /// session. Resume without a session is the caller's Debug, as F5 is in Visual Studio's keymap.</summary>
    public void Run(string action)
    {
        UiThread.Check();
        OpenDocument? active = editors.Active;
        int caretLine = active is null ? 0 : editors.CodeEditor.CaretPosition.Line;
        switch (action)
        {
            case AppActions.ToggleBreakpoint when active is not null:
                Breakpoints.Toggle(active.Path, caretLine);
                break;
            case AppActions.ToggleBreakpointEnabled when active is not null:
                Breakpoints.ToggleEnabled(active.Path, caretLine);
                break;
            case AppActions.Resume:
                _ = ResumeAsync(session => session.ContinueAsync(), "resume");
                break;
            case AppActions.Pause:
                _ = Session?.PauseAsync();
                break;
            case AppActions.StepOver:
                _ = ResumeAsync(session => session.StepOverAsync(), "step over");
                break;
            case AppActions.StepInto:
                _ = ResumeAsync(session => session.StepIntoAsync(), "step into");
                break;
            case AppActions.StepOut:
                _ = ResumeAsync(session => session.StepOutAsync(), "step out");
                break;
            case AppActions.RunToCursor when active is not null && Session is { State: DebugState.Paused }:
                _ = ResumeAsync(session => session.RunToCursorAsync(active.Path, caretLine), "run to the cursor");
                break;
            case AppActions.SetNextStatement when active is not null && Session is { State: DebugState.Paused } session:
                _ = SetNextStatementAsync(session, active.Path, caretLine);
                break;
            case AppActions.ShowExecutionPoint when executionPoint is { } point:
                Navigate?.Invoke(this, (point.Path, new TextPosition(Current(point.Path, point.Line), 0)));
                break;
            case AppActions.AddToWatches when Session is { State: DebugState.Paused } && SelectedOrWord() is { Length: > 0 } watched:
                _ = Evaluate(watched, watch: true);
                break;
            case AppActions.Evaluate when Session is { State: DebugState.Paused }:
                view.FocusEvaluate(SelectedOrWord());
                break;
        }
    }

    /// <summary>Sets the paused program going as an action asks. One the debugger refuses leaves it paused where it was,
    /// the Debug window as it was, and the reason in the status bar.</summary>
    private async Task ResumeAsync(Func<DebugSession, Task> resume, string action)
    {
        if (Session is not { } session)
        {
            return;
        }
        try
        {
            await resume(session);
        }
        catch (DapException error) when (session == Session && session.State != DebugState.Ended)
        {
            Message?.Invoke(this, $"Could not {action}: {error.Reason}");
        }
        catch (DapException)
        {
            // The session ended meanwhile, which says why itself.
        }
    }

    /// <summary>Rider's Set Next Statement: the paused thread goes on from a line (from zero) of the method it is
    /// in, skipping or repeating what lies between; null when it went, else why not.</summary>
    public async Task<string?> SetNextStatementAsync(DebugSession session, string path, int line)
    {
        UiThread.Check();
        string? problem = null;
        try
        {
            if (session.StoppedThread is not int thread)
            {
                problem = "No thread is paused";
            }
            else if (await session.GotoTargetAsync(path, Built(path, line) + 1) is not { } target)
            {
                problem = $"Line {line + 1} is not in the method the program is paused in";
            }
            else
            {
                await session.GotoAsync(thread, target);
            }
        }
        catch (DapException error)
        {
            problem = error.Reason;
        }
        if (problem is not null)
        {
            Message?.Invoke(this, $"Set Next Statement: {problem}");
        }
        return problem;
    }

    /// <summary>A file opened in a tab: its breakpoints, and the stop shown in it, follow its edits, and its gutter shows them.</summary>
    public void Attach(OpenDocument document)
    {
        UiThread.Check();
        EventHandler<DocumentChangedEventArgs> follow = (_, change) =>
        {
            // The built lines first: a breakpoint's move asks them what it now asks of the debugger.
            lines?.Follow(document.Path, change);
            Breakpoints.Follow(document.Path, change);
            FollowStop(document, change);
        };
        following[document] = follow;
        document.Text.Changed += follow;
        UpdateMarks(document);
    }

    public void Detach(OpenDocument document)
    {
        UiThread.Check();
        if (following.Remove(document, out EventHandler<DocumentChangedEventArgs>? follow))
        {
            document.Text.Changed -= follow;
        }
    }

    /// <summary>The value of the expression under the pointer while paused (a.b.c up to the word
    /// hovered), in the chosen frame; null when there is none or it does not evaluate.</summary>
    public async Task<string?> ValueAt(int offset, CancellationToken cancellation)
    {
        UiThread.Check();
        if (Session is not { State: DebugState.Paused } session || frame is null || ExpressionAt(editors.CodeEditor.Document.Snapshot, offset) is not { } expression)
        {
            return null;
        }
        try
        {
            EvaluateResponse value = await session.EvaluateAsync(expression, frame.Id, "hover").WaitAsync(cancellation);
            return $"{expression} = {(value.Type is { Length: > 0 } type && value.Result != type ? $"{{{type}}} " : "")}{value.Result}";
        }
        catch (DapException)
        {
            return null;
        }
    }

    /// <summary>The member access chain ending at the word around an offset: "this.name" for "name"
    /// in "this.name.Length", as Rider evaluates a hover.</summary>
    public static string? ExpressionAt(TextSnapshot text, int offset)
    {
        if (offset >= text.Length || !IsWordChar(text[offset]))
        {
            if (offset == 0 || offset > text.Length || !IsWordChar(text[offset - 1]))
            {
                return null;
            }
            offset--;
        }
        int end = offset;
        while (end < text.Length && IsWordChar(text[end]))
        {
            end++;
        }
        int start = offset;
        while (true)
        {
            while (start > 0 && IsWordChar(text[start - 1]))
            {
                start--;
            }
            if (start > 1 && text[start - 1] == '.' && IsWordChar(text[start - 2]))
            {
                start--;
                continue;
            }
            break;
        }
        string expression = text.GetText(start, end - start);
        return expression.Length == 0 || char.IsAsciiDigit(expression[0]) || Keywords.Contains(expression) ? null : expression;
    }

    private static readonly HashSet<string> Keywords =
    [
        "var", "int", "long", "string", "bool", "double", "float", "decimal", "char", "byte", "object", "void", "class",
        "struct", "record", "interface", "enum", "namespace", "using", "public", "private", "protected", "internal",
        "static", "readonly", "const", "new", "return", "if", "else", "for", "foreach", "while", "do", "switch", "case",
        "break", "continue", "in", "is", "as", "await", "async", "true", "false", "null", "throw", "try", "catch", "finally",
    ];

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private async Task OnStateChanged(DebugSession session, string name)
    {
        if (session != Session)
        {
            return;
        }
        // What the program wrote before it stopped or ended is shown before the stop or the end is.
        if (session.State == DebugState.Ended)
        {
            view.Console.Input = null;
            view.Console.Sink.End(ConsoleStream.Output);
            view.Console.Sink.End(ConsoleStream.Error);
            view.Console.Sink.End(ConsoleStream.System);
        }
        view.Console.Flush();
        view.SetState(session.State);
        // Every stop is new and every run lets the last one's frames and values go: none is asked of again.
        shown++;
        frame = null;
        clearing.Stop();
        switch (session.State)
        {
            case DebugState.Paused:
                int stop = shown;
                await ShowStop(session);
                if (stop != shown)
                {
                    break;
                }
                Message?.Invoke(this, stopException is { } thrown ? $"{name}: paused on {thrown.Text}"
                    : undescribed is { } why ? $"{name}: paused on an exception the debugger could not describe ({why})"
                    : $"{name}: paused ({session.StopReason})");
                break;
            case DebugState.Running:
                executionPoint = null;
                stopException = null;
                UpdateAllMarks();
                ClearInlineValues();
                clearing.Start();
                Message?.Invoke(this, $"{name}: running");
                break;
            case DebugState.Ended:
                executionPoint = null;
                lines = null;
                UpdateAllMarks();
                ClearInlineValues();
                view.ShowFrames([], null, [], null);
                view.Tree.Show([]);
                view.SetState(DebugState.Ended);
                Message?.Invoke(this, session.Failure is { } failure
                    ? $"Debugging {name} ended: {failure}"
                    : $"Debugging {name} ended{(session.ExitCode is int code ? $" with exit code {code}" : "")}");
                break;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows a stop: its exception described, then its thread's frames and the top one's values. An exception the
    /// debugger cannot describe (a getter of its own that never returns) still shows where the program stopped.</summary>
    private async Task ShowStop(DebugSession session)
    {
        if (session.StoppedThread is not int thread)
        {
            return;
        }
        int stop = shown;
        stopException = null;
        undescribed = null;
        if (session.StopReason == "exception")
        {
            try
            {
                ExceptionInfoResponse info = await session.ExceptionInfoAsync(thread);
                IReadOnlyList<DapStackFrame> stack = await session.StackTraceAsync(thread);
                DapStackFrame? thrown = stack.FirstOrDefault(f => f.Source?.Path is not null);
                stopException = (Describe(info), thrown?.Id ?? -1);
            }
            catch (DapException error)
            {
                undescribed = error.Reason;
            }
            if (stop != shown)
            {
                return;
            }
        }
        try
        {
            await ChooseThread(thread);
        }
        catch (DapException error)
        {
            Message?.Invoke(this, $"The debugger could not show the stop: {error.Reason}");
        }
    }

    private async Task ChooseThread(int thread)
    {
        if (Session is not { State: DebugState.Paused } session)
        {
            return;
        }
        int stop = shown;
        IReadOnlyList<DapThread> threads = await session.ThreadsAsync();
        IReadOnlyList<DapStackFrame> stack = await session.StackTraceAsync(thread);
        if (stop != shown)
        {
            return;
        }
        DapStackFrame? top = stack.FirstOrDefault(f => f.Source?.Path is not null) ?? stack.FirstOrDefault();
        executionPoint = thread == session.StoppedThread && top?.Source?.Path is { } path ? (path, top.Line - 1) : executionPoint;
        view.ShowFrames(threads, threads.FirstOrDefault(t => t.Id == thread), stack, top);
        UpdateAllMarks();
        if (top is not null)
        {
            await ChooseFrame(top, navigate: true);
        }
    }

    private async Task ChooseFrame(DapStackFrame chosen, bool navigate)
    {
        if (Session is not { State: DebugState.Paused })
        {
            return;
        }
        frame = chosen;
        if (navigate && chosen.Source?.Path is { } path && File.Exists(path))
        {
            Navigate?.Invoke(this, (path, new TextPosition(Current(path, Math.Max(0, chosen.Line - 1)), Math.Max(0, chosen.Column - 1))));
        }
        await ShowVariables();
    }

    /// <summary>The chosen frame's variables, under its watches and the last result evaluated.</summary>
    private async Task ShowVariables()
    {
        UiThread.Check();
        if (Session is not { State: DebugState.Paused } session || frame is not { } current)
        {
            return;
        }
        List<VariableNode> rows = [];
        foreach (string watch in watches)
        {
            rows.Add(await Evaluated(session, current, watch, VariableRole.Watch));
        }
        if (lastResult is { } result)
        {
            rows.Add(await Evaluated(session, current, result.Expression, VariableRole.Result));
        }
        Dictionary<string, string> values = [];
        try
        {
            IReadOnlyList<DapScope> scopes = await session.ScopesAsync(current.Id);
            foreach (DapScope scope in scopes)
            {
                IReadOnlyList<DapVariable> variables = await session.VariablesAsync(scope.VariablesReference);
                foreach (DapVariable variable in variables)
                {
                    values.TryAdd(variable.Name, variable.Value);
                }
                if (scopes.Count == 1)
                {
                    // One scope (the locals) needs no node of its own, as Rider shows them.
                    rows.AddRange(variables.Select(v => VariableNode.From(v, 0)));
                }
                else
                {
                    rows.Add(new VariableNode(scope.Name, string.Empty, null, scope.VariablesReference, 0, VariableRole.Scope));
                }
            }
        }
        catch (DapException error)
        {
            rows.Add(new VariableNode("error", error.Reason, null, 0, 0, VariableRole.Error));
        }
        if (frame == current)
        {
            view.Tree.Show(rows);
            ShowInlineValues(current, values);
        }
    }

    /// <summary>The chosen frame's values after the lines of its method, in its file and no other.</summary>
    private void ShowInlineValues(DapStackFrame current, IReadOnlyDictionary<string, string> values)
    {
        string? path = current.Source?.Path is { } source ? PathNames.Canonical(source) : null;
        foreach (OpenDocument document in editors.Documents)
        {
            if (document.Path != path)
            {
                document.LineAnnotations = [];
                continue;
            }
            int line = Current(document.Path, current.Line - 1);
            IReadOnlyList<LineAnnotation> inline = document.Syntax is { } syntax ? InlineValues.For(syntax.Current, line, values) : [];
            document.LineAnnotations = stopException is { } thrown && thrown.FrameId == current.Id
                ? [new LineAnnotation(line, thrown.Text, LineAnnotationKind.Exception), .. inline]
                : inline;
        }
    }

    /// <summary>An exception as Rider's stop names it: its type without the namespace, and its message.</summary>
    public static string Describe(ExceptionInfoResponse info)
    {
        string type = info.Details?.TypeName ?? info.ExceptionId.Replace("CLR/", "", StringComparison.Ordinal);
        string shortType = type[(type.LastIndexOf('.') + 1)..];
        string? message = info.Details?.Message ?? info.Description;
        return string.IsNullOrEmpty(message) ? shortType : $"{shortType}: {message}";
    }

    private void ClearInlineValues()
    {
        foreach (OpenDocument document in editors.Documents)
        {
            document.LineAnnotations = [];
        }
    }

    private static async Task<VariableNode> Evaluated(DebugSession session, DapStackFrame frame, string expression, VariableRole role)
    {
        try
        {
            EvaluateResponse value = await session.EvaluateAsync(expression, frame.Id, role == VariableRole.Watch ? "watch" : "repl");
            return new VariableNode(expression, value.Result, value.Type, value.VariablesReference, 0, role);
        }
        catch (DapException error)
        {
            return new VariableNode(expression, error.Reason, null, 0, 0, role == VariableRole.Watch ? VariableRole.Watch : VariableRole.Error);
        }
    }

    /// <summary>Shows the chosen frame's variables and inline values again, as a value changed.</summary>
    public Task RefreshAsync() => ShowVariables();

    /// <summary>Set Value: what a row's expression names takes the value typed (a C# expression), and the frame's
    /// variables and inline values show what it now is; a refusal is said in the status bar.</summary>
    public async Task SetValue(VariableNode node, string value)
    {
        UiThread.Check();
        node.IsEditing = false;
        if (Session is not { State: DebugState.Paused } session || frame is not { } current || node.Expression is not { } expression)
        {
            return;
        }
        try
        {
            await session.SetExpressionAsync(expression, value, current.Id);
        }
        catch (DapException error)
        {
            Message?.Invoke(this, $"Could not set {expression}: {error.Reason}");
        }
        await ShowVariables();
    }

    private async Task Evaluate(string expression, bool watch)
    {
        if (watch)
        {
            if (!watches.Contains(expression))
            {
                watches.Add(expression);
            }
        }
        else
        {
            lastResult = (expression, string.Empty);
        }
        await ShowVariables();
    }

    private string SelectedOrWord()
    {
        CodeEditor editor = editors.CodeEditor;
        Selection primary = editor.Selections.Primary;
        TextSnapshot text = editor.Document.Snapshot;
        return !primary.IsEmpty ? text.GetText(primary.Start, primary.Length) : ExpressionAt(text, primary.Caret) ?? string.Empty;
    }

    private void UpdateAllMarks()
    {
        foreach (OpenDocument document in editors.Documents)
        {
            UpdateMarks(document);
        }
    }

    private void UpdateMarks(string path)
    {
        foreach (OpenDocument document in editors.Documents.Where(d => d.Path == path))
        {
            UpdateMarks(document);
        }
    }

    private void UpdateMarks(OpenDocument document)
    {
        bool debugging = IsDebugging;
        List<LineMark> marks = [.. Breakpoints.In(document.Path).Select(b => new LineMark(b.Line, b switch
        {
            { Enabled: false } => LineMarkKind.BreakpointDisabled,
            { Status: BreakpointStatus.Bound } when debugging => LineMarkKind.BreakpointBound,
            { Status: BreakpointStatus.Invalid } when debugging => LineMarkKind.BreakpointInvalid,
            _ => LineMarkKind.Breakpoint,
        }, Conditional: b.Condition is not null || b.HitCondition is not null, Tracepoint: !b.Suspend))];
        if (executionPoint is { } point && point.Path == document.Path)
        {
            marks.Add(new LineMark(Current(point.Path, point.Line), LineMarkKind.ExecutionPoint));
        }
        document.LineMarks = marks;
    }

    /// <summary>Carries the stop shown in a file through an edit to it, as Rider's execution point and inline values go
    /// with the text: the arrow from the built lines, the values a line at a time.</summary>
    private void FollowStop(OpenDocument document, DocumentChangedEventArgs change)
    {
        if (executionPoint is { } point && point.Path == document.Path)
        {
            UpdateMarks(document);
        }
        if (document.LineAnnotations.Count > 0)
        {
            document.LineAnnotations = [.. document.LineAnnotations.Select(a => a with { Line = TextEditing.MapLine(change.Before, change.Edits, change.After, a.Line) })];
        }
    }

    /// <summary>Where a built line of a file is now, while a session runs.</summary>
    private int Current(string path, int line) => lines?.ToCurrent(path, line) ?? line;

    /// <summary>The built line a line of a file has, while a session runs.</summary>
    private int Built(string path, int line) => lines?.ToBuilt(path, line) ?? line;
}
