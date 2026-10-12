using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop;

public struct IMAGE_COR_ILMETHOD_SECT_EH_SMALL
{
    public struct _Clauses_e__FixedBuffer
    {
        public IMAGE_COR_ILMETHOD_SECT_EH_CLAUSE_SMALL e0;

        [UnscopedRef]
        public ref IMAGE_COR_ILMETHOD_SECT_EH_CLAUSE_SMALL this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return ref Unsafe.Add(ref e0, index);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnscopedRef]
        public Span<IMAGE_COR_ILMETHOD_SECT_EH_CLAUSE_SMALL> AsSpan(int length)
        {
            return MemoryMarshal.CreateSpan(ref e0, length);
        }
    }

    public IMAGE_COR_ILMETHOD_SECT_SMALL SectSmall;

    public ushort Reserved;

    public _Clauses_e__FixedBuffer Clauses;
}
