using UnityEngine;
using UnityEngine.InputSystem;

public class FluteGUI : MonoBehaviour
{
    public GUIStyle noteStyle;
    public GUIStyle tuneStyle;
    public GUIStyle keyLetterStyle;

    public Texture2D upDownTex;
    public Texture2D aButtonTex;
    public Texture2D bButtonTex;
    /// <summary>Keyboard Esc hint icon (same asset as HowMany).</summary>
    public Texture2D escapeKeyTex;
    public Texture2D blankKeyTex;

    public EObjectType instrumentType;

    public AudioClip[] fluteNotes;
    public AudioClip[] mandolinNotes;

    public AudioClip cupAppearsSound;

    private int note;
    private string tune = "";

    public void Start()
    {
        PlayerObject.DisableControls(EControlMask.Flute, true);
    }

    private void DismissFlute()
    {
        Destroy(gameObject);
        PlayerObject.DisableControls(EControlMask.Flute, false);
    }

    private void PlaySelectedNote()
    {
        AudioClip clip = null;
        if (instrumentType == EObjectType.Flute)
        {
            if (fluteNotes != null && fluteNotes.Length > note && fluteNotes[note] != null)
            {
                clip = fluteNotes[note];
            }
        }
        else
        {
            if (mandolinNotes != null && mandolinNotes.Length > note && mandolinNotes[note] != null)
            {
                clip = mandolinNotes[note];
            }
        }

        if (clip != null)
        {
            Utils.PlayClip2d(clip, false);
        }

        tune += (char)('0' + note);
        if (tune.Length > 9)
        {
            tune = tune.Remove(0, 1);
        }

        if (tune == "354237875"
            && LevelLoader.sLevelLoader.loadedLevel == 3
            && (PlayerObject.Player.mainCamera.transform.position - Utils.GetCupPos()).magnitude < 20.0f)
        {
            if (instrumentType == EObjectType.Flute
                && !PlayerData.sData.cupFound)
            {
                Vector3 offset = Utils.GetCupPos() - PlayerObject.Player.mainCamera.transform.position;
                if (!Physics.Raycast(PlayerObject.Player.mainCamera.transform.position, offset.normalized, offset.magnitude, LayerMasks.EnvironmentOnly))
                {
                    UUObject cup = LevelLoader.CreateObjectOfType(EObjectType.ShinyCup);
                    cup.PostLoadInitialize();
                    Inventory.GrantNewItemToMouseOrInventory(cup);
                    Messages.Add(1, 136);

                    Utils.PlayClip2d(cupAppearsSound);

                    PlayerData.sData.cupFound = true;

                    DismissFlute();
                }
            }
            else if (instrumentType == EObjectType.Mandolin
                     && !PlayerData.sData.playedMardinOnMandolin)
            {
                PlayerData.sData.playedMardinOnMandolin = true;
                DismissFlute();
            }
        }
    }

    public void Update()
    {
        if (GameInput.EscapePressedThisFrame()
            || (Gamepad.current?.bButton.wasReleasedThisFrame ?? false))
        {
            DismissFlute();
            return;
        }

        if (Gamepad.current != null)
        {
            note = Mathf.Clamp((int)(5.0f + 5.0f * (Gamepad.current.leftStick.value.y)), 0, 9);
            if (Gamepad.current.aButton.wasPressedThisFrame)
            {
                PlaySelectedNote();
            }
        }
    }

    private static int KeyCodeToDigit(KeyCode k)
    {
        if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9)
        {
            return k - KeyCode.Alpha0;
        }
        if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9)
        {
            return k - KeyCode.Keypad0;
        }
        return -1;
    }

    public void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Flute;

        Texture2D tex = DataLoader.sDataLoader.invTex[6];
        Rect r = new Rect((Screen.width - 3 * tex.width) / 2, (Screen.height - 3.6f * tex.height) / 2, 3 * tex.width, 3.6f * tex.height);
        GuiInput.RegisterBlockingRect(r);
        Event e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            int d = KeyCodeToDigit(e.keyCode);
            if (d >= 0)
            {
                note = d;
                PlaySelectedNote();
                e.Use();
            }
        }

        bool mouseKbHints = GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard;

        GUI.DrawTexture(r, tex);
        if (!mouseKbHints)
        {
            GUI.DrawTexture(new Rect(r.x + 29, r.y + 69, 24, 24), upDownTex);
        }
        tex = DataLoader.sDataLoader.objTex[(int)instrumentType];
        GUI.DrawTexture(new Rect(r.x + 38, r.y + 32, 3 * tex.width, 3.6f * tex.height), tex);

        if (mouseKbHints)
        {
            GUI.DrawTexture(new Rect(r.x + 100, r.y + 30, 24, 24), blankKeyTex);
            GUI.Label(new Rect(r.x + 100, r.y + 30, 24, 24), "0", keyLetterStyle);
            GUI.Label(new Rect(r.x + 125, r.y + 30, 20, 29), "-", tuneStyle);
            GUI.DrawTexture(new Rect(r.x + 130, r.y + 30, 24, 24), blankKeyTex);
            GUI.Label(new Rect(r.x + 130, r.y + 30, 24, 24), "9", keyLetterStyle);
            GUI.Label(new Rect(r.x + 160, r.y + 30, 120, 29), "Play", tuneStyle);

            Rect escHintRect = new Rect(r.x + 130, r.y + 60, 140, 29);
            if (escapeKeyTex != null)
            {
                GUI.DrawTexture(new Rect(r.x + 130, r.y + 60, 24, 24), escapeKeyTex);
            }
            GUI.Label(new Rect(r.x + 160, r.y + 60, 120, 29), "Stop", tuneStyle);

            if (GuiInput.TryConsumeClickInRect(escHintRect))
            {
                DismissFlute();
                return;
            }
        }
        else
        {
            GUI.Label(new Rect(r.x + 77, r.y + 22, 32, 32), $"{note}", noteStyle);

            if (aButtonTex != null)
            {
                GUI.DrawTexture(new Rect(r.x + 130, r.y + 30, 24, 24), aButtonTex);
            }
            GUI.Label(new Rect(r.x + 160, r.y + 30, 100, 20), "Play", tuneStyle);
            if (bButtonTex != null)
            {
                GUI.DrawTexture(new Rect(r.x + 130, r.y + 60, 24, 24), bButtonTex);
            }
            GUI.Label(new Rect(r.x + 160, r.y + 60, 100, 20), "Stop", tuneStyle);
        }

        GUI.Label(new Rect(r.x + 80, r.y + 100, 100, 20), tune, tuneStyle);
        //GUI.Label(Screen.safeArea, "354237875");
        GuiInput.TryConsumeClickInPanel(r);
    }
}
