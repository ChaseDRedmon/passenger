using Passenger.Dap;
using Passenger.Debugger;
using Passenger.Debugger.Models;
using Passenger.Debugger.Models.Response;
using Passenger.Debugger.PresentationHintModels;

namespace Passenger.DebugAdapter;

/// <summary>The engine behind the Debug Adapter Protocol. Each request runs under the engine's lock,
/// after the runtime events queued before it, so a request never sees the debuggee half way through
/// an event; each engine event goes out as the protocol's. Lines and columns are the client's choice
/// of base, the engine's own counting from one.</summary>
public sealed class DebugAdapter
{
    private readonly ManagedDebugger debugger;
    private readonly DapConnection connection;
    private readonly Action<string>? log;
    private bool linesStartAt1 = true;
    private int? stoppedThread;
    private bool columnsStartAt1 = true;
    private int shutdownRequested;

    public DebugAdapter(DapConnection connection, Action<string>? log = null)
    {
        this.connection = connection;
        this.log = log;
        debugger = new ManagedDebugger(log);
        Subscribe();
        Register();
    }

    /// <summary>Raised once a disconnect has been answered: the session is over and the process may end.
    /// A terminate is not the end, since a client may still disconnect after it. Raised on any thread.</summary>
    public event Action? ShutdownRequested;

    /// <summary>Lets the debuggee go when the client went away without disconnecting, under the engine's lock as a disconnect
    /// is, so the event loop is not amid one of the program's events. Does nothing after a disconnect, which let it go
    /// already. A request still holding the lock after 10 s (one that never ends) is not waited for longer.</summary>
    public async Task AbortAsync()
    {
        try
        {
            using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10));
            IDisposable? held = null;
            try
            {
                held = await debugger.DapRequestAndRuntimeEventLock.LockAsync(limit.Token);
                await debugger.DrainRuntimeEventQueue();
            }
            catch (OperationCanceledException)
            {
                log?.Invoke("Abort: a request still holds the engine after 10 s; the program is let go without waiting for it");
            }
            using (held)
            {
                debugger.Disconnect(true);
            }
        }
        catch (Exception error)
        {
            log?.Invoke($"Abort: {error}");
        }
    }

    private void Register()
    {
        connection.Handle<InitializeArguments, DapCapabilities>("initialize", Initialize, then: () => connection.SendEvent("initialized"));
        connection.Handle<LaunchArguments>("launch", Launch);
        connection.Handle<AttachArguments>("attach", Attach);
        connection.Handle<DapEmpty>("configurationDone", _ => Locked(() => debugger.ConfigurationDone()));
        connection.Handle<SetBreakpointsArguments, SetBreakpointsResponse>("setBreakpoints", SetBreakpoints);
        connection.Handle<SetFunctionBreakpointsArguments, SetBreakpointsResponse>("setFunctionBreakpoints", SetFunctionBreakpoints);
        connection.Handle<SetExceptionBreakpointsArguments>("setExceptionBreakpoints", SetExceptionBreakpoints);
        connection.Handle<DapEmpty, ThreadsResponse>("threads", async _ =>
        {
            List<(int id, string name)> threads = await Locked(() => debugger.GetThreads());
            return new ThreadsResponse([.. threads.Select(t => new DapThread(t.id, t.name))]);
        });
        connection.Handle<StackTraceArguments, StackTraceResponse>("stackTrace", async arguments =>
        {
            List<StackFrameInfo> frames = await Locked(() => debugger.GetStackTrace(arguments.ThreadId, arguments.StartFrame ?? 0, arguments.Levels));
            return new StackTraceResponse([.. frames.Select(ToProtocol)]);
        });
        connection.Handle<ResolveStackFrameArguments, ResolveStackFrameResponse>("resolveStackFrame", async arguments =>
            new ResolveStackFrameResponse(ToProtocol(await Locked(() => debugger.ResolveStackFrame(arguments.StackFrameId)))));
        connection.Handle<ScopesArguments, ScopesResponse>("scopes", async arguments =>
        {
            List<ScopeInfo> scopes = await Locked(() => debugger.GetScopes(arguments.FrameId));
            return new ScopesResponse([.. scopes.Select(s => new DapScope(s.Name, s.VariablesReference, s.Expensive))]);
        });
        connection.Handle<VariablesArguments, VariablesResponse>("variables", async arguments =>
        {
            List<VariableInfo> variables = await Locked(() => debugger.GetVariables(arguments.VariablesReference));
            return new VariablesResponse([.. variables.Select(v => new DapVariable
            {
                Name = v.Name,
                EvaluateName = v.Name,
                Value = v.Value,
                Type = v.Type,
                PresentationHint = ToProtocol(v.PresentationHint),
                VariablesReference = v.VariablesReference,
            })]);
        });
        connection.Handle<EvaluateArguments, EvaluateResponse>("evaluate", async arguments =>
        {
            VariableInfo value = await Locked(() => debugger.Evaluate(arguments.Expression, arguments.FrameId));
            return new EvaluateResponse
            {
                Result = value.Value,
                Type = value.Type,
                VariablesReference = value.VariablesReference,
                PresentationHint = ToProtocol(value.PresentationHint),
            };
        });
        // Set Next Statement: a target per line asked of, in the method the last stopped thread is in; its id is the
        // IL offset, which goto hands back.
        connection.Handle<GotoTargetsArguments, GotoTargetsResponse>("gotoTargets", async arguments =>
        {
            if (stoppedThread is not int thread || arguments.Source.Path is not { } path)
            {
                return new GotoTargetsResponse();
            }
            (int Line, int Column, int ILOffset)? target = await Locked(() => debugger.GotoTarget(thread, path, LineIn(arguments.Line)));
            return target is { } found
                ? new GotoTargetsResponse { Targets = [new GotoTarget(found.ILOffset, $"Line {found.Line}", LineOut(found.Line), ColumnOut(found.Column))] }
                : new GotoTargetsResponse();
        });
        connection.Handle<GotoArguments, object>("goto", async arguments =>
        {
            await Locked(() =>
            {
                debugger.SetNextStatement(arguments.ThreadId, arguments.TargetId);
                return 0;
            });
            return new object();
        });
        connection.Handle<SetExpressionArguments, SetExpressionResponse>("setExpression", async arguments =>
        {
            // The expression compiler compiles an assignment as it does any expression, and the interpreter writes it
            // through to the frame, the object or the array; the value is then read back as it now is.
            VariableInfo assigned = await Locked(() => debugger.Evaluate($"{arguments.Expression} = {arguments.Value}", arguments.FrameId));
            if (assigned.PresentationHint?.Attributes == AttributesValue.FailedEvaluation)
            {
                throw new InvalidOperationException(assigned.Value.StartsWith("error: ", StringComparison.Ordinal) ? assigned.Value["error: ".Length..] : assigned.Value);
            }
            VariableInfo now = await Locked(() => debugger.Evaluate(arguments.Expression, arguments.FrameId));
            return new SetExpressionResponse { Value = now.Value, Type = now.Type, VariablesReference = now.VariablesReference };
        });
        connection.Handle<ThreadArguments, ContinueResponse>("continue", async _ =>
        {
            await Locked(() => debugger.HandleContinueRequest());
            return new ContinueResponse(AllThreadsContinued: true);
        });
        connection.Handle<ThreadArguments>("next", arguments => Locked(() => debugger.StepNext(arguments.ThreadId)));
        connection.Handle<ThreadArguments>("stepIn", arguments => Locked(() => debugger.StepIn(arguments.ThreadId)));
        connection.Handle<ThreadArguments>("stepOut", arguments => Locked(() => debugger.StepOut(arguments.ThreadId)));
        connection.Handle<ThreadArguments>("pause", _ => Locked(() => debugger.Pause()));
        connection.Handle<DisconnectArguments>("disconnect", arguments => Locked(() => debugger.Disconnect(arguments.TerminateDebuggee ?? false)), then: RequestShutdown);
        connection.Handle<TerminateArguments>("terminate", _ => Locked(() => debugger.Terminate()));
        connection.Handle<ThreadArguments, ExceptionInfoResponse>("exceptionInfo", ExceptionInfo);
        // Not under the engine's lock: a program waiting for input may be running, and the write is all.
        connection.Handle<InputArguments>("input", arguments =>
        {
            debugger.WriteInput(arguments.Text, arguments.Close ?? false);
            return Task.CompletedTask;
        });
    }

    private void Subscribe()
    {
        debugger.OnStopped += (threadId, _) => stoppedThread = threadId;
        debugger.OnStopped2 += (threadId, _, _, _, _, _) => stoppedThread = threadId;
        debugger.OnStopped += (threadId, reason) => connection.SendEvent("stopped", new StoppedEvent
        {
            Reason = StopReason(reason),
            ThreadId = threadId,
            AllThreadsStopped = true,
        });
        debugger.OnStopped2 += (threadId, filePath, line, column, reason, hitBreakpointIds) => connection.SendEvent("stopped", new StoppedEvent
        {
            Reason = StopReason(reason),
            ThreadId = threadId,
            AllThreadsStopped = true,
            HitBreakpointIds = hitBreakpointIds,
            Source = new DapSource { Path = filePath },
            Line = line,
            Column = column,
        });
        debugger.OnBreakpointChanged += breakpoint => connection.SendEvent("breakpoint", new BreakpointEvent { Reason = "changed", Breakpoint = ToProtocol(breakpoint, isEvent: true) });
        // Told only when known: a process attached to on macOS or Linux tells its exit code to its parent alone.
        debugger.OnExited += exitCode =>
        {
            if (exitCode is int code)
            {
                connection.SendEvent("exited", new ExitedEvent(code));
            }
        };
        debugger.OnTerminated += () => connection.SendEvent("terminated", new TerminatedEvent());
        debugger.OnThreadStarted += threadId => connection.SendEvent("thread", new ThreadEvent("started", threadId));
        debugger.OnThreadExited += threadId => connection.SendEvent("thread", new ThreadEvent("exited", threadId));
        debugger.OnModuleLoaded += (id, name, path) => connection.SendEvent("module", new ModuleEvent("new", new DapModule(id, name, path)));
        debugger.OnModuleUnloaded += (id, name, path) => connection.SendEvent("module", new ModuleEvent("removed", new DapModule(id, name, path)));
        debugger.OnProcessStarted += (processId, name) => connection.SendEvent("process", new ProcessEvent(name, processId, IsLocalProcess: true, StartMethod: "launch"));
        debugger.OnAttached += processId => connection.SendEvent("process", new ProcessEvent($"process {processId}", processId, IsLocalProcess: true, StartMethod: "attach"));
        debugger.OnOutput += (output, isStdError) => connection.SendEvent("output", new OutputEvent(output, isStdError ? "stderr" : "stdout"));
        debugger.OnConsole += message => connection.SendEvent("output", new OutputEvent(message, "console"));
        debugger.SendRunInTerminalRequest += RunInTerminal;
    }

    private Task<DapCapabilities> Initialize(InitializeArguments arguments)
    {
        linesStartAt1 = arguments.LinesStartAt1 ?? true;
        columnsStartAt1 = arguments.ColumnsStartAt1 ?? true;
        return Task.FromResult(new DapCapabilities
        {
            SupportsConfigurationDoneRequest = true,
            SupportsFunctionBreakpoints = true,
            SupportsConditionalBreakpoints = true,
            SupportsHitConditionalBreakpoints = true,
            SupportsLogPoints = true,
            SupportsEvaluateForHovers = true,
            SupportsStepBack = false,
            SupportsSetVariable = false,
            SupportsSetExpression = true,
            SupportsRestartFrame = false,
            SupportsTerminateRequest = true,
            SupportsExceptionInfoRequest = true,
            SupportsExceptionFilterOptions = true,
            SupportsStepInTargetsRequest = false,
            SupportsGotoTargetsRequest = true,
            ExceptionBreakpointFilters =
            [
                new DapExceptionFilter("all", "All Exceptions", Default: false),
                new DapExceptionFilter("user-unhandled", "User-Unhandled Exceptions", Default: true),
            ],
        });
    }

    private async Task Launch(LaunchArguments arguments)
    {
        try
        {
            string program = string.IsNullOrEmpty(arguments.Program) ? throw new InvalidOperationException("Missing program path") : arguments.Program;
            List<string> args = [.. arguments.Args ?? []];
            LaunchRequestConsoleType console = arguments.Console switch
            {
                "integratedTerminal" => LaunchRequestConsoleType.IntegratedTerminal,
                "externalTerminal" => LaunchRequestConsoleType.ExternalTerminal,
                "internalConsole" or null => LaunchRequestConsoleType.InternalConsole,
                _ => throw new InvalidOperationException($"Invalid console type: '{arguments.Console}'"),
            };
            // A .dll runs under the host; an apphost or another executable runs itself.
            if (Path.GetExtension(program).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                args.Insert(0, program);
                program = "dotnet";
            }
            LaunchInfo launch = new()
            {
                Program = program,
                Arguments = args,
                Cwd = arguments.Cwd,
                Env = arguments.Env?.ToDictionary() ?? [],
                StopAtEntry = arguments.StopAtEntry ?? false,
                LaunchRequestConsoleType = console,
            };
            await Locked(() => debugger.Launch(launch, arguments.JustMyCode ?? true));
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Failed to launch: {error.Message}", error);
        }
    }

    private async Task Attach(AttachArguments arguments)
    {
        try
        {
            int processId = arguments.ProcessId ?? throw new InvalidOperationException("Missing process ID");
            RemoteAttachInfo? remote = null;
            if (arguments.CoreClrMobileDebuggerOptions is { } options)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(options.Address);
                ArgumentException.ThrowIfNullOrWhiteSpace(options.Platform);
                ArgumentException.ThrowIfNullOrWhiteSpace(options.MscordbiPath);
                ArgumentException.ThrowIfNullOrWhiteSpace(options.AssembliesPath);
                if (!File.Exists(options.MscordbiPath))
                {
                    throw new InvalidOperationException($"coreClrMobileDebuggerOptions.mscordbiPath does not exist: {options.MscordbiPath}");
                }
                if (!Directory.Exists(options.AssembliesPath))
                {
                    throw new InvalidOperationException($"coreClrMobileDebuggerOptions.assembliesPath does not exist: {options.AssembliesPath}");
                }
                remote = new RemoteAttachInfo
                {
                    Address = options.Address,
                    Port = options.Port ?? throw new InvalidOperationException("coreClrMobileDebuggerOptions.port is missing"),
                    Platform = options.Platform,
                    IsServer = options.IsServer ?? throw new InvalidOperationException("coreClrMobileDebuggerOptions.isServer is missing"),
                    MscordbiPath = options.MscordbiPath,
                    AssembliesPath = options.AssembliesPath,
                };
            }
            bool justMyCode = arguments.JustMyCode ?? true;
            await Locked(() =>
            {
                if (remote is null)
                {
                    debugger.Attach(processId, justMyCode);
                }
                else
                {
                    debugger.AttachRemote(remote, justMyCode);
                }
            });
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Failed to attach: {error.Message}", error);
        }
    }

    private async Task<SetBreakpointsResponse> SetBreakpoints(SetBreakpointsArguments arguments)
    {
        string path = arguments.Source?.Path ?? throw new InvalidOperationException("Missing source path");
        BreakpointRequest[] requests = [.. (arguments.Breakpoints ?? []).Select(b => new BreakpointRequest(
            LineIn(b.Line),
            b.Condition,
            b.HitCondition,
            b.Column is int column ? ColumnIn(column) : null,
            b.LogMessage,
            b.Suspend))];
        List<BreakpointManager.BreakpointInfo> breakpoints = await Locked(() => debugger.SetBreakpoints(path, requests));
        return new SetBreakpointsResponse([.. breakpoints.Select(b => ToProtocol(b, isEvent: false))]);
    }

    private async Task<SetBreakpointsResponse> SetFunctionBreakpoints(SetFunctionBreakpointsArguments arguments)
    {
        FunctionBreakpointRequest[] requests = [.. (arguments.Breakpoints ?? []).Select(b => new FunctionBreakpointRequest(b.Name, b.Condition, b.HitCondition, b.LogMessage))];
        List<BreakpointManager.BreakpointInfo> breakpoints = await Locked(() => debugger.SetFunctionBreakpoints(requests));
        return new SetBreakpointsResponse([.. breakpoints.Select(b => new DapBreakpoint { Id = b.Id, Verified = b.Verified, Message = b.Message })]);
    }

    private async Task SetExceptionBreakpoints(SetExceptionBreakpointsArguments arguments)
    {
        List<ExceptionBreakpointRequest> requests = [];
        foreach (string filter in arguments.Filters ?? [])
        {
            requests.Add(new ExceptionBreakpointRequest(ExceptionFilter(filter)));
        }
        foreach (DapExceptionFilterOptions option in arguments.FilterOptions ?? [])
        {
            requests.Add(new ExceptionBreakpointRequest(ExceptionFilter(option.FilterId), option.Condition));
        }
        log?.Invoke($"Exception breakpoints: {string.Join(", ", requests)}");
        await Locked(() => debugger.SetExceptionBreakpoints(requests));

        static ExceptionBreakpointFilter ExceptionFilter(string filter) => filter switch
        {
            "all" => ExceptionBreakpointFilter.All,
            "user-unhandled" => ExceptionBreakpointFilter.UserUnhandled,
            _ => throw new InvalidOperationException($"Unknown exception breakpoint filter: '{filter}'"),
        };
    }

    private async Task<ExceptionInfoResponse> ExceptionInfo(ThreadArguments arguments)
    {
        ExceptionInfo info = await Locked(() => debugger.ExceptionInfo(new ThreadId(arguments.ThreadId)));
        return new ExceptionInfoResponse
        {
            ExceptionId = info.ExceptionId,
            Description = info.Description,
            BreakMode = info.BreakMode switch
            {
                ExceptionBreakMode.Never => "never",
                ExceptionBreakMode.Always => "always",
                ExceptionBreakMode.Unhandled => "unhandled",
                ExceptionBreakMode.UserUnhandled => "userUnhandled",
                _ => "unhandled",
            },
            Code = info.Code,
            Details = ToProtocol(info.Details),
        };
    }

    private int RunInTerminal(LaunchInfo launch)
    {
        string programForTitle = launch.Program is "dotnet" ? launch.Arguments[0] : launch.Program;
        Dictionary<string, string> env = new(launch.Env)
        {
            // The program waits at startup for the debugger to attach and let it go.
            ["DOTNET_DefaultDiagnosticPortSuspend"] = "1",
        };
        RunInTerminalArguments arguments = new(
            launch.LaunchRequestConsoleType switch
            {
                LaunchRequestConsoleType.IntegratedTerminal => "integrated",
                LaunchRequestConsoleType.ExternalTerminal => "external",
                _ => throw new ArgumentOutOfRangeException(nameof(launch), $"Invalid console type for runInTerminal: '{launch.LaunchRequestConsoleType}'"),
            },
            $"{Path.GetFileName(programForTitle)} [DEBUG]",
            launch.Cwd,
            [launch.Program, .. launch.Arguments],
            env);
        // Asked from inside the launch request, which waits on it: the connection reads its answer on
        // another thread. VS Code answers with no process for its terminals (microsoft/vscode#61640).
        RunInTerminalResponse response = connection.RequestAsync<RunInTerminalResponse>("runInTerminal", arguments).GetAwaiter().GetResult();
        return response.ProcessId ?? throw new InvalidOperationException("runInTerminal returned no process ID; use internalConsole, or a client that returns one.");
    }

    private void RequestShutdown()
    {
        if (Interlocked.Exchange(ref shutdownRequested, 1) == 0)
        {
            log?.Invoke("Shutdown requested");
            ShutdownRequested?.Invoke();
        }
    }

    private async Task<T> Locked<T>(Func<Task<T>> work)
    {
        using (await debugger.DapRequestAndRuntimeEventLock.LockAsync())
        {
            await debugger.DrainRuntimeEventQueue();
            return await work().ConfigureAwait(false);
        }
    }

    private async Task<T> Locked<T>(Func<T> work)
    {
        using (await debugger.DapRequestAndRuntimeEventLock.LockAsync())
        {
            await debugger.DrainRuntimeEventQueue();
            return work();
        }
    }

    private async Task Locked(Func<Task> work)
    {
        using (await debugger.DapRequestAndRuntimeEventLock.LockAsync())
        {
            await debugger.DrainRuntimeEventQueue();
            await work().ConfigureAwait(false);
        }
    }

    private async Task Locked(Action work)
    {
        using (await debugger.DapRequestAndRuntimeEventLock.LockAsync())
        {
            await debugger.DrainRuntimeEventQueue();
            work();
        }
    }

    private DapBreakpoint ToProtocol(BreakpointManager.BreakpointInfo breakpoint, bool isEvent)
    {
        bool bound = breakpoint is { IsFunctionBreakpoint: false, Verified: true };
        DapSource? source = isEvent
            ? (bound ? new DapSource { Path = breakpoint.FilePath, Name = Path.GetFileName(breakpoint.FilePath), SourceReference = 0 } : null)
            : new DapSource { Path = breakpoint.FilePath };
        return new DapBreakpoint
        {
            Id = breakpoint.Id,
            Verified = breakpoint.Verified,
            Line = breakpoint.IsFunctionBreakpoint ? null : LineOut(breakpoint.Line),
            Column = bound && breakpoint.Column is int column ? ColumnOut(column) : null,
            EndLine = bound ? LineOut(breakpoint.EndLine) : null,
            EndColumn = bound && breakpoint.EndColumn is int endColumn ? ColumnOut(endColumn) : null,
            Offset = isEvent && bound ? 0 : null,
            Message = breakpoint.Message,
            Source = source,
        };
    }

    private DapStackFrame ToProtocol(StackFrameInfo frame) => new()
    {
        Id = frame.Id,
        Name = frame.Name,
        Line = LineOut(frame.Line),
        EndLine = frame.EndLine is int endLine ? LineOut(endLine) : null,
        Column = ColumnOut(frame.Column),
        EndColumn = frame.EndColumn is int endColumn ? ColumnOut(endColumn) : null,
        Source = frame.Source is not null ? new DapSource { Path = frame.Source, Name = Path.GetFileName(frame.Source), SourceReference = 0 } : null,
        PresentationHint = frame.IsUserCode ? null : "subtle",
        IsResolved = frame.IsResolved,
        DecompiledSourceInfo = frame.DecompiledSourceInfo is { } decompiled
            ? new DapDecompiledSource(decompiled.TypeFullName, new DapAssembly(decompiled.Assembly.AssemblyPath, decompiled.Assembly.Mvid), decompiled.CallingUserCodeAssemblyPath)
            : null,
    };

    private static DapPresentationHint? ToProtocol(VariablePresentationHint? hint)
    {
        if (hint is not { } h)
        {
            return null;
        }
        string? kind = h.Kind switch
        {
            null => null,
            PresentationHintKind.Property => "property",
            PresentationHintKind.Method => "method",
            PresentationHintKind.Event => "event",
            PresentationHintKind.Class => "class",
            PresentationHintKind.Data => "data",
            _ => throw new ArgumentOutOfRangeException(nameof(hint), h.Kind, null),
        };
        IReadOnlyList<string>? attributes = h.Attributes switch
        {
            null or AttributesValue.None => null,
            AttributesValue.FailedEvaluation => ["failedEvaluation"],
            _ => throw new ArgumentOutOfRangeException(nameof(hint), h.Attributes, null),
        };
        return new DapPresentationHint(kind, attributes);
    }

    private static DapExceptionDetails ToProtocol(ExceptionInfo.ExceptionDetails details) => new()
    {
        Message = details.Message,
        TypeName = details.TypeName,
        FullTypeName = details.FullTypeName,
        EvaluateName = details.EvaluateName,
        StackTrace = details.StackTrace,
        InnerException = [.. details.InnerException.Select(ToProtocol)],
        FormattedDescription = details.FormattedDescription,
        HResult = details.HResult,
        Source = details.Source,
    };

    private static string StopReason(string reason) => reason.ToLowerInvariant() switch
    {
        "step" or "breakpoint" or "exception" or "pause" or "entry" or "goto" or "function breakpoint" or "data breakpoint" or "instruction breakpoint" => reason.ToLowerInvariant(),
        _ => "unknown",
    };

    private int LineIn(int line) => linesStartAt1 ? line : line + 1;

    private int LineOut(int line) => linesStartAt1 ? line : line - 1;

    private int ColumnIn(int column) => columnsStartAt1 ? column : column + 1;

    private int ColumnOut(int column) => columnsStartAt1 ? column : column - 1;
}
