using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugMDAExtensions
{
	extension(ICorDebugMDA instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugMDA.TryGetName(System.UInt32,System.UInt32@,System.Char[])" />
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

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugMDA.TryGetDescription(System.UInt32,System.UInt32@,System.Char[])" />
		public string Description
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetDescription(0u, out var pcchName, null));
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
						Marshal.ThrowExceptionForHR(instance.TryGetDescription(num, out var pcchName2, array));
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

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugMDA.TryGetXML(System.UInt32,System.UInt32@,System.Char[])" />
		public string XML
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetXML(0u, out var pcchName, null));
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
						Marshal.ThrowExceptionForHR(instance.TryGetXML(num, out var pcchName2, array));
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

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugMDA.TryGetOSThreadId(System.UInt32@)" />
		public int OSThreadId
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetOSThreadId(out var pOsTid));
				return checked((int)pOsTid);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugMDA.TryGetFlags(ICorDebugSharp.CorDebugMDAFlags@)" />
		public void GetFlags(ref CorDebugMDAFlags pFlags)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetFlags(ref pFlags));
		}
	}
}
