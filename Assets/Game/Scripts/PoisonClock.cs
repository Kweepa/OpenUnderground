/// <summary>
/// When the player's poison hurts, and how much.
/// </summary>
/// <remarks>
/// The original works the poison once a game minute - every third world tick, UW.EXE 0x2adee -
/// with damage equal to the level, typed poison, and then the level drops by one (0x2ae02-0x2ae3b).
/// A game minute of the original is a minute of real time, measured in DOSBox by the user. The
/// minute starts when the player is poisoned, and a stronger poisoning raises the level without
/// starting it again.
///
/// Ours, behind <see cref="PlayerInput.PartialResistances"/>: the same minute, but its damage
/// comes one point at a time, every 60 / level seconds, the last on the minute itself, so a minute
/// still does the level and a run from level L still does L(L+1)/2. Poison Resistance makes the
/// wait between two points three times longer, and the pulses start again with each minute, so a
/// minute does level / 3 rounded down, and levels 1 and 2 nothing.
/// </remarks>
public class PoisonClock
{
    /// <summary>A minute of the original's game time: three world ticks of 20 seconds.</summary>
    public const float MinuteSeconds = 60.0f;

    private float minuteLeft = MinuteSeconds;
    private float pulseLeft;
    private bool pulseArmed;
    private bool pulseResisted;
    private float intoMinute;
    private int pulsesThisMinute;

    /// <summary>The points already done in the minute under way, in pulses.</summary>
    public int PulsesThisMinute => pulsesThisMinute;

    /// <summary>Back to the start of a minute, as when the player is not poisoned.</summary>
    public void Reset()
    {
        minuteLeft = MinuteSeconds;
        pulseArmed = false;
        intoMinute = 0.0f;
        pulsesThisMinute = 0;
    }

    /// <summary>
    /// Runs the clock on by <paramref name="seconds"/> at the poison <paramref name="level"/>, more
    /// than 0. Returns the points of damage due now: the whole level at the end of a minute, or in
    /// pulses of one point when <paramref name="spread"/>. <paramref name="minuteOver"/> says that
    /// the level drops by one now.
    /// </summary>
    public int Step(float seconds, int level, bool spread, bool resisted, out bool minuteOver)
    {
        minuteLeft -= seconds;
        minuteOver = minuteLeft <= 0.0f;
        if (!spread)
        {
            if (minuteOver)
            {
                minuteLeft += MinuteSeconds;
                return level;
            }

            return 0;
        }

        if (pulseArmed && resisted != pulseResisted)
        {
            // Poison Resistance put on or taken off: the wait for the next point starts again
            // now, at the new pace. The minute and the points it has done stay as they are.
            pulseArmed = false;
        }

        if (!pulseArmed)
        {
            // From the start of the minute, which may lie a frame back.
            pulseLeft = PulseSeconds(level, resisted) - intoMinute;
            pulseResisted = resisted;
            pulseArmed = true;
            intoMinute = 0.0f;
        }

        int points = 0;
        int count = PulsesPerMinute(level, resisted);
        pulseLeft -= seconds;
        while (!minuteOver && pulseLeft <= 0.0f && pulsesThisMinute < count)
        {
            ++points;
            ++pulsesThisMinute;
            pulseLeft += PulseSeconds(level, resisted);
        }

        if (minuteOver)
        {
            // The last point of the minute falls on the minute itself. A minute that the level
            // rose in may be short of points; it gets its last one, not the ones it missed.
            if (pulsesThisMinute < count)
            {
                ++points;
            }

            minuteLeft += MinuteSeconds;
            intoMinute = MinuteSeconds - minuteLeft;
            pulsesThisMinute = 0;
            pulseArmed = false;
        }

        return points;
    }

    /// <summary>The points a minute does at <paramref name="level"/> when the damage is spread.</summary>
    public static int PulsesPerMinute(int level, bool resisted)
    {
        return resisted ? level / 3 : level;
    }

    /// <summary>The seconds between two points when the damage is spread.</summary>
    public static float PulseSeconds(int level, bool resisted)
    {
        return (resisted ? 3.0f : 1.0f) * MinuteSeconds / level;
    }

    /// <summary>
    /// What the poison would still do from <paramref name="level"/> on, minute by minute, if left
    /// alone: level + (level - 1) + ... + 1, or under Poison Resistance with the damage spread the
    /// sum of each minute's level / 3. Sleep pays it at once (UW.EXE 0x81f8f, at 0x82073-0x820c1).
    /// </summary>
    public static int Left(int level, bool spread, bool resisted)
    {
        int total = 0;
        for (int l = level; l > 0; --l)
        {
            total += spread ? PulsesPerMinute(l, resisted) : l;
        }

        return total;
    }
}
