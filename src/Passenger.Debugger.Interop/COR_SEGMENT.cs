namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Contains information about a region of memory in the managed heap.</para>
/// </summary>
/// <remarks>
/// <para>The <c>COR_SEGMENTS</c> structure represents a region of memory in the managed heap.  <c>COR_SEGMENTS</c> objects are members of the <see cref="T:Passenger.Debugger.Interop.ICorDebugHeapSegmentEnum">ICorDebugHeapRegionEnum</see> collection object, which is populated by calling the <see cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateHeapRegions(Passenger.Debugger.Interop.ICorDebugHeapSegmentEnum@)">ICorDebugProcess5::EnumerateHeapRegions</see> method.</para>
/// <para>The <c>heap</c> field is the processor number, which corresponds to the heap being reported. For workstation garbage collectors, its value is always zero, because workstations have only one garbage collection heap. For server garbage collectors, its value corresponds to the processor the heap is attached to. Note that there may be more or fewer garbage collection heaps than there are actual processors due to the implementation details of the garbage collector.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-segment-structure">Microsoft Learn</see>.</para>
/// </remarks>
public struct COR_SEGMENT
{
    /// <summary>
    /// <para>The starting address of the memory region.</para>
    /// </summary>
    public CORDB_ADDRESS start;

    /// <summary>
    /// <para>The ending address of the memory region.</para>
    /// </summary>
    public CORDB_ADDRESS end;

    /// <summary>
    /// <para>A <see cref="T:Passenger.Debugger.Interop.CorDebugGenerationTypes">CorDebugGenerationTypes</see> enumeration member that indicates the generation of the memory region.</para>
    /// </summary>
    public CorDebugGenerationTypes type;

    /// <summary>
    /// <para>The heap number in which the memory region resides. See the Remarks section for more information.</para>
    /// </summary>
    public uint heap;
}
