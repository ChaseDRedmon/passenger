using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcess5Extensions
{
    extension(ICorDebugProcess5 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetTypeFields(Passenger.Debugger.Interop.COR_TYPEID,System.UInt32,Passenger.Debugger.Interop.COR_FIELD[],System.UInt32@)" />
        public COR_FIELD[] GetTypeFields(COR_TYPEID id)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetTypeFields(id, 0u, null, out var pceltNeeded));
            if (pceltNeeded == 0)
            {
                return Array.Empty<COR_FIELD>();
            }
            for (int i = 0; i < 3; i++)
            {
                uint num = pceltNeeded;
                checked
                {
                    COR_FIELD[] array = new COR_FIELD[(int)num];
                    Marshal.ThrowExceptionForHR(instance.TryGetTypeFields(id, num, array, out var pceltNeeded2));
                    if (pceltNeeded2 > num)
                    {
                        pceltNeeded = pceltNeeded2;
                        continue;
                    }
                    if (pceltNeeded2 != num)
                    {
                        Array.Resize(ref array, (int)pceltNeeded2);
                    }
                    return array;
                }
            }
            throw new InvalidOperationException("Native buffer size did not stabilize.");
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateGCReferences(System.Boolean,Passenger.Debugger.Interop.ICorDebugGCReferenceEnum@)" />
        public IEnumerable<COR_GC_REFERENCE> EnumerateGCReferences(bool enumerateWeakReferences)
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateGCReferences(enumerateWeakReferences, out ICorDebugGCReferenceEnum ppEnum));
            return EnumerateGCReferencesCore(ppEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateHandles(Passenger.Debugger.Interop.CorGCReferenceType,Passenger.Debugger.Interop.ICorDebugGCReferenceEnum@)" />
        public IEnumerable<COR_GC_REFERENCE> EnumerateHandles(CorGCReferenceType types)
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateHandles(types, out ICorDebugGCReferenceEnum ppEnum));
            return EnumerateHandlesCore(ppEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateHeap(Passenger.Debugger.Interop.ICorDebugHeapEnum@)" />
        public IEnumerable<COR_HEAPOBJECT> EnumerateHeap()
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateHeap(out ICorDebugHeapEnum ppObjects));
            return EnumerateHeapCore(ppObjects);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateHeapRegions(Passenger.Debugger.Interop.ICorDebugHeapSegmentEnum@)" />
        public IEnumerable<COR_SEGMENT> EnumerateHeapRegions()
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateHeapRegions(out ICorDebugHeapSegmentEnum ppRegions));
            return EnumerateHeapRegionsCore(ppRegions);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateHeap(Passenger.Debugger.Interop.ICorDebugHeapEnum@)" />
        public COR_HEAPOBJECT[] Heap
        {
            get
            {
                return instance.EnumerateHeap().ToArray();
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateHeapRegions(Passenger.Debugger.Interop.ICorDebugHeapSegmentEnum@)" />
        public COR_SEGMENT[] HeapRegions
        {
            get
            {
                return instance.EnumerateHeapRegions().ToArray();
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetArrayLayout(Passenger.Debugger.Interop.COR_TYPEID,Passenger.Debugger.Interop.COR_ARRAY_LAYOUT@)" />
        public COR_ARRAY_LAYOUT GetArrayLayout(COR_TYPEID id)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetArrayLayout(id, out var pLayout));
            return pLayout;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetGCHeapInformation(Passenger.Debugger.Interop.COR_HEAPINFO@)" />
        public COR_HEAPINFO GCHeapInformation
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetGCHeapInformation(out var pHeapInfo));
                return pHeapInfo;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetObject(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.ICorDebugObjectValue@)" />
        public ICorDebugObjectValue GetObject(CORDB_ADDRESS addr)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetObject(addr, out ICorDebugObjectValue pObject));
            return pObject;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetTypeForTypeID(Passenger.Debugger.Interop.COR_TYPEID,Passenger.Debugger.Interop.ICorDebugType@)" />
        public ICorDebugType GetTypeForTypeID(COR_TYPEID id)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetTypeForTypeID(id, out ICorDebugType ppType));
            return ppType;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetTypeID(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.COR_TYPEID@)" />
        public COR_TYPEID GetTypeID(CORDB_ADDRESS obj)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetTypeID(obj, out var pId));
            return pId;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetTypeLayout(Passenger.Debugger.Interop.COR_TYPEID,Passenger.Debugger.Interop.COR_TYPE_LAYOUT@)" />
        public COR_TYPE_LAYOUT GetTypeLayout(COR_TYPEID id)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetTypeLayout(id, out var pLayout));
            return pLayout;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnableNGENPolicy(Passenger.Debugger.Interop.CorDebugNGENPolicy)" />
        public void EnableNGENPolicy(CorDebugNGENPolicy ePolicy)
        {
            Marshal.ThrowExceptionForHR(instance.TryEnableNGENPolicy(ePolicy));
        }
    }

    private static IEnumerable<COR_GC_REFERENCE> EnumerateGCReferencesCore(ICorDebugGCReferenceEnum enumerator)
    {
        while (true)
        {
            COR_GC_REFERENCE[] array = new COR_GC_REFERENCE[1];
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

    private static IEnumerable<COR_GC_REFERENCE> EnumerateHandlesCore(ICorDebugGCReferenceEnum enumerator)
    {
        while (true)
        {
            COR_GC_REFERENCE[] array = new COR_GC_REFERENCE[1];
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

    private static IEnumerable<COR_HEAPOBJECT> EnumerateHeapCore(ICorDebugHeapEnum enumerator)
    {
        while (true)
        {
            COR_HEAPOBJECT[] array = new COR_HEAPOBJECT[1];
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

    private static IEnumerable<COR_SEGMENT> EnumerateHeapRegionsCore(ICorDebugHeapSegmentEnum enumerator)
    {
        while (true)
        {
            COR_SEGMENT[] array = new COR_SEGMENT[1];
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
