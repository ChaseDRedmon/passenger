#nullable enable

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Represents the set of registers available on the computer that is currently executing code.</para>
/// </summary>
/// <remarks>
/// <para>The <c>ICorDebugRegisterSet</c> interface supports only 32-bit registers. Use the ICorDebugRegisterSet2 interface on platforms such as IA-64 that require additional registers.</para>
/// <para><b>Note:</b> This interface does not support being called remotely, either cross-machine or cross-process.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugregisterset-interface">Microsoft Learn</see>.</para>
/// </remarks>
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCB0B-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugRegisterSet
{
    /// <summary>
    /// <para>Gets a bit mask indicating which registers in this <see cref="T:Passenger.Debugger.Interop.ICorDebugRegisterSet">ICorDebugRegisterSet</see> are currently available.</para>
    /// </summary>
    /// <param name="pAvailable">
    /// <para>A bit mask that indicates which registers are currently available.</para>
    /// </param>
    /// <remarks>
    /// <para>A register may be unavailable if its value cannot be determined for the given situation.</para>
    /// <para>The returned mask contains a bit for each register (1 &lt;&lt; the register index). The bit value is 1 if the register is available, or 0 if it is not available.</para>
    /// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugregisterset-getregistersavailable-method">Microsoft Learn</see>.</para>
    /// </remarks>
    [PreserveSig]
    int TryGetRegistersAvailable(out ulong pAvailable);

    /// <summary>
    /// <para>Gets the value of each register (on the computer that is currently executing code) that is specified by the bit mask.</para>
    /// </summary>
    /// <param name="mask">
    /// <para>A bit mask that specifies which register values are to be retrieved. Each bit corresponds to a register. If a bit is set to one, the register's value is retrieved; otherwise, the register's value is not retrieved.</para>
    /// </param>
    /// <param name="regCount">
    /// <para>The number of register values to be retrieved.</para>
    /// </param>
    /// <param name="regBuffer">
    /// <para>An array of <c>CORDB_REGISTER</c> objects, each of which receives a value of a register.</para>
    /// </param>
    /// <remarks>
    /// <para>The size of the array should be equal to the number of bits set to one in the bit mask. The <c>regCount</c> parameter specifies the number of elements in the buffer that will receive the register values. If the <c>regCount</c> value is too small for the number of registers indicated by the mask, the higher numbered registers will be truncated from the set. If the <c>regCount</c> value is too large, the unused <c>regBuffer</c> elements will be unmodified.</para>
    /// <para>If the bit mask specifies a register that is unavailable, <c>GetRegisters</c> returns an indeterminate value for that register.</para>
    /// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugregisterset-getregisters-method">Microsoft Learn</see>.</para>
    /// </remarks>
    [PreserveSig]
    int TryGetRegisters(ulong mask, uint regCount, [Out][MarshalUsing(CountElementName = "regCount")] ulong[] regBuffer);

    /// <summary>
    /// <para><c>SetRegisters</c> is not implemented.</para>
    /// <para><b>Note:</b> Use the higher-level operations such as  ICorDebugILFrame :: SetIP  or  ICorDebugNativeFrame :: SetIP .</para>
    /// </summary>
    /// <remarks>
    /// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugregisterset-setregisters-method">Microsoft Learn</see>.</para>
    /// </remarks>
    [PreserveSig]
    int TrySetRegisters(ulong mask, uint regCount, [In][MarshalUsing(CountElementName = "regCount")] ulong[] regBuffer);

    /// <summary>
    /// <para>Gets the context of the current thread.</para>
    /// </summary>
    /// <param name="contextSize">
    /// <para>The size, in bytes, of the <c>context</c> array.</para>
    /// </param>
    /// <param name="context">
    /// <para>An array of bytes that compose the Win32 <c>CONTEXT</c> structure for the current platform.</para>
    /// </param>
    /// <remarks>
    /// <para>The debugger should call this function instead of the Win32 <c>GetThreadContext</c> function, because the thread may be in a "hijacked" state where its context has been temporarily changed. The data returned is a Win32 <c>CONTEXT</c> structure for the current platform.</para>
    /// <para>For non-leaf frames, clients should check which registers are valid by using <see cref="M:Passenger.Debugger.Interop.ICorDebugRegisterSet.TryGetRegistersAvailable(System.UInt64@)">ICorDebugRegisterSet::GetRegistersAvailable</see>.</para>
    /// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugregisterset-getthreadcontext-method">Microsoft Learn</see>.</para>
    /// </remarks>
    [PreserveSig]
    int TryGetThreadContext(uint contextSize, [In][Out][MarshalUsing(CountElementName = "contextSize")] byte[] context);

    /// <summary>
    /// <para><c>SetThreadContext</c> is not implemented.</para>
    /// <para><b>Note:</b> Use the higher-level operation  ICorDebugNativeFrame :: SetIP  to set the context of a thread.</para>
    /// </summary>
    /// <remarks>
    /// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugregisterset-setthreadcontext-method">Microsoft Learn</see>.</para>
    /// </remarks>
    [PreserveSig]
    int TrySetThreadContext(uint contextSize, [In][MarshalUsing(CountElementName = "contextSize")] byte[] context);
}
