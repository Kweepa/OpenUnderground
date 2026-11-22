using System;

/// <summary>
/// What MusicPlayer plays the XMI tracks through: MeltySynth with a soundfont, or the original's
/// FM music on an emulated OPL2 (AilOplSynth). Both are driven from the audio thread only.
/// </summary>
public interface IMusicSynth
{
    /// <summary>A MIDI channel message: command is the status's high nibble (0xB0 for a controller).</summary>
    void ProcessMidiMessage(int channel, int command, int data1, int data2);

    void NoteOn(int channel, int key, int velocity);

    void NoteOff(int channel, int key);

    void Render(Span<float> left, Span<float> right);

    int ActiveVoiceCount { get; }

    int MaximumPolyphony { get; }
}

/// <summary>MeltySynth's synthesizer, with a soundfont.</summary>
public sealed class MeltySynthMusic : IMusicSynth
{
    private readonly MeltySynth.Synthesizer synthesizer;

    public MeltySynthMusic(MeltySynth.Synthesizer synthesizer)
    {
        this.synthesizer = synthesizer;
    }

    public void ProcessMidiMessage(int channel, int command, int data1, int data2) => synthesizer.ProcessMidiMessage(channel, command, data1, data2);

    public void NoteOn(int channel, int key, int velocity) => synthesizer.NoteOn(channel, key, velocity);

    public void NoteOff(int channel, int key) => synthesizer.NoteOff(channel, key);

    public void Render(Span<float> left, Span<float> right) => synthesizer.Render(left, right);

    public int ActiveVoiceCount => synthesizer.ActiveVoiceCount;

    public int MaximumPolyphony => synthesizer.MaximumPolyphony;
}
