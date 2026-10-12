using Passenger.Debugger.ExpressionEvaluator.Compiler;
using Passenger.Debugger.Interop;

namespace Passenger.Debugger.ExpressionEvaluator.Cil;

// 🤖
internal sealed class CilExpressionEvaluator(RuntimeAssemblyPrimitiveTypeClasses primitiveTypes, ManagedDebugger debugger)
{
    private readonly CilExpressionCompiler _compiler = new(debugger);
    private readonly CilInterpreter _interpreter = new(debugger, primitiveTypes);

    internal async Task<EvaluationResult> Evaluate(string expression, CompiledExpressionEvaluationContext context)
    {
        try
        {
            if (expression == "$exception" && debugger.GetCurrentException(context.ThreadId) is { } currentException)
            {
                if (currentException is not ICorDebugReferenceValue { IsNull: false } reference)
                {
                    return new EvaluationResult { Value = currentException };
                }
                using EvaluationHandleScope handles = new EvaluationHandleScope();
                ICorDebugHandleValue handle = handles.CreateOwnedHandle(reference);
                return new EvaluationResult { Value = handle, OwnedResultHandle = handles.DetachIfOwned(handle) };
            }
            CompiledEvaluationMethod? compiled = _compiler.TryCompile(expression, context, out string? error);
            if (compiled is null)
            {
                return new EvaluationResult { Error = $"error: {error}" };
            }

            Lazy<DelegateMaterializerAssembly> delegateMaterializer = new Lazy<DelegateMaterializerAssembly>(() => _compiler.GetDelegateMaterializer(context));
            CilInterpretationResult interpretationResult = await _interpreter.InterpretAsync(compiled, context, delegateMaterializer);
            return new EvaluationResult { Value = interpretationResult.Value, OwnedResultHandle = interpretationResult.OwnedResultHandle };
        }
        catch (Exception ex)
        {
            return new EvaluationResult { Error = $"error: {ex.Message}" };
        }
    }
}

internal sealed class EvaluationResult : IDisposable
{
    private ICorDebugHandleValue? _ownedResultHandle;

    public ICorDebugValue? Value { get; init; }
    public string? Error { get; init; }
    public ICorDebugHandleValue? OwnedResultHandle
    {
        init => _ownedResultHandle = value;
    }

    public void RelinquishResultHandleOwnership()
    {
        _ownedResultHandle = null;
    }

    public void Dispose()
    {
        _ownedResultHandle?.TryDispose();
        _ownedResultHandle = null;
    }
}
