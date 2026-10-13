using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Passenger.Debugger;

internal sealed record FunctionBreakpointPattern(string? TypeName, string MethodName, int? MethodArity, ImmutableArray<string>? Parameters)
{
    public static FunctionBreakpointPattern Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Function name cannot be empty.");
        }

        value = value.Trim();

        ImmutableArray<string>? parameters = null;
        int openingParenthesis = FindTopLevel(value, '(');
        if (openingParenthesis >= 0)
        {
            if (value[^1] != ')')
            {
                throw new ArgumentException("The function parameter list is not closed.");
            }

            string parameterText = value[(openingParenthesis + 1)..^1];
            parameters = string.IsNullOrWhiteSpace(parameterText)
                ? []
                : SplitTopLevel(parameterText, ',').Select(NormalizeType).ToImmutableArray();
            value = value[..openingParenthesis].Trim();
        }

        int lastDot = FindLastTopLevel(value, '.');
        string? typeName = lastDot < 0 ? null : NormalizeQualifiedName(value[..lastDot]);
        string method = lastDot < 0 ? value : value[(lastDot + 1)..];
        (string? methodName, int? methodArity) = NormalizeNameSegment(method);
        if (methodName.Length == 0)
        {
            throw new ArgumentException("Function name cannot be empty.");
        }

        return new FunctionBreakpointPattern(typeName, methodName, methodArity, parameters);
    }

    public bool MatchesType(string candidate) => TypeName is null ||
        candidate == TypeName || candidate.EndsWith('.' + TypeName, StringComparison.Ordinal);

    public bool MatchesParameters(ImmutableArray<string> candidate)
    {
        if (Parameters is null)
        {
            return true;
        }

        if (Parameters.Value.Length != candidate.Length)
        {
            return false;
        }

        return Parameters.Value.Zip(candidate).All(pair =>
            pair.First == pair.Second || pair.Second.EndsWith('.' + pair.First, StringComparison.Ordinal));
    }

    private static string NormalizeQualifiedName(string value) => string.Join('.',
        SplitTopLevel(value, '.').Select(segment =>
        {
            (string? name, int? arity) = NormalizeNameSegment(segment);
            return arity is null ? name : $"{name}`{arity}";
        }));

    private static (string Name, int? Arity) NormalizeNameSegment(string value)
    {
        value = value.Trim();
        int genericStart = FindTopLevel(value, '<');
        if (genericStart < 0)
        {
            return (value, null);
        }

        if (value[^1] != '>')
        {
            throw new ArgumentException($"Generic name '{value}' is not closed.");
        }

        List<string> arguments = SplitTopLevel(value[(genericStart + 1)..^1], ',');
        return (value[..genericStart].Trim(), arguments.Count);
    }

    private static string NormalizeType(string value)
    {
        value = string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
        if (value.EndsWith('?'))
        {
            return $"System.Nullable`1<{NormalizeType(value[..^1])}>";
        }

        int genericStart = FindTopLevel(value, '<');
        if (genericStart >= 0)
        {
            if (value[^1] != '>')
            {
                throw new ArgumentException($"Generic type '{value}' is not closed.");
            }

            List<string> arguments = SplitTopLevel(value[(genericStart + 1)..^1], ',').Select(NormalizeType).ToList();
            return $"{NormalizeSimpleType(value[..genericStart])}`{arguments.Count}<{string.Join(',', arguments)}>";
        }
        return NormalizeSimpleType(value);
    }

    private static string NormalizeSimpleType(string value) => value switch
    {
        "bool" => "System.Boolean",
        "byte" => "System.Byte",
        "sbyte" => "System.SByte",
        "char" => "System.Char",
        "short" => "System.Int16",
        "ushort" => "System.UInt16",
        "int" => "System.Int32",
        "uint" => "System.UInt32",
        "long" => "System.Int64",
        "ulong" => "System.UInt64",
        "float" => "System.Single",
        "double" => "System.Double",
        "decimal" => "System.Decimal",
        "string" => "System.String",
        "object" => "System.Object",
        "nint" => "System.IntPtr",
        "nuint" => "System.UIntPtr",
        "void" => "System.Void",
        _ => value
    };

    private static int FindTopLevel(string value, char target)
    {
        int depth = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == target && depth == 0)
            {
                return i;
            }

            if (value[i] == '<')
            {
                depth++;
            }
            else if (value[i] == '>')
            {
                depth--;
            }

            if (depth < 0)
            {
                throw new ArgumentException("Unexpected '>'.");
            }
        }
        return -1;
    }

    private static int FindLastTopLevel(string value, char target)
    {
        int depth = 0;
        for (int i = value.Length - 1; i >= 0; i--)
        {
            if (value[i] == '>')
            {
                depth++;
            }
            else if (value[i] == '<')
            {
                depth--;
            }
            else if (value[i] == target && depth == 0)
            {
                return i;
            }
        }
        return -1;
    }

    private static List<string> SplitTopLevel(string value, char separator)
    {
        List<string> result = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '<')
            {
                depth++;
            }
            else if (value[i] == '>')
            {
                depth--;
            }
            else if (value[i] == separator && depth == 0)
            {
                result.Add(value[start..i]);
                start = i + 1;
            }
        }
        result.Add(value[start..]);
        if (result.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A name or parameter is missing.");
        }

        return result;
    }
}

internal sealed class FunctionBreakpointSignatureTypeProvider : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => $"{genericType}<{string.Join(',', typeArguments)}>";
    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "System.Boolean",
        PrimitiveTypeCode.Byte => "System.Byte",
        PrimitiveTypeCode.SByte => "System.SByte",
        PrimitiveTypeCode.Char => "System.Char",
        PrimitiveTypeCode.Int16 => "System.Int16",
        PrimitiveTypeCode.UInt16 => "System.UInt16",
        PrimitiveTypeCode.Int32 => "System.Int32",
        PrimitiveTypeCode.UInt32 => "System.UInt32",
        PrimitiveTypeCode.Int64 => "System.Int64",
        PrimitiveTypeCode.UInt64 => "System.UInt64",
        PrimitiveTypeCode.Single => "System.Single",
        PrimitiveTypeCode.Double => "System.Double",
        PrimitiveTypeCode.IntPtr => "System.IntPtr",
        PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
        PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.String => "System.String",
        PrimitiveTypeCode.Void => "System.Void",
        PrimitiveTypeCode.TypedReference => "System.TypedReference",
        _ => typeCode.ToString()
    };
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => GetTypeName(reader, handle);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => GetTypeName(reader, handle);
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    internal static string GetTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        TypeDefinitionHandle declaringType = type.GetDeclaringType();
        return declaringType.IsNil
            ? JoinNamespace(reader.GetString(type.Namespace), name)
            : $"{GetTypeName(reader, declaringType)}.{name}";
    }

    internal static string GetTypeName(MetadataReader reader, TypeReferenceHandle handle)
    {
        TypeReference type = reader.GetTypeReference(handle);
        string name = reader.GetString(type.Name);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? $"{GetTypeName(reader, (TypeReferenceHandle)type.ResolutionScope)}.{name}"
            : JoinNamespace(reader.GetString(type.Namespace), name);
    }

    private static string JoinNamespace(string ns, string name) => string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
}

internal sealed record ResolvedFunctionBreakpoint(int MethodToken, ModuleMetadataReader.ResolvedBreakpoint Source);

internal static class FunctionBreakpointMetadataResolver
{
    public static IEnumerable<ResolvedFunctionBreakpoint> Resolve(ModuleMetadataReader metadataReader, FunctionBreakpointPattern pattern)
    {
        MetadataReader reader = metadataReader.PeMetadataReader;
        FunctionBreakpointSignatureTypeProvider provider = new FunctionBreakpointSignatureTypeProvider();
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            if (!pattern.MatchesType(FunctionBreakpointSignatureTypeProvider.GetTypeName(reader, typeHandle)))
            {
                continue;
            }

            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != pattern.MethodName)
                {
                    continue;
                }

                if (pattern.MethodArity is not null && method.GetGenericParameters().Count != pattern.MethodArity)
                {
                    continue;
                }

                if (!pattern.MatchesParameters(method.DecodeSignature(provider, null).ParameterTypes))
                {
                    continue;
                }

                int token = MetadataTokens.GetToken(methodHandle);
                ModuleMetadataReader.ResolvedBreakpoint? source = metadataReader.ResolveBreakpointAtMethodEntry(token);
                if (source is not null)
                {
                    yield return new ResolvedFunctionBreakpoint(token, source);
                }
            }
        }
    }
}
