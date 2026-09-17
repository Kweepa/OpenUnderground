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

    /// <summary>
    /// Measured fundamental of each of the ten flute samples, in Hz.
    /// </summary>
    /// <remarks>
    /// The samples are a clean diatomic scale, C4 to E5, and correctly ordered - that part was
    /// never the problem. What varies is the timbre. Analysing the harmonic content of the ten
    /// recordings, the share of energy in the fundamental against the second harmonic:
    ///     note 1  C4   H1 0.30  H2 0.15
    ///     note 2  D4   H1 0.16  H2 0.32   <- overblown
    ///     note 3  E4   H1 0.49  H2 0.05
    ///     note 4  F4   H1 0.49  H2 0.06
    ///     note 5  G4   H1 0.36  H2 0.40   <- overblown
    ///     note 6  A4   H1 0.22  H2 0.53   <- overblown
    ///     note 7  B4   H1 0.18  H2 0.58   <- overblown
    ///     note 8  C5   H1 0.74  H2 0.07
    ///     note 9  D5   H1 0.35  H2 0.45   <- overblown
    ///     note 10 E5   H1 0.41  H2 0.20
    /// Four of the ten have a second harmonic louder than the fundamental, which is what an
    /// overblown flute sounds like: thin and reedy, and next to a fundamental-led note it reads as
    /// a different instrument altogether. Playing a scale on it sounds like several players.
    /// </remarks>
    private static readonly float[] fluteNoteHz =
    {
        266.0f, 296.0f, 332.0f, 352.0f, 396.0f, 444.0f, 498.0f, 528.0f, 593.0f, 664.0f,
    };

    /// <summary>
    /// Pitch ratio applied to the whole instrument on top of the per note transposition.
    /// </summary>
    /// <remarks>
    /// The recorded scale is C4 to E5, which is the bottom of a flute's range - the register where
    /// the instrument is breathiest and weakest, and where these particular recordings are at their
    /// least characterful. An octave up puts it at C5 to E6, where a flute is full and bright, and
    /// it sounds markedly more like one. It also halves the length of each note, from about 1.9
    /// seconds to 0.95, which suits an instrument being played a note at a time.
    /// 2.0 is an octave, 1.5 a fifth, 1.0 as recorded.
    /// </remarks>
    private const float fluteTranspose = 2.0f;

    /// <summary>Index of the sample the low half of the scale is transposed from, and its pitch.</summary>
    /// <remarks>
    /// Both bases are chosen from the three fundamental-led recordings - notes 3, 4 and 8 - so
    /// that the whole scale has one character. Two rather than one, because transposing a single
    /// sample across the full range would mean stretching it an octave down for the bottom note,
    /// which sounds dull and doubles its length. Split this way nothing moves more than four
    /// semitones.
    /// </remarks>
    private const int lowBaseNote = 2;   // E4, H1 0.49

    /// <summary>Index of the sample the high half is transposed from.</summary>
    private const int highBaseNote = 7;  // C5, H1 0.74, the purest of the set

    /// <summary>First note taken from <see cref="highBaseNote"/> rather than <see cref="lowBaseNote"/>.</summary>
    private const int highGroupFirstNote = 5;

    private void PlaySelectedNote()
    {
        if (instrumentType == EObjectType.Flute)
        {
            PlayFluteNote();
        }
        else if (mandolinNotes != null && mandolinNotes.Length > note && mandolinNotes[note] != null)
        {
            // The mandolin recordings are consistent with each other, so they are played as they
            // are. Only the flute needs resampling.
            Utils.PlayClip2d(mandolinNotes[note], false);
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

    /// <summary>
    /// Plays one note of the flute by transposing whichever base sample is closer to it.
    /// </summary>
    /// <remarks>
    /// Falls back to the recording for the note itself if a base sample is missing, so a project
    /// with a different set of clips still makes a sound rather than none.
    /// </remarks>
    private void PlayFluteNote()
    {
        if (fluteNotes == null || note < 0 || note >= fluteNotes.Length)
        {
            return;
        }

        int baseNote = note >= highGroupFirstNote ? highBaseNote : lowBaseNote;
        if (baseNote < fluteNotes.Length && fluteNotes[baseNote] != null
            && note < fluteNoteHz.Length && baseNote < fluteNoteHz.Length)
        {
            Utils.PlayClip2dAtPitch(fluteNotes[baseNote],
                fluteTranspose * fluteNoteHz[note] / fluteNoteHz[baseNote]);
        }
        else if (fluteNotes[note] != null)
        {
            // Fallback: the recording for the note itself, still transposed so that a missing
            // base does not drop one note an octave below the rest.
            Utils.PlayClip2dAtPitch(fluteNotes[note], fluteTranspose);
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
