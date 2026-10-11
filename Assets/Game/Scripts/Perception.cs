using UnityEngine;

/// <summary>
/// How a creature perceives the player, as UW.EXE 0x1a99b does it, on whole tiles.
/// </summary>
/// <remarks>
/// The player has a noise and a visibility, 0 to 15 each (PlayerObject.NoiseLevel and Visibility,
/// the two nibbles the original keeps in byte 0x1d of his critterStats row), and a creature has a
/// hearing and a sight, 0 to 15 each, the low and high nibble of byte 0x1e of its own row. The
/// hearing radius in tiles is noise * hearing / 16 and the sight radius visibility * sight / 16:
///
///   within half the hearing radius   sensed, whatever the creature faces
///   within the sight radius          seen, if the player is in the three octants in front of its
///                                    heading and nothing blocks the line from eye to eye
///   within twice the hearing radius  near: heard, but not where
///   otherwise                        lost
///
/// Two changes of ours, asked by the user: closed doors block the sight, which the original's line of
/// sight (0x17671) passes straight through (10 October 2026); and any noise at all is heard near from
/// the next tile, the hearing radius being at least 1 for a creature that hears (11 October 2026), so a
/// player creeping close is left to the Sneak roll of Critter.SneaksPastNearHearing() and not simply
/// unheard.
/// </remarks>
public static class Perception
{
    public enum EResult
    {
        Sensed,
        Near,
        Lost
    }

    /// <summary>
    /// The farthest a creature perceives, in tiles: the original's line of sight takes at most ten steps
    /// along its major axis (UW.EXE 0x17671), and its creatures do not think at all more than ten tiles
    /// from the player (0x1b138).
    /// </summary>
    public const int MaxTiles = 10;

    /// <summary>
    /// The octant of a tile delta, 0 to 7: 0 is +y, the remake's +z, and the numbers go clockwise as
    /// seen from above, +x being 2. The boundaries are the lines y = 2x, x = 2y and their mirrors, so
    /// the four axis octants are 53.1 degrees wide and the four diagonal ones 36.9 (UW.EXE 0x18302). A
    /// zero delta is octant 4.
    /// </summary>
    public static int Octant(int dx, int dy)
    {
        if (dy > 2 * dx)
        {
            if (dx > -2 * dy)
            {
                return dy > -2 * dx ? 0 : 7;
            }
            return dx > 2 * dy ? 5 : 6;
        }
        if (dx > -2 * dy)
        {
            return dx > 2 * dy ? 2 : 1;
        }
        return dy > -2 * dx ? 3 : 4;
    }

    /// <summary>
    /// The coarse heading of a yaw in degrees, 0 to 7, in the numbering of <see cref="Octant"/>: the
    /// original keeps three bits of a creature's 8-bit heading, so anything from 0 to 44 degrees is 0.
    /// </summary>
    public static int CoarseHeading(float yawDegrees)
    {
        float yaw = Mathf.Repeat(yawDegrees, 360.0f);
        return Mathf.Clamp((int)(yaw / 45.0f), 0, 7);
    }

    /// <summary>Whether a delta lies in the three octants in front of a coarse heading.</summary>
    public static bool InFront(int dx, int dy, int heading)
    {
        int rel = (Octant(dx, dy) - heading + 8) % 8;
        return rel is 0 or 1 or 7;
    }

    /// <summary>
    /// The hearing test, with the creature's hearing nibble: sensed within half the radius, near within
    /// twice it, the radius being at least 1 while the player makes any noise.
    /// </summary>
    public static EResult Hear(int noise, int hearing, int d2)
    {
        if (noise <= 0)
        {
            return EResult.Lost;
        }

        int a = hearing > 0 ? Mathf.Max(1, noise * hearing / 16) : 0;
        if (4 * d2 < a * a)
        {
            return EResult.Sensed;
        }

        return d2 < 4 * a * a ? EResult.Near : EResult.Lost;
    }

    /// <summary>The sight radius test, with the creature's sight nibble: the line of sight is the caller's.</summary>
    public static bool WithinSight(int visibility, int sight, int d2)
    {
        int b = visibility * sight / 16;
        return d2 <= b * b;
    }
}
