using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop;

public struct IMAGE_COR_ILMETHOD_SECT_EH_FAT
{
    public struct _Clauses_e__FixedBuffer
    {
        public IMAGE_COR_ILMETHOD_SECT_EH_CLAUSE_FAT e0;

        [UnscopedRef]
        public ref IMAGE_COR_ILMETHOD_SECT_EH_CLAUSE_FAT this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return ref Unsafe.Add(ref e0, index);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnscopedRef]
        public Span<IMAGE_COR_ILMETHOD_SECT_EH_CLAUSE_FAT> AsSpan(int length)
        {
            return MemoryMarshal.CreateSpan(ref e0, length);
        }
    }

    public IMAGE_COR_ILMETHOD_SECT_FAT SectFat;

    public _Clauses_e__FixedBuffer Clauses;
}
