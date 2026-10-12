using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugInstanceFieldSymbolExtensions
{
	extension(ICorDebugInstanceFieldSymbol instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugInstanceFieldSymbol.TryGetName(System.UInt32,System.UInt32@,System.Char[])" />
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

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugInstanceFieldSymbol.TryGetOffset(System.UInt32@)" />
		public int Offset
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetOffset(out var pcbOffset));
				return checked((int)pcbOffset);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugInstanceFieldSymbol.TryGetSize(System.UInt32@)" />
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
