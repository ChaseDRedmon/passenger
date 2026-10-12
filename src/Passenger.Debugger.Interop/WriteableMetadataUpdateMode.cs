namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Provides values that specify whether in-memory updates to metadata are visible to a debugger.</para>
/// </summary>
/// <remarks>
/// <para>A member of the <c>WriteableMetadataUpdateMode</c> enumeration can be passed to the <see cref="M:Passenger.Debugger.Interop.ICorDebugProcess7.TrySetWriteableMetadataUpdateMode(Passenger.Debugger.Interop.WriteableMetadataUpdateMode)">SetWriteableMetadataUpdateMode</see> method to control whether in-memory updates to metadata in the target process are visible to the debugger.</para>
/// <para>The <c>LegacyCompatPolicy</c> option enforces the same behavior as in versions of .NET Framework prior to 4.5.2. This often means that metadata from updates is not visible. However, calls to a number of debugging methods implicitly coerce the debugger to make updates visible. For example, if the debugger passes <see cref="M:Passenger.Debugger.Interop.ICorDebugILFrame.TryGetLocalVariable(System.UInt32,Passenger.Debugger.Interop.ICorDebugValue@)">ICorDebugILFrame::GetLocalVariable</see> the index of a variable not found in the method's original metadata, all metadata for the module is updated to a snapshot matching the current state of the process. In other words, with the <c>LegacyCompatPolicy</c> option, the debugger might see none, some, or all of the available metadata updates, depending on how it uses other parts of the unmanaged debugging API.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/writeablemetadataupdatemode-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum WriteableMetadataUpdateMode
{
    /// <summary>
    /// <para>Maintain compatibility with older versions of .NET Framework when making in-memory updates to metadata visible.</para>
    /// </summary>
    LegacyCompatPolicy,
    /// <summary>
    /// <para>Make in-memory updates to metadata visible to the debugger.</para>
    /// </summary>
    AlwaysShowUpdates
}
