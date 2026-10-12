using System;
using System.Runtime.InteropServices.Marshalling;

namespace ICorDebugSharp;

/// <summary>
/// <para>Maps a Windows Runtime GUID to its corresponding ICorDebugType object.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugguidtotypemapping-structure">Microsoft Learn</see>.</para>
/// </remarks>
[NativeMarshalling(typeof(CorDebugGuidToTypeMappingMarshaller))]
public struct CorDebugGuidToTypeMapping
{
	/// <summary>
	/// <para>The GUID of the cached Windows Runtime type.</para>
	/// </summary>
	public Guid iid;

	/// <summary>
	/// <para>A pointer to an ICorDebugType object that provides information about the cached type.</para>
	/// </summary>
	public ICorDebugType pType;
}
