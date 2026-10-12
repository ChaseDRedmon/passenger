using System.Diagnostics;
using System.Threading.Channels;
using NeoSmart.AsyncLock;
using Passenger.Debugger.ExpressionEvaluator.Cil;
using Passenger.Debugger.Interop;
using Passenger.Debugger.Models;
using Passenger.Debugger.Models.Response;

namespace Passenger.Debugger;

// v1 of this class was AI generated, and could definitely do with some cleaning up
public partial class ManagedDebugger
{
    private ICorDebug? _corDebug;
    private ICorDebugProcess? _process;

    /// <summary>Set on the runtime's callback thread the moment the debuggee is gone, before the event is
    /// queued: from then on mscordbi neuters the process on its own thread and shuts its transport down, and
    /// a call into the process races that and can crash the adapter (a Stop or Detach sent over a transport
    /// being torn down).</summary>
    private readonly ManualResetEventSlim _exited = new();
    private readonly CorDebugManagedCallback _callbacks;
    private readonly BreakpointManager _breakpointManager;
    private readonly VariableManager _variableManager;
    private readonly FrameReferenceManager _frameReferenceManager;
    private readonly Action<string>? _logger;
    private sealed class ThreadInfo(ICorDebugThread thread)
    {
        public ICorDebugThread Thread { get; } = thread;
        public string? Name { get; set; }
    }

    private readonly Dictionary<int, ThreadInfo> _threads = new();
    private int? _mainThreadId;
    /// <summary>The operating system's thread the launched program was started on, which runs its Main, where it can be told.</summary>
    private int? _initialThreadId;
    private readonly Dictionary<CORDB_ADDRESS, ModuleInfo> _modules = new();
    private readonly HashSet<COR_TYPEID> _initializedStaticTypes = [];
    private ICorDebugFunction? _suppressFinalizeFunction;
    /// <summary>
    /// Monotonically increasing version of the set of loaded debuggee modules. Incremented whenever a module
    /// is loaded or unloaded, so anything derived from <see cref="AllModules"/> (the expression compile cache and the
    /// metadata-blocks cache) can detect staleness and rebuild.
    /// </summary>
    internal int ModuleSet_Version { get; private set; }
    private bool _isAttached;
    private bool _isRemoteAttach;
    private int? _pendingAttachProcessId;
    private bool _justMyCode;
    private bool _stopAtEntry;
    private EntryBreakpoint? _entryBreakpoint;
    private sealed record EntryBreakpoint(ICorDebugFunctionBreakpoint CorBreakpoint, CORDB_ADDRESS ModuleBaseAddress, int MethodToken, int IlOffset);

    // The active exception filters and optional type conditions supplied by the DAP client.
    private IReadOnlyList<ExceptionBreakpointRequest> _exceptionBreakpoints = [];
    // Per-thread state retained between callbacks for the same exception propagation sequence.
    private readonly Dictionary<ThreadId, ExceptionPropagationState> _exceptionStates = [];
    // The mode that caused each thread's latest exception stop, used by the exceptionInfo response.
    private readonly Dictionary<ThreadId, ExceptionBreakMode> _exceptionBreakModes = [];
    private AsyncStepper? _asyncStepper;
    private CilExpressionEvaluator _expressionEvaluator = null!;

    private Process? _debuggeeProcess;

    /// <summary>The program the adapter launched on macOS and Linux, and how it ended once it has.</summary>
    private SuspendedLaunch? _launch;
    private Task<int?>? _launchExit;
    private Task? _launchOutput;
    private int _exitReported;

    /// <summary>Tells the client the program has ended, once, after the last of its output.</summary>
    private void ReportExit(int? exitCode)
    {
        if (Interlocked.Exchange(ref _exitReported, 1) == 1)
        {
            return;
        }
        // A program's own child holding its output open must not hold up the news.
        _launchOutput?.Wait(TimeSpan.FromSeconds(1));
        OnExited?.Invoke(exitCode);
    }

    // Every event below is raised on any thread (the runtime's callback thread, a request's, an output reader).
    public event Action<int, string>? OnStopped;
    // ThreadId, FilePath, Line, Column, Reason, HitBreakpointIds, DecompiledSourceInfo
    public event Action<int, string, int, int, string, List<int>?>? OnStopped2;
    public event Action<int?>? OnExited;
    public event Action? OnTerminated;
    public event Action<int>? OnThreadStarted;
    public event Action<int>? OnThreadExited;
    public event Action<string, string, string>? OnModuleLoaded;
    public event Action<string, string, string>? OnModuleUnloaded;
    // Output text, isError (true for stderr, false for stdout)
    public event Action<string, bool>? OnOutput;
    /// <summary>What the debugger itself has to tell the user, beside the program's output.</summary>
    public event Action<string>? OnConsole;
    public event Action<int, string>? OnProcessStarted;

    /// <summary>The debugger has hold of a process it attached to: the attach completes after the request does, since
    /// a process started suspended has no runtime to attach to until its client lets it go.</summary>
    public event Action<int>? OnAttached;
    public event Action<BreakpointManager.BreakpointInfo>? OnBreakpointChanged;
    public event Func<LaunchInfo, int> SendRunInTerminalRequest = null!;

    public EvalStatus EvalStatus { get; }

    private Task? _runtimeEventCallbackProcessing;
    private readonly Channel<CorDebugManagedCallbackEventArgs> _runtimeEventChannel;
    public readonly AsyncLock DapRequestAndRuntimeEventLock = new();

    public ManagedDebugger(Action<string>? logger = null)
    {
        _logger = logger;
        _breakpointManager = new BreakpointManager();
        _variableManager = new VariableManager();
        _frameReferenceManager = new FrameReferenceManager();
        _callbacks = new CorDebugManagedCallback();
        EvalStatus = new EvalStatus();
        _asyncStepper = new AsyncStepper(_modules, this);
        // The runtime's callback thread writes, and so does whichever thread hands back an event put off during an evaluation.
        _runtimeEventChannel = Channel.CreateUnbounded<CorDebugManagedCallbackEventArgs>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
        _callbacks.OnAnyEvent += QueueEvent;
        _runtimeEventCallbackProcessing = Task.Run(ProcessRuntimeEventQueue);
    }

    private void QueueEvent(object? sender, CorDebugManagedCallbackEventArgs e)
    {
        if (e is ExitProcessCorDebugManagedCallbackEventArgs)
        {
            _exited.Set();
        }
        _runtimeEventChannel.Writer.TryWrite(e);
    }

    public async Task DrainRuntimeEventQueue()
    {
        // Caller should have obtained this lock, and we are re-entrant here
        using (await DapRequestAndRuntimeEventLock.LockAsync())
        {
            ChannelReader<CorDebugManagedCallbackEventArgs> reader = _runtimeEventChannel.Reader;
            // Process all immediately available events
            while (reader.TryRead(out CorDebugManagedCallbackEventArgs? callbackEvent))
            {
                await OnAnyEvent(this, callbackEvent).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessRuntimeEventQueue()
    {
        try
        {
            ChannelReader<CorDebugManagedCallbackEventArgs> reader = _runtimeEventChannel.Reader;
            while (await reader.WaitToReadAsync())
            {
                // If a Dap request has obtained the lock, we will pause here. It will drain runtime events, and our TryRead may return false, which is fine
                using (await DapRequestAndRuntimeEventLock.LockAsync())
                {
                    if (reader.TryRead(out CorDebugManagedCallbackEventArgs? callbackEvent) is false)
                    {
                        continue;
                    }

                    await OnAnyEvent(this, callbackEvent).ConfigureAwait(false);
                }
            }
        }
        catch (Exception e)
        {
            _logger?.Invoke($"Critical failure processing runtime event queue, no further events will be processed: {e}");
            throw;
        }
    }

    internal async Task<CorDebugManagedCallbackEventArgs> ProcessRuntimeEventsUntilEvalEvent()
    {
        ChannelReader<CorDebugManagedCallbackEventArgs> reader = _runtimeEventChannel.Reader;
        while (await reader.WaitToReadAsync())
        {
            if (reader.TryRead(out CorDebugManagedCallbackEventArgs? callbackEvent) is false)
            {
                throw new InvalidOperationException("Expected to read an event from the runtime event queue, but none was available");
            }

            await OnAnyEvent(this, callbackEvent).ConfigureAwait(false);
            if (callbackEvent is EvalCompleteCorDebugManagedCallbackEventArgs or EvalExceptionCorDebugManagedCallbackEventArgs)
            {
                return callbackEvent;
            }
        }
        throw new InvalidOperationException("Expected to read an eval event from the runtime event queue, but Channel completed unexpectedly");
    }

    private async Task OnAnyEvent(object? sender, CorDebugManagedCallbackEventArgs e)
    {
        try
        {
            _logger?.Invoke($"Event: {e.GetType().Name}");
            switch (e)
            {
                case LogMessageCorDebugManagedCallbackEventArgs a: HandleLogMessage(sender, a); break;
                case CreateProcessCorDebugManagedCallbackEventArgs a: HandleProcessCreated(sender, a); break;
                case ExitProcessCorDebugManagedCallbackEventArgs a: HandleProcessExited(sender, a); break;
                case CreateThreadCorDebugManagedCallbackEventArgs a: HandleThreadCreated(sender, a); break;
                case ExitThreadCorDebugManagedCallbackEventArgs a: HandleThreadExited(sender, a); break;
                case LoadModuleCorDebugManagedCallbackEventArgs a: HandleModuleLoaded(sender, a); break;
                case UnloadModuleCorDebugManagedCallbackEventArgs a: HandleModuleUnloaded(sender, a); break;
                case BreakpointCorDebugManagedCallbackEventArgs a: await HandleBreakpoint(sender, a).ConfigureAwait(false); break;
                case StepCompleteCorDebugManagedCallbackEventArgs a: HandleStepComplete(sender, a); break;
                case BreakCorDebugManagedCallbackEventArgs a: HandleBreak(sender, a); break;
                case ExceptionCorDebugManagedCallbackEventArgs a: HandleException(sender, a); break;
                case Exception2CorDebugManagedCallbackEventArgs a: HandleException2(sender, a); break;
                case DeferredExceptionStop a: StopAtException(a.Thread, a.BreakMode); break;
                case EvalCompleteCorDebugManagedCallbackEventArgs or EvalExceptionCorDebugManagedCallbackEventArgs: break; // don't continue on these, as they are being used for expression evaluation
                default:
                    if (_process is not null)
                    {
                        Continue();
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Error handling event {e.GetType().Name}: {ex}");
            if (_process is not null && _process.TryIsRunning(out bool isRunning) is Cor.S_OK && isRunning is false)
            {
                Continue();
            }
        }
    }

    private void HandleLogMessage(object? sender, LogMessageCorDebugManagedCallbackEventArgs logMessageEvent)
    {
        _logger?.Invoke($"Log: {logMessageEvent.Message}");
        Continue();
    }

    /// <summary>
    /// Actually attach to an existing process
    /// </summary>
    private void PerformAttach(int processId)
    {
        _logger?.Invoke($"Attaching to process: {processId}");

        // Not awaited: a process held at its diagnostic port has no runtime to attach to until the client lets it go.
        _ = Task.Run(async () =>
        {
            try
            {
                _exited.Reset();
                _corDebug = await ClrDebugExtensions.Automatic(processId, resume: null, _logger);
                _corDebug.Initialize();
                _corDebug.SetManagedHandler(_callbacks);

                _logger?.Invoke($"Taking hold of process {processId}");
                _process = _corDebug.DebugActiveProcess(processId, false);
                _isAttached = true;
                ConfigureExceptionCallbacks();

                _logger?.Invoke($"Attached to process: {processId}");
                OnAttached?.Invoke(processId);
                SendAllBreakpointEvents();
            }
            catch (Exception error)
            {
                // Otherwise lost with the task, and the client would wait for a stop that can never come.
                _logger?.Invoke($"Attaching to process {processId} failed: {error}");
                OnConsole?.Invoke($"Could not attach to process {processId}: {error.Message}\n");
                OnTerminated?.Invoke();
            }
        });
    }

    private void PerformRemoteAttach(RemoteAttachInfo remoteAttachInfo)
    {
        _logger?.Invoke($"Attaching to remote process on {remoteAttachInfo.Address}:{remoteAttachInfo.Port}");

        _exited.Reset();
        _corDebug = ClrDebugExtensions.Mobile(remoteAttachInfo);
        _corDebug.SetManagedHandler(_callbacks);
        try
        {
            // It is expected that this does not return a ICorDebugProcess in the remote scenario - it is obtained via the CreateProcess callback instead
            // ClrDebug throws because it does not expect to receive a null pointer
            _ = _corDebug.DebugActiveProcess(0, false);
        }
        catch { /* */ }

        _logger?.Invoke($"Debugger listening on port {remoteAttachInfo.Port}, awaiting connection from debuggee");
        _ = Task.Run(SendAllBreakpointEvents);
    }

    private void SendAllBreakpointEvents()
    {
        // Send a breakpoint changed event with verified false for every breakpoint, so the IDE can mark the BP as unverified, until it receives our later BP events when we bind them
        foreach (BreakpointManager.BreakpointInfo bp in _breakpointManager.GetAllBreakpoints())
        {
            OnBreakpointChanged?.Invoke(bp);
        }
    }

    /// <summary>Lets the process go on after one of the runtime's own events, unless another thread's event put off during
    /// an evaluation is handled first, or a pause waits to be taken there. During an evaluation it only lets that go on.</summary>
    private void Continue()
    {
        ArgumentNullException.ThrowIfNull(_process);
        if (!EvalStatus.IsRunning && (TakeDeferredEvent() || TakePendingPause()))
        {
            return;
        }
        _process.Continue(false);
    }

    /// <summary>Hands the first event put off during an evaluation to the event loop in place of a continue, the process
    /// still stopped and its thread still where the event found it; true when there was one.</summary>
    private bool TakeDeferredEvent()
    {
        if (EvalStatus.IsRunning || !_deferredEvents.TryDequeue(out CorDebugManagedCallbackEventArgs? later))
        {
            return false;
        }
        _runtimeEventChannel.Writer.TryWrite(later);
        return true;
    }

    /// <summary>Other threads' events queued with the one an evaluation began from, which the runtime hands over before the
    /// evaluation can run: their threads stay suspended where they were until each is handled, in order.</summary>
    private readonly Queue<CorDebugManagedCallbackEventArgs> _deferredEvents = new();

    /// <summary>An exception on another thread that stops the program, found while an evaluation ran.</summary>
    private sealed class DeferredExceptionStop(ICorDebugThread thread, ExceptionBreakMode breakMode) : CorDebugManagedCallbackEventArgs
    {
        public ICorDebugThread Thread { get; } = thread;
        public ExceptionBreakMode BreakMode { get; } = breakMode;
    }

    private void ConfigureExceptionCallbacks()
    {
        if (_process is not ICorDebugProcess8 process8)
        {
            return;
        }

        int result = process8.TryEnableExceptionCallbacksOutsideOfMyCode(!_justMyCode);
        if (result is not Cor.S_OK)
        {
            _logger?.Invoke($"Unable to configure exception callbacks outside user code: {result}");
        }
    }

    private ICorDebugStepper? _stepper;

    /// <summary>A pause that came while the process was held for an event of its own, taken at the next Continue.</summary>
    private bool _pauseRequested;

    /// <summary>
    /// Setup a stepper without continuing execution
    /// </summary>
    internal ICorDebugStepper SetupStepper(ICorDebugThread thread, AsyncStepper.StepType stepType)
    {
        ICorDebugFrame frame = thread.ActiveFrame;
        if (frame is not ICorDebugILFrame ilFrame)
        {
            throw new InvalidOperationException("Active frame is not an IL frame");
        }

        if (_stepper is not null)
        {
            throw new InvalidOperationException("A step operation is already in progress");
        }

        ICorDebugStepper stepper = frame.CreateStepper();
        stepper.SetInterceptMask(CorDebugIntercept.INTERCEPT_ALL & ~(CorDebugIntercept.INTERCEPT_SECURITY | CorDebugIntercept.INTERCEPT_CLASS_INIT));
        stepper.SetUnmappedStopMask(CorDebugUnmappedStop.STOP_NONE);
        if (_justMyCode)
        {
            stepper.SetJMC(true);
        }

        if (stepType == AsyncStepper.StepType.StepOut)
        {
            stepper.StepOut();
        }
        else // StepIn or StepOver
        {
            ModuleMetadataReader metadataReader = _modules[frame.Function.Module.BaseAddress].MetadataReader;

            int currentIlOffset = ilFrame.IP.pnOffset;
            (int ilStartOffset, int ilEndOffset)? nullableResult = metadataReader.GetStartAndEndSequencePointIlOffsetsForIlOffset(frame.Function.Token, currentIlOffset);
            if (nullableResult is var (startIlOffset, endIlOffset))
            {
                if (startIlOffset == endIlOffset)
                {
                    endIlOffset = frame.Function.ILCode.Size;
                }
                COR_DEBUG_STEP_RANGE stepRange = new COR_DEBUG_STEP_RANGE
                {
                    startOffset = checked((uint)startIlOffset),
                    endOffset = checked((uint)endIlOffset)
                };
                bool stepIn = stepType is AsyncStepper.StepType.StepIn;
                stepper.StepRange(stepIn, [stepRange], 1);
            }
            else
            {
                bool stepIn = stepType is AsyncStepper.StepType.StepIn;
                stepper.Step(stepIn);
            }
        }

        _stepper = stepper;
        return stepper;
    }

    /// <summary>
    /// Try to bind a breakpoint to the actual code using symbol information
    /// </summary>
    private bool TryBindBreakpoint(BreakpointManager.BreakpointInfo bp)
    {
        try
        {
            if (_process is null)
            {
                return false;
            }

            // The module whose symbols name the file best: the very path over a document recorded
            // under another root, never just a file of the same name (SourcePaths).
            ModuleInfo? targetModule = null;
            ModuleMetadataReader.ResolvedBreakpoint? resolved = null;

            foreach (ModuleInfo moduleInfo in _modules.Values
                .Where(m => m.MetadataReader.HasSymbols)
                .Select(m => (Module: m, Score: m.MetadataReader.DocumentMatch(bp.FilePath)))
                .Where(m => m.Score > 0)
                .OrderByDescending(m => m.Score)
                .Select(m => m.Module))
            {
                resolved = moduleInfo.MetadataReader.ResolveBreakpoint(bp.FilePath, bp.Line, bp.Column);
                if (resolved is not null)
                {
                    targetModule = moduleInfo;
                    break;
                }
            }

            if (targetModule is null || resolved is null)
            {
                // No module found with symbols for this file
                bp.Verified = false;
                bp.Message = "The breakpoint will not currently be hit. No symbols have been loaded for this document.";
                _logger?.Invoke($"Breakpoint at {bp.FilePath}:{bp.Line} - no symbols found");
                return false;
            }

            // Visual Studio's rule: Just My Code never stops in a method its code says is not the user's, and
            // no debugger stops in a [DebuggerHidden] one (upstream #54).
            if (targetModule.MetadataReader.IsHidden(resolved.MethodToken)
                || (_justMyCode && targetModule.IsUserCode && targetModule.MetadataReader.IsNonUserCode(resolved.MethodToken)))
            {
                bp.Verified = false;
                bp.Message = "The breakpoint will not be hit: the method is marked [DebuggerStepThrough], [DebuggerNonUserCode] or [DebuggerHidden], which Just My Code passes over.";
                return false;
            }

            // Get the function from the method token
            ICorDebugFunction function = targetModule.Module.GetFunctionFromToken(resolved.MethodToken);
            ICorDebugCode ilCode = function.ILCode;

            // Create a breakpoint at the resolved IL offset
            ICorDebugFunctionBreakpoint corBreakpoint = TryGetEntryBreakpoint(targetModule.BaseAddress, resolved.MethodToken, resolved.ILOffset)
                ?? ilCode.CreateBreakpoint(resolved.ILOffset);
            if (corBreakpoint != _entryBreakpoint?.CorBreakpoint)
            {
                corBreakpoint.Activate(true);
            }

            // Update breakpoint info
            bp.CorBreakpoint = corBreakpoint;
            bp.Verified = true;
            bp.Line = resolved.StartLine;
            bp.Column = resolved.StartColumn;
            bp.EndLine = resolved.EndLine;
            bp.EndColumn = resolved.EndColumn;
            bp.ResolvedBreakpointFromPdb = resolved;
            bp.ModuleBaseAddress = targetModule.BaseAddress;
            bp.Message = null;

            _logger?.Invoke($"Breakpoint bound at {bp.FilePath}:{bp.Line} -> resolved to line {resolved.StartLine}, IL offset {resolved.ILOffset} in method 0x{resolved.MethodToken:X}");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Error binding breakpoint at {bp.FilePath}:{bp.Line}: {ex.Message}");
            bp.Verified = false;
            bp.Message = $"Error binding breakpoint: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Try to bind all pending breakpoints (called when a new module is loaded)
    /// </summary>
    private void TryBindPendingBreakpoints()
    {
        IEnumerable<BreakpointManager.BreakpointInfo> pendingBreakpoints = _breakpointManager.GetPendingBreakpoints().Where(bp => !bp.IsFunctionBreakpoint);

        foreach (BreakpointManager.BreakpointInfo? bp in pendingBreakpoints)
        {
            if (TryBindBreakpoint(bp))
            {
                // Notify that the breakpoint changed (became verified)
                OnBreakpointChanged?.Invoke(bp);
            }
        }
    }

    /// <summary>
    /// Binds all matching functions in a module and returns true only when the BreakpointInfo becomes verified.
    /// </summary>
    private bool TryBindFunctionBreakpoint(BreakpointManager.BreakpointInfo bp, ModuleInfo module)
    {
        if (module.MetadataReader.HasSymbols is false || bp.FunctionName is null)
        {
            return false;
        }

        bool wasVerified = bp.Verified;
        try
        {
            FunctionBreakpointPattern pattern = FunctionBreakpointPattern.Parse(bp.FunctionName);
            foreach (ResolvedFunctionBreakpoint resolved in FunctionBreakpointMetadataResolver.Resolve(module.MetadataReader, pattern))
            {
                if (bp.FunctionBindings.Any(binding => binding.ModuleBaseAddress == module.BaseAddress && binding.MethodToken == resolved.MethodToken))
                {
                    continue;
                }
                ICorDebugFunction function = module.Module.GetFunctionFromToken(resolved.MethodToken);
                ICorDebugFunctionBreakpoint corBreakpoint = TryGetEntryBreakpoint(module.BaseAddress, resolved.MethodToken, resolved.Source.ILOffset)
                    ?? function.ILCode.CreateBreakpoint(resolved.Source.ILOffset);
                if (corBreakpoint != _entryBreakpoint?.CorBreakpoint)
                {
                    corBreakpoint.Activate(true);
                }

                bp.FunctionBindings.Add(new BreakpointManager.FunctionBreakpointBinding(corBreakpoint, module.BaseAddress, resolved.MethodToken, resolved.Source));
                bp.Verified = true;
                bp.Message = null;
            }
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Error binding function breakpoint '{bp.FunctionName}' in {module.ModuleName}: {ex.Message}");
            if (!bp.Verified)
            {
                bp.Message = $"Error binding function breakpoint: {ex.Message}";
            }
        }
        return wasVerified is false && bp.Verified;
    }

    private void EnsureNoProcessBeingDebugged()
    {
        if (_process is not null)
        {
            throw new InvalidOperationException("A process is already being debugged, you must terminate/detach first.");
        }
    }

    private ICorDebugFunctionBreakpoint? TryGetEntryBreakpoint(CORDB_ADDRESS moduleBaseAddress, int methodToken, int ilOffset)
    {
        return _entryBreakpoint is { } entry && entry.ModuleBaseAddress == moduleBaseAddress &&
            entry.MethodToken == methodToken && entry.IlOffset == ilOffset
            ? entry.CorBreakpoint
            : null;
    }

    internal ICorDebugILFrame GetIlFrameForThreadIdAndStackDepth(ThreadId threadId, FrameStackDepth stackDepth)
    {
        ICorDebugFrame frame = GetFrameForThreadIdAndStackDepth(threadId, stackDepth);
        if (frame is not ICorDebugILFrame ilFrame)
        {
            throw new InvalidOperationException("Frame is not an IL frame");
        }

        return ilFrame;
    }

    internal ICorDebugFrame GetFrameForThreadIdAndStackDepth(ThreadId threadId, FrameStackDepth stackDepth)
    {
        // We need to re-obtain the frame in case it has been neutered
        ICorDebugThread thread = _process!.GetThread(threadId.Value);
        ICorDebugFrame frame = EnumerateFramesForThread(thread).ElementAt(stackDepth.Value);
        return frame;
    }

    private static IEnumerable<ICorDebugFrame> EnumerateFramesForThread(ICorDebugThread thread)
    {
        foreach (ICorDebugChain chain in thread.EnumerateChains())
        {
            if (chain.IsManaged is false)
            {
                continue;
            }

            foreach (ICorDebugFrame frame in chain.EnumerateFrames())
            {
                yield return frame;
            }
        }
    }

    internal IReadOnlyCollection<ModuleInfo> AllModules => _modules.Values;
    internal ModuleInfo GetModuleInfoForModule(ICorDebugModule module) => _modules[module.BaseAddress];

    internal ICorDebugValue? GetCurrentException(ThreadId threadId)
    {
        ICorDebugThread? thread = _process?.GetThread(threadId.Value);
        if (thread is null)
        {
            return null;
        }

        thread.TryGetCurrentException(out ICorDebugValue? currentException);
        return currentException;
    }

    // Not intended to implement IDisposable - it is intended that this is called via Disconnect()
    private void Dispose()
    {
        if (_process is null)
        {
            return; // A client may call Terminate, then Disconnect, both of which call Dispose. Dispose only needs to be run once.
        }
        // A process that has exited is the runtime's to tear down: nothing below may call into it.
        bool exited = _exited.IsSet;

        // Dispose modules, which releases PDB files
        foreach (ModuleInfo moduleInfo in _modules.Values)
        {
            moduleInfo.Dispose();
        }
        _modules.Clear();

        // Deactivate all breakpoints
        if (_entryBreakpoint is { } entryBreakpoint && !exited)
        {
            entryBreakpoint.CorBreakpoint.TryActivate(false);
        }
        _entryBreakpoint = null;
        _stopAtEntry = false;
        foreach (BreakpointManager.BreakpointInfo? bp in _breakpointManager.GetAllBreakpoints().Where(b => !exited && ((b.CorBreakpoint is not null && b.IsFunctionBreakpoint is false) || b.IsFunctionBreakpoint)))
        {
            IEnumerable<ICorDebugFunctionBreakpoint> corBreakpoints = bp.IsFunctionBreakpoint ? bp.FunctionBindings.Select(binding => binding.CorBreakpoint) : [bp.CorBreakpoint!];
            foreach (ICorDebugFunctionBreakpoint? corBreakpoint in corBreakpoints)
            {
                int hResult = corBreakpoint.TryActivate(false);
                if (hResult is Cor.CORDBG_E_PROCESS_TERMINATED)
                {
                    break;
                }

                if (hResult is not Cor.S_OK)
                {
                    _logger?.Invoke($"Failed to deactivate breakpoint {bp.Id}: {hResult}");
                }
            }
        }
        _breakpointManager.Clear();

        _asyncStepper?.Dispose();
        _asyncStepper = null;
        _stepper = null!;
        _threads.Clear();
        _deferredEvents.Clear();
        _mainThreadId = null;
        _initialThreadId = null;
        _exceptionStates.Clear();
        _exceptionBreakModes.Clear();
        _initializedStaticTypes.Clear();
        _suppressFinalizeFunction = null;
        if (exited)
        {
            _variableManager.Clear();
        }
        else
        {
            _variableManager.ClearAndTryDisposeHandleValues();
        }
        _frameReferenceManager.Clear();

        // Unsubscribe from callbacks to avoid any further event dispatch
        _callbacks.OnAnyEvent -= QueueEvent;
        _runtimeEventChannel.Writer.Complete();
        // ProcessRuntimeEventQueue is blocked on DapRequestAndRuntimeEventLock (which we hold) and would
        // never complete if we waited on it here — that is the deadlock. Read and discard remaining events ourselves,
        // then let the processor exit once the lock is released.
        while (_runtimeEventChannel.Reader.TryRead(out _)) { }

        // Detach from the process
        if (!exited)
        {
            _process?.TryDetach();
        }
        else if (_corDebug is not null)
        {
            ShutDown(_corDebug);
        }

        _isAttached = false;
        _process = null;
        _corDebug = null;

        _debuggeeProcess?.Dispose();
        _debuggeeProcess = null;
        _launch?.Dispose();
        _launch = null;
    }

    /// <summary>ICorDebug's own shutdown, once the process it debugged has exited: refused until the runtime's
    /// thread has finished neutering the process, then it stops that thread, so the adapter never exits (or
    /// releases the runtime's objects) under it.</summary>
    private void ShutDown(ICorDebug corDebug)
    {
        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        int result;
        while ((result = corDebug.TryTerminate()) == Cor.CORDBG_E_ILLEGAL_SHUTDOWN_ORDER && DateTime.UtcNow < until)
        {
            Thread.Sleep(20);
        }
        if (result != Cor.S_OK)
        {
            _logger?.Invoke($"ICorDebug.Terminate: 0x{result:X8}");
        }
    }

    private sealed class ExceptionPropagationState
    {
        // True once exception propagation has entered a JMC-marked frame. If the runtime later finds a
        // handler outside JMC code, the exception is user-unhandled.
        public required bool HasReachedUserCode { get; set; }

        // Prevents the first-chance and user-first-chance callbacks for the same exception from producing
        // duplicate "all exceptions" stops.
        public required bool AlwaysStopReported { get; set; }
    }
}

public class EvalStatus
{
    /// <summary>The thread an evaluation runs on, while one runs: the only thread of the program let go meanwhile.</summary>
    public int? ThreadId { get; set; }

    public bool IsRunning => ThreadId is not null;
}
