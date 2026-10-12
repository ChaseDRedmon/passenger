using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ICorDebugSharp;

/// <summary>
/// <para>Provides methods that allow you to access the local variables and code in a stack frame of intermediate language (IL) code. A parameter specifies whether the debugger has access to variables and code added in profiler ReJIT instrumentation.</para>
/// </summary>
/// <remarks>
/// <para>These methods offer functionality in addition to that provided by the <see cref="M:ICorDebugSharp.ICorDebugILFrame.TryEnumerateLocalVariables(ICorDebugSharp.ICorDebugValueEnum@)">EnumerateLocalVariables</see>, <see cref="M:ICorDebugSharp.ICorDebugFrame.TryGetCode(ICorDebugSharp.ICorDebugCode@)">GetCode</see>, and <see cref="M:ICorDebugSharp.ICorDebugILFrame.TryGetLocalVariable(System.UInt32,ICorDebugSharp.ICorDebugValue@)">GetLocalVariable</see> methods. Each method includes a <c>flags</c> parameter that specifies whether additional local variables or code defined by a profiler's ReJIT request are visible.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugilframe4-interface">Microsoft Learn</see>.</para>
/// </remarks>
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("AD914A30-C6D1-4AC5-9C5E-577F3BAA8A45")]
public partial interface ICorDebugILFrame4
{
	/// <summary>
	/// <para>Gets an enumerator for the local variable in the frame, and optionally includes variables added in profiler ReJIT instrumentation.</para>
	/// </summary>
	/// <param name="flags">
	/// <para>An <see cref="T:ICorDebugSharp.ILCodeKind">ILCodeKind</see> enumeration member that specifies whether variables added in profiler ReJIT instrumentation are included in the frame.</para>
	/// </param>
	/// <param name="ppValueEnum">
	/// <para>A pointer to the address of an "ICorDebugValueEnum" object that is the enumerator for the local variables in this frame.</para>
	/// </param>
	/// <remarks>
	/// <para>This method is similar to the <see cref="M:ICorDebugSharp.ICorDebugILFrame.TryEnumerateLocalVariables(ICorDebugSharp.ICorDebugValueEnum@)">EnumerateLocalVariables</see> method, except that it optionally accesses variables added in profiler ReJIT instrumentation. Setting <c>flags</c> to <c>ILCODE_ORIGINAL_IL</c> is equivalent to calling <see cref="M:ICorDebugSharp.ICorDebugILFrame.TryEnumerateLocalVariables(ICorDebugSharp.ICorDebugValueEnum@)">ICorDebugILFrame::EnumerateLocalVariables</see>. Setting <c>flags</c> to <c>ILCODE_REJIT_IL</c> allows the debugger to access the local variables added in profiler ReJIT instrumentation. If the intermediate language (IL) is not instrumented, the enumeration is empty and the method returns <c>S_OK</c>.</para>
	/// <para>The enumerator may not include all of the local variables in the running method, since some of them may not be active.</para>
	/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugilframe4-enumeratelocalvariablesex-method">Microsoft Learn</see>.</para>
	/// </remarks>
	[PreserveSig]
	int TryEnumerateLocalVariablesEx(ILCodeKind flags, out ICorDebugValueEnum ppValueEnum);

	/// <summary>
	/// <para>Gets the value of the specified local variable in this intermediate language (IL) stack frame, and optionally accesses a variable added in profiler ReJIT instrumentation.</para>
	/// </summary>
	/// <param name="flags">
	/// <para>An <see cref="T:ICorDebugSharp.ILCodeKind">ILCodeKind</see> enumeration member that specifies whether a variable added in profiler ReJIT instrumentation is included in the frame.</para>
	/// </param>
	/// <param name="dwIndex">
	/// <para>The index of the local variable in the IL stack frame.</para>
	/// </param>
	/// <param name="ppValue">
	/// <para>A pointer to the address of an "ICorDebugValue" object that represents the retrieved value.</para>
	/// </param>
	/// <remarks>
	/// <para>This method is similar to the <see cref="M:ICorDebugSharp.ICorDebugILFrame.TryGetLocalVariable(System.UInt32,ICorDebugSharp.ICorDebugValue@)">GetLocalVariable</see> method, except that it optionally accesses a variable added in profiler ReJIT instrumentation. Calling this method with a <c>flags</c> value of <c>ILCODE_ORIGINAL_IL</c> is equivalent to calling <see cref="M:ICorDebugSharp.ICorDebugILFrame.TryGetLocalVariable(System.UInt32,ICorDebugSharp.ICorDebugValue@)">GetLocalVariable</see>; if the method is instrumented with additional local variables, those variables cannot be accessed. <c>ILCODE_REJIT_IL</c> allows the debugger to access the local variables added in profiler ReJIT instrumentation. If the IL is not instrumented, the method returns <c>E_INVALIDARG</c>.</para>
	/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugilframe4-getlocalvariableex-method">Microsoft Learn</see>.</para>
	/// </remarks>
	[PreserveSig]
	int TryGetLocalVariableEx(ILCodeKind flags, uint dwIndex, out ICorDebugValue ppValue);

	/// <summary>
	/// <para>Gets a pointer to the code that this stack frame is executing.</para>
	/// </summary>
	/// <param name="flags">
	/// <para>An <see cref="T:ICorDebugSharp.ILCodeKind">ILCodeKind</see> enumeration member that specifies whether the intermediate language (IL) defined by the profiler's ReJIT request is included in the frame.</para>
	/// </param>
	/// <param name="ppCode">
	/// <para>A pointer to the address of an "ICorDebugCode" object that represents the code that this stack frame is executing.</para>
	/// </param>
	/// <remarks>
	/// <para>This method is similar to the <see cref="M:ICorDebugSharp.ICorDebugFrame.TryGetCode(ICorDebugSharp.ICorDebugCode@)">ICorDebugFrame::GetCode</see> method, except that it optionally accesses code defined by the profiler's ReJIT request. Calling this method with a <c>flags</c> value of <c>ILCODE_ORIGINAL_IL</c> is equivalent to calling <see cref="M:ICorDebugSharp.ICorDebugFrame.TryGetCode(ICorDebugSharp.ICorDebugCode@)">GetCode</see>; if the method is instrumented, its IL will not be accessible. <c>ILCODE_REJIT_IL</c> allows the debugger to access the IL defined by the profiler's ReJIT request. If the IL is not instrumented, <c>ppCode</c> is <b>null</b>, and the method returns <c>S_OK</c>.</para>
	/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugilframe4-getcodeex-method">Microsoft Learn</see>.</para>
	/// </remarks>
	[PreserveSig]
	int TryGetCodeEx(ILCodeKind flags, out ICorDebugCode ppCode);
}
