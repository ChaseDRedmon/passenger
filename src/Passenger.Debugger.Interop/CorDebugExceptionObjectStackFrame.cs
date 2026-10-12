using System.Runtime.InteropServices.Marshalling;

namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Represents stack frame information from an exception object.</para>
/// </summary>
/// <remarks>
/// <para>The caller must release the pointer to the ICorDebugModule object once it is no longer in use.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugexceptionobjectstackframe-structure">Microsoft Learn</see>.</para>
/// </remarks>
[NativeMarshalling(typeof(CorDebugExceptionObjectStackFrameMarshaller))]
public struct CorDebugExceptionObjectStackFrame
{
    /// <summary>
    /// <para>A pointer to the ICorDebugModule object for the current frame.</para>
    /// </summary>
    public ICorDebugModule pModule;

    /// <summary>
    /// <para>The value of the instruction pointer (EIP/RIP) for the current frame.</para>
    /// </summary>
    public CORDB_ADDRESS ip;

    /// <summary>
    /// <para>The method token for the current frame.</para>
    /// </summary>
    public mdMethodDef methodDef;

    /// <summary>
    /// <para>A value that indicates whether the frame is the last frame in a foreign exception.</para>
    /// </summary>
    public int isLastForeignExceptionFrame;
}
