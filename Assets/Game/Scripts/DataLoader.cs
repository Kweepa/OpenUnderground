using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class Palette
{
    public Color[] colors = new Color[256];
}

public class DataLoader : MonoBehaviour
{
    public static DataLoader sDataLoader;

    public AudioClip impact;
    public AudioClip splash;
    public AudioClip lavaSplash;
    public AudioClip lavaBurn;
    public GameObject waterSplashParticle;
    public GameObject lavaSplashParticle;

    public List<List<Texture2D>> wallTex;
    public List<List<Texture2D>> floorTex;
    public Texture2D[] objTex;
    public Texture2D[] cursorTex;
    public Texture2D[] mapCursorTex;
    public Texture2D[] tmFlatTex;
    public Texture2D[] tmObjTex;
    public Texture2D[] doorTex;

    public Texture2D[] panelsTex; // some invalid type, 0x0 and 1x1 resolution images
    public Texture2D[] eyesTex; // eyes from top screen
    public Texture2D[] flasksTex; // health and mana flask graphics
    public Texture2D[] armor_fTex; // female paperdoll armor graphics
    public Texture2D[] armor_mTex; // male paperdoll armor graphics
    public Texture2D[] bodiesTex; // paperdoll bodies
    public Texture2D[] compassTex; // compass graphics
    public Texture2D[] opbtnTex; // opening screen buttons, create new game, etc.; palette #2
    public Texture2D[] spellsTex; // spells graphics

    public Texture2D[] animoTex; // small animations (includes blood splats and sparks) 
    public Texture2D[] invTex; // inventory graphics, scroll backgrounds(?)

    public Texture2D[] _3dwinTex; // 3d window graphics
    public Texture2D[] converseTex; // conversation screen bitmaps
    public Texture2D[] dragonsTex; // scroll dragons animations
    public Texture2D[] chainsTex; // rotating chains for the stats window
    public Texture2D[] weaponsTex; // weapon hit animations, for left and right handedness
    public Texture2D[] lftiTex; // (left interaction?) game action button graphics

    public Texture2D[] litTorchTex;
    public Texture2D[] litCandleTex;
    public Texture2D[] litTaperTex;

    public Texture2D[] lightSpellTex;
    
    public Texture2D[] buttonsTex;
    public Texture2D[] viewsTex;
    public Texture2D[] leftITex;
    public Texture2D[] optButtonsTex;
    public Texture2D[] allOptButtonsTex;

    public ComObjProps[] comObjProps;

    public List<ObjectCombination> objCombinations;

    public ObjectsData objectsData;

    public Palette[] palettes = new Palette[8];
    public byte[] auxPals;

    public Stream graveData;

    public static string GetCleanedObjectName(EObjectType type)
    {
        string cleanedName = StringLoader.GetString(4, (int)type);
        int ampIndex = cleanedName.IndexOf('&');
        if (ampIndex > 0)
        {
            cleanedName = cleanedName.Substring(0, ampIndex);
        }
        cleanedName = cleanedName.Replace('_', ' ');
        if (cleanedName == "")
        {
            cleanedName = type.ToString();
        }
        return cleanedName;
    }

    public static string GetArticle(EObjectType type)
    {
        string name = StringLoader.GetString(4, (int)type);
        int scoreIndex = name.IndexOf('_');
        if (scoreIndex > 0)
        {
            return name.Substring(0, scoreIndex);
        }
        return "";
    }

    public static string GetPlural(int type)
    {
        string name = StringLoader.GetString(4, type);
        int ampIndex = name.IndexOf('&');
        if (ampIndex > 0)
        {
            return name.Substring(ampIndex + 1);
        }
        int scoreIndex = name.IndexOf('_') + 1;
        return name.Substring(scoreIndex, ampIndex - scoreIndex) + "s";
    }

    protected void Start()
    {
        sDataLoader = this;
        DontDestroyOnLoad(gameObject);

        float startT = Time.realtimeSinceStartup;

        auxPals = File.ReadAllBytes(Path.Combine(GameDataPath.GetDataPath(), "allpals.dat"));

        // load palettes
        Stream palStream = new Stream("../Data/pals.dat");
        for (int p = 0; p < 8; ++p)
        {
            palettes[p] = new Palette();
            for (int c = 0; c < 256; ++c)
            {
                Color col;
                float power = 1.1f;
                col.r = Mathf.Pow(palStream.GetByte() / 63.0f, power);
                col.g = Mathf.Pow(palStream.GetByte() / 63.0f, power);
                col.b = Mathf.Pow(palStream.GetByte() / 63.0f, power);
                col.a = 1.0f;
                palettes[p].colors[c] = col;
            }
        }

        // now read in textures

        wallTex = GraphicsLoader.GetAllCyclingTextures("../Data/w64.tr", zeroAlpha: 1.0f, wrapMode: TextureWrapMode.Repeat);
        floorTex = GraphicsLoader.GetAllCyclingTextures("../Data/f32.tr", zeroAlpha: 1.0f, wrapMode: TextureWrapMode.Repeat);
        objTex = GraphicsLoader.GetTextures("../Data/objects.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp, mips: false);
        compassTex = GraphicsLoader.GetTextures("../Data/compass.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        cursorTex = GraphicsLoader.GetTextures("../Data/cursors.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        mapCursorTex = GraphicsLoader.GetTextures("../Data/cursors.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp, paletteIndex: 1);
        tmFlatTex = GraphicsLoader.GetTextures("../Data/tmflat.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        tmObjTex = GraphicsLoader.GetTextures("../Data/tmobj.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        doorTex = GraphicsLoader.GetTextures("../Data/doors.gr", zeroAlpha: 1.0f, wrapMode: TextureWrapMode.Clamp);
        TextureOverrideLoader.Apply(wallTex, floorTex, doorTex);
        animoTex = GraphicsLoader.GetTextures("../Data/animo.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        List<Vector2> panelsSizes = new List<Vector2> { new Vector2(83, 114), new Vector2(83, 114), new Vector2(83, 114), new Vector2(6, 60) };
        panelsTex = GraphicsLoader.GetTextures("../Data/panels.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp, explicitSizes: panelsSizes);
        flasksTex = GraphicsLoader.GetTextures("../Data/flasks.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        bodiesTex = GraphicsLoader.GetTextures("../Data/bodies.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        armor_fTex = GraphicsLoader.GetTextures("../Data/armor_f.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        armor_mTex = GraphicsLoader.GetTextures("../Data/armor_m.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);

        // make the plate gauntlets blue
        Utils.RetintImages(armor_fTex, new[] { 8, 23, 38, 53 }, new Color(1.0f, 1.0f, 1.2f));
        Utils.RetintImages(armor_mTex, new[] { 8, 23, 38, 53 }, new Color(1.0f, 1.0f, 1.2f));

        // dither the chain gauntlets
        Utils.DitherImages(armor_fTex, new[] { 7, 22, 37, 52 }, 0.2f);
        Utils.DitherImages(armor_mTex, new[] { 7, 22, 37, 52 }, 0.2f);

        spellsTex = GraphicsLoader.GetTextures("../Data/spells.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        _3dwinTex = GraphicsLoader.GetTextures("../Data/3dwin.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        dragonsTex = GraphicsLoader.GetTextures("../Data/dragons.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        eyesTex = GraphicsLoader.GetTextures("../Data/eyes.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        chainsTex = GraphicsLoader.GetTextures("../Data/chains.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        invTex = GraphicsLoader.GetTextures("../Data/inv.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        lftiTex = GraphicsLoader.GetTextures("../Data/lfti.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
        opbtnTex = GraphicsLoader.GetTextures("../Data/opbtn.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp, paletteIndex: 2);

#if true
      buttonsTex = GraphicsLoader.GetTextures("../Data/buttons.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
      viewsTex = GraphicsLoader.GetTextures("../Data/views.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp);
      leftITex = GraphicsLoader.GetTextures( "../Data/lfti.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp );
      optButtonsTex = GraphicsLoader.GetTextures( "../Data/optb.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp );
      allOptButtonsTex = GraphicsLoader.GetTextures( "../Data/optbtns.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp );
#endif

        comObjProps = ComObjProps.GetAllProps("../Data/comobj.dat");
        objCombinations = CombineObjects.GetAllCombinations("../Data/cmb.dat");

        objectsData = new ObjectsData("../Data/objects.dat");

        litTorchTex = GraphicsLoader.GetCyclingTextures("../Data/objects.gr", 149, 16, 8);
        litCandleTex = GraphicsLoader.GetCyclingTextures("../Data/objects.gr", 150, 16, 8);
        litTaperTex = GraphicsLoader.GetCyclingTextures("../Data/objects.gr", 151, 16, 8);

        lightSpellTex = GraphicsLoader.GetCyclingTextures("../Data/spells.gr", 17, 16, 8);

        graveData = new Stream("../Data/grave.dat");

        UUTerrain.Load();

        Debug.Log($"Time to load data: {Time.realtimeSinceStartup - startT:F3}s.");

        //WriteOutObjectPNGs();
    }

    void WriteOutObjectPNGs()
    {
        for (int i = 0; i < objTex.Length; ++i)
        {
            if (objTex[i] != null)
            {
                byte[] bytes = objTex[i].EncodeToPNG();
                string cleanedName = GetCleanedObjectName((EObjectType)i);
                File.WriteAllBytes(Application.dataPath + "/../ObjectPNGs/" + i.ToString("0000") + " " + cleanedName + ".png", bytes);
            }
        }
    }
}
