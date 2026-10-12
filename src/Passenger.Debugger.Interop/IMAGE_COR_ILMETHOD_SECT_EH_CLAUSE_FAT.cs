using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp;

public struct IMAGE_COR_ILMETHOD_SECT_EH_CLAUSE_FAT
{
	[StructLayout(LayoutKind.Explicit)]
	public struct _Anonymous_e__Union
	{
		[FieldOffset(0)]
		public uint ClassToken;

		[FieldOffset(0)]
		public uint FilterOffset;
	}

	public CorExceptionFlag Flags;

	public uint TryOffset;

	public uint TryLength;

	public uint HandlerOffset;

	public uint HandlerLength;

	public _Anonymous_e__Union Anonymous;

	[UnscopedRef]
	public ref uint ClassToken
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get
		{
			return ref Anonymous.ClassToken;
		}
	}

	[UnscopedRef]
	public ref uint FilterOffset
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get
		{
			return ref Anonymous.FilterOffset;
		}
	}
}
