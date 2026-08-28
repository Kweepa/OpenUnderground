using System;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CreateCharacter : MonoBehaviour
{
    private enum ERollState
    {
        Sex,
        Handedness,
        Class,
        Skills,
        Portrait,
        Difficulty,
        Name,
        Keep,
        Done
    }

    public enum EStat
    {
        None,
        Strength,
        Intellect,
        Dexterity
    }

    private ERollState rollState = ERollState.Sex;
    private int skillRollIndex;
    private int skillChoiceIndex;
    private bool keepChar = true;
    private float fadeAlpha;
    public static bool wasCancelled = false;
    
    // Track the skill state after RollStats() (after auto-rolled single-choice skills)
    // This is the state we restore to when rolling back
    private int[] skillsAfterRollStats = new int[20];

    public Font font;
    public GUIStyle labelStyle;
    public GUIStyle highlightedLabelStyle;
    public GUIStyle buttonStyle;
    public GUIStyle selectedButtonStyle;

    private Texture2D background;
    public Texture2D[] buttons;
    public Texture2D[] headsTex;
    
    public AudioClip moveSelection;
    public AudioClip makeSelection;
    public AudioClip diceRoll;
    public AudioClip cancelSelection;
    public AudioClip startGame;

    public string[] classDescriptions;
    [EnumNamedArray(typeof(ESkill))]
    public string[] skillDescriptions;
    [EnumNamedArray(typeof(ESkill))]
    public EStat[] skillGoverningStat;
    public GUIStyle descriptionStyle;

    private void Start()
    {
        background = GraphicsLoader.ReadBYT("../Data/chargen.byt", 3);
        buttons = GraphicsLoader.GetTextures("../Data/chrbtns.gr", zeroAlpha: 0.0f, wrapMode: TextureWrapMode.Clamp, paletteIndex: 3);

        #if false
        for (int i = 17; i < 27; ++i)
        {
            byte[] b = buttons[i].EncodeToPNG();
            System.IO.File.WriteAllBytes($"{Application.dataPath}/Game/Textures/Portraits/paperdoll{i}.png", b);
        }
        #endif

        for (int b = 1; b <= 2; ++b)
        {
            Color[] c = buttons[b].GetPixels();
            for (int y = 2; y < buttons[b].height - 2; ++y)
            {
                for (int x = 2; x < buttons[b].width - 2; ++x)
                {
                    c[buttons[b].width * y + x].a = 0;
                }
            }

            buttons[b].SetPixels(c);
            buttons[b].Apply();
        }

        // reset character
        // Initialize sex based on last played character (default to male for original game consistency)
        string lastCharacterSex = PlayerPrefs.GetString("LastCharacterSex", "male");
        PlayerData.sData.female = (lastCharacterSex == "female");
        PlayerData.sData.leftHanded = true;
        PlayerData.sData.playerClass = EPlayerClass.Fighter;
        PlayerData.sData.strength = 0;
        PlayerData.sData.dexterity = 0;
        PlayerData.sData.intellect = 0;
        PlayerData.sData.vitality = 0;
        PlayerData.sData.hp = 0;
        PlayerData.sData.mana = 0;
        PlayerData.sData.maxMana = 0;
        PlayerData.sData.xp = 0;
        for (int i = 0; i < 20; ++i)
        {
            PlayerData.sData.skill[i] = 0;
        }

        PlayerData.sData.hunger = 64;
        PlayerData.sData.fatigue = 0;
        PlayerData.sData.drunkenness = 0;

        PlayerData.sData.ShuffleDreams();

        PlayerData.sData.playerName = "";
    }

    private void FindFirstMultiChoiceSkillScreen()
    {
        // Find the first skill choice screen with multiple options (not auto-rolled)
        PlayerObject.ClassData data = PlayerObject.classData[(int)PlayerData.sData.playerClass];
        skillRollIndex = 0;
        byte[] skills = data.skills[skillRollIndex];
        while (skills.Length == 1 && skillRollIndex < data.skills.Count - 1)
        {
            skillRollIndex++;
            skills = data.skills[skillRollIndex];
        }
        // Ensure it's within bounds
        if (skillRollIndex >= data.skills.Count)
        {
            skillRollIndex = data.skills.Count - 1;
        }
    }

    private void IncreaseSkill(byte skill)
    {
        int curValue = PlayerData.sData.skill[skill];

        int governingStatValue = 0;
        EStat governingStat = skillGoverningStat[skill];
        switch (governingStat)
        {
        case EStat.Strength:
            governingStatValue = PlayerData.sData.strength;
            break;
        case EStat.Intellect:
            governingStatValue = PlayerData.sData.intellect;
            break;
        case EStat.Dexterity:
            governingStatValue = PlayerData.sData.dexterity;
            break;
        }
        int statBasedBonus = Math.Max(0, governingStatValue - curValue) / 5;

        PlayerData.sData.skill[skill] += Math.Min(UnityEngine.Random.Range(1, 13) + statBasedBonus, 12);
    }

    private void RollStats()
    {
        PlayerObject.ClassData data = PlayerObject.classData[(int) PlayerData.sData.playerClass];

        PlayerData.sData.strength = data.minStrength;
        PlayerData.sData.intellect = data.minIntellect;
        PlayerData.sData.dexterity = data.minDexterity;

        Utils.PlayClip2d(diceRoll);

        // roll up a random character
        int pointsRemaining = data.pointsRemaining;
        while (pointsRemaining-- > 0)
        {
            int r = UnityEngine.Random.Range(0, 3);
            switch (r)
            {
            case 0:
                ++PlayerData.sData.strength;
                break;
            case 1:
                ++PlayerData.sData.intellect;
                break;
            case 2:
                ++PlayerData.sData.dexterity;
                break;
            }
        }
        // https://wiki.ultimacodex.com/wiki/Character_attributes#Ultima_Underworld_and_Ultima_Underworld_II
        int strength = PlayerData.sData.strength;
        PlayerData.sData.vitality = 29 + (6 * strength + 3) / 25;
        PlayerData.sData.hp = PlayerData.sData.vitality;

        for (int i = 0; i < PlayerData.sData.skill.Length; ++i)
        {
            PlayerData.sData.skill[i] = 0;
        }

        skillRollIndex = 0;
        byte[] skills = data.skills[0];
        while (skills.Length == 1 || skillRollIndex == 0)
        {
            foreach (var skill in skills)
            {
                IncreaseSkill(skill);
            }
            skills = data.skills[++skillRollIndex];
        }
        
        // Save the skill state after auto-rolling single-choice skills
        // This is the state we'll restore to when rolling back
        System.Array.Copy(PlayerData.sData.skill, skillsAfterRollStats, PlayerData.sData.skill.Length);
    }

    private void Update()
    {
        GameInput.RefreshLastActiveDevice();
        bool cancelPressedThisFrame =
            (Gamepad.current?.bButton.wasPressedThisFrame ?? false)
            || (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false);

        switch (rollState)
        {
        case ERollState.Sex:
            if ((Gamepad.current?.dpad.up.wasPressedThisFrame ?? false) || (Gamepad.current?.dpad.down.wasPressedThisFrame ?? false))
            {
                PlayerData.sData.female = !PlayerData.sData.female;
                Utils.PlayClip2d(moveSelection);
            }
            else if ((Gamepad.current?.aButton.wasPressedThisFrame) ?? false)
            {
                Utils.PlayClip2d(makeSelection);
                ++rollState;
            }
            else if (cancelPressedThisFrame)
            {
                Utils.PlayClip2d(cancelSelection);
                wasCancelled = true;
                Destroy(gameObject);
            }
            break;
        case ERollState.Handedness:
            if ((Gamepad.current?.dpad.up.wasPressedThisFrame ?? false) || (Gamepad.current?.dpad.down.wasPressedThisFrame ?? false))
            {
                PlayerData.sData.leftHanded = !PlayerData.sData.leftHanded;
                Utils.PlayClip2d(moveSelection);
            }
            else if (Gamepad.current?.aButton.wasPressedThisFrame ?? false)
            {
                Utils.PlayClip2d(makeSelection);
                ++rollState;
            }
            else if (cancelPressedThisFrame)
            {
                Utils.PlayClip2d(cancelSelection);
                --rollState;
            }
            break;
        case ERollState.Class:
            if (Gamepad.current?.dpad.up.wasPressedThisFrame ?? false)
            {
                PlayerData.sData.playerClass -= 2;
                if (PlayerData.sData.playerClass < EPlayerClass.Fighter)
                {
                    PlayerData.sData.playerClass += 8;
                }
                Utils.PlayClip2d(moveSelection);
            }
            else if (Gamepad.current?.dpad.down.wasPressedThisFrame ?? false)
            {
                PlayerData.sData.playerClass += 2;
                if (PlayerData.sData.playerClass > EPlayerClass.Shepherd)
                {
                    PlayerData.sData.playerClass -= 8;
                }
                Utils.PlayClip2d(moveSelection);
            }
            else if ((Gamepad.current?.dpad.left.wasPressedThisFrame ?? false)
                     || (Gamepad.current?.dpad.right.wasPressedThisFrame ?? false))
            {
                PlayerData.sData.playerClass = (EPlayerClass)((int)PlayerData.sData.playerClass ^ 1);
                Utils.PlayClip2d(moveSelection);
            }
            else if (Gamepad.current?.aButton.wasPressedThisFrame ?? false)
            {
                PlayerObject.LoadSkills();

                RollStats();
                // skillsAfterRollStats is now set by RollStats()

                ++rollState;
            }
            else if (cancelPressedThisFrame)
            {
                Utils.PlayClip2d(cancelSelection);
                --rollState;
            }
            break;
        case ERollState.Skills:
            {
                PlayerObject.ClassData data = PlayerObject.classData[(int)PlayerData.sData.playerClass]; 
                byte[] skills = data.skills[skillRollIndex];
            
                if (Gamepad.current?.dpad.left.wasPressedThisFrame ?? false)
                {
                    Utils.PlayClip2d(moveSelection);
                    skillChoiceIndex ^= 1;
                }
                else if (Gamepad.current?.dpad.right.wasPressedThisFrame ?? false)
                {
                    Utils.PlayClip2d(moveSelection);
                    skillChoiceIndex ^= 1;
                }
                else if (Gamepad.current?.dpad.up.wasPressedThisFrame ?? false)
                {
                    Utils.PlayClip2d(moveSelection);
                    skillChoiceIndex -= 2;
                    if (skillChoiceIndex < 0)
                    {
                        skillChoiceIndex += skills.Length;
                    }
                }
                else if (Gamepad.current?.dpad.down.wasPressedThisFrame ?? false)
                {
                    Utils.PlayClip2d(moveSelection);
                    skillChoiceIndex += 2;
                    if (skillChoiceIndex >= skills.Length)
                    {
                        skillChoiceIndex -= skills.Length;
                    }
                }
                else if (Gamepad.current?.aButton.wasPressedThisFrame ?? false)
                {
                    Utils.PlayClip2d(diceRoll);
                    byte chosenSkill = skills[skillChoiceIndex];
                    // Increase the skill
                    IncreaseSkill(chosenSkill);
                    ++skillRollIndex;
                    skillChoiceIndex = 0;
                    if (skillRollIndex == 5 || data.skills[skillRollIndex].Length == 0)
                    {
                        ++rollState;
                    }
                }
                else if (cancelPressedThisFrame)
                {
                    Utils.PlayClip2d(cancelSelection);
                    // Restore all skills to the state after RollStats()
                    System.Array.Copy(skillsAfterRollStats, PlayerData.sData.skill, skillsAfterRollStats.Length);
                    // Reset skill selection state - find first multi-choice screen
                    FindFirstMultiChoiceSkillScreen();
                    skillChoiceIndex = 0;
                    // Go back to Class (skip Stats)
                    rollState = ERollState.Class;
                }
            }
            break;
        case ERollState.Portrait:
            if ((Gamepad.current?.dpad.up.wasPressedThisFrame ?? false)
                || (Gamepad.current?.dpad.left.wasPressedThisFrame ?? false))
            {
                Utils.PlayClip2d(moveSelection);
                PlayerData.sData.portrait = (PlayerData.sData.portrait + 4) % 5;
            }
            else if ((Gamepad.current?.dpad.down.wasPressedThisFrame ?? false)
                     || (Gamepad.current?.dpad.right.wasPressedThisFrame ?? false))
            {
                Utils.PlayClip2d(moveSelection);
                PlayerData.sData.portrait = (PlayerData.sData.portrait + 1) % 5;
            }
            else if (Gamepad.current?.aButton.wasPressedThisFrame ?? false)
            {
                Utils.PlayClip2d(makeSelection);
                ++rollState;
            }
            else if (cancelPressedThisFrame)
            {
                Utils.PlayClip2d(cancelSelection);
                // Restore all skills to the state after RollStats()
                System.Array.Copy(skillsAfterRollStats, PlayerData.sData.skill, skillsAfterRollStats.Length);
                // Reset skill selection state - find first multi-choice screen
                FindFirstMultiChoiceSkillScreen();
                skillChoiceIndex = 0;
                --rollState;
            }
            break;
        case ERollState.Difficulty:
            if ((Gamepad.current?.dpad.up.wasPressedThisFrame ?? false) || (Gamepad.current?.dpad.down.wasPressedThisFrame ?? false))
            {
                Utils.PlayClip2d(moveSelection);
                PlayerData.sData.easy = !PlayerData.sData.easy;
            }
            else if (Gamepad.current?.aButton.wasPressedThisFrame ?? false)
            {
                Utils.PlayClip2d(makeSelection);
                ++rollState;
#if UNITY_EDITOR
                EditorApplication.ExecuteMenuItem("Window/General/Game");
#endif
            }
            else if (cancelPressedThisFrame)
            {
                Utils.PlayClip2d(cancelSelection);
                --rollState;
            }
            break;
        case ERollState.Name:
            break;
        case ERollState.Keep:
            if ((Gamepad.current?.dpad.up.wasPressedThisFrame ?? false) || (Gamepad.current?.dpad.down.wasPressedThisFrame ?? false))
            {
                Utils.PlayClip2d(moveSelection);
                keepChar = !keepChar;
            }
            if ((Gamepad.current?.aButton.wasPressedThisFrame ?? false))
            {
                if (keepChar)
                {
                    Utils.PlayClip2d(startGame);
                    ++rollState;
                }
                else
                {
                    Utils.PlayClip2d(cancelSelection);
                    // try again
                    rollState = ERollState.Sex;

                    // reset character
                    PlayerData.sData.female = true;
                    PlayerData.sData.leftHanded = true;
                    PlayerData.sData.playerClass = EPlayerClass.Fighter;
                    for (int i = 0; i < 20; ++i)
                    {
                        PlayerData.sData.skill[i] = 0;
                    }
                    PlayerData.sData.strength = 0;
                    PlayerData.sData.intellect = 0;
                    PlayerData.sData.dexterity = 0;
                    PlayerData.sData.portrait = 0;
                    PlayerData.sData.playerName = "";
                    PlayerData.sData.easy = false;
                    
                    keepChar = true;
                }
            }
            else if (cancelPressedThisFrame)
            {
                Utils.PlayClip2d(cancelSelection);
                --rollState;
            }
            break;
        case ERollState.Done:
            fadeAlpha += Time.deltaTime;
            if (fadeAlpha > 1.0f)
            {
                Skills.ManaAdvanced();
                
                // Save character sex to PlayerPrefs for frontend background display
                PlayerPrefs.SetString("LastCharacterSex", PlayerData.sData.female ? "female" : "male");
                PlayerPrefs.Save();
                
                wasCancelled = false; // Character creation completed successfully
                Destroy(gameObject);
            }
            break;
        }
    }

    private int screenX(int x)
    {
        return Screen.width * x / 320;
    }

    private int screenY(int y)
    {
        return Screen.height * y / 200;
    }

    private void GuiLabel(int x, int y, string text, GUIStyle textStyle)
    {
        GUI.Label(new Rect(screenX(x), screenY(y), 400, 100), text, textStyle);
    }

    private void GuiLabel(int x, int y, int w, int h, string text, GUIStyle textStyle)
    {
        GUI.Label(new Rect(screenX(x), screenY(y), screenX(w), screenY(h)), text, textStyle);
    }

    private static Texture2D boxTex;
    private static Texture2D yellowBoxTex;

    private void GuiBlackBox(int x1, int x2, int y1, int y2)
    {
        if (boxTex == null)
        {
            boxTex = new Texture2D(1, 1);
            boxTex.SetPixel(0, 0, new Color(0.0f, 0.0f, 0.0f, 1.0f));
            boxTex.Apply();
        }
        int x = screenX(x1);
        int y = screenY(y1);
        int w = screenX(x2) - screenX(x1);
        int h = screenY(y2) - screenY(y1);
        GUI.DrawTexture(new Rect(x, y, w, h), boxTex, ScaleMode.StretchToFill);
    }

    private void GuiYellowBox(int x1, int x2, int y1, int y2)
    {
        if (yellowBoxTex == null)
        {
            yellowBoxTex = new Texture2D(1, 1);
            // Gold/bronze color matching the UI borders (more muted than bright yellow)
            yellowBoxTex.SetPixel(0, 0, new Color(0.6f, 0.4f, 0.1f, 1.0f));
            yellowBoxTex.Apply();
        }
        int x = screenX(x1);
        int y = screenY(y1);
        int w = screenX(x2) - screenX(x1);
        int h = screenY(y2) - screenY(y1);
        GUI.DrawTexture(new Rect(x, y, w, h), yellowBoxTex, ScaleMode.StretchToFill);
    }

    private void GuiButton(int x, int y, string text, bool selected)
    {
        int sx = screenX(x);
        int sy = screenY(y);
        int w = screenX(67);
        int h = screenY(16);

        GUI.DrawTexture(new Rect(sx,  sy, w, h), buttons[0], ScaleMode.StretchToFill);
        GUI.DrawTexture(new Rect(sx, sy, w, h), buttons[selected ? 2 : 1], ScaleMode.StretchToFill, true);
        GuiBlackBox(x + 2, x + 66, y, y + 1);
        GuiBlackBox(x + 2, x + 66, y + 14, y + 15);
        GuiBlackBox(x + 1, x + 2, y + 1, y + 14);
        GuiBlackBox(x + 66, x + 67, y + 1, y + 14);
        GUI.Label(new Rect(sx, sy, w, h), text, selected ? selectedButtonStyle : buttonStyle);
    }

    private Rect ButtonRect(int x, int y)
    {
        return new Rect(screenX(x), screenY(y), screenX(67), screenY(16));
    }

    private void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.CreateCharacter;

        labelStyle.fontSize = Screen.height / 20;
        highlightedLabelStyle.fontSize = labelStyle.fontSize;
        buttonStyle.fontSize = Screen.height / 20;
        selectedButtonStyle.fontSize = buttonStyle.fontSize;
        descriptionStyle.fontSize = Screen.height / 30;

        GuiInput.RegisterBlockingRect(Screen.safeArea);
        GUI.DrawTexture(Screen.safeArea, background, ScaleMode.StretchToFill, false);

        bool allowHover = GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard;
        Vector2 mouseGui = Event.current != null ? Event.current.mousePosition : Vector2.zero;

        switch (rollState)
        {
        case ERollState.Sex:
            GuiLabel(200, 75, "Choose character sex:", labelStyle);
            // Display last played sex first
            string lastCharacterSex = PlayerPrefs.GetString("LastCharacterSex", "male");
            if (lastCharacterSex == "female")
            {
                bool hoverTop = allowHover && ButtonRect(207, 93).Contains(mouseGui);
                bool hoverBot = allowHover && ButtonRect(207, 113).Contains(mouseGui);
                GuiButton(207, 93, "Female", allowHover ? hoverTop : PlayerData.sData.female);
                GuiButton(207, 113, "Male", allowHover ? hoverBot : !PlayerData.sData.female);
            }
            else
            {
                bool hoverTop = allowHover && ButtonRect(207, 93).Contains(mouseGui);
                bool hoverBot = allowHover && ButtonRect(207, 113).Contains(mouseGui);
                GuiButton(207, 93, "Male", allowHover ? hoverTop : !PlayerData.sData.female);
                GuiButton(207, 113, "Female", allowHover ? hoverBot : PlayerData.sData.female);
            }
            GuiLabel(170, 160, 140, 40, "Gender has no effect on your character\u2019s strength or abilities.", descriptionStyle);
            break;
        case ERollState.Handedness:
            GuiLabel(200, 75, "Select handedness:", labelStyle);
            bool hoverHandLeft = allowHover && ButtonRect(207, 93).Contains(mouseGui);
            bool hoverHandRight = allowHover && ButtonRect(207, 113).Contains(mouseGui);
            GuiButton(207, 93, "Left", allowHover ? hoverHandLeft : PlayerData.sData.leftHanded);
            GuiButton(207, 113, "Right", allowHover ? hoverHandRight : !PlayerData.sData.leftHanded);
            GuiLabel(170, 160, 140, 40, "Determines which hand you use to hold your primary weapon, and which is your shield-hand.", descriptionStyle);
            break;
        case ERollState.Class:
            GuiLabel(200, 15, "Pick a class:", labelStyle);
            int hoveredClassIndex = -1;
            for (int i = 0; i < 8; ++i)
            {
                int x = 170 + 74 * (i & 1);
                int y = 32 + 20 * (i / 2);
                bool hovered = allowHover && ButtonRect(x, y).Contains(mouseGui);
                if (hovered)
                    hoveredClassIndex = i;
                GuiButton(x, y, ((EPlayerClass)i).ToString(), allowHover ? hovered : i == (int)PlayerData.sData.playerClass);
            }
            if (!allowHover)
            {
                GuiLabel(170, 130, 140, 60, classDescriptions[(int)PlayerData.sData.playerClass].Replace(" - ", "\u2014").Replace("'", "\u2019"), descriptionStyle);
            }
            else if (hoveredClassIndex >= 0)
            {
                GuiLabel(170, 130, 140, 60, classDescriptions[hoveredClassIndex].Replace(" - ", "\u2014").Replace("'", "\u2019"), descriptionStyle);
            }
            break;
        case ERollState.Skills:
            GuiLabel(200, 20, "Pick a skill:", labelStyle);
            PlayerObject.ClassData classData = PlayerObject.classData[(int) PlayerData.sData.playerClass];
            // Ensure skillRollIndex is within bounds
            if (skillRollIndex < 0 || skillRollIndex >= classData.skills.Count)
            {
                skillRollIndex = Mathf.Max(0, Mathf.Min(skillRollIndex, classData.skills.Count - 1));
            }
            byte[] skills = classData.skills[skillRollIndex];
            // Ensure skills array is valid
            if (skills != null && skills.Length > 0)
            {
                int hoveredSkillChoiceIndex = -1;
                for (int i = 0; i < skills.Length; ++i)
                {
                    int x = 170 + 74 * (i & 1);
                    int y = 37 + 20 * (i / 2);
                    bool hovered = allowHover && ButtonRect(x, y).Contains(mouseGui);
                    if (hovered)
                        hoveredSkillChoiceIndex = i;
                    GuiButton(x, y, ((ESkill)skills[i]).ToString(), allowHover ? hovered : i == skillChoiceIndex);
                }
                if (!allowHover)
                {
                    GuiLabel(170, 160, 140, 40, skillDescriptions[skills[skillChoiceIndex]], descriptionStyle);
                }
                else if (hoveredSkillChoiceIndex >= 0)
                {
                    GuiLabel(170, 160, 140, 40, skillDescriptions[skills[hoveredSkillChoiceIndex]], descriptionStyle);
                }
            }
            break;
        case ERollState.Portrait:
            GuiLabel(200, 5, "Pick a look:", labelStyle);
            
            for (int i = 0; i < 5; ++i)
            {
                Texture2D tex = headsTex[(PlayerData.sData.female ? 5 : 0) + i];

                int texWidth = tex.width * 5 / 6;
                
                // Draw portrait
                GUI.DrawTexture(new Rect(screenX(217), screenY(20 + 35 * i), screenX(texWidth), screenY(tex.height)), tex);
            }
            
            // Second pass: draw highlight on top of one portrait (hover only for kb/m; selection for gamepad)
            int borderIndex = PlayerData.sData.portrait;
            if (allowHover)
            {
                borderIndex = -1;
                for (int hi = 0; hi < 5; ++hi)
                {
                    Texture2D ht = headsTex[(PlayerData.sData.female ? 5 : 0) + hi];
                    int tw = ht.width * 5 / 6;
                    Rect portraitR = new Rect(screenX(217), screenY(20 + 35 * hi), screenX(tw), screenY(ht.height));
                    if (portraitR.Contains(mouseGui))
                    {
                        borderIndex = hi;
                        break;
                    }
                }
            }
            if (borderIndex >= 0 && borderIndex < 5)
            {
                int borderThickness = 2;
                int x = 217;
                int y = 20 + 35 * borderIndex;
                int w = headsTex[0].width * 5 / 6;
                int h = headsTex[0].height;
                
                // Draw gold border around selected portrait
                int borderX1 = x - borderThickness;
                int borderX2 = x + w + borderThickness;
                int borderY1 = y - borderThickness;
                int borderY2 = y + h + borderThickness;
                
                // Top border
                GuiYellowBox(borderX1, borderX2, borderY1, y);
                // Bottom border
                GuiYellowBox(borderX1, borderX2, y + h, borderY2);
                // Left border
                GuiYellowBox(borderX1, x, borderY1, borderY2);
                // Right border
                GuiYellowBox(x + w, borderX2, borderY1, borderY2);
            }
            break;
        case ERollState.Difficulty:
            GuiLabel(200, 75, "Choose difficulty:", labelStyle);
            bool hoverStandard = allowHover && ButtonRect(207, 93).Contains(mouseGui);
            bool hoverEasy = allowHover && ButtonRect(207, 113).Contains(mouseGui);
            GuiButton(207, 93, "Standard", allowHover ? hoverStandard : !PlayerData.sData.easy);
            GuiButton(207, 113, "Easy", allowHover ? hoverEasy : PlayerData.sData.easy);
            GuiLabel(170, 160, 140, 60, "In easy mode, monsters and hostile characters are less dangerous and are easier to defeat than in standard mode.", descriptionStyle);
            break;
        case ERollState.Name:
            // Show virtual keyboard if not already visible
            if (KeyboardGUI.sKeyboard != null && !KeyboardGUI.sKeyboard.IsVisible())
            {
                float keyboardX = screenX(180);
                float keyboardY = screenY(80);
                KeyboardGUI.sKeyboard.Show(PlayerData.sData.playerName, (result) =>
                    {
                        PlayerData.sData.playerName = result;
                        Utils.PlayClip2d(makeSelection);
                        keepChar = true;
                        ++rollState;
                    },
                    null,
                    new Vector2(keyboardX, keyboardY),
                    "Name:",
                    false,
                    false); // allowCancel = false (can't cancel or enter empty string)
            }
            break;
        case ERollState.Keep:
            GuiLabel(200, 75, "Keep this character?", labelStyle);
            GuiButton(207, 93, "Yes", allowHover ? ButtonRect(207, 93).Contains(mouseGui) : keepChar);
            GuiButton(207, 113, "No", allowHover ? ButtonRect(207, 113).Contains(mouseGui) : !keepChar);
            break;
        case ERollState.Done:
            GuiLabel(200, 85, PlayerData.sData.playerName, labelStyle);
            GuiLabel(200, 100, "enters the abyss...", labelStyle);
            break;
        }
        if (rollState > ERollState.Sex)
        {
            GuiLabel(20, 20, PlayerData.sData.female ? "Female" : "Male", labelStyle);
        }

        if (rollState > ERollState.Class)
        {
            EStat governingStat = EStat.None;
            if (rollState == ERollState.Skills)
            {
                PlayerObject.ClassData classData = PlayerObject.classData[(int) PlayerData.sData.playerClass];
                byte[] skills = classData.skills[skillRollIndex];
                governingStat = skillGoverningStat[skills[skillChoiceIndex]];
            }
            GuiLabel(90, 20, PlayerData.sData.playerClass.ToString(), labelStyle);
            GuiLabel(90, 48, $"Strength: {PlayerData.sData.strength}", governingStat == EStat.Strength ? highlightedLabelStyle : labelStyle);
            GuiLabel(90, 66, $"Dexterity: {PlayerData.sData.dexterity}", governingStat == EStat.Dexterity ? highlightedLabelStyle : labelStyle);
            GuiLabel(90, 84, $"Intellect: {PlayerData.sData.intellect}", governingStat == EStat.Intellect ? highlightedLabelStyle : labelStyle);
            GuiLabel(90, 102, $"Vitality: {PlayerData.sData.vitality}", labelStyle);

            int y = 133;
            for (int i = 0; i < 20; ++i)
            {
                if (PlayerData.sData.skill[i] > 0)
                {
                    GuiLabel(30, y, $"{((ESkill)i)}: {PlayerData.sData.skill[i]}", labelStyle);
                    y += 10;
                }
            }
        }

        if (rollState > ERollState.Portrait)
        {
            Texture2D tex = buttons[(PlayerData.sData.female ? 22 : 17) + PlayerData.sData.portrait];
            GUI.DrawTexture(new Rect(screenX(34), screenY(48), screenX(tex.width) * 0.833f, screenY(tex.height)), tex);
        }

        if (rollState > ERollState.Name)
        {
            GuiLabel(50, 10, PlayerData.sData.playerName, labelStyle);
        }

        if (rollState == ERollState.Done)
        {
            Utils.DrawFade(fadeAlpha);
        }

        HandleCreateCharacterMouseClicks();
    }

    private void HandleCreateCharacterMouseClicks()
    {
        if (!GuiInput.IsLeftMouseDownGui)
            return;

        switch (rollState)
        {
        case ERollState.Sex:
            {
                string last = PlayerPrefs.GetString("LastCharacterSex", "male");
                Rect top = new Rect(screenX(207), screenY(93), screenX(67), screenY(16));
                Rect bot = new Rect(screenX(207), screenY(113), screenX(67), screenY(16));
                if (last == "female")
                {
                    if (GuiInput.TryConsumeClickInRect(top))
                    {
                        PlayerData.sData.female = true;
                        Utils.PlayClip2d(makeSelection);
                        ++rollState;
                    }
                    else if (GuiInput.TryConsumeClickInRect(bot))
                    {
                        PlayerData.sData.female = false;
                        Utils.PlayClip2d(makeSelection);
                        ++rollState;
                    }
                }
                else
                {
                    if (GuiInput.TryConsumeClickInRect(top))
                    {
                        PlayerData.sData.female = false;
                        Utils.PlayClip2d(makeSelection);
                        ++rollState;
                    }
                    else if (GuiInput.TryConsumeClickInRect(bot))
                    {
                        PlayerData.sData.female = true;
                        Utils.PlayClip2d(makeSelection);
                        ++rollState;
                    }
                }
            }
            break;
        case ERollState.Handedness:
            if (GuiInput.TryConsumeClickInRect(new Rect(screenX(207), screenY(93), screenX(67), screenY(16))))
            {
                PlayerData.sData.leftHanded = true;
                Utils.PlayClip2d(makeSelection);
                ++rollState;
            }
            else if (GuiInput.TryConsumeClickInRect(new Rect(screenX(207), screenY(113), screenX(67), screenY(16))))
            {
                PlayerData.sData.leftHanded = false;
                Utils.PlayClip2d(makeSelection);
                ++rollState;
            }
            break;
        case ERollState.Class:
            for (int i = 0; i < 8; ++i)
            {
                int bx = 170 + 74 * (i & 1);
                int by = 32 + 20 * (i / 2);
                if (!GuiInput.TryConsumeClickInRect(new Rect(screenX(bx), screenY(by), screenX(67), screenY(16))))
                    continue;
                PlayerData.sData.playerClass = (EPlayerClass)i;
                PlayerObject.LoadSkills();
                RollStats();
                Utils.PlayClip2d(makeSelection);
                ++rollState;
                break;
            }
            break;
        case ERollState.Skills:
            {
                PlayerObject.ClassData data = PlayerObject.classData[(int)PlayerData.sData.playerClass];
                if (skillRollIndex < 0 || skillRollIndex >= data.skills.Count)
                    break;
                byte[] skills = data.skills[skillRollIndex];
                if (skills == null || skills.Length == 0)
                    break;
                for (int i = 0; i < skills.Length; ++i)
                {
                    int bx = 170 + 74 * (i & 1);
                    int by = 37 + 20 * (i / 2);
                    if (!GuiInput.TryConsumeClickInRect(new Rect(screenX(bx), screenY(by), screenX(67), screenY(16))))
                        continue;
                    skillChoiceIndex = i;
                    Utils.PlayClip2d(diceRoll);
                    byte chosenSkill = skills[skillChoiceIndex];
                    IncreaseSkill(chosenSkill);
                    ++skillRollIndex;
                    skillChoiceIndex = 0;
                    if (skillRollIndex == 5 || skillRollIndex >= data.skills.Count || data.skills[skillRollIndex].Length == 0)
                        ++rollState;
                    break;
                }
            }
            break;
        case ERollState.Portrait:
            for (int i = 0; i < 5; ++i)
            {
                Texture2D headTex = headsTex[(PlayerData.sData.female ? 5 : 0) + i];
                int texWidth = headTex.width * 5 / 6;
                Rect pr = new Rect(screenX(217), screenY(20 + 35 * i), screenX(texWidth), screenY(headTex.height));
                if (GuiInput.TryConsumeClickInRect(pr))
                {
                    PlayerData.sData.portrait = i;
                    Utils.PlayClip2d(makeSelection);
                    ++rollState;
                    break;
                }
            }
            break;
        case ERollState.Difficulty:
            if (GuiInput.TryConsumeClickInRect(new Rect(screenX(207), screenY(93), screenX(67), screenY(16))))
            {
                PlayerData.sData.easy = false;
                Utils.PlayClip2d(makeSelection);
                ++rollState;
#if UNITY_EDITOR
                EditorApplication.ExecuteMenuItem("Window/General/Game");
#endif
            }
            else if (GuiInput.TryConsumeClickInRect(new Rect(screenX(207), screenY(113), screenX(67), screenY(16))))
            {
                PlayerData.sData.easy = true;
                Utils.PlayClip2d(makeSelection);
                ++rollState;
#if UNITY_EDITOR
                EditorApplication.ExecuteMenuItem("Window/General/Game");
#endif
            }
            break;
        case ERollState.Keep:
            if (GuiInput.TryConsumeClickInRect(new Rect(screenX(207), screenY(93), screenX(67), screenY(16))))
            {
                keepChar = true;
                Utils.PlayClip2d(startGame);
                ++rollState;
            }
            else if (GuiInput.TryConsumeClickInRect(new Rect(screenX(207), screenY(113), screenX(67), screenY(16))))
            {
                Utils.PlayClip2d(cancelSelection);
                rollState = ERollState.Sex;
                PlayerData.sData.female = true;
                PlayerData.sData.leftHanded = true;
                PlayerData.sData.playerClass = EPlayerClass.Fighter;
                for (int i = 0; i < 20; ++i)
                    PlayerData.sData.skill[i] = 0;
                PlayerData.sData.strength = 0;
                PlayerData.sData.intellect = 0;
                PlayerData.sData.dexterity = 0;
                PlayerData.sData.portrait = 0;
                PlayerData.sData.playerName = "";
                PlayerData.sData.easy = false;
                keepChar = true;
            }
            break;
        }

        GuiInput.TryConsumeClickInPanel(Screen.safeArea);
    }
}
