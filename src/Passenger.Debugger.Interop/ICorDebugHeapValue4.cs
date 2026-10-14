#nullable enable

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Pins a heap value, so the garbage collector never moves it while the handle is held (.NET 5 and later).</para>
/// </summary>
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("B35DD495-A555-463B-9BE9-C55338486BB8")]
public partial interface ICorDebugHeapValue4
{
    [PreserveSig]
    int TryCreatePinnedHandle(out ICorDebugHandleValue ppHandle);
}
