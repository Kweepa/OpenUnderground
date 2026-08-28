using UnityEngine;

public class WeaponChargeGem : MonoBehaviour
{
    public float power;
    public static WeaponChargeGem sChargeGem;

    public void Start()
    {
#if false
        powerTex = GraphicsLoader.GetTextures("../Data/power.gr", 0.0f, TextureWrapMode.Clamp);
        ScrubUpTextures();
#endif
        sChargeGem = this;
    }
    
#if false
    public Texture2D[] powerTex;
    private float sparkleTime;
    private int sparkleIndex;

    private void ScrubUpTextures()
    {
        foreach (Texture2D tex in powerTex)
        {
            Color[] cols = tex.GetPixels();
            for (int i = 0; i < cols.Length; ++i)
            {
                if ((cols[i].r < 0.2f && cols[i].g < 0.2f && cols[i].b < 0.2f)
                    || (cols[i].r == cols[i].g && cols[i].g == cols[i].b && cols[i].r < 0.6f))
                {
                    cols[i].a = 0.0f;
                }
            }
            tex.SetPixels(cols);
            tex.Apply();
        }
    }

    public void Update()
    {
        if (power == 1.0f)
        {
            sparkleTime += Time.deltaTime;
            if (sparkleTime > 0.2f)
            {
                sparkleTime -= 0.2f;
                sparkleIndex = ++sparkleIndex % 5;
            }
        }
        else
        {
            sparkleIndex = 0;
            sparkleTime = 0.0f;
        }
    }
    
    public void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.WeaponChargeGem;

        Texture2D tex = powerTex[power < 1.0f ? (int)(power * (powerTex.Length - 5)) : powerTex.Length - 5 + sparkleIndex];
        float width = 3 * tex.width;
        float height = 3 * tex.height;
        GUI.DrawTexture(new Rect(Screen.width - 140 - width / 2, Screen.height - height, width, height), tex);
    }
#endif
}
