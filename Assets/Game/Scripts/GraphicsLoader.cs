using System.Collections.Generic;
using UnityEngine;

public class GraphicsLoader
{
    // [cycleStart, cycleEnd)
    public static Texture2D[] GetCyclingTextures(string relativePath, int texIndex, int cycleStart, int cycleLength, TextureWrapMode wrapMode = TextureWrapMode.Clamp)
    {
        Stream stream = new Stream(relativePath);
        byte isForcedSize = stream.GetByte();
        int count = stream.GetUShort();
        int[] offsets = stream.GetIntArray(count);

        stream.Seek(offsets[texIndex]);

        byte format = stream.GetByte();
        byte width = stream.GetByte();
        byte height = stream.GetByte();

        int thisAuxPalIndex = 0;
        byte[] auxPalIndices = null;
        byte[] indices = null;

        switch (format)
        {
        case 4:
            // raw
        {
            ushort size = stream.GetUShort();
            indices = stream.GetByteArray(size);
        }
            break;
        case 8:
            // 4-bit RLE
        {
            thisAuxPalIndex = stream.GetByte();
            ushort compressedSize = stream.GetUShort();
            auxPalIndices = ReadRLE(stream, width * height, compressedSize, 4);
        }
            break;
        case 10:
            // 4-bit palettized
        {
            thisAuxPalIndex = stream.GetByte();
            //ushort compressedSize =
            stream.GetUShort();
            auxPalIndices = stream.GetNybbleArray(width * height);
        }
            break;
        }

        if (auxPalIndices != null)
        {
            indices = new byte[width * height];
            for (int p = 0; p < auxPalIndices.Length; ++p)
            {
                indices[p] = DataLoader.sDataLoader.auxPals[16 * thisAuxPalIndex + auxPalIndices[p]];
            }
        }

        {
            byte[] flippedIndices = new byte[indices.Length];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    flippedIndices[(height - y - 1) * width + x] = indices[y * width + x];
                }
            }

            indices = flippedIndices;
        }

        Texture2D[] texs = new Texture2D[cycleLength];

        for (int i = 0; i < cycleLength; ++i)
        {
            texs[i] = new Texture2D(width, height);
            Color[] cols = texs[i].GetPixels();
            for (int p = 0; p < indices.Length; ++p)
            {
                int index = indices[p];
                if (index >= cycleStart && index < cycleStart + cycleLength)
                {
                    int newIndex = cycleStart + ((index + i) % cycleLength);
                    cols[p] = DataLoader.sDataLoader.palettes[0].colors[newIndex];
                }
                else
                {
                    cols[p] = DataLoader.sDataLoader.palettes[0].colors[index];
                    if (indices[p] == 0)
                    {
                        cols[p].a = 0.0f;
                    }
                }
            }

            texs[i].SetPixels(cols);
            texs[i].filterMode = FilterMode.Point;
            texs[i].wrapMode = TextureWrapMode.Clamp;
            texs[i].Apply();
        }

        return texs;
    }

    private static byte[] GetPaletteIndices(Stream stream, ref int width, ref int height)
    {
        byte[] indices = null;
        
        if (width > 0)
        {
            indices = new byte[width * height];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    byte index = stream.GetByte();
                    // always palette 0?
                    indices[width * (height - 1 - y) + x] = index;
                }
            }
        }
        else // !isForcedSize
        {
            // need to do some RLE decompression
            byte format = stream.GetByte();
            width = stream.GetByte();
            height = stream.GetByte();

            byte[] auxPalIndices = null;

            int thisAuxPalIndex = 0;

            switch (format)
            {
            case 4:
                // raw
                {
                    ushort size = stream.GetUShort();
                    indices = stream.GetByteArray(size);
                }
                break;
            case 6:
                // 5-bit RLE
                Debug.Log("5-bit RLE assumed unused!");
                break;
            case 8:
                // 4-bit RLE
                {
                    thisAuxPalIndex = stream.GetByte();
                    ushort compressedSize = stream.GetUShort();
                    auxPalIndices = ReadRLE(stream, width * height, compressedSize, 4);
                }
                break;
            case 10:
                // 4-bit palettized
                {
                    thisAuxPalIndex = stream.GetByte();
                    //ushort compressedSize =
                    stream.GetUShort();
                    auxPalIndices = stream.GetNybbleArray(width * height);
                }
                break;
            }

            if (auxPalIndices != null)
            {
                indices = new byte[width * height];
                for (int p = 0; p < auxPalIndices.Length; ++p)
                {
                    indices[p] = DataLoader.sDataLoader.auxPals[16 * thisAuxPalIndex + auxPalIndices[p]];
                }
            }

            if (indices != null)
            {
                byte[] flippedIndices = new byte[indices.Length];
                for (int y = 0; y < height; ++y)
                {
                    for (int x = 0; x < width; ++x)
                    {
                        flippedIndices[(height - y - 1) * width + x] = indices[y * width + x];
                    }
                }

                indices = flippedIndices;
            }
        }

        return indices;
    }
    
    public static Texture2D[] GetTextures(string relativePath, float zeroAlpha, TextureWrapMode wrapMode, int paletteIndex = 0, List<Vector2> explicitSizes = null, bool mips = true)
    {
        Stream stream = new Stream(relativePath);
        byte isForcedSize = stream.GetByte();
        int forcedWidth = 0;
        if (isForcedSize == 2)
        {
            forcedWidth = stream.GetByte();
        }

        int count = stream.GetUShort();
        Texture2D[] texs = new Texture2D[count];
        int[] offsets = stream.GetIntArray(count);

        for (int c = 0; c < count; ++c)
        {
            stream.Seek(offsets[c]);
            if (stream.End())
            {
                continue;
            }

            int width = forcedWidth;
            int height = forcedWidth;

            if (explicitSizes != null)
            {
                width = (byte)explicitSizes[c].x;
                height = (byte)explicitSizes[c].y;
            }

            byte[] indices = GetPaletteIndices(stream, ref width, ref height);
            
            if (indices != null)
            {
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, mips);

                Color[] colors = tex.GetPixels();
            
                for (int p = 0; p < indices.Length; ++p)
                {
                    Color col = DataLoader.sDataLoader.palettes[paletteIndex].colors[indices[p]];
                    if (indices[p] == 0)
                    {
                        col.a = zeroAlpha;
                    }

                    colors[p] = col;
                }
            
                tex.SetPixels(colors);
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = wrapMode;
                tex.Apply();

                texs[c] = tex;
            }
        }

        return texs;
    }

    public static List<List<Texture2D>> GetAllCyclingTextures(string relativePath, float zeroAlpha, TextureWrapMode wrapMode, int paletteIndex = 0, List<Vector2> explicitSizes = null)
    {
        Stream stream = new Stream(relativePath);
        byte isForcedSize = stream.GetByte();
        int forcedWidth = 0;
        if (isForcedSize == 2)
        {
            forcedWidth = stream.GetByte();
        }

        int count = stream.GetUShort();
        List<List<Texture2D>> texs = new();
        int[] offsets = stream.GetIntArray(count);

        for (int c = 0; c < count; ++c)
        {
            stream.Seek(offsets[c]);
            if (stream.End())
            {
                continue;
            }

            int width = forcedWidth;
            int height = forcedWidth;

            if (explicitSizes != null)
            {
                width = (byte)explicitSizes[c].x;
                height = (byte)explicitSizes[c].y;
            }

            byte[] indices = GetPaletteIndices(stream, ref width, ref height);

            // check for cycling colors
            int cycleStart = 0;
            int cycleLength = 1;
            bool cycleBackwards = false;
            int maxP = 16;
            foreach (byte p in indices)
            {
                // fire/lava loop
                if (p >= 16 && p < 24)
                {
                    cycleStart = 16;
                    maxP = Mathf.Max(p, maxP);
                    cycleLength = maxP + 1 - cycleStart;
                    cycleBackwards = true;
                }

                // water loop
                if (p >= 48 && p < 64)
                {
                    cycleStart = 48;
                    maxP = Mathf.Max(p, maxP);
                    cycleLength = maxP + 1 - cycleStart;
                }
            }

            texs.Add(new List<Texture2D>(cycleLength));
            for (int i = 0; i < cycleLength; ++i)
            {
                Texture2D tex = new Texture2D(width, height);

                Color[] cols = tex.GetPixels();
                for (int p = 0; p < indices.Length; ++p)
                {
                    int index = indices[p];
                    if (index >= cycleStart && index < cycleStart + cycleLength)
                    {
                        if (cycleBackwards)
                        {
                            // go backwards through the colors
                            index = cycleStart + cycleLength - 1 - ((index - cycleStart + cycleLength - i) % cycleLength);
                        }
                        else
                        {
                            index = cycleStart + ((index - cycleStart + i) % cycleLength);
                        }
                    }
                    cols[p] = DataLoader.sDataLoader.palettes[paletteIndex].colors[index];
                    if (indices[p] == 0)
                    {
                        cols[p].a = 0.0f;
                    }
                }

                tex.SetPixels(cols);
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = wrapMode;
                tex.Apply();

                texs[c].Add(tex);
            }
        }

        return texs;
    }

    static int ReadCode(Stream stream, int codeSize)
    {
        return stream.ReadUpperBits(codeSize);
    }

    static int ReadCode2(Stream stream, int codeSize)
    {
        int code1 = ReadCode(stream, codeSize);
        int code2 = ReadCode(stream, codeSize);
        // note: does not use codeSize as it should. error in RLE format.
        return (code1 << 4) | code2;
    }

    static int ReadCode3(Stream stream, int codeSize)
    {
        int code1 = ReadCode(stream, codeSize);
        int code2 = ReadCode(stream, codeSize);
        int code3 = ReadCode(stream, codeSize);
        // note: does not use codeSize as it should. error in RLE format.
        return (((code1 << 4) | code2) << 4) | code3;
    }

    static int ReadRLECount(Stream stream, int codeSize)
    {
        int value = ReadCode(stream, codeSize);
        if (value == 0)
        {
            value = ReadCode2(stream, codeSize);
            if (value == 0)
            {
                value = ReadCode3(stream, codeSize);
            }
        }

        return value;
    }

    public static byte[] ReadRLE(Stream stream, int uncompressedSize, int compressedSize, int codeSize)
    {
        bool state = true;
        byte[] auxPalIndices = new byte[uncompressedSize];
        int cur = 0;
        int streamStart = stream.GetPos();

        try
        {
            while (cur < uncompressedSize && stream.GetPos() - streamStart < compressedSize)
            {
                int count = ReadRLECount(stream, codeSize);
                if (state)
                {
                    if (count == 2)
                    {
                        int repeats = ReadRLECount(stream, codeSize);
                        while (repeats-- > 0)
                        {
                            count = ReadRLECount(stream, codeSize);
                            byte value = (byte)ReadCode(stream, codeSize);
                            for (int c = 0; c < count; ++c)
                            {
                                auxPalIndices[cur++] = value;
                            }
                        }
                    }
                    else if (count > 2)
                    {
                        byte value = (byte)ReadCode(stream, codeSize);
                        for (int c = 0; c < count; ++c)
                        {
                            auxPalIndices[cur++] = value;
                        }
                    }
                }
                else
                {
                    for (int c = 0; c < count; ++c)
                    {
                        auxPalIndices[cur++] = (byte)ReadCode(stream, codeSize);
                    }
                }

                state = !state;
            }
        }
        catch (System.Exception)
        {
            // this can happen, apparently. sometimes the texture is one pixel short
        }

        return auxPalIndices;
    }

    public static Texture2D ReadBYT(string relativePath, int palIndex = 0)
    {
        Stream stream = new Stream(relativePath);

        Color[] col = new Color[320 * 200];
        for (int y = 199; y >= 0; --y)
        {
            for (int x = 0; x < 320; ++x)
            {
                int c = 320 * y + x;
                int i = stream.GetByte();
                col[c] = DataLoader.sDataLoader.palettes[palIndex].colors[i];
            }
        }

        Texture2D tex = new Texture2D(320, 200, TextureFormat.RGB24, mipChain: false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.SetPixels(col);
        tex.Apply();

        return tex;
    }

    public static Texture2D[] ReadBYT(string relativePath, int palIndex, int firstCycle, int cycleLength)
    {
        Stream stream = new Stream(relativePath);

        Texture2D[] texs = new Texture2D[cycleLength];
        for (int t = 0; t < texs.Length; ++t)
        {
            stream.Seek(0);
            Color[] col = new Color[320 * 200];
            for (int y = 199; y >= 0; --y)
            {
                for (int x = 0; x < 320; ++x)
                {
                    int c = 320 * y + x;
                    int i = stream.GetByte();
                    if (i >= firstCycle && i < firstCycle + cycleLength)
                    {
                        i = firstCycle + ((i - firstCycle + cycleLength - t) % texs.Length);
                    }

                    col[c] = DataLoader.sDataLoader.palettes[palIndex].colors[i];
                }
            }

            Texture2D tex = new Texture2D(320, 200, TextureFormat.RGB24, mipChain: false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels(col);
            tex.Apply();

            texs[t] = tex;
        }

        return texs;
    }
}
