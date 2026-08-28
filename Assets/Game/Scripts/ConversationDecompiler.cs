using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public partial class Conversation
{
    private struct DecodedInstruction
    {
        public int ip;
        public EOp op;
        public short arg;
        public int nextIp;
        public bool hasArg;
    }

    private void WritePseudoCDecompileArtifacts()
    {
        try
        {
            var instructions = DecodeInstructions();
            var labels = CollectLabels(instructions);
            string npcPart = SanitizeFileComponent(npcName);
            string baseName = $"conv_{conversationIndex}_{npcPart}";
            string outDir = Path.Combine("Conversations", "decompiled");
            Directory.CreateDirectory(outDir);

            string detailed = BuildPseudoC(instructions, labels, includeInstructionTrace: true);
            string clean = BuildPseudoC(instructions, labels, includeInstructionTrace: false);

            File.WriteAllText(Path.Combine(outDir, $"{baseName}.pseudo.c.txt"), detailed);
            File.WriteAllText(Path.Combine(outDir, $"{baseName}.pseudo.clean.c.txt"), clean);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Conversation decompiler failed for {npcName} ({conversationIndex}): {ex.Message}");
        }
    }

    private List<DecodedInstruction> DecodeInstructions()
    {
        var list = new List<DecodedInstruction>(code.Length);
        int ipLocal = 0;
        while (ipLocal < code.Length)
        {
            short raw = code[ipLocal];
            EOp op = (EOp)raw;
            int argc = raw >= 0 && raw < numArgs.Length ? numArgs[raw] : 0;
            bool hasArg = argc > 0 && ipLocal + 1 < code.Length;
            short arg = hasArg ? code[ipLocal + 1] : (short)0;
            int nextIp = ipLocal + 1 + argc;
            list.Add(new DecodedInstruction
            {
                ip = ipLocal,
                op = op,
                arg = arg,
                nextIp = nextIp,
                hasArg = hasArg
            });
            if (nextIp <= ipLocal)
            {
                break;
            }
            ipLocal = nextIp;
        }
        return list;
    }

    private HashSet<int> CollectLabels(List<DecodedInstruction> instructions)
    {
        var labels = new HashSet<int>();
        labels.Add(0);
        foreach (var ins in instructions)
        {
            switch (ins.op)
            {
                case EOp.Jmp:
                    labels.Add(ins.arg);
                    break;
                case EOp.Call:
                    labels.Add(ins.arg);
                    break;
                case EOp.BEQ:
                case EOp.BNE:
                case EOp.Branch:
                {
                    int argIndex = ins.ip + 1;
                    int target = (argIndex + ins.arg) & 65535;
                    labels.Add(target);
                    labels.Add(ins.nextIp);
                    break;
                }
            }
        }
        return labels;
    }

    private string BuildPseudoC(List<DecodedInstruction> instructions, HashSet<int> labels, bool includeInstructionTrace)
    {
        var sb = new StringBuilder(32768);
        sb.AppendLine("/*");
        sb.AppendLine("  Auto-generated pseudo-C from conversation VM bytecode.");
        sb.AppendLine("  Control flow is reconstructed with labels/goto where needed.");
        if (includeInstructionTrace)
        {
            sb.AppendLine("  BEQ/BNE ladder folding to if/else-if appears only in the .pseudo.clean.c.txt artifact.");
        }
        sb.AppendLine("*/");
        sb.AppendLine();
        sb.AppendLine($"void conversation_{conversationIndex}_{SanitizeIdentifier(npcName)}()");
        sb.AppendLine("{");
        sb.AppendLine("    // localN refers to varLocal[N] in the VM frame.");
        sb.AppendLine("    // Expressions are reconstructed from VM stack operations.");
        sb.AppendLine();

        Dictionary<int, int> predecessorCount = BuildPredecessorCount(instructions);
        Dictionary<int, int> ipToIndex = BuildIpToFirstIndex(instructions);
        pseudoExprId = 0;
        var stack = new List<string>(64);
        bool suppressUntilNextLabel = false;
        for (int idx = 0; idx < instructions.Count; ++idx)
        {
            var ins = instructions[idx];
            if (labels.Contains(ins.ip))
            {
                int preds = predecessorCount.TryGetValue(ins.ip, out int c) ? c : 0;
                if (ins.ip != 0 && stack.Count > 0 && preds > 1)
                {
                    // Label joins can have different incoming stack states.
                    // Clear stale symbols instead of implying false certainty.
                    stack.Clear();
                }
                suppressUntilNextLabel = false;
                sb.AppendLine($"L{ins.ip}:");
            }
            else if (suppressUntilNextLabel && !includeInstructionTrace)
            {
                continue;
            }

            if (!includeInstructionTrace
                && TryConsumeBeqLinearBodyMergeChain(instructions, idx, ipToIndex, stack, out int chainEndExclusive, out string dispatchLhs, out List<BeqChainArm> chainArms))
            {
                AppendBeqChainBlock(sb, dispatchLhs, chainArms);
                idx = chainEndExclusive - 1;
                continue;
            }

            string stmt = RenderInstructionHighLevel(instructions, idx, stack, includeInstructionTrace);
            if (!includeInstructionTrace && string.IsNullOrWhiteSpace(stmt))
            {
                continue;
            }
            if (includeInstructionTrace)
            {
                string opName = opcodes[(int)ins.op];
                string arg = ins.hasArg ? $" {ins.arg}" : "";
                sb.AppendLine($"    {stmt}    // {ins.ip:D4}: {opName}{arg}");
            }
            else
            {
                sb.AppendLine($"    {stmt}");
            }

            string stmtTrim = stmt.TrimStart();
            if (!includeInstructionTrace && (stmt.StartsWith("goto L", StringComparison.Ordinal) || stmt.Contains(" return;", StringComparison.Ordinal) || stmtTrim.StartsWith("return", StringComparison.Ordinal)))
            {
                suppressUntilNextLabel = true;
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private Dictionary<int, int> BuildPredecessorCount(List<DecodedInstruction> instructions)
    {
        var preds = new Dictionary<int, int>();
        foreach (var ins in instructions)
        {
            void AddPred(int ipTarget)
            {
                if (ipTarget < 0 || ipTarget >= code.Length) return;
                preds[ipTarget] = preds.TryGetValue(ipTarget, out int n) ? n + 1 : 1;
            }

            switch (ins.op)
            {
                case EOp.Jmp:
                case EOp.Call:
                    AddPred(ins.arg);
                    break;
                case EOp.BEQ:
                case EOp.BNE:
                case EOp.Branch:
                {
                    int argIndex = ins.ip + 1;
                    int target = (argIndex + ins.arg) & 65535;
                    AddPred(target);
                    if (ins.op != EOp.Branch)
                    {
                        AddPred(ins.nextIp);
                    }
                    break;
                }
                case EOp.Exit:
                case EOp.Ret:
                    break;
                default:
                    AddPred(ins.nextIp);
                    break;
            }
        }
        if (!preds.ContainsKey(0))
        {
            preds[0] = 1;
        }
        return preds;
    }

    private struct BeqChainArm
    {
        public string Rhs;
        public List<string> BodyLines;
    }

    private static Dictionary<int, int> BuildIpToFirstIndex(List<DecodedInstruction> instructions)
    {
        var map = new Dictionary<int, int>(instructions.Count);
        for (int i = 0; i < instructions.Count; ++i)
        {
            int ip = instructions[i].ip;
            if (!map.ContainsKey(ip))
            {
                map[ip] = i;
            }
        }
        return map;
    }

    private static int BranchRelativeTarget(DecodedInstruction ins)
    {
        int argIndex = ins.ip + 1;
        return (argIndex + ins.arg) & 65535;
    }

    private static bool TryParseTestEqual(string expr, out string lhs, out string rhs)
    {
        lhs = rhs = null;
        if (string.IsNullOrEmpty(expr))
        {
            return false;
        }
        string c = expr.Trim();
        while (true)
        {
            string n = StripOuterParens(c);
            if (n == c)
            {
                break;
            }
            c = n;
        }
        const string op = " == ";
        int p = c.IndexOf(op, StringComparison.Ordinal);
        if (p <= 0)
        {
            return false;
        }
        lhs = c.Substring(0, p).Trim();
        rhs = c.Substring(p + op.Length).Trim();
        return lhs.Length > 0 && rhs.Length > 0;
    }

    /// <summary>
    /// Parses (lhs != rhs) shape (e.g. from TestNotEqual). Used with BNE ladders where the
    /// fall-through arm runs when the values are equal, matching if (lhs == rhs) in structured form.
    /// </summary>
    private static bool TryParseTestNotEqual(string expr, out string lhs, out string rhs)
    {
        lhs = rhs = null;
        if (string.IsNullOrEmpty(expr))
        {
            return false;
        }
        string c = expr.Trim();
        while (true)
        {
            string n = StripOuterParens(c);
            if (n == c)
            {
                break;
            }
            c = n;
        }
        const string op = " != ";
        int p = c.IndexOf(op, StringComparison.Ordinal);
        if (p <= 0)
        {
            return false;
        }
        lhs = c.Substring(0, p).Trim();
        rhs = c.Substring(p + op.Length).Trim();
        return lhs.Length > 0 && rhs.Length > 0;
    }

    private static void AddControlFlowTargets(DecodedInstruction ins, List<int> dst)
    {
        switch (ins.op)
        {
            case EOp.Jmp:
                dst.Add(ins.arg);
                return;
            case EOp.Call:
                dst.Add(ins.arg);
                return;
            case EOp.BEQ:
            case EOp.BNE:
                dst.Add(BranchRelativeTarget(ins));
                dst.Add(ins.nextIp);
                return;
            case EOp.Branch:
                dst.Add(BranchRelativeTarget(ins));
                return;
        }
    }

    private static bool IsBeqChainExternallySafe(
        List<DecodedInstruction> instructions,
        int startIdx,
        int endIdxExclusive,
        int entryIp,
        int mergeIp,
        HashSet<int> chainIps)
    {
        var buf = new List<int>(4);
        for (int j = 0; j < instructions.Count; ++j)
        {
            if (j >= startIdx && j < endIdxExclusive)
            {
                continue;
            }
            buf.Clear();
            AddControlFlowTargets(instructions[j], buf);
            foreach (int t in buf)
            {
                if (chainIps.Contains(t) && t != entryIp && t != mergeIp)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private void AppendBeqChainBlock(StringBuilder sb, string dispatchLhs, List<BeqChainArm> arms)
    {
        for (int i = 0; i < arms.Count; ++i)
        {
            string kw = i == 0 ? "if" : "else if";
            sb.AppendLine($"    {kw} ({dispatchLhs} == {arms[i].Rhs})");
            sb.AppendLine("    {");
            foreach (string line in arms[i].BodyLines)
            {
                sb.AppendLine($"        {line}");
            }
            sb.AppendLine("    }");
        }
    }

    private bool TryConsumeBeqLinearBodyMergeChain(
        List<DecodedInstruction> instructions,
        int startIdx,
        Dictionary<int, int> ipToIndex,
        List<string> stack,
        out int endIdxExclusive,
        out string dispatchLhs,
        out List<BeqChainArm> arms)
    {
        arms = null;
        dispatchLhs = null;
        endIdxExclusive = startIdx;
        if (startIdx >= instructions.Count)
        {
            return false;
        }
        EOp ladderOp = instructions[startIdx].op;
        if (ladderOp != EOp.BEQ && ladderOp != EOp.BNE)
        {
            return false;
        }

        var work = new List<string>(stack);
        arms = new List<BeqChainArm>();
        string sharedLhs = null;
        int mergeIp = -1;
        int idx = startIdx;
        int endPos = startIdx;
        var seenArmIps = new HashSet<int>();

        while (true)
        {
            if (idx >= instructions.Count || instructions[idx].op != ladderOp)
            {
                return false;
            }
            if (!seenArmIps.Add(instructions[idx].ip))
            {
                return false;
            }

            var branchIns = instructions[idx];
            string cond = PopExpr(work);
            string lhs;
            string rhs;
            if (ladderOp == EOp.BEQ)
            {
                if (!TryParseTestEqual(cond, out lhs, out rhs))
                {
                    return false;
                }
            }
            else
            {
                if (!TryParseTestNotEqual(cond, out lhs, out rhs))
                {
                    return false;
                }
            }
            lhs = lhs.Trim();
            rhs = rhs.Trim();
            if (sharedLhs == null)
            {
                sharedLhs = lhs;
            }
            else if (!string.Equals(sharedLhs, lhs, StringComparison.Ordinal))
            {
                return false;
            }

            int skipTarget = BranchRelativeTarget(branchIns);
            int fallIp = branchIns.nextIp;
            if (!ipToIndex.TryGetValue(fallIp, out int bodyPos))
            {
                return false;
            }

            var bodyLines = new List<string>();
            int pos = bodyPos;
            bool foundTerm = false;
            while (pos < instructions.Count)
            {
                var bin = instructions[pos];
                if (bin.op == EOp.Branch || bin.op == EOp.Jmp)
                {
                    int t = bin.op == EOp.Jmp ? (bin.arg & 65535) : BranchRelativeTarget(bin);
                    if (mergeIp < 0)
                    {
                        mergeIp = t;
                    }
                    else if (mergeIp != t)
                    {
                        return false;
                    }
                    pos++;
                    foundTerm = true;
                    break;
                }
                if (bin.op == EOp.BEQ || bin.op == EOp.BNE || bin.op == EOp.Ret || bin.op == EOp.Exit)
                {
                    return false;
                }

                string line = RenderInstructionHighLevel(instructions, pos, work, false);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    bodyLines.Add(line);
                }
                pos++;
            }

            if (!foundTerm || mergeIp < 0)
            {
                return false;
            }
            if (bodyLines.Count == 0)
            {
                return false;
            }

            arms.Add(new BeqChainArm { Rhs = rhs, BodyLines = bodyLines });
            endPos = pos;

            if (skipTarget == mergeIp)
            {
                break;
            }
            if (!ipToIndex.TryGetValue(skipTarget, out idx))
            {
                return false;
            }
            // Skip target is the next arm's entry IP, often stack setup (push/fetch/tsteq) before the next BEQ/BNE.
            while (idx < instructions.Count && instructions[idx].op != ladderOp)
            {
                var scanIns = instructions[idx];
                if (scanIns.op == EOp.Branch || scanIns.op == EOp.Jmp || scanIns.op == EOp.Ret || scanIns.op == EOp.Exit
                    || scanIns.op == EOp.BEQ || scanIns.op == EOp.BNE)
                {
                    return false;
                }
                _ = RenderInstructionHighLevel(instructions, idx, work, false);
                idx++;
            }
            if (idx >= instructions.Count || instructions[idx].op != ladderOp)
            {
                return false;
            }
        }

        if (arms.Count < 2)
        {
            return false;
        }

        int entryIp = instructions[startIdx].ip;
        endIdxExclusive = endPos;
        var chainIps = new HashSet<int>();
        for (int i = startIdx; i < endIdxExclusive; ++i)
        {
            chainIps.Add(instructions[i].ip);
        }
        if (!IsBeqChainExternallySafe(instructions, startIdx, endIdxExclusive, entryIp, mergeIp, chainIps))
        {
            return false;
        }

        dispatchLhs = sharedLhs;
        stack.Clear();
        stack.AddRange(work);
        return true;
    }

    private int pseudoExprId;

    private string PopExpr(List<string> stack)
    {
        if (stack.Count == 0) return $"stack_{pseudoExprId++}";
        int i = stack.Count - 1;
        string v = stack[i];
        stack.RemoveAt(i);
        return v;
    }

    private void PushExpr(List<string> stack, string expr)
    {
        stack.Add(expr);
    }

    private static bool TryParseConstInt(string expr, out int value)
    {
        string t = StripOuterParens(expr).Trim();
        return int.TryParse(t, out value);
    }

    /// <summary>
    /// True if the next babl_menu or babl_fmenu import call is reached before control flow diverges.
    /// upcomingIsFmenu is set when that call is babl_fmenu (used to avoid annotating 0/1 flag slots).
    /// </summary>
    private bool HasUpcomingBablMenuFamilyCall(List<DecodedInstruction> instructions, int instructionIndex, out bool upcomingIsFmenu, int lookahead = 40)
    {
        upcomingIsFmenu = false;
        int end = Math.Min(instructions.Count - 1, instructionIndex + lookahead);
        for (int i = instructionIndex + 1; i <= end; ++i)
        {
            var ins = instructions[i];
            if (ins.op == EOp.CallImport)
            {
                var import = GetImport(ins.arg);
                if (import != null && import.name == "babl_menu")
                {
                    upcomingIsFmenu = false;
                    return true;
                }
                if (import != null && import.name == "babl_fmenu")
                {
                    upcomingIsFmenu = true;
                    return true;
                }
                continue;
            }

            // Stop scanning if control flow likely diverges first.
            if (ins.op == EOp.Jmp || ins.op == EOp.BEQ || ins.op == EOp.BNE || ins.op == EOp.Branch || ins.op == EOp.Ret || ins.op == EOp.Exit)
            {
                break;
            }
        }
        return false;
    }

    private static bool TryParseLocalAddr(string expr, out int localIndex)
    {
        localIndex = 0;
        const string prefix = "local_addr(";
        if (!expr.StartsWith(prefix, StringComparison.Ordinal) || !expr.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }
        string inner = expr.Substring(prefix.Length, expr.Length - prefix.Length - 1);
        return int.TryParse(inner, out localIndex);
    }

    private static string StripOuterParens(string expr)
    {
        if (string.IsNullOrEmpty(expr)) return expr;
        string t = expr.Trim();
        if (t.Length < 2 || t[0] != '(' || t[t.Length - 1] != ')') return t;
        int depth = 0;
        for (int i = 0; i < t.Length; ++i)
        {
            char c = t[i];
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0 && i != t.Length - 1)
                {
                    return t;
                }
            }
        }
        return t.Substring(1, t.Length - 2).Trim();
    }

    private static string NegateCond(string cond)
    {
        string c = StripOuterParens(cond);
        if (c.StartsWith("!", StringComparison.Ordinal))
        {
            return StripOuterParens(c.Substring(1));
        }

        string[] pairs =
        {
            " == ", " != ",
            " != ", " == ",
            " <= ", " > ",
            " >= ", " < ",
            " < ", " >= ",
            " > ", " <= "
        };
        for (int i = 0; i < pairs.Length; i += 2)
        {
            int p = c.IndexOf(pairs[i], StringComparison.Ordinal);
            if (p > 0)
            {
                string lhs = c.Substring(0, p).Trim();
                string rhs = c.Substring(p + pairs[i].Length).Trim();
                return $"{lhs}{pairs[i + 1]}{rhs}";
            }
        }

        return $"!({c})";
    }

    private static int CountTrailingPops(List<DecodedInstruction> instructions, int callIndex, int max = 8)
    {
        int count = 0;
        int i = callIndex + 1;
        while (i < instructions.Count && count < max && instructions[i].op == EOp.Pop)
        {
            count++;
            i++;
        }
        return count;
    }

    private static string[] PeekArgs(List<string> stack, int count)
    {
        if (count <= 0) return Array.Empty<string>();
        if (count > stack.Count) count = stack.Count;
        var args = new string[count];
        int start = stack.Count - count;
        for (int i = 0; i < count; ++i)
        {
            args[i] = stack[start + i];
        }
        return args;
    }

    private string RenderInstructionHighLevel(List<DecodedInstruction> instructions, int instructionIndex, List<string> stack, bool includeInstructionTrace)
    {
        var ins = instructions[instructionIndex];
        switch (ins.op)
        {
            case EOp.Nop:
            case EOp.Start:
                return includeInstructionTrace ? ";" : "";
            case EOp.PushImm:
                PushExpr(stack, ins.arg.ToString());
                return "";
            case EOp.PushI_Eff:
                PushExpr(stack, $"local_addr({ins.arg})");
                return "";
            case EOp.PushReg:
                PushExpr(stack, "result");
                return "";
            case EOp.SaveReg:
            {
                string v = PopExpr(stack);
                return $"result = {v};";
            }
            case EOp.Pop:
                _ = PopExpr(stack);
                return "";
            case EOp.Swap:
            {
                string a = PopExpr(stack);
                string b = PopExpr(stack);
                PushExpr(stack, a);
                PushExpr(stack, b);
                return "";
            }
            case EOp.FetchM:
            {
                string addr = PopExpr(stack);
                if (TryParseLocalAddr(addr, out int localN))
                {
                    PushExpr(stack, $"local{localN}");
                }
                else
                {
                    PushExpr(stack, $"mem[{addr}]");
                }
                return "";
            }
            case EOp.Sto:
            {
                string value = PopExpr(stack);
                string addr = PopExpr(stack);
                bool isMenuSetup = HasUpcomingBablMenuFamilyCall(instructions, instructionIndex, out bool upcomingFmenu);
                string stringComment = "";
                if (isMenuSetup && TryParseConstInt(value, out int stringIndex))
                {
                    bool skipStringLookup = upcomingFmenu ? stringIndex < 2 : stringIndex == 0;
                    if (!skipStringLookup)
                    {
                        try
                        {
                            string txt = StringLoader.GetString(stringBlock, stringIndex);
                            if (!string.IsNullOrEmpty(txt))
                            {
                                string escaped = txt.Replace("\\", "\\\\").Replace("\"", "\\\"");
                                stringComment = $" // option \"{escaped}\"";
                            }
                        }
                        catch
                        {
                        }
                    }
                }

                if (TryParseLocalAddr(addr, out int localN))
                {
                    return $"local{localN} = {value};{stringComment}";
                }
                return $"mem[{addr}] = {value};{stringComment}";
            }
            case EOp.Offset:
            {
                string i = PopExpr(stack);
                string b = PopExpr(stack);
                PushExpr(stack, $"({b} + {i})");
                return "";
            }
            case EOp.Add:
            case EOp.Mul:
            case EOp.Sub:
            case EOp.Div:
            case EOp.Mod:
            case EOp.Or:
            case EOp.And:
            case EOp.TestGreater:
            case EOp.TestGreaterEqual:
            case EOp.TestLess:
            case EOp.TestLessEqual:
            case EOp.TestEqual:
            case EOp.TestNotEqual:
            {
                string s0 = PopExpr(stack);
                string s1 = PopExpr(stack);
                string op = ins.op switch
                {
                    EOp.Add => "+",
                    EOp.Mul => "*",
                    EOp.Sub => "-",
                    EOp.Div => "/",
                    EOp.Mod => "%",
                    EOp.Or => "||",
                    EOp.And => "&&",
                    EOp.TestGreater => ">",
                    EOp.TestGreaterEqual => ">=",
                    EOp.TestLess => "<",
                    EOp.TestLessEqual => "<=",
                    EOp.TestEqual => "==",
                    EOp.TestNotEqual => "!=",
                    _ => "?"
                };
                if (ins.op == EOp.Or || ins.op == EOp.And || ins.op.ToString().StartsWith("Test", StringComparison.Ordinal))
                {
                    PushExpr(stack, $"({s1} {op} {s0})");
                }
                else
                {
                    PushExpr(stack, $"({s1} {op} {s0})");
                }
                return "";
            }
            case EOp.Not:
            {
                string v = PopExpr(stack);
                PushExpr(stack, $"(!({v}))");
                return "";
            }
            case EOp.Neg:
            {
                string v = PopExpr(stack);
                PushExpr(stack, $"(-{v})");
                return "";
            }
            case EOp.BEQ:
            {
                int argIndex = ins.ip + 1;
                int target = (argIndex + ins.arg) & 65535;
                string cond = PopExpr(stack);
                return $"if ({NegateCond(cond)}) goto L{target};";
            }
            case EOp.BNE:
            {
                int argIndex = ins.ip + 1;
                int target = (argIndex + ins.arg) & 65535;
                string cond = PopExpr(stack);
                return $"if ({StripOuterParens(cond)}) goto L{target};";
            }
            case EOp.Branch:
            {
                int argIndex = ins.ip + 1;
                int target = (argIndex + ins.arg) & 65535;
                return $"goto L{target};";
            }
            case EOp.Jmp:
                return $"goto L{ins.arg};";
            case EOp.Call:
            {
                int argc = CountTrailingPops(instructions, instructionIndex);
                string[] args = PeekArgs(stack, argc);
                if (includeInstructionTrace)
                {
                    if (args.Length > 0)
                    {
                        return $"call(L{ins.arg}, {string.Join(", ", args)});";
                    }
                    return $"call(L{ins.arg});";
                }
                if (args.Length > 0)
                {
                    return $"call L{ins.arg}({string.Join(", ", args)});";
                }
                return $"call L{ins.arg}();";
            }
            case EOp.CallImport:
            {
                int argc = CountTrailingPops(instructions, instructionIndex);
                string[] args = PeekArgs(stack, argc);
                string fn = GetImportName(ins.arg);
                if (args.Length > 0)
                {
                    return $"{fn}({string.Join(", ", args)});";
                }
                return $"{fn}();";
            }
            case EOp.Ret:
                return includeInstructionTrace ? "return_from_call();" : "return;";
            case EOp.PushBP:
                PushExpr(stack, "bp");
                return "";
            case EOp.PopBP:
            {
                string rhs = PopExpr(stack);
                if (!includeInstructionTrace)
                {
                    return "";
                }
                return $"bp = {rhs};";
            }
            case EOp.SPToBP:
                return includeInstructionTrace ? "bp = sp;" : "";
            case EOp.BPToSP:
                return includeInstructionTrace ? "sp = bp;" : "";
            case EOp.AddSP:
            {
                string n = PopExpr(stack);
                return includeInstructionTrace ? $"reserve_locals({n});" : "";
            }
            case EOp.Say:
                return RenderSayFromExpr(PopExpr(stack));
            case EOp.Respond:
                return "yield_for_response();";
            case EOp.Exit:
                return "exit_conversation(); return;";
            case EOp.StrCmp:
                return "/* strcmp opcode */;";
            default:
                return includeInstructionTrace ? $"/* unsupported {ins.op} */;" : "";
        }
    }

    private string RenderSayFromExpr(string expr)
    {
        if (TryParseConstInt(expr, out int idx))
        {
            string text;
            try
            {
                text = StringLoader.GetString(stringBlock, idx);
            }
            catch
            {
                text = "";
            }
            if (!string.IsNullOrEmpty(text))
            {
                string escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
                return $"say({idx}); // \"{escaped}\"";
            }
            return $"say({idx});";
        }
        return $"say({expr});";
    }

    private string RenderInstruction(DecodedInstruction ins, bool includeInstructionTrace)
    {
        switch (ins.op)
        {
            case EOp.Nop: return includeInstructionTrace ? ";" : "";
            case EOp.Add: return "value = (lhs + rhs);";
            case EOp.Mul: return "value = (lhs * rhs);";
            case EOp.Sub: return "value = (lhs - rhs);";
            case EOp.Div: return "value = (lhs / rhs);";
            case EOp.Mod: return "value = (lhs % rhs);";
            case EOp.Or: return "value = ((lhs != 0 || rhs != 0) ? 1 : 0);";
            case EOp.And: return "value = ((lhs != 0 && rhs != 0) ? 1 : 0);";
            case EOp.Not: return "value = (value != 0 ? 0 : 1);";
            case EOp.TestGreater: return "value = ((lhs > rhs) ? 1 : 0);";
            case EOp.TestGreaterEqual: return "value = ((lhs >= rhs) ? 1 : 0);";
            case EOp.TestLess: return "value = ((lhs < rhs) ? 1 : 0);";
            case EOp.TestLessEqual: return "value = ((lhs <= rhs) ? 1 : 0);";
            case EOp.TestEqual: return "value = ((lhs == rhs) ? 1 : 0);";
            case EOp.TestNotEqual: return "value = ((lhs != rhs) ? 1 : 0);";
            case EOp.Jmp: return $"goto L{ins.arg};";
            case EOp.BEQ:
            {
                int argIndex = ins.ip + 1;
                int target = (argIndex + ins.arg) & 65535;
                return $"if (cond == 0) goto L{target};";
            }
            case EOp.BNE:
            {
                int argIndex = ins.ip + 1;
                int target = (argIndex + ins.arg) & 65535;
                return $"if (cond != 0) goto L{target};";
            }
            case EOp.Branch:
            {
                int argIndex = ins.ip + 1;
                int target = (argIndex + ins.arg) & 65535;
                return $"goto L{target};";
            }
            case EOp.Call: return $"call(L{ins.arg});";
            case EOp.CallImport: return $"{GetImportName(ins.arg)}();";
            case EOp.Ret: return "return_from_call();";
            case EOp.PushImm: return $"value = {ins.arg};";
            case EOp.PushI_Eff: return $"addr = local_addr({ins.arg});";
            case EOp.Pop: return includeInstructionTrace ? "discard(value);" : "";
            case EOp.Swap: return "swap(lhs, rhs);";
            case EOp.PushBP: return "value = bp;";
            case EOp.PopBP: return "bp = value;";
            case EOp.SPToBP: return "bp = sp;";
            case EOp.BPToSP: return "sp = bp;";
            case EOp.AddSP: return "reserve_locals(value);";
            case EOp.FetchM: return "value = mem[addr];";
            case EOp.Sto: return "mem[addr] = value;";
            case EOp.Offset: return "addr = (base_addr + index);";
            case EOp.Start: return includeInstructionTrace ? ";" : "";
            case EOp.SaveReg: return "result = value;";
            case EOp.PushReg: return "value = result;";
            case EOp.StrCmp: return "/* strcmp opcode */;";
            case EOp.Exit: return "exit_conversation(); return;";
            case EOp.Say: return RenderSay(ins.arg);
            case EOp.Respond: return "yield_for_response();";
            case EOp.Neg: return "value = -value;";
            default: return $"/* unknown opcode {(int)ins.op} */;";
        }
    }

    private string RenderSay(short stringIndex)
    {
        string text;
        try
        {
            text = StringLoader.GetString(stringBlock, stringIndex);
        }
        catch
        {
            text = "";
        }
        if (string.IsNullOrEmpty(text))
        {
            return $"say({stringIndex});";
        }
        string escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"say({stringIndex}); // \"{escaped}\"";
    }

    private string GetImportName(short id)
    {
        var import = GetImport(id);
        if (import == null || string.IsNullOrEmpty(import.name))
        {
            return $"import_{id}";
        }
        return SanitizeIdentifier(import.name);
    }

    private static string SanitizeFileComponent(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return "unknown_npc";
        }
        string t = s.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            t = t.Replace(c, '_');
        }
        t = t.Replace(' ', '_');
        while (t.Contains("__"))
        {
            t = t.Replace("__", "_");
        }
        return string.IsNullOrWhiteSpace(t) ? "unknown_npc" : t;
    }

    private static string SanitizeIdentifier(string s)
    {
        string t = SanitizeFileComponent(s);
        var sb = new StringBuilder(t.Length);
        for (int i = 0; i < t.Length; ++i)
        {
            char c = t[i];
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }
        if (sb.Length == 0 || (sb[0] >= '0' && sb[0] <= '9'))
        {
            sb.Insert(0, '_');
        }
        return sb.ToString();
    }
}
