using UnityEngine;

public class FlickerLight : MonoBehaviour
{
    private Light cachedLight;
    private float timeToNextFlicker;

    void Start()
    {
        cachedLight = GetComponent<Light>();
        
        #if false
        Stream stream = new Stream("../Data/Shades.dat");
        byte[] bytes = stream.GetByteArray(stream.GetLength());
        string x = "";
        for (int i = 0; i < bytes.Length; ++i)
        {
            x += string.Format("{0,2:X}", bytes[i]).Replace(' ', '0') + " ";
        }
        Debug.Log(x);
        #endif
    }

    /// <summary>
    /// The light the player is currently carrying: the two shoulder slots, the ambient light of
    /// level 9, and the Daylight, Night Vision and Light spells, resolved together.
    /// </summary>
    /// <remarks>
    /// Pulled out of Update so that anything else needing to know how brightly lit the player is -
    /// Critter, for how far it can see them - reads the same numbers, rather than a copy that can
    /// drift away from this one.
    /// </remarks>
    public static void GetPlayerLightProps(out float lightRange, out float lightIntensity, out float lightFlicker)
    {
        UUObject objA = Inventory.sInv.invSlotContents[(int) EInvSlot.LeftShoulder];
        UUObject objB = Inventory.sInv.invSlotContents[(int) EInvSlot.RightShoulder];
        lightRange = 12.0f;
        lightIntensity = 1.2f;
        lightFlicker = 1.0f;

        if (LevelLoader.sLevelLoader.loadedLevel == 9)
        {
            lightRange = 32.0f;
            lightIntensity = 3.0f;
            lightFlicker = 0.96f;
        }
        
        if (objA != null) objA.GetLightProps(ref lightRange, ref lightIntensity, ref lightFlicker);
        if (objB != null) objB.GetLightProps(ref lightRange, ref lightIntensity, ref lightFlicker);

        if (Magic.sMagic != null)
        {
            if (Magic.sMagic.IsSpellActive(Magic.ESpell.Daylight) || Magic.sMagic.IsSpellActive(Magic.ESpell.NightVision))
            {
                lightRange = Mathf.Max(lightRange, 30.0f);
                lightIntensity = Mathf.Max(lightIntensity, 4.0f);
                lightFlicker = 0.97f;
            }
            else if (Magic.sMagic.IsSpellActive(Magic.ESpell.Light))
            {
                // should look like a torch
                lightRange = Mathf.Max(lightRange, 20.0f);
                lightIntensity = Mathf.Max(lightIntensity, 2.5f);
                lightFlicker = 0.96f;
            }
        }
    }

    void Update()
    {
        GetPlayerLightProps(out float lightRange, out float lightIntensity, out float lightFlicker);

        if (cachedLight != null)
        {
            cachedLight.range = lightRange;
            timeToNextFlicker -= Time.deltaTime;
            if (timeToNextFlicker < 0.0f)
            {
                // * 0.5 because there are now two lights 
                cachedLight.intensity = 0.5f * Random.Range(lightFlicker, 1.0f) * lightIntensity;
                timeToNextFlicker = Random.Range(0.02f, 0.1f);
            }
        }
    }
}
