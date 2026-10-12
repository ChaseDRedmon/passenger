using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Extends the <see cref="T:Passenger.Debugger.Interop.ICorDebugDebugEvent">ICorDebugDebugEvent</see> interface to support module-level events.</para>
/// </summary>
/// <remarks>
/// <para>The <see cref="T:Passenger.Debugger.Interop.CorDebugDebugEventKind">MODULE_LOADED</see> and <see cref="T:Passenger.Debugger.Interop.CorDebugDebugEventKind">MODULE_UNLOADED</see> event types implement this interface.</para>
/// <para><b>Note:</b> The interface is available with .NET Native only. Attempting to call  QueryInterface  to retrieve an interface pointer returns  E_NOINTERFACE  for ICorDebug scenarios outside of .NET Native.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmoduledebugevent-interface">Microsoft Learn</see>.</para>
/// </remarks>
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("51A15E8D-9FFF-4864-9B87-F4FBDEA747A2")]
public partial interface ICorDebugModuleDebugEvent : ICorDebugDebugEvent
{
    /// <summary>
    /// <para>Gets the merged module that was just loaded or unloaded.</para>
    /// </summary>
    /// <param name="ppModule">
    /// <para>A pointer to the address of an ICorDebugModule object that represents the merged module that was just loaded or unloaded.</para>
    /// </param>
    /// <remarks>
    /// <para>You can call the <see cref="M:Passenger.Debugger.Interop.ICorDebugDebugEvent.TryGetEventKind(Passenger.Debugger.Interop.CorDebugDebugEventKind@)">GetEventKind</see> method to determine whether the module was loaded or unloaded.</para>
    /// <para><b>Note:</b> This method is available with .NET Native only.</para>
    /// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmoduledebugevent-getmodule-method">Microsoft Learn</see>.</para>
    /// </remarks>
    [PreserveSig]
    int TryGetModule(out ICorDebugModule ppModule);

}
