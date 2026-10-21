using UnityEngine;
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
#endif

/// <summary>
/// The cheats, set from the Inspector. Assets/Editor/CheatsEditor.cs draws them in the order of the
/// fields below, with the level and its two buttons, the tile to teleport to, the three things to
/// spawn and the skill to set each on one row of its own. In the Editor, what is set there is kept
/// for the next run; see KeepInEditorPrefs().
/// </summary>
public class Cheats : MonoBehaviour
{
    // The levels there are: the eight of the abyss and the Ethereal Void, which is what
    // LevelLoader.Update() accepts.
    public const int FirstLevel = 1;
    public const int LastLevel = 9;

    // The level to go to: the last one set in the panel, not the one the game is on, so the
    // stairs and a load leave it alone. It is applied only from the panel - typed and confirmed
    // by leaving the box, Enter in the box even with the value unchanged, or + and -. A new game
    // starts on the first level and a load on the saved one, whatever the box says.
    public int level = 1;

    // The tile to jump to on the level currently loaded: the player lands on its centre when either
    // box is changed, on Enter or when it loses focus.
    public Vector2Int teleportToTile;

    // The player's poison and hit points, shown as they are and set the same way: type a value and it
    // is applied when the box loses focus or on Enter. Poison runs 0 to 15, as the game caps it; hit
    // points 0 to the vitality, and 0 kills. Both follow the game while nothing is typed.
    [Delayed]
    public int poisonValue;
    [Delayed]
    public int hitPointsValue;

    public bool giveAllKeys;
    public bool showConversationHeads;
    public bool giveRunestones;
    public bool castWithoutMana;
    public bool boostCasting;
    public bool invincible;
    public bool boostDamageFromPlayer;

    // What the three Spawn buttons put down: an object of quality 30, a stack of 20, and a creature
    // that far ahead of the player, hostile or not.
    public EObjectType objectToSpawn = EObjectType.ShinySword;
    public EObjectType stackToSpawn = EObjectType.Coin;
    public EObjectType critterToSpawn = EObjectType.GoblinA;
    public float critterDistance = 3.0f;
    public bool critterHostile = true;

    // A skill to set. Pick it and the box beside it shows its value; type a new one and it is applied
    // when the box loses focus or on Enter, and only if it is 0 to 30. The box follows the skill when
    // the game changes it, as long as nothing is being typed.
    public ESkill skillToSet = ESkill.Search;
    [Delayed]
    public int skillValue;

    public bool mapWithoutMap;
    public bool revealMap;
    public bool advanceSkillsWithoutPoints;

    // Tick to change the weapon hand, right to left and back. What the two hands hold goes with it -
    // the weapon stays the weapon, the shield the shield - and a weapon or a shield is put on again,
    // so the weapon shows in the new hand. Clears itself on the next frame.
    public bool swapWeaponHand;

    public bool showOldHeads;
    public bool showSpellIcons;
    public bool showFloorMats;
    public bool showWallMats;
    public bool showDoorMats;
    public bool showSwitches;
    public bool labelCenteredObject;
    public bool addIndexToObjectName;

    // What the boxes showed last, so a change typed in one can be told from one made by the game.
    private ESkill shownSkill = (ESkill)(-1);
    private int shownValue = -1;
    private int shownPoison = -1;
    private int shownHitPoints = -1;

    public static Cheats sCheats;

    protected void Awake()
    {
#if UNITY_EDITOR
        RestoreFromEditorPrefs();
#endif
    }

    protected void Start()
    {
        sCheats = this;
    }

#if UNITY_EDITOR
    // In the Editor only, never in a build: a field set from the Inspector is written to EditorPrefs
    // and put back when the next run starts, so a test setup survives a restart. Only what is set
    // there is written, not what the game changes - the level the stairs lead to, say. The three
    // boxes that follow the game and the weapon-hand tick, which is a button, are not kept.
    private const string PrefsPrefix = "UnityUnderground.Cheats.";
    private static readonly string[] notKept = { nameof(poisonValue), nameof(hitPointsValue), nameof(skillValue), nameof(swapWeaponHand) };
    private static FieldInfo[] keptFields;

    public static FieldInfo[] KeptFields()
    {
        if (keptFields == null)
        {
            List<FieldInfo> fields = new List<FieldInfo>();
            foreach (FieldInfo field in typeof(Cheats).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (Array.IndexOf(notKept, field.Name) < 0)
                {
                    fields.Add(field);
                }
            }
            keptFields = fields.ToArray();
        }
        return keptFields;
    }

    public void KeepInEditorPrefs(FieldInfo field)
    {
        string key = PrefsPrefix + field.Name;
        switch (field.GetValue(this))
        {
        case bool b:
            EditorPrefs.SetBool(key, b);
            break;
        case float f:
            EditorPrefs.SetFloat(key, f);
            break;
        case int i:
            EditorPrefs.SetInt(key, i);
            break;
        case Enum e:
            EditorPrefs.SetInt(key, Convert.ToInt32(e));
            break;
        case Vector2Int v:
            EditorPrefs.SetString(key, $"{v.x},{v.y}");
            break;
        }
    }

    private void RestoreFromEditorPrefs()
    {
        foreach (FieldInfo field in KeptFields())
        {
            string key = PrefsPrefix + field.Name;
            if (!EditorPrefs.HasKey(key))
            {
                continue;
            }

            Type type = field.FieldType;
            if (type == typeof(bool))
            {
                field.SetValue(this, EditorPrefs.GetBool(key));
            }
            else if (type == typeof(float))
            {
                field.SetValue(this, EditorPrefs.GetFloat(key));
            }
            else if (type == typeof(int))
            {
                field.SetValue(this, EditorPrefs.GetInt(key));
            }
            else if (type.IsEnum)
            {
                field.SetValue(this, Enum.ToObject(type, EditorPrefs.GetInt(key)));
            }
            else if (type == typeof(Vector2Int))
            {
                string[] xy = EditorPrefs.GetString(key).Split(',');
                if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y))
                {
                    field.SetValue(this, new Vector2Int(x, y));
                }
            }
        }
    }
#endif

    public void Update()
    {
        if (swapWeaponHand)
        {
            swapWeaponHand = false;
            DoSwapWeaponHand();
        }

        UpdateSkillToSet();
        UpdatePoisonAndHitPoints();
    }

    public void SpawnObject()
    {
        UUObject obj = Utils.SpawnSingleObject(objectToSpawn);
        if (obj != null)
        {
            obj.quality = 30;
        }
    }

    public void SpawnStack()
    {
        UUObject obj = Utils.SpawnSingleObject(stackToSpawn);
        if (obj != null && obj.stackable)
        {
            obj.quantity = 20;
        }
    }

    public void SpawnCritter()
    {
        CheatsKit.SpawnCritter(critterToSpawn, critterDistance, critterHostile);
    }

    public void GoToLevel()
    {
        if (LevelLoader.sLevelLoader != null)
        {
            LevelLoader.sLevelLoader.RequestLevelFromCheats(level);
        }
    }

    public void Teleport()
    {
        Tile t = LevelLoader.GetTile(teleportToTile.x, teleportToTile.y);
        if (t == null)
        {
            Debug.LogWarning($"[CHEAT] no tile at ({teleportToTile.x},{teleportToTile.y})");
            return;
        }

        // A metre above the floor, so landing on a slope or on a step does not start inside it;
        // the character controller settles in a frame or two.
        Vector3 pos = t.GetCenter() + Vector3.up;
        PlayerObject.Player.TeleportTo(pos, PlayerObject.Player.transform.rotation);
        Debug.Log($"[CHEAT] teleport to tile ({t.x},{t.y})");
    }

    private void UpdatePoisonAndHitPoints()
    {
        if (PlayerData.sData == null)
        {
            return;
        }

        PlayerData data = PlayerData.sData;
        poisonValue = Track("poison", poisonValue, ref shownPoison, () => data.poison, 15, v => data.poison = v);
        hitPointsValue = Track("hit points", hitPointsValue, ref shownHitPoints, () => data.hp, data.vitality, v =>
        {
            if (v == 0)
            {
                // 0 goes through the damage path, so the death comes as it would in play.
                data.hp = 1;
                PlayerObject.Player.Damage(Skills.ESkillTestResult.Success, 1, EDamageType.Direct);
            }
            else
            {
                data.hp = v;
            }
        });
    }

    /// <summary>One box that shows a value of the game and sets it: a typed value from 0 to max is
    /// applied, anything else is refused, and while nothing is typed the box follows the game.</summary>
    private static int Track(string label, int typed, ref int shown, System.Func<int> get, int max, System.Action<int> set)
    {
        if (shown < 0)
        {
            shown = get();
            return shown;
        }

        if (typed != shown)
        {
            if (typed >= 0 && typed <= max)
            {
                set(typed);
                Debug.Log($"[CHEAT] {label} set to {typed}");
            }
            else
            {
                Debug.LogWarning($"[CHEAT] {label}: {typed} is not 0 to {max}, left at {get()}");
            }
        }

        // What the game holds now, after the set or whatever the game did on its own.
        shown = get();
        return shown;
    }

    private void UpdateSkillToSet()
    {
        if (PlayerData.sData == null || PlayerData.sData.skill == null)
        {
            return;
        }

        int index = (int)skillToSet;
        if (index < 0 || index >= PlayerData.sData.skill.Length)
        {
            return;
        }

        int current = PlayerData.sData.skill[index];
        if (skillToSet != shownSkill)
        {
            // Another skill picked: show its value, change nothing.
            shownSkill = skillToSet;
            skillValue = shownValue = current;
        }
        else if (skillValue != shownValue)
        {
            // A value typed and committed ([Delayed]: on losing focus or on Enter).
            if (skillValue >= 0 && skillValue <= 30)
            {
                PlayerData.sData.skill[index] = skillValue;
                shownValue = skillValue;
                Debug.Log($"[CHEAT] {skillToSet} set to {skillValue}");
            }
            else
            {
                Debug.LogWarning($"[CHEAT] {skillToSet}: {skillValue} is not 0 to 30, left at {current}");
                skillValue = shownValue = current;
            }
        }
        else if (current != shownValue)
        {
            // The game changed it (training, a shrine, a load): follow it.
            skillValue = shownValue = current;
        }
    }

    private void DoSwapWeaponHand()
    {
        Inventory inv = Inventory.sInv;
        if (inv == null || PlayerData.sData == null)
        {
            return;
        }

        int right = (int)EInvSlot.RightHand;
        int left = (int)EInvSlot.LeftHand;
        UUObject inRight = inv.invSlotContents[right];
        UUObject inLeft = inv.invSlotContents[left];
        bool fistOut = inv.fist != null && inv.fist.isActiveAndEnabled;

        // Only what is worn in a hand is taken off and put on again: a weapon or a shield. Equip()
        // on anything else is its use, and would eat it or light it.
        static bool Worn(UUObject o) => o is WeaponBase || o is Armour;
        if (Worn(inRight)) inRight.Unequip();
        if (Worn(inLeft)) inLeft.Unequip();
        if (fistOut)
        {
            inv.fist.Unequip();
        }

        PlayerData.sData.leftHanded = !PlayerData.sData.leftHanded;
        inv.invSlotContents[right] = inLeft;
        inv.invSlotContents[left] = inRight;

        if (Worn(inRight)) inRight.Equip();
        if (Worn(inLeft)) inLeft.Equip();
        if (fistOut)
        {
            inv.fist.Equip();
        }
        Debug.Log($"[CHEAT] weapon hand is now {(PlayerData.sData.leftHanded ? "left" : "right")}");
    }
}
