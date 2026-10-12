using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugStringValueExtensions
{
	extension(ICorDebugStringValue instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStringValue.TryGetString(System.UInt32,System.UInt32@,System.Char[])" />
		public string String
		{
			get
			{
				int num = get_Length(instance);
				if (num == 0)
				{
					return string.Empty;
				}
				char[] array = new char[num];
				int num2;
				checked
				{
					Marshal.ThrowExceptionForHR(instance.TryGetString((uint)num, out var pcchString, array));
					if (pcchString > num)
					{
						throw new InvalidOperationException("Native buffer size exceeded the reported capacity.");
					}
					num2 = (int)pcchString;
				}
				if (num2 > 0 && array[num2 - 1] == '\0')
				{
					num2--;
				}
				return new string(array, 0, num2);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStringValue.TryGetLength(System.UInt32@)" />
		public int Length
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetLength(out var pcchString));
				return checked((int)pcchString);
			}
		}
	}
}
