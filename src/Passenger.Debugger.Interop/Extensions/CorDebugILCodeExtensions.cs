using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugILCodeExtensions
{
    extension(ICorDebugILCode instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILCode.TryGetEHClauses(System.UInt32,System.UInt32@,Passenger.Debugger.Interop.CorDebugEHClause[])" />
        public CorDebugEHClause[] EHClauses
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetEHClauses(0u, out var pcClauses, null));
                if (pcClauses == 0)
                {
                    return Array.Empty<CorDebugEHClause>();
                }
                for (int i = 0; i < 3; i++)
                {
                    uint num = pcClauses;
                    checked
                    {
                        CorDebugEHClause[] array = new CorDebugEHClause[(int)num];
                        Marshal.ThrowExceptionForHR(instance.TryGetEHClauses(num, out var pcClauses2, array));
                        if (pcClauses2 > num)
                        {
                            pcClauses = pcClauses2;
                            continue;
                        }
                        if (pcClauses2 != num)
                        {
                            Array.Resize(ref array, (int)pcClauses2);
                        }
                        return array;
                    }
                }
                throw new InvalidOperationException("Native buffer size did not stabilize.");
            }
        }
    }
}
