using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Passenger.Debugger.ExpressionEvaluator;
using Passenger.Debugger.ExpressionEvaluator.Cil;
using Passenger.Debugger.ExpressionEvaluator.Compiler;
using Passenger.Debugger.Interop;
using Passenger.Debugger.Models;
using Passenger.Debugger.Models.Response;
using Passenger.Debugger.PresentationHintModels;
using ZLinq;

namespace Passenger.Debugger;

public record BreakpointRequest(int Line, string? Condition = null, string? HitCondition = null, int? Column = null, string? LogMessage = null, bool? Suspend = null);
public record FunctionBreakpointRequest(string Name, string? Condition = null, string? HitCondition = null, string? LogMessage = null);
public record ExceptionBreakpointRequest(ExceptionBreakpointFilter Filter, string? Condition = null);
public enum ExceptionBreakpointFilter { All, UserUnhandled }

public partial class ManagedDebugger
{
    // Store launch info for deferred attach in ConfigurationDone
    private LaunchInfo? _pendingLaunchInfo;
    private RemoteAttachInfo? _pendingRemoteAttachInfo;

    /// <summary>
    /// Stores the launch request info for use in handling ConfigurationDone
    /// </summary>
    public void Launch(LaunchInfo launchInfo, bool justMyCode)
    {
        EnsureNoProcessBeingDebugged();
        _logger?.Invoke($"Launching program: {launchInfo.Program} {string.Join(' ', launchInfo.Arguments)}");
        _justMyCode = justMyCode;
        _stopAtEntry = launchInfo.StopAtEntry;
        _pendingLaunchInfo = launchInfo;
    }

    /// <summary>A program named without a folder, found on the PATH as a shell would find it.</summary>
    private static string Resolve(string program)
    {
        if (program.Contains(Path.DirectorySeparatorChar) || Environment.ProcessPath is not { } self)
        {
            return program;
        }
        // The adapter runs under the very dotnet that runs a .dll it launches.
        if (program == "dotnet" && Path.GetFileNameWithoutExtension(self) == "dotnet")
        {
            return self;
        }
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(folder, program);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return program;
    }

    private static Process StartHeldByDiagnosticPort(LaunchInfo launchInfo)
    {
        ProcessStartInfo processStartInfo = new ProcessStartInfo
        {
            FileName = launchInfo.Program,
            WorkingDirectory = launchInfo.Cwd ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Its own input, never the adapter's: that is the protocol's pipe, which a program reading
            // its console would otherwise drain. The client writes to it with WriteInput.
            RedirectStandardInput = true,
        };
        foreach (string arg in launchInfo.Arguments)
        {
            processStartInfo.ArgumentList.Add(arg);
        }
        foreach ((string? key, string? value) in launchInfo.Env)
        {
            processStartInfo.Environment[key] = value;
        }
        processStartInfo.Environment["DOTNET_DefaultDiagnosticPortSuspend"] = "1";
        return Process.Start(processStartInfo) ?? throw new InvalidOperationException("Process start failed");
    }

    /// <summary>
    /// Actually perform the launch using DbgShim APIs
    /// </summary>
    private async Task PerformLaunch()
    {
        if (_pendingLaunchInfo is null)
        {
            _logger?.Invoke("No pending launch to perform");
            return;
        }

        LaunchInfo launchInfo = _pendingLaunchInfo;
        _pendingLaunchInfo = null;

        int processId;
        Func<Task> resume;
        Action kill;
        _exitReported = 0;
        if (OperatingSystem.IsWindows())
        {
            // Still held by the diagnostic port, whose variable the program's own .NET children inherit and are
            // held by too; Windows is to move to CreateProcessForLaunch as macOS and Linux have.
            Process process = StartHeldByDiagnosticPort(launchInfo);
            _debuggeeProcess = process;
            processId = process.Id;
            resume = () => DiagnosticClientHelper.DiagnosticClientResumeRuntime(process.Id);
            kill = () => process.Kill();
        }
        else
        {
            SuspendedLaunch launch = SuspendedLaunch.Start(Resolve(launchInfo.Program), launchInfo.Arguments, launchInfo.Cwd ?? Environment.CurrentDirectory, launchInfo.Env);
            _launch = launch;
            _launchOutput = Task.WhenAll(Forward(launch.Output, isError: false), Forward(launch.Error, isError: true));
            _launchExit = Task.Run(launch.WaitForExit);
            // The exit is told as soon as the program has gone, not when the runtime's word of it is handled:
            // mscordbi can crash tearing the session down first (a race in its transport), taking that word with it.
            _ = _launchExit.ContinueWith(ended => ReportExit(ended.Result), TaskScheduler.Default);
            processId = launch.ProcessId;
            resume = () =>
            {
                launch.Resume();
                return Task.CompletedTask;
            };
            kill = launch.Kill;
        }

        _logger?.Invoke($"Process created suspended with PID: {processId}");

        _exited.Reset();
        try
        {
            _corDebug = await ClrDebugExtensions.Automatic(processId, resume);
            _corDebug.Initialize();
            _corDebug.SetManagedHandler(_callbacks);
            _process = _corDebug.DebugActiveProcess(processId, false);
        }
        catch
        {
            // A program the debugger could not take hold of (not .NET, or failed first) is not left behind, held.
            kill();
            throw;
        }
        _isAttached = true;
        ConfigureExceptionCallbacks();

        _logger?.Invoke($"Successfully attached to process: {processId}");
        OnProcessStarted?.Invoke(processId, launchInfo.Program);
        SendAllBreakpointEvents();
        return;

        // A program's output as it comes, a prompt with no line end included.
        Task Forward(Stream stream, bool isError) => Task.Run(async () =>
        {
            try
            {
                using StreamReader reader = new(stream, Encoding.UTF8);
                char[] buffer = new char[4096];
                int read;
                while ((read = await reader.ReadAsync(buffer)) > 0)
                {
                    InvokeOnOutputWithTryCatch(new string(buffer, 0, read), isError);
                }
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
            }
        });

        // The DataReceived callbacks run on background threads - a throwing subscriber (e.g. a disposed protocol layer) must not crash the adapter
        void InvokeOnOutputWithTryCatch(string output, bool isError)
        {
            try
            {
                OnOutput?.Invoke(output, isError);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error forwarding debuggee output: {ex.Message}");
            }
        }
    }

    public bool RemoveBreakpoint(int id)
    {
        _logger?.Invoke($"RemoveBreakpoint: {id}");
        BreakpointManager.BreakpointInfo? bp = _breakpointManager.GetBreakpoint(id);
        if (bp is null)
        {
            return false;
        }

        if (bp.CorBreakpoint is not null)
        {
            try
            {
                bp.CorBreakpoint.Activate(false);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error deactivating breakpoint: {ex.Message}");
            }
        }
        return _breakpointManager.RemoveBreakpoint(id);
    }

    /// <summary>
    /// Store process ID for later attach (actual attach happens in ConfigurationDone)
    /// </summary>
    public void Attach(int processId, bool justMyCode)
    {
        EnsureNoProcessBeingDebugged();
        _logger?.Invoke($"Storing attach target: {processId}");
        _justMyCode = justMyCode;
        _stopAtEntry = false;
        _pendingAttachProcessId = processId;
    }

    public void AttachRemote(RemoteAttachInfo remoteAttachInfo, bool justMyCode)
    {
        EnsureNoProcessBeingDebugged();
        _justMyCode = justMyCode;
        _stopAtEntry = false;
        _pendingRemoteAttachInfo = remoteAttachInfo;
        _isRemoteAttach = true;
    }

    /// <summary>
    /// Called when DAP configuration is complete - performs deferred launch or attach
    /// </summary>
    public async Task ConfigurationDone()
    {
        //System.Diagnostics.Debugger.Launch();
        _logger?.Invoke("ConfigurationDone");

        if (_pendingLaunchInfo is { LaunchRequestConsoleType: LaunchRequestConsoleType.ExternalTerminal or LaunchRequestConsoleType.IntegratedTerminal })
        {
            int launchedProcessId = await Task.Run(() => SendRunInTerminalRequest.Invoke(_pendingLaunchInfo)); // get off the dispatcher thread
            _pendingLaunchInfo = null;
            PerformAttach(launchedProcessId);
            await DiagnosticClientHelper.DiagnosticClientResumeRuntime(launchedProcessId);
        }
        else if (_pendingLaunchInfo is not null) // If we have a pending launch, perform it
        {
            await PerformLaunch();
        }
        else if (_pendingRemoteAttachInfo is not null)
        {
            PerformRemoteAttach(_pendingRemoteAttachInfo);
            _pendingRemoteAttachInfo = null;
        }
        // Otherwise check for pending attach
        else if (_pendingAttachProcessId.HasValue)
        {
            PerformAttach(_pendingAttachProcessId.Value);
            _pendingAttachProcessId = null;
        }
    }

    /// <summary>
    /// Continue execution
    /// </summary>
    public void HandleContinueRequest()
    {
        _logger?.Invoke("Continue");
        ContinueWithVariableClearAllowSuperfluousContinue();
    }

    private void ContinueWithVariableClearAllowSuperfluousContinue()
    {
        ArgumentNullException.ThrowIfNull(_process);
        _pauseRequested = false;
        _variableManager.ClearAndDisposeHandleValues();
        _frameReferenceManager.Clear();

        int result = _process.TryContinue(false);
        if (result is Cor.CORDBG_E_SUPERFLOUS_CONTINUE)
        {
            return;
        }

        Marshal.ThrowExceptionForHR(result);
    }

    private void ContinueWithVariableClear()
    {
        ArgumentNullException.ThrowIfNull(_process);
        _pauseRequested = false;
        _variableManager.ClearAndDisposeHandleValues();
        _frameReferenceManager.Clear();

        _process.Continue(false);
    }

    /// <summary>
    /// Pause execution
    /// </summary>
    public void Pause()
    {
        _logger?.Invoke("Pause");
        ArgumentNullException.ThrowIfNull(_process);
        if (_process.IsRunning is false)
        {
            // Held for one of the runtime's own events (a module loading, a thread starting): the pause is taken where
            // the engine would let it go on, in Continue.
            _pauseRequested = true;
            return;
        }

        _process.Stop(0);
        _asyncStepper?.Disable();
        if (_stepper is not null)
        {
            _stepper.Deactivate();
            _stepper = null;
        }
        // ICorDebug's Stop is synchronous and raises no callback of its own, so the stop is reported here.
        if (PausedThread() is { } thread)
        {
            OnStopped?.Invoke(thread, "pause");
            return;
        }
        // No thread runs managed code yet (the runtime is still starting): the pause is taken at its next event.
        _pauseRequested = true;
        _process.Continue(false);
    }

    /// <summary>A pause asked for while the process was held for the runtime's own event, taken instead of letting it go
    /// on once a thread runs managed code; true when it was.</summary>
    private bool TakePendingPause()
    {
        if (!_pauseRequested || PausedThread() is not { } thread)
        {
            return false;
        }
        _pauseRequested = false;
        OnStopped?.Invoke(thread, "pause");
        return true;
    }

    /// <summary>The thread a pause shows: the main thread, else the first running managed code; none before any does.</summary>
    private int? PausedThread()
    {
        if (_mainThreadId is { } main && _threads.TryGetValue(main, out ThreadInfo? mainThread) && HasManagedFrames(mainThread.Thread))
        {
            return main;
        }
        foreach ((int id, ThreadInfo info) in _threads)
        {
            if (HasManagedFrames(info.Thread))
            {
                return id;
            }
        }
        return null;
    }

    /// <summary>A thread paused outside managed code (in a sleep, a wait, a read) has no line to step from: any step runs
    /// it out to managed code first, where the step completes as one from the caller would.</summary>
    private bool StepOutToManagedCode(ICorDebugThread thread)
    {
        if (thread.ActiveFrame is ICorDebugILFrame)
        {
            return false;
        }
        if (_stepper is not null)
        {
            throw new InvalidOperationException("A step operation is already in progress");
        }
        ICorDebugStepper stepper = thread.CreateStepper();
        stepper.SetUnmappedStopMask(CorDebugUnmappedStop.STOP_NONE);
        if (_justMyCode)
        {
            stepper.SetJMC(true);
        }
        stepper.StepOut();
        _stepper = stepper;
        ContinueWithVariableClear();
        return true;
    }

    /// <summary>
    /// Step to the next line
    /// </summary>
    public async Task StepNext(int threadId)
    {
        _logger?.Invoke($"StepNext on thread {threadId}");
        if (_threads.TryGetValue(threadId, out ThreadInfo? threadInfo))
        {
            ICorDebugThread thread = threadInfo.Thread;
            if (StepOutToManagedCode(thread))
            {
                return;
            }
            ICorDebugFrame frame = thread.ActiveFrame;
            if (frame is not ICorDebugILFrame ilFrame)
            {
                throw new InvalidOperationException("Active frame is not an IL frame");
            }

            if (_stepper is not null)
            {
                throw new InvalidOperationException("A step operation is already in progress");
            }

            // Try async stepping first
            if (_asyncStepper is not null)
            {
                (bool handledByAsyncStepper, bool? useSimpleStepper) = await _asyncStepper.TrySetupAsyncStep(thread, AsyncStepper.StepType.StepOver);
                if (handledByAsyncStepper)
                {
                    if (useSimpleStepper is false)
                    {
                        ContinueWithVariableClear();
                        return;
                    }
                }
            }

            ICorDebugStepper stepper = SetupStepper(thread, AsyncStepper.StepType.StepOver);
            ContinueWithVariableClear();
        }
    }

    /// <summary>
    /// Step into
    /// </summary>
    public async Task StepIn(int threadId)
    {
        _logger?.Invoke($"StepIn on thread {threadId}");
        if (_threads.TryGetValue(threadId, out ThreadInfo? threadInfo))
        {
            ICorDebugThread thread = threadInfo.Thread;
            if (StepOutToManagedCode(thread))
            {
                return;
            }
            ICorDebugFrame? frame = thread.ActiveFrame;
            if (frame is not null)
            {
                // Try async stepping first
                if (_asyncStepper is not null)
                {
                    (bool handledByAsyncStepper, bool? useSimpleStepper) = await _asyncStepper.TrySetupAsyncStep(thread, AsyncStepper.StepType.StepIn);
                    if (handledByAsyncStepper)
                    {
                        if (useSimpleStepper is false)
                        {
                            ContinueWithVariableClear();
                            return;
                        }
                    }
                }

                ICorDebugStepper stepper = SetupStepper(thread, AsyncStepper.StepType.StepIn);
                ContinueWithVariableClear();
            }
        }
    }

    /// <summary>
    /// Step out
    /// </summary>
    public async Task StepOut(int threadId)
    {
        _logger?.Invoke($"StepOut on thread {threadId}");
        if (_threads.TryGetValue(threadId, out ThreadInfo? threadInfo))
        {
            ICorDebugThread thread = threadInfo.Thread;
            if (StepOutToManagedCode(thread))
            {
                return;
            }
            ICorDebugFrame? frame = thread.ActiveFrame;
            if (frame is not null)
            {
                // Try async stepping first
                if (_asyncStepper is not null)
                {
                    (bool handledByAsyncStepper, bool? useSimpleStepper) = await _asyncStepper.TrySetupAsyncStep(thread, AsyncStepper.StepType.StepOut);
                    if (handledByAsyncStepper)
                    {
                        if (useSimpleStepper is false)
                        {
                            ContinueWithVariableClear();
                            return;
                        }
                    }
                }

                ICorDebugStepper? stepper = SetupStepper(thread, AsyncStepper.StepType.StepOut);
                if (stepper is not null)
                {
                    ContinueWithVariableClear();
                }
            }
        }
    }

    /// <summary>
    /// Set breakpoints for a source file with optional conditions
    /// </summary>
    public List<BreakpointManager.BreakpointInfo> SetBreakpoints(string filePath, BreakpointRequest[] breakpoints)
    {
        //System.Diagnostics.Debugger.Launch();
        _logger?.Invoke($"SetBreakpoints: {filePath}, breakpoints: {string.Join(",", breakpoints.Select(b => $"L{b.Line} {(b.Column is not null ? $"C{b.Column}" : null)} {(b.Condition is not null ? $"[{b.Condition}]" : null)}"))}");

        // Deactivate and clear existing breakpoints for this file
        List<BreakpointManager.BreakpointInfo> existingBreakpoints = _breakpointManager.GetBreakpointsForFile(filePath);
        foreach (BreakpointManager.BreakpointInfo bp in existingBreakpoints)
        {
            if (bp.CorBreakpoint is not null)
            {
                try
                {
                    bp.CorBreakpoint.Activate(false);
                }
                catch (Exception ex)
                {
                    _logger?.Invoke($"Error deactivating breakpoint: {ex.Message}");
                }
            }
        }
        _breakpointManager.ClearBreakpointsForFile(filePath);

        // Create new breakpoints
        List<BreakpointManager.BreakpointInfo> result = new List<BreakpointManager.BreakpointInfo>();
        foreach (BreakpointRequest request in breakpoints)
        {
            BreakpointManager.BreakpointInfo bp = _breakpointManager.CreateBreakpoint(filePath, request.Line, request.Column, request.Condition, request.HitCondition, request.LogMessage, request.Suspend);
            if (request.Condition is not null && SyntaxFactory.ParseExpression(request.Condition).DescendantNodesAndSelf().Any(static node => node is AnonymousFunctionExpressionSyntax))
            {
                bp.Message = "Lambda expressions are not supported in breakpoint conditions.";
                result.Add(bp);
                continue;
            }

            // Try to bind the breakpoint if we have a process
            if (_process is not null)
            {
                TryBindBreakpoint(bp);
            }
            else
            {
                // No process yet, mark as pending
                bp.Message = "Breakpoint has not been processed by the debugger.";
            }

            result.Add(bp);
        }

        return result;
    }

    public List<BreakpointManager.BreakpointInfo> SetFunctionBreakpoints(FunctionBreakpointRequest[] breakpoints)
    {
        foreach (BreakpointManager.BreakpointInfo bp in _breakpointManager.GetFunctionBreakpoints())
        {
            foreach (BreakpointManager.FunctionBreakpointBinding binding in bp.FunctionBindings)
            {
                try { binding.CorBreakpoint.Activate(false); }
                catch (Exception ex) { _logger?.Invoke($"Error deactivating function breakpoint: {ex.Message}"); }
            }
        }
        _breakpointManager.ClearFunctionBreakpoints();

        List<BreakpointManager.BreakpointInfo> result = new List<BreakpointManager.BreakpointInfo>();
        foreach (FunctionBreakpointRequest request in breakpoints)
        {
            BreakpointManager.BreakpointInfo bp = _breakpointManager.CreateFunctionBreakpoint(request.Name, request.Condition, request.HitCondition, request.LogMessage);
            try
            {
                _ = FunctionBreakpointPattern.Parse(request.Name);
                foreach (ModuleInfo module in _modules.Values)
                {
                    TryBindFunctionBreakpoint(bp, module);
                }
                if (!bp.Verified)
                {
                    bp.Message = _process is null
                    ? "Breakpoint has not been processed by the debugger."
                    : $"No functions matching '{request.Name}' were found.";
                }
            }
            catch (ArgumentException ex)
            {
                bp.Message = ex.Message;
            }
            result.Add(bp);
        }
        return result;
    }

    /// <summary>
    /// Get all threads
    /// </summary>
    public List<(int id, string name)> GetThreads()
    {
        List<(int, string)> result = new List<(int, string)>();
        if (_process is null)
        {
            return result;
        }

        try
        {
            foreach ((int id, ThreadInfo? threadInfo) in _threads)
            {
                // A thread with no managed frame (one of the runtime's own) is a row that opens to
                // nothing, which no client can use (upstream #59).
                if (!HasManagedFrames(threadInfo.Thread))
                {
                    continue;
                }
                threadInfo.Name ??= threadInfo.Thread.GetThreadNameOrNull();
                result.Add((id, threadInfo.Name ?? (id == _mainThreadId ? "Main Thread" : "<No Name>")));
            }
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Error getting threads: {ex.Message}");
        }

        return result;
    }

    private static bool HasManagedFrames(ICorDebugThread thread)
    {
        try
        {
            return EnumerateFramesForThread(thread).Any();
        }
        catch (Exception error) when (error is COMException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Get stack trace for a thread
    /// </summary>
    public List<StackFrameInfo> GetStackTrace(int threadIdInt, int startFrame = 0, int? levels = null)
    {
        List<StackFrameInfo> result = new List<StackFrameInfo>();

        if (!_threads.TryGetValue(threadIdInt, out ThreadInfo? threadInfo))
        {
            return result;
        }
        ICorDebugThread thread = threadInfo.Thread;

        try
        {
            long endFrame = levels is null ? long.MaxValue : (long)startFrame + levels.Value;
            int index = 0;

            // Physical frames come first and are enumerated lazily, so a request for the topmost frames does not
            // have to walk the entire stack. We store the IL frames so their synthetic async caller frames can be
            // appended after all physical frames, once enumeration reaches past them.
            List<(int physicalDepth, ICorDebugILFrame frame)> ilFramesForSyntheticAsyncFrames = new List<(int physicalDepth, ICorDebugILFrame frame)>();
            ThreadId threadId = new ThreadId(threadIdInt);
            foreach ((int physicalDepth, ICorDebugFrame? frame) in EnumerateFramesForThread(thread).AsValueEnumerable().Index())
            {
                if (index >= endFrame)
                {
                    return result;
                }

                if (index >= startFrame)
                {
                    int frameId = _frameReferenceManager.GetOrCreateFrameId(threadId, new FrameStackDepth(physicalDepth));
                    result.Add(CreateStackFrameInfo(frameId, frame, false));
                }
                if (frame is ICorDebugILFrame ilFrame)
                {
                    ilFramesForSyntheticAsyncFrames.Add((physicalDepth, ilFrame));
                }

                index++;
            }

            foreach ((int physicalDepth, ICorDebugILFrame? ilFrame) in ilFramesForSyntheticAsyncFrames)
            {
                FrameStackDepth physicalFrameStackDepth = new FrameStackDepth(physicalDepth);
                foreach ((int syntheticFrameIndex, SyntheticAsyncCallerFrame? syntheticFrame) in GetSyntheticAsyncCallerFrames(ilFrame).AsValueEnumerable().Index())
                {
                    if (index >= endFrame)
                    {
                        return result;
                    }

                    if (index < startFrame)
                    {
                        index++;
                        continue;
                    }
                    index++;

                    int syntheticFrameId = _frameReferenceManager.GetOrCreateSyntheticAsyncFrameId(threadId, physicalFrameStackDepth, syntheticFrameIndex, syntheticFrame);
                    result.Add(CreateSyntheticAsyncStackFrameInfo(syntheticFrameId, threadId, physicalFrameStackDepth, syntheticFrame, decompileIfNeeded: false));
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Error getting stack trace: {ex.Message}");
        }

        return result;
    }

    public StackFrameInfo ResolveStackFrame(int frameId)
    {
        (ThreadId threadId, FrameStackDepth physicalFrameStackDepth, SyntheticAsyncCallerFrame frame)? syntheticFrame = _frameReferenceManager.GetSyntheticAsyncFrameById(frameId);
        if (syntheticFrame is not null)
        {
            return CreateSyntheticAsyncStackFrameInfo(frameId, syntheticFrame.Value.threadId, syntheticFrame.Value.physicalFrameStackDepth, syntheticFrame.Value.frame, decompileIfNeeded: true);
        }

        (ThreadId threadId, FrameStackDepth frameStackDepth) frameInfo = _frameReferenceManager.GetFrameInfoById(frameId) ?? throw new ArgumentException($"Unknown stack frame ID '{frameId}'.", nameof(frameId));
        ICorDebugFrame frame = GetFrameForThreadIdAndStackDepth(frameInfo.threadId, frameInfo.frameStackDepth);
        if (frame is not ICorDebugILFrame)
        {
            throw new InvalidOperationException($"Stack frame '{frameId}' cannot be resolved because it is not an IL frame.");
        }

        return CreateStackFrameInfo(frameId, frame, true);
    }

    private StackFrameInfo CreateStackFrameInfo(int frameId, ICorDebugFrame frame, bool decompileIfNeeded)
    {
        StackFrameInfo stackFrameInfo = new StackFrameInfo
        {
            Id = frameId,
            Name = null!,
            Line = 0,
            EndLine = null,
            Column = 0,
            EndColumn = null,
            Source = null,
            IsUserCode = false,
            IsResolved = true,
            DecompiledSourceInfo = null
        };

        if (frame is ICorDebugILFrame ilFrame)
        {
            ICorDebugFunction function = ilFrame.Function;
            ModuleInfo module = _modules[function.Module.BaseAddress];
            // returns null if the function is not a state machine method
            int? kickoffMethodToken = module.MetadataReader.GetStateMachineKickoffMethodToken(function.Token);
            stackFrameInfo.Name = GetMethodFormattedName(module, kickoffMethodToken ?? function.Token, ilFrame.TypeParameters);
            SourceInfo? sourceInfo = GetSourceInfoAtFrame(ilFrame, decompileIfNeeded);
            stackFrameInfo.IsUserCode = module.IsUserCode;
            stackFrameInfo.IsResolved = module.MetadataReader.HasSymbols;
            if (sourceInfo is not null)
            {
                stackFrameInfo.Line = sourceInfo.Value.StartLine;
                stackFrameInfo.EndLine = sourceInfo.Value.EndLine;
                stackFrameInfo.Column = sourceInfo.Value.StartColumn;
                stackFrameInfo.EndColumn = sourceInfo.Value.EndColumn;
                stackFrameInfo.Source = sourceInfo.Value.FilePath;
                stackFrameInfo.DecompiledSourceInfo = sourceInfo.Value.DecompiledSourceInfo;
            }
        }
        else if (frame is ICorDebugInternalFrame internalFrame)
        {
            stackFrameInfo.Name = internalFrame.FrameType.ToDisplayName();
        }
        else if (frame is ICorDebugNativeFrame)
        {
            stackFrameInfo.Name = "[Native Frame]";
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "Unknown frame type");
        }

        return stackFrameInfo;
    }

    private StackFrameInfo CreateSyntheticAsyncStackFrameInfo(int frameId, ThreadId threadId, FrameStackDepth physicalFrameStackDepth, SyntheticAsyncCallerFrame frame, bool decompileIfNeeded)
    {
        SourceInfo? source = GetSyntheticAsyncFrameSourceInfo(frame, threadId, physicalFrameStackDepth, decompileIfNeeded);
        return new StackFrameInfo
        {
            Id = frameId,
            Name = frame.Name,
            Line = source?.StartLine ?? 0,
            EndLine = source?.EndLine,
            Column = source?.StartColumn ?? 0,
            EndColumn = source?.EndColumn,
            Source = source?.FilePath,
            IsUserCode = frame.Module.IsUserCode,
            IsResolved = frame.Module.MetadataReader.HasSymbols,
            DecompiledSourceInfo = source?.DecompiledSourceInfo
        };
    }

    /// <summary>
    /// Get scopes for a stack frame
    /// </summary>
    public List<ScopeInfo> GetScopes(int frameId)
    {
        List<ScopeInfo> result = new List<ScopeInfo>();

        (ThreadId threadId, FrameStackDepth physicalFrameStackDepth, SyntheticAsyncCallerFrame frame)? syntheticFrame = _frameReferenceManager.GetSyntheticAsyncFrameById(frameId);
        if (syntheticFrame is not null)
        {
            int syntheticLocalsReference = _variableManager.CreateReference(new VariablesReference(StoredReferenceKind.SyntheticAsyncScope,
                syntheticFrame.Value.frame.StateMachine, syntheticFrame.Value.threadId, syntheticFrame.Value.physicalFrameStackDepth, null));
            result.Add(new ScopeInfo { Name = "Locals", VariablesReference = syntheticLocalsReference, Expensive = false });
            return result;
        }
        (ThreadId threadId, FrameStackDepth frameStackDepth)? frameInfo = _frameReferenceManager.GetFrameInfoById(frameId);
        if (frameInfo is not var (threadId, frameStackDepth))
        {
            return result;
        }

        ICorDebugFrame frame = GetFrameForThreadIdAndStackDepth(threadId, frameStackDepth);
        if (frame is not ICorDebugILFrame ilFrame)
        {
            return result;
        }

        ICorDebugValue[] localVariables = ilFrame.LocalVariables;
        ICorDebugValue[] arguments = ilFrame.Arguments;
        ICorDebugThread? thread = _threads.GetValueOrDefault(threadId.Value)?.Thread;
        ArgumentNullException.ThrowIfNull(thread);
        bool hasCurrentException = thread.TryGetCurrentException(out _) is Cor.S_OK;
        if (localVariables.Length is 0 && arguments.Length is 0 && !hasCurrentException)
        {
            return result;
        }

        // can this just be the same reference?
        int localsRef = _variableManager.CreateReference(new VariablesReference(StoredReferenceKind.Scope, null, threadId, frameStackDepth, null));
        result.Add(new ScopeInfo
        {
            Name = "Locals",
            VariablesReference = localsRef,
            Expensive = false
        });
        return result;
    }

    /// <summary>
    /// Get variables for a scope
    /// </summary>
    public async Task<List<VariableInfo>> GetVariables(int variablesReferenceInt)
    {
        List<VariableInfo> result = new List<VariableInfo>();

        VariablesReference? variablesReferenceNullable = _variableManager.GetReference(variablesReferenceInt);
        if (variablesReferenceNullable is not { } variablesReference)
        {
            throw new ArgumentException("Invalid variables reference");
        }

        try
        {
            if (variablesReference.ReferenceKind is StoredReferenceKind.Scope)
            {
                ICorDebugILFrame ilFrame = GetIlFrameForThreadIdAndStackDepth(variablesReference.ThreadId, variablesReference.FrameStackDepth);
                ICorDebugFunction corDebugFunction = ilFrame.Function;
                ModuleInfo module = _modules[corDebugFunction.Module.BaseAddress];
                await AddCurrentException(result, variablesReference.ThreadId, variablesReference.FrameStackDepth);
                ICorDebugValue? classContainingHoistedLocalsValue = await AddArguments(module, corDebugFunction, result, variablesReference.ThreadId, variablesReference.FrameStackDepth);
                await AddLocalVariables(module, corDebugFunction, result, variablesReference.ThreadId, variablesReference.FrameStackDepth, classContainingHoistedLocalsValue);
            }
            else if (variablesReference.ReferenceKind is StoredReferenceKind.SyntheticAsyncScope)
            {
                ICorDebugValue stateMachine = variablesReference.ObjectValue!;
                await AddMembers(stateMachine, stateMachine.ExactType, variablesReference.ThreadId, variablesReference.FrameStackDepth, result);
            }
            else if (variablesReference.ReferenceKind is StoredReferenceKind.StackVariable)
            {
                if (variablesReference.DebuggerProxyInstance is not null)
                {
                    // get the public members of the debugger proxy instance instead
                    ICorDebugObjectValue objectValue = variablesReference.DebuggerProxyInstance.UnwrapDebugValueToObject();
                    await AddMembersAndStaticPseudoVariable(variablesReference.DebuggerProxyInstance, objectValue.ExactType, variablesReference.ThreadId, variablesReference.FrameStackDepth, result, false);
                    int rawValueVariablesReference = _variableManager.CreateReference(new VariablesReference(StoredReferenceKind.RawView, variablesReference.ObjectValue, variablesReference.ThreadId, variablesReference.FrameStackDepth, null));
                    VariableInfo rawValuePseudoVariable = new VariableInfo
                    {
                        Name = "Raw View",
                        Value = "",
                        Type = "",
                        PresentationHint = new VariablePresentationHint { Kind = PresentationHintKind.Class },
                        VariablesReference = rawValueVariablesReference
                    };
                    result.Add(rawValuePseudoVariable);
                    return result;
                }
                ICorDebugValue unwrappedDebugValue = variablesReference.ObjectValue!.UnwrapDebugValue();

                if (unwrappedDebugValue is ICorDebugArrayValue arrayValue)
                {
                    await AddArrayElements(arrayValue, variablesReference.ThreadId, variablesReference.FrameStackDepth, result, referenceValue: variablesReference.ObjectValue);
                }
                else if (unwrappedDebugValue is ICorDebugObjectValue objectValue)
                {
                    if (IsEnumerable(objectValue.ExactType))
                    {
                        AddEnumerablePseudoVariables(variablesReference, result);
                    }
                    else
                    {
                        await AddMembersAndStaticPseudoVariable(variablesReference.ObjectValue!, objectValue.ExactType, variablesReference.ThreadId, variablesReference.FrameStackDepth, result);
                    }
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(unwrappedDebugValue));
                }
            }
            else if (variablesReference.ReferenceKind is StoredReferenceKind.StaticClassVariable)
            {
                ICorDebugObjectValue objectValue = variablesReference.ObjectValue!.UnwrapDebugValueToObject();
                await AddStaticMembers(variablesReference.ObjectValue!, objectValue.ExactType, variablesReference.ThreadId, variablesReference.FrameStackDepth, result);
            }
            else if (variablesReference.ReferenceKind is StoredReferenceKind.RawView)
            {
                ICorDebugObjectValue objectValue = variablesReference.ObjectValue!.UnwrapDebugValueToObject();
                await AddMembersAndStaticPseudoVariable(variablesReference.ObjectValue!, objectValue.ExactType, variablesReference.ThreadId, variablesReference.FrameStackDepth, result);
            }
            else if (variablesReference.ReferenceKind is StoredReferenceKind.EnumerableResults)
            {
                await AddEnumerableResults(variablesReference, result);
            }
            else if (variablesReference.ReferenceKind is StoredReferenceKind.ArrayRange)
            {
                ICorDebugArrayValue arrayValue = (ICorDebugArrayValue)variablesReference.ObjectValue!.UnwrapDebugValue();
                await AddArrayElements(arrayValue, variablesReference.ThreadId, variablesReference.FrameStackDepth, result, variablesReference.ArrayIndices, variablesReference.ArrayStartOffset, variablesReference.ArrayCount, variablesReference.ObjectValue);
            }
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Error getting variables: {ex.Message}, {ex}");
            throw;
        }

        return result;
    }

    /// <summary>
    /// Evaluate an expression
    /// </summary>
    public async Task<VariableInfo> Evaluate(string expression, int? frameId)
    {
        _logger?.Invoke($"Evaluate: {expression}");
        if (frameId is null or 0)
        {
            throw new InvalidOperationException("Frame ID is required for evaluation");
        }

        if (_frameReferenceManager.GetSyntheticAsyncFrameById(frameId.Value) is not null)
        {
            throw new InvalidOperationException("Expression evaluation is not supported for synthetic async caller frames");
        }

        (ThreadId threadId, FrameStackDepth frameStackDepth)? frameInfo = _frameReferenceManager.GetFrameInfoById(frameId.Value);
        if (frameInfo is not var (threadId, frameStackDepth))
        {
            throw new InvalidOperationException("Frame ID does not exist");
        }

        ICorDebugThread? thread = _threads.GetValueOrDefault(threadId.Value)?.Thread;
        ArgumentNullException.ThrowIfNull(thread);

        CompiledExpressionEvaluationContext evalContext = new CompiledExpressionEvaluationContext(thread, threadId, frameStackDepth);
        ArgumentNullException.ThrowIfNull(_expressionEvaluator);
        using EvaluationResult result = await _expressionEvaluator.Evaluate(expression, evalContext);

        if (result.Error is not null)
        {
            _logger?.Invoke($"Evaluation error: {result.Error}");
            return new VariableInfo
            {
                Name = null!,
                Value = result.Error,
                Type = null,
                PresentationHint = new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation },
                VariablesReference = 0
            };
        }
        (string? friendlyTypeName, string? value, ICorDebugValue? debuggerProxyInstance, bool resultIsError) = await GetValueForCorDebugValueAsync(result.Value!, threadId, frameStackDepth, true);
        VariablePresentationHint? variablePresentationHint = resultIsError ? new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation } : null;
        int variablesReference = GetVariablesReference(result.Value!, friendlyTypeName, threadId, frameStackDepth, debuggerProxyInstance);
        VariableInfo variableInfo = new VariableInfo
        {
            Name = null!,
            Value = value,
            Type = friendlyTypeName,
            PresentationHint = variablePresentationHint,
            VariablesReference = variablesReference
        };
        if (variablesReference != 0)
        {
            result.RelinquishResultHandleOwnership();
        }

        return variableInfo;
    }

    /// <summary>
    /// Terminate the debugged process
    /// </summary>
    public void Terminate()
    {
        _logger?.Invoke("Terminate");
        if (_process is not null && !_exited.IsSet)
        {
            try
            {
                // CORDBG_E_PROCESS_NOT_SYNCHRONIZED is thrown if attempting to terminate a running process. We need to stop it first.
                if (_process.TryIsRunning(out bool isRunning) is Cor.S_OK && isRunning)
                {
                    int stopResult = _process.TryStop(0);
                    if (stopResult is not (Cor.S_OK or Cor.CORDBG_E_PROCESS_TERMINATED))
                    {
                        _logger?.Invoke($"Error stopping process before terminating it: {stopResult}");
                    }
                }
                _process.Terminate(0);
                // The process goes on the runtime's thread; letting go of it before it has would race that.
                if (!_exited.Wait(TimeSpan.FromSeconds(5)))
                {
                    _logger?.Invoke("The process had not exited 5 s after Terminate");
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error terminating process: {ex.Message}");
            }
        }
        Dispose();
    }

    /// <summary>
    /// Disconnect from the debuggee and Dispose
    /// </summary>
    public void Disconnect(bool terminateDebuggee)
    {
        _logger?.Invoke($"Disconnect (terminate: {terminateDebuggee})");

        if (terminateDebuggee)
        {
            Terminate();
        }
        else
        {
            if (_process is not null && _isAttached && !_exited.IsSet && _process?.TryIsRunning(out bool isRunning) is Cor.S_OK && isRunning)
            {
                int hResult = _process.TryStop(0);
                if (hResult is not (Cor.S_OK or Cor.CORDBG_E_PROCESS_TERMINATED))
                {
                    _logger?.Invoke($"Error stopping process during disconnect: {hResult}");
                }
            }
            Dispose();
        }
    }

    public async Task<ExceptionInfo> ExceptionInfo(ThreadId threadId)
    {
        _logger?.Invoke($"ExceptionInfo for thread {threadId.Value}");
        ICorDebugThread thread = _process!.GetThread(threadId.Value);
        if (thread.TryGetCurrentException(out ICorDebugValue? currentException) is not Cor.S_OK)
        {
            _logger?.Invoke("No current exception");
            throw new InvalidOperationException("No current exception on thread");
        }

        FrameStackDepth frameStackDepth = new FrameStackDepth(0);
        (string? friendlyTypeName, _, _, _) = await GetValueForCorDebugValueAsync(currentException, threadId, frameStackDepth, false);

        string source = await GetExceptionPropertyValue(currentException, "Source");
        string message = await GetExceptionPropertyValue(currentException, "Message");
        ExceptionInfo.ExceptionDetails details = await Details(currentException, friendlyTypeName, "$exception", depth: 0);
        string innermost = Innermost(details);

        ExceptionInfo exceptionInfo = new ExceptionInfo
        {
            ExceptionId = $"CLR/{friendlyTypeName}",
            // With an inner exception, the line netcoredbg and Visual Studio add, naming the innermost.
            Description = $"Exception thrown: '{friendlyTypeName}' in {source}.dll: '{message}'"
                + (details.InnerException.Count > 0 ? $"\n Inner exceptions found, see $exception in variables window for more details.\n Innermost exception: {innermost}" : ""),
            BreakMode = _exceptionBreakModes.GetValueOrDefault(threadId, ExceptionBreakMode.Unknown),
            Code = 0,
            Details = details,
        };
        return exceptionInfo;

        // An exception's details, its inner exceptions' within (upstream #50), as deep as they go,
        // within reason.
        async Task<ExceptionInfo.ExceptionDetails> Details(ICorDebugValue exception, string typeName, string evaluateName, int depth)
        {
            string detailMessage = await GetExceptionPropertyValue(exception, "Message");
            List<ExceptionInfo.ExceptionDetails> inner = [];
            ICorDebugValue? innerValue = depth < 10 ? await exception.GetPropertyValue(ProcessRuntimeEventsUntilEvalEvent, EvalStatus, (ICorDebugILFrame)thread.ActiveFrame, "InnerException") : null;
            try
            {
                if (innerValue is not null && innerValue.UnwrapDebugValue() is not ICorDebugReferenceValue { IsNull: true })
                {
                    (string? innerType, _, _, _) = await GetValueForCorDebugValueAsync(innerValue, threadId, frameStackDepth, false);
                    inner.Add(await Details(innerValue, innerType ?? "System.Exception", $"{evaluateName}.InnerException", depth + 1));
                }
            }
            finally
            {
                if (innerValue is ICorDebugHandleValue innerHandle)
                {
                    innerHandle.TryDispose();
                }
            }
            int lastDot = typeName.LastIndexOf('.');
            return new ExceptionInfo.ExceptionDetails
            {
                Message = detailMessage,
                TypeName = lastDot is not -1 ? typeName[(lastDot + 1)..] : typeName,
                FullTypeName = typeName,
                EvaluateName = evaluateName,
                StackTrace = await GetExceptionPropertyValue(exception, "StackTrace"),
                InnerException = inner,
                FormattedDescription = $"**{typeName}:** '{detailMessage}'",
                HResult = int.Parse(await GetExceptionPropertyValue(exception, "HResult")),
                Source = await GetExceptionPropertyValue(exception, "Source"),
            };
        }

        static string Innermost(ExceptionInfo.ExceptionDetails details) =>
            details.InnerException.Count > 0 ? Innermost(details.InnerException[0]) : details.FullTypeName;

        async Task<string> GetExceptionPropertyValue(ICorDebugValue exception, string propertyName)
        {
            ICorDebugValue propertyValue = await exception.GetPropertyValue(ProcessRuntimeEventsUntilEvalEvent, EvalStatus, (ICorDebugILFrame)thread.ActiveFrame, propertyName)
                ?? throw new InvalidOperationException($"Exception property '{propertyName}' returned no value");
            try
            {
                (_, string? value, _, _) = await GetValueForCorDebugValueAsync(propertyValue, threadId, frameStackDepth, false);
                return value;
            }
            finally
            {
                if (propertyValue is ICorDebugHandleValue handle)
                {
                    handle.TryDispose();
                }
            }
        }
    }

    /// <summary>Text for the launched program's standard input, or the end of its input when
    /// <paramref name="close"/>; nothing for a program the debugger attached to.</summary>
    public void WriteInput(string text, bool close)
    {
        if (_launch is { } launch && !_exited.IsSet)
        {
            try
            {
                launch.Input.Write(Encoding.UTF8.GetBytes(text));
                launch.Input.Flush();
                if (close)
                {
                    launch.Input.Close();
                }
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                _logger?.Invoke($"The program's input is closed: {error.Message}");
            }
            return;
        }
        if (_debuggeeProcess is not { HasExited: false } process)
        {
            return;
        }
        process.StandardInput.Write(text);
        process.StandardInput.Flush();
        if (close)
        {
            process.StandardInput.Close();
        }
    }

    public void SetExceptionBreakpoints(IReadOnlyList<ExceptionBreakpointRequest> breakpoints)
    {
        _exceptionBreakpoints = breakpoints.ToArray();
    }
}
