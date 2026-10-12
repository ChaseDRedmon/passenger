using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop;

public struct IMAGE_COR_ILMETHOD_SECT_FAT
{
    public uint _bitfield;

    public uint Kind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get
        {
            return _bitfield & 0xFF;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _bitfield = (_bitfield & 0xFFFFFF00u) | (value & 0xFF);
        }
    }

    public uint DataSize
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get
        {
            return (_bitfield >> 8) & 0xFFFFFF;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _bitfield = (_bitfield & 0xFF) | ((value & 0xFFFFFF) << 8);
        }
    }
}
