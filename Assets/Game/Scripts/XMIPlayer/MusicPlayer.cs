using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System;

public enum ESampleRate
{
    SampleRate11kHz = 11025,
    SampleRate22kHz = 22050,
    SampleRate32kHz = 32000,
    SampleRate44_1kHz = 44100,
    SampleRate48kHz = 48000
}

[RequireComponent(typeof(AudioSource))]
public class MusicPlayer : MonoBehaviour
{
    private string soundFontFileName = "Soundfonts/MT32.sf2";
    private float masterVolumeScale = 0.5f;
    /// <summary>Multiplies output after user volume (1 = normal, 0.5 = half for cutscenes, etc.).</summary>
    private float duckMultiplier = 1f;
    
    [Header("User-Supplied GOG Data")]
    [Tooltip("The subfolder for the user's game data (relative to the .exe)")]
    public string userDataSubfolder = "SOUND";
    
    [Header("XMI Playback Settings")]
    [Tooltip("Maximum simultaneous voices (lower = better performance, higher = more complex music)")]
    [Range(8, 32)]
    public int maximumPolyphony = 12;
    
    [Tooltip("Stereo width (0.0 = mono, 1.0 = full stereo)")]
    [Range(0.0f, 1.0f)]
    public float stereoWidth = 0.5f;
    
    [Tooltip("Sample rate for XMI playback (lower = better performance, higher = better quality)")]
    public ESampleRate xmiSampleRate = ESampleRate.SampleRate48kHz;

    [Tooltip("When at max polyphony, skip incoming NoteOns softer than this velocity (0-127)")]
    [Range(0, 127)]
    public int noteDropVelocityThreshold = 24;

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
    private readonly int[] pitchBends = NewChannelArray();
    private readonly int[] channelPressures = NewChannelArray();
    private readonly int[,] heldNotes = new int[16, 128];
    // Set by StopMusic() on the main thread: the held notes are forgotten on the next buffer.
    private volatile bool forgetHeldNotes;

    // Resampling state
    private double resamplePosition = 0.0; // Use double for precision, continuous (doesn't wrap)
    private int unityOutputSampleRate; // Cached from Awake() - AudioSettings can't be accessed from audio thread

    // Velocity remap: MeltySynth uses (v/127)^2 gain; LUT can pre-lift soft notes
    private readonly byte[] velocityRemap = new byte[128];
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
    // True from the request to the first sample, so that a track still loading counts as playing and
    // Music does not take it for one that has ended.
    private bool clipLoading;
    // Bumped by every switch and stop: a load that finishes after it belongs to a track nobody wants.
    private int clipRequest;
    
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

        // 3. Load MeltySynth with performance-optimized settings
        var settings = new MeltySynth.SynthesizerSettings((int)xmiSampleRate);
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
            created.ProcessMidiMessage(i, 0xB0, 7, 127);
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

    private static int[] NewChannelArray()
    {
        int[] values = new int[16];
        for (int i = 0; i < values.Length; ++i)
        {
            values[i] = -1;
        }
        return values;
    }

    // --- Channel state, on the audio thread ---

    private void RecordController(int channel, int controller, int value)
    {
        if (controller >= 120)
        {
            // The channel mode messages. Reset All Controllers clears what the channel had, and All
            // Sound Off and All Notes Off - which the sequencer sends when a track loops - the notes.
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
                synthesizer.ProcessMidiMessage(channel, 0xB0, controller, controllerValues[channel, controller]);
            }
            if (programs[channel] >= 0)
            {
                synthesizer.ProcessMidiMessage(channel, 0xC0, programs[channel], 0);
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
        
        // Build full path and load
        string fullPath = Path.Combine(soundFolderPath, fileName);
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"Song file not found at path: {fullPath}");
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
        
        // Reset resampling state when switching tracks
        resamplePosition = 0.0;
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
        
        // Reset all MIDI channels - stop all notes
        if (synthesizer != null)
        {
            for (int channel = 0; channel < 16; channel++)
            {
                // All Notes Off (CC 123)
                synthesizer.ProcessMidiMessage(channel, 0xB0, 123, 0);
                // All Sound Off (CC 120)
                synthesizer.ProcessMidiMessage(channel, 0xB0, 120, 0);
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
    /// Tried on the Windows player and under Proton for mp3, ogg and wav (mod 900's probe, October
    /// 2026): every way of asking - streamed, compressed - decodes the whole track into memory, a few
    /// tens of megabytes for a few minutes of music, so only the track that plays is kept.
    /// </remarks>
    private IEnumerator LoadClip(string fullPath, bool loop, int request)
    {
        using UnityWebRequest web = UnityWebRequestMultimedia.GetAudioClip(FileUri(fullPath), AudioTypeOf(fullPath));
        yield return web.SendWebRequest();
        if (request != clipRequest)
        {
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
            currentTrack = "";
            yield break;
        }

        clip = loaded;
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

        // Use configured sample rate
        int actualSampleRate = (int)xmiSampleRate;
        
        // Create sequencer - TPQN will be auto-calculated from meta-events
        sequencer = new XMISequencer(fullPath, commandQueue, actualSampleRate, loop);
        if (!sequencer.IsLoaded)
        {
            sequencer = null;
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
        synthesizer.NoteOn(channel, note, remappedVelocity);
        heldNotes[channel, note] = remappedVelocity;

        active = synthesizer.ActiveVoiceCount;
        if (active > peakActiveVoices)
        {
            peakActiveVoices = active;
        }
    }

    // --- Unity Audio Hook ---
    // Runs on the dedicated, real-time Audio Thread
    void OnAudioFilterRead(float[] data, int channels)
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
        
        int unityOutputRate = unityOutputSampleRate; // Use cached value (can't call AudioSettings from audio thread)
        int customRate = (int)xmiSampleRate;

        // 1. Process sequencer events with sample-accurate timing
        //    This happens BEFORE rendering to ensure notes start precisely
        //    Sequencer processes at custom rate, so convert Unity's sample count
        if (sequencer != null)
        {
            if (customRate == unityOutputRate)
            {
                sequencer.ProcessSamples(sampleCount);
            }
            else
            {
                // Convert Unity's sample count to custom rate samples
                float rateRatio = (float)customRate / unityOutputRate;
                int sequencerSamples = Mathf.CeilToInt(sampleCount * rateRatio);
                sequencer.ProcessSamples(sequencerSamples);
            }
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
                synthesizer.ProcessMidiMessage(cmd.Channel, 0xC0, cmd.Program, 0);
                programs[cmd.Channel] = cmd.Program;
                break;
            case MusicCommandType.ControllerChange:
                // MIDI Controller Change: (channel, 0xB0, controller, value)
                synthesizer.ProcessMidiMessage(cmd.Channel, 0xB0, cmd.Controller, cmd.Value);
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

        // 3. Render audio at custom sample rate, then resample to Unity's output rate
        int voicesNow = synthesizer.ActiveVoiceCount;
        if (voicesNow > peakActiveVoices)
        {
            peakActiveVoices = voicesNow;
        }

        if (customRate == unityOutputRate)
        {
            // No resampling needed - render directly
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
        else
        {
            // Resampling needed - generate samples on-demand and resample
            // Calculate step size: how fast to step through source buffer
            // When upsampling (customRate < unityRate), step is < 1.0
            double step = (double)customRate / unityOutputRate;
            
            // Calculate how many source samples we need
            double startPos = resamplePosition;
            double endPos = resamplePosition + (sampleCount - 1) * step;
            int startIndex = (int)Math.Floor(startPos);
            int endIndex = (int)Math.Ceiling(endPos) + 1; // +1 for interpolation
            int samplesToGenerate = endIndex - startIndex;
            
            // Generate samples at custom rate
            Span<float> leftBufferCustom = stackalloc float[samplesToGenerate];
            Span<float> rightBufferCustom = stackalloc float[samplesToGenerate];
            synthesizer.Render(leftBufferCustom, rightBufferCustom);
            
            // Resample from custom rate to Unity's output rate using linear interpolation
            double readPos = resamplePosition - startIndex; // Relative to buffer start
            for (int i = 0, j = 0; i < sampleCount; i++)
            {
                int index = (int)readPos;
                double fraction = readPos - index;
                
                // Clamp indices to avoid out-of-bounds
                int index0 = Mathf.Clamp(index, 0, samplesToGenerate - 1);
                int index1 = Mathf.Clamp(index + 1, 0, samplesToGenerate - 1);
                
                // Linear interpolation
                float left = (float)(leftBufferCustom[index0] * (1.0 - fraction) + leftBufferCustom[index1] * fraction);
                float right = (float)(rightBufferCustom[index0] * (1.0 - fraction) + rightBufferCustom[index1] * fraction);
                
                // Mix to reduce stereo width (0.0 = mono, 1.0 = full stereo)
                float mono = (left + right) * 0.5f;
                float mixedLeft = mono + (left - mono) * stereoWidth;
                float mixedRight = mono + (right - mono) * stereoWidth;
                
                float vol = masterVolumeScale * duckMultiplier;
                data[j++] = mixedLeft * vol;
                data[j++] = mixedRight * vol;
                
                readPos += step;
            }
            
            // Update resample position (continuous, doesn't wrap)
            resamplePosition = endPos + step;
        }
    }
}
