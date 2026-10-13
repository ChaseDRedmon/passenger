#nullable enable

using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Implements "ICorDebugEnum" methods and enumerates "ICorDebugType" arrays.</para>
/// </summary>
/// <remarks>
/// <para><b>Note:</b> This interface does not support being called remotely, either cross-machine or cross-process.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugtypeenum-interface">Microsoft Learn</see>.</para>
/// </remarks>
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("10F27499-9DF2-43CE-8333-A321D7C99CB4")]
public partial interface ICorDebugTypeEnum : ICorDebugEnum
{
    /// <summary>
    /// <para>Gets the number of "ICorDebugType" instances specified by <c>celt</c> from the enumeration, starting at the current position.</para>
    /// </summary>
    /// <param name="celt">
    /// <para>The number of <c>ICorDebugType</c> instances to be retrieved.</para>
    /// </param>
    /// <param name="values">
    /// <para>An array of pointers, each of which points to an <c>ICorDebugType</c> object.</para>
    /// </param>
    /// <param name="pceltFetched">
    /// <para>Pointer to the number of <c>ICorDebugType</c> instances actually returned. This value may be null if <c>celt</c> is one.</para>
    /// </param>
    /// <remarks>
    /// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugtypeenum-next-method">Microsoft Learn</see>.</para>
    /// </remarks>
    [PreserveSig]
    int TryNext(uint celt, [Out][MarshalUsing(CountElementName = "celt")] ICorDebugType[] values, out uint pceltFetched);

}
