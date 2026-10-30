using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The game's input actions: every key and button the game reads during play, by name, so that they can be
/// rebound. One action map per context and per device - the pad and the keyboard each have their own, because
/// the code reads the two apart and a player rebinds one device at a time.
/// </summary>
/// <remarks>
/// Built in code at start-up rather than in an .inputactions asset: the same objects either way, and here every
/// change is a readable diff. <see cref="InputActionAsset.ToJson"/> writes the asset out if one is ever wanted.
///
/// What is not here, on purpose: the mouse (its buttons, wheel and pointer point at things, they are not bindings),
/// Esc and Start, which must always reach the menus, the menus themselves, and text entry - the on-screen keyboard,
/// typing runes, the flute's notes, conversation replies - which reads characters and stays on IMGUI events.
///
/// Every map is enabled all the time and the contexts are still decided where each action is read, by the control
/// masks and <see cref="GameInput.LastActiveDevice"/>, exactly as before the actions existed.
/// </remarks>
public static class GameActions
{
    /// <summary>Binding groups of the two triggers on the pad, which swap with the character's weapon hand.</summary>
    public const string RightHandedGroup = "RightHanded";
    public const string LeftHandedGroup = "LeftHanded";

    public static readonly InputActionAsset Asset = CreateAsset();

    public static readonly PadWorldActions PadWorld = new PadWorldActions(Asset.AddActionMap("Pad.World"));
    public static readonly KeysWorldActions KeysWorld = new KeysWorldActions(Asset.AddActionMap("Keys.World"));
    public static readonly PadInventoryActions PadInventory = new PadInventoryActions(Asset.AddActionMap("Pad.Inventory"));
    public static readonly CursorActions KeysInventory = new CursorActions(Asset.AddActionMap("Keys.Inventory"), keyboard: true);
    public static readonly PadMagicActions PadMagic = new PadMagicActions(Asset.AddActionMap("Pad.Magic"));
    public static readonly CursorActions KeysMagic = new CursorActions(Asset.AddActionMap("Keys.Magic"), keyboard: true);
    public static readonly PadMapActions PadMap = new PadMapActions(Asset.AddActionMap("Pad.Map"));
    public static readonly KeysMapActions KeysMap = new KeysMapActions(Asset.AddActionMap("Keys.Map"));

    private static bool? appliedLeftHanded;

    static GameActions()
    {
        Asset.Enable();
        ApplyHandedness(false);
    }

    private static InputActionAsset CreateAsset()
    {
        InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
        asset.name = "GameActions";
        return asset;
    }

    /// <summary>
    /// Called once a frame, before anything reads input (<see cref="InputDeviceRouter"/>): follows the character's
    /// weapon hand onto the triggers. A left-handed character attacks with the left trigger and pins the stats with
    /// the right one, as <see cref="PlayerData.leftHanded"/> has always decided.
    /// </summary>
    public static void Refresh()
    {
        if (PlayerData.sData != null)
        {
            ApplyHandedness(PlayerData.sData.leftHanded);
        }
    }

    private static void ApplyHandedness(bool leftHanded)
    {
        if (appliedLeftHanded == leftHanded)
        {
            return;
        }

        appliedLeftHanded = leftHanded;
        InputBinding mask = InputBinding.MaskByGroup(leftHanded ? LeftHandedGroup : RightHandedGroup);
        PadWorld.Attack.bindingMask = mask;
        PadWorld.Stats.bindingMask = mask;
    }

    private static InputAction Button(InputActionMap map, string name, string binding)
    {
        return map.AddAction(name, InputActionType.Button, binding);
    }

    private static InputAction Stick(InputActionMap map, string name, string binding)
    {
        return map.AddAction(name, InputActionType.Value, binding, expectedControlLayout: "Vector2");
    }

    /// <summary>W, S, A and D, or the arrows, as one vector: each axis -1, 0 or 1, not normalised.</summary>
    private static InputAction KeyVector(InputActionMap map, string name, string up, string down, string left, string right)
    {
        InputAction action = map.AddAction(name, InputActionType.Value, expectedControlLayout: "Vector2");
        action.AddCompositeBinding("2DVector(mode=1)")
            .With("Up", up)
            .With("Down", down)
            .With("Left", left)
            .With("Right", right);
        return action;
    }

    /// <summary>Walking about: the pad.</summary>
    public sealed class PadWorldActions
    {
        public readonly InputAction Move;
        public readonly InputAction Look;
        public readonly InputAction Sprint;
        public readonly InputAction Jump;
        public readonly InputAction LookAt;
        public readonly InputAction Use;
        /// <summary>Cancels a weapon charge and drops the item in hand: one button, both at once.</summary>
        public readonly InputAction Cancel;
        public readonly InputAction Attack;
        /// <summary>A value, not a button: the stats panel pins on its own threshold of the trigger.</summary>
        public readonly InputAction Stats;
        public readonly InputAction MagicPanel;
        public readonly InputAction Inventory;
        public readonly InputAction Map;
        /// <summary>Held, not pressed: the caller times the hold.</summary>
        public readonly InputAction Quicksave;
        /// <summary>Held, not pressed: the caller times the hold.</summary>
        public readonly InputAction Sleep;
        public readonly InputAction MessagesBack;
        public readonly InputAction MessagesForward;

        internal PadWorldActions(InputActionMap map)
        {
            Move = Stick(map, "Move", "<Gamepad>/leftStick");
            Look = Stick(map, "Look", "<Gamepad>/rightStick");
            Sprint = Button(map, "Sprint", "<Gamepad>/leftStickPress");
            Jump = Button(map, "Jump", "<Gamepad>/buttonSouth");
            LookAt = Button(map, "LookAt", "<Gamepad>/buttonWest");
            Use = Button(map, "Use", "<Gamepad>/buttonNorth");
            Cancel = Button(map, "Cancel", "<Gamepad>/buttonEast");

            Attack = map.AddAction("Attack", InputActionType.Button);
            Attack.AddBinding("<Gamepad>/rightTrigger", groups: RightHandedGroup);
            Attack.AddBinding("<Gamepad>/leftTrigger", groups: LeftHandedGroup);
            Stats = map.AddAction("Stats", InputActionType.Value, expectedControlLayout: "Axis");
            Stats.AddBinding("<Gamepad>/leftTrigger", groups: RightHandedGroup);
            Stats.AddBinding("<Gamepad>/rightTrigger", groups: LeftHandedGroup);

            MagicPanel = Button(map, "MagicPanel", "<Gamepad>/leftShoulder");
            Inventory = Button(map, "Inventory", "<Gamepad>/rightShoulder");
            Map = Button(map, "Map", "<Gamepad>/select");
            Quicksave = Button(map, "Quicksave", "<Gamepad>/rightStickPress");
            Sleep = Button(map, "Sleep", "<Gamepad>/dpad/left");
            MessagesBack = Button(map, "MessagesBack", "<Gamepad>/dpad/up");
            MessagesForward = Button(map, "MessagesForward", "<Gamepad>/dpad/down");
        }
    }

    /// <summary>Walking about: the keyboard. The mouse is read directly (see the remarks above).</summary>
    public sealed class KeysWorldActions
    {
        /// <summary>W, S, A, D. The only movement keys while the magic or inventory panel is open.</summary>
        public readonly InputAction Move;
        /// <summary>The arrows, which belong to the panels' cursors while a panel is open.</summary>
        public readonly InputAction MoveAlternate;
        public readonly InputAction Sprint;
        public readonly InputAction WalkQuietly;
        /// <summary>Held with a panel open or a spell waiting for its target: the mouse turns the view.</summary>
        public readonly InputAction MouseLook;
        public readonly InputAction Jump;
        public readonly InputAction Use;
        public readonly InputAction CancelCharge;
        public readonly InputAction Cast;
        public readonly InputAction MagicPanel;
        public readonly InputAction Inventory;
        public readonly InputAction Stats;
        public readonly InputAction Map;
        public readonly InputAction Quicksave;
        public readonly InputAction Sleep;
        public readonly InputAction MessagesBack;
        public readonly InputAction MessagesForward;

        internal KeysWorldActions(InputActionMap map)
        {
            Move = KeyVector(map, "Move", "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d");
            MoveAlternate = KeyVector(map, "MoveAlternate",
                "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow");
            Sprint = Button(map, "Sprint", "<Keyboard>/leftShift");
            WalkQuietly = Button(map, "WalkQuietly", "<Keyboard>/ctrl");
            MouseLook = Button(map, "MouseLook", "<Keyboard>/leftShift");
            Jump = Button(map, "Jump", "<Keyboard>/space");
            Use = Button(map, "Use", "<Keyboard>/f");
            CancelCharge = Button(map, "CancelCharge", "<Keyboard>/x");
            Cast = Button(map, "Cast", "<Keyboard>/c");
            MagicPanel = Button(map, "MagicPanel", "<Keyboard>/q");
            Inventory = Button(map, "Inventory", "<Keyboard>/e");
            Stats = Button(map, "Stats", "<Keyboard>/r");
            Map = Button(map, "Map", "<Keyboard>/tab");
            Quicksave = Button(map, "Quicksave", "<Keyboard>/f5");
            Sleep = Button(map, "Sleep", "<Keyboard>/f10");
            MessagesBack = Button(map, "MessagesBack", "<Keyboard>/pageUp");
            MessagesForward = Button(map, "MessagesForward", "<Keyboard>/pageDown");
        }
    }

    /// <summary>A cursor moved a cell at a time: the d-pad on the pad, the arrows on the keyboard.</summary>
    public class CursorActions
    {
        public readonly InputAction Up;
        public readonly InputAction Down;
        public readonly InputAction Left;
        public readonly InputAction Right;

        internal CursorActions(InputActionMap map, bool keyboard)
        {
            Up = Button(map, "Up", keyboard ? "<Keyboard>/upArrow" : "<Gamepad>/dpad/up");
            Down = Button(map, "Down", keyboard ? "<Keyboard>/downArrow" : "<Gamepad>/dpad/down");
            Left = Button(map, "Left", keyboard ? "<Keyboard>/leftArrow" : "<Gamepad>/dpad/left");
            Right = Button(map, "Right", keyboard ? "<Keyboard>/rightArrow" : "<Gamepad>/dpad/right");
        }
    }

    /// <summary>The inventory panel and the barter tray on the pad. The keyboard only moves the cursor there.</summary>
    public sealed class PadInventoryActions : CursorActions
    {
        /// <summary>Tapped, looks at the item or uses it; held, picks it up. The caller tells the two apart.</summary>
        public readonly InputAction Select;
        /// <summary>Equip, eat, unequip, offer in a trade; held on the pack, the caller times it.</summary>
        public readonly InputAction SecondaryUse;
        /// <summary>Held and released: throws the item, or repairs it at an anvil.</summary>
        public readonly InputAction Throw;
        public readonly InputAction Back;

        internal PadInventoryActions(InputActionMap map) : base(map, keyboard: false)
        {
            Select = Button(map, "Select", "<Gamepad>/buttonSouth");
            SecondaryUse = Button(map, "SecondaryUse", "<Gamepad>/buttonNorth");
            Throw = Button(map, "Throw", "<Gamepad>/buttonWest");
            Back = Button(map, "Back", "<Gamepad>/buttonEast");
        }
    }

    /// <summary>The magic panel on the pad. The keyboard moves the cursor with the arrows and casts with Cast.</summary>
    public sealed class PadMagicActions : CursorActions
    {
        /// <summary>Lays the rune under the cursor, or dispels the active spell under it.</summary>
        public readonly InputAction Select;
        public readonly InputAction Clear;
        public readonly InputAction Cast;

        internal PadMagicActions(InputActionMap map) : base(map, keyboard: false)
        {
            Select = Button(map, "Select", "<Gamepad>/buttonSouth");
            Clear = Button(map, "Clear", "<Gamepad>/buttonEast");
            Cast = Button(map, "Cast", "<Gamepad>/buttonWest");
        }
    }

    /// <summary>The map on the pad. Esc and the mouse work it from the keyboard side.</summary>
    public sealed class PadMapActions
    {
        /// <summary>
        /// The left stick's two axes apart, as the map has always read them: without the stick's dead zone, which
        /// the quill's own curve and its whole-pixel steps make up for.
        /// </summary>
        public readonly InputAction QuillX;
        public readonly InputAction QuillY;
        /// <summary>Places a note, edits the one under the quill, or presses the close button.</summary>
        public readonly InputAction Select;
        public readonly InputAction EraseNote;
        public readonly InputAction PreviousPage;
        public readonly InputAction NextPage;
        public readonly InputAction Close;
        /// <summary>Closes the map when released.</summary>
        public readonly InputAction Back;

        internal PadMapActions(InputActionMap map)
        {
            QuillX = map.AddAction("QuillX", InputActionType.Value, "<Gamepad>/leftStick/x", expectedControlLayout: "Axis");
            QuillY = map.AddAction("QuillY", InputActionType.Value, "<Gamepad>/leftStick/y", expectedControlLayout: "Axis");
            Select = Button(map, "Select", "<Gamepad>/buttonSouth");
            EraseNote = Button(map, "EraseNote", "<Gamepad>/buttonWest");
            PreviousPage = Button(map, "PreviousPage", "<Gamepad>/dpad/up");
            NextPage = Button(map, "NextPage", "<Gamepad>/dpad/down");
            Close = Button(map, "Close", "<Gamepad>/select");
            Back = Button(map, "Back", "<Gamepad>/buttonEast");
        }
    }

    /// <summary>The map on the keyboard.</summary>
    public sealed class KeysMapActions
    {
        public readonly InputAction Close;

        internal KeysMapActions(InputActionMap map)
        {
            Close = Button(map, "Close", "<Keyboard>/tab");
        }
    }
}
