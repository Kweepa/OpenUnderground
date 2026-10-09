using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[System.Serializable]
public class MessageClip
{
    public string contains;
    public AudioClip clip;
}

public class Messages : MonoBehaviour
{
    public GUIStyle fontStyle;
    public AudioClip newMessage;

    public MessageClip[] clips;

    private static Messages sMessages;
    private AudioSource currentSource;

    struct SDisplayMessage
    {
        public string message;
        public float time;
    }

    private List<SDisplayMessage> messages = new ();

    struct SPendingMessage
    {
        public string message;
        public float time;
        public float delay;
    }

    /// <summary>Messages waiting for their delay to run out, in the order they were added.</summary>
    private List<SPendingMessage> pendingMessages = new ();

    /// <summary>While above zero, Add() holds every message back by this many seconds.</summary>
    private static float sAddDelay;

    [Tooltip("How many past messages PageUp/PageDown can scroll back through.")]
    public int historyCapacity = 100;

    /// <summary>
    /// Scrollback of the last <see cref="historyCapacity"/> messages, oldest at index 0.
    /// </summary>
    /// <remarks>
    /// A plain shifting array rather than a ring buffer: when it is full it copies the whole
    /// thing down by one, which happens at most once per message and costs nothing, and in
    /// exchange index 0 stays the oldest line so the window arithmetic in OnGUI is trivial.
    /// </remarks>
    private string[] historyLines;
    private int historyStored;

    /// <summary>
    /// Serial number of historyLines[0]. Each message recorded takes the next one, so a message
    /// keeps its number while older ones drop off the front of the buffer, and the view can be
    /// pinned to a message instead of to an index that shifts.
    /// </summary>
    private int historyFirstSerial;

    /// <summary>True while the history is on screen instead of the live messages.</summary>
    private bool historyOpen;

    /// <summary>
    /// Where the view sits: the bottom line on screen is line historyAnchorLine of the message
    /// numbered historyAnchorSerial, as wrapped in the box. Pinned to a message, a message arriving
    /// while you read does not move the text, and a new width keeps you on the same message.
    /// </summary>
    private int historyAnchorSerial;
    private int historyAnchorLine;

    /// <summary>
    /// Scrolling asked for by Update(), in steps: positive goes back, negative forward. Applied in
    /// OnGUI(), the only place that knows the width of the box and so how the messages wrap.
    /// </summary>
    private int historyPendingSteps;
    private bool historyOpenRequested;

    /// <summary>The history wrapped to the box: every line on screen, oldest first.</summary>
    private readonly List<string> wrappedLines = new ();

    /// <summary>Index in wrappedLines of each message's first line, plus one past the end.</summary>
    private readonly List<int> wrappedFirst = new ();
    private float wrappedWidth = -1.0f;
    private bool wrappedDirty = true;

    /// <summary>The message style with no word wrap, to measure the lines and draw them.</summary>
    private GUIStyle lineStyle;

    /// <summary>The live messages wrapped to the box, rebuilt on every repaint.</summary>
    private readonly List<string> liveLines = new ();

    private float historyRepeatTimer;

    /// <summary>Messages shown at once in the live view.</summary>
    private const int LiveMessageCount = 4;

    /// <summary>
    /// Messages kept while scrolled back. Higher than the live cap so that little is lost during
    /// a long browse, but the four newest are still all that reappear on returning to the live
    /// view; the rest stay readable in the history, which is the point of the feature.
    /// </summary>
    private const int BrowsingMessageCount = 40;

    private const float HistoryRepeatDelay = 0.35f;
    private const float HistoryRepeatInterval = 0.10f;

    /// <summary>
    /// Lines the history shows at once. The live view sizes its box to the text, and four messages
    /// that wrap can take a dozen lines there; the history keeps one fixed height.
    /// </summary>
    private const int HistoryLineCount = 8;

    /// <summary>
    /// Lines one press scrolls: less than the box holds, so the lines at the edge stay in sight.
    /// Lines, not messages - a message that wraps counts once for each line it takes.
    /// </summary>
    private const int HistoryScrollStep = 6;

    /// <summary>The box as tall as the history draws it: HistoryLineCount lines.</summary>
    private static readonly string FullBoxSample = "A" + string.Concat(System.Linq.Enumerable.Repeat("\nA", HistoryLineCount - 1));

    private const float BoxPadLeft = 10.0f;

    /// <summary>The right margin is wider, to hold the scroll arrows of the history.</summary>
    private const float BoxPadRight = 20.0f;
    private const float BoxPadY = 6.0f;
    private const float BoxBorder = 2.0f;

    /// <summary>Space between the scroll arrows and the border, at the side and at the top or bottom.</summary>
    private const float ArrowGap = 2.0f;

    /// <summary>
    /// Box width on a 1280 wide screen, the narrowest the game is laid out for: centred there, it
    /// stays 10 pixels clear of the bars of the timed spells, which end at x 312 whatever the
    /// resolution (Magic.OnGUI(): HudRowX 94, then 68 to the bar and 150 of bar). A narrower
    /// window keeps this width; a wider one widens the box as much as the screen, so the gap
    /// stays 10 pixels.
    /// </summary>
    private const float MinBoxWidth = 1280 - 2 * (312 + 10);

    /// <summary>The width the box reaches at 1920, and keeps on any wider screen.</summary>
    private const float MaxBoxWidth = 1920 - 2 * (312 + 10);

    /// <summary>
    /// Seconds of no scrolling before the history closes itself and the live view comes back.
    /// </summary>
    /// <remarks>
    /// Browsing freezes the message countdowns, so without this the box stays on screen for as
    /// long as you leave it there, in front of the game. The seven seconds a live message lasts.
    /// </remarks>
    private const float HistoryIdleTimeout = 7.0f;

    private float historyIdleTimer;

    protected void Start()
    {
        sMessages = this;
    }

    protected void Update()
    {
        // BeginDelay() and EndDelay() come in pairs within one call, so by now the delay is off;
        // this only matters if a spell threw in between, which would leave every message late.
        sAddDelay = 0.0f;

        // Game time, not real time: with timeScale at 0 - paused, on the map, in a conversation,
        // on the save screen - a delayed message waits too, instead of turning up behind it.
        for (int i = 0; i < pendingMessages.Count; )
        {
            SPendingMessage pending = pendingMessages[i];
            pending.delay -= Time.deltaTime;
            if (pending.delay <= 0.0f)
            {
                pendingMessages.RemoveAt(i);
                AddMessageInternal(pending.message, pending.time);
            }
            else
            {
                pendingMessages[i] = pending;
                ++i;
            }
        }

        UpdateHistoryScrolling();

        // While scrolled back, hold more than the live cap so a burst of combat messages is not
        // thrown away behind your back.
        while (messages.Count > (historyOpen ? BrowsingMessageCount : LiveMessageCount))
        {
            messages.RemoveAt(0);
        }

        // Freeze the countdowns while browsing: text must not expire out from under the reader.
        if (historyOpen)
        {
            return;
        }

        for (int i = messages.Count - 1; i >= 0; --i)
        {
            SDisplayMessage mess = messages[i];
            mess.time -= Time.deltaTime;
            if (mess.time < 0.0f)
            {
                messages.RemoveAt(i);
            }
            else
            {
                messages[i] = mess;
            }
        }
    }

    /// <summary>
    /// PageUp and PageDown scroll the message history, with key repeat while held.
    /// </summary>
    /// <remarks>
    /// Uses unscaledDeltaTime, not deltaTime: browsing is a UI action and has to keep working
    /// when timeScale is 0, which it is on the map, in conversations and on the save screen.
    /// The presses are only counted here and applied in OnGUI(), which knows how the messages
    /// wrap. With nothing stored PageUp does nothing rather than opening an empty history.
    /// </remarks>
    private void UpdateHistoryScrolling()
    {
        // The virtual keyboard and conversations both write into this same message area, so
        // leave the input to them while either is up.
        bool blocked = (KeyboardGUI.sKeyboard != null && KeyboardGUI.sKeyboard.IsVisible())
            || Conversations.runningConversation != null;

        Keyboard keyboard = GameInput.CurrentKeyboard;

        // The d-pad is shared: the panels use it for their cursors, the map for its pages, the
        // quantity prompt for its count. controlsActive is exactly "nothing else has the
        // controls", so it is the condition under which the d-pad is ours. PageUp and PageDown
        // need no such test - nothing else in the game reads them - which is why the keyboard can
        // scroll the log with a panel open and the gamepad cannot.
        Gamepad pad = PlayerObject.Player != null && PlayerObject.Player.controlsActive
            ? GameInput.CurrentGamepad
            : null;

        if (blocked || (keyboard == null && pad == null))
        {
            // Drop back to the live view rather than leaving a frozen box on screen: a
            // conversation is about to write here itself, and with no device that can scroll
            // there is no way out of the history.
            CloseHistory();
            historyIdleTimer = 0.0f;
            return;
        }

        bool upPressed = (keyboard?.pageUpKey.wasPressedThisFrame ?? false)
            || (pad?.dpad.up.wasPressedThisFrame ?? false);
        bool downPressed = (keyboard?.pageDownKey.wasPressedThisFrame ?? false)
            || (pad?.dpad.down.wasPressedThisFrame ?? false);
        bool up = (keyboard?.pageUpKey.isPressed ?? false) || (pad?.dpad.up.isPressed ?? false);
        bool down = (keyboard?.pageDownKey.isPressed ?? false) || (pad?.dpad.down.isPressed ?? false);
        int step = 0;

        if (upPressed)
        {
            step = 1;
            historyRepeatTimer = HistoryRepeatDelay;
        }
        else if (downPressed)
        {
            step = -1;
            historyRepeatTimer = HistoryRepeatDelay;
        }
        else if (up || down)
        {
            historyRepeatTimer -= Time.unscaledDeltaTime;
            if (historyRepeatTimer <= 0.0f)
            {
                step = up ? 1 : -1;
                historyRepeatTimer = HistoryRepeatInterval;
            }
        }
        else
        {
            historyRepeatTimer = 0.0f;
        }

        // Opening the history lands on the newest lines, whatever the step size is: going back a
        // step from the live view would skip straight past them.
        if (step > 0 && !historyOpen)
        {
            if (historyStored > 0)
            {
                historyOpen = true;
                historyOpenRequested = true;
            }
        }
        else if (historyOpen)
        {
            historyPendingSteps += step;
        }

        if (step != 0 || up || down)
        {
            historyIdleTimer = HistoryIdleTimeout;
        }
        else if (historyOpen)
        {
            // unscaledDeltaTime for the same reason as the key repeat above: the history has to
            // time out on the map and in the save screen too, where timeScale is 0.
            historyIdleTimer -= Time.unscaledDeltaTime;
            if (historyIdleTimer <= 0.0f)
            {
                CloseHistory();
            }
        }
    }

    private void CloseHistory()
    {
        historyOpen = false;
        historyOpenRequested = false;
        historyPendingSteps = 0;
    }

    protected void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Messages;

        if (PlayerObject.HudHidden)
        {
            return;
        }

        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        lineStyle ??= new GUIStyle(fontStyle) { wordWrap = false };

        // The box is centred, widens with the screen from MinBoxWidth at 1280 to MaxBoxWidth at
        // 1920, and ends just above the row of flasks, rune shelf and worn spells.
        float boxWidth = Mathf.Clamp(MinBoxWidth + (Screen.width - 1280), MinBoxWidth, MaxBoxWidth);
        float textWidth = boxWidth - BoxPadLeft - BoxPadRight;
        float boxX = (Screen.width - boxWidth) * 0.5f;
        float boxBottom = Magic.HudRowTop - 4;

        bool moreAbove = false;
        bool moreBelow = false;
        string allMessages = historyOpen && historyStored > 0 ? HistoryWindow(textWidth, out moreAbove, out moreBelow) : null;
        bool browsing = allMessages != null;
        float boxHeight;
        if (browsing)
        {
            // The history is always as tall as its HistoryLineCount lines, however few there are.
            boxHeight = lineStyle.CalcHeight(new GUIContent(FullBoxSample), textWidth) + 2.0f * BoxPadY;
        }
        else
        {
            liveLines.Clear();
            for (int i = 0; i < messages.Count; ++i)
            {
                WrapInto(messages[i].message, textWidth, liveLines);
            }
            allMessages = string.Join("\n", liveLines).TrimEnd('\n');

            // Without this an empty box would sit on screen whenever there are no messages. A
            // label with empty text was simply invisible before.
            if (allMessages.Length == 0)
            {
                return;
            }
            boxHeight = 0.0f;
        }

        // The lines are already broken to the box, so the height is theirs. The bottom edge is
        // fixed: the text sits on it, and the live box grows upward as messages arrive and
        // shrinks back down as they expire.
        float textHeight = lineStyle.CalcHeight(new GUIContent(allMessages), textWidth);
        boxHeight = Mathf.Max(boxHeight, textHeight + 2.0f * BoxPadY);
        Rect boxRect = new Rect(boxX, boxBottom - boxHeight, boxWidth, boxHeight);

        Color previousGuiColor = GUI.color;
        GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.2f);
        GUI.DrawTexture(boxRect, Texture2D.whiteTexture);
        GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.7f);
        GUI.DrawTexture(new Rect(boxRect.x, boxRect.y, boxRect.width, BoxBorder), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(boxRect.x, boxRect.yMax - BoxBorder, boxRect.width, BoxBorder), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(boxRect.x, boxRect.y, BoxBorder, boxRect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(boxRect.xMax - BoxBorder, boxRect.y, BoxBorder, boxRect.height), Texture2D.whiteTexture);
        GUI.color = previousGuiColor;

        Rect textRect = new Rect(boxX + BoxPadLeft, boxBottom - BoxPadY - textHeight, textWidth, textHeight);
        Color previousTextColor = lineStyle.normal.textColor;
        lineStyle.normal.textColor = new Color(0, 0, 0, 1.0f);
        GUI.Label(textRect, allMessages, lineStyle);
        // Pale yellow while browsing: without a cue there is no telling history from live text.
        lineStyle.normal.textColor = browsing ? new Color(1.0f, 0.85f, 0.4f, 1.0f) : Color.white;
        GUI.Label(new Rect(textRect.x + 1.0f, textRect.y - 1.0f, textRect.width, textRect.height), allMessages, lineStyle);
        lineStyle.normal.textColor = previousTextColor;

        DrawScrollArrows(boxRect, moreAbove, moreBelow);
    }

    /// <summary>
    /// The lines of the history on screen, after applying the scrolling Update() asked for; null
    /// when a step forward from the newest lines has closed the history.
    /// </summary>
    private string HistoryWindow(float textWidth, out bool moreAbove, out bool moreBelow)
    {
        moreAbove = false;
        moreBelow = false;
        if (wrappedDirty || textWidth != wrappedWidth)
        {
            WrapHistory(textWidth);
        }

        int last = wrappedLines.Count - 1;
        // the bottom line of the oldest window, the furthest back the view goes
        int oldest = Mathf.Min(HistoryLineCount, wrappedLines.Count) - 1;

        int bottom;
        int message = historyAnchorSerial - historyFirstSerial;
        if (historyOpenRequested || message >= historyStored)
        {
            bottom = last;
        }
        else if (message < 0)
        {
            // the message the view was on has dropped off the front of the buffer
            bottom = oldest;
        }
        else
        {
            bottom = Mathf.Min(wrappedFirst[message] + historyAnchorLine, wrappedFirst[message + 1] - 1);
        }
        historyOpenRequested = false;

        for (; historyPendingSteps > 0; --historyPendingSteps)
        {
            bottom -= HistoryScrollStep;
        }
        for (; historyPendingSteps < 0; ++historyPendingSteps)
        {
            if (bottom >= last)
            {
                CloseHistory();
                return null;
            }
            bottom = Mathf.Min(bottom + HistoryScrollStep, last);
        }
        bottom = Mathf.Max(bottom, oldest);

        // pin the view to the message that holds its bottom line
        int found = wrappedFirst.BinarySearch(bottom);
        int holder = found >= 0 ? found : ~found - 1;
        historyAnchorSerial = historyFirstSerial + holder;
        historyAnchorLine = bottom - wrappedFirst[holder];

        int top = Mathf.Max(0, bottom - (HistoryLineCount - 1));
        moreAbove = top > 0;
        moreBelow = bottom < last;
        return string.Join("\n", wrappedLines.GetRange(top, bottom - top + 1));
    }

    /// <summary>
    /// Breaks every message of the history into the lines it takes in the box. Done again only
    /// when a message arrives, the history is cleared or the width changes.
    /// </summary>
    private void WrapHistory(float textWidth)
    {
        wrappedLines.Clear();
        wrappedFirst.Clear();
        for (int i = 0; i < historyStored; ++i)
        {
            wrappedFirst.Add(wrappedLines.Count);
            WrapInto(historyLines[i], textWidth, wrappedLines);
        }
        wrappedFirst.Add(wrappedLines.Count);
        wrappedWidth = textWidth;
        wrappedDirty = false;
    }

    /// <summary>
    /// Breaks a message into the lines it takes in a box textWidth wide, at its own newlines and
    /// between words, and adds them to lines. A single word wider than the box keeps a line of
    /// its own. The live view and the history both wrap here, so a message breaks in the same
    /// places in both and the text keeps the same right margin; Unity's own word wrap breaks
    /// a little differently.
    /// </summary>
    private void WrapInto(string message, float textWidth, List<string> lines)
    {
        foreach (string paragraph in message.TrimEnd().Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' '))
            {
                string longer = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && lineStyle.CalcSize(new GUIContent(longer)).x > textWidth)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = longer;
                }
            }
            lines.Add(line);
        }
    }

    /// <summary>
    /// The up and down arrows of the original's cursors, at the size the quantity prompt draws
    /// its left and right ones, in the right margin of the box: up in the top corner when there
    /// is more to read further back, down in the bottom corner when there is more further on.
    /// </summary>
    private static void DrawScrollArrows(Rect box, bool up, bool down)
    {
        Texture2D[] cursors = DataLoader.sDataLoader != null ? DataLoader.sDataLoader.cursorTex : null;
        if (cursors == null || cursors.Length <= 2)
        {
            return;
        }

        Texture2D upArrow = cursors[1];
        if (up && upArrow != null)
        {
            float width = 1.5f * upArrow.width;
            GUI.DrawTexture(new Rect(box.xMax - BoxBorder - ArrowGap - width, box.y + BoxBorder + ArrowGap + 1,
                width, 1.5f * upArrow.height), upArrow);
        }

        Texture2D downArrow = cursors[2];
        if (down && downArrow != null)
        {
            float width = 1.5f * downArrow.width;
            float height = 1.5f * downArrow.height;
            GUI.DrawTexture(new Rect(box.xMax - BoxBorder - ArrowGap - width, box.yMax - BoxBorder - ArrowGap - 1 - height,
                width, height), downArrow);
        }
    }

    public static void Add(string messageText, float time = 7.0f)
    {
        if (sAddDelay > 0.0f)
        {
            AddLater(messageText, sAddDelay, time);
        }
        else
        {
            sMessages.AddMessageInternal(messageText, time);
        }
    }

    /// <summary>
    /// Shows a message once delay seconds of game time have passed, on screen and in its place
    /// among the others, so it can follow a line that is printed after it is added.
    /// </summary>
    public static void AddLater(string messageText, float delay, float time = 7.0f)
    {
        sMessages.pendingMessages.Add(new SPendingMessage { message = messageText, time = time, delay = delay });
    }

    /// <summary>Until EndDelay(), every message added comes out delay seconds later.</summary>
    public static void BeginDelay(float delay)
    {
        sAddDelay = delay;
    }

    public static void EndDelay()
    {
        sAddDelay = 0.0f;
    }

    public static void Add(int block, int index)
    {
        Add(StringLoader.GetString(block, index));
    }

    /// <summary>
    /// Clears the messages currently on screen. The scrollback is deliberately left alone: this
    /// is called when a conversation starts, and losing the log at that point would defeat it.
    /// </summary>
    public static void Clear()
    {
        sMessages.messages.Clear();
        sMessages.pendingMessages.Clear();
    }

    /// <summary>
    /// Clears the screen and the scrollback both. Called when a game is loaded: a load in the
    /// middle of a game keeps this scene, so without it Page Up would scroll back into the game
    /// just left.
    /// </summary>
    public static void ClearAll()
    {
        if (sMessages == null)
        {
            return;
        }
        Clear();
        sMessages.historyStored = 0;
        sMessages.wrappedDirty = true;
        sMessages.CloseHistory();
    }

    private void RecordInHistory(string messageText)
    {
        // Allocated lazily, and resized in place if the capacity is edited in the inspector at
        // runtime, so the log survives the change.
        int capacity = Mathf.Max(1, historyCapacity);
        if (historyLines == null || historyLines.Length != capacity)
        {
            System.Array.Resize(ref historyLines, capacity);
            historyStored = Mathf.Min(historyStored, historyLines.Length);
        }

        if (historyStored >= historyLines.Length)
        {
            for (int i = 1; i < historyLines.Length; ++i)
            {
                historyLines[i - 1] = historyLines[i];
            }
            historyStored = historyLines.Length - 1;
            ++historyFirstSerial;
        }

        historyLines[historyStored] = messageText.TrimEnd();
        ++historyStored;

        // The view is pinned to a message, so the text on screen stays put; only the wrapping has
        // to take the new message in.
        wrappedDirty = true;
    }

    void AddMessageInternal(string messageText, float time)
    {
        float actualTime = time;
        
        AudioClip actualClip = newMessage;
        bool randomizePitch = true;
        
        #if false
        foreach (var clip in clips)
        {
            if (messageText.Contains(clip.contains))
            {
                if (clip.clip != null)
                {
                    actualTime = clip.clip.length;
                    actualClip = clip.clip;
                    randomizePitch = false;

                    if (currentSource != null)
                    {
                        currentSource.Stop();
                        Destroy(currentSource.transform.gameObject);
                    }
                }
                else
                {
                    Debug.LogWarning($"Messages clip list has missing clip reference for '{clip.contains}'.");
                }
                break;
            }
        }
        #endif

        currentSource = Utils.PlayClip2d(actualClip, randomizePitch);

        RecordInHistory(messageText);

        SDisplayMessage mess = new SDisplayMessage { message = messageText, time = actualTime };
        messages.Add(mess);
    }
}
