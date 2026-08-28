using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(1000)]
public class Interaction : MonoBehaviour
{
    public Font font;

    public UUObject centeredObject;
    private float centeredObjectDist;

    private float mouseLeftHoldTime;
    private bool mouseLeftUseFired;

    /// <summary>Left mouse went down on a portable object in range (world pickup drag-to-inventory intent).</summary>
    private bool leftMouseDownOnPortableNear;
    private Vector2 leftMouseDownScreenPos;
    /// <summary>Mouse moved enough while left was held — counts as "left drag" for pickup drag.</summary>
    private bool leftDragDetectedForPickup;
    private const float kLeftDragPickupThresholdSq = 8f * 8f;

    /// <summary>Portable in range at LMB press; mouse-hold Use targets this so pickup matches crosshair at gesture start.</summary>
    private UUObject worldPickupSnapshot;

    /// <summary>True while <see cref="DoUse"/> runs from left-hold; portable pickup uses hand/UI path (not <see cref="Inventory.Add"/>).</summary>
    private bool mouseLeftHoldPortablePickupUse;

    public bool MouseLeftHoldPortablePickupUse => mouseLeftHoldPortablePickupUse;

    private readonly Collider[] cachedColliders = new Collider[8];

    /// <summary>Set in <see cref="OnGUI"/> when the cursor is free and the pointer is over modal UI.</summary>
    private bool suppressWorldHoverUi;

    private bool cachedHasMaterialHit;
    private RaycastHit cachedObjectHit;

    public static Interaction sInt;

    /// <summary>
    /// <see cref="PlayerObject.controlsDisabled"/> can include <see cref="EControlMask.Inventory"/> / <see cref="EControlMask.Magic"/>
    /// while panels are open; <see cref="PlayerObject.controlsActive"/> is then false. World mouse look/use must still run in those modes.
    /// Block only when a non-UI (or blocking) mask is set — align with <see cref="GameplayCursorPolicy"/> UI bits.
    /// </summary>
    private static bool ShouldReturnEarlyForControls(EControlMask disabled)
    {
        if (disabled == 0)
        {
            return false;
        }

        const EControlMask ignoredForWorldInteraction =
            EControlMask.Magic
            | EControlMask.Inventory
            | EControlMask.RoamingSight
            | EControlMask.Conversation
            | EControlMask.HowMany
            | EControlMask.RepairDialog
            | EControlMask.Map
            | EControlMask.SaveLoad;

        return (disabled & ~ignoredForWorldInteraction) != 0;
    }

    protected void Start()
    {
        sInt = this;
    }

    void Update()
    {
        RaycastHit objectHit = default;
        bool hasMaterialHit = false;
        centeredObject = null;

        Camera mainCam = PlayerObject.Player.mainCamera;
        Vector3 camPos = mainCam.transform.position;
        Vector3 camFwd = mainCam.transform.forward;

        const float maxDist = 10.0f;
        float radius = 0.1f;

        bool pointerFreeForWorldPick = Mouse.current != null
            && GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard
            && !GuiInput.LastFrameBlocksPointer;

        bool useMouseRay = Cursor.lockState != CursorLockMode.Locked && pointerFreeForWorldPick;
        Vector3 rayOrigin = camPos;
        Vector3 rayDir = camFwd;
        if (useMouseRay)
        {
            Ray screenRay = mainCam.ScreenPointToRay(Mouse.current.position.ReadValue());
            rayOrigin = screenRay.origin;
            rayDir = screenRay.direction;
        }

        // Same single forward ray as pre–mouse-ray work (project layers; stash used a shorter list + different spelling).
        int fatRayMask =
            (1 << LayerMask.NameToLayer("Objects")) |
            (1 << LayerMask.NameToLayer("NonBlockingObject")) |
            (1 << LayerMask.NameToLayer("PhysicsDebris")) |
            (1 << LayerMask.NameToLayer("Characters"));

        RaycastHit[] envRayHits = Physics.RaycastAll(rayOrigin, rayDir, maxDist, LayerMasks.EnvironmentAndCeiling);
        RaycastHit[] fatHits = Physics.SphereCastAll(rayOrigin, radius, rayDir, maxDist, fatRayMask);

        float bestNonUuDist = float.MaxValue;
        for (int i = 0; i < envRayHits.Length; i++)
        {
            RaycastHit hit = envRayHits[i];
            if (hit.collider.transform.root.GetComponent<UUObject>() != null)
                continue;
            if (hit.distance < bestNonUuDist)
            {
                bestNonUuDist = hit.distance;
                objectHit = hit;
                hasMaterialHit = true;
            }
        }

        UUObject bestNonIncidental = null;
        float bestNonIncidentalDist = bestNonUuDist;
        UUObject bestIncidental = null;
        float bestIncidentalDist = bestNonUuDist;

        void ConsiderHit(RaycastHit hit)
        {
            UUObject obj = hit.collider.transform.root.GetComponent<UUObject>();
            if (obj == null)
                return;
            float dist = hit.distance;
            if (obj.isIncidental)
            {
                if (dist < bestIncidentalDist)
                {
                    bestIncidental = obj;
                    bestIncidentalDist = dist;
                }
            }
            else if (dist < bestNonIncidentalDist)
            {
                bestNonIncidental = obj;
                bestNonIncidentalDist = dist;
            }
        }

        for (int i = 0; i < envRayHits.Length; i++)
            ConsiderHit(envRayHits[i]);
        for (int i = 0; i < fatHits.Length; i++)
            ConsiderHit(fatHits[i]);

        if (bestNonIncidental != null)
        {
            centeredObject = bestNonIncidental;
            centeredObjectDist = bestNonIncidentalDist;
        }
        else if (bestIncidental != null)
        {
            centeredObject = bestIncidental;
            centeredObjectDist = bestIncidentalDist;
        }

        if (GuiInput.LastFrameBlocksPointer)
        {
            centeredObject = null;
            centeredObjectDist = 0f;
            hasMaterialHit = false;
            objectHit = default;
        }

        cachedHasMaterialHit = hasMaterialHit;
        cachedObjectHit = objectHit;
    }

    private void LateUpdate()
    {
        if (PlayerObject.Player == null)
        {
            return;
        }

        if (ShouldReturnEarlyForControls(PlayerObject.Player.controlsDisabled))
        {
            return;
        }

        // Inventory/magic open: suppress gamepad world look/use (X/Y); mouse/kb may world-interact when nothing is on the cursor.
        if (PlayerPanelState.IsEffectivelyExploringInventory)
        {
            if (Inventory.sInv != null && Inventory.sInv.mouseCursorCarriedPortable != null)
            {
                return;
            }

            if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
            {
                UpdateMouseKeyboardInteraction(cachedHasMaterialHit, cachedObjectHit);
            }

            return;
        }

        if (PlayerPanelState.IsEffectivelyExploringMagic)
        {
            if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
            {
                UpdateMouseKeyboardInteraction(cachedHasMaterialHit, cachedObjectHit);
            }

            return;
        }

        UpdateGamepadInteraction(cachedHasMaterialHit, cachedObjectHit);
        UpdateMouseKeyboardInteraction(cachedHasMaterialHit, cachedObjectHit);
    }

    private void UpdateGamepadInteraction(bool hasMaterialHit, RaycastHit objectHit)
    {
        Gamepad gp = GameInput.CurrentGamepad;
        if (gp == null)
            return;

        bool tryingLook = gp.xButton.wasPressedThisFrame;
        if (tryingLook)
        {
            DoLook(hasMaterialHit, objectHit);
        }

        bool tryingUse = gp.yButton.wasPressedThisFrame;
        if (tryingUse)
        {
            DoUse();
        }

        if (gp.bButton.wasPressedThisFrame)
        {
            Inventory.sInv.usingItem = null;
            Inventory.sInv.mouseCursorCarriedPortable = null;
        }
    }

    private void UpdateMouseKeyboardInteraction(bool hasMaterialHit, RaycastHit objectHit)
    {
        if (Magic.sMagic != null && Magic.sMagic.SuppressWorldMousePrimaryUntilPrimaryReleased)
        {
            return;
        }

        if (GuiInput.ShouldSuppressWorldMousePrimary())
        {
            return;
        }

        if (suppressWorldHoverUi)
        {
            return;
        }

        // Cursor-held inventory portable: Inventory uses LMB for aim-throw (panel open or closed); skip world look/use on mouse.
        if (Inventory.sInv != null
            && Inventory.sInv.mouseCursorCarriedPortable != null
            && GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
            return;

        bool mouseLookFromLeftRelease = false;
        Mouse mouse = GameInput.CurrentMouse;
        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                mouseLeftHoldTime = 0f;
                mouseLeftUseFired = false;
                leftMouseDownOnPortableNear = centeredObject != null
                    && HasSomethingToInteractWith()
                    && centeredObject.isPortable;
                if (leftMouseDownOnPortableNear)
                {
                    worldPickupSnapshot = centeredObject;
                }
                else
                {
                    worldPickupSnapshot = null;
                }
                leftMouseDownScreenPos = mouse.position.ReadValue();
                leftDragDetectedForPickup = false;
            }
            if (mouse.leftButton.isPressed)
            {
                if (leftMouseDownOnPortableNear
                    && (mouse.position.ReadValue() - leftMouseDownScreenPos).sqrMagnitude > kLeftDragPickupThresholdSq)
                {
                    leftDragDetectedForPickup = true;
                }

                mouseLeftHoldTime += Time.deltaTime;
                // Drag on portable: pick up immediately. No drag: same 0.3s hold as before (doors etc.).
                if (!mouseLeftUseFired && leftDragDetectedForPickup)
                {
                    mouseLeftUseFired = true;
                    DoUse(fromMouseLeftHold: true);
                }
                else if (!mouseLeftUseFired && !leftDragDetectedForPickup && mouseLeftHoldTime >= 0.3f)
                {
                    mouseLeftUseFired = true;
                    DoUse(fromMouseLeftHold: true);
                }
            }
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (!mouseLeftUseFired && mouseLeftHoldTime < 0.3f
                    && (Inventory.sInv == null || !Inventory.sInv.ConsumeSuppressMouseLookOnNextLeftRelease()))
                {
                    mouseLookFromLeftRelease = true;
                }
                mouseLeftHoldTime = 0f;
                mouseLeftUseFired = false;
                // Don't clear pickup drag intent while How many? (Pickup) is open — snapshot is taken in AskHowMany.
                bool skipClearPickupIntent =
                    HowMany.sHowMany != null
                    && HowMany.IsActive()
                    && HowMany.sHowMany.howManyReason == EHowManyReason.Pickup
                    && HowMany.sHowMany.howManyItem != null;
                if (!skipClearPickupIntent)
                {
                    leftMouseDownOnPortableNear = false;
                    leftDragDetectedForPickup = false;
                    worldPickupSnapshot = null;
                }
            }
        }

        if (mouseLookFromLeftRelease)
        {
            DoLook(hasMaterialHit, objectHit);
        }
    }

    private void DoLook(bool hasMaterialHit, RaycastHit objectHit)
    {
        if (centeredObject != null)
        {
            string lookText = StringLoader.GetString(1, 260) + centeredObject.GetLookName();
            if (DataLoader.sDataLoader.comObjProps[(int)centeredObject.type].canBeOwned)
            {
                int ownerRace = centeredObject.ownerIndex & 31;
                if (ownerRace > 0 && ownerRace < 29)
                {
                    string raceString = StringLoader.GetString(1, 370 + ownerRace);
                    if (!string.IsNullOrEmpty(raceString))
                    {
                        lookText += " belonging to" + raceString;
                    }
                }
            }

            lookText += ".";
            Messages.Add(lookText);
        }
        else if (hasMaterialHit)
        {
            // convert hit to material index
            LevelLoader.sLevelLoader.TryInteract(objectHit);
        }
    }

    private void DoUse(bool fromMouseLeftHold = false)
    {
        UUObject target = centeredObject;
        if (fromMouseLeftHold && worldPickupSnapshot != null)
        {
            target = worldPickupSnapshot;
        }

        if (target != null && HasSomethingToInteractWith(target))
        {
            if (fromMouseLeftHold)
                mouseLeftHoldPortablePickupUse = true;
            try
            {
                target.TryInteract(null, null, EAction.Use);
                TutorialManager.NotifyInteractableUsed(target);
            }
            finally
            {
                mouseLeftHoldPortablePickupUse = false;
            }
        }
    }

    /// <summary>Left mouse held with portable under cursor or drag detected — world pickup intent for How many? snapshot.</summary>
    public bool HasWorldPickupIntentForPickup()
    {
        Mouse m = GameInput.CurrentMouse;
        if (m == null || !m.leftButton.isPressed)
            return false;
        return leftMouseDownOnPortableNear || leftDragDetectedForPickup;
    }

    /// <summary>
    /// When LMB is held and the centered target is in use range, mouse movement is treated as interact drag (pickup / hold-to-use), not camera look.
    /// </summary>
    public bool ShouldSuppressMouseLookWhileLeftHeld()
    {
        Mouse m = GameInput.CurrentMouse;
        if (m == null || !m.leftButton.isPressed)
        {
            return false;
        }

        if (Inventory.sInv != null && Inventory.sInv.mouseCursorCarriedPortable != null)
        {
            return false;
        }

        return HasSomethingToInteractWith();
    }


    /// <summary>
    /// Single-qty world pickup from left hold / drag: consume intent and return true to hand-only (no <see cref="Inventory.Add"/>).
    /// </summary>
    public bool TryConsumeWorldPickupIntentForImmediatePickup()
    {
        Mouse m = GameInput.CurrentMouse;
        if (m == null || !m.leftButton.isPressed)
            return false;
        if (!leftMouseDownOnPortableNear && !leftDragDetectedForPickup)
            return false;
        leftMouseDownOnPortableNear = false;
        leftDragDetectedForPickup = false;
        worldPickupSnapshot = null;
        return true;
    }

    public void ClearWorldPickupIntentFlags()
    {
        leftMouseDownOnPortableNear = false;
        leftDragDetectedForPickup = false;
        worldPickupSnapshot = null;
    }

    /// <param name="subject">Per-object reach; when null, uses <see cref="centeredObject"/>.</param>
    public float GetInteractionDistance(UUObject subject = null)
    {
        if (Magic.sMagic.IsSpellActive(Magic.ESpell.Telekinesis))
        {
            return 9.0f;
        }

        float thresholdDist = 3.0f;

        UUObject obj = subject != null ? subject : centeredObject;
        if (obj != null)
        {
            thresholdDist = obj.GetInteractionDistance();
        }

        if (obj is SwitchBase && Inventory.sInv != null)
        {
            if (Inventory.sInv.mouseCursorCarriedPortable is Pole pole)
            {
                thresholdDist = pole.thresholdDist;
            }
        }

        return thresholdDist;
    }

    private float GetWorldInteractionDistance(UUObject obj)
    {
        Vector3 from = PlayerObject.Player.mainCamera.transform.position;
        if (obj.cachedRenderer != null)
        {
            return Vector3.Distance(from, obj.cachedRenderer.bounds.ClosestPoint(from));
        }

        return Vector3.Distance(from, obj.transform.position);
    }

    private bool HasSomethingToInteractWith()
    {
        return HasSomethingToInteractWith(centeredObject);
    }

    private bool HasSomethingToInteractWith(UUObject obj)
    {
        return obj != null && GetWorldInteractionDistance(obj) < GetInteractionDistance(obj);
    }

    private Texture2D greyscaleCursorTex = null;
    private Texture2D nameLabelBgTex = null;

    private Texture2D GetGreyscaleCursor(Texture2D originalTex)
    {
        if (greyscaleCursorTex == null && originalTex != null)
        {
            // Create a copy of the texture
            greyscaleCursorTex = new Texture2D(originalTex.width, originalTex.height, TextureFormat.RGBA32, false);
            Color[] originalPixels = originalTex.GetPixels();
            Color[] greyscalePixels = new Color[originalPixels.Length];
            
            const float brightnessMultiplier = 1.3f; // Increase brightness
            
            for (int i = 0; i < originalPixels.Length; i++)
            {
                Color original = originalPixels[i];
                // Apply transparentization logic (same as tryTransparentizeCursor)
                if (original.r == 0 && original.b > 0)
                {
                    greyscalePixels[i] = new Color(0, 0, 0, 0); // Transparent
                }
                else
                {
                    // Convert to greyscale using luminance formula
                    float grey = 0.299f * original.r + 0.587f * original.g + 0.114f * original.b;
                    // Apply brightness multiplier and preserve alpha
                    greyscalePixels[i] = new Color(grey * brightnessMultiplier, grey * brightnessMultiplier, grey * brightnessMultiplier, original.a);
                }
            }
            
            greyscaleCursorTex.SetPixels(greyscalePixels);
            greyscaleCursorTex.filterMode = FilterMode.Point;
            greyscaleCursorTex.wrapMode = TextureWrapMode.Clamp;
            greyscaleCursorTex.Apply();
        }
        
        return greyscaleCursorTex != null ? greyscaleCursorTex : originalTex;
    }

    private void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Crosshair;

        bool freeCursor = Cursor.lockState != CursorLockMode.Locked;
        suppressWorldHoverUi = freeCursor && GuiInput.BlocksPointer(GuiInput.MousePositionGuiSpace);

        // so allow cursor when casting, but hide during cutscene
        if ((PlayerObject.Player.controlsDisabled & (EControlMask.Conversation | EControlMask.EnterMoongate | EControlMask.RoamingSight | EControlMask.Cutscene)) == 0)
        {
            Texture2D cursorTex = GetGreyscaleCursor(DataLoader.sDataLoader.cursorTex[0]);
            if (cursorTex != null)
            {
                // Get weapon charge (0.0 to 1.0)
                float power = WeaponChargeGem.sChargeGem != null ? WeaponChargeGem.sChargeGem.power : 0.0f;
                
                // Calculate cursor color based on charge
                Color cursorColor;
                if (power >= 1.0f)
                {
                    // Pulse white twice per second (every 0.5s)
                    float pulseTime = Time.time * 2.0f; // 2 Hz
                    float pulse = (Mathf.Sin(pulseTime * Mathf.PI) + 1.0f) * 0.5f; // 0 to 1
                    cursorColor = Color.Lerp(Color.white, Color.green, pulse);
                }
                else if (power > 0.0f)
                {
                    float t = power;
                    Color orange = new Color(1.0f, 0.5f, 0.0f);
                    float greenThreshold = 0.9f;
                    if (t < greenThreshold)
                    {
                        cursorColor = Color.Lerp(Color.red, orange, t / greenThreshold);
                    }
                    else
                    {
                        cursorColor = Color.Lerp(orange, Color.green, (t - greenThreshold) / (1.0f - greenThreshold));
                    }
                }
                else
                {
                    cursorColor = Color.gray;
                }
                
                float w = 5 * cursorTex.width;
                float h = 6 * cursorTex.height;
                bool drawCenterCrosshair = Cursor.lockState == CursorLockMode.Locked;

                if (drawCenterCrosshair)
                {
                    // Apply color tint
                    Color originalColor = GUI.color;
                    GUI.color = cursorColor;
                    GUI.DrawTexture(new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h), cursorTex);
                    GUI.color = originalColor;
                }

                // Display object name at bottom-right of center reticle (locked) or near software cursor (free); hidden over slide-outs (see suppressWorldHoverUi).
                if (centeredObject != null && !suppressWorldHoverUi)
                {
                    float textX;
                    float textY;
                    if (drawCenterCrosshair)
                    {
                        float cursorX = (Screen.width - w) / 2;
                        float cursorY = (Screen.height - h) / 2;
                        textX = cursorX + w;
                        textY = cursorY + h;
                    }
                    else
                    {
                        Vector2 guiMouse = GuiInput.MousePositionGuiSpace;
                        textX = guiMouse.x + 8f;
                        textY = guiMouse.y + 8f;
                    }
                    
                    // Check if object is interactable (accounts for telekinesis distance)
                    bool isInteractable = HasSomethingToInteractWith();
                    
                    // Create style for object name label
                    GUIStyle nameStyle = new GUIStyle();
                    nameStyle.font = font;
                    nameStyle.fontSize = 16;

                    // Owned portables in range show orange; other interactables yellow; out of range white.
                    bool isOwnedPortable = false;
                    if (centeredObject.isPortable && DataLoader.sDataLoader != null)
                    {
                        var props = DataLoader.sDataLoader.comObjProps[(int)centeredObject.type];
                        if (props.canBeOwned)
                        {
                            int ownerRace = centeredObject.ownerIndex & 31;
                            if (ownerRace > 0 && ownerRace < 29)
                            {
                                isOwnedPortable = true;
                            }
                        }
                    }

                    if (isInteractable && isOwnedPortable)
                    {
                        nameStyle.normal.textColor = new Color(1.0f, 0.6f, 0.1f);
                    }
                    else
                    {
                        nameStyle.normal.textColor = isInteractable ? Color.yellow : Color.white;
                    }
                    nameStyle.alignment = TextAnchor.LowerLeft;
                    
                    // Create black background for readability (cached)
                    if (nameLabelBgTex == null)
                    {
                        nameLabelBgTex = new Texture2D(1, 1);
                        nameLabelBgTex.SetPixel(0, 0, new Color(0, 0, 0, 0.7f));
                        nameLabelBgTex.Apply();
                    }
                    nameStyle.normal.background = nameLabelBgTex;
                    nameStyle.padding = new RectOffset(4, 4, 2, 2);
                    
                    // Use GetUnderCursorName for the object name (hides door frames)
                    string displayText = centeredObject.GetUnderCursorName();
                    
                    // Only display if there's text to show
                    if (!string.IsNullOrEmpty(displayText))
                    {
                        displayText = displayText.TrimEnd();
                        // Calculate text size for proper background
                        GUIContent content = new GUIContent(displayText);
                        Vector2 textSize = nameStyle.CalcSize(content);
                        Rect textRect = new Rect(textX, textY, textSize.x, textSize.y);
                        
                        GUI.Label(textRect, displayText, nameStyle);
                    }
                }
            }

            if (centeredObject != null && Cheats.sCheats.labelCenteredObject)
            {
                Vector3 bestPos = centeredObject.cachedRenderer != null ? centeredObject.cachedRenderer.bounds.center : centeredObject.transform.position;
                DebugGUI.DrawTextOnGUI(bestPos + 0.6f * Vector3.down, string.Format("{0} {1}", centeredObject.name, centeredObject.type), font);

                if (HasSomethingToInteractWith())
                {
                    Vector3 screen = PlayerObject.Player.mainCamera.WorldToScreenPoint(bestPos + 0.2f * Vector3.up);
                    if (screen.z > 0.0f)
                    {
                    }
                }
            }
        }
    }
}
