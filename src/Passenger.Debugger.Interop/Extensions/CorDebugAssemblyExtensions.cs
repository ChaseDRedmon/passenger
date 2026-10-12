using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugAssemblyExtensions
{
	extension(ICorDebugAssembly instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly.TryGetCodeBase(System.UInt32,System.UInt32@,System.Char[])" />
		public string CodeBase
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetCodeBase(0u, out var pcchName, null));
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
						Marshal.ThrowExceptionForHR(instance.TryGetCodeBase(num, out var pcchName2, array));
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

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly.TryGetName(System.UInt32,System.UInt32@,System.Char[])" />
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

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly.TryEnumerateModules(ICorDebugSharp.ICorDebugModuleEnum@)" />
		public IEnumerable<ICorDebugModule> EnumerateModules()
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateModules(out ICorDebugModuleEnum ppModules));
			return EnumerateModulesCore(ppModules);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly.TryEnumerateModules(ICorDebugSharp.ICorDebugModuleEnum@)" />
		public ICorDebugModule[] Modules
		{
			get
			{
				return instance.EnumerateModules().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly.TryGetAppDomain(ICorDebugSharp.ICorDebugAppDomain@)" />
		public ICorDebugAppDomain AppDomain
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetAppDomain(out ICorDebugAppDomain ppAppDomain));
				return ppAppDomain;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly.TryGetProcess(ICorDebugSharp.ICorDebugProcess@)" />
		public ICorDebugProcess Process
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetProcess(out ICorDebugProcess ppProcess));
				return ppProcess;
			}
		}
	}

	private static IEnumerable<ICorDebugModule> EnumerateModulesCore(ICorDebugModuleEnum enumerator)
	{
		while (true)
		{
			ICorDebugModule[] array = new ICorDebugModule[1];
			int errorCode = enumerator.TryNext(1u, array, out var pceltFetched);
			if (pceltFetched == 0)
			{
				yield break;
			}
			Marshal.ThrowExceptionForHR(errorCode);
			if (pceltFetched != 1)
			{
				break;
			}
			yield return array[0];
		}
		throw new InvalidOperationException("Native debugger enumerator returned an invalid item count.");
	}
}
