using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp;

public struct IMAGE_COR_ILMETHOD_SECT_EH_CLAUSE_SMALL
{
	[StructLayout(LayoutKind.Explicit)]
	public struct _Anonymous_e__Union
	{
		[FieldOffset(0)]
		public uint ClassToken;

		[FieldOffset(0)]
		public uint FilterOffset;
	}

	public int _bitfield1;

	public uint _bitfield2;

	public _Anonymous_e__Union Anonymous;

	public CorExceptionFlag Flags
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		readonly get
		{
			return (CorExceptionFlag)(_bitfield1 << 16 >> 16);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set
		{
			_bitfield1 = (_bitfield1 & -65536) | (int)(value & (CorExceptionFlag)0xFFFF);
		}
	}

	public uint TryOffset
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		readonly get
		{
			return (uint)((_bitfield1 >> 16) & 0xFFFF);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set
		{
			_bitfield1 = (_bitfield1 & 0xFFFF) | (int)((value & 0xFFFF) << 16);
		}
	}

	public uint TryLength
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		readonly get
		{
			return _bitfield2 & 0xFF;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set
		{
			_bitfield2 = (_bitfield2 & 0xFFFFFF00u) | (value & 0xFF);
		}
	}

	public uint HandlerOffset
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		readonly get
		{
			return (_bitfield2 >> 8) & 0xFFFF;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set
		{
			_bitfield2 = (_bitfield2 & 0xFF0000FFu) | ((value & 0xFFFF) << 8);
		}
	}

	public uint HandlerLength
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		readonly get
		{
			return (_bitfield2 >> 24) & 0xFF;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set
		{
			_bitfield2 = (_bitfield2 & 0xFFFFFF) | ((value & 0xFF) << 24);
		}
	}

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
