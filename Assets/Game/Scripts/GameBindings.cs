using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Scripting;

/// <summary>The device a binding belongs to: the keyboard with the mouse's three spare buttons, or the pad.</summary>
public enum EBindDevice
{
    Keyboard,
    Gamepad,
}

/// <summary>How a binding is pressed, which decides what it may be bound to.</summary>
public enum EBindKind
{
    /// <summary>Pressed: any free key, a chord with Ctrl, Shift or Alt, a pad button.</summary>
    Press,
    /// <summary>Held down while walking: a key on its own, never a chord, since a modifier is held while walking.</summary>
    Held,
    /// <summary>A whole stick: only the other stick.</summary>
    Stick,
}

/// <summary>What a rebind did: changed the bindings, found the key already there, or was refused.</summary>
public enum EBindResult
{
    Changed,
    Unchanged,
    Refused,
}

/// <summary>The pages of the pad's map, which are its contexts: where a binding is live.</summary>
[Flags]
public enum EPadPages
{
    None = 0,
    Walking = 1,
    Inventory = 2,
    Magic = 4,
    Map = 8,
}

/// <summary>One binding the player can change: an action of <see cref="GameActions"/>, or one part of the keys' walking vector.</summary>
public sealed class BindEntry
{
    /// <summary>Its name in PlayerPrefs: map, action and part, "Keys.World/Move/Up".</summary>
    public readonly string Id;
    public readonly EBindDevice Device;
    public readonly EBindKind Kind;
    /// <summary>The pad's pages it is live on; None on the keyboard, which is one context.</summary>
    public readonly EPadPages Pages;
    public readonly InputAction Action;
    /// <summary>The part of a composite it is, or null.</summary>
    public readonly string Part;
    /// <summary>An action that always has the same binding: the map's Close beside the walking Map.</summary>
    public readonly InputAction Follower;
    /// <summary>For the map's quill, the second of its two axes; its stick is <see cref="Action"/>'s.</summary>
    public readonly InputAction AxisY;
    /// <summary>What it does, in the lines that say what was changed.</summary>
    public readonly string Label;
    internal string Default;
    internal string Current;

    internal BindEntry(string id, EBindDevice device, EBindKind kind, EPadPages pages, string label, InputAction action,
        string part = null, InputAction follower = null, InputAction axisY = null)
    {
        Id = id;
        Device = device;
        Kind = kind;
        Pages = pages;
        Label = label;
        Action = action;
        Part = part;
        Follower = follower;
        AxisY = axisY;
    }

    /// <summary>The binding in force, as kept: "&lt;Keyboard&gt;/j", "ctrl+&lt;Keyboard&gt;/j", "&lt;Gamepad&gt;/buttonSouth"...</summary>
    public string Binding => Current;

    public bool IsDefault => Current == Default;

    /// <summary>The binding it has when nothing was changed.</summary>
    public string DefaultBinding => Default;
}

/// <summary>
/// A key that acts only while none of some modifiers is down: a plain J beside a Ctrl+J of another action, so
/// that the chord does not also fire the plain key's action.
/// </summary>
/// <remarks>
/// The Input System's own answer, <see cref="InputSettings.shortcutKeysConsumeInput"/>, ranks every composite above
/// a plain binding and lets it swallow the press: the arrows' walking vector would then take the arrows from the
/// panels' cursor. This composite leaves every other binding as it was.
/// </remarks>
// made by reflection from its name: kept from the player build's stripping
[Preserve]
[DisplayStringFormat("{binding}")]
public class KeyAloneComposite : InputBindingComposite<float>
{
    [InputControl(layout = "Button")] public int binding;
    [InputControl(layout = "Button")] public int modifier;

    public override float ReadValue(ref InputBindingCompositeContext context)
    {
        return context.ReadValueAsButton(modifier) ? 0f : context.ReadValue<float>(binding);
    }

    public override float EvaluateMagnitude(ref InputBindingCompositeContext context)
    {
        return ReadValue(ref context);
    }
}

/// <summary>
/// The player's own bindings, mod 320: which key or button each action of <see cref="GameActions"/> answers to,
/// changed on the control maps of the game menu (<see cref="ControlsMap"/>), kept in PlayerPrefs, put back by
/// Reset and Undo changes like the options.
/// </summary>
/// <remarks>
/// <para>Every change is a swap: the action takes the key, and whatever held that key takes the action's old one,
/// so none shares a key with another (the user's rule of 2 November 2026). A keyboard action may have no key at all
/// (mod 1080, the user's of 4 and 5 November 2026): Delete on its row takes its key away after a Yes, some of the
/// original's actions start with none, and an action that gives its key to one with none is left with none, the
/// swap's own rule. The pad keeps a button on every action. A key that cannot take part in a swap - Esc, Enter, the arrows, the Windows keys,
/// Start, the d-pad that moves the panels' cursor - is refused, and the refusal says why.</para>
/// <para>The keyboard is one context; the pad has one per page of its map, and a swap of two buttons runs on every
/// page an action it moves is live on, so that no page is left with two actions on one button.</para>
/// <para>The pad's bindings are kept as for a right-handed character and mirrored, left trigger with right, for a
/// left-handed one, on every page: Kweepa's link between the weapon hand and the triggers, made to follow the
/// player's own layout.</para>
/// <para>The actions' bindings are rebuilt from this table whenever it changes, the asset disabled meanwhile: that is
/// what lets a plain key become a chord. Unity's own override JSON is not used: it finds a binding by its id, and
/// the ids of bindings made in code are new every run.</para>
/// </remarks>
public static class GameBindings
{
    private const string KeyboardPrefsKey = "Bindings_Keyboard";
    private const string GamepadPrefsKey = "Bindings_Gamepad";
    private const string KeyAlone = "KeyAlone";

    private static readonly string[] Modifiers = { "ctrl", "shift", "alt" };

    public static readonly List<BindEntry> All = new List<BindEntry>();

    private static bool loaded;
    private static bool appliedLeftHanded;
    private static bool keysEnabled = true;
    private static bool digitsSuspended;
    private static EPadOwner padOwner = EPadOwner.Game;

    /// <summary>Who has the pad's buttons this frame: the game, a conversation, or a dialog that reads them itself.</summary>
    private enum EPadOwner
    {
        Game,
        Conversation,
        Dialog,
    }

    /// <summary>
    /// The screens that read the pad's buttons directly - the how-many box, the yes/no questions, the flute, a cutscene, the
    /// on-screen keyboard, the small-window warning, the game menu - and the sleep under its fade: while one is up the game's
    /// pad buttons are silent, so that a button the player moved onto A, B, X or Y does not act behind it as well, nor
    /// the press that closes it on its last frame (fixes 459 and 456, the user's answer j of 11 November 2026).
    /// </summary>
    private const EControlMask PadOwnedByDialog = EControlMask.HowMany | EControlMask.RepairDialog | EControlMask.Flute
        | EControlMask.Cutscene | EControlMask.Keyboard | EControlMask.Warning | EControlMask.SaveLoad | EControlMask.Resting;

    /// <summary>The buttons a conversation reads for its replies.</summary>
    private static readonly string[] ConversationButtons = { "<Gamepad>/buttonSouth", "<Gamepad>/dpad/up", "<Gamepad>/dpad/down" };

    static GameBindings()
    {
        InputSystem.RegisterBindingComposite<KeyAloneComposite>(KeyAlone);

        GameActions.KeysWorldActions k = GameActions.KeysWorld;
        AddMovePart("Up", "Move forward");
        AddMovePart("Down", "Move back");
        AddMovePart("Left", "Move left");
        AddMovePart("Right", "Move right");
        KeyEntry(k.Sprint, EBindKind.Held, "Sprint modifier");
        KeyEntry(k.WalkQuietly, EBindKind.Held, "Walk quietly modifier");
        KeyEntry(k.MouseLook, EBindKind.Held, "Look with the mouse modifier");
        KeyEntry(k.CancelCharge, EBindKind.Held, "Cancel a charge");
        KeyEntry(k.Jump, EBindKind.Press, "Jump");
        KeyEntry(k.Use, EBindKind.Press, "Use or pick up");
        KeyEntry(k.Cast, EBindKind.Press, "Cast the spell");
        KeyEntry(k.MagicPanel, EBindKind.Press, "Magic panel");
        KeyEntry(k.Inventory, EBindKind.Press, "Inventory");
        KeyEntry(k.Stats, EBindKind.Press, "Stats");
        KeyEntry(k.Map, EBindKind.Press, "Map", GameActions.KeysMap.Close);
        KeyEntry(k.Quicksave, EBindKind.Press, "Quicksave");
        KeyEntry(k.Sleep, EBindKind.Press, "Sleep");
        KeyEntry(k.Track, EBindKind.Press, "Track");
        KeyEntry(k.MessagesBack, EBindKind.Press, "Messages back");
        KeyEntry(k.MessagesForward, EBindKind.Press, "Messages forward");
        KeyEntry(k.Controls, EBindKind.Press, "Controls");
        KeyEntry(k.TurnLeft, EBindKind.Held, "Turn left");
        KeyEntry(k.TurnRight, EBindKind.Held, "Turn right");
        KeyEntry(k.WalkSlowly, EBindKind.Held, "Move forward quietly");
        KeyEntry(k.LookDown, EBindKind.Press, "Look down");
        KeyEntry(k.LookAhead, EBindKind.Press, "Look ahead");
        KeyEntry(k.LookUp, EBindKind.Press, "Look up");
        KeyEntry(k.FlyUp, EBindKind.Held, "Fly up");
        KeyEntry(k.FlyDown, EBindKind.Held, "Fly down");
        KeyEntry(k.LongJump, EBindKind.Press, "Standing long jump");
        KeyEntry(k.CharacterPanel, EBindKind.Press, "Inventory and stats");

        const EPadPages walking = EPadPages.Walking;
        // the walking actions that still act with the inventory or the magic panel open: the sticks
        // (PlayerObject.cs, look and move), Sprint, the triggers (Attack closes the panels, PlayerPanelInput), the two
        // panels' buttons, Quicksave, and Map (MapScreen opens the map over either panel)
        const EPadPages withPanels = EPadPages.Walking | EPadPages.Inventory | EPadPages.Magic;
        GameActions.PadWorldActions p = GameActions.PadWorld;
        PadEntry(p.Move, EBindKind.Stick, withPanels, "Move");
        PadEntry(p.Look, EBindKind.Stick, withPanels, "Look");
        PadEntry(p.Sprint, EBindKind.Press, withPanels, "Sprint");
        PadEntry(p.Jump, EBindKind.Press, walking, "Jump");
        PadEntry(p.LookAt, EBindKind.Press, walking, "Look at");
        PadEntry(p.Use, EBindKind.Press, walking, "Use or pick up");
        PadEntry(p.Cancel, EBindKind.Press, walking, "Cancel a charge or a spell, drop the item");
        PadEntry(p.Attack, EBindKind.Press, withPanels, "Attack, or cast a readied spell");
        PadEntry(p.Stats, EBindKind.Press, withPanels, "Stats");
        PadEntry(p.MagicPanel, EBindKind.Press, withPanels, "Magic panel");
        PadEntry(p.Inventory, EBindKind.Press, withPanels, "Inventory");
        // live with the panels open too: on a button of theirs it took their press, and on the d-pad their cursor (fix 458)
        PadEntry(p.Map, EBindKind.Press, withPanels | EPadPages.Map, "Map", GameActions.PadMap.Close);
        PadEntry(p.Quicksave, EBindKind.Press, withPanels, "Quicksave");
        PadEntry(p.Sleep, EBindKind.Press, walking, "Sleep");
        PadEntry(p.Track, EBindKind.Press, walking, "Track");
        PadEntry(p.MessagesBack, EBindKind.Press, walking, "Messages back");
        PadEntry(p.MessagesForward, EBindKind.Press, walking, "Messages forward");

        GameActions.PadInventoryActions inv = GameActions.PadInventory;
        PadEntry(inv.Select, EBindKind.Press, EPadPages.Inventory, "Look or use; hold: pick up");
        PadEntry(inv.SecondaryUse, EBindKind.Press, EPadPages.Inventory, "Equip, eat, unequip");
        PadEntry(inv.Throw, EBindKind.Press, EPadPages.Inventory, "Throw");
        PadEntry(inv.Back, EBindKind.Press, EPadPages.Inventory, "Back, or cancel a readied spell");

        GameActions.PadMagicActions magic = GameActions.PadMagic;
        PadEntry(magic.Select, EBindKind.Press, EPadPages.Magic, "Lay the rune, or dispel");
        PadEntry(magic.Clear, EBindKind.Press, EPadPages.Magic, "Clear the runes");
        PadEntry(magic.Cast, EBindKind.Press, EPadPages.Magic, "Cast");

        GameActions.PadMapActions map = GameActions.PadMap;
        BindEntry quill = new BindEntry("Pad.Map/Quill", EBindDevice.Gamepad, EBindKind.Stick, EPadPages.Map, "Move the quill",
            map.QuillX, axisY: map.QuillY);
        quill.Default = StickOf(map.QuillX.bindings[0].path);
        All.Add(quill);
        PadEntry(map.Select, EBindKind.Press, EPadPages.Map, "Place or edit a note");
        PadEntry(map.EraseNote, EBindKind.Press, EPadPages.Map, "Erase the note");
        PadEntry(map.PreviousNote, EBindKind.Press, EPadPages.Map, "Previous note");
        PadEntry(map.NextNote, EBindKind.Press, EPadPages.Map, "Next note");
        PadEntry(map.PreviousPage, EBindKind.Press, EPadPages.Map, "Previous page");
        PadEntry(map.NextPage, EBindKind.Press, EPadPages.Map, "Next page");
        PadEntry(map.Back, EBindKind.Press, EPadPages.Map, "Close (on release)");

        foreach (BindEntry e in All)
        {
            e.Current = e.Default;
        }
    }

    private static void AddMovePart(string part, string label)
    {
        InputAction move = GameActions.KeysWorld.Move;
        BindEntry e = new BindEntry("Keys.World/Move/" + part, EBindDevice.Keyboard, EBindKind.Held, EPadPages.None, label, move, part);
        foreach (InputBinding b in move.bindings)
        {
            if (b.isPartOfComposite && string.Equals(b.name, part, StringComparison.OrdinalIgnoreCase))
            {
                e.Default = b.path;
            }
        }
        All.Add(e);
    }

    private static void KeyEntry(InputAction action, EBindKind kind, string label, InputAction follower = null)
    {
        BindEntry e = new BindEntry(action.actionMap.name + "/" + action.name, EBindDevice.Keyboard, kind, EPadPages.None, label,
            action, follower: follower);
        e.Default = KeyDefault(action);
        All.Add(e);
    }

    /// <summary>
    /// A keyboard action's binding as GameActions writes it, in the kept form: a plain path, a chord from its
    /// OneModifier composite ("shift+&lt;Keyboard&gt;/space"), or "" for an action that starts with no key.
    /// </summary>
    private static string KeyDefault(InputAction action)
    {
        if (action.bindings.Count == 0)
        {
            return "";
        }
        InputBinding first = action.bindings[0];
        if (!first.isComposite)
        {
            return first.path ?? "";
        }
        string modifier = null;
        string key = null;
        for (int i = 1; i < action.bindings.Count && action.bindings[i].isPartOfComposite; i++)
        {
            InputBinding part = action.bindings[i];
            if (string.Equals(part.name, "Modifier", StringComparison.OrdinalIgnoreCase))
            {
                modifier = ModifierOf(ControlName(part.path));
            }
            else if (string.Equals(part.name, "Binding", StringComparison.OrdinalIgnoreCase))
            {
                key = part.path;
            }
        }
        return modifier != null && key != null ? modifier + "+" + key : key ?? "";
    }

    private static void PadEntry(InputAction action, EBindKind kind, EPadPages pages, string label, InputAction follower = null)
    {
        BindEntry e = new BindEntry(action.actionMap.name + "/" + action.name, EBindDevice.Gamepad, kind, pages, label, action,
            follower: follower);
        e.Default = action.bindings[0].path;
        All.Add(e);
    }

    private static string StickOf(string axisPath)
    {
        int slash = axisPath.LastIndexOf('/');
        return slash > 0 && (axisPath.EndsWith("/x", StringComparison.Ordinal) || axisPath.EndsWith("/y", StringComparison.Ordinal))
            ? axisPath.Substring(0, slash)
            : axisPath;
    }

    // ---------------------------------------------------------------- the frame, loading and applying

    /// <summary>
    /// Once a frame, before anything reads input (<see cref="GameActions.Refresh"/>): loads the player's bindings the
    /// first time, follows the character's weapon hand onto the pad's triggers, and silences the keyboard's actions
    /// while it is writing - typing runes or on the on-screen keyboard - so that a letter the player bound does not
    /// act while it spells, and the pad's buttons while a dialog reads them. Once a frame, so a dialog closed by a press
    /// gives the buttons back on the next frame, with that press gone.
    /// </summary>
    public static void Refresh()
    {
        EnsureLoaded();
        if (PlayerData.sData != null && PlayerData.sData.leftHanded != appliedLeftHanded)
        {
            appliedLeftHanded = PlayerData.sData.leftHanded;
            Apply(EBindDevice.Gamepad);
        }
        bool keys = !GameInput.IsGameplayKeyboardInputBlocked();
        bool digits = DigitsAnswer();
        EPadOwner owner = PadOwner();
        if (keys != keysEnabled || digits != digitsSuspended || owner != padOwner)
        {
            keysEnabled = keys;
            digitsSuspended = digits;
            padOwner = owner;
            ApplyEnables();
        }
    }

    private static EPadOwner PadOwner()
    {
        PlayerObject p = PlayerObject.Player;
        if (p == null)
        {
            return EPadOwner.Game;
        }
        if ((p.controlsDisabled & PadOwnedByDialog) != 0)
        {
            return EPadOwner.Dialog;
        }
        return (p.controlsDisabled & EControlMask.Conversation) != 0 ? EPadOwner.Conversation : EPadOwner.Game;
    }

    /// <summary>
    /// Whether a pad action may act, by who has the pad. The sticks and the stats' trigger are values whose readers check
    /// the controls themselves, and always read. Under a dialog every button is the dialog's. In a conversation the pack
    /// stays, for the barter, with the button that opens and closes it unless that is one the replies are chosen with;
    /// the rest of walking, and the magic panel, are silent.
    /// </summary>
    private static bool PadActionLive(InputAction action)
    {
        if (action.type != InputActionType.Button)
        {
            return true;
        }
        switch (padOwner)
        {
            case EPadOwner.Dialog:
                return false;
            case EPadOwner.Conversation:
                if (action.actionMap == GameActions.PadInventory.Select.actionMap)
                {
                    return true;
                }
                if (action == GameActions.PadWorld.Inventory)
                {
                    BindEntry e = Of(action);
                    return e == null || Array.IndexOf(ConversationButtons, Actual(e.Current)) < 0;
                }
                return false;
            default:
                return true;
        }
    }

    /// <summary>
    /// True in a conversation and while an instrument is played, where the digits always reply and play: an action
    /// the player bound to a digit is silent there (the user's, 2 November 2026: the digits are bound like any key,
    /// and stay fixed only in those two places).
    /// </summary>
    private static bool DigitsAnswer()
    {
        PlayerObject p = PlayerObject.Player;
        return p != null && (p.controlsDisabled & (EControlMask.Conversation | EControlMask.Flute)) != 0;
    }

    private static bool UsesDigit(string binding)
    {
        Split(binding, out string _, out string key);
        // the Input System names the digit keys 1 to 0, not digit1 (its Keyboard layout)
        string name = ControlName(key);
        return key != null && key.StartsWith("<Keyboard>/", StringComparison.Ordinal) && name.Length == 1 && char.IsDigit(name[0]);
    }

    /// <summary>
    /// The keyboard's actions on or off: all off while the keyboard writes; those on a digit off while the digits
    /// answer. The pad's buttons by who has the pad (<see cref="PadActionLive"/>). Action by action, so that no map's
    /// own Enable() is relied on to turn back on what was turned off alone.
    /// </summary>
    private static void ApplyEnables()
    {
        foreach (InputActionMap map in KeyMaps())
        {
            foreach (InputAction action in map.actions)
            {
                bool on = keysEnabled;
                if (on && digitsSuspended)
                {
                    BindEntry e = Of(action);
                    on = !(e != null && UsesDigit(e.Current));
                    if (on && action == GameActions.KeysWorld.Move)
                    {
                        foreach (BindEntry part in All)
                        {
                            on &= !(part.Action == action && part.Part != null && UsesDigit(part.Current));
                        }
                    }
                }
                if (on)
                {
                    action.Enable();
                }
                else
                {
                    action.Disable();
                }
            }
        }
        foreach (InputActionMap map in PadMaps())
        {
            foreach (InputAction action in map.actions)
            {
                if (PadActionLive(action))
                {
                    action.Enable();
                }
                else
                {
                    action.Disable();
                }
            }
        }
    }

    /// <summary>The pad's maps a dialog silences; the map's own stays, the map being no dialog.</summary>
    private static IEnumerable<InputActionMap> PadMaps()
    {
        yield return GameActions.PadWorld.Move.actionMap;
        yield return GameActions.PadInventory.Select.actionMap;
        yield return GameActions.PadMagic.Select.actionMap;
    }

    private static IEnumerable<InputActionMap> KeyMaps()
    {
        yield return GameActions.KeysWorld.Move.actionMap;
        yield return GameActions.KeysMap.Close.actionMap;
    }

    private static void EnsureLoaded()
    {
        if (loaded)
        {
            return;
        }
        loaded = true;
        appliedLeftHanded = PlayerData.sData != null && PlayerData.sData.leftHanded;
        Load(EBindDevice.Keyboard, PlayerPrefs.GetString(KeyboardPrefsKey, ""));
        Load(EBindDevice.Gamepad, PlayerPrefs.GetString(GamepadPrefsKey, ""));
        Apply(EBindDevice.Keyboard);
        Apply(EBindDevice.Gamepad);
    }

    private static void Load(EBindDevice device, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        string problem = null;
        foreach (string line in text.Split('\n'))
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }
            int eq = line.IndexOf('=');
            BindEntry e = eq > 0 ? Find(line.Substring(0, eq)) : null;
            string binding = eq > 0 ? line.Substring(eq + 1).Trim() : null;
            if (e == null || e.Device != device)
            {
                problem = "unknown line '" + line + "'";
                break;
            }
            string refused = Allowed(e, binding);
            if (refused != null)
            {
                problem = line + ": " + refused;
                break;
            }
            e.Current = binding;
        }
        problem = problem ?? Clash(device);
        if (problem != null)
        {
            Debug.LogWarning($"[GameBindings] the saved {device} bindings do not check out ({problem}): the defaults are used.");
            ResetDevice(device);
        }
    }

    /// <summary>Writes the bindings that differ from the defaults, one line each. Called when the menu closes, with the options.</summary>
    public static void Save()
    {
        EnsureLoaded();
        Store(EBindDevice.Keyboard);
        Store(EBindDevice.Gamepad);
    }

    /// <summary>One device's bindings to its key: at every change, and when the menu closes.</summary>
    private static void Store(EBindDevice device)
    {
        PlayerPrefs.SetString(device == EBindDevice.Keyboard ? KeyboardPrefsKey : GamepadPrefsKey, Serialize(device));
    }

    private static string Serialize(EBindDevice device)
    {
        StringBuilder sb = new StringBuilder();
        foreach (BindEntry e in All)
        {
            if (e.Device == device && !e.IsDefault)
            {
                sb.Append(e.Id).Append('=').Append(e.Current).Append('\n');
            }
        }
        return sb.ToString();
    }

    public static BindEntry Find(string id)
    {
        foreach (BindEntry e in All)
        {
            if (e.Id == id)
            {
                return e;
            }
        }
        return null;
    }

    /// <summary>The entry an action is bound through, or null when the action is not the player's to change.</summary>
    public static BindEntry Of(InputAction action, string part = null)
    {
        foreach (BindEntry e in All)
        {
            if ((e.Action == action || e.Follower == action || e.AxisY == action) && e.Part == part)
            {
                return e;
            }
        }
        return null;
    }

    /// <summary>Rebuilds the actions of a device from the table: every binding erased and made again.</summary>
    private static void Apply(EBindDevice device)
    {
        GameActions.Asset.Disable();
        try
        {
            if (device == EBindDevice.Keyboard)
            {
                InputAction move = GameActions.KeysWorld.Move;
                Clear(move);
                // a part with no key is left out of the vector (mod 1080), and with none the vector has no binding
                List<string> parts = new List<string>();
                foreach (string part in new[] { "Up", "Down", "Left", "Right" })
                {
                    if (Find("Keys.World/Move/" + part).Current.Length > 0)
                    {
                        parts.Add(part);
                    }
                }
                if (parts.Count > 0)
                {
                    InputActionSetupExtensions.CompositeSyntax vector = move.AddCompositeBinding("2DVector(mode=1)");
                    foreach (string part in parts)
                    {
                        vector.With(part, Find("Keys.World/Move/" + part).Current);
                    }
                }
            }
            foreach (BindEntry e in All)
            {
                if (e.Device != device || e.Part != null)
                {
                    continue;
                }
                if (device == EBindDevice.Keyboard)
                {
                    AddKey(e.Action, e);
                    if (e.Follower != null)
                    {
                        AddKey(e.Follower, e);
                    }
                }
                else
                {
                    string actual = Actual(e.Current);
                    Clear(e.Action);
                    if (e.AxisY != null)
                    {
                        Clear(e.AxisY);
                        e.Action.AddBinding(actual + "/x");
                        e.AxisY.AddBinding(actual + "/y");
                    }
                    else
                    {
                        e.Action.AddBinding(actual);
                    }
                    if (e.Follower != null)
                    {
                        Clear(e.Follower);
                        e.Follower.AddBinding(actual);
                    }
                }
            }
        }
        finally
        {
            GameActions.Asset.Enable();
            ApplyEnables();
        }
        // written as they change, as the options are: a game closed with the menu open (Alt-F4) keeps them
        Store(device);
    }

    private static void Clear(InputAction action)
    {
        while (action.bindings.Count > 0)
        {
            action.ChangeBinding(0).Erase();
        }
    }

    /// <summary>
    /// A keyboard binding: a chord as the Input System's OneModifier, the modifier pressed first; a plain key beside a
    /// chord on the same key as a <see cref="KeyAloneComposite"/>, so that the chord does not fire it too.
    /// </summary>
    private static void AddKey(InputAction action, BindEntry e)
    {
        Clear(action);
        if (string.IsNullOrEmpty(e.Current))
        {
            // no key (mod 1080): the action stays, with no binding
            return;
        }
        Split(e.Current, out string modifier, out string key);
        if (modifier != null)
        {
            // 1 is ModifiersOrder.Ordered: Ctrl, then J; a J held when Ctrl comes is not the chord
            action.AddCompositeBinding("OneModifier(modifiersOrder=1)")
                .With("Modifier", "<Keyboard>/" + modifier)
                .With("Binding", key);
            return;
        }
        List<string> excluded = new List<string>();
        foreach (BindEntry other in All)
        {
            if (other != e && other.Device == EBindDevice.Keyboard)
            {
                Split(other.Current, out string m, out string k);
                if (m != null && k == key && !excluded.Contains(m))
                {
                    excluded.Add(m);
                }
            }
        }
        if (excluded.Count == 0)
        {
            action.AddBinding(key);
            return;
        }
        InputActionSetupExtensions.CompositeSyntax composite = action.AddCompositeBinding(KeyAlone).With("Binding", key);
        foreach (string m in excluded)
        {
            composite.With("Modifier", "<Keyboard>/" + m);
        }
    }

    // ---------------------------------------------------------------- what a binding is

    /// <summary>A keyboard binding's two halves: "ctrl+&lt;Keyboard&gt;/j" is ctrl and &lt;Keyboard&gt;/j; a plain key has no modifier.</summary>
    public static void Split(string binding, out string modifier, out string key)
    {
        modifier = null;
        key = binding;
        if (string.IsNullOrEmpty(binding))
        {
            return;
        }
        int plus = binding.IndexOf('+');
        if (plus > 0 && binding.IndexOf('<') > plus)
        {
            modifier = binding.Substring(0, plus);
            key = binding.Substring(plus + 1);
        }
    }

    /// <summary>The name of a path's control after its device: "&lt;Keyboard&gt;/leftShift" is leftShift.</summary>
    public static string ControlName(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }
        int close = path.IndexOf('>');
        return close >= 0 && close + 2 <= path.Length ? path.Substring(close + 2) : path;
    }

    private static bool IsMouse(string path) => path != null && path.StartsWith("<Mouse>/", StringComparison.Ordinal);

    /// <summary>The modifier word a key is, when it is one of the six modifier keys, or ctrl, shift, alt themselves.</summary>
    private static string ModifierOf(string control)
    {
        switch (control)
        {
            case "leftCtrl":
            case "rightCtrl":
            case "ctrl":
                return "ctrl";
            case "leftShift":
            case "rightShift":
            case "shift":
                return "shift";
            case "leftAlt":
            case "rightAlt":
            case "alt":
                return "alt";
            default:
                return null;
        }
    }

    /// <summary>The keys of the keyboard a plain key stands for: ctrl is both Ctrl keys.</summary>
    private static string[] KeysOf(string control)
    {
        switch (control)
        {
            case "ctrl": return new[] { "leftCtrl", "rightCtrl" };
            case "shift": return new[] { "leftShift", "rightShift" };
            case "alt": return new[] { "leftAlt", "rightAlt" };
            default: return new[] { control };
        }
    }

    private static bool KeysOverlap(string a, string b)
    {
        foreach (string x in KeysOf(a))
        {
            if (Array.IndexOf(KeysOf(b), x) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The pad's path as the game reads it now: the triggers swapped for a left-handed character.</summary>
    public static string Actual(string kept)
    {
        return appliedLeftHanded ? MirrorTriggers(kept) : kept;
    }

    /// <summary>A pressed pad control as it is kept: for a right-handed character.</summary>
    public static string Kept(string actual)
    {
        return appliedLeftHanded ? MirrorTriggers(actual) : actual;
    }

    private static string MirrorTriggers(string path)
    {
        switch (path)
        {
            case "<Gamepad>/leftTrigger": return "<Gamepad>/rightTrigger";
            case "<Gamepad>/rightTrigger": return "<Gamepad>/leftTrigger";
            default: return path;
        }
    }

    /// <summary>The trigger the weapon hand pulls, as a path: the right one for a right-handed character.</summary>
    public static string WeaponTrigger => appliedLeftHanded ? "<Gamepad>/leftTrigger" : "<Gamepad>/rightTrigger";

    // ---------------------------------------------------------------- what may be bound

    private static readonly string[] MenuKeys = { "tab", "pageUp", "pageDown", "r", "z", "delete" };

    private static readonly string[] PadButtons =
    {
        "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder", "leftTrigger", "rightTrigger",
        "select", "leftStickPress", "rightStickPress", "dpad/up", "dpad/down", "dpad/left", "dpad/right",
    };

    /// <summary>Why an entry cannot take a binding, in a line for the player; null when it can.</summary>
    public static string Refusal(BindEntry e, string binding)
    {
        if (string.IsNullOrEmpty(binding))
        {
            return "That is no key.";
        }
        if (e.Device == EBindDevice.Gamepad)
        {
            return PadRefusal(e, binding);
        }
        Split(binding, out string modifier, out string key);
        string name = ControlName(key);
        if (modifier != null && Array.IndexOf(Modifiers, modifier) < 0)
        {
            return "That is no key.";
        }
        if (IsMouse(key))
        {
            if (name != "middleButton" && name != "backButton" && name != "forwardButton")
            {
                return "The left and right mouse buttons cannot be bound.";
            }
        }
        else if (!key.StartsWith("<Keyboard>/", StringComparison.Ordinal))
        {
            return "That is no key.";
        }
        else
        {
            switch (name)
            {
                case "escape":
                    return "Esc always opens the game menu.";
                case "enter":
                    return "Enter is always for typing runes.";
                case "upArrow":
                case "downArrow":
                case "leftArrow":
                case "rightArrow":
                    return "The arrows always walk.";
                case "leftMeta":
                case "rightMeta":
                    return "The Windows keys belong to Windows.";
            }
            if (ModifierOf(name) == null && !ControlsMap.OnDrawing(name))
            {
                return "Only the keys drawn on the map can be bound.";
            }
            if (modifier != null && ModifierOf(name) != null)
            {
                return "One modifier at a time.";
            }
            if (modifier == "alt" && (name == "f4" || name == "tab"))
            {
                return "Alt+" + KeyLabel(key) + " belongs to Windows.";
            }
        }
        if (modifier != null && e.Kind == EBindKind.Held)
        {
            // held as you walk: a modifier is held then too (the user's wording, 2 November 2026)
            return "No modifier possible.";
        }
        if (e.Id == "Keys.World/Controls" && Array.IndexOf(MenuKeys, name) >= 0)
        {
            return KeyLabel(key) + " works the game menu, which Controls opens.";
        }
        return null;
    }

    /// <summary>
    /// <see cref="Refusal"/>, but with no key allowed on the keyboard: what a swap may hand an action, and what a saved
    /// line may say (mod 1080).
    /// </summary>
    private static string Allowed(BindEntry e, string binding)
    {
        return e.Device == EBindDevice.Keyboard && binding != null && binding.Length == 0 ? null : Refusal(e, binding);
    }

    private static string PadRefusal(BindEntry e, string path)
    {
        string name = ControlName(path);
        if (name == "start")
        {
            return PadGlyphs.Name("Start") + " always opens the game menu.";
        }
        if (e.Kind == EBindKind.Stick)
        {
            return name == "leftStick" || name == "rightStick" ? null : e.Label + " takes a stick: tilt the one you want.";
        }
        if (Array.IndexOf(PadButtons, name) < 0)
        {
            return "That is no button.";
        }
        if (name.StartsWith("dpad", StringComparison.Ordinal) && (e.Pages & (EPadPages.Inventory | EPadPages.Magic)) != 0)
        {
            return "The d-pad always moves the cursor in the inventory and the magic panel.";
        }
        return null;
    }

    /// <summary>
    /// The first pair of bindings that cannot stand together on a device, as a line; null when the table checks out.
    /// </summary>
    public static string Clash(EBindDevice device)
    {
        for (int i = 0; i < All.Count; i++)
        {
            BindEntry a = All[i];
            if (a.Device != device)
            {
                continue;
            }
            for (int j = i + 1; j < All.Count; j++)
            {
                BindEntry b = All[j];
                if (b.Device != device)
                {
                    continue;
                }
                if (device == EBindDevice.Keyboard && SameKey(a, b))
                {
                    if (MayShare(a, b))
                    {
                        continue;
                    }
                    return $"{KeyLabel(a.Current)} would be both {a.Label} and {b.Label}.";
                }
                string clash = device == EBindDevice.Gamepad ? PadClash(a, b) : KeyClash(a, b) ?? KeyClash(b, a);
                if (clash != null)
                {
                    return clash;
                }
            }
        }
        return null;
    }

    private static string PadClash(BindEntry a, BindEntry b)
    {
        return (a.Pages & b.Pages) != 0 && a.Current == b.Current
            ? $"{PadLabel(a.Current)} would be both {a.Label} and {b.Label}."
            : null;
    }

    /// <summary>
    /// Sprint and Look with the mouse share Left Shift by design: one walks, the other turns with a panel open. They are
    /// the one pair that may share a key; any other two actions on one key are refused.
    /// </summary>
    private static bool MayShare(BindEntry a, BindEntry b)
    {
        return (a.Id == "Keys.World/Sprint" && b.Id == "Keys.World/MouseLook") || (a.Id == "Keys.World/MouseLook" && b.Id == "Keys.World/Sprint");
    }

    /// <summary>True when another keyboard action is on the same key as this one: Sprint and Look with the mouse, as they start.</summary>
    private static bool Shares(BindEntry e)
    {
        foreach (BindEntry x in All)
        {
            if (x != e && x.Device == EBindDevice.Keyboard && SameKey(x, e))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Two keyboard bindings on the same key: plain keys that overlap (ctrl and leftCtrl), or the same chord.</summary>
    private static bool SameKey(BindEntry a, BindEntry b)
    {
        if (string.IsNullOrEmpty(a.Current) || string.IsNullOrEmpty(b.Current))
        {
            return false;
        }
        Split(a.Current, out string ma, out string ka);
        Split(b.Current, out string mb, out string kb);
        if (IsMouse(ka) != IsMouse(kb) || ma != mb)
        {
            return false;
        }
        return ma == null ? KeysOverlap(ControlName(ka), ControlName(kb)) : ka == kb;
    }

    private static string KeyClash(BindEntry a, BindEntry b)
    {
        if (string.IsNullOrEmpty(a.Current) || string.IsNullOrEmpty(b.Current))
        {
            return null;
        }
        Split(a.Current, out string ma, out string ka);
        Split(b.Current, out string mb, out string kb);
        string na = ControlName(ka);
        string nb = ControlName(kb);
        bool sameDevice = IsMouse(ka) == IsMouse(kb);
        // two plain keys or two chords: the same key is counted by Clash(), against the defaults' pairs
        if ((ma == null) == (mb == null))
        {
            return null;
        }
        // a is a chord, b a plain key
        if (ma == null)
        {
            return null;
        }
        if (b.Kind == EBindKind.Held && sameDevice && KeysOverlap(na, nb))
        {
            // Ctrl+W, W being held to walk
            return "No modifier possible.";
        }
        if (b.Kind == EBindKind.Press && !IsMouse(kb) && ModifierOf(nb) == ma)
        {
            // the user's wording, 2 November 2026
            return $"{KeyLabel("<Keyboard>/" + ma)} is already in use as a single key.";
        }
        return null;
    }

    // ---------------------------------------------------------------- the swap

    /// <summary>
    /// Binds an entry to a key or button, swapping with whatever held it. Returns the line to show: what moved, or
    /// why nothing did. <paramref name="actualOrKept"/> is the pressed control as the game reads it; the pad's is
    /// turned into its kept form here.
    /// </summary>
    public static string Bind(BindEntry e, string actualOrKept, out EBindResult result)
    {
        EnsureLoaded();
        result = EBindResult.Refused;
        string binding = e.Device == EBindDevice.Gamepad ? Kept(actualOrKept) : actualOrKept;
        string refused = Refusal(e, binding);
        if (refused != null)
        {
            return refused;
        }
        if (binding == e.Current)
        {
            result = EBindResult.Unchanged;
            return e.Device == EBindDevice.Gamepad
                ? $"{e.Label} is already on {PadLabel(binding)}."
                : $"{e.Label} is already {KeyLabel(binding)}.";
        }

        Dictionary<BindEntry, string> before = new Dictionary<BindEntry, string>();
        foreach (BindEntry x in All)
        {
            before[x] = x.Current;
        }
        string old = e.Current;
        List<BindEntry> moved = e.Device == EBindDevice.Gamepad ? PadMovers(e, binding, old) : KeyMovers(e, binding);
        // an action that shares its key - Sprint or Look with the mouse while both are on Shift - moves only to a free key:
        // a swap would hand the shared key to a third action (the user's rule, 2 November 2026)
        if (e.Device == EBindDevice.Keyboard && moved.Count > 1 && Shares(e))
        {
            return "Please select a free key.";
        }
        foreach (BindEntry m in moved)
        {
            string next = m == e ? binding : e.Device == EBindDevice.Gamepad ? (m.Current == old ? binding : old) : old;
            string why = m == e ? null : Allowed(m, next);
            if (why != null)
            {
                Restore(before);
                return $"{m.Label} would take {Label(e.Device, next)}: {why}";
            }
            m.Current = next;
        }
        string clash = Clash(e.Device);
        if (clash != null)
        {
            Restore(before);
            return clash;
        }
        Apply(e.Device);
        result = EBindResult.Changed;
        string said = Moved(e, binding, old, moved);
        bool warn = e.Device == EBindDevice.Gamepad && moved.Contains(Of(GameActions.PadWorld.Inventory)) && ConversationTakesInventory();
        // on a line of its own, which the map draws in red
        return warn ? said + "\n" + ConversationWarning() : said;
    }

    /// <summary>
    /// True when the button that opens the pack is one a conversation chooses its replies with: silent there (fix 459),
    /// it cannot open the pack for a barter, which the player is told when binding it (the user's answer aa, 11 November 2026).
    /// </summary>
    private static bool ConversationTakesInventory()
    {
        BindEntry e = Of(GameActions.PadWorld.Inventory);
        return e != null && Array.IndexOf(ConversationButtons, Actual(e.Current)) >= 0;
    }

    private static string ConversationWarning()
    {
        string button = PadLabel(Of(GameActions.PadWorld.Inventory).Current);
        return $"In conversations {button} picks the replies: it cannot open the inventory there.";
    }

    /// <summary>
    /// Takes a keyboard action's key away: Delete on its row, after a Yes (the user's, 5 November 2026). Nothing else
    /// moves; the pad's buttons are never taken away.
    /// </summary>
    public static string Unbind(BindEntry e, out EBindResult result)
    {
        EnsureLoaded();
        if (e.Device != EBindDevice.Keyboard)
        {
            result = EBindResult.Refused;
            return "Every pad button keeps an action.";
        }
        if (e.Current.Length == 0)
        {
            result = EBindResult.Unchanged;
            return $"{e.Label} has no key already.";
        }
        e.Current = "";
        Apply(EBindDevice.Keyboard);
        result = EBindResult.Changed;
        return $"{e.Label}: no key.";
    }

    private static void Restore(Dictionary<BindEntry, string> before)
    {
        foreach (KeyValuePair<BindEntry, string> kv in before)
        {
            kv.Key.Current = kv.Value;
        }
    }

    private static List<BindEntry> KeyMovers(BindEntry e, string binding)
    {
        List<BindEntry> movers = new List<BindEntry> { e };
        Split(binding, out string mb, out string kb);
        foreach (BindEntry x in All)
        {
            if (x == e || x.Device != EBindDevice.Keyboard || MayShare(x, e))
            {
                continue;
            }
            Split(x.Current, out string mx, out string kx);
            bool same = mx == mb && IsMouse(kx) == IsMouse(kb) && (mb == null ? KeysOverlap(ControlName(kx), ControlName(kb)) : kx == kb);
            if (same)
            {
                movers.Add(x);
            }
        }
        return movers;
    }

    /// <summary>
    /// The entries a swap of two pad buttons moves: on the entry's pages, and on every page that an entry it moves is
    /// live on, every entry on either button.
    /// </summary>
    private static List<BindEntry> PadMovers(BindEntry e, string binding, string old)
    {
        EPadPages pages = e.Pages;
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (BindEntry x in All)
            {
                if (x.Device == EBindDevice.Gamepad && (x.Pages & pages) != 0 && (x.Current == binding || x.Current == old)
                    && (x.Pages | pages) != pages)
                {
                    pages |= x.Pages;
                    grew = true;
                }
            }
        }
        List<BindEntry> movers = new List<BindEntry> { e };
        foreach (BindEntry x in All)
        {
            if (x != e && x.Device == EBindDevice.Gamepad && (x.Pages & pages) != 0 && (x.Current == binding || x.Current == old))
            {
                movers.Add(x);
            }
        }
        return movers;
    }

    private static string Moved(BindEntry e, string binding, string old, List<BindEntry> moved)
    {
        if (e.Device == EBindDevice.Keyboard)
        {
            StringBuilder sb = new StringBuilder($"{e.Label}: {KeyLabel(binding)}.");
            foreach (BindEntry m in moved)
            {
                if (m != e)
                {
                    sb.Append($" {m.Label}: {KeyLabel(old)}.");
                }
            }
            return sb.ToString();
        }
        if (moved.Count == 1)
        {
            return $"{e.Label}: {PadLabel(binding)}.";
        }
        EPadPages pages = EPadPages.None;
        foreach (BindEntry m in moved)
        {
            pages |= m.Pages;
        }
        List<string> names = new List<string>();
        if ((pages & EPadPages.Walking) != 0) names.Add("WALKING");
        if ((pages & EPadPages.Inventory) != 0) names.Add("INVENTORY");
        if ((pages & EPadPages.Magic) != 0) names.Add("MAGIC");
        if ((pages & EPadPages.Map) != 0) names.Add("MAP");
        string where = names.Count == 1 ? names[0] : string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1];
        return $"{PadLabel(binding)} and {PadLabel(old)} swapped on {where}.";
    }

    // ---------------------------------------------------------------- reset and undo

    public static bool AnyChanged(params EOptionSection[] sections)
    {
        EnsureLoaded();
        foreach (BindEntry e in All)
        {
            if (!e.IsDefault && Array.IndexOf(sections, SectionOf(e.Device)) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    public static void Reset(params EOptionSection[] sections)
    {
        EnsureLoaded();
        foreach (EBindDevice d in new[] { EBindDevice.Keyboard, EBindDevice.Gamepad })
        {
            if (Array.IndexOf(sections, SectionOf(d)) >= 0)
            {
                ResetDevice(d);
                Apply(d);
            }
        }
    }

    private static void ResetDevice(EBindDevice device)
    {
        foreach (BindEntry e in All)
        {
            if (e.Device == device)
            {
                e.Current = e.Default;
            }
        }
    }

    /// <summary>
    /// The Classic keys (mod 1080): the original's keyboard from its reference card (uw1data, page 6). The card's
    /// actions on the card's keys; the remake's own conveniences on their usual keys where the card leaves them free;
    /// what the card takes and leaves no place for - the magic panel (Q, a right click on the rune bag instead) and
    /// Cancel a charge (X) - with no key, and Inventory and Stats too, whose one key on the card is F7, the
    /// character panel turned over, and Walk quietly, whose slow walk is S's (the user's answers of 4 November 2026).
    /// Kept by physical key, like every binding.
    /// </summary>
    private static readonly string[,] ClassicKeys =
    {
        { "Keys.World/Move/Up", "<Keyboard>/w" },
        { "Keys.World/Move/Down", "<Keyboard>/x" },
        { "Keys.World/Move/Left", "<Keyboard>/z" },
        { "Keys.World/Move/Right", "<Keyboard>/c" },
        { "Keys.World/TurnLeft", "<Keyboard>/a" },
        { "Keys.World/TurnRight", "<Keyboard>/d" },
        { "Keys.World/WalkSlowly", "<Keyboard>/s" },
        { "Keys.World/Sprint", "<Keyboard>/leftShift" },
        { "Keys.World/WalkQuietly", "" },
        { "Keys.World/MouseLook", "<Keyboard>/leftShift" },
        { "Keys.World/CancelCharge", "" },
        { "Keys.World/Jump", "<Keyboard>/j" },
        { "Keys.World/LongJump", "shift+<Keyboard>/j" },
        { "Keys.World/Use", "<Keyboard>/f" },
        { "Keys.World/Cast", "<Keyboard>/f8" },
        { "Keys.World/MagicPanel", "" },
        { "Keys.World/Inventory", "" },
        { "Keys.World/Stats", "" },
        { "Keys.World/CharacterPanel", "<Keyboard>/f7" },
        { "Keys.World/Map", "<Keyboard>/tab" },
        { "Keys.World/Quicksave", "<Keyboard>/f5" },
        { "Keys.World/Sleep", "<Keyboard>/f10" },
        { "Keys.World/Track", "<Keyboard>/f9" },
        { "Keys.World/MessagesBack", "<Keyboard>/pageUp" },
        { "Keys.World/MessagesForward", "<Keyboard>/pageDown" },
        { "Keys.World/Controls", "<Keyboard>/f1" },
        { "Keys.World/LookDown", "<Keyboard>/1" },
        { "Keys.World/LookAhead", "<Keyboard>/2" },
        { "Keys.World/LookUp", "<Keyboard>/3" },
        { "Keys.World/FlyUp", "<Keyboard>/e" },
        { "Keys.World/FlyDown", "<Keyboard>/q" },
    };

    private static string ClassicOf(BindEntry e)
    {
        for (int i = 0; i < ClassicKeys.GetLength(0); i++)
        {
            if (ClassicKeys[i, 0] == e.Id)
            {
                return ClassicKeys[i, 1];
            }
        }
        // an action the table does not name keeps its default
        return e.Default;
    }

    /// <summary>True when every key is the Classic keys' one: the button that sets them is dimmed.</summary>
    public static bool IsClassic()
    {
        EnsureLoaded();
        foreach (BindEntry e in All)
        {
            if (e.Device == EBindDevice.Keyboard && e.Current != ClassicOf(e))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Sets the whole keyboard to the Classic keys at once, through the same checks as a swap.</summary>
    public static void SetClassic()
    {
        EnsureLoaded();
        Dictionary<BindEntry, string> before = new Dictionary<BindEntry, string>();
        foreach (BindEntry e in All)
        {
            before[e] = e.Current;
        }
        string problem = null;
        foreach (BindEntry e in All)
        {
            if (e.Device == EBindDevice.Keyboard)
            {
                string classic = ClassicOf(e);
                problem = problem ?? Allowed(e, classic);
                e.Current = classic;
            }
        }
        problem = problem ?? Clash(EBindDevice.Keyboard);
        if (problem != null)
        {
            Debug.LogError($"[GameBindings] the Classic keys do not check out ({problem}): left as they were.");
            Restore(before);
            return;
        }
        Apply(EBindDevice.Keyboard);
    }

    private static EOptionSection SectionOf(EBindDevice d) => d == EBindDevice.Keyboard ? EOptionSection.Keyboard : EOptionSection.Gamepad;

    /// <summary>Every binding as it stands, kept when the menu opens so that Undo changes can go back to it.</summary>
    public sealed class Snapshot
    {
        private readonly Dictionary<BindEntry, string> kept = new Dictionary<BindEntry, string>();

        public Snapshot()
        {
            EnsureLoaded();
            foreach (BindEntry e in All)
            {
                kept[e] = e.Current;
            }
        }

        public bool Differs()
        {
            foreach (KeyValuePair<BindEntry, string> kv in kept)
            {
                if (kv.Key.Current != kv.Value)
                {
                    return true;
                }
            }
            return false;
        }

        public void Restore()
        {
            if (!Differs())
            {
                return;
            }
            GameBindings.Restore(kept);
            Apply(EBindDevice.Keyboard);
            Apply(EBindDevice.Gamepad);
        }
    }

    // ---------------------------------------------------------------- names, for the maps and the texts

    private static string Label(EBindDevice device, string binding) => device == EBindDevice.Gamepad ? PadLabel(binding) : KeyLabel(binding);

    /// <summary>A keyboard binding's name as the map writes it: "J", "Ctrl+J", "Mouse 4".</summary>
    public static string KeyLabel(string binding)
    {
        if (string.IsNullOrEmpty(binding))
        {
            return "no key";
        }
        Split(binding, out string modifier, out string key);
        string name = ControlsMap.KeyLabel(ControlName(key), IsMouse(key));
        return modifier != null ? ControlsMap.KeyLabel(modifier, false) + "+" + name : name;
    }

    /// <summary>A pad binding's name, as the game reads it now and as the pad in use prints it: "A", "RB", "L3", "D-pad ↑".</summary>
    public static string PadLabel(string kept)
    {
        string name = ControlName(Actual(kept));
        bool ps = PadGlyphs.PlayStation;
        switch (name)
        {
            case "buttonSouth": return ps ? "Cross" : "A";
            case "buttonEast": return ps ? "Circle" : "B";
            case "buttonWest": return ps ? "Square" : "X";
            case "buttonNorth": return ps ? "Triangle" : "Y";
            case "leftShoulder": return PadGlyphs.Name("LB");
            case "rightShoulder": return PadGlyphs.Name("RB");
            case "leftTrigger": return PadGlyphs.Name("LT");
            case "rightTrigger": return PadGlyphs.Name("RT");
            case "select": return PadGlyphs.Name("Select");
            case "start": return PadGlyphs.Name("Start");
            case "leftStickPress": return "L3";
            case "rightStickPress": return "R3";
            case "leftStick": return "the left stick";
            case "rightStick": return "the right stick";
            case "dpad/up": return "D-pad ↑";
            case "dpad/down": return "D-pad ↓";
            case "dpad/left": return "D-pad ←";
            case "dpad/right": return "D-pad →";
            default: return name;
        }
    }

    /// <summary>The key an action is bound to, for a line of text: "E", "Ctrl+J", "W" for one part of the walking keys.</summary>
    public static string KeyText(InputAction action, string part = null)
    {
        BindEntry e = Of(action, part);
        return e != null ? KeyLabel(e.Current) : "?";
    }

    /// <summary>True when a keyboard action has a key: false for one the player took away or that starts with none.</summary>
    public static bool IsBound(InputAction action, string part = null)
    {
        BindEntry e = Of(action, part);
        return e != null && !string.IsNullOrEmpty(e.Current);
    }

    /// <summary>
    /// The pad control an action is bound to, as a sentence names it: "A", "the right shoulder button", "the left
    /// trigger", "the left stick" - with the PlayStation pad's names when those are shown.
    /// </summary>
    public static string PadText(InputAction action)
    {
        BindEntry e = Of(action);
        if (e == null)
        {
            return "?";
        }
        string name = ControlName(Actual(e.Current));
        bool ps = PadGlyphs.PlayStation;
        switch (name)
        {
            case "leftShoulder": return ps ? "L1" : "the left shoulder button";
            case "rightShoulder": return ps ? "R1" : "the right shoulder button";
            case "leftTrigger": return ps ? "L2" : "the left trigger";
            case "rightTrigger": return ps ? "R2" : "the right trigger";
            case "leftStickPress": return "the left stick's click";
            case "rightStickPress": return "the right stick's click";
            case "dpad/up": return "up on the d-pad";
            case "dpad/down": return "down on the d-pad";
            case "dpad/left": return "left on the d-pad";
            case "dpad/right": return "right on the d-pad";
            default: return PadLabel(e.Current);
        }
    }

    /// <summary>The short name of the pad control an action is bound to: "A", "RB", "L3".</summary>
    public static string PadName(InputAction action)
    {
        BindEntry e = Of(action);
        return e != null ? PadLabel(e.Current) : "?";
    }

    /// <summary>The pad control an action is bound to now, as the game reads it: "&lt;Gamepad&gt;/leftTrigger"...</summary>
    public static string PadPath(InputAction action)
    {
        BindEntry e = Of(action);
        return e != null ? Actual(e.Current) : null;
    }
}
