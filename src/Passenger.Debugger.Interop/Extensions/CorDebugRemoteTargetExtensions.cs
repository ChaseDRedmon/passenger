using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugRemoteTargetExtensions
{
	extension(ICorDebugRemoteTarget instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugRemoteTarget.TryGetHostName(System.UInt32,System.UInt32@,System.Char[])" />
		public string HostName
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetHostName(0u, out var pcchHostName, null));
				if (pcchHostName == 0)
				{
					return string.Empty;
				}
				for (int i = 0; i < 3; i++)
				{
					uint num = pcchHostName;
					char[] array;
					int num2;
					checked
					{
						array = new char[(int)num];
						Marshal.ThrowExceptionForHR(instance.TryGetHostName(num, out var pcchHostName2, array));
						if (pcchHostName2 > num)
						{
							pcchHostName = pcchHostName2;
							continue;
						}
						num2 = (int)pcchHostName2;
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
	}
}
