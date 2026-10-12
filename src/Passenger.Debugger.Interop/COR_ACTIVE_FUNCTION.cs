using System.Runtime.InteropServices.Marshalling;

namespace ICorDebugSharp;

/// <summary>
/// <para>Contains information about the functions that are currently active in a thread's frames. This structure is used by the <see cref="M:ICorDebugSharp.ICorDebugThread2.TryGetActiveFunctions(System.UInt32,System.UInt32@,ICorDebugSharp.COR_ACTIVE_FUNCTION[])">ICorDebugThread2::GetActiveFunctions</see> method.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-active-function-structure">Microsoft Learn</see>.</para>
/// </remarks>
[NativeMarshalling(typeof(CorActiveFunctionMarshaller))]
public struct COR_ACTIVE_FUNCTION
{
	/// <summary>
	/// <para>Pointer to the application domain owner of the <c>ilOffset</c> field.</para>
	/// </summary>
	public ICorDebugAppDomain pAppDomain;

	/// <summary>
	/// <para>Pointer to the module owner of the <c>ilOffset</c> field.</para>
	/// </summary>
	public ICorDebugModule pModule;

	/// <summary>
	/// <para>Pointer to the function owner of the <c>ilOffset</c> field.</para>
	/// </summary>
	public ICorDebugFunction2 pFunction;

	/// <summary>
	/// <para>The common intermediate language (CIL) offset of the frame.</para>
	/// </summary>
	public uint ilOffset;

	/// <summary>
	/// <para>Reserved for future extensibility.</para>
	/// </summary>
	public uint flags;
}
