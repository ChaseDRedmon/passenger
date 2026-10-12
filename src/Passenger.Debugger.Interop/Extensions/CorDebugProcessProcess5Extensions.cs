using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcessProcess5Extensions
{
	extension(ICorDebugProcess instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess5" />
		public bool SupportsProcess5
		{
			get
			{
				return instance is ICorDebugProcess5;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess5" />
		public ICorDebugProcess5 Process5
		{
			get
			{
				return (instance as ICorDebugProcess5) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess5.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnableNGENPolicy(ICorDebugSharp.CorDebugNGENPolicy)" />
		public int TryEnableNGENPolicy(CorDebugNGENPolicy ePolicy)
		{
			return get_Process5(instance).TryEnableNGENPolicy(ePolicy);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnableNGENPolicy(ICorDebugSharp.CorDebugNGENPolicy)" />
		public void EnableNGENPolicy(CorDebugNGENPolicy ePolicy)
		{
			get_Process5(instance).EnableNGENPolicy(ePolicy);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnumerateGCReferences(System.Boolean,ICorDebugSharp.ICorDebugGCReferenceEnum@)" />
		public int TryEnumerateGCReferences(bool enumerateWeakReferences, out ICorDebugGCReferenceEnum ppEnum)
		{
			return get_Process5(instance).TryEnumerateGCReferences(enumerateWeakReferences, out ppEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnumerateGCReferences(System.Boolean,ICorDebugSharp.ICorDebugGCReferenceEnum@)" />
		public IEnumerable<COR_GC_REFERENCE> EnumerateGCReferences(bool enumerateWeakReferences)
		{
			return get_Process5(instance).EnumerateGCReferences(enumerateWeakReferences);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnumerateHandles(ICorDebugSharp.CorGCReferenceType,ICorDebugSharp.ICorDebugGCReferenceEnum@)" />
		public int TryEnumerateHandles(CorGCReferenceType types, out ICorDebugGCReferenceEnum ppEnum)
		{
			return get_Process5(instance).TryEnumerateHandles(types, out ppEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnumerateHandles(ICorDebugSharp.CorGCReferenceType,ICorDebugSharp.ICorDebugGCReferenceEnum@)" />
		public IEnumerable<COR_GC_REFERENCE> EnumerateHandles(CorGCReferenceType types)
		{
			return get_Process5(instance).EnumerateHandles(types);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnumerateHeap(ICorDebugSharp.ICorDebugHeapEnum@)" />
		public int TryEnumerateHeap(out ICorDebugHeapEnum ppObjects)
		{
			return get_Process5(instance).TryEnumerateHeap(out ppObjects);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnumerateHeap(ICorDebugSharp.ICorDebugHeapEnum@)" />
		public IEnumerable<COR_HEAPOBJECT> EnumerateHeap()
		{
			return get_Process5(instance).EnumerateHeap();
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnumerateHeap(ICorDebugSharp.ICorDebugHeapEnum@)" />
		public COR_HEAPOBJECT[] Heap
		{
			get
			{
				return get_Process5(instance).EnumerateHeap().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnumerateHeapRegions(ICorDebugSharp.ICorDebugHeapSegmentEnum@)" />
		public int TryEnumerateHeapRegions(out ICorDebugHeapSegmentEnum ppRegions)
		{
			return get_Process5(instance).TryEnumerateHeapRegions(out ppRegions);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnumerateHeapRegions(ICorDebugSharp.ICorDebugHeapSegmentEnum@)" />
		public IEnumerable<COR_SEGMENT> EnumerateHeapRegions()
		{
			return get_Process5(instance).EnumerateHeapRegions();
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryEnumerateHeapRegions(ICorDebugSharp.ICorDebugHeapSegmentEnum@)" />
		public COR_SEGMENT[] HeapRegions
		{
			get
			{
				return get_Process5(instance).EnumerateHeapRegions().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetArrayLayout(ICorDebugSharp.COR_TYPEID,ICorDebugSharp.COR_ARRAY_LAYOUT@)" />
		public int TryGetArrayLayout(COR_TYPEID id, out COR_ARRAY_LAYOUT pLayout)
		{
			return get_Process5(instance).TryGetArrayLayout(id, out pLayout);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetArrayLayout(ICorDebugSharp.COR_TYPEID,ICorDebugSharp.COR_ARRAY_LAYOUT@)" />
		public COR_ARRAY_LAYOUT GetArrayLayout(COR_TYPEID id)
		{
			return get_Process5(instance).GetArrayLayout(id);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetGCHeapInformation(ICorDebugSharp.COR_HEAPINFO@)" />
		public int TryGetGCHeapInformation(out COR_HEAPINFO pHeapInfo)
		{
			return get_Process5(instance).TryGetGCHeapInformation(out pHeapInfo);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetGCHeapInformation(ICorDebugSharp.COR_HEAPINFO@)" />
		public COR_HEAPINFO GCHeapInformation
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Process5(instance).TryGetGCHeapInformation(out var pHeapInfo));
				return pHeapInfo;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetObject(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.ICorDebugObjectValue@)" />
		public int TryGetObject(CORDB_ADDRESS addr, out ICorDebugObjectValue pObject)
		{
			return get_Process5(instance).TryGetObject(addr, out pObject);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetObject(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.ICorDebugObjectValue@)" />
		public ICorDebugObjectValue GetObject(CORDB_ADDRESS addr)
		{
			return get_Process5(instance).GetObject(addr);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetTypeFields(ICorDebugSharp.COR_TYPEID,System.UInt32,ICorDebugSharp.COR_FIELD[],System.UInt32@)" />
		public int TryGetTypeFields(COR_TYPEID id, uint celt, COR_FIELD[]? fields, out uint pceltNeeded)
		{
			return get_Process5(instance).TryGetTypeFields(id, celt, fields, out pceltNeeded);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetTypeForTypeID(ICorDebugSharp.COR_TYPEID,ICorDebugSharp.ICorDebugType@)" />
		public int TryGetTypeForTypeID(COR_TYPEID id, out ICorDebugType ppType)
		{
			return get_Process5(instance).TryGetTypeForTypeID(id, out ppType);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetTypeForTypeID(ICorDebugSharp.COR_TYPEID,ICorDebugSharp.ICorDebugType@)" />
		public ICorDebugType GetTypeForTypeID(COR_TYPEID id)
		{
			return get_Process5(instance).GetTypeForTypeID(id);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetTypeID(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.COR_TYPEID@)" />
		public int TryGetTypeID(CORDB_ADDRESS obj, out COR_TYPEID pId)
		{
			return get_Process5(instance).TryGetTypeID(obj, out pId);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetTypeID(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.COR_TYPEID@)" />
		public COR_TYPEID GetTypeID(CORDB_ADDRESS obj)
		{
			return get_Process5(instance).GetTypeID(obj);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetTypeLayout(ICorDebugSharp.COR_TYPEID,ICorDebugSharp.COR_TYPE_LAYOUT@)" />
		public int TryGetTypeLayout(COR_TYPEID id, out COR_TYPE_LAYOUT pLayout)
		{
			return get_Process5(instance).TryGetTypeLayout(id, out pLayout);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetTypeLayout(ICorDebugSharp.COR_TYPEID,ICorDebugSharp.COR_TYPE_LAYOUT@)" />
		public COR_TYPE_LAYOUT GetTypeLayout(COR_TYPEID id)
		{
			return get_Process5(instance).GetTypeLayout(id);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetTypeFields(ICorDebugSharp.COR_TYPEID,System.UInt32,ICorDebugSharp.COR_FIELD[],System.UInt32@)" />
		public COR_FIELD[] GetTypeFields(COR_TYPEID id)
		{
			Marshal.ThrowExceptionForHR(get_Process5(instance).TryGetTypeFields(id, 0u, null, out var pceltNeeded));
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
					Marshal.ThrowExceptionForHR(get_Process5(instance).TryGetTypeFields(id, num, array, out var pceltNeeded2));
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
