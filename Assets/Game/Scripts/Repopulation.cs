using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The dungeon bringing creatures back by itself, as UW.EXE 0x85017 does from the world tick.
/// </summary>
/// <remarks>
/// Every 24 ticks of 21 seconds there is one chance in four of a pass (0x26494, 0x2acbd). The pass
/// fires each create-object trap that no trigger counts and whose template is a creature, when the
/// player's tile is at least 8 tiles from the trap's in x or in y (0x84fc4); the trap makes its copy
/// as Trap.DoCreateObjectTrap() does, with its roll and the original's cap. The copies carry a mark.
///
/// Three changes are ours, made so that a level feels less empty and more alive rather than cleared
/// for good. A trap makes nothing while the copy it made is alive, wherever it has wandered, so a
/// level cannot fill up with them; with that bound the copies are kept when the player leaves the
/// level or sleeps, where the original deletes them (0x70c05). And the time is made up: the checks
/// that fell while the player was on another level are run when he comes back, with no distance
/// gate since he was not there, and those of a sleep when he wakes, gated from where he lies, so a
/// long absence or a night's sleep finds most traps full again. The original stops the world while
/// the player is away, and fires every trap once at any distance after a full sleep.
/// </remarks>
public static class Repopulation
{
    // 24 world ticks of 21 seconds, in game time (UW.EXE 0x2acbd, 0x26494).
    private const double CheckSeconds = 24 * 21;

    // The distance gate, in tiles on either axis (UW.EXE 0x84fc4).
    private const int FarTiles = 8;

    // Set by LevelLoader.LoadLevel(): the next update makes up the time away with no distance gate.
    private static bool entering;

    /// <summary>The player is going to another level, or into a new game.</summary>
    public static void EnteringLevel()
    {
        entering = true;
    }

    /// <summary>
    /// Runs the checks that have fallen due on the current level since its clock last moved. Called
    /// every frame of play by PlayerObject.Update(), and by the sleep each time it moves the clock on.
    /// </summary>
    public static void Update()
    {
        LevelLoader loader = LevelLoader.sLevelLoader;
        int level = loader.loadedLevel;
        if (level < 1 || level >= loader.levels.Length || loader.levels[level] == null
            || !loader.sentPostLoadInitialize || PlayerObject.Player == null)
        {
            return;
        }

        bool gated = !entering;
        entering = false;

        PlayerData data = PlayerData.sData;
        if (data.repopulationClock == null || data.repopulationClock.Length < loader.levels.Length)
        {
            double[] clock = new double[loader.levels.Length];
            data.repopulationClock?.CopyTo(clock, 0);
            data.repopulationClock = clock;
        }

        // A level seen for the first time, or a save from before the clock: it starts now, rather
        // than making up the whole game at once.
        if (data.repopulationClock[level] <= 0.0)
        {
            data.repopulationClock[level] = data.gameTime;
            return;
        }

        while (data.gameTime - data.repopulationClock[level] >= CheckSeconds)
        {
            data.repopulationClock[level] += CheckSeconds;
            // one time in four (UW.EXE 0x2aec4)
            if (Random.Range(0, 4) == 0)
            {
                Pass(gated);
            }
        }
    }

    // UW.EXE 0x85017: the traps in the order of the original's walk, y outer and x inner.
    private static void Pass(bool gated)
    {
        Level lev = LevelLoader.GetLevel();
        Vector2Int player = Critter.TileOf(PlayerObject.Player.transform.position);

        List<Trap> traps = new List<Trap>();
        foreach (UUObject obj in lev.objects)
        {
            if (obj is Trap trap && Qualifies(trap))
            {
                traps.Add(trap);
            }
        }

        traps.Sort((a, b) => a.initialTile.y != b.initialTile.y
            ? a.initialTile.y.CompareTo(b.initialTile.y)
            : a.initialTile.x.CompareTo(b.initialTile.x));

        int made = 0;
        foreach (Trap trap in traps)
        {
            Tile t = trap.initialTile;
            if (gated && Mathf.Abs(t.x - player.x) < FarTiles && Mathf.Abs(t.y - player.y) < FarTiles)
            {
                continue;
            }

            if (CopyAlive(trap))
            {
                continue;
            }

            if (trap.DoCreateObjectTrap(trap, fromRepopulation: true) is Critter copy)
            {
                ++made;
                if (copy.movementType == Critter.EMovementType.Flying)
                {
                    copy.transform.position += 2.0f * Vector3.up;
                    copy.FitFlightHeight();
                }
            }
        }

        Debug.Log($"Repopulation: a pass on level {LevelLoader.sLevelLoader.loadedLevel}{(gated ? "" : " on entering")}, {traps.Count} traps, {made} made.");
    }

    // A create-object trap no trigger counts (bits 9-12 of w0, UW.EXE 0x8502f) whose link is a
    // creature. The original takes only the first such trap of a tile; no tile of the game has two.
    private static bool Qualifies(Trap trap)
    {
        return trap.type == EObjectType.CreateObjectTrap
            && trap.flags == 0
            && trap.link > 0 && trap.link < 256
            && trap.initialTile != null
            && LevelLoader.GetObj(trap.link) is Critter;
    }

    // Ours: whether the copy this trap made is still alive, anywhere on the level.
    private static bool CopyAlive(Trap trap)
    {
        foreach (LevelObject obj in LevelLoader.worldObj)
        {
            if (obj is Critter c && c.repopulationTrap == trap.objectIndex && c.IsAlive)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a creature with the repopulation's mark stands in the 10 x 10 tiles the original walks
    /// around a template's home, the cap that stops any create-object trap from making a creature
    /// (UW.EXE 0x84f8e, its predicate 0x84f3d, the walk 0x34d67 with radius 4).
    /// </summary>
    public static bool MarkedCreatureNear(Vector2Int home)
    {
        foreach (LevelObject obj in LevelLoader.worldObj)
        {
            if (obj is Critter c && c.repopulationMark && c.IsAlive && c.gameObject.activeInHierarchy
                && Utils.InOriginalArea(home, 4, Critter.TileOf(c.transform.position)))
            {
                return true;
            }
        }

        return false;
    }
}
