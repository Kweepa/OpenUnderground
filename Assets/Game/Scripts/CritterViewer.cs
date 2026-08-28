using UnityEngine;

public class CritterViewer : MonoBehaviour
{
    private int[] dup =
    {
        0, 0, 0, 0, 0, 1, 0, 1, 1, 1, 0, 0,
        1, 1, 1, 0, 1, 1, 1, 1, 0, 0, 1, 0,
        1, 1, 0, 0, 1, 0, 1, 1, 0, 0, 1, 0,
        1, 1, 0, 0, 1, 1, 1, 0, 1, 1, 1, 1,
        1, 1, 0, 1, 1, 0, 0, 1, 0, 1, 0, 1,
        0, 0, 0
    };

    public bool twoFrames;
    public int focusCrit;
    
    public void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.CritterViewer;

        GUI.DrawTexture(PlayerObject.Player.mainCamera.pixelRect, Texture2D.grayTexture, ScaleMode.StretchToFill, false);

        if (focusCrit < 64 || focusCrit > 126)
        {
            int j = 0;
            int spacing = twoFrames ? 320 : 160;
            for (int i = 0; i < 63; ++i) // 63 is the player
            {
                if (!twoFrames || dup[i] == 0)
                {
                    EObjectType type = (EObjectType)(64 + i);
                    CritterLoader.Critter critter = CritterLoader.GetCritter(type);

                    if (critter != null && critter.slots.Length > 0 && critter.slots[0,0] != null)
                    {
                        // first number is 8 * anim + rot, second is frame #
                        // anim 4 is idle
                        if (critter.slots[8 * 4 + 4, 0].mat != null)
                        {
                            int x = (j % (twoFrames ? 6 : 12));
                            int y = (j / (twoFrames ? 6 : 12));
                            string cleanedName = DataLoader.GetCleanedObjectName(type);
                            GUI.Label(new Rect(spacing * x + 12, 220 * y + 182, 160, 40), $"{64 + i}. {cleanedName}");
                            for (int f = 0; f <= (twoFrames ? 2 : 0); f += 2)
                            {
                                // both of these have super wide side views
                                int xoff = (f == 2 && (i + 64 == (int)EObjectType.GiantRatBrown || i + 64 == (int)EObjectType.GreenLizardman)) ? -40 : 0;
                                Texture tex = critter.slots[8 * 4 + 4 - f, 0].mat.mainTexture;
                                GUI.DrawTexture(new Rect(spacing * x + 12 + 80 * f + xoff, 220 * y + 182 - 3 * tex.height, 3 * tex.width, 3 * tex.height), tex);
                            }
                            ++j;
                        }
                    }
                }
            }
        }
        else
        {
            int[][] variants =
            {
                new []{ 64, 82 },
                new []{ 65, 69 },
                new []{ 66, 73 },
                new []{ 67, 72 },
                new []{ 68, 83, 92 },
                new []{ 70, 71, 76, 77, 78, 80 },
                new []{ 75, 81 },
                new []{ 84, 86 },
                new []{ 85, 88, 89 },
                new []{ 87, 116 },
                new []{ 90, 95, 98, 104 },
                new []{ 93, 94 },
                new []{ 96, 111, 112 },
                new []{ 97, 100, 101, 113},
                new []{ 99, 105, 110 },
                new []{ 103, 106, 108, 109, 115, 123 },
                new []{ 114, 119, 121 }
            };
            CritterLoader.Critter critter = CritterLoader.GetCritter((EObjectType)focusCrit);
            if (critter != null && critter.slots.Length > 0)
            {
                string cleanedName = DataLoader.GetCleanedObjectName((EObjectType)focusCrit);
                GUI.Label(new Rect(0, 0, 160, 40), $"{focusCrit}. {cleanedName}");
                int y = 16;
                int startX = 16;
                // idle, walk, idle, combat idle, bash, slash, thrust, projectile, die
                int[] slots = { 32 + 4, 128 + 2, 128, 0, 1, 2, 3, 5, 12 };
                string[] slotNames =
                    { "idle", "walk", "walk", "combat idle", "bash", "slash", "thrust", "projectile", "die" };

                if (focusCrit == (int)EObjectType.TwilightZoneA)
                {
                    slots = new [] { 32, 40, 80, 128 };
                    slotNames = new [] { "bat", "teeth", "spiral", "devil panther" };
                }
                else if (focusCrit == (int)EObjectType.TwilightZoneB)
                {
                    slots = new [] { 32, 80 };
                    slotNames = new [] { "lightning", "fish" };
                }
                else if (focusCrit == (int)EObjectType.TwilightZoneC)
                {
                    slots = new [] { 32, 80 };
                    slotNames = new [] { "eyeball", "skull" };
                }
                
                for (int s = 0; s < slots.Length; ++s)
                {
                    int slot = slots[s];
                    if (critter.frameCount[slot] > 0)
                    {
                        int x = startX;
                        int maxy = 0;
                        for (int i = 0; i < critter.frameCount[slot]; ++i)
                        {
                            Texture tex = critter.slots[slot, i].mat.mainTexture;
                            maxy = Mathf.Max(maxy, tex.height);
                        }
                        for (int i = 0; i < critter.frameCount[slot]; ++i)
                        {
                            Texture tex = critter.slots[slot, i].mat.mainTexture;
                            GUI.DrawTexture(new Rect(x, y + 3.6f * (maxy - tex.height), 3 * tex.width, 3.6f * tex.height), tex);
                            x += 3 * tex.width + 16;
                        }

                        int curY = (int)(3.6f * maxy) + 16; 
                        y += curY;
                        
                        GUI.Label(new Rect(startX, y - 20, 160, 40), slotNames[s]);

                        if (y >= Screen.height - curY)
                        {
                            y = 16;
                            startX = Screen.width / 2;
                        }
                    }
                }
                // draw 360s for each palette variant
                int variant = -1;
                for (int i = 0; i < variants.Length; ++i)
                {
                    if (variants[i][0] == focusCrit)
                    {
                        variant = i;
                        break;
                    }
                }

                {
                    int x = startX;
                    int h = 0;
                    for (int i = 0; i < 8; ++i)
                    {
                        Texture tex = critter.slots[32 + i, 0].mat.mainTexture;
                        h = tex.height;
                        GUI.DrawTexture(new Rect(x, y, 3 * tex.width, 3.6f * tex.height), tex);
                        x += 3 * tex.width + 16;
                    }

                    if (variant >= 0)
                    {
                        for (int j = 1; j < variants[variant].Length; ++j)
                        {
                            CritterLoader.Critter varCrit = CritterLoader.GetCritter((EObjectType)variants[variant][j]);
                            if (varCrit != null)
                            {
                                int curY = (int)(3.6f * h) + 16; 
                                y += curY;

                                if (y >= Screen.height - curY)
                                {
                                    y = 16;
                                    startX = Screen.width / 2;
                                }

                                x = startX;
                                for (int i = 0; i < 8; ++i)
                                {
                                    Texture tex = varCrit.slots[32 + i, 0].mat.mainTexture;
                                    GUI.DrawTexture(new Rect(x, y, 3 * tex.width, 3.6f * tex.height), tex);
                                    x += 3 * tex.width + 16;
                                }
                                GUI.Label(new Rect(startX, y + curY - 20, 160, 40), $"{variants[variant][j]}");
                            }
                        }
                    }
                }
            }
        }
    }
}
