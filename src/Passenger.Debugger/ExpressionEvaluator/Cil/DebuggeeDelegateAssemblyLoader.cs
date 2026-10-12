using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Passenger.Debugger.ExpressionEvaluator.Compiler;
using Passenger.Debugger.Interop;

namespace Passenger.Debugger.ExpressionEvaluator.Cil;

/// <summary>
/// 🤖, Loads generated delegate code into a collectible assembly load context in the debuggee.
/// </summary>
internal sealed class DebuggeeDelegateAssemblyLoader(
    ManagedDebugger debugger,
    CompiledEvaluationMethod compiled,
    EvaluationMetadataResolver resolver,
    CompiledExpressionEvaluationContext context,
    EvaluationHandleScope handles,
    Lazy<DelegateMaterializerAssembly> materializer) : IAsyncDisposable
{
    private int? _sessionId;

    public DelegateMaterializerAssembly Materializer => materializer.Value;

    public async Task<ICorDebugFunction> GetMaterializerFunctionAsync(string methodName)
    {
        ModuleInfo module = await EnsureMaterializerLoadedAsync();
        MetadataReader reader = module.MetadataReader.PeMetadataReader;
        TypeDefinitionHandle type = reader.TypeDefinitions.First(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == Materializer.TypeName);
        MethodDefinitionHandle method = reader.GetTypeDefinition(type).GetMethods().First(handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == methodName);
        return module.Module.GetFunctionFromToken(MetadataTokens.GetToken(method));
    }

    public async Task<int> GetSessionIdAsync()
    {
        if (_sessionId is { } existing)
        {
            return existing;
        }

        ICorDebugFunction begin = await GetMaterializerFunctionAsync("Begin");
        debugger.RegisterTransientEvaluationModule(compiled.ModuleVersionId);
        ICorDebugValue assembly = await CreateByteArrayAsync(compiled.Assembly);
        ICorDebugValue contextModule = await CreateStringAsync(resolver.CurrentFrameModuleVersionId.ToString("D"));
        ICorDebugValue result = handles.Track(await context.Thread.CreateEval().CallParameterizedFunctionAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            begin,
            0,
            null,
            2,
            [assembly, contextModule],
            throwOnException: true)) ?? throw new InvalidOperationException("Failed to create the delegate assembly load session");
        int sessionId = CilValue.FromCorValue(result).AsInt32();
        _sessionId = sessionId;
        return sessionId;
    }

    public async ValueTask DisposeAsync()
    {
        if (_sessionId is not { } sessionId)
        {
            return;
        }

        _sessionId = null;
        ICorDebugFunction end = await GetMaterializerFunctionAsync("End");
        ICorDebugValue sessionIdArgument = CreateInt32(sessionId);
        await context.Thread.CreateEval().CallParameterizedFunctionAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            end,
            0,
            null,
            1,
            [sessionIdArgument],
            throwOnException: true);
    }

    private async Task<ModuleInfo> EnsureMaterializerLoadedAsync()
    {
        if (FindModule(Materializer.ModuleVersionId) is { } loaded)
        {
            return loaded;
        }

        ICorDebugValue assembly = await CreateByteArrayAsync(Materializer.Assembly);
        ResolvedRuntimeMethod load = resolver.ResolveRuntimeMethod("System.Reflection", "Assembly", "Load", "Byte[]");
        handles.Track(await context.Thread.CreateEval().CallParameterizedFunctionAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            load.Function,
            0,
            null,
            1,
            [assembly],
            throwOnException: true));
        return FindModule(Materializer.ModuleVersionId)
            ?? throw new InvalidOperationException($"Loaded delegate materializer module '{Materializer.ModuleVersionId}' was not reported by the runtime");
    }

    private async Task<ICorDebugValue> CreateByteArrayAsync(byte[] bytes)
    {
        ICorDebugType byteType = resolver.GetCorDebugType(new ResolvedCilType(PrimitiveTypeCode.Byte, null));
        ICorDebugValue arrayReference = handles.Track(await context.Thread.CreateEval().NewParameterizedArrayAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            byteType,
            checked((uint)bytes.Length),
            throwOnException: true)) ?? throw new InvalidOperationException("Failed to allocate generated assembly buffer");
        ICorDebugArrayValue array = arrayReference.UnwrapDebugValue() as ICorDebugArrayValue
            ?? throw new InvalidOperationException("Generated assembly buffer is not an array");
        for (int index = 0; index < bytes.Length; index++)
        {
            ICorDebugGenericValue element = array.GetElementAtPosition(index) as ICorDebugGenericValue
                ?? throw new InvalidOperationException("Generated assembly buffer element is unavailable");
            SetByte(element, bytes[index]);
        }
        return arrayReference;
    }

    private async Task<ICorDebugValue> CreateStringAsync(string value) =>
        handles.Track(await context.Thread.CreateEval().NewStringAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            value,
            throwOnException: true))!;

    private ICorDebugValue CreateInt32(int value)
    {
        ICorDebugValue result = context.Thread.CreateEval().CreateValue(CorElementType.I4, null);
        new CorDebugLocation(result).Write(CilValue.FromPrimitive(value));
        return result;
    }

    private ModuleInfo? FindModule(Guid moduleVersionId) =>
        debugger.AllModules.FirstOrDefault(module => module.MetadataReader.Mvid == moduleVersionId);

    private static unsafe void SetByte(ICorDebugGenericValue destination, byte value) => destination.SetValue((nint)(&value));
}
