/// <summary>
/// A simple struct to define a command for our synthesizer.
/// We use this to safely pass data to the audio thread queue.
/// </summary>
public enum MusicCommandType { NoteOn, NoteOff, ProgramChange, ControllerChange, PolyphonicAftertouch, PitchBend, ChannelAftertouch }

public struct MusicCommand
{
    public MusicCommandType Type;
    public int Channel;
    
    // NoteOn/Off
    public int Note;
    public int Velocity;

    // ProgramChange
    public int Program;

    // ControllerChange
    public int Controller; // ADDED
    public int Value;      // ADDED
}
