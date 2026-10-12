namespace ICorDebugSharp;

/// <summary>
/// <para>Provides values that specify whether the debugger is able to access local variables or code added in profiler ReJIT instrumentation.</para>
/// </summary>
/// <remarks>
/// <para>A member of the <c>ILCodeKind</c> enumeration can be passed to the <see cref="M:ICorDebugSharp.ICorDebugILFrame4.TryEnumerateLocalVariablesEx(ICorDebugSharp.ILCodeKind,ICorDebugSharp.ICorDebugValueEnum@)">EnumerateLocalVariablesEx</see> and <see cref="M:ICorDebugSharp.ICorDebugILFrame4.TryGetLocalVariableEx(ICorDebugSharp.ILCodeKind,System.UInt32,ICorDebugSharp.ICorDebugValue@)">GetLocalVariableEx</see> methods to determine whether the debugger can access variables added in profiler ReJIT instrumentation, and to the <see cref="M:ICorDebugSharp.ICorDebugILFrame4.TryGetCodeEx(ICorDebugSharp.ILCodeKind,ICorDebugSharp.ICorDebugCode@)">GetCodeEx</see> method to determine whether the debugger can access instrumented IL.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/ilcodekind-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum ILCodeKind
{
	/// <summary>
	/// <para>The debugger does not have access to information from ReJIT instrumentation.</para>
	/// </summary>
	ILCODE_ORIGINAL_IL = 1,
	/// <summary>
	/// <para>The debugger has access to information from ReJIT instrumentation.</para>
	/// </summary>
	ILCODE_REJIT_IL
}
