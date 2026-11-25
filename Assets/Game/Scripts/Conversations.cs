using System;
using UnityEngine;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Random = UnityEngine.Random;
#if UNITY_EDITOR
using UnityEditor;
#endif

#pragma warning disable 0219

// first conversation is at 53, 9, 19.

public class ConversationOption
{
    public ConversationOption(int _actualIndex, string _gambit)
    {
        actualIndex = _actualIndex;
        gambit = _gambit;
    }

    public readonly int actualIndex;
    public readonly string gambit;
}

public partial class Conversation
{
    private int unk0;
    private int unk1;

    public Conversation(Stream stream, int index)
    {
        conversationIndex = index;
        unk0 = stream.GetInt(); // always 2088
        int numInstructions = stream.GetUShort();
        unk1 = stream.GetInt(); // always 0
        stringBlock = stream.GetUShort();
        memSlotsForVars = stream.GetUShort();
        int numImportedGlobs = stream.GetUShort();
        
        imports = new ConversationImportRecord[numImportedGlobs];
        for (int g = 0; g < numImportedGlobs; ++g)
        {
            imports[g] = new ConversationImportRecord(stream);
        }

        // read the code
        code = stream.GetShortArray(numInstructions);
        ApplyConversation139JmpFix();
        ApplyConversation3GiftFix();

#if UNITY_EDITOR && false
        disassemble.AppendLine($"StringBlock {stringBlock}");
        // disassemble
        ip = 0;
        while (ip < code.Length)
        {
            AddToDisassembly();
            int c = code[ip] < numArgs.Length ? numArgs[code[ip]] : 0;
            ip += c + 1;
        }

        System.IO.File.WriteAllText($"Conversations/conv{index}.txt", disassemble.ToString());
#endif
    }

    /// <summary>
    /// Patches for conv 139 (Trisch / Taper) bytecode loaded from cnv.ark.
    /// </summary>
    /// <summary>
    /// conv 139 (Taper): original bytecode uses <c>jmp 1418</c> after <c>say</c> so execution lands on
    /// <c>tsteq</c> without the preceding <c>pushi 2</c>/<c>fetchm</c>. Stack tops are then <c>0,0</c>,
    /// <c>tsteq</c> succeeds, and the script falls through to <c>say</c> 33 — skipping string 32 and looping.
    /// Evidence: with unpatched jmps, <c>tsteq</c> at 1418 saw <c>0,0</c> on the stack after <c>say</c>, then fell through to <c>say</c> 33 (skipping 32 / looping).
    /// Patch: retarget the first two jmps to <c>1413</c> (re-push operands); retarget the post–say-33 jmp to <c>1426</c> (<c>local3++</c>).
    /// </summary>
    private void ApplyConversation139JmpFix()
    {
        if (conversationIndex != 139 || code == null || code.Length <= 1425)
        {
            return;
        }

        short jmp = (short)EOp.Jmp;
        if (code[1398] == jmp && code[1399] == 1418)
        {
            code[1399] = 1413;
        }

        if (code[1411] == jmp && code[1412] == 1418)
        {
            code[1412] = 1413;
        }

        if (code[1424] == jmp && code[1425] == 1418)
        {
            code[1425] = 1426;
        }
    }

    /// <summary>
    /// conv 3 (Goldthirst): a bug of the original's script. "I bring thee this gift." after the
    /// flattery (the menu at pc 968, option 3) calls the "goodly gift" reply at 1580 without reading
    /// the trade area, so nothing given is thanked as a gift. Every other gift line calls 1356, which
    /// weighs what is on the trade area; the call is pointed there.
    /// </summary>
    private void ApplyConversation3GiftFix()
    {
        if (conversationIndex != 3 || code == null || code.Length <= 1580)
        {
            return;
        }

        if (code[1053] == (short)EOp.Call && code[1054] == 1580)
        {
            code[1054] = 1356;
        }
    }

    public void InitializePrivateGlobals(Stream stream)
    {
        int n = stream.GetUShort();
        privateGlobalsLength = n;
    }

    public void StartConversation(string _npcName)
    {
        npcName = _npcName;
        //WritePseudoCDecompileArtifacts();
        output = new StringBuilder();
        disassemble = new StringBuilder();

        disassemble.AppendLine(npcName);

        mem = new short[65536];

        topOfImports = 0;
        bottomOfImports = 256;
        
        foreach (var import in imports)
        {
            if (import.type == 0x010f) // variable
            {
                short val = 0;
                switch (import.name)
                {
                case "play_hp":
                    val = (short)PlayerData.sData.hp;
                    break;
                case "play_sex":
                    val = (short)(PlayerData.sData.female ? 1 : 0);
                    break;
                case "play_drawn":
                    // could be whether the player has drawn their weapon
                    val = 1;
                    break;
                case "play_poison":
                    val = (short)PlayerData.sData.poison;
                    break;
                case "play_level":
                    val = (short)PlayerData.sData.charLevel;
                    break;
                case "play_mana":
                    val = (short)PlayerData.sData.mana;
                    break;
                case "play_power":
                    val = (short)PlayerData.sData.strength;
                    break;
                case "play_arms":
                    // ?
                    val = 1;
                    break;
                case "play_health":
                    val = (short)PlayerData.sData.vitality;
                    break;
                case "play_hunger":
                    val = (short)PlayerData.sData.hunger;
                    break;
                case "new_player_exp":
                    val = (short)(PlayerData.sData.xp / 20);
                    break;
                case "npc_talkedto":
                    val = (short)npc.talkedTo;
                    break;
                case "npc_hp":
                    val = (short)npc.hp;
                    break;
                case "npc_attitude":
                    // 0 for a creature attacking the player, whatever its attitude, and 6 for an
                    // ally: UW.EXE 0x70400 (setup_converse_data, CONVVARS.C)
                    val = (short)(npc.goal == Critter.EGoal.AttackTarget5 && npc.gtarg == 1 ? 0
                                  : npc.playerAlly ? 6 : (int)npc.attitude);
                    break;
                case "npc_gtarg":
                    val = (short)npc.gtarg;
                    break;
                case "npc_goal":
                    val = (short)npc.goal;
                    break;
                case "npc_name":
                    val = 1; // ?
                    break;
                case "npc_level":
                    val = (short)npc.critterLevel;
                    break;
                case "npc_power":
                    val = 1; // ?
                    break;
                case "npc_arms":
                    val = 1; // ?
                    break;
                case "npc_health":
                    val = (short)npc.hp;
                    break;
                case "npc_hunger":
                    val = (short)npc.hunger;
                    break;
                case "npc_whoami":
                    val = (short)npc.whoami;
                    break;
                case "npc_yhome":
                    val = (short)npc.yhome;
                    break;
                case "npc_xhome":
                    val = (short)npc.xhome;
                    break;
                case "game_mins":
                    val = (short)((PlayerData.sData.gameTime / 60) % (24 * 60));
                    break;
                case "game_days":
                    val = (short)(PlayerData.sData.gameTime / (24 * 60 * 60));
                    break;
                case "game_time":
                    val = (short)(PlayerData.sData.gameTime / 60); // assume minutes
                    break;
                case "dungeon_level":
                    val = (short)LevelLoader.sLevelLoader.loadedLevel;
                    break;
                case "riddlecounter":
                    val = 1; // ?
                    break;
                }

                // copy variable
                mem[import.id] = val;
                if (import.id > topOfImports)
                {
                    topOfImports = import.id;
                }

                if (import.id < bottomOfImports)
                {
                    bottomOfImports = import.id;
                }
            }

            #if false
            disassemble.AppendLine($"Import {i}: {imports[i].name} = {imports[i].id}/{imports[i].unk}");
            #endif
        }

        ++topOfImports;

        // copy charGlobals into memory
        if (npc.charGlobals == null || npc.charGlobals.Length < topOfImports + privateGlobalsLength)
        {
            npc.charGlobals = new short[topOfImports + privateGlobalsLength];
        }
        for (int i = 0; i < npc.charGlobals.Length; ++i)
        {
            if (i < bottomOfImports || i >= topOfImports)
            {
                mem[i] = npc.charGlobals[i];
            }
        }
        
        // pad out by 64 to check for data corruption
        sp = topOfImports + privateGlobalsLength + 64;
        bp = sp;
        ip = 0;
        call_level = 0;
    }

    private int topOfImports;
    private int bottomOfImports;

    private int infLoopCheck;

    private void EndConversation()
    {
        yield = true;
        exit = true;
        foreach (var import in imports)
        {
            // copy imports back to game, possibly modified
            if (import.type == 0x010f) // variable
            {
                short val = mem[import.id];

                switch (import.name)
                {
                case "play_sex":
                    // can't imagine this being needed
                    break;
                case "play_name":
                    // can't imagine this being needed either
                    break;
                case "play_hp":
                    PlayerData.sData.hp = val;
                    break;
                case "play_drawn":
                    // no idea what this is - maybe whether the player is wielding their weapon
                    break;
                case "play_poison":
                    PlayerData.sData.poison = val;
                    break;
                case "play_level":
                    PlayerData.sData.charLevel = val;
                    break;
                case "play_mana":
                    PlayerData.sData.mana = val;
                    break;
                case "play_power":
                    PlayerData.sData.strength = val;
                    break;
                case "play_arms":
                    // possibly which weapon is equipped?
                    break;
                case "play_health":
                    PlayerData.sData.vitality = val;
                    break;
                case "play_hunger":
                    PlayerData.sData.hunger = val;
                    break;
                case "new_player_exp":
                    // increase xp only if it seems to have changed
                    // otherwise we'll likely end up truncating the xp and losing some
                    if (20 * val > PlayerData.sData.xp)
                    {
                        PlayerData.sData.xp = 20 * val;
                        PlayerObject.ApplyXpProgress();
                    }
                    break;
                case "npc_talkedto":
                    // might need to skip this as talked to seems to be stored in conversation global data. we shall see
                    npc.talkedTo = val;
                    break;
                case "npc_hp":
                    npc.hp = val;
                    break;
                case "npc_attitude":
                    // Above 3 is an ally, friendly: UW.EXE 0x70892 (update_converse_data, CONVVARS.C)
                    if (val > 3)
                    {
                        npc.attitude = Critter.EAttitude.Friendly;
                        npc.playerAlly = true;
                    }
                    else
                    {
                        npc.attitude = (Critter.EAttitude)val;
                    }
                    break;
                case "npc_gtarg":
                    npc.gtarg = val;
                    break;
                case "npc_goal":
                    npc.goal = (Critter.EGoal)val;
                    break;
                case "npc_name": // ?
                    break;
                case "npc_level":
                    npc.critterLevel = val;
                    break;
                case "npc_power": // ?
                    break;
                case "npc_arms": // ?
                    break;
                case "npc_health":
                    // Not written back: it comes after npc_hp and undid a script's heal (slot 22 sets
                    // npc_hp to 125). The original reads it out and never back (UW.EXE 0x70892,
                    // update_converse_data, CONVVARS.C).
                    break;
                case "npc_hunger":
                    npc.hunger = val;
                    break;
                case "npc_whoami":
                    // I guess in case they need a big change in conversation (or the conversation character icon)
                    npc.whoami = (EWhoAmI) val;
                    break;
                case "npc_yhome":
                    npc.yhome = val;
                    break;
                case "npc_xhome":
                    npc.xhome = val;
                    break;
                case "game_mins":
                    // I hope we don't have to change these
                    break;
                case "game_days":
                    break;
                case "game_time":
                    // don't want to do this as it would clip the time
                    //PlayerData.sData.gameTime = 60 * val; // assume minutes, see the import
                    break;
                case "dungeon_level":
                    // probably won't change anything... but there's no new coords so best to not try
                    //LevelLoader.sLevelLoader.loadedLevel = val;
                    break;
                case "riddlecounter":
                    break;
                }
            }
        }

        // copy private globals back
        for (int i = 0; i < topOfImports + privateGlobalsLength; ++i)
        {
            npc.charGlobals[i] = mem[i];
        }
        
        // move contents of trade tray back to the player
        Inventory.sInv.TryTakeAllFromTradeTray();
        
#if UNITY_EDITOR
        System.IO.File.WriteAllText($"Conversations/conv_interpret{conversationIndex}.txt", disassemble.ToString());
#endif

        npc.EndConversation();
    }

    public StringBuilder disassemble = new StringBuilder();

    private static readonly string[] opcodes =
    {
        "nop", "add", "mul", "sub", "div", "mod", "or", "and", "not", "tstgt", "tstge", "tstlt", "tstle", "tsteq",
        "tstne", "jmp",
        "beq", "bne", "bra", "call", "calli", "ret", "pushi", "pushi_eff", "pop", "swap", "pushbp", "popbp", "sptobp",
        "bptosp", "addsp", "fetchm",
        "sto", "offset", "start", "save_reg", "push_reg", "strcmp", "exit", "say", "respond", "neg"
    };

    private static readonly int[] numArgs =
    {
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1,
        1, 1, 1, 1, 1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0
    };

    private void AddToDisassembly()
    {
        int op = code[ip];
        if (op < opcodes.Length)
        {
            int argi = 0;
            if (ip + 1 < code.Length)
            {
                argi = code[ip + 1];
            }

            string args = numArgs[op] == 1 ? argi.ToString() : "";
            if (opcodes[op] == "calli")
            {
                ConversationImportRecord import = GetImport(argi);
                args = import.name;
            }

            disassemble.AppendFormat("{0:0000}: {1} {2}", ip, opcodes[op], args);
            if (mem != null && sp > 1)
            {
                disassemble.AppendFormat("                                 // stk={0},{1} bp={2} sp={3}", mem[sp], mem[sp-1], bp, sp);
            }
            disassemble.Append("\n");
        }
        else
        {
            disassemble.AppendFormat("{0:0000}: {1} // ???\n", ip, op);
        }
    }

    string VarForDisassembly(int i)
    {
        string g = "";
        if (i < bottomOfImports)
        {
            g = $"charGlobal[{i}]";
        }
        else if (i < topOfImports)
        {
            g = GetImportedGlobalVariablename(i);
        }
        else if (i < topOfImports + privateGlobalsLength)
        {
            g = $"charGlobal[{i}]";
        }
        else if (i > bp && i <= sp)
        {
            g = $"varLocal[{i - bp}]";
        }
        else if (i == bp)
        {
            g = "returnaddress(err?)";
        }
        else if (i == bp - 1)
        {
            g = "numparams(err?)";
        }
        else if (i >= topOfImports + privateGlobalsLength && i < bp - 1)
        {
            g = $"functionParam[{bp - i}]";
        }

        if (g == "")
        {
            g = $"mem[{i}]";
        }

        return g;
    }

    public float pauseTime;
    private string pauseText = "";

    private void AddDelayedText(string text)
    {
        yield = true;
        pauseTime = 0.8f;
        pauseText = text;
    }

    private bool yield;
    private bool exit;
    public bool babl_get_input;
    public string babl_input = "";
    
    private enum EOp
    {
        Nop,
        Add,
        Mul,
        Sub,
        Div,
        Mod,
        Or,
        And,
        Not,
        TestGreater,
        TestGreaterEqual,
        TestLess,
        TestLessEqual,
        TestEqual,
        TestNotEqual,
        Jmp,
        BEQ,
        BNE,
        Branch,
        Call,
        CallImport,
        Ret,
        PushImm,
        PushI_Eff,
        Pop,
        Swap,
        PushBP,
        PopBP,
        SPToBP,
        BPToSP,
        AddSP,
        FetchM,
        Sto,
        Offset,
        Start,
        SaveReg,
        PushReg,
        StrCmp,
        Exit,
        Say,
        Respond,
        Neg,
    }
    
    public bool Update()
    {
        if (pauseTime > 0.0f)
        {
            pauseTime -= Time.unscaledDeltaTime;
            if (pauseTime <= 0.0f)
            {
                output.AppendLine(pauseText);
                LogToHistory(npcName, pauseText, Messages.EHistoryKind.Speaker);
                pauseText = "";
                pauseTime = 0.0f;
            }
            return false;
        }
        yield = false;
        exit = false;
        int maxInstructions = 32768;
        while (--maxInstructions > 0 && !yield && Conversations.gambits.Count == 0 && !babl_get_input)
        {
            // A pc outside the code ends the conversation. Sseetharee's "Why not?" calls -1 (slot 15,
            // pc 1411): the original then fetches the word before the code, the segment of the heap
            // block's pointer to itself (UW.EXE 0x672f1), far above the last opcode, and stops cleanly
            // (0x68a70). Here code[-1] threw on every frame and the conversation never ended.
            if (ip < 0 || ip >= code.Length)
            {
                Debug.LogWarningFormat("Conversation {0}: pc {1} outside the code. Ending.", conversationIndex, ip);
                EndConversation();
                break;
            }
            AddToDisassembly();
            EOp op = (EOp)code[ip++];
            if (ip >= code.Length && op != EOp.Ret) // ret
            {
                exit = true;
                break;
            }
            short s0, s1;
            switch (op)
            {
            case EOp.Nop:
                break;
            case EOp.Add:
                Push(Pop() + Pop());
                break;
            case EOp.Mul:
                Push(Pop() * Pop());
                break;
            case EOp.Sub:
                s0 = Pop();
                s1 = Pop();
                Push(s1 - s0);
                break;
            case EOp.Div:
                s0 = Pop();
                s1 = Pop();
                Push(s1 / s0);
                break;
            case EOp.Mod:
                s0 = Pop();
                s1 = Pop();
                Push(s1 % s0);
                break;
            case EOp.Or:
                s0 = Pop();
                s1 = Pop();
                Push(s0 != 0 || s1 != 0 ? 1 : 0); 
                break;
            case EOp.And:
                s0 = Pop();
                s1 = Pop();
                Push(s0 != 0 && s1 != 0 ? 1 : 0);
                break;
            case EOp.Not:
                Push(Pop() != 0 ? 0 : 1);
                break;
            case EOp.TestGreater:
                s0 = Pop();
                s1 = Pop();
                Push(s1 > s0 ? 1 : 0);
                break;
            case EOp.TestGreaterEqual:
                s0 = Pop();
                s1 = Pop();
                Push(s1 >= s0 ? 1 : 0);
                break;
            case EOp.TestLess:
                s0 = Pop();
                s1 = Pop();
                Push(s1 < s0 ? 1 : 0);
                break;
            case EOp.TestLessEqual:
                s0 = Pop();
                s1 = Pop();
                Push(s1 <= s0 ? 1 : 0);
                break;
            case EOp.TestEqual:
                s0 = Pop();
                s1 = Pop();
                Push(s1 == s0 ? 1 : 0);
                break;
            case EOp.TestNotEqual:
                s0 = Pop();
                s1 = Pop();
                Push(s1 != s0 ? 1 : 0);
                break;
            case EOp.Jmp:
                ip = code[ip];
                break;
            case EOp.BEQ:
                s0 = code[ip];
                if (Pop() == 0)
                {
                    ip = (ip + s0) & 65535;
                }
                else
                {
                    ++ip;
                }
                break;
            case EOp.BNE:
                s0 = code[ip];
                if (Pop() != 0)
                {
                    ip = (ip + s0) & 65535;
                }
                else
                {
                    ++ip;
                }
                break;
            case EOp.Branch:
                ip = (ip + code[ip]) & 65535;
                break;
            case EOp.Call:
                Push(ip + 1);
                ip = code[ip];
                ++call_level;
                break;
            case EOp.CallImport:
                s0 = code[ip++];
                ConversationImportRecord import = GetImport(s0);
                if (import != null)
                {
                    CallImport(import);
                }
                // possibly yield
                break;
            case EOp.Ret:
                if (--call_level < 0)
                {
                    yield = true;
                    exit = true;
                }
                else
                {
                    ip = Pop();
                }
                break;
            case EOp.PushImm:
                Push(code[ip++]);
                break;
            case EOp.PushI_Eff:
                {
                    short arg = code[ip++];
                    Push(bp + arg);
                }
                break;
            case EOp.Pop:
                Pop();
                break;
            case EOp.Swap:
                s0 = Pop();
                s1 = Pop();
                Push(s0);
                Push(s1);
                break;
            case EOp.PushBP:
                Push(bp);
                break;
            case EOp.PopBP:
                bp = Pop();
                break;
            case EOp.SPToBP:
                bp = sp;
                break;
            case EOp.BPToSP:
                sp = bp;
                break;
            case EOp.AddSP:
                s0 = Pop();
                // UW.EXE 0x68a2e (babl_run, BABL.C): the original only moves sp, so the
                // count stays in the first reserved word. Goldthirst's hero visit
                // (conversation 3, pc 2784) tests that word without ever writing it, and
                // the count sends the gift to the nugget test, as in the original.
                // One word more than the original reserves, for the arrays this VM keeps
                // one word higher; the rest is zeroed.
                Push(s0);
                for (int i = 0; i < s0; ++i) Push(0);
                break;
            case EOp.FetchM:
                s0 = Pop();
                {
                    disassemble.AppendLine($"    // push({VarForDisassembly(s0)})");
                }
                Push(mem[s0]);
                break;
            case EOp.Sto:
                s0 = Pop();
                s1 = Pop();
                {
                    disassemble.AppendLine($"    // {VarForDisassembly(s1)} = {s0}");
                }
                mem[s1] = s0;
                break;
            case EOp.Offset:
                s0 = Pop();
                s1 = Pop();
                Push(s0 + s1); // offset is a 1-indexed offset into an array (0 element is size?)
                break;
            case EOp.Start:
                // just a start marker
                break;
            case EOp.SaveReg:
                result = Pop();
                break;
            case EOp.PushReg:
                Push(result);
                break;
            case EOp.StrCmp:
                break;
            case EOp.Exit:
                yield = true;
                exit = true;
                EndConversation();
                break;
            case EOp.Say:
                s0 = Pop();
                AddDelayedText(MakeSubstitutions(s0));
                Magic.LearnSpellsFromText(stringBlock, s0);
                disassemble.AppendLine("    // " + StringLoader.GetString(stringBlock, s0));
                break;
            case EOp.Respond:
                yield = true;
                break;
            case EOp.Neg:
                Push(-Pop());
                break;
            default:
                // An opcode out of range ends the conversation the normal way, as the original does
                // (UW.EXE 0x68a70): variables written back, trade tray returned (slot 6, pc 1902).
                Debug.LogWarningFormat("Conversation {0}: bad opcode {1} at pc {2}. Ending.", conversationIndex, (int)op, ip - 1);
                EndConversation();
                break;
            }
        }

        // drop out
        if (maxInstructions == 0)
        {
            exit = true;
        }

        return exit;
    }

    /// <summary>
    /// The script's contains: whether the word appears in the text as a whole word, bounded by the ends,
    /// white space or punctuation, ignoring case (UW.EXE 0x68194, StringContains, BABL.C). A plain substring
    /// let a keyword match inside a longer word of the player's answer.
    /// </summary>
    private static bool ContainsWord(string text, string word)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word))
        {
            return false;
        }

        for (int at = text.IndexOf(word, StringComparison.CurrentCultureIgnoreCase); at >= 0;
             at = text.IndexOf(word, at + 1, StringComparison.CurrentCultureIgnoreCase))
        {
            int end = at + word.Length;
            if ((at == 0 || !char.IsLetterOrDigit(text[at - 1])) && (end >= text.Length || !char.IsLetterOrDigit(text[end])))
            {
                return true;
            }
        }

        return false;
    }

    private short Pop()
    {
        return mem[sp--];
    }

    private void Push(int v)
    {
        mem[++sp] = (short)v;
    }

    private ConversationImportRecord GetImport(int id)
    {
        // look up imported function
        foreach (var import in imports)
        {
            if (import.type == 0x0111 && import.id == id)
            {
                return import;
            }
        }

        return null;
    }

    private void CallImport(ConversationImportRecord import)
    {
        switch (import.name)
        {
        case "babl_menu":
            {
                // this is a 1-indexed list (with a 0-th element)
                int ptr = Arg(1);
                int index = 1;
                while (mem[ptr + index] != 0)
                {
                    string gambit = MakeSubstitutions(mem[ptr + index]);
                    Conversations.gambits.Add(new ConversationOption(index, gambit));
                    ++index;
                }
            }
            break;
        case "babl_fmenu":
            {
                // these are 1-indexed lists (with a 0-th element)
                int ptra = Arg(1) + 1;
                int ptrb = Arg(2) + 1;
                while (mem[ptra] != 0)
                {
                    if (mem[ptrb] != 0)
                    {
                        int stringIndex = mem[ptra];
                        string gambit = MakeSubstitutions(stringIndex);
                        Conversations.gambits.Add(new ConversationOption(stringIndex, gambit));
                    }

                    ++ptra;
                    ++ptrb;
                }
            }
            break;
        case "babl_ask":
            // get the player to input a string
            yield = true;
            babl_get_input = true;
            babl_input = "";
#if UNITY_EDITOR
            EditorApplication.ExecuteMenuItem("Window/General/Game");
#endif
            break;
        case "random":
            // 1 to n, both included: UW.EXE 0x67d0d (BabRand, BABL.C), rand() * n / 0x8000 + 1.
            // Random.Range(1, n) left n out, so Gulik's and Judy's random(2) always gave 1.
            result = (int)((long)Random.Range(0, 0x8000) * mem[Arg(1)] / 0x8000) + 1;
            break;
        case "print":
            AddDelayedText($"<color=#000000><i>{MakeSubstitutions(mem[Arg(1)])}</i></color>");
            break;
        case "get_quest":
            {
                int questFlag = mem[Arg(1)];
                result = PlayerData.GetQuestFlagFromConversation(conversationIndex, questFlag);
            }
            break;
        case "set_quest":
            {
                int value = mem[Arg(1)];
                int questFlag = mem[Arg(2)];
                PlayerData.SetQuestFlagFromConversation(conversationIndex, questFlag, value);
                // Sseetharee frees the prisoner after a trade struck in the lizardmen's tongue.
                if (conversationIndex == (int)EWhoAmI.Sseetharee && questFlag == (int)EQuestFlag.MurgoFreed && value != 0)
                {
                    PlayerData.sData.freedMurgoByTrade = true;
                }
                result = 0;
            }
            break;
        case "sex":
            {
                // there's a 0th element to this list...
                int ptra = Arg(1); // pointer if female
                int ptrb = Arg(2); // pointer if male
                result = mem[PlayerData.sData.female ? ptra : ptrb];
            }
            break;
        case "show_inv":
            {
                // the function copies the item positions and object ids of
                // all visible items in the barter area to the array in arg1
                // and arg2 (which needs at most 4 array values each)
                int ptra = Arg(1) + 1; // pointer to array of positions
                int ptrb = Arg(2) + 1; // point to array of items in barter area
                result = Inventory.sInv.Conversation_show_inv(ref mem, ptra, ptrb);
            }
            break;
        case "give_to_npc":
            {
                // transfers a number of items from the player trade tray to
                // the npc's inventory
                int ptra = Arg(1);
                int countAddr = Arg(2);
                int count = mem[countAddr];

                // --- HEURISTIC FIX ---
                // The game scripts are inconsistent. Some pass the base pointer of the
                // 1-indexed item array (Goldthirst), while others pre-calculate (using `offset`) and
                // pass the pointer to the first element (Shak).
                // If mem[ptra] == 0, we need to add 1 since 0 is not a valid player slot
                if (mem[ptra] == 0)
                {
                    ++ptra;
                }
            
                result = Inventory.sInv.Conversation_give_to_npc(npc, ref mem, ptra, count);
            }
            break;
        case "give_ptr_npc":
            {
                // TODO: check this (might need +1 to listPos)
                // e.g., giving Judy the pic of Tom
                int quantity = mem[Arg(1)];
                int listPos = mem[Arg(2)];
                result = Inventory.sInv.Conversation_give_ptr_npc(npc, ref mem, quantity, listPos);
            }
            break;
        case "take_from_npc":
            {
                bool npcHadItem = false;
                int itemId = mem[Arg(1)];
                if (itemId >= 1000)
                {
                    // One item of the class, the first in the speaker's inventory, as UW.EXE 0x6c041
                    // (npc_barter_give, BARTER.C): a class is item types (n - 1000) * 16 onwards, read
                    // straight from the type, so the launchers stay in class 1 as GetClass() would not.
                    // It goes into the pack, not the hand or a trade slot: the remake's pack has no limit.
                    int category = itemId - 1000;
                    for (int i = 0; i < npc.loot.Count; ++i)
                    {
                        UUObject obj = npc.loot[i];
                        if ((int)obj.type >> 4 == category)
                        {
                            npc.loot.RemoveAt(i);
                            obj.PostLoadInitialize();
                            Inventory.Add(obj);
                            npcHadItem = true;
                            break;
                        }
                    }
                }
                else
                {
                    // example is Ironwit, level 2, getting potion of Fly
                    foreach (UUObject obj in npc.loot)
                    {
                        if (obj.typeNum == itemId)
                        {
                            npc.loot.Remove(obj);
                            obj.PostLoadInitialize();
                            Inventory.Add(obj);
                            npcHadItem = true;
                            break;
                        }
                    }
                }

                if (npcHadItem)
                {
                    result = 1; // ok (2 = no space in player inventory (won't happen))
                }
                else
                {
                    result = 0; // npc didn't have it
                }
            }
            break;
        case "take_id_from_npc":
            {
                int index = Arg(1);
                result = Inventory.sInv.Conversation_take_id_from_npc(npc, index);
            }
            break;
        case "take_from_npc_inv":
            {
                // moves object from npc to player inventory, by npc inventory index (#16, Ishtass)
                int index = mem[Arg(1)];
                if (index > 0 && index <= npc.loot.Count)
                {
                    UUObject obj = npc.loot[index - 1];
                    npc.loot.RemoveAt(index - 1);
                    obj.PostLoadInitialize();
                    Inventory.Add(obj);
                    result = 1;
                }
                else
                {
                    result = 0;
                }
            }
            break;
        case "identify_inv":
            {
                // gets value of item and writes identification string to string block
                // possibly the unknown arguments determine what the function does (for example, actually identifying the item)
                // looks like only Dominus will set the lore on the item - possibly using x_obj_stuff 
                // used by Dominus [194], Shak [2] several times, the guard on level 7 you have to bribe [216], and Goldthirst [3]
            
                // need to see what's different in the calls (so that Shak doesn't mention the item is magical, while Dominus does).
                // also need to check that x_obj_stuff sets the lore result.
            
                int loreResult = mem[Arg(1)];
                int addrName = Arg(2);
                int addArticle = mem[Arg(3)];
                int tradeSlot = mem[Arg(4)];
                result = Inventory.sInv.Conversation_identify_inv(stringBlock, tradeSlot, addArticle, loreResult);
                mem[addrName] = 0;
            }
            break;
        case "do_offer":
            {
                Critter.ETradeResult tradeResult = npc.TryTrade();
                AddDelayedText(MakeSubstitutions(mem[Arg((int)tradeResult)]));
                // Scripts treat result as 1 = leave barter, 0 = stay (uw-formats). Only a deal made returns 1, as in
                // UW.EXE 0x6b69f (do_offer, BARTER.C): a weary trader keeps the player in the barter, where every
                // offer is refused but a demand or a decline is still possible.
                result = tradeResult == Critter.ETradeResult.Yes ? 1 : 0;
            }
            break;
        case "do_demand":
            {
                int deny = mem[Arg(1)];
                int accept = mem[Arg(2)];
                int attitude = (int)npc.attitude;
                int attitudeId = 0;
                foreach (var varImport in imports)
                {
                    if (varImport.name == "npc_attitude")
                    {
                        attitude = mem[varImport.id];
                        attitudeId = varImport.id;
                        break;
                    }
                }
                if (npc.GivesInToDemand(attitude))
                {
                    AddDelayedText(MakeSubstitutions(accept));
                    // one step down, to hostile at the lowest: UW.EXE 0x6b84a (do_demand, BARTER.C)
                    if (attitude > 0)
                    {
                        SetNpcAttitude(attitudeId, (Critter.EAttitude)(--attitude));
                    }
                    npc.StealBarterItems();
                    result = 1; // accepted
                }
                else
                {
                    AddDelayedText(MakeSubstitutions(deny));
                    // The original sets the NPC to attack the player (goal 5, target 1); the remake makes it hostile.
                    SetNpcAttitude(attitudeId, Critter.EAttitude.Hostile);
                    result = 0; // denied
                }
            }
            break;
        case "do_inv_create":
            {
                EObjectType type = (EObjectType) mem[Arg(1)];
                // create item in npc inventory
                UUObject obj = LevelLoader.CreateObjectOfType(type);
                obj.PostLoadInitialize();
                npc.loot.Add(obj);
                result = npc.loot.Count - 1;
            }
            break;
        case "do_inv_delete":
            {
                // e.g., Marrowsuck - deleting the scales and thread when making the boots
                EObjectType type = (EObjectType) mem[Arg(1)];
                // delete item from npc inventory
                result = 0;
                for (int i = 0; i < npc.contents.Count; ++i)
                {
                    if (npc.contents[i].type == type)
                    {
                        npc.contents.RemoveAt(i);
                        result = 1;
                        break;
                    }
                }
            }
            break;
        case "check_inv_quality":
            {
                int itemPosition = mem[Arg(1)];
                result = Inventory.sInv.Conversation_check_inv_quality(itemPosition);
            }
            break;
        case "set_inv_quality":
            {
                int quality = mem[Arg(1)];
                // Immediate NPC-inv position (Shak pushes 2), same convention as take_id_from_npc.
                int itemIndex = Arg(2);
                Inventory.sInv.Conversation_set_inv_quality(npc, itemIndex, quality);
                result = 0; // no result.
            }
            break;
        case "count_inv":
            {
                int pos = mem[Arg(1)];
                result = Inventory.sInv.Conversation_count_inv(pos);
            }
            break;
        case "setup_to_barter":
            {
                // move things into the npc's barter tray
                npc.Conversation_setup_to_barter();
                result = 1;
            }
            break;
        case "do_judgement":
            {
                // appraise current trade
                string judgement = Inventory.sInv.Conversation_do_judgement();
                output.AppendLine(judgement);
                LogToHistory(null, judgement, Messages.EHistoryKind.Narration);
                result = 0;
            }
            break;
        case "do_decline":
            {
                // should just remove items from the barter tray back to the npc
                result = npc.Conversation_do_decline();
            }
            break;
        case "set_likes_dislikes":
            {
                // -1 terminated lists
                int ptrToLikes = Arg(2);
                int ptrToDislikes = Arg(1);
                // pass these on to the barter system to determine if a deal is acceptable
                npc.likes.Clear();
                for (; mem[ptrToLikes] != -1; ++ptrToLikes)
                {
                    npc.likes.Add((EObjectType)mem[ptrToLikes]);
                }
                npc.dislikes.Clear();
                for (; mem[ptrToDislikes] != -1; ++ptrToDislikes)
                {
                    npc.dislikes.Add((EObjectType)mem[ptrToDislikes]);
                }
                result = 0;
            }
            break;
        case "gronk_door":
            {
                // UW.EXE 0x69fc9 (gronk_door, BABLHACK.C): the door at tile (arg3, arg2), each argument
                // through its variable; 0 opens, 1 closes, 2 toggles, anything else does nothing.
                // The lock is left alone: OpenDoor (UW.EXE 0x387c6, USEITEMS.C) never looks at it, so
                // a door a character opens is locked again once the player closes it (Dorna's armory,
                // Rawstag's door, Goldthirst's treasure chamber). A door on the move is still the
                // tile's door here, and Open() and Close() turn it round, as the original does.
                int doorX = mem[Arg(3)];
                int doorY = mem[Arg(2)];
                int mode = mem[Arg(1)];

                Door door = LevelLoader.GetTile(doorX, doorY).door;
                if (door != null)
                {
                    switch (mode)
                    {
                    case 0:
                        door.Open();
                        break;
                    case 1:
                        door.Close();
                        break;
                    case 2:
                        door.Toggle();
                        break;
                    }
                    result = 1;
                }
                else
                {
                    result = 0;
                }
            }
            break;
        case "set_race_attitude":
            {
                // UW.EXE 0x699dc (set_race_attitude, BABLHACK.C): every creature of the speaker's own
                // type and of the race, not a loner, in the square of range tiles around the speaker's
                // tile, clipped to 1..63, takes the attitude. A loner (byte 0xa bit 7, deafToPackAlarm)
                // is always skipped here, whatever the pack alarm's switch says: the user's choice.
                int range = mem[Arg(1)];
                Critter.EAttitude attitude = (Critter.EAttitude)(mem[Arg(2)] & 3);
                int race = mem[Arg(3)];
                Vector2Int home = Critter.TileOf(npc.transform.position);
                int x0 = Mathf.Max(1, home.x - range);
                int y0 = Mathf.Max(1, home.y - range);
                int x1 = home.x + range >= 64 ? 63 : home.x + range;
                int y1 = home.y + range >= 64 ? 63 : home.y + range;
                foreach (Critter crit in UnityEngine.Object.FindObjectsByType<Critter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    Vector2Int tile = Critter.TileOf(crit.transform.position);
                    if (crit.IsAlive && crit.type == npc.type && crit.race == race && !crit.deafToPackAlarm
                        && tile.x >= x0 && tile.x <= x1 && tile.y >= y0 && tile.y <= y1)
                    {
                        crit.attitude = attitude;
                    }
                }
            }
            break;
        case "place_object":
            {
                int tileY = mem[Arg(1)];
                int tileX = mem[Arg(2)];
                int itemSlot = mem[Arg(3)]; // from do_inv_create
                // place in world
                UUObject obj = npc.loot[itemSlot];
                npc.loot.RemoveAt(itemSlot);
                Tile t = LevelLoader.GetTile(tileX, tileY);
                obj.gameObject.SetActive(true);
                obj.x = 3;
                obj.y = 3;
                obj.WorldInitialize(t, t.x, t.y);
                obj.PostLoadInitialize();
                result = 1;
            }
            break;
        case "remove_talker":
            // remove the npc at the end of the conversation
            npc.removeTalker = true;
            break;
        case "set_attitude":
            {
                // Both arguments through the script's variables, as every call passes them
                // (UW.EXE 0x699a1): the attitude, 0 hostile to 3 friendly, and the whoami of the
                // creature that takes it.
                Critter.EAttitude attitude = (Critter.EAttitude)mem[Arg(1)];
                int whoami = mem[Arg(2)];
                // find the npc that has this whoami - slow, but during conversation, so not a big deal
                foreach (Critter crit in UnityEngine.Object.FindObjectsByType<Critter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if ((int) crit.whoami == whoami)
                    {
                        crit.attitude = attitude;
                        break;
                    }
                }
            }
            break;
        case "x_skills":
            {
                int val = mem[Arg(1)];
                int skill = mem[Arg(2)];
                if (val == 10000)
                {
                    // do it twice like the shrine does?
                    Skills.AdvanceSkill((ESkill)skill);
                }
                else
                {
                    result = PlayerData.sData.skill[skill];
                }
            }
            break;
        case "x_traps":
            {
                // according to hank, called x_traps because it uses the same variables as trap SetVariable/CheckVariable
                int val = mem[Arg(1)];
                int var = mem[Arg(2)];
                if (val == 10001)
                {
                    result = PlayerData.sData.globalVars[var];
                }
                else
                {
                    PlayerData.sData.globalVars[var] = val;
                }
            }
            break;
        case "x_obj_stuff":
            {
                // get or set properties of object
                result = Inventory.sInv.Conversation_x_obj_stuff(ref mem, Arg(9), Arg(8), Arg(7), Arg(6), Arg(5), Arg(4), Arg(3),
                    Arg(2), Arg(1));
            }
            break;
        case "find_inv":
            {
                result = 0;
                int inPlayerInv = mem[Arg(1)];
                EObjectType itemId = (EObjectType)mem[Arg(2)];
                if (inPlayerInv == 1)
                {
                    // search player for item
                    if (Inventory.sInv.FindObjectInInventory(itemId) != null)
                    {
                        result = 1;
                    }
                }
                else
                {
                    // search npc for item
                    for (int i = 0; i < npc.loot.Count; ++i)
                    {
                        if (npc.loot[i].type == itemId)
                        {
                            result = i + 1;
                            break;
                        }
                    }
                }
            }
            break;
        case "find_barter":
            {
                int itemId = mem[Arg(1)];
                // searches for item in barter area
                // returns pos in inventory object list, or 0
                result = Inventory.sInv.Conversation_find_barter(itemId);
            }
            break;
        case "find_barter_total":
            {
                // ???
                result = 0;
                int ptrToCount = Arg(1);
                int ptrToSlots = Arg(2) + 1;
                int ptrToSlotCount = Arg(3);
                int itemId = mem[Arg(4)];

                result = Inventory.sInv.Conversation_find_barter_total(itemId, ref mem, ptrToSlots, ptrToSlotCount);
                mem[ptrToCount] = (short)result;
            }
            break;
        case "compare":
            {
                int a = mem[Arg(1)];
                int b = mem[Arg(2)];
                string stringA = StringLoader.GetString(stringBlock, a);
                string stringB = StringLoader.GetString(stringBlock, b);
                result = stringA.Equals(stringB, StringComparison.CurrentCultureIgnoreCase) ? 1 : 0;
            }
            break;
        case "contains":
            {
                // for example: ask questions of grey lizardmen in south of level 3
                int a = mem[Arg(1)];
                int b = mem[Arg(2)];
                string stringA = StringLoader.GetString(stringBlock, a);
                string stringB = StringLoader.GetString(stringBlock, b);
                result = ContainsWord(stringA, stringB) ? 1 : 0;
            }
            break;
        case "length":
            {
                // Lakshi Longtooth on level 4 uses this when being asked "tell me about..."
                // I don't know yet what he needs it for, but this doesn't break anything.
                // (stringA is the player's input string)
                int a = mem[Arg(1)];
                string stringA = StringLoader.GetString(stringBlock, a);
                result = stringA.Length; 
            }
            break;
        default:
            Debug.LogFormat("Function {0} was thought to be unused!", import.name);
            break;
        }
    }

    private int Arg(int i)
    {
        return mem[sp - i];
    }

    private void SetNpcAttitude(int attitudeId, Critter.EAttitude newAttitude)
    {
        if (attitudeId > 0)
        {
            mem[attitudeId] = (short)newAttitude;
        }
        else
        {
            // just directly set
            --npc.attitude;
        }
        
    }

    private static readonly string[][] substitutions =
    {
        new[]{ "didst misunderstood", "didst misunderstand" },
        new[]{ "caves, uninhabited", "caves uninhabited" },
        new[]{ "wilt surely", "wilt doubtless" }, // remove double surely
        new[]{ "that why I", "that is why I" },
        new[]{ "you must!.", "you must!" },
        new[]{ "please   let me know", "please let me know" }, // three spaces!
        new[]{ " where it is put", "where it is put" },
        new[]{ "fields of the Stygian", "fields of the Abyss" }, // garamon
        new[]{ "A battle took place", "a battle took place" },
        new[]{ ".   ", ".  " },
        new[]{ "thou returns", "thou return" },
        new[]{ "heppened", "happened" },
        new[]{ @"\1", "<color=#873E13><i>" },
        new[]{ @"\0", "</i></color>" },
        new[]{ "foolist", "foolish" },
        new[]{ "Er...I see.", "Err... I see." },
        new[]{ "\"Tis", "'Tis" },
        new[]{ " me me ", " to me " },
        new[]{ "possesive", "possessive" },
        new[]{ "civilty", "civility" },
        new[]{ "disrepect", "disrespect" },
        new[]{ "runebag", "rune bag" },
        new[]{ "Battlesites", "Battle sites" },
        new[]{ " a be ", " be a " },
        new[]{ " too see ye ", " to see ye " },
        new[]{ "attemping", "attempting" },
        new[]{ "indestructable", "indestructible" },
        new[]{ "valorious", "valorous" },
        new[]{ "burden on", "burden to" },
        new[]{ "safer then", "safer than" },
        new[]{ " the the ", " the " },
        new[]{ "notihg", "nothing" },
        new[]{ " it as ", " it is as " },
        new[]{ " to forced ", " to be forced " },
        new[]{ " gamy leg", " gammy leg" },
        new[]{ "Its really", "It's really" },
        new[]{ "I bge ", "I beg" },
        new[]{ "acquiantance", "acquaintance" },
        new[]{ "knowledgable", "knowledgeable" },
        new[]{ "amoungst", "amongst" },
        new[]{ "Why he\u2019s", "Why, he\u2019s" }
    };

    private string MakeSubstitutions(int stringIndex)
    {
        string r = StringLoader.GetString(stringBlock, stringIndex);

        foreach (var sub in substitutions)
        {
            r = r.Replace(sub[0], sub[1]);
        }

        // specific hack for Marrowsuck conversation (don't want to change replacements to a regex for perf reasons)
        if (r == "Farewell" || r.EndsWith(" that") || r.EndsWith(" time") || r.EndsWith(" me"))
        {
            r = r + ".";
        }

        // (?:blah) makes a non-capturing group.
        Regex regex = new Regex(@"@([GSP])([SI])(-?\d+)(?:(?:C)(-?\d+))?");
        while (true)
        {
            Match match = regex.Match(r);
            if (match.Groups.Count > 1)
            {
                string preMatch = r.Substring(0, match.Index);
                string postMatch = r.Substring(match.Index + match.Length);

                string sourceType = match.Groups[1].Value;
                string substitutionType = match.Groups[2].Value;
                int value = Int32.Parse(match.Groups[3].Value);
                Int32.TryParse(match.Groups[4].Value, out int index);
                
                string replacement = "";

                switch (sourceType)
                {
                case "G": // global
                    replacement = GetGlobalVariable(value);
                    if (replacement == "" || index != 0)
                    {
                        // only place index is used is to pick out minutes from an hour/minute struct.
                        // one of the two is an amount of time (@GI<N>C2) and the other is a time remaining (@SI<N>C2).
                        replacement = "";
                        value = mem[value + index];
                    }
                    break;
                case "S": // "stack" var
                    {
                        value = mem[bp + value + index];
                    }
                    break;
                case "P": // pointer
                    {
                        int ptr = mem[bp + value];
                        value = mem[ptr];
                    }
                    break;
                }

                if (replacement == "")
                {
                    switch (substitutionType)
                    {
                    case "S":
                        replacement = StringLoader.GetString(stringBlock, value);
                        break;
                    case "I":
                        replacement = value.ToString();
                        break;
                    }
                }

                r = preMatch + replacement + postMatch;
            }
            else
            {
                break;
            }
        }
        return r;
    }

    private string GetImportedGlobalVariablename(int id)
    {
        foreach (ConversationImportRecord import in imports)
        {
            if (import.type == 0x010f && import.id == id)
            {
                return import.name;
            }
        }

        return "";
    }

    private string GetGlobalVariable(int id)
    {
        string var = "";
        foreach (ConversationImportRecord import in imports)
        {
            if (import.type == 0x010f && import.id == id)
            {
                switch (import.name)
                {
                case "play_name":
                    var = PlayerData.sData.playerName;
                    break;
                case "npc_name":
                    var = npcName;
                    break;
                case "npc_xhome":
                    var = npc.xhome.ToString();
                    break;
                case "npc_yhome":
                    var = npc.yhome.ToString();
                    break;
                case "npc_whoami":
                    var = npc.whoami.ToString();
                    break;
                case "npc_hunger":
                    var = npc.hunger.ToString();
                    break;
                case "npc_health":
                    var = npc.hp.ToString();
                    break;
                case "npc_hp":
                    var = npc.hp.ToString();
                    break;
                case "npc_power":
                    break;
                case "npc_goal":
                    var = npc.goal.ToString();
                    break;
                case "npc_attitude":
                    var = npc.attitude.ToString();
                    break;
                case "npc_gtarg":
                    var = npc.gtarg.ToString();
                    break;
                case "npc_talkedTo":
                    var = npc.talkedTo.ToString();
                    break;
                case "npc_level":
                    var = npc.critterLevel.ToString();
                    break;
                case "game_time":
                    break;
                case "game_days":
                    break;
                case "game_mins":
                    break;
                }

                break;
            }
        }

        return var;
    }

    public int conversationIndex;
    public Critter npc;
    public string npcName;
    public readonly int stringBlock;
    public int memSlotsForVars;
    private readonly ConversationImportRecord[] imports;
    private readonly short[] code;
    private int privateGlobalsLength;

    public static StringBuilder output;

    /// <summary>
    /// A line of the conversation into the message history, after the name of who says it, so
    /// that Page Up can read it again once the conversation is over, and after a load. A line
    /// nobody says, such as the appraisal of a trade, goes in with no name. The scroll's tags are
    /// taken off, since the history breaks its lines where a tag could be cut in two; kind gives
    /// the line its colour there instead, line by line.
    /// </summary>
    public static void LogToHistory(string speaker, string text, Messages.EHistoryKind kind)
    {
        text = text == null ? "" : RichTextTag.Replace(text, "").Trim();
        if (text.Length == 0)
        {
            return;
        }
        string name = string.IsNullOrEmpty(speaker) ? "" : char.ToUpper(speaker[0]) + speaker.Substring(1) + ": ";
        Messages.AddToHistory(name + text, kind);
    }

    private static readonly Regex RichTextTag =
        new (@"</?(?:color|i|b|size)(?:=[^>]*)?>");

    private short[] mem;
    private static int sp;
    private static int ip;
    private static int bp;
    public static int result;
    private static int call_level;
}

public class ConversationImportRecord
{
    public ConversationImportRecord(Stream stream)
    {
        int nameLen = stream.GetUShort();
        name = stream.GetString(nameLen);
        id = stream.GetUShort();
        unk = stream.GetUShort();
        type = stream.GetUShort();
        returnType = stream.GetUShort();
    }

    public readonly string name;
    public readonly int id;
    public int unk;
    public readonly int type;
    public int returnType;
}

[Serializable]
public class Conversations : MonoBehaviour
{
    public Conversation[] conversations;
    public static Conversations sConversations;
    public static Conversation runningConversation;

    private static bool waitForEnd;
    private bool waitForRelease;
    private bool waitAFrameAfterRelease;
    private static int currentGambit;

    private static float chatScrollOffset;
    private static int lastOutputLength;
    private static float chatMaxScroll;

    public static List<ConversationOption> gambits;

    public Texture2D[] convTex;
    public Texture2D[] scrledgeTex; // scroll paper edges
    public Texture2D[] charHeadTex;
    public Texture2D[] genHeadsTex;
    public Texture2D[] headsTex; // avatar heads

    public class SparseEnumArrayAttribute : PropertyAttribute
    {
        public Type EnumType { get; private set; }
        public SparseEnumArrayAttribute(Type enumType) { EnumType = enumType; }
    }

    [Serializable]
    public class SparseTextureArray
    {
        public Texture2D[] entries;
    }
    
    [SparseEnumArray(typeof(EWhoAmI))]
    public SparseTextureArray newCharPortraits;
    public Texture2D[] newGenPortraits;
    public Texture2D[] newPlayerPortraits;

    public Font font;

    public AudioClip startConversationSound;
    public AudioClip moveSelection;
    public AudioClip makeSelection;

    public float chatScrollWheelSpeed = 40f;
    public float chatScrollStickSpeed = 300f;
    public float chatScrollStickDeadZone = 0.1f;

    protected void Awake()
    {
        gambits = new List<ConversationOption>();

        sConversations = this;
        {
            Stream stream = new Stream("../Data/cnv.ark");
            int numConversations = stream.GetUShort();
            int[] offsets = stream.GetIntArray(numConversations);

            conversations = new Conversation[numConversations];
            for (int i = 0; i < numConversations; ++i)
            {
                if (offsets[i] != 0)
                {
                    stream.Seek(offsets[i]);
                    conversations[i] = new Conversation(stream, i);
                }
            }
        }

        {
            Stream stream = new Stream("../Data/babglobs.dat");
            while (!stream.End())
            {
                int slot = stream.GetUShort();
                conversations[slot].InitializePrivateGlobals(stream);
            }
        }

        // 0 - name
        // 1 - trade tray
        // 2 - portrait
        // 3 - scroll top
        // 4 - scroll bottom
        // 5 - inventory
        convTex = GraphicsLoader.GetTextures("../Data/converse.gr", 1.0f, TextureWrapMode.Clamp);
        scrledgeTex = GraphicsLoader.GetTextures("../Data/scrledge.gr", 1.0f, TextureWrapMode.Clamp);
        charHeadTex = GraphicsLoader.GetTextures("../Data/charhead.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        genHeadsTex = GraphicsLoader.GetTextures("../Data/genhead.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        headsTex = GraphicsLoader.GetTextures("../Data/heads.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
    }

    public static void TryGetGivenName(ref string givenName, int whoami)
    {
        if (sConversations.conversations[whoami] != null)
        {
            givenName = StringLoader.GetString(7, 16 + whoami);
        }
    }

    public static bool StartConversation(Critter critter)
    {
        bool started = false;
        if (runningConversation == null)
        {
            int conversationIndex = (int)critter.whoami;
            string npcName = critter.singularArticle + critter.singularName;
            runningConversation = sConversations.conversations[conversationIndex];
            if (runningConversation == null)
            {
                // for generic critters, need to be friendly or mellow
                if (critter.attitude != Critter.EAttitude.Upset)
                {
                    conversationIndex = (int)critter.type + 192;
                    runningConversation = sConversations.conversations[conversationIndex];
                }
            }
            else
            {
                npcName = StringLoader.GetString(7, 16 + conversationIndex);
            }

            if (runningConversation != null)
            {
                Messages.Clear();
                runningConversation.npc = critter;
                critter.StartConversation();
                runningConversation.conversationIndex = conversationIndex;
                runningConversation.StartConversation(npcName);

                // The original asks for its screens track while you talk, the same one it asks
                // for when you sleep.
                Music.Conversation();
                waitForEnd = false;
                chatScrollOffset = 0f;
                lastOutputLength = 0;
                chatMaxScroll = 0f;
                started = true;

                Utils.PlayClip2d(sConversations.startConversationSound);
                
                PlayerObject.DisableControls(EControlMask.Conversation, true);

                PlayerData.sData.MarkHadConversation(conversationIndex);

                Time.timeScale = 0.0f;
            }
        }

        return started;
    }

    protected void Update()
    {
        if (SmallWindowWarning.BlocksInput)
        {
            // the small-window warning is modal: nothing here answers until it is closed and its keys let go
            return;
        }

        // Inventory sets controlsDisabled.Inventory while the panel is open — do not gate Conversation.Update on it or dialogue stalls.
        // Still pause while How Many? is open (quantity dialog).
        if (runningConversation != null && (PlayerObject.Player.controlsDisabled & EControlMask.HowMany) == 0)
        {
            UpdateConversationChatScroll();

            if (waitForEnd)
            {
                bool gpConfirm = GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false;
                bool mouseConfirm = (GameInput.CurrentMouse?.leftButton.wasPressedThisFrame ?? false)
                    || (GameInput.CurrentMouse?.rightButton.wasPressedThisFrame ?? false)
                    || AnyKeyEndsConversation();

                if (gpConfirm || mouseConfirm)
                {
                    waitForEnd = false;
                    Utils.PlayClip2d(makeSelection);
                    // Only wait for A release when we actually dismissed with A — otherwise (mouse + connected
                    // gamepad) we'd stall forever waiting for an A release that never comes.
                    if (gpConfirm)
                    {
                        waitForRelease = true;
                    }
                    else
                    {
                        waitAFrameAfterRelease = true;
                    }
                    //System.IO.File.WriteAllText("lastConversation.txt", runningConversation.disassemble.ToString());
                }
            }
            else if (waitForRelease)
            {
                if (GameInput.CurrentGamepad?.aButton.wasReleasedThisFrame ?? true)
                {
                    waitForRelease = false;
                    waitAFrameAfterRelease = true;
                }
            }
            else if (waitAFrameAfterRelease)
            {
                waitAFrameAfterRelease = false;
                runningConversation = null;
                Music.ResumeExploringMusic();
                PlayerObject.DisableControls(EControlMask.Conversation, false);
                Time.timeScale = 1.0f;

                // Bartering opens the inventory panel and nothing closed it again, so you walked
                // away from every trade with the panel still up. This is the single place in the
                // project where a conversation ends - farewell, dismissal and interruption all
                // pass through here - so one check covers every exit route.
                // fromUserToggle is false because this is not the player reaching for the panel;
                // it is the same value PlayerPanelInput passes when it closes a panel by itself,
                // and TutorialManager.NotifyPanelChanged uses it to tell the two apart.
                if (PlayerPanelState.IsExploringInventory)
                {
                    PlayerPanelState.SetPanel(EPlayerPanel.None, false);
                }
            }
            else
            {
                if (gambits.Count > 0)
                {
                    // Keyboard 1–9 always pick gambits (even with inventory open). Mouse clicks too (OnGUI).
                    // With inventory open, gamepad dpad / A stay on inventory — not gambits.
                    if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
                    {
                        TrySelectGambitByNumberKeys();
                    }
                    else if (!PlayerPanelState.IsEffectivelyExploringInventory)
                    {
                        if ((GameInput.CurrentGamepad?.dpad.up.wasPressedThisFrame ?? false) && currentGambit > 0)
                        {
                            --currentGambit;
                            Utils.PlayClip2d(moveSelection);
                        }
                        else if ((GameInput.CurrentGamepad?.dpad.down.wasPressedThisFrame ?? false) && currentGambit < gambits.Count - 1)
                        {
                            ++currentGambit;
                            Utils.PlayClip2d(moveSelection);
                        }

                        if (GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false)
                        {
                            SelectGambitAtIndex(currentGambit);
                        }
                    }
                }

                if (runningConversation.Update())
                {
                    // ended
                    waitForEnd = true;
                }
            }
        }
    }

    /// <summary>Draws tex with its top left at x, y in reference pixels (UiScale).</summary>
    private void DrawTexture(Texture2D tex, float x, float y, float aspect)
    {
        UiScale.DrawTexture(UiScale.R(x, y, 3.6f / aspect * tex.width, 3.6f * tex.height), tex);
    }

    private static readonly Color32 textColor = new Color32(50, 29, 18, 255);
    private static readonly Color32 nameTextColor = new Color32(145, 147, 178, 255);
    private static readonly Color32 paperColor = new Color32(143, 103, 81, 255);
    private static readonly Color32 paperEdgeColor = new Color32(77, 57, 40, 255);

    /// <summary>
    /// Interpolate layout compression between 1000px (none) and 800px (max): shorter chat log and tighter vertical space around the option strip.
    /// The heights are reference pixels (UiScale); a screen is never less than 768 of them tall. At the most the
    /// option strip also loses boxTrim above and below its answers: it moves up by that, and is that much shorter; and its
    /// top edge, the line under the chat, comes down by dividerDrop over the answers, which do not move.
    /// </summary>
    private static void GetConversationShortLayout(out float chatCompress, out float gapCompress, out float boxTrim)
    {
        GetConversationShortLayout(out chatCompress, out gapCompress, out boxTrim, out _);
    }

    private static void GetConversationShortLayout(out float chatCompress, out float gapCompress, out float boxTrim,
        out float dividerDrop)
    {
        const float refHeight = 1000f;
        const float minHeight = 800f;
        float h = UiScale.Height;
        if (h >= refHeight)
        {
            chatCompress = 0f;
            gapCompress = 0f;
            boxTrim = 0f;
            dividerDrop = 0f;
            return;
        }

        float t = (refHeight - h) / (refHeight - minHeight);
        t = Mathf.Clamp01(t);
        chatCompress = 100f * t;
        gapCompress = 50f * t;
        boxTrim = 7f * t;
        dividerDrop = 8f * t;
    }

    /// <summary>
    /// The conversation's width in reference pixels (UiScale). It was 882 from the first release; the right side, which
    /// had 18 pixels between the end of the text and the scroll edge, gave them up on 30 October 2026 (mod 640), and
    /// everything right of the middle moved left with it, the text keeping its 842.
    /// </summary>
    public const float PanelWidth = 864f;

    /// <summary>How much the right half moved left when the panel lost its 20: name box, trade tray, portrait.</summary>
    private const float RightShift = 882f - PanelWidth;

    /// <summary>Left of the trade trays, from the panel's left edge, for <see cref="Inventory"/>'s trade slots.</summary>
    public const float LeftTrayX = 140f;
    public const float RightTrayX = 575f - RightShift;

    /// <summary>
    /// The panel's left edge in reference pixels: centred, but never so far right that it runs under the inventory,
    /// which slides in from the right for a trade (1366 wide, the narrowest, puts it four pixels clear).
    /// </summary>
    public static float PanelLeft =>
        Mathf.Min((UiScale.Width - PanelWidth) / 2, UiScale.Width - Inventory.InvSlide - PanelWidth - 4f);

    /// <summary>Screen-space rect for the centered conversation UI (matches <see cref="OnGUI"/> layout).</summary>
    public static Rect GetConversationPanelGuiRect()
    {
        if (runningConversation == null)
        {
            return default;
        }

        GetConversationShortLayout(out float chatCompress, out float gapCompress, out float boxTrim);
        const float panelWidth = PanelWidth;
        float xoff = PanelLeft;
        const float optionsPanelBaseTop = 760f;
        float optionsTop = optionsPanelBaseTop - chatCompress - gapCompress - boxTrim;
        const float top = 10f;
        float bottom = optionsTop + 4f + 160f - boxTrim + 4f;
        return UiScale.R(xoff, top, panelWidth, bottom - top);
    }

    /// <summary>Screen-space rect for the conversation chat log (matches <see cref="OnGUI"/> viewRect).</summary>
    public static Rect GetConversationChatViewRect()
    {
        if (runningConversation == null)
        {
            return default;
        }

        GetConversationShortLayout(out float chatCompress, out _, out _, out float dividerDrop);
        float xoff = PanelLeft;
        float viewRectHeight = 500f - chatCompress + dividerDrop;
        return UiScale.R(xoff + 10f, 200f, GambitPanelWidth, viewRectHeight);
    }

    private static bool IsChatScrollAllowed()
    {
        return runningConversation != null
            && (gambits.Count > 0 || runningConversation.babl_get_input || waitForEnd)
            && runningConversation.pauseTime <= 0f;
    }

    private void UpdateConversationChatScroll()
    {
        int outputLength = Conversation.output?.Length ?? 0;
        if (outputLength != lastOutputLength)
        {
            lastOutputLength = outputLength;
            chatScrollOffset = 0f;
        }

        if (IsChatScrollAllowed())
        {
            if (chatMaxScroll > 0f)
            {
                Vector2 stick = GameInput.CurrentGamepad?.leftStick.ReadValue() ?? Vector2.zero;
                if (Mathf.Abs(stick.y) > chatScrollStickDeadZone)
                {
                    chatScrollOffset += stick.y * chatScrollStickSpeed * Time.unscaledDeltaTime;
                    chatScrollOffset = Mathf.Clamp(chatScrollOffset, 0f, chatMaxScroll);
                }
            }
        }
        else
        {
            chatScrollOffset = 0f;
        }
    }

    public static bool IsGuiMouseOverConversationPanel(Vector2 guiMouse)
    {
        Rect r = GetConversationPanelGuiRect();
        return r.width > 0f && r.height > 0f && r.Contains(guiMouse);
    }

    private const float GambitPanelWidth = 842f;
    private const float GambitLineGap = 2f;

    /// <summary>
    /// A key pressed this frame that ends a conversation: any key, as the original's reference card says ("click either
    /// mouse-button or press any key"), on the press as a click, but Shift, Ctrl, Alt and the Windows keys alone, Print
    /// Screen, the function keys (F12 is Steam's screenshot key), the lock keys and Pause, and anything with Alt held
    /// (Alt-Enter, Alt-Tab), so a screenshot or a switch of window does not end it.
    /// </summary>
    private static bool AnyKeyEndsConversation()
    {
        Keyboard kb = GameInput.CurrentKeyboard;
        if (kb == null || !kb.anyKey.wasPressedThisFrame || GameInput.AltHeld)
        {
            return false;
        }
        foreach (KeyControl k in kb.allKeys)
        {
            if (k == null || !k.wasPressedThisFrame)
            {
                continue;
            }
            switch (k.keyCode)
            {
            // Key is also the project's door key class: the Input System's is named in full
            case UnityEngine.InputSystem.Key.LeftShift:
            case UnityEngine.InputSystem.Key.RightShift:
            case UnityEngine.InputSystem.Key.LeftCtrl:
            case UnityEngine.InputSystem.Key.RightCtrl:
            case UnityEngine.InputSystem.Key.LeftAlt:
            case UnityEngine.InputSystem.Key.RightAlt:
            case UnityEngine.InputSystem.Key.LeftMeta:
            case UnityEngine.InputSystem.Key.RightMeta:
            case UnityEngine.InputSystem.Key.ContextMenu:
            case UnityEngine.InputSystem.Key.PrintScreen:
            // the function keys, F12 being Steam's screenshot key, and the lock keys: none is a way to answer
            case UnityEngine.InputSystem.Key.F1:
            case UnityEngine.InputSystem.Key.F2:
            case UnityEngine.InputSystem.Key.F3:
            case UnityEngine.InputSystem.Key.F4:
            case UnityEngine.InputSystem.Key.F5:
            case UnityEngine.InputSystem.Key.F6:
            case UnityEngine.InputSystem.Key.F7:
            case UnityEngine.InputSystem.Key.F8:
            case UnityEngine.InputSystem.Key.F9:
            case UnityEngine.InputSystem.Key.F10:
            case UnityEngine.InputSystem.Key.F11:
            case UnityEngine.InputSystem.Key.F12:
            case UnityEngine.InputSystem.Key.CapsLock:
            case UnityEngine.InputSystem.Key.NumLock:
            case UnityEngine.InputSystem.Key.ScrollLock:
            case UnityEngine.InputSystem.Key.Pause:
                continue;
            }
            return true;
        }
        return false;
    }

    private void TrySelectGambitByNumberKeys()
    {
        if (gambits.Count == 0)
        {
            return;
        }

        Keyboard kb = GameInput.CurrentKeyboard;
        if (kb == null)
        {
            return;
        }

        KeyControl[] digits =
        {
            kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key,
            kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key
        };

        for (int i = 0; i < digits.Length && i < gambits.Count; ++i)
        {
            KeyControl d = digits[i];
            if (d != null && d.wasPressedThisFrame)
            {
                SelectGambitAtIndex(i);
                return;
            }
        }
    }

    /// <summary>Height/layout string: always italic body so hover and highlight don’t reflow; optional "n. " prefix in mouse UI.</summary>
    private static string FormatGambitMeasurement(ConversationOption opt, int zeroBasedIndex, bool mouseUi)
    {
        if (mouseUi)
        {
            return $"{zeroBasedIndex + 1}. <i>{opt.gambit}</i>";
        }

        return "<i>" + opt.gambit + "</i>";
    }

    private static string FormatGambitDisplay(ConversationOption opt, int zeroBasedIndex, bool mouseUi, bool highlighted)
    {
        if (mouseUi)
        {
            if (highlighted)
            {
                return $"{zeroBasedIndex + 1}. <i><color=#873E13>{opt.gambit}</color></i>";
            }

            return $"{zeroBasedIndex + 1}. <i>{opt.gambit}</i>";
        }

        if (highlighted)
        {
            return "<i><color=#873E13>" + opt.gambit + "</color></i>";
        }

        return "<i>" + opt.gambit + "</i>";
    }

    private static string FormatGambitLineForLog(ConversationOption opt)
    {
        return $"<color=#873E13><i>{opt.gambit}</i></color>";
    }

    private void SelectGambitAtIndex(int index)
    {
        if (index < 0 || index >= gambits.Count)
        {
            return;
        }

        ConversationOption chosen = gambits[index];
        Conversation.result = chosen.actualIndex;
        Conversation.output.AppendLine(FormatGambitLineForLog(chosen));
        Conversation.LogToHistory(PlayerData.sData.playerName, chosen.gambit, Messages.EHistoryKind.Player);
        gambits.Clear();
        currentGambit = 0;
        Utils.PlayClip2d(makeSelection);
    }

    /// <summary>Height of one gambit block; uses measurement text (italic, prefix when mouse UI) so hover doesn’t shift layout.</summary>
    private static float MeasureGambitBlockHeight(GUIStyle textStyle, string measurementRichText)
    {
        textStyle.richText = true;
        textStyle.wordWrap = true;
        GUIContent content = new GUIContent(measurementRichText);
        float h = textStyle.CalcHeight(content, GambitPanelWidth);
        float minH = textStyle.lineHeight > 0f ? textStyle.lineHeight : textStyle.fontSize + 4f;
        return h < minH ? minH : h;
    }

    private static int ComputeGambitHoverIndex(
        float xLeft,
        float yTop,
        GUIStyle textStyle,
        List<ConversationOption> gambitList,
        bool mouseUi,
        Matrix4x4 guiMatrix)
    {
        if (!mouseUi || gambitList.Count == 0)
        {
            return -1;
        }

        Vector2 mouseGui = GuiInput.MousePositionGuiSpace;
        float y = yTop;
        for (int i = 0; i < gambitList.Count; ++i)
        {
            string measure = FormatGambitMeasurement(gambitList[i], i, true);
            float h = MeasureGambitBlockHeight(textStyle, measure);
            Rect hit = new Rect(xLeft, y, GambitPanelWidth, h);
            if (GuiInput.PointInGuiMatrixRect(guiMatrix, hit, mouseGui))
            {
                return i;
            }

            y += h + GambitLineGap;
        }

        return -1;
    }

    /// <summary>
    /// Draw each gambit as its own wrapped block; mouse UI adds "n. " prefix, hover uses same styling as gamepad selection.
    /// </summary>
    private static void DrawGambitOptions(
        float xLeft,
        float yTop,
        GUIStyle textStyle,
        List<ConversationOption> gambitList,
        int gamepadHighlightIndex,
        Matrix4x4 guiMatrix)
    {
        textStyle.richText = true;
        textStyle.wordWrap = true;
        textStyle.clipping = TextClipping.Overflow;

        bool mouseUi = GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard;
        int hoverIndex = ComputeGambitHoverIndex(xLeft, yTop, textStyle, gambitList, mouseUi, guiMatrix);

        float y = yTop;
        for (int i = 0; i < gambitList.Count; ++i)
        {
            bool highlighted = mouseUi
                ? hoverIndex == i
                : gamepadHighlightIndex == i;
            string measure = FormatGambitMeasurement(gambitList[i], i, mouseUi);
            string display = FormatGambitDisplay(gambitList[i], i, mouseUi, highlighted);
            float h = MeasureGambitBlockHeight(textStyle, measure);
            GUI.Label(UiScale.R(xLeft, y, GambitPanelWidth, h), new GUIContent(display), UiScale.Style(textStyle));
            y += h + GambitLineGap;
        }
    }

    /// <summary>Hit-test each gambit block (including wrapped lines) for mouse selection.</summary>
    private void TryMousePickGambit(float xLeft, float yTop, GUIStyle textStyle, List<ConversationOption> gambitList)
    {
        if (gambitList.Count == 0)
        {
            return;
        }

        bool mouseUi = GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard;
        float y = yTop;
        for (int i = 0; i < gambitList.Count; ++i)
        {
            string measure = FormatGambitMeasurement(gambitList[i], i, mouseUi);
            float h = MeasureGambitBlockHeight(textStyle, measure);
            Rect hit = new Rect(xLeft, y, GambitPanelWidth, h);
            if (GuiInput.TryConsumeClickInRect(UiScale.R(hit)))
            {
                SelectGambitAtIndex(i);
                break;
            }

            y += h + GambitLineGap;
        }
    }

    private void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Conversation;
        
        // center the conversation; it is laid out in reference pixels (UiScale)
        float xoff = PanelLeft;
        // the gambits are hit-tested through this, from screen to reference pixels
        Matrix4x4 referenceToScreen = Matrix4x4.Scale(new Vector3(UiScale.Factor, UiScale.Factor, 1f));

        if (runningConversation != null)
        {
            GuiInput.RegisterBlockingRect(GetConversationPanelGuiRect());

            GetConversationShortLayout(out float chatCompress, out float gapCompress, out float boxTrim, out float dividerDrop);
            const float chatPaperTop = 194f;
            float chatPaperHeight = 520f - chatCompress;
            float chatScrollRowH = chatPaperHeight / 5f;
            // the chat runs down to the line over the answers, wherever dividerDrop puts it
            float viewRectHeight = 500f - chatCompress + dividerDrop;
            const float optionsPanelBaseTop = 760f;
            float optionsTop = optionsPanelBaseTop - chatCompress - gapCompress - boxTrim;
            float optionsHeight = 160f - boxTrim;
            float gambitsY = optionsTop + 17f;

            DrawTexture(convTex[0], xoff, 10, 1.2f); // name box
            DrawTexture(convTex[0], xoff + 600 - RightShift, 10, 1.2f); // name box
            DrawTexture(convTex[2], xoff, 50, 1.0f); // portrait box
            DrawTexture(convTex[1], xoff + LeftTrayX, 50, 1.2f); // trade tray
            DrawTexture(convTex[1], xoff + RightTrayX, 50, 1.2f); // trade tray
            DrawTexture(convTex[2], xoff + 745 - RightShift, 50, 1.0f); // portrait box
            Critter npc = runningConversation.npc;
            Texture2D npcHeadTex = null;
            if (npc.whoami != EWhoAmI.None)
            {
                int whoAmI = (int) npc.whoami;
                if (whoAmI < newCharPortraits.entries.Length)
                {
                    npcHeadTex = newCharPortraits.entries[whoAmI];
                }
                if (npcHeadTex == null && whoAmI <= charHeadTex.Length)
                {
                    npcHeadTex = charHeadTex[whoAmI - 1];
                }
            }
            if (npcHeadTex == null)
            {
                if (npc.typeNum < newGenPortraits.Length)
                {
                    npcHeadTex = newGenPortraits[npc.typeNum];
                }
                if (npcHeadTex == null)
                {
                    npcHeadTex = genHeadsTex[npc.typeNum - 64];
                }
            }
            DrawTexture(npcHeadTex, xoff + 8, 57, 1.0f);
            int playerHeadIndex = (PlayerData.sData.female ? 5 : 0) + PlayerData.sData.portrait;
            Texture2D playerHeadTex = headsTex[playerHeadIndex];
            if (newPlayerPortraits[playerHeadIndex] != null)
            {
                playerHeadTex = newPlayerPortraits[playerHeadIndex];
            }
            DrawTexture(playerHeadTex, xoff + 753 - RightShift, 57, 1.0f);

            Texture2D paper = new Texture2D(1, 1);
            paper.SetPixels(new Color[] { paperColor });
            paper.Apply();
            Texture2D paperEdge = new Texture2D(1, 1);
            paperEdge.SetPixels(new Color[] { paperEdgeColor });
            paperEdge.Apply();
            UiScale.DrawTexture(UiScale.R(xoff, 190, PanelWidth, 4), paperEdge);
            UiScale.DrawTexture(UiScale.R(xoff, chatPaperTop, PanelWidth, chatPaperHeight), paper);
            UiScale.DrawTexture(UiScale.R(xoff, chatPaperTop + chatPaperHeight, PanelWidth, 4), paperEdge);
            for (int i = 0; i < 5; ++i)
            {
                float rowY = chatPaperTop + chatScrollRowH * i;
                Texture2D left = scrledgeTex[i];
                UiScale.DrawTexture(UiScale.R(xoff, rowY, 3 * left.width, chatScrollRowH), left);
                Texture2D right = scrledgeTex[5 + i];
                UiScale.DrawTexture(UiScale.R(PanelWidth + xoff - 12, rowY, 3 * right.width, chatScrollRowH), right);
            }

            GUIStyle textStyle = new GUIStyle
            {
                font = font,
                fontSize = 25,
                normal = { textColor = nameTextColor },
                wordWrap = true,
                clipping = TextClipping.Overflow
            };

            if (!waitForEnd && !waitForRelease && !waitAFrameAfterRelease)
            {
                // On a short screen the strip overlaps the foot of the chat's paper; its top edge, the line between
                // the two, comes down by dividerDrop and the answers stay where they are.
                float stripTop = optionsTop + dividerDrop;
                float stripHeight = optionsHeight - dividerDrop;
                UiScale.DrawTexture(UiScale.R(xoff, stripTop, PanelWidth, 4), paperEdge);
                UiScale.DrawTexture(UiScale.R(xoff, stripTop + 4f, PanelWidth, stripHeight), paper);
                UiScale.DrawTexture(UiScale.R(xoff, stripTop + 4f + stripHeight, PanelWidth, 4), paperEdge);
                for (int i = 0; i < 2; ++i)
                {
                    Texture2D left = scrledgeTex[10 + i];
                    UiScale.DrawTexture(UiScale.R(xoff, stripTop + 4f + stripHeight / 2 * i, 3 * left.width, stripHeight / 2), left);
                    Texture2D right = scrledgeTex[16 + i];
                    UiScale.DrawTexture(UiScale.R(PanelWidth + xoff - 12, stripTop + 4f + stripHeight / 2 * i, 3 * right.width, stripHeight / 2), right);
                }
            }

            textStyle.alignment = TextAnchor.MiddleLeft;
            // add this to see attitude changes ({(CritterObject.EAttitude) runningConversation.npc.attitude})
            // would be awesome to have portraits change with attitude... :)
            GUI.Label(UiScale.R(xoff + 20, 10, 3 * convTex[0].width - 40, 3.6f * convTex[0].height),
                $"{runningConversation.npcName}", UiScale.Style(textStyle));
            textStyle.alignment = TextAnchor.MiddleRight;
            GUI.Label(UiScale.R(xoff + 620 - RightShift, 10, 3 * convTex[0].width - 40, 3.6f * convTex[0].height), PlayerData.sData.playerName,
                UiScale.Style(textStyle));

            textStyle.normal.textColor = textColor;
            textStyle.padding.left = 10;

            string convText = Conversation.output.ToString();
            if (convText.EndsWith(Environment.NewLine))
            {
                convText = convText.Trim('\n', '\r');
            }

            // five step approach below is to avoid clipping the left side of italic characters
            // when the text overflows the rectangle
            // 1. Define your display area
            Rect viewRect = new Rect(xoff + 10, 200, GambitPanelWidth, viewRectHeight);

            // 2. Calculate how tall the text actually is
            GUIContent content = new GUIContent(convText);
            float textHeight = textStyle.CalcHeight(content, viewRect.width);

            // 3. Determine the Y-Offset
            // If text is short, (200 - 50) = 150. It starts 150px down (Bottom Aligned).
            // If text is long, (200 - 350) = -150. It starts 150px ABOVE the top (Clipped).
            float maxScroll = Mathf.Max(0f, textHeight - viewRect.height);
            chatMaxScroll = maxScroll;
            chatScrollOffset = Mathf.Clamp(chatScrollOffset, 0f, maxScroll);
            float yOffset = viewRect.height - textHeight + chatScrollOffset;

            if (Event.current.type == EventType.ScrollWheel
                && IsChatScrollAllowed()
                && maxScroll > 0f)
            {
                Rect chatViewRect = GetConversationChatViewRect();
                if (chatViewRect.Contains(Event.current.mousePosition))
                {
                    // The offset grows towards the older lines, and an IMGUI wheel event has a
                    // positive delta.y when the wheel turns down, towards the newer ones.
                    chatScrollOffset -= Event.current.delta.y * chatScrollWheelSpeed;
                    chatScrollOffset = Mathf.Clamp(chatScrollOffset, 0f, maxScroll);
                    Event.current.Use();
                }
            }

            // 4. Draw using a Group to ensure clean clipping
            GUI.BeginGroup(UiScale.R(viewRect));
                // Use UpperLeft alignment in your style for stability
                textStyle.alignment = TextAnchor.UpperLeft;
        
                // 5. Draw the label at our calculated Y position
                Rect labelRect = new Rect(0, yOffset, viewRect.width, textHeight);
                GUI.Label(UiScale.R(labelRect), content, UiScale.Style(textStyle));
            GUI.EndGroup();
            
            if (runningConversation.pauseTime <= 0.0f)
            {
                if (runningConversation.babl_get_input)
                {
                    // Show virtual keyboard if not already visible
                    if (KeyboardGUI.sKeyboard != null && !KeyboardGUI.sKeyboard.IsVisible())
                    {
                        float keyboardX = xoff + 20;
                        float keyboardY = optionsTop + 30f + 50f;
                        KeyboardGUI.sKeyboard.Show(runningConversation.babl_input, (result) => {
                            runningConversation.babl_input = result.Replace("'", "\u2019");
                            runningConversation.babl_get_input = false;
                            Conversation.LogToHistory(PlayerData.sData.playerName, runningConversation.babl_input, Messages.EHistoryKind.Player);
                            // find the input in the strings
                            Conversation.result = StringLoader.FindStringInBlock(runningConversation.stringBlock,
                                runningConversation.babl_input);
                            if (Conversation.result == 0)
                            {
                                // write the string to string 0
                                StringLoader.SetString(runningConversation.stringBlock, 0, runningConversation.babl_input);
                            }
                        }, null, new Vector2(UiScale.S(keyboardX), UiScale.S(keyboardY)), "Response:", false, false); // allowCancel = false (can't cancel or enter empty string)
                    }
                }
                else
                {
                    textStyle.alignment = TextAnchor.UpperLeft;
                    textStyle.clipping = TextClipping.Overflow;
                    DrawGambitOptions(xoff + 10f, gambitsY, textStyle, gambits, currentGambit, referenceToScreen);
                    TryMousePickGambit(xoff + 10f, gambitsY, textStyle, gambits);
                }
            }
        }

        if (Cheats.sCheats.showConversationHeads)
        {
            int p = 0;
            int headsPerLine = 12;
            for (int i = 1; i < (int)EWhoAmI.Count; ++i)
            {
                Texture2D tex = i < charHeadTex.Length ? charHeadTex[i-1] : null;
                if (!Cheats.sCheats.showOldHeads)
                {
                    if (i < newCharPortraits.entries.Length && newCharPortraits.entries[i] != null)
                    {
                        tex = newCharPortraits.entries[i];
                    }
                }
                if (tex != null)
                {
                    int x = 140 * (p % headsPerLine);
                    int y = 150 * (p / headsPerLine);
                    // draw square so that it's easier for Ben to resize
                    GUI.DrawTexture(new Rect(x, y, 3 * tex.width, 3 * tex.height), tex);
                    GUI.Label(new Rect(x, y + 3 * tex.height, 128, 20), $"{i}. {StringLoader.GetString(7, 17 + i - 1)}");
                    ++p;
                }
            }

            p = p - (p % headsPerLine) + headsPerLine;
            int[] hasPortrait =
            {
                0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 1, 1, 0, 0,
                0, 1, 0, 0, 0, 1, 1, 1, 0, 1, 1, 1, 0, 0, 1,
                1, 1, 1, 0, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 1,
                1, 0, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 1
            };
            for (int i = 0; i < genHeadsTex.Length; ++i)
            {
                Texture2D tex = (genHeadsTex[i] != null && hasPortrait[i] > 0) ? genHeadsTex[i] : null;
                if (!Cheats.sCheats.showOldHeads)
                {
                    if (newGenPortraits[i + 64] != null)
                    {
                        tex = newGenPortraits[i + 64];
                    }
                }
                if (tex != null)
                {
                    int x = 140 * (p % headsPerLine);
                    int y = 150 * (p / headsPerLine);
                    // draw square so that it's easier for Ben to resize
                    GUI.DrawTexture(new Rect(x, y, 3 * tex.width, 3 * tex.height), tex);
                    GUI.Label(new Rect(x, y + 3 * tex.height, 160, 20), $"{64+i}. {DataLoader.GetCleanedObjectName((EObjectType)(64 + i))}");
                    ++p;
                }
            }
        }
    }
}
