using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// The two control maps of the game menu's CONTROLS tab: the keyboard drawn with the keys of the selected action
/// lit, beside the list of actions, and the pad drawn with a callout for every button, one page per context.
/// </summary>
/// <remarks>
/// Both are drawn from the bindings of <see cref="GameActions"/> each time, so they say what the game reads, not
/// what a text once said. What the actions do not hold - the mouse, Esc and Start, text entry, the menus' own
/// buttons - is written here as fixed lines. Laid out in reference pixels and drawn through the menu's own
/// drawing calls, so it has the menu's look and scale.
/// </remarks>
public sealed class ControlsMap
{
    /// <summary>The map of the device used last: what F1 opens.</summary>
    public static EOptionSection ForLastDevice() =>
        GameInput.LastActiveDevice == GameInputDevice.Gamepad ? EOptionSection.Gamepad : EOptionSection.Keyboard;

    /// <summary>The sections of CONTROLS that are maps rather than rows of settings.</summary>
    public static bool Shows(EOptionSection section) => section == EOptionSection.Keyboard || section == EOptionSection.Gamepad;

    private const float Font = GameMenu.PageSmallFont;
    private const float KeyRowHeight = 44f;
    private const float LineHeight = 30f;
    private const float GlyphSize = 36f;

    // The pad's labels: the sizes they are drawn at, the first of PadSizes whose labels fit beside the pad (at 768
    // lines eight callouts on one side do not fit at the menu's own size).
    private static readonly float[,] PadSizes = { { GameMenu.PageSmallFont, LineHeight, GlyphSize }, { 24f, 28f, 34f }, { 22f, 26f, 32f } };
    private float padFont = GameMenu.PageSmallFont;
    private float padLine = LineHeight;
    private float padGlyph = GlyphSize;
    private const float ChipHeight = 46f;
    // Select's and Start's leader lines rise at this angle from the horizontal before they run across (the user's 45
    // degrees of 1 November 2026, made 40 the same day)
    private const float UnderShoulderDegrees = 40f;
    // the pad drawn this much lower than the middle of its space, on every page and both pads, so the leader lines reach
    // it at a slant and none runs level across another's end; the labels stay where the pad's middle place puts them
    // (the user's, 1 November 2026)
    private const float PadDrop = 5f;
    private const int NoteLines = 2;
    private const float NoteLineHeight = 28f;

    private readonly GameMenu menu;

    public ControlsMap(GameMenu menu)
    {
        this.menu = menu;
    }

    // ---------------------------------------------------------------- the keyboard

    private sealed class KeyRow
    {
        public string Header;
        public string Label;
        /// <summary>The binding the row shows and rebinds (GameBindings), or null for a row that is always the same.</summary>
        public string EntryId;
        /// <summary>Keys always shown beside the binding's, or all of a fixed row's: the arrows of the four moves.</summary>
        public Func<List<string>> Keys;
        /// <summary>Written in place of the keys' names, for a run like 1 to 9.</summary>
        public string KeysText;
        public string Note;

        public BindEntry Entry => EntryId != null ? GameBindings.Find(EntryId) : null;
    }

    // The sections, the user's of 4 November 2026 (mod 1080): MOVEMENT, LOOKING, GAMEPLAY, PANELS, GENERAL.
    private static readonly KeyRow[] KeyRows =
    {
        new KeyRow { Header = "MOVEMENT" },
        new KeyRow { Label = "Move forward", EntryId = "Keys.World/Move/Up", Keys = () => Bound(GameActions.KeysWorld.MoveAlternate, "Up") },
        new KeyRow { Label = "Move back", EntryId = "Keys.World/Move/Down", Keys = () => Bound(GameActions.KeysWorld.MoveAlternate, "Down") },
        new KeyRow { Label = "Move left", EntryId = "Keys.World/Move/Left", Keys = () => Bound(GameActions.KeysWorld.MoveAlternate, "Left") },
        new KeyRow { Label = "Move right", EntryId = "Keys.World/Move/Right", Keys = () => Bound(GameActions.KeysWorld.MoveAlternate, "Right") },
        new KeyRow { Label = "Turn left", EntryId = "Keys.World/TurnLeft" },
        new KeyRow { Label = "Turn right", EntryId = "Keys.World/TurnRight" },
        new KeyRow { Label = "Move forward quietly", EntryId = "Keys.World/WalkSlowly", Note = "Hold: forward, slowly and quietly." },
        new KeyRow { Label = "Walk quietly modifier", EntryId = "Keys.World/WalkQuietly", Note = "Hold with a move key to walk slowly and quietly." },
        new KeyRow { Label = "Sprint modifier", EntryId = "Keys.World/Sprint", Note = "Hold; forward only, and it uses stamina." },
        new KeyRow { Label = "Jump", EntryId = "Keys.World/Jump" },
        new KeyRow { Label = "Standing long jump", EntryId = "Keys.World/LongJump", Note = "From standing, a leap forward; on the move, a jump." },
        new KeyRow { Label = "Fly up", EntryId = "Keys.World/FlyUp", Note = "Hold, with Fly or Levitate." },
        new KeyRow { Label = "Fly down", EntryId = "Keys.World/FlyDown", Note = "Hold, with Fly or Levitate." },
        new KeyRow { Header = "LOOKING" },
        new KeyRow { Label = "Look down", EntryId = "Keys.World/LookDown", Note = "A step a press, four at most; hold to repeat." },
        new KeyRow { Label = "Look ahead", EntryId = "Keys.World/LookAhead" },
        new KeyRow { Label = "Look up", EntryId = "Keys.World/LookUp", Note = "A step a press, four at most; hold to repeat." },
        new KeyRow { Label = "Look with the mouse modifier", EntryId = "Keys.World/MouseLook", Note = "Hold, with a panel open or a spell waiting for its target." },
        new KeyRow { Header = "GAMEPLAY" },
        new KeyRow { Label = "Use or pick up", EntryId = "Keys.World/Use", Note = "Straight into your pack; with an item on the cursor, put it there." },
        new KeyRow { Label = "Cancel a charge", EntryId = "Keys.World/CancelCharge" },
        new KeyRow { Label = "Sleep", EntryId = "Keys.World/Sleep", Note = "On a bedroll, if there is one in your pack." },
        new KeyRow { Label = "Track", EntryId = "Keys.World/Track", Note = "The creatures nearby, and in which direction." },
        new KeyRow { Label = "Type runes", Keys = () => new List<string> { "enter" }, Note = "Then type the runes' letters; Enter again to cast." },
        // at most five answers in the original's conversations: four in a babl_menu, five in two babl_fmenu (Hewstone,
        // Ishtass on levels 2 and 3, read in CNV.ARK); the digits up to 9 still answer, none has more to answer
        new KeyRow { Label = "Reply in a conversation", Keys = () => Digits(1, 5), KeysText = "1 - 5" },
        // the how-many box's keys (mod 1140): fixed, as the conversation's digits
        new KeyRow
        {
            Label = "Choose how many",
            Keys = () => Join(Digits(0, 9), new List<string> { "leftArrow", "rightArrow", "home", "end" }),
            KeysText = "0 - 9, ← →, Home, End",
            Note = "Type the number or step it; Home for one, End for all; Enter takes them.",
        },
        new KeyRow { Label = "Play an instrument", Keys = () => Digits(0, 9), KeysText = "0 - 9" },
        new KeyRow { Header = "PANELS" },
        new KeyRow { Label = "Magic panel", EntryId = "Keys.World/MagicPanel", Note = "Or a right click on the rune bag in the inventory." },
        new KeyRow { Label = "Cast the spell", EntryId = "Keys.World/Cast" },
        new KeyRow { Label = "Inventory", EntryId = "Keys.World/Inventory" },
        new KeyRow { Label = "Stats", EntryId = "Keys.World/Stats" },
        new KeyRow { Label = "Inventory and stats", EntryId = "Keys.World/CharacterPanel", Note = "The inventory, then the stats, then closed." },
        new KeyRow { Label = "Map", EntryId = "Keys.World/Map" },
        new KeyRow { Label = "Messages back", EntryId = "Keys.World/MessagesBack" },
        new KeyRow { Label = "Messages forward", EntryId = "Keys.World/MessagesForward" },
        new KeyRow { Header = "GENERAL" },
        new KeyRow { Label = "Quicksave", EntryId = "Keys.World/Quicksave" },
        new KeyRow { Label = "Controls", EntryId = "Keys.World/Controls", Note = "Open these maps: the pad's if you used the pad last." },
        new KeyRow { Label = "Game menu", Keys = () => new List<string> { "escape" }, Note = "With a panel open, close it first." },
        new KeyRow
        {
            // the arrows stay the panels' cursor, as they stay a second set of walking keys (mod 320, the user's choice
            // of one key for each action)
            Label = "Move the selection",
            Keys = () => Join(Bound(GameActions.KeysInventory.Up), Bound(GameActions.KeysInventory.Down),
                Bound(GameActions.KeysInventory.Left), Bound(GameActions.KeysInventory.Right)),
            Note = "In the inventory and the magic panel.",
        },
    };

    /// <summary>A row's keys, as names of the drawing's keys: its binding's (a chord as ctrl+j), then the fixed ones.</summary>
    private static List<string> RowKeys(KeyRow row)
    {
        List<string> keys = new List<string>();
        BindEntry e = row.Entry;
        if (e != null && !string.IsNullOrEmpty(e.Binding))
        {
            keys.Add(MapName(e.Binding));
        }
        if (row.Keys != null)
        {
            foreach (string k in row.Keys())
            {
                if (!keys.Contains(k))
                {
                    keys.Add(k);
                }
            }
        }
        return keys;
    }

    /// <summary>
    /// A row's note, ending with the binding's default in parentheses (the user's, 2 November 2026), so that a key
    /// moved by a swap can be found again without a reset.
    /// </summary>
    private static string RowNote(KeyRow row)
    {
        BindEntry e = row.Entry;
        if (e == null)
        {
            return row.Note;
        }
        string d = "(default: " + GameBindings.KeyLabel(e.DefaultBinding) + ")";
        return row.Note != null ? row.Note + " " + d : d;
    }

    /// <summary>A keyboard binding as a name of the drawing: j, ctrl+j, mouse/backButton.</summary>
    private static string MapName(string binding)
    {
        GameBindings.Split(binding, out string modifier, out string key);
        string name = GameBindings.ControlName(key);
        if (key != null && key.StartsWith("<Mouse>/", StringComparison.Ordinal))
        {
            name = "mouse/" + name;
        }
        return modifier != null ? modifier + "+" + name : name;
    }

    /// <summary>True for a key the keyboard map draws: only those can be bound, so a binding can always be shown.</summary>
    public static bool OnDrawing(string control)
    {
        foreach (Cap cap in Caps)
        {
            if (cap.Name == control)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>A key's or a mouse button's name, as the map writes it: for the lines that say what a rebind did.</summary>
    public static string KeyLabel(string control, bool mouse) => KeyName(mouse ? "mouse/" + control : control);

    /// <summary>What the mouse does, which no action holds: it points, it is not bound.</summary>
    private static readonly string[] MouseLines =
    {
        "Left button: click to look at, hold or drag to use or pick up",
        "Right button: hold and release to attack",
        "Right click on the rune bag: the magic panel",
        "Wheel: scroll lists",
    };

    private struct Cap
    {
        public string Name;
        public float X;
        public float Y;
        public float W;
    }

    private const float KeyboardUnitsWide = 18.5f;
    private const float KeyboardUnitsHigh = 6.5f;

    // An ANSI keyboard without the numeric pad, in key widths: the F row, the main block and the block of six with
    // the arrows. Keys are named by the Input System's controls, which are their places, not their letters.
    private static readonly Cap[] Caps = BuildCaps();

    private int keySelected = 1;
    private int keyScroll;
    private bool keyDragging;
    private float keyGrab;
    private const float KeyScrollBarWidth = 14f;
    /// <summary>The selection was moved by the keys or the drawing: the list scrolls it into view once.</summary>
    private bool keyFollow;

    // ---------------------------------------------------------------- the pad

    private enum EPadContext
    {
        Walking,
        Inventory,
        Magic,
        Map,
        Menus,
    }

    private static readonly string[] ContextNames = { "WALKING", "INVENTORY", "MAGIC", "MAP", "MENUS" };

    private struct PadLine
    {
        public EPadControl Control;
        public string Prefix;
        public string Text;
        /// <summary>The binding the line rebinds, or null for a line that is always the same.</summary>
        public BindEntry Entry;
        /// <summary>For a fixed line, why it cannot be changed.</summary>
        public string Why;
    }

    private sealed class Callout
    {
        public EPadControl Control;
        public readonly List<string> Lines = new List<string>();
        /// <summary>Each line's binding, or null where the line is fixed; and why it is.</summary>
        public readonly List<BindEntry> Entries = new List<BindEntry>();
        public readonly List<string> Whys = new List<string>();
        public Vector2 Anchor;
        public float Top;
        public float Height;
        public List<string> Wrapped;
        /// <summary>The line of <see cref="Lines"/> each wrapped line comes from.</summary>
        public List<int> WrappedOwner;
    }

    /// <summary>A place the keys stop at on a pad page: one line of one callout, since a stick holds two.</summary>
    private struct Stop
    {
        public int Callout;
        public int Line;
    }

    private static List<Stop> Stops(List<Callout> callouts)
    {
        List<Stop> stops = new List<Stop>();
        for (int c = 0; c < callouts.Count; c++)
        {
            for (int l = 0; l < callouts[c].Lines.Count; l++)
            {
                stops.Add(new Stop { Callout = c, Line = l });
            }
        }
        return stops;
    }

    private EPadContext context;
    private readonly int[] padSelected = new int[5];

    // ---------------------------------------------------------------- input

    /// <summary>
    /// The keys of a map, from the menu's frame. True when they went past the end of its list, up or down, so that
    /// the menu moves them to its row of buttons.
    /// </summary>
    public bool Update(EOptionSection section, GameMenu.Nav nav)
    {
        if (section == EOptionSection.Keyboard)
        {
            int count = Selectable().Count;
            int at = Selectable().IndexOf(keySelected);
            if (nav.Accept)
            {
                // Enter or A rebinds the row; a click only selected it (the user's, 2 November 2026)
                Rebind(KeyRows[Mathf.Clamp(keySelected, 0, KeyRows.Length - 1)]);
            }
            else if (Keyboard.current != null && Keyboard.current.deleteKey.wasPressedThisFrame)
            {
                AskUnbind(KeyRows[Mathf.Clamp(keySelected, 0, KeyRows.Length - 1)]);
            }
            else if (nav.Down || nav.Up)
            {
                int next = at + (nav.Down ? 1 : -1);
                if (next < 0 || next >= count)
                {
                    return true;
                }
                keySelected = Selectable()[next];
                keyFollow = true;
                notice = null;
                menu.PlaySound(menu.Skin.MoveSound);
            }
            else if (nav.Wheel != 0)
            {
                keyScroll += nav.Wheel;
            }
            return false;
        }

        if (nav.Left || nav.Right)
        {
            int n = ContextNames.Length;
            context = (EPadContext)(((int)context + (nav.Left ? -1 : 1) + n) % n);
            notice = null;
            menu.PlaySound(menu.Skin.MoveSound);
            return false;
        }
        List<Callout> callouts = Callouts(context);
        List<Stop> stops = Stops(callouts);
        int i = Mathf.Clamp(padSelected[(int)context], 0, Mathf.Max(0, stops.Count - 1));
        if (nav.Accept && stops.Count > 0)
        {
            Rebind(callouts[stops[i].Callout], stops[i].Line);
        }
        else if (nav.Down || nav.Up)
        {
            int next = i + (nav.Down ? 1 : -1);
            if (next < 0 || next >= stops.Count)
            {
                return true;
            }
            padSelected[(int)context] = next;
            notice = null;
            menu.PlaySound(menu.Skin.MoveSound);
        }
        return false;
    }

    /// <summary>
    /// The keys come back to a map from the row of buttons under it: going up they land on its last entry, going
    /// down on its first, as on the rows of a section. Landing where they left from made them bounce between that
    /// entry and the buttons, at the top with Up and at the bottom with Down (the user's report of 1 November 2026).
    /// </summary>
    public void EnterFromButtons(EOptionSection section, bool up)
    {
        if (section == EOptionSection.Keyboard)
        {
            List<int> rows = Selectable();
            if (rows.Count > 0)
            {
                keySelected = up ? rows[rows.Count - 1] : rows[0];
                keyFollow = true;
            }
            return;
        }
        padSelected[(int)context] = up ? Mathf.Max(0, Stops(Callouts(context)).Count - 1) : 0;
    }

    // ---------------------------------------------------------------- rebinding (mod 320)

    private BindEntry waiting;
    private int waitFrom;
    // a modifier pressed while waiting: bound on its own if it is let go with nothing pressed with it
    private string pendingModifier;
    private string notice;
    private EBindResult noticeKind;
    private float noticeUntil;

    /// <summary>
    /// How long a line of what was done stays: 3 seconds and 0.04 a character, so a long one is read to its end (the
    /// user's, 11 November 2026).
    /// </summary>
    private static float NoticeSeconds(string text) => 3f + 0.04f * (text?.Length ?? 0);

    private static readonly string[] ModifierKeys = { "leftShift", "rightShift", "leftCtrl", "rightCtrl", "leftAlt", "rightAlt" };

    private static readonly string[] PadButtonNames =
    {
        "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder", "leftTrigger", "rightTrigger",
        "select", "start", "leftStickPress", "rightStickPress", "dpad/up", "dpad/down", "dpad/left", "dpad/right",
    };

    /// <summary>True while a binding waits for its new key or button: the menu hands every key to <see cref="UpdateWait"/>.</summary>
    public bool Waiting => waiting != null;

    private bool NoticeShown => notice != null && Time.unscaledTime < noticeUntil;

    /// <summary>Errors and refusals are written in red (the user's, 2 November 2026).</summary>
    public static readonly Color RefusedColor = new Color32(235, 72, 56, 255);

    // the other lines in white, which reads better than the list's colour (the user's, 2 November 2026)
    private Color NoticeColor => noticeKind == EBindResult.Refused ? RefusedColor : Color.white;

    private void Notice(string text, EBindResult kind)
    {
        notice = text;
        noticeKind = kind;
        noticeUntil = Time.unscaledTime + NoticeSeconds(text);
        if (kind == EBindResult.Refused)
        {
            Ding();
        }
        else
        {
            menu.PlaySound(kind == EBindResult.Changed ? menu.Skin.MakeSound : menu.Skin.MoveSound);
        }
    }

    /// <summary>
    /// The sound of a refusal: the game's own, the one the inventory plays when an item cannot go where it is put
    /// (<see cref="Inventory.fail"/>, also the magic panel's failed cast and the cancel of the how-many and repair
    /// boxes), the user's choice of 2 November 2026 over Windows' beep. Without an inventory - the title screen's
    /// menu - the menu's back sound.
    /// </summary>
    private void Ding()
    {
        AudioClip refusal = Inventory.sInv != null ? Inventory.sInv.fail : null;
        menu.PlaySound(refusal != null ? refusal : menu.Skin.BackSound);
    }

    /// <summary>
    /// The box drawn over the pad's map while it waits for a button (the user's, 2 November 2026): what to press, and
    /// for which action. Null when there is none.
    /// </summary>
    public string WaitBox
    {
        get
        {
            if (waiting == null || waiting.Device != EBindDevice.Gamepad)
            {
                return null;
            }
            return (waiting.Kind == EBindKind.Stick ? "Select a new stick for:\n" : "Select a new button for:\n") + waiting.Label;
        }
    }

    /// <summary>A row of the keyboard asked to be rebound: it waits for a key, or a fixed row says it is fixed.</summary>
    private void Rebind(KeyRow row)
    {
        if (row.Entry != null)
        {
            BeginWait(row.Entry);
        }
        else
        {
            Notice(row.Label + " can't be changed.", EBindResult.Refused);
        }
    }

    /// <summary>
    /// Delete on a row of the keyboard: after a Yes, the action keeps no key (the user's, 5 November 2026), as Delete on
    /// a save asks and deletes it. Not while a row waits for its key, where Delete is a key like any other.
    /// </summary>
    private void AskUnbind(KeyRow row)
    {
        BindEntry e = row.Entry;
        if (e == null)
        {
            Notice(row.Label + " can't be changed.", EBindResult.Refused);
            return;
        }
        if (string.IsNullOrEmpty(e.Binding))
        {
            Notice(GameBindings.Unbind(e, out EBindResult none), none);
            return;
        }
        menu.Ask($"Remove the key of {e.Label}?", () =>
        {
            string line = GameBindings.Unbind(e, out EBindResult result);
            Notice(line, result);
        });
    }

    /// <summary>A line of the pad's map asked to be rebound: it waits for a button, or a fixed line says why.</summary>
    private void Rebind(Callout c, int line)
    {
        BindEntry e = c.Entries[line];
        if (e != null)
        {
            BeginWait(e);
        }
        else
        {
            Notice(c.Whys[line], EBindResult.Refused);
        }
    }

    private void BeginWait(BindEntry e)
    {
        waiting = e;
        // from the next frame: this one's Enter or A is the press that asked
        waitFrom = Time.frameCount + 1;
        pendingModifier = null;
        notice = null;
        menu.PlaySound(menu.Skin.MoveSound);
    }

    /// <summary>The menu closes or opens: a rebind left waiting is dropped.</summary>
    public void CancelWait()
    {
        waiting = null;
        notice = null;
    }

    /// <summary>What the keyboard's list says while it waits for a key; the pad's waits under a box (<see cref="WaitBox"/>).</summary>
    private string Prompt()
    {
        return waiting == null ? null : $"Press the new key for {waiting.Label}. Esc, or Start or B on the pad: cancel.";
    }

    /// <summary>Every frame a binding waits: the first key or button pressed is its new one, and swaps with what held it.</summary>
    public void UpdateWait()
    {
        if (waiting == null || Time.frameCount < waitFrom)
        {
            return;
        }
        Mouse mouse = Mouse.current;
        if (GameInput.EscapePressedThisFrame())
        {
            // cancelled: nothing to say (the user's, 2 November 2026)
            GameInput.ConsumeEscape();
            EndWait(null, EBindResult.Unchanged);
            return;
        }
        if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
        {
            if (waiting.Device == EBindDevice.Keyboard)
            {
                // the two buttons that point and attack: an error, as any key that cannot be bound
                EndWait(GameBindings.Refusal(waiting, "<Mouse>/leftButton"), EBindResult.Refused);
            }
            else
            {
                // a click while the pad's box is up only cancels it
                EndWait(null, EBindResult.Unchanged);
            }
            return;
        }

        if (waiting.Device == EBindDevice.Keyboard)
        {
            // a key is waited for, but the pad that asked for it can cancel: Start or B, as Esc (fix 454, a player
            // with only a pad was held there)
            foreach (Gamepad pad in Gamepad.all)
            {
                if (pad.startButton.wasPressedThisFrame || pad.bButton.wasPressedThisFrame)
                {
                    EndWait(null, EBindResult.Unchanged);
                    return;
                }
            }
        }

        if (waiting.Device == EBindDevice.Gamepad)
        {
            foreach (Gamepad pad in Gamepad.all)
            {
                if (pad.startButton.wasPressedThisFrame)
                {
                    EndWait(null, EBindResult.Unchanged);
                    return;
                }
                if (waiting.Kind == EBindKind.Stick)
                {
                    if (pad.leftStick.ReadValue().magnitude > 0.7f)
                    {
                        Finish("<Gamepad>/leftStick");
                        return;
                    }
                    if (pad.rightStick.ReadValue().magnitude > 0.7f)
                    {
                        Finish("<Gamepad>/rightStick");
                        return;
                    }
                }
                foreach (string name in PadButtonNames)
                {
                    ButtonControl b = pad.TryGetChildControl<ButtonControl>(name);
                    if (b != null && b.wasPressedThisFrame)
                    {
                        Finish("<Gamepad>/" + name);
                        return;
                    }
                }
            }
            return;
        }

        Keyboard keys = Keyboard.current;
        if (keys == null)
        {
            return;
        }
        foreach (KeyControl key in keys.allKeys)
        {
            if (key == null || !key.wasPressedThisFrame)
            {
                continue;
            }
            if (Array.IndexOf(ModifierKeys, key.name) >= 0)
            {
                pendingModifier = pendingModifier ?? key.name;
                continue;
            }
            Chord(keys, "<Keyboard>/" + key.name);
            return;
        }
        if (mouse != null)
        {
            ButtonControl[] spare = { mouse.middleButton, mouse.backButton, mouse.forwardButton };
            foreach (ButtonControl b in spare)
            {
                if (b != null && b.wasPressedThisFrame)
                {
                    Chord(keys, "<Mouse>/" + b.name);
                    return;
                }
            }
        }
        if (pendingModifier != null)
        {
            KeyControl m = keys.TryGetChildControl<KeyControl>(pendingModifier);
            if (m == null || m.wasReleasedThisFrame || !m.isPressed)
            {
                Finish("<Keyboard>/" + pendingModifier);
            }
        }
    }

    /// <summary>A key pressed while waiting, with the one modifier held with it, if any.</summary>
    private void Chord(Keyboard keys, string key)
    {
        List<string> held = new List<string>();
        if (keys.ctrlKey.isPressed)
        {
            held.Add("ctrl");
        }
        if (keys.shiftKey.isPressed)
        {
            held.Add("shift");
        }
        if (keys.altKey.isPressed)
        {
            held.Add("alt");
        }
        if (held.Count > 1)
        {
            EndWait("One modifier at a time.", EBindResult.Refused);
            return;
        }
        Finish(held.Count == 1 ? held[0] + "+" + key : key);
    }

    private void Finish(string binding)
    {
        BindEntry e = waiting;
        string line = GameBindings.Bind(e, binding, out EBindResult result);
        EndWait(line, result);
    }

    private void EndWait(string line, EBindResult result)
    {
        waiting = null;
        if (line != null)
        {
            Notice(line, result);
        }
        else
        {
            menu.PlaySound(menu.Skin.BackSound);
        }
        menu.DisarmAfterRebind();
    }

    /// <summary>The hints along the bottom, for the device used last.</summary>
    public Hint[] Hints(EOptionSection section, bool pad)
    {
        List<Hint> hints = new List<Hint>();
        if (Waiting)
        {
            hints.Add(waiting.Device == EBindDevice.Gamepad
                ? new Hint("cancel", HintKey.PadButton("Start"))
                : new Hint("cancel", HintKey.Of(menu.Skin.EscapeKey)));
            return hints.ToArray();
        }
        hints.Add(pad ? new Hint("rebind", HintKey.Of(menu.Skin.AButton)) : new Hint("rebind", HintKey.Keyboard("Enter")));
        if (!pad && section == EOptionSection.Keyboard)
        {
            hints.Add(new Hint("remove key", HintKey.Keyboard("Del")));
        }
        if (pad)
        {
            if (section == EOptionSection.Gamepad)
            {
                hints.Add(new Hint("page", HintKey.Of(menu.Skin.DPadLeftRight)));
            }
            hints.Add(new Hint("tab", HintKey.PadButton("LB"), HintKey.PadButton("RB")));
            hints.Add(new Hint("section", HintKey.PadButton("LT"), HintKey.PadButton("RT")));
            hints.Add(new Hint("close", HintKey.Of(menu.Skin.BButton)));
        }
        else
        {
            if (section == EOptionSection.Gamepad)
            {
                hints.Add(new Hint("page", HintKey.Keyboard("←"), HintKey.Keyboard("→")));
            }
            hints.Add(new Hint("tab", HintKey.Keyboard("Tab")));
            hints.Add(new Hint("section", HintKey.Keyboard("PgUp"), HintKey.Keyboard("PgDn")));
            hints.Add(new Hint("close", HintKey.Of(menu.Skin.EscapeKey)));
        }
        return hints.ToArray();
    }

    // ---------------------------------------------------------------- drawing

    /// <summary>Draws a map in the area under the sections. True when the pointer took the selection.</summary>
    public bool Draw(EOptionSection section, Rect area, bool hoverSelects)
    {
        return section == EOptionSection.Keyboard ? DrawKeyboardPage(area, hoverSelects) : DrawPadPage(area, hoverSelects);
    }

    private bool DrawKeyboardPage(Rect area, bool hoverSelects)
    {
        bool took = false;
        float listW = Mathf.Round(area.width * 0.38f);
        Rect drawing = new Rect(area.x, area.y, area.width - listW - 24f, area.height);
        Rect list = new Rect(drawing.xMax + 24f, area.y, listW, area.height);
        menu.DrawPanel(drawing);
        menu.DrawPanel(list);

        KeyRow selectedRow = KeyRows[Mathf.Clamp(keySelected, 0, KeyRows.Length - 1)];
        HashSet<string> lit = new HashSet<string>();
        foreach (string k in RowKeys(selectedRow))
        {
            foreach (string c in Controls(k))
            {
                lit.Add(c);
            }
        }
        HashSet<string> used = new HashSet<string>();
        foreach (KeyRow row in KeyRows)
        {
            if (row.Header == null)
            {
                foreach (string k in RowKeys(row))
                {
                    foreach (string c in Controls(k))
                    {
                        used.Add(c);
                    }
                }
            }
        }

        // the keyboard across the top of its panel, the mouse and what it does under it
        float inner = drawing.width - 48f;
        float unit = Mathf.Min(inner / KeyboardUnitsWide, drawing.height * 0.62f / KeyboardUnitsHigh);
        float ox = drawing.x + (drawing.width - unit * KeyboardUnitsWide) / 2;
        float oy = drawing.y + 24f;
        float gap = Mathf.Max(2f, unit * 0.07f);
        float capFont = Mathf.Clamp(unit * 0.42f, 14f, 24f);
        foreach (Cap cap in Caps)
        {
            Rect r = new Rect(ox + cap.X * unit + gap / 2, oy + cap.Y * unit + gap / 2, cap.W * unit - gap, unit - gap);
            bool on = lit.Contains(cap.Name);
            bool bound = used.Contains(cap.Name);
            if (on)
            {
                menu.DrawHighlight(r, true);
            }
            menu.DrawFrame(r, on ? Color.white : bound ? GameMenu.UnselectColor : Faint(GameMenu.Dim));
            string name = KeyName(cap.Name);
            float size = capFont;
            float width = menu.MeasureText(name, size);
            if (width > r.width - 6f)
            {
                size = Mathf.Max(11f, size * (r.width - 6f) / width);
            }
            menu.DrawText(r, name, size, TextAnchor.MiddleCenter, on ? Color.white : bound ? GameMenu.SelectColor : GameMenu.Dim);
            if (bound && !Waiting && menu.WasClicked(r))
            {
                // a click on a key selects the first action it does; only Enter rebinds it (the user's, 2 November 2026)
                for (int i = 0; i < KeyRows.Length; i++)
                {
                    if (KeyRows[i].Header == null && RowKeys(KeyRows[i]).Exists(k => Array.IndexOf(Controls(k), cap.Name) >= 0))
                    {
                        keySelected = i;
                        keyFollow = true;
                        took = true;
                        break;
                    }
                }
            }
        }

        float mouseTop = oy + KeyboardUnitsHigh * unit + 28f;
        float mouseH = Mathf.Min(drawing.yMax - 24f - mouseTop, unit * 3f);
        if (mouseH > 40f)
        {
            float mouseW = mouseH * MouseOutline.GridWidth / MouseOutline.GridHeight;
            Rect mouse = new Rect(ox, mouseTop, mouseW, mouseH);
            Color previous = GUI.color;
            GUI.color = GameMenu.UnselectColor;
            GUI.DrawTexture(UiScale.R(mouse), MouseOutline.TextureFor(UiScale.S(mouse.width)));
            GUI.color = previous;
            float textX = mouse.xMax + 24f;
            float textW = drawing.xMax - 24f - textX;
            // half a line above the mouse's top, so its lines sit beside it rather than below it (the user's, 1 November 2026)
            float y = mouseTop - LineHeight / 2;
            foreach (string m in MouseLines)
            {
                List<string> lines = Wrap(m, Font, textW);
                foreach (string l in lines)
                {
                    menu.DrawText(new Rect(textX, y, textW, LineHeight), l, Font, TextAnchor.MiddleLeft, GameMenu.SelectColor);
                    y += LineHeight;
                }
                y += 6f;
            }
        }

        took |= DrawKeyList(list, hoverSelects);
        return took;
    }

    private bool DrawKeyList(Rect list, bool hoverSelects)
    {
        bool took = false;
        KeyRow selectedRow = KeyRows[Mathf.Clamp(keySelected, 0, KeyRows.Length - 1)];
        // the notes' space is kept whatever the row: when it came and went with the selection, the list under a
        // still pointer moved and the pointer took another row. Two lines, the user's of 1 November 2026; a note
        // that wraps to more is cut, so the notes are kept short.
        // while a rebind waits, and for a while after it, the notes' place says what to press or what was done
        string noteText = Waiting ? Prompt() : NoticeShown ? notice : RowNote(selectedRow);
        Color noteInk = Waiting ? Color.white : NoticeShown ? NoticeColor : GameMenu.UnselectColor;
        List<string> note = noteText != null ? Wrap(noteText, Font, list.width - 40f) : new List<string>();
        if (note.Count > NoteLines)
        {
            note.RemoveRange(NoteLines, note.Count - NoteLines);
        }
        // Every row, the groups' titles too, is one height, and the list holds as many whole rows as fit: so it shows
        // the same number of rows wherever it is scrolled to. With titles a little shorter than the rows, the rows
        // that fit changed with the scroll and a row came and went at the bottom (the user's, 1 November 2026).
        // the notes at the panel's foot, on lines a little closer than the list's text, so the list has every row
        // the rest can hold (the user's, 1 November 2026: there was room for one more)
        float noteH = NoteLines * NoteLineHeight;
        float noteY = list.yMax - 6f - noteH;
        float top = list.y + 6f;
        int visible = Mathf.Max(1, Mathf.FloorToInt((noteY - 2f - top) / KeyRowHeight));
        int maxScroll = Mathf.Max(0, KeyRows.Length - visible);

        // The wheel moves the scroll freely; only a selection made by the keys, or by a click on the drawing, brings
        // its row into view, with its group's title when it is the group's first.
        if (keyFollow)
        {
            keyFollow = false;
            int first = keySelected > 0 && KeyRows[keySelected - 1].Header != null ? keySelected - 1 : keySelected;
            if (first < keyScroll)
            {
                keyScroll = first;
            }
            else if (keySelected >= keyScroll + visible)
            {
                keyScroll = keySelected - visible + 1;
            }
        }
        keyScroll = Mathf.Clamp(keyScroll, 0, maxScroll);

        for (int k = 0; k < visible && keyScroll + k < KeyRows.Length; k++)
        {
            int i = keyScroll + k;
            KeyRow row = KeyRows[i];
            float h = KeyRowHeight;
            // the rows stop short of the scroll bar, so that a click on it is the bar's and not a row's (the user's report
            // of 2 November 2026: the bar could not be clicked)
            Rect r = new Rect(list.x + 8f, top + k * h, list.width - 16f - (maxScroll > 0 ? KeyScrollBarWidth + 8f : 0f), h);
            if (row.Header != null)
            {
                menu.DrawText(new Rect(r.x + 12f, r.y, r.width - 24f, h), "<b>" + row.Header + "</b>", Font, TextAnchor.MiddleLeft, GameMenu.UnselectColor);
            }
            else
            {
                bool selected = i == keySelected;
                if (hoverSelects && !Waiting && !keyDragging && menu.IsHovered(r) && !selected)
                {
                    keySelected = i;
                    selected = true;
                    took = true;
                }
                if (!Waiting && menu.WasClicked(r, out bool twice))
                {
                    keySelected = i;
                    selected = true;
                    took = true;
                    // a double click rebinds, as Enter does (the user's, 2 November 2026)
                    if (twice)
                    {
                        Rebind(row);
                    }
                }
                if (selected)
                {
                    menu.DrawHighlight(r, true);
                }
                Color ink = selected ? Color.white : GameMenu.SelectColor;
                string keys = Waiting && selected ? "..." : row.KeysText ?? KeysText(RowKeys(row));
                float keysW = Mathf.Min(menu.MeasureText(keys, Font) + 4f, r.width * 0.45f);
                float keysX = r.xMax - 12f - keysW;
                menu.DrawText(new Rect(r.x + 12f, r.y, keysX - 24f - r.x, h), row.Label, Font, TextAnchor.MiddleLeft, ink);
                menu.DrawText(new Rect(keysX, r.y, keysW, h), keys, Font, TextAnchor.MiddleRight, ink);
            }
        }

        // the scroll bar of the save and load lists, the menu's one (the user's, 2 November 2026)
        if (maxScroll > 0)
        {
            Rect bar = new Rect(list.xMax - 8f - KeyScrollBarWidth, top, KeyScrollBarWidth, visible * KeyRowHeight);
            keyScroll = menu.DrawScrollBar(bar, keyScroll, visible, KeyRows.Length, ref keyDragging, ref keyGrab);
        }
        else
        {
            keyDragging = false;
        }

        float ny = noteY;
        int noteWarnFrom = !Waiting && NoticeShown ? FirstParagraphLines(noteText, Font, list.width - 40f) : int.MaxValue;
        for (int i = 0; i < note.Count; i++)
        {
            menu.DrawText(new Rect(list.x + 20f, ny, list.width - 40f, NoteLineHeight), note[i], Font, TextAnchor.MiddleLeft,
                i >= noteWarnFrom ? RefusedColor : noteInk);
            ny += NoteLineHeight;
        }
        return took;
    }

    private static List<int> Selectable()
    {
        List<int> list = new List<int>();
        for (int i = 0; i < KeyRows.Length; i++)
        {
            if (KeyRows[i].Header == null)
            {
                list.Add(i);
            }
        }
        return list;
    }

    private bool DrawPadPage(Rect area, bool hoverSelects)
    {
        bool took = false;
        menu.DrawPanel(area);

        // the contexts, as the sections are: one chip each, the current one lit
        float chipW = 0f;
        foreach (string name in ContextNames)
        {
            chipW = Mathf.Max(chipW, menu.MeasureText(name, Font) + 36f);
        }
        float chipGap = 10f;
        float chipsW = ContextNames.Length * (chipW + chipGap) - chipGap;
        float chipX = area.x + (area.width - chipsW) / 2;
        for (int i = 0; i < ContextNames.Length; i++)
        {
            Rect r = new Rect(chipX + i * (chipW + chipGap), area.y + 12f, chipW, ChipHeight);
            if (menu.DrawButton(r, ContextNames[i], true, i == (int)context) && i != (int)context)
            {
                context = (EPadContext)i;
                menu.PlaySound(menu.Skin.MoveSound);
                took = true;
            }
        }

        // the labels take the whole height beside the pad; the footnote goes under the pad, between them
        padFont = PadSizes[0, 0];
        padLine = PadSizes[0, 1];
        padGlyph = PadSizes[0, 2];
        float top = area.y + 12f + ChipHeight + 12f;
        float bottom = area.yMax - 12f;
        float picW = Mathf.Min(area.width * 0.36f, (bottom - top - FootnoteMaxLines * padLine) * PadOutline.GridWidth / PadOutline.GridHeight);
        float picH = picW * PadOutline.GridHeight / PadOutline.GridWidth;
        // the footnotes' space is kept on every page, as tall as the longest, so the pad does not move between pages
        // while a rebind waits, and for a while after it, the footnote's place says what to press or what was done
        // the box over the map says what to press; after it, the footnote's place says what was done
        string footnote = !Waiting && NoticeShown ? notice : Footnote(context);
        Color footInk = !Waiting && NoticeShown ? NoticeColor : GameMenu.UnselectColor;
        float footW = picW + 2 * FootnoteOverhang;
        List<string> foot = footnote != null ? Wrap(footnote, padFont, footW) : new List<string>();
        if (foot.Count > FootnoteMaxLines)
        {
            foot.RemoveRange(FootnoteMaxLines, foot.Count - FootnoteMaxLines);
        }
        int footLines = 0;
        foreach (EPadContext page in Enum.GetValues(typeof(EPadContext)))
        {
            string f = Footnote(page);
            if (f != null)
            {
                footLines = Mathf.Max(footLines, Mathf.Min(FootnoteMaxLines, Wrap(f, padFont, footW).Count));
            }
        }
        float footH = footLines * padLine;
        Rect pic = new Rect(area.center.x - picW / 2, top + (bottom - top - picH - footH) / 2, picW, picH);

        // pic places the labels; drawn is where the pad, the lines' ends and the footnote are drawn
        Rect drawn = new Rect(pic.x, pic.y + PadDrop, pic.width, pic.height);

        Color previous = GUI.color;
        GUI.color = GameMenu.UnselectColor;
        GUI.DrawTexture(UiScale.R(drawn), PadOutline.TextureFor(UiScale.S(drawn.width), PadGlyphs.PlayStation));
        GUI.color = previous;

        List<Callout> callouts = Callouts(context);
        List<Stop> stops = Stops(callouts);
        int selectedStop = Mathf.Clamp(padSelected[(int)context], 0, Mathf.Max(0, stops.Count - 1));
        int selected = stops.Count > 0 ? stops[selectedStop].Callout : -1;
        int selectedLine = stops.Count > 0 ? stops[selectedStop].Line : -1;
        float leftEdge = area.x + 24f;
        float rightEdge = area.xMax - 24f;
        float columnGap = 48f;
        // one size for every page, the first at which all of them fit, so a label stands in the same place on every
        // page that has it
        for (int size = 0; size < PadSizes.GetLength(0); size++)
        {
            padFont = PadSizes[size, 0];
            padLine = PadSizes[size, 1];
            padGlyph = PadSizes[size, 2];
            bool fits = true;
            foreach (EPadContext page in Enum.GetValues(typeof(EPadContext)))
            {
                List<Callout> trial = Callouts(page);
                fits &= PlaceSide(trial, true, pic, pic.x - columnGap - leftEdge, top, bottom);
                fits &= PlaceSide(trial, false, pic, rightEdge - pic.xMax - columnGap, top, bottom);
            }
            if (fits)
            {
                break;
            }
        }
        PlaceSide(callouts, true, pic, pic.x - columnGap - leftEdge, top, bottom);
        PlaceSide(callouts, false, pic, rightEdge - pic.xMax - columnGap, top, bottom);

        for (int i = 0; i < callouts.Count; i++)
        {
            Callout c = callouts[i];
            bool left = PadOutline.OnLeft(c.Control);
            float columnW = left ? pic.x - columnGap - leftEdge : rightEdge - pic.xMax - columnGap;
            float columnX = left ? leftEdge : pic.xMax + columnGap;
            Rect block = new Rect(columnX, c.Top, columnW, c.Height);
            // the line under the pointer, on a callout of two (a stick and its click)
            int lineUnder = 0;
            float ly = c.Top + Mathf.Max(0f, (padGlyph - padLine) / 2);
            for (int w = 0; w < c.Wrapped.Count; w++)
            {
                if (menu.IsHovered(new Rect(block.x, ly + w * padLine, block.width, padLine)))
                {
                    lineUnder = c.WrappedOwner[w];
                }
            }
            int stopUnder = stops.FindIndex(st => st.Callout == i && st.Line == lineUnder);
            bool on = i == selected;
            if (!Waiting && hoverSelects && menu.IsHovered(block) && stopUnder >= 0 && stopUnder != selectedStop)
            {
                padSelected[(int)context] = stopUnder;
                selectedStop = stopUnder;
                selectedLine = lineUnder;
                on = true;
                took = true;
            }
            if (!Waiting && stopUnder >= 0 && menu.WasClicked(block, out bool twice))
            {
                // a click selects; a double click rebinds, as A and Enter do (the user's, 2 November 2026)
                padSelected[(int)context] = stopUnder;
                selectedStop = stopUnder;
                selectedLine = lineUnder;
                on = true;
                took = true;
                if (twice)
                {
                    Rebind(c, lineUnder);
                }
            }
            if (on)
            {
                menu.DrawHighlight(block, true);
            }

            Color ink = on ? Color.white : GameMenu.SelectColor;
            Rect glyph = GlyphRect(c.Control, block, left);
            bool several = c.Lines.Count > 1;
            Vector2 attach = new Vector2(left ? block.xMax + 6f : block.x - 6f, glyph.center.y);
            Vector2 anchor = new Vector2(drawn.x + c.Anchor.x / PadOutline.GridWidth * drawn.width, drawn.y + c.Anchor.y / PadOutline.GridHeight * drawn.height);
            Color lineColor = on ? Color.white : GameMenu.UnselectColor;
            float lineWidth = on ? 3f : 2f;
            if (UnderShoulder(c.Control))
            {
                // up at UnderShoulderDegrees, outward, to the label's height, then straight across to it
                float rise = Mathf.Abs(attach.y - anchor.y);
                float run = rise / Mathf.Tan(UnderShoulderDegrees * Mathf.Deg2Rad);
                float bendX = anchor.x + (left ? -run : run);
                bendX = left ? Mathf.Max(bendX, attach.x) : Mathf.Min(bendX, attach.x);
                Vector2 bend = new Vector2(bendX, attach.y);
                Line(anchor, bend, lineWidth, lineColor);
                Line(bend, attach, lineWidth, lineColor);
            }
            else
            {
                Line(anchor, attach, lineWidth, lineColor);
            }
            menu.DrawRect(new Rect(anchor.x - 4f, anchor.y - 4f, 8f, 8f), on ? Color.white : GameMenu.SelectColor);

            DrawGlyph(c.Control, glyph, on);
            float textX = left ? block.x - TextOverhang : glyph.xMax + 12f;
            float textW = left ? glyph.x - 12f - textX : block.xMax + TextOverhang - textX;
            float y = c.Top + Mathf.Max(0f, (padGlyph - padLine) / 2);
            for (int w = 0; w < c.Wrapped.Count; w++)
            {
                // of a callout of two lines, the selected one in white, the other in the list's colour
                Color lineInk = on && several && c.WrappedOwner[w] != selectedLine ? GameMenu.SelectColor : ink;
                string l = c.Wrapped[w];
                menu.DrawText(new Rect(textX, y, textW, padLine), l, padFont, left ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, lineInk);
                y += padLine;
            }
        }

        float fy = drawn.yMax + 8f;
        int footWarnFrom = !Waiting && NoticeShown ? FirstParagraphLines(footnote, padFont, footW) : int.MaxValue;
        for (int i = 0; i < foot.Count; i++)
        {
            menu.DrawText(new Rect(drawn.x - FootnoteOverhang, fy, drawn.width + 2 * FootnoteOverhang, padLine), foot[i], padFont,
                TextAnchor.MiddleCenter, i >= footWarnFrom ? RefusedColor : footInk);
            fy += padLine;
        }
        return took;
    }

    /// <summary>
    /// Puts the labels of one side in a column: each as near its control's height as it can, in the order of the
    /// controls from top to bottom, none overlapping and none outside the area.
    /// </summary>
    /// <returns>False when they need more height than there is, and the top ones are pushed up past it.</returns>
    private bool PlaceSide(List<Callout> callouts, bool left, Rect pic, float columnW, float top, float bottom)
    {
        List<Callout> side = callouts.FindAll(c => PadOutline.OnLeft(c.Control) == left);
        // A page with Select or Start but not the shoulders above them (MAP) keeps the shoulders' places empty, so
        // Select and Start stand where they do on WALKING and MENUS (the user's, 1 November 2026). Placeholders, of one
        // line, never drawn: they are not in the page's list.
        if (side.Exists(c => UnderShoulder(c.Control)))
        {
            EPadControl[] above = left
                ? new[] { EPadControl.LeftTrigger, EPadControl.LeftShoulder }
                : new[] { EPadControl.RightTrigger, EPadControl.RightShoulder };
            foreach (EPadControl control in above)
            {
                if (!side.Exists(c => c.Control == control))
                {
                    Callout placeholder = new Callout { Control = control, Anchor = PadOutline.Anchor(control) };
                    placeholder.Lines.Add("");
                    side.Add(placeholder);
                }
            }
        }
        side.Sort((a, b) => CompareInColumn(a, b, left));
        const float spacing = 8f;
        foreach (Callout c in side)
        {
            float textW = columnW - GlyphWidth(c.Control) - 12f + TextOverhang;
            c.Wrapped = new List<string>();
            c.WrappedOwner = new List<int>();
            for (int l = 0; l < c.Lines.Count; l++)
            {
                foreach (string w in Wrap(c.Lines[l], padFont, textW))
                {
                    c.Wrapped.Add(w);
                    c.WrappedOwner.Add(l);
                }
            }
            c.Height = Mathf.Max(padGlyph, c.Wrapped.Count * padLine + Mathf.Max(0f, (padGlyph - padLine)));
            float anchorY = pic.y + c.Anchor.y / PadOutline.GridHeight * pic.height;
            // Select and Start go straight under the shoulders, whatever their height on the pad
            c.Top = UnderShoulder(c.Control) ? float.MinValue : anchorY - padGlyph / 2;
        }
        // down, so that none overlaps the one above; then up from the bottom, so that none goes past the area
        float floor = top;
        foreach (Callout c in side)
        {
            c.Top = Mathf.Max(c.Top, floor);
            floor = c.Top + c.Height + spacing;
        }
        float ceiling = bottom;
        for (int i = side.Count - 1; i >= 0; i--)
        {
            Callout c = side[i];
            c.Top = Mathf.Min(c.Top, ceiling - c.Height);
            ceiling = c.Top - spacing;
        }
        return side.Count == 0 || side[0].Top >= top - 0.5f;
    }

    private const float ChipFont = 22f;
    // A label's text may run this far past its box on the outer side, toward the panel's edge: on the PlayStation pad,
    // whose keys' names are longer, Start's "Game menu; hold: controls" then keeps to one line at 1366 x 768, with
    // the labels where they were (the user's, 1 November 2026).
    private const float TextOverhang = 10f;

    /// <summary>A round face button is a picture; the others a frame with their name, as wide as it needs.</summary>
    private float GlyphWidth(EPadControl control)
    {
        string name = ChipName(control);
        return name == null ? padGlyph : Mathf.Max(padGlyph, menu.MeasureText(name, ChipFont) + 20f);
    }

    private Rect GlyphRect(EPadControl control, Rect block, bool left)
    {
        float w = GlyphWidth(control);
        return new Rect(left ? block.xMax - w : block.x, block.y, w, padGlyph);
    }

    private void DrawGlyph(EPadControl control, Rect r, bool on)
    {
        Texture2D picture = control == EPadControl.South ? menu.Skin.AButton
            : control == EPadControl.East ? menu.Skin.BButton
            : control == EPadControl.West ? menu.Skin.XButton
            : control == EPadControl.North ? menu.Skin.YButton : null;
        if (picture != null)
        {
            GUI.DrawTexture(UiScale.R(r), PadGlyphs.Face(picture));
            return;
        }
        string name = ChipName(control);
        menu.DrawFrame(r, on ? Color.white : GameMenu.UnselectColor);
        menu.DrawText(r, name, ChipFont, TextAnchor.MiddleCenter, on ? Color.white : GameMenu.SelectColor);
    }

    /// <summary>A button's name on its key, as the pad in use prints it (PadGlyphs.Name()).</summary>
    private static string ChipName(EPadControl control)
    {
        switch (control)
        {
            case EPadControl.LeftTrigger: return PadGlyphs.Name("LT");
            case EPadControl.LeftShoulder: return PadGlyphs.Name("LB");
            case EPadControl.LeftStick: return "LS";
            case EPadControl.DPad: return "D-pad";
            case EPadControl.Select: return PadGlyphs.Name("Select");
            case EPadControl.Start: return PadGlyphs.Name("Start");
            case EPadControl.RightStick: return "RS";
            case EPadControl.RightShoulder: return PadGlyphs.Name("RB");
            case EPadControl.RightTrigger: return PadGlyphs.Name("RT");
            default: return null;
        }
    }

    // The footnote under the pad: at most this many lines, the space for them kept under the pad on every page, and
    // wider than the pad by this much each side, short of the label columns (the user's, 1 November 2026: the
    // WALKING one says what the ledge rumble is, and grew).
    private const int FootnoteMaxLines = 3;
    private const float FootnoteOverhang = 40f;

    private static string Footnote(EPadContext context)
    {
        switch (context)
        {
            case EPadContext.Walking:
                // the ledge rumble is said here because nothing else says it, and a player took it for a fault
                return "The triggers change sides with your character's weapon hand. "
                    + "A rumble as you run means a ledge ahead. It can be turned off under INPUT.";
            case EPadContext.Inventory: return "With the inventory open, " + GameBindings.PadText(GameActions.PadWorld.Move) + " still walks.";
            case EPadContext.Menus: return "The menus' buttons are always the same.";
            default: return null;
        }
    }

    /// <summary>A line from one point to another in reference pixels, of a width in reference pixels.</summary>
    private static void Line(Vector2 from, Vector2 to, float width, Color color)
    {
        Vector2 a = UiScale.R(from.x, from.y, 0f, 0f).position;
        Vector2 b = UiScale.R(to.x, to.y, 0f, 0f).position;
        Vector2 d = b - a;
        float length = d.magnitude;
        if (length < 0.5f)
        {
            return;
        }
        float w = Mathf.Max(1f, UiScale.S(width));
        Matrix4x4 matrix = GUI.matrix;
        Color previous = GUI.color;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
        GUI.color = color;
        GUI.DrawTexture(new Rect(a.x, a.y - w / 2, length, w), Texture2D.whiteTexture);
        GUI.matrix = matrix;
        GUI.color = previous;
    }

    // ---------------------------------------------------------------- what the pad does

    private List<Callout> Callouts(EPadContext page)
    {
        List<PadLine> lines = new List<PadLine>();
        switch (page)
        {
            case EPadContext.Walking:
                AddBound(lines, GameActions.PadWorld.Move, "Move");
                AddBound(lines, GameActions.PadWorld.Look, "Look");
                AddBound(lines, GameActions.PadWorld.Sprint, "Sprint");
                AddBound(lines, GameActions.PadWorld.Jump, "Jump");
                AddBound(lines, GameActions.PadWorld.LookAt, "Look at");
                AddBound(lines, GameActions.PadWorld.Use, "Use or pick up");
                AddBound(lines, GameActions.PadWorld.Cancel, "Cancel a charge, drop the item");
                AddBound(lines, GameActions.PadWorld.Attack, "Attack");
                AddBound(lines, GameActions.PadWorld.Stats, "Stats");
                AddBound(lines, GameActions.PadWorld.MagicPanel, "Magic panel");
                AddBound(lines, GameActions.PadWorld.Inventory, "Inventory");
                AddBound(lines, GameActions.PadWorld.Map, "Map");
                AddBound(lines, GameActions.PadWorld.Quicksave, "Quicksave (hold)");
                AddBound(lines, GameActions.PadWorld.Sleep, "Sleep (hold)");
                AddBound(lines, GameActions.PadWorld.Track, "Track");
                AddBound(lines, GameActions.PadWorld.MessagesBack, "Messages back");
                AddBound(lines, GameActions.PadWorld.MessagesForward, "Messages forward");
                AddFixed(lines, EPadControl.Start, "Game menu; hold: controls", PadGlyphs.Name("Start") + " can't be changed.");
                break;
            case EPadContext.Inventory:
                AddBound(lines, GameActions.PadInventory.Up, "Move the selection");
                AddBound(lines, GameActions.PadInventory.Down, "Move the selection");
                AddBound(lines, GameActions.PadInventory.Left, "Move the selection");
                AddBound(lines, GameActions.PadInventory.Right, "Move the selection");
                AddBound(lines, GameActions.PadInventory.Select, "Look or use; hold: pick up");
                AddBound(lines, GameActions.PadInventory.SecondaryUse, "Equip, eat, unequip (hold)");
                AddBound(lines, GameActions.PadInventory.Throw, "Throw (hold and let go)");
                AddBound(lines, GameActions.PadInventory.Back, "Back");
                AddBound(lines, GameActions.PadWorld.Move, "Move");
                AddBound(lines, GameActions.PadWorld.Inventory, "Close the inventory");
                break;
            case EPadContext.Magic:
                AddBound(lines, GameActions.PadMagic.Up, "Move the rune cursor");
                AddBound(lines, GameActions.PadMagic.Down, "Move the rune cursor");
                AddBound(lines, GameActions.PadMagic.Left, "Move the rune cursor");
                AddBound(lines, GameActions.PadMagic.Right, "Move the rune cursor");
                AddBound(lines, GameActions.PadMagic.Select, "Lay the rune, or dispel");
                AddBound(lines, GameActions.PadMagic.Clear, "Clear the runes");
                AddBound(lines, GameActions.PadMagic.Cast, "Cast");
                AddBound(lines, GameActions.PadWorld.MagicPanel, "Close the magic panel");
                AddBound(lines, GameActions.PadWorld.Move, "Move");
                break;
            case EPadContext.Map:
                AddBound(lines, GameActions.PadMap.QuillX, "Move the quill");
                AddBound(lines, GameActions.PadMap.QuillY, "Move the quill");
                AddBound(lines, GameActions.PadMap.Select, "Place or edit a note");
                AddBound(lines, GameActions.PadMap.EraseNote, "Erase the note");
                AddBound(lines, GameActions.PadMap.PreviousNote, "Previous note");
                AddBound(lines, GameActions.PadMap.NextNote, "Next note");
                AddBound(lines, GameActions.PadMap.PreviousPage, "Previous page");
                AddBound(lines, GameActions.PadMap.NextPage, "Next page");
                AddBound(lines, GameActions.PadMap.Close, "Close");
                AddBound(lines, GameActions.PadMap.Back, "Close (on release)");
                break;
            default:
                AddFixed(lines, EPadControl.South, "Choose", MenusFixed);
                AddFixed(lines, EPadControl.East, "Back, close", MenusFixed);
                AddFixed(lines, EPadControl.DPad, "Move", MenusFixed);
                AddFixed(lines, EPadControl.LeftShoulder, "Previous tab", MenusFixed);
                AddFixed(lines, EPadControl.RightShoulder, "Next tab", MenusFixed);
                AddFixed(lines, EPadControl.LeftTrigger, "Previous section", MenusFixed);
                AddFixed(lines, EPadControl.RightTrigger, "Next section", MenusFixed);
                AddFixed(lines, EPadControl.Start, "Game menu", MenusFixed);
                AddFixed(lines, EPadControl.Select, "Reset; hold: reset all", MenusFixed);
                AddFixed(lines, EPadControl.North, "Delete a save", MenusFixed);
                AddFixed(lines, EPadControl.West, "Hold: quit to the title", MenusFixed);
                break;
        }

        // one callout per control, its lines in the order given; the d-pad's directions folded into one line when
        // they all do the same
        List<Callout> callouts = new List<Callout>();
        foreach (PadLine line in lines)
        {
            Callout c = callouts.Find(x => x.Control == line.Control);
            if (c == null)
            {
                c = new Callout { Control = line.Control, Anchor = PadOutline.Anchor(line.Control) };
                callouts.Add(c);
            }
            string text = line.Prefix != null ? line.Prefix + " " + line.Text : line.Text;
            if (!c.Lines.Contains(text))
            {
                c.Lines.Add(text);
                c.Entries.Add(line.Entry);
                c.Whys.Add(line.Why);
            }
        }
        Callout dpad = callouts.Find(x => x.Control == EPadControl.DPad);
        if (dpad != null)
        {
            List<PadLine> directions = lines.FindAll(l => l.Control == EPadControl.DPad && l.Prefix != null);
            if (directions.Count == 4 && directions.TrueForAll(l => l.Text == directions[0].Text && l.Entry == null))
            {
                dpad.Lines.Clear();
                dpad.Entries.Clear();
                dpad.Whys.Clear();
                dpad.Lines.Add(directions[0].Text);
                dpad.Entries.Add(null);
                dpad.Whys.Add(directions[0].Why);
            }
        }

        // the order the keys walk them: the left column from the top, then the right
        callouts.Sort((a, b) =>
        {
            bool la = PadOutline.OnLeft(a.Control);
            bool lb = PadOutline.OnLeft(b.Control);
            if (la != lb)
            {
                return la ? -1 : 1;
            }
            // the order the labels stand in: X and B are at one height, and walking them by the order their lines
            // were written swapped them on two pages (the user's report of 1 November 2026)
            return CompareInColumn(a, b, la);
        });
        return callouts;
    }

    /// <summary>
    /// The order of two labels in one column, from the top: by their height on the pad, and at one height the one
    /// nearer the column first. The only such pair is X and B, the square and the circle, which stand level: B above
    /// X, the user's choice of 1 November 2026 (it was the other way). The two lines still do not cross: B's runs up
    /// to its label from the outer button, X's down past it from the inner one.
    /// </summary>
    private static int CompareInColumn(Callout a, Callout b, bool left)
    {
        if (OrderY(a) != OrderY(b))
        {
            return OrderY(a).CompareTo(OrderY(b));
        }
        return left ? a.Anchor.x.CompareTo(b.Anchor.x) : b.Anchor.x.CompareTo(a.Anchor.x);
    }

    /// <summary>Select and Start, whose labels go right under the shoulders' (the user's, 1 November 2026).</summary>
    private static bool UnderShoulder(EPadControl control) => control == EPadControl.Select || control == EPadControl.Start;

    /// <summary>The height a callout is put in order by: Select and Start just under the shoulder of their side.</summary>
    private static float OrderY(Callout c)
    {
        if (c.Control == EPadControl.Select)
        {
            return PadOutline.Anchor(EPadControl.LeftShoulder).y + 0.5f;
        }
        if (c.Control == EPadControl.Start)
        {
            return PadOutline.Anchor(EPadControl.RightShoulder).y + 0.5f;
        }
        return c.Anchor.y;
    }

    private const string MenusFixed = "The menus' buttons can't be changed.";
    private const string CursorFixed = "The d-pad's cursor can't be changed.";

    private static void AddFixed(List<PadLine> lines, EPadControl control, string text, string why)
    {
        lines.Add(new PadLine { Control = control, Text = text, Why = why });
    }

    /// <summary>
    /// A line for each pad binding of an action, with the binding it rebinds (none for the panels' cursor, which is
    /// fixed); on a trigger the hand that pulls it, whatever the action (the triggers follow the weapon hand).
    /// </summary>
    private static void AddBound(List<PadLine> lines, InputAction action, string text)
    {
        BindEntry entry = GameBindings.Of(action);
        foreach (InputBinding b in action.bindings)
        {
            if (b.isComposite)
            {
                continue;
            }
            if (PadControlOf(b.effectivePath, out EPadControl control, out string prefix))
            {
                string shown = text;
                if (control == EPadControl.LeftTrigger || control == EPadControl.RightTrigger)
                {
                    shown += b.effectivePath == GameBindings.WeaponTrigger ? " (weapon hand)" : " (off hand)";
                }
                lines.Add(new PadLine { Control = control, Prefix = prefix, Text = shown, Entry = entry, Why = entry == null ? CursorFixed : null });
            }
        }
    }

    private static bool PadControlOf(string path, out EPadControl control, out string prefix)
    {
        control = EPadControl.South;
        prefix = null;
        const string device = "<Gamepad>/";
        if (string.IsNullOrEmpty(path) || !path.StartsWith(device, StringComparison.Ordinal))
        {
            return false;
        }
        string name = path.Substring(device.Length);
        string root = name.Split('/')[0];
        switch (root)
        {
            case "leftStick": control = EPadControl.LeftStick; return true;
            case "rightStick": control = EPadControl.RightStick; return true;
            case "leftStickPress": control = EPadControl.LeftStick; prefix = "Click:"; return true;
            case "rightStickPress": control = EPadControl.RightStick; prefix = "Click:"; return true;
            case "dpad":
                control = EPadControl.DPad;
                prefix = name.EndsWith("/up") ? "↑" : name.EndsWith("/down") ? "↓" : name.EndsWith("/left") ? "←" : name.EndsWith("/right") ? "→" : null;
                return true;
            case "buttonSouth": control = EPadControl.South; return true;
            case "buttonEast": control = EPadControl.East; return true;
            case "buttonWest": control = EPadControl.West; return true;
            case "buttonNorth": control = EPadControl.North; return true;
            case "leftShoulder": control = EPadControl.LeftShoulder; return true;
            case "rightShoulder": control = EPadControl.RightShoulder; return true;
            case "leftTrigger": control = EPadControl.LeftTrigger; return true;
            case "rightTrigger": control = EPadControl.RightTrigger; return true;
            case "select": control = EPadControl.Select; return true;
            case "start": control = EPadControl.Start; return true;
            default: return false;
        }
    }

    // ---------------------------------------------------------------- keys and their names

    /// <summary>The keyboard controls an action is bound to, or one part of its W, S, A, D vector.</summary>
    private static List<string> Bound(InputAction action, string part = null)
    {
        List<string> names = new List<string>();
        const string device = "<Keyboard>/";
        foreach (InputBinding b in action.bindings)
        {
            if (b.isComposite)
            {
                continue;
            }
            bool wanted = part == null
                ? !b.isPartOfComposite
                : b.isPartOfComposite && string.Equals(b.name, part, StringComparison.OrdinalIgnoreCase);
            string path = b.effectivePath;
            if (wanted && !string.IsNullOrEmpty(path) && path.StartsWith(device, StringComparison.Ordinal))
            {
                names.Add(path.Substring(device.Length));
            }
        }
        return names;
    }

    private static List<string> Join(params List<string>[] lists)
    {
        List<string> all = new List<string>();
        foreach (List<string> l in lists)
        {
            foreach (string name in l)
            {
                if (!all.Contains(name))
                {
                    all.Add(name);
                }
            }
        }
        return all;
    }

    private static List<string> Digits(int from, int to)
    {
        List<string> keys = new List<string>();
        for (int d = from; d <= to; d++)
        {
            // the Input System's names of the digit keys
            keys.Add(d.ToString());
        }
        return keys;
    }

    /// <summary>The keys of the drawing a bound name stands for: Ctrl, Shift and Alt are either of two.</summary>
    private static string[] Controls(string name)
    {
        int plus = name.IndexOf('+');
        if (plus > 0)
        {
            List<string> both = new List<string>(Controls(name.Substring(0, plus)));
            both.AddRange(Controls(name.Substring(plus + 1)));
            return both.ToArray();
        }
        if (name.StartsWith("mouse/", StringComparison.Ordinal))
        {
            // a mouse button lights no key
            return new string[0];
        }
        switch (name)
        {
            case "ctrl": return new[] { "leftCtrl", "rightCtrl" };
            case "shift": return new[] { "leftShift", "rightShift" };
            case "alt": return new[] { "leftAlt", "rightAlt" };
            default: return new[] { name };
        }
    }

    private static string KeysText(List<string> keys)
    {
        if (keys.Count == 0)
        {
            // an action with no key (mod 1080)
            return "—";
        }
        List<string> names = new List<string>();
        foreach (string k in keys)
        {
            string n = KeyName(k);
            if (!names.Contains(n))
            {
                names.Add(n);
            }
        }
        return string.Join("  ", names);
    }

    /// <summary>
    /// A key's name: the letters, digits and signs as the player's own keyboard layout prints them, so that on an
    /// AZERTY keyboard the key in W's place reads Z; the others by a short name that fits a key.
    /// </summary>
    private static string KeyName(string control)
    {
        int plus = control.IndexOf('+');
        if (plus > 0)
        {
            return KeyName(control.Substring(0, plus)) + "+" + KeyName(control.Substring(plus + 1));
        }
        switch (control)
        {
            case "mouse/middleButton": return "Mouse 3";
            case "mouse/backButton": return "Mouse 4";
            case "mouse/forwardButton": return "Mouse 5";
            case "escape": return "Esc";
            case "backspace": return "Back";
            case "tab": return "Tab";
            case "capsLock": return "Caps";
            case "enter": return "Enter";
            case "leftShift":
            case "rightShift":
            case "shift": return "Shift";
            case "leftCtrl":
            case "rightCtrl":
            case "ctrl": return "Ctrl";
            case "leftAlt":
            case "rightAlt":
            case "alt": return "Alt";
            case "leftMeta":
            case "rightMeta": return "Win";
            case "contextMenu": return "Menu";
            case "space": return "Space";
            case "insert": return "Ins";
            case "delete": return "Del";
            case "home": return "Home";
            case "end": return "End";
            case "pageUp": return "PgUp";
            case "pageDown": return "PgDn";
            case "upArrow": return "↑";
            case "downArrow": return "↓";
            case "leftArrow": return "←";
            case "rightArrow": return "→";
        }
        if (control.Length >= 2 && control[0] == 'f' && char.IsDigit(control[1]))
        {
            return control.ToUpperInvariant();
        }
        KeyControl key = Keyboard.current?.TryGetChildControl<KeyControl>(control);
        if (key != null && !string.IsNullOrEmpty(key.displayName) && key.displayName.Length <= 2)
        {
            return key.displayName.ToUpperInvariant();
        }
        switch (control)
        {
            case "backquote": return "`";
            case "minus": return "-";
            case "equals": return "=";
            case "leftBracket": return "[";
            case "rightBracket": return "]";
            case "backslash": return "\\";
            case "semicolon": return ";";
            case "quote": return "'";
            case "comma": return ",";
            case "period": return ".";
            case "slash": return "/";
        }
        return control.ToUpperInvariant();
    }

    private static Cap[] BuildCaps()
    {
        List<Cap> caps = new List<Cap>();
        void Row(float y, float x, params (string name, float width)[] keys)
        {
            foreach ((string name, float width) in keys)
            {
                caps.Add(new Cap { Name = name, X = x, Y = y, W = width });
                x += width;
            }
        }
        (string, float) K(string name, float width = 1f) => (name, width);

        Row(0f, 0f, K("escape"));
        Row(0f, 2f, K("f1"), K("f2"), K("f3"), K("f4"));
        Row(0f, 6.5f, K("f5"), K("f6"), K("f7"), K("f8"));
        Row(0f, 11f, K("f9"), K("f10"), K("f11"), K("f12"));
        // the digits by the Input System's own names, 1 to 0: "digit1" named nothing, and a digit could not be bound (the
        // user's report of 2 November 2026)
        Row(1.5f, 0f, K("backquote"), K("1"), K("2"), K("3"), K("4"), K("5"), K("6"), K("7"),
            K("8"), K("9"), K("0"), K("minus"), K("equals"), K("backspace", 2f));
        Row(2.5f, 0f, K("tab", 1.5f), K("q"), K("w"), K("e"), K("r"), K("t"), K("y"), K("u"), K("i"), K("o"), K("p"),
            K("leftBracket"), K("rightBracket"), K("backslash", 1.5f));
        Row(3.5f, 0f, K("capsLock", 1.75f), K("a"), K("s"), K("d"), K("f"), K("g"), K("h"), K("j"), K("k"), K("l"),
            K("semicolon"), K("quote"), K("enter", 2.25f));
        Row(4.5f, 0f, K("leftShift", 2.25f), K("z"), K("x"), K("c"), K("v"), K("b"), K("n"), K("m"), K("comma"), K("period"),
            K("slash"), K("rightShift", 2.75f));
        Row(5.5f, 0f, K("leftCtrl", 1.25f), K("leftMeta", 1.25f), K("leftAlt", 1.25f), K("space", 6.25f), K("rightAlt", 1.25f),
            K("rightMeta", 1.25f), K("contextMenu", 1.25f), K("rightCtrl", 1.25f));
        Row(1.5f, 15.5f, K("insert"), K("home"), K("pageUp"));
        Row(2.5f, 15.5f, K("delete"), K("end"), K("pageDown"));
        Row(4.5f, 16.5f, K("upArrow"));
        Row(5.5f, 15.5f, K("leftArrow"), K("downArrow"), K("rightArrow"));
        return caps.ToArray();
    }

    // ---------------------------------------------------------------- text

    /// <summary>A text broken into lines no wider than a width, at word boundaries, measured in the menu's font.</summary>
    /// <summary>
    /// The lines of a line of what was done before its warning: a second paragraph, after a line break, is a warning,
    /// drawn on a line of its own and in red (the user's, 11 November 2026).
    /// </summary>
    private int FirstParagraphLines(string text, float size, float width)
    {
        int nl = text?.IndexOf('\n') ?? -1;
        return nl < 0 ? int.MaxValue : Wrap(text.Substring(0, nl), size, width).Count;
    }

    /// <summary>A text cut into lines of a width; a line break starts a new line.</summary>
    private List<string> Wrap(string text, float size, float width)
    {
        List<string> lines = new List<string>();
        int nl = text.IndexOf('\n');
        if (nl >= 0)
        {
            lines.AddRange(Wrap(text.Substring(0, nl), size, width));
            lines.AddRange(Wrap(text.Substring(nl + 1), size, width));
            return lines;
        }
        string current = "";
        foreach (string word in text.Split(' '))
        {
            string tried = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && menu.MeasureText(tried, size) > width)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = tried;
            }
        }
        if (current.Length > 0)
        {
            lines.Add(current);
        }
        return lines;
    }

    private static Color Faint(Color c) => new Color(c.r, c.g, c.b, c.a * 0.5f);
}
