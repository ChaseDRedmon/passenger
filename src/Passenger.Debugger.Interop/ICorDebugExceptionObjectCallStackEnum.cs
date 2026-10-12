using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ICorDebugSharp;

/// <summary>
/// <para>Provides an enumerator for call stack information that is embedded in an exception object. This interface is a subclass of the ICorDebugEnum interface.</para>
/// </summary>
/// <remarks>
/// <para>The <c>ICorDebugExceptionObjectCallStackEnum</c> interface implements the ICorDebugEnum interface.</para>
/// <para>An <c>ICorDebugExceptionObjectCallStackEnum</c> instance is populated with <see cref="T:ICorDebugSharp.CorDebugExceptionObjectStackFrame">CorDebugExceptionObjectStackFrame</see> objects by calling the <see cref="M:ICorDebugSharp.ICorDebugExceptionObjectValue.TryEnumerateExceptionCallStack(ICorDebugSharp.ICorDebugExceptionObjectCallStackEnum@)">ICorDebugExceptionObjectValue::EnumerateExceptionCallStack</see> method. The call stack items in the collection can be enumerated by calling the <see cref="M:ICorDebugSharp.ICorDebugExceptionObjectCallStackEnum.TryNext(System.UInt32,ICorDebugSharp.CorDebugExceptionObjectStackFrame[],System.UInt32@)">ICorDebugExceptionObjectCallStackEnum::Next</see> method</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugexceptionobjectcallstackenum-interface">Microsoft Learn</see>.</para>
/// </remarks>
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("ED775530-4DC4-41F7-86D0-9E2DEF7DFC66")]
public partial interface ICorDebugExceptionObjectCallStackEnum : ICorDebugEnum
{
	/// <summary>
	/// <para>Gets the specified number of <see cref="T:ICorDebugSharp.CorDebugExceptionObjectStackFrame">CorDebugExceptionObjectStackFrame</see> instances that contain information from an exception object's call stack.</para>
	/// </summary>
	/// <param name="celt">
	/// <para>The number of <see cref="T:ICorDebugSharp.CorDebugExceptionObjectStackFrame">CorDebugExceptionObjectStackFrame</see> instances to be retrieved.</para>
	/// </param>
	/// <param name="values">
	/// <para>An array of pointers, each of which points to a <see cref="T:ICorDebugSharp.CorDebugExceptionObjectStackFrame">CorDebugExceptionObjectStackFrame</see> object.</para>
	/// </param>
	/// <param name="pceltFetched">
	/// <para>A pointer to the number of <see cref="T:ICorDebugSharp.CorDebugExceptionObjectStackFrame">CorDebugExceptionObjectStackFrame</see> instances actually returned.</para>
	/// </param>
	/// <remarks>
	/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugexceptionobjectcallstackenum-next-method">Microsoft Learn</see>.</para>
	/// </remarks>
	[PreserveSig]
	int TryNext(uint celt, [Out][MarshalUsing(CountElementName = "celt")] CorDebugExceptionObjectStackFrame[] values, out uint pceltFetched);

}
