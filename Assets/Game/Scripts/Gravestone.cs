using TMPro;
using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using UnityEngine.InputSystem;

[System.Serializable]
public class GravestoneClip
{
    public string contains;
    public AudioClip clip;
}

public enum EGravestoneMaterial
{
    Clean,
    Mossy,
    MossyWithFlourish
}

public class Gravestone : UUObject
{
    public GravestoneClip[] clips;

    public string[] names;

    public GameObject lionirGems;
    public GameObject avirillGems;

    public MeshRenderer stone;

    [EnumNamedArray(typeof(EGravestoneMaterial))]
    public Material[] materials;

    public string stoneText;
    public CutscenePlayer graveCutscene;
    /// <summary>Shown during grave camera skip prompt (gamepad).</summary>
    public Texture2D aButton;
    public Vector3 cameraOffset;
    public float cameraTime = 60;
    public float cameraCenterHeight = 0.5f;
    public int graveId;

    public AudioClip buryClip;

    private double goodToPlayClipTime;

    private bool showPressA;

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        // Call base to load names and other UUObject initialization
        base.PostLoadInitialize(restoredFromSave);
        
        TMP_Text text = gameObject.GetComponentInChildren<TMP_Text>();

        if (special >= 512)
        {
            DataLoader.sDataLoader.graveData.Seek(special - 512);
            graveId = DataLoader.sDataLoader.graveData.GetByte();
        }

        stoneText = StringLoader.GetString(8, special & 511);

        if (stoneText.Contains("empty grave"))
        {
            stoneText = "Garamon";
        }
        else if (string.IsNullOrEmpty(stoneText) && graveId > 0 && graveId <= names.Length)
        {
            // get from grave id
            stoneText = names[graveId - 1];
            if (text != null)
            {
                // all on one line, so that Lionir and Avirill are all aligned
                text.textWrappingMode = TextWrappingModes.NoWrap;
            }
            if (stoneText == "Sir Lionir")
            {
                lionirGems.SetActive(true);
            }
            else if (stoneText == "Sir Avirill")
            {
                avirillGems.SetActive(true);
            }
    }
        
        if (text != null)
        {
            text.text = stoneText;
        }

        if (!restoredFromSave)
        {
            Vector3 mid = transform.position + Vector3.up;
            RaycastHit hitForward;
            RaycastHit hitBack;
            float maxDist = 15.0f;
            int layerMask = LayerMasks.EnvironmentAndCeiling;
            bool didHitForward = Physics.Raycast(mid, transform.forward, out hitForward, maxDist, layerMask);
            bool didHitBack = Physics.Raycast(mid, -transform.forward, out hitBack, maxDist, layerMask);
            if (didHitForward && (!didHitBack || hitForward.distance < hitBack.distance))
            {
                // rotate 180, so it points out of the room it's in
                transform.rotation = Quaternion.AngleAxis(180.0f, Vector3.up) * transform.rotation;
            }
        }

        if (LevelLoader.sLevelLoader.loadedLevel == 4)
        {
            stone.material = materials[(int)EGravestoneMaterial.Clean];
        }
        else
        {
            stone.material = materials[(int)(Random.value < 0.5f ? EGravestoneMaterial.Mossy : EGravestoneMaterial.MossyWithFlourish)];
        }
    }
    
    
    private IEnumerator StartGaramonWandering(Critter garamon)
    {
        yield return new WaitForSeconds(1.0f);
        garamon.goal = Critter.EGoal.Wander2;
    }

    private IEnumerator GraveCameraLook(float time)
    {
        PlayerObject.DisableControls(EControlMask.Cutscene, true);

        Vector3 stoneCenter = transform.position + cameraCenterHeight * Vector3.up;
        Vector3 cameraPos = transform.TransformPoint(cameraOffset);

        GameObject cameraObj = new GameObject("GraveCamera");
        cameraObj.transform.SetPositionAndRotation(cameraPos, Quaternion.LookRotation(stoneCenter - cameraPos));

        cameraObj.AddComponent<Camera>();

        Camera old = PlayerObject.Player.mainCamera;
        old.tag = "Untagged";
        cameraObj.tag = "MainCamera";

        yield return new WaitForSeconds(1.0f);

        showPressA = true;

        float elapsed = 1.0f;
        bool skipPending = false;
        while (elapsed < time)
        {
            elapsed += Time.deltaTime;
            if ((GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false)
                || (GameInput.CurrentMouse?.leftButton.wasPressedThisFrame ?? false)
                || GameInput.EscapePressedThisFrame())
            {
                skipPending = true;
                showPressA = false;
            }
            else if (skipPending && !(GameInput.CurrentGamepad?.aButton.isPressed ?? false)
                     && !(GameInput.CurrentMouse?.leftButton.isPressed ?? false))
            {
                elapsed = time;
            }
            yield return null;
        }

        showPressA = false;

        cameraObj.tag = "Untagged";
        old.tag = "MainCamera";
        Destroy(cameraObj);
        PlayerObject.DisableControls(EControlMask.Cutscene, false);
    }

    private void OnGUI()
    {
        if (!showPressA || aButton == null)
        {
            return;
        }
        if (GameInput.LastActiveDevice != GameInputDevice.Gamepad)
        {
            return;
        }

        GUI.depth = (int)EGUIDepth.Cutscene;
        float aw = 48;
        float ah = 48;
        float yBorder = 0.05f * Screen.height;
        GUI.DrawTexture(new Rect(Screen.width / 2 - aw / 2, Screen.height - yBorder - ah, aw, ah), aButton);
    }

    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        if (action == EAction.Use && originator != null)
        {
            Bones bones = originator as Bones;
            if (bones != null)
            {
                if (bones.quality == 63) // quality 63 identifies Garamon's bones (they can't be destroyed)
                {
                    if (LevelLoader.sLevelLoader.loadedLevel == 5 && !isLinked && special == 545) // Garamon's grave
                    {
                        Messages.Add(1, 134);
                        Critter garamon = LevelLoader.GetObj(196) as Critter;
                        if (garamon != null)
                        {
                            Conversations.StartConversation(garamon);
                    
                            // enable garamon and position here
                            LevelLoader.AddToWorld(garamon);
                            garamon.PostLoadInitialize();
                            garamon.transform.root.transform.position = transform.position + 3.0f * Vector3.up;
                            PlayerObject.Player.StartCoroutine(StartGaramonWandering(garamon));
                            PlayerData.sData.garamonAtRest = true;

                            Utils.PlayClip(buryClip, transform.position);
                            
                            Utils.DestroyItem(bones);
                        }
                    }
                    else
                    {
                        Messages.Add(1, 259); // not at rest
                    }
                }
                else
                {
                    Messages.Add(1, 134); // thoughtfully

                    Utils.PlayClip(buryClip, transform.position);
                            
                    Utils.DestroyItem(bones);
                }
            }
        }
        else
        {
            if (action == EAction.Use)
            {
                float timeToShow = cameraTime;

                if (stoneText != "")
                {
                    timeToShow = 7.0f;

                    #if false
                    if (Time.realtimeSinceStartupAsDouble > goodToPlayClipTime)
                    {
                        foreach (var clip in clips)
                        {
                            if (stoneText.Contains(clip.contains))
                            {
                                Utils.PlayClip2d(clip.clip, false);
                                timeToShow = clip.clip.length;
                                goodToPlayClipTime = Time.realtimeSinceStartupAsDouble + timeToShow;
                                break;
                            }
                        }
                    }
                    #endif
                }
                else if (graveId == 0)
                {
                    Messages.Add("The inscription is illegible.");
                }

#if true
                StartCoroutine(GraveCameraLook(timeToShow));
#else
                if (graveCutscene != null)
                {
                    CutscenePlayer player = Instantiate(graveCutscene);
                    player.fixedFrame = graveId - 1;
                }
#endif
            }
        }
    }
}
