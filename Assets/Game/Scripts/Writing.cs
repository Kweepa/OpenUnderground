using TMPro;
using UnityEngine;

[System.Serializable]
public class WritingClip
{
    public string contains;
    public AudioClip clip;
}

public class Writing : UUObject
{
    public TMP_Text text;

    public WritingClip[] clips;
    public MeshRenderer backgroundRenderer;
    public Material woodMaterial;

    private double goodToPlayClipTime;

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);
        
        text.text = StringLoader.GetString(8, special & 511);
        text.text = text.text.Replace("  ", " ");
        text.text = text.text.Replace(" - ", "\u2014");
        text.text = text.text.Replace("Drakhri", "Drakhai");
        if (text.text.Length < 20)
        {
            text.alignment = TextAlignmentOptions.Center;
        }

        // flags contains the type of writing (plaque, sign, runes, writing)
        // there's at least one that's labelled wrong, on the first floor (a keep out sign flagged as writing) so patch that
        if (LevelLoader.sLevelLoader.loadedLevel == 1 && text.text == "Keep Out")
        {
            flags = 4; // was 8
        }

        // prevent "magical writing"
        isEnchanted = false;
        
        // swap out the background for a wooden one for certain signs
        if (flags == 4 && backgroundRenderer != null && woodMaterial != null)
        {
            backgroundRenderer.material = woodMaterial;
        }

        if (!restoredFromSave)
        {
            Vector3 p = transform.position;
            if (angle == 0 || angle == 4)
            {
                switch (x)
                {
                case 0:
                    p.x += 0.5f;
                    break;
                case 1:
                    p.x += 0.5f - Tile.xzScale / 8.0f;
                    break;
                case 6:
                    p.x -= 0.5f - Tile.xzScale / 8.0f;
                    break;
                case 7:
                    p.x -= 0.5f;
                    break;
                }
            }
            else if (angle == 2 || angle == 6)
            {
                switch (y)
                {
                case 0:
                    p.z += 0.5f;
                    break;
                case 1:
                    p.z += 0.5f - Tile.xzScale / 8.0f;
                    break;
                case 6:
                    p.z -= 0.5f - Tile.xzScale / 8.0f;
                    break;
                case 7:
                    p.z -= 0.5f;
                    break;
                }
            }

            transform.SetPositionAndRotation(p, transform.rotation);
        }
        SnapToWallUsingTileBoundaries();
    }

    public override string GetUnderCursorName()
    {
        return null;
    }

    public override float GetInteractionDistance()
    {
        // depends on light level?
        return 5.0f;
    }

    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        #if false
        float clipLength = 7.0f;
        if (Time.realtimeSinceStartupAsDouble > goodToPlayClipTime)
        {
            foreach (var clip in clips)
            {
                if (text.text.Contains(clip.contains))
                {
                    Utils.PlayClip2d(clip.clip, false);
                    clipLength = clip.clip.length;
                    goodToPlayClipTime = Time.realtimeSinceStartupAsDouble + clipLength;
                    break;
                }
            }
        }
        Messages.Add($"{StringLoader.GetString(8, 368 + flags)}\u201c{text.text}\u201d", clipLength);
        #endif

        TryChainInteraction(EAction.Look);
    }
}
