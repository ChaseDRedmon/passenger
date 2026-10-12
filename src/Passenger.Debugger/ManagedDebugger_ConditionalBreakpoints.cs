using System.Runtime.InteropServices;
using Passenger.Debugger.ExpressionEvaluator;
using Passenger.Debugger.ExpressionEvaluator.Cil;
using Passenger.Debugger.ExpressionEvaluator.Compiler;
using Passenger.Debugger.Interop;

namespace Passenger.Debugger;

public partial class ManagedDebugger
{
    private async Task<bool> EvaluateBreakpointCondition(ICorDebugThread corThread, string condition)
    {
        try
        {
            ThreadId threadId = new ThreadId(corThread.Id);
            FrameStackDepth frameStackDepth = new FrameStackDepth(0); // Top frame

            CompiledExpressionEvaluationContext evalContext = new CompiledExpressionEvaluationContext(corThread, threadId, frameStackDepth);
            using EvaluationResult result = await _expressionEvaluator.Evaluate(condition, evalContext);

            if (result.Error is not null)
            {
                _logger?.Invoke($"Condition evaluation error for '{condition}': {result.Error}");
                return false; // Don't stop on error - condition couldn't be evaluated, so skip the breakpoint
            }

            return IsTruthyValue(result.Value);
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Exception evaluating condition '{condition}': {ex.Message}");
            return false; // Don't stop on exception - condition couldn't be evaluated, so skip the breakpoint
        }
    }

    private static bool EvaluateHitCondition(int hitCount, string hitCondition)
    {
        // Support common hit count formats:
        // "10" or "==10" - break when hit count equals 10
        // ">=10" - break when hit count is >= 10
        // ">10" - break when hit count is > 10
        // "%10" - break every 10th hit (modulo)

        hitCondition = hitCondition.Trim();
        ReadOnlySpan<char> hitConditionSpan = hitCondition.AsSpan();
        return hitConditionSpan switch
        {
            ['>', '=', ..] => int.TryParse(hitConditionSpan[2..], out int threshold) && hitCount >= threshold,
            ['>', ..] => int.TryParse(hitConditionSpan[1..], out int threshold) && hitCount > threshold,
            ['<', '=', ..] => int.TryParse(hitConditionSpan[2..], out int threshold) && hitCount <= threshold,
            ['<', ..] => int.TryParse(hitConditionSpan[1..], out int threshold) && hitCount < threshold,
            ['%', ..] => int.TryParse(hitConditionSpan[1..], out int modulo) && modulo > 0 && hitCount % modulo == 0,
            ['=', '=', ..] => int.TryParse(hitConditionSpan[2..], out int target) && hitCount == target,
            _ => int.TryParse(hitConditionSpan, out int target) && hitCount == target // Plain number means break when hit count = number
        };
    }

    /// <summary>
    /// Check if a debug value is truthy (true, non-zero, non-null)
    /// </summary>
    private static bool IsTruthyValue(ICorDebugValue? value)
    {
        if (value is null)
        {
            return false;
        }

        ICorDebugValue unwrapped = value.UnwrapDebugValue();

        if (unwrapped is ICorDebugGenericValue genericValue)
        {
            nint buffer = Marshal.AllocHGlobal(genericValue.Size);
            try
            {
                genericValue.GetValue(buffer);
                return genericValue.Type switch
                {
                    CorElementType.BOOLEAN => Marshal.ReadByte(buffer) != 0,
                    CorElementType.I1 or CorElementType.U1 => Marshal.ReadByte(buffer) != 0,
                    CorElementType.I2 or CorElementType.U2 => Marshal.ReadInt16(buffer) != 0,
                    CorElementType.I4 or CorElementType.U4 => Marshal.ReadInt32(buffer) != 0,
                    CorElementType.I8 or CorElementType.U8 => Marshal.ReadInt64(buffer) != 0,
                    CorElementType.R4 => BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(buffer)), 0) != 0,
                    CorElementType.R8 => BitConverter.ToDouble(BitConverter.GetBytes(Marshal.ReadInt64(buffer)), 0) != 0,
                    _ => true // Unknown types - default to true
                };
            }
            catch
            {
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        if (unwrapped is ICorDebugReferenceValue refValue)
        {
            return !refValue.IsNull;
        }

        return true;
    }
}
