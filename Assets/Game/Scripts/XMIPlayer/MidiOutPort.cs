using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// A MIDI output port of the system, for the music played on an external synthesizer: the Munt MT-32
/// emulator, or a real MT-32 behind a MIDI interface.
/// </summary>
/// <remarks>
/// Windows' own winmm.dll, called directly, so nothing is built or shipped with the game; under
/// Proton, Wine lists the Linux system's MIDI ports through the same calls. On any other system no
/// port is found. Every call is made under a lock, so the music thread and the main thread can
/// both send.
/// </remarks>
public sealed class MidiOutPort : IDisposable
{
    public readonly string Name;
    private IntPtr handle;
    private readonly object sendLock = new object();

    private MidiOutPort(IntPtr handle, string name)
    {
        this.handle = handle;
        Name = name;
    }

    /// <summary>The names of the output ports, in the system's order (a port's place is its device number).</summary>
    public static List<string> PortNames()
    {
        List<string> names = new List<string>();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            uint count = midiOutGetNumDevs();
            for (uint i = 0; i < count; i++)
            {
                MIDIOUTCAPS caps = new MIDIOUTCAPS();
                bool ok = midiOutGetDevCaps(new UIntPtr(i), ref caps, (uint)Marshal.SizeOf(typeof(MIDIOUTCAPS))) == 0;
                names.Add(ok ? caps.szPname ?? "" : "");
            }
        }
        catch (Exception)
        {
            // no winmm: no ports
        }
#endif
        return names;
    }

    /// <summary>
    /// True for a port that cannot be the MT-32: Windows' General MIDI synthesizer, which ignores the
    /// MT-32's messages and plays other instruments, and the ALSA loopback that Wine lists, which
    /// plays nothing.
    /// </summary>
    public static bool Excluded(string name)
    {
        return string.IsNullOrEmpty(name)
            || name.IndexOf("GS Wavetable", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Midi Through", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>True for a port that names itself an MT-32 or the Munt emulator, preferred over any other.</summary>
    public static bool LooksLikeMT32(string name)
    {
        return name.IndexOf("MT-32", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("MT32", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Munt", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>The device number of the port to play on, -1 for none: one that looks like an MT-32 first, else the first allowed.</summary>
    public static int ChoosePort(List<string> names)
    {
        int first = -1;
        for (int i = 0; i < names.Count; i++)
        {
            if (Excluded(names[i]))
            {
                continue;
            }
            if (LooksLikeMT32(names[i]))
            {
                return i;
            }
            if (first < 0)
            {
                first = i;
            }
        }
        return first;
    }

    /// <summary>Opens the port with this device number, or returns null and the reason.</summary>
    public static MidiOutPort Open(int device, string name, out string error)
    {
        error = null;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            uint result = midiOutOpen(out IntPtr h, (uint)device, IntPtr.Zero, IntPtr.Zero, 0);
            if (result == 0)
            {
                return new MidiOutPort(h, name);
            }
            error = $"midiOutOpen returned {result}";
        }
        catch (Exception e)
        {
            error = e.Message;
        }
#else
        error = "MIDI output is only available on Windows";
#endif
        return null;
    }

    public bool IsOpen => handle != IntPtr.Zero;

    /// <summary>A channel message of one or two data bytes. False if the port refused it.</summary>
    public bool Send(int status, int data1, int data2 = 0)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        lock (sendLock)
        {
            if (handle == IntPtr.Zero)
            {
                return false;
            }
            uint message = (uint)(status & 0xFF) | (uint)(data1 & 0x7F) << 8 | (uint)(data2 & 0x7F) << 16;
            return midiOutShortMsg(handle, message) == 0;
        }
#else
        return false;
#endif
    }

    /// <summary>A whole System Exclusive message, F0 to F7. Returns when the port has taken it; false if it refused it.</summary>
    public bool SendSysEx(byte[] message)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        lock (sendLock)
        {
            if (handle == IntPtr.Zero)
            {
                return false;
            }
            int headerSize = Marshal.SizeOf(typeof(MIDIHDR));
            IntPtr data = Marshal.AllocHGlobal(message.Length);
            IntPtr header = Marshal.AllocHGlobal(headerSize);
            try
            {
                Marshal.Copy(message, 0, data, message.Length);
                MIDIHDR h = new MIDIHDR { lpData = data, dwBufferLength = (uint)message.Length, dwBytesRecorded = (uint)message.Length };
                Marshal.StructureToPtr(h, header, false);
                if (midiOutPrepareHeader(handle, header, (uint)headerSize) != 0)
                {
                    return false;
                }
                bool sent = midiOutLongMsg(handle, header, (uint)headerSize) == 0;
                // the driver may send it after returning: the buffer is kept until it says it is done
                for (int waited = 0; sent && waited < 1000; waited++)
                {
                    MIDIHDR now = (MIDIHDR)Marshal.PtrToStructure(header, typeof(MIDIHDR));
                    if ((now.dwFlags & MHDR_DONE) != 0)
                    {
                        break;
                    }
                    Thread.Sleep(1);
                }
                midiOutUnprepareHeader(handle, header, (uint)headerSize);
                return sent;
            }
            finally
            {
                Marshal.FreeHGlobal(header);
                Marshal.FreeHGlobal(data);
            }
        }
#else
        return false;
#endif
    }

    public void Dispose()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        lock (sendLock)
        {
            if (handle != IntPtr.Zero)
            {
                midiOutReset(handle);
                midiOutClose(handle);
                handle = IntPtr.Zero;
            }
        }
#endif
    }

    /// <summary>
    /// Asks Windows for a 1 ms timer resolution while the music thread runs, so that its sleeps last
    /// 1 ms and not a 15.6 ms tick. Nothing elsewhere.
    /// </summary>
    public static void FineTimer(bool on)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            if (on)
            {
                timeBeginPeriod(1);
            }
            else
            {
                timeEndPeriod(1);
            }
        }
        catch (Exception)
        {
            // no winmm: the sleeps are coarser, nothing else changes
        }
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private const uint MHDR_DONE = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MIDIOUTCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public ushort wTechnology;
        public ushort wVoices;
        public ushort wNotes;
        public ushort wChannelMask;
        public uint dwSupport;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIDIHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public IntPtr lpNext;
        public IntPtr reserved;
        public uint dwOffset;
        // dwReserved[8], as plain fields: an array left null would not marshal
        public IntPtr r0, r1, r2, r3, r4, r5, r6, r7;
    }

    [DllImport("winmm.dll")]
    private static extern uint midiOutGetNumDevs();

    [DllImport("winmm.dll", EntryPoint = "midiOutGetDevCapsW", CharSet = CharSet.Unicode)]
    private static extern uint midiOutGetDevCaps(UIntPtr deviceId, ref MIDIOUTCAPS caps, uint size);

    [DllImport("winmm.dll")]
    private static extern uint midiOutOpen(out IntPtr handle, uint deviceId, IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern uint midiOutShortMsg(IntPtr handle, uint message);

    [DllImport("winmm.dll")]
    private static extern uint midiOutPrepareHeader(IntPtr handle, IntPtr header, uint size);

    [DllImport("winmm.dll")]
    private static extern uint midiOutLongMsg(IntPtr handle, IntPtr header, uint size);

    [DllImport("winmm.dll")]
    private static extern uint midiOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);

    [DllImport("winmm.dll")]
    private static extern uint midiOutReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern uint midiOutClose(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);
#endif
}
