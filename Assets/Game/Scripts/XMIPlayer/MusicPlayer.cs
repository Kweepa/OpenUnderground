using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System;

[RequireComponent(typeof(AudioSource))]
public partial class MusicPlayer : MonoBehaviour
{
    private string soundFontFileName = "Soundfonts/MT32.sf2";

    /// <summary>The soundfont the synthesizer was built with, so that a change of volume alone does not rebuild it.</summary>
    public string SoundFontFileName => soundFontFileName;

    private float masterVolumeScale = 0.5f;
    /// <summary>Multiplies output after user volume (1 = normal, 0.5 = half for cutscenes, etc.).</summary>
    private float duckMultiplier = 1f;
    
    [Header("User-Supplied GOG Data")]
    [Tooltip("The subfolder for the user's game data (relative to the .exe)")]
    public string userDataSubfolder = "SOUND";
    
    [Header("XMI Playback Settings")]
    [Tooltip("Maximum simultaneous voices (lower = better performance, higher = more complex music)")]
    [Range(8, 128)]
    public int maximumPolyphony = 64;
    
    [Tooltip("Stereo width (0.0 = mono, 1.0 = full stereo)")]
    [Range(0.0f, 1.0f)]
    public float stereoWidth = 0.5f;
    
    [Tooltip("When at max polyphony, skip incoming NoteOns softer than this velocity (0-127; 0 = never)")]
    [Range(0, 127)]
    public int noteDropVelocityThreshold = 0;

    [Tooltip("0 = MeltySynth native velocity (v^2), 1 = sqrt pre-compensate toward linear loudness")]
    [Range(0f, 1f)]
    public float velocityCurveAmount = 1f;

    // --- Public-Static ---
    public static MusicPlayer Instance { get; private set; }
    
    // --- Audio System ---
    private MeltySynth.Synthesizer synthesizer;
    private XMISequencer sequencer;
    private ConcurrentQueue<MusicCommand> commandQueue;

    // A synthesizer built with another soundfont, waiting for the audio thread to take it up between
    // two buffers, so that the track carries on through the change instead of starting over.
    private MeltySynth.Synthesizer pendingSynthesizer;

    // What has been sent to the synthesizer on each channel, kept so that a new one can be put in
    // the same state: the controllers in the order they were last set (data entry only means
    // something after the parameter it follows), the program, the pitch bend, the channel pressure
    // and the notes held. Written and read on the audio thread only.
    private readonly int[,] controllerValues = new int[16, 128];
    private readonly List<int>[] controllerOrder = NewControllerOrder();
    private readonly int[] programs = NewChannelArray();
    // The last volume (CC 7) the track gave each channel, before MT32Volume() moves it.
    private readonly int[] trackVolumes = NewChannelArray(127);
    // The same for the reverb send (CC 91), MeltySynth's default until the track sets one.
    private readonly int[] trackReverbs = NewChannelArray(DefaultReverbSend);
    private readonly int[] pitchBends = NewChannelArray();
    private readonly int[] channelPressures = NewChannelArray();
    private readonly int[,] heldNotes = new int[16, 128];
    // Set by StopMusic() on the main thread: the held notes are forgotten on the next buffer.
    private volatile bool forgetHeldNotes;

    private int unityOutputSampleRate; // Cached from Awake() - AudioSettings can't be accessed from audio thread

    /// <summary>
    /// The rate the synthesizer and the sequencer run at: Unity's own output rate, so that what the
    /// synthesizer renders goes out sample for sample. Rendering at another rate and resampling in
    /// OnAudioFilterRead() rendered a sample or two more per buffer than it used, a seam at every
    /// buffer, heard as a roughness on held notes.
    /// </summary>
    private int SynthesisRate => unityOutputSampleRate > 0 ? unityOutputSampleRate : 48000;

    // Velocity remap: MeltySynth uses (v/127)^2 gain; LUT can pre-lift soft notes
    private readonly byte[] velocityRemap = new byte[128];
    // True while the soundfont is MT32.sf2, which needs MT32Velocity() for some programs.
    // Set on the main thread, read on the audio thread.
    private volatile bool mt32SoundFont;
    private float lastBuiltVelocityCurveAmount = float.NaN;

    // Voice / drop stats for debug HUD (written on audio thread, read on main)
    private int peakActiveVoices;
    private int atCapNoteOns;
    private int droppedNoteOns;
    
    // --- Track Management ---
    public string currentTrack = "";
    private string soundFolderPath;

    // --- Recorded music: mp3, ogg and wav, the player's own tracks ---
    // Played by Unity's decoder on an AudioSource of their own. Not this object's: its
    // OnAudioFilterRead() writes the synthesizer over whatever its source plays.
    private AudioSource clipSource;
    private AudioClip clip;
    // The request the clip came from. A streamed clip plays from the bytes it downloaded, so the
    // request lives as long as the clip and is disposed with it.
    private UnityWebRequest clipWeb;
    // True from the request to the first sample, so that a track still loading counts as playing and
    // Music does not take it for one that has ended.
    private bool clipLoading;
    // Bumped by every switch and stop: a load that finishes after it belongs to a track nobody wants.
    private int clipRequest;
    // The last track asked for could not be started: a file that will not load or decode.
    private bool startFailed;
    // The track to loop once the one playing has ended (FollowWith()); cleared by every switch and stop.
    private string followTrack;

    /// <summary>
    /// Loops the track at path once the one playing has ended, as the original's main menu follows its
    /// title theme with theme 4 (UW.EXE 0x2055c, loop_music_maybe, SOUND.C). Null cancels it, as does
    /// any switch, stop or LetCurrentTrackFinish().
    /// </summary>
    public void FollowWith(string path)
    {
        followTrack = path;
    }

    /// <summary>The track playing goes round again when it ends, the reverse of LetCurrentTrackFinish().</summary>
    public void LoopCurrentTrack()
    {
        if (sequencer != null)
        {
            sequencer.Loop = true;
        }
        if (clipSource != null)
        {
            clipSource.loop = true;
        }
    }

    /// <summary>
    /// Whether the last track asked for failed to start, answered once. Music waits before drawing
    /// again, instead of asking for the same broken file on every frame.
    /// </summary>
    public bool TakeStartFailure()
    {
        bool failed = startFailed;
        startFailed = false;
        return failed;
    }
    
    void Awake()
    {
        // --- Setup Singleton ---
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // --- 1. Load saved audio settings ---
        LoadAudioSettings();
        
        // --- Cache Unity's output sample rate (must be done on main thread) ---
        unityOutputSampleRate = AudioSettings.outputSampleRate;

        RebuildVelocityRemap();
        
        // --- 2. Initialize Synthesizer (from bundled StreamingAssets) ---
        InitializeSynthesizer();
        // and the external synthesizer, when it is the one chosen (MusicPlayerExternalMidi.cs)
        InitializeExternalMidi();
        
        clipSource = new GameObject("RecordedMusic").AddComponent<AudioSource>();
        clipSource.transform.SetParent(transform, false);
        clipSource.playOnAwake = false;
        clipSource.spatialBlend = 0.0f;
        clipSource.outputAudioMixerGroup = GetComponent<AudioSource>().outputAudioMixerGroup;

        // --- 3. Set up sound folder path ---
        try
        {
            soundFolderPath = GameDataPath.GetSoundPath();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error finding user data path: {ex.Message}");
            Debug.LogWarning("Make sure the 'SOUND' folder is accessible.");
        }
    }

    void OnValidate()
    {
        RebuildVelocityRemap();
    }

    void Update()
    {
        // Rebuild LUT if Inspector value changed at runtime
        if (velocityCurveAmount != lastBuiltVelocityCurveAmount)
        {
            RebuildVelocityRemap();
        }

        // The synthesizer applies these two on the audio thread; a recorded track has its source.
        if (clipSource != null)
        {
            clipSource.volume = masterVolumeScale * duckMultiplier;
        }

        // the external synthesizer's volume, and the soundfont back if its port stopped answering
        UpdateExternalMidi();

        if (followTrack != null && !IsPlaying)
        {
            // Stopped first, so that a follow-on that is the track just ended starts again as well.
            string next = followTrack;
            StopMusic();
            SwitchTrack(next, true);
        }
    }

    /// <summary>
    /// Rebuild velocity remap LUT. amount 0 = identity; 1 = full sqrt pre-compensation for MeltySynth's (v/127)^2 gain.
    /// </summary>
    private void RebuildVelocityRemap()
    {
        float amount = Mathf.Clamp01(velocityCurveAmount);
        velocityRemap[0] = 0;
        for (int v = 1; v < 128; v++)
        {
            float t = v / 127f;
            float shaped = Mathf.Lerp(t, Mathf.Sqrt(t), amount);
            int remapped = Mathf.Clamp(Mathf.RoundToInt(127f * shaped), 1, 127);
            velocityRemap[v] = (byte)remapped;
        }
        lastBuiltVelocityCurveAmount = amount;
    }

    private void ResetVoiceStats()
    {
        peakActiveVoices = 0;
        atCapNoteOns = 0;
        droppedNoteOns = 0;
    }

    void LoadAudioSettings()
    {
        // Load saved settings from PlayerPrefs
        masterVolumeScale = PlayerPrefs.GetFloat("Options_MusicVolume", 0.5f);
        string savedSoundFont = PlayerPrefs.GetString("Options_SoundFont", "");
        
        // Only use saved sound font if it's not empty
        if (!string.IsNullOrEmpty(savedSoundFont))
        {
            soundFontFileName = savedSoundFont;
        }
    }
    
    void InitializeSynthesizer()
    {
        if (string.IsNullOrEmpty(soundFontFileName))
        {
            Debug.LogError("SoundFont filename is not set!");
            return;
        }

        // 1. Build the full, platform-specific path to the file
        string fullPath = Path.Combine(Application.streamingAssetsPath, soundFontFileName);

        // 2. Check if the file exists
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"Failed to find SoundFont at path: {fullPath}");
            Debug.LogWarning("Make sure the file is in 'Assets/StreamingAssets/' and the filename is correct.");
            return;
        }

        mt32SoundFont = Path.GetFileName(soundFontFileName).Equals("MT32.sf2", StringComparison.OrdinalIgnoreCase);

        // 3. Load MeltySynth with performance-optimized settings
        var settings = new MeltySynth.SynthesizerSettings(SynthesisRate);
        settings.MaximumPolyphony = maximumPolyphony;  // Limit voices to prevent CPU overload
        var created = new MeltySynth.Synthesizer(fullPath, settings); 
        
        if (commandQueue == null)
        {
            commandQueue = new ConcurrentQueue<MusicCommand>();
        }

        // Pre-initialize all channels to a moderate volume.
        // Note: actual volume will be scaled by masterVolumeScale during playback.
        for (int i = 0; i < 16; i++)
        {
            // MIDI Controller Change: (channel, 0xB0, controller 7 (volume), value 127)
            created.ProcessMidiMessage(i, 0xB0, 7, SynthVolume(i, 127));
        }
        if (mt32SoundFont)
        {
            // Every channel's volume is MT32Headroom lower (MT32Volume()), so that a program can be
            // raised by up to that much; the master volume gives it back.
            created.MasterVolume *= Mathf.Pow(10f, MT32Headroom / 20f);
        }

        // With a track playing, the audio thread takes the new synthesizer up itself, after putting it
        // in the old one's state. Without one there is nothing to carry over.
        if (synthesizer != null && sequencer != null && sequencer.IsLoaded)
        {
            Interlocked.Exchange(ref pendingSynthesizer, created);
        }
        else
        {
            synthesizer = created;
        }
    }

    private static List<int>[] NewControllerOrder()
    {
        var order = new List<int>[16];
        for (int i = 0; i < order.Length; ++i)
        {
            order[i] = new List<int>();
        }
        return order;
    }

    private static int[] NewChannelArray(int value = -1)
    {
        int[] values = new int[16];
        for (int i = 0; i < values.Length; ++i)
        {
            values[i] = value;
        }
        return values;
    }

    // --- Channel state, on the audio thread ---

    private void RecordController(int channel, int controller, int value)
    {
        if (controller >= 120)
        {
            // The channel mode messages. Reset All Controllers clears what the channel had, and All
            // Sound Off and All Notes Off - the second sent when a track loops - the notes.
            if (controller == 121)
            {
                controllerOrder[channel].Clear();
            }
            else if (controller == 120 || controller == 123)
            {
                for (int note = 0; note < 128; ++note)
                {
                    heldNotes[channel, note] = 0;
                }
            }
            return;
        }

        controllerValues[channel, controller] = value;
        if (controller == 7)
        {
            trackVolumes[channel] = value;
        }
        else if (controller == 91)
        {
            trackReverbs[channel] = value;
        }
        controllerOrder[channel].Remove(controller);
        controllerOrder[channel].Add(controller);
    }

    /// <summary>
    /// Puts the synthesizer just taken up in the state the old one was left in, so that the track
    /// carries on with the same instruments, levels and notes, only through another soundfont. The
    /// notes held start again, which is a new attack and not silence until the next note.
    /// </summary>
    private void ReplayChannelState()
    {
        for (int channel = 0; channel < 16; ++channel)
        {
            foreach (int controller in controllerOrder[channel])
            {
                int value = controllerValues[channel, controller];
                synthesizer.ProcessMidiMessage(channel, 0xB0, controller,
                    controller == 7 ? SynthVolume(channel, value) : controller == 91 ? SynthReverb(channel, value) : value);
            }
            if (programs[channel] >= 0)
            {
                synthesizer.ProcessMidiMessage(channel, 0xC0, SynthProgram(channel, programs[channel]), 0);
                if (mt32SoundFont)
                {
                    synthesizer.ProcessMidiMessage(channel, 0xB0, 91, SynthReverb(channel, trackReverbs[channel]));
                }
            }
            if (pitchBends[channel] >= 0)
            {
                synthesizer.ProcessMidiMessage(channel, 0xE0, pitchBends[channel] & 0x7F, (pitchBends[channel] >> 7) & 0x7F);
            }
            if (channelPressures[channel] >= 0)
            {
                synthesizer.ProcessMidiMessage(channel, 0xD0, channelPressures[channel], 0);
            }
            for (int note = 0; note < 128; ++note)
            {
                if (heldNotes[channel, note] > 0)
                {
                    synthesizer.NoteOn(channel, note, heldNotes[channel, note]);
                }
            }
        }
    }
    
    /// <summary>
    /// Reload the synthesizer with a new sound font. The track playing carries on from where it is.
    /// </summary>
    /// <remarks>
    /// It used to stop the track and start it again from the beginning, unlooped, so that a change of
    /// soundfont in the options also cut the title music's loop. Now the new synthesizer is handed to
    /// the audio thread, which sets it up like the old one and goes on with the same sequencer.
    /// </remarks>
    public void ReloadSynthesizerWithSoundFont(string newSoundFontFileName, float musicVolume)
    {
        if (newSoundFontFileName != soundFontFileName)
        {
            // Update sound font filename
            soundFontFileName = newSoundFontFileName;
        
            // Reload synthesizer
            InitializeSynthesizer();
        
            Debug.Log($"Synthesizer reloaded with sound font: {soundFontFileName}");
        }
        masterVolumeScale = musicVolume;
    }

    /// <summary>
    /// Temporarily scale music loudness (e.g. 0.5 during intro cutscene). Does not change saved options volume.
    /// </summary>
    public void SetMusicDuckMultiplier(float multiplier)
    {
        duckMultiplier = Mathf.Max(0f, multiplier);
    }

    /// <summary>
    /// Switch to a different music track. Stops current track and starts new one immediately.
    /// </summary>
    /// <remarks>
    /// An XMI plays through the synthesizer at once. Any other file - mp3, ogg, wav - is loaded by
    /// Unity's decoder in the background and starts when it is ready, a few hundred milliseconds for a
    /// long track; IsPlaying is true meanwhile.
    /// </remarks>
    /// <param name="fileName">A file of the sound folder (e.g., "UW01.XMI"), or a full path</param>
    /// <param name="loop">Whether to loop the track when it ends (default: false)</param>
    public void SwitchTrack(string fileName, bool loop = false)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            StopMusic();
            return;
        }
        
        if (fileName == currentTrack)
            return;
        
        // Stop current music
        StopMusic();
        startFailed = false;
        
        // Build full path and load
        string fullPath = Path.Combine(soundFolderPath, fileName);
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"Song file not found at path: {fullPath}");
            startFailed = true;
            return;
        }
        
        // Load and start new track
        if (!Path.GetExtension(fullPath).Equals(".xmi", StringComparison.OrdinalIgnoreCase))
        {
            currentTrack = fileName;
            clipLoading = true;
            StartCoroutine(LoadClip(fullPath, loop, ++clipRequest));
            return;
        }

        LoadSong(fullPath, loop);
        currentTrack = fileName;
    }
    
    /// <summary>
    /// Let the track that is playing run to its end instead of looping round again.
    /// </summary>
    /// <remarks>
    /// The title screen track is started looping and has to stop looping when the game begins, so
    /// that it plays out once and hands over. Nothing else changes: the track keeps playing from
    /// where it is, and IsPlaying goes false when it ends, the same as any track started unlooped.
    /// </remarks>
    public void LetCurrentTrackFinish()
    {
        followTrack = null;
        if (sequencer != null)
        {
            sequencer.Loop = false;
        }
        if (clipSource != null)
        {
            clipSource.loop = false;
        }
    }

    /// <summary>
    /// Stop music playback completely.
    /// </summary>
    public void StopMusic()
    {
        // Clear sequencer (IsLoaded is checked in OnAudioFilterRead)
        sequencer = null;
        followTrack = null;

        ++clipRequest;
        clipLoading = false;
        if (clipSource != null)
        {
            clipSource.Stop();
            clipSource.clip = null;
        }
        if (clip != null)
        {
            Destroy(clip);
            clip = null;
        }
        clipWeb?.Dispose();
        clipWeb = null;
        
        // Every note released, as the original's driver ends a track: a Note Off for each note sounding and
        // the sustain pedal up, never All Sound Off, so the last notes ring on with their own release and
        // reverb under the next track - opening the map, the exploring music fades instead of stopping
        // dead. UW.EXE 0x1f39c (load_new_music, SOUND.C) stops the old sequence just before starting the
        // new one, and the driver's stop sends note-offs only.
        if (synthesizer != null)
        {
            for (int channel = 0; channel < 16; channel++)
            {
                // Sustain off (CC 64), then All Notes Off (CC 123)
                synthesizer.ProcessMidiMessage(channel, 0xB0, 64, 0);
                synthesizer.ProcessMidiMessage(channel, 0xB0, 123, 0);
            }
        }
        forgetHeldNotes = true;
        
        currentTrack = "";
    }
    
    /// <summary>
    /// Check if music is currently playing.
    /// </summary>
    public bool IsPlaying
    {
        get { return (sequencer != null && sequencer.IsLoaded) || clipLoading || (clipSource != null && clipSource.isPlaying); }
    }

    /// <summary>
    /// Current playback position in seconds, or 0 if nothing is loaded.
    /// </summary>
    public double CurrentTime
    {
        get
        {
            if (sequencer != null)
            {
                return sequencer.CurrentTime;
            }
            return clipSource != null && clipSource.clip != null ? clipSource.time : 0.0;
        }
    }

    public int ActiveVoiceCount => synthesizer != null ? synthesizer.ActiveVoiceCount : 0;
    public int MaximumPolyphonyCount => synthesizer != null ? synthesizer.MaximumPolyphony : maximumPolyphony;
    public int PeakActiveVoices => peakActiveVoices;
    public int AtCapNoteOns => atCapNoteOns;
    public int DroppedNoteOns => droppedNoteOns;
    public float VelocityCurveAmount => velocityCurveAmount;
    
    /// <summary>
    /// Loads a recorded track with Unity's decoder and plays it, unless another switch or a stop has
    /// come in the meantime.
    /// </summary>
    /// <remarks>
    /// The track is streamed: decoded as it plays, not whole before it starts. Decoded whole, the
    /// work is done inside GetContent(), on the main thread, and the game stands still meanwhile:
    /// 28 ms for an 11-second mp3, 14.4 s for a 68-minute one, against a few ms streamed (measured
    /// in play, November 2026). The clip reports DecompressOnLoad either way, so its loadType does
    /// not tell the two apart.
    /// </remarks>
    private IEnumerator LoadClip(string fullPath, bool loop, int request)
    {
        UnityWebRequest web = UnityWebRequestMultimedia.GetAudioClip(FileUri(fullPath), AudioTypeOf(fullPath));
        ((DownloadHandlerAudioClip)web.downloadHandler).streamAudio = true;
        yield return web.SendWebRequest();
        if (request != clipRequest)
        {
            web.Dispose();
            yield break;
        }

        clipLoading = false;
        AudioClip loaded = null;
        if (web.result == UnityWebRequest.Result.Success)
        {
            try
            {
                loaded = DownloadHandlerAudioClip.GetContent(web);
            }
            catch (Exception e)
            {
                Debug.LogError($"Cannot decode {fullPath}: {e.Message}");
            }
        }
        else
        {
            Debug.LogError($"Cannot load {fullPath}: {web.error}");
        }

        if (loaded == null || loaded.length <= 0.0f)
        {
            if (loaded != null)
            {
                Destroy(loaded);
            }
            web.Dispose();
            currentTrack = "";
            startFailed = true;
            yield break;
        }

        clip = loaded;
        clipWeb = web;
        clipSource.clip = clip;
        clipSource.loop = loop;
        clipSource.volume = masterVolumeScale * duckMultiplier;
        clipSource.Play();
    }

    public static AudioType AudioTypeOf(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
        case ".mp3": return AudioType.MPEG;
        case ".ogg": return AudioType.OGGVORBIS;
        case ".wav": return AudioType.WAV;
        default: return AudioType.UNKNOWN;
        }
    }

    /// <summary>
    /// A file:// URI with every part of the path escaped, so spaces, accents and '#' in a name
    /// survive; new Uri(path) does not agree with itself across runtimes on '#'.
    /// </summary>
    public static string FileUri(string path)
    {
        string[] parts = path.Replace('\\', '/').Split('/');
        for (int i = 0; i < parts.Length; ++i)
        {
            bool drive = i == 0 && parts[i].Length == 2 && parts[i][1] == ':';
            if (!drive)
            {
                parts[i] = Uri.EscapeDataString(parts[i]);
            }
        }
        string joined = string.Join("/", parts);
        return joined.StartsWith("/") ? "file://" + joined : "file:///" + joined;
    }

    // This function takes the *full absolute path*
    private void LoadSong(string fullPath, bool loop = false)
    {
        ResetVoiceStats();
        RebuildVelocityRemap();

        // Create sequencer - TPQN will be auto-calculated from meta-events
        sequencer = new XMISequencer(fullPath, commandQueue, SynthesisRate, loop);
        if (!sequencer.IsLoaded)
        {
            sequencer = null;
            startFailed = true;
        }
    }

    private void HandleNoteOn(int channel, int note, int velocity)
    {
        if (velocity <= 0)
        {
            synthesizer.NoteOff(channel, note);
            heldNotes[channel, note] = 0;
            return;
        }

        int active = synthesizer.ActiveVoiceCount;
        int maxPoly = synthesizer.MaximumPolyphony;
        if (active > peakActiveVoices)
        {
            peakActiveVoices = active;
        }

        if (active >= maxPoly)
        {
            atCapNoteOns++;
            if (velocity < noteDropVelocityThreshold)
            {
                droppedNoteOns++;
                return;
            }
        }

        int remappedVelocity = velocityRemap[Mathf.Clamp(velocity, 0, 127)];
        if (mt32SoundFont && channel != 9)
        {
            int mt32Velocity = MT32Velocity(programs[channel], velocity);
            if (mt32Velocity > 0)
            {
                remappedVelocity = mt32Velocity;
            }
        }
        synthesizer.NoteOn(channel, note, remappedVelocity);
        heldNotes[channel, note] = remappedVelocity;

        active = synthesizer.ActiveVoiceCount;
        if (active > peakActiveVoices)
        {
            peakActiveVoices = active;
        }
    }

    /// <summary>
    /// The velocity to play a program at with MT32.sf2, or -1 for the usual remap.
    /// </summary>
    /// <remarks>
    /// The soundfont gives every program the same answer to velocity, but on an MT-32 each timbre
    /// has its own, and the original sends velocities unchanged. Both curves were measured against
    /// a recording of the original on an MT-32.
    /// SoundTrack (program 36) barely answers to velocity. The map's track opens on a SoundTrack pad
    /// at velocity 14, which an MT-32 plays 6 dB under the melody that follows and the usual remap
    /// 17 dB under, so the first two seconds sounded silent; 85 + 0.3 x velocity puts it 6 dB under.
    /// The Timpani (program 112) is louder than the usual remap makes it but still soft at low
    /// velocity: the Wanderer's strokes at velocity 48 stand out of the music by 3-4 dB on an MT-32
    /// and by 0-1 dB with the usual remap, and its stroke at 14 is not heard. 30 + 1.5 x velocity
    /// matches both.
    /// </remarks>
    private static int MT32Velocity(int program, int velocity)
    {
        switch (program)
        {
            case 36:
                return Mathf.Clamp(Mathf.RoundToInt(85f + 0.3f * velocity), 1, 127);
            case 112:
                return Mathf.Clamp(Mathf.RoundToInt(30f + 1.5f * velocity), 1, 127);
            default:
                return -1;
        }
    }

    /// <summary>How far MT32Volume() can raise a program, in dB.</summary>
    private const float MT32Headroom = 12f;

    /// <summary>MeltySynth's reverb send (CC 91) on a channel no message has set.</summary>
    private const int DefaultReverbSend = 40;

    /// <summary>The reverb send (CC 91) to give the synthesizer for a channel the track set to send.</summary>
    private int SynthReverb(int channel, int send)
    {
        int own = mt32SoundFont && channel != 9 && programs[channel] >= 0 ? MT32Reverb(programs[channel]) : -1;
        return own >= 0 ? own : send;
    }

    /// <summary>
    /// The reverb send to play a program with on MT32.sf2, or -1 for the track's. Jungle Tune's notes
    /// are a few hundredths of a second long, and an MT-32 lets them ring on in its reverb for a
    /// second after; the glockenspiel that plays them stops short without more of it.
    /// </summary>
    private static int MT32Reverb(int program)
    {
        return program == 127 ? 127 : -1;
    }

    /// <summary>The soundfont's preset that plays a track's program: MT32Preset() with MT32.sf2.</summary>
    private int SynthProgram(int channel, int program)
    {
        return mt32SoundFont && channel != 9 ? MT32Preset(program) : program;
    }

    /// <summary>The volume (CC 7) to give the synthesizer for a channel the track set to volume.</summary>
    private int SynthVolume(int channel, int volume)
    {
        if (!mt32SoundFont)
        {
            return volume;
        }
        float gain = channel == 9 || programs[channel] < 0 ? 0f : MT32Volume(programs[channel]);
        return Mathf.Clamp(Mathf.RoundToInt(volume * Mathf.Pow(10f, (gain - MT32Headroom) / 40f)), 0, 127);
    }

    /// <summary>
    /// The preset of MT32.sf2 that plays an MT-32 program the soundfont does not have.
    /// </summary>
    /// <remarks>
    /// MT32.sf2 names and plays most programs as the MT-32 does, but fills some with General MIDI
    /// presets: another instrument. Its Shakuhachi is mostly breath where the MT-32's has a pitch. Each of these is played instead by the preset of the soundfont
    /// closest to the MT-32's timbre, chosen against a recording of the tracks on an MT-32 (the
    /// level at each note's pitch and harmonics, and how much of it is pitch and how much noise).
    /// The three other General MIDI fillers the tracks use (33, 43, 121) come close enough. The
    /// choices between presets of like timbre were made by ear against the recording.
    /// </remarks>
    private static int MT32Preset(int program)
    {
        switch (program)
        {
            case 23: return 9;      // Celesta 2, a Bandoneon in MT32.sf2: its Glockenspiel
            case 38: return 11;     // Warm Bell, a Synth Bass: Vibraphone
            case 25: return 92;     // Syn Brass 2, a Steel Guitar: French Horn, which holds its notes
            case 34: return 37;     // Chorale, a Picked Bass: Atmosphere
            case 49: return 48;     // Str Sect 2, a Slow Strings that swells late: Strings
            case 107: return 77;    // Shakuhachi, mostly breath in MT32.sf2: Pan Pipes
            case 127: return 101;   // Jungle Tune, a Gun Shot with no pitch: Glock, a glockenspiel
            default: return program;
        }
    }

    /// <summary>
    /// How much louder or softer to play a program with MT32.sf2 than the soundfont makes it, in dB.
    /// </summary>
    /// <remarks>
    /// Measured against the same recording, at each note's pitch, against the track's median note,
    /// checked on the tracks' overall level and peaks, and corrected by ear (Flute 2 and the French
    /// Horn). Not raised although they measure soft: the whistle (109) and flute (72) of Descent,
    /// whose samples are breathier than the MT-32's and already carry the track's level.
    /// </remarks>
    private static float MT32Volume(int program)
    {
        switch (program)
        {
            case 93: return 5f;     // French Horn 2
            case 85: return 8f;     // English Horn
            case 107: return 6f;    // Shakuhachi, on Pan Pipes
            case 25: return 5f;     // Syn Brass 2, on the French Horn
            case 92: return 4f;     // French Horn 1
            case 34: return 3f;     // Chorale, on Atmosphere
            case 127: return 5f;    // Jungle Tune, on the Glock
            case 84: return -3f;    // Oboe
            case 23: return -7f;    // Celesta 2, on the Glockenspiel
            case 58: return -7f;    // Harp 2
            case 73: return -4f;    // Flute 2
            case 74: return -8f;    // Piccolo 1
            default: return 0f;
        }
    }

    // --- Unity Audio Hook ---
    // Runs on the dedicated, real-time Audio Thread, called from OnAudioFilterRead() (MusicPlayerExternalMidi.cs)
    // unless the music is playing on an external synthesizer
    private void RenderSynth(float[] data, int channels)
    {
        if (forgetHeldNotes)
        {
            forgetHeldNotes = false;
            Array.Clear(heldNotes, 0, heldNotes.Length);
        }

        // A soundfont changed while a track was playing: take the new synthesizer up here, between
        // two buffers, where nothing else is using the old one.
        MeltySynth.Synthesizer replacement = Interlocked.Exchange(ref pendingSynthesizer, null);
        if (replacement != null)
        {
            synthesizer = replacement;
            ReplayChannelState();
        }

        if (synthesizer == null) return;
        
        if (channels != 2) return;
        int sampleCount = data.Length / channels;
        
        // 1. Process sequencer events with sample-accurate timing
        //    This happens BEFORE rendering to ensure notes start precisely
        if (sequencer != null)
        {
            sequencer.ProcessSamples(sampleCount);
        }

        // 2. Process all pending commands
        while (commandQueue.TryDequeue(out MusicCommand cmd))
        {
            switch (cmd.Type)
            {
            case MusicCommandType.NoteOn:
                HandleNoteOn(cmd.Channel, cmd.Note, cmd.Velocity);
                break;
            case MusicCommandType.NoteOff:
                synthesizer.NoteOff(cmd.Channel, cmd.Note);
                heldNotes[cmd.Channel, cmd.Note] = 0;
                break;
            case MusicCommandType.ProgramChange:
                // MIDI Program Change: (channel, 0xC0, program, 0)
                synthesizer.ProcessMidiMessage(cmd.Channel, 0xC0, SynthProgram(cmd.Channel, cmd.Program), 0);
                programs[cmd.Channel] = cmd.Program;
                if (mt32SoundFont)
                {
                    // the new program's level and reverb
                    synthesizer.ProcessMidiMessage(cmd.Channel, 0xB0, 7, SynthVolume(cmd.Channel, trackVolumes[cmd.Channel]));
                    synthesizer.ProcessMidiMessage(cmd.Channel, 0xB0, 91, SynthReverb(cmd.Channel, trackReverbs[cmd.Channel]));
                }
                break;
            case MusicCommandType.ControllerChange:
                // MIDI Controller Change: (channel, 0xB0, controller, value)
                if (cmd.Controller == 7)
                {
                    synthesizer.ProcessMidiMessage(cmd.Channel, 0xB0, 7, SynthVolume(cmd.Channel, cmd.Value));
                }
                else if (cmd.Controller == 91)
                {
                    synthesizer.ProcessMidiMessage(cmd.Channel, 0xB0, 91, SynthReverb(cmd.Channel, cmd.Value));
                }
                else
                {
                    synthesizer.ProcessMidiMessage(cmd.Channel, 0xB0, cmd.Controller, cmd.Value);
                }
                RecordController(cmd.Channel, cmd.Controller, cmd.Value);
                break;
            case MusicCommandType.PolyphonicAftertouch:
                // MIDI Polyphonic Key Pressure: (channel, 0xA0, note, value)
                synthesizer.ProcessMidiMessage(cmd.Channel, 0xA0, cmd.Note, cmd.Value);
                break;
            case MusicCommandType.PitchBend:
                // MIDI Pitch Bend: (channel, 0xE0, LSB, MSB)
                int pitchLSB = cmd.Value & 0x7F;
                int pitchMSB = (cmd.Value >> 7) & 0x7F;
                synthesizer.ProcessMidiMessage(cmd.Channel, 0xE0, pitchLSB, pitchMSB);
                pitchBends[cmd.Channel] = cmd.Value & 0x3FFF;
                break;
            case MusicCommandType.ChannelAftertouch:
                // MIDI Channel Pressure: (channel, 0xD0, value, 0)
                synthesizer.ProcessMidiMessage(cmd.Channel, 0xD0, cmd.Value, 0);
                channelPressures[cmd.Channel] = cmd.Value;
                break;
            }
        }

        // 3. Render audio at Unity's output rate
        int voicesNow = synthesizer.ActiveVoiceCount;
        if (voicesNow > peakActiveVoices)
        {
            peakActiveVoices = voicesNow;
        }

        Span<float> leftBuffer = stackalloc float[sampleCount];
        Span<float> rightBuffer = stackalloc float[sampleCount];
        synthesizer.Render(leftBuffer, rightBuffer);

        // Apply stereo width mixing, master volume scaling, and interleave
        for (int i = 0, j = 0; i < sampleCount; i++)
        {
            float left = leftBuffer[i];
            float right = rightBuffer[i];
            
            // Mix to reduce stereo width (0.0 = mono, 1.0 = full stereo)
            float mono = (left + right) * 0.5f;
            float mixedLeft = mono + (left - mono) * stereoWidth;
            float mixedRight = mono + (right - mono) * stereoWidth;
            
            float vol = masterVolumeScale * duckMultiplier;
            data[j++] = mixedLeft * vol;
            data[j++] = mixedRight * vol;
        }
    }
}
