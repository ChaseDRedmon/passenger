using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugStaticFieldSymbolExtensions
{
	extension(ICorDebugStaticFieldSymbol instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStaticFieldSymbol.TryGetName(System.UInt32,System.UInt32@,System.Char[])" />
		public string Name
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetName(0u, out var pcchName, null));
				if (pcchName == 0)
				{
					return string.Empty;
				}
				for (int i = 0; i < 3; i++)
				{
					uint num = pcchName;
					char[] array;
					int num2;
					checked
					{
						array = new char[(int)num];
						Marshal.ThrowExceptionForHR(instance.TryGetName(num, out var pcchName2, array));
						if (pcchName2 > num)
						{
							pcchName = pcchName2;
							continue;
						}
						num2 = (int)pcchName2;
					}
					if (num2 > 0 && array[num2 - 1] == '\0')
					{
						num2--;
					}
					return new string(array, 0, num2);
				}
				throw new InvalidOperationException("Native buffer size did not stabilize.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStaticFieldSymbol.TryGetAddress(ICorDebugSharp.CORDB_ADDRESS@)" />
		public CORDB_ADDRESS Address
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetAddress(out var pRVA));
				return pRVA;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStaticFieldSymbol.TryGetSize(System.UInt32@)" />
		public int Size
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetSize(out var pcbSize));
				return checked((int)pcbSize);
			}
		}
	}
}
