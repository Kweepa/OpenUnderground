// The Yamaha YM3812 (OPL2) FM synthesis chip of the AdLib and Sound Blaster cards, for the
// original's FM music (AilOplSynth). Ported to C# from ymfm, the YM3812 path only: rhythm mode,
// the timers, CSM and the status register are left out, since the AIL driver that plays the music
// never uses them. The arithmetic is ymfm's, so the output is the same sample for sample.
//
// One change of shape: an instance is the Sound Blaster Pro's pair of chips, left and right. The
// driver writes the same values to both except the operators' levels (registers 0x40-0x55), and
// nothing in a chip but its output depends on those levels: the phases, the envelopes and which
// channels sound are the same on both. So one instance clocks them once and computes the output
// twice, with each side's level and each side's feedback memory, and once when the two agree. Its
// samples are those of two ymfm chips given the same writes.
//
// ymfm: https://github.com/aaronsgiles/ymfm, commit 81aec25c (src/ymfm.h, ymfm_fm.h, ymfm_fm.ipp,
// ymfm_opl.h, ymfm_opl.cpp). Its licence follows, as clause 1 asks.
//
// BSD 3-Clause License
//
// Copyright (c) 2021, Aaron Giles
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
// 1. Redistributions of source code must retain the above copyright notice, this
//    list of conditions and the following disclaimer.
//
// 2. Redistributions in binary form must reproduce the above copyright notice,
//    this list of conditions and the following disclaimer in the documentation
//    and/or other materials provided with the distribution.
//
// 3. Neither the name of the copyright holder nor the names of its
//    contributors may be used to endorse or promote products derived from
//    this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System.Runtime.CompilerServices;

namespace Ymfm
{
    /// <summary>
    /// A pair of YM3812s, left and right: nine two-operator channels, written through their
    /// registers, one sample for each side per call of <see cref="Generate"/> at <see cref="SampleRate"/>.
    /// </summary>
    public sealed class Ym3812
    {
        /// <summary>The clock of the AdLib and Sound Blaster cards, in Hz.</summary>
        public const uint DefaultClock = 3579545;

        public const int Channels = 9;
        public const int Operators = 18;

        // ymfm's fm_engine_base: the clock divider is 4 on OPL2 and each sample takes one
        // clock per operator.
        private const uint ClockPrescale = 4;
        private const uint AllChannels = (1u << Channels) - 1;
        private const uint RegMode = 0x04;

        private readonly OplRegisters regs = new OplRegisters();
        private readonly FmChannel[] channel = new FmChannel[Channels];
        private readonly FmOperator[] op = new FmOperator[Operators];

        private uint envCounter;
        private uint activeChannels = AllChannels;
        private uint modifiedChannels = AllChannels;
        private uint prepareCount;

        public Ym3812()
        {
            for (uint chnum = 0; chnum < Channels; chnum++)
            {
                channel[chnum] = new FmChannel(regs, chnum);
            }
            for (uint opnum = 0; opnum < Operators; opnum++)
            {
                op[opnum] = new FmOperator(regs, OplRegisters.OperatorOffset(opnum));
            }
            // the OPL2's fixed map: channel 0 has operators 0 and 3, and so on
            uint[] first = { 0, 1, 2, 6, 7, 8, 12, 13, 14 };
            for (int chnum = 0; chnum < Channels; chnum++)
            {
                channel[chnum].Assign(op[first[chnum]], op[first[chnum] + 3]);
            }
            Reset();
        }

        /// <summary>The rate the chip makes its samples at for a clock, in Hz: 49716 for the cards' clock.</summary>
        public static uint SampleRate(uint clock) => clock / (ClockPrescale * Operators);

        public void Reset()
        {
            regs.Reset();
            Write(RegMode, 0);
            foreach (FmChannel c in channel)
            {
                c.Reset();
            }
            foreach (FmOperator o in op)
            {
                o.Reset();
            }
        }

        /// <summary>Writes a register of both chips, as the address and data ports would.</summary>
        public void Write(uint regnum, byte data)
        {
            modifiedChannels = AllChannels;
            if (regnum == RegMode)
            {
                regs.Write(regnum, data, out _, out _);
                return;
            }
            if (regs.Write(regnum, data, out uint keyonChannel, out uint keyonOpmask) && keyonChannel < Channels)
            {
                channel[keyonChannel].KeyOnOff(keyonOpmask, FmOperator.KeyonNormal);
            }
        }

        /// <summary>Writes an operator's level register (0x40-0x55), one value for each chip.</summary>
        public void WriteLevels(uint regnum, byte left, byte right)
        {
            modifiedChannels = AllChannels;
            regs.WriteLevels(regnum, left, right);
        }

        /// <summary>The next sample of each chip, as its DAC (a YM3014) puts it out.</summary>
        public void Generate(out int left, out int right)
        {
            Clock();
            int outLeft = 0, outRight = 0;
            for (int chnum = 0; chnum < Channels; chnum++)
            {
                if (((activeChannels >> chnum) & 1) != 0)
                {
                    channel[chnum].Output2Op(1, 32767, ref outLeft, ref outRight);
                }
            }
            left = RoundtripFp(outLeft);
            right = outRight == outLeft ? left : RoundtripFp(outRight);
        }

        private void Clock()
        {
            if (modifiedChannels != 0 || prepareCount++ >= 4096)
            {
                activeChannels = 0;
                for (int chnum = 0; chnum < Channels; chnum++)
                {
                    if (channel[chnum].Prepare())
                    {
                        activeChannels |= 1u << chnum;
                    }
                }
                modifiedChannels = prepareCount = 0;
            }

            envCounter += 4;
            int lfoRawPm = regs.ClockNoiseAndLfo();
            for (int chnum = 0; chnum < Channels; chnum++)
            {
                channel[chnum].Clock(envCounter, lfoRawPm);
            }
        }

        // ymfm's roundtrip_fp: the value through the DAC's 10.3 floating point and back.
        private static int RoundtripFp(int value)
        {
            if (value < -32768)
            {
                return -32768;
            }
            if (value > 32767)
            {
                return 32767;
            }
            int scanvalue = value ^ (value >> 31);
            int exponent = 7 - CountLeadingZeros(unchecked((uint)(scanvalue << 17)));
            if (exponent < 1)
            {
                exponent = 1;
            }
            exponent -= 1;
            int mask = (1 << exponent) - 1;
            return value & ~mask;
        }

        internal static int CountLeadingZeros(uint value)
        {
            if (value == 0)
            {
                return 32;
            }
            int count = 0;
            while ((value & 0x80000000u) == 0)
            {
                value <<= 1;
                count++;
            }
            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint Bitfield(uint value, int start, int length = 1)
        {
            return (value >> start) & ((1u << length) - 1);
        }
    }

    /// <summary>
    /// ymfm's opdata_cache: what an operator needs from the registers, fetched once per change. A
    /// struct inside the operator, so that the hot path reads it without one more indirection.
    /// </summary>
    internal struct OpdataCache
    {
        public const uint PhaseStepDynamic = 1;
        public ushort[] Waveform;
        public uint PhaseStep;
        public uint TotalLevel;
        public uint TotalLevelRight;    // the right chip's: its own level register
        public uint BlockFreq;
        public uint Multiple;
        public uint EgSustain;
        public bool AmEnable;
        public bool PmEnable;
        public byte EgRateAttack;
        public byte EgRateDecay;
        public byte EgRateSustain;
        public byte EgRateRelease;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint EgRate(int state)
        {
            switch (state)
            {
            case FmOperator.EgAttack: return EgRateAttack;
            case FmOperator.EgDecay: return EgRateDecay;
            case FmOperator.EgSustain: return EgRateSustain;
            case FmOperator.EgRelease: return EgRateRelease;
            default: return 0x3f;       // EG_DEPRESS, never reached on the OPL2
            }
        }
    }

    /// <summary>
    /// ymfm's opl_registers_base&lt;2&gt;: the register array, the LFOs and the waveforms, with the right
    /// chip's level registers beside the array.
    /// </summary>
    internal sealed class OplRegisters
    {
        public const int WaveformLength = 0x400;
        private const int Waveforms = 4;

        private readonly byte[] regdata = new byte[0x100];
        private readonly byte[] levelRight = new byte[0x16];
        private static readonly ushort[][] waveform = BuildWaveforms();
        private ushort lfoAmCounter;
        private ushort lfoPmCounter;
        private uint noiseLfsr = 1;
        private byte lfoAm;

        private static readonly sbyte[] PmScale = { 8, 4, 0, -4, -8, -4, 0, 4 };
        private static readonly byte[] FnumToAtten = { 0, 24, 32, 37, 40, 43, 45, 47, 48, 50, 51, 52, 53, 54, 55, 56 };

        // the four waveforms, the same for every chip
        private static ushort[][] BuildWaveforms()
        {
            ushort[][] waveform = new ushort[Waveforms][];
            for (int w = 0; w < Waveforms; w++)
            {
                waveform[w] = new ushort[WaveformLength];
            }
            ushort[] wf0 = waveform[0];
            for (uint index = 0; index < WaveformLength; index++)
            {
                wf0[index] = (ushort)(FmTables.AbsSinAttenuation(index) | (Ym3812.Bitfield(index, 9) << 15));
            }
            ushort zeroval = wf0[0];
            for (uint index = 0; index < WaveformLength; index++)
            {
                waveform[1][index] = Ym3812.Bitfield(index, 9) != 0 ? zeroval : wf0[index];
                waveform[2][index] = (ushort)(wf0[index] & 0x7fff);
                waveform[3][index] = Ym3812.Bitfield(index, 8) != 0 ? zeroval : (ushort)(wf0[index] & 0x7fff);
            }
            return waveform;
        }

        public static uint OperatorOffset(uint opnum) => opnum + 2 * (opnum / 6);

        public void Reset()
        {
            System.Array.Clear(regdata, 0, regdata.Length);
            System.Array.Clear(levelRight, 0, levelRight.Length);
        }

        /// <summary>Stores a register of both chips; true with the channel and its operators when it was a key on or off.</summary>
        public bool Write(uint index, byte data, out uint channel, out uint opmask)
        {
            channel = 0;
            opmask = 0;
            // writes to the mode register with the high bit set ignore the low bits
            if (index == 0x04 && Ym3812.Bitfield(data, 7) != 0)
            {
                regdata[index] |= 0x80;
            }
            else
            {
                regdata[index] = data;
            }
            if (index >= 0x40 && index <= 0x55)
            {
                levelRight[index - 0x40] = data;
            }

            // 0xbd keys the rhythm instruments on, and rhythm mode is left out
            if ((index & 0xf0) == 0xb0)
            {
                channel = index & 0x0f;
                if (channel < 9)
                {
                    opmask = Ym3812.Bitfield(data, 5) != 0 ? 15u : 0u;
                    return true;
                }
            }
            return false;
        }

        public void WriteLevels(uint index, byte left, byte right)
        {
            regdata[index] = left;
            levelRight[index - 0x40] = right;
        }

        /// <summary>The noise and the two LFOs, a step; returns the PM LFO's value.</summary>
        public int ClockNoiseAndLfo()
        {
            noiseLfsr <<= 1;
            noiseLfsr |= Ym3812.Bitfield(noiseLfsr, 23) ^ Ym3812.Bitfield(noiseLfsr, 9) ^ Ym3812.Bitfield(noiseLfsr, 8) ^ Ym3812.Bitfield(noiseLfsr, 1);

            // the AM LFO has 210*64 steps, 3.7 Hz at the chip's rate
            uint amCounter = lfoAmCounter++;
            if (amCounter >= 210 * 64 - 1)
            {
                lfoAmCounter = 0;
            }
            int shift = 9 - 2 * (int)LfoAmDepth;
            lfoAm = (byte)(((amCounter < 105 * 64) ? amCounter : (210 * 64 + 63 - amCounter)) >> shift);

            // the PM LFO has 8192 steps, 6.1 Hz
            uint pmCounter = lfoPmCounter++;
            return PmScale[Ym3812.Bitfield(pmCounter, 10, 3)] >> (int)(LfoPmDepth ^ 1);
        }

        public uint LfoAmOffset => lfoAm;

        public void CacheOperatorData(uint choffs, uint opoffs, ref OpdataCache cache)
        {
            cache.Waveform = waveform[OpWaveform(opoffs) % Waveforms];

            uint blockFreq = cache.BlockFreq = ChBlockFreq(choffs);

            // the 4-bit keycode: the block and one bit of the frequency, chosen by note_select
            uint keycode = Ym3812.Bitfield(blockFreq, 10, 3) << 1;
            keycode |= Ym3812.Bitfield(blockFreq, 9 - (int)NoteSelect, 1);

            // the multiple as an x.1 value, 0 meaning 0.5: 0,1,2,3,4,5,6,7,8,9,10,10,12,12,15,15
            uint multiple = OpMultiple(opoffs);
            cache.Multiple = ((multiple & 0xe) | Ym3812.Bitfield(0xc2aa, (int)multiple)) * 2;
            if (cache.Multiple == 0)
            {
                cache.Multiple = 1;
            }

            cache.AmEnable = Byte(0x20, 7, 1, opoffs) != 0;
            cache.PmEnable = Byte(0x20, 6, 1, opoffs) != 0;
            cache.PhaseStep = !cache.PmEnable
                ? ComputePhaseStep(cache.BlockFreq, cache.Multiple, 0)
                : OpdataCache.PhaseStepDynamic;

            // the total level and the key scaling, for each chip
            cache.TotalLevel = TotalLevel(regdata[0x40 + opoffs], blockFreq);
            cache.TotalLevelRight = TotalLevel(levelRight[opoffs], blockFreq);

            // a 4-bit sustain level, 15 meaning 31
            cache.EgSustain = OpSustainLevel(opoffs);
            cache.EgSustain |= (cache.EgSustain + 1) & 0x10;
            cache.EgSustain <<= 5;

            uint ksrval = keycode >> (int)(2 * (OpKsr(opoffs) ^ 1));
            cache.EgRateAttack = EffectiveRate(OpAttackRate(opoffs) * 4, ksrval);
            cache.EgRateDecay = EffectiveRate(OpDecayRate(opoffs) * 4, ksrval);
            cache.EgRateSustain = OpEgSustain(opoffs) != 0 ? (byte)0 : EffectiveRate(OpReleaseRate(opoffs) * 4, ksrval);
            cache.EgRateRelease = EffectiveRate(OpReleaseRate(opoffs) * 4, ksrval);
        }

        // the total level, scaled by 8, with the key scale level added
        private static uint TotalLevel(byte register, uint blockFreq)
        {
            uint totalLevel = Ym3812.Bitfield(register, 0, 6) << 3;
            uint temp = Ym3812.Bitfield(register, 6, 2);
            uint ksl = Ym3812.Bitfield(temp, 1) | (Ym3812.Bitfield(temp, 0) << 1);
            if (ksl != 0)
            {
                totalLevel += KeyScaleAtten(Ym3812.Bitfield(blockFreq, 10, 3), Ym3812.Bitfield(blockFreq, 6, 4)) << (int)ksl;
            }
            return totalLevel;
        }

        public static uint ComputePhaseStep(in OpdataCache cache, int lfoRawPm)
        {
            return ComputePhaseStep(cache.BlockFreq, cache.Multiple, cache.PmEnable ? lfoRawPm : 0);
        }

        private static uint ComputePhaseStep(uint blockFreq, uint multiple, int lfoRawPm)
        {
            // the frequency number as a 12-bit fraction, moved by the PM LFO by its top three bits
            uint fnum = Ym3812.Bitfield(blockFreq, 0, 10) << 2;
            fnum = unchecked(fnum + (uint)((lfoRawPm * (int)Ym3812.Bitfield(blockFreq, 7, 3)) >> 1));
            fnum &= 0xfff;
            uint block = Ym3812.Bitfield(blockFreq, 10, 3);
            uint phaseStep = (fnum << (int)block) >> 2;
            return (phaseStep * multiple) >> 1;
        }

        private static uint KeyScaleAtten(uint block, uint fnum4msb)
        {
            int result = FnumToAtten[fnum4msb] - 8 * (int)(block ^ 7);
            return (uint)System.Math.Max(0, result);
        }

        private static byte EffectiveRate(uint rawrate, uint ksr)
        {
            return (byte)(rawrate == 0 ? 0 : System.Math.Min(rawrate + ksr, 63));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint Byte(uint offset, int start, int count, uint extraOffset = 0)
        {
            return Ym3812.Bitfield(regdata[offset + extraOffset], start, count);
        }

        // the system-wide registers
        private uint WaveformEnable => Byte(0x01, 5, 1);
        private uint NoteSelect => Byte(0x08, 6, 1);
        private uint LfoAmDepth => Byte(0xbd, 7, 1);
        private uint LfoPmDepth => Byte(0xbd, 6, 1);

        // the channel registers
        private uint ChBlockFreq(uint choffs) => (Byte(0xb0, 0, 5, choffs) << 8) | Byte(0xa0, 0, 8, choffs);
        public uint ChFeedback(uint choffs) => Byte(0xc0, 1, 3, choffs);
        public uint ChAlgorithm(uint choffs) => Byte(0xc0, 0, 1, choffs);

        // the operator registers
        private uint OpEgSustain(uint opoffs) => Byte(0x20, 5, 1, opoffs);
        private uint OpKsr(uint opoffs) => Byte(0x20, 4, 1, opoffs);
        private uint OpMultiple(uint opoffs) => Byte(0x20, 0, 4, opoffs);
        private uint OpAttackRate(uint opoffs) => Byte(0x60, 4, 4, opoffs);
        private uint OpDecayRate(uint opoffs) => Byte(0x60, 0, 4, opoffs);
        private uint OpSustainLevel(uint opoffs) => Byte(0x80, 4, 4, opoffs);
        private uint OpReleaseRate(uint opoffs) => Byte(0x80, 0, 4, opoffs);
        private uint OpWaveform(uint opoffs) => WaveformEnable != 0 ? Byte(0xe0, 0, 2, opoffs) : 0;
    }

    /// <summary>ymfm's fm_operator: one operator's phase and envelope.</summary>
    internal sealed class FmOperator
    {
        public const int EgDepress = 0;
        public const int EgAttack = 1;
        public const int EgDecay = 2;
        public const int EgSustain = 3;
        public const int EgRelease = 4;

        public const int KeyonNormal = 0;
        private const int KeyonCsm = 2;

        private const uint EgQuiet = 0x380;

        private readonly OplRegisters regs;
        private readonly uint opoffs;
        private uint choffs;
        private uint phase;
        private ushort envAttenuation = 0x3ff;
        private int envState = EgRelease;
        private byte keyState;
        private byte keyonLive;
        private OpdataCache cache;

        public FmOperator(OplRegisters regs, uint opoffs)
        {
            this.regs = regs;
            this.opoffs = opoffs;
        }

        public uint Choffs
        {
            set { choffs = value; }
        }

        public uint TotalLevel => cache.TotalLevel;

        public uint TotalLevelRight => cache.TotalLevelRight;

        public void Reset()
        {
            phase = 0;
            envAttenuation = 0x3ff;
            envState = EgRelease;
            keyState = 0;
            keyonLive = 0;
        }

        /// <summary>Fetches the registers and applies the key state; true while the operator sounds.</summary>
        public bool Prepare()
        {
            regs.CacheOperatorData(choffs, opoffs, ref cache);
            ClockKeystate(keyonLive != 0 ? 1u : 0u);
            keyonLive &= unchecked((byte)~(1 << KeyonCsm));
            return envState != EgRelease || envAttenuation < EgQuiet;
        }

        public void Clock(uint envCounter, int lfoRawPm)
        {
            // Released to silence, an operator stays so until its next key on, which starts its phase
            // again from 0: its envelope and phase steps would change nothing heard, and are skipped.
            if (envState == EgRelease && envAttenuation == 0x3ff)
            {
                return;
            }
            if (Ym3812.Bitfield(envCounter, 0, 2) == 0)
            {
                ClockEnvelope(envCounter >> 2);
            }
            ClockPhase(lfoRawPm);
        }

        public uint Phase => phase >> 10;

        /// <summary>The operator's output at a phase, with one chip's total level (TotalLevel or TotalLevelRight).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ComputeVolume(uint phase, uint amOffset, uint totalLevel)
        {
            if (envAttenuation > EgQuiet)
            {
                return 0;
            }
            uint sinAttenuation = cache.Waveform[phase & (OplRegisters.WaveformLength - 1)];
            uint envAttenuationNow = EnvelopeAttenuation(amOffset, totalLevel) << 2;
            int result = (int)FmTables.AttenuationToVolume((sinAttenuation & 0x7fff) + envAttenuationNow);
            return Ym3812.Bitfield(sinAttenuation, 15) != 0 ? -result : result;
        }

        public void KeyOnOff(uint on, int type)
        {
            keyonLive = (byte)((keyonLive & ~(1 << type)) | ((int)Ym3812.Bitfield(on, 0) << type));
        }

        private void StartAttack()
        {
            if (envState == EgAttack)
            {
                return;
            }
            envState = EgAttack;
            phase = 0;
            if (cache.EgRateAttack >= 62)
            {
                envAttenuation = 0;
            }
        }

        private void StartRelease()
        {
            if (envState >= EgRelease)
            {
                return;
            }
            envState = EgRelease;
        }

        private void ClockKeystate(uint keystate)
        {
            if ((keystate ^ keyState) != 0)
            {
                keyState = (byte)keystate;
                if (keystate != 0)
                {
                    StartAttack();
                }
                else
                {
                    StartRelease();
                }
            }
        }

        private void ClockEnvelope(uint envCounter)
        {
            if (envState == EgAttack && envAttenuation == 0)
            {
                envState = EgDecay;
            }
            if (envState == EgDecay && envAttenuation >= cache.EgSustain)
            {
                envState = EgSustain;
            }

            uint rate = cache.EgRate(envState);
            int rateShift = (int)(rate >> 2);
            envCounter <<= rateShift;
            if (Ym3812.Bitfield(envCounter, 0, 11) != 0)
            {
                return;
            }
            uint relevantBits = Ym3812.Bitfield(envCounter, rateShift <= 11 ? 11 : rateShift, 3);
            uint increment = FmTables.AttenuationIncrement(rate, relevantBits);

            if (envState == EgAttack)
            {
                // the attack is exponential: each step takes a share of what is left
                if (rate < 62)
                {
                    envAttenuation = unchecked((ushort)(envAttenuation + (((uint)~envAttenuation * increment) >> 4)));
                }
            }
            else
            {
                envAttenuation = (ushort)(envAttenuation + increment);
                if (envAttenuation >= 0x400)
                {
                    envAttenuation = 0x3ff;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClockPhase(int lfoRawPm)
        {
            uint phaseStep = cache.PhaseStep;
            if (phaseStep == OpdataCache.PhaseStepDynamic)
            {
                phaseStep = OplRegisters.ComputePhaseStep(in cache, lfoRawPm);
            }
            phase = unchecked(phase + phaseStep);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint EnvelopeAttenuation(uint amOffset, uint totalLevel)
        {
            uint result = envAttenuation;
            if (cache.AmEnable)
            {
                result += amOffset;
            }
            result += totalLevel;
            return System.Math.Min(result, 0x3ffu);
        }
    }

    /// <summary>
    /// ymfm's fm_channel, two operators, as the OPL2 connects them; with a feedback memory for each
    /// chip, since the modulator's output, which it holds, follows that chip's level.
    /// </summary>
    internal sealed class FmChannel
    {
        private readonly OplRegisters regs;
        private readonly uint choffs;
        private short feedback0, feedback1, feedbackIn;
        private short feedbackRight0, feedbackRight1, feedbackInRight;
        private uint feedbackShift;     // 10 - the feedback, or 0 for none
        private bool additive;
        private FmOperator op0;
        private FmOperator op1;

        public FmChannel(OplRegisters regs, uint choffs)
        {
            this.regs = regs;
            this.choffs = choffs;
        }

        public void Assign(FmOperator modulator, FmOperator carrier)
        {
            op0 = modulator;
            op1 = carrier;
            op0.Choffs = choffs;
            op1.Choffs = choffs;
        }

        public void Reset()
        {
            feedback0 = feedback1 = feedbackIn = 0;
            feedbackRight0 = feedbackRight1 = feedbackInRight = 0;
        }

        public void KeyOnOff(uint states, int type)
        {
            op0.KeyOnOff(Ym3812.Bitfield(states, 0), type);
            op1.KeyOnOff(Ym3812.Bitfield(states, 1), type);
        }

        public bool Prepare()
        {
            // the channel's registers, fetched with its operators' (every write prepares again)
            uint fb = regs.ChFeedback(choffs);
            feedbackShift = fb != 0 ? 10 - fb : 0;
            additive = Ym3812.Bitfield(regs.ChAlgorithm(choffs), 0) != 0;

            // both operators are prepared, whatever the first answers
            bool active0 = op0.Prepare();
            bool active1 = op1.Prepare();
            return active0 || active1;
        }

        public void Clock(uint envCounter, int lfoRawPm)
        {
            feedback0 = feedback1;
            feedback1 = feedbackIn;
            feedbackRight0 = feedbackRight1;
            feedbackRight1 = feedbackInRight;
            op0.Clock(envCounter, lfoRawPm);
            op1.Clock(envCounter, lfoRawPm);
        }

        /// <summary>
        /// The channel's sample on each chip, added to the two outputs. On the OPL2 the carrier is
        /// modulated by the modulator's previous output.
        /// </summary>
        public void Output2Op(int rshift, int clipmax, ref int outLeft, ref int outRight)
        {
            uint amOffset = regs.LfoAmOffset;
            int left = Output(rshift, clipmax, amOffset, op0.TotalLevel, op1.TotalLevel, feedback0, feedback1, out feedbackIn);
            if (op0.TotalLevelRight == op0.TotalLevel && op1.TotalLevelRight == op1.TotalLevel
                && feedbackRight0 == feedback0 && feedbackRight1 == feedback1)
            {
                // the two chips agree: the same sample
                feedbackInRight = feedbackIn;
                outLeft += left;
                outRight += left;
                return;
            }
            int right = Output(rshift, clipmax, amOffset, op0.TotalLevelRight, op1.TotalLevelRight, feedbackRight0, feedbackRight1, out feedbackInRight);
            outLeft += left;
            outRight += right;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Output(int rshift, int clipmax, uint amOffset, uint level0, uint level1, short fb0, short fb1, out short fbIn)
        {
            int opmod = 0;
            if (feedbackShift != 0)
            {
                opmod = (fb0 + fb1) >> (int)feedbackShift;
            }

            fbIn = (short)op0.ComputeVolume(unchecked(op0.Phase + (uint)opmod), amOffset, level0);

            int result;
            if (!additive)
            {
                // FM: the modulator into the carrier
                opmod = fb1 >> 1;
                result = op1.ComputeVolume(unchecked(op1.Phase + (uint)opmod), amOffset, level1) >> rshift;
            }
            else
            {
                // additive: the two summed
                result = fb1 >> rshift;
                result += op1.ComputeVolume(op1.Phase, amOffset, level1) >> rshift;
                int clipmin = -clipmax - 1;
                if (result < clipmin)
                {
                    result = clipmin;
                }
                else if (result > clipmax)
                {
                    result = clipmax;
                }
            }
            return result;
        }
    }

    /// <summary>ymfm's tables: the sine and power curves, and the envelope increments.</summary>
    internal static class FmTables
    {
        private static readonly ushort[] SinTable =
        {
            0x859,0x6c3,0x607,0x58b,0x52e,0x4e4,0x4a6,0x471,0x443,0x41a,0x3f5,0x3d3,0x3b5,0x398,0x37e,0x365,
            0x34e,0x339,0x324,0x311,0x2ff,0x2ed,0x2dc,0x2cd,0x2bd,0x2af,0x2a0,0x293,0x286,0x279,0x26d,0x261,
            0x256,0x24b,0x240,0x236,0x22c,0x222,0x218,0x20f,0x206,0x1fd,0x1f5,0x1ec,0x1e4,0x1dc,0x1d4,0x1cd,
            0x1c5,0x1be,0x1b7,0x1b0,0x1a9,0x1a2,0x19b,0x195,0x18f,0x188,0x182,0x17c,0x177,0x171,0x16b,0x166,
            0x160,0x15b,0x155,0x150,0x14b,0x146,0x141,0x13c,0x137,0x133,0x12e,0x129,0x125,0x121,0x11c,0x118,
            0x114,0x10f,0x10b,0x107,0x103,0x0ff,0x0fb,0x0f8,0x0f4,0x0f0,0x0ec,0x0e9,0x0e5,0x0e2,0x0de,0x0db,
            0x0d7,0x0d4,0x0d1,0x0cd,0x0ca,0x0c7,0x0c4,0x0c1,0x0be,0x0bb,0x0b8,0x0b5,0x0b2,0x0af,0x0ac,0x0a9,
            0x0a7,0x0a4,0x0a1,0x09f,0x09c,0x099,0x097,0x094,0x092,0x08f,0x08d,0x08a,0x088,0x086,0x083,0x081,
            0x07f,0x07d,0x07a,0x078,0x076,0x074,0x072,0x070,0x06e,0x06c,0x06a,0x068,0x066,0x064,0x062,0x060,
            0x05e,0x05c,0x05b,0x059,0x057,0x055,0x053,0x052,0x050,0x04e,0x04d,0x04b,0x04a,0x048,0x046,0x045,
            0x043,0x042,0x040,0x03f,0x03e,0x03c,0x03b,0x039,0x038,0x037,0x035,0x034,0x033,0x031,0x030,0x02f,
            0x02e,0x02d,0x02b,0x02a,0x029,0x028,0x027,0x026,0x025,0x024,0x023,0x022,0x021,0x020,0x01f,0x01e,
            0x01d,0x01c,0x01b,0x01a,0x019,0x018,0x017,0x017,0x016,0x015,0x014,0x014,0x013,0x012,0x011,0x011,
            0x010,0x00f,0x00f,0x00e,0x00d,0x00d,0x00c,0x00c,0x00b,0x00a,0x00a,0x009,0x009,0x008,0x008,0x007,
            0x007,0x007,0x006,0x006,0x005,0x005,0x005,0x004,0x004,0x004,0x003,0x003,0x003,0x002,0x002,0x002,
            0x002,0x001,0x001,0x001,0x001,0x001,0x001,0x001,0x000,0x000,0x000,0x000,0x000,0x000,0x000,0x000
        };

        // ymfm's s_power_table, each entry ((a | 0x400) << 2)
        private static readonly ushort[] PowerTable = BuildPowerTable(new ushort[]
        {
            0x3fa,0x3f5,0x3ef,0x3ea,0x3e4,0x3df,0x3da,0x3d4,0x3cf,0x3c9,0x3c4,0x3bf,0x3b9,0x3b4,0x3ae,0x3a9,
            0x3a4,0x39f,0x399,0x394,0x38f,0x38a,0x384,0x37f,0x37a,0x375,0x370,0x36a,0x365,0x360,0x35b,0x356,
            0x351,0x34c,0x347,0x342,0x33d,0x338,0x333,0x32e,0x329,0x324,0x31f,0x31a,0x315,0x310,0x30b,0x306,
            0x302,0x2fd,0x2f8,0x2f3,0x2ee,0x2e9,0x2e5,0x2e0,0x2db,0x2d6,0x2d2,0x2cd,0x2c8,0x2c4,0x2bf,0x2ba,
            0x2b5,0x2b1,0x2ac,0x2a8,0x2a3,0x29e,0x29a,0x295,0x291,0x28c,0x288,0x283,0x27f,0x27a,0x276,0x271,
            0x26d,0x268,0x264,0x25f,0x25b,0x257,0x252,0x24e,0x249,0x245,0x241,0x23c,0x238,0x234,0x230,0x22b,
            0x227,0x223,0x21e,0x21a,0x216,0x212,0x20e,0x209,0x205,0x201,0x1fd,0x1f9,0x1f5,0x1f0,0x1ec,0x1e8,
            0x1e4,0x1e0,0x1dc,0x1d8,0x1d4,0x1d0,0x1cc,0x1c8,0x1c4,0x1c0,0x1bc,0x1b8,0x1b4,0x1b0,0x1ac,0x1a8,
            0x1a4,0x1a0,0x19c,0x199,0x195,0x191,0x18d,0x189,0x185,0x181,0x17e,0x17a,0x176,0x172,0x16f,0x16b,
            0x167,0x163,0x160,0x15c,0x158,0x154,0x151,0x14d,0x149,0x146,0x142,0x13e,0x13b,0x137,0x134,0x130,
            0x12c,0x129,0x125,0x122,0x11e,0x11b,0x117,0x114,0x110,0x10c,0x109,0x106,0x102,0x0ff,0x0fb,0x0f8,
            0x0f4,0x0f1,0x0ed,0x0ea,0x0e7,0x0e3,0x0e0,0x0dc,0x0d9,0x0d6,0x0d2,0x0cf,0x0cc,0x0c8,0x0c5,0x0c2,
            0x0be,0x0bb,0x0b8,0x0b5,0x0b1,0x0ae,0x0ab,0x0a8,0x0a4,0x0a1,0x09e,0x09b,0x098,0x094,0x091,0x08e,
            0x08b,0x088,0x085,0x082,0x07e,0x07b,0x078,0x075,0x072,0x06f,0x06c,0x069,0x066,0x063,0x060,0x05d,
            0x05a,0x057,0x054,0x051,0x04e,0x04b,0x048,0x045,0x042,0x03f,0x03c,0x039,0x036,0x033,0x030,0x02d,
            0x02a,0x028,0x025,0x022,0x01f,0x01c,0x019,0x016,0x014,0x011,0x00e,0x00b,0x008,0x006,0x003,0x000
        });

        private static readonly uint[] IncrementTable =
        {
            0x00000000, 0x00000000, 0x10101010, 0x10101010,  // 0-3
            0x10101010, 0x10101010, 0x11101110, 0x11101110,  // 4-7
            0x10101010, 0x10111010, 0x11101110, 0x11111110,  // 8-11
            0x10101010, 0x10111010, 0x11101110, 0x11111110,  // 12-15
            0x10101010, 0x10111010, 0x11101110, 0x11111110,  // 16-19
            0x10101010, 0x10111010, 0x11101110, 0x11111110,  // 20-23
            0x10101010, 0x10111010, 0x11101110, 0x11111110,  // 24-27
            0x10101010, 0x10111010, 0x11101110, 0x11111110,  // 28-31
            0x10101010, 0x10111010, 0x11101110, 0x11111110,  // 32-35
            0x10101010, 0x10111010, 0x11101110, 0x11111110,  // 36-39
            0x10101010, 0x10111010, 0x11101110, 0x11111110,  // 40-43
            0x10101010, 0x10111010, 0x11101110, 0x11111110,  // 44-47
            0x11111111, 0x21112111, 0x21212121, 0x22212221,  // 48-51
            0x22222222, 0x42224222, 0x42424242, 0x44424442,  // 52-55
            0x44444444, 0x84448444, 0x84848484, 0x88848884,  // 56-59
            0x88888888, 0x88888888, 0x88888888, 0x88888888   // 60-63
        };

        private static ushort[] BuildPowerTable(ushort[] raw)
        {
            ushort[] table = new ushort[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                table[i] = (ushort)((raw[i] | 0x400) << 2);
            }
            return table;
        }

        /// <summary>The attenuation of a sine's quarter at a phase of 0-1023 (4.8 fixed point).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint AbsSinAttenuation(uint input)
        {
            if (Ym3812.Bitfield(input, 8) != 0)
            {
                input = ~input;
            }
            return SinTable[input & 0xff];
        }

        /// <summary>An attenuation (4.8 fixed point) to a linear 13-bit volume.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint AttenuationToVolume(uint input)
        {
            return (uint)(PowerTable[input & 0xff] >> (int)(input >> 8));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint AttenuationIncrement(uint rate, uint index)
        {
            return Ym3812.Bitfield(IncrementTable[rate], 4 * (int)index, 4);
        }
    }
}
