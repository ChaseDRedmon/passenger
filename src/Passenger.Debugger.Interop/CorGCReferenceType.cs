namespace ICorDebugSharp;

/// <summary>
/// <para>Identifies the source of an object to be garbage-collected.</para>
/// </summary>
/// <remarks>
/// <para>The <c>CorGCReferenceType</c> enumeration is used as follows:</para>
/// <list type="bullet">
/// <item><description><para>As the value of the <c>type</c> field of the <see cref="T:ICorDebugSharp.COR_GC_REFERENCE">COR_GC_REFERENCE</see> structure, it indicates the source of a reference or handle.</para>
/// </description></item>
/// <item><description><para>As the <c>types</c> argument to the <see cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnumerateHandles(ICorDebugSharp.CorGCReferenceType,ICorDebugSharp.ICorDebugGCReferenceEnum@)">ICorDebugProcess5::EnumerateHandles</see> method, it specifies the types of handles to include in the enumeration.</para>
/// </description></item>
/// </list>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/corgcreferencetype-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorGCReferenceType
{
	/// <summary>
	/// <para>A handle to a strong reference from the object handle table.</para>
	/// </summary>
	CorHandleStrong = 1,
	/// <summary>
	/// <para>A handle to a pinned strong reference from the object handle table.</para>
	/// </summary>
	CorHandleStrongPinning = 2,
	/// <summary>
	/// <para>A handle to a weak reference from the object handle table.</para>
	/// </summary>
	CorHandleWeakShort = 4,
	CorHandleWeakLong = 8,
	/// <summary>
	/// <para>A handle to a weak reference-counted object from the object handle table.</para>
	/// </summary>
	CorHandleWeakRefCount = 16,
	/// <summary>
	/// <para>A handle to a reference-counted object from the object handle table.</para>
	/// </summary>
	CorHandleStrongRefCount = 32,
	/// <summary>
	/// <para>A handle to a dependent object from the object handle table.</para>
	/// </summary>
	CorHandleStrongDependent = 64,
	/// <summary>
	/// <para>An asynchronous pinned object from the object handle table.</para>
	/// </summary>
	CorHandleStrongAsyncPinned = 128,
	/// <summary>
	/// <para>A strong handle that keeps an approximate size of the collective closure of all objects and object roots at garbage collection time.</para>
	/// </summary>
	CorHandleStrongSizedByref = 256,
	CorHandleWeakNativeCom = 512,
	/// <summary>
	/// <para>A weak handle to a RCW (Runtime Callable Wrapper). An RCW is a managed object that proxies calls to an underlying COM object.</para>
	/// </summary>
	CorHandleWeakWinRT = CorHandleWeakNativeCom,
	/// <summary>
	/// <para>A reference from the managed stack.</para>
	/// </summary>
	CorReferenceStack = -2147483647,
	/// <summary>
	/// <para>A reference from the finalizer queue.</para>
	/// </summary>
	CorReferenceFinalizer = 80000002,
	/// <summary>
	/// <para>Return only strong references from the handle table. This value is used by the <see cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnumerateHandles(ICorDebugSharp.CorGCReferenceType,ICorDebugSharp.ICorDebugGCReferenceEnum@)">ICorDebugProcess5::EnumerateHandles</see> method only.</para>
	/// </summary>
	CorHandleStrongOnly = 483,
	/// <summary>
	/// <para>Return only weak references from the handle table. This value is used by the <see cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnumerateHandles(ICorDebugSharp.CorGCReferenceType,ICorDebugSharp.ICorDebugGCReferenceEnum@)">ICorDebugProcess5::EnumerateHandles</see> method only.</para>
	/// </summary>
	CorHandleWeakOnly = 540,
	/// <summary>
	/// <para>Return all references from the handle table. This value is used by the <see cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnumerateHandles(ICorDebugSharp.CorGCReferenceType,ICorDebugSharp.ICorDebugGCReferenceEnum@)">ICorDebugProcess5::EnumerateHandles</see> method only.</para>
	/// </summary>
	CorHandleAll = int.MaxValue
}
