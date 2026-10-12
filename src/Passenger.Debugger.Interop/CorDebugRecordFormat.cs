namespace ICorDebugSharp;

/// <summary>
/// <para>Describes the format of the data in a byte array that contains information about a native exception debug event.</para>
/// </summary>
/// <remarks>
/// <para>A member of the <c>CorDebugRecordFormat</c> enumeration is passed to the <see cref="M:ICorDebugSharp.ICorDebugProcess6.TryDecodeEvent(System.Byte[],System.UInt32,ICorDebugSharp.CorDebugRecordFormat,System.UInt32,System.UInt32,ICorDebugSharp.ICorDebugDebugEvent@)">DecodeEvent</see> method to indicate the format of the byte array in its <c>pRecord</c> argument.</para>
/// <para><b>Note:</b> This enumeration is intended for use in .NET Native debugging scenarios only.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugrecordformat-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorDebugRecordFormat
{
	/// <summary>
	/// <para>The data is a 32-bit Windows exception record.</para>
	/// </summary>
	FORMAT_WINDOWS_EXCEPTIONRECORD32 = 1,
	/// <summary>
	/// <para>The data is a 64-bit Windows exception record.</para>
	/// </summary>
	FORMAT_WINDOWS_EXCEPTIONRECORD64
}
