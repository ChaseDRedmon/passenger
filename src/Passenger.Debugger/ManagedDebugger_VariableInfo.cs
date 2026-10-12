using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Passenger.Debugger.Interop;
using Passenger.Debugger.Models.Response;
using Passenger.Debugger.PresentationHintModels;
using ZLinq;

namespace Passenger.Debugger;

public partial class ManagedDebugger
{
    private async Task AddLocalVariables(ModuleInfo module, ICorDebugFunction corDebugFunction, List<VariableInfo> result, ThreadId threadId, FrameStackDepth stackDepth, ICorDebugValue? classContainingHoistedLocalsValue)
    {
        if (classContainingHoistedLocalsValue is not null)
        {
            // If we have a classContainingHoistedLocalsValue, it means captured variables from the outer scope are stored
            // as fields on the compiler-generated closure class - read those first, walking the full closure chain
            // so that variables captured from enclosing lambdas are also included.
            // We do NOT return here: non-captured locals declared inside the lambda body are still plain IL locals
            // on the lambda method frame and must also be read below.
            await AddClosureChainMembers(classContainingHoistedLocalsValue, threadId, stackDepth, result);
        }
        ICorDebugILFrame corDebugIlFrame = GetIlFrameForThreadIdAndStackDepth(threadId, stackDepth);
        if (corDebugIlFrame.LocalVariables.Length is 0)
        {
            return;
        }

        int currentIlOffset = corDebugIlFrame.IP.pnOffset;
        foreach ((int index, ICorDebugValue? localVariableCorDebugValue) in corDebugIlFrame.LocalVariables.Index())
        {
            string? localVariableName = module.MetadataReader.GetLocalVariableName(corDebugFunction.Token, index, currentIlOffset);
            if (localVariableName is null)
            {
                continue; // Compiler generated locals will not be found. E.g. DefaultInterpolatedStringHandler
            }

            if (IsLambdaDisplayClass(localVariableCorDebugValue))
            {
                await AddClosureChainMembers(localVariableCorDebugValue, threadId, stackDepth, result);
                continue;
            }
            await WithFailureHandling(result, localVariableName, async () =>
            {
                (string? friendlyTypeName, string? value, ICorDebugValue? debuggerProxyInstance, bool resultIsError) = await GetValueForCorDebugValueAsync(localVariableCorDebugValue, threadId, stackDepth, true);
                VariablePresentationHint? variablePresentationHint = resultIsError ? new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation } : null;
                result.Add(new VariableInfo
                {
                    Name = localVariableName,
                    Value = value,
                    Type = friendlyTypeName,
                    PresentationHint = variablePresentationHint,
                    VariablesReference = GetVariablesReference(localVariableCorDebugValue, friendlyTypeName, threadId, stackDepth, debuggerProxyInstance)
                });
            });
        }
    }

    private static bool IsLambdaDisplayClass(ICorDebugValue value)
    {
        ICorDebugType type = value.ExactType;
        if (type.Type is not CorElementType.CLASS)
        {
            return false;
        }

        IMetaDataImport metadataImport = type.Class.Module.GetMetaDataInterface<IMetaDataImport>();
        string typeName = metadataImport.GetTypeDefProps(type.Class.Token).szTypeDef;
        return GeneratedNameParser.GetKind(typeName) is GeneratedNameKind.LambdaDisplayClass;
    }

    /// Walks the compiler-generated closure chain starting at <paramref name="closureValue"/>,
    /// calling AddMembers on each closure class. Parent closures are linked via a field of
    /// kind <see cref="GeneratedNameKind.DisplayClassLocalOrField"/> (e.g. "&lt;&gt;8__1").
    private async Task AddClosureChainMembers(ICorDebugValue closureValue, ThreadId threadId, FrameStackDepth stackDepth, List<VariableInfo> result)
    {
        // A captured parameter or local is in the closure and in its own slot too, which the method no
        // longer updates: the closure's copy is the live one, and takes the other's place (upstream #58).
        List<VariableInfo> members = [];
        await AddMembers(closureValue, closureValue.ExactType, threadId, stackDepth, members);
        foreach (VariableInfo member in members)
        {
            int existing = result.FindIndex(v => v.Name == member.Name);
            if (existing >= 0)
            {
                result[existing] = member;
            }
            else
            {
                result.Add(member);
            }
        }

        // Follow the DisplayClassLocalOrField link to the parent closure, if any
        ICorDebugObjectValue objectValue = closureValue.UnwrapDebugValueToObject();
        IMetaDataImport metadataImport = objectValue.Class.Module.GetMetaDataInterface<IMetaDataImport>();
        IEnumerable<mdFieldDef> fields = metadataImport.EnumFields(objectValue.Class.Token);
        foreach (mdFieldDef field in fields)
        {
            (string szField, mdTypeDef pClass, CorFieldAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) fieldProps = metadataImport.GetFieldProps(field);
            if (GeneratedNameParser.GetKind(fieldProps.szField) is GeneratedNameKind.DisplayClassLocalOrField)
            {
                ICorDebugValue parentClosureValue = objectValue.GetFieldValue(objectValue.Class, field);
                await AddClosureChainMembers(parentClosureValue, threadId, stackDepth, result);
                break; // only one parent link per closure class
            }
        }
    }

    /// Returns classContainingHoistedLocalsValue if applicable
    private async Task<ICorDebugValue?> AddArguments(ModuleInfo module, ICorDebugFunction corDebugFunction, List<VariableInfo> result, ThreadId threadId, FrameStackDepth stackDepth)
    {
        ICorDebugILFrame corDebugIlFrame = GetIlFrameForThreadIdAndStackDepth(threadId, stackDepth);
        ICorDebugValue[] arguments = corDebugIlFrame.Arguments;
        if (arguments.Length is 0)
        {
            return null;
        }

        IMetaDataImport metadataImport = module.Module.GetMetaDataInterface<IMetaDataImport>();

        // localsScope.Frame.Arguments includes the implicit "this" parameter for instance methods,
        // but GetParamForMethodIndex does NOT include it - it is named by convention
        // so we need to check the method attributes to see if it's static or instance, to conditionally handle "this"
        (string szMethod, mdTypeDef pClass, CorMethodAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, int pulCodeRVA, int pdwImplFlags) methodProps = metadataImport!.GetMethodProps(corDebugFunction.Token);
        bool isStatic = methodProps.pdwAttr.IsMdStatic();
        ICorDebugValue? classContainingHoistedLocalsValue = null;
        if (isStatic is false)
        {
            string methodName = methodProps.szMethod;
            ICorDebugValue? implicitThisValue = arguments[0];
            if (methodName is "MoveNext" || methodName.Contains(">b")) // async or lambda
            {
                string containingClassName = metadataImport.GetTypeDefProps(corDebugFunction.Class.Token).szTypeDef;
                GeneratedNameKind classGeneratedNameKind = GeneratedNameParser.GetKind(containingClassName);
                if (classGeneratedNameKind is GeneratedNameKind.StateMachineType or GeneratedNameKind.LambdaDisplayClass)
                {
                    // In this case, 'this' is actually a compiler generated class that contains a field pointing to the 'this' that the user expects
                    // We are also going to use this to decide that the containing class contains hoisted locals, so we should return it
                    classContainingHoistedLocalsValue = implicitThisValue;
                    // This may return null, as even though we have checked isStatic is true, that is for the MoveNext method - the user's method may be static, and therefore would have no 'this' proxy field
                    implicitThisValue = GetAsyncOrLambdaProxyFieldValue(implicitThisValue, metadataImport);
                }
            }
            if (implicitThisValue is not null)
            {
                await WithFailureHandling(result, "this", async () =>
                {
                    (string? friendlyTypeName, string? value, ICorDebugValue? debuggerProxyInstance, bool resultIsError) = await GetValueForCorDebugValueAsync(implicitThisValue, threadId, stackDepth, true);
                    VariablePresentationHint? variablePresentationHint = resultIsError ? new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation } : null;
                    result.Add(new VariableInfo
                    {
                        Name = "this", // Hardcoded - 'this' has no metadata
                        Value = value,
                        Type = friendlyTypeName,
                        PresentationHint = variablePresentationHint,
                        VariablesReference = GetVariablesReference(implicitThisValue, friendlyTypeName, threadId, stackDepth, debuggerProxyInstance)
                    });
                });
            }
        }
        int skipCount = isStatic ? 0 : 1; // Skip 'this' for instance methods, as we already handled it
        foreach ((int index, ICorDebugValue? argumentCorDebugValue) in arguments.Skip(skipCount).Index())
        {
            // index 0 is the return value, so we add 1 to get to the arguments
            // GetParamForMethodIndex does not include the instance 'this' parameter
            mdParamDef paramDef = metadataImport!.GetParamForMethodIndex(corDebugFunction.Token, index + 1);
            (string szName, mdMethodDef pmd, int pulSequence, CorParamAttr pdwAttr, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) paramProps = metadataImport.GetParamProps(paramDef);
            string? argumentName = paramProps.szName;
            if (argumentName is null)
            {
                continue;
            }

            await WithFailureHandling(result, argumentName, async () =>
            {
                (string? friendlyTypeName, string? value, ICorDebugValue? debuggerProxyInstance, bool resultIsError) = await GetValueForCorDebugValueAsync(argumentCorDebugValue, threadId, stackDepth, true);
                VariablePresentationHint? variablePresentationHint = resultIsError ? new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation } : null;
                result.Add(new VariableInfo
                {
                    Name = argumentName,
                    Value = value,
                    Type = friendlyTypeName,
                    PresentationHint = variablePresentationHint,
                    VariablesReference = GetVariablesReference(argumentCorDebugValue, friendlyTypeName, threadId, stackDepth, debuggerProxyInstance)
                });
            });
        }
        return classContainingHoistedLocalsValue;
    }

    private async Task AddCurrentException(List<VariableInfo> result, ThreadId threadId, FrameStackDepth stackDepth)
    {
        ICorDebugThread? thread = _threads.GetValueOrDefault(threadId.Value)?.Thread;
        ArgumentNullException.ThrowIfNull(thread);
        thread.TryGetCurrentException(out ICorDebugValue? currentException);
        if (currentException is not null)
        {
            await WithFailureHandling(result, "$exception", async () =>
            {
                (string? friendlyTypeName, string? value, ICorDebugValue? debuggerProxyInstance, bool resultIsError) = await GetValueForCorDebugValueAsync(currentException, threadId, stackDepth, true);
                VariablePresentationHint? presentationHint = resultIsError ? new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation } : null;
                result.Add(new VariableInfo
                {
                    Name = "$exception",
                    Value = value,
                    Type = friendlyTypeName,
                    PresentationHint = presentationHint,
                    VariablesReference = GetVariablesReference(currentException, friendlyTypeName, threadId, stackDepth, debuggerProxyInstance)
                });
            });
        }
    }

    private int GetVariablesReference(ICorDebugValue corDebugValue, string friendlyTypeName, ThreadId threadId, FrameStackDepth stackDepth, ICorDebugValue? debuggerProxyInstance)
    {
        ICorDebugValue unwrappedDebugValue = corDebugValue.UnwrapDebugValue();
        if (unwrappedDebugValue is ICorDebugArrayValue arrayValue)
        {
            if (arrayValue.Count is 0)
            {
                return 0;
            }

            return GenerateUniqueVariableReference(corDebugValue, threadId, stackDepth, debuggerProxyInstance);
        }
        else if (unwrappedDebugValue is ICorDebugObjectValue objectValue)
        {
            if (TryGetNullableUnderlyingValue(corDebugValue, out _, out ICorDebugValue? underlyingValueOrNull))
            {
                if (underlyingValueOrNull is null)
                {
                    return 0;
                }

                if (underlyingValueOrNull.UnwrapDebugValue() is not ICorDebugObjectValue objValue)
                {
                    return 0; // underlying value is primitive
                }

                objectValue = objValue;
            }

            CorElementType type = objectValue.Type;
            // Strings are objects but typically displayed as primitives
            if (type is CorElementType.STRING)
            {
                return 0;
            }
            // Decimal is a struct but should be treated as a primitive
            if (friendlyTypeName is "decimal" or "decimal?")
            {
                return 0;
            }
            // a boxed primitive is CorElementType.VALUETYPE but should be displayed as a primitive. They can never be nullable.
            if (friendlyTypeName is "bool" or "byte" or "sbyte" or "char" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "float" or "double" or "nint" or "nuint")
            {
                return 0;
            }

            if (type is CorElementType.CLASS or CorElementType.VALUETYPE or CorElementType.SZARRAY or CorElementType.ARRAY)
            {
                return GenerateUniqueVariableReference(corDebugValue, threadId, stackDepth, debuggerProxyInstance);
            }
        }
        return 0;
    }

    private int GenerateUniqueVariableReference(ICorDebugValue value, ThreadId threadId, FrameStackDepth stackDepth, ICorDebugValue? debuggerProxyInstance)
    {
        VariablesReference variablesReference = new VariablesReference(StoredReferenceKind.StackVariable, value, threadId, stackDepth, debuggerProxyInstance);
        int reference = _variableManager.CreateReference(variablesReference);
        return reference;
    }

    private async Task AddMembersAndStaticPseudoVariable(ICorDebugValue corDebugValue, ICorDebugType corDebugType, ThreadId threadId, FrameStackDepth stackDepth, List<VariableInfo> result, bool includeNonPublicMembers = true)
    {
        bool requiresStaticPseudoVariable = await AddMembers(corDebugValue, corDebugType, threadId, stackDepth, result, includeNonPublicMembers);
        if (requiresStaticPseudoVariable)
        {
            VariableInfo variableInfo = new VariableInfo
            {
                Name = "Static members",
                Value = "",
                Type = "",
                PresentationHint = new VariablePresentationHint { Kind = PresentationHintKind.Class },
                VariablesReference = _variableManager.CreateReference(new VariablesReference(StoredReferenceKind.StaticClassVariable, corDebugValue, threadId, stackDepth, null))
            };
            result.Add(variableInfo);
        }
    }

    private void AddEnumerablePseudoVariables(VariablesReference variablesReference, List<VariableInfo> result)
    {
        result.Add(new VariableInfo
        {
            Name = "Raw View",
            Value = "",
            Type = "",
            PresentationHint = new VariablePresentationHint { Kind = PresentationHintKind.Class },
            VariablesReference = _variableManager.CreateReference(variablesReference with { ReferenceKind = StoredReferenceKind.RawView })
        });
        result.Add(new VariableInfo
        {
            Name = "Results",
            Value = "Expanding will force enumeration of the object",
            Type = "",
            PresentationHint = new VariablePresentationHint { Kind = PresentationHintKind.Class },
            VariablesReference = _variableManager.CreateReference(variablesReference with { ReferenceKind = StoredReferenceKind.EnumerableResults })
        });
    }

    private bool IsEnumerable(ICorDebugType type)
    {
        for (ICorDebugType? currentType = type; currentType is not null; currentType = currentType.Base)
        {
            IMetaDataImport metadataReader = currentType.Class.Module.GetMetaDataInterface<IMetaDataImport>();
            foreach (mdInterfaceImpl interfaceImpl in metadataReader.EnumInterfaceImpls(currentType.Class.Token))
            {
                mdToken interfaceToken = metadataReader.GetInterfaceImplProps(interfaceImpl).ptkIface;
                Handle interfaceHandle = MetadataTokens.Handle(checked((int)interfaceToken.Value));
                MetadataReader peMetadataReader = _modules[currentType.Class.Module.BaseAddress].MetadataReader.PeMetadataReader;
                string? interfaceName = interfaceHandle.Kind switch
                {
                    HandleKind.TypeDefinition => FunctionBreakpointSignatureTypeProvider.GetTypeName(peMetadataReader, (TypeDefinitionHandle)interfaceHandle),
                    HandleKind.TypeReference => FunctionBreakpointSignatureTypeProvider.GetTypeName(peMetadataReader, (TypeReferenceHandle)interfaceHandle),
                    HandleKind.TypeSpecification => peMetadataReader.GetTypeSpecification((TypeSpecificationHandle)interfaceHandle).DecodeSignature(new FunctionBreakpointSignatureTypeProvider(), null),
                    _ => null
                };
                if (interfaceName is "System.Collections.IEnumerable" || interfaceName?.StartsWith("System.Collections.Generic.IEnumerable`1<", StringComparison.Ordinal) is true)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private async Task AddEnumerableResults(VariablesReference variablesReference, List<VariableInfo> result)
    {
        ModuleInfo? linqModule = _modules.Values.FirstOrDefault(module => module.ModuleName is "System.Linq.dll");
        if (linqModule is null)
        {
            throw new InvalidOperationException("System.Linq is not loaded");
        }

        ICorDebugValue proxyInstance = await CreateDebuggerProxyInstance(variablesReference.ObjectValue!, variablesReference.ThreadId, linqModule.Module, "System.Linq.SystemCore_EnumerableDebugView", []);
        try
        {
            ICorDebugObjectValue proxyObject = proxyInstance.UnwrapDebugValueToObject();
            await AddMembers(proxyInstance, proxyObject.ExactType, variablesReference.ThreadId, variablesReference.FrameStackDepth, result, false);
        }
        finally
        {
            if (proxyInstance is ICorDebugHandleValue handle)
            {
                handle.TryDispose();
            }
        }
    }

    /// Returns a bool indicating if a Static Members pseudo variable is required
    private async Task<bool> AddMembers(ICorDebugValue corDebugValue, ICorDebugType corDebugType, ThreadId threadId, FrameStackDepth stackDepth, List<VariableInfo> result, bool includeNonPublicMembers = true)
    {
        bool hasStaticMembers = false;
        ICorDebugClass corDebugClass = corDebugType.Class;
        ICorDebugModule module = corDebugClass.Module;
        mdTypeDef mdTypeDef = corDebugClass.Token;
        IMetaDataImport metadataImport = module.GetMetaDataInterface<IMetaDataImport>();
        IEnumerable<mdFieldDef> mdFieldDefs = includeNonPublicMembers ? metadataImport.EnumFields(mdTypeDef) : metadataImport.EnumFields(mdTypeDef).AsValueEnumerable().Where(s => s.IsPublic(metadataImport)).ToArray();
        IEnumerable<mdProperty> mdProperties = includeNonPublicMembers ? metadataImport.EnumProperties(mdTypeDef) : metadataImport.EnumProperties(mdTypeDef).AsValueEnumerable().Where(s => s.IsPublic(metadataImport)).ToArray();
        mdFieldDef[] staticFieldDefs = mdFieldDefs.AsValueEnumerable().Where(s => s.IsStatic(metadataImport)).ToArray();
        mdFieldDef[] nonStaticFieldDefs = mdFieldDefs.AsValueEnumerable().Except(staticFieldDefs).ToArray();
        mdProperty[] staticProperties = mdProperties.AsValueEnumerable().Where(p => p.IsStatic(metadataImport)).ToArray();
        mdProperty[] nonStaticProperties = mdProperties.AsValueEnumerable().Except(staticProperties).ToArray();
        if (staticFieldDefs.Length > 0 || staticProperties.Length > 0)
        {
            hasStaticMembers = true;
        }

        await AddFields(nonStaticFieldDefs, metadataImport, corDebugType, corDebugValue, result, threadId, stackDepth);
        // We need to pass the un-unwrapped reference value here, as we need to invoke CallParameterizedFunction with the correct parameters
        await AddProperties(nonStaticProperties, metadataImport, corDebugClass, threadId, stackDepth, corDebugValue, result);

        // Handle members on base types recursively
        ICorDebugType? baseType = corDebugType.Base;
        if (baseType is null)
        {
            return hasStaticMembers;
        }

        string baseTypeName = GetCorDebugTypeFriendlyName(baseType);
        if (baseTypeName is "System.Object" or "System.ValueType" or "System.Enum")
        {
            return hasStaticMembers;
        }

        return hasStaticMembers | await AddMembers(corDebugValue, baseType, threadId, stackDepth, result);
    }

    private async Task AddStaticMembers(ICorDebugValue corDebugValue, ICorDebugType corDebugType, ThreadId threadId, FrameStackDepth stackDepth, List<VariableInfo> result)
    {
        ICorDebugClass corDebugClass = corDebugType.Class;
        ICorDebugModule module = corDebugClass.Module;
        mdTypeDef mdTypeDef = corDebugClass.Token;
        IMetaDataImport metadataImport = module.GetMetaDataInterface<IMetaDataImport>();
        mdFieldDef[] staticFieldDefs = metadataImport.EnumFields(mdTypeDef).AsValueEnumerable().Where(s => s.IsStatic(metadataImport)).ToArray();
        mdProperty[] staticProperties = metadataImport.EnumProperties(mdTypeDef).AsValueEnumerable().Where(s => s.IsStatic(metadataImport)).ToArray();

        await AddFields(staticFieldDefs, metadataImport, corDebugType, corDebugValue, result, threadId, stackDepth);
        // We need to pass the un-unwrapped reference value here, as we need to invoke CallParameterizedFunction with the correct parameters
        await AddProperties(staticProperties, metadataImport, corDebugClass, threadId, stackDepth, corDebugValue, result);

        // Handle members on base types recursively
        ICorDebugType? baseType = corDebugType.Base;
        if (baseType is null)
        {
            return;
        }

        string baseTypeName = GetCorDebugTypeFriendlyName(baseType);
        if (baseTypeName is "System.Object" or "System.ValueType" or "System.Enum")
        {
            return;
        }

        await AddStaticMembers(corDebugValue, baseType, threadId, stackDepth, result);
    }

    private async Task AddFields(mdFieldDef[] mdFieldDefs, IMetaDataImport metadataImport, ICorDebugType corDebugType, ICorDebugValue corDebugValue, List<VariableInfo> result, ThreadId threadId, FrameStackDepth stackDepth)
    {
        ICorDebugClass corDebugClass = corDebugType.Class;
        foreach (mdFieldDef mdFieldDef in mdFieldDefs)
        {
            (string szField, mdTypeDef pClass, CorFieldAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, CorElementType pdwCPlusTypeFlag, nint ppValue, int pcchValue) fieldProps = metadataImport.GetFieldProps(mdFieldDef);
            string? fieldName = fieldProps.szField;
            if (fieldName is null)
            {
                continue;
            }

            await WithFailureHandling(result, fieldName, async () =>
            {
                GeneratedNameParser.TryParseGeneratedName(fieldName, out GeneratedNameKind generatedNameKind, out int openBracketOffset, out int closeBracketOffset);
                if (generatedNameKind is GeneratedNameKind.HoistedLocalField)
                {
                    // e.g. we are in an async method - local variables in the user's method are stored in fields on a generated class, e.g. "<intVar>5__1"
                    // we want to extract "intVar"
                    ReadOnlySpan<char> originalLocalVariableName = fieldName.AsSpan()[(openBracketOffset + 1)..closeBracketOffset];
                    fieldName = originalLocalVariableName.ToString();
                }
                else if (generatedNameKind is not GeneratedNameKind.None)
                {
                    return;
                }
                bool isStatic = fieldProps.pdwAttr.IsFdStatic();
                bool isLiteral = fieldProps.pdwAttr.IsFdLiteral();
                bool debuggerBrowsableRootHidden = false;
                bool hasDebuggerBrowsableAttribute = metadataImport.TryGetCustomAttributeByName(mdFieldDef, "System.Diagnostics.DebuggerBrowsableAttribute", out nint debuggerBrowsableAttributePointer, out uint debuggerBrowsableAttributeSize) is Cor.S_OK;
                if (hasDebuggerBrowsableAttribute)
                {
                    // https://github.com/Samsung/netcoredbg/blob/6476bc00c2beaab9255c750235a68de3a3d0cfae/src/debugger/evaluator.cpp#L913
                    DebuggerBrowsableState debuggerBrowsableState = (DebuggerBrowsableState)GetDebuggerBrowsableCustomAttributeResultInt(debuggerBrowsableAttributePointer, debuggerBrowsableAttributeSize);
                    if (debuggerBrowsableState == DebuggerBrowsableState.Never)
                    {
                        return; // I may not end up doing this, as it would be ideal to still be able to hover the variable in the editor and see the value
                    }

                    if (debuggerBrowsableState == DebuggerBrowsableState.RootHidden)
                    {
                        debuggerBrowsableRootHidden = true;
                    }
                }
                if (isLiteral)
                {
                    object? literalValue = GetLiteralValue(fieldProps.ppValue, fieldProps.pdwCPlusTypeFlag, fieldProps.pcchValue);
                    (string FriendlyName, ModuleInfo Module, EntityHandle Handle)? fieldType = GetFieldType(corDebugClass, mdFieldDef);
                    string? fieldTypeName = fieldType?.FriendlyName ?? GetFriendlyTypeName(fieldProps.pdwCPlusTypeFlag);
                    string literalValueFormatted = literalValue switch
                    {
                        null => "null",
                        bool b => b.ToString().ToLowerInvariant(),
                        char c => $"{(int)c} '{c}'",
                        string str => SymbolDisplay.FormatLiteral(str, quote: true),
                        _ => literalValue.ToString()!
                    };
                    if (fieldType is { } declaredType && TryGetEnumDisplayValue(declaredType, literalValueFormatted, out string? enumDisplayValue))
                    {
                        literalValueFormatted = enumDisplayValue;
                    }
                    VariableInfo literalVariableInfo = new VariableInfo
                    {
                        Name = fieldName,
                        Value = literalValueFormatted,
                        Type = fieldTypeName,
                        VariablesReference = 0
                    };
                    result.Add(literalVariableInfo);
                    return;
                }

                ICorDebugObjectValue objectValue = corDebugValue.UnwrapDebugValueToObject();
                ICorDebugValue fieldCorDebugValue = isStatic ? await GetStaticFieldValueAsync(corDebugType, mdFieldDef, threadId, stackDepth) : objectValue.GetFieldValue(corDebugClass, mdFieldDef);
                if (debuggerBrowsableRootHidden)
                {
                    ICorDebugValue unwrappedDebugValue = fieldCorDebugValue.UnwrapDebugValue();
                    if (unwrappedDebugValue is ICorDebugArrayValue arrayValue)
                    {
                        await AddArrayElements(arrayValue, threadId, stackDepth, result, referenceValue: fieldCorDebugValue);
                        return;
                    }
                }
                (string? friendlyTypeName, string? value, ICorDebugValue? debuggerProxyInstance, bool resultIsError) = await GetValueForCorDebugValueAsync(fieldCorDebugValue, threadId, stackDepth, true);
                VariablePresentationHint? variablePresentationHint = resultIsError ? new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation } : null;
                VariableInfo variableInfo = new VariableInfo
                {
                    Name = fieldName,
                    Value = value,
                    Type = friendlyTypeName,
                    PresentationHint = variablePresentationHint,
                    VariablesReference = GetVariablesReference(fieldCorDebugValue, friendlyTypeName, threadId, stackDepth, debuggerProxyInstance)
                };
                result.Add(variableInfo);
            });
        }
    }

    private (string FriendlyName, ModuleInfo Module, EntityHandle Handle)? GetFieldType(ICorDebugClass corDebugClass, mdFieldDef mdFieldDef)
    {
        if (_modules.TryGetValue(corDebugClass.Module.BaseAddress, out ModuleInfo? moduleInfo) is false)
        {
            return null;
        }

        Handle handle = MetadataTokens.Handle(mdFieldDef);
        if (handle.Kind is not HandleKind.FieldDefinition)
        {
            return null;
        }

        MetadataReader reader = moduleInfo.MetadataReader.PeMetadataReader;
        FieldDefinition field = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
        string typeName = field.DecodeSignature(new FunctionBreakpointSignatureTypeProvider(), null);
        EntityHandle typeHandle = field.DecodeSignature(FieldTypeHandleProvider.Instance, null);
        return (ClassNameToMaybeLanguageAlias(typeName), moduleInfo, typeHandle);
    }

    internal class EvalException(string message) : Exception(message);
    private async Task AddProperties(mdProperty[] mdProperties, IMetaDataImport metadataImport, ICorDebugClass corDebugClass, ThreadId threadId, FrameStackDepth stackDepth, ICorDebugValue corDebugValue, List<VariableInfo> result)
    {
        foreach (mdProperty mdProperty in mdProperties)
        {
            (string szProperty, mdTypeDef pClass, CorPropertyAttr pdwPropFlags, nint ppvSig, int pbSig, CorElementType pdwCPlusTypeFlag, nint ppDefaultValue, int pcchDefaultValue, mdMethodDef pmdSetter, mdMethodDef pmdGetter, mdMethodDef rmdOtherMethod, int pcOtherMethod) propertyProps = metadataImport.GetPropertyProps(mdProperty);
            string? propertyName = propertyProps.szProperty;
            if (propertyName is null)
            {
                continue;
            }

            await WithFailureHandling(result, propertyName, async () =>
            {
                ICorDebugILFrame variablesReferenceIlFrame = GetIlFrameForThreadIdAndStackDepth(threadId, stackDepth);

                // Get the get method for the property
                mdMethodDef getMethodDef = propertyProps.pmdGetter;
                if (getMethodDef == 0)
                {
                    return; // No get method
                }

                // Get method attributes to check if it's static
                (string szMethod, mdTypeDef pClass, CorMethodAttr pdwAttr, nint ppvSigBlob, int pcbSigBlob, int pulCodeRVA, int pdwImplFlags) getterMethodProps = metadataImport.GetMethodProps(getMethodDef);
                CorMethodAttr getterAttr = getterMethodProps.pdwAttr;

                bool isStatic = getterAttr.IsMdStatic();

                bool debuggerBrowsableRootHidden = false;
                bool hasDebuggerBrowsableAttribute = metadataImport.TryGetCustomAttributeByName(mdProperty, "System.Diagnostics.DebuggerBrowsableAttribute", out nint debuggerBrowsableAttributePointer, out uint debuggerBrowsableAttributeSize) is Cor.S_OK;
                if (hasDebuggerBrowsableAttribute)
                {
                    // https://github.com/Samsung/netcoredbg/blob/6476bc00c2beaab9255c750235a68de3a3d0cfae/src/debugger/evaluator.cpp#L913
                    DebuggerBrowsableState debuggerBrowsableState = (DebuggerBrowsableState)GetDebuggerBrowsableCustomAttributeResultInt(debuggerBrowsableAttributePointer, debuggerBrowsableAttributeSize);
                    if (debuggerBrowsableState == DebuggerBrowsableState.Never)
                    {
                        return; // I may not end up doing this, as it would be ideal to still be able to hover the variable in the editor and see the value
                    }

                    if (debuggerBrowsableState == DebuggerBrowsableState.RootHidden)
                    {
                        debuggerBrowsableRootHidden = true;
                    }
                }

                ICorDebugFunction getMethod = corDebugClass.Module.GetFunctionFromToken(getMethodDef);
                ICorDebugEval eval = variablesReferenceIlFrame.Chain.Thread.CreateEval();

                // May not be correct, will need further testing
                ICorDebugType parameterizedContainingType = corDebugValue.ExactType;

                ICorDebugType[] typeParameterTypes = parameterizedContainingType.TypeParameters;

                // For instance properties, pass the object; for static, pass nothing
                ICorDebugValue[] corDebugValues = isStatic ? [] : [corDebugValue];

                ICorDebugValue? returnValue = await eval.CallParameterizedFunctionAsync(ProcessRuntimeEventsUntilEvalEvent, EvalStatus, getMethod, typeParameterTypes.Length, typeParameterTypes, corDebugValues.Length, corDebugValues);

                if (returnValue is null)
                {
                    return;
                }

                bool retainReturnValue = false;
                try
                {
                    if (debuggerBrowsableRootHidden)
                    {
                        ICorDebugValue unwrappedDebugValue = returnValue.UnwrapDebugValue();
                        if (unwrappedDebugValue is ICorDebugArrayValue arrayValue)
                        {
                            retainReturnValue = await AddArrayElements(arrayValue, threadId, stackDepth, result, referenceValue: returnValue);
                            return;
                        }
                    }
                    (string? friendlyTypeName, string? value, ICorDebugValue? debuggerProxyInstance, bool resultIsError) = await GetValueForCorDebugValueAsync(returnValue, threadId, stackDepth, true);
                    VariablePresentationHint? variablePresentationHint = resultIsError ? new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation } : null;
                    int variablesReference = GetVariablesReference(returnValue, friendlyTypeName, threadId, stackDepth, debuggerProxyInstance);
                    VariableInfo variableInfo = new VariableInfo
                    {
                        Name = propertyName,
                        Value = value,
                        Type = friendlyTypeName,
                        PresentationHint = variablePresentationHint,
                        VariablesReference = variablesReference
                    };
                    retainReturnValue = variablesReference != 0;
                    result.Add(variableInfo);
                }
                finally
                {
                    if (!retainReturnValue && returnValue is ICorDebugHandleValue handle)
                    {
                        handle.TryDispose();
                    }
                }
            });
        }
    }

    private async Task<bool> AddArrayElements(ICorDebugArrayValue arrayValue, ThreadId threadId, FrameStackDepth stackDepth, List<VariableInfo> result, uint[]? indexPrefix = null, uint? startOffset = null, uint? count = null, ICorDebugValue? referenceValue = null)
    {
        int rank = arrayValue.Rank;
        indexPrefix ??= [];
        uint[] dimensions = arrayValue.GetDimensions(rank);
        uint[] baseIndices = arrayValue.HasBaseIndicies() ? arrayValue.GetBaseIndicies(rank) : new uint[rank];
        int currentDimension = indexPrefix.Length;
        uint currentDimensionStart = baseIndices[currentDimension];
        uint currentDimensionLength = dimensions[currentDimension];

        if (currentDimension < rank - 1)
        {
            for (uint offset = 0u; offset < currentDimensionLength; offset++)
            {
                uint[] indices = [.. indexPrefix, currentDimensionStart + offset];
                string name = $"[{string.Join(", ", indices)}, ...]";
                result.Add(new VariableInfo
                {
                    Name = name,
                    Value = "",
                    Type = "",
                    PresentationHint = new VariablePresentationHint { Kind = PresentationHintKind.Class },
                    VariablesReference = _variableManager.CreateReference(new VariablesReference(StoredReferenceKind.ArrayRange, referenceValue ?? arrayValue, threadId, stackDepth, null, indices))
                });
            }
            return true;
        }
        if (count is null && currentDimensionLength > 100)
        {
            for (uint rangeStart = 0u; rangeStart < currentDimensionLength; rangeStart += 100)
            {
                uint rangeCount = Math.Min(100u, currentDimensionLength - rangeStart);
                uint firstIndex = currentDimensionStart + rangeStart;
                uint lastIndex = firstIndex + rangeCount - 1;
                string name = indexPrefix.Length is 0
                    ? $"[{firstIndex}..{lastIndex}]"
                    : $"[{string.Join(", ", indexPrefix)}, {firstIndex}..{lastIndex}]";
                result.Add(new VariableInfo
                {
                    Name = name,
                    Value = "",
                    Type = "",
                    PresentationHint = new VariablePresentationHint { Kind = PresentationHintKind.Class },
                    VariablesReference = _variableManager.CreateReference(new VariablesReference(StoredReferenceKind.ArrayRange, referenceValue ?? arrayValue, threadId, stackDepth, null, indexPrefix, rangeStart, rangeCount))
                });
            }
            return true;
        }

        // Get the elements first, as the CorDebugArrayValue arrayValue may get neutered during 'await GetValueForCorDebugValueAsync' below, if any evals are required
        uint elementCount = count ?? currentDimensionLength;
        (uint[] Indices, ICorDebugValue Element)[] elements = ValueEnumerable.Range(0, checked((int)elementCount))
            .Select(offset =>
            {
                uint[] indices = [.. indexPrefix, currentDimensionStart + (startOffset ?? 0) + checked((uint)offset)];
                return (Indices: indices, Element: arrayValue.GetElement(rank, indices));
            })
            .ToArray();
        foreach ((uint[]? indices, ICorDebugValue? element) in elements)
        {
            string name = $"[{string.Join(", ", indices)}]";
            await WithFailureHandling(result, name, async () =>
            {
                (string? friendlyTypeName, string? value, ICorDebugValue? debuggerProxyInstance, bool resultIsError) = await GetValueForCorDebugValueAsync(element, threadId, stackDepth, true);
                VariablePresentationHint? variablePresentationHint = resultIsError ? new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation } : new VariablePresentationHint { Kind = PresentationHintKind.Data };
                int variableReference = GetVariablesReference(element, friendlyTypeName, threadId, stackDepth, debuggerProxyInstance);
                VariableInfo variableInfo = new VariableInfo
                {
                    Name = name,
                    Type = friendlyTypeName,
                    Value = value,
                    PresentationHint = variablePresentationHint,
                    VariablesReference = variableReference
                };
                result.Add(variableInfo);
            });
        }
        return false;
    }

    private static async Task WithFailureHandling(List<VariableInfo> result, string fieldName, Func<Task> func)
    {
        try
        {
            await func();
        }
        catch (Exception ex)
        {
            result.Add(new VariableInfo
            {
                Name = fieldName,
                Value = ex.Message,
                Type = null,
                PresentationHint = new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation },
                VariablesReference = 0
            });
        }
    }
}
