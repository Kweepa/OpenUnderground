using System.Collections.Generic;
using UnityEngine;

// possible achievements
// killed one of each enemy type (excluding wisps) (Dungeon Diversity Day)
// equipped each weapon type (shortsword, longsword, etc) (Swiss Army Knight)
// equipped each armour type (including shields) (Hide and chic, Hidebound) (Mail call, Linked in) (Full metal jacket)
// got all runestones (No rune for error) (Leave no stone unturned)
// cast each spell (In Mani Delicto) (Rune-atic) (No rune for error) (Spell check complete)
// opened all secret doors? (Stygian secret service)
// repaired one of each type (boots, gloves, shield, etc) of armour and a weapon (Hammer time)
// defeated Navrey (Good Night, Night Eyes) (Mine Sweeper (Spider edition)) (Web Resigner)
// defeated the gazer in the mines (Eye of the Mineholder) (Mine Sweeper (Gazer edition))
// defeated Sir Rodrick (Sir Render) (Order Restored)
// freed Arial (Damsel Un-distressed)
// maxed out xp (Experience required) (XP marks the spot) (XP-ert)
// completed the game 
// fully upped a skill (Skill overkill) (Master of one)
// learned the lizardman language (Click to continue) (Clickbait) (Pit polyglot) (Murgo unchained)
// opened all chests (No Chest Left Behind)
// carried X items simultaneously (Pack mule) (Hoard-core mode) (Walking warehouse) (Beast of burden) (Carry on)
// caught 20 fish (All hail the master baiter) (Finesse)
// get to level 8 (rock bottom) (the lowest of the low) (bottom feeder)
// fill out the map (location, location, location) (Who charted?)
// walked on water using magic (Puddle jumper) (Surface tension)
// walked on lava with dragonscale boots (Blazing trails)
// read 20 books (Booked solid) 
// stew (Pairs with regret) (Simmer down)
// dreaming (Snooze clues) (Dream on)
// died with the seed in hand (Sprout of luck) (Sow what?) (Dead weight)
// goblin toilet (You forgot to flush) (Gotta go) (Took the plunge) (Nature calls)
// hallucinate (Through the looking glass)
// drunk (Last call) (Drunkard's descent) (Brew-ha-ha) (Barrel roll)
// play tune on mandolin (No Wonder) (The Abyss variations)
// armageddon (Ctrl+Z, Avatar) (Tabula rasa) (Power, meet stupid) (Uncle Ben's price)
// teleport (Vas Rel Pro) (Commuter)
// escaped goldthirst's treasury (Midas touch) (Golden fleece)
// kill some rats (Tail ender)
// complete the game in less than an hour (Rush hour) (Happy hour)
// carry 20 books (Mobile library) (Burden of knowledge)
// recover the blueprints (Me blueprints) (Me head's away) (Lost the heid)
// hang around for ages (Move it along) (Lost count) (Putting down roots)
// pacifist run (only attack golem, rodrick, tyball)
// collect all Talismans. need a good name
// defeat golem and get shield of valor

public enum EAchievement
{
    LeatherSet,
    MailSet,
    PlateSet,
    StoneSet,
    AllSpells,
    Repairs,
    Navrey,
    Gazer,
    Rodrick,
    Arial,
    Xp,
    Complete,
    Skill,
    Language,
    AllConversations,
    AllChests,
    Fish,
    AllEnemies,
    Hoarding,
    LevelEight,
    Map,
    Stew,
    Reading,
    Dreaming,
    DieWithSeed,
    BronusBook,
    Popcorn,
    GoblinToilet,
    FishRelease,
    Hallucinate,
    Drunk,
    Mandolin,
    Armageddon,
    Zanium,
    Teleport,
    WaterWalk,
    LavaWalk,
    Treasury,
    SpeedRun,
    BookBurner,
    Pacifist,
    BookCollector,
    AllTalismans,
    Golem,
    
    Count
}

public enum EDisplayPhase
{
    SlideIn,
    Stay,
    SlideOut
}

public class AchievementDisplay
{
    public EDisplayPhase phase;
    public int index;
    public float time;

    public bool Update()
    {
        switch (phase)
        {
        case EDisplayPhase.SlideIn:
            time += 5 * Time.unscaledDeltaTime;
            if (time > 1.0f)
            {
                phase = EDisplayPhase.Stay;
                time = 0.0f;
            }
            break;
        case EDisplayPhase.Stay:
            time += Time.unscaledDeltaTime;
            if (time > 5.0f)
            {
                phase = EDisplayPhase.SlideOut;
                time = 0.0f;
            }
            break;
        case EDisplayPhase.SlideOut:
            time += 5 * Time.unscaledDeltaTime;
            if (time > 1.0f)
            {
                return true;
            }
            break;
        }
        return false;
    }
}


public class Achievements : MonoBehaviour
{
    public GUIStyle titleStyle;
    public GUIStyle lowTitleStyle;
    public GUIStyle textStyle;

    public float teleportDistance = 200.0f;
    public int hoardingCount = 100;
    public int readingCount = 30;
    public float mapRevealFraction = 0.8f;
    public int fishCount = 20;
    public int repairCount = 10;
    public int waterWalkSteps = 100;
    public int lavaWalkSteps = 10;
    public int booksBurned = 20;

    private int index;

    private readonly string[] achievementNames =
    {
        "Hidebound",
        "Linked in",
        "Suits you",
        "Show some stones",
        "Spell check",
        "Hammer time", // common
        "Night-Eyes wide shut",
        "Mine sweeper",
        "Sir render",
        "Damsel un-distressed",
        "Xp-ert",
        "Dungeon dusted",
        "Skill overkill",
        "Click to continue",
        "Conversed all-star",
        "Chestbreaker",
        "Venti vidi fishy",
        "Diversity day",
        "Carry on",
        "Hit rock bottom",
        "Location, location, location",
        "Pairs with desperation",
        "Booked solid",
        "Snooze clues",
        "Sow what?",
        "Chapter one: kaboom!",
        "Movie night",
        "Take the plunge",
        "Back to school",
        "Through the looking glass",
        "Last call",
        "The Goldthirst variations",
        "Tabula rasa",
        "Pack it up",
        "Commuter",
        "Puddle jumper",
        "Blazing trails",
        "Golden fleece",
        "Rush hour",
        "0451",
        "Give peace a chance",
        "Mobile library",
        "Cabinet of Cabirus",
        "So sayest I"
    };

    private readonly string[] achievementDescs =
    {
        "Equipped a full set of leather armour.",
        "Equipped a full set of chainmail.",
        "Equipped a full set of plate armour.",
        "Found all twenty four runestones.",
        "Cast all known spells.",
        "Repaired ten items using an anvil.",
        "Defeated Navrey Night-Eyes.",
        "Defeated the gazer in the mines.",
        "Defeated Sir Rodrick.",
        "Released Arial.",
        "Maxed out experience points.",
        "Completed the game.",
        "Maxed out one skill.",
        "Learned the lizardman language.",
        "Talked with everyone.",
        "Opened all the chests in the game.",
        "Caught twenty fish.",
        "Defeated one of each creature.",
        "Carried one hundred unique items.",
        "Reached the lowest level of the Abyss.",
        "Filled out the map.",
        "Made rotworm stew.",
        "Read thirty books or scrolls.",
        "Heard all of Garamon\u2019s dream clues.",
        "Died while carrying the silver seed.",
        "Tried to read Bronus\u2019s book.",
        "Made and ate popcorn.",
        "Descended via the gray goblin toilet.",
        "Threw a fish into water.",
        "Hallucinated on mushrooms.",
        "Got drunk and passed out.",
        "Played Mardin\u2019s song on the mandolin.",
        "Disappointed the wisps.",
        "Collected enough zanium.",
        "Gate travelled far and often.",
        "Walked 100 steps on water.",
        "Walked 100 steps on lava without damage.",
        "Stole gold from Goldthirst\u2019s treasury.",
        "Completed the game in sixty minutes.",
        "Threw twenty books into lava.",
        "Hurt as few as possible critters.",
        "Carried twenty books or scrolls.",
        "Collected all of Cabirus\u2019s talismans.",
        "Defeated the golem and got the shield."
    };

    public bool[] achieved = new bool[(int)EAchievement.Count];

    private readonly List<AchievementDisplay> displayed = new();
    
    // Bit masks for optimized CheckAllEnemies achievement check
    private static readonly ulong ALL_REQUIRED_CRITTERS = (1UL << 0) | (1UL << 1) | (1UL << 2) | (1UL << 3) | (1UL << 4) | (1UL << 5)
        | (1UL << 17) | (1UL << 18) | (1UL << 19) | (1UL << 23) | (1UL << 27) | (1UL << 28) | (1UL << 38)
        | (1UL << 41) | (1UL << 46) | (1UL << 47) | (1UL << 48) | (1UL << 49) | (1UL << 50)
        | (1UL << 52) | (1UL << 53) | (1UL << 54) | (1UL << 55) | (1UL << 56) | (1UL << 57);
    private static readonly ulong ANY_GREEN_GOBLIN = (1UL << 6) | (1UL << 7) | (1UL << 13);
    private static readonly ulong ANY_GREY_GOBLIN = (1UL << 12) | (1UL << 14) | (1UL << 16);
    private static readonly ulong ANY_FIGHTER = (1UL << 29) | (1UL << 30) | (1UL << 31) | (1UL << 34) | (1UL << 40);
    private static readonly ulong ANY_GHOST = (1UL << 33) | (1UL << 36) | (1UL << 37);
    private static readonly ulong ANY_MAGE = (1UL << 39) | (1UL << 42) | (1UL << 43) | (1UL << 44) | (1UL << 45) | (1UL << 51) | (1UL << 59);

    public void Start()
    {
        for (int i = 0; i < (int)EAchievement.Count; ++i)
        {
            if (PlayerPrefs.HasKey($"Achievement{i}"))
            {
                achieved[i] = true;
            }
        }
    }

    public void Update()
    {
        // update existing
        for (int i = displayed.Count - 1; i >= 0; --i)
        {
            if (displayed[i].Update())
            {
                displayed.RemoveAt(i);
            }
        }

        // check one each frame
        ++index;

        EAchievement ach = (EAchievement)index;

        if (ach == EAchievement.Count)
        {
            index = 0;
            ach = (EAchievement)index;
        }

        if (!achieved[index])
        {
            bool got = false;
            switch (ach)
            {
            case EAchievement.LeatherSet:
                got = CheckLeatherSet();
                break;
            case EAchievement.MailSet:
                got = CheckMailSet();
                break;
            case EAchievement.PlateSet:
                got = CheckPlateSet();
                break;
            case EAchievement.StoneSet:
                got = CheckStoneSet();
                break;
            case EAchievement.AllSpells:
                got = CheckAllSpells();
                break;
            case EAchievement.Repairs:
                got = CheckRepairs();
                break;
            case EAchievement.Navrey:
                got = CheckNavrey();
                break;
            case EAchievement.Gazer:
                got = CheckGazer();
                break;
            case EAchievement.Rodrick:
                got = CheckRodrick();
                break;
            case EAchievement.Arial:
                got = CheckArial();
                break;
            case EAchievement.Xp:
                got = CheckXp();
                break;
            case EAchievement.Complete:
                got = CheckComplete();
                break;
            case EAchievement.Skill:
                got = CheckSkill();
                break;
            case EAchievement.Language:
                got = CheckLanguage();
                break;
            case EAchievement.AllConversations:
                got = CheckAllConversations();
                break;
            case EAchievement.AllChests:
                got = CheckAllChests();
                break;
            case EAchievement.Fish:
                got = CheckFish();
                break;
            case EAchievement.AllEnemies:
                got = CheckAllEnemies();
                break;
            case EAchievement.LevelEight:
                got = CheckLevelEight();
                break;
            case EAchievement.Map:
                got = CheckMap();
                break;
            case EAchievement.Stew:
                got = CheckStew();
                break;
            case EAchievement.Reading:
                got = CheckReading();
                break;
            case EAchievement.Dreaming:
                got = CheckDreaming();
                break;
            case EAchievement.Hoarding:
                got = CheckHoarding();
                break;
            case EAchievement.DieWithSeed:
                got = CheckDieWithSeed();
                break;
            case EAchievement.BronusBook:
                got = CheckBronusBook();
                break;
            case EAchievement.Popcorn:
                got = CheckPopcorn();
                break;
            case EAchievement.GoblinToilet:
                got = CheckGoblinToilet();
                break;
            case EAchievement.FishRelease:
                got = CheckFishRelease();
                break;
            case EAchievement.Hallucinate:
                got = CheckHallucinate();
                break;
            case EAchievement.Drunk:
                got = CheckDrunk();
                break;
            case EAchievement.Mandolin:
                got = CheckMandolin();
                break;
            case EAchievement.Armageddon:
                got = CheckArmageddon();
                break;
            case EAchievement.Zanium:
                got = CheckZanium();
                break;
            case EAchievement.Teleport:
                got = CheckTeleport();
                break;
            case EAchievement.WaterWalk:
                got = CheckWaterWalk();
                break;
            case EAchievement.LavaWalk:
                got = CheckLavaWalk();
                break;
            case EAchievement.Treasury:
                got = CheckTreasury();
                break;
            case EAchievement.SpeedRun:
                got = CheckSpeedRun();
                break;
            case EAchievement.BookBurner:
                got = CheckBookBurner();
                break;
            case EAchievement.Pacifist:
                got = CheckPacifist();
                break;
            case EAchievement.BookCollector:
                got = CheckBookCollector();
                break;
            case EAchievement.AllTalismans:
                got = CheckAllTalismans();
                break;
            case EAchievement.Golem:
                got = CheckGolem();
                break;
            }

            if (got)
            {
                GotAchievement();
            }
        }
    }

    private bool CheckGolem()
    {
        if (Inventory.sInv != null)
        {
            return Inventory.sInv.FindObjectInInventory(EObjectType.ShinyShield) != null;
        }
        return false;
    }

    private bool CheckAllTalismans()
    {
        return (PlayerData.sData?.talismansCollected ?? 0) == 255;
    }

    private bool CheckBookCollector()
    {
        if (Inventory.sInv != null)
        {
            List<UUObject> books = Inventory.sInv.FindObjectsInInventory(UUObject.EClass.Books);
            UUObject bronusBook = Inventory.sInv.FindObjectInInventory(EObjectType.ExplodingBook);
            if (bronusBook != null)
            {
                books.Add(bronusBook);
            }
            return books.Count >= 20;
        }
        return false;
    }

    private bool CheckPacifist()
    {
        return CheckComplete() && !(PlayerData.sData?.pacifistStopped ?? true); 
    }

    private bool CheckBookBurner()
    {
        return (PlayerData.sData?.booksBurned ?? 0) >= booksBurned;
    }

    private bool CheckSpeedRun()
    {
        return CheckComplete() && (PlayerData.sData?.playTime ?? 9999) < 60 * 60; 
    }

    private bool CheckTreasury()
    {
        if (PlayerData.sData?.raidedTreasury ?? false)
        {
            Tile t = LevelLoader.GetClosestTile(PlayerObject.Player.mainCamera!.transform.position);
            {
                if (t.x is < 4 or > 10 || t.y is < 20 or > 26)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private bool CheckLavaWalk()
    {
        return (PlayerData.sData?.lavaWalkSteps ?? 0) >= lavaWalkSteps;
    }

    private bool CheckWaterWalk()
    {
        return (PlayerData.sData?.waterWalkSteps ?? 0) >= waterWalkSteps;
    }

    private bool CheckTeleport()
    {
        return (PlayerData.sData?.gateTravelDistance ?? 0) > teleportDistance;
    }

    private bool CheckZanium()
    {
        int num = 0;
        if (Inventory.sInv != null)
        {
            foreach (UUObject zanium in Inventory.sInv.FindObjectsInInventory(EObjectType.GlowingRock))
            {
                num += zanium.quantity;
            }
        }
        return num >= 80;
    }

    private bool CheckArmageddon()
    {
        Magic magic = Magic.sMagic;
        if (magic != null)
        {
            return magic.castSpells[(int)Magic.ESpell.Armageddon];
        }
        return false;
    }

    private bool CheckMandolin()
    {
        return PlayerData.sData?.playedMardinOnMandolin ?? false;
    }

    private bool CheckDrunk()
    {
        return PlayerData.sData?.passedOutDrunk ?? false;
    }

    private bool CheckHallucinate()
    {
        return PlayerData.sData?.tripped ?? false;
    }

    private bool CheckFishRelease()
    {
        return PlayerData.sData?.releasedFish ?? false;
    }

    private bool CheckGoblinToilet()
    {
        return PlayerData.sData?.usedGoblinToilet ?? false;
    }

    private bool CheckPopcorn()
    {
        return PlayerData.sData?.atePopcorn ?? false;
    }

    private bool CheckBronusBook()
    {
        return PlayerData.GetQuestFlag(EQuestFlag.BronusBookGoBoom) != 0;
    }

    private bool CheckDieWithSeed()
    {
        return (PlayerData.sData?.hp ?? 1) <= 0
            && (Inventory.sInv?.FindObjectInInventory(EObjectType.SilverSeed) ?? false);
    }

    private bool CheckHoarding()
    {
        return Inventory.sInv != null && Inventory.GetAllItems().Count >= hoardingCount;
    }

    private bool CheckDreaming()
    {
        if (PlayerData.sData != null)
        {
            for (int i = 0; i < 10; ++i)
            {
                if (!PlayerData.sData.dreamDreamt[i])
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    private bool CheckReading()
    {
        return (PlayerData.sData?.booksRead ?? 1) >= readingCount;
    }

    private bool CheckStew()
    {
        return Inventory.sInv?.FindObjectInInventory(EObjectType.RotwormStew) ?? false;
    }

    private bool CheckMap()
    {
        // 8 maps of 64x64 tiles, with approx 0.5 occupancy.
        return (PlayerData.sData?.mapTilesRevealed ?? 0) >= mapRevealFraction * 0.5 * 8 * 64 * 64;
    }

    private bool CheckLevelEight()
    {
        return (LevelLoader.sLevelLoader?.loadedLevel ?? 0) == 8;
    }

    private bool CheckAllEnemies()
    {
        PlayerData data = PlayerData.sData;
        if (data != null)
        {
            ulong flags = data.defeatedCritterFlags;
            
            if ((flags & ALL_REQUIRED_CRITTERS) == ALL_REQUIRED_CRITTERS
                && (flags & ANY_GREEN_GOBLIN) > 0
                && (flags & ANY_GREY_GOBLIN) > 0
                && (flags & ANY_FIGHTER) > 0
                && (flags & ANY_GHOST) > 0
                && (flags & ANY_MAGE) > 0)
            {
                return true;
            }
        }
        return false;
    }

    private bool CheckFish()
    {
        return (PlayerData.sData?.numFishCaught ?? 0) >= fishCount;
    }

    private bool CheckAllChests()
    {
        PlayerData data = PlayerData.sData;
        return data != null && data.openedChest[1] && data.openedChest[2] && data.openedChest[4];
    }

    private bool CheckAllConversations()
    {
        Conversations conv = Conversations.sConversations;
        if (conv != null)
        {
            for (int i = 0; i < conv.conversations.Length; ++i)
            {
                if (Conversations.sConversations?.conversations[i] != null
                    && !(PlayerData.sData?.hadConversation[i] ?? false)) 
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    private bool CheckLanguage()
    {
        // freeing Murgo proves you have learned the language
        return PlayerData.GetQuestFlag(EQuestFlag.MurgoFreed) != 0;
    }

    private bool CheckSkill()
    {
        if (PlayerData.sData != null)
        {
            for (int i = 0; i < 20; ++i)
            {
                if (PlayerData.sData.skill[i] == 30)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private bool CheckComplete()
    {
        return PlayerData.sData?.enteredGreenMoongate ?? false;
    }

    private bool CheckArial()
    {
        return PlayerData.sData?.releasedArial ?? false;
    }

    private bool CheckXp()
    {
        return (PlayerData.sData?.charLevel ?? 0) == 16;
    }

    private bool CheckGazer()
    {
        return PlayerData.GetQuestFlag(EQuestFlag.GazerKilled) != 0;
    }

    private bool CheckRodrick()
    {
        return PlayerData.GetQuestFlag(EQuestFlag.RodrickKilled) != 0;
    }

    private bool CheckNavrey()
    {
        return PlayerData.sData?.navreyKilled ?? false;
    }

    private bool CheckRepairs()
    {
        return (PlayerData.sData?.numRepairs ?? 0) >= repairCount;
    }

    private bool CheckAllSpells()
    {
        // for now check if all spells have been cast in this playthrough
        // might need to change this to save spells cast in the player prefs
        Magic magic = Magic.sMagic;
        if (magic != null)
        {
            for (int i = 0; i < 48; ++i)
            {
                if (!magic.castSpells[i])
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    private bool CheckStoneSet()
    {
        if (Magic.sMagic != null)
        {
            for (int i = 0; i < 24; ++i)
            {
                if (!Magic.sMagic.hasRunestone[i])
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    private bool CheckLeatherSet()
    {
        Inventory inv = Inventory.sInv;
        if (inv != null)
        {
            return (inv.invSlotContents[(int)EInvSlot.Head]?.type ?? 0) == EObjectType.LeatherCap
                && (inv.invSlotContents[(int)EInvSlot.Torso]?.type ?? 0) == EObjectType.LeatherVest
                && (inv.invSlotContents[(int)EInvSlot.Legs]?.type ?? 0) == EObjectType.LeatherLeggings
                && (inv.invSlotContents[(int)EInvSlot.Hands]?.type ?? 0) == EObjectType.LeatherGloves
                && (inv.invSlotContents[(int)EInvSlot.Feet]?.type ?? 0) == EObjectType.LeatherBoots;
        }
        return false;
    }

    private bool CheckMailSet()
    {
        Inventory inv = Inventory.sInv;
        if (inv != null)
        {
            return (inv.invSlotContents[(int)EInvSlot.Head]?.type ?? 0) == EObjectType.ChainCowl
                && (inv.invSlotContents[(int)EInvSlot.Torso]?.type ?? 0) == EObjectType.MailShirt
                && (inv.invSlotContents[(int)EInvSlot.Legs]?.type ?? 0) == EObjectType.MailLeggings
                && (inv.invSlotContents[(int)EInvSlot.Hands]?.type ?? 0) == EObjectType.ChainGauntlets
                && (inv.invSlotContents[(int)EInvSlot.Feet]?.type ?? 0) == EObjectType.ChainBoots;
        }
        return false;
    }

    private bool CheckPlateSet()
    {
        Inventory inv = Inventory.sInv;
        if (inv != null)
        {
            return (inv.invSlotContents[(int)EInvSlot.Head]?.type ?? 0) == EObjectType.Helmet
                && (inv.invSlotContents[(int)EInvSlot.Torso]?.type ?? 0) == EObjectType.Breastplate
                && (inv.invSlotContents[(int)EInvSlot.Legs]?.type ?? 0) == EObjectType.PlateLeggings
                && (inv.invSlotContents[(int)EInvSlot.Hands]?.type ?? 0) == EObjectType.PlateGauntlets
                && (inv.invSlotContents[(int)EInvSlot.Feet]?.type ?? 0) == EObjectType.PlateBoots;
        }
        return false;
    }

    private void GotAchievement()
    {
        achieved[index] = true;

        AchievementDisplay disp = new AchievementDisplay { index = index };
        displayed.Insert(0, disp);
        
        PlayerPrefs.SetInt($"Achievement{index}", 1);
        PlayerPrefs.Save();
    }

    public string DrawAllAchievements(bool revealAchievements)
    {
        Matrix4x4 oldMatrix = GUI.matrix;
        float scale = Screen.safeArea.width / (340 * 4 + 100);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1.0f));

        int numAchieved = 0;
        for (int i = 0; i < (int) EAchievement.Count; ++i)
        {
            Rect r = new Rect(20 + 360 * (i % 4), 100 + 60 * (i / 4), 340, 45);
            if (achieved[i])
            {
                GUI.Label(r, achievementNames[i], titleStyle);
                GUI.Label(r, achievementDescs[i], textStyle);
                ++numAchieved;
            }
            else
            {
                GUI.Label(r, revealAchievements ? achievementNames[i] : "???", lowTitleStyle);
            }
        }
        
        GUI.matrix = oldMatrix;

        return $"{numAchieved}/{(int)EAchievement.Count}";
    }

    public void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Achievements;

        float y = Screen.height - 180;
        foreach (AchievementDisplay disp in displayed)
        {
            float x = Screen.width - 350;
            switch (disp.phase)
            {
            case EDisplayPhase.SlideIn:
                x += (1 - disp.time) * 350;
                break;
            case EDisplayPhase.SlideOut:
                x += disp.time * 350;
                break;
            }

            Rect r = new Rect(x, y, 340, 45);
            GUI.Label(r, achievementNames[disp.index], titleStyle);
            GUI.Label(r, achievementDescs[disp.index], textStyle);

            y -= 50;
        }
    }
}
