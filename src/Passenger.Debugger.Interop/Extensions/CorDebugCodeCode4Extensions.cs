using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugCodeCode4Extensions
{
    extension(ICorDebugCode instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugCode4" />
        public bool SupportsCode4
        {
            get
            {
                return instance is ICorDebugCode4;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugCode4" />
        public ICorDebugCode4 Code4
        {
            get
            {
                return (instance as ICorDebugCode4) ?? throw new NotSupportedException("ICorDebugCode does not support ICorDebugCode4.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode4.TryEnumerateVariableHomes(Passenger.Debugger.Interop.ICorDebugVariableHomeEnum@)" />
        public int TryEnumerateVariableHomes(out ICorDebugVariableHomeEnum ppEnum)
        {
            return instance.Code4.TryEnumerateVariableHomes(out ppEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode4.TryEnumerateVariableHomes(Passenger.Debugger.Interop.ICorDebugVariableHomeEnum@)" />
        public IEnumerable<ICorDebugVariableHome> EnumerateVariableHomes()
        {
            return instance.Code4.EnumerateVariableHomes();
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode4.TryEnumerateVariableHomes(Passenger.Debugger.Interop.ICorDebugVariableHomeEnum@)" />
        public ICorDebugVariableHome[] VariableHomes
        {
            get
            {
                return instance.Code4.EnumerateVariableHomes().ToArray();
            }
        }
    }
}
