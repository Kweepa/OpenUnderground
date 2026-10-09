using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A look made for you every few seconds: Search points out a hidden door in front of you or at
/// your side, without opening it.
/// </summary>
/// <remarks>
/// A hidden door is a special wall - a Decal - whose look trigger opens the tile behind it. In the
/// original you find one by looking at the right wall, and the look rolls your Search skill against
/// the difficulty the level designers wrote into that trigger (see Trigger.PassesSearchRoll). That
/// is still how a door opens here. What the original does not have, and this adds, is the looking:
/// asking a player to click every wall in the dungeon is an old mechanic that few have the patience
/// for, so every ten seconds a roll against the same difficulty, and harder than a look's (see
/// ReportChance), is made for the hidden walls you could be looking at, and a success names the
/// direction. The look that opens the door is still yours, and it rolls again, so a report can be
/// followed by a miss.
///
/// This is an addition, not a restoration: the original reads the Search skill in three places,
/// all on an action of the player's or a spell (UW.EXE 0x26e10, 0x83b3b and 0x34963), and none of
/// them on a timer.
/// </remarks>
public static class SecretDoorSearch
{
    /// <summary>Seconds between two rounds of rolls.</summary>
    public const float checkInterval = 10.0f;

    /// <summary>
    /// How far a hidden wall can be noticed, in tiles, counted as a square around the tile you
    /// stand on. That tile is included: a hidden wall is part of the tile in front of it, so when
    /// you stand with your back to the corridor and your nose to the wall, it is on your own tile.
    /// </summary>
    public const int searchRangeTiles = 3;

    /// <summary>
    /// How far from the way you face a wall can be and still be noticed, in degrees.
    /// </summary>
    /// <remarks>
    /// In front of you and at your sides, never behind: a search is something you do with your
    /// eyes. A little over ninety, so that a wall at your shoulder still counts when you have turned
    /// slightly away from it.
    /// </remarks>
    public const float maxAngleFromFacing = 100.0f;

    /// <summary>
    /// Seconds before the same door will be mentioned again.
    /// </summary>
    /// <remarks>
    /// Announcing each door once and never again would be tidier, but a single line can scroll
    /// past under combat messages and then the discovery is simply lost. Repeating on a delay
    /// costs nothing once the player has acted on it, since a door that has been opened stops
    /// being a candidate. This lives in memory only: after a reload every door is fair game
    /// again, which is the harmless direction for it to fail in.
    /// </remarks>
    public const float repeatMessageDelay = 60.0f;

    /// <summary>
    /// How far short of the target the line of sight ray stops, in metres.
    /// </summary>
    /// <remarks>
    /// A hidden door is part of a wall, so a ray aimed at it is blocked by the thing it is
    /// aiming for. Stopping short leaves the target's own surface out of the test while still
    /// catching anything genuinely in between - tiles are 3 metres across, so half a metre is
    /// nowhere near enough to see through a wall that is actually there.
    /// </remarks>
    private const float lineOfSightBackoff = 0.5f;

    /// <summary>How many links to follow looking for the trap behind a look trigger.</summary>
    private const int maxChainDepth = 8;

    private readonly struct Candidate
    {
        public readonly int objectIndex;
        public readonly Vector3 position;
        public readonly int tileDistance;
        public readonly int difficulty;

        public Candidate(int objectIndex, Vector3 position, int tileDistance, int difficulty)
        {
            this.objectIndex = objectIndex;
            this.position = position;
            this.tileDistance = tileDistance;
            this.difficulty = difficulty;
        }
    }

    // Time.time of the last message about each object index. Object indices are only unique
    // within a level, so the table is dropped whenever the level changes rather than keyed by
    // both - the entries are worthless across a level transition anyway.
    private static readonly Dictionary<int, float> lastReportedTime = new Dictionary<int, float>();
    private static int lastPolledLevel = -1;

    /// <summary>
    /// How long a freshly loaded level is left alone before the first roll, in seconds.
    /// </summary>
    /// <remarks>
    /// Loading a saved game builds the level from the original data first and puts the objects
    /// back afterwards, so for a moment every trigger reads as it was placed rather than as the
    /// player left it: a door opened an hour ago would be announced again. Waiting is the cheap
    /// way to be sure the save has finished talking. Two seconds is under the fade in, so nothing
    /// is lost by it.
    /// </remarks>
    private const float settleDelay = 2.0f;

    // Time.time before which no roll happens. Armed by Reset, which the level loader calls.
    private static float settledTime;

    private static readonly List<Candidate> candidates = new List<Candidate>();

    /// <summary>
    /// One round of Search rolls. Called on a timer from PlayerObject.Update.
    /// </summary>
    public static void Poll()
    {
        // Not while fighting. Searching a wall is something you do with your hands free, and a
        // line about a door somewhere to the side is noise when a creature is already swinging.
        // The caller holds its timer full during a fight, so the first round after one waits a
        // whole interval.
        if (Music.IsInCombat())
        {
            return;
        }

        // Nor while the game is paused or the player is busy with something else: a conversation, the
        // map, a cutscene, sleep, a menu. Each sets a bit of controlsDisabled, and the ones that stop
        // the clock stop this caller too; both are tested, so that neither kind slips through. The
        // inventory and the spell panel are the exception: with either open a click on a wall still
        // looks at it, so the look made for you goes on as well.
        const EControlMask stillLooking = EControlMask.Inventory | EControlMask.Magic;
        if (Time.timeScale == 0.0f || (PlayerObject.Player.controlsDisabled & ~stillLooking) != 0)
        {
            return;
        }

        if (Time.time < settledTime)
        {
            return;
        }

        // With no points in Search there is no roll, as with Track.
        int skill = Skills.GetSkill(ESkill.Search);
        if (skill <= 0)
        {
            return;
        }

        int level = LevelLoader.sLevelLoader.loadedLevel;
        if (level != lastPolledLevel)
        {
            lastPolledLevel = level;
            lastReportedTime.Clear();
        }

        Vector3 playerPos = PlayerObject.Player.transform.position;
        int tileX = Tile.GetTileX(playerPos.x);
        int tileY = Tile.GetTileY(playerPos.z);

        candidates.Clear();
        CollectCandidates(tileX, tileY);

        // Nearest first: it is the most useful thing to be told.
        candidates.Sort((a, b) => a.tileDistance.CompareTo(b.tileDistance));

        foreach (Candidate candidate in candidates)
        {
            if (lastReportedTime.TryGetValue(candidate.objectIndex, out float reportedAt)
                && Time.time - reportedAt < repeatMessageDelay)
            {
                continue;
            }

            if (!IsInFrontOrBeside(candidate.position) || !HasLineOfSight(candidate.position))
            {
                continue;
            }

            // A difficulty of zero means the look needs no roll, and neither does this.
            if (candidate.difficulty > 0 && Random.value >= ReportChance(skill, candidate.difficulty))
            {
                continue;
            }

            lastReportedTime[candidate.objectIndex] = Time.time;
            Report(candidate.position - playerPos);

            // One door per round. Emptying the whole room into the log in a single tick would
            // read as a bug even when every roll was honest.
            return;
        }
    }

    /// <summary>
    /// Forgets what has already been announced. Called when a level is unloaded.
    /// </summary>
    public static void Reset()
    {
        lastPolledLevel = -1;
        lastReportedTime.Clear();
        settledTime = Time.time + settleDelay;
    }

    private static void CollectCandidates(int tileX, int tileY)
    {
        for (int x = Mathf.Max(0, tileX - searchRangeTiles); x <= Mathf.Min(tileX + searchRangeTiles, 63); ++x)
        {
            for (int y = Mathf.Max(0, tileY - searchRangeTiles); y <= Mathf.Min(tileY + searchRangeTiles, 63); ++y)
            {
                Tile t = LevelLoader.GetTile(x, y);
                if (t == null)
                {
                    continue;
                }

                // Chebyshev distance, so the range is the square the two loops above walk rather
                // than a circle inscribed in it. It only orders the candidates.
                int tileDistance = Mathf.Max(Mathf.Abs(x - tileX), Mathf.Abs(y - tileY));

                int o = t.firstObject;
                int guard = 0;
                while (o != 0 && ++guard < 64)
                {
                    if (LevelLoader.GetObj(o) is Decal decal && decal.isActiveAndEnabled)
                    {
                        Trigger trigger = HiddenDoorTrigger(decal);
                        if (trigger != null)
                        {
                            candidates.Add(new Candidate(decal.objectIndex, decal.transform.position, tileDistance, trigger.z));
                        }
                    }
                    o = LevelLoader.sLevelLoader.GetChainIndexFromObjectData(o);
                }
            }
        }
    }

    /// <summary>
    /// The look trigger that makes this decal a hidden door not yet found, or null.
    /// </summary>
    /// <remarks>
    /// A hidden door is a decal whose look trigger is still armed and sets off a change terrain
    /// trap, which is what opens the tile behind the wall. Once the door has been found the
    /// trigger turns itself off, or its chain deletes the decal, so the test also tells a found
    /// door from one still hidden. The chain is walked rather than only its first link in case a
    /// door sits behind a check of some kind first; the depth limit is there to survive
    /// malformed data, not because deep chains are expected.
    /// </remarks>
    private static Trigger HiddenDoorTrigger(Decal decal)
    {
        if (decal.link == 0 || decal.stackable)
        {
            return null;
        }

        if (!(LevelLoader.GetObj(decal.link) is Trigger trigger)
            || trigger.type != EObjectType.LookTrigger
            || (trigger.flags & 4) == 0)
        {
            return null;
        }

        UUObject obj = trigger;
        for (int depth = 0; depth < maxChainDepth && obj.link != 0; ++depth)
        {
            obj = LevelLoader.GetObj(obj.link);
            if (obj == null)
            {
                return null;
            }

            if (obj.type == EObjectType.ChangeTerrainTrap)
            {
                return trigger;
            }
        }
        return null;
    }

    /// <summary>
    /// How fast the timer's handicap wears off as Search rises: ln(10) / 10, rounded.
    /// </summary>
    public const float handicapFade = 0.2303f;

    /// <summary>
    /// The chance that one round of the timer reports a hidden wall of this difficulty.
    /// </summary>
    /// <remarks>
    /// Harder than a look, since the timer looks for you and never tires: a look's chance at the
    /// same Search, times 1 - 0.5 e^(-0.2303 Search). The factor is one half at Search 0, 95% at
    /// 10 and 99.95% at 30, so the handicap is felt by a beginner and fades out. For the
    /// difficulties the level designers used, 3 and 10, that is 67% and 46% a round at Search 10,
    /// and all but certain at 30. At Search 0 the timer makes no roll at all (Poll).
    /// </remarks>
    internal static float ReportChance(int skill, int difficulty)
    {
        float factor = 1.0f - 0.5f * Mathf.Exp(-handicapFade * Mathf.Max(skill, 0));
        return factor * LookChance(skill, difficulty);
    }

    /// <summary>
    /// The chance that a look passes GetResult(skill, difficulty) with Success or better.
    /// </summary>
    /// <remarks>
    /// Skills.GetResult draws 0 to 30, thirty-one values, and succeeds when skill + draw -
    /// difficulty reaches 16: that is 15 + skill - difficulty of the thirty-one.
    /// </remarks>
    private static float LookChance(int skill, int difficulty)
    {
        return Mathf.Clamp01((15 + skill - difficulty) / 31.0f);
    }

    /// <summary>
    /// Whether a point is in front of the player or to one side, rather than behind.
    /// </summary>
    internal static bool IsInFrontOrBeside(Vector3 targetPos)
    {
        Transform eye = PlayerObject.Player.mainCamera.transform;
        Vector3 facing = eye.forward;
        facing.y = 0.0f;
        Vector3 toTarget = targetPos - eye.position;
        toTarget.y = 0.0f;
        if (facing.sqrMagnitude < 1e-6f || toTarget.sqrMagnitude < 1e-6f)
        {
            return true;
        }

        return Vector3.Angle(facing, toTarget) <= maxAngleFromFacing;
    }

    /// <summary>
    /// Whether the player can actually see the wall the door is in.
    /// </summary>
    /// <remarks>
    /// Without this a door in the next corridor is reported through the stone between, which
    /// reads as clairvoyance rather than as a sharp pair of eyes.
    /// </remarks>
    internal static bool HasLineOfSight(Vector3 targetPos)
    {
        Vector3 eye = PlayerObject.Player.mainCamera.transform.position;
        Vector3 toTarget = targetPos - eye;
        float distance = toTarget.magnitude;
        if (distance <= lineOfSightBackoff)
        {
            return true;
        }

        return !Physics.Raycast(eye, toTarget / distance, distance - lineOfSightBackoff,
                                LayerMasks.EnvironmentAndCeiling);
    }

    private static void Report(Vector3 offset)
    {
        // Horizontal only, so a door on a different floor height still gets a compass direction
        // rather than a degenerate one.
        offset.y = 0.0f;
        if (offset.sqrMagnitude < 0.001f)
        {
            return;
        }

        // Block 1, 36 to 43 are the eight directions, the same ones Detect Monster uses. They read
        // "to the North" and carry no full stop, and the spell builds its line the same way -
        // block 1 entry 59 is "You detect a creature " - so this line is built the same way. The
        // sentence itself is not the original's, which has no line for a hidden door at all, and it
        // says "suspect" because the timer does not open anything: the look that follows still has to
        // pass its own roll.
        string direction = StringLoader.GetString(1, 36 + Utils.OffsetToOctant(offset));
        Messages.Add("You suspect something is hidden " + direction + ".");
    }
}
