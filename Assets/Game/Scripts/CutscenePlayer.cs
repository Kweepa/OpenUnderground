using System;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

// TODO: something about the music. original just kept whatever was playing, playing :|

public struct LargePageHeader
{
    public int numPages;
    public int numRecords;
    public int width;
    public int height;
    public int numFrames;
}

public struct LargePageDescriptor
{
    public int firstRecord;
    public int numRecords;
    public int numBytes; // excluding header
}

public enum CutEventType
{
    Fade,
    Cut,
    Frame,
    Sound,
    Vol,
    Voc,
    Pause,
    Block,
    Sub,
    NewSub,
    Jump,
    Blur,
    PressA,
    End
}

public enum CutEventOption
{
    None,
    Loop,
    MatchVoc
}

public class CutEvent
{
    public static CutEvent FromString(string evt)
    {
        string[] bits = evt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (bits.Length >= 3)
        {
            float value = 0;
            string cutName = "";
            if (float.TryParse(bits[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float time))
            {
                if (CutEventType.TryParse(bits[1], true, out CutEventType type))
                {
                    bool gotValue;
                    if (type == CutEventType.Fade)
                    {
                        value = bits[2] == "out" ? 0.0f : 1.0f;
                        gotValue = true;
                    }
                    else if (type == CutEventType.Cut && bits[2].StartsWith("cs"))
                    {
                        cutName = bits[2];
                        gotValue = true;
                    }
                    else
                    {
                        gotValue = float.TryParse(bits[2], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
                    }

                    if (gotValue)
                    {
                        CutEvent e = new CutEvent();
                        e.time = time;
                        e.type = type;
                        e.value = value;
                        if (bits.Length > 3)
                        {
                            CutEventOption.TryParse(bits[3], true, out e.option);
                        }
                        e.cutName = cutName;
                        return e;
                    }
                }
            }
        }
        
        return null;
    }
    public float time;
    public CutEventType type;
    public float value;
    public CutEventOption option;
    public string cutName;
}

public enum CutsceneFitType
{
    Spill,
    Fill,
    Corner
}

public class CutscenePlayer : MonoBehaviour
{
    private List<CutEvent> events;
    private int eventIndex;

    [Tooltip("the NNN in csNNN")]
    public int sceneIndex;
    [Tooltip("how quickly to play back")]
    public float frameDuration = 0.1667f;
    [Tooltip("font for subtitles")]
    public Font font;
    [Tooltip("how to fit the cutscene to the play window")]
    public CutsceneFitType fitType = CutsceneFitType.Spill;
    [Tooltip("whether to play to the end or stop a couple of frames before")]
    public bool playAllFrames;
    [Tooltip("if frameNum event issued, just show this frame. mainly for windows and gravestones")]
    public int fixedFrame;

    /// <summary>Prompt glyph while waiting for advance (shown for gamepad).</summary>
    public Texture2D aButton;

    private Texture2D[] outputImages;
    
    private byte[] dstImage;

    private AudioClip audioClip;
    [Tooltip("component to use to play back dialog")]
    public AudioSource audioSource;
    [Tooltip("override dialog clips")]
    public AudioClip[] voiceClips;
    [Tooltip("subtitle overrides to match the dialog clips")]
    public string[] voiceSubs;

    [Tooltip("component to use to play back sound effects")]
    public AudioSource soundSource;
    [Tooltip("sound effects")]
    public AudioClip[] soundClips;
    
    [Tooltip("Audio clip to play during cup of wonder cutscene")]
    public AudioClip cupOfWonderClip;

    [Tooltip("exposed playback time (so you can jump ahead")]
    public float cutsceneTime;
    [Tooltip("exposed frame number (so you can select a frame to show")]
    public int frameNum;
    [Tooltip("show some debug info when playing back")]
    public bool debugShowFrames;

    public void Start()
    {
        events = new List<CutEvent>();
        string[] lines = File.ReadAllLines(Path.Combine(Application.streamingAssetsPath, $"Cutscenes/cs{sceneIndex:D3}.txt"));
        foreach (string line in lines)
        {
            CutEvent e = CutEvent.FromString(line);
            if (e != null)
            {
                events.Add(e);
            }
        }

        eventIndex = 0;

        PlayerObject.DisableControls(EControlMask.Cutscene, true);
        Time.timeScale = 0.0f;
        
        // Handle cup of wonder cutscenes
        if (sceneIndex is 13 or 14 or 15)
        {
            // Stop the music
            MusicPlayer musicPlayer = MusicPlayer.Instance;
            if (musicPlayer != null)
            {
                musicPlayer.StopMusic();
            }
            
            Utils.PlayClip2d(cupOfWonderClip);
        }
    }

    private float frameTime;

    private float fadeSpeed;
    private float fadeAlpha;
    private float pauseTime;
    private bool loopCut;
    private float matchVocTime;
    private bool stopLoop;
    private int stringBlock;
    private int subtitleIndex;
    private float subtitleTime;
    private bool blur;
    private bool subtitleOverride;
    private bool waitForA;
    
    public void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;
        deltaTime = Mathf.Min(deltaTime, 0.025f); // Clamp to 1/40th second max
        // pressing the button to select certain cutscenes (e.g., credits) soaked up all the fade in!
        if (cutsceneTime > 0.5f && ((GameInput.CurrentGamepad?.bButton.isPressed ?? false)
                                    || (GameInput.CurrentMouse?.rightButton.isPressed ?? false)))
        {
            deltaTime *= 10.0f;
        }

        if (waitForA)
        {
            if ((GameInput.CurrentGamepad?.aButton.wasReleasedThisFrame ?? false)
                || (GameInput.CurrentMouse?.leftButton.wasReleasedThisFrame ?? false)
                || GameInput.EscapePressedThisFrame())
            {
                waitForA = false;
            }
            else
            {
                deltaTime = 0.0f;
            }
        }

        while (eventIndex < events.Count && events[eventIndex].time <= cutsceneTime)
        {
            CutEvent e = events[eventIndex];
            switch (e.type)
            {
            case CutEventType.Cut:
                {
                    int cutValue = (int)e.value;
                    string cutName = e.cutName.StartsWith("cs") ? e.cutName : $"cs{sceneIndex:D3}.n{cutValue:D2}";
                    ReadCutsFile(cutName);
                    frameNum = 0;
                    frameTime = 0.0f;
                    pauseTime = 0.0f;
                    loopCut = e.option == CutEventOption.Loop;
                    stopLoop = false;
                    matchVocTime = 0.0f;
                    if (e.option == CutEventOption.MatchVoc)
                    {
                        matchVocTime = audioClip.length - 0.5f;
                    }
                }
                break;
            case CutEventType.Frame:
                frameNum = fixedFrame;
                break;
            case CutEventType.Vol:
                audioSource.volume = e.value;
                break;
            case CutEventType.Voc:
                {
                    int vocIndex = (int)e.value;
                    if (vocIndex < voiceClips.Length && voiceClips[vocIndex] != null)
                    {
                        audioClip = voiceClips[vocIndex];
                        audioSource.clip = audioClip;
                        #if false
                        audioSource.Play();
                        #endif
                    }
                    else
                    {
                        PlayVoc(vocIndex);
                    }
                }
                break;
            case CutEventType.Sound:
                soundSource.clip = soundClips[(int)e.value];
                soundSource.Play();
                break;
            case CutEventType.Fade:
                fadeSpeed = e.value == 1 ? -2.0f : 2.0f;
                fadeAlpha = e.value == 1 ? 1.0f : 0.0f;
                break;
            case CutEventType.Pause:
                pauseTime = e.value;
                break;
            case CutEventType.Block:
                stringBlock = (int)e.value;
                break;
            case CutEventType.Sub:
                subtitleIndex = (int)e.value;
                subtitleTime = 3.0f;
                if (e.option == CutEventOption.MatchVoc)
                {
                    subtitleTime = audioClip.length;
                }
                subtitleOverride = false;
                break;
            case CutEventType.NewSub:
                subtitleIndex = (int)e.value;
                subtitleTime = 3.0f;
                if (e.option == CutEventOption.MatchVoc)
                {
                    subtitleTime = audioClip.length;
                }
                subtitleOverride = true;
                break;
            case CutEventType.Jump:
                cutsceneTime += e.value;
                break;
            case CutEventType.Blur:
                blur = e.value > 0;
                break;
            case CutEventType.PressA:
                waitForA = true;
                break;
            case CutEventType.End:
                // Resume music for cup of wonder cutscene (sceneIndex 13)
                if (sceneIndex == 13)
                {
                    Music.ResumeExploringMusic();
                }
                
                Destroy(gameObject);
                Time.timeScale = 1.0f;
                PlayerObject.DisableControls(EControlMask.Cutscene, false);
                break;
            }

            ++eventIndex;
        }
        cutsceneTime += deltaTime;

        fadeAlpha += fadeSpeed * deltaTime;
        fadeAlpha = Math.Clamp(fadeAlpha, 0.0f, 1.0f);

        if (matchVocTime > 0.0f)
        {
            matchVocTime -= deltaTime;
            if (matchVocTime <= 0.0f)
            {
                matchVocTime = 0.0f;
                stopLoop = true;
            }
        }

        if (subtitleTime > 0.0f)
        {
            subtitleTime -= deltaTime;
            if (subtitleTime <= 0.0f)
            {
                subtitleTime = 0.0f;
            }
        }

        if (pauseTime > 0.0f)
        {
            pauseTime -= deltaTime;
        }
        else
        {
            frameTime += deltaTime;
            while (frameTime > frameDuration)
            {
                frameTime -= frameDuration;
                // note, cuts seem to have an extra couple of frames at the end, unless looped
                if (stopLoop)
                {
                    frameNum = outputImages.Length - 4;
                    stopLoop = false;
                }
                else
                {
                    if (loopCut)
                    {
                        ++frameNum;
                        frameNum %= outputImages.Length - 2;
                    }
                    else if (matchVocTime > 0.0f)
                    {
                        ++frameNum;
                        if (frameNum == outputImages.Length - 3)
                        {
                            frameNum = 1;
                        }
                    }
                    else
                    {
                        if (outputImages != null)
                        {
                            if (frameNum < outputImages.Length - (playAllFrames ? 1 : 3))
                            {
                                ++frameNum;
                            }
                        }
                        else
                        {
                            Debug.Log("No output images!");
                        }
                    }
                }
            }
        }
    }

    private void PlayVoc(int index)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(GameDataPath.GetSoundPath(), $"{index:D2}.voc"));
        string header = "";
        // check header
        for (int i = 0; i < 19; i++)
        {
            header += (char)data[i];
        }
        if (header == "Creative Voice File")
        {
            if (data[0x1a] == 1) // regular sound data
            {
                int dataSize = data[0x1b] + 256 * (data[0x1c] + 256 * data[0x1d]);
                int samplingRate = data[0x1e];
                int hertz = -1000000 / (samplingRate - 256);
                int packMethod = data[0x1f];
                int numSamples = dataSize - 2;
                float[] samples = new float[numSamples];
                for (int i = 0; i < numSamples; i++)
                {
                    samples[i] = data[0x20 + i] / 256.0f - 0.5f;
                }
                audioClip = AudioClip.Create($"V{index:D2}", dataSize, 1, hertz, false);
                audioClip.SetData(samples, 0);

                audioSource.clip = audioClip;
                audioSource.Play();
            }
            else
            {
                // looks like the audio files in UU1 are all very simple, so no need to handle other block types
                Debug.Log($"Block type {data[0x1a]} not handled");
            }
        }
        else
        {
            Debug.Log("Invalid File Header " + index);
        }
    }

    void DecodeFrame(int inPtr, byte[] srcData)
    {
        // from an implementation by Underworld Adventures (hacking tools)
        int outPtr = 0;

        while (inPtr < srcData.Length)
        {
            int count = srcData[inPtr++];

            if (count == 0)
            {
                // run
                int wordCount = srcData[inPtr++];
                byte pixel = srcData[inPtr++];
                Array.Fill(dstImage, pixel, outPtr, wordCount);
                outPtr += wordCount;
            }
            else if (count < 128)
            {
                // dump
                Array.Copy(srcData, inPtr, dstImage, outPtr, count);
                inPtr += count;
                outPtr += count;
            }
            else
            {
                count &= 127;
                if (count != 0)
                {
                    // shortSkip
                    outPtr += count;
                }
                else
                {
                    // longOp
                    int wordCount = srcData[inPtr++];
                    wordCount += 256 * srcData[inPtr++];

                    if (wordCount == 0)
                    {
                        break;
                    }
                    if (wordCount >= 0x8000)
                    {
                        wordCount &= 0x7fff; // Remove sign bit.
                        if (wordCount >= 0x4000)
                        {
                            // longRun
                            wordCount &= 0x3fff; // Clear "longRun" bit
                            byte pixel = srcData[inPtr++];
                            Array.Fill(dstImage, pixel, outPtr, wordCount);
                            outPtr += wordCount;
                        }
                        else
                        {
                            // longDump
                            Array.Copy(srcData, inPtr, dstImage, outPtr, wordCount);
                            inPtr += wordCount;
                            outPtr += wordCount;
                        }
                    }
                    else
                    {
                        // longSkip
                        outPtr += wordCount;
                    }
                }
            }
        }
    }

    private int getShortAtAddress(byte[] data, long offset)
    {
        return data[offset] + 256 * data[offset + 1];
    }

    private int getIntAtAddress(byte[] data, long offset)
    {
        return data[offset] + 256 * (data[offset + 1] + 256 * (data[offset + 2] + 256 * data[offset + 3]));
    }

    private void ReadCutsFile(string cutName)
    {
        string fullPath = Path.Combine(GameDataPath.GetCutsPath(), cutName); 
        byte[] cutsFile =  File.ReadAllBytes(fullPath);

        long addptr = 0;
        Palette pal = new Palette();

        int lpf = 'L' + 256 * ('P' + 256 * ('F' + 256 * ' '));
        if (getIntAtAddress(cutsFile, 0) != lpf)
        {
            Debug.Log($"File id mismatch in {cutName}");
        }
        int anim = 'A' + 256 * ('N' + 256 * ('I' + 256 * 'M'));
        if (getIntAtAddress(cutsFile, 0x10) != anim)
        {
            Debug.Log($"Content type mismatch in {cutName}");
        }

        LargePageHeader head;
        head.numPages = getShortAtAddress(cutsFile, 0x6);
        head.numRecords = getIntAtAddress(cutsFile, 0x8);
        head.width = getShortAtAddress(cutsFile, 0x14);
        head.height = getShortAtAddress(cutsFile, 0x16);
        head.numFrames = getShortAtAddress(cutsFile, 0x40);
        addptr += 256; // past header and colour cycling

        outputImages = new Texture2D[head.numFrames];

        // Init the buffer
        dstImage = new byte[head.width * head.height];

        // read the palette
        for (int i = 0; i < 256; ++i)
        {
            pal.colors[i].b = cutsFile[addptr++] / 255.0f;
            pal.colors[i].g = cutsFile[addptr++] / 255.0f;
            pal.colors[i].r = cutsFile[addptr++] / 255.0f;
            pal.colors[i].a = 1.0f;
            ++addptr; // skip alpha?
        }

        // read the large page descriptors
        LargePageDescriptor[] desc = new LargePageDescriptor[head.numPages];
        for (int i = 0; i < head.numPages; ++i)
        {
            desc[i].firstRecord = getShortAtAddress(cutsFile, addptr);
            desc[i].numRecords = getShortAtAddress(cutsFile, addptr + 2);
            desc[i].numBytes = getShortAtAddress(cutsFile, addptr + 4);
            addptr += 6;
        }

        for (int f = 0; f < head.numFrames; ++f)
        {
            int i = 0;
            for (; i < head.numPages; i++)
            {
                if (desc[i].firstRecord <= f && f < desc[i].firstRecord + desc[i].numRecords)
                {
                    break;
                }
            }

            // 2816 == (1 + 4 + 6) * 256
            // (header + palette + lp descs)
            addptr = 2816 + 0x10000 * i;
            long curlp = addptr;
            LargePageDescriptor curl;
            curl.firstRecord = getShortAtAddress(cutsFile, curlp + 0);
            curl.numRecords = getShortAtAddress(cutsFile, curlp + 2);
            curl.numBytes = getShortAtAddress(cutsFile, curlp + 4);
            long thepage = curlp + 8;
            int destframe = f - curl.firstRecord;

            int offset = 0;
            long pagepointer = thepage;
            for (int k = 0; k < destframe; k++)
            {
                offset += getShortAtAddress(cutsFile, pagepointer + 2 * k);
            }

            long ppointer = thepage + 2 * curl.numRecords + offset + 4;
            
            // updates to dstImage
            // note that frames can contain just the parts that change
            DecodeFrame((int)ppointer, cutsFile);

            Texture2D tex = new Texture2D(head.width, head.height);
            Color[] colors = new Color[head.width * head.height];
            int dp = 0;
            for (int y = 0; y < head.height; ++y)
            {
                int cp = head.width * (head.height - y - 1);
                for (int x = 0; x < head.width; ++x, ++dp, ++cp)
                {
                    // flip right side up
                    colors[cp] = pal.colors[dstImage[dp]];
                }
            }
            tex.SetPixels(colors);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            
            outputImages[f] = tex;

            if (fitType == CutsceneFitType.Corner)
            {
                // carefully positioned to trim off the dragons and compass
                // note that the x,y here are from the bottom left
                Color[] subColors = tex.GetPixels(150, 3, 168, 109);
                Texture2D subTex = new Texture2D(168, 109);
                subTex.SetPixels(subColors);
                subTex.wrapMode = TextureWrapMode.Clamp;
                subTex.filterMode = FilterMode.Point;
                subTex.Apply();

                outputImages[f] = subTex;
            }
        }
    }
    
    private static string[][] replacements =
    {
        new [] { "each attempt to sleep", "each attempt to rest" },
        new [] { "a great evil upon us", "a great evil" },
        new [] { "visitor from far", "visitor, and from far" },
        new [] { "I\u2019d suspect my brother sent thee, were he alive.", "Were he not dead, I\u2019d suspect my brother sent thee." },
        new [] { "Still, thou shalt", "No matter, thou shalt serve to" },
        new [] { "_._._._", "... " },
        new [] { "_._._.", "..." },
        new [] { "._._._", "..." },
        new [] { "piercing blue", "hard grey" },
        new [] { "^", "" },
        new [] { "comings", "coming" },
        new [] { "survive the Abyss", "survive the Stygian Abyss" },
        new [] { "thy lies", "then thy lies" },
        new [] { "until Arial\u2019s voice do I hear", "\u2019til I hear Arial\u2019s voice" },
        new [] { "deja", "d\u00e9j\u00e0" },
        new [] { "\u2019\u2019\u2019", "\u2019\u201d"},
        new [] { "``", "\u201c" },
        new [] { "\u2019\u2019", "\u201d" },
        new [] { "`", "\u2018" },
        new [] { "saved our land!", "saved our land!\u201d" },
    };

    public void OnGUI()
    {
        if (outputImages == null) return;

        // on top of the compass
        GUI.depth = (int)EGUIDepth.Cutscene;

        GUIStyle style = new GUIStyle();
        style.font = font;
        style.normal.textColor = Color.white;
        style.fontSize = 40;
        style.alignment = TextAnchor.UpperCenter;
        style.wordWrap = true;

        int y = 0;
        int w = Screen.width;
        int h = Screen.height;
        if (fitType == CutsceneFitType.Spill)
        {
            h = 54 * w / 80;
        }
        else if (fitType == CutsceneFitType.Corner)
        {
            h = 54 * w / 80;
            y = Screen.height - h;
        }

        // display
        {
            Rect rect = new Rect(0, y, w, h);

            Texture2D t = outputImages[frameNum];
            GUI.DrawTexture(rect, t);
            if (blur)
            {
                GUI.color = new Color(1.0f, 1.0f, 1.0f, 0.25f);
                float onePixelX = Screen.width / (float) t.width;
                float onePixelY = 1.2f * onePixelX;
                rect.x = -onePixelX;
                GUI.DrawTexture(rect, t);
                rect.x = onePixelX;
                GUI.DrawTexture(rect, t);
                rect.x = 0.0f;
                rect.y -= onePixelY;
                GUI.DrawTexture(rect, t);
                rect.y += 2.0f * onePixelY;
                GUI.DrawTexture(rect, t);
                GUI.color = Color.white;
            }
        }

        // fade in/out
        Utils.DrawFade(fadeAlpha);
        
        // subtitles
        if (subtitleTime > 0.0f)
        {
            string subtitle;
            if (subtitleOverride)
            {
                subtitle = StringLoader.FixUpQuotesAndErrors(voiceSubs[subtitleIndex]);
            }
            else
            {
                subtitle = StringLoader.GetString(stringBlock, subtitleIndex);

                for (int i = 0; i < replacements.Length; ++i)
                {
                    subtitle = subtitle.Replace(replacements[i][0], replacements[i][1]);
                }
            }

            float xBorder = 0.1f * Screen.width;
            float yBorder = 0.05f * Screen.height;
            GUI.Label(new Rect(xBorder, Screen.height - yBorder - 2 * style.fontSize, Screen.width - 2 * xBorder, 3 * style.fontSize), subtitle, style);
        }

        if (waitForA && aButton != null && GameInput.LastActiveDevice == GameInputDevice.Gamepad)
        {
            float aw = 48;
            float ah = 48;
            float yBorder = 0.05f * Screen.height;
            GUI.DrawTexture(new Rect(Screen.width / 2 - aw / 2, Screen.height - yBorder - ah, aw, ah), aButton);
        }

        if (debugShowFrames)
        {
            for (int i = 0; i < outputImages.Length - 1; ++i)
            {
                GUI.DrawTexture(new Rect(340 * (i % 6), 220 * (i / 6), 320, 200), outputImages[i]);
            }
        }
    }
}
