using UnityEngine;
using UnityEngine.InputSystem;

public class RepairDialog : MonoBehaviour
{
    public GUIStyle repairDialogStyle;
    public Texture2D aButton;
    public Texture2D bButton;
    /// <summary>Keyboard Esc hint icon (same asset as HowMany).</summary>
    public Texture2D escapeKey;
    public Texture2D buttonOutline;

    public AudioClip dialogOn;
    public AudioClip cancel;
    public AudioClip confirm;

    public UUObject itemToRepair;
    public int durability;
    private string difficultyWord;
    private string riskWord;

    protected bool initialButtonsReleased;
    private int repairDialogHold;

    /// <summary>Mouse confirm/cancel chosen on LMB down; applied after release + one frame so look/input does not leak.</summary>
    private enum DeferredMouseRepairAction
    {
        None,
        Cancel,
        Confirm
    }

    private DeferredMouseRepairAction deferredMouseRepairAction;
    private int executeDeferredMouseRepairOnFrame = -1;

    public static RepairDialog sRepairDialog;
    public static System.Action<UUObject, int> OnRepairConfirmed;

    public static void AskRepair(UUObject item, int itemDurability)
    {
        if (sRepairDialog == null)
        {
            Debug.LogError("RepairDialog singleton not found!");
            return;
        }

        sRepairDialog.itemToRepair = item;
        sRepairDialog.durability = itemDurability;
        sRepairDialog.initialButtonsReleased = false;
        sRepairDialog.deferredMouseRepairAction = DeferredMouseRepairAction.None;
        sRepairDialog.executeDeferredMouseRepairOnFrame = -1;

        int repairSkill = Skills.GetSkill(ESkill.Repair);
        int difficulty = repairSkill + 15 - itemDurability; // the roll has to beat 30 minus this

        // Six bands instead of the original's five, and they are cut on the chance of success
        // rather than on tenths of the roll. The original's are exact tenths (UW.EXE 0x74909),
        // but "hard" alone then spans 32% down to 3% and "very difficult" is spent on the case
        // that cannot succeed at all, so a broadsword and a long sword get the same word while
        // one is three times likelier to come back whole. The success chance is (30 - x) / 31
        // with x = durability - Repair + 15, which is 30 - this value.
        //
        // A random wobble used to sit on this, widening as Repair fell, so that an unskilled
        // smith misjudged the job. It is gone: the original has nothing of the kind, and at a low
        // Repair it moved the answer by up to 48 points of a 100-point scale - which made the one
        // number the player decides on worthless exactly when it mattered most.
        sRepairDialog.difficultyWord =
              difficulty >= 31 ? StringLoader.GetString(1, 219)   // 100%
            : difficulty >= 24 ? StringLoader.GetString(1, 220)   // over 75%
            : difficulty >= 16 ? StringLoader.GetString(1, 221)   // over 50%
            : difficulty >= 8  ? StringLoader.GetString(1, 222)   // over 25%
            : difficulty >= 4  ? StringLoader.GetString(1, 223)   // over 10%
            : "next to impossible";                                        // 10% or less

        // And the chance of ruining it, which the original's estimate never mentions: the word
        // only ever described the chance of success. A critical failure needs the roll at or
        // below x - 12, and the save after it fails when rand & 63 clears quality + Repair, so
        // the two together are the chance of losing the item on this attempt.
        float criticalFailure = Mathf.Clamp(18 - difficulty, 0, 31) / 31.0f;
        float saveFails = Mathf.Max(0, 63 - item.quality - repairSkill) / 64.0f;
        float ruinChance = criticalFailure * saveFails;
        sRepairDialog.riskWord = ruinChance <= 0.0f ? ""
            : ruinChance <= 0.05f ? " and risky"
            : " and very risky";

        Utils.PlayClip2d(sRepairDialog.dialogOn);
    }

    public static bool IsActive()
    {
        return sRepairDialog != null && (sRepairDialog.repairDialogHold > 0 || sRepairDialog.itemToRepair != null);
    }

    protected void Start()
    {
        sRepairDialog = this;
    }

    private void ConfirmRepair()
    {
        deferredMouseRepairAction = DeferredMouseRepairAction.None;
        executeDeferredMouseRepairOnFrame = -1;
        Utils.PlayClip2d(confirm);
        OnRepairConfirmed?.Invoke(itemToRepair, durability);
        itemToRepair = null;
    }

    private void CancelRepair()
    {
        deferredMouseRepairAction = DeferredMouseRepairAction.None;
        executeDeferredMouseRepairOnFrame = -1;
        itemToRepair = null;
        Utils.PlayClip2d(cancel);
    }

    protected void Update()
    {
        PlayerObject.DisableControls(EControlMask.RepairDialog, IsActive());

        if (itemToRepair != null && GameInput.EscapePressedThisFrame())
        {
            CancelRepair();
        }

        if (itemToRepair != null && deferredMouseRepairAction != DeferredMouseRepairAction.None)
        {
            Mouse m = GameInput.CurrentMouse;
            if (m != null && m.leftButton.isPressed)
            {
                executeDeferredMouseRepairOnFrame = -1;
            }
            else
            {
                if (executeDeferredMouseRepairOnFrame < 0)
                {
                    executeDeferredMouseRepairOnFrame = Time.frameCount + 1;
                }
                else if (Time.frameCount >= executeDeferredMouseRepairOnFrame)
                {
                    if (deferredMouseRepairAction == DeferredMouseRepairAction.Confirm)
                    {
                        ConfirmRepair();
                    }
                    else
                    {
                        CancelRepair();
                    }
                }
            }
        }

        if (itemToRepair != null)
        {
            if (deferredMouseRepairAction != DeferredMouseRepairAction.None)
            {
                return;
            }

            if (!initialButtonsReleased)
            {
                Gamepad gp = GameInput.CurrentGamepad;
                if (gp != null)
                {
                    if ((gp.aButton.isPressed || gp.aButton.wasReleasedThisFrame
                         || gp.bButton.isPressed || gp.bButton.wasReleasedThisFrame))
                    {
                        return;
                    }
                }

                initialButtonsReleased = true;
            }

            repairDialogHold = 2;

            if (GameInput.CurrentKeyboard != null
                     && (GameInput.CurrentKeyboard.enterKey.wasPressedThisFrame
                         || GameInput.CurrentKeyboard.numpadEnterKey.wasPressedThisFrame))
            {
                ConfirmRepair();
            }
            else if (GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false)
            {
                ConfirmRepair();
            }
            else if (GameInput.CurrentGamepad?.bButton.wasPressedThisFrame ?? false)
            {
                CancelRepair();
            }
        }

        bool aButtonInAction = (GameInput.CurrentGamepad?.aButton.isPressed ?? false) || (GameInput.CurrentGamepad?.aButton.wasReleasedThisFrame ?? false);
        bool bButtonInAction = (GameInput.CurrentGamepad?.bButton.isPressed ?? false) || (GameInput.CurrentGamepad?.bButton.wasReleasedThisFrame ?? false);

        if (itemToRepair == null && !aButtonInAction && !bButtonInAction && repairDialogHold > 0)
        {
            --repairDialogHold;
        }
    }

    protected void OnGUI()
    {
        if (itemToRepair != null)
        {
            GUI.depth = (int)EGUIDepth.RepairDialog;

            Texture2D tex = DataLoader.sDataLoader.invTex[6];
            float w = 5 * tex.width;
            // One row taller than it was: the risk clause makes the line wrap to three, and at
            // six rows the last one landed on top of the buttons.
            float h = 7 * tex.height;

            Rect r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            GuiInput.RegisterBlockingRect(r);
            GUI.DrawTexture(r, tex);

            // Item icon on the left
            Texture2D itemTex = itemToRepair.GetInventoryTex();
            if (itemTex != null)
            {
                GUI.DrawTexture(new Rect(r.x + 30, r.y + 20, 3 * itemTex.width, 3.6f * itemTex.height), itemTex);
            }

            // Anvil sprite on the right
            Texture2D anvilTex = DataLoader.sDataLoader.objTex[(int)EObjectType.Anvil];
            if (anvilTex != null)
            {
                GUI.DrawTexture(new Rect(r.x + w - 40 - 3 * anvilTex.width, r.y + 20, 3 * anvilTex.width, 3.6f * anvilTex.height), anvilTex);
            }

            // Title: "Repair?"
            repairDialogStyle.alignment = TextAnchor.UpperCenter;
            repairDialogStyle.fontSize = 30;
            GUI.Label(new Rect(r.x, r.y + 10, w, h), "Repair?", repairDialogStyle);

            // Difficulty text: "You think it will be [difficulty] to repair the [item name]."
            repairDialogStyle.fontSize = 24;
            repairDialogStyle.alignment = TextAnchor.MiddleCenter;
            string difficultyText = StringLoader.GetString(1, 216); // "You think it will be"
            difficultyText += difficultyWord;
            difficultyText += riskWord;
            difficultyText += StringLoader.GetString(1, 217); // "to repair the"
            difficultyText += itemToRepair.singularName + ".";
            difficultyText += StringLoader.GetString(1, 218); // "Make an attempt?"
            // Centred in the band between the title row and the button strip, not in the whole
            // panel: that keeps a long line off the buttons however it wraps, and puts the block
            // half a row lower than centring on the panel did. The strip is 54 tall in the mouse
            // layout, which is the taller of the two.
            GUI.Label(new Rect(r.x + w / 4, r.y + tex.height, w / 2, r.height - tex.height - 54),
                      difficultyText, repairDialogStyle);

            // Buttons
            bool mouseUi = GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard;

            repairDialogStyle.fontSize = 20;
            repairDialogStyle.alignment = TextAnchor.MiddleLeft;

            if (mouseUi)
            {
                // Match the original gamepad hint placement + click rects.
                int kk = 29;
                int ww = 100;
                float yy = r.y + r.height - 25 - kk;

                Rect hintCancelRect = new Rect(r.x + 40, yy, ww, kk);
                Rect hintConfirmRect = new Rect(r.x + r.width - 160, yy, ww, kk);


                if (buttonOutline != null)
                {
                    GUI.DrawTexture(hintCancelRect, buttonOutline, ScaleMode.StretchToFill, true);
                    GUI.DrawTexture(hintConfirmRect, buttonOutline, ScaleMode.StretchToFill, true);
                }

                if (escapeKey != null)
                {
                    GUI.DrawTexture(new Rect(hintCancelRect.x + 10, hintCancelRect.y + 2, 24, 24), escapeKey);
                }
                GUI.Label(new Rect(hintCancelRect.x + 10 + 24 + 5, hintCancelRect.y, ww, kk), "Forgo", repairDialogStyle);

                repairDialogStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(hintConfirmRect, "Attempt", repairDialogStyle);
                repairDialogStyle.alignment = TextAnchor.MiddleLeft;

                if (GuiInput.TryConsumeClickInRect(hintCancelRect))
                {
                    deferredMouseRepairAction = DeferredMouseRepairAction.Cancel;
                    executeDeferredMouseRepairOnFrame = -1;
                }
                else if (GuiInput.TryConsumeClickInRect(hintConfirmRect))
                {
                    deferredMouseRepairAction = DeferredMouseRepairAction.Confirm;
                    executeDeferredMouseRepairOnFrame = -1;
                }
            }
            else
            {
                // Keep the original gamepad hint placement + click rects.
                int k = 20;
                Rect bRect = new Rect(r.x + 50, r.y + r.height - 25 - k, k, k);
                Rect bLabelRect = new Rect(r.x + 50 + k + 5, r.y + r.height - 25 - k, 150, k);
                Rect aRect = new Rect(r.x + r.width - 170, r.y + r.height - 25 - k, k, k);
                Rect aLabelRect = new Rect(r.x + r.width - 170 + k + 5, r.y + r.height - 25 - k, 150, k);

                if (bButton != null)
                {
                    GUI.DrawTexture(bRect, bButton);
                }
                GUI.Label(bLabelRect, "Forgo", repairDialogStyle);

                if (aButton != null)
                {
                    GUI.DrawTexture(aRect, aButton);
                }
                GUI.Label(aLabelRect, "Attempt", repairDialogStyle);

                int kk = 20;
                Rect cancelRect = new Rect(r.x + 50, r.y + r.height - 25 - kk, 200, kk + 8);
                Rect confirmRect = new Rect(r.x + r.width - 220, r.y + r.height - 25 - kk, 200, kk + 8);
                if (GuiInput.TryConsumeClickInRect(cancelRect))
                {
                    CancelRepair();
                }
                else if (GuiInput.TryConsumeClickInRect(confirmRect))
                {
                    ConfirmRepair();
                }
            }

            GuiInput.TryConsumeClickInPanel(r);
        }
    }
}
