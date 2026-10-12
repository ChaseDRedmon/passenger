using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugObjectValueExtensions
{
	extension(ICorDebugObjectValue instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugObjectValue.TryGetClass(ICorDebugSharp.ICorDebugClass@)" />
		public ICorDebugClass Class
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetClass(out ICorDebugClass ppClass));
				return ppClass;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugObjectValue.TryGetContext(ICorDebugSharp.ICorDebugContext@)" />
		public ICorDebugContext Context
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetContext(out ICorDebugContext ppContext));
				return ppContext;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugObjectValue.TryGetFieldValue(ICorDebugSharp.ICorDebugClass,ICorDebugSharp.mdFieldDef,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetFieldValue(ICorDebugClass pClass, mdFieldDef fieldDef)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetFieldValue(pClass, fieldDef, out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugObjectValue.TryGetManagedCopy(System.IntPtr@)" />
		public nint ManagedCopy
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetManagedCopy(out var ppObject));
				return ppObject;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugObjectValue.TryGetVirtualMethod(ICorDebugSharp.mdMemberRef,ICorDebugSharp.ICorDebugFunction@)" />
		public ICorDebugFunction GetVirtualMethod(mdMemberRef memberRef)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetVirtualMethod(memberRef, out ICorDebugFunction ppFunction));
			return ppFunction;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugObjectValue.TryIsValueClass(System.Boolean@)" />
		public bool IsValueClass
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryIsValueClass(out var pbIsValueClass));
				return pbIsValueClass;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugObjectValue.TrySetFromManagedCopy(System.IntPtr)" />
		public void SetFromManagedCopy(nint pObject)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetFromManagedCopy(pObject));
		}
	}
}
