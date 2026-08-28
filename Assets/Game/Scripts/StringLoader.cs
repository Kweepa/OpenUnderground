using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Note, this is using the Avatar font which has extra Latin and punctuation
// However, I modified the font with glyphr studio to add some space after a capital L

public class StringLoader
{
    private static Dictionary<int, List<string>> Blocks = new();

    public static string GetString(int block, int index)
    {
        if (Blocks.TryGetValue(block, out List<string> values))
        {
            if (index < values.Count)
            {
                return values[index];
            }

            return "Index out of range";
        }
        return "Unknown block";
    }

    // this is only called from the conversation system so it doesn't need to fix up any strings
    public static void SetString(int block, int index, string value)
    {
        if (Blocks.TryGetValue(block, out List<string> values))
        {
            if (index < values.Count)
            {
                values[index] = value;
            }
        }
    }

    public static int FindStringInBlock(int block, string s)
    {
        if (Blocks.TryGetValue(block, out List<string> values))
        {
            for (int i = 0; i < values.Count; ++i)
            {
                string t = s.Trim();
                if (String.Compare(t, values[i], StringComparison.CurrentCultureIgnoreCase) == 0)
                {
                    return i;
                }
            }
        }
        return 0;
    }

    class HuffNode
    {
        public char chr;
        public HuffNode left;
        public HuffNode right;
    }

    static string DecompressString(Stream stream, HuffNode root)
    {
        StringBuilder sb = new StringBuilder(256);
        HuffNode h = root;
        while (true)
        {
            
            if (h.left != null)
            {
                h = stream.ReadUpperBit() == 0 ? h.left : h.right;
            }
            else
            {
                if (h.chr == '|')
                {
                    break;
                }

                sb.Append(h.chr);
                h = root;
            }
        }

        return sb.ToString();
    }

    private static readonly string[][] replacements =
    {
        new [] { "'till ", "'til " },
        new [] { "'the ", "\u2018the " },
        new [] { "'Folanae", "\u2018Folanae" },
        new [] { "'E'", "\u2018E'" },
        new [] { "'W'", "\u2018W'" },
        new [] { " -- ", "\u2014" },
        new [] { " - ", "\u2014" },
        new [] { "\\m", "" },
        new [] { "  ", " " },
        new [] { "\\p", "\n" },
        new [] { "`Tis", "'Tis" },
        new [] { " tis ", " 'tis " },
        new [] { "partake from", "partake of" },
        new [] { "Volcanos:", "Volcanoes:" },
        new [] { "splahes", "splashes" },
        new [] { "Enscribed", "Inscribed" },
        new [] { " . . . . .", "..." },
        new [] { " . . .", "..." },
        new [] { ". . .", "..." },
        new [] { " ...", "..." },
        new [] { "twelveth", "twelfth" }, // twelveth is an old fashioned 
        new [] { "tommorrow", "tomorrow" },
        new [] { "to lead a chamber", "to lead to a chamber" },
        new [] { "servicable", "serviceable" },
        new [] { "by using fountain", "by drinking from the fountain" },
        new [] { "Constuction", "Construction" },
        new [] { "Invisibilty", "Invisibility" },
        new [] { "Abyss' ", "Abyss's "},
        new [] { "Cabirus' ", "Cabirus's " },
        new [] { "the Book of Honesty.\n", "the Book of Honesty" },
        new [] { "the Taper of Sacrifice.\n", "the Taper of Sacrifice" },
        new [] { "the Wine of Compassion.\n", "the Wine of Compassion" },
        new [] { "the Standard of Honor.\n", "the Standard of Honor" },
        new [] { "the Shield of Valor.\n", "the Shield of Valor" },
        new [] { "the Cup of Wonder.\n", "the Cup of Wonder" },
        new [] { "the Sword of Justice.\n", "the Sword of Justice" },
        new [] { "the Ring of Humility.\n", "the Ring of Humility" },
    };

    // Cache this at the class level to reuse the memory buffer
    private static readonly StringBuilder fixupStringBuilder = new StringBuilder(1024);

    public static string FixUpQuotesAndErrors(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return s;
        }

        fixupStringBuilder.Clear();
        fixupStringBuilder.Append(s);

        // 1. Perform replacements directly in the buffer
        // This modifies the existing buffer instead of creating new strings
        foreach (var replacement in replacements)
        {
            fixupStringBuilder.Replace(replacement[0], replacement[1]);
        }

        // 2. Iterate the StringBuilder directly
        bool quotesOpened = false;
        for (int i = 0; i < fixupStringBuilder.Length; ++i)
        {
            char c = fixupStringBuilder[i];
            if (c == '\"')
            {
                fixupStringBuilder[i] = quotesOpened ? '\u201d' : '\u201c';
                quotesOpened = !quotesOpened;
            }
            else if (c == '\'')
            {
                fixupStringBuilder[i] = '\u2019';
            }
        }

        // This is the ONLY allocation: the final result for the UI
        return fixupStringBuilder.ToString();
    }
    
    static List<string> GetStringBlock(Stream stream, HuffNode root)
    {
        int numStrings = stream.GetUShort();
        ushort[] offsets = stream.GetUShortArray(numStrings);
        List<string> strings = new List<string>();
        int i = stream.GetPos();
        for (int c = 0; c < numStrings; ++c)
        {
            stream.Seek(i + offsets[c]);
            string s = DecompressString(stream, root);
            strings.Add(FixUpQuotesAndErrors(s));
        }

        return strings;
    }

    public static void LoadStrings()
    {
        if (Blocks.Count > 0)
        {
            Magic.BindSpellNamesFromStrings();
            return;
        }
        float startT = Time.realtimeSinceStartup;
      
        Stream stream = new Stream("../data/strings.pak");

        int numNodes = stream.GetUShort();
        uint[] nodeData = stream.GetUIntArray(numNodes);

        HuffNode[] nodes = new HuffNode[numNodes];
        for (int c = 0; c < numNodes; ++c)
        {
            nodes[c] = new HuffNode();
        }

        for (int c = 0; c < numNodes; ++c)
        {
            nodes[c].chr = (char)(nodeData[c] & 255);
            uint parent = (nodeData[c] >> 8) & 255;
            uint l = (nodeData[c] >> 16) & 255;
            nodes[c].left = l < numNodes ? nodes[l] : null;
            uint r = (nodeData[c] >> 24) & 255;
            nodes[c].right = r < numNodes ? nodes[r] : null;
        }

        uint numBlocks = stream.GetUShort();
        for (int c = 0; c < numBlocks; ++c)
        {
            int blockId = stream.GetUShort();
            int blockOffset = stream.GetInt();
            int savedPos = stream.GetPos();
            stream.Seek(blockOffset);
            List<string> block = GetStringBlock(stream, nodes[^1]);
            Blocks[blockId] = block;
            stream.Seek(savedPos);

#if false
         for (int q = 0; q < block.Count; ++q)
         {
             if (block[q].ToLower().Contains("sleep"))
             {
                 Debug.Log($"{c}/{q}: {block[q]}");
             }
         }
#endif

#if false
         if (c == 15)
         {
             string x = "";
             for (int q = 0; q < block.Count; ++q)
             {
                 x += block[q] + "\n";
             }
             Debug.Log(x);
         }
#endif
        }

        Debug.Log($"Time to load strings: {Time.realtimeSinceStartup - startT:F3}s.");

        Magic.BindSpellNamesFromStrings();

#if false
        string totes = "";
        for (uint c = 0; c < 32768; ++c)
        {
            List<string> strings;
            if (Blocks.TryGetValue(c, out strings))
            {
                for (int q = 0; q < strings.Count; ++q)
                {
                    if (strings[q] != null)
                    {
                        totes += $"[{c}][{q}] {strings[q]}\n";
                    }
                }
            }
        }
        System.IO.File.WriteAllText("allstrings.txt", totes);
#endif
    }
}
