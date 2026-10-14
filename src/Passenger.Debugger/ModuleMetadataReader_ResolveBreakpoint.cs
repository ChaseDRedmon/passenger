using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Passenger.Debugger.ExpressionEvaluator.Cil;

namespace Passenger.Debugger;
public partial class ModuleMetadataReader
{
    private (DocumentHandle Document, int Score) BestDocument(string sourceFilePath)
    {
        MetadataReader? reader = _pdbMetadataReader;
        (DocumentHandle Document, int Score) best = (default, 0);
        if (reader is null)
        {
            return best;
        }
        foreach (DocumentHandle handle in reader.Documents)
        {
            int score = SourcePaths.Match(sourceFilePath, reader.GetString(reader.GetDocument(handle).Name));
            if (score > best.Score)
            {
                best = (handle, score);
            }
        }
        return best;
    }

    private sealed record MethodCandidate(
    int Token,
    SequencePoint FirstSp,      // smallest End where EndLine >= line (next-line snapping)
    SequencePoint LastSp,       // largest End where EndLine >= line
    SequencePoint? CoveringSp,  // latest Start where the requested source position is covered
    string DocPath)
    {
        public bool CoversLine => CoveringSp.HasValue;
    }

    /// <summary>How well this module's symbols name a source file (<see cref="SourcePaths.Match"/>):
    /// 0 when no document of its is that file.</summary>
    public int DocumentMatch(string sourceFilePath) => BestDocument(sourceFilePath).Score;

    public ResolvedBreakpoint? ResolveBreakpoint(string sourceFilePath, int line, int? column = null)
    {
        MetadataReader? reader = _pdbMetadataReader;
        if (reader is null)
        {
            return null;
        }

        DocumentHandle documentHandle = BestDocument(sourceFilePath).Document;

        if (documentHandle.IsNil)
        {
            return null;
        }

        List<MethodCandidate> candidates = reader.MethodDebugInformation
            .Select(h => CollectCandidate(h, documentHandle, line, column))
            .OfType<MethodCandidate>()
            .ToList();
        bool rewritten = IsRewrittenDocument(documentHandle);
        if (rewritten && column is int requested && !candidates.Any(c => c.CoversLine) && UnpointedAttribute(documentHandle, new LineCol(line, requested)) is { } setter)
        {
            ResolvedBreakpoint attributed = new(setter.Token, setter.Point.Offset, line, line, requested, requested, reader.GetString(reader.GetDocument(documentHandle).Name)) { Attributed = true };
            return attributed with { BindOffset = JitBoundaryAtOrBefore(attributed.MethodToken, attributed.ILOffset) };
        }
        ResolvedBreakpoint? resolved = Resolve(candidates, rewritten);
        return resolved is not null && rewritten ? resolved with { BindOffset = JitBoundaryAtOrBefore(resolved.MethodToken, resolved.ILOffset) } : resolved;
    }

    /// <summary>Where the setter of an XAML attribute that XamlIl gave no point of its own runs: XamlX keeps one point
    /// pending, so of a tag's attributes about every other one has none, its setter run under its element's point again,
    /// which comes in the IL right after the point of the attribute before it. That point is the one written last before the
    /// attribute (a template's builder's over the template's own, which begins its method); the point after it in the IL
    /// when that goes back to the element, else it itself. Null when nothing is written before the attribute.</summary>
    private (int Token, SequencePoint Point)? UnpointedAttribute(DocumentHandle document, LineCol requested)
    {
        MetadataReader reader = _pdbMetadataReader!;
        (int Token, SequencePoint Point)? prior = null;
        foreach (MethodDebugInformationHandle handle in reader.MethodDebugInformation)
        {
            MethodDebugInformation info = reader.GetMethodDebugInformation(handle);
            if (info.SequencePointsBlob.IsNil)
            {
                continue;
            }
            foreach (SequencePoint point in info.GetSequencePoints())
            {
                LineCol at = point.Start();
                if (point.IsHidden || (point.Document.IsNil ? info.Document : point.Document) != document || at > requested)
                {
                    continue;
                }
                if (prior is not { } best || at > best.Point.Start() || (at == best.Point.Start() && point.Offset < best.Point.Offset))
                {
                    prior = (MetadataTokens.GetToken(handle.ToDefinitionHandle()), point);
                }
            }
        }
        if (prior is not { } found)
        {
            return null;
        }
        SequencePoint? next = VisibleSequencePoints(found.Token).FirstOrDefault(p => p.Offset > found.Point.Offset) is { IsHidden: false } after && after.Offset > found.Point.Offset ? after : null;
        return next is { } back && back.Start() < found.Point.Start() ? (found.Token, back) : found;
    }

    /// <summary>Whether a document's sequence points were written by a rewriter of the compiled IL rather than a compiler:
    /// Avalonia's XAML compiler (XamlIl, through Cecil) names no language for the .axaml files it maps its code to, and
    /// places a point at each element and attribute, mid-expression, where C#'s compiler places them where the stack is
    /// empty.</summary>
    private bool IsRewrittenDocument(DocumentHandle document)
    {
        MetadataReader reader = _pdbMetadataReader!;
        GuidHandle language = reader.GetDocument(document).Language;
        return language.IsNil || reader.GetGuid(language) == Guid.Empty;
    }

    /// <summary>The nearest IL offset at or before <paramref name="offset"/> that CoreCLR's JIT maps to native code for a
    /// debugger: the method's start, a nop, or the instruction after a call (its call-site boundaries; it also maps where the
    /// stack is empty, which needs no move). A breakpoint anywhere else binds and is never hit. The instructions passed over
    /// call nothing, so the program stops in the same state, only those pushes before. Never moved back over a branch's
    /// target or a jump away, where another path would miss it; then the offset is kept as it is.</summary>
    internal int JitBoundaryAtOrBefore(int methodToken, int offset)
    {
        if (_peReader is null)
        {
            return offset;
        }
        MethodDefinition method = _peMetadataReader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(methodToken));
        if (method.RelativeVirtualAddress == 0 || _peReader.GetMethodBody(method.RelativeVirtualAddress).GetILBytes() is not { } il)
        {
            return offset;
        }
        IReadOnlyList<CilInstruction> instructions;
        try
        {
            instructions = CilInstructionDecoder.Decode(il);
        }
        catch (NotSupportedException)
        {
            return offset;
        }
        HashSet<int> targets = [];
        foreach (CilInstruction instruction in instructions)
        {
            switch (instruction.Operand)
            {
                case int target when instruction.OpCode.OperandType is System.Reflection.Emit.OperandType.InlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineBrTarget:
                    targets.Add(target);
                    break;
                case int[] cases:
                    targets.UnionWith(cases);
                    break;
            }
        }
        int at = instructions.Count - 1;
        while (at >= 0 && instructions[at].Offset > offset)
        {
            at--;
        }
        if (at < 0 || instructions[at].Offset != offset)
        {
            return offset;
        }
        for (int i = at; i >= 0; i--)
        {
            CilInstruction here = instructions[i];
            if (i == 0 || here.Code == ILOpCode.Nop || instructions[i - 1].Code is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Calli)
            {
                return here.Offset;
            }
            if (targets.Contains(here.Offset) || instructions[i - 1].Code is ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Leave or ILOpCode.Leave_s
                or ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow or ILOpCode.Endfinally or ILOpCode.Jmp)
            {
                return offset;
            }
        }
        return offset;
    }

    private ResolvedBreakpoint? Resolve(List<MethodCandidate> candidates, bool rewritten)
    {
        List<MethodCandidate> covering = candidates.Where(c => c.CoversLine).ToList();

        if (covering.Count == 0)
        {
            return candidates
                .OrderBy(c => c.FirstSp.Start())
                .Select(c => NewResolvedBreakpoint(c.Token, c.FirstSp, c.DocPath))
                .FirstOrDefault();
        }

        if (covering.Count == 1)
        {
            return NewResolvedBreakpoint(covering[0].Token, covering[0].CoveringSp!.Value, covering[0].DocPath);
        }

        // Keep only the candidates whose covering SP starts latest — i.e. whose SP most
        // specifically matches the target line from above or at.  This naturally picks the
        // innermost lambda when the enclosing method only covers the line via a large
        // spanning SP (e.g. a delegate-assignment SP that spans the whole lambda body),
        // without needing explicit case analysis.
        LineCol maxCoverStart = covering.Max(c => c.CoveringSp!.Value.Start());
        List<MethodCandidate> primary = covering.Where(c => c.CoveringSp!.Value.Start() == maxCoverStart).ToList();

        if (primary.Count == 1)
        {
            return NewResolvedBreakpoint(primary[0].Token, primary[0].CoveringSp!.Value, primary[0].DocPath);
        }

        if (rewritten)
        {
            // XamlIl maps a template's root element twice: where the template is made (its deferred content, mid-method)
            // and where it builds its content, the method its point begins. The builder's runs for each control made.
            MethodCandidate builder = primary.MinBy(c => c.CoveringSp!.Value.Offset)!;
            return NewResolvedBreakpoint(builder.Token, builder.CoveringSp!.Value, builder.DocPath);
        }

        // https://github.com/Samsung/netcoredbg/blob/8b8b22200fecdb1aec5f47af63215462d8c79a4b/src/managed/SymbolReader.cs#L801-L817

        // Only reach here when multiple methods have a covering SP starting at the exact
        // same source position — the same-line lambda case (e.g. items.Select(i => i * 2)).
        // Apply netcoredbg's two-case containment check for that narrow scenario.
        List<MethodCandidate> sorted = primary.OrderBy(c => c.FirstSp.Start()).ToList();
        MethodCandidate outer = sorted[^2];
        MethodCandidate nested = sorted[^1];

        // Case 1: lambda range fully inside outer's first SP → BP is on the call-site line
        if (nested.FirstSp.Start() > outer.FirstSp.Start() &&
            nested.LastSp.End() < outer.FirstSp.End())
        {
            return NewResolvedBreakpoint(outer.Token, outer.CoveringSp!.Value, outer.DocPath);
        }

        // Case 2: outer's first SP ends after nested's -> BP is closer to the lambda body
        if (outer.FirstSp.End() > nested.FirstSp.End())
        {
            return NewResolvedBreakpoint(nested.Token, nested.CoveringSp!.Value, nested.DocPath);
        }

        return NewResolvedBreakpoint(outer.Token, outer.CoveringSp!.Value, outer.DocPath);
    }

    private MethodCandidate? CollectCandidate(MethodDebugInformationHandle handle, DocumentHandle docHandle, int line, int? column)
    {
        MetadataReader reader = _pdbMetadataReader ?? throw new InvalidOperationException("No symbols are loaded.");
        MethodDebugInformation info = reader.GetMethodDebugInformation(handle);
        if (info.SequencePointsBlob.IsNil)
        {
            return null;
        }

        SequencePoint? firstSP = null;
        SequencePoint? lastSP = null;
        SequencePoint? coveringSP = null;
        string? docPath = null;

        foreach (SequencePoint sp in info.GetSequencePoints())
        {
            if (sp.IsHidden)
            {
                continue;
            }

            DocumentHandle spDoc = sp.Document.IsNil ? info.Document : sp.Document;
            if (spDoc != docHandle || sp.IsBeforeRequestedPosition(line, column))
            {
                continue;
            }

            docPath ??= reader.GetString(reader.GetDocument(spDoc).Name);

            if (firstSP is null || sp.End() < firstSP.Value.End())
            {
                firstSP = sp;
            }

            if (lastSP is null || sp.End() > lastSP.Value.End())
            {
                lastSP = sp;
            }

            if (sp.CoversRequestedPosition(line, column) && sp.ShouldReplaceCoveringSequencePoint(coveringSP, column))
            {
                coveringSP = sp;
            }
        }

        if (firstSP is null)
        {
            return null;
        }

        return new MethodCandidate(MetadataTokens.GetToken(handle.ToDefinitionHandle()), firstSP.Value, lastSP!.Value, coveringSP, docPath!);
    }
    private static ResolvedBreakpoint NewResolvedBreakpoint(int token, SequencePoint sp, string docPath) =>
        new(token, sp.Offset, sp.StartLine, sp.EndLine, sp.StartColumn, sp.EndColumn, docPath);
}

internal readonly record struct LineCol(int Line, int Column) : IComparable<LineCol>
{
    public int CompareTo(LineCol other)
    {
        int c = Line.CompareTo(other.Line);
        return c != 0 ? c : Column.CompareTo(other.Column);
    }
    public static bool operator <(LineCol a, LineCol b) => a.CompareTo(b) < 0;
    public static bool operator >(LineCol a, LineCol b) => a.CompareTo(b) > 0;
    public static bool operator <=(LineCol a, LineCol b) => a.CompareTo(b) <= 0;
    public static bool operator >=(LineCol a, LineCol b) => a.CompareTo(b) >= 0;
}

file static class LineColExtensions
{
    public static LineCol Start(this SequencePoint sp) => new(sp.StartLine, sp.StartColumn);
    public static LineCol End(this SequencePoint sp) => new(sp.EndLine, sp.EndColumn);
}
file static class SequencePointExtensions
{
    public static bool IsBeforeRequestedPosition(this SequencePoint sp, int requestedLine, int? requestedColumn)
    {
        return requestedColumn is null
            ? sp.EndLine < requestedLine
            : sp.End() < new LineCol(requestedLine, requestedColumn.Value);
    }

    public static bool CoversRequestedPosition(this SequencePoint sp, int requestedLine, int? requestedColumn)
    {
        return requestedColumn is null
            ? sp.StartLine <= requestedLine
            : new LineCol(requestedLine, requestedColumn.Value) is var requestedPosition && sp.Start() <= requestedPosition && requestedPosition <= sp.End();
    }

    public static bool ShouldReplaceCoveringSequencePoint(this SequencePoint sp, SequencePoint? coveringSp, int? requestedColumn)
    {
        if (coveringSp is null)
        {
            return true;
        }

        return requestedColumn is null
            ? sp.StartLine > coveringSp.Value.StartLine ||
              (sp.StartLine == coveringSp.Value.StartLine && sp.StartColumn < coveringSp.Value.StartColumn)
            : sp.Start() > coveringSp.Value.Start();
    }
}
