using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcessProcess5Extensions
{
    extension(ICorDebugProcess instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess5" />
        public bool SupportsProcess5
        {
            get
            {
                return instance is ICorDebugProcess5;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess5" />
        public ICorDebugProcess5 Process5
        {
            get
            {
                return (instance as ICorDebugProcess5) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess5.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnableNGENPolicy(Passenger.Debugger.Interop.CorDebugNGENPolicy)" />
        public int TryEnableNGENPolicy(CorDebugNGENPolicy ePolicy)
        {
            return instance.Process5.TryEnableNGENPolicy(ePolicy);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnableNGENPolicy(Passenger.Debugger.Interop.CorDebugNGENPolicy)" />
        public void EnableNGENPolicy(CorDebugNGENPolicy ePolicy)
        {
            instance.Process5.EnableNGENPolicy(ePolicy);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateGCReferences(System.Boolean,Passenger.Debugger.Interop.ICorDebugGCReferenceEnum@)" />
        public int TryEnumerateGCReferences(bool enumerateWeakReferences, out ICorDebugGCReferenceEnum ppEnum)
        {
            return instance.Process5.TryEnumerateGCReferences(enumerateWeakReferences, out ppEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateGCReferences(System.Boolean,Passenger.Debugger.Interop.ICorDebugGCReferenceEnum@)" />
        public IEnumerable<COR_GC_REFERENCE> EnumerateGCReferences(bool enumerateWeakReferences)
        {
            return instance.Process5.EnumerateGCReferences(enumerateWeakReferences);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateHandles(Passenger.Debugger.Interop.CorGCReferenceType,Passenger.Debugger.Interop.ICorDebugGCReferenceEnum@)" />
        public int TryEnumerateHandles(CorGCReferenceType types, out ICorDebugGCReferenceEnum ppEnum)
        {
            return instance.Process5.TryEnumerateHandles(types, out ppEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateHandles(Passenger.Debugger.Interop.CorGCReferenceType,Passenger.Debugger.Interop.ICorDebugGCReferenceEnum@)" />
        public IEnumerable<COR_GC_REFERENCE> EnumerateHandles(CorGCReferenceType types)
        {
            return instance.Process5.EnumerateHandles(types);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateHeap(Passenger.Debugger.Interop.ICorDebugHeapEnum@)" />
        public int TryEnumerateHeap(out ICorDebugHeapEnum ppObjects)
        {
            return instance.Process5.TryEnumerateHeap(out ppObjects);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateHeap(Passenger.Debugger.Interop.ICorDebugHeapEnum@)" />
        public IEnumerable<COR_HEAPOBJECT> EnumerateHeap()
        {
            return instance.Process5.EnumerateHeap();
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateHeap(Passenger.Debugger.Interop.ICorDebugHeapEnum@)" />
        public COR_HEAPOBJECT[] Heap
        {
            get
            {
                return instance.Process5.EnumerateHeap().ToArray();
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateHeapRegions(Passenger.Debugger.Interop.ICorDebugHeapSegmentEnum@)" />
        public int TryEnumerateHeapRegions(out ICorDebugHeapSegmentEnum ppRegions)
        {
            return instance.Process5.TryEnumerateHeapRegions(out ppRegions);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateHeapRegions(Passenger.Debugger.Interop.ICorDebugHeapSegmentEnum@)" />
        public IEnumerable<COR_SEGMENT> EnumerateHeapRegions()
        {
            return instance.Process5.EnumerateHeapRegions();
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryEnumerateHeapRegions(Passenger.Debugger.Interop.ICorDebugHeapSegmentEnum@)" />
        public COR_SEGMENT[] HeapRegions
        {
            get
            {
                return instance.Process5.EnumerateHeapRegions().ToArray();
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetArrayLayout(Passenger.Debugger.Interop.COR_TYPEID,Passenger.Debugger.Interop.COR_ARRAY_LAYOUT@)" />
        public int TryGetArrayLayout(COR_TYPEID id, out COR_ARRAY_LAYOUT pLayout)
        {
            return instance.Process5.TryGetArrayLayout(id, out pLayout);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetArrayLayout(Passenger.Debugger.Interop.COR_TYPEID,Passenger.Debugger.Interop.COR_ARRAY_LAYOUT@)" />
        public COR_ARRAY_LAYOUT GetArrayLayout(COR_TYPEID id)
        {
            return instance.Process5.GetArrayLayout(id);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetGCHeapInformation(Passenger.Debugger.Interop.COR_HEAPINFO@)" />
        public int TryGetGCHeapInformation(out COR_HEAPINFO pHeapInfo)
        {
            return instance.Process5.TryGetGCHeapInformation(out pHeapInfo);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetGCHeapInformation(Passenger.Debugger.Interop.COR_HEAPINFO@)" />
        public COR_HEAPINFO GCHeapInformation
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Process5.TryGetGCHeapInformation(out var pHeapInfo));
                return pHeapInfo;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetObject(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.ICorDebugObjectValue@)" />
        public int TryGetObject(CORDB_ADDRESS addr, out ICorDebugObjectValue pObject)
        {
            return instance.Process5.TryGetObject(addr, out pObject);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetObject(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.ICorDebugObjectValue@)" />
        public ICorDebugObjectValue GetObject(CORDB_ADDRESS addr)
        {
            return instance.Process5.GetObject(addr);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetTypeFields(Passenger.Debugger.Interop.COR_TYPEID,System.UInt32,Passenger.Debugger.Interop.COR_FIELD[],System.UInt32@)" />
        public int TryGetTypeFields(COR_TYPEID id, uint celt, COR_FIELD[]? fields, out uint pceltNeeded)
        {
            return instance.Process5.TryGetTypeFields(id, celt, fields, out pceltNeeded);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetTypeForTypeID(Passenger.Debugger.Interop.COR_TYPEID,Passenger.Debugger.Interop.ICorDebugType@)" />
        public int TryGetTypeForTypeID(COR_TYPEID id, out ICorDebugType ppType)
        {
            return instance.Process5.TryGetTypeForTypeID(id, out ppType);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetTypeForTypeID(Passenger.Debugger.Interop.COR_TYPEID,Passenger.Debugger.Interop.ICorDebugType@)" />
        public ICorDebugType GetTypeForTypeID(COR_TYPEID id)
        {
            return instance.Process5.GetTypeForTypeID(id);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetTypeID(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.COR_TYPEID@)" />
        public int TryGetTypeID(CORDB_ADDRESS obj, out COR_TYPEID pId)
        {
            return instance.Process5.TryGetTypeID(obj, out pId);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetTypeID(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.COR_TYPEID@)" />
        public COR_TYPEID GetTypeID(CORDB_ADDRESS obj)
        {
            return instance.Process5.GetTypeID(obj);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetTypeLayout(Passenger.Debugger.Interop.COR_TYPEID,Passenger.Debugger.Interop.COR_TYPE_LAYOUT@)" />
        public int TryGetTypeLayout(COR_TYPEID id, out COR_TYPE_LAYOUT pLayout)
        {
            return instance.Process5.TryGetTypeLayout(id, out pLayout);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetTypeLayout(Passenger.Debugger.Interop.COR_TYPEID,Passenger.Debugger.Interop.COR_TYPE_LAYOUT@)" />
        public COR_TYPE_LAYOUT GetTypeLayout(COR_TYPEID id)
        {
            return instance.Process5.GetTypeLayout(id);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess5.TryGetTypeFields(Passenger.Debugger.Interop.COR_TYPEID,System.UInt32,Passenger.Debugger.Interop.COR_FIELD[],System.UInt32@)" />
        public COR_FIELD[] GetTypeFields(COR_TYPEID id)
        {
            Marshal.ThrowExceptionForHR(instance.Process5.TryGetTypeFields(id, 0u, null, out var pceltNeeded));
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
                    Marshal.ThrowExceptionForHR(instance.Process5.TryGetTypeFields(id, num, array, out var pceltNeeded2));
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
    }
}
