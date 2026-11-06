using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The game menu's Monitor, Display mode and Resolution rows: values that are the screen's own
/// state, read from <see cref="Screen"/> and set through it, not numbers in PlayerPrefs keys.
/// <see cref="GameOptions"/> hands the three rows here.
/// </summary>
/// <remarks>
/// Three modes (the user's of 6 November 2026). Fullscreen (borderless), <see cref="FullScreenMode.FullScreenWindow"/>:
/// a window covering the desktop at its own size and refresh rate, which only Windows can change; Alt-Tab is
/// instant. Fullscreen (exclusive), <see cref="FullScreenMode.ExclusiveFullScreen"/>: the monitor switches to the
/// size chosen, from its own modes (<see cref="Screen.resolutions"/>) up to its largest, at the highest rate it has
/// for that size, and its own settings decide whether another shape gets bands, is zoomed or stretched. Windowed:
/// any size, so its sizes are common ones, generated.
///
/// Every size carries its shape. A list holds the monitor's own shape in full and one size of each common shape,
/// the widest shape first and each from the largest size down; 21:9 only on a 21:9 monitor; nothing under
/// 1280x720, where the small-window warning starts, but for a default window where nothing else fits. "(desktop)"
/// marks Windows' own size, "(native)" the monitor's largest, "(scaled)" a size under 1366x768, where the
/// interface is drawn smaller (<see cref="UiScale"/>), "(last)" the size the window was last dragged to.
///
/// The desktop's size is read from <see cref="Screen.mainWindowDisplayInfo"/> and <see cref="Screen.GetDisplayLayout"/>,
/// never from <see cref="Screen.currentResolution"/>, which in Fullscreen below the desktop's size returns the
/// size drawn.
///
/// A change waits for the rows to be still a second, so stepping with left and right switches the screen once; a
/// choice from the drop-down applies at once; then the menu asks to keep it, and without an answer it goes back
/// after 10 seconds. A reset applies at once and asks the same; Undo changes applies at once and does not ask.
/// Each mode remembers its size, exclusive per monitor, and the fullscreen kind used last is remembered too; at
/// the start they are set again, and Alt-Enter, which Unity handles and always takes to borderless, is followed
/// to the remembered kind and size.
/// </remarks>
[DefaultExecutionOrder(-100)]
public class ScreenOptions : MonoBehaviour
{
    /// <summary>The rows' keys in the table: not PlayerPrefs keys, as the music page's is not.</summary>
    public const string MonitorKey = "screen:monitor";
    public const string ModeKey = "screen:mode";
    public const string ResolutionKey = "screen:resolution";

    /// <summary>The exclusive size chosen on each monitor, "name=1920x1080|..."; none for a monitor's largest.</summary>
    public const string FullscreenSizeKey = "Options_FullscreenSize";
    /// <summary>The window's last size, "1600x900", the one it comes back at; empty before the game was first windowed.</summary>
    public const string WindowSizeKey = "Options_WindowSize";
    /// <summary>The last size the window was dragged to, none of the list's: the list's "(last)", kept when a listed size is chosen.</summary>
    public const string DraggedWindowKey = "Options_WindowDraggedSize";
    /// <summary>The monitor the game was last kept on; none at a first start, which goes to the main monitor.</summary>
    public const string MonitorPrefsKey = "Options_Monitor";
    /// <summary>Unity's own key for the last mode; written by Unity at the start, so it cannot tell a first start (log only).</summary>
    private const string UnityModeKey = "Screenmanager Fullscreen mode";
    /// <summary>The desktop's size of each monitor seen, "name=2560x1440|...": a fallback, should exclusive mode hide it.</summary>
    private const string DesktopSizesKey = "Options_DesktopSizes";

    /// <summary>The three ways of showing the game, in the order of the Display mode row.</summary>
    public enum Kind
    {
        /// <summary>A window covering the desktop at its own size and rate (FullScreenWindow): Alt-Tab is instant.</summary>
        Borderless,
        /// <summary>The monitor switched to the size chosen, up to its largest, at its highest rate (ExclusiveFullScreen).</summary>
        Exclusive,
        Windowed,
    }

    public static readonly string[] ModeNames = { "Fullscreen (borderless)", "Fullscreen (exclusive)", "Windowed" };

    /// <summary>The fullscreen kind used last, "borderless" or "exclusive": what Alt-Enter and Windowed's way back go to.</summary>
    public const string FullscreenKindKey = "Options_FullscreenKind";

    public const float SettleSeconds = 1f;
    public const float ConfirmSeconds = 10f;

    // the interface is drawn smaller below this (UiScale): "(scaled)"
    private const int UnscaledWidth = 1366;
    private const int UnscaledHeight = 768;
    // nothing smaller is listed: the small-window warning's limit
    private const int FloorWidth = 1280;
    private const int FloorHeight = 720;
    // two ratios name the same shape within this share: 1366x768 is 16:9, 3440x1440 is 21:9
    private const float ShapeTolerance = 0.03f;
    // a mode or size just set takes a few frames to show in Screen; meanwhile the rows show what was set
    private const float QuietSeconds = 1f;

    /// <summary>A shape with a name, as the lists write it; its order here is its order in a list.</summary>
    private sealed class Shape
    {
        public readonly string Name;
        public readonly float Ratio;
        /// <summary>Common sizes, for a window; none where the shape is offered only as a monitor's own.</summary>
        public readonly Vector2Int[] Sizes;

        public Shape(string name, float ratio, params Vector2Int[] sizes)
        {
            Name = name;
            Ratio = ratio;
            Sizes = sizes;
        }
    }

    private static readonly Shape[] Shapes =
    {
        new Shape("5:4", 5f / 4f),
        new Shape("4:3", 4f / 3f, new Vector2Int(1280, 960), new Vector2Int(1400, 1050), new Vector2Int(1600, 1200),
            new Vector2Int(1920, 1440), new Vector2Int(2048, 1536)),
        new Shape("3:2", 3f / 2f),
        new Shape("16:10", 16f / 10f, new Vector2Int(1280, 800), new Vector2Int(1440, 900), new Vector2Int(1680, 1050),
            new Vector2Int(1920, 1200), new Vector2Int(2560, 1600), new Vector2Int(3840, 2400)),
        new Shape("16:9", 16f / 9f, new Vector2Int(1280, 720), new Vector2Int(1366, 768), new Vector2Int(1600, 900),
            new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3200, 1800), new Vector2Int(3840, 2160)),
        new Shape("21:9", 64f / 27f, new Vector2Int(1720, 720), new Vector2Int(2560, 1080), new Vector2Int(3440, 1440),
            new Vector2Int(3840, 1600), new Vector2Int(5120, 2160)),
        new Shape("32:9", 32f / 9f),
    };

    // Offered on every monitor; any other shape only as the monitor's own (21:9 among them, the user's rule).
    private static readonly string[] CommonShapes = { "4:3", "16:10", "16:9" };

    // A window where none of the list's sizes fits (a Steam Deck, a 1280x1024 monitor): the largest of these smaller than the monitor.
    private static readonly Vector2Int[] DefaultWindows =
    {
        new Vector2Int(1280, 720), new Vector2Int(1152, 648), new Vector2Int(1024, 576), new Vector2Int(960, 540),
        new Vector2Int(854, 480), new Vector2Int(640, 360),
    };

    /// <summary>The screen's monitor, mode and size.</summary>
    public readonly struct ScreenSetup : IEquatable<ScreenSetup>
    {
        public readonly string Monitor;
        public readonly Kind Kind;
        public readonly int Width;
        public readonly int Height;

        public ScreenSetup(string monitor, Kind kind, int width, int height)
        {
            Monitor = monitor ?? "";
            Kind = kind;
            Width = width;
            Height = height;
        }

        public bool Windowed => Kind == Kind.Windowed;

        public Vector2Int Size => new Vector2Int(Width, Height);

        public ScreenSetup With(Vector2Int size) => new ScreenSetup(Monitor, Kind, size.x, size.y);

        public bool Equals(ScreenSetup d) => Monitor == d.Monitor && Kind == d.Kind && Width == d.Width && Height == d.Height;
        public override bool Equals(object obj) => obj is ScreenSetup d && Equals(d);
        public override int GetHashCode() => (int)Kind ^ (Width << 2) ^ (Height << 16);
        public override string ToString() => Kind + " " + Width + "x" + Height + " on " + Monitor;
    }

    /// <summary>The setup and the two remembered sizes, as Undo changes and a No put them back.</summary>
    public readonly struct Saved : IEquatable<Saved>
    {
        public readonly ScreenSetup Setup;
        public readonly string FullscreenSize;
        public readonly string WindowSize;
        public readonly string FullscreenKind;

        public Saved(ScreenSetup setup, string fullscreenSize, string windowSize, string fullscreenKind)
        {
            Setup = setup;
            FullscreenSize = fullscreenSize;
            WindowSize = windowSize;
            FullscreenKind = fullscreenKind;
        }

        public bool Equals(Saved s) =>
            Setup.Equals(s.Setup) && FullscreenSize == s.FullscreenSize && WindowSize == s.WindowSize && FullscreenKind == s.FullscreenKind;
        public override bool Equals(object obj) => obj is Saved s && Equals(s);
        public override int GetHashCode() => Setup.GetHashCode();
    }

    /// <summary>One entry of the Resolution row: a size, and the note after it.</summary>
    private readonly struct Entry
    {
        public readonly Vector2Int Size;
        public readonly string Tag;

        public Entry(Vector2Int size, string tag)
        {
            Size = size;
            Tag = tag;
        }
    }

    private static bool pending;
    private static float settleAt;
    private static ScreenSetup target;
    private static bool awaiting;
    private static float confirmEndsAt;
    private static Saved before;
    private static ScreenSetup applied;
    private static float quietUntil = -1f;
    private static AsyncOperation moving;
    private static ScreenSetup afterMove;

    // read before the first scene, before anything of the game is saved: no game folder saved means a first start
    private static bool installedBefore;

    private static bool lastWindowed;
    private static Vector2Int lastWindowSize;
    private static string lastMonitor;
    // After a change of ours, once Screen has followed: the window put back on its monitor if Windows placed it
    // elsewhere, and the mode and size set again if Unity took only part of them (from exclusive 1280x720 to
    // borderless 2560x1440 it changed the mode and kept 1280x720). Once only.
    private static bool verify;
    private static bool started;
    private static string listsLogged;

    // the rows ask many times a frame: the monitors and the modes are read once a frame
    private static readonly List<DisplayInfo> layout = new List<DisplayInfo>();
    private static int layoutFrame = -1;
    private static Resolution[] modes = Array.Empty<Resolution>();
    private static int modesFrame = -1;
    private static Dictionary<string, Vector2Int> desktopSizes;

    // ---------------------------------------------------------------- monitors

    private static string CleanName(DisplayInfo d) =>
        string.IsNullOrEmpty(d.name) ? "Monitor" : d.name.Replace("|", "/").Replace("=", "-").Replace("#", " ");

    /// <summary>
    /// A monitor's id: its name, and "#2" after the name of a second monitor of the same model. Not its place
    /// on the desktop: Unity's display info gives each monitor's work area from its own corner, 0,0 on every one.
    /// </summary>
    private static string MonitorId(int index)
    {
        List<DisplayInfo> monitors = Monitors();
        string name = CleanName(monitors[index]);
        int before = 0;
        for (int j = 0; j < index; j++)
        {
            before += CleanName(monitors[j]) == name ? 1 : 0;
        }
        return before == 0 ? name : name + "#" + (before + 1);
    }

    /// <summary>Every monitor, in Windows' order; the one the window is on when the layout cannot be read.</summary>
    private static List<DisplayInfo> Monitors()
    {
        if (layoutFrame == Time.frameCount && layout.Count > 0)
        {
            return layout;
        }
        layoutFrame = Time.frameCount;
        layout.Clear();
        try
        {
            Screen.GetDisplayLayout(layout);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Display] no monitor layout: " + e.Message);
        }
        if (layout.Count == 0)
        {
            layout.Add(Screen.mainWindowDisplayInfo);
        }
        return layout;
    }

    private static Resolution[] Modes()
    {
        if (modesFrame != Time.frameCount)
        {
            modesFrame = Time.frameCount;
            modes = Screen.resolutions ?? Array.Empty<Resolution>();
        }
        return modes;
    }

    /// <summary>The place of a monitor in the layout: by its id, then by its name alone, then the first.</summary>
    private static int MonitorIndex(string id)
    {
        List<DisplayInfo> monitors = Monitors();
        for (int i = 0; i < monitors.Count; i++)
        {
            if (MonitorId(i) == id)
            {
                return i;
            }
        }
        string name = id.Split('#')[0];
        for (int i = 0; i < monitors.Count; i++)
        {
            if (CleanName(monitors[i]) == name)
            {
                return i;
            }
        }
        return 0;
    }

    /// <summary>
    /// The place in the layout of the monitor the window is on: by name, and between two of the same model
    /// by size and refresh rate; the first when nothing matches.
    /// </summary>
    private static int CurrentIndex()
    {
        List<DisplayInfo> monitors = Monitors();
        DisplayInfo here = Screen.mainWindowDisplayInfo;
        int byName = -1;
        for (int i = 0; i < monitors.Count; i++)
        {
            if (CleanName(monitors[i]) != CleanName(here))
            {
                continue;
            }
            if (monitors[i].width == here.width && monitors[i].height == here.height
                && Math.Abs(monitors[i].refreshRate.value - here.refreshRate.value) < 0.01)
            {
                return i;
            }
            if (byName < 0)
            {
                byName = i;
            }
        }
        return Mathf.Max(0, byName);
    }

    /// <summary>The monitor the window is on, by the layout's own id for it.</summary>
    private static string CurrentMonitor => MonitorId(CurrentIndex());

    private static bool Exclusive => Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen;

    private static Dictionary<string, Vector2Int> DesktopSizes()
    {
        if (desktopSizes != null)
        {
            return desktopSizes;
        }
        desktopSizes = new Dictionary<string, Vector2Int>();
        foreach (string pair in PlayerPrefs.GetString(DesktopSizesKey, "").Split('|'))
        {
            int eq = pair.LastIndexOf('=');
            if (eq > 0 && TryParse(pair.Substring(eq + 1), out Vector2Int s))
            {
                desktopSizes[pair.Substring(0, eq)] = s;
            }
        }
        return desktopSizes;
    }

    private static void RememberDesktop(string id, Vector2Int size)
    {
        Dictionary<string, Vector2Int> sizes = DesktopSizes();
        if (sizes.TryGetValue(id, out Vector2Int known) && known == size)
        {
            return;
        }
        sizes[id] = size;
        List<string> pairs = new List<string>();
        foreach (KeyValuePair<string, Vector2Int> kv in sizes)
        {
            pairs.Add(kv.Key + "=" + KeyText(kv.Value));
        }
        PlayerPrefs.SetString(DesktopSizesKey, string.Join("|", pairs));
    }

    /// <summary>
    /// The monitor's largest size, "(native)", the top of the exclusive list: the native size Unity reports for the main
    /// monitor, else the largest mode of the monitor the window is on, never below the desktop's; for another monitor,
    /// its desktop's size until the game is there. Modes above it (a driver's downsampling ones) are not offered.
    /// </summary>
    private static Vector2Int Largest(string id)
    {
        Vector2Int desktop = Desktop(id);
        int index = MonitorIndex(id);
        if (index != CurrentIndex())
        {
            return desktop;
        }
        Vector2Int best = desktop;
        if (index == PrimaryIndex() && Display.main != null && Display.main.systemWidth > 0 && Display.main.systemHeight > 0)
        {
            Vector2Int system = new Vector2Int(Display.main.systemWidth, Display.main.systemHeight);
            return system.x * system.y >= best.x * best.y ? system : best;
        }
        foreach (Resolution r in Modes())
        {
            if (r.width * r.height > best.x * best.y)
            {
                best = new Vector2Int(r.width, r.height);
            }
        }
        return best;
    }

    /// <summary>
    /// The desktop's size on a monitor, what borderless Fullscreen covers: the display info's, which stays the
    /// desktop's in exclusive mode too (the Philips read 1280x1024 while exclusive at 1280x800, 6 November 2026).
    /// Should it read nothing, the size last read, or the largest mode.
    /// </summary>
    private static Vector2Int Desktop(string id)
    {
        int index = MonitorIndex(id);
        DisplayInfo d = Monitors()[index];
        if (d.width > 0 && d.height > 0)
        {
            Vector2Int size = new Vector2Int(d.width, d.height);
            RememberDesktop(MonitorId(index), size);
            return size;
        }
        if (DesktopSizes().TryGetValue(MonitorId(index), out Vector2Int remembered))
        {
            return remembered;
        }
        Vector2Int largest = new Vector2Int(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        foreach (Resolution r in Modes())
        {
            if (r.width * r.height > largest.x * largest.y)
            {
                largest = new Vector2Int(r.width, r.height);
            }
        }
        return largest;
    }

    // ---------------------------------------------------------------- shapes and lists

    /// <summary>The named shape of a size, or null: within 3% of its ratio.</summary>
    private static Shape ShapeOf(Vector2Int s)
    {
        if (s.x <= 0 || s.y <= 0)
        {
            return null;
        }
        float r = (float)s.x / s.y;
        foreach (Shape shape in Shapes)
        {
            if (Mathf.Abs(r - shape.Ratio) <= shape.Ratio * ShapeTolerance)
            {
                return shape;
            }
        }
        return null;
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            int t = a % b;
            a = b;
            b = t;
        }
        return Mathf.Max(1, a);
    }

    /// <summary>A shape as written: its name, or the ratio reduced (9:16), or as a decimal when the numbers are large.</summary>
    private static string ShapeText(Vector2Int s)
    {
        Shape shape = ShapeOf(s);
        if (shape != null)
        {
            return shape.Name;
        }
        if (s.x <= 0 || s.y <= 0)
        {
            return "";
        }
        int g = Gcd(s.x, s.y);
        int a = s.x / g;
        int b = s.y / g;
        return a <= 32 && b <= 32
            ? a + ":" + b
            : ((float)s.x / s.y).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ":1";
    }

    /// <summary>
    /// Where a size goes in a list: by its shape's ratio, never by its own, which would put 1366x768 (1.7786) apart
    /// from 1920x1080 (1.7778); an unnamed shape by its own ratio.
    /// </summary>
    private static float OrderOf(Vector2Int s)
    {
        Shape shape = ShapeOf(s);
        return shape != null ? shape.Ratio : (float)s.x / Mathf.Max(1, s.y);
    }

    /// <summary>The user's order of 6 November 2026: the widest shape first (21:9, 16:9, 16:10, 4:3), each from the largest size down.</summary>
    private static void Sort(List<Entry> entries)
    {
        entries.Sort((a, b) =>
        {
            int byShape = OrderOf(b.Size).CompareTo(OrderOf(a.Size));
            return byShape != 0 ? byShape : (b.Size.x * b.Size.y).CompareTo(a.Size.x * a.Size.y);
        });
    }

    private static bool AboveFloor(Vector2Int s) => s.x >= FloorWidth && s.y >= FloorHeight;

    private static bool Offered(Shape shape, Shape own) =>
        shape != null && (shape == own || Array.IndexOf(CommonShapes, shape.Name) >= 0);

    /// <summary>Of each shape not the monitor's, the largest size only.</summary>
    private static void KeepLargestOfOtherShapes(List<Vector2Int> sizes, Shape own)
    {
        Dictionary<Shape, Vector2Int> largest = new Dictionary<Shape, Vector2Int>();
        foreach (Vector2Int s in sizes)
        {
            Shape shape = ShapeOf(s);
            if (shape != null && shape != own && (!largest.TryGetValue(shape, out Vector2Int l) || s.x * s.y > l.x * l.y))
            {
                largest[shape] = s;
            }
        }
        sizes.RemoveAll(s =>
        {
            Shape shape = ShapeOf(s);
            return shape == null || (shape != own && largest[shape] != s);
        });
    }

    /// <summary>Borderless covers the desktop: its size alone.</summary>
    private static List<Entry> BorderlessEntries(string monitor) => new List<Entry> { new Entry(Desktop(monitor), "desktop") };

    /// <summary>
    /// Exclusive on a monitor: its largest size, then the modes it reports no larger, of its own shape (one per
    /// height: 1360x768 and 1366x768 are one) and the largest of each common shape. The modes are those of the
    /// monitor the window is on: for another monitor, its size alone until the game is there.
    /// </summary>
    private static List<Entry> ExclusiveEntries(string monitor)
    {
        Vector2Int native = Largest(monitor);
        Shape own = ShapeOf(native);
        List<Vector2Int> sizes = new List<Vector2Int>();
        if (MonitorIndex(monitor) == MonitorIndex(CurrentMonitor))
        {
            foreach (Resolution r in Modes())
            {
                Vector2Int s = new Vector2Int(r.width, r.height);
                // a mode of the monitor's shape and height is the native size by another name (1360x768 beside 1366x768)
                if (s.x > native.x || s.y > native.y || !AboveFloor(s) || !Offered(ShapeOf(s), own)
                    || (s.y == native.y && ShapeOf(s) == own))
                {
                    continue;
                }
                int same = sizes.FindIndex(o => o.y == s.y && ShapeOf(o) == ShapeOf(s));
                if (same < 0)
                {
                    sizes.Add(s);
                }
                else if (s.x > sizes[same].x)
                {
                    sizes[same] = s;
                }
            }
            KeepLargestOfOtherShapes(sizes, own);
        }
        List<Entry> entries = new List<Entry> { new Entry(native, "native") };
        foreach (Vector2Int s in sizes)
        {
            entries.Add(new Entry(s, null));
        }
        Sort(entries);
        return entries;
    }

    /// <summary>
    /// A window on a monitor: common sizes smaller than the monitor both ways, of its own shape and the largest
    /// of each common shape, none taller than 4:3 nor wider than 21:9; a 16:9 default where none fits.
    /// </summary>
    private static List<Vector2Int> WindowSizes(string monitor)
    {
        Vector2Int native = Desktop(monitor);
        Shape own = ShapeOf(native);
        List<Vector2Int> sizes = new List<Vector2Int>();
        foreach (Shape shape in Shapes)
        {
            if (!Offered(shape, own) || shape.Ratio < 4f / 3f - 0.01f || shape.Ratio > 64f / 27f + 0.01f)
            {
                continue;
            }
            foreach (Vector2Int s in shape.Sizes)
            {
                if (s.x < native.x && s.y < native.y && AboveFloor(s))
                {
                    sizes.Add(s);
                }
            }
        }
        KeepLargestOfOtherShapes(sizes, own);
        if (sizes.Count == 0)
        {
            // a monitor smaller than every default: half of it, never nothing
            Vector2Int fallback = new Vector2Int(Mathf.Max(320, native.x / 4 * 2), Mathf.Max(180, native.y / 4 * 2));
            foreach (Vector2Int s in DefaultWindows)
            {
                if (s.x < native.x && s.y < native.y)
                {
                    fallback = s;
                    break;
                }
            }
            sizes.Add(fallback);
        }
        return sizes;
    }

    /// <summary>The window's list: its last size first, "(last)", then the rest in order.</summary>
    private static List<Entry> WindowEntries(string monitor, Vector2Int? last)
    {
        List<Entry> rest = new List<Entry>();
        foreach (Vector2Int s in WindowSizes(monitor))
        {
            if (!last.HasValue || s != last.Value)
            {
                rest.Add(new Entry(s, null));
            }
        }
        Sort(rest);
        if (last.HasValue)
        {
            rest.Insert(0, new Entry(last.Value, "last"));
        }
        return rest;
    }

    /// <summary>The list the Resolution row walks for this setup, with the setup's own size in it.</summary>
    private static List<Entry> EntriesFor(ScreenSetup d)
    {
        List<Entry> entries = d.Kind == Kind.Windowed ? WindowEntries(d.Monitor, LastWindow(d))
            : d.Kind == Kind.Exclusive ? ExclusiveEntries(d.Monitor) : BorderlessEntries(d.Monitor);
        if (d.Width > 0 && d.Height > 0 && entries.FindIndex(e => e.Size == d.Size) < 0)
        {
            // a mode the lists do not hold, set on the command line: shown as it is
            entries.Add(new Entry(d.Size, null));
            Sort(entries);
        }
        return entries;
    }

    /// <summary>
    /// The list's "(last)": the last size the window was dragged to, if it fits this monitor; none before the
    /// first drag. Choosing a listed size leaves it as it is, so the dragged size can be chosen back.
    /// </summary>
    private static Vector2Int? LastWindow(ScreenSetup shown)
    {
        Vector2Int native = Desktop(shown.Monitor);
        if (TryParse(PlayerPrefs.GetString(DraggedWindowKey, ""), out Vector2Int s) && s.x <= native.x && s.y <= native.y)
        {
            return s;
        }
        return null;
    }

    private static string EntryText(Entry e)
    {
        List<string> notes = new List<string>();
        if (!string.IsNullOrEmpty(e.Tag))
        {
            notes.Add(e.Tag);
        }
        if (e.Size.x < UnscaledWidth || e.Size.y < UnscaledHeight)
        {
            notes.Add("scaled");
        }
        string text = e.Size.x + "x" + e.Size.y + " " + ShapeText(e.Size);
        return notes.Count > 0 ? text + " (" + string.Join(", ", notes) + ")" : text;
    }

    private static string KeyText(Vector2Int s) => s.x + "x" + s.y;

    private static bool TryParse(string text, out Vector2Int size)
    {
        size = Vector2Int.zero;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        string[] parts = text.Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 0 && h > 0)
        {
            size = new Vector2Int(w, h);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The exclusive size remembered for one monitor, "1920x1080", or empty for its largest. The key holds one per
    /// monitor, "AORUS CV27Q=1920x1080|Philips 19SL=1280x720": a size chosen on one monitor is not taken to another.
    /// </summary>
    private static string FullscreenSizeOf(string monitor)
    {
        foreach (string pair in PlayerPrefs.GetString(FullscreenSizeKey, "").Split('|'))
        {
            int eq = pair.LastIndexOf('=');
            if (eq > 0 && pair.Substring(0, eq) == monitor)
            {
                return pair.Substring(eq + 1);
            }
        }
        return "";
    }

    private static void SetFullscreenSizeOf(string monitor, string size)
    {
        List<string> pairs = new List<string>();
        foreach (string pair in PlayerPrefs.GetString(FullscreenSizeKey, "").Split('|'))
        {
            int eq = pair.LastIndexOf('=');
            // a size of an earlier test build, with no monitor, is dropped
            if (eq > 0 && pair.Substring(0, eq) != monitor)
            {
                pairs.Add(pair);
            }
        }
        if (!string.IsNullOrEmpty(size))
        {
            pairs.Add(monitor + "=" + size);
        }
        PlayerPrefs.SetString(FullscreenSizeKey, string.Join("|", pairs));
    }

    /// <summary>The size exclusive mode opens at: the one remembered for this monitor, if it lists it, or its largest.</summary>
    private static Vector2Int ExclusiveTarget(string monitor)
    {
        if (TryParse(FullscreenSizeOf(monitor), out Vector2Int s)
            && ExclusiveEntries(monitor).FindIndex(e => e.Size == s) >= 0)
        {
            return s;
        }
        return Largest(monitor);
    }

    /// <summary>The fullscreen kind used last, borderless until exclusive is chosen.</summary>
    private static Kind FullscreenKind => PlayerPrefs.GetString(FullscreenKindKey, "") == "exclusive" ? Kind.Exclusive : Kind.Borderless;

    /// <summary>The size a mode opens at on a monitor.</summary>
    private static Vector2Int TargetSize(string monitor, Kind kind) =>
        kind == Kind.Windowed ? WindowTarget(monitor) : kind == Kind.Exclusive ? ExclusiveTarget(monitor) : Desktop(monitor);

    /// <summary>A mode's default size: the desktop's, the monitor's largest, the largest window that fits.</summary>
    private static Vector2Int DefaultSize(string monitor, Kind kind) =>
        kind == Kind.Windowed ? WindowDefault(monitor) : kind == Kind.Exclusive ? Largest(monitor) : Desktop(monitor);

    /// <summary>Windowed's default: the largest of the monitor's own shape, else the largest 16:9, else the largest.</summary>
    private static Vector2Int WindowDefault(string monitor)
    {
        List<Vector2Int> sizes = WindowSizes(monitor);
        Shape own = ShapeOf(Desktop(monitor));
        Vector2Int best = sizes[0];
        int bestRank = -1;
        foreach (Vector2Int s in sizes)
        {
            Shape shape = ShapeOf(s);
            int rank = shape != null && shape == own ? 2 : shape != null && shape.Name == "16:9" ? 1 : 0;
            if (rank > bestRank || (rank == bestRank && s.x * s.y > best.x * best.y))
            {
                best = s;
                bestRank = rank;
            }
        }
        return best;
    }

    /// <summary>The size a window opens at: its last, if it fits this monitor, or the default.</summary>
    private static Vector2Int WindowTarget(string monitor)
    {
        Vector2Int native = Desktop(monitor);
        if (TryParse(PlayerPrefs.GetString(WindowSizeKey, ""), out Vector2Int s) && s.x <= native.x && s.y <= native.y)
        {
            return s;
        }
        return WindowDefault(monitor);
    }

    /// <summary>Called when the menu opens: the screen and the lists written to the log when they change.</summary>
    public static void Scan()
    {
        ScreenSetup d = Shown;
        List<string> monitors = new List<string>();
        for (int i = 0; i < Monitors().Count; i++)
        {
            monitors.Add(MonitorId(i) + " " + Monitors()[i].width + "x" + Monitors()[i].height);
        }
        string line = $"[Display] {Describe()}; monitors {string.Join(", ", monitors)}; "
            + $"borderless {Join(BorderlessEntries(d.Monitor))}; exclusive {Join(ExclusiveEntries(d.Monitor))}; "
            + $"Windowed {Join(WindowEntries(d.Monitor, LastWindow(d)))}";
        if (line != listsLogged)
        {
            listsLogged = line;
            Debug.Log(line);
        }
    }

    private static string Join(List<Entry> entries)
    {
        List<string> parts = new List<string>();
        foreach (Entry e in entries)
        {
            parts.Add(EntryText(e));
        }
        return string.Join(" · ", parts);
    }

    /// <summary>The screen as Unity reports it, for the log: the mode, the sizes, the monitor and what Unity calls native.</summary>
    private static string Describe()
    {
        DisplayInfo d = Screen.mainWindowDisplayInfo;
        string system = Display.main != null ? Display.main.systemWidth + "x" + Display.main.systemHeight : "?";
        Resolution current = Screen.currentResolution;
        return $"{Screen.fullScreenMode} {Screen.width}x{Screen.height} on {CurrentMonitor}, display info {d.width}x{d.height} "
            + $"{d.refreshRate.value:0.###} Hz, systemWidth {system}, currentResolution {current.width}x{current.height}";
    }

    // ---------------------------------------------------------------- the screen now

    private static ScreenSetup Live =>
        new ScreenSetup(CurrentMonitor, LiveKind, Screen.width, Screen.height);

    private static Kind LiveKind =>
        Screen.fullScreenMode == FullScreenMode.Windowed ? Kind.Windowed
        : Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? Kind.Exclusive : Kind.Borderless;

    /// <summary>What the rows show: a change waiting to be applied, one being applied, or the screen as it is.</summary>
    private static ScreenSetup Shown => pending ? target : moving != null || Time.unscaledTime < quietUntil ? applied : Live;

    // ---------------------------------------------------------------- the rows, for GameOptions

    private static bool IsMonitor(GameOption o) => o.Key == MonitorKey;
    private static bool IsMode(GameOption o) => o.Key == ModeKey;

    private static string MonitorText(int i)
    {
        DisplayInfo d = Monitors()[i];
        Vector2Int size = Desktop(MonitorId(i));
        return (i + 1) + ": " + (string.IsNullOrEmpty(d.name) ? "Monitor" : d.name) + " " + size.x + "x" + size.y;
    }

    /// <summary>The row's value: the monitor's place, the mode's place, or the size's place in its list.</summary>
    public static float Get(GameOption o)
    {
        ScreenSetup d = Shown;
        if (IsMonitor(o))
        {
            return MonitorIndex(d.Monitor);
        }
        if (IsMode(o))
        {
            return (int)d.Kind;
        }
        return EntriesFor(d).FindIndex(e => e.Size == d.Size);
    }

    public static string ValueText(GameOption o)
    {
        ScreenSetup d = Shown;
        if (IsMonitor(o))
        {
            return MonitorText(MonitorIndex(d.Monitor));
        }
        if (IsMode(o))
        {
            return ModeNames[(int)d.Kind];
        }
        List<Entry> entries = EntriesFor(d);
        int i = entries.FindIndex(e => e.Size == d.Size);
        return EntryText(i >= 0 ? entries[i] : new Entry(d.Size, null));
    }

    public static string DefaultText(GameOption o)
    {
        if (IsMonitor(o))
        {
            return "the main monitor, at the first start";
        }
        if (IsMode(o))
        {
            return ModeNames[0];
        }
        ScreenSetup d = Shown;
        return d.Kind == Kind.Windowed ? EntryText(new Entry(WindowDefault(d.Monitor), null)) + " in a window"
            : d.Kind == Kind.Exclusive ? EntryText(new Entry(Largest(d.Monitor), "native"))
            : EntryText(new Entry(Desktop(d.Monitor), "desktop"));
    }

    public static bool IsDefault(GameOption o)
    {
        ScreenSetup d = Shown;
        if (IsMonitor(o))
        {
            return true;
        }
        if (IsMode(o))
        {
            return d.Kind == Kind.Borderless;
        }
        return d.Size == DefaultSize(d.Monitor, d.Kind);
    }

    /// <summary>False where a step would go nowhere: past either end of a list, which does not wrap round.</summary>
    public static bool CanStep(GameOption o, int direction)
    {
        ScreenSetup d = Shown;
        int count;
        int at;
        if (IsMode(o))
        {
            count = ModeNames.Length;
            at = (int)d.Kind;
        }
        else if (IsMonitor(o))
        {
            count = Monitors().Count;
            at = MonitorIndex(d.Monitor);
        }
        else
        {
            List<Entry> entries = EntriesFor(d);
            count = entries.Count;
            at = entries.FindIndex(e => e.Size == d.Size);
        }
        int next = at + direction;
        return at >= 0 && next >= 0 && next < count;
    }

    /// <summary>Left or right on a row: the next monitor, the other mode, or the next size; applied once the rows are still.</summary>
    public static void Step(GameOption o, int direction)
    {
        if (!CanStep(o, direction))
        {
            return;
        }
        Set(o, Get(o) + direction);
    }

    /// <summary>A value set from the table: a monitor by its place, a mode by its place, a size by its place in the list.</summary>
    public static void Set(GameOption o, float value) => SetAfter(o, value, SettleSeconds);

    /// <summary>The rows with a list to open, the menu's drop-down: all three.</summary>
    public static bool HasList(GameOption o) => true;

    /// <summary>What the drop-down of a row lists, in the row's order.</summary>
    public static List<string> ListTexts(GameOption o)
    {
        List<string> texts = new List<string>();
        if (IsMonitor(o))
        {
            for (int i = 0; i < Monitors().Count; i++)
            {
                texts.Add(MonitorText(i));
            }
        }
        else if (IsMode(o))
        {
            texts.AddRange(ModeNames);
        }
        else
        {
            foreach (Entry e in EntriesFor(Shown))
            {
                texts.Add(EntryText(e));
            }
        }
        return texts;
    }

    /// <summary>An entry chosen in the drop-down: put into effect at the next frame, no second of waiting.</summary>
    public static void Choose(GameOption o, int index) => SetAfter(o, index, 0f);

    private static void SetAfter(GameOption o, float value, float delay)
    {
        ScreenSetup d = Shown;
        int i = Mathf.RoundToInt(value);
        if (IsMonitor(o))
        {
            List<DisplayInfo> monitors = Monitors();
            if (i >= 0 && i < monitors.Count && i != MonitorIndex(d.Monitor))
            {
                SetMonitor(MonitorId(i), delay);
            }
            return;
        }
        if (IsMode(o))
        {
            if (i >= 0 && i < ModeNames.Length)
            {
                SetMode((Kind)i, delay);
            }
            return;
        }
        List<Entry> entries = EntriesFor(d);
        if (i >= 0 && i < entries.Count && entries[i].Size != d.Size)
        {
            SetSize(entries[i].Size, delay);
        }
    }

    /// <summary>
    /// Reset: Fullscreen (borderless) at the desktop's size, on the monitor the game is on, the remembered sizes
    /// and kind forgotten; put into effect at the next frame and asked about as a change is.
    /// </summary>
    public static void Reset(GameOption o)
    {
        if (IsMonitor(o))
        {
            return;
        }
        if (IsMode(o))
        {
            SetMode(Kind.Borderless, 0f);
            return;
        }
        Begin();
        PlayerPrefs.SetString(FullscreenSizeKey, "");
        PlayerPrefs.SetString(WindowSizeKey, "");
        PlayerPrefs.SetString(DraggedWindowKey, "");
        PlayerPrefs.SetString(FullscreenKindKey, "");
        ScreenSetup d = Shown;
        Request(d.With(DefaultSize(d.Monitor, d.Kind)), 0f);
    }

    private static void SetMonitor(string monitor, float delay)
    {
        ScreenSetup d = Shown;
        Begin();
        // another monitor's modes are known only once the game is there: exclusive starts at its largest size
        Vector2Int size = d.Kind == Kind.Exclusive ? Largest(monitor) : TargetSize(monitor, d.Kind);
        Request(new ScreenSetup(monitor, d.Kind, size.x, size.y), delay);
    }

    private static void SetMode(Kind kind, float delay)
    {
        ScreenSetup d = Shown;
        if (kind == d.Kind)
        {
            return;
        }
        Begin();
        if (d.Windowed)
        {
            // the window as it is, dragged or not, is the size it comes back at
            PlayerPrefs.SetString(WindowSizeKey, KeyText(d.Size));
        }
        if (kind != Kind.Windowed)
        {
            PlayerPrefs.SetString(FullscreenKindKey, kind == Kind.Exclusive ? "exclusive" : "borderless");
        }
        Vector2Int size = TargetSize(d.Monitor, kind);
        Request(new ScreenSetup(d.Monitor, kind, size.x, size.y), delay);
    }

    private static void SetSize(Vector2Int size, float delay)
    {
        ScreenSetup d = Shown;
        Begin();
        if (d.Windowed)
        {
            PlayerPrefs.SetString(WindowSizeKey, KeyText(size));
        }
        else if (d.Kind == Kind.Exclusive)
        {
            SetFullscreenSizeOf(d.Monitor, size == Largest(d.Monitor) ? "" : KeyText(size));
        }
        Request(d.With(size), delay);
    }

    /// <summary>The state to go back to on a No, taken at the first change of a run of them.</summary>
    private static void Begin()
    {
        if (!pending && !awaiting)
        {
            before = Capture();
        }
    }

    private static void Request(ScreenSetup setup, float delay)
    {
        target = setup;
        pending = true;
        settleAt = Time.unscaledTime + delay;
    }

    // ---------------------------------------------------------------- applying, keeping, going back

    /// <summary>True from a change put into effect until it is kept or undone: the menu shows its box.</summary>
    public static bool AwaitingConfirm => awaiting;

    /// <summary>A change waits for the rows to be still, for a move to end or for an answer: the small-window warning waits too.</summary>
    public static bool Busy => pending || awaiting || moving != null;

    /// <summary>The whole seconds left before the change goes back by itself, as the box counts them; the full count while a move is under way.</summary>
    public static int ConfirmSecondsLeft
    {
        get
        {
            int full = Mathf.CeilToInt(ConfirmSeconds);
            return moving != null ? full : Mathf.Clamp(Mathf.CeilToInt(confirmEndsAt - Time.unscaledTime), 0, full);
        }
    }

    public static bool ConfirmTimedOut => awaiting && moving == null && Time.unscaledTime >= confirmEndsAt;

    /// <summary>Every frame the menu is open: a change whose second has passed is put into effect.</summary>
    public static void Tick()
    {
        if (!pending || Time.unscaledTime < settleAt)
        {
            return;
        }
        pending = false;
        if (target.Equals(before.Setup))
        {
            // stepped back to where the run began: nothing to show, nothing to ask, and the sizes and the fullscreen
            // kind the steps wrote put back, or Alt-Enter would change kind
            RestoreKeys(before);
            return;
        }
        Apply(target);
        awaiting = true;
        confirmEndsAt = Time.unscaledTime + ConfirmSeconds;
        Debug.Log($"[Display] applied {target}, from {before.Setup}; asking to keep it");
    }

    /// <summary>The menu is being closed: a change still waiting for its second is put into effect now. True when the menu must stay open for the box.</summary>
    public static bool ApplyPendingNow()
    {
        if (pending)
        {
            settleAt = 0f;
            Tick();
        }
        return awaiting;
    }

    /// <summary>Yes in the box.</summary>
    public static void Keep()
    {
        awaiting = false;
        PlayerPrefs.SetString(MonitorPrefsKey, Shown.Monitor);
        Debug.Log($"[Display] kept {Shown}; {Describe()}");
    }

    /// <summary>No in the box, B, Esc, or the 10 seconds gone: monitor, mode, size and the remembered sizes as they were.</summary>
    public static void Revert()
    {
        awaiting = false;
        pending = false;
        Restore(before);
        Debug.Log($"[Display] went back to {before.Setup}");
    }

    /// <summary>The menu closes some other way than its own keys: a change not yet answered is not kept.</summary>
    public static void Cancel()
    {
        if (awaiting)
        {
            Revert();
        }
        else if (pending)
        {
            pending = false;
            RestoreKeys(before);
        }
    }

    /// <summary>The two remembered sizes and the fullscreen kind as they were in <paramref name="s"/>.</summary>
    private static void RestoreKeys(Saved s)
    {
        PlayerPrefs.SetString(FullscreenSizeKey, s.FullscreenSize);
        PlayerPrefs.SetString(WindowSizeKey, s.WindowSize);
        PlayerPrefs.SetString(FullscreenKindKey, s.FullscreenKind);
    }

    public static Saved Capture() =>
        new Saved(Shown, PlayerPrefs.GetString(FullscreenSizeKey, ""), PlayerPrefs.GetString(WindowSizeKey, ""),
            PlayerPrefs.GetString(FullscreenKindKey, ""));

    /// <summary>Undo changes: the state recorded when the menu opened, at once, with no question.</summary>
    public static void Restore(Saved s)
    {
        pending = false;
        awaiting = false;
        RestoreKeys(s);
        // and the monitor, which a kept change wrote (Keep()): the next start would go back to the one undone
        PlayerPrefs.SetString(MonitorPrefsKey, s.Setup.Monitor);
        if (!s.Setup.Equals(Shown))
        {
            Apply(s.Setup);
        }
    }

    /// <summary>Puts a setup into effect: to another monitor first, then the mode and size.</summary>
    private static void Apply(ScreenSetup d)
    {
        applied = d;
        quietUntil = Time.unscaledTime + QuietSeconds;
        verify = true;
        lastMonitor = d.Monitor;
        lastWindowed = d.Windowed;
        if (d.Windowed)
        {
            lastWindowSize = d.Size;
        }
        List<DisplayInfo> monitors = Monitors();
        int to = MonitorIndex(d.Monitor);
        if (to != MonitorIndex(CurrentMonitor))
        {
            if (Exclusive)
            {
                // the monitor gets its desktop back before the window leaves it
                Vector2Int here = Desktop(CurrentMonitor);
                Screen.SetResolution(here.x, here.y, FullScreenMode.FullScreenWindow);
            }
            DisplayInfo m = monitors[to];
            Vector2Int position = d.Windowed
                ? new Vector2Int(Mathf.Max(0, (m.workArea.width - d.Width) / 2), Mathf.Max(0, (m.workArea.height - d.Height) / 2))
                : Vector2Int.zero;
            afterMove = d;
            moving = Screen.MoveMainWindowTo(m, position);
            return;
        }
        SetModeAndSize(d);
    }

    /// <summary>
    /// Windowed, borderless at the monitor's own size, or exclusive at any other, at the highest refresh
    /// rate the monitor has for that size.
    /// </summary>
    private static void SetModeAndSize(ScreenSetup d)
    {
        if (d.Windowed)
        {
            Screen.SetResolution(d.Width, d.Height, FullScreenMode.Windowed);
            return;
        }
        if (d.Kind == Kind.Borderless)
        {
            Screen.SetResolution(d.Width, d.Height, FullScreenMode.FullScreenWindow);
            return;
        }
        RefreshRate best = default;
        bool found = false;
        foreach (Resolution r in Modes())
        {
            if (r.width == d.Width && r.height == d.Height && (!found || r.refreshRateRatio.value > best.value))
            {
                best = r.refreshRateRatio;
                found = true;
            }
        }
        if (found)
        {
            Screen.SetResolution(d.Width, d.Height, FullScreenMode.ExclusiveFullScreen, best);
        }
        else
        {
            Screen.SetResolution(d.Width, d.Height, FullScreenMode.ExclusiveFullScreen);
        }
    }

    // ---------------------------------------------------------------- the start, moves, and Alt-Enter

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BeforeFirstScene()
    {
        installedBefore = GameDataPath.HasSavedPath;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        GameObject go = new GameObject(nameof(ScreenOptions));
        DontDestroyOnLoad(go);
        go.AddComponent<ScreenOptions>();
    }

    private static bool SetOnCommandLine()
    {
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (arg == "-screen-width" || arg == "-screen-height" || arg == "-screen-fullscreen" || arg == "-window-mode" || arg == "-monitor")
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The main monitor's place in the layout: the one whose size is the main display's, else the first.</summary>
    private static int PrimaryIndex()
    {
        List<DisplayInfo> monitors = Monitors();
        if (Display.main != null)
        {
            for (int i = 0; i < monitors.Count; i++)
            {
                if (monitors[i].width == Display.main.systemWidth && monitors[i].height == Display.main.systemHeight)
                {
                    return i;
                }
            }
        }
        return 0;
    }

    private static bool MonitorPresent(string id)
    {
        for (int i = 0; i < Monitors().Count; i++)
        {
            if (MonitorId(i) == id)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// At the start: on the monitor the game was last kept on, or at a first start the main one (Unity may open
    /// on another, the user's of 6 November 2026); in Fullscreen at the size remembered, or the monitor's own, in
    /// the mode that size takes, since Unity's own keys may hold a size for another monitor, or exclusive mode at
    /// the desktop's size. Nothing when the screen was given on the command line.
    /// </summary>
    private static void FitAtStart()
    {
        ScreenSetup live = Live;
        lastWindowed = live.Windowed;
        lastWindowSize = live.Size;
        lastMonitor = live.Monitor;
        bool commandLine = SetOnCommandLine();
        Debug.Log($"[Display] start: {Describe()}; {(installedBefore ? "game folder saved" : "first start")}, Unity's screen keys "
            + $"{(PlayerPrefs.HasKey(UnityModeKey) ? "present" : "absent")}, "
            + $"monitor kept {PlayerPrefs.GetString(MonitorPrefsKey, "none")}{(commandLine ? "; set on the command line, left as it is" : "")}");
        if (commandLine)
        {
            return;
        }
        // A first start has no game folder saved yet (every build since the first saves it) and opens in
        // Fullscreen (ProjectSettings). Unity's own screen keys cannot tell: Unity writes them as it starts, the
        // user's test of 6 November 2026. A player of an earlier build has the folder but no monitor of ours:
        // the game stays where Unity reopens it, windowed or not.
        string kept = PlayerPrefs.GetString(MonitorPrefsKey, "");
        bool firstStart = string.IsNullOrEmpty(kept) && !installedBefore && !live.Windowed;
        string monitor = firstStart ? MonitorId(PrimaryIndex()) : MonitorPresent(kept) ? kept : live.Monitor;
        PlayerPrefs.SetString(MonitorPrefsKey, monitor);
        if (MonitorIndex(monitor) != CurrentIndex())
        {
            Kind there = live.Windowed ? Kind.Windowed : FullscreenKind;
            Vector2Int size = there == Kind.Exclusive ? Largest(monitor) : TargetSize(monitor, there);
            Debug.Log($"[Display] start: moved to {monitor}{(firstStart ? ", the main monitor at a first start" : "")}");
            Apply(new ScreenSetup(monitor, there, size.x, size.y));
            return;
        }
        if (live.Windowed)
        {
            return;
        }
        // the fullscreen kind used last, at its size: Unity reopens exclusive mode at the size it closed in, which may
        // be another monitor's, and borderless at the size of its own keys
        Kind kind = FullscreenKind;
        Vector2Int want = TargetSize(live.Monitor, kind);
        if (want != live.Size || kind != live.Kind)
        {
            Debug.Log($"[Display] start: {kind} {want.x}x{want.y}");
            Apply(new ScreenSetup(live.Monitor, kind, want.x, want.y));
        }
    }

    /// <summary>The window put back on its monitor, or the mode and size set again, where Unity or Windows took only part of a change.</summary>
    private static void Verify()
    {
        ScreenSetup live = Live;
        if (MonitorIndex(applied.Monitor) != CurrentIndex())
        {
            Debug.Log($"[Display] {Describe()}: put back on {applied.Monitor}");
            Apply(applied);
            verify = false;
            return;
        }
        if (live.Windowed != applied.Windowed)
        {
            // Unity takes the mode; a different one is the player's Alt-Enter in the meantime, left to the watcher
            return;
        }
        // a window's size is left to Windows, which may keep it inside the desktop
        if (!applied.Windowed && (live.Size != applied.Size || live.Kind != applied.Kind))
        {
            Debug.Log($"[Display] {Describe()}: not all taken, {applied} set again");
            SetModeAndSize(applied);
            quietUntil = Time.unscaledTime + QuietSeconds;
        }
    }

    private void Update()
    {
        if (!started)
        {
            started = true;
            FitAtStart();
            return;
        }
        if (moving != null)
        {
            if (!moving.isDone)
            {
                return;
            }
            moving = null;
            SetModeAndSize(afterMove);
            quietUntil = Time.unscaledTime + QuietSeconds;
            confirmEndsAt = Time.unscaledTime + ConfirmSeconds;
            return;
        }
        if (verify && !pending && Time.unscaledTime >= quietUntil)
        {
            verify = false;
            Verify();
            return;
        }
        if (Busy || Time.unscaledTime < quietUntil)
        {
            return;
        }
        ScreenSetup live = Live;
        if (live.Windowed == lastWindowed)
        {
            if (live.Windowed)
            {
                lastWindowSize = live.Size;
            }
            // a window dragged to another monitor stays there: that is its monitor now
            if (live.Monitor != lastMonitor)
            {
                lastMonitor = live.Monitor;
                PlayerPrefs.SetString(MonitorPrefsKey, live.Monitor);
            }
            // a window dragged to a size of its own: the list's "(last)"
            if (live.Windowed && !WindowSizes(live.Monitor).Contains(live.Size)
                && PlayerPrefs.GetString(DraggedWindowKey, "") != KeyText(live.Size))
            {
                PlayerPrefs.SetString(DraggedWindowKey, KeyText(live.Size));
            }
            return;
        }
        // The mode changed and the menu did not change it: Alt-Enter. The size goes to the one remembered,
        // and the window stays on the monitor it was on: with no window position of its own yet (a first
        // start), Windows may open the window on another monitor.
        lastWindowed = live.Windowed;
        if (!live.Windowed && lastWindowSize.x > 0)
        {
            PlayerPrefs.SetString(WindowSizeKey, KeyText(lastWindowSize));
        }
        string monitor = string.IsNullOrEmpty(lastMonitor) ? live.Monitor : lastMonitor;
        // Unity's Alt-Enter goes borderless: the game takes it to the fullscreen kind used last
        Kind kind = live.Windowed ? Kind.Windowed : FullscreenKind;
        Vector2Int size = TargetSize(monitor, kind);
        Debug.Log($"[Display] Alt-Enter: {Describe()}; set to {kind} {size.x}x{size.y} on {monitor}");
        Apply(new ScreenSetup(monitor, kind, size.x, size.y));
    }
}
