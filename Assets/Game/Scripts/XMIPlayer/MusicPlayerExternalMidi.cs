using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// The music played on an external MIDI synthesizer - the Munt MT-32 emulator, or a real MT-32 -
/// instead of the soundfont: what the original sent to a Roland MT-32, sent to a port of the system.
/// </summary>
/// <remarks>
/// The original plays its music through Miles' AIL driver MT32MPU.ADV. What it sends was read in the
/// driver's source (AIL 2.14, released as freeware in 2000), whose tables the game's own copy holds,
/// and checked against a raw MIDI capture of the original under DOSBox, which matches it byte for
/// byte: at start a reset and the driver's setup of parts, partial reserve and reverb; each track as
/// written, velocities untouched and every volume controller at 96% (UW.EXE 0x1f39c, load_new_music,
/// SOUND.C, asks for a relative volume of 0x60); the XMIDI controllers kept to the driver; a stop is
/// a Note Off for each note sounding and the sustain released, nothing else, so the synthesizer's
/// own release and reverb ring on. The music uses only the MT-32's built-in timbres: the custom ones
/// in SOUND/UW.MT are the sound effects'.
/// While a port is open a thread of its own drives the sequencer, every millisecond, in place of the
/// audio callback. With the MIDI device chosen and no port, or a port that stopped answering, the
/// music is silent (the user's rule, 15 November 2026: the soundfont plays only when it is the one
/// chosen); the synthesizer still runs the track, so that it carries on where it is when the port
/// comes back or a soundfont is chosen. The music volume and the cutscenes' ducking become the MT-32's
/// master volume, full volume being the original's level.
/// What no port says: Munt's Windows driver plays by itself when its window, mt32emu-qt, is closed,
/// and answers as before, so the music goes on there without the instruments the track had set until
/// the next track sets them again.
/// </remarks>
public partial class MusicPlayer
{
    /// <summary>The PlayerPrefs key: 1 when the music goes to an external MIDI synthesizer.</summary>
    public const string ExternalMidiKey = "Options_MusicExternalMidi";

    // UW.EXE 0x1f39c (load_new_music, SOUND.C): every track starts at relative volume 0x60, which the
    // driver applies to each volume controller as value * 96 / 100.
    private const int RelativeVolumePercent = 96;
    // The programs the driver sets at start on channels 2-9, its "Roland defaults".
    private static readonly byte[] DriverDefaultPrograms = { 68, 48, 95, 78, 41, 3, 110, 122 };

    // Held by whoever runs the sequencer and reads the command queue: the audio callback, or the MIDI thread.
    private readonly object sequencerLock = new object();
    private MidiOutPort externalPort;
    private Thread externalThread;
    // True while the MIDI thread runs the sequencer and the audio callback stays silent.
    private volatile bool externalOwnsSequencer;
    private volatile bool externalQuit;
    private volatile bool externalFailed;
    private volatile int externalWantedVolume = 100;
    // The synthesizer takes the music back on its next buffer, in the state the port had.
    private volatile bool replaySynthOnNextBuffer;
    // The MIDI device is chosen and no port plays: the synthesizer runs the track and is not heard.
    private volatile bool externalSilent;

    public static bool ExternalMidiChosen => PlayerPrefs.GetInt(ExternalMidiKey, 0) != 0;

    /// <summary>The port the music is playing on, or null.</summary>
    public string ExternalMidiPort => externalPort != null && !externalFailed ? externalPort.Name : null;

    // ---------------------------------------------------------------- main thread

    void OnAudioFilterRead(float[] data, int channels)
    {
        lock (sequencerLock)
        {
            if (externalOwnsSequencer)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            if (replaySynthOnNextBuffer)
            {
                replaySynthOnNextBuffer = false;
                if (synthesizer != null)
                {
                    ReplayChannelState();
                }
            }
            RenderSynth(data, channels);
            if (externalSilent)
            {
                Array.Clear(data, 0, data.Length);
            }
        }
    }

    private void InitializeExternalMidi()
    {
        if (ExternalMidiChosen)
        {
            StartExternalMidi(out _);
        }
        UpdateExternalSilence();
    }

    /// <summary>Silent while the MIDI device is chosen and no port plays the music.</summary>
    private void UpdateExternalSilence()
    {
        externalSilent = ExternalMidiChosen && (externalPort == null || externalFailed);
    }

    /// <summary>
    /// Sends the music to the external synthesizer: an MT-32 port first, else the first port that is not
    /// Windows' General MIDI synthesizer. True when one was opened, with its name; the track playing
    /// carries on there. False when there is none: while the device is chosen, the music is silent.
    /// </summary>
    public bool StartExternalMidi(out string portName)
    {
        portName = null;
        if (externalPort != null && !externalFailed)
        {
            portName = externalPort.Name;
            return true;
        }
        StopExternalMidi();

        List<string> names = MidiOutPort.PortNames();
        int device = MidiOutPort.ChoosePort(names);
        if (device < 0)
        {
            Debug.Log($"Music: no MIDI port for the external synthesizer among {names.Count}: {string.Join(", ", names)}");
            UpdateExternalSilence();
            return false;
        }
        MidiOutPort port = MidiOutPort.Open(device, names[device], out string error);
        if (port == null)
        {
            Debug.LogWarning($"Music: cannot open the MIDI port {names[device]}: {error}");
            UpdateExternalSilence();
            return false;
        }

        externalPort = port;
        externalFailed = false;
        externalQuit = false;
        externalWantedVolume = ExternalVolume();
        externalThread = new Thread(ExternalLoop) { IsBackground = true, Name = "External MIDI", Priority = System.Threading.ThreadPriority.AboveNormal };
        externalThread.Start();
        portName = port.Name;
        Debug.Log($"Music: playing on the MIDI port {port.Name}");
        UpdateExternalSilence();
        return true;
    }

    /// <summary>The music back to the soundfont, where it is, and the port closed after the MT-32 is reset.</summary>
    public void StopExternalMidi()
    {
        if (externalPort == null)
        {
            return;
        }
        externalQuit = true;
        if (externalThread != null && !externalThread.Join(2000))
        {
            Debug.LogWarning("Music: the MIDI thread did not stop in time");
        }
        externalThread = null;
        externalPort.Dispose();
        externalPort = null;
        externalFailed = false;
        UpdateExternalSilence();
    }

    /// <summary>
    /// The MT-32's master volume for the music volume and the ducking: full volume is 100, the
    /// original's level, and the curve is a square root, so the default 50% is 71 - about as loud as the
    /// soundfonts at 50% (the user's, 15 November 2026: linear, 50 was a little too soft).
    /// </summary>
    private int ExternalVolume()
    {
        float v = Mathf.Clamp01(masterVolumeScale * duckMultiplier);
        return Mathf.Clamp(Mathf.RoundToInt(100f * Mathf.Sqrt(v)), 0, 100);
    }

    private void UpdateExternalMidi()
    {
        UpdateExternalSilence();
        if (externalPort == null)
        {
            return;
        }
        externalWantedVolume = ExternalVolume();
        if (externalFailed)
        {
            string name = externalPort.Name;
            externalThread?.Join(1000);
            externalThread = null;
            externalPort.Dispose();
            externalPort = null;
            externalFailed = false;
            Debug.LogWarning($"Music: the MIDI port {name} stopped answering; the music is silent until it is back");
            UpdateExternalSilence();
            if (FindAnyObjectByType<Messages>() != null)
            {
                Messages.Add("The MIDI device stopped answering: no music until it is back.");
            }
        }
    }

    void OnApplicationQuit()
    {
        StopExternalMidi();
    }

    void OnDestroy()
    {
        StopExternalMidi();
    }

    // ---------------------------------------------------------------- the MIDI thread

    private void ExternalLoop()
    {
        MidiOutPort port = externalPort;
        MidiOutPort.FineTimer(true);
        try
        {
            if (!SendDriverStart(port))
            {
                externalFailed = true;
                return;
            }

            lock (sequencerLock)
            {
                // the synthesizer falls silent and the port takes the channels up where it left them
                SilenceSynthesizer();
                externalOwnsSequencer = true;
                if (!ReplayChannelStateToPort(port))
                {
                    externalFailed = true;
                }
            }

            int sentVolume = -1;
            int sampleRate = SynthesisRate;
            double carried = 0.0;
            Stopwatch clock = Stopwatch.StartNew();
            long last = clock.ElapsedTicks;
            while (!externalQuit && !externalFailed)
            {
                long now = clock.ElapsedTicks;
                double seconds = (now - last) / (double)Stopwatch.Frequency;
                last = now;
                bool ok = true;
                lock (sequencerLock)
                {
                    if (forgetHeldNotes)
                    {
                        // a stop: what sounds goes off before the next track's first note
                        forgetHeldNotes = false;
                        ok &= ReleaseHeldOnPort(port, true);
                    }
                    XMISequencer playing = sequencer;
                    if (playing != null)
                    {
                        carried += seconds * sampleRate;
                        int samples = (int)carried;
                        carried -= samples;
                        if (samples > 0)
                        {
                            playing.ProcessSamples(samples);
                        }
                    }
                    else
                    {
                        carried = 0.0;
                    }
                    while (commandQueue.TryDequeue(out MusicCommand cmd))
                    {
                        ok &= SendToPort(port, cmd);
                    }
                }
                int volume = externalWantedVolume;
                if (volume != sentVolume)
                {
                    // the MT-32's master volume, system area 10 00 16, 0 to 100
                    ok &= port.SendSysEx(RolandSysEx(0x10, 0x00, 0x16, (byte)volume));
                    sentVolume = volume;
                }
                if (!ok)
                {
                    externalFailed = true;
                    break;
                }
                Thread.Sleep(1);
            }

            lock (sequencerLock)
            {
                // the notes keep their place, for the synthesizer to sound them again; not heard if the
                // port failed while the device is still the choice
                ReleaseHeldOnPort(port, false);
                if (externalFailed)
                {
                    externalSilent = true;
                }
                externalOwnsSequencer = false;
                replaySynthOnNextBuffer = true;
            }
            if (!externalFailed)
            {
                SendDriverStop(port);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Music: the MIDI thread failed: {e}");
            externalFailed = true;
            lock (sequencerLock)
            {
                externalSilent = true;
                externalOwnsSequencer = false;
                replaySynthOnNextBuffer = true;
            }
        }
        finally
        {
            MidiOutPort.FineTimer(false);
        }
    }

    /// <summary>
    /// What MT32MPU.ADV sends when the game starts, in its order and with its pauses, which a real
    /// MT-32 needs after a reset: the reset, parts 1-8 on channels 2-9 and the rhythm on 10, the
    /// partial reserve, the reverb, then each channel's controllers, pitch bend and program.
    /// </summary>
    private bool SendDriverStart(MidiOutPort port)
    {
        bool ok = port.SendSysEx(RolandSysEx(0x7F, 0x00, 0x00, 0x01));
        Thread.Sleep(160);
        ok &= port.SendSysEx(RolandSysEx(0x10, 0x00, 0x0D, 1, 2, 3, 4, 5, 6, 7, 8, 9));
        Thread.Sleep(60);
        ok &= port.SendSysEx(RolandSysEx(0x10, 0x00, 0x04, 3, 4, 3, 4, 3, 4, 3, 4, 4));
        Thread.Sleep(60);
        ok &= port.SendSysEx(RolandSysEx(0x10, 0x00, 0x01, 0, 3, 2));
        Thread.Sleep(60);
        int[,] controllers = { { 7, 127 }, { 1, 0 }, { 10, 64 }, { 11, 127 }, { 64, 0 } };
        for (int c = 0; c < controllers.GetLength(0); c++)
        {
            for (int channel = 1; channel <= 9; channel++)
            {
                ok &= port.Send(0xB0 | channel, controllers[c, 0], controllers[c, 1]);
            }
        }
        Thread.Sleep(140);
        for (int channel = 1; channel <= 9; channel++)
        {
            ok &= port.Send(0xE0 | channel, 0x00, 0x40);
            if (channel <= DriverDefaultPrograms.Length)
            {
                ok &= port.Send(0xC0 | channel, DriverDefaultPrograms[channel - 1]);
            }
        }
        return ok;
    }

    /// <summary>What the driver sends when the game ends: the reset, the parts' channels, and the display cleared.</summary>
    private void SendDriverStop(MidiOutPort port)
    {
        port.SendSysEx(RolandSysEx(0x7F, 0x00, 0x00, 0x01));
        Thread.Sleep(160);
        port.SendSysEx(RolandSysEx(0x10, 0x00, 0x0D, 1, 2, 3, 4, 5, 6, 7, 8, 9));
        byte[] blank = new byte[20];
        for (int i = 0; i < blank.Length; i++)
        {
            blank[i] = 0x20;
        }
        port.SendSysEx(RolandSysEx(0x20, 0x00, 0x00, blank));
    }

    private void SilenceSynthesizer()
    {
        if (synthesizer == null)
        {
            return;
        }
        for (int channel = 0; channel < 16; channel++)
        {
            synthesizer.ProcessMidiMessage(channel, 0xB0, 123, 0);
            synthesizer.ProcessMidiMessage(channel, 0xB0, 120, 0);
        }
    }

    /// <summary>A command of the sequencer, as the driver would send it. False if the port refused it.</summary>
    private bool SendToPort(MidiOutPort port, MusicCommand cmd)
    {
        int channel = cmd.Channel & 0x0F;
        switch (cmd.Type)
        {
            case MusicCommandType.NoteOn:
                if (cmd.Velocity <= 0)
                {
                    heldNotes[channel, cmd.Note] = 0;
                    return port.Send(0x80 | channel, cmd.Note, 0);
                }
                heldNotes[channel, cmd.Note] = cmd.Velocity;
                return port.Send(0x90 | channel, cmd.Note, cmd.Velocity);
            case MusicCommandType.NoteOff:
                heldNotes[channel, cmd.Note] = 0;
                return port.Send(0x80 | channel, cmd.Note, 0);
            case MusicCommandType.ProgramChange:
                programs[channel] = cmd.Program;
                return port.Send(0xC0 | channel, cmd.Program);
            case MusicCommandType.ControllerChange:
                return SendControllerToPort(port, channel, cmd.Controller, cmd.Value);
            case MusicCommandType.PolyphonicAftertouch:
                return port.Send(0xA0 | channel, cmd.Note, cmd.Value);
            case MusicCommandType.PitchBend:
                pitchBends[channel] = cmd.Value & 0x3FFF;
                return port.Send(0xE0 | channel, cmd.Value & 0x7F, (cmd.Value >> 7) & 0x7F);
            case MusicCommandType.ChannelAftertouch:
                channelPressures[channel] = cmd.Value;
                return port.Send(0xD0 | channel, cmd.Value);
        }
        return true;
    }

    /// <summary>
    /// A controller as the driver handles it: the volume at the track's relative volume, the XMIDI
    /// controllers kept back, the Roland ones made into System Exclusive writes.
    /// </summary>
    private bool SendControllerToPort(MidiOutPort port, int channel, int controller, int value)
    {
        if (controller == 120 || controller == 123)
        {
            // the sequencer's own loop sends these; the driver ends notes with note-offs only
            return ReleaseHeldOnChannel(port, channel, true);
        }
        if ((controller >= 110 && controller <= 119) || (controller >= 32 && controller <= 46) || controller == 58)
        {
            // XMIDI's own controllers - loops, channel locks, bank select, System Exclusive queues,
            // rhythm timbres - none of which the driver passes on, and none the music needs
            return true;
        }
        RecordController(channel, controller, value);
        switch (controller)
        {
            case 7:
                return port.Send(0xB0 | channel, 7, value * RelativeVolumePercent / 100);
            case 59:
                return WritePatchOfChannel(port, channel, 6, value);
            case 60:
                // UW10.XMI sets the bender range of channel 5's patch to 0 this way
                return WritePatchOfChannel(port, channel, 4, value);
            case 61:
            case 62:
            case 63:
                // reverb mode, time and level, system area 10 00 01-03
                return port.SendSysEx(RolandSysEx(0x10, 0x00, controller - 60, (byte)value));
            default:
                return port.Send(0xB0 | channel, controller, value);
        }
    }

    /// <summary>
    /// The driver's write to the patch memory of the channel's current program - its reverb switch or
    /// bender range - followed by the program again, so that the part takes it.
    /// </summary>
    private bool WritePatchOfChannel(MidiOutPort port, int channel, int offset, int value)
    {
        int program = programs[channel];
        if (program < 0)
        {
            return true;
        }
        int address = program * 8 + offset;
        bool ok = port.SendSysEx(RolandSysEx(0x05, (address >> 7) & 0x7F, address & 0x7F, (byte)value));
        return ok & port.Send(0xC0 | channel, program);
    }

    private bool ReleaseHeldOnChannel(MidiOutPort port, int channel, bool forget)
    {
        bool ok = true;
        for (int note = 0; note < 128; note++)
        {
            if (heldNotes[channel, note] > 0)
            {
                ok &= port.Send(0x80 | channel, note, 0);
                if (forget)
                {
                    heldNotes[channel, note] = 0;
                }
            }
        }
        return ok;
    }

    /// <summary>A stop as the driver makes it: a Note Off for every note sounding, and the sustain pedal released where it is down.</summary>
    private bool ReleaseHeldOnPort(MidiOutPort port, bool forget)
    {
        bool ok = true;
        for (int channel = 0; channel < 16; channel++)
        {
            ok &= ReleaseHeldOnChannel(port, channel, forget);
            if (controllerValues[channel, 64] >= 64 && controllerOrder[channel].Contains(64))
            {
                ok &= port.Send(0xB0 | channel, 64, 0);
                if (forget)
                {
                    RecordController(channel, 64, 0);
                }
            }
        }
        return ok;
    }

    /// <summary>The channels as the synthesizer had them, sent to the port: a track carries on when the music moves there.</summary>
    private bool ReplayChannelStateToPort(MidiOutPort port)
    {
        bool ok = true;
        for (int channel = 0; channel < 16; channel++)
        {
            foreach (int controller in controllerOrder[channel].ToArray())
            {
                if (controller != 59 && controller != 60)
                {
                    ok &= SendControllerToPort(port, channel, controller, controllerValues[channel, controller]);
                }
            }
            if (programs[channel] >= 0)
            {
                ok &= port.Send(0xC0 | channel, programs[channel]);
            }
            if (pitchBends[channel] >= 0)
            {
                ok &= port.Send(0xE0 | channel, pitchBends[channel] & 0x7F, (pitchBends[channel] >> 7) & 0x7F);
            }
            if (channelPressures[channel] >= 0)
            {
                ok &= port.Send(0xD0 | channel, channelPressures[channel]);
            }
            for (int note = 0; note < 128; note++)
            {
                if (heldNotes[channel, note] > 0)
                {
                    ok &= port.Send(0x90 | channel, note, heldNotes[channel, note]);
                }
            }
        }
        return ok;
    }

    /// <summary>A Roland data set (DT1) for the MT-32: address, data and checksum.</summary>
    public static byte[] RolandSysEx(int a0, int a1, int a2, params byte[] data)
    {
        byte[] message = new byte[10 + data.Length];
        message[0] = 0xF0;
        message[1] = 0x41;
        message[2] = 0x10;
        message[3] = 0x16;
        message[4] = 0x12;
        message[5] = (byte)a0;
        message[6] = (byte)a1;
        message[7] = (byte)a2;
        int sum = a0 + a1 + a2;
        for (int i = 0; i < data.Length; i++)
        {
            message[8 + i] = (byte)(data[i] & 0x7F);
            sum += data[i] & 0x7F;
        }
        message[8 + data.Length] = (byte)((128 - sum % 128) % 128);
        message[9 + data.Length] = 0xF7;
        return message;
    }
}
