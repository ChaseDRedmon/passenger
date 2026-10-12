using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using Passenger.Debugger.ExpressionEvaluator.Compiler;
using Passenger.Debugger.Interop;

namespace Passenger.Debugger.ExpressionEvaluator.Cil;

internal readonly record struct CilInterpretationResult(ICorDebugValue Value, ICorDebugHandleValue? OwnedResultHandle);

// 🤖
internal sealed class CilInterpreter(ManagedDebugger debugger, RuntimeAssemblyPrimitiveTypeClasses primitiveTypes)
{
    public async Task<CilInterpretationResult> InterpretAsync(CompiledEvaluationMethod compiled, CompiledExpressionEvaluationContext context, Lazy<DelegateMaterializerAssembly> delegateMaterializer)
    {
        using EvaluationHandleScope handles = new EvaluationHandleScope();
        MethodDefinition method = compiled.MetadataReader.GetMethodDefinition(compiled.EntryMethod);
        MethodBodyBlock body = compiled.PeReader.GetMethodBody(method.RelativeVirtualAddress);
        DecodedMethod decoded = compiled.GetDecodedMethod(compiled.EntryMethod);
        ICorDebugILFrame? frame = context.RootValue is null ? debugger.GetIlFrameForThreadIdAndStackDepth(context.ThreadId, context.StackDepth) : null;
        Func<ICorDebugILFrame> refind = () => debugger.GetIlFrameForThreadIdAndStackDepth(context.ThreadId, context.StackDepth);
        ICilLocation[] arguments = CreateArguments(frame, context, refind);
        ICilLocation[] locals = CreateLocals(compiled, frame, body.LocalSignature, context.RootValue is not null, refind);
        EvaluationMetadataResolver resolver = CreateResolver(compiled, context, frame);
        await using DebuggeeDelegateAssemblyLoader delegateAssemblyLoader = new DebuggeeDelegateAssemblyLoader(debugger, compiled, resolver, context, handles, delegateMaterializer);
        Dictionary<string, ICilLocation> syntheticVariables = new Dictionary<string, ICilLocation>(StringComparer.Ordinal);
        HashSet<EvaluationClosure> evaluationClosures = new HashSet<EvaluationClosure>(ReferenceEqualityComparer.Instance);
        CilValue result = await InterpretAsync(compiled, decoded, arguments, locals, resolver, context, handles, syntheticVariables, new Dictionary<FieldDefinitionHandle, ICilLocation>(), evaluationClosures, delegateAssemblyLoader);
        ICorDebugValue value = result.Value is EvaluationDelegate
            ? await MaterializeDelegateAsync(result, compiled, resolver, delegateAssemblyLoader, context, handles)
            : await MaterializeAsync(result, context, handles, resolver.ResolveMethodReturnType(compiled.EntryMethod), resolver);
        return new CilInterpretationResult(value, handles.DetachIfOwned(value));
    }

    private EvaluationMetadataResolver CreateResolver(CompiledEvaluationMethod compiled, CompiledExpressionEvaluationContext context, ICorDebugILFrame? frame)
    {
        (ICorDebugType[]? typeGenericArguments, ICorDebugType[]? methodGenericArguments) = context.RootValue is not null
            ? (context.RootValue.ExactType.TypeParameters, [])
            : SplitFrameTypeParameters(debugger, frame);
        ICorDebugModule preferredModule = context.RootValue is not null ? context.RootValue.ExactType.Class.Module : frame!.Function.Module;
        ModuleInfo currentFrameModule = debugger.GetModuleInfoForModule(preferredModule);
        return new EvaluationMetadataResolver(
            debugger,
            compiled.MetadataReader,
            compiled.PeReader,
            context.Thread.AppDomain,
            typeGenericArguments,
            methodGenericArguments,
            currentFrameModule);
    }

    private static ICilLocation[] CreateArguments(ICorDebugILFrame? frame, CompiledExpressionEvaluationContext context, Func<ICorDebugILFrame> refind)
    {
        if (context.RootValue is not null)
        {
            return [new CorDebugLocation(context.RootValue)];
        }

        ICorDebugILFrame ilFrame = frame ?? throw new InvalidOperationException("Arguments need a frame");
        return [.. Enumerable.Range(0, ilFrame.Arguments.Length).Select(i => (ICilLocation)new FrameSlotLocation(ilFrame, i, argument: true, refind))];
    }

    private static ICilLocation[] CreateLocals(CompiledEvaluationMethod compiled, ICorDebugILFrame? frame, StandaloneSignatureHandle localSignature, bool isTypeContext, Func<ICorDebugILFrame> refind)
    {
        int localCount = localSignature.IsNil
            ? 0
            : compiled.MetadataReader.GetStandaloneSignature(localSignature)
                .DecodeLocalSignature(LocalCountSignatureProvider.Instance, genericContext: null).Length;
        ICorDebugValue[]? frameLocals = frame?.LocalVariables;
        ICilLocation[] result = new ICilLocation[localCount];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = !isTypeContext && i < frameLocals!.Length
                ? new FrameSlotLocation(frame!, i, argument: false, refind)
                : new TemporaryLocation(CilValue.Null());
        }
        return result;
    }

    private static ICilLocation[] CreateTemporaryLocals(EvaluationMetadataResolver resolver, StandaloneSignatureHandle localSignature)
    {
        int count = resolver.GetEvaluationLocalCount(localSignature);
        ICilLocation[] result = new ICilLocation[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new TemporaryLocation(CilValue.Null());
        }

        return result;
    }

    private static (ICorDebugType[] TypeArguments, ICorDebugType[] MethodArguments) SplitFrameTypeParameters(ManagedDebugger debugger, ICorDebugILFrame? frame)
    {
        if (frame is null)
        {
            return ([], []);
        }

        ICorDebugType[] typeParameters;
        try
        {
            typeParameters = frame.TypeParameters;
        }
        catch
        {
            return ([], []);
        }
        int declaringTypeArity = GetDeclaringTypeArity(debugger, frame);
        return (typeParameters.Take(declaringTypeArity).ToArray(), typeParameters.Skip(declaringTypeArity).ToArray());
    }

    private static int GetDeclaringTypeArity(ManagedDebugger debugger, ICorDebugILFrame frame)
    {
        try
        {
            mdTypeDef declaringTypeToken = frame.Function.Class.Token;
            ModuleInfo moduleInfo = debugger.GetModuleInfoForModule(frame.Function.Module);
            return moduleInfo.MetadataReader.PeMetadataReader
                .GetTypeDefinition((TypeDefinitionHandle)MetadataTokens.Handle(declaringTypeToken))
                .GetGenericParameters()
                .Count();
        }
        catch
        {
            return 0;
        }
    }

    private async Task<CilValue> InterpretAsync(
        CompiledEvaluationMethod compiled,
        DecodedMethod decoded,
        ICilLocation[] arguments,
        ICilLocation[] locals,
        EvaluationMetadataResolver resolver,
        CompiledExpressionEvaluationContext context,
        EvaluationHandleScope handles,
        Dictionary<string, ICilLocation> syntheticVariables,
        Dictionary<FieldDefinitionHandle, ICilLocation> evaluationStaticFields,
        HashSet<EvaluationClosure> evaluationClosures,
        DebuggeeDelegateAssemblyLoader delegateAssemblyLoader)
    {
        IReadOnlyList<CilInstruction> instructions = decoded.Instructions;
        IReadOnlyDictionary<int, int> offsets = decoded.Offsets;
        Stack<CilValue> stack = new Stack<CilValue>();
        int index = 0;
        ResolvedCilType? constrainedType = null;
        while (index < instructions.Count)
        {
            CilInstruction instruction = instructions[index++];
            try
            {
                OpCode op = instruction.OpCode;

                if (op == OpCodes.Nop || op == OpCodes.Break)
                {
                    continue;
                }

                if (op == OpCodes.Constrained)
                {
                    constrainedType = resolver.ResolveTypeToken((int)instruction.Operand!);
                    continue;
                }
                if (op == OpCodes.Ldnull) { stack.Push(CilValue.Null()); continue; }
                if (op == OpCodes.Ldstr) { stack.Push(CilValue.FromPrimitive(resolver.ResolveUserString((int)instruction.Operand!))); continue; }
                if (op == OpCodes.Ldtoken) { stack.Push(CilValue.FromPrimitive(resolver.ResolveTypeToken((int)instruction.Operand!))); continue; }
                if (op == OpCodes.Ldc_I4_M1) { stack.Push(CilValue.FromPrimitive(-1)); continue; }
                if (op == OpCodes.Ldc_I4_0) { stack.Push(CilValue.FromPrimitive(0)); continue; }
                if (op == OpCodes.Ldc_I4_1) { stack.Push(CilValue.FromPrimitive(1)); continue; }
                if (op == OpCodes.Ldc_I4_2) { stack.Push(CilValue.FromPrimitive(2)); continue; }
                if (op == OpCodes.Ldc_I4_3) { stack.Push(CilValue.FromPrimitive(3)); continue; }
                if (op == OpCodes.Ldc_I4_4) { stack.Push(CilValue.FromPrimitive(4)); continue; }
                if (op == OpCodes.Ldc_I4_5) { stack.Push(CilValue.FromPrimitive(5)); continue; }
                if (op == OpCodes.Ldc_I4_6) { stack.Push(CilValue.FromPrimitive(6)); continue; }
                if (op == OpCodes.Ldc_I4_7) { stack.Push(CilValue.FromPrimitive(7)); continue; }
                if (op == OpCodes.Ldc_I4_8) { stack.Push(CilValue.FromPrimitive(8)); continue; }
                if (op == OpCodes.Ldc_I4 || op == OpCodes.Ldc_I4_S) { stack.Push(CilValue.FromPrimitive(Convert.ToInt32(instruction.Operand))); continue; }
                if (op == OpCodes.Ldc_I8) { stack.Push(CilValue.FromPrimitive((long)instruction.Operand!)); continue; }
                if (op == OpCodes.Ldc_R4) { stack.Push(CilValue.FromPrimitive((float)instruction.Operand!)); continue; }
                if (op == OpCodes.Ldc_R8) { stack.Push(CilValue.FromPrimitive((double)instruction.Operand!)); continue; }

                if (TryGetArgumentIndex(op, instruction.Operand, out int argumentIndex))
                {
                    stack.Push(handles.Root(arguments[argumentIndex].Read()).WithSourceLocation(arguments[argumentIndex]));
                    continue;
                }
                if (TryGetArgumentAddressIndex(op, instruction.Operand, out argumentIndex))
                {
                    stack.Push(CilValue.FromLocation(arguments[argumentIndex]));
                    continue;
                }
                if (TryGetStoreArgumentIndex(op, instruction.Operand, out argumentIndex))
                {
                    arguments[argumentIndex].Write(await MaterializeForStoreAsync(stack.Pop(), context, handles));
                    continue;
                }

                if (TryGetLocalIndex(op, instruction.Operand, out int localIndex))
                {
                    stack.Push(handles.Root(locals[localIndex].Read()).WithSourceLocation(locals[localIndex]));
                    continue;
                }
                if (TryGetLocalAddressIndex(op, instruction.Operand, out localIndex))
                {
                    stack.Push(CilValue.FromLocation(locals[localIndex]));
                    continue;
                }
                if (TryGetStoreLocalIndex(op, instruction.Operand, out localIndex))
                {
                    locals[localIndex].Write(await MaterializeForStoreAsync(stack.Pop(), context, handles));
                    continue;
                }

                if (op == OpCodes.Dup) { stack.Push(stack.Peek()); continue; }
                if (op == OpCodes.Pop) { stack.Pop(); continue; }
                if (op == OpCodes.Ldftn || op == OpCodes.Ldvirtftn)
                {
                    if (op == OpCodes.Ldvirtftn)
                    {
                        stack.Pop();
                    }

                    int methodToken = (int)instruction.Operand!;
                    EntityHandle methodHandle = MetadataTokens.EntityHandle(methodToken);
                    EvaluationFunctionPointer function = methodHandle.Kind == HandleKind.MethodDefinition
                        ? new EvaluationFunctionPointer(methodToken)
                        : CreateRuntimeFunctionPointer(resolver.ResolveMethod(methodToken));
                    stack.Push(CilValue.FromVirtual(function));
                    continue;
                }
                if (op == OpCodes.Neg) { stack.Push(Negate(stack.Pop())); continue; }
                if (op == OpCodes.Not)
                {
                    // An int32 stays one: only a 64-bit operand gives a 64-bit result.
                    CilValue operand = stack.Pop();
                    stack.Push(operand.Value is long or ulong ? CilValue.FromPrimitive(~operand.AsInt64()) : CilValue.FromPrimitive(~operand.AsInt32()));
                    continue;
                }

                if (IsBinary(op))
                {
                    CilValue right = stack.Pop();
                    CilValue left = stack.Pop();
                    stack.Push(EvaluateBinary(op, left, right));
                    continue;
                }

                if (op == OpCodes.Ceq || op == OpCodes.Cgt || op == OpCodes.Cgt_Un || op == OpCodes.Clt || op == OpCodes.Clt_Un)
                {
                    CilValue right = stack.Pop();
                    CilValue left = stack.Pop();
                    stack.Push(CilValue.FromPrimitive(Compare(op, left, right) ? 1 : 0));
                    continue;
                }

                if (IsConversion(op))
                {
                    stack.Push(ConvertValue(op, stack.Pop()));
                    continue;
                }

                if (op == OpCodes.Br || op == OpCodes.Br_S)
                {
                    index = GetTargetIndex(offsets, instruction);
                    continue;
                }
                if (op == OpCodes.Brtrue || op == OpCodes.Brtrue_S || op == OpCodes.Brfalse || op == OpCodes.Brfalse_S)
                {
                    bool condition = stack.Pop().IsTrue();
                    if ((op == OpCodes.Brtrue || op == OpCodes.Brtrue_S) == condition)
                    {
                        index = GetTargetIndex(offsets, instruction);
                    }

                    continue;
                }
                if (IsComparisonBranch(op))
                {
                    CilValue right = stack.Pop();
                    CilValue left = stack.Pop();
                    if (EvaluateBranch(op, left, right))
                    {
                        index = GetTargetIndex(offsets, instruction);
                    }

                    continue;
                }
                if (op == OpCodes.Switch)
                {
                    int selected = stack.Pop().AsInt32();
                    int[] targets = (int[])instruction.Operand!;
                    if ((uint)selected < (uint)targets.Length)
                    {
                        index = offsets[targets[selected]];
                    }

                    continue;
                }

                if (op == OpCodes.Ldobj || op.Name?.StartsWith("ldind.", StringComparison.Ordinal) == true) { stack.Push(handles.Root(stack.Pop().Dereference())); continue; }
                if (op == OpCodes.Stobj || op.Name?.StartsWith("stind.", StringComparison.Ordinal) == true)
                {
                    CilValue value = stack.Pop();
                    ICilLocation address = stack.Pop().Location ?? throw new InvalidOperationException("stind requires a managed location");
                    address.Write(await MaterializeForStoreAsync(value, context, handles));
                    continue;
                }
                if (op == OpCodes.Cpobj)
                {
                    CilValue source = stack.Pop().Dereference();
                    ICilLocation destination = stack.Pop().Location ?? throw new InvalidOperationException("cpobj requires a managed location");
                    destination.Write(await MaterializeForStoreAsync(source, context, handles));
                    continue;
                }
                if (op == OpCodes.Initobj)
                {
                    ResolvedCilType type = resolver.ResolveTypeToken((int)instruction.Operand!);
                    ICilLocation location = stack.Pop().Location ?? throw new InvalidOperationException("initobj requires a managed location");
                    location.Write(await CreateDefaultValueAsync(type, resolver, context, handles));
                    continue;
                }
                if (op == OpCodes.Newarr)
                {
                    uint length = checked((uint)stack.Pop().AsInt32());
                    ResolvedCilType elementCilType = resolver.ResolveTypeToken((int)instruction.Operand!);
                    ICorDebugType elementType = resolver.GetCorDebugType(elementCilType);
                    ICorDebugValue? array = await CreateArrayAsync(elementCilType, elementType, length, resolver, context, handles);
                    stack.Push(array is null ? CilValue.Null() : CilValue.FromCorValue(array));
                    continue;
                }
                if (op == OpCodes.Ldlen)
                {
                    ICorDebugArrayValue array = GetArrayValue(stack.Pop());
                    stack.Push(CilValue.FromPrimitive(array.Count));
                    continue;
                }
                if (op == OpCodes.Ldelema)
                {
                    int indexValue = stack.Pop().AsInt32();
                    ICorDebugArrayValue array = GetArrayValue(stack.Pop());
                    stack.Push(CilValue.FromLocation(new CorDebugLocation(array.GetElementAtPosition(indexValue))));
                    continue;
                }
                if (op.Name?.StartsWith("ldelem.", StringComparison.Ordinal) == true || op == OpCodes.Ldelem)
                {
                    int indexValue = stack.Pop().AsInt32();
                    ICorDebugArrayValue array = GetArrayValue(stack.Pop());
                    stack.Push(handles.Root(new CorDebugLocation(array.GetElementAtPosition(indexValue)).Read()));
                    continue;
                }
                if (op.Name?.StartsWith("stelem.", StringComparison.Ordinal) == true || op == OpCodes.Stelem)
                {
                    CilValue elementValue = stack.Pop();
                    int indexValue = stack.Pop().AsInt32();
                    ICorDebugArrayValue array = GetArrayValue(stack.Pop());
                    new CorDebugLocation(array.GetElementAtPosition(indexValue)).Write(await MaterializeForStoreAsync(elementValue, context, handles));
                    continue;
                }
                if (op == OpCodes.Isinst || op == OpCodes.Castclass)
                {
                    CilValue source = stack.Pop();
                    if (source.IsNull)
                    {
                        stack.Push(CilValue.Null());
                        continue;
                    }
                    ResolvedCilType targetType = resolver.ResolveTypeToken((int)instruction.Operand!);
                    bool isInstance = await IsInstanceOfTypeAsync(source, targetType, resolver, context, handles);
                    if (isInstance)
                    {
                        stack.Push(source);
                    }
                    else if (op == OpCodes.Isinst)
                    {
                        stack.Push(CilValue.Null());
                    }
                    else
                    {
                        throw new InvalidCastException($"InvalidCastException: Cannot cast the debuggee value to '{targetType.RuntimeType?.ToString() ?? "the requested type"}'");
                    }

                    continue;
                }
                if (op == OpCodes.Box)
                {
                    ResolvedCilType targetType = resolver.ResolveTypeToken((int)instruction.Operand!);
                    stack.Push(await BoxAsync(stack.Pop(), resolver.GetCorDebugType(targetType), context, handles));
                    continue;
                }
                if (op == OpCodes.Unbox_Any)
                {
                    CilValue source = stack.Pop();
                    if (source.Location is not null)
                    {
                        source = source.Dereference();
                    }

                    if (source.IsNull)
                    {
                        throw new NullReferenceException();
                    }

                    ICorDebugBoxValue? boxed = source.CorValue is ICorDebugReferenceValue reference
                        ? reference.Dereference() as ICorDebugBoxValue
                        : source.CorValue as ICorDebugBoxValue;
                    if (boxed is null)
                    {
                        throw new InvalidCastException("The CIL value is not boxed");
                    }

                    ResolvedCilType targetType = resolver.ResolveTypeToken((int)instruction.Operand!);
                    if (!IsUnboxCompatible(boxed.Object, targetType, resolver))
                    {
                        throw new InvalidCastException($"InvalidCastException: Cannot unbox the debuggee value to '{targetType.RuntimeType?.ToString() ?? "the requested type"}'");
                    }
                    stack.Push(CilValue.FromCorValue(boxed.Object));
                    continue;
                }
                if (op == OpCodes.Unbox)
                {
                    CilValue source = stack.Pop();
                    if (source.Location is not null)
                    {
                        source = source.Dereference();
                    }

                    if (source.IsNull)
                    {
                        throw new NullReferenceException();
                    }

                    ICorDebugBoxValue? boxed = source.CorValue is ICorDebugReferenceValue reference
                        ? reference.Dereference() as ICorDebugBoxValue
                        : source.CorValue as ICorDebugBoxValue;
                    if (boxed is null)
                    {
                        throw new InvalidCastException("The CIL value is not boxed");
                    }

                    // The address of a box's value, which must be the type asked for, as unbox.any's is.
                    ResolvedCilType unboxTarget = resolver.ResolveTypeToken((int)instruction.Operand!);
                    if (!IsUnboxCompatible(boxed.Object, unboxTarget, resolver))
                    {
                        throw new InvalidCastException($"InvalidCastException: Cannot unbox the debuggee value to '{unboxTarget.RuntimeType?.ToString() ?? "the requested type"}'");
                    }
                    stack.Push(CilValue.FromLocation(new CorDebugLocation(boxed.Object)));
                    continue;
                }

                if (op == OpCodes.Ldfld)
                {
                    if (MetadataTokens.EntityHandle((int)instruction.Operand!) is { Kind: HandleKind.FieldDefinition } evaluationField)
                    {
                        EvaluationClosure receiver = GetEvaluationClosure(stack.Pop());
                        ICilLocation evaluationFieldLocation = receiver.Fields[(FieldDefinitionHandle)evaluationField];
                        stack.Push(handles.Root(evaluationFieldLocation.Read()).WithSourceLocation(evaluationFieldLocation));
                        continue;
                    }
                    ResolvedRuntimeField field = resolver.ResolveField((int)instruction.Operand!);
                    ICorDebugObjectValue objectValue = GetFieldReceiver(stack.Pop());
                    stack.Push(handles.Root(CilValue.FromCorValue(objectValue.GetFieldValue(field.DeclaringType.Class, field.Token))));
                    continue;
                }
                if (op == OpCodes.Ldflda)
                {
                    if (MetadataTokens.EntityHandle((int)instruction.Operand!) is { Kind: HandleKind.FieldDefinition } evaluationField)
                    {
                        EvaluationClosure receiver = GetEvaluationClosure(stack.Pop());
                        stack.Push(CilValue.FromLocation(receiver.Fields[(FieldDefinitionHandle)evaluationField]));
                        continue;
                    }
                    ResolvedRuntimeField field = resolver.ResolveField((int)instruction.Operand!);
                    ICorDebugObjectValue objectValue = GetFieldReceiver(stack.Pop());
                    stack.Push(CilValue.FromLocation(new CorDebugLocation(objectValue.GetFieldValue(field.DeclaringType.Class, field.Token))));
                    continue;
                }
                if (op == OpCodes.Stfld)
                {
                    CilValue value = stack.Pop();
                    if (MetadataTokens.EntityHandle((int)instruction.Operand!) is { Kind: HandleKind.FieldDefinition } evaluationField)
                    {
                        EvaluationClosure receiver = GetEvaluationClosure(stack.Pop());
                        FieldDefinitionHandle fieldHandle = (FieldDefinitionHandle)evaluationField;
                        ICilLocation evaluationFieldLocation = receiver.Fields[fieldHandle];
                        if (evaluationFieldLocation is TemporaryLocation && value.SourceLocation is { } sourceLocation)
                        {
                            receiver.FieldBindings[fieldHandle] = sourceLocation;
                        }
                        evaluationFieldLocation.Write(value);
                        continue;
                    }
                    ResolvedRuntimeField field = resolver.ResolveField((int)instruction.Operand!);
                    ICorDebugObjectValue objectValue = GetFieldReceiver(stack.Pop());
                    new CorDebugLocation(objectValue.GetFieldValue(field.DeclaringType.Class, field.Token)).Write(await MaterializeForStoreAsync(value, context, handles));
                    continue;
                }
                if (op == OpCodes.Ldsfld)
                {
                    if (MetadataTokens.EntityHandle((int)instruction.Operand!) is { Kind: HandleKind.FieldDefinition } evaluationField)
                    {
                        stack.Push(GetEvaluationStaticField(evaluationStaticFields, (FieldDefinitionHandle)evaluationField).Read());
                        continue;
                    }
                    ResolvedRuntimeField field = resolver.ResolveField((int)instruction.Operand!);
                    ICorDebugValue value = await GetStaticFieldValue(field, resolver, context);
                    stack.Push(handles.Root(CilValue.FromCorValue(value)));
                    continue;
                }
                if (op == OpCodes.Ldsflda)
                {
                    if (MetadataTokens.EntityHandle((int)instruction.Operand!) is { Kind: HandleKind.FieldDefinition } evaluationField)
                    {
                        stack.Push(CilValue.FromLocation(GetEvaluationStaticField(evaluationStaticFields, (FieldDefinitionHandle)evaluationField)));
                        continue;
                    }
                    ResolvedRuntimeField field = resolver.ResolveField((int)instruction.Operand!);
                    stack.Push(CilValue.FromLocation(new CorDebugLocation(await GetStaticFieldValue(field, resolver, context))));
                    continue;
                }
                if (op == OpCodes.Stsfld)
                {
                    if (MetadataTokens.EntityHandle((int)instruction.Operand!) is { Kind: HandleKind.FieldDefinition } evaluationField)
                    {
                        GetEvaluationStaticField(evaluationStaticFields, (FieldDefinitionHandle)evaluationField).Write(stack.Pop());
                        continue;
                    }
                    ResolvedRuntimeField field = resolver.ResolveField((int)instruction.Operand!);
                    new CorDebugLocation(await GetStaticFieldValue(field, resolver, context)).Write(await MaterializeForStoreAsync(stack.Pop(), context, handles));
                    continue;
                }

                if (op == OpCodes.Newobj)
                {
                    if (MetadataTokens.EntityHandle((int)instruction.Operand!) is { Kind: HandleKind.MethodDefinition } evaluationConstructor)
                    {
                        MethodDefinitionHandle constructorHandle = (MethodDefinitionHandle)evaluationConstructor;
                        MethodSignature<string> signature = resolver.ResolveEvaluationMethodSignature(constructorHandle);
                        if (signature.ParameterTypes.Length != 0)
                        {
                            throw new NotSupportedException("Generated evaluation constructors with parameters are not supported");
                        }

                        EvaluationClosure evaluationClosure = new EvaluationClosure(resolver.GetEvaluationMethodDeclaringType(constructorHandle), constructorHandle);
                        foreach (FieldDefinitionHandle field in resolver.GetEvaluationInstanceFields(evaluationClosure.Type))
                        {
                            evaluationClosure.Fields[field] = new TemporaryLocation(CilValue.Null());
                        }
                        evaluationClosures.Add(evaluationClosure);
                        stack.Push(CilValue.FromVirtual(evaluationClosure));
                        continue;
                    }
                    ResolvedRuntimeMethod constructor = resolver.ResolveMethod((int)instruction.Operand!);
                    CilValue[] constructorArguments = new CilValue[constructor.Signature.ParameterTypes.Length];
                    for (int argument = constructorArguments.Length - 1; argument >= 0; argument--)
                    {
                        constructorArguments[argument] = stack.Pop();
                    }

                    if (constructorArguments is [var delegateTarget, { Value: EvaluationFunctionPointer function }])
                    {
                        stack.Push(CilValue.FromVirtual(new EvaluationDelegate(function, delegateTarget, constructor.DeclaringType)));
                        continue;
                    }
                    ICorDebugValue[] argumentValues = new ICorDebugValue[constructorArguments.Length];
                    List<(ICilLocation Location, ICorDebugValue Value)> temporaryByRefArguments = new List<(ICilLocation Location, ICorDebugValue Value)>();
                    for (int argument = 0; argument < argumentValues.Length; argument++)
                    {
                        argumentValues[argument] = constructor.Signature.ParameterTypes[argument].EndsWith('&')
                            ? await MaterializeByRefArgumentAsync(constructorArguments[argument], context, handles, temporaryByRefArguments)
                            : await MaterializeForCallAsync(constructorArguments[argument], context, handles);
                    }
                    ICorDebugType[] typeArguments = constructor.DeclaringType.TypeArguments.IsDefaultOrEmpty
                        ? []
                        : constructor.DeclaringType.TypeArguments.Select(resolver.GetCorDebugType).ToArray();
                    ICorDebugEval eval = context.Thread.CreateEval();
                    ICorDebugValue? newValue;
                    try
                    {
                        newValue = handles.Track(await eval.NewParameterizedObjectAsync(
                            debugger.ProcessRuntimeEventsUntilEvalEvent,
                            debugger.EvalStatus,
                            constructor.Function,
                            typeArguments.Length,
                            typeArguments.Length == 0 ? null : typeArguments,
                            argumentValues.Length,
                            argumentValues,
                            throwOnException: true));
                    }
                    finally
                    {
                        WriteBackTemporaryByRefArguments(temporaryByRefArguments, handles);
                    }
                    stack.Push(newValue is null ? CilValue.Null() : CilValue.FromCorValue(newValue));
                    continue;
                }

                if (op == OpCodes.Call || op == OpCodes.Callvirt)
                {
                    ResolvedCilType? callConstrainedType = constrainedType;
                    constrainedType = null;
                    if (resolver.TryResolveDebuggerIntrinsic((int)instruction.Operand!, out string? debuggerIntrinsic))
                    {
                        if (debuggerIntrinsic == "CreateVariable")
                        {
                            stack.Pop(); // Custom type payload.
                            stack.Pop(); // Custom type payload ID.
                            string name = stack.Pop().Value as string ?? throw new InvalidOperationException("Synthetic variable name is unavailable");
                            ResolvedCilType type = stack.Pop().Value as ResolvedCilType ?? throw new InvalidOperationException("Synthetic variable type is unavailable");
                            syntheticVariables[name] = await CreateSyntheticVariableLocationAsync(type, resolver, context, handles);
                        }
                        else if (debuggerIntrinsic == "GetVariableAddress")
                        {
                            string name = stack.Pop().Value as string ?? throw new InvalidOperationException("Synthetic variable name is unavailable");
                            if (!syntheticVariables.TryGetValue(name, out ICilLocation? location))
                            {
                                throw new InvalidOperationException($"Synthetic variable '{name}' is unavailable");
                            }

                            stack.Push(CilValue.FromLocation(location));
                        }
                        else if (debuggerIntrinsic == "GetObjectByAlias")
                        {
                            string name = stack.Pop().Value as string ?? throw new InvalidOperationException("Synthetic variable name is unavailable");
                            if (!syntheticVariables.TryGetValue(name, out ICilLocation? location))
                            {
                                throw new InvalidOperationException($"Synthetic variable '{name}' is unavailable");
                            }

                            stack.Push(handles.Root(location.Read()));
                        }
                        else if (debuggerIntrinsic == "GetException")
                        {
                            ICorDebugValue exception = debugger.GetCurrentException(context.ThreadId)
                                            ?? throw new InvalidOperationException("No current exception is available");
                            stack.Push(handles.Root(CilValue.FromCorValue(exception)));
                        }
                        else
                        {
                            throw new NotSupportedException($"Debugger intrinsic '{debuggerIntrinsic}' is not supported");
                        }
                        continue;
                    }
                    if (resolver.TryResolveEvaluationMethod((int)instruction.Operand!, out ResolvedEvaluationMethod evaluationMethod))
                    {
                        CilValue[] methodArguments = new CilValue[evaluationMethod.Signature.ParameterTypes.Length + (evaluationMethod.IsStatic ? 0 : 1)];
                        for (int argument = methodArguments.Length - 1; argument >= 0; argument--)
                        {
                            methodArguments[argument] = stack.Pop();
                        }

                        CilValue methodResult = await InterpretAsync(
                            compiled,
                            compiled.GetDecodedMethod(evaluationMethod.Handle),
                            methodArguments.Select(value => (ICilLocation)new TemporaryLocation(value)).ToArray(),
                            CreateTemporaryLocals(resolver, resolver.GetEvaluationMethodBody(evaluationMethod.Handle).LocalSignature),
                            resolver,
                            context,
                            handles,
                            syntheticVariables,
                            evaluationStaticFields,
                            evaluationClosures,
                            delegateAssemblyLoader);
                        if (evaluationMethod.Signature.ReturnType != PrimitiveTypeCode.Void.ToString())
                        {
                            stack.Push(methodResult);
                        }

                        continue;
                    }
                    ResolvedRuntimeMethod method = resolver.ResolveMethod((int)instruction.Operand!);
                    CilValue[] argumentValues = new CilValue[method.Signature.ParameterTypes.Length];
                    for (int argument = argumentValues.Length - 1; argument >= 0; argument--)
                    {
                        argumentValues[argument] = stack.Pop();
                    }

                    CilValue? receiverValue = null;
                    if (!method.IsStatic)
                    {
                        receiverValue = stack.Pop();
                    }

                    if (resolver.GetRuntimeTypeName(method.DeclaringType) == "System.Type" && method.Name == "GetTypeFromHandle")
                    {
                        ResolvedCilType tokenType = argumentValues[0].Value as ResolvedCilType
                                        ?? throw new InvalidOperationException("GetTypeFromHandle requires a type token");
                        ICorDebugValue? typeValue = await GetSystemTypeAsync(tokenType, resolver, context, handles);
                        stack.Push(typeValue is null ? CilValue.Null() : CilValue.FromTypeToken(tokenType, typeValue));
                        continue;
                    }
                    (bool Handled, CilValue? Result) intrinsic = await TryExecuteIntrinsicCallAsync(method, receiverValue, argumentValues, resolver, context, handles);
                    if (intrinsic.Handled)
                    {
                        if (intrinsic.Result is not null)
                        {
                            stack.Push(intrinsic.Result);
                        }

                        continue;
                    }
                    if (receiverValue?.Value is StringBuilder || receiverValue?.Location?.Read().Value is StringBuilder || argumentValues.Any(a => a.Value is StringBuilder))
                    {
                        throw new InvalidOperationException($"Unhandled interpolated-string call '{resolver.GetRuntimeTypeName(method.DeclaringType)}.{method.Name}'");
                    }

                    ICorDebugValue[] callArguments = new ICorDebugValue[method.Signature.ParameterTypes.Length + (method.IsStatic ? 0 : 1)];
                    List<(ICilLocation Location, ICorDebugValue Value)> temporaryByRefArguments = new List<(ICilLocation Location, ICorDebugValue Value)>();
                    (ICilLocation Home, ICorDebugValue Box)? constrainedCopyBack = null;
                    for (int argument = 0; argument < argumentValues.Length; argument++)
                    {
                        callArguments[argument + (method.IsStatic ? 0 : 1)] = method.Signature.ParameterTypes[argument].EndsWith('&')
                            ? await MaterializeByRefArgumentAsync(argumentValues[argument], context, handles, temporaryByRefArguments)
                            : await MaterializeForCallAsync(argumentValues[argument], compiled, resolver, delegateAssemblyLoader, context, handles);
                    }
                    if (!method.IsStatic)
                    {
                        CilValue receiver = receiverValue!;
                        if (receiver.Location is not null)
                        {
                            receiver = receiver.Dereference();
                        }

                        if (receiver.IsNull)
                        {
                            throw new NullReferenceException();
                        }

                        callArguments[0] = receiver.Value is EvaluationDelegate or EvaluationClosure
                            ? await MaterializeForCallAsync(receiver, compiled, resolver, delegateAssemblyLoader, context, handles)
                            : await MaterializeReceiverAsync(receiver, context, callConstrainedType, resolver, handles);
                        // A constrained call on a struct runs on a boxed copy; what it changes goes back where
                        // the struct lives, as the call on the struct's own address would have (upstream #30).
                        if (callConstrainedType is not null && receiverValue!.Location is { } receiverHome
                            && callArguments[0] != receiver.CorValue && callArguments[0].UnwrapDebugValue() is ICorDebugGenericValue)
                        {
                            constrainedCopyBack = (receiverHome, callArguments[0]);
                        }
                    }

                    int declaringTypeArity = resolver.GetRuntimeTypeGenericArity(method.DeclaringType);
                    ICorDebugType[] declaringTypeArguments = !method.IsStatic && declaringTypeArity > 0 && callArguments[0].ExactType is { } receiverType
                        ? receiverType.TypeParameters.Take(declaringTypeArity).ToArray()
                        : method.DeclaringType.TypeArguments.IsDefaultOrEmpty
                            ? []
                            : method.DeclaringType.TypeArguments.Select(resolver.GetCorDebugType).ToArray();
                    ICorDebugType[] methodTypeArguments = method.MethodTypeArguments.IsDefaultOrEmpty
                        ? []
                        : method.MethodTypeArguments.Select(resolver.GetCorDebugType).ToArray();
                    ICorDebugType[] typeArguments = [.. declaringTypeArguments, .. methodTypeArguments];
                    ICorDebugEval eval = context.Thread.CreateEval();
                    ICorDebugValue? callResult;
                    try
                    {
                        callResult = handles.Track(await eval.CallParameterizedFunctionAsync(
                            debugger.ProcessRuntimeEventsUntilEvalEvent,
                            debugger.EvalStatus,
                            method.Function,
                            typeArguments.Length,
                            typeArguments.Length == 0 ? null : typeArguments,
                            callArguments.Length,
                            callArguments,
                            throwOnException: true));
                    }
                    finally
                    {
                        WriteBackTemporaryByRefArguments(temporaryByRefArguments, handles);
                        SynchronizeEvaluationClosures(evaluationClosures.Select(CilValue.FromVirtual), handles);
                        if (constrainedCopyBack is { } copy)
                        {
                            copy.Home.Write(CilValue.FromDebuggeeValue(copy.Box.UnwrapDebugValue()));
                        }
                    }
                    if (method.Signature.ReturnType != PrimitiveTypeCode.Void.ToString())
                    {
                        if (callResult is null)
                        {
                            stack.Push(CilValue.Null());
                        }
                        else if (method.Signature.ReturnType.EndsWith('&'))
                        {
                            stack.Push(CilValue.FromLocation(new CorDebugLocation(callResult)));
                        }
                        else
                        {
                            stack.Push(CilValue.FromCorValue(callResult));
                        }
                    }
                    continue;
                }

                if (op == OpCodes.Ret)
                {
                    return stack.Count == 0 ? CilValue.Null() : stack.Pop();
                }

                throw new NotSupportedException($"CIL opcode '{op.Name}' at IL_{instruction.Offset:X4} is not supported yet");
            }
            catch (Exception ex) when (ex is not NotSupportedException and not ManagedDebugger.EvalException)
            {
                throw new InvalidOperationException($"CIL execution failed at IL_{instruction.Offset:X4} ({instruction.OpCode.Name}): {ex.GetType().Name}: {ex.Message}", ex);
            }
        }

        throw new InvalidOperationException("Generated evaluation method ended without ret");
    }

    private static EvaluationFunctionPointer CreateRuntimeFunctionPointer(ResolvedRuntimeMethod method) =>
        new(MetadataTokens.GetToken(method.Handle), method);

    private static EvaluationClosure GetEvaluationClosure(CilValue value)
    {
        if (value.Location is not null)
        {
            value = value.Dereference();
        }

        return value.Value as EvaluationClosure ?? throw new InvalidOperationException("Generated evaluation field requires an evaluation object");
    }

    private static void SynchronizeEvaluationClosures(IEnumerable<CilValue> values, EvaluationHandleScope handles)
    {
        HashSet<EvaluationClosure> visited = new HashSet<EvaluationClosure>(ReferenceEqualityComparer.Instance);
        foreach (CilValue value in values)
        {
            SynchronizeEvaluationClosure(value, handles, visited);
        }
    }

    private static void SynchronizeEvaluationClosure(CilValue value, EvaluationHandleScope handles, HashSet<EvaluationClosure> visited)
    {
        if (value.Location is not null)
        {
            value = value.Dereference();
        }

        if (value.Value is EvaluationDelegate evaluationDelegate)
        {
            SynchronizeEvaluationClosure(evaluationDelegate.Target, handles, visited);
            return;
        }
        if (value.Value is not EvaluationClosure evaluationClosure ||
            evaluationClosure.MaterializedValue is null ||
            !visited.Add(evaluationClosure))
        {
            return;
        }

        ICorDebugObjectValue materializedObject = evaluationClosure.MaterializedValue.UnwrapDebugValueToObject();
        ICorDebugClass materializedClass = evaluationClosure.MaterializedValue.ExactType.Class;
        foreach ((FieldDefinitionHandle fieldHandle, ICilLocation? location) in evaluationClosure.Fields)
        {
            CilValue currentValue = location.Read();
            if (currentValue.Value is EvaluationClosure or EvaluationDelegate)
            {
                SynchronizeEvaluationClosure(currentValue, handles, visited);
                continue;
            }
            ICorDebugValue fieldValue = materializedObject.GetFieldValue(materializedClass, (mdFieldDef)MetadataTokens.GetToken(fieldHandle));
            CilValue synchronizedValue = handles.Root(CilValue.FromCorValue(fieldValue));
            location.Write(synchronizedValue);
            if (evaluationClosure.FieldBindings.TryGetValue(fieldHandle, out ICilLocation? binding))
            {
                binding.Write(synchronizedValue);
            }
        }
    }

    private static ICilLocation GetEvaluationStaticField(Dictionary<FieldDefinitionHandle, ICilLocation> fields, FieldDefinitionHandle handle)
    {
        if (!fields.TryGetValue(handle, out ICilLocation? field))
        {
            fields[handle] = field = new TemporaryLocation(CilValue.Null());
        }

        return field;
    }

    private async Task<ICorDebugValue> MaterializeAsync(
        CilValue value,
        CompiledExpressionEvaluationContext context,
        EvaluationHandleScope handles,
        ResolvedCilType? expectedType = null,
        EvaluationMetadataResolver? resolver = null)
    {
        if (value.CorValue is not null && expectedType?.Primitive is null or PrimitiveTypeCode.String or PrimitiveTypeCode.Object)
        {
            return value.CorValue;
        }

        ICorDebugEval eval = context.Thread.CreateEval();
        CorElementType? expectedElementType = GetPrimitiveElementType(expectedType?.Primitive);
        if (value.Value is null && expectedElementType is { } primitiveElementType && value.CorValue?.UnwrapDebugValue() is ICorDebugGenericValue sourceGeneric)
        {
            ICorDebugGenericValue primitiveResult = (ICorDebugGenericValue)eval.CreateValue(primitiveElementType, null);
            byte[] sourceBytes = sourceGeneric.GetValueAsBytes();
            if (sourceBytes.Length != primitiveResult.Size && value.TryGetInt64(out long integer))
            {
                // An enum narrower or wider than the result goes through its value, signed as its type is:
                // an sbyte enum's -1 widened byte by byte would be 255 (upstream #30).
                sourceBytes = CilValueEncoding.GetBytesForSize(integer, primitiveResult.Size);
            }
            unsafe
            {
                fixed (byte* pointer = sourceBytes)
                {
                    primitiveResult.SetValue((nint)pointer);
                }
            }
            return primitiveResult;
        }
        if (value.Value is null)
        {
            return eval.CreateValue(CorElementType.CLASS, null);
        }

        if (value.Value is string text)
        {
            return handles.Track(await eval.NewStringAsync(debugger.ProcessRuntimeEventsUntilEvalEvent, debugger.EvalStatus, text, throwOnException: true))!;
        }

        if (expectedType?.RuntimeType is { } runtimeType && resolver is not null)
        {
            ICorDebugValue typedResult = handles.Track(await eval.NewParameterizedObjectNoConstructorAsync(
                debugger.ProcessRuntimeEventsUntilEvalEvent,
                debugger.EvalStatus,
                runtimeType.Class,
                0,
                null,
                throwOnException: true)) ?? throw new InvalidOperationException("Failed to create evaluation result value type");
            new CorDebugLocation(typedResult).Write(value);
            return typedResult;
        }

        CorElementType elementType = expectedElementType ?? value.Value switch
        {
            bool => CorElementType.BOOLEAN,
            char => CorElementType.CHAR,
            sbyte => CorElementType.I1,
            byte => CorElementType.U1,
            short => CorElementType.I2,
            ushort => CorElementType.U2,
            int => CorElementType.I4,
            uint => CorElementType.U4,
            long => CorElementType.I8,
            ulong => CorElementType.U8,
            float => CorElementType.R4,
            double => CorElementType.R8,
            _ => throw new NotSupportedException($"Cannot materialize CIL value '{value.Value.GetType().Name}'")
        };
        ICorDebugValue result = eval.CreateValue(elementType, null);
        ICorDebugGenericValue generic = (ICorDebugGenericValue)result;
        object materializedValue = elementType == CorElementType.BOOLEAN ? value.IsTrue() : value.Value;
        byte[] bytes = CilValueEncoding.GetBytes(materializedValue, elementType);
        unsafe
        {
            fixed (byte* pointer = bytes)
            {
                generic.SetValue((nint)pointer);
            }
        }
        return result;
    }

    private static CorElementType? GetPrimitiveElementType(PrimitiveTypeCode? primitive) => primitive switch
    {
        PrimitiveTypeCode.Boolean => CorElementType.BOOLEAN,
        PrimitiveTypeCode.Char => CorElementType.CHAR,
        PrimitiveTypeCode.SByte => CorElementType.I1,
        PrimitiveTypeCode.Byte => CorElementType.U1,
        PrimitiveTypeCode.Int16 => CorElementType.I2,
        PrimitiveTypeCode.UInt16 => CorElementType.U2,
        PrimitiveTypeCode.Int32 => CorElementType.I4,
        PrimitiveTypeCode.UInt32 => CorElementType.U4,
        PrimitiveTypeCode.Int64 => CorElementType.I8,
        PrimitiveTypeCode.UInt64 => CorElementType.U8,
        PrimitiveTypeCode.Single => CorElementType.R4,
        PrimitiveTypeCode.Double => CorElementType.R8,
        PrimitiveTypeCode.IntPtr => CorElementType.I,
        PrimitiveTypeCode.UIntPtr => CorElementType.U,
        _ => null
    };

    private async ValueTask<ICorDebugValue> GetStaticFieldValue(ResolvedRuntimeField field, EvaluationMetadataResolver resolver, CompiledExpressionEvaluationContext context)
    {
        ICorDebugType type = resolver.GetCorDebugType(field.DeclaringType);
        return await debugger.GetStaticFieldValueAsync(type, field.Token, context.ThreadId, context.StackDepth);
    }

    private async Task<CilValue> CreateDefaultValueAsync(ResolvedCilType type, EvaluationMetadataResolver resolver, CompiledExpressionEvaluationContext context, EvaluationHandleScope handles)
    {
        if (GetPrimitiveElementType(type.Primitive) is { } primitiveType)
        {
            return primitiveType switch
            {
                CorElementType.R4 => CilValue.FromPrimitive(0f),
                CorElementType.R8 => CilValue.FromPrimitive(0d),
                CorElementType.I8 => CilValue.FromPrimitive(0L),
                CorElementType.U8 => CilValue.FromPrimitive(0UL),
                _ => CilValue.FromPrimitive(0)
            };
        }
        if (IsReferenceType(type))
        {
            return CilValue.Null();
        }
        ResolvedRuntimeType runtimeType = type.RuntimeType ?? throw new NotSupportedException("Initializing this CIL type is not supported");
        ICorDebugType[] typeArguments = runtimeType.TypeArguments.IsDefaultOrEmpty
            ? []
            : runtimeType.TypeArguments.Select(resolver.GetCorDebugType).ToArray();
        ICorDebugEval eval = context.Thread.CreateEval();
        ICorDebugValue value = handles.Track(await eval.NewParameterizedObjectNoConstructorAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            runtimeType.Class,
            typeArguments.Length,
            typeArguments.Length == 0 ? null : typeArguments,
            throwOnException: true))
            ?? throw new InvalidOperationException("Failed to create a default value type");
        return CilValue.FromCorValue(value);
    }

    private static bool IsReferenceType(ResolvedCilType type)
    {
        if (type.Primitive is PrimitiveTypeCode.String or PrimitiveTypeCode.Object)
        {
            return true;
        }

        if (type.ElementType is not null)
        {
            return true;
        }

        return type.RuntimeType is { } runtimeType && !EvaluationMetadataResolver.IsValueType(runtimeType);
    }

    private async Task<ICilLocation> CreateSyntheticVariableLocationAsync(ResolvedCilType type, EvaluationMetadataResolver resolver, CompiledExpressionEvaluationContext context, EvaluationHandleScope handles)
    {
        ICorDebugValue arrayReference = await CreateArrayAsync(type, resolver.GetCorDebugType(type), 1, resolver, context, handles)
            ?? throw new InvalidOperationException("Failed to allocate synthetic variable storage");
        if (arrayReference.UnwrapDebugValue() is not ICorDebugArrayValue)
        {
            throw new InvalidOperationException("Failed to allocate synthetic variable storage");
        }

        return new SyntheticVariableLocation(arrayReference);
    }

    private async Task<ICorDebugValue?> CreateArrayAsync(
        ResolvedCilType elementCilType,
        ICorDebugType elementType,
        uint length,
        EvaluationMetadataResolver resolver,
        CompiledExpressionEvaluationContext context,
        EvaluationHandleScope handles)
    {
        ICorDebugEval eval = context.Thread.CreateEval();
        if (IsEvalAllocatableArrayElementType(elementType))
        {
            return handles.Track(await eval.NewParameterizedArrayAsync(debugger.ProcessRuntimeEventsUntilEvalEvent, debugger.EvalStatus, elementType, length, throwOnException: true));
        }

        // ICorDebugEval::NewArray can only allocate arrays of primitive and reference types; for other
        // value types (e.g. DateTime) the runtime throws ArgumentOutOfRangeException in the debuggee.
        // In this case we have to allocate through a real Array.CreateInstance call.
        ICorDebugValue arrayType = await GetSystemTypeAsync(elementCilType, resolver, context, handles) ?? throw new InvalidOperationException("Failed to resolve the element type for array allocation");
        ResolvedRuntimeMethod createInstance = resolver.ResolveRuntimeMethod("System", "Array", "CreateInstance", "System.Type", PrimitiveTypeCode.Int32.ToString());
        ICorDebugValue lengthValue = await MaterializeAsync(CilValue.FromPrimitive(checked((int)length)), context, handles);
        return handles.Track(await eval.CallParameterizedFunctionAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            createInstance.Function,
            0,
            null,
            2,
            [arrayType, lengthValue],
            throwOnException: true));
    }

    private bool IsEvalAllocatableArrayElementType(ICorDebugType elementType)
    {
        if (elementType.Type is not CorElementType.VALUETYPE)
        {
            return true;
        }

        // A token names a type only within its module: a struct of the user's can share a primitive's token.
        ICorDebugClass elementClass = elementType.Class;
        return primitiveTypes.CorElementToValueClassMap.Values.Any(valueClass => valueClass.Token == elementClass.Token
            && valueClass.Module.BaseAddress == elementClass.Module.BaseAddress);
    }

    /// <summary>
    /// Verifies that a boxed value can be unboxed to <paramref name="targetType"/>: the boxed value must be an
    /// exact primitive match or an exact runtime type match. Enum/underlying-type and interface-compatible boxes
    /// are intentionally not accepted (unbox.any is only emitted for value-type targets).
    /// </summary>
    private static bool IsUnboxCompatible(ICorDebugValue boxedObject, ResolvedCilType targetType, EvaluationMetadataResolver resolver)
    {
        ICorDebugValue unwrapped = boxedObject.UnwrapDebugValue();
        if (targetType.Primitive is { } primitive)
        {
            CorElementType? expectedElementType = GetPrimitiveElementType(primitive);
            return unwrapped is ICorDebugGenericValue generic && expectedElementType is not null && generic.Type == expectedElementType;
        }
        if (targetType.RuntimeType is { } runtimeType)
        {
            ICorDebugType exactType = boxedObject.ExactType;
            if (exactType.Type is not (CorElementType.VALUETYPE or CorElementType.CLASS))
            {
                return false;
            }

            // The same definition, and, for a generic one, the same arguments: a Pair<int> is no Pair<string>.
            return exactType.Class.Token == runtimeType.Class.Token
                && exactType.Class.Module.BaseAddress == runtimeType.Class.Module.BaseAddress
                && (runtimeType.TypeArguments.IsDefaultOrEmpty
                    || exactType.TypeParameters.Select(ManagedDebugger.GetCorDebugTypeFriendlyName)
                        .SequenceEqual(runtimeType.TypeArguments.Select(a => ManagedDebugger.GetCorDebugTypeFriendlyName(resolver.GetCorDebugType(a)))));
        }
        return false;
    }

    private async Task<bool> IsInstanceOfTypeAsync(CilValue value, ResolvedCilType targetType, EvaluationMetadataResolver resolver, CompiledExpressionEvaluationContext context, EvaluationHandleScope handles)
    {
        ICorDebugValue typeValue = await GetSystemTypeAsync(targetType, resolver, context, handles)
            ?? throw new InvalidOperationException("Failed to resolve the target System.Type");
        ResolvedRuntimeMethod method = resolver.ResolveRuntimeMethod("System", "Type", "IsInstanceOfType", PrimitiveTypeCode.Object.ToString());
        ICorDebugValue sourceValue = value.CorValue ?? throw new NotSupportedException("Runtime type checks require a debuggee value");
        ICorDebugEval eval = context.Thread.CreateEval();
        ICorDebugValue? result = handles.Track(await eval.CallParameterizedFunctionAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            method.Function,
            0,
            null,
            2,
            [typeValue, sourceValue],
            throwOnException: true));
        return result is not null && CilValue.FromCorValue(result).IsTrue();
    }

    private async Task<ICorDebugValue?> GetSystemTypeAsync(ResolvedCilType type, EvaluationMetadataResolver resolver, CompiledExpressionEvaluationContext context, EvaluationHandleScope handles)
    {
        ResolvedRuntimeMethod getType = resolver.ResolveRuntimeMethod("System", "Type", "GetType", PrimitiveTypeCode.String.ToString());
        ICorDebugEval eval = context.Thread.CreateEval();
        ICorDebugValue typeName = handles.Track(await eval.NewStringAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            resolver.GetAssemblyQualifiedTypeName(type),
            throwOnException: true))!;
        return handles.Track(await eval.CallParameterizedFunctionAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            getType.Function,
            0,
            null,
            1,
            [typeName],
            throwOnException: true));
    }

    private static ICorDebugArrayValue GetArrayValue(CilValue value) =>
        value.CorValue?.UnwrapDebugValue() as ICorDebugArrayValue
        ?? throw new NullReferenceException("Array reference is null");

    private static ICorDebugObjectValue GetFieldReceiver(CilValue receiver)
    {
        ICorDebugValue? corValue = receiver.CorValue;
        if (corValue is null && receiver.Location is IDebuggeeLocation directLocation)
        {
            corValue = directLocation.Value;
        }
        else if (corValue is null && receiver.Location is not null)
        {
            corValue = receiver.Location.Read().CorValue;
        }

        return corValue?.UnwrapDebugValueToObject() ?? throw new NullReferenceException("Instance field receiver is null");
    }

    private async Task<CilValue> BoxAsync(CilValue value, ICorDebugType targetType, CompiledExpressionEvaluationContext context, EvaluationHandleScope handles)
    {
        if (value.Location is not null)
        {
            value = value.Dereference();
        }

        byte[] sourceBytes = value.CorValue?.UnwrapDebugValue() is ICorDebugGenericValue sourceGeneric
            ? sourceGeneric.GetValueAsBytes()
            : value.Value is { } primitive
                ? CilValueEncoding.GetBytes(primitive, GetPrimitiveElementType(primitive))
                : throw new InvalidOperationException("Cannot box a null value");
        ICorDebugType[] typeArguments = targetType.TypeParameters;
        ICorDebugEval eval = context.Thread.CreateEval();
        ICorDebugValue boxed = handles.Track(await eval.NewParameterizedObjectNoConstructorAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            targetType.Class,
            typeArguments.Length,
            typeArguments.Length == 0 ? null : typeArguments,
            throwOnException: true))
            ?? throw new InvalidOperationException("Failed to box CIL value");
        ICorDebugGenericValue boxedGeneric = (ICorDebugGenericValue)boxed.UnwrapDebugValue();
        unsafe
        {
            fixed (byte* pointer = sourceBytes)
            {
                boxedGeneric.SetValue((nint)pointer);
            }
        }
        return CilValue.FromCorValue(boxed);
    }

    private async Task<ICorDebugValue> MaterializeForCallAsync(CilValue value, CompiledExpressionEvaluationContext context, EvaluationHandleScope handles)
    {
        if (value.Location is not null)
        {
            value = value.Dereference();
        }

        if (value.CorValue is not null)
        {
            return value.CorValue;
        }

        return await MaterializeAsync(value, context, handles);
    }

    private async Task<ICorDebugValue> MaterializeForCallAsync(
        CilValue value,
        CompiledEvaluationMethod compiled,
        EvaluationMetadataResolver resolver,
        DebuggeeDelegateAssemblyLoader delegateAssemblyLoader,
        CompiledExpressionEvaluationContext context,
        EvaluationHandleScope handles)
    {
        if (value.Location is not null)
        {
            value = value.Dereference();
        }

        return value.Value switch
        {
            EvaluationDelegate => await MaterializeDelegateAsync(value, compiled, resolver, delegateAssemblyLoader, context, handles),
            EvaluationClosure evaluationClosure => await MaterializeEvaluationClosureAsync(evaluationClosure, compiled, resolver, delegateAssemblyLoader, context, handles),
            _ => await MaterializeForCallAsync(value, context, handles)
        };
    }

    private async Task<ICorDebugValue> MaterializeDelegateAsync(
        CilValue value,
        CompiledEvaluationMethod compiled,
        EvaluationMetadataResolver resolver,
        DebuggeeDelegateAssemblyLoader delegateAssemblyLoader,
        CompiledExpressionEvaluationContext context,
        EvaluationHandleScope handles)
    {
        EvaluationDelegate delegateValue = value.Value as EvaluationDelegate ?? throw new InvalidOperationException("CIL value is not an evaluation delegate");
        DelegateMaterializerAssembly materializer = delegateAssemblyLoader.Materializer;
        int delegateAssemblySessionId = 0;
        Guid methodModuleVersionId;
        if (delegateValue.Function.RuntimeMethod is { } runtimeMethod)
        {
            methodModuleVersionId = runtimeMethod.DeclaringType.Module.MetadataReader.Mvid;
        }
        else
        {
            methodModuleVersionId = compiled.ModuleVersionId;
            delegateAssemblySessionId = await delegateAssemblyLoader.GetSessionIdAsync();
        }

        ICorDebugValue target = delegateValue.Target.IsNull
            ? await MaterializeAsync(CilValue.Null(), context, handles)
            : await MaterializeForCallAsync(delegateValue.Target, compiled, resolver, delegateAssemblyLoader, context, handles);
        ICorDebugFunction factory = await delegateAssemblyLoader.GetMaterializerFunctionAsync(materializer.MethodName);
        ICorDebugValue sessionIdArgument = await MaterializeAsync(CilValue.FromPrimitive(delegateAssemblySessionId), context, handles);
        ICorDebugValue methodModule = await MaterializeAsync(CilValue.FromPrimitive(methodModuleVersionId.ToString("D")), context, handles);
        ICorDebugValue methodToken = await MaterializeAsync(CilValue.FromPrimitive(delegateValue.Function.MethodToken), context, handles);
        string delegateTypeName = resolver.GetAssemblyQualifiedTypeName(new ResolvedCilType(null, delegateValue.DelegateType));
        ICorDebugValue delegateType = await MaterializeAsync(CilValue.FromPrimitive(delegateTypeName), context, handles);
        ICorDebugValue contextModule = await MaterializeAsync(CilValue.FromPrimitive(resolver.CurrentFrameModuleVersionId.ToString("D")), context, handles);
        ICorDebugValue result = await context.Thread.CreateEval().CallParameterizedFunctionAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            factory,
            0,
            null,
            6,
            [sessionIdArgument, methodModule, methodToken, delegateType, contextModule, target],
            throwOnException: true) ?? throw new InvalidOperationException("Delegate materializer returned no value");
        return handles.Root(CilValue.FromCorValue(result)).CorValue!;
    }

    private async Task<ICorDebugValue> MaterializeEvaluationClosureAsync(
        EvaluationClosure evaluationClosure,
        CompiledEvaluationMethod compiled,
        EvaluationMetadataResolver resolver,
        DebuggeeDelegateAssemblyLoader delegateAssemblyLoader,
        CompiledExpressionEvaluationContext context,
        EvaluationHandleScope handles)
    {
        if (evaluationClosure.MaterializedValue is not null)
        {
            return evaluationClosure.MaterializedValue;
        }

        int delegateAssemblySessionId = await delegateAssemblyLoader.GetSessionIdAsync();
        ICorDebugFunction createObject = await delegateAssemblyLoader.GetMaterializerFunctionAsync("CreateObject");
        ICorDebugValue sessionIdArgument = await MaterializeAsync(CilValue.FromPrimitive(delegateAssemblySessionId), context, handles);
        ICorDebugValue constructorToken = await MaterializeAsync(CilValue.FromPrimitive(MetadataTokens.GetToken(evaluationClosure.Constructor)), context, handles);
        ICorDebugValue created = await context.Thread.CreateEval().CallParameterizedFunctionAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            createObject,
            0,
            null,
            2,
            [sessionIdArgument, constructorToken],
            throwOnException: true) ?? throw new InvalidOperationException("Failed to materialize generated closure object");
        ICorDebugValue value = handles.Root(CilValue.FromCorValue(created)).CorValue!;
        evaluationClosure.MaterializedValue = value;
        ICorDebugFunction setField = await delegateAssemblyLoader.GetMaterializerFunctionAsync("SetField");
        foreach ((FieldDefinitionHandle fieldHandle, ICilLocation? location) in evaluationClosure.Fields)
        {
            CilValue fieldValue = location.Read();
            if (fieldValue.Location is not null)
            {
                fieldValue = fieldValue.Dereference();
            }

            ICorDebugValue materializedField = await MaterializeForCallAsync(fieldValue, compiled, resolver, delegateAssemblyLoader, context, handles);
            if (materializedField.UnwrapDebugValue() is ICorDebugGenericValue
                {
                    Type: not (CorElementType.CLASS or CorElementType.OBJECT or CorElementType.STRING or CorElementType.SZARRAY or CorElementType.ARRAY)
                })
            {
                materializedField = (await BoxAsync(CilValue.FromCorValue(materializedField), resolver.GetCorDebugType(resolver.ResolveEvaluationFieldType(fieldHandle)), context, handles)).CorValue!;
            }
            ICorDebugValue fieldToken = await MaterializeAsync(CilValue.FromPrimitive(MetadataTokens.GetToken(fieldHandle)), context, handles);
            await context.Thread.CreateEval().CallParameterizedFunctionAsync(
                debugger.ProcessRuntimeEventsUntilEvalEvent,
                debugger.EvalStatus,
                setField,
                0,
                null,
                4,
                [sessionIdArgument, fieldToken, value, materializedField],
                throwOnException: true);
        }
        return value;
    }

    /// <summary>
    /// Normalizes a value before it is written into a debuggee location. Host values that have no debuggee
    /// representation yet (e.g. strings produced by <c>ldstr</c>) must be materialized into the debuggee first,
    /// because <see cref="CorDebugLocation.Write"/> can only persist values backed by an ICorDebugValue.
    /// </summary>
    private async Task<CilValue> MaterializeForStoreAsync(CilValue value, CompiledExpressionEvaluationContext context, EvaluationHandleScope handles)
    {
        if (value.Location is not null)
        {
            value = value.Dereference();
        }

        if (value.CorValue is not null || value.IsNull)
        {
            return value;
        }

        if (value.Value is string text)
        {
            ICorDebugEval eval = context.Thread.CreateEval();
            ICorDebugValue? materialized = handles.Track(await eval.NewStringAsync(
                debugger.ProcessRuntimeEventsUntilEvalEvent,
                debugger.EvalStatus,
                text,
                throwOnException: true));
            return CilValue.FromDebuggeeValue(materialized ?? throw new InvalidOperationException($"The debuggee made no string for \"{text}\""));
        }
        return value;
    }

    private async Task<ICorDebugValue> MaterializeByRefArgumentAsync(
        CilValue value,
        CompiledExpressionEvaluationContext context,
        EvaluationHandleScope handles,
        List<(ICilLocation Location, ICorDebugValue Value)> temporaryArguments)
    {
        if (value.Location is IDebuggeeLocation location)
        {
            return location.Value;
        }

        if (value.Location is SyntheticVariableLocation synthetic)
        {
            return synthetic.StorageValue;
        }

        if (value.Location is null)
        {
            throw new InvalidOperationException("A by-reference argument requires a managed location");
        }

        ICorDebugValue materialized = await MaterializeForCallAsync(value.Location.Read(), context, handles);
        temporaryArguments.Add((value.Location, materialized));
        return materialized;
    }

    private static void WriteBackTemporaryByRefArguments(IEnumerable<(ICilLocation Location, ICorDebugValue Value)> arguments, EvaluationHandleScope handles)
    {
        foreach ((ICilLocation? location, ICorDebugValue? value) in arguments)
        {
            location.Write(handles.Root(CilValue.FromCorValue(value)));
        }
    }

    private async Task<ICorDebugValue> MaterializeReceiverAsync(
        CilValue value,
        CompiledExpressionEvaluationContext context,
        ResolvedCilType? constrainedType,
        EvaluationMetadataResolver resolver,
        EvaluationHandleScope handles)
    {
        ICorDebugThread thread = context.Thread;
        if (constrainedType is not null && value.CorValue?.UnwrapDebugValue() is ICorDebugGenericValue sourceGeneric)
        {
            ICorDebugType exactType = value.CorValue.ExactType;
            ICorDebugType[] typeArguments = exactType.TypeParameters;
            ICorDebugEval constrainedEval = thread.CreateEval();
            ICorDebugValue constrainedBox = handles.Track(await constrainedEval.NewParameterizedObjectNoConstructorAsync(
                debugger.ProcessRuntimeEventsUntilEvalEvent,
                debugger.EvalStatus,
                exactType.Class,
                typeArguments.Length,
                typeArguments.Length == 0 ? null : typeArguments,
                throwOnException: true))
                ?? throw new InvalidOperationException("Failed to box constrained CIL receiver");
            ICorDebugGenericValue constrainedGeneric = (ICorDebugGenericValue)constrainedBox.UnwrapDebugValue();
            byte[] constrainedData = sourceGeneric.GetValueAsBytes();
            unsafe
            {
                fixed (byte* pointer = constrainedData)
                {
                    constrainedGeneric.SetValue((nint)pointer);
                }
            }
            return constrainedBox;
        }
        if (value.CorValue is not null)
        {
            return value.CorValue;
        }

        if (value.Value is null)
        {
            return await MaterializeForCallAsync(value, context, handles);
        }

        CorElementType elementType = GetPrimitiveElementType(value.Value);
        if (!primitiveTypes.CorElementToValueClassMap.TryGetValue(elementType, out ICorDebugClass? boxedClass))
        {
            return await MaterializeForCallAsync(value, context, handles);
        }
        byte[] data = CilValueEncoding.GetBytes(value.Value, elementType);

        ICorDebugEval eval = thread.CreateEval();
        ICorDebugValue boxed = handles.Track(await eval.NewParameterizedObjectNoConstructorAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            boxedClass,
            0,
            null,
            throwOnException: true)) ?? throw new InvalidOperationException("Failed to box CIL receiver");
        ICorDebugGenericValue boxedGeneric = (ICorDebugGenericValue)boxed.UnwrapDebugValue();
        unsafe
        {
            fixed (byte* pointer = data)
            {
                boxedGeneric.SetValue((nint)pointer);
            }
        }
        return boxed;
    }

    private static CorElementType GetPrimitiveElementType(object value) => value switch
    {
        bool => CorElementType.BOOLEAN,
        char => CorElementType.CHAR,
        sbyte => CorElementType.I1,
        byte => CorElementType.U1,
        short => CorElementType.I2,
        ushort => CorElementType.U2,
        int => CorElementType.I4,
        uint => CorElementType.U4,
        long => CorElementType.I8,
        ulong => CorElementType.U8,
        float => CorElementType.R4,
        double => CorElementType.R8,
        _ => throw new NotSupportedException($"Value '{value.GetType().Name}' is not a primitive CIL value")
    };

    // The following method calls are special-cased instead of being executed through ICorDebugEval because
    // they cannot be run in the debuggee: the DefaultInterpolatedStringHandler calls that the compiler lowers
    // interpolated strings to. Real pointer/span arithmetic (Unsafe.*, MemoryMarshal, span-based String.Join)
    // is not modeled - it cannot be executed through ICorDebugEval, and I am undecided whether it should be emulated on the
    // debugger side, so such expressions surface as eval errors rather than returning incorrect results.
    private async Task<(bool Handled, CilValue? Result)> TryExecuteIntrinsicCallAsync(
        ResolvedRuntimeMethod method,
        CilValue? receiver,
        CilValue[] arguments,
        EvaluationMetadataResolver resolver,
        CompiledExpressionEvaluationContext context,
        EvaluationHandleScope handles)
    {
        string declaringType = resolver.GetRuntimeTypeName(method.DeclaringType);
        if (declaringType != "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler")
        {
            return (false, null);
        }

        ICilLocation? receiverLocation = receiver?.Location;
        if (method.Name == ".ctor")
        {
            receiverLocation?.Write(CilValue.FromPrimitive(new StringBuilder()));
            return (true, null);
        }
        StringBuilder builder = receiverLocation?.Read().Value as StringBuilder
            ?? receiver?.Value as StringBuilder
            ?? throw new InvalidOperationException("Interpolated string handler receiver is unavailable");
        if (method.Name == "AppendLiteral")
        {
            builder.Append(arguments[0].Value as string);
            return (true, null);
        }
        if (method.Name == "AppendFormatted")
        {
            CilValue value = arguments[0];
            int alignment = arguments.Select(a => a.Value).OfType<int>().Skip(value.Value is int ? 1 : 0).FirstOrDefault();
            string? format = arguments.Select(a => a.Value).OfType<string>().FirstOrDefault();
            string text = await FormatInterpolatedValueAsync(value, format, resolver, context, handles);
            if (alignment != 0)
            {
                text = alignment > 0 ? text.PadLeft(alignment) : text.PadRight(-alignment);
            }

            builder.Append(text);
            return (true, null);
        }
        if (method.Name is "ToStringAndClear" or "ToString")
        {
            return (true, CilValue.FromPrimitive(builder.ToString()));
        }
        return (false, null);
    }

    private async Task<string> FormatInterpolatedValueAsync(
        CilValue value,
        string? format,
        EvaluationMetadataResolver resolver,
        CompiledExpressionEvaluationContext context,
        EvaluationHandleScope handles)
    {
        if (value.Location is not null)
        {
            value = value.Dereference();
        }

        if (value.IsNull)
        {
            return string.Empty;
        }

        if (value.Value is IFormattable formattable)
        {
            return formattable.ToString(format, null);
        }

        if (value.Value is not null)
        {
            return value.Value.ToString() ?? string.Empty;
        }

        if (value.GetStringText() is { } text)
        {
            return text;
        }

        ICorDebugValue receiver = value.CorValue ?? throw new InvalidOperationException("Interpolated value is unavailable");
        bool isDebuggeeValueType = receiver.ExactType.Type == CorElementType.VALUETYPE;
        if (isDebuggeeValueType)
        {
            receiver = (await BoxAsync(value, receiver.ExactType, context, handles)).CorValue!;
        }
        ResolvedRuntimeMethod concat = resolver.ResolveRuntimeMethod("System", "String", "Concat", PrimitiveTypeCode.Object.ToString());
        ICorDebugEval eval = context.Thread.CreateEval();
        ICorDebugValue? result = handles.Track(await eval.CallParameterizedFunctionAsync(
            debugger.ProcessRuntimeEventsUntilEvalEvent,
            debugger.EvalStatus,
            concat.Function,
            0,
            null,
            1,
            [receiver],
            throwOnException: true));
        return result?.UnwrapDebugValue() is ICorDebugStringValue stringValue ? stringValue.String : string.Empty;
    }

    private static bool TryGetArgumentIndex(OpCode op, object? operand, out int index)
    {
        index = op.Value switch
        {
            var value when value == OpCodes.Ldarg_0.Value => 0,
            var value when value == OpCodes.Ldarg_1.Value => 1,
            var value when value == OpCodes.Ldarg_2.Value => 2,
            var value when value == OpCodes.Ldarg_3.Value => 3,
            var value when value == OpCodes.Ldarg.Value || value == OpCodes.Ldarg_S.Value => Convert.ToInt32(operand),
            _ => -1
        };
        return index >= 0;
    }

    private static bool TryGetArgumentAddressIndex(OpCode op, object? operand, out int index)
    {
        index = op == OpCodes.Ldarga || op == OpCodes.Ldarga_S ? Convert.ToInt32(operand) : -1;
        return index >= 0;
    }

    private static bool TryGetStoreArgumentIndex(OpCode op, object? operand, out int index)
    {
        index = op == OpCodes.Starg || op == OpCodes.Starg_S ? Convert.ToInt32(operand) : -1;
        return index >= 0;
    }

    private static bool TryGetLocalIndex(OpCode op, object? operand, out int index)
    {
        index = op.Value switch
        {
            var value when value == OpCodes.Ldloc_0.Value => 0,
            var value when value == OpCodes.Ldloc_1.Value => 1,
            var value when value == OpCodes.Ldloc_2.Value => 2,
            var value when value == OpCodes.Ldloc_3.Value => 3,
            var value when value == OpCodes.Ldloc.Value || value == OpCodes.Ldloc_S.Value => Convert.ToInt32(operand),
            _ => -1
        };
        return index >= 0;
    }

    private static bool TryGetLocalAddressIndex(OpCode op, object? operand, out int index)
    {
        index = op == OpCodes.Ldloca || op == OpCodes.Ldloca_S ? Convert.ToInt32(operand) : -1;
        return index >= 0;
    }

    private static bool TryGetStoreLocalIndex(OpCode op, object? operand, out int index)
    {
        index = op.Value switch
        {
            var value when value == OpCodes.Stloc_0.Value => 0,
            var value when value == OpCodes.Stloc_1.Value => 1,
            var value when value == OpCodes.Stloc_2.Value => 2,
            var value when value == OpCodes.Stloc_3.Value => 3,
            var value when value == OpCodes.Stloc.Value || value == OpCodes.Stloc_S.Value => Convert.ToInt32(operand),
            _ => -1
        };
        return index >= 0;
    }

    private static bool IsBinary(OpCode op) => op == OpCodes.Add || op == OpCodes.Sub || op == OpCodes.Mul ||
        op == OpCodes.Add_Ovf || op == OpCodes.Add_Ovf_Un || op == OpCodes.Sub_Ovf || op == OpCodes.Sub_Ovf_Un ||
        op == OpCodes.Mul_Ovf || op == OpCodes.Mul_Ovf_Un ||
        op == OpCodes.Div || op == OpCodes.Div_Un || op == OpCodes.Rem || op == OpCodes.Rem_Un ||
        op == OpCodes.And || op == OpCodes.Or || op == OpCodes.Xor || op == OpCodes.Shl || op == OpCodes.Shr || op == OpCodes.Shr_Un;

    private static CilValue EvaluateBinary(OpCode op, CilValue left, CilValue right)
    {
        if (left.Value is float or double || right.Value is float or double)
        {
            double a = left.AsFloat();
            double b = right.AsFloat();
            double result = op.Value switch
            {
                var value when value == OpCodes.Add.Value => a + b,
                var value when value == OpCodes.Sub.Value => a - b,
                var value when value == OpCodes.Mul.Value => a * b,
                var value when value == OpCodes.Div.Value => a / b,
                var value when value == OpCodes.Rem.Value => a % b,
                _ => throw new NotSupportedException($"Floating-point operation '{op.Name}' is not supported")
            };
            if (left.Value is double || right.Value is double)
            {
                return CilValue.FromPrimitive(result);
            }

            return CilValue.FromPrimitive((float)result);
        }

        if (left.Value is long or ulong || right.Value is long or ulong)
        {
            long a = left.AsInt64();
            long b = right.AsInt64();
            if (op == OpCodes.Div_Un)
            {
                return CilValue.FromPrimitive(unchecked((ulong)a) / unchecked((ulong)b));
            }

            if (op == OpCodes.Rem_Un)
            {
                return CilValue.FromPrimitive(unchecked((ulong)a) % unchecked((ulong)b));
            }

            if (op == OpCodes.Shr_Un)
            {
                return CilValue.FromPrimitive(unchecked((long)(unchecked((ulong)a) >> ((int)b & 0x3f))));
            }

            if (op == OpCodes.Add_Ovf)
            {
                return CilValue.FromPrimitive(checked(a + b));
            }

            if (op == OpCodes.Sub_Ovf)
            {
                return CilValue.FromPrimitive(checked(a - b));
            }

            if (op == OpCodes.Mul_Ovf)
            {
                return CilValue.FromPrimitive(checked(a * b));
            }

            if (op == OpCodes.Add_Ovf_Un)
            {
                return CilValue.FromPrimitive(checked(unchecked((ulong)a) + unchecked((ulong)b)));
            }

            if (op == OpCodes.Sub_Ovf_Un)
            {
                return CilValue.FromPrimitive(checked(unchecked((ulong)a) - unchecked((ulong)b)));
            }

            if (op == OpCodes.Mul_Ovf_Un)
            {
                return CilValue.FromPrimitive(checked(unchecked((ulong)a) * unchecked((ulong)b)));
            }

            return CilValue.FromPrimitive(op.Value switch
            {
                var value when value == OpCodes.Add.Value => a + b,
                var value when value == OpCodes.Sub.Value => a - b,
                var value when value == OpCodes.Mul.Value => a * b,
                var value when value == OpCodes.Div.Value => a / b,
                var value when value == OpCodes.Rem.Value => a % b,
                var value when value == OpCodes.And.Value => a & b,
                var value when value == OpCodes.Or.Value => a | b,
                var value when value == OpCodes.Xor.Value => a ^ b,
                var value when value == OpCodes.Shl.Value => a << ((int)b & 0x3f),
                var value when value == OpCodes.Shr.Value => a >> ((int)b & 0x3f),
                _ => throw new NotSupportedException($"Integer operation '{op.Name}' is not supported")
            });
        }

        int x = left.AsInt32();
        int y = right.AsInt32();
        if (op == OpCodes.Div_Un)
        {
            return CilValue.FromPrimitive(unchecked((uint)x) / unchecked((uint)y));
        }

        if (op == OpCodes.Rem_Un)
        {
            return CilValue.FromPrimitive(unchecked((uint)x) % unchecked((uint)y));
        }

        if (op == OpCodes.Shr_Un)
        {
            return CilValue.FromPrimitive(unchecked((int)(unchecked((uint)x) >> (y & 0x1f))));
        }

        if (op == OpCodes.Add_Ovf)
        {
            return CilValue.FromPrimitive(checked(x + y));
        }

        if (op == OpCodes.Sub_Ovf)
        {
            return CilValue.FromPrimitive(checked(x - y));
        }

        if (op == OpCodes.Mul_Ovf)
        {
            return CilValue.FromPrimitive(checked(x * y));
        }

        if (op == OpCodes.Add_Ovf_Un)
        {
            return CilValue.FromPrimitive(checked(unchecked((uint)x) + unchecked((uint)y)));
        }

        if (op == OpCodes.Sub_Ovf_Un)
        {
            return CilValue.FromPrimitive(checked(unchecked((uint)x) - unchecked((uint)y)));
        }

        if (op == OpCodes.Mul_Ovf_Un)
        {
            return CilValue.FromPrimitive(checked(unchecked((uint)x) * unchecked((uint)y)));
        }

        return CilValue.FromPrimitive(op.Value switch
        {
            var value when value == OpCodes.Add.Value => x + y,
            var value when value == OpCodes.Sub.Value => x - y,
            var value when value == OpCodes.Mul.Value => x * y,
            var value when value == OpCodes.Div.Value => x / y,
            var value when value == OpCodes.Rem.Value => x % y,
            var value when value == OpCodes.And.Value => x & y,
            var value when value == OpCodes.Or.Value => x | y,
            var value when value == OpCodes.Xor.Value => x ^ y,
            var value when value == OpCodes.Shl.Value => x << (y & 0x1f),
            var value when value == OpCodes.Shr.Value => x >> (y & 0x1f),
            _ => throw new NotSupportedException($"Integer operation '{op.Name}' is not supported")
        });
    }

    private static CilValue Negate(CilValue value) => value.Value is float or double
        ? CilValue.FromPrimitive(-value.AsFloat())
        : value.Value is long or ulong
            ? CilValue.FromPrimitive(-value.AsInt64())
            : CilValue.FromPrimitive(-value.AsInt32());

    private static bool Compare(OpCode op, CilValue left, CilValue right)
    {
        if (op == OpCodes.Ceq)
        {
            if (left.IsNull || right.IsNull)
            {
                return left.IsNull == right.IsNull;
            }

            if (left.CorValue is ICorDebugReferenceValue leftRef && right.CorValue is ICorDebugReferenceValue rightRef)
            {
                return leftRef.Value == rightRef.Value;
            }

            if (left.Value is float or double || right.Value is float or double)
            {
                return left.AsFloat() == right.AsFloat();
            }

            if (left.TryGetInt64(out long leftInteger) && right.TryGetInt64(out long rightInteger))
            {
                return leftInteger == rightInteger;
            }

            return Equals(left.Value, right.Value);
        }
        if (left.CorValue is ICorDebugReferenceValue || right.CorValue is ICorDebugReferenceValue || left.IsNull || right.IsNull)
        {
            if (op == OpCodes.Cgt_Un)
            {
                return !left.IsNull && right.IsNull;
            }

            if (op == OpCodes.Clt_Un)
            {
                return left.IsNull && !right.IsNull;
            }

            throw new InvalidOperationException($"Reference values cannot be compared with '{op.Name}'");
        }
        if (left.Value is float or double || right.Value is float or double)
        {
            double a = left.AsFloat();
            double b = right.AsFloat();
            return op == OpCodes.Cgt ? a > b : op == OpCodes.Clt ? a < b :
                op == OpCodes.Cgt_Un ? double.IsNaN(a) || double.IsNaN(b) || a > b : double.IsNaN(a) || double.IsNaN(b) || a < b;
        }
        if (op == OpCodes.Cgt_Un)
        {
            return unchecked((ulong)left.AsInt64()) > unchecked((ulong)right.AsInt64());
        }

        if (op == OpCodes.Clt_Un)
        {
            return unchecked((ulong)left.AsInt64()) < unchecked((ulong)right.AsInt64());
        }

        return op == OpCodes.Cgt ? left.AsInt64() > right.AsInt64() : left.AsInt64() < right.AsInt64();
    }

    private static bool IsConversion(OpCode op) => op.Name?.StartsWith("conv.", StringComparison.Ordinal) == true;

    private static CilValue ConvertValue(OpCode op, CilValue value)
    {
        bool isFloat = value.Value is float or double;
        long signed = isFloat ? unchecked((long)value.AsFloat()) : value.AsInt64();
        ulong unsigned = isFloat ? unchecked((ulong)value.AsFloat()) : value.AsUInt64();
        return op.Value switch
        {
            var v when v == OpCodes.Conv_I1.Value => CilValue.FromPrimitive((int)(sbyte)signed),
            var v when v == OpCodes.Conv_U1.Value => CilValue.FromPrimitive((int)(byte)signed),
            var v when v == OpCodes.Conv_I2.Value => CilValue.FromPrimitive((int)(short)signed),
            var v when v == OpCodes.Conv_U2.Value => CilValue.FromPrimitive((int)(ushort)signed),
            var v when v == OpCodes.Conv_I4.Value => CilValue.FromPrimitive((int)signed),
            var v when v == OpCodes.Conv_U4.Value => CilValue.FromPrimitive((uint)signed),
            var v when v == OpCodes.Conv_I8.Value => CilValue.FromPrimitive(signed),
            var v when v == OpCodes.Conv_U8.Value => CilValue.FromPrimitive(unsigned),
            var v when v == OpCodes.Conv_R4.Value => CilValue.FromPrimitive(isFloat ? (float)value.AsFloat() : (float)signed),
            var v when v == OpCodes.Conv_R8.Value => CilValue.FromPrimitive(isFloat ? value.AsFloat() : (double)signed),
            var v when v == OpCodes.Conv_R_Un.Value => CilValue.FromPrimitive((double)unsigned),
            // The debuggee's pointers, which need not be the debugger's own.
            var v when v == OpCodes.Conv_I.Value => CilValue.FromPrimitive(CilValue.PointerSize == 8 ? signed : (int)signed),
            var v when v == OpCodes.Conv_U.Value => CilValue.FromPrimitive(CilValue.PointerSize == 8 ? unsigned : (uint)unsigned),
            var v when v == OpCodes.Conv_Ovf_I1.Value => CilValue.FromPrimitive((int)checked((sbyte)(isFloat ? value.AsFloat() : signed))),
            var v when v == OpCodes.Conv_Ovf_U1.Value => CilValue.FromPrimitive((int)checked((byte)(isFloat ? value.AsFloat() : signed))),
            var v when v == OpCodes.Conv_Ovf_I2.Value => CilValue.FromPrimitive((int)checked((short)(isFloat ? value.AsFloat() : signed))),
            var v when v == OpCodes.Conv_Ovf_U2.Value => CilValue.FromPrimitive((int)checked((ushort)(isFloat ? value.AsFloat() : signed))),
            var v when v == OpCodes.Conv_Ovf_I4.Value => CilValue.FromPrimitive(checked((int)(isFloat ? value.AsFloat() : signed))),
            var v when v == OpCodes.Conv_Ovf_U4.Value => CilValue.FromPrimitive(checked((uint)(isFloat ? value.AsFloat() : signed))),
            var v when v == OpCodes.Conv_Ovf_I8.Value => CilValue.FromPrimitive(isFloat ? checked((long)value.AsFloat()) : signed),
            var v when v == OpCodes.Conv_Ovf_U8.Value => CilValue.FromPrimitive(isFloat ? checked((ulong)value.AsFloat()) : unsigned),
            // The checked conversions of an unsigned source: its bits read as unsigned.
            var v when v == OpCodes.Conv_Ovf_I1_Un.Value => CilValue.FromPrimitive((int)checked((sbyte)unsigned)),
            var v when v == OpCodes.Conv_Ovf_U1_Un.Value => CilValue.FromPrimitive((int)checked((byte)unsigned)),
            var v when v == OpCodes.Conv_Ovf_I2_Un.Value => CilValue.FromPrimitive((int)checked((short)unsigned)),
            var v when v == OpCodes.Conv_Ovf_U2_Un.Value => CilValue.FromPrimitive((int)checked((ushort)unsigned)),
            var v when v == OpCodes.Conv_Ovf_I4_Un.Value => CilValue.FromPrimitive(checked((int)unsigned)),
            var v when v == OpCodes.Conv_Ovf_U4_Un.Value => CilValue.FromPrimitive(checked((uint)unsigned)),
            var v when v == OpCodes.Conv_Ovf_I8_Un.Value => CilValue.FromPrimitive(checked((long)unsigned)),
            var v when v == OpCodes.Conv_Ovf_U8_Un.Value => CilValue.FromPrimitive(unsigned),
            var v when v == OpCodes.Conv_Ovf_I_Un.Value => CilValue.FromPrimitive(CilValue.PointerSize == 8 ? checked((long)unsigned) : checked((int)unsigned)),
            var v when v == OpCodes.Conv_Ovf_U_Un.Value => CilValue.FromPrimitive(CilValue.PointerSize == 8 ? unsigned : checked((uint)unsigned)),
            var v when v == OpCodes.Conv_Ovf_I.Value => CilValue.FromPrimitive(CilValue.PointerSize == 8 ? signed : checked((int)signed)),
            var v when v == OpCodes.Conv_Ovf_U.Value => CilValue.FromPrimitive(CilValue.PointerSize == 8 ? checked((ulong)signed) : checked((uint)signed)),
            _ => throw new NotSupportedException($"Conversion opcode '{op.Name}' is not supported yet")
        };
    }

    private static bool IsComparisonBranch(OpCode op) => op.FlowControl == FlowControl.Cond_Branch && op != OpCodes.Brtrue && op != OpCodes.Brtrue_S && op != OpCodes.Brfalse && op != OpCodes.Brfalse_S && op != OpCodes.Switch;

    private static bool EvaluateBranch(OpCode op, CilValue left, CilValue right)
    {
        if (op == OpCodes.Beq || op == OpCodes.Beq_S)
        {
            return Compare(OpCodes.Ceq, left, right);
        }

        if (op == OpCodes.Bne_Un || op == OpCodes.Bne_Un_S)
        {
            return !Compare(OpCodes.Ceq, left, right);
        }

        if (op == OpCodes.Bgt || op == OpCodes.Bgt_S)
        {
            return Compare(OpCodes.Cgt, left, right);
        }

        if (op == OpCodes.Bgt_Un || op == OpCodes.Bgt_Un_S)
        {
            return Compare(OpCodes.Cgt_Un, left, right);
        }

        if (op == OpCodes.Blt || op == OpCodes.Blt_S)
        {
            return Compare(OpCodes.Clt, left, right);
        }

        if (op == OpCodes.Blt_Un || op == OpCodes.Blt_Un_S)
        {
            return Compare(OpCodes.Clt_Un, left, right);
        }

        if (op == OpCodes.Bge || op == OpCodes.Bge_S)
        {
            return !Compare(OpCodes.Clt, left, right);
        }

        if (op == OpCodes.Bge_Un || op == OpCodes.Bge_Un_S)
        {
            return !Compare(OpCodes.Clt_Un, left, right);
        }

        if (op == OpCodes.Ble || op == OpCodes.Ble_S)
        {
            return !Compare(OpCodes.Cgt, left, right);
        }

        if (op == OpCodes.Ble_Un || op == OpCodes.Ble_Un_S)
        {
            return !Compare(OpCodes.Cgt_Un, left, right);
        }

        throw new NotSupportedException($"Conditional branch '{op.Name}' is not supported");
    }

    private static int GetTargetIndex(IReadOnlyDictionary<int, int> offsets, CilInstruction instruction) => offsets[(int)instruction.Operand!];
}
