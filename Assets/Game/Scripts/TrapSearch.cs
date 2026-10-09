using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A trap on an object: a look finds it with the Search skill, and the Traps skill disarms it.
/// </summary>
/// <remarks>
/// In the original a look in Look mode, after the description, runs the trap check on the object
/// looked at (UW.EXE 0x26e10): the first trap or trigger in the object's list, a trigger followed
/// to its trap, and only a damage, teleport or arrow trap (0x82a99). Search rolls against a fixed
/// 8, and a success asks "You found a trap!  Do you wish to try to disarm it?" with Yes shown
/// (0x3a181). A yes rolls Traps against the same 8 (0x82b3d): a success takes the trap off that
/// object, a failure only says so, and a critical failure sets it off. The remake has no Look
/// mode, so every look runs the check, as every look rolls a hidden wall's Search.
///
/// Remove Trap runs the same finder and the same disarm with the skill at 45, which always
/// succeeds against 8 (0x35900), and says nothing when there is no trap; here on every trapped
/// object within the automatic look's three tiles, where the original takes one object.
///
/// Besides Remove Trap's area, four things are ours. A look that has just rolled for an object
/// waits Trigger.searchRollCooldown seconds before another look rolls it again, as for a hidden
/// wall. Taking an object rolls too: a trap found stops the pick-up with the question and leaves
/// the object where it is, and a trap not found goes off. The question wants the object within
/// hand's reach (IsInReach). And every checkInterval seconds a roll is made for the trapped
/// objects in front of the player or at a side, and a success points one out, without asking
/// anything: the look that follows still rolls its own Search. The original reads the Search
/// skill only on an action of the player's or a spell.
/// </remarks>
public static class TrapSearch
{
    /// <summary>The difficulty of both rolls, finding and disarming: UW.EXE 0x82b2a and 0x82bf2.</summary>
    public const int difficulty = 8;

    /// <summary>The skill Remove Trap rolls both with, in place of Search and Traps: UW.EXE 0x3591b and 0x35932.</summary>
    public const int removeTrapSkill = 45;

    /// <summary>Seconds between two rounds of the automatic look.</summary>
    /// <remarks>
    /// Much less than the hidden walls' ten, the user's choice after playing it: a player does not
    /// stand looking at an object for long before taking or using it, so a slower round would
    /// rarely get a chance.
    /// </remarks>
    public const float checkInterval = 3.0f;

    /// <summary>How far the automatic look reaches, in tiles, counted as a square around the player's tile.</summary>
    public const int searchRangeTiles = 3;

    /// <summary>Seconds before the automatic look mentions the same object again.</summary>
    public const float repeatMessageDelay = 60.0f;

    // Time.time before which a look does not roll again for an object, by object.
    private static readonly Dictionary<UUObject, float> nextLookRollTime = new Dictionary<UUObject, float>();

    // Time.time of the automatic look's last message about each object.
    private static readonly Dictionary<UUObject, float> lastReportedTime = new Dictionary<UUObject, float>();

    private const float settleDelay = 2.0f;
    private static float settledTime;

    private readonly struct Candidate
    {
        public readonly UUObject obj;
        public readonly int tileDistance;

        public Candidate(UUObject obj, int tileDistance)
        {
            this.obj = obj;
            this.tileDistance = tileDistance;
        }
    }

    private static readonly List<Candidate> candidates = new List<Candidate>();

    /// <summary>
    /// The disarmable trap on an object, the way the original's finder looks for it (UW.EXE
    /// 0x82a99): the first trap or trigger in the list at the object's link, and behind a trigger
    /// the object its own link names. Only a damage, teleport or arrow trap counts.
    /// </summary>
    /// <remarks>
    /// Only the first object of that kind is looked at, as in the original: a list that starts
    /// with, say, a door trap has no trap to find. Two tests are the remake's. An object is only
    /// searched on the level it comes from, as UUObject.SendLookToTrigger() does, because its link
    /// is an index into that level's objects. And a trigger the remake has switched off, or an
    /// arrow trap it has emptied after firing, counts as no trap: the original deletes a one-shot
    /// trap when it fires, and the remake keeps the objects and turns them off instead.
    /// </remarks>
    public static bool FindDisarmable(UUObject obj, out Trigger trigger, out Trap trap)
    {
        trigger = null;
        trap = null;

        if (obj == null || !obj.isLinked || obj.link == 0 || obj.stackable)
        {
            return false;
        }

        int loadedLevel = LevelLoader.sLevelLoader.loadedLevel;
        int homeLevel = obj.originalLevel > 0 ? obj.originalLevel : loadedLevel;
        if (homeLevel != loadedLevel)
        {
            return false;
        }

        UUObject first = FirstTrapOrTrigger(obj);
        if (first == null)
        {
            return false;
        }

        if (first is Trigger t)
        {
            if ((t.flags & 4) == 0 || t.link == 0)
            {
                return false;
            }

            trigger = t;
            trap = LevelLoader.GetObj(t.link) as Trap;
        }
        else
        {
            trap = first as Trap;
        }

        if (trap == null)
        {
            trigger = null;
            return false;
        }

        bool disarmable = trap.type switch
        {
            EObjectType.DamageTrap or EObjectType.TeleportTrap => true,
            EObjectType.ArrowTrap => ((trap.quality << 5) | trap.ownerIndex) != 0,
            _ => false
        };
        if (!disarmable)
        {
            trigger = null;
            trap = null;
        }
        return disarmable;
    }

    /// <summary>
    /// The first object of class 6 - a trap or a trigger, types 384 to 447 - in the list at the
    /// object's link, walked by its next field (UW.EXE 0x2a476, with no descent into contents).
    /// </summary>
    private static UUObject FirstTrapOrTrigger(UUObject obj)
    {
        int index = obj.link;
        for (int guard = 0; index != 0 && guard < 1024; ++guard)
        {
            UUObject o = LevelLoader.GetObj(index);
            if (o == null)
            {
                // A slot the loader did not build is no trap or trigger, which it builds all of.
                index = LevelLoader.sLevelLoader.GetChainIndexFromObjectData(index);
                continue;
            }

            if ((int)o.type >= 384 && (int)o.type < 448)
            {
                return o;
            }
            index = o.chainIndex;
        }
        return null;
    }

    /// <summary>
    /// The trap check of a look at an object in the world. Returns true when it has asked the
    /// question, in which case afterLook is called once the question is answered; otherwise the
    /// caller goes on at once.
    /// </summary>
    /// <remarks>
    /// The original asks between the description and the event the look sends down the object's
    /// list (UW.EXE 0x26e10), so the rest of the look waits for the answer here too. It asks at
    /// any distance; here a trap found out of reach is only named, because a trap is disarmed by
    /// hand (the user's rule, 27 September 2026): see IsInReach.
    /// </remarks>
    public static bool TryLook(UUObject obj, System.Action afterLook)
    {
        return Ask(obj, false, afterLook, afterLook);
    }

    /// <summary>
    /// The trap check of a pick-up: a trap found stops the pick-up and asks the question, and the
    /// object stays where it is whatever the answer - disarmed or not - to be taken again. A trap
    /// not found goes off, and the object is taken. Returns true when it has asked, and then the
    /// caller must not pick the object up.
    /// </summary>
    /// <remarks>
    /// Ours, at the user's request of 27 September 2026: in the original only a look in Look mode
    /// searches for a trap, and taking an object sends it event 2 with no roll (UW.EXE 0x26b3e). A
    /// no leaves the object on the floor too, because once it is carried nothing can try the
    /// disarm again: the inventory's look has no trap check, in the original as here.
    /// </remarks>
    public static bool TryPickup(UUObject obj)
    {
        return Ask(obj, true, null, null);
    }

    /// <summary>
    /// The Search roll and the question, for a look and for a pick-up.
    /// </summary>
    private static bool Ask(UUObject obj, bool fromPickup, System.Action afterNo, System.Action afterYes)
    {
        if (!FindDisarmable(obj, out Trigger trigger, out Trap trap))
        {
            return false;
        }

        // The wait between two rolls is for looks only: a pick-up always rolls, since one that
        // does not find the trap sets it off.
        if (!fromPickup && nextLookRollTime.TryGetValue(obj, out float next) && Time.time < next)
        {
            return false;
        }
        nextLookRollTime[obj] = Time.time + Trigger.searchRollCooldown;

        if (Skills.GetResult(Skills.GetSkill(ESkill.Search), difficulty) < Skills.ESkillTestResult.Success)
        {
            // A miss of a look says nothing, as in the original, where the finder returns 0 for no
            // trap and for a failure alike. A miss of a pick-up sets the trap off, as a critical
            // failure of the disarm does, and the object is then taken: the user's rule of 27
            // September 2026, and ours.
            // A trap behind the object's own pick-up trigger is left to the pick-up that follows,
            // which sets it off as the original does (UUObject.SendPickupToTrigger), so it goes off
            // once and not twice.
            if (fromPickup)
            {
                Messages.Add("You set off the " + NameOf(trap) + "!");
                if (trigger == null || obj.GetPickupTrigger() != trigger)
                {
                    SetOff(obj, trigger, trap);
                }
            }
            return false;
        }

        string prompt = StringLoader.GetString(1, 244); // "You found a trap!  Do you wish to try to disarm it? "
        if (!fromPickup && !IsInReach(obj))
        {
            // The first sentence of the question, and the original's line for a thing out of reach.
            int stop = prompt.IndexOf('!');
            Messages.Add(stop >= 0 ? prompt.Substring(0, stop + 1) : prompt.TrimEnd());
            Messages.Add(1, 94); // "You cannot reach that."
            return false;
        }

        RepairDialog.AskYesNo(prompt.TrimEnd(), yes =>
        {
            // The original prints the question on the scroll and the answer after it.
            Messages.Add(prompt + (yes ? "Yes" : "No"));
            if (yes)
            {
                Disarm(obj, Skills.GetSkill(ESkill.Traps));
                afterYes?.Invoke();
            }
            else
            {
                afterNo?.Invoke();
            }
        });
        return true;
    }

    /// <summary>
    /// Whether an object is within hand's reach: the reach of a use or a pick-up, without
    /// Telekinesis, which moves things but does not disarm them.
    /// </summary>
    /// <remarks>
    /// Ours, the user's rule of 27 September 2026, for the question after a look. The original asks
    /// after a look at any distance, and gives its targeted spells a tile and a half (DS:0x286 = 144
    /// eighths of a tile, squared, passed to 0x2661b).
    /// </remarks>
    public static bool IsInReach(UUObject obj)
    {
        return Interaction.sInt != null && Interaction.sInt.IsWithinHandReach(obj);
    }

    /// <summary>
    /// Remove Trap: every trap a look could find on the objects within searchRangeTiles of the
    /// player - the automatic look's square, all around - is disarmed, for certain, with the
    /// disarm's own message; nothing is said when there is none.
    /// </summary>
    /// <remarks>
    /// In the original Remove Trap is a targeted spell, one object in reach, and runs the same
    /// finder and disarm with the skill at 45, which always succeeds against 8 (UW.EXE 0x35900).
    /// Here it works on an area around the player, the user's choice of 27 September 2026, and
    /// ours: the automatic look's reach, so that a trap it points out can be removed. It stays
    /// silent when nothing is in range, as the original is on an object with no trap: a message
    /// would tell a player that a trap is somewhere near.
    /// </remarks>
    public static void RemoveTrap()
    {
        Vector3 playerPos = PlayerObject.Player.transform.position;
        int tileX = Tile.GetTileX(playerPos.x);
        int tileY = Tile.GetTileY(playerPos.z);

        List<UUObject> trapped = new List<UUObject>();
        foreach (UUObject obj in LevelLoader.GetLevel().objects)
        {
            if (obj == null || !obj.isActiveAndEnabled || obj.link == 0)
            {
                continue;
            }

            Vector3 pos = obj.transform.position;
            int tileDistance = Mathf.Max(Mathf.Abs(Tile.GetTileX(pos.x) - tileX), Mathf.Abs(Tile.GetTileY(pos.z) - tileY));
            if (tileDistance <= searchRangeTiles && FindDisarmable(obj, out _, out _))
            {
                trapped.Add(obj);
            }
        }

        foreach (UUObject obj in trapped)
        {
            Disarm(obj, removeTrapSkill);
        }
    }

    /// <summary>
    /// The disarm, UW.EXE 0x82b3d: a Traps roll against 8, with the original's three outcomes and
    /// its three messages, which it keeps in the executable rather than in STRINGS.PAK
    /// (DS:0x1cf0-0x1d43).
    /// </summary>
    private static void Disarm(UUObject obj, int skill)
    {
        if (!FindDisarmable(obj, out Trigger trigger, out Trap trap))
        {
            return;
        }

        string trapName = NameOf(trap);
        Skills.ESkillTestResult result = Skills.GetResult(skill, difficulty);
        switch (result)
        {
        case Skills.ESkillTestResult.Success:
        case Skills.ESkillTestResult.CriticalSuccess:
            Messages.Add("The " + trapName + " on the " + NameOf(obj) + " was successfully dearmed.");
            Unhook(obj, trigger != null ? trigger : trap);
            break;
        case Skills.ESkillTestResult.Failure:
            Messages.Add("Unable to defuse trap.");
            break;
        default:
            Messages.Add("Your bumbling attempts have set off the " + trapName + ".");
            SetOff(obj, trigger, trap);
            break;
        }
    }

    /// <summary>
    /// Takes a trigger or a trap out of the object's list, so that this object no longer sets
    /// the trap off.
    /// </summary>
    /// <remarks>
    /// The original deletes it (UW.EXE 0x2a0b2). A trap that several objects' triggers share keeps
    /// a count of them in its flags, and the disarm of one object only removes that object's
    /// trigger and lowers the count, so the trap stays armed for the others; the last disarm
    /// deletes the trap too (0x84e5c, 0x84983). Taking the one trigger out of the one list does the
    /// same here: the trap stays for every trigger still pointing at it, and nothing reaches it
    /// once the last is gone. The trigger is switched off as well, which is how the remake marks a
    /// spent one. The link and the next field are both in the save.
    /// </remarks>
    private static void Unhook(UUObject obj, UUObject target)
    {
        if (obj.link == target.objectIndex)
        {
            obj.link = target.chainIndex;
        }
        else
        {
            UUObject prev = LevelLoader.GetObj(obj.link);
            for (int guard = 0; prev != null && guard < 1024; ++guard)
            {
                if (prev.chainIndex == target.objectIndex)
                {
                    prev.chainIndex = target.chainIndex;
                    break;
                }
                prev = prev.chainIndex != 0 ? LevelLoader.GetObj(prev.chainIndex) : null;
            }
        }

        target.chainIndex = 0;
        if (target is Trigger trigger)
        {
            trigger.flags &= ~4;
        }
    }

    /// <summary>
    /// A critical failure: the trap goes off on the player, the way it would have gone off by itself.
    /// </summary>
    /// <remarks>
    /// Through its trigger when there is one: the original fires the trigger with event -1, which
    /// skips the event test and the roll (UW.EXE 0x83b3b), and that is EAction.Trigger here; a
    /// repeatable trigger stays armed after. A trap hung straight on the object goes off at the
    /// object's tile and is then deleted (0x83d4a, 0x84983), so it goes off once.
    /// </remarks>
    private static void SetOff(UUObject obj, Trigger trigger, Trap trap)
    {
        if (trigger != null)
        {
            trigger.TryInteract(null, obj, EAction.Trigger);
            return;
        }

        trap.TryInteract(null, obj, EAction.Trigger);
        Unhook(obj, trap);
    }

    /// <summary>
    /// A use of the object sets off a trap hung straight on it, and the trap is then deleted, as the
    /// original's use event does (UW.EXE 0x385d6: a trap first in the list, with no trigger pointing
    /// at it, answers event 4). Used by the drink of the one trapped potion.
    /// </summary>
    public static void SetOffOnUse(UUObject obj)
    {
        if (FindDisarmable(obj, out Trigger trigger, out Trap trap) && trigger == null && trap.flags == 0)
        {
            SetOff(obj, null, trap);
        }
    }

    /// <summary>A name with no article, as the original composes both names of its messages (UW.EXE 0x3605e).</summary>
    /// <remarks>
    /// One name is ours: a damage trap with an owner poisons and does no damage (Trap.TryInteract),
    /// so it is called a poison trap, at the user's request of 27 September 2026. The original has
    /// no such type and calls it by its type's name, "damage trap" (STRINGS.PAK block 4, 384).
    /// </remarks>
    private static string NameOf(UUObject obj)
    {
        if (obj.type == EObjectType.DamageTrap && obj.ownerIndex > 0)
        {
            return "poison trap";
        }

        if (!string.IsNullOrEmpty(obj.singularName))
        {
            return obj.singularName;
        }

        string s = StringLoader.GetString(4, (int)obj.type);
        int amp = s.IndexOf('&');
        if (amp >= 0)
        {
            s = s.Substring(0, amp);
        }
        int underscore = s.IndexOf('_');
        return underscore >= 0 ? s.Substring(underscore + 1) : s;
    }

    /// <summary>
    /// One round of the automatic look. Called on a timer from PlayerObject.Update.
    /// </summary>
    /// <remarks>
    /// The same rules as the hidden walls' round (SecretDoorSearch.Poll): not in a fight, not while
    /// the game is paused or the player is busy, an object in front or at a side and in sight,
    /// within searchRangeTiles; one message a round, the nearest first, and the same object not
    /// again for repeatMessageDelay seconds. The roll is the timer's, harder than a look's
    /// (SecretDoorSearch.ReportChance), against the look's own 8.
    /// </remarks>
    public static void Poll()
    {
        if (Music.IsInCombat())
        {
            return;
        }

        const EControlMask stillLooking = EControlMask.Inventory | EControlMask.Magic;
        if (Time.timeScale == 0.0f || (PlayerObject.Player.controlsDisabled & ~stillLooking) != 0)
        {
            return;
        }

        if (Time.time < settledTime)
        {
            return;
        }

        // The roll is Search's, and with no points in it there is none, as with Track.
        int skill = Skills.GetSkill(ESkill.Search);
        if (skill <= 0)
        {
            return;
        }

        Vector3 playerPos = PlayerObject.Player.transform.position;
        int tileX = Tile.GetTileX(playerPos.x);
        int tileY = Tile.GetTileY(playerPos.z);

        candidates.Clear();
        foreach (UUObject obj in LevelLoader.GetLevel().objects)
        {
            if (obj == null || !obj.isActiveAndEnabled || obj.link == 0)
            {
                continue;
            }

            Vector3 pos = obj.transform.position;
            int tileDistance = Mathf.Max(Mathf.Abs(Tile.GetTileX(pos.x) - tileX), Mathf.Abs(Tile.GetTileY(pos.z) - tileY));
            if (tileDistance > searchRangeTiles)
            {
                continue;
            }

            if (lastReportedTime.TryGetValue(obj, out float reportedAt) && Time.time - reportedAt < repeatMessageDelay)
            {
                continue;
            }

            if (FindDisarmable(obj, out _, out _)
                && SecretDoorSearch.IsInFrontOrBeside(pos)
                && SecretDoorSearch.HasLineOfSight(pos))
            {
                candidates.Add(new Candidate(obj, tileDistance));
            }
        }

        // Nearest first, and one message a round.
        candidates.Sort((a, b) => a.tileDistance.CompareTo(b.tileDistance));
        float chance = SecretDoorSearch.ReportChance(skill, difficulty);
        foreach (Candidate candidate in candidates)
        {
            if (Random.value >= chance)
            {
                continue;
            }

            lastReportedTime[candidate.obj] = Time.time;
            // Not the original's: it has no line for this, and none for a trap noticed without a look.
            Messages.Add("You suspect the " + NameOf(candidate.obj) + " is trapped.");
            return;
        }
    }

    /// <summary>
    /// Forgets the waits and the messages. Called when a level is loaded, as SecretDoorSearch.Reset.
    /// </summary>
    public static void Reset()
    {
        nextLookRollTime.Clear();
        lastReportedTime.Clear();
        settledTime = Time.time + settleDelay;
    }
}
