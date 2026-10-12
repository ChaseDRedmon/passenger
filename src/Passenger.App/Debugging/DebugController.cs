using Passenger.App.Editors;
using Passenger.App.ToolWindows;
using Passenger.App.Workspace;
using Passenger.Dap;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Debugging;

/// <summary>The debugger in the IDE: the breakpoints in the gutter, a session's stops shown where
/// they are (the execution line, the Debug window's frames and variables), the stepping actions, and
/// a value when the pointer rests on an expression while paused.</summary>
public sealed class DebugController
{
    private readonly EditorArea editors;
    private readonly DebugView view;
    private readonly List<string> watches = [];
    private readonly Dictionary<OpenDocument, EventHandler<DocumentChangedEventArgs>> following = [];
    private (string Path, int Line)? executionPoint;
    private (string Expression, string Value)? lastResult;
    private DapStackFrame? frame;

    public DebugController(EditorArea editors, DebugView view)
    {
        this.editors = editors;
        this.view = view;
        Breakpoints.Changed += (_, path) => UpdateMarks(path);
        view.ActionRequested += (_, action) => ActionRequested?.Invoke(this, action);
        view.FrameChosen += (_, chosen) => _ = ChooseFrame(chosen, navigate: true);
        view.ThreadChosen += (_, thread) => _ = ChooseThread(thread.Id);
        view.EvaluateRequested += (_, request) => _ = Evaluate(request.Expression, request.Watch);
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
    public async Task StartAsync(DebugTarget target, string name)
    {
        if (IsDebugging)
        {
            await StopAsync();
        }
        view.Clear();
        executionPoint = null;
        frame = null;
        lastResult = null;
        view.SetState(DebugState.Starting);
        Message?.Invoke(this, $"Debugging {name}");
        try
        {
            DebugSession session = await DebugSession.LaunchAsync(target.Program, target.WorkingDirectory, [], Breakpoints, line => view.Console.Document.Append($"{line}\n"));
            Session = session;
            view.Tree.Load = reference => session.VariablesAsync(reference);
            session.Output += (_, output) => view.Console.Document.Append(output.Output);
            session.StateChanged += (_, _) => _ = OnStateChanged(session, name);
            await OnStateChanged(session, name);
        }
        catch (Exception error) when (error is DapException or IOException or InvalidOperationException or TimeoutException)
        {
            view.SetState(DebugState.Ended);
            Message?.Invoke(this, $"Could not debug {name}: {(error is DapException dap ? dap.Reason : error.Message)}");
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task StopAsync()
    {
        if (Session is { } session)
        {
            await session.StopAsync();
        }
    }

    /// <summary>Runs one of the debugger's actions (<c>app.debug.*</c>) on the active editor and the
    /// session. Resume without a session is the caller's Debug, as F5 is in Visual Studio's keymap.</summary>
    public void Run(string action)
    {
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
                _ = Session?.ContinueAsync();
                break;
            case AppActions.Pause:
                _ = Session?.PauseAsync();
                break;
            case AppActions.StepOver:
                _ = Session?.StepOverAsync();
                break;
            case AppActions.StepInto:
                _ = Session?.StepIntoAsync();
                break;
            case AppActions.StepOut:
                _ = Session?.StepOutAsync();
                break;
            case AppActions.RunToCursor when active is not null && Session is { State: DebugState.Paused } session:
                _ = session.RunToCursorAsync(active.Path, caretLine);
                break;
            case AppActions.ShowExecutionPoint when executionPoint is { } point:
                Navigate?.Invoke(this, (point.Path, new TextPosition(point.Line, 0)));
                break;
            case AppActions.Evaluate when Session is { State: DebugState.Paused }:
                view.FocusEvaluate(SelectedOrWord());
                break;
        }
    }

    /// <summary>A file opened in a tab: its breakpoints follow its edits, and its gutter shows them.</summary>
    public void Attach(OpenDocument document)
    {
        EventHandler<DocumentChangedEventArgs> follow = (_, change) => Breakpoints.Follow(document.Path, change);
        following[document] = follow;
        document.Text.Changed += follow;
        UpdateMarks(document);
    }

    public void Detach(OpenDocument document)
    {
        if (following.Remove(document, out EventHandler<DocumentChangedEventArgs>? follow))
        {
            document.Text.Changed -= follow;
        }
    }

    /// <summary>The value of the expression under the pointer while paused (a.b.c up to the word
    /// hovered), in the chosen frame; null when there is none or it does not evaluate.</summary>
    public async Task<string?> ValueAt(int offset, CancellationToken cancellation)
    {
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
        view.SetState(session.State);
        switch (session.State)
        {
            case DebugState.Paused:
                await ShowStop(session);
                Message?.Invoke(this, $"{name}: paused ({session.StopReason})");
                break;
            case DebugState.Running:
                executionPoint = null;
                UpdateAllMarks();
                Message?.Invoke(this, $"{name}: running");
                break;
            case DebugState.Ended:
                executionPoint = null;
                frame = null;
                UpdateAllMarks();
                view.ShowFrames([], null, [], null);
                view.Tree.Show([]);
                view.SetState(DebugState.Ended);
                Message?.Invoke(this, $"Debugging {name} ended{(session.ExitCode is int code ? $" with exit code {code}" : "")}");
                break;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ShowStop(DebugSession session)
    {
        if (session.StoppedThread is not int thread)
        {
            return;
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
        IReadOnlyList<DapThread> threads = await session.ThreadsAsync();
        IReadOnlyList<DapStackFrame> stack = await session.StackTraceAsync(thread);
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
        frame = chosen;
        if (navigate && chosen.Source?.Path is { } path && File.Exists(path))
        {
            Navigate?.Invoke(this, (path, new TextPosition(Math.Max(0, chosen.Line - 1), Math.Max(0, chosen.Column - 1))));
        }
        await ShowVariables();
    }

    /// <summary>The chosen frame's variables, under its watches and the last result evaluated.</summary>
    private async Task ShowVariables()
    {
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
        try
        {
            IReadOnlyList<DapScope> scopes = await session.ScopesAsync(current.Id);
            foreach (DapScope scope in scopes)
            {
                if (scopes.Count == 1)
                {
                    // One scope (the locals) needs no node of its own, as Rider shows them.
                    rows.AddRange((await session.VariablesAsync(scope.VariablesReference)).Select(v => VariableNode.From(v, 0)));
                }
                else
                {
                    rows.Add(new VariableNode(scope.Name, string.Empty, null, scope.VariablesReference, 0, VariableRole.Value));
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
        }))];
        if (executionPoint is { } point && point.Path == document.Path)
        {
            marks.Add(new LineMark(point.Line, LineMarkKind.ExecutionPoint));
        }
        document.LineMarks = marks;
    }
}
