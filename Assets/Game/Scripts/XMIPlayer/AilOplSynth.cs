using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// The original's music on an AdLib or Sound Blaster: the tracks' FM timbres from the player's own
/// SOUND/UW.AD, put on two emulated OPL2 chips (Ymfm.Ym3812, left and right, as on a Sound Blaster
/// Pro) by the rules of the AIL 2 driver the game loads for those cards.
/// </summary>
/// <remarks>
/// On every FM card the original loads the AWnn.XMI arrangement of a track and the timbres of
/// SOUND\UW.AD (UW.EXE 0x1f39c, load_new_music, SOUND.C, and 0x1f26e, init_timbres), and plays them
/// through John Miles's Audio Interface Library 2, whose AdLib driver is YAMAHA.INC with XMIDI.ASM
/// above it (AIL 2.14 source, freeware since 2000). This class ports what that driver does for the
/// Sound Blaster Pro (STEREO_3812): 16 slots for the notes on 9 voices of the chip, a voice taken
/// from the channel with most voices when they run out; the velocity through vel_graph; the
/// channel's volume, expression and velocity scaling the carrier's level, and the modulator's too
/// when the timbre is additive; the pan as two levels, one per chip; pitch bend over 12 semitones;
/// channel 10 from bank 127 at each timbre's own note; notes on channels 2-10 only. XMIDI's start
/// values come first (volume 127, pan 64, the Roland default programs on channels 2-9), and every
/// volume of the track is scaled to 96%, as the game asks (AIL_set_relative_volume 0x60, SOUND.C).
/// Where the game's own build of the driver, SBPFM.ADV (1991), differs from that source, it is
/// followed: the voices are handed out from the first free one, with no rover, and nothing is
/// taken while nine slots or fewer are active (AssignVoice(), UpdatePriority()). Measured against a
/// recording of the original on a Sound Blaster Pro, a reference renderer that follows these rules
/// matched it within half a dB per instrument.
/// </remarks>
public sealed class AilOplSynth : IMusicSynth
{
    // YAMAHA.INC's sizes and constants
    private const int NumVoices = 9;
    private const int NumSlots = 16;
    private const int NumChans = 16;
    private const int Free = 0;
    private const int KeyOn = 1;
    private const int UAvekm = 0x80;
    private const int UKsltl = 0x40;
    private const int UAdsr = 0x20;
    private const int UWs = 0x10;
    private const int UFbc = 0x08;
    private const int UFreq = 0x01;
    private const int UAllRegs = 0xF9;
    private const int DefPitchRange = 12;
    private const int BnkSize = 14;
    private const int RhythmChannel = 9;
    private const int MinTrueChannel = 1;   // channel 2, 0-based
    private const int MaxRecChannel = 9;    // channel 10, 0-based

    // the controllers the driver reads
    private const int Modulation = 1;
    private const int PartVolume = 7;
    private const int Panpot = 10;
    private const int Expression = 11;
    private const int Sustain = 64;
    private const int VoiceProtect = 112;
    private const int PatchBankSel = 114;
    private const int AllSoundOff = 120;
    private const int ResetAllCtrls = 121;
    private const int AllNotesOff = 123;

    /// <summary>The volume the game gives its music, in percent (AIL_set_relative_volume, SOUND.C).</summary>
    private const int RelativeVolume = 0x60;

    /// <summary>The cards' clock, and the rate the chips make their samples at (49716 Hz).</summary>
    private const uint ChipClock = Ymfm.Ym3812.DefaultClock;

    /// <summary>
    /// From the chips' 16-bit samples to the synthesizer's floats: 9 dB over their full scale. Measured,
    /// 7 dB made the music about as loud as with the MT-32 soundfont at the same volume (the mean of six
    /// tracks, each 3 to 11 dB softer at full scale); by ear the user found it still a little soft and
    /// asked for 2 dB more (22 November 2026). The loudest peak of the twelve tracks is then about 1 dB
    /// over full scale at full volume, under it at the default half. MusicPlayer.OplTrackGain() raises
    /// the title theme further.
    /// </summary>
    private const float OutputGain = 2.82f / 32768f;

    private static readonly ushort[] FreqTable =
    {
        0x02b2,0x02b4,0x02b7,0x02b9,0x02bc,0x02be,0x02c1,0x02c3,0x02c6,0x02c9,0x02cb,0x02ce,0x02d0,0x02d3,0x02d6,0x02d8,
        0x02db,0x02dd,0x02e0,0x02e3,0x02e5,0x02e8,0x02eb,0x02ed,0x02f0,0x02f3,0x02f6,0x02f8,0x02fb,0x02fe,0x0301,0x0303,
        0x0306,0x0309,0x030c,0x030f,0x0311,0x0314,0x0317,0x031a,0x031d,0x0320,0x0323,0x0326,0x0329,0x032b,0x032e,0x0331,
        0x0334,0x0337,0x033a,0x033d,0x0340,0x0343,0x0346,0x0349,0x034c,0x034f,0x0352,0x0356,0x0359,0x035c,0x035f,0x0362,
        0x0365,0x0368,0x036b,0x036f,0x0372,0x0375,0x0378,0x037b,0x037f,0x0382,0x0385,0x0388,0x038c,0x038f,0x0392,0x0395,
        0x0399,0x039c,0x039f,0x03a3,0x03a6,0x03a9,0x03ad,0x03b0,0x03b4,0x03b7,0x03bb,0x03be,0x03c1,0x03c5,0x03c8,0x03cc,
        0x03cf,0x03d3,0x03d7,0x03da,0x03de,0x03e1,0x03e5,0x03e8,0x03ec,0x03f0,0x03f3,0x03f7,0x03fb,0x03fe,0xfe01,0xfe03,
        0xfe05,0xfe07,0xfe08,0xfe0a,0xfe0c,0xfe0e,0xfe10,0xfe12,0xfe14,0xfe16,0xfe18,0xfe1a,0xfe1c,0xfe1e,0xfe20,0xfe21,
        0xfe23,0xfe25,0xfe27,0xfe29,0xfe2b,0xfe2d,0xfe2f,0xfe31,0xfe34,0xfe36,0xfe38,0xfe3a,0xfe3c,0xfe3e,0xfe40,0xfe42,
        0xfe44,0xfe46,0xfe48,0xfe4a,0xfe4c,0xfe4f,0xfe51,0xfe53,0xfe55,0xfe57,0xfe59,0xfe5c,0xfe5e,0xfe60,0xfe62,0xfe64,
        0xfe67,0xfe69,0xfe6b,0xfe6d,0xfe6f,0xfe72,0xfe74,0xfe76,0xfe79,0xfe7b,0xfe7d,0xfe7f,0xfe82,0xfe84,0xfe86,0xfe89,
        0xfe8b,0xfe8d,0xfe90,0xfe92,0xfe95,0xfe97,0xfe99,0xfe9c,0xfe9e,0xfea1,0xfea3,0xfea5,0xfea8,0xfeaa,0xfead,0xfeaf
    };

    // a velocity of 0-127, by eighths, to 82-127: "to reduce perceived playback noise"
    private static readonly byte[] VelGraph = { 82, 85, 88, 91, 94, 97, 100, 103, 106, 109, 112, 115, 118, 121, 124, 127 };

    // the pan to one side's level: full from the centre outwards, falling to 0 at the far other side
    private static readonly byte[] PanGraph = BuildPanGraph();

    // each voice's two operators, and each operator's register offset
    private static readonly byte[] Op0 = { 0, 1, 2, 6, 7, 8, 12, 13, 14 };
    private static readonly byte[] Op1 = { 3, 4, 5, 9, 10, 11, 15, 16, 17 };
    private static readonly byte[] OpIndex = { 0, 1, 2, 3, 4, 5, 8, 9, 10, 11, 12, 13, 16, 17, 18, 19, 20, 21 };

    // XMIDI's start values: the Roland default programs on channels 2-9
    private static readonly int[] PrgDefault = { 68, 48, 95, 78, 41, 3, 110, 122 };

    // the Sound Blaster Pro's two chips, left and right, clocked as one (Ym3812.cs)
    private readonly Ymfm.Ym3812 chips = new Ymfm.Ym3812();
    private readonly object gate = new object();

    // UW.AD's timbres, by bank * 256 + patch: the 14-byte .BNK definitions, size word first
    private readonly Dictionary<int, byte[]> timbres = new Dictionary<int, byte[]>();

    // the channels
    private readonly int[] midiVol = new int[NumChans];
    private readonly int[] midiExpress = new int[NumChans];
    private readonly int[] midiPan = new int[NumChans];
    private readonly int[] midiMod = new int[NumChans];
    private readonly int[] midiSus = new int[NumChans];
    private readonly int[] midiVprot = new int[NumChans];
    private readonly int[] midiBank = new int[NumChans];
    private readonly int[] midiProgram = new int[NumChans];
    private readonly byte[][] midiTimbre = new byte[NumChans][];
    private readonly int[] midiVoices = new int[NumChans];
    private readonly int[] midiPitchL = new int[NumChans];
    private readonly int[] midiPitchH = new int[NumChans];

    // the slots, one per note
    private readonly int[] sStatus = new int[NumSlots];
    private readonly int[] sChannel = new int[NumSlots];
    private readonly int[] sKeynum = new int[NumSlots];
    private readonly int[] sNote = new int[NumSlots];
    private readonly int[] sTranspose = new int[NumSlots];
    private readonly int[] sVelocity = new int[NumSlots];
    private readonly int[] sSustain = new int[NumSlots];
    private readonly int[] sVoice = new int[NumSlots];
    private readonly int[] sUpdate = new int[NumSlots];
    private readonly int[] sBlock = new int[NumSlots];
    private readonly int[] sKbfShadow = new int[NumSlots];
    private readonly int[] sPriority = new int[NumSlots];
    private readonly int[] sVPriority = new int[NumSlots];
    private readonly byte[][] sTimbre = new byte[NumSlots][];

    // the voices: the channel each plays, or -1
    private readonly int[] vChannel = new int[NumVoices];

    // From the chips' rate to the output's: the last two samples of each chip, and how far the output
    // has gone past the first, in units of 1 / (clock * output rate) seconds, so that a chip sample is
    // 72 * output rate units and an output sample clock units, with no drift.
    private readonly long chipSampleUnits;
    private long position;
    private int previousLeft;
    private int previousRight;
    private int currentLeft;
    private int currentRight;

    /// <summary>The music synthesizer, or null when the timbres cannot be read.</summary>
    public static AilOplSynth Create(string timbreFile, int outputRate, out string error)
    {
        error = null;
        try
        {
            return new AilOplSynth(File.ReadAllBytes(timbreFile), outputRate);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
        {
            error = e.Message;
            return null;
        }
    }

    public AilOplSynth(byte[] globalTimbreLibrary, int outputRate)
    {
        ReadTimbres(globalTimbreLibrary);
        chipSampleUnits = 72L * outputRate;
        position = chipSampleUnits;   // the first output sample waits for the chips' first

        ResetSynth();
        InitSynth();
        InitXmidi();
    }

    public int ActiveVoiceCount
    {
        get
        {
            int count = 0;
            for (int v = 0; v < NumVoices; v++)
            {
                if (vChannel[v] != -1)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public int MaximumPolyphony => NumVoices;

    // ---------------------------------------------------------------- UW.AD

    /// <summary>
    /// A Global Timbre Library: entries of patch, bank and a 32-bit offset, ended by 0xFFFF; at each
    /// offset a timbre whose first word is its size, 14 for a two-operator .BNK timbre (bank 1 holds
    /// the sound effects' TVFX timbres, which the music never asks for).
    /// </summary>
    private void ReadTimbres(byte[] gtl)
    {
        for (int p = 0; p + 6 <= gtl.Length; p += 6)
        {
            int patch = gtl[p];
            int bank = gtl[p + 1];
            if (patch == 0xff && bank == 0xff)
            {
                return;
            }
            int offset = gtl[p + 2] | (gtl[p + 3] << 8) | (gtl[p + 4] << 16) | (gtl[p + 5] << 24);
            if (offset < 0 || offset + 2 > gtl.Length)
            {
                throw new InvalidDataException($"timbre {bank}:{patch} out of the file");
            }
            int size = gtl[offset] | (gtl[offset + 1] << 8);
            if (size == BnkSize && offset + BnkSize <= gtl.Length)
            {
                byte[] t = new byte[BnkSize];
                Array.Copy(gtl, offset, t, 0, BnkSize);
                timbres[(bank << 8) | patch] = t;
            }
        }
        throw new InvalidDataException("no end to the timbre list");
    }

    private byte[] IndexTimbre(int bank, int patch)
    {
        return timbres.TryGetValue((bank << 8) | patch, out byte[] t) ? t : null;
    }

    // ---------------------------------------------------------------- start

    /// <summary>reset_synth: the chips' registers to the driver's start values.</summary>
    private void ResetSynth()
    {
        for (uint reg = 0x01; reg <= 0xf5; reg++)
        {
            UpdateReg(reg, InitialRegister(reg));
        }
    }

    /// <summary>array0_init: what reset_synth writes to each register.</summary>
    private static byte InitialRegister(uint reg)
    {
        if (reg >= 0x20 && reg <= 0x35)
        {
            return 1;       // multiple 1
        }
        if (reg >= 0x40 && reg <= 0x55)
        {
            return 63;      // silent
        }
        if (reg >= 0x60 && reg <= 0x75)
        {
            return 255;     // fastest attack and decay
        }
        if (reg >= 0x80 && reg <= 0x95)
        {
            return 15;      // fastest release
        }
        switch (reg)
        {
        case 0x01: return 0x20;     // waveform select on
        case 0x04: return 0x60;     // both timers masked
        case 0xbd: return 0xc0;     // deep AM and vibrato, no rhythm mode
        default: return 0;
        }
    }

    /// <summary>init_synth: every channel without a timbre, every slot free, every voice unassigned.</summary>
    private void InitSynth()
    {
        for (int ch = 0; ch < NumChans; ch++)
        {
            midiTimbre[ch] = null;
            midiVoices[ch] = 0;
            midiProgram[ch] = -1;
            midiBank[ch] = 0;
            midiVol[ch] = 127;
            midiExpress[ch] = 127;
            midiPan[ch] = 64;
            midiPitchL[ch] = 0x00;
            midiPitchH[ch] = 0x40;
        }
        for (int si = 0; si < NumSlots; si++)
        {
            sStatus[si] = Free;
            sVoice[si] = -1;
        }
        for (int v = 0; v < NumVoices; v++)
        {
            vChannel[v] = -1;
        }
    }

    /// <summary>XMIDI's start: the controllers' default values, the pitch wheel centred, the Roland default programs.</summary>
    private void InitXmidi()
    {
        int[] controllers = { PartVolume, Modulation, Panpot, Expression, Sustain, PatchBankSel, 110, 111, VoiceProtect };
        int[] defaults = { 127, 0, 64, 127, 0, 0, 0, 0, 0 };
        for (int c = 0; c < controllers.Length; c++)
        {
            for (int ch = MinTrueChannel; ch <= MaxRecChannel; ch++)
            {
                SendMidiMessage(0xB0 | ch, controllers[c], defaults[c]);
            }
        }
        for (int ch = MinTrueChannel; ch <= MaxRecChannel; ch++)
        {
            SendMidiMessage(0xE0 | ch, 0x00, 0x40);
            if (ch - MinTrueChannel < PrgDefault.Length)
            {
                SendMidiMessage(0xC0 | ch, PrgDefault[ch - MinTrueChannel], 0);
            }
        }
    }

    // ---------------------------------------------------------------- the messages

    public void ProcessMidiMessage(int channel, int command, int data1, int data2)
    {
        lock (gate)
        {
            int status = (command & 0xF0) | (channel & 0x0F);
            if ((command & 0xF0) == 0xB0)
            {
                Controller(channel & 0x0F, data1 & 0x7F, data2 & 0x7F);
            }
            else
            {
                SendMidiMessage(status, data1 & 0x7F, data2 & 0x7F);
            }
        }
    }

    public void NoteOn(int channel, int key, int velocity)
    {
        lock (gate)
        {
            SendMidiMessage(0x90 | (channel & 0x0F), key & 0x7F, velocity & 0x7F);
        }
    }

    public void NoteOff(int channel, int key)
    {
        lock (gate)
        {
            SendMidiMessage(0x80 | (channel & 0x0F), key & 0x7F, 0);
        }
    }

    /// <summary>
    /// A controller as XMIDI hands it on: its volume scaled by the game's relative volume; the remake's
    /// All Notes Off and All Sound Off kept apart from the driver's.
    /// </summary>
    private void Controller(int ch, int controller, int value)
    {
        if (controller == PartVolume && RelativeVolume != 100)
        {
            // XMIDI_control, XMIDI.ASM
            value = Math.Min(value * RelativeVolume / 100, 127);
        }
        else if (controller == AllNotesOff)
        {
            // MusicPlayer sends it when a track stops or loops, where XMIDI sends a Note Off for
            // every note it holds, by its key (flush_note_queue). The driver's own handler looks the
            // notes up by S_note, which on channel 10 is the timbre's note and not the key, and
            // would leave the drums on.
            ReleaseChannel(ch, true);
            return;
        }
        else if (controller == AllSoundOff)
        {
            // not the driver's: the remake's, when the music moves to an external synthesizer
            ReleaseChannel(ch, false);
            return;
        }
        SendMidiMessage(0xB0 | ch, controller, value);
    }

    private void ReleaseChannel(int ch, bool keepSustained)
    {
        for (int si = 0; si < NumSlots; si++)
        {
            if (sStatus[si] == KeyOn && sChannel[si] == ch)
            {
                if (keepSustained)
                {
                    NoteOffSlots(ch, sKeynum[si]);
                }
                else
                {
                    ReleaseVoice(si);
                    sStatus[si] = Free;
                }
            }
        }
    }

    /// <summary>send_MIDI_message: a channel message to the synthesizer.</summary>
    private void SendMidiMessage(int status, int d1, int d2)
    {
        int ch = status & 0x0F;
        switch (status & 0xF0)
        {
        case 0x90:
            if (ch < MinTrueChannel || ch > MaxRecChannel)
            {
                return;
            }
            if (d2 == 0)
            {
                NoteOffSlots(ch, d1);
            }
            else
            {
                NoteOnSlot(ch, d1, d2);
            }
            return;
        case 0x80:
            NoteOffSlots(ch, d1);
            return;
        case 0xC0:
            midiProgram[ch] = d1;
            midiTimbre[ch] = IndexTimbre(midiBank[ch], d1);
            return;
        case 0xE0:
            midiPitchL[ch] = d1;
            midiPitchH[ch] = d2;
            FlagUpdates(ch, UFreq);
            return;
        case 0xB0:
            break;
        default:
            return;
        }

        switch (d1)
        {
        case PatchBankSel:
            midiBank[ch] = d2;
            return;
        case VoiceProtect:
            midiVprot[ch] = d2;
            return;
        case Modulation:
            midiMod[ch] = d2;
            FlagUpdates(ch, UAvekm);
            return;
        case PartVolume:
            midiVol[ch] = d2;
            FlagUpdates(ch, UKsltl);
            return;
        case Expression:
            midiExpress[ch] = d2;
            FlagUpdates(ch, UKsltl);
            return;
        case Panpot:
            midiPan[ch] = d2;
            FlagUpdates(ch, UKsltl);
            return;
        case Sustain:
            midiSus[ch] = d2;
            if (d2 < 64)
            {
                ReleaseSustain(ch);
            }
            return;
        case ResetAllCtrls:
            midiSus[ch] = 0;
            ReleaseSustain(ch);
            midiMod[ch] = 0;
            midiExpress[ch] = 127;
            midiPitchL[ch] = 0x00;
            midiPitchH[ch] = 0x40;
            FlagUpdates(ch, UAvekm | UKsltl | UFreq);
            return;
        case AllNotesOff:
            for (int si = 0; si < NumSlots; si++)
            {
                if (sStatus[si] == KeyOn && sChannel[si] == ch)
                {
                    NoteOffSlots(ch, sNote[si]);
                }
            }
            return;
        }
    }

    /// <summary>Marks a part of every slot of a channel as changed and puts it on the chip.</summary>
    private void FlagUpdates(int ch, int flags)
    {
        for (int si = 0; si < NumSlots; si++)
        {
            if (sStatus[si] != Free && sChannel[si] == ch)
            {
                sUpdate[si] |= flags;
                UpdateVoice(si);
            }
        }
    }

    /// <summary>release_sustain: the notes the pedal held, released.</summary>
    private void ReleaseSustain(int ch)
    {
        for (int si = 0; si < NumSlots; si++)
        {
            if (sStatus[si] != Free && sChannel[si] == ch && sSustain[si] != 0)
            {
                NoteOffSlots(ch, sNote[si]);
            }
        }
    }

    /// <summary>note_off: every slot playing the key on the channel, released, or held by the pedal.</summary>
    private void NoteOffSlots(int ch, int note)
    {
        for (int si = 0; si < NumSlots; si++)
        {
            if (sStatus[si] != KeyOn || sKeynum[si] != note || sChannel[si] != ch)
            {
                continue;
            }
            if (midiSus[ch] >= 64)
            {
                sSustain[si] = 1;
                continue;
            }
            ReleaseVoice(si);
            sStatus[si] = Free;
        }
    }

    /// <summary>note_on: a free slot gets the note and the channel's timbre, then a voice.</summary>
    private void NoteOnSlot(int ch, int note, int velocity)
    {
        byte[] timbre = ch == RhythmChannel ? IndexTimbre(127, note) : midiTimbre[ch];
        if (timbre == null)
        {
            return;
        }

        int si = 0;
        while (si < NumSlots && sStatus[si] != Free)
        {
            si++;
        }
        if (si == NumSlots)
        {
            return;
        }

        sChannel[si] = ch;
        sKeynum[si] = note;
        if (ch == RhythmChannel)
        {
            // a drum sounds at its timbre's own note
            sNote[si] = timbre[2];
            sTranspose[si] = 0;
        }
        else
        {
            sNote[si] = note;
            sTranspose[si] = (sbyte)timbre[2];
        }
        sVelocity[si] = VelGraph[velocity >> 3];
        sTimbre[si] = timbre;
        sStatus[si] = KeyOn;
        sSustain[si] = 0;

        BnkPhase(si);
        sVoice[si] = -1;
        AssignVoice(si);
    }

    /// <summary>BNK_phase: the slot set up from its timbre, every register to be written.</summary>
    private void BnkPhase(int si)
    {
        sBlock[si] = 0x20;          // key on
        sPriority[si] = 32767;      // a .BNK timbre's priority, the average
        sUpdate[si] = UAllRegs;
    }

    // ---------------------------------------------------------------- the voices

    /// <summary>
    /// assign_voice: the first free voice from voice 0, or one taken by priority. The game's own
    /// SBPFM.ADV (1991) has no rover, the "old" assignment the 1992 source keeps under CIRC_ASSIGN
    /// FALSE (SBPFM.ADV 0x209c-0x20e7): a note just released on a low voice has its release cut.
    /// </summary>
    private void AssignVoice(int si)
    {
        for (int voice = 0; voice < NumVoices; voice++)
        {
            if (vChannel[voice] == -1)
            {
                sVoice[si] = voice;
                midiVoices[sChannel[si]]++;
                vChannel[voice] = sChannel[si];
                sUpdate[si] = UAllRegs;
                UpdateVoice(si);
                return;
            }
        }
        UpdatePriority();
    }

    /// <summary>release_voice: the note keyed off and its voice freed; a .BNK slot is freed with it.</summary>
    private void ReleaseVoice(int si)
    {
        if (sVoice[si] == -1)
        {
            return;
        }
        sBlock[si] &= ~0x20;
        sUpdate[si] |= UFreq;
        UpdateVoice(si);

        midiVoices[sChannel[si]]--;
        vChannel[sVoice[si]] = -1;
        sVoice[si] = -1;
        sStatus[si] = Free;
    }

    /// <summary>
    /// update_priority: while a slot without a voice ranks as high as one with, or higher, the
    /// second's voice goes to the first and its note is dropped. A slot's priority is the average less
    /// the voices its channel holds, so a new note always gets a voice, taken from the channel that
    /// has most, its own included; the priorities are worked out once, at the start, ties go to the
    /// last slot, and nothing is done with nine slots active or fewer. Read in the game's
    /// SBPFM.ADV (1991, 0x2518-0x2607; the free of the robbed slot at 0x25c3): the 1992 source's rule,
    /// less its check for a priority of 0 and with the nine-slot return.
    /// </summary>
    private void UpdatePriority()
    {
        int slotCount = 0;
        for (int si = 0; si < NumSlots; si++)
        {
            if (sStatus[si] == Free)
            {
                continue;
            }
            slotCount++;
            int ch = sChannel[si];
            int priority = midiVprot[ch] >= 64 ? 0xffff : sPriority[si];
            priority -= midiVoices[ch];
            sVPriority[si] = priority < 0 ? 0 : priority;
        }
        if (slotCount <= NumVoices)
        {
            return;
        }

        while (true)
        {
            int high = 0, highSlot = -1;
            int low = 0xffff, lowSlot = -1;
            for (int si = 0; si < NumSlots; si++)
            {
                if (sStatus[si] == Free)
                {
                    continue;
                }
                int p = sVPriority[si];
                if (sVoice[si] == -1)
                {
                    if (p >= high)
                    {
                        high = p;
                        highSlot = si;
                    }
                }
                else if (p <= low)
                {
                    low = p;
                    lowSlot = si;
                }
            }

            if (high < low || highSlot < 0 || lowSlot < 0)
            {
                return;
            }

            int voice = sVoice[lowSlot];
            ReleaseVoice(lowSlot);

            sVoice[highSlot] = voice;
            midiVoices[sChannel[highSlot]]++;
            vChannel[voice] = sChannel[highSlot];
            sUpdate[highSlot] = UAllRegs;
            UpdateVoice(highSlot);

            if (--slotCount == 0)
            {
                return;
            }
        }
    }

    /// <summary>update_voice: the slot's changed parts written to its voice's registers.</summary>
    private void UpdateVoice(int si)
    {
        int voice = sVoice[si];
        if (voice == -1)
        {
            return;
        }
        byte[] t = sTimbre[si];
        int ch = sChannel[si];
        int modAvekm = t[3], modKsltl = t[4], modAd = t[5], modSr = t[6], modWs = t[7];
        int fbc = t[8], carAvekm = t[9], carKsltl = t[10], carAd = t[11], carSr = t[12], carWs = t[13];
        int v0 = Op0[voice];
        int v1 = Op1[voice];

        if ((sUpdate[si] & UAvekm) != 0)
        {
            int vibrato = midiMod[ch] >= 64 ? 0x40 : 0;
            WriteRegister(v0, 0x20, modAvekm | vibrato);
            WriteRegister(v1, 0x20, carAvekm | vibrato);
            sUpdate[si] &= ~UAvekm;
        }

        if ((sUpdate[si] & UKsltl) != 0)
        {
            // the channel's volume and expression and the note's velocity, then each side's share
            int vol = Scale127(midiVol[ch] * midiExpress[ch]);
            vol = Scale127(vol * sVelocity[si]);
            int lvol = Scale127(PanGraph[midiPan[ch]] * vol);
            int rvol = Scale127(PanGraph[127 - midiPan[ch]] * vol);

            // the carrier is always scaled, the modulator only when the timbre adds the two
            WriteLevel(v0, modKsltl, (fbc & 1) != 0, lvol, rvol);
            WriteLevel(v1, carKsltl, true, lvol, rvol);
            sUpdate[si] &= ~UKsltl;
        }

        if ((sUpdate[si] & UAdsr) != 0)
        {
            WriteRegister(v0, 0x60, modAd);
            WriteRegister(v1, 0x60, carAd);
            WriteRegister(v0, 0x80, modSr);
            WriteRegister(v1, 0x80, carSr);
            sUpdate[si] &= ~UAdsr;
        }

        if ((sUpdate[si] & UWs) != 0)
        {
            WriteRegister(v1, 0xe0, carWs);
            WriteRegister(v0, 0xe0, modWs);
            sUpdate[si] &= ~UWs;
        }

        if ((sUpdate[si] & UFbc) != 0)
        {
            SendByte(voice, 0xc0, fbc & 0x0f);
            sUpdate[si] &= ~UFbc;
        }

        if ((sUpdate[si] & UFreq) != 0)
        {
            if ((sBlock[si] & 0x20) == 0)
            {
                // key off at the frequency it had
                SendByte(voice, 0xb0, sKbfShadow[si] & 0xdf);
            }
            else
            {
                int fnum = FNumber(si);
                SendByte(voice, 0xa0, fnum & 0xff);
                int kbf = ((fnum >> 8) & 0xff) | sBlock[si];
                sKbfShadow[si] = kbf;
                SendByte(voice, 0xb0, kbf);
            }
            sUpdate[si] &= ~UFreq;
        }
    }

    /// <summary>An operator's level: the timbre's, scaled by each side's volume if asked, with its key scaling bits.</summary>
    private void WriteLevel(int op, int ksltl, bool scaled, int lvol, int rvol)
    {
        int level = ~ksltl & 0x3f;
        int l = level, r = level;
        if (scaled)
        {
            l = level * lvol / 127;
            r = level * rvol / 127;
        }
        int ksl = ksltl & 0xc0;
        StereoRegister(op, 0x40, (~l & 0x3f) | ksl, (~r & 0x3f) | ksl);
    }

    /// <summary>The F-number and block of the slot's note and its channel's pitch bend (the register pair's two bytes).</summary>
    private int FNumber(int si)
    {
        int ch = sChannel[si];
        int bend = ((midiPitchH[ch] << 7) | midiPitchL[ch]) - 0x2000;
        bend >>= 5;                     // +-0x100
        bend *= DefPitchRange;          // +-0xc00, in 1/256 of a semitone

        int note = sNote[si] + sTranspose[si];
        note -= 24;
        do
        {
            note += 12;
        }
        while (note < 0);
        note += 12;
        do
        {
            note -= 12;
        }
        while (note > 95);

        // in 1/16 of a semitone, rounded
        int ax = (short)(bend + (note << 8));
        ax += 8;
        ax >>= 4;

        ax -= 12 * 16;
        do
        {
            ax += 12 * 16;
        }
        while (ax < 0);
        ax += 12 * 16;
        do
        {
            ax -= 12 * 16;
        }
        while (ax > 96 * 16 - 1);

        int semitone = ax >> 4;
        int index = (semitone % 12) * 16 + (ax & 15);
        int f = (short)FreqTable[index];
        int block = semitone / 12 - 1;
        if (f < 0)
        {
            block++;
        }
        if (block < 0)
        {
            block++;
            f >>= 1;
        }
        int high = (((f >> 8) & 3) | (block << 2)) & 0xff;
        return (high << 8) | (f & 0xff);
    }

    /// <summary>(x * 2) / 256, about x / 127, rounded up unless 0.</summary>
    private static int Scale127(int x)
    {
        int a = ((x * 2) >> 8) & 0xff;
        return a == 0 ? 0 : a + 1;
    }

    private static byte[] BuildPanGraph()
    {
        byte[] graph = new byte[128];
        for (int i = 0; i < 64; i++)
        {
            graph[i] = (byte)(i * 2);
        }
        graph[63] = 127;
        for (int i = 64; i < 128; i++)
        {
            graph[i] = 127;
        }
        return graph;
    }

    // ---------------------------------------------------------------- the chips

    /// <summary>write_register: an operator's register, the same on both chips.</summary>
    private void WriteRegister(int op, int baseRegister, int value)
    {
        UpdateReg((uint)(OpIndex[op] + baseRegister), (byte)value);
    }

    /// <summary>send_byte: a voice's register, the same on both chips.</summary>
    private void SendByte(int voice, int baseRegister, int value)
    {
        UpdateReg((uint)(voice + baseRegister), (byte)value);
    }

    /// <summary>stereo_register: an operator's register, one value for the left chip and one for the right.</summary>
    private void StereoRegister(int op, int baseRegister, int leftValue, int rightValue)
    {
        Enqueue((uint)(OpIndex[op] + baseRegister), (byte)leftValue, (byte)rightValue, true);
    }

    private void UpdateReg(uint reg, byte value)
    {
        Enqueue(reg, value, value, false);
    }

    // The writes waiting for the chips. The driver writes a register in about 26 microseconds -
    // the address, a 3.3 us wait, the data, a 23 us wait (update_reg, YAMAHA.INC) - and the chips see
    // them one at a time, at that pace. It matters: a voice taken for a new note is keyed off and,
    // twelve writes later, on again, and the chips' envelope starts the new note's attack only from
    // a key off it has seen for at least one sample. Written all at once, between two samples, the
    // key off would be missed, and the new note would carry on the old one's envelope at its pitch.
    private const int QueueSize = 4096;
    private readonly uint[] queueRegister = new uint[QueueSize];
    private readonly byte[] queueLeft = new byte[QueueSize];
    private readonly byte[] queueRight = new byte[QueueSize];
    private readonly bool[] queueLevels = new bool[QueueSize];
    private int queueHead;
    private int queueCount;
    // the time left before the next write, in 1/1000 of a chip sample: one write every 1.31 samples
    private int writeWait;
    private const int WriteTime = 1313;     // 26.4 us at 49716 Hz

    private void Enqueue(uint reg, byte leftValue, byte rightValue, bool levels)
    {
        if (queueCount == QueueSize)
        {
            // never on the tracks: hundreds of notes at once
            ApplyWrite(queueHead);
            queueHead = (queueHead + 1) % QueueSize;
            queueCount--;
        }
        int i = (queueHead + queueCount) % QueueSize;
        queueRegister[i] = reg;
        queueLeft[i] = leftValue;
        queueRight[i] = rightValue;
        queueLevels[i] = levels;
        queueCount++;
    }

    private void ApplyWrite(int i)
    {
        if (queueLevels[i])
        {
            chips.WriteLevels(queueRegister[i], queueLeft[i], queueRight[i]);
        }
        else
        {
            chips.Write(queueRegister[i], queueLeft[i]);
        }
    }

    /// <summary>The writes whose time has come, before the chips' next sample.</summary>
    private void ApplyDueWrites()
    {
        writeWait -= 1000;
        while (queueCount > 0 && writeWait <= 0)
        {
            ApplyWrite(queueHead);
            queueHead = (queueHead + 1) % QueueSize;
            queueCount--;
            writeWait += WriteTime;
        }
        if (queueCount == 0 && writeWait < 0)
        {
            writeWait = 0;
        }
    }

    /// <summary>The two chips' output at the output rate, interpolated between their own samples.</summary>
    public void Render(Span<float> leftOut, Span<float> rightOut)
    {
        lock (gate)
        {
            for (int i = 0; i < leftOut.Length; i++)
            {
                while (position >= chipSampleUnits)
                {
                    position -= chipSampleUnits;
                    previousLeft = currentLeft;
                    previousRight = currentRight;
                    ApplyDueWrites();
                    chips.Generate(out currentLeft, out currentRight);
                }
                float frac = (float)position / chipSampleUnits;
                leftOut[i] = (previousLeft + (currentLeft - previousLeft) * frac) * OutputGain;
                rightOut[i] = (previousRight + (currentRight - previousRight) * frac) * OutputGain;
                position += ChipClock;
            }
        }
    }
}
