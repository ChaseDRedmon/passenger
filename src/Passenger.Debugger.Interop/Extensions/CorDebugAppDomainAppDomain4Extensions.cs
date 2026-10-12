using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugAppDomainAppDomain4Extensions
{
	extension(ICorDebugAppDomain instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugAppDomain4" />
		public bool SupportsAppDomain4
		{
			get
			{
				return instance is ICorDebugAppDomain4;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugAppDomain4" />
		public ICorDebugAppDomain4 AppDomain4
		{
			get
			{
				return (instance as ICorDebugAppDomain4) ?? throw new NotSupportedException("ICorDebugAppDomain does not support ICorDebugAppDomain4.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain4.TryGetObjectForCCW(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.ICorDebugValue@)" />
		public int TryGetObjectForCCW(CORDB_ADDRESS ccwPointer, out ICorDebugValue ppManagedObject)
		{
			return get_AppDomain4(instance).TryGetObjectForCCW(ccwPointer, out ppManagedObject);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain4.TryGetObjectForCCW(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetObjectForCCW(CORDB_ADDRESS ccwPointer)
		{
			return get_AppDomain4(instance).GetObjectForCCW(ccwPointer);
		}
	}
}
