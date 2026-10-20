using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Passenger.Debugger.Interop;

namespace Passenger.Debugger;

public static class Extensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLiteral(this mdFieldDef mdFieldDef, IMetaDataImport metadataImport)
    {
        (string szField, mdTypeDef pClass, CorFieldAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) fieldProps = metadataImport.GetFieldProps(mdFieldDef);
        bool isStatic = fieldProps.pdwAttr.IsFdLiteral();
        return isStatic;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsStatic(this mdFieldDef mdFieldDef, IMetaDataImport metadataImport)
    {
        (string szField, mdTypeDef pClass, CorFieldAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) fieldProps = metadataImport.GetFieldProps(mdFieldDef);
        bool isStatic = fieldProps.pdwAttr.IsFdStatic();
        return isStatic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPublic(this mdFieldDef mdFieldDef, IMetaDataImport metadataImport)
    {
        (string szField, mdTypeDef pClass, CorFieldAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) fieldProps = metadataImport.GetFieldProps(mdFieldDef);
        bool isPublic = fieldProps.pdwAttr.IsFdPublic();
        return isPublic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPublic(this mdProperty mdProperty, IMetaDataImport metadataImport)
    {
        (string szProperty, mdTypeDef pClass, CorPropertyAttr pdwPropFlags, nint ppvSig, int pbSig, CorElementType pdwCPlusTypeFlag, nint ppDefaultValue, int pcchDefaultValue, mdMethodDef pmdSetter, mdMethodDef pmdGetter, mdMethodDef rmdOtherMethod, int pcOtherMethod) propertyProps = metadataImport.GetPropertyProps(mdProperty);
        (string szMethod, mdTypeDef pClass, CorMethodAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, int pulCodeRVA, int pdwImplFlags) getterMethodProps = metadataImport.GetMethodProps(propertyProps.pmdGetter);
        bool isPublic = getterMethodProps.pdwAttr.IsMdPublic();
        return isPublic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsStatic(this mdProperty mdProperty, IMetaDataImport metadataImport)
    {
        (string szProperty, mdTypeDef pClass, CorPropertyAttr pdwPropFlags, nint ppvSig, int pbSig, CorElementType pdwCPlusTypeFlag, nint ppDefaultValue, int pcchDefaultValue, mdMethodDef pmdSetter, mdMethodDef pmdGetter, mdMethodDef rmdOtherMethod, int pcOtherMethod) propertyProps = metadataImport.GetPropertyProps(mdProperty);
        (string szMethod, mdTypeDef pClass, CorMethodAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, int pulCodeRVA, int pdwImplFlags) getterMethodProps = metadataImport.GetMethodProps(propertyProps.pmdGetter);
        bool isStatic = getterMethodProps.pdwAttr.IsMdStatic();
        return isStatic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsStatic(this mdMethodDef methodToken, IMetaDataImport metaDataImport)
    {
        (string szMethod, mdTypeDef pClass, CorMethodAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, int pulCodeRVA, int pdwImplFlags) methodProps = metaDataImport.GetMethodProps(methodToken);
        bool isStatic = methodProps.pdwAttr.IsMdStatic();
        return isStatic;
    }

    // To my knowledge, only strings from CustomAttributes 'Type' ctor use this '+' format
    public static mdTypeDef? FindMaybeNestedTypeDefByNameOrNull(this IMetaDataImport metadataImport, string typeName)
    {
        string[] nestedClasses = typeName.Split('+');
        mdTypeDef? enclosingClass = null;
        foreach (string nestedClass in nestedClasses)
        {
            mdTypeDef? mdTypeDef = metadataImport.FindTypeDefByNameOrNull(nestedClass, enclosingClass ?? mdToken.Nil);
            if (mdTypeDef is null)
            {
                return null;
            }

            enclosingClass = mdTypeDef;
        }
        return enclosingClass;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static mdTypeDef? FindTypeDefByNameOrNull(this IMetaDataImport metadataImport, string typeName, mdToken enclosingClass)
    {
        int result = metadataImport.TryFindTypeDefByName(typeName, enclosingClass, out mdTypeDef mdTypeDef);
        if (result is Cor.S_OK)
        {
            return mdTypeDef;
        }

        return null;
    }

    public static mdTypeDef? FindTypeDefByNameOrNullInCandidateNamespaces(this IMetaDataImport metadataImport, string typeName, mdToken enclosingClass, ImmutableArray<string> candidateNamespaces)
    {
        foreach (string candidateNamespace in candidateNamespaces)
        {
            string fullTypeName = string.IsNullOrEmpty(candidateNamespace) ? typeName : $"{candidateNamespace}.{typeName}";
            int result = metadataImport.TryFindTypeDefByName(fullTypeName, enclosingClass, out mdTypeDef mdTypeDef);
            if (result is Cor.S_OK)
            {
                return mdTypeDef;
            }
        }
        return null;
    }

    // https://github.com/Samsung/netcoredbg/blob/8b8b22200fecdb1aec5f47af63215462d8c79a4b/src/debugger/evaluator.cpp#L695
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsCompilerGeneratedFieldName(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        if (fieldName.Length > 1 && fieldName.StartsWith('<'))
        {
            return true;
        }

        if (fieldName.Length > 4 && fieldName.StartsWith("CS$<", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    public static mdProperty? GetPropertyWithName(this IMetaDataImport metaDataImport, mdTypeDef mdTypeDef, string propertyName)
    {
        IEnumerable<mdProperty> properties = metaDataImport.EnumProperties(mdTypeDef);

        foreach (mdProperty property in properties)
        {
            if (property.IsNil)
            {
                continue;
            }

            (string szProperty, mdTypeDef pClass, CorPropertyAttr pdwPropFlags, nint ppvSig, int pbSig, CorElementType pdwCPlusTypeFlag, nint ppDefaultValue, int pcchDefaultValue, mdMethodDef pmdSetter, mdMethodDef pmdGetter, mdMethodDef rmdOtherMethod, int pcOtherMethod) propertyProps = metaDataImport.GetPropertyProps(property);
            if (propertyProps.szProperty == propertyName)
            {
                return property;
            }
        }

        return null;
    }

    public static bool HasAnyAttribute(this IMetaDataImport metadataImport, mdToken token, string[] attributeNames)
    {
        foreach (string attributeName in attributeNames)
        {
            if (metadataImport.TryGetCustomAttributeByName(token, attributeName, out _, out _) is Cor.S_OK)
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsExtensionMethod(this IMetaDataImport metadataImport, mdToken token)
    {
        return metadataImport.HasAnyAttribute(token, [AttributeConstants.ExtensionMethodAttributeName]);
    }

    internal static async Task<ICorDebugValue?> CallParameterlessInstanceMethodAsync(this ICorDebugEval eval, Func<Task<CorDebugManagedCallbackEventArgs>> processEventsUntilEvalEventFunc, EvalStatus evalStatus, ICorDebugFunction corDebugFunction, ICorDebugValue corDebugValue)
    {
        const bool isStatic = false;

        ICorDebugType[] typeParameterArgs = corDebugValue.ExactType.TypeParameters;

        // For instance properties, pass the object; for static, pass nothing. Must pass the original CorDebugReferenceValue, not the dereferenced one.
        ICorDebugValue[] corDebugValues = isStatic ? [] : [corDebugValue];
        ICorDebugValue? result = await eval.CallParameterizedFunctionAsync(processEventsUntilEvalEventFunc, evalStatus, corDebugFunction, typeParameterArgs.Length, typeParameterArgs, corDebugValues.Length, corDebugValues);
        return result;
    }

    internal static async Task<ICorDebugValue?> CallParameterizedFunctionAsync(this ICorDebugEval eval, Func<Task<CorDebugManagedCallbackEventArgs>> processEventsUntilEvalEventFunc, EvalStatus evalStatus, ICorDebugFunction corDebugFunction, int typeParamCount, ICorDebugType[]? typeParameterArgs, int paramCount, ICorDebugValue[] corDebugValues, bool throwOnException = false)
    {
        // Ensure that the object passed in corDebugValues is a CorDebugReferenceValue (when containing object is an instance class), ie must not be dereferenced
        return await RunEvalAsync(eval, processEventsUntilEvalEventFunc, evalStatus, throwOnException,
            () => eval.CallParameterizedFunction(corDebugFunction, typeParamCount, typeParameterArgs, paramCount, corDebugValues),
            e =>
            {
                int getResultResult = e.Eval.TryGetResult(out ICorDebugValue? result);
                if (getResultResult is not Cor.CORDBG_S_FUNC_EVAL_HAS_NO_RESULT && result is null)
                {
                    Marshal.ThrowExceptionForHR(getResultResult);
                }

                return result;
            });
    }

    internal static async Task<ICorDebugValue?> NewParameterizedObjectNoConstructorAsync(this ICorDebugEval eval, Func<Task<CorDebugManagedCallbackEventArgs>> processEventsUntilEvalEventFunc, EvalStatus evalStatus, ICorDebugClass pClass, int nTypeArgs, ICorDebugType[]? ppTypeArgs, bool throwOnException = false)
    {
        return await RunEvalAsync(eval, processEventsUntilEvalEventFunc, evalStatus, throwOnException,
            () => eval.NewParameterizedObjectNoConstructor(pClass, nTypeArgs, ppTypeArgs),
            e => e.Eval.Result);
    }

    internal static async Task<ICorDebugValue?> NewParameterizedObjectAsync(this ICorDebugEval eval, Func<Task<CorDebugManagedCallbackEventArgs>> processEventsUntilEvalEventFunc, EvalStatus evalStatus, ICorDebugFunction corDebugFunction, int nTypeArgs, ICorDebugType[]? ppTypeArgs, int argCount, ICorDebugValue[] argValues, bool throwOnException = false)
    {
        return await RunEvalAsync(eval, processEventsUntilEvalEventFunc, evalStatus, throwOnException,
            () => eval.NewParameterizedObject(corDebugFunction, nTypeArgs, ppTypeArgs, argCount, argValues),
            e => e.Eval.Result);
    }

    internal static async Task<ICorDebugValue?> NewParameterizedArrayAsync(this ICorDebugEval eval, Func<Task<CorDebugManagedCallbackEventArgs>> processEventsUntilEvalEventFunc, EvalStatus evalStatus, ICorDebugType elementType, uint length, bool throwOnException = false)
    {
        return await RunEvalAsync(eval, processEventsUntilEvalEventFunc, evalStatus, throwOnException,
            () => eval.NewParameterizedArray(elementType, 1, [length], [0]),
            e => e.Eval.Result);
    }

    internal static async Task<ICorDebugValue> NewStringAsync(this ICorDebugEval eval, Func<Task<CorDebugManagedCallbackEventArgs>> processEventsUntilEvalEventFunc, EvalStatus evalStatus, string str, bool throwOnException = false)
    {
        return (await RunEvalAsync(eval, processEventsUntilEvalEventFunc, evalStatus, throwOnException,
            () => eval.NewString(str),
            e => e.Eval.Result))!;
    }

    /// <summary>How long code the debugger runs in the program (a getter, a watch's call) may take before
    /// it is stopped, so one that never returns cannot hang the debugger (upstream #30). A static of the adapter's
    /// process, one per debug session whichever window started it; the adapter takes PASSENGER_EVAL_TIMEOUT_MS
    /// (the tests' run gives 1 s), and the waits for an abort to take scale with it.</summary>
    public static TimeSpan EvaluationTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The variable that gives <see cref="EvaluationTimeout"/> in milliseconds.</summary>
    public const string EvaluationTimeoutVariable = "PASSENGER_EVAL_TIMEOUT_MS";

    /// <summary>How long an abort may take before a rude abort is tried: 2 s of the 5 s.</summary>
    private static TimeSpan AbortWait => EvaluationTimeout * 0.4;

    /// <summary>How long a rude abort may take before the evaluation is left to run on: 5 s of the 5 s.</summary>
    private static TimeSpan RudeAbortWait => EvaluationTimeout;

    /// <summary>Why a read's evaluation did not run, or was left to end in the program: a continue, a step or a stop came for
    /// the program, which does not wait for it (<see cref="ManagedDebugger.Leaving"/>).</summary>
    internal const string NotEvaluated = "Not evaluated: the program went on";
    private const string GaveWay = "The program went on before the evaluation ended";

    private static async Task<ICorDebugValue?> RunEvalAsync(ICorDebugEval eval, Func<Task<CorDebugManagedCallbackEventArgs>> processEventsUntilEvalEventFunc, EvalStatus evalStatus, bool throwOnException, Action startEval, Func<EvalCompleteCorDebugManagedCallbackEventArgs, ICorDebugValue?> onComplete)
    {
        ICorDebugValue? returnValue = null;

        ICorDebugThread thread = eval.Thread;
        if (evalStatus.ImplicitOff is { } off && !evalStatus.Explicit)
        {
            throw new ManagedDebugger.EvalException(off);
        }
        CancellationToken leaving = evalStatus.Leaving;
        if (leaving.IsCancellationRequested)
        {
            throw new ManagedDebugger.EvalException(NotEvaluated);
        }
        if (evalStatus.Abandoned.ContainsKey(thread.Id))
        {
            throw new ManagedDebugger.EvalException("An evaluation that could not be stopped is still running on this thread, so no other can run on it until it returns");
        }

        try
        {
            startEval();
        }
        catch (COMException refused)
        {
            // Nothing ran: the runtime runs no code on a thread a pause caught in optimized or native code.
            throw new ManagedDebugger.EvalRefusedException(refused);
        }

        ICorDebugProcess process = thread.Process;
        evalStatus.ThreadId = thread.Id;
        using CancellationTokenSource reading = new();
        evalStatus.Reading = reading.Token;
        try
        {
            // Only the evaluation's thread runs, as in Visual Studio: the others' values hold still and none meets a breakpoint unseen.
            process.SetAllThreadsDebugState(CorDebugThreadState.THREAD_SUSPEND, thread);
            process.Continue(false);
            Task<CorDebugManagedCallbackEventArgs> waiting = processEventsUntilEvalEventFunc();
            long began = System.Diagnostics.Stopwatch.GetTimestamp();
            bool ended = await Task.WhenAny(waiting, Task.Delay(EvaluationTimeout, leaving)) == waiting;
            if (!ended && leaving.IsCancellationRequested)
            {
                // Left to a request that leaves the stop, which neither waits on it nor aborts it early (an abort holds the
                // runtime half a second): the program is taken back to go on with it, its end let go whenever it comes, and
                // it is aborted only once its time is up, as it would have been. One that ended as it was given up is read.
                ended = await GiveUp(waiting) is not null;
                if (!ended)
                {
                    process.Stop(0);
                    evalStatus.Abandoned[thread.Id] = eval;
                    evalStatus.AbortWhenDue?.Invoke(thread.Id, eval, EvaluationTimeout - System.Diagnostics.Stopwatch.GetElapsedTime(began));
                    throw new ManagedDebugger.EvalException(GaveWay);
                }
            }
            if (!ended)
            {
                // Abort, then a rude abort if that does not take; either way the runtime still ends the
                // evaluation with its callback, which is waited for so no other request reads it.
                eval.TryAbort();
                if (await Task.WhenAny(waiting, Task.Delay(AbortWait)) != waiting && eval is ICorDebugEval2 rude)
                {
                    rude.TryRudeAbort();
                }
                bool stopped = await Task.WhenAny(waiting, Task.Delay(RudeAbortWait)) == waiting || await GiveUp(waiting) is not null;
                if (stopped && eval.TryGetResult(out ICorDebugValue? partial) is Cor.S_OK && partial is ICorDebugHandleValue handle)
                {
                    handle.TryDispose();
                }
                if (!stopped)
                {
                    // Code blocked outside the runtime (a read of input) cannot be stopped: the program is taken back, paused
                    // as the IDE shows it, and the evaluation runs on with it, its end let go whenever it comes.
                    process.Stop(0);
                    evalStatus.Abandoned[thread.Id] = eval;
                }
                evalStatus.ImplicitOff = $"Implicit evaluation is disabled: an evaluation took longer than {EvaluationTimeout.TotalSeconds:0.#} s. It is enabled again once the program runs on";
                throw new ManagedDebugger.EvalException(stopped
                    ? $"The evaluation took longer than {EvaluationTimeout.TotalSeconds:0.#} s and was stopped: it may wait on another thread, which stays paused while it runs"
                    : $"The evaluation took longer than {EvaluationTimeout.TotalSeconds:0.#} s and could not be stopped: it runs on in the program until it returns, the program paused meanwhile");
            }
            CorDebugManagedCallbackEventArgs evalEvent = await waiting;
            if (evalEvent is EvalCompleteCorDebugManagedCallbackEventArgs completeEvent)
            {
                if (completeEvent.Eval != eval)
                {
                    throw new ManagedDebugger.EvalException("EvalComplete callback error - Eval does not match");
                }

                returnValue = onComplete(completeEvent);
            }
            else if (evalEvent is EvalExceptionCorDebugManagedCallbackEventArgs exceptionEvent)
            {
                if (exceptionEvent.Eval != eval)
                {
                    throw new ManagedDebugger.EvalException("EvalException callback error - Eval does not match");
                }

                ICorDebugValue exceptionValue = exceptionEvent.Eval.Result ?? throw new ManagedDebugger.EvalException("EvalException callback error - Result is null");
                if (throwOnException)
                {
                    try
                    {
                        throw new ManagedDebugger.EvalException($"Evaluation threw {ManagedDebugger.GetCorDebugTypeFriendlyName(exceptionValue.ExactType)}");
                    }
                    finally
                    {
                        if (exceptionValue is ICorDebugHandleValue handle)
                        {
                            handle.TryDispose();
                        }
                    }
                }
                returnValue = exceptionValue;
            }
            return returnValue;
        }
        finally
        {
            evalStatus.ThreadId = null;
            evalStatus.Reading = default;
            // Taken at the next Continue; a process that ended meanwhile has no threads to set.
            process.TrySetAllThreadsDebugState(CorDebugThreadState.THREAD_RUN, thread);
        }

        // Ends the wait for the evaluation's event, which reads the runtime's events while the request holds the engine's
        // lock and so must not outlive it; the event, if it came at the last moment.
        async Task<CorDebugManagedCallbackEventArgs?> GiveUp(Task<CorDebugManagedCallbackEventArgs> waiting)
        {
            reading.Cancel();
            try
            {
                return await waiting;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
    }

    public static ICorDebugValue NewBooleanValue(this ICorDebugEval eval, bool value)
    {
        ICorDebugValue corValue = eval.CreateValue(CorElementType.BOOLEAN, null);

        if (value is true && corValue is ICorDebugGenericValue genValue)
        {
            int size = genValue.Size;
            byte[] valueData = new byte[size];
            valueData[0] = 1;
            unsafe
            {
                fixed (byte* p = valueData)
                {
                    nint ptr = (IntPtr)p;
                    genValue.SetValue(ptr);
                }
            }
        }

        return corValue;
    }

    public static string ToDisplayName(this CorDebugInternalFrameType frameType)
    {
        return frameType switch
        {
            CorDebugInternalFrameType.STUBFRAME_M2U => "[Managed to Native Transition]",
            CorDebugInternalFrameType.STUBFRAME_U2M => "[Native to Managed Transition]",
            CorDebugInternalFrameType.STUBFRAME_APPDOMAIN_TRANSITION => "[Appdomain Transition]",
            CorDebugInternalFrameType.STUBFRAME_LIGHTWEIGHT_FUNCTION => "[Lightweight function]",
            CorDebugInternalFrameType.STUBFRAME_FUNC_EVAL => "[Func Eval]",
            CorDebugInternalFrameType.STUBFRAME_INTERNALCALL => "[Internal Call]",
            CorDebugInternalFrameType.STUBFRAME_CLASS_INIT => "[Class Init]",
            CorDebugInternalFrameType.STUBFRAME_EXCEPTION => "[Exception]",
            CorDebugInternalFrameType.STUBFRAME_SECURITY => "[Security]",
            CorDebugInternalFrameType.STUBFRAME_JIT_COMPILATION => "[JIT Compilation]",
            _ => "[Unknown]"
        };
    }
}
