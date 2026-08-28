using UnityEngine;
using System.Collections.Generic;
using Screen = UnityEngine.Device.Screen;

public class ShowAllObjectIcons : MonoBehaviour
{
    public bool ShowAll;
    public void OnGUI()
    {
        GUI.depth = -1;

        GUI.DrawTexture(PlayerObject.Player.mainCamera.pixelRect, Texture2D.grayTexture, ScaleMode.StretchToFill, false);

        int[] count = new int[512];
        foreach (var o in GameObject.FindObjectsByType<UUObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            ++count[(int)o.type];
        }

        {
            int i = 0, j = 0;
            foreach (var tex in DataLoader.sDataLoader.objTex)
            {
                int m = Screen.width / 100;
                int x = j % m;
                int y = j / m;
                string nameOfThing = StringLoader.GetString(4, i);

                bool show = ShowAll && tex != null;
                if (!show)
                {
                    if (nameOfThing.Length > 0 && !nameOfThing.Contains("open ")
                        && i != 271 && i != 277 && i != 288 && i != 351 && i != 345
                        && ((i < 19) || (i >= 24 && i < 64) || (i >= 128 && i <= 147) ||
                            (i >= 152 && i <= 231)
                            || (i >= 256 && i <= 319) || i == 326 || i == 336
                            || (i >= 340 && i <= 352) || (i == 357) || (i >= 448 && i <= 459)))
                    {
                        show = true;
                    }
                }
            
                if (show)
                {
                    GUI.DrawTexture(new Rect(100 * x + 2, 90 * y + 2, 3 * tex.width, 3.6f * tex.height), tex);
                    GUI.Label(new Rect(100 * x, 90 * y + 55, 90, 40), $"{i}. {DataLoader.GetCleanedObjectName((EObjectType)i)}");
                    GUI.Label(new Rect(100 * x + 64, 90 * y, 20, 20), $"{DataLoader.sDataLoader.comObjProps[i].scale}");
                
                    ++j;
                }
                ++i;
            }
        }

        List<List<string>> descs = new List<List<string>>();
        for (int i = 0; i < 16; ++i)
        {
            descs.Add(new List<string>());
        }
        int by = 980;
        foreach (var o in GameObject.FindObjectsByType<UUObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (o.getClass == UUObject.EClass.Keys)
            {
                string desc = StringLoader.GetString(5, (100 + o.ownerIndex));
                if (desc.Length > 0)
                {
                    desc = desc.Substring(0, desc.Length - 1);
                    int index = (int)o.type - 256;
                    if (!descs[index].Contains(desc))
                    {
                        descs[index].Add(desc);
                    }
                }
            }

            if (o.getClass == UUObject.EClass.Books)
            {
                if (o.special >= 512)
                {
                    string desc = StringLoader.GetString(3, o.special - 512);
                    if (desc.Length > 0)
                    {
                        GUI.Label(new Rect(100, by, 500, 20), $"{o.type}. {desc}");
                        by += 14;
                    }
                }
            }

            if (o.getClass == UUObject.EClass.TrapsA || o.getClass == UUObject.EClass.TrapsB)
            {
                if (o.type == EObjectType.CreateObjectTrap)
                {
                    UUObject spawn = LevelLoader.GetLevel().objects[o.chainIndex];
                    if (spawn.getClass == UUObject.EClass.Keys
                        && !o.name.EndsWith("_KeySpawner"))
                    {
                        o.name += "_KeySpawner";
                    }
                }
            }
        }

        {
            int y = 980;
            for (int i = 0; i < 16; ++i)
            {
                if (descs[i].Count > 0)
                {
                    string joined = "";
                    for (int j = 0; j < descs[i].Count; ++j)
                    {
                        joined += descs[i][j] + " ";
                    }
                    GUI.Label(new Rect(600, y, 1000, 20), $"{256 + i}.({descs[i].Count}) {joined}");
                    y += 14;
                }
            }
        }
    }
}
