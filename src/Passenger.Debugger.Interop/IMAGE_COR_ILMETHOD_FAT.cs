using System.Runtime.CompilerServices;

namespace ICorDebugSharp;

public struct IMAGE_COR_ILMETHOD_FAT
{
	public uint _bitfield;

	public uint CodeSize;

	public uint LocalVarSigTok;

	public uint Flags
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		readonly get
		{
			return _bitfield & 0xFFF;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set
		{
			_bitfield = (_bitfield & 0xFFFFF000u) | (value & 0xFFF);
		}
	}

	public uint Size
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		readonly get
		{
			return (_bitfield >> 12) & 0xF;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set
		{
			_bitfield = (_bitfield & 0xFFFF0FFFu) | ((value & 0xF) << 12);
		}
	}

	public uint MaxStack
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		readonly get
		{
			return (_bitfield >> 16) & 0xFFFF;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set
		{
			_bitfield = (_bitfield & 0xFFFF) | ((value & 0xFFFF) << 16);
		}
	}
}
