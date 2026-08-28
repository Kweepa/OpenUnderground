using System;
using UnityEngine;

public class CritterHealthDisplay : MonoBehaviour
{
    public int lastDamageFraction;
    public float enabledFraction;
    private float damageTime;
    private float visibleY;

    public static CritterHealthDisplay sHealthDisplay;

    private void Start()
    {
        sHealthDisplay = this;
    }

    public void Update()
    {
        if (damageTime > 0.0f)
        {
            damageTime -= Time.deltaTime;
            enabledFraction = Mathf.MoveTowards(enabledFraction, 1.0f, Time.deltaTime);
            visibleY = Utils.DampedApproach(visibleY, 1.0f, 0.5f);
        }
        else
        {
            enabledFraction = Mathf.MoveTowards(enabledFraction, 0.0f, Time.deltaTime);
            if (enabledFraction == 0.0f)
            {
                visibleY = Utils.DampedApproach(visibleY, 0.0f, 0.5f);
            }
        }
    }

    public static void CritterDamaged(float fractionalHealthRemaining)
    {
        sHealthDisplay.lastDamageFraction = Math.Clamp((int)(3.0f * (1.0f - fractionalHealthRemaining)), 0, 2);
        sHealthDisplay.damageTime = 5.0f;
    }

    public void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.CritterHealth;

        int index = 0;
        if (enabledFraction > 0.0f)
        {
            index = 1 + 3 * lastDamageFraction + Math.Clamp((int)(3.0f * enabledFraction), 0, 2);
        }
        
        Texture2D tex = DataLoader.sDataLoader.eyesTex[index];
        float w = 5 * tex.width;
        float h = 6 * tex.height;
        GUI.DrawTexture(new Rect((Screen.width - w) / 2, (20 + h) * visibleY - h, w, h), tex); 
    }
}
