using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugComObjectValueExtensions
{
    extension(ICorDebugComObjectValue instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugComObjectValue.TryGetCachedInterfacePointers(System.Boolean,System.UInt32,System.UInt32@,Passenger.Debugger.Interop.CORDB_ADDRESS[])" />
        public CORDB_ADDRESS[] GetCachedInterfacePointers(bool bIInspectableOnly)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetCachedInterfacePointers(bIInspectableOnly, 0u, out var pcEltFetched, null));
            if (pcEltFetched == 0)
            {
                return Array.Empty<CORDB_ADDRESS>();
            }
            for (int i = 0; i < 3; i++)
            {
                uint num = pcEltFetched;
                checked
                {
                    CORDB_ADDRESS[] array = new CORDB_ADDRESS[(int)num];
                    Marshal.ThrowExceptionForHR(instance.TryGetCachedInterfacePointers(bIInspectableOnly, num, out var pcEltFetched2, array));
                    if (pcEltFetched2 > num)
                    {
                        pcEltFetched = pcEltFetched2;
                        continue;
                    }
                    if (pcEltFetched2 != num)
                    {
                        Array.Resize(ref array, (int)pcEltFetched2);
                    }
                    return array;
                }
            }
            throw new InvalidOperationException("Native buffer size did not stabilize.");
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugComObjectValue.TryGetCachedInterfaceTypes(System.Boolean,Passenger.Debugger.Interop.ICorDebugTypeEnum@)" />
        public IEnumerable<ICorDebugType> GetCachedInterfaceTypes(bool bIInspectableOnly)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetCachedInterfaceTypes(bIInspectableOnly, out ICorDebugTypeEnum ppInterfacesEnum));
            return GetCachedInterfaceTypesCore(ppInterfacesEnum);
        }
    }

    private static IEnumerable<ICorDebugType> GetCachedInterfaceTypesCore(ICorDebugTypeEnum enumerator)
    {
        while (true)
        {
            ICorDebugType[] array = new ICorDebugType[1];
            int errorCode = enumerator.TryNext(1u, array, out var pceltFetched);
            if (pceltFetched == 0)
            {
                yield break;
            }
            Marshal.ThrowExceptionForHR(errorCode);
            if (pceltFetched != 1)
            {
                break;
            }
            yield return array[0];
        }
        throw new InvalidOperationException("Native debugger enumerator returned an invalid item count.");
    }
}
