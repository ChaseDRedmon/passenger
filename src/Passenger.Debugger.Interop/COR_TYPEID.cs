#nullable enable

namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Contains a type identifier.</para>
/// </summary>
/// <remarks>
/// <para>The <c>COR_TYPEID</c> structure is returned by a number of debugging methods that provide information about objects to be garbage-collected. It can then be passed as an argument to other debugging methods that provide additional information about that item. For example, by enumerating an ICorDebugHeapEnum object, you can retrieve individual COR_HEAPOBJECT objects that represent individual objects on the managed heap. You can then pass the <c>COR_TYPEID</c> value from the <c>COR_HEAPOBJECT.type</c> field to the ICorDebugProcess5::GetTypeForTypeID method to retrieve an ICorDebugType object that provides type information about the object.</para>
/// <para>A <c>COR_TYPEID</c> object is intended to be opaque. Its individual fields should not be accessed or manipulated. Its sole use is as an identifier that is provided as an <c>out</c> parameter in a method call and that can, in turn, be passed to other methods to provide additional information.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-typeid-structure">Microsoft Learn</see>.</para>
/// </remarks>
public struct COR_TYPEID
{
    /// <summary>
    /// <para>The first token.</para>
    /// </summary>
    public ulong token1;

    /// <summary>
    /// <para>The second token.</para>
    /// </summary>
    public ulong token2;
}
