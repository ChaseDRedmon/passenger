using System.Runtime.InteropServices.Marshalling;

namespace ICorDebugSharp;

/// <summary>
/// <para>Defines an object that is blocking a thread and the specific reason that the thread is blocked.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugblockingobject-structure">Microsoft Learn</see>.</para>
/// </remarks>
[NativeMarshalling(typeof(CorDebugBlockingObjectMarshaller))]
public struct CorDebugBlockingObject
{
	/// <summary>
	/// <para>The object on which the thread is blocking. This object is valid only for the duration of the current synchronized state. If two threads are blocking on the same object within the same synchronized state, you may expect the <see cref="M:ICorDebugSharp.ICorDebugValue.TryGetAddress(ICorDebugSharp.CORDB_ADDRESS@)">ICorDebugValue::GetAddress</see> method to return the same value. However, the interfaces may or may not be pointer equivalent.</para>
	/// </summary>
	public ICorDebugValue pBlockingObject;

	/// <summary>
	/// <para>The number of milliseconds before the blocking operation will time out, or the value INFINITE, which indicates that it will not time out. The time-out value specifies the total length of time for the blocking operation, not the time that is still remaining.</para>
	/// </summary>
	public uint dwTimeout;

	/// <summary>
	/// <para>The reason that the thread is blocked on this object.</para>
	/// </summary>
	public CorDebugBlockingReason blockingReason;
}
