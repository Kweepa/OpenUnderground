using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.PostProcessing;

public struct SSpell
{
    public SSpell(string _runes, string _name, int _cost, int _icon, int _duration, int _stringIndex)
    {
        runes = _runes;
        name = _name;
        cost = _cost;
        icon = _icon;
        duration = _duration;
        stringIndex = _stringIndex;
    }

    public int circle => cost / 3;

    public readonly string runes;
    public string name;
    public readonly int cost;
    public readonly int icon;
    public readonly int duration; // approximate seconds. there will be some variability
    /// <summary>Block 6 index in STRINGS.PAK for this spell's display / enchantment name.</summary>
    public readonly int stringIndex;
}

[System.Serializable]
public class SActiveSpell
{
    public int spell;
    public float time;
}

[System.Serializable]
public class MagicSaveData
{
    public double lastIncrementedMana;
    public double lastCheckedPermanentSpells;
    public List<SActiveSpell> activeSpells = new List<SActiveSpell>();
    /// <summary>Parallel to Magic.castSpells (spell cast tracking / achievements). Null in older saves.</summary>
    public bool[] castSpells;

    /// <summary>Mouse-only primed aim after rune success; mana not yet spent. Missing in older saves: JsonUtility defaults to false.</summary>
    public bool hasMousePrimedSpell;

    /// <summary>Index into <see cref="Magic"/> static spell list when <see cref="hasMousePrimedSpell"/>.</summary>
    public int mousePrimedSpellListIndex;

    /// <summary>Whether the primed cast was a critical success (halved mana on finalize).</summary>
    public bool mousePrimedSpellWasCritical;
}

[DefaultExecutionOrder(-150)]
public class Magic : MonoBehaviour
{
    public Texture cursor;
    public Font font;
    public Texture2D aButton;
    public Texture2D bButton;
    public Texture2D xButton;
    public Texture2D flask;
    public AudioClip defaultCastSound;
    public UUObject runeOfWarding;
    public GameObject sheetLightningParticle;
    public GameObject flameWindParticle;

    public AudioClip[] runeClipsFemale;
    public AudioClip[] runeClipsMale;

    public AudioClip moveToEmpty;
    public AudioClip moveToRune; 
    public AudioClip clearSpell;
    public AudioClip castFailed;

    public float roamingSightMouseSensitivity = 2.0f;

    public static Magic sMagic;

    private float lerpIn;
    private float holdTime;
    private int index;

    private float bubbleTime;
    private float bubbleDelay;

    private Texture2D flaskCopy;
    private Texture2D flaskBase;

    private readonly List<int> spellInProgress = new();
    private bool castASpellWithTheseRunes;

    private bool wasExploringMagic;

    /// <summary>Set from <see cref="Update"/> so <see cref="OnGUI"/> can read IMGUI keyboard events only while the magic panel is interactable.</summary>
    private bool magicPanelExploring;

    /// <summary>Grid rune under the mouse (mouse/keyboard mode); -1 if none.</summary>
    private int magicGridHoverRuneIndex = -1;

    private float timeBeforeCanCastAgain;

    /// <summary>Spell index after successful rune cast (mouse/keyboard); mana applied on aim confirm.</summary>
    private int mousePrimedSpellIndex = -1;

    private bool mousePrimedSpellWasCritical;

    /// <summary>Wand that primed the spell; charge consumed on aim confirm (null for rune cast).</summary>
    private Wand mousePrimedWand;

    /// <summary>Ignore the LMB press that primed the spell (and IMGUI/Input System frame skew) until release.</summary>
    private bool suppressMousePrimedAimUntilPrimaryReleased;

    /// <summary>Ignore the RMB release that primed a wand spell until the next secondary click.</summary>
    private bool suppressMousePrimedCancelUntilSecondaryReleased;

    /// <summary>After a primed cast LMB, block <see cref="Interaction"/> press/release look for the full click.</summary>
    private bool suppressWorldMousePrimaryUntilReleased;

    /// <summary>Keep <see cref="suppressWorldMousePrimaryUntilReleased"/> through the release frame (cleared in Update on the following frame).</summary>
    private bool suppressWorldMousePrimaryClearAfterReleaseFrame;

    public bool SuppressWorldMousePrimaryUntilPrimaryReleased => suppressWorldMousePrimaryUntilReleased;

    /// <summary>True while a mouse-primed spell awaits aim confirm (magic panel may be closed).</summary>
    public bool HasMousePrimedSpellAwaitingAim => mousePrimedSpellIndex >= 0;

    /// <summary>True when the player owns the runes for a first-circle spell and has at least 3 mana.</summary>
    public bool CanBuildFirstCircleSpell()
    {
        if (PlayerData.sData == null || PlayerData.sData.mana < 3)
        {
            return false;
        }

        for (int i = 0; i < spells.Count; i++)
        {
            if (spells[i].circle != 1)
            {
                continue;
            }

            if (HasAllRunesForSpell(spells[i].runes))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasAllRunesForSpell(string runes)
    {
        for (int r = 0; r < runes.Length; r++)
        {
            if (!HasRuneLetter(runes[r]))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasRuneLetter(char c)
    {
        for (int i = 0; i < hasRunestone.Length; i++)
        {
            if (!hasRunestone[i])
            {
                continue;
            }

            char rune = (char)('A' + i + (i == 23 ? 1 : 0));
            if (rune == c)
            {
                return true;
            }
        }

        return false;
    }

    private readonly Collider[] cachedColliders = new Collider[32];
    
    private WeaponBase hiddenWeapon;

    public enum ESpell
    {
        CreateFood,
        Leap,
        Light,
        MagicArrow,
        ResistBlows,
        Stealth,

        CreateFear,
        Curse,
        DetectMonster,
        LesserHeal,
        RuneOfWarding,
        SlowFall,

        Conceal,
        Lightning,
        NightVision,
        Speed,
        StrengthenDoor,
        ThickSkin,

        Flameproof,
        Heal,
        Poison,
        RemoveTrap,
        WaterWalk,

        CurePoison,
        Fireball,
        Levitate,
        MissileProtection,
        NameEnchantment,
        Open,
        SmiteUndead,

        Daylight,
        GateTravel,
        GreaterHeal,
        Paralyze,
        SheetLightning,
        Telekinesis,

        Ally,
        Fly,
        Invisibility,
        Confusion,
        Reveal,
        SummonMonster,

        Armageddon,
        FlameWind,
        FreezeTime,
        IronFlesh,
        RoamingSight,
        Tremor,

        Cursed,
        ManaRegeneration,
        Regeneration,
        PoisonResistance,
        Acid
    }

    [EnumNamedArray(typeof(ESpell))]
    public AudioClip[] castSpellSound;

    public AudioClip primeSpellSound;

    // STRINGS.PAK block 6 indices used for enchantment matching (language-stable).
    public const int StringIndexMagicLantern = 4;
    public const int StringIndexPoisonResistance = 55;
    public const int StringIndexCursed = 144;
    public const int StringIndexRegeneration = 190;
    public const int StringIndexManaRegeneration = 191;
    public const int StringIndexTheFrog = 211;
    public const int StringIndexManaBoost = 307;
    public const int StringIndexRestoreMana = 308;

    private static readonly List<SSpell> spells = new()
    {
        // first circle — stringIndex from block 6 castable range (256+)
        new SSpell("IMY", "Create Food", 3, -1, 0, 259), // done
        new SSpell("UP", "Leap", 6, 0, 60, 261), // done
        new SSpell("IL", "Light", 3, 17, 1500, 256), // done
        new SSpell("OJ", "Magic Arrow", 3, -1, 0, 258), // done
        new SSpell("BIS", "Resist Blows", 3, 15, 90, 257), // done
        new SSpell("SH", "Stealth", 3, 6, 30, 260), // done (quiet)

        // second circle
        new SSpell("QC", "Create Fear", 6, -1, 0, 266), // done (PAK: Cause Fear)
        new SSpell("AS", "Curse", 24, 5, 0, 262), // gotten from crowns on level 7, also on a scroll on level 4. guess at level
        new SSpell("WM", "Detect Monster", 6, -1, 0, 265), // done
        new SSpell("IBM", "Lesser Heal", 6, -1, 0, 264), // done
        new SSpell("IJ", "Rune of Warding", 6, -1, 0, 267), // done
        new SSpell("RDP", "Slow Fall", 6, 1, 30, 263), // done

        // third circle
        new SSpell("BSL", "Conceal", 9, 7, 60, 269), // done (can't be seen)
        new SSpell("OG", "Lightning", 9, -1, 0, 271), // done (PAK: Electrical Bolt)
        new SSpell("QL", "Night Vision", 9, 12, 120, 270), // done
        new SSpell("RTP", "Speed", 9, 13, 30, 268), // done
        new SSpell("SJ", "Strengthen Door", 9, -1, 0, 272), // unused
        new SSpell("IS", "Thick Skin", 12, 16, 90, 273), // done

        // fourth circle
        new SSpell("SF", "Flameproof", 12, 10, 90, 278), // done
        new SSpell("IM", "Heal", 12, -1, 0, 275), // done
        new SSpell("NM", "Poison", 12, -1, 0, 277), // done  
        new SSpell("AJ", "Remove Trap", 12, -1, 0, 279), // ???
        new SSpell("YP", "Water Walk", 9, 3, 180, 274), // done

        // fifth circle
        new SSpell("AN", "Cure Poison", 15, -1, 0, 285), // done
        new SSpell("PF", "Fireball", 15, -1, 0, 280), // done
        new SSpell("HP", "Levitate", 15, 2, 60, 276), // done
        new SSpell("GSP", "Missile Protection", 15, 9, 60, 283), // done
        new SSpell("OWY", "Name Enchantment", 15, -1, 0, 282), // done
        new SSpell("EY", "Open", 15, -1, 0, 284), // done
        new SSpell("ACM", "Smite Undead", 15, -1, 0, 281), // done

        // sixth circle
        new SSpell("VIL", "Daylight", 18, 20, 300, 290), // done
        new SSpell("VRP", "Gate Travel", 18, -1, 0, 288), // done
        new SSpell("VIM", "Greater Heal", 18, -1, 0, 286), // done
        new SSpell("AEP", "Paralyze", 18, -1, 0, 289), // done
        new SSpell("VOG", "Sheet Lightning", 18, -1, 0, 287),
        new SSpell("OPY", "Telekinesis", 18, 14, 30, 291), // done

        // seventh circle
        new SSpell("IMR", "Ally", 21, -1, 0, 293), // done
        new SSpell("VHP", "Fly", 21, 4, 30, 292), // done
        new SSpell("VSL", "Invisibility", 21, 8, 120, 295), // done (both quiet and can't be seen)
        new SSpell("VAW", "Confusion", 21, -1, 0, 296),
        new SSpell("OAQ", "Reveal", 21, -1, 0, 297), // done
        new SSpell("KM", "Summon Monster", 21, -1, 0, 294), // guessing the circle...

        // eighth circle
        new SSpell("VKC", "Armageddon", 24, -1, 0, 303), // done
        new SSpell("FH", "Flame Wind", 24, -1, 0, 301), // explosions in an area
        new SSpell("AT", "Freeze Time", 24, 11, 30, 302), // done
        new SSpell("IVS", "Iron Flesh", 24, 18, 120, 298), // done
        new SSpell("OPW", "Roaming Sight", 24, 19, 30, 300), // done
        new SSpell("VPY", "Tremor", 24, -1, 0, 299), // done

        // uncastable - get from rings, crowns, etc (low-table / special block 6 indices)
        new SSpell("", "Cursed", 0, 5, 0, StringIndexCursed),
        new SSpell("", "Mana Regeneration", 0, -1, 0, StringIndexManaRegeneration),
        new SSpell("", "Regeneration", 0, -1, 0, StringIndexRegeneration),
        new SSpell("", "Poison Resistance", 0, -1, 0, StringIndexPoisonResistance),
        new SSpell("ZZZ", "Acid", 0, -1, 0, 305)
    };

    private static bool spellNamesBound;

    /// <summary>
    /// Overwrite spell names from STRINGS.PAK block 6 so enchantment matching works with translated strings.
    /// </summary>
    public static void BindSpellNamesFromStrings()
    {
        for (int i = 0; i < spells.Count; ++i)
        {
            SSpell spell = spells[i];
            spell.name = StringLoader.GetString(6, spell.stringIndex);
            spells[i] = spell;
        }

        spellNamesBound = true;
    }

    private static void EnsureSpellNamesBound()
    {
        if (!spellNamesBound)
        {
            BindSpellNamesFromStrings();
        }
    }

    private readonly List<SActiveSpell> activeSpells = new();

    private class SPermanentSpell
    {
        public ESpell spell;
        public UUObject obj;
    }

    private readonly List<SPermanentSpell> permanentSpells = new();

    public readonly bool[] castSpells = new bool[52];

    // similar spells with duration
    private static readonly string[][] similarSpells =
    {
        new[] { "IVS", "IS", "BIS" }, // shield
        new[] { "VSL", "BSL", "SH" }, // stealth
        new[] { "VHP", "HP", "RDP" }, // float 
        new[] { "VIL", "IL" }, // light
        new[] { "RTP", "AT" }, // time
    };

    private static readonly string[] runeShortNames =
    {
        "An", "Bet", "Corp", "Des", "Ex", "Flam", "Grav", "Hur", "In", "Jux", "Kal", "Lor",
        "Mani", "Nox", "Ort", "Por", "Quas", "Rel", "Sanct", "Tym", "Uus", "Vas", "Wis", "Ylem"
    };

    private void ClearRunestones()
    {
        for (int i = 0; i < 24; ++i)
        {
            hasRunestone[i] = false;
        }
    }

    public static void AddRunestone(EObjectType rune)
    {
        // this is solely to allow the runestone to update an open magic panel
        if (sMagic == null)
            return;
        int i = rune - EObjectType.RunestoneAn;
        if ((uint)i < 24u)
        {
            bool wasNew = !sMagic.hasRunestone[i];
            sMagic.hasRunestone[i] = true;
            if (wasNew)
            {
                TutorialManager.NotifyRunestoneStowed();
            }
        }
    }

    public bool IsSpellActive(ESpell spell)
    {
        foreach (SActiveSpell s in activeSpells)
        {
            if ((ESpell)s.spell == spell)
            {
                return true;
            }
        }

        foreach (SPermanentSpell s in permanentSpells)
        {
            if (s.spell == spell)
            {
                return true;
            }
        }

        return false;
    }

    public void EquipEnchantedItem(UUObject obj)
    {
        EnsureSpellNamesBound();

        SPermanentSpell permanentSpell = new();
        ESpell resolved = 0;

        if (obj.enchantmentName == StringLoader.GetString(6, StringIndexMagicLantern))
        {
            resolved = ESpell.Light;
        }
        else if (TryFindSpellIndexFromEnchantmentName(obj.enchantmentName, out int spellIndex))
        {
            resolved = (ESpell)spellIndex;
        }

        switch (resolved)
        {
        case ESpell.Levitate:
        case ESpell.Leap:
        case ESpell.ResistBlows:
        case ESpell.MissileProtection:
        case ESpell.SlowFall:
        case ESpell.Cursed:
        case ESpell.Light:
        case ESpell.PoisonResistance:
        case ESpell.Invisibility:
        case ESpell.ManaRegeneration:
        case ESpell.Regeneration:
        case ESpell.ThickSkin:
        case ESpell.Stealth:
            permanentSpell.spell = resolved;
            break;
        }

        if (permanentSpell.spell != 0)
        {
            permanentSpell.obj = obj;
            permanentSpells.Add(permanentSpell);
        }
    }

    public void UnequipEnchantedItem(UUObject obj)
    {
        for (int i = 0; i < permanentSpells.Count; ++i)
        {
            if (permanentSpells[i].obj == obj)
            {
                permanentSpells.RemoveAt(i);
                break;
            }
        }
    }

    /// <summary>
    /// Clears runtime-only enchanted-item spell entries. Not serialized; must be reset when inventory
    /// is torn down without calling <see cref="UnequipEnchantedItem"/> (e.g. save load).
    /// </summary>
    public void ClearPermanentSpells()
    {
        permanentSpells.Clear();
    }

    public int GetSpellArmourScore()
    {
        int armour = 0;
        foreach (SActiveSpell s in activeSpells)
        {
            switch (spells[s.spell].runes)
            {
            case "BIS":
                armour += 10;
                break;
            case "IS":
                armour += 15;
                break;
            case "IVS":
                armour += 20;
                break;
            }
        }

        return armour;
    }

    public readonly bool[] hasRunestone = new bool[24];

    private bool fixedAlpha;

    private static readonly int[] flaskOffsets = { 2, 2, 2, 2, 2, 1, 1, 1, 2, 2, 2, 2, 0 };

    private void ScrubUpFlaskTextures()
    {
        // remove background pixels from fluid textures
        // set the alpha on the flask texture depending on the brightness
        if (!fixedAlpha
            && DataLoader.sDataLoader
            && DataLoader.sDataLoader.flasksTex != null
            && DataLoader.sDataLoader.flasksTex.Length > 0)
        {
            for (int i = 0; i < DataLoader.sDataLoader.flasksTex.Length; ++i)
            {
                Texture2D tex = DataLoader.sDataLoader.flasksTex[i];
                Color[] pix = tex.GetPixels();
                for (int j = 0; j < pix.Length; ++j)
                {
                    if (pix[j].a > 0)
                    {
                        if (i < 25)
                        {
                            // health - red
                            if (pix[j].r <= pix[j].b)
                            {
                                pix[j].a = 0;
                            }
                        }
                        else if (i < 50)
                        {
                            // mana - blue
                            if (pix[j].r >= pix[j].b / 2)
                            {
                                pix[j].a = 0;
                            }
                        }
                        else if (i < 75)
                        {
                            // pizen - green                            
                            if (pix[j].r >= 0.6f * pix[j].g)
                            {
                                pix[j].a = 0;
                            }
                        }
                        else
                        {
                            // flasks
                            if (!(pix[j].b > pix[j].r || pix[j].r > 0.5f))
                            {
                                pix[j].a = 0;
                            }
                            else
                            {
                                pix[j].a = 0.5f * (pix[j].r + pix[j].g + pix[j].b);
                            }
                        }
                    }
                }

                tex.SetPixels(pix);
                tex.Apply();
            }

            fixedAlpha = true;
        }
    }

    public static void ShowPanel()
    {
        if (!PlayerPanelState.ArePanelsAvailable)
        {
            return;
        }

        sMagic.holdTime = 2.0f;
    }

    public static void HidePanel()
    {
        sMagic.holdTime = 0.0f;
        if (PlayerPanelState.ActivePanel == EPlayerPanel.Magic)
            PlayerPanelState.SetPanel(EPlayerPanel.None);
    }

    /// <summary>GUI rect of the sliding magic panel (for outside-click dismiss).</summary>
    public Rect GetPanelGuiRectForOutsideClick()
    {
        if (lerpIn < 0.02f)
            return default;
        const float kExtendedInvPosition = 16f + 4f * 60f;
        float invPosition = lerpIn * kExtendedInvPosition - kExtendedInvPosition + 16f;
        Texture2D tex = DataLoader.sDataLoader.panelsTex[1];
        float w = 3 * tex.width;
        float bottom = Mathf.Max(30f + 3.6f * tex.height, 514f + 70f);
        return new Rect(invPosition, 30f, w, bottom - 30f);
    }

    public void Start()
    {
        if (flask != null)
        {
            flaskCopy = new Texture2D(flask.width, flask.height);
            flaskBase = new Texture2D(flask.width, flask.height);

            Color[] mask = flask.GetPixels();
            Color[] pix = DataLoader.sDataLoader.flasksTex[75].GetPixels();
            Color[] basePix = DataLoader.sDataLoader.flasksTex[76].GetPixels();
            for (int j = 0; j < pix.Length; ++j)
            {
                if (pix[j].b >= pix[j].g)
                {
                    pix[j].a = pix[j].b;
                }
                pix[j].a *= mask[j].a;

                if (basePix[j].r <= basePix[j].b)
                {
                    basePix[j].a = 0;
                }
            }

            flaskCopy.filterMode = FilterMode.Point;
            flaskCopy.wrapMode = TextureWrapMode.Clamp;
            flaskCopy.SetPixels(pix);
            flaskCopy.Apply();

            flaskBase.filterMode = FilterMode.Point;
            flaskBase.wrapMode = TextureWrapMode.Clamp;
            flaskBase.SetPixels(basePix);
            flaskBase.Apply();
        }

        bubbleDelay = 5.0f;

        sMagic = this;
    }

    public void OnDestroy()
    {
        ClearMousePrimedSpellIfAny();
    }

    private double lastIncrementedMana;
    private double lastCheckedPermanentSpells;

    private void PlayMoveSound()
    {
        Utils.PlayClip2d(moveToEmpty);
    }

    public void RestoreMana(int mana)
    {
        if (!PlayerData.sData.dead)
        {
            PlayerData.sData.mana = Mathf.Min(PlayerData.sData.mana + mana, PlayerData.sData.maxMana);
            bubbleTime = 2.0f;
        }
    }

    public void Update()
    {
        if (suppressWorldMousePrimaryUntilReleased)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                suppressWorldMousePrimaryUntilReleased = false;
                suppressWorldMousePrimaryClearAfterReleaseFrame = false;
            }
            else if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
            {
                // Stay suppressed through the release frame so Interaction LateUpdate skips short-click look.
                if (suppressWorldMousePrimaryClearAfterReleaseFrame)
                {
                    suppressWorldMousePrimaryUntilReleased = false;
                    suppressWorldMousePrimaryClearAfterReleaseFrame = false;
                }
                else
                {
                    suppressWorldMousePrimaryClearAfterReleaseFrame = true;
                }
            }
        }

        if (mousePrimedSpellIndex >= 0 && GameInput.LastActiveDevice != GameInputDevice.MouseKeyboard)
        {
            ClearMousePrimedSpellIfAny();
        }

        bool exploring
            = PlayerPanelState.IsEffectivelyExploringMagic
            && Conversations.runningConversation == null
            && (PlayerObject.Player.controlsDisabled & (EControlMask.SaveLoad | EControlMask.Map)) == 0;

        PlayerObject.DisableControls(EControlMask.Magic, exploring);

        if (exploring && !wasExploringMagic)
            RefreshRunestones();
        if (wasExploringMagic && !exploring)
        {
            holdTime = 0.0f;
        }
        wasExploringMagic = exploring;
        magicPanelExploring = exploring;

        // recover mana over time
        if (PlayerData.sData.gameTime > lastIncrementedMana + 5 * 60)
        {
            RestoreMana(Random.Range(1, 6) + Skills.GetSkill(ESkill.Mana) / 6 + PlayerData.sData.charLevel / 5);
            lastIncrementedMana += 5 * 60;
        }

        // update permanent spells
        if (PlayerData.sData.gameTime > lastCheckedPermanentSpells + 10)
        {
            foreach (SPermanentSpell spell in permanentSpells)
            {
                switch (spell.spell)
                {
                case ESpell.Regeneration:
                    PlayerObject.Player.RestoreHealth(1);
                    break;
                case ESpell.ManaRegeneration:
                    RestoreMana(1);
                    break;
                }
            }
            lastCheckedPermanentSpells += 10;
        }

        if (timeBeforeCanCastAgain > 0.0f)
        {
            timeBeforeCanCastAgain -= Time.deltaTime;
        }

        if (exploring)
        {
            holdTime = 2.0f;
            if (!PlayerData.sData.leftHanded)
            {
                StatsPanel.sStatsPanel.Hide();
            }

            int column = index % 4;
            bool padLeft = GameInput.DpadOrArrowLeftPressedThisFrame();
            bool padRight = GameInput.DpadOrArrowRightPressedThisFrame();
            bool padUp = GameInput.DpadOrArrowUpPressedThisFrame();
            bool padDown = GameInput.DpadOrArrowDownPressedThisFrame();
            if (padLeft && column > 0)
            {
                --index;
                PlayMoveSound();
            }
            else if (padRight && column < 3)
            {
                ++index;
                PlayMoveSound();
            }
            else if (padUp && index > 3)
            {
                index -= 4;
                PlayMoveSound();
            }
            else if (padDown && index < 20)
            {
                index += 4;
                PlayMoveSound();
            }

            if (GameInput.LastActiveDevice == GameInputDevice.Gamepad)
            {
                if ((GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false) && hasRunestone[index])
                {
                    if (castASpellWithTheseRunes)
                    {
                        spellInProgress.Clear();
                    }
                    if (spellInProgress.Count < 3)
                    {
                        spellInProgress.Add(index);
                        Utils.PlayClip2d(moveToRune);
                        castASpellWithTheseRunes = false;
                    }
                }

                if (GameInput.CurrentGamepad?.bButton.wasPressedThisFrame ?? false)
                {
                    spellInProgress.Clear();
                    Utils.PlayClip2d(clearSpell);
                }

                if ((GameInput.CurrentGamepad?.xButton.wasPressedThisFrame ?? false) && spellInProgress.Count > 1)
                    TryCastFromSpellRunes();
            }
        }

        if (!exploring
            && (PlayerObject.Player.controlsDisabled & EControlMask.Map) == 0
            && GameInput.MagicCastKeyboardConfirmPressedThisFrame())
        {
            TryCastFromSpellRunes();
        }

        if (holdTime > 0.0f)
        {
            holdTime -= Time.unscaledDeltaTime;
        }

        float lerpTarget = holdTime > 0.0f ? 1.0f : 0.0f;
        lerpIn = Utils.DampedApproachUnscaledTime(lerpIn, lerpTarget, 0.2f);

        for (int i = activeSpells.Count - 1; i >= 0; --i)
        {
            activeSpells[i].time -= Time.deltaTime;
            if (activeSpells[i].time < 0.0f)
            {
                RemoveSpell(i);
            }
        }

        if (bubbleTime > 0.0f)
        {
            bubbleTime -= Time.deltaTime;
            bubbleDelay = Random.Range(3.0f, 5.0f);
        }
        else
        {
            bubbleDelay -= Time.deltaTime;
            if (bubbleDelay < 0.0f)
            {
                bubbleTime = 2.0f;
            }
        }
    }

    private void LateUpdate()
    {
        // After OnGUI so <see cref="GuiInput.BlocksPointer"/> sees the magic panel; before <see cref="Interaction"/> LateUpdate.
        UpdateMousePrimedSpellAiming();
    }

    private void RefreshRunestones()
    {
        UUObject runebag = Inventory.sInv.FindObjectInInventory(EObjectType.RuneBag);
        for (int i = 0; i < 24; ++i)
        {
            hasRunestone[i] = false;
            char rune = (char)('A' + i + (i == 23 ? 1 : 0));
            char lowerCaseRune = (char)(rune + 32);
            if (Cheats.sCheats.giveRunestones.Contains(rune) || Cheats.sCheats.giveRunestones.Contains(lowerCaseRune))
            {
                hasRunestone[i] = true;
            }
            else if (runebag != null)
            {
                hasRunestone[i] = Inventory.sInv.FindObjectInInventoryRecursive(runebag.contents, EObjectType.RunestoneAn + i);
            }
        }
    }

    private static bool SpellUsesMousePrimedAiming(string runes)
    {
        switch (runes)
        {
        case "OJ":
        case "OG":
        case "PF":
        case "ZZZ":
        case "OWY":
        case "SJ":
        case "EY":
            return true;
        default:
            return false;
        }
    }

    private static bool SpellKeepsWandPrimedAfterAimConfirm(string runes)
    {
        switch (runes)
        {
        case "OJ":
        case "OG":
        case "PF":
        case "ZZZ":
            return true;
        default:
            return false;
        }
    }

    private void ClearMousePrimedSpellIfAny()
    {
        if (mousePrimedSpellIndex < 0)
        {
            return;
        }

        mousePrimedSpellIndex = -1;
        mousePrimedSpellWasCritical = false;
        mousePrimedWand = null;
        suppressMousePrimedAimUntilPrimaryReleased = false;
        suppressMousePrimedCancelUntilSecondaryReleased = false;
        suppressWorldMousePrimaryUntilReleased = false;
        suppressWorldMousePrimaryClearAfterReleaseFrame = false;
        SoftwareCursorOverlay.DefaultTextureOverride = null;
    }

    private static bool TryFindSpellIndexFromEnchantmentName(string enchantmentName, out int spellIndex)
    {
        spellIndex = -1;
        if (string.IsNullOrEmpty(enchantmentName))
        {
            return false;
        }

        EnsureSpellNamesBound();

        for (int i = 0; i < spells.Count; ++i)
        {
            if (spells[i].name == enchantmentName)
            {
                spellIndex = i;
                return true;
            }
        }

        return false;
    }

    private void ApplyPrimedSpellCursorTexture()
    {
        DataLoader dl = DataLoader.sDataLoader;
        if (dl != null && dl.cursorTex != null && dl.cursorTex.Length > 9 && dl.cursorTex[9] != null)
        {
            SoftwareCursorOverlay.DefaultTextureOverride = dl.cursorTex[9];
        }
        else
        {
            SoftwareCursorOverlay.DefaultTextureOverride = null;
        }
    }

    private void BeginMousePrimedSpell(int spellIndex, bool criticalSuccess, Wand wandOrNull)
    {
        ClearMousePrimedSpellIfAny();
        mousePrimedSpellIndex = spellIndex;
        mousePrimedSpellWasCritical = criticalSuccess;
        mousePrimedWand = wandOrNull;
        suppressMousePrimedAimUntilPrimaryReleased = true;
        if (wandOrNull != null)
        {
            suppressMousePrimedCancelUntilSecondaryReleased = true;
        }
        ApplyPrimedSpellCursorTexture();
        Utils.PlayClip2d(primeSpellSound);
    }

    /// <summary>Returns true when the spell is deferred for mouse aim (no mana spent yet).</summary>
    private bool TryBeginMousePrimedSpellAfterRuneSuccess(int spellIndex, bool criticalSuccess)
    {
        if (GameInput.LastActiveDevice != GameInputDevice.MouseKeyboard)
        {
            return false;
        }

        if (!SpellUsesMousePrimedAiming(spells[spellIndex].runes))
        {
            return false;
        }

        BeginMousePrimedSpell(spellIndex, criticalSuccess, null);

        return true;
    }

    /// <summary>Prime a wand spell for mouse aim (charge not consumed until aim confirm).</summary>
    public bool TryBeginMousePrimedSpellFromWand(Wand wand)
    {
        if (wand == null || GameInput.LastActiveDevice != GameInputDevice.MouseKeyboard)
        {
            return false;
        }

        if (!TryFindSpellIndexFromEnchantmentName(wand.enchantmentName, out int spellIndex))
        {
            return false;
        }

        if (!SpellUsesMousePrimedAiming(spells[spellIndex].runes))
        {
            return false;
        }

        BeginMousePrimedSpell(spellIndex, false, wand);

        return true;
    }

    private void FinalizeMousePrimedSpell(Vector3? worldAimDirection, UUObject nameEnchantInventoryItem)
    {
        int i = mousePrimedSpellIndex;
        if (i < 0)
        {
            return;
        }

        bool crit = mousePrimedSpellWasCritical;
        Wand primedWand = mousePrimedWand;
        bool keepWandPrimed = primedWand != null && SpellKeepsWandPrimedAfterAimConfirm(spells[i].runes);
        if (!keepWandPrimed)
        {
            mousePrimedSpellIndex = -1;
            mousePrimedSpellWasCritical = false;
            mousePrimedWand = null;
            SoftwareCursorOverlay.DefaultTextureOverride = null;
        }

        if (primedWand != null)
        {
            if (!primedWand || primedWand.ChargesRemaining <= 0)
            {
                if (keepWandPrimed)
                {
                    ClearMousePrimedSpellIfAny();
                }
                return;
            }

            timeBeforeCanCastAgain = spells[i].cost / 3.0f / PlayerData.sData.charLevel;
            if (nameEnchantInventoryItem != null && Inventory.sInv != null)
            {
                Inventory.sInv.SuppressInventoryMousePrimaryShortReleaseOnce();
            }

            if (Cast(i, false, worldAimDirection, nameEnchantInventoryItem))
            {
                primedWand.ConsumeChargeAfterUse();
            }
            if (keepWandPrimed)
            {
                if (!primedWand)
                {
                    ClearMousePrimedSpellIfAny();
                }
                else
                {
                    suppressMousePrimedAimUntilPrimaryReleased = true;
                }
            }
            suppressWorldMousePrimaryUntilReleased = true;
            return;
        }

        int cost = spells[i].cost;
        if (crit)
        {
            PlayerData.sData.mana -= cost / 2;
            bubbleTime = 3.0f;
        }
        else
        {
            PlayerData.sData.mana -= cost;
            bubbleTime = 2.0f;
        }

        castSpells[i] = true;
        timeBeforeCanCastAgain = spells[i].cost / 3.0f / PlayerData.sData.charLevel;
        if (nameEnchantInventoryItem != null && Inventory.sInv != null)
        {
            Inventory.sInv.SuppressInventoryMousePrimaryShortReleaseOnce();
        }

        Cast(i, false, worldAimDirection, nameEnchantInventoryItem);
        suppressWorldMousePrimaryUntilReleased = true;
    }

    private static bool ShouldPauseMousePrimedSpellAimingForModalUi()
    {
        if (MapScreen.IsMapScreenVisible())
        {
            return true;
        }

        if (PlayerObject.Player != null
            && (PlayerObject.Player.controlsDisabled & (EControlMask.HowMany | EControlMask.RepairDialog)) != 0)
        {
            return true;
        }

        if (Conversations.runningConversation != null)
        {
            return true;
        }

        if (PlayerObject.Player != null)
        {
            if ((PlayerObject.Player.controlsDisabled & EControlMask.SaveLoad) != 0)
            {
                return true;
            }

            if ((PlayerObject.Player.controlsDisabled & EControlMask.Keyboard) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateMousePrimedSpellAiming()
    {
        if (mousePrimedSpellIndex < 0)
        {
            return;
        }

        if (GameInput.LastActiveDevice != GameInputDevice.MouseKeyboard)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        if (TryCancelMousePrimedSpellOnSecondaryClick(mouse))
        {
            return;
        }

        if (ShouldPauseMousePrimedSpellAimingForModalUi())
        {
            return;
        }

        if (suppressMousePrimedAimUntilPrimaryReleased)
        {
            if (!mouse.leftButton.isPressed)
            {
                suppressMousePrimedAimUntilPrimaryReleased = false;
            }
            else
            {
                return;
            }
        }

        if (!mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        if (GuiInput.ShouldSuppressWorldMousePrimary())
        {
            return;
        }

        Vector2 guiMouse = GuiInput.MousePositionGuiSpace;
        string runes = spells[mousePrimedSpellIndex].runes;
        Camera cam = PlayerObject.Player.mainCamera;

        if (runes == "OWY"
            && Inventory.sInv != null
            && Inventory.sInv.TryGetNameEnchantmentInventoryTarget(guiMouse, out UUObject invObj)
            && invObj != null)
        {
            FinalizeMousePrimedSpell(null, invObj);
            return;
        }

        if (GuiInput.BlocksPointer(guiMouse))
        {
            return;
        }

        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        Vector3 dir = ray.direction;
        if (dir.sqrMagnitude < 1e-8f)
        {
            dir = cam.transform.forward;
        }
        else
        {
            dir.Normalize();
        }

        FinalizeMousePrimedSpell(dir, null);
    }

    private bool TryCancelMousePrimedSpellOnSecondaryClick(Mouse mouse)
    {
        if (!mouse.rightButton.wasReleasedThisFrame)
        {
            return false;
        }

        if (suppressMousePrimedCancelUntilSecondaryReleased)
        {
            if (!mouse.rightButton.isPressed)
            {
                suppressMousePrimedCancelUntilSecondaryReleased = false;
            }

            return false;
        }

        ClearMousePrimedSpellIfAny();
        return true;
    }

    private bool TryGetBestUuObjectAlongAimRay(Vector3 rayOrigin, Vector3 rayDir, out UUObject best, out float bestDist)
    {
        best = null;
        bestDist = float.MaxValue;
        rayDir.Normalize();
        const float maxDist = 10.0f;
        float radius = 0.1f;
        int fatRayMask =
            (1 << LayerMask.NameToLayer("Objects")) |
            (1 << LayerMask.NameToLayer("NonBlockingObject")) |
            (1 << LayerMask.NameToLayer("PhysicsDebris")) |
            (1 << LayerMask.NameToLayer("Characters"));

        RaycastHit[] envRayHits = Physics.RaycastAll(rayOrigin, rayDir, maxDist, LayerMasks.EnvironmentAndCeiling);
        RaycastHit[] fatHits = Physics.SphereCastAll(rayOrigin, radius, rayDir, maxDist, fatRayMask);

        float bestEnvNonUuDist = float.MaxValue;
        for (int h = 0; h < envRayHits.Length; h++)
        {
            RaycastHit hit = envRayHits[h];
            if (hit.collider.transform.root.GetComponent<UUObject>() != null)
            {
                continue;
            }

            if (hit.distance < bestEnvNonUuDist)
            {
                bestEnvNonUuDist = hit.distance;
            }
        }

        if (bestEnvNonUuDist >= float.MaxValue)
        {
            bestEnvNonUuDist = maxDist;
        }

        UUObject bestNonIncidental = null;
        float bestNonIncidentalDist = bestEnvNonUuDist;
        UUObject bestIncidental = null;
        float bestIncidentalDist = bestEnvNonUuDist;

        void ConsiderHit(RaycastHit hit)
        {
            UUObject obj = hit.collider.transform.root.GetComponent<UUObject>();
            if (obj == null)
            {
                return;
            }

            float dist = hit.distance;
            if (obj.isIncidental)
            {
                if (dist < bestIncidentalDist)
                {
                    bestIncidental = obj;
                    bestIncidentalDist = dist;
                }
            }
            else if (dist < bestNonIncidentalDist)
            {
                bestNonIncidental = obj;
                bestNonIncidentalDist = dist;
            }
        }

        for (int h = 0; h < envRayHits.Length; h++)
        {
            ConsiderHit(envRayHits[h]);
        }

        for (int h = 0; h < fatHits.Length; h++)
        {
            ConsiderHit(fatHits[h]);
        }

        if (bestNonIncidental != null)
        {
            best = bestNonIncidental;
            bestDist = bestNonIncidentalDist;
            return true;
        }

        if (bestIncidental != null)
        {
            best = bestIncidental;
            bestDist = bestIncidentalDist;
            return true;
        }

        return false;
    }

    private bool TryGetDoorAlongAimRay(Vector3 rayOrigin, Vector3 rayDir, out Door doorOut)
    {
        doorOut = null;
        rayDir.Normalize();
        const float maxDist = 10.0f;
        float radius = 0.1f;
        int fatRayMask =
            (1 << LayerMask.NameToLayer("Objects")) |
            (1 << LayerMask.NameToLayer("NonBlockingObject")) |
            (1 << LayerMask.NameToLayer("PhysicsDebris")) |
            (1 << LayerMask.NameToLayer("Characters"));

        RaycastHit[] envRayHits = Physics.RaycastAll(rayOrigin, rayDir, maxDist, LayerMasks.EnvironmentAndCeiling);
        RaycastHit[] fatHits = Physics.SphereCastAll(rayOrigin, radius, rayDir, maxDist, fatRayMask);
        float best = float.MaxValue;
        Door bestDoor = null;

        void TryHit(RaycastHit hit)
        {
            Door d = hit.collider.transform.root.GetComponent<Door>();
            if (d == null)
            {
                return;
            }

            if (hit.distance < best)
            {
                best = hit.distance;
                bestDoor = d;
            }
        }

        for (int h = 0; h < envRayHits.Length; h++)
        {
            TryHit(envRayHits[h]);
        }

        for (int h = 0; h < fatHits.Length; h++)
        {
            TryHit(fatHits[h]);
        }

        doorOut = bestDoor;
        return doorOut != null;
    }

    private void TryCastFromSpellRunes()
    {
        if (spellInProgress.Count <= 1)
        {
            return;
        }

        TutorialManager.NotifyCastAttempt();

        string spellToCast = "";
        foreach (int r in spellInProgress)
        {
            int rune = r == 23 ? 24 : r;
            spellToCast += (char)('A' + rune);
        }

        bool wasASpell = false;
        for (int i = 0; i < spells.Count; ++i)
        {
            if (spells[i].runes != spellToCast)
                continue;

            if (mousePrimedSpellIndex >= 0 && spells[i].runes != spells[mousePrimedSpellIndex].runes)
            {
                ClearMousePrimedSpellIfAny();
            }

            wasASpell = true;
            castASpellWithTheseRunes = true;

            if ((PlayerData.sData.charLevel + 1) / 2 < spells[i].circle && !Cheats.sCheats.castWithoutMana)
            {
                Messages.Add(1, 210);
            }
            else if ((!IsMagicAvailable() || PlayerData.sData.mana < spells[i].cost) && !Cheats.sCheats.castWithoutMana)
            {
                Messages.Add(1, 211);
            }
            else if (timeBeforeCanCastAgain > 0.0f)
            {
                Messages.Add(1, Random.Range(212, 214));
                Utils.PlayClip2d(castFailed);
            }
            else
            {
                int casting = Skills.GetSkill(ESkill.Casting);
                if (Cheats.sCheats.boostCasting)
                {
                    casting += 20;
                }
                Skills.ESkillTestResult result = Skills.GetResult(casting, spells[i].cost);
                switch (result)
                {
                case Skills.ESkillTestResult.CriticalFailure:
                    Messages.Add(1, 214);
                    PlayerObject.Player.Damage(Skills.ESkillTestResult.Success, Random.Range(1, 6), EDamageType.Damage);
                    break;
                case Skills.ESkillTestResult.Failure:
                    Messages.Add(1, Random.Range(212, 214));
                    Utils.PlayClip2d(castFailed);
                    break;
                case Skills.ESkillTestResult.Success:
                    if (TryBeginMousePrimedSpellAfterRuneSuccess(i, false))
                    {
                        break;
                    }

                    PlayerData.sData.mana -= spells[i].cost;
                    bubbleTime = 2.0f;
                    TryCast(i);
                    castSpells[i] = true;
                    break;
                case Skills.ESkillTestResult.CriticalSuccess:
                    if (TryBeginMousePrimedSpellAfterRuneSuccess(i, true))
                    {
                        break;
                    }

                    PlayerData.sData.mana -= spells[i].cost / 2;
                    bubbleTime = 3.0f;
                    TryCast(i);
                    castSpells[i] = true;
                    break;
                }
            }
            break;
        }

        if (!wasASpell)
            Messages.Add("Not a spell.");
    }

    private void RemoveSpell(int _index)
    {
        int spell = activeSpells[_index].spell;
        activeSpells.RemoveAt(_index);

        switch ((ESpell)spell)
        {
        case ESpell.NightVision:
            if (!IsSpellActive(ESpell.NightVision)) // make sure no other night vision spells active
            {
                PostProcessVolume vol = PlayerObject.Player.GetComponentInChildren<PostProcessVolume>();
                if (vol != null)
                {
                    if (vol.profile.TryGetSettings(out ColorGrading colorGrading))
                    {
                        colorGrading.enabled.Override(false);
                    }
                }
            }
            break;
        }
    }

    public bool TryCast(string spellName, bool anonymous = false)
    {
        EnsureSpellNamesBound();

        for (int i = 0; i < spells.Count; ++i)
        {
            if (spells[i].name == spellName)
            {
                return TryCast(i, anonymous);
            }
        }

        return false;
    }

    public bool TryCast(int i, bool anonymous = false)
    {
        // level / char level
        timeBeforeCanCastAgain = spells[i].cost / 3.0f / PlayerData.sData.charLevel;

        // duration spell?
        if (spells[i].icon >= 0)
        {
            if (activeSpells.Count == 3)
            {
                // remove one. use some heuristics to determine which
                // first, if there's already a similar spell, replace it
                for (int j = 0; j < activeSpells.Count; ++j)
                {
                    // same spell
                    if (activeSpells[j].spell == i)
                    {
                        RemoveSpell(j);
                        break;
                    }

                    // check for similar but weaker
                    for (int k = 0; k < similarSpells.Length; ++k)
                    {
                        for (int l = 0; l < similarSpells[k].Length; ++l)
                        {
                            if (spells[i].runes == similarSpells[k][l])
                            {
                                for (int m = l + 1; m < similarSpells[k].Length; ++m)
                                {
                                    if (spells[activeSpells[j].spell].runes == similarSpells[k][m])
                                    {
                                        // found one weaker than the current cast
                                        RemoveSpell(j);
                                        break;
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
            }

            if (activeSpells.Count == 3)
            {
                // lowest circle spell
                int lowestCost = 27;
                int lowestIndex = 0;
                for (int j = 0; j < activeSpells.Count; ++j)
                {
                    if (spells[activeSpells[j].spell].cost < lowestCost)
                    {
                        lowestCost = spells[activeSpells[j].spell].cost;
                        lowestIndex = j;
                    }
                }
                RemoveSpell(lowestIndex);
            }

            SActiveSpell activeSpell = new();
            activeSpell.spell = i;
            // determine time (could depend on skills)
            activeSpell.time = Random.Range(0.8f, 1.1f) * spells[i].duration;
            activeSpells.Add(activeSpell);
            
            PlayCastSound(i);

            if (spells[activeSpell.spell].runes == "QL") // night vision
            {
                PostProcessVolume vol = PlayerObject.Player.GetComponentInChildren<PostProcessVolume>();
                if (vol != null)
                {
                    if (vol.profile.TryGetSettings(out ColorGrading colorGrading))
                    {
                        colorGrading.enabled.Override(true);
                    }
                }
            }
            else if (spells[activeSpell.spell].runes == "OPW") // roaming sight
            {
                CastRoamingSight();
            }

            return true;
        }

        return Cast(i, anonymous);
    }

    private AudioClip GetCastSoundClip(int _index)
    {
        if (castSpellSound != null && _index >= 0 && _index < castSpellSound.Length && castSpellSound[_index] != null)
        {
            return castSpellSound[_index];
        }

        if (defaultCastSound != null)
        {
            return defaultCastSound;
        }

        return null;
    }

    private void PlayCastSound(int _index)
    {
        AudioClip clip = GetCastSoundClip(_index);
        if (clip != null)
        {
            Utils.PlayClip2d(clip);
        }
    }

    private void PlayCastSound2d(int _index)
    {
        AudioClip clip = GetCastSoundClip(_index);
        if (clip != null)
        {
            Utils.PlayClip2d(clip);
        }
    }

    bool Cast(int _index, bool anonymous, Vector3? mouseWorldAimDirOpt = null, UUObject nameEnchantInventoryItem = null)
    {
        bool success = true;

        switch (spells[_index].runes)
        {
        case "IBM":
            CastLesserHeal();
            break;
        case "IM":
            CastHeal();
            break;
        case "VIM":
            CastGreaterHeal();
            break;
        case "IMY":
            CastCreateFood();
            break;
        case "OJ":
            CastProjectile(EObjectType.MagicMissile, mouseWorldAimDirOpt);
            break;
        case "OG":
            CastProjectile(EObjectType.LightningBolt, mouseWorldAimDirOpt);
            break;
        case "PF":
            CastProjectile(EObjectType.Fireball, mouseWorldAimDirOpt);
            break;
        case "ZZZ":
            CastProjectile(EObjectType.Acid, mouseWorldAimDirOpt);
            break;
        case "QC":
            CastCreateFear();
            break;
        case "WM":
            CastDetectMonster();
            break;
        case "AN":
            CastCurePoison();
            break;
        case "IJ":
            CastRuneOfWarding();
            break;
        case "VPY":
            CastTremor();
            break;
        case "OAQ":
            CastReveal();
            break;
        case "OWY":
            success = CastNameEnchantment(mouseWorldAimDirOpt, nameEnchantInventoryItem);
            break;
        case "SJ":
            CastStrengthenDoor(mouseWorldAimDirOpt);
            break;
        case "VRP":
            CastGateTravel();
            break;
        case "NM":
            CastPoison();
            break;
        case "EY":
            CastOpen(mouseWorldAimDirOpt);
            break;
        case "ACM":
            CastSmiteUndead();
            break;
        case "AEP":
            CastParalyze();
            break;
        case "VKC":
            CastArmageddon();
            break;
        case "IMR":
            CastAlly();
            break;
        case "VAW":
            CastConfusion();
            break;
        case "KM":
            CastSummonMonster();
            break;
        case "VOG":
            CastSheetLightning();
            break;
        case "FH":
            CastFlameWind();
            break;
        case "AS":
            CastCurse();
            break;
        }

        if (success)
        {
            if (!anonymous)
            {
                Messages.Add($"Casting {spells[_index].name}.");
            }

            if (nameEnchantInventoryItem != null)
            {
                PlayCastSound2d(_index);
            }
            else
            {
                PlayCastSound(_index);
            }
        }

        return success;
    }

    void CastLesserHeal()
    {
        // success depends on Casting skill (determined elsewhere)
        // amount to heal depends on Mana skill?
        // possibly also depends on Vitality

        PlayerObject.Player.RestoreHealth(PlayerData.sData.vitality / 4);
    }

    void CastHeal()
    {
        // since heal is twice the mana cost, to make it worthwhile it should be more than twice the benefit
        // for lesser vs heal vs greater:
        // mana cost is 6, 12, 18
        // healing is 2/8 * vit, 5/8 * vit, 8/8 * vit
        PlayerObject.Player.RestoreHealth(5 * PlayerData.sData.vitality / 8);
    }

    void CastGreaterHeal()
    {
        // restore all health
        PlayerObject.Player.RestoreHealth(PlayerData.sData.vitality);
    }

    void CastCurePoison()
    {
        PlayerData.sData.poison = 0;
    }

    void CastCreateFood()
    {
        // find a spot on the ground if possible (could be jumping or flying or swimming)
        // if you are swimming it will just splash and disappear I guess
        // start at 4m away so it's visible if you are looking straight ahead
        Vector3 start = PlayerObject.Player.transform.position + Vector3.up;
        Vector3 dir = PlayerObject.Player.transform.forward;
        int layerMask = LayerMasks.EnvironmentAndCeiling | (1 << LayerMask.NameToLayer("Objects"));
        RaycastHit hit;
        bool bam = Physics.SphereCast(start, 0.25f, dir, out hit, 4.0f, layerMask);
        start += dir * (bam ? hit.distance : 4.0f);
        bam = Physics.SphereCast(start, 0.2f, Vector3.down, out hit, 2.0f, layerMask);
        start += Vector3.down * (bam ? hit.distance : 2.0f);
        // spawn food here - weight heavily towards fish, since these are specifically needed for a couple of puzzles
        EObjectType foodToSpawn = Random.value < 0.5f ? EObjectType.Fish : (EObjectType)Random.Range((int)EObjectType.PieceOfMeat, (int)EObjectType.Fish); 
        UUObject obj = LevelLoader.CreateObjectOfType(foodToSpawn);
        obj.transform.position = start;
        obj.quality = 48; // fresh
        obj.PostLoadInitialize(); // Initialize name properties so food has proper inspect name
        LevelLoader.AddToWorld(obj);

        ParticleSpawner.SpawnParticle(EParticleType.MagicCreateFood, start);
    }

    void CastProjectile(EObjectType type, Vector3? aimDirFromCameraOpt = null)
    {
        // aim the projectile at whatever is being looked at
        int layerMask = LayerMasks.EnvironmentAndCeiling | (1 << LayerMask.NameToLayer("Characters"));
        Vector3 start = PlayerObject.Player.mainCamera.transform.position;
        Vector3 dir = aimDirFromCameraOpt ?? PlayerObject.Player.mainCamera.transform.forward;
        if (dir.sqrMagnitude < 1e-8f)
        {
            dir = PlayerObject.Player.mainCamera.transform.forward;
        }
        else
        {
            dir.Normalize();
        }

        float maxDistance = 20.0f;
        Vector3 target = start + maxDistance * dir;
        RaycastHit hit;
        if (Physics.Raycast(start, dir, out hit, maxDistance, layerMask))
        {
            target = hit.point;
        }
        start = PlayerObject.Player.transform.position + 0.6f * Vector3.up;
        dir = (target - start).normalized;
        
        Projectile proj = LevelLoader.CreateObjectOfType(type) as Projectile;
        if (proj != null)
        {
            // Initialize name properties so projectile has proper name
            proj.PostLoadInitialize();
            proj.projectileOwner = PlayerObject.Player.gameObject;
            proj.gameObject.transform.SetPositionAndRotation(start, Quaternion.LookRotation(dir, Vector3.up));
            LevelLoader.AddToWorld(proj);
            Rigidbody rb = proj.gameObject.GetComponent<Rigidbody>();
            if (proj != null)
            {
                // set velocity
                rb.linearVelocity = 10.0f * dir;
            }
            proj.doDamage = true;
        }
    }

    void CastCreateFear()
    {
        // find enemies around and tell them to flee
        int count = Physics.OverlapSphereNonAlloc(PlayerObject.Player.transform.position, 3.0f * Tile.xzScale,
                     cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
        for (int i = 0; i < count; i++)
        {
            Collider col = cachedColliders[i];
            if (col is CharacterController)
            {
                Critter critter = col.transform.root.gameObject.GetComponent<Critter>();
                if (critter != null && (critter.attitude is Critter.EAttitude.Upset or Critter.EAttitude.Hostile))
                {
                    critter.CreateFear();
                }
            }
        }
    }

    void CastDetectMonster()
    {
        // find enemies around and report approximate position
        int[] critters = { 0, 0, 0, 0, 0, 0, 0, 0 };
        int count = Physics.OverlapSphereNonAlloc(PlayerObject.Player.transform.position, 6.0f * Tile.xzScale,
                     cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
        for (int i = 0; i < count; i++)
        {
            Collider col = cachedColliders[i];
            if (col is CharacterController)
            {
                Critter critter = col.transform.root.gameObject.GetComponent<Critter>();
                if (critter != null && (critter.attitude is Critter.EAttitude.Upset or Critter.EAttitude.Hostile))
                {
                    Vector3 offset = critter.transform.position - PlayerObject.Player.transform.position;
                    // Zero out Y component to ensure horizontal-only direction calculation
                    offset.y = 0.0f;
                    // Skip if critter is at exact same horizontal position (directly above/below)
                    if (offset.sqrMagnitude > 0.001f)
                    {
                        ++critters[Utils.OffsetToOctant(offset)];
                    }
                }
            }
        }
        int largestOctant = 0;
        int numCrittersInLargestOctant = 0;
        for (int i = 0; i < 8; ++i)
        {
            if (critters[i] > numCrittersInLargestOctant)
            {
                numCrittersInLargestOctant = critters[i];
                largestOctant = i;
            }
        }

        if (numCrittersInLargestOctant == 0)
        {
            Messages.Add(1, 62);
        }
        else
        {
            int _index = numCrittersInLargestOctant == 1 ? 59 : (numCrittersInLargestOctant < 4 ? 60 : 61);
            string mess = StringLoader.GetString(1, _index) + StringLoader.GetString(1, 36 + largestOctant);
            Messages.Add(mess);
        }
    }

    void CastRuneOfWarding()
    {
        UUObject rune = Instantiate(runeOfWarding);
        if (rune != null)
        {
            // find a spot in front of the player to place the rune
            int environmentLayerMask = LayerMasks.EnvironmentAndCeiling;

            Vector3 startPos = PlayerObject.Player.transform.position;
            Vector3 forward = PlayerObject.Player.transform.forward;
            Vector3 runePos = startPos + 2.0f * forward;
            if (Physics.Raycast(startPos, forward, out RaycastHit wallHit, 3.0f, environmentLayerMask))
            {
                runePos = startPos + (wallHit.distance - 1.0f) * forward;
            }
            
            rune.transform.SetPositionAndRotation(runePos, PlayerObject.Player.transform.rotation);
            LevelLoader.AddToWorld(rune);
            Messages.Add(1, 276); // placed
        }
    }

    public void CastTremor()
    {
        // Search for boulders in a 5m radius around the player and break them
        // Boulders can be in either Environment or Objects layer, but not Ceiling
        int boulderLayerMask = LayerMasks.EnvironmentOnly | (1 << LayerMask.NameToLayer("Objects"));
        int boulderCount = Physics.OverlapSphereNonAlloc(PlayerObject.Player.transform.position, 6.0f,
                     cachedColliders, boulderLayerMask);
        int brokenCount = 0;
        for (int i = 0; i < boulderCount; ++i)
        {
            Collider col = cachedColliders[i];
            Boulder boulder = col.transform.root.gameObject.GetComponent<Boulder>();
            if (boulder != null)
            {
                boulder.Break();
                if (++brokenCount > 3)
                {
                    break;
                }
            }
        }

        // choose a bunch of random points around the player
        for (int i = 0; i < 12; ++i)
        {
            Vector2 off = 4.0f * Tile.xzScale * Random.insideUnitCircle;
            Vector3 pos = PlayerObject.Player.transform.position + new Vector3(off.x, 0.0f, off.y);
            int tx = Tile.GetTileX(pos.x);
            int ty = Tile.GetTileY(pos.z);
            Tile t = LevelLoader.GetTile(tx, ty);
            if (t != null && t.type != 0)
            {
                pos.y = 14.0f * Tile.yScale;

                // Cast upward to find the ceiling and spawn ceiling dust
                int environmentLayerMask = LayerMasks.EnvironmentAndCeiling;
                float maxDistance = 50.0f; // Reasonable ceiling height
                    
                if (Physics.Raycast(pos, Vector3.up, out RaycastHit ceilingHit, maxDistance, environmentLayerMask))
                {
                    // Spawn ceiling dust particle at the ceiling hit point
                    ParticleSpawner.SpawnParticle(EParticleType.CeilingDust, ceilingHit.point);
                }
                
                UUObject rock = LevelLoader.CreateObjectOfType(EObjectType.MediumBoulder + Random.Range(0, 3));
                if (rock != null)
                {
                    // Initialize name properties so rock has proper name
                    rock.PostLoadInitialize();
                    rock.transform.root.SetPositionAndRotation(pos, Quaternion.identity);
                    rock.isStatic = false;
                    LevelLoader.AddToWorld(rock);
                }
            }
        }

        // find enemies and hurt them, set them to flee/confuse
        int count = Physics.OverlapSphereNonAlloc(PlayerObject.Player.transform.position, 4.0f * Tile.xzScale,
                     cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
        for (int i = 0; i < count; i++)
        {
            Collider col = cachedColliders[i];
            if (col is CharacterController)
            {
                Critter critter = col.transform.root.gameObject.GetComponent<Critter>();
                if (critter != null && critter.IsDamageable()
                    && critter.attitude == Critter.EAttitude.Hostile && critter.type != EObjectType.SlasherOfVeils)
                {
                    critter.TryDamage(5, Skills.ESkillTestResult.Success);
                    critter.CreateFear();
                }
            }
        }

        StartCoroutine(Tremor());
    }

    private IEnumerator Tremor()
    {
        Camera cam = PlayerObject.Player.GetComponentInChildren<Camera>();
        if (cam != null)
        {
            float timeRemaining = 5.0f;

            while (timeRemaining > 0.0f)
            {
                timeRemaining -= Time.deltaTime;
                Vector3 euler = cam.transform.localEulerAngles;
                euler.z = Random.Range(-10.0f, 10.0f);
                cam.transform.localEulerAngles = euler;
                yield return null;
            }
            
            // Reset camera roll to zero after tremor ends
            Vector3 finalEuler = cam.transform.localEulerAngles;
            finalEuler.z = 0.0f;
            cam.transform.localEulerAngles = finalEuler;
        }
    }

    private void CastReveal()
    {
        int tileX = Tile.GetTileX(PlayerObject.Player.transform.position.x);
        int tileY = Tile.GetTileY(PlayerObject.Player.transform.position.z);
        int radius = 2;
        for (int x = Mathf.Max(0, tileX - radius); x <= Mathf.Min(tileX + radius, 63); ++x)
        {
            for (int y = Mathf.Max(0, tileY - radius); y <= Mathf.Min(tileY + radius, 63); ++y)
            {
                Tile t = LevelLoader.GetTile(x, y);

                // check tile contents for decal on the right face
                int o = t.firstObject;
                while (o != 0)
                {
                    UUObject obj = LevelLoader.GetObj(o);
                    if (obj != null)
                    {
                        Decal decal = obj as Decal;
                        if (decal != null && (decal.angle & 1) == 0 && decal.isActiveAndEnabled)
                        {
                            // look at the tile behind the decal
                            int behindX = Tile.GetTileX(decal.transform.position.x - decal.transform.forward.x);
                            int behindY = Tile.GetTileY(decal.transform.position.z - decal.transform.forward.z);
                            Tile b = LevelLoader.GetTile(behindX, behindY);
                            if (b != null && b.type == 1)
                            {
                                decal.TryChainInteraction(EAction.Look);
                                break;
                            }
                        }
                    }
                    o = LevelLoader.sLevelLoader.GetChainIndexFromObjectData(o);
                }
            }
        }
    }

    bool CastNameEnchantment(Vector3? aimDirFromCameraOpt = null, UUObject inventoryItem = null)
    {
        if (inventoryItem != null)
        {
            inventoryItem.NameEnchantment(spawnParticleEffect: false);
            return true;
        }

        if (aimDirFromCameraOpt.HasValue)
        {
            Camera c = PlayerObject.Player.mainCamera;
            Vector3 d = aimDirFromCameraOpt.Value;
            if (d.sqrMagnitude < 1e-8f)
            {
                d = c.transform.forward;
            }
            else
            {
                d.Normalize();
            }

            if (TryGetBestUuObjectAlongAimRay(c.transform.position, d, out UUObject picked, out _)
                && picked != null)
            {
                picked.NameEnchantment();
                return true;
            }

            Messages.Add("You must click on an item to identify.");
            return false;
        }

        if (Interaction.sInt.centeredObject != null)
        {
            Interaction.sInt.centeredObject.NameEnchantment();
            return true;
        }

        Messages.Add("You must be looking at an item to identify.");
        return false;
    }

    void CastStrengthenDoor(Vector3? aimDirFromCameraOpt = null)
    {
        if (aimDirFromCameraOpt.HasValue)
        {
            Camera c = PlayerObject.Player.mainCamera;
            Vector3 d = aimDirFromCameraOpt.Value;
            if (d.sqrMagnitude < 1e-8f)
            {
                d = c.transform.forward;
            }
            else
            {
                d.Normalize();
            }

            if (TryGetDoorAlongAimRay(c.transform.position, d, out Door door) && door != null)
            {
                door.Spike();
            }

            return;
        }

        if (Interaction.sInt.centeredObject != null)
        {
            Door door = Interaction.sInt.centeredObject as Door;
            if (door != null)
            {
                door.Spike();
            }
        }
    }

    void CastGateTravel()
    {
        if (PlayerData.sData.moonstoneDropped)
        {
            float horizontalDistance = (PlayerObject.Player.mainCamera.transform.position - PlayerData.sData.moonstoneDroppedPosition).magnitude;
            float verticalDistance = 7.0f * Mathf.Abs(LevelLoader.sLevelLoader.loadedLevel - PlayerData.sData.moonstoneDroppedLevel);
            float totalDistance = horizontalDistance + verticalDistance;
            if (totalDistance > 10.0f)
            {
                PlayerData.sData.gateTravelDistance += totalDistance;
            }
            LevelLoader.sLevelLoader.ChangeLevel(PlayerData.sData.moonstoneDroppedLevel, PlayerData.sData.moonstoneDroppedPosition + Vector3.up);
        }
        else
        {
            Messages.Add(1, 271);
        }
    }

    void CastPoison()
    {
        // find enemies around and poison them
        float radius = 3.0f * Tile.xzScale;
        int count = Physics.OverlapSphereNonAlloc(
                     PlayerObject.Player.mainCamera.transform.position + radius * PlayerObject.Player.mainCamera.transform.forward, radius,
                     cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
        for (int i = 0; i < count; i++)
        {
            Collider col = cachedColliders[i];
            if (col is CharacterController)
            {
                Critter critter = col.transform.root.gameObject.GetComponent<Critter>();
                if (critter != null && (critter.attitude is Critter.EAttitude.Upset or Critter.EAttitude.Hostile))
                {
                    critter.Poison();
                }
            }
        }
    }

    void CastOpen(Vector3? aimDirFromCameraOpt = null)
    {
        // find doors/chests around and open them
        float radius = 3.0f * Tile.xzScale;
        Vector3 fwd = aimDirFromCameraOpt ?? PlayerObject.Player.mainCamera.transform.forward;
        if (fwd.sqrMagnitude < 1e-8f)
        {
            fwd = PlayerObject.Player.mainCamera.transform.forward;
        }
        else
        {
            fwd.Normalize();
        }

        int count = Physics.OverlapSphereNonAlloc(
                     PlayerObject.Player.mainCamera.transform.position + radius * fwd, radius,
                     cachedColliders, LayerMasks.EnvironmentAndCeiling);
        List<Lockable> lockablesAttempted = new List<Lockable>();
        for (int i = 0; i < count; i++)
        {
            Collider col = cachedColliders[i];
            Lockable lockable = col.transform.root.gameObject.GetComponent<Lockable>();
            if (lockable != null && lockable.Locked(out UUObject lockObj) && !lockablesAttempted.Contains(lockable))
            {
                lockablesAttempted.Add(lockable);
                if (lockable is Door)
                {
                    Quaternion q = lockable.transform.rotation;
                    // maybe rotate the particle 180 so it's on the same side of the door as the player
                    if (Vector3.Dot(PlayerObject.Player.mainCamera.transform.position - lockable.transform.position, lockable.transform.forward) < 0.0f)
                    {
                        q *= Quaternion.AngleAxis(180.0f, Vector3.up);
                    }
                    ParticleSpawner.SpawnParticle(EParticleType.MagicOpenDoor, lockable.transform.position, q);
                }
                else if (lockable is Chest)
                {
                    ParticleSpawner.SpawnParticle(EParticleType.MagicOpenChest, lockable.transform.position, lockable.transform.rotation);
                }
                lockable.Unlock();
            }
        }
    }

    void CastSmiteUndead()
    {
        // find enemies around and if undead, smite
        float radius = 3.0f * Tile.xzScale;
        int count = Physics.OverlapSphereNonAlloc(
                     PlayerObject.Player.mainCamera.transform.position + radius * PlayerObject.Player.mainCamera.transform.forward, radius,
                     cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
        for (int i = 0; i < count; i++)
        {
            Collider col = cachedColliders[i];
            if (col is CharacterController)
            {
                Critter critter = col.transform.root.gameObject.GetComponent<Critter>();
                if (critter != null && (critter.type is EObjectType.Skeleton or EObjectType.GhostA or EObjectType.GhostB or EObjectType.GhostC or EObjectType.DireGhost))
                {
                    critter.TryDamage(99, Skills.ESkillTestResult.Success);
                }
            }
        }
    }

    void CastParalyze()
    {
        // find enemies around and if undead, smite
        float radius = 3.0f * Tile.xzScale;
        int count = Physics.OverlapSphereNonAlloc(
                     PlayerObject.Player.mainCamera.transform.position + radius * PlayerObject.Player.mainCamera.transform.forward, radius,
                     cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
        for (int i = 0; i < count; i++)
        {
            Collider col = cachedColliders[i];
            if (col is CharacterController)
            {
                Critter critter = col.transform.root.gameObject.GetComponent<Critter>();
                if (critter != null)
                {
                    critter.TryParalyze();
                }
            }
        }
    }

    void CastCurse()
    {
        float radius = 3.0f * Tile.xzScale;
        int count = Physics.OverlapSphereNonAlloc(
                     PlayerObject.Player.mainCamera.transform.position + radius * PlayerObject.Player.mainCamera.transform.forward, radius,
                     cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
        for (int i = 0; i < count; i++)
        {
            Collider col = cachedColliders[i];
            if (col is CharacterController)
            {
                Critter critter = col.transform.root.gameObject.GetComponent<Critter>();
                if (critter != null)
                {
                    critter.TryCurse();
                }
            }
        }
    }

    void CastRoamingSight()
    {
        StartCoroutine(RoamingSight());
    }

    private void RoamingSightControls(Camera c, ref float yaw, ref float yawVel, ref float pitch, ref Vector3 pos, ref Vector3 vel)
    {
        pos.y = Utils.DampedApproach(pos.y, 14.0f, 1.0f);

        Vector2 kbMove = PlayerInput.ReadMoveVector();
        Vector2 gpMove = GameInput.CurrentGamepad?.leftStick.ReadValue() ?? Vector2.zero;
        Vector2 moveControl = kbMove.sqrMagnitude > 0.0001f ? kbMove : gpMove;

        // left/right at 75%, backward at 50% of forward speed
        Vector3 desiredMove = 6.0f * Utils.DeadZone(moveControl.x) * c.transform.right
            + 9.0f * (moveControl.y > 0.0f ? 1.0f : 0.5f) * Utils.DeadZone(moveControl.y) * c.transform.forward;
        vel.x = Utils.DampedApproach(vel.x, desiredMove.x, 1.0f);
        pos.x += vel.x * Time.deltaTime;
        vel.z = Utils.DampedApproach(vel.z, desiredMove.z, 1.0f);
        pos.z += vel.z * Time.deltaTime;

        float targetYawVel = 45.0f * (GameInput.CurrentGamepad?.rightStick.ReadValue().x ?? 0.0f);
        if (PlayerInput.ShouldApplyMouseLook() && GameInput.CurrentMouse != null)
        {
            float sens = PlayerInput.MouseLookSpeed * roamingSightMouseSensitivity;
            targetYawVel += GameInput.CurrentMouse.delta.ReadValue().x * sens;
        }

        yawVel = Utils.DampedApproach(yawVel, targetYawVel, 1.0f);
        yaw += yawVel * Time.deltaTime;
        pitch = Utils.DampedApproach(pitch, 30.0f, 1.0f);
        gameObject.transform.position = pos;
        gameObject.transform.eulerAngles = new Vector3(pitch, yaw, 0.0f);
    }

    private IEnumerator RoamingSight()
    {
        // adding the camera to the Magic object - seems ok
        Camera c = gameObject.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = Color.black;
        Light l = c.gameObject.AddComponent<Light>();
        l.range = 30.0f;
        l.intensity = 1.3f;
        Camera old = PlayerObject.Player.mainCamera;
        old.tag = "Untagged";
        tag = "MainCamera";
        float yaw = old.transform.eulerAngles.y;
        float pitch = old.transform.eulerAngles.x;
        Vector3 pos = old.transform.position;
        Vector3 vel = Vector3.zero;
        float yawVel = 0.0f;
        PlayerObject.DisableControls(EControlMask.RoamingSight, true);
        
        // Hide the active weapon (or fist if no weapon equipped)
        EInvSlot weaponSlot = PlayerData.sData.leftHanded ? EInvSlot.LeftHand : EInvSlot.RightHand;
        WeaponBase weapon = Inventory.sInv.invSlotContents[(int)weaponSlot] as WeaponBase;
        if (weapon == null)
        {
            // Fist is equipped when weapon slot is null
            weapon = Inventory.sInv.fist;
        }
        if (weapon != null && weapon.gameObject.activeSelf)
        {
            hiddenWeapon = weapon;
            hiddenWeapon.gameObject.SetActive(false);
        }
        
        while (IsSpellActive(ESpell.RoamingSight))
        {
            RoamingSightControls(c, ref yaw, ref yawVel, ref pitch, ref pos, ref vel);
            yield return null;
        }

        // fade down to black
        PlayerObject.Player.fadeIn = false;

        while (PlayerObject.Player.fade < 1.0f)
        {
            PlayerObject.Player.fade += Time.unscaledDeltaTime;
            RoamingSightControls(c, ref yaw, ref yawVel, ref pitch, ref pos, ref vel);
            yield return null;
        }
        PlayerObject.Player.fade = 1.0f;

        tag = "Untagged";
        old.tag = "MainCamera";

        // wait a frame before destroying camera
        yield return null;

        Destroy(c);
        Destroy(l);

        PlayerObject.DisableControls(EControlMask.RoamingSight, false);
        
        // Show the weapon again
        if (hiddenWeapon != null)
        {
            hiddenWeapon.gameObject.SetActive(true);
            hiddenWeapon = null;
        }

        // fade up from black
        while (PlayerObject.Player.fade > 0.0f)
        {
            PlayerObject.Player.fade -= Time.unscaledDeltaTime;
            yield return null;
        }
        PlayerObject.Player.fade = 0.0f;
    }

    private void CastArmageddon()
    {
        // destroy all UUObjects
        Inventory.sInv.Clear();
        ClearRunestones();

        foreach (UUObject o in FindObjectsByType<UUObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // don't like how the door includes the frame - maybe move the frame to a separate object without a UUObject.
            // it does remove all locks so doors are immediately openable
            if (!(o is Decal or Door or Bridge))
            {
                if (o is Critter)
                {
                    Utils.DestroyCritter((Critter)o);
                }
                else
                {
                    Utils.DestroyItem(o);
                }
            }
        }
    }

    private void CastAlly()
    {
        // find enemies around and have them ally with the player
        float radius = 3.0f * Tile.xzScale;
        int count = Physics.OverlapSphereNonAlloc(
                     PlayerObject.Player.mainCamera.transform.position + radius * PlayerObject.Player.mainCamera.transform.forward, radius,
                     cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
        for (int i = 0; i < count; i++)
        {
            Collider col = cachedColliders[i];
            if (col is CharacterController)
            {
                Critter critter = col.transform.root.gameObject.GetComponent<Critter>();
                if (critter != null)
                {
                    critter.TryAlly();
                }
            }
        }
    }

    private void CastConfusion()
    {
        // find enemies around and confuse them
        float radius = 3.0f * Tile.xzScale;
        int count = Physics.OverlapSphereNonAlloc(
                     PlayerObject.Player.mainCamera.transform.position + radius * PlayerObject.Player.mainCamera.transform.forward, radius,
                     cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
        for (int i = 0; i < count; i++)
        {
            Collider col = cachedColliders[i];
            if (col is CharacterController)
            {
                Critter critter = col.transform.root.gameObject.GetComponent<Critter>();
                if (critter != null)
                {
                    critter.TryConfuse();
                }
            }
        }
    }

    void CastSummonMonster()
    {
        // find a spot on the ground if possible (could be jumping or flying or swimming)
        // if you are swimming it will just splash and disappear I guess, unless it's a flyer
        // start at 4m away so it's visible if you are looking straight ahead
        Vector3 start = PlayerObject.Player.transform.position + Vector3.up;
        Vector3 dir = PlayerObject.Player.transform.forward;
        int layerMask = LayerMasks.EnvironmentAndCeiling | (1 << LayerMask.NameToLayer("Objects"));
        RaycastHit hit;
        bool bam = Physics.SphereCast(start, 0.25f, dir, out hit, 4.0f, layerMask);
        start += dir * (bam ? hit.distance : 4.0f);
        bam = Physics.SphereCast(start, 0.2f, Vector3.down, out hit, 2.0f, layerMask);
        start += Vector3.down * (bam ? hit.distance : 2.0f);

        // spawn monster here - just the ones that don't talk in the game
        EObjectType typeToSpawn = PlayerObject.Player.GetRandomEncounteredCritter();
        if (typeToSpawn != 0)
        {
            // TODO: spawn flyer if on water, or if over ledge
            CritterEncounterData? encounterData = PlayerObject.Player.GetEncounteredCritterData(typeToSpawn);
            if (encounterData != null)
            {
                // Create dummy objData (unlink it)
                ushort[] objData = { (ushort)((int)typeToSpawn | (1 << 15)), 0, 40, 0 };
                // Read critterData from the level where this critter was encountered
                byte[] critterData = LevelLoader.sLevelLoader.ReadCritterDataFromLevel(encounterData.Value.level, encounterData.Value.objectIndex);
                
                if (critterData != null)
                {
                    Critter obj = LevelLoader.sLevelLoader.CreateObjectOfType(objData, critterData) as Critter;
                    if (obj != null)
                    {
                        obj.transform.position = start;
                        // Calculate sub-tile coordinates (0-7) from world position
                        obj.x = Tile.GetSubTileX(start.x);
                        obj.y = Tile.GetSubTileY(start.z);
                        LevelLoader.AddToWorld(obj);
                        obj.WorldInitialize();
                        obj.PostLoadInitialize();

                        Vector3 spawnPos = start;
                        if (obj.movementType == Critter.EMovementType.Flying)
                        {
                            spawnPos.y += 2.0f;
                        }

                        int tileX = Tile.GetTileX(spawnPos.x);
                        int tileY = Tile.GetTileY(spawnPos.z);
                        // allow it to wander (otherwise it tries to path to the wrong place)
                        obj.xhome = tileX;
                        obj.yhome = tileY;
                        obj.transform.position = spawnPos;
                        obj.x = Tile.GetSubTileX(spawnPos.x);
                        obj.y = Tile.GetSubTileY(spawnPos.z);

                        obj.attitude = Critter.EAttitude.Hostile;
                        obj.playerAlly = true;

                        ParticleSpawner.SpawnParticle(EParticleType.MagicCreateFood, spawnPos);
                    }
                }
            }
        }
    }

    private void CastSheetLightning()
    {
        CastSheetLightning(PlayerObject.Player);
    }

    private void CastFlameWind()
    {
        CastFlameWind(PlayerObject.Player);
    }

    public void CastSheetLightning(MonoBehaviour owner)
    {
        SpawnCascadingEffect(owner, sheetLightningParticle, "SheetLightning", 8, 12);
    }

    public void CastFlameWind(MonoBehaviour owner)
    {
        // TODO: pass damage type and filter damage to critters based on vulnerabilities
        SpawnCascadingEffect(owner, flameWindParticle, "FlameWind", 14, 18);
    }
    
    private void SpawnCascadingEffect(MonoBehaviour owner, GameObject particlePrefab, string effectName, int minDamage, int maxDamage)
    {
        // Create the effect object
        GameObject effectObj = new GameObject($"CascadingSpellEffect_{effectName}");
        CascadingSpellEffect effect = effectObj.AddComponent<CascadingSpellEffect>();
        effect.Initialize(owner, particlePrefab, minDamage, maxDamage);
        effect.levelIndex = LevelLoader.sLevelLoader.loadedLevel;
        LevelLoader.worldObj.AddLast(effect);
    }

    public void StopDamageWaves()
    {
        // Find and stop all active cascading spell effects
        foreach (CascadingSpellEffect effect in FindObjectsByType<CascadingSpellEffect>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            effect.Stop();
        }
    }

    private bool IsMagicAvailable()
    {
        if (LevelLoader.sLevelLoader.loadedLevel == 7)
        {
            UUObject orb = LevelLoader.GetObj(820); // the orb
            if (orb != null && orb.gameObject.activeInHierarchy)
            {
                return false;
            }
        }

        if (LevelLoader.sLevelLoader.loadedLevel == 9)
        {
            return false;
        }

        return true;
    }

    public void StopAllSpells()
    {
        for (int i = activeSpells.Count - 1; i >= 0; --i)
        {
            RemoveSpell(i);
        }
    }

    private Rect gr;
    private readonly GUIStyle style = new();

    private readonly Dictionary<int, Texture2D> bubbleMap = new();

    /// <summary>
    /// Maps physical letter keys to rune slots (24 runes; no X/Z). Y selects Ylem (index 23).
    /// </summary>
    private static int KeyCodeToRuneIndex(KeyCode keyCode)
    {
        if (keyCode >= KeyCode.A && keyCode <= KeyCode.W)
            return keyCode - KeyCode.A;
        if (keyCode == KeyCode.Y)
            return 23;
        return -1;
    }

    /// <summary>
    /// Mouse/keyboard spell typing uses IMGUI <see cref="Event"/>s (see <see cref="Event.keyCode"/>); gamepad is unchanged.
    /// </summary>
    private void HandleMagicPanelKeyboardGui()
    {
        if (!magicPanelExploring)
            return;
        if (GameInput.LastActiveDevice == GameInputDevice.Gamepad)
            return;
        if (Event.current.type != EventType.KeyDown)
            return;

        KeyCode k = Event.current.keyCode;
        if (k == KeyCode.Return || k == KeyCode.KeypadEnter)
        {
            if (spellInProgress.Count > 1)
                TryCastFromSpellRunes();
            Event.current.Use();
            return;
        }

        int runeIndex = KeyCodeToRuneIndex(k);
        if (runeIndex < 0 || !hasRunestone[runeIndex])
            return;

        if (castASpellWithTheseRunes)
            spellInProgress.Clear();
        if (spellInProgress.Count < 3)
        {
            spellInProgress.Add(runeIndex);
            Utils.PlayClip2d(moveToRune);
            castASpellWithTheseRunes = false;
        }

        Event.current.Use();
    }

    private void UpdateMagicGridHover(float invPosition)
    {
        magicGridHoverRuneIndex = -1;
        if (GameInput.LastActiveDevice == GameInputDevice.Gamepad)
            return;

        Vector2 guiMouse = GuiInput.MousePositionGuiSpace;
        for (int i = 0; i < 24; ++i)
        {
            if (!hasRunestone[i])
                continue;
            Texture2D tex = DataLoader.sDataLoader.objTex[232 + i];
            float rw = 3 * tex.width;
            float rh = 3.6f * tex.height;
            Rect r = new Rect(invPosition + 54 * (i % 4) + 21, 48 + 53 * (i / 4), rw, rh);
            if (r.Contains(guiMouse))
            {
                magicGridHoverRuneIndex = i;
                return;
            }
        }
    }

    private void HandleMagicRuneGuiMouse(float invPosition)
    {
        if (GameInput.LastActiveDevice == GameInputDevice.Gamepad)
            return;
        if (Event.current.type != EventType.MouseDown || Event.current.button != 0)
            return;
        Vector2 m = Event.current.mousePosition;

        Texture2D panelTex = DataLoader.sDataLoader.panelsTex[1];
        float panelW = 3 * panelTex.width;

        // Click current spell runes (bottom row) -> cast
        if (spellInProgress.Count > 1)
        {
            for (int i = 0; i < spellInProgress.Count; ++i)
            {
                int ri = spellInProgress[i];
                Texture2D stex = DataLoader.sDataLoader.objTex[232 + ri];
                float rw = 3 * stex.width;
                float rh = 3.6f * stex.height;
                Rect spellRuneRect = new Rect(invPosition + 90 + 48 * i, 444, rw, rh);
                if (!spellRuneRect.Contains(m))
                    continue;
                TryCastFromSpellRunes();
                GuiInput.MarkPrimaryClickConsumed();
                Event.current.Use();
                return;
            }
        }

        for (int i = 0; i < 24; ++i)
        {
            if (!hasRunestone[i])
                continue;
            Texture2D tex = DataLoader.sDataLoader.objTex[232 + i];
            float rw = 3 * tex.width;
            float rh = 3.6f * tex.height;
            Rect r = new Rect(invPosition + 54 * (i % 4) + 21, 48 + 53 * (i / 4), rw, rh);
            if (!r.Contains(m))
                continue;
            if (castASpellWithTheseRunes)
                spellInProgress.Clear();
            if (spellInProgress.Count < 3)
            {
                spellInProgress.Add(i);
                Utils.PlayClip2d(moveToRune);
                castASpellWithTheseRunes = false;
            }
            Event.current.Use();
            return;
        }

        // Bottom bar [R]->[bag]: clear spell
        if (spellInProgress.Count > 0)
        {
            Rect clearBarRect = new Rect(invPosition + 63f, 30 +343f, 123f, 52f);
            if (clearBarRect.Contains(m))
            {
                spellInProgress.Clear();
                Utils.PlayClip2d(clearSpell);
                Event.current.Use();
            }
        }

        {
            Rect todoClickRect = new Rect(invPosition + 7f, 30 + 373f, 54f, 85f);
            if (todoClickRect.Contains(m))
            {
                Messages.Add($"{StringLoader.GetString(1, 90)}{PlayerData.sData.mana}/{PlayerData.sData.maxMana}.");
                Event.current.Use();
                return;
            }
        }
    }

    private void DrawTex(float x, float y, float w, float h, Texture tex)
    {
        gr.x = x;
        gr.y = y;
        gr.width = w;
        gr.height = h;
        GUI.DrawTexture(gr, tex);
    }

    private void DrawFlask(int x, int flaskY, int type, int numSegs)
    {
        // draw flask
        int y = flaskY;
        {
            Texture2D tex = flaskCopy;
            DrawTex(x, y - 3 * tex.height + 21, 3 * tex.width, 3 * tex.height, tex);
        }
        if (numSegs > 0)
        {
            for (int i = 0; i < numSegs; ++i)
            {
                Texture2D tex = DataLoader.sDataLoader.flasksTex[25 * type + i];
                DrawTex(x, y, 3 * tex.width, 3 * tex.height, tex);
                if (i < numSegs - 1)
                {
                    y -= 3 * flaskOffsets[i];
                }
            }

#if true
            // draw masked out bubbles
            if (bubbleTime > 0.0f && numSegs > 0)
            {
                int bubbleIndex = ((int)(6 * bubbleTime)) % 12;
                int bubbleTexIndex = 25 * type + 13 + bubbleIndex;
                int maskTexIndex = 25 * type + numSegs - 1;
                int key = bubbleTexIndex + (maskTexIndex << 9);
                if (!bubbleMap.TryGetValue(key, out Texture2D maskedBubbleTex))
                {
                    Texture2D bubbleTex = DataLoader.sDataLoader.flasksTex[bubbleTexIndex];
                    Texture2D maskTex = DataLoader.sDataLoader.flasksTex[maskTexIndex];
                    maskedBubbleTex = new Texture2D(bubbleTex.width, Mathf.Min(bubbleTex.height, maskTex.height));
                    Color[] bubblePix = bubbleTex.GetPixels();
                    Color[] maskPix = maskTex.GetPixels();
                    for (int i = 1; i <= Mathf.Min(bubblePix.Length, maskPix.Length); ++i)
                    {
                        // upside down so pull from end (^ means index from end)
                        bubblePix[^i].a = maskPix[^i].a;
                    }

                    maskedBubbleTex.SetPixels(bubblePix);
                    maskedBubbleTex.wrapMode = TextureWrapMode.Clamp;
                    maskedBubbleTex.filterMode = FilterMode.Point;
                    maskedBubbleTex.Apply();
                    
                    bubbleMap.Add(key, maskedBubbleTex);
                }

                DrawTex(x, y, 3 * maskedBubbleTex.width, 3 * maskedBubbleTex.height, maskedBubbleTex);
            }
#endif

            // draw the base on top
            {
                Texture2D tex = flaskBase;
                DrawTex(x, flaskY - 3 * tex.height + 21, 3 * tex.width, 3 * tex.height, tex);
            }
        }
    }

    private Texture2D GetSpellIcon(int spellIndex)
    {
        if (spellIndex == (int)ESpell.Light)
        {
            Texture2D[] texs = DataLoader.sDataLoader.lightSpellTex;
            int cycle = (Time.frameCount / 16) % texs.Length;
            return texs[cycle];
        }

        return DataLoader.sDataLoader.spellsTex[spells[spellIndex].icon];
    }

    public void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Magic;

        ScrubUpFlaskTextures();

        style.font = font;
        style.fontSize = 20;
        style.normal.textColor = Color.yellow;

        // draw active spells
        for (int i = 0; i < activeSpells.Count; ++i)
        {
            SSpell spell = spells[activeSpells[i].spell];
            Texture2D tex = GetSpellIcon(activeSpells[i].spell);
            DrawTex(100, Screen.height - 120 - 64 * i, 64, 64, tex);
            GUI.Label(new Rect(168, Screen.height - 100 - 64 * i, 100, 20), spell.name, style);
            DrawTex(168, Screen.height - 70 - 64 * i,
                150 * activeSpells[i].time / spells[activeSpells[i].spell].duration, 5, Texture2D.whiteTexture);
        }

        {
            int psx = 100;
            foreach (SPermanentSpell spell in permanentSpells)
            {
                if (spell.obj.loreResult >= Skills.ESkillTestResult.CriticalSuccess)
                {
                    int icon = spells[(int)spell.spell].icon;
                    // if there isn't an icon for it (mana regen, regen, poison resistance) just don't draw anything
                    if (icon >= 0 && icon < DataLoader.sDataLoader.spellsTex.Length)
                    {
                        DrawTex(psx, Screen.height - 60, 48, 48, DataLoader.sDataLoader.spellsTex[icon]);
                        psx += 64;
                    }
                }
            }
        }

        if (lerpIn > 0.01f)
        {
            GuiInput.RegisterBlockingRect(GetPanelGuiRectForOutsideClick());

            const float kExtendedInvPosition = 16 + 4 * 60;
            float invPosition = lerpIn * kExtendedInvPosition - kExtendedInvPosition + 16;

            if (!MapScreen.IsMapScreenVisible())
            {
                UpdateMagicGridHover(invPosition);
                // this is deliberately removed - prefer player movement with WASD over typing spells
                //HandleMagicPanelKeyboardGui();
                HandleMagicRuneGuiMouse(invPosition);
            }

            // background
            {
                Texture2D tex = DataLoader.sDataLoader.panelsTex[1];
                DrawTex(invPosition, 30, 3 * tex.width, 3.6f * tex.height, tex);
            }

            // draw owned runes
            for (int i = 0; i < 24; ++i)
            {
                if (hasRunestone[i])
                {
                    Texture2D tex = DataLoader.sDataLoader.objTex[232 + i];
                    DrawTex(invPosition + 54 * (i % 4) + 21, 48 + 53 * (i / 4), 3 * tex.width, 3.6f * tex.height, tex);
                }
            }

            if (GameInput.LastActiveDevice == GameInputDevice.Gamepad)
            {
                // draw cursor
                DrawTex(invPosition + 54 * (index % 4) + 21 - 3, 48 + 53 * (index / 4) - 2, 3 * cursor.width, 3.6f * cursor.height, cursor);

                if (hasRunestone[index])
                {
                    style.fontSize = 16;
                    GUI.Label(new Rect(invPosition + 5, 30, 100, 20), DataLoader.GetCleanedObjectName(EObjectType.RunestoneAn + index), style);
                }
            }
            else if (magicGridHoverRuneIndex >= 0 && hasRunestone[magicGridHoverRuneIndex])
            {
                style.fontSize = 16;
                GUI.Label(new Rect(invPosition + 5, 30, 100, 20),
                    DataLoader.GetCleanedObjectName(EObjectType.RunestoneAn + magicGridHoverRuneIndex), style);
            }

            // draw current spell runes
            for (int i = 0; i < spellInProgress.Count; ++i)
            {
                Texture2D tex = DataLoader.sDataLoader.objTex[232 + spellInProgress[i]];
                DrawTex(invPosition + 90 + 48 * i, 444, 3 * tex.width, 3.6f * tex.height, tex);
                style.fontSize = 16;
                GUI.Label(new Rect(invPosition + 96 + 48 * i, 490, 40, 20), runeShortNames[spellInProgress[i]], style);
            }

            if (lerpIn > 0.02f && GameInput.LastActiveDevice == GameInputDevice.Gamepad)
            {
                style.fontSize = 16;
                int sz = 24;

                if (hasRunestone[index])
                {
                    DrawTex(invPosition + 60, 514, sz, sz, aButton);
                    GUI.Label(new Rect(invPosition + 90, 514, 100, 60), "Select", style);
                }
                if (spellInProgress.Count > 1)
                {
                    DrawTex(invPosition + 140, 514, sz, sz, xButton);
                    GUI.Label(new Rect(invPosition + 170, 514, 100, 60), "Cast", style);
                }
                if (spellInProgress.Count > 0)
                {
                    DrawTex(invPosition + 50, 374, sz, sz, bButton);
                }
            }

            if (Cheats.sCheats.showSpellIcons)
            {
                float x = invPosition;
                for (int i = 0; i < spells.Count; ++i)
                {
                    if (spells[i].icon != -1)
                    {
                        Texture2D tex = GetSpellIcon(i);
                        DrawTex(x, 600, 3 * tex.width, 3.6f * tex.height, tex);
                        x += 60;
                    }
                }
            }
        }

        // for now draw health flask here
        if ((PlayerObject.Player.controlsDisabled & EControlMask.Cutscene) == 0)
        {
            int manaSegs = PlayerData.sData.maxMana > 0 ? 13 * PlayerData.sData.mana / PlayerData.sData.maxMana : 0;
            if (!IsMagicAvailable())
            {
                manaSegs = 0;
            }
            DrawFlask(18, Screen.height - 50, 1, manaSegs);

            int numSegs = PlayerData.sData.vitality > 0 ? 13 * PlayerData.sData.hp / PlayerData.sData.vitality : 13;
            int type = PlayerData.sData.poison > 0 ? 2 : 0;
            DrawFlask(Screen.width - 90, Screen.height - 50, type, numSegs);
        }
    }
    
    public MagicSaveData SaveToData()
    {
        MagicSaveData saveData = new MagicSaveData
        {
            lastIncrementedMana = this.lastIncrementedMana,
            lastCheckedPermanentSpells = this.lastCheckedPermanentSpells
        };

        saveData.castSpells = new bool[castSpells.Length];
        for (int i = 0; i < castSpells.Length; i++)
        {
            saveData.castSpells[i] = castSpells[i];
        }
        
        // Save active spells
        foreach (SActiveSpell activeSpell in activeSpells)
        {
            saveData.activeSpells.Add(new SActiveSpell
            {
                spell = activeSpell.spell,
                time = activeSpell.time
            });
        }

        saveData.hasMousePrimedSpell = mousePrimedSpellIndex >= 0;
        if (saveData.hasMousePrimedSpell)
        {
            saveData.mousePrimedSpellListIndex = mousePrimedSpellIndex;
            saveData.mousePrimedSpellWasCritical = mousePrimedSpellWasCritical;
        }
        
        return saveData;
    }

    public void LoadFromData(MagicSaveData data)
    {
        if (data == null) return;
        
        lastIncrementedMana = data.lastIncrementedMana;
        lastCheckedPermanentSpells = data.lastCheckedPermanentSpells;

        mousePrimedSpellIndex = -1;
        mousePrimedSpellWasCritical = false;
        SoftwareCursorOverlay.DefaultTextureOverride = null;
        if (data.hasMousePrimedSpell
            && data.mousePrimedSpellListIndex >= 0
            && data.mousePrimedSpellListIndex < spells.Count
            && SpellUsesMousePrimedAiming(spells[data.mousePrimedSpellListIndex].runes))
        {
            mousePrimedSpellIndex = data.mousePrimedSpellListIndex;
            mousePrimedSpellWasCritical = data.mousePrimedSpellWasCritical;
            ApplyPrimedSpellCursorTexture();
        }

        for (int i = 0; i < castSpells.Length; i++)
        {
            castSpells[i] = data.castSpells != null && i < data.castSpells.Length && data.castSpells[i];
        }
        
        // Clear and restore active spells
        activeSpells.Clear();
        if (data.activeSpells != null)
        {
            foreach (SActiveSpell savedSpell in data.activeSpells)
            {
                activeSpells.Add(new SActiveSpell
                {
                    spell = savedSpell.spell,
                    time = savedSpell.time
                });
            }
        }

        // Sync night vision greyscale (Color Grading) to loaded spell state — cast enables PP override; load must also clear it when NV is not in the save
        PostProcessVolume nightVisionVol = PlayerObject.Player?.GetComponentInChildren<PostProcessVolume>();
        if (nightVisionVol != null && nightVisionVol.profile.TryGetSettings(out ColorGrading nightVisionGrading))
        {
            nightVisionGrading.enabled.Override(IsSpellActive(ESpell.NightVision));
        }
    }
}
