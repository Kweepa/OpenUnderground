using System;
using UnityEngine;

public class CritterHealthDisplay : MonoBehaviour
{
    public int lastDamageFraction;
    public float enabledFraction;
    private float damageTime;

    /// <summary>
    /// Whether the last creature the player hit is down to a quarter of its health or less.
    /// </summary>
    /// <remarks>
    /// The original scores a fight by who is losing it, and this is the half about the other side:
    /// on every blow it works out (hp * 64) / (maxHp + 1) for whoever was struck and compares it
    /// against 16, which is a quarter (UW.EXE 0x1c447). It only counts while the eyes are still
    /// showing, so the reading goes stale with them rather than outliving the fight.
    /// The threshold is the one the eyes already use rather than the original's quarter, and
    /// that is deliberate: red eyes are the cue the player actually reads, and music that says
    /// "it is nearly down" a third of a health bar after the creature looks it is music that
    /// arrives late. Same number as lastDamageFraction == 2, which is a third.
    /// </remarks>
    public static bool LastTargetNearlyDead =>
        sHealthDisplay != null && sHealthDisplay.damageTime > 0.0f && sHealthDisplay.lastDamageFraction == 2;
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
