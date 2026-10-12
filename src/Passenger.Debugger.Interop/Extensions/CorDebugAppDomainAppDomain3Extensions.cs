using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugAppDomainAppDomain3Extensions
{
	extension(ICorDebugAppDomain instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugAppDomain3" />
		public bool SupportsAppDomain3
		{
			get
			{
				return instance is ICorDebugAppDomain3;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugAppDomain3" />
		public ICorDebugAppDomain3 AppDomain3
		{
			get
			{
				return (instance as ICorDebugAppDomain3) ?? throw new NotSupportedException("ICorDebugAppDomain does not support ICorDebugAppDomain3.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain3.TryGetCachedWinRTTypes(ICorDebugSharp.ICorDebugGuidToTypeEnum@)" />
		public int TryGetCachedWinRTTypes(out ICorDebugGuidToTypeEnum ppGuidToTypeEnum)
		{
			return get_AppDomain3(instance).TryGetCachedWinRTTypes(out ppGuidToTypeEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain3.TryGetCachedWinRTTypes(ICorDebugSharp.ICorDebugGuidToTypeEnum@)" />
		public ICorDebugGuidToTypeEnum CachedWinRTTypes
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_AppDomain3(instance).TryGetCachedWinRTTypes(out ICorDebugGuidToTypeEnum ppGuidToTypeEnum));
				return ppGuidToTypeEnum;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain3.TryGetCachedWinRTTypesForIIDs(System.UInt32,System.Guid[],ICorDebugSharp.ICorDebugTypeEnum@)" />
		public int TryGetCachedWinRTTypesForIIDs(uint cReqTypes, Guid[] iidsToResolve, out ICorDebugTypeEnum ppTypesEnum)
		{
			return get_AppDomain3(instance).TryGetCachedWinRTTypesForIIDs(cReqTypes, iidsToResolve, out ppTypesEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain3.TryGetCachedWinRTTypesForIIDs(System.UInt32,System.Guid[],ICorDebugSharp.ICorDebugTypeEnum@)" />
		public IEnumerable<ICorDebugType> GetCachedWinRTTypesForIIDs(int cReqTypes, Guid[] iidsToResolve)
		{
			return get_AppDomain3(instance).GetCachedWinRTTypesForIIDs(cReqTypes, iidsToResolve);
		}
	}
}
