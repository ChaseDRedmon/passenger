#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugAppDomainExtensions
{
    extension(ICorDebugAppDomain instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain.TryEnumerateSteppers(Passenger.Debugger.Interop.ICorDebugStepperEnum@)" />
        public IEnumerable<ICorDebugStepper> EnumerateSteppers()
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateSteppers(out ICorDebugStepperEnum ppSteppers));
            return EnumerateSteppersCore(ppSteppers);
        }
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
