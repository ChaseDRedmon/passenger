using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugAppDomainExtensions
{
	extension(ICorDebugAppDomain instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain.TryGetName(System.UInt32,System.UInt32@,System.Char[])" />
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

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain.TryEnumerateAssemblies(ICorDebugSharp.ICorDebugAssemblyEnum@)" />
		public IEnumerable<ICorDebugAssembly> EnumerateAssemblies()
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateAssemblies(out ICorDebugAssemblyEnum ppAssemblies));
			return EnumerateAssembliesCore(ppAssemblies);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain.TryEnumerateBreakpoints(ICorDebugSharp.ICorDebugBreakpointEnum@)" />
		public IEnumerable<ICorDebugBreakpoint> EnumerateBreakpoints()
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateBreakpoints(out ICorDebugBreakpointEnum ppBreakpoints));
			return EnumerateBreakpointsCore(ppBreakpoints);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain.TryEnumerateSteppers(ICorDebugSharp.ICorDebugStepperEnum@)" />
		public IEnumerable<ICorDebugStepper> EnumerateSteppers()
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateSteppers(out ICorDebugStepperEnum ppSteppers));
			return EnumerateSteppersCore(ppSteppers);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain.TryEnumerateAssemblies(ICorDebugSharp.ICorDebugAssemblyEnum@)" />
		public ICorDebugAssembly[] Assemblies
		{
			get
			{
				return instance.EnumerateAssemblies().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain.TryEnumerateBreakpoints(ICorDebugSharp.ICorDebugBreakpointEnum@)" />
		public ICorDebugBreakpoint[] Breakpoints
		{
			get
			{
				return instance.EnumerateBreakpoints().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain.TryEnumerateSteppers(ICorDebugSharp.ICorDebugStepperEnum@)" />
		public ICorDebugStepper[] Steppers
		{
			get
			{
				return instance.EnumerateSteppers().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain.TryGetID(System.UInt32@)" />
		public int Id
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetID(out var pId));
				return checked((int)pId);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain.TryGetModuleFromMetaDataInterface(System.IntPtr,ICorDebugSharp.ICorDebugModule@)" />
		public ICorDebugModule GetModuleFromMetaDataInterface(nint pIMetaData)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetModuleFromMetaDataInterface(pIMetaData, out ICorDebugModule ppModule));
			return ppModule;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain.TryGetObject(ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue Object
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetObject(out ICorDebugValue ppObject));
				return ppObject;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain.TryGetProcess(ICorDebugSharp.ICorDebugProcess@)" />
		public ICorDebugProcess Process
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetProcess(out ICorDebugProcess ppProcess));
				return ppProcess;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain.TryIsAttached(System.Boolean@)" />
		public bool IsAttached
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryIsAttached(out var pbAttached));
				return pbAttached;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain.TryAttach" />
		public void Attach()
		{
			Marshal.ThrowExceptionForHR(instance.TryAttach());
		}
	}

	private static IEnumerable<ICorDebugAssembly> EnumerateAssembliesCore(ICorDebugAssemblyEnum enumerator)
	{
		while (true)
		{
			ICorDebugAssembly[] array = new ICorDebugAssembly[1];
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

	private static IEnumerable<ICorDebugBreakpoint> EnumerateBreakpointsCore(ICorDebugBreakpointEnum enumerator)
	{
		while (true)
		{
			ICorDebugBreakpoint[] array = new ICorDebugBreakpoint[1];
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

	private static IEnumerable<ICorDebugStepper> EnumerateSteppersCore(ICorDebugStepperEnum enumerator)
	{
		while (true)
		{
			ICorDebugStepper[] array = new ICorDebugStepper[1];
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
