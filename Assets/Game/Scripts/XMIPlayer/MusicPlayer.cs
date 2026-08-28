using UnityEngine;
using System.Collections.Concurrent;
using System.IO;
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
        synthesizer = new MeltySynth.Synthesizer(fullPath, settings); 
        
        if (commandQueue == null)
        {
            commandQueue = new ConcurrentQueue<MusicCommand>();
        }

        // Pre-initialize all channels to a moderate volume.
        // Note: actual volume will be scaled by masterVolumeScale during playback.
        for (int i = 0; i < 16; i++)
        {
            // MIDI Controller Change: (channel, 0xB0, controller 7 (volume), value 127)
            synthesizer.ProcessMidiMessage(i, 0xB0, 7, 127);
        }
    }
    
    /// <summary>
    /// Reload the synthesizer with a new sound font and restart the current track.
    /// </summary>
    public void ReloadSynthesizerWithSoundFont(string newSoundFontFileName, float musicVolume)
    {
        if (newSoundFontFileName != soundFontFileName)
        {
            // Remember current track
            string trackToRestart = currentTrack;
        
            // Stop current playback
            StopMusic();
        
            // Update sound font filename
            soundFontFileName = newSoundFontFileName;
        
            // Reload synthesizer
            InitializeSynthesizer();
        
            // Restart the track if one was playing
            if (!string.IsNullOrEmpty(trackToRestart))
            {
                SwitchTrack(trackToRestart);
            }
        
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
    /// <param name="fileName">XMI filename (e.g., "UW01.XMI")</param>
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
        LoadSong(fullPath, loop);
        currentTrack = fileName;
        
        // Reset resampling state when switching tracks
        resamplePosition = 0.0;
    }
    
    /// <summary>
    /// Stop music playback completely.
    /// </summary>
    public void StopMusic()
    {
        // Clear sequencer (IsLoaded is checked in OnAudioFilterRead)
        sequencer = null;
        
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
        
        currentTrack = "";
    }
    
    /// <summary>
    /// Check if music is currently playing.
    /// </summary>
    public bool IsPlaying
    {
        get { return sequencer != null && sequencer.IsLoaded; }
    }

    /// <summary>
    /// Current playback position in seconds, or 0 if nothing is loaded.
    /// </summary>
    public double CurrentTime
    {
        get { return sequencer != null ? sequencer.CurrentTime : 0.0; }
    }

    public int ActiveVoiceCount => synthesizer != null ? synthesizer.ActiveVoiceCount : 0;
    public int MaximumPolyphonyCount => synthesizer != null ? synthesizer.MaximumPolyphony : maximumPolyphony;
    public int PeakActiveVoices => peakActiveVoices;
    public int AtCapNoteOns => atCapNoteOns;
    public int DroppedNoteOns => droppedNoteOns;
    public float VelocityCurveAmount => velocityCurveAmount;
    
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
                break;
            case MusicCommandType.ProgramChange:
                // MIDI Program Change: (channel, 0xC0, program, 0)
                synthesizer.ProcessMidiMessage(cmd.Channel, 0xC0, cmd.Program, 0);
                break;
            case MusicCommandType.ControllerChange:
                // MIDI Controller Change: (channel, 0xB0, controller, value)
                synthesizer.ProcessMidiMessage(cmd.Channel, 0xB0, cmd.Controller, cmd.Value);
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
                break;
            case MusicCommandType.ChannelAftertouch:
                // MIDI Channel Pressure: (channel, 0xD0, value, 0)
                synthesizer.ProcessMidiMessage(cmd.Channel, 0xD0, cmd.Value, 0);
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
