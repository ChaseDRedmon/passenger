using System.Diagnostics;
using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

[Collection(DebugSession.Collection)]
public class StepTests(ITestOutputHelper testOutputHelper)
{
    private const string Looping = "LoopingConsoleApp";

    [Fact]
    public async Task SharpDbgCli_StepRequests_Returns_StoppedEventsAtCorrectLocation()
    {
        bool startSuspended = true;

        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id)
            .WaitForInitializedEvent();
        debugProtocolHost
            .WithBreakpointsRequest(20)
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo = stoppedEvent.ReadStopInfo();
        Assert.EndsWith("MyClass.cs", stopInfo.filePath);
        Assert.Equal(20, stopInfo.line);

        StoppedEvent stoppedEvent2 = await debugProtocolHost
            .WithStepInRequest(stoppedEvent.ThreadId!.Value)
            .WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo2 = stoppedEvent2.ReadStopInfo();
        Assert.EndsWith("AnotherClass.cs", stopInfo2.filePath);
        Assert.Equal(7, stopInfo2.line);

        StoppedEvent stoppedEvent3 = await debugProtocolHost
            .WithStepOutRequest(stoppedEvent.ThreadId!.Value)
            .WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo3 = stoppedEvent3.ReadStopInfo();
        // Stepping out should land us back on the same line as the method we just stepped out of
        Assert.EndsWith("MyClass.cs", stopInfo3.filePath);
        Assert.Equal(20, stopInfo3.line);

        // Continue so we loop and are back at the breakpoint
        StoppedEvent stoppedEvent4 = await debugProtocolHost.WithContinueRequest().WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo4 = stoppedEvent4.ReadStopInfo();
        Assert.EndsWith("MyClass.cs", stopInfo4.filePath);
        Assert.Equal(20, stopInfo4.line);

        // Now, put a breakpoint inside AnotherMethod, and step over. We should still be in AnotherClass.cs
        debugProtocolHost.WithBreakpointsRequest(8, Debuggees.Source("DebuggableConsoleApp", "Namespace1", "AnotherClass.cs"));

        StoppedEvent stoppedEvent5 = await debugProtocolHost.WithStepOverRequest(stoppedEvent.ThreadId!.Value).WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo5 = stoppedEvent5.ReadStopInfo();
        Assert.EndsWith("AnotherClass.cs", stopInfo5.filePath);
        Assert.Equal(8, stopInfo5.line);

        StoppedEvent stoppedEvent6 = await debugProtocolHost.WithStepOverRequest(stoppedEvent.ThreadId!.Value).WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo6 = stoppedEvent6.ReadStopInfo();
        Assert.EndsWith("AnotherClass.cs", stopInfo6.filePath);
        Assert.Equal(9, stopInfo6.line);

        StoppedEvent stoppedEvent7 = await debugProtocolHost.WithContinueRequest().WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo7 = stoppedEvent7.ReadStopInfo();
        Assert.EndsWith("MyClass.cs", stopInfo7.filePath);
        Assert.Equal(20, stopInfo7.line);

        // breakpoint on a line that would F11 into unmapped code
        debugProtocolHost.WithBreakpointsRequest(13, Debuggees.Source("DebuggableConsoleApp", "Namespace1", "AnotherClass.cs"));
        StoppedEvent stoppedEvent8 = await debugProtocolHost.WithContinueRequest().WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo8 = stoppedEvent8.ReadStopInfo();
        Assert.EndsWith("AnotherClass.cs", stopInfo8.filePath);
        Assert.Equal(13, stopInfo8.line);

        // ensure that we do not receive stop info with no source. StepIn with JMC false, landing in non-user code, continues and stops at the next user code
        StoppedEvent stoppedEvent9 = await debugProtocolHost.WithStepInRequest(stoppedEvent.ThreadId!.Value).WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo9 = stoppedEvent9.ReadStopInfo();
        Assert.EndsWith("AnotherClass.cs", stopInfo9.filePath);
        Assert.Equal(14, stopInfo9.line);

        List<int> threadIds = [stoppedEvent.ThreadId!.Value, stoppedEvent2.ThreadId!.Value, stoppedEvent3.ThreadId!.Value, stoppedEvent4.ThreadId!.Value, stoppedEvent5.ThreadId!.Value, stoppedEvent6.ThreadId!.Value, stoppedEvent7.ThreadId!.Value, stoppedEvent8.ThreadId!.Value, stoppedEvent9.ThreadId!.Value];
        Assert.Single(threadIds.Distinct());
    }

    [Fact]
    public async Task SharpDbgCli_StepRequests_StepIntoModuleWithoutSymbols_Decompiles()
    {
        bool startSuspended = true;

        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id, false)
            .WaitForInitializedEvent();
        debugProtocolHost
            .WithBreakpointsRequest(15)
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo = stoppedEvent.ReadStopInfo();
        Assert.EndsWith("MyClass.cs", stopInfo.filePath);
        Assert.Equal(15, stopInfo.line);

        StoppedEvent stoppedEvent2 = await debugProtocolHost.WithStepInRequest(stoppedEvent.ThreadId!.Value).WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo2 = stoppedEvent2.ReadStopInfo();
        Assert.EndsWith("Console.cs", stopInfo2.filePath);
        // The decompiled code's line: a newer ICSharpCode.Decompiler may move it (the cache keeps each version's apart).
        Assert.Equal(1219, stopInfo2.line);
    }

    [Fact]
    public async Task SharpDbgCli_Continue_StopsAtBreakpointOnMultilineSwitchExpression()
    {
        bool startSuspended = true;

        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id, false)
            .WaitForInitializedEvent();
        debugProtocolHost
            .WithBreakpointsRequest([7, 9], Debuggees.Source("DebuggableConsoleApp", "MultilineSwitchInMethodCall.cs"))
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo = stoppedEvent.ReadStopInfo();
        Assert.EndsWith("MultilineSwitchInMethodCall.cs", stopInfo.filePath);
        Assert.Equal(7, stopInfo.line);

        StoppedEvent stoppedEvent2 = await debugProtocolHost.WithContinueRequest().WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo2 = stoppedEvent2.ReadStopInfo();
        Assert.EndsWith("MultilineSwitchInMethodCall.cs", stopInfo2.filePath);
        Assert.Equal(9, stopInfo2.line);
    }

    [Fact]
    // Testing behaviour that suppresses a breakpoint if a Step lands at the same location
    public async Task SharpDbgCli_StepOverToBreakpointedLine_StepOver_StopsAtNextLine()
    {
        bool startSuspended = true;

        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id, false)
            .WaitForInitializedEvent();
        debugProtocolHost
            .WithBreakpointsRequest([25, 26], Debuggees.Source("DebuggableConsoleApp", "Program.cs"))
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo = stoppedEvent.ReadStopInfo();
        Assert.EndsWith("Program.cs", stopInfo.filePath);
        Assert.Equal(25, stopInfo.line);

        StoppedEvent stoppedEvent2 = await debugProtocolHost.WithStepOverRequest(stoppedEvent.ThreadId!.Value).WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo2 = stoppedEvent2.ReadStopInfo();
        Assert.EndsWith("Program.cs", stopInfo2.filePath);
        Assert.Equal(26, stopInfo2.line);

        StoppedEvent stoppedEvent3 = await debugProtocolHost.WithStepOverRequest(stoppedEvent.ThreadId!.Value).WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo3 = stoppedEvent3.ReadStopInfo();
        Assert.EndsWith("Program.cs", stopInfo3.filePath);
        Assert.Equal(27, stopInfo3.line);
    }

    [Fact]
    public async Task A_step_over_goes_on_past_another_threads_breakpoint_whose_condition_is_false()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await StepOverWhileAnotherThreadPasses(host, "step", "step over the call while another thread passes", "a step lost runs on to here",
            new DapSourceBreakpoint(Debuggees.Line(Looping, "passed by another thread"), Condition: "pass < 0"));
        AssertStepEnded(await host.WaitForStoppedEvent(), "the call's step ends here");
    }

    [Fact]
    public async Task A_step_over_goes_on_past_another_threads_tracepoint_which_logs_meanwhile()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await StepOverWhileAnotherThreadPasses(host, "step", "step over the call while another thread passes", "a step lost runs on to here",
            new DapSourceBreakpoint(Debuggees.Line(Looping, "passed by another thread"), LogMessage: "pass {pass}"));
        await host.WaitForEvent<OutputEvent>(output => output.Output.StartsWith("pass ", StringComparison.Ordinal));
        AssertStepEnded(await host.WaitForStoppedEvent(), "the call's step ends here");
    }

    [Fact]
    public async Task A_step_over_a_call_goes_on_past_a_breakpoint_inside_it_whose_condition_is_false()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await StepOverWhileAnotherThreadPasses(host, "step", "step over the call while another thread passes", "a step lost runs on to here",
            new DapSourceBreakpoint(Debuggees.Line(Looping, "wait for the other thread"), Condition: "times == 0"));
        AssertStepEnded(await host.WaitForStoppedEvent(), "the call's step ends here");
    }

    [Fact]
    public async Task A_step_over_an_await_goes_on_past_another_threads_breakpoint_whose_condition_is_false()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await StepOverWhileAnotherThreadPasses(host, "await", "step over the await while another thread passes", "a step over the await lost runs on to here",
            new DapSourceBreakpoint(Debuggees.Line(Looping, "passed by another thread"), Condition: "pass < 0"));
        AssertStepEnded(await host.WaitForStoppedEvent(), "the await's step ends here");
    }

    [Fact]
    public async Task A_step_over_an_await_stops_at_its_next_line_though_asking_the_builder_which_run_resumed_would_wait_on_a_paused_thread()
    {
        // The method's builder answers which run of it resumed only once the passing thread has passed again, which never
        // happens while an evaluation runs alone, as one waiting on what a paused thread holds never ends: the step tells
        // its run apart without running the program.
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await StepOverWhileAnotherThreadPasses(host, "waiting", "step over the await whose builder waits", "a step over the waiting builder's await lost runs on to here",
            new DapSourceBreakpoint(Debuggees.Line(Looping, "passed by another thread"), Condition: "pass < 0"));
        AssertStepEnded(await host.WaitForStoppedEvent(), "the waiting builder's step ends here");
    }

    [Fact]
    public async Task A_step_over_an_await_stops_at_its_next_line_though_asking_the_builder_which_run_leaves_would_wait_on_a_paused_thread()
    {
        // As above, asked as the stepping thread leaves the method at the await, where asking Task's own builder was seen to
        // wait until the other threads ran: the step lost there kept its stepper, which took the next breakpoint for its own.
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await StepOverWhileAnotherThreadPasses(host, "waiting first", "step over the await whose builder waits", "a step over the waiting builder's await lost runs on to here",
            new DapSourceBreakpoint(Debuggees.Line(Looping, "passed by another thread"), Condition: "pass < 0"));
        AssertStepEnded(await host.WaitForStoppedEvent(), "the waiting builder's step ends here");
    }

    [Fact]
    public async Task A_step_over_an_await_whose_method_goes_on_on_another_thread_shows_what_the_statement_called_there_returned()
    {
        // The statement's call after the await is made on the thread the step goes on on, not the one that stepped.
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await StepOverWhileAnotherThreadPasses(host, "elsewhere", "step over the await that goes on elsewhere",
            "a step over the await that goes on elsewhere lost runs on to here",
            new DapSourceBreakpoint(Debuggees.Line(Looping, "passed by another thread"), Condition: "pass < 0"));
        StoppedEvent stepped = await host.WaitForStoppedEvent();
        AssertStepEnded(stepped, "the step over the await that goes on elsewhere ends here");
        Assert.True(host.Locals(stepped).Any(v => (v.Name, v.Value) == ("Say() returned", "\"said elsewhere\"")),
            "Variables leaves out what the statement's call after the await returned, on the thread the method went on on");
    }

    [Fact]
    public async Task A_step_of_a_thread_the_program_does_not_have_is_refused_rather_than_answered_as_taken()
    {
        using DebugSession host = await AttachToBreakpoint(20);
        StoppedEvent stopped = await host.WaitForStoppedEvent();

        DapException refused = Assert.Throws<DapException>(() => host.WithStepOverRequest(999_999));
        Assert.Contains("no thread 999999", refused.Reason, StringComparison.Ordinal);
        DapException refusedOut = Assert.Throws<DapException>(() => host.WithStepOutRequest(999_999));
        Assert.Contains("no thread 999999", refusedOut.Reason, StringComparison.Ordinal);

        // Nothing went: the thread that stopped steps on from where it was.
        StoppedEvent stepped = await host.WithStepOverRequest(stopped.ThreadId!.Value).WaitForStoppedEvent();
        Assert.Equal(("step", 21), (stepped.Reason, stepped.Line));
    }

    [Fact]
    public async Task A_frame_or_value_from_before_a_step_is_refused_rather_than_naming_one_of_the_new_stops()
    {
        using DebugSession host = await AttachToBreakpoint(20);
        StoppedEvent stopped = await host.WaitForStoppedEvent();
        int thread = stopped.ThreadId!.Value;
        DapStackFrame before = host.GetTopStackFrame(thread);
        host.WithScopesRequest(before.Id, out ScopesResponse scopes);
        int locals = scopes.Scopes.Single().VariablesReference;
        host.WithVariablesRequest(locals, out List<DapVariable> variables);
        int structVar = variables.Single(v => v.Name == "structVar").VariablesReference;
        Assert.True(structVar > 0, "structVar has no members to open");

        // Into AnotherClass.MyStaticMethod, whose frame, scope and values are asked for as the IDE asks at a stop.
        StoppedEvent stepped = await host.WithStepInRequest(thread).WaitForStoppedEvent();
        Assert.EndsWith("AnotherClass.cs", stepped.ReadStopInfo().filePath);
        DapStackFrame after = host.GetTopStackFrame(thread);
        host.WithScopesRequest(after.Id, out ScopesResponse afterScopes);
        host.WithVariablesRequest(afterScopes.Scopes.Single().VariablesReference, out List<DapVariable> now);
        Assert.Contains(now, v => v.Name == "myList");

        Assert.NotEqual(before.Id, after.Id);
        foreach (Action stale in new Action[]
        {
            () => host.WithVariablesRequest(locals, out _),
            () => host.WithVariablesRequest(structVar, out _),
            () => host.WithScopesRequest(before.Id, out _),
            () => host.WithEvaluateRequest(before.Id, "myInt", out _),
        })
        {
            DapException refused = Assert.Throws<DapException>(stale);
            Assert.Contains("earlier stop", refused.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_step_on_one_thread_and_another_threads_breakpoint_hit_as_it_ends_both_stop()
    {
        // Each turn the stepping thread lets the racing one go in the call it steps over, and the racing one passes its
        // breakpoint at once: the step's end and the hit come together, either first. The hit is never passed over, and the
        // step that had ended stops first; a hit that comes before the step has ended ends it, as any stop does.
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        (int letGo, int stepped, int passed) = RaceLines;
        StoppedEvent stop = await LaunchRace(host, [new DapSourceBreakpoint(letGo), new DapSourceBreakpoint(passed)]);
        int stepping = stop.ThreadId!.Value;
        int both = 0;
        for (int turn = 1; turn <= RaceTurns; turn++)
        {
            Assert.Equal(("breakpoint", letGo, stepping), (stop.Reason, stop.Line, stop.ThreadId));
            List<StoppedEvent> stops = [await host.WithStepOverRequest(stepping).WaitForStoppedEvent()];
            while ((stop = await host.WithContinueRequest().WaitForStoppedEvent()).Line != letGo)
            {
                stops.Add(stop);
            }
            string seen = string.Join(", ", stops.Select(s => $"{s.Reason} at line {s.Line} on thread {s.ThreadId}"));
            Assert.True(stops.Count(s => (s.Reason, s.Line) == ("breakpoint", passed)) == 1,
                $"Turn {turn}: the racing thread's breakpoint was passed over as the step ended on the other thread; the stops were {seen}");
            bool stepStopped = stops.Any(s => (s.Reason, s.Line, s.ThreadId) == ("step", stepped, stepping));
            Assert.True(stops[0].Line == passed || stepStopped,
                $"Turn {turn}: the step was lost to the racing thread's breakpoint that stopped after it ended; the stops were {seen}");
            Assert.True(!stepStopped || stops[0].Reason == "step",
                $"Turn {turn}: the racing thread's breakpoint stopped before the step that had already ended; the stops were {seen}");
            both += stepStopped ? 1 : 0;
        }
        testOutputHelper.WriteLine($"Both stops were seen in {both} of {RaceTurns} turns");
    }

    [Fact]
    public async Task A_step_that_ends_while_another_threads_exception_breakpoint_tries_its_condition_stops_once_the_condition_is_tried()
    {
        // The racing thread throws as it is let go, and its exception finds its handler as the step over the call that
        // let it go ends, sooner or later each turn: the exception breakpoint's condition, which runs a getter and never
        // holds, is tried without the step's end stopping the program part way through it.
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        (int letGo, int stepped, _) = RaceLines;
        DapExceptionOptions raced = new([new DapExceptionPathSegment(["CLR"]), new DapExceptionPathSegment(["RaceException"])], "always")
        {
            Condition = "$exception.Message.Length < 0",
        };
        StoppedEvent stop = await LaunchRace(host, [new DapSourceBreakpoint(letGo)], raced);
        int stepping = stop.ThreadId!.Value;
        for (int turn = 1; turn <= ThrowingTurns; turn++)
        {
            Assert.Equal(("breakpoint", letGo, stepping), (stop.Reason, stop.Line, stop.ThreadId));
            StoppedEvent step = await host.WithStepOverRequest(stepping).WaitForStoppedEvent();
            Assert.True((step.Reason, step.Line, step.ThreadId) == ("step", stepped, stepping),
                $"Turn {turn}: the step over the call stopped for {step.Reason} at line {step.Line} on thread {step.ThreadId}, not at line {stepped}");
            stop = await host.WithContinueRequest().WaitForStoppedEvent();
            Assert.True(stop.Line == letGo,
                $"Turn {turn}: the racing thread's exception stopped the program ({stop.Reason} at line {stop.Line} on thread {stop.ThreadId}), its "
                + "condition never holding: it could not be tried, the step's end having stopped the program while its getter ran");
        }
    }

    /// <summary>How many times a race between a step's end and another thread's breakpoint is run: each turn goes either way.</summary>
    private const int RaceTurns = 30;

    /// <summary>The throwing race's turns: its forty lengths of wait twice over, so the exception's handler is found before,
    /// as and after the step ends.</summary>
    private const int ThrowingTurns = 80;

    /// <summary>LoopingConsoleApp's lines in its race: where the stepping thread lets the racing one go, where the step over
    /// that ends, and the line the racing thread passes once let go.</summary>
    private static (int LetGo, int Stepped, int Passed) RaceLines => (Debuggees.Line(Looping, "step over the call that lets the racing thread go"),
        Debuggees.Line(Looping, "the step over the call that lets it go ends here"), Debuggees.Line(Looping, "passed by the racing thread"));

    /// <summary>LoopingConsoleApp launched racing with the breakpoints given, and throwing with the exception breakpoint when
    /// one is given; its first stop, the stepping thread at the line where it lets the racing thread go.</summary>
    private static async Task<StoppedEvent> LaunchRace(DebugSession host, List<DapSourceBreakpoint> breakpoints, DapExceptionOptions? exceptions = null)
    {
        string program = Debuggees.Program(Looping);
        host.WithInitializeRequest().Request("launch", new LaunchArguments
        {
            Program = program,
            Args = exceptions is null ? ["race"] : ["race", "throws"],
            Cwd = Path.GetDirectoryName(program),
            Console = "internalConsole",
            JustMyCode = true,
            StopAtEntry = false,
        });
        await host.WaitForInitializedEvent();
        if (exceptions is not null)
        {
            host.Request("setExceptionBreakpoints", new SetExceptionBreakpointsArguments([], ExceptionOptions: [exceptions]));
        }
        host.WithBreakpointsRequest(Debuggees.Source(Looping, "Program.cs"), breakpoints).WithConfigurationDoneRequest();
        return await host.WaitForStoppedEvent();
    }

    /// <summary>DebuggableConsoleApp attached to as it starts, with a breakpoint on a line of MyClass.cs.</summary>
    private async Task<DebugSession> AttachToBreakpoint(int line)
    {
        DebugSession host = DebugSession.ForAttach(testOutputHelper, startSuspended: true, out Process program);
        await host.WithInitializeRequest().WithAttachRequest(program.Id).WaitForInitializedEvent();
        host.WithBreakpointsRequest(line).WithConfigurationDoneRequest().WithOptionalResumeRuntime(program.Id, startSuspended: true);
        return host;
    }

    /// <summary>LoopingConsoleApp launched in a mode (its arguments, by spaces) where its first thread waits for a second to
    /// pass a line, stopped on the line that waits and stepping over it, with the other breakpoint given and one where a
    /// step lost runs on to.</summary>
    private static async Task StepOverWhileAnotherThreadPasses(DebugSession host, string mode, string from, string runsOnTo, DapSourceBreakpoint other)
    {
        string program = Debuggees.Program(Looping);
        host.WithInitializeRequest().Request("launch", new LaunchArguments
        {
            Program = program,
            Args = [.. mode.Split(' ')],
            Cwd = Path.GetDirectoryName(program),
            Console = "internalConsole",
            JustMyCode = true,
            StopAtEntry = false,
        });
        await host.WaitForInitializedEvent();
        host.WithBreakpointsRequest(Debuggees.Source(Looping, "Program.cs"),
            [new DapSourceBreakpoint(Debuggees.Line(Looping, from)), other, new DapSourceBreakpoint(Debuggees.Line(Looping, runsOnTo))])
            .WithConfigurationDoneRequest();
        StoppedEvent stopped = await host.WaitForStoppedEvent();
        Assert.Equal(Debuggees.Line(Looping, from), stopped.Line);
        host.WithStepOverRequest(stopped.ThreadId!.Value);
    }

    private static void AssertStepEnded(StoppedEvent stepped, string at)
    {
        int line = Debuggees.Line(Looping, at);
        Assert.True(stepped.Reason == "step" && stepped.Line == line,
            $"The step was lost to a breakpoint that did not stop, and the program ran on: it stopped for {stepped.Reason} at line {stepped.Line}, where the step would have stopped at line {line}");
    }
}
