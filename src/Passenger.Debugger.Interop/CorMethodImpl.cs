namespace ICorDebugSharp;

/// <summary>
/// <para>Contains values that describe method implementation features.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/cormethodimpl-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorMethodImpl
{
	/// <summary>
	/// <para>Flags that describe code type.</para>
	/// </summary>
	miCodeTypeMask = 3,
	/// <summary>
	/// <para>Specifies that the method implementation is common intermediate language (CIL).</para>
	/// </summary>
	miIL = 0,
	/// <summary>
	/// <para>Specifies that the method implementation is native.</para>
	/// </summary>
	miNative = 1,
	/// <summary>
	/// <para>Specifies that the method implementation is OPTIL.</para>
	/// </summary>
	miOPTIL = 2,
	/// <summary>
	/// <para>Specifies that the method implementation is provided by the common language runtime.</para>
	/// </summary>
	miRuntime = miCodeTypeMask,
	/// <summary>
	/// <para>Flags that indicate whether the code is managed or unmanaged.</para>
	/// </summary>
	miManagedMask = 4,
	/// <summary>
	/// <para>Specifies that the method implementation is unmanaged.</para>
	/// </summary>
	miUnmanaged = miManagedMask,
	/// <summary>
	/// <para>Specifies that the method implementation is managed.</para>
	/// </summary>
	miManaged = miIL,
	/// <summary>
	/// <para>Specifies that the method is defined. This flag is used primarily in merge scenarios.</para>
	/// </summary>
	miForwardRef = 16,
	/// <summary>
	/// <para>Specifies that the method signature cannot be mangled for an HRESULT conversion.</para>
	/// </summary>
	miPreserveSig = 128,
	/// <summary>
	/// <para>Reserved for internal use by the common language runtime.</para>
	/// </summary>
	miInternalCall = 4096,
	/// <summary>
	/// <para>Specifies that the method is single-threaded through its body.</para>
	/// </summary>
	miSynchronized = 32,
	/// <summary>
	/// <para>Specifies that the method cannot be inlined.</para>
	/// </summary>
	miNoInlining = 8,
	/// <summary>
	/// <para>Specifies that the method should be inlined if possible.</para>
	/// </summary>
	miAggressiveInlining = 256,
	/// <summary>
	/// <para>Specifies that the method should not be optimized.</para>
	/// </summary>
	miNoOptimization = 64,
	miAggressiveOptimization = 512,
	miAsync = 8192,
	miUserMask = 13308,
	/// <summary>
	/// <para>The maximum valid value for a <c>CorMethodImpl</c>.</para>
	/// </summary>
	miMaxMethodImplVal = 65535
}
