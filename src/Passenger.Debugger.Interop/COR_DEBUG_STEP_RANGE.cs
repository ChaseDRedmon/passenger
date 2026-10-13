#nullable enable

namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Contains the offset information for a range of code.</para>
/// <para>This structure is used by the <see cref="M:Passenger.Debugger.Interop.ICorDebugStepper.TryStepRange(System.Boolean,Passenger.Debugger.Interop.COR_DEBUG_STEP_RANGE[],System.UInt32)">ICorDebugStepper::StepRange</see> method.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-debug-step-range-structure">Microsoft Learn</see>.</para>
/// </remarks>
public struct COR_DEBUG_STEP_RANGE
{
    /// <summary>
    /// <para>The offset of the beginning of the range.</para>
    /// </summary>
    public uint startOffset;

    /// <summary>
    /// <para>The offset of the end of the range.</para>
    /// </summary>
    public uint endOffset;
}
