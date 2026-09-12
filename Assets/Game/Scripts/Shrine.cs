using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Shrine : UUObject
{
    public AudioClip interacted;
    public AudioClip goodMantra;
    public AudioClip badMantra;

    private bool getMantra;
    private string mantra = "";
    
    // in order of ESkill
    private static readonly string[] skillMantras =
    {
        "ra", "anra", "ora", "amo", "gar", "koh", "fahm", "imu", "lahn", "sol",
        "romm", "lu", "sahf", "mul", "lon", "un", "aam", "fal", "hunn", "ono"
    };

    public override void Update()
    {
        base.Update();

        if (getMantra && StatsPanel.sStatsPanel != null)
        {
            StatsPanel.sStatsPanel.Show();
        }
    }

    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        // get some input
        getMantra = true;
        Utils.PlayClip2d(interacted);
#if UNITY_EDITOR
        EditorApplication.ExecuteMenuItem("Window/General/Game");
#endif
        float keyboardX = Screen.width / 2 - 200;
        float keyboardY = Screen.height / 3 + 40;
        KeyboardGUI.sKeyboard.Show(mantra, result =>
            {
                // Empty string cancels - process as "not a mantra"
                if (string.IsNullOrEmpty(result.Trim()))
                {
                    ProcessMantra("");
                }
                else
                {
                    ProcessMantra(result);
                }
                mantra = "";
                getMantra = false;
                // Keyboard is automatically dismissed by the callback completion
            },
            () => {
                // X button cancel - process as "not a mantra"
                ProcessMantra("");
                mantra = "";
                getMantra = false;
            },
            new Vector2(keyboardX, keyboardY),
            "Mantra:",
            false,
            true); // allowCancel = true (X or empty string cancels)
    }

    private void HandleInsahn()
    {
        // direct the player towards the cup (assuming it hasn't been moved!)
        int cupLevel = 3;
        int levelOff = LevelLoader.sLevelLoader.loadedLevel - cupLevel;

        string msg = StringLoader.GetString(1, 35);
        if (levelOff == 0)
        {
            int octant = Utils.OffsetToOctant(Utils.GetCupPos() - PlayerObject.Player.mainCamera.transform.position);
            msg += StringLoader.GetString(1, 36 + octant) + ".";
        }
        else
        {
            msg += StringLoader.GetString(1, 51 + levelOff) + ".";
        }
        Messages.Add(msg, 10.0f);
    }

    private void HandleFanlo()
    {
        if (!PlayerData.sData.saidFanlo)
        {
            UUObject key = LevelLoader.CreateObjectOfType(EObjectType.KeyOfTruth);
            key.PostLoadInitialize();
            Inventory.GrantNewItemToMouseOrInventory(key);
            PlayerData.sData.saidFanlo = true;
            Messages.Add(1, 136);
        }
        else
        {
            Messages.Add(1, 132);
        }
    }
    
    private void ProcessMantra(string mantraText)
    {
        mantraText = mantraText.ToLower();
        bool foundMantra = false;

        switch (mantraText)
        {
        case "insahn":
            // cup of wonder
            HandleInsahn();
            foundMantra = true;
            break;
        case "fanlo":
            // key of truth
            HandleFanlo();
            foundMantra = true;
            break;
        }

        if (!foundMantra)
        {
            ESkill firstSkill = ESkill.Attack;
            ESkill secondSkill = ESkill.Attack;
            // How many skills a group mantra advances belongs to the mantra: the original's three
            // cases set it alongside the group itself (UW.EXE 0x818b6, 0x818c5, 0x818d4).
            int groupCount = 0;
            for (int i = 0; i < 20; ++i)
            {
                if (mantraText == skillMantras[i])
                {
                    firstSkill = secondSkill = (ESkill)i;
                    foundMantra = true;
                    break;
                }
            }

            if (!foundMantra)
            {
                switch (mantraText)
                {
                case "mu ahm":
                    firstSkill = ESkill.Mana;
                    secondSkill = ESkill.Casting;
                    groupCount = 2;
                    foundMantra = true;
                    break;
                case "om cah":
                    firstSkill = ESkill.Traps;
                    secondSkill = ESkill.Swimming;
                    groupCount = 4;
                    foundMantra = true;
                    break;
                case "summ ra":
                    firstSkill = ESkill.Attack;
                    secondSkill = ESkill.Missile;
                    groupCount = 3;
                    foundMantra = true;
                    break;
                default:
                    Messages.Add(1, 25); // not a mantra
                    break;
                }
            }

            if (foundMantra && PlayerData.sData.skillPoints == 0 && !Cheats.sCheats.advanceSkillsWithoutPoints)
            {
                Messages.Add(1, 24); // you are not ready to advance
                foundMantra = false;
            }

            if (foundMantra)
            {
                if (firstSkill == secondSkill)
                {
                    if (!Skills.AdvanceSkill(firstSkill))
                    {
                        Messages.Add(1, 27); // you cannot advance any further.
                        Utils.PlayClip2d(badMantra);
                        return;
                    }
                    else
                    {
                        Skills.AdvanceSkill(firstSkill);
                        Messages.Add(1, 26);
                        Messages.Add($"{StringLoader.GetString(1, 28)}{firstSkill}.");
                        --PlayerData.sData.skillPoints;
                    }
                }
                else
                {
                    if (!Skills.AdvanceSkills(firstSkill, secondSkill, groupCount))
                    {
                        Utils.PlayClip2d(badMantra);
                        return;
                    }
                }
            }
        }

        if (foundMantra)
        {
            Utils.PlayClip2d(goodMantra);
            PlayerObject.Rumble(0.4f, 0.1f, 0.3f);
            ParticleSpawner.SpawnParticle(EParticleType.ShrineActivate, transform.position);
            StatsPanel.sStatsPanel.Show();
        }
        else
        {
            Utils.PlayClip2d(badMantra);
        }
    }
}
