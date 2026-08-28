using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class CritterLoader : MonoBehaviour
{
    public class Critter
    {
        public int animSet;
        public int auxPal;

        public Frame[,] slots = new Frame[160, 8];
        public int[] frameCount = new int[160];
    }

    private Critter[] critters = new Critter[64];

    public class Frame
    {
        public byte hotspotX;
        public byte hotspotY;
        public Material mat;
    }

    public string[] animNames = new string[32];

    public List<Texture2D> allCritterTex = new List<Texture2D>();

    public static CritterLoader sCritterLoader;

    public static Critter GetCritter(EObjectType type)
    {
        return sCritterLoader.critters[(int)type - 0x40];
    }

    protected void Start()
    {
        sCritterLoader = this;
        DontDestroyOnLoad(gameObject);

        float beforeTime = Time.realtimeSinceStartup;

        string critPath = GameDataPath.GetCritPath();

        {
            Stream stream = new Stream(Path.Combine(critPath, "assoc.anm"));

            for (int i = 0; i < 32; ++i)
            {
                byte[] animName = stream.GetByteArray(8);
                animNames[i] = "";
                for (int c = 0; c < 8 && animName[c] != 0; ++c)
                {
                    animNames[i] += (char)animName[c];
                }
            }

            for (int i = 0; i < 64; ++i)
            {
                critters[i] = new Critter();
                critters[i].animSet = stream.GetByte();
                critters[i].auxPal = stream.GetByte();

                LoadCritterPage(critPath, critters[i], 0);
                LoadCritterPage(critPath, critters[i], 1);
            }
        }
        
        Debug.Log($"Time to load critters {Time.realtimeSinceStartup - beforeTime:F3}s.");
    }

    private void LoadCritterPage(string critPath, Critter crit, int pageIndex)
    {
        // octal! really!
        string octal = (crit.animSet / 8).ToString() + (crit.animSet & 7).ToString();
        Stream stream = new Stream(Path.Combine(critPath, $"cr{octal}page.n0{pageIndex}"));

        Shader shader = Shader.Find("Transparent/Diffuse");

        byte slotBase = stream.GetByte();
        byte numSlots = stream.GetByte();
        byte[] segmentIndices = stream.GetByteArray(numSlots);
        byte numSegments = stream.GetByte();
        List<byte[]> frameIndices = new List<byte[]>();
        for (int i = 0; i < numSegments; ++i)
        {
            frameIndices.Add(stream.GetByteArray(8));
        }

        byte numPals = stream.GetByte();
        List<byte[]> pals = new List<byte[]>();
        for (int i = 0; i < numPals; ++i)
        {
            pals.Add(stream.GetByteArray(32));
        }

        byte numFrames = stream.GetByte();
        stream.GetByte(); // skip over 06 (compression type?)
        ushort[] offsets = stream.GetUShortArray(numFrames);

        Frame[] allFrames = new Frame[numFrames];

        for (int i = 0; i < numFrames; ++i)
        {
            stream.Seek(offsets[i]);
            byte width = stream.GetByte();
            byte height = stream.GetByte();
            byte hotspotX = stream.GetByte();
            byte hotspotY = stream.GetByte();
            byte format = stream.GetByte();
            ushort dataLength = stream.GetUShort();
            byte[] auxPalIndices = null;
            switch (format)
            {
            case 6:
                auxPalIndices = GraphicsLoader.ReadRLE(stream, width * height, (5 * dataLength + 7) / 8, 5);
                break;
            case 8:
                auxPalIndices = GraphicsLoader.ReadRLE(stream, width * height, (4 * dataLength + 7) / 8, 4);
                break;
            }

            byte[] flippedIndices = new byte[auxPalIndices.Length];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    flippedIndices[(height - y - 1) * width + x] = auxPalIndices[y * width + x];
                }
            }

            auxPalIndices = flippedIndices;

            // pass them through the auxPal, then through the main palette
            Color[] colors = new Color[auxPalIndices.Length];
            for (int p = 0; p < colors.Length; ++p)
            {
                int index = pals[crit.auxPal][auxPalIndices[p]];
                Color col = DataLoader.sDataLoader.palettes[0].colors[index];
                if (index == 0)
                {
                    col.a = 0.0f;
                }

                colors[p] = col;
            }

            Texture2D tex = new Texture2D(width, height);
            tex.SetPixels(colors);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();

            Frame frame = new Frame();
            frame.hotspotX = hotspotX;
            frame.hotspotY = hotspotY;

            Material mat = new Material(shader);
            mat.mainTexture = tex;
            mat.SetFloat("_Glossiness", 0.2f);

            frame.mat = mat;

            allCritterTex.Add(tex);
            allFrames[i] = frame;
        }

        // assign the slots and segments.
        for (int slot = 0; slot < numSlots; ++slot)
        {
            int segmentIndex = segmentIndices[slot];
            if (segmentIndex != 255)
            {
                for (int frameIndex = 0; frameIndex < 8; ++frameIndex)
                {
                    int allFramesIndex = frameIndices[segmentIndex][frameIndex];
                    if (allFramesIndex != 255)
                    {
                        crit.slots[slotBase + slot, frameIndex] = allFrames[allFramesIndex];
                    }
                    else
                    {
                        crit.frameCount[slotBase + slot] = frameIndex;
                        break;
                    }
                }
            }
        }

#if false
        byte[] bytes = tex.EncodeToPNG();
        string directory = Application.dataPath + "/../Critters/" + animNames[charIndex] + "_" + pa.ToString();
        if ( !Directory.Exists( directory ) )
        {
           Directory.CreateDirectory( directory );
        }
        File.WriteAllBytes( string.Format( "{0}/Slot{1:00}_{2}.png", directory, slot + slotBase, frame ), bytes );
#endif
    }
}
