using UnityEngine;
using Screen = UnityEngine.Device.Screen;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public class MapNote
{
    public string note = "";
    public float x;
    public float y;
}

[System.Serializable]
public class MapPageSaveData
{
    public string mappedRLE = ""; // RLE encoded: "0:100,5,50" = 100 false, 5 true, 50 false
    public List<MapNote> notes = new List<MapNote>();
}

[System.Serializable]
public class MapSaveData
{
    public List<MapPageSaveData> pages = new List<MapPageSaveData>();
    
    // Optimized boolean RLE: start value + comma-separated run lengths
    // Example: "0:100,5,50" = 100 false, 5 true, 50 false
    public static string EncodeMappedToRLE(bool[] mapped)
    {
        if (mapped == null || mapped.Length == 0) return "";
        
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        bool currentValue = mapped[0];
        int count = 1;
        
        // Store starting value
        sb.Append(currentValue ? '1' : '0').Append(':');
        
        for (int i = 1; i < mapped.Length; i++)
        {
            if (mapped[i] == currentValue)
            {
                count++;
            }
            else
            {
                sb.Append(count).Append(',');
                currentValue = !currentValue; // Toggle
                count = 1;
            }
        }
        // Append final run length (no trailing comma)
        sb.Append(count);
        
        return sb.ToString();
    }
    
    public static bool[] DecodeMappedFromRLE(string rle, int expectedLength)
    {
        bool[] result = new bool[expectedLength];
        if (string.IsNullOrEmpty(rle)) return result;
        
        // Parse starting value
        bool currentValue = rle[0] == '1';
        int index = 0;
        
        // Skip "0:" or "1:" prefix
        string[] runs = rle.Substring(2).Split(',');
        
        foreach (string run in runs)
        {
            if (int.TryParse(run, out int count))
            {
                for (int i = 0; i < count && index < expectedLength; i++)
                {
                    result[index++] = currentValue;
                }
                currentValue = !currentValue; // Alternate
            }
        }
        
        return result;
    }
}

public class MapPage
{
    private void CloneStamp(int dx, int dy, int w, int h, int sx, int sy)
    {
        for (int y = 0; y < h; ++y)
        {
            for (int x = 0; x < w; ++x)
            {
                col[320 * (dy + y) + dx + x] = col[320 * (sy + y) + sx + x];
            }
        }
    }

    public void Init()
    {
        bg = GraphicsLoader.ReadBYT("../Data/blnkmap.byt", palIndex: 1);

        col = bg.GetPixels();
        
        int[] coords = { 253, 183, 30, 8 };
    
        // overwrite the level indicator with some random noise from elsewhere on the screen
        CloneStamp(coords[0], coords[1], coords[2], coords[3], coords[0] - 32, coords[1]);

        int[] ec = { 271, 56, 32, 26 };
        
        // overwrite the eraser with some noise from elsewhere
        CloneStamp(ec[0], ec[1], ec[2], ec[3], ec[0] - 32, ec[1]);
    
        bg.SetPixels(col);
        bg.Apply();
    }

    private void DrawWall(Color[] c, int p, Tile t, int dir, int x, int y, int levelIndex)
    {
        // dir 0, 1, 2, 3 = n, e, s, w
        int nx = Mathf.Clamp(x + (dir == 1 ? 1 : 0) - (dir == 3 ? 1 : 0), 0, 63);
        int ny = Mathf.Clamp(y + (dir == 0 ? 1 : 0) - (dir == 2 ? 1 : 0), 0, 63);
        // Get tile from the specified level, not the currently loaded level
        Tile n = null;
        if (levelIndex >= 1 && levelIndex < LevelLoader.sLevelLoader.levels.Length 
            && LevelLoader.sLevelLoader.levels[levelIndex] != null)
        {
            n = LevelLoader.sLevelLoader.levels[levelIndex].tiles[nx, ny];
        }
        bool floorStep = n.type == 0 || t.floorHeight - n.floorHeight > 2;
        bool isSquare = t.type == 1 || (t.type is >= 6 and <= 9);
        bool hidden = n.hidden && n.door == null;
        switch (dir)
        {
        case 0: // north
            if (y == 63 || floorStep || (isSquare && (n.type == 4 || n.type == 5)) || hidden)
            {
                c[p + 960] *= wall;
                c[p + 961] *= wall;
                c[p + 962] *= wall;
            }
            break;
        case 1: // east
            if (x == 63 || floorStep || (isSquare && (n.type == 2 || n.type == 4)) || hidden)
            {
                c[p + 3] *= wall;
                c[p + 323] *= wall;
                c[p + 643] *= wall;
            }
            break;
        case 2: // south
            if (y == 0 || floorStep || (isSquare && (n.type == 2 || n.type == 3)) || hidden)
            {
                c[p - 320] *= wall;
                c[p - 319] *= wall;
                c[p - 318] *= wall;
            }
            break;
        case 3: // west
            if (x == 0 || floorStep || (isSquare && (n.type == 3 || n.type == 5)) || hidden)
            {
                c[p - 1] *= wall;
                c[p + 319] *= wall;
                c[p + 639] *= wall;
            }
            break;
        }
    }

    public void RewriteTile(Tile t)
    {
        if (!mapped[64 * t.y + t.x])
        {
            return;
        }
        
        // choose a random spot to the right of the map to clone
        CloneStamp((3 * t.x + 32), (3 * (t.y + 2)), 3, 3, Random.Range(224, 288), Random.Range(80, 120));
        
        // Use current loaded level for RewriteTile (called during gameplay)
        UpdateTile(t, LevelLoader.sLevelLoader.loadedLevel);
    }

    public void UpdateTile(Tile t, int levelIndex)
    {
        int x = t.x;
        int y = t.y;

        Color fs = floor;

        // check for stairs up/down
        if (t.isStair)
        {
            fs = stairs;
        }
        
        // CRITICAL FIX: Use the correct level's floors array, not the currently loaded level
        // GetFloorTerrain() uses GetLevel() which returns the currently loaded level, not the tile's level
        ETerrainType floorTerrain;
        if (levelIndex > 0 && levelIndex < LevelLoader.sLevelLoader.levels.Length 
            && LevelLoader.sLevelLoader.levels[levelIndex] != null)
        {
            // Use the level index passed to UpdateTile, not the currently loaded level
            floorTerrain = (ETerrainType)UUTerrain.floors[LevelLoader.sLevelLoader.levels[levelIndex].floors[t.floorTexture]];
        }
        else
        {
            // Fallback to the old method if level isn't available
            floorTerrain = t.GetFloorTerrain();
        }
        
        switch (floorTerrain)
        {
        case ETerrainType.Water:
            fs = water;
            break;
        case ETerrainType.Lava:
            fs = lava;
            break;
        }
        
        int p = 960 * (y + 2) + 32 + 3 * x;
        
        switch (t.type)
        {
        case 0:
            // closed tile
            break;
        case 1:
        case 6:
        case 7:
        case 8:
        case 9:
            if (t.bridge != null)
            {
                fs = bridge;
                if (t.bridge.GetLookName() == "water")
                {
                    fs = water;
                }
            }
            col[p] *= fs * Random.Range(0.8f, 1.0f);
            col[p + 1] *= fs * Random.Range(0.8f, 1.0f);
            col[p + 2] *= fs * Random.Range(0.8f, 1.0f);
            col[p + 320] *= fs * Random.Range(0.8f, 1.0f);
            col[p + 321] *= fs * Random.Range(0.8f, 1.0f);
            col[p + 322] *= fs * Random.Range(0.8f, 1.0f);
            col[p + 640] *= fs * Random.Range(0.8f, 1.0f);
            col[p + 641] *= fs * Random.Range(0.8f, 1.0f);
            col[p + 642] *= fs * Random.Range(0.8f, 1.0f);
            DrawWall(col, p, t, 0, x, y, levelIndex);
            DrawWall(col, p, t, 1, x, y, levelIndex);
            DrawWall(col, p, t, 2, x, y, levelIndex);
            DrawWall(col, p, t, 3, x, y, levelIndex);
            if (t.door != null)
            {
                switch ((int)t.door.transform.eulerAngles.y)
                {
                    case 90:
                    case 270:
                        col[p + 1] *= wall;
                        col[p + 321] *= wall;
                        col[p + 641] *= wall;
                        break;
                    case 0:
                    case 180:
                        col[p + 320] *= wall;
                        col[p + 321] *= wall;
                        col[p + 322] *= wall;
                        break;
                }
            }
            break;
        case 4:
            // ne (open to ne)
            col[p + 2] *= wall;
            col[p + 321] *= wall;
            col[p + 322] *= fs;
            col[p + 640] *= wall;
            col[p + 641] *= fs;
            col[p + 642] *= fs;
            DrawWall(col, p, t, 1, x, y, levelIndex);
            DrawWall(col, p, t, 0, x, y, levelIndex);
            break;
        case 5:
            // nw
            col[p] *= wall;
            col[p + 320] *= fs;
            col[p + 321] *= wall;
            col[p + 640] *= fs;
            col[p + 641] *= fs;
            col[p + 642] *= wall;
            DrawWall(col, p, t, 0, x, y, levelIndex);
            DrawWall(col, p, t, 3, x, y, levelIndex);
            break;
        case 2:
            // se
            col[p] *= wall;
            col[p + 1] *= fs;
            col[p + 2] *= fs;
            col[p + 321] *= wall;
            col[p + 322] *= fs;
            col[p + 642] *= wall;
            DrawWall(col, p, t, 2, x, y, levelIndex);
            DrawWall(col, p, t, 1, x, y, levelIndex);
            break;
        case 3:
            // sw
            col[p] *= fs;
            col[p + 1] *= fs;
            col[p + 2] *= wall;
            col[p + 320] *= fs;
            col[p + 321] *= wall;
            col[p + 640] *= wall;
            DrawWall(col, p, t, 2, x, y, levelIndex);
            DrawWall(col, p, t, 3, x, y, levelIndex);
            break;
        }
    }

    private static int[] xo = { 0, 0, -1, 1, -1, 1, 0, 0, 0, 0 };
    private static int[] yo = { 0, 0, -1, -1, 1, 1, 0, 0, 0, 0 };

    public void Update(bool debugRevealMap)
    {
        // update the map as we go
        int tileX = Tile.GetTileX(PlayerObject.Player.transform.position.x);
        int tileY = Tile.GetTileY(PlayerObject.Player.transform.position.z);
        Tile pt = LevelLoader.GetTile(tileX, tileY);

        int radius = debugRevealMap ? 63 : 1;
        for (int x = Mathf.Max(0, tileX - radius); x <= Mathf.Min(tileX + radius, 63); ++x)
        {
            for (int y = Mathf.Max(0, tileY - radius); y <= Mathf.Min(tileY + radius, 63); ++y)
            {
                Tile t = LevelLoader.GetTile(x, y);

                // if you somehow get to a hidden tile, reveal it
                if (x == tileX && y == tileY && t.hidden)
                {
                    t.hidden = false;
                }

                if (t.hidden && !debugRevealMap)
                {
                    continue;
                }

                if (!debugRevealMap)
                {
                    int dx = x - tileX;
                    int dy = y - tileY;
                    // diagonal
                    if (dx * dx + dy * dy == 2)
                    {
                        // can't look through solid wall of triangular tile
                        if (x - tileX == xo[pt.type] && y - tileY == yo[pt.type])
                        {
                            continue;
                        }
                        // can't look through pinch point
                        if (LevelLoader.GetTile(tileX + dx, tileY).type == 0
                            && LevelLoader.GetTile(tileX, tileY + dy).type == 0)
                        {
                            continue;
                        }
                    }
                }

                if (!mapped[64 * y + x])
                {
                    mapped[64 * y + x] = true;
                    if (t.type != 0)
                    {
                        PlayerObject.AddXP(1);
                        ++PlayerData.sData.mapTilesRevealed;
                    }
                    UpdateTile(t, LevelLoader.sLevelLoader.loadedLevel);
                }
            }
        }
        bg.SetPixels(col);
        bg.Apply();
    }
    
    private static Color wall = new Color(0.8f, 0.6f, 0.6f);
    private static Color water = new Color(0.6f, 0.8f, 1.0f);
    private static Color floor = Color.white;
    private static Color bridge = new Color(1.0f, 0.7f, 0.5f);
    private static Color lava = new Color(1.0f, 0.5f, 0.6f);
    private static Color stairs = new Color(0.7f, 0.5f, 0.5f);

    public Texture2D bg;
    internal Color[] col; // Internal: accessible within assembly but not exposed to Unity Inspector
    
    public bool[] mapped = new bool[64 * 64];

    public List<MapNote> notes = new List<MapNote>();
}

[DefaultExecutionOrder(-50)]
public class MapScreen : MonoBehaviour
{
    private bool visible; // is the map screen visible?

    public static bool IsMapScreenVisible() => sMapScreen != null && sMapScreen.visible;

    /// <summary>Used by <see cref="SoftwareCursorOverlay"/> so the quill shares IMGUI depth with the software cursor.</summary>
    public static bool TryGetSoftwareCursorQuill(out Texture2D tex, out float guiX, out float guiY, out float drawWidth, out float drawHeight)
    {
        tex = null;
        guiX = guiY = 0f;
        drawWidth = drawHeight = 0f;
        if (sMapScreen == null || !sMapScreen.visible)
            return false;

        DataLoader dl = DataLoader.sDataLoader;
        if (dl?.mapCursorTex == null)
            return false;

        int idx = sMapScreen.currentNote != null ? 14 : 12;
        if (idx < 0 || idx >= dl.mapCursorTex.Length || dl.mapCursorTex[idx] == null)
            return false;

        tex = dl.mapCursorTex[idx];
        guiX = sMapScreen.quillX;
        guiY = sMapScreen.quillY;
        drawWidth = 2f * tex.width;
        drawHeight = 2f * tex.height;
        return true;
    }

    public Font font;
    public Texture2D[] mapPin; // Array of 8 directional textures: N, NE, E, SE, S, SW, W, NW

    public Texture2D[] legendIcons;
    public Texture2D[] mouseLegendIcons;

    private int quillX;
    private int quillY;
    private MapNote currentNote;
    private MapNote highlightedNote;
    private string initialNoteText; // Track initial note text when editing starts
    private bool isNewNote; // Track if currentNote is a newly created note

    private MapPage[] mapPages = new MapPage[21];
    private MapPage mapPage;

    private int selectedPage;

    private static MapScreen sMapScreen;

    private static Color textColor = new Color(0.2f, 0.1f, 0.1f);
    private static Color highlightedTextColor = new Color(1.0f, 0.6f, 0.4f);

    private bool awaitingCloseAButtonRelease;

    private static bool MapKeyboardOverlayActive()
    {
        if (KeyboardGUI.sKeyboard != null && KeyboardGUI.sKeyboard.IsVisible())
            return true;
        if (PlayerObject.Player != null
            && (PlayerObject.Player.controlsDisabled & EControlMask.Keyboard) > 0)
            return true;
        // Note editor shows the keyboard from OnGUI after Update; block map Esc until the note is cleared.
        return sMapScreen != null && sMapScreen.currentNote != null;
    }

    private void TryEraseHighlightedMapNote()
    {
        if (highlightedNote == null)
            return;
        mapPage.notes.Remove(highlightedNote);
        highlightedNote = null;
    }

    private Rect MapLevelCornerRectUp()
    {
        float xs = Screen.width / 320f;
        float ys = Screen.height / 200f;
        const float rw = 36f;
        const float rh = 30f;
        return new Rect(Screen.width - rw * xs - 2f * xs, 2f * ys, rw * xs, rh * ys);
    }

    private Rect MapLevelCornerRectDown()
    {
        float xs = Screen.width / 320f;
        float ys = Screen.height / 200f;
        const float rw = 36f;
        const float rh = 30f;
        float h = rh * ys;
        return new Rect(Screen.width - rw * xs - 2f * xs, Screen.height - h - 2f * ys, rw * xs, h);
    }

    /// <summary>Top/bottom parchment corner arrows: previous/next map page (mouse). Gamepad still uses d-pad.
    /// Clicks on a corner hitbox always consume LMB so they never fall through to note placement (e.g. level 1 up arrow).</summary>
    private bool TryConsumeMapLevelCornerClick(Vector2 guiMouse)
    {
        if (currentNote != null)
            return false;
        if (MapLevelCornerRectUp().Contains(guiMouse))
        {
            if (selectedPage > 1)
            {
                --selectedPage;
                mapPage = mapPages[selectedPage];
            }
            return true;
        }
        if (MapLevelCornerRectDown().Contains(guiMouse))
        {
            if (selectedPage < mapPages.Length - 1)
            {
                ++selectedPage;
                mapPage = mapPages[selectedPage];
            }
            return true;
        }
        return false;
    }

    private void TryApplyMapPrimaryAction()
    {
        if (MapKeyboardOverlayActive())
            return;

        if (highlightedNote != null)
        {
            currentNote = highlightedNote;
            initialNoteText = currentNote.note;
            isNewNote = false;
            highlightedNote = null;
            quillX = (int)currentNote.x * Screen.width / 320;
            quillY = (int)currentNote.y * Screen.height / 200;
#if UNITY_EDITOR
            EditorApplication.ExecuteMenuItem("Window/General/Game");
#endif
            return;
        }

        if (currentNote == null)
        {
            Rect closeButton = new Rect(269, 151, 37, 23);
            int qx = quillX * 320 / Screen.width;
            int qy = quillY * 200 / Screen.height;
            if (closeButton.Contains(new Vector2(qx, qy)))
                awaitingCloseAButtonRelease = true;
            else
            {
                currentNote = new MapNote();
                initialNoteText = "";
                isNewNote = true;
                mapPage.notes.Add(currentNote);
            }

            return;
        }

        if (currentNote.note.Length == 0)
            mapPage.notes.Remove(currentNote);

        currentNote = null;
    }
    
    public void Start()
    {
        if (DataLoader.sDataLoader != null && DataLoader.sDataLoader.palettes[1].colors.Length > 0)
        {
            quillX = Screen.width / 2;
            quillY = Screen.height / 2;

            for (int i = 1; i < mapPages.Length; ++i)
            {
                mapPages[i] = new MapPage();
                mapPages[i].Init();
            }
            selectedPage = LevelLoader.sLevelLoader.loadedLevel;
            mapPage = mapPages[selectedPage];

            sMapScreen = this;
        }
    }
    
    public void Update()
    {
        // Guard against no level loaded yet
        if (LevelLoader.sLevelLoader.loadedLevel == 0 
            || LevelLoader.GetLevel() == null)
        {
            return;
        }
        
        bool mapDisabled = (PlayerObject.Player.controlsDisabled & (EControlMask.Conversation | EControlMask.Cutscene | EControlMask.Flute | EControlMask.RoamingSight | EControlMask.EnterMoongate | EControlMask.HowMany | EControlMask.SaveLoad)) > 0;
        if (mapDisabled)
        {
            visible = false;
        }
        
        Map map = Inventory.sInv != null
            ? Inventory.sInv.FindObjectInInventory(EObjectType.Map) as Map
            : null;
        if (!visible
            && !mapDisabled
            && (map != null || Cheats.sCheats.mapWithoutMap)
            && (GameInput.SelectPressedThisFrame()
                || GameInput.TabPressedThisFrame()
                || (map != null && map.popUpMap)))
        {
            if (map != null)
            {
                map.popUpMap = false;
            }

            PlayerObject.DisableControls(EControlMask.Map, true);
            Time.timeScale = 0.0f;
            visible = true;

            StatsPanel.sStatsPanel?.Hide();
            
            // Switch to map music
            Music.EnterMapScreen();
            
            selectedPage = LevelLoader.sLevelLoader.loadedLevel;
            mapPage = mapPages[selectedPage];
            
            // move the quill near the player
            float xScale = Screen.width / 320.0f;
            float yScale = Screen.height / 200.0f;
            quillX = (int) (xScale * (32 + PlayerObject.Player.transform.position.x));
            quillY = Screen.height - (int) (yScale * (9 + PlayerObject.Player.transform.position.z));
        }
        else if (visible
                 && !MapKeyboardOverlayActive()
                 && (GameInput.SelectPressedThisFrame()
                     || GameInput.EscapePressedThisFrame()
                     || GameInput.TabPressedThisFrame()
                     || (GameInput.CurrentGamepad?.bButton.wasReleasedThisFrame ?? false)
                     || (awaitingCloseAButtonRelease && (GameInput.CurrentGamepad?.aButton.wasReleasedThisFrame ?? false))
                     || (awaitingCloseAButtonRelease
                         && GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard
                         && (GameInput.CurrentMouse?.leftButton.wasReleasedThisFrame ?? false))))
        {
            CloseMapInternal();
        }

        if (visible)
        {
            // If a mouse is plugged in, Mouse.current is non-null even when using a gamepad — follow the
            // active scheme so the stick still moves the quill when LastActiveDevice is Gamepad.
            if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard
                && GameInput.CurrentMouse != null)
            {
                Vector2 mp = GameInput.CurrentMouse.position.ReadValue();
                quillX = Mathf.Clamp((int)mp.x, 0, Screen.width);
                quillY = Mathf.Clamp(Screen.height - (int)mp.y, 0, Screen.height);
            }
            else
            {
                float stickX = GameInput.CurrentGamepad?.leftStick.x.value ?? 0.0f;
                float stickY = GameInput.CurrentGamepad?.leftStick.y.value ?? 0.0f;

                float signX = stickX >= 0 ? 1.0f : -1.0f;
                float signY = stickY >= 0 ? 1.0f : -1.0f;
                float curvedX = signX * Mathf.Pow(Mathf.Abs(stickX), 1.5f);
                float curvedY = signY * Mathf.Pow(Mathf.Abs(stickY), 1.5f);

                quillX += (int)(20.0f * curvedX);
                quillY -= (int)(20.0f * curvedY);
                quillX = Mathf.Clamp(quillX, 0, Screen.width);
                quillY = Mathf.Clamp(quillY, 0, Screen.height);
            }

            highlightedNote = null;
            if (currentNote == null)
            {
                float bestDistance = 500.0f;
                foreach (MapNote note in mapPage.notes)
                {
                    float quillPX = quillX * 320.0f / Screen.width;
                    float quillPY = quillY * 200.0f / Screen.height; 
                    if (note != currentNote)
                    {
                        float x = note.x + 4;
                        float y = note.y + 2;
                        float dx = quillPX - x;
                        float dy = quillPY - y;
                        if (dx * dx < 16 && dy * dy < 4)
                        {
                            float distance = dx * dx + dy * dy;
                            if (distance < bestDistance)
                            {
                                bestDistance = distance;
                                highlightedNote = note;
                            }
                        }
                    }
                }
            }

            bool keyboardActive = MapKeyboardOverlayActive();

            if (!keyboardActive && highlightedNote != null && (GameInput.CurrentGamepad?.xButton.wasPressedThisFrame ?? false))
                TryEraseHighlightedMapNote();

            if (!keyboardActive && (GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false))
                TryApplyMapPrimaryAction();

            if (!keyboardActive
                && GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard
                && GameInput.CurrentMouse != null)
            {
                if (GameInput.CurrentMouse.rightButton.wasPressedThisFrame)
                    TryEraseHighlightedMapNote();
                if (GameInput.CurrentMouse.leftButton.wasPressedThisFrame)
                {
                    if (!KeyboardGUI.ConsumeMapPrimaryClickSuppression())
                    {
                        Vector2 guiMouse = GuiInput.ScreenToGuiMouse(GameInput.CurrentMouse.position.ReadValue());
                        if (!TryConsumeMapLevelCornerClick(guiMouse))
                            TryApplyMapPrimaryAction();
                    }
                }
            }
            if (currentNote != null)
            {
                currentNote.x = quillX * 320.0f / Screen.width;
                currentNote.y = quillY * 200.0f / Screen.height;
            }
            else if ((GameInput.CurrentGamepad?.dpad.up.wasPressedThisFrame ?? false) && selectedPage > 1)
            {
                --selectedPage;
                mapPage = mapPages[selectedPage];
            }
            else if ((GameInput.CurrentGamepad?.dpad.down.wasPressedThisFrame ?? false) && selectedPage < mapPages.Length - 1)
            {
                ++selectedPage;
                mapPage = mapPages[selectedPage];
            }
        }

        mapPages[LevelLoader.sLevelLoader.loadedLevel].Update(Cheats.sCheats.revealMap);
    }

    /// <summary>Called when Esc/M requests closing the map from keyboard (same as gamepad close).</summary>
    /// <param name="ignoreMapNoteGuard">Use when forcing the map closed (e.g. stats pin) even during note edit.</param>
    public static void RequestCloseFromKeyboard(bool ignoreMapNoteGuard = false)
    {
        if (sMapScreen == null || !sMapScreen.visible)
            return;
        if (!ignoreMapNoteGuard && sMapScreen.currentNote != null)
            return;
        if (KeyboardGUI.sKeyboard != null && KeyboardGUI.sKeyboard.IsVisible())
            return;
        if ((PlayerObject.Player.controlsDisabled & EControlMask.Keyboard) != 0)
            return;
        sMapScreen.CloseMapInternal();
    }

    private void CloseMapInternal()
    {
        if (!visible)
            return;

        awaitingCloseAButtonRelease = false;

        if (currentNote != null)
        {
            if (currentNote.note.Length == 0)
            {
                mapPage.notes.Remove(currentNote);
            }
            currentNote = null;
        }
        highlightedNote = null;

        PlayerObject.DisableControls(EControlMask.Map, false);
        Time.timeScale = 1.0f;
        visible = false;

        Music.ExitMapScreen();
    }

    public static void UpdateTile(Tile t)
    {
        sMapScreen.mapPages[LevelLoader.sLevelLoader.loadedLevel].RewriteTile(t);
    }

    private void DrawLegend(Texture2D[] icons, string[] labels, GUIStyle labelStyle)
    {
        float w = 32.0f * Screen.width / 320;
        float x = Screen.width - w;
        float k = labelStyle.fontSize;
        if (icons != null && labels != null && icons.Length == labels.Length)
        {
            for (int i = 0; i < icons.Length; ++i)
            {
                if (icons[i] != null)
                {
                    float y = 0.6f * Screen.height + 1.2f * k * i;
                    GUI.DrawTexture(new Rect(x - 1.2f * k, y, k, k), icons[i]);
                    GUI.Label(new Rect(x, y - k / 6, w, k), labels[i], labelStyle);
                }
            }
        }
    }

    public void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Map;

        if (visible)
        {
            Rect mapRect = PlayerObject.Player.mainCamera.pixelRect;
            GuiInput.RegisterBlockingRect(mapRect);
            GUI.DrawTexture(mapRect, mapPage.bg);

            float xScale = Screen.width / 320.0f;
            float yScale = Screen.height / 200.0f;

            // the player icon showing facing direction
            if (selectedPage == LevelLoader.sLevelLoader.loadedLevel)
            {
                float playerYaw = PlayerObject.Player.transform.eulerAngles.y;
                int directionIndex = (int)((playerYaw + 22.5f) / 45.0f) % 8;
                
                if (mapPin != null && mapPin.Length > directionIndex && mapPin[directionIndex] != null)
                {
                    float newPinWidth = xScale * mapPin[directionIndex].width;
                    float newPinHeight = yScale * mapPin[directionIndex].height;

                    float px = xScale * (32 + PlayerObject.Player.transform.position.x) - newPinWidth / 2;
                    float py = yScale * (6 + PlayerObject.Player.transform.position.z) + newPinHeight / 2;
                    
                    GUI.DrawTexture(new Rect(px, Screen.height - py, newPinWidth, newPinHeight), mapPin[directionIndex]);
                }
            }

            GUIStyle style = new GUIStyle { font = font, normal = { textColor = textColor }, fontSize = 10 * Screen.height / 200, fontStyle = FontStyle.Italic };

            GUI.Label(new Rect(255.0f / 320.0f * Screen.width, 7.0f / 200.0f * Screen.height, style.fontSize, style.fontSize), $"Level {selectedPage}", style);

            style.fontStyle = FontStyle.Normal;
            style.fontSize = 5 * Screen.height / 200;

            {
                if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
                {
                    DrawLegend(mouseLegendIcons, new [] { "Make note", "Erase note", "Close map" }, style);
                }
                else
                {
                    DrawLegend(legendIcons, new [] { "Make note", "Erase note", "Change level", "Close map" }, style);
                }
            }
            
            foreach (var note in mapPage.notes)
            {
                if (note != currentNote)
                {
                    style.normal.textColor = note == highlightedNote ? highlightedTextColor : textColor;
                    GUI.Label(new Rect(note.x * Screen.width / 320.0f, note.y * Screen.height / 200.0f - 10.0f, 100, 20), note.note, style);
                }
            }
            
            // Draw current note in highlight color while being edited
            if (currentNote != null)
            {
                style.normal.textColor = highlightedTextColor;
                GUI.Label(new Rect(currentNote.x * Screen.width / 320.0f, currentNote.y * Screen.height / 200.0f - 10.0f, 100, 20), currentNote.note, style);
            }

            if (currentNote != null)
            {
                // Show virtual keyboard if not already visible
                if (KeyboardGUI.sKeyboard != null && !KeyboardGUI.sKeyboard.IsVisible())
                {
                    // Calculate keyboard position relative to quill
                    // Position keyboard on opposite side of center from quill, but keep it close to quill
                    float quillScreenX = quillX;
                    float quillScreenY = quillY;
                    
                    float screenCenterX = Screen.width / 2.0f;
                    float screenCenterY = Screen.height / 2.0f;
                    
                    // Calculate keyboard panel dimensions (approximate, will be clamped anyway)
                    float panelWidth = (40 + 5) * 10 - 5 + 20 * 2; // KEY_WIDTH + KEY_SPACING * 10 - KEY_SPACING + PANEL_PADDING * 2
                    float panelHeight = 40 + 20 * 2 + (40 + 5) * 4 - 5 + 30 + 10; // TEXT_FIELD_HEIGHT + PANEL_PADDING * 2 + (KEY_HEIGHT + KEY_SPACING) * 4 - KEY_SPACING + buttonAreaHeight + 10
                    
                    // Position keyboard on opposite side of quill from center
                    float keyboardX, keyboardY;
                    
                    if (quillScreenX < screenCenterX)
                    {
                        // Quill is left of center, position keyboard to the right of quill
                        keyboardX = quillScreenX + 50; // Offset to the right of quill
                    }
                    else
                    {
                        // Quill is right of center, position keyboard to the left of quill
                        keyboardX = quillScreenX - panelWidth - 50; // Offset to the left of quill
                    }
                    
                    if (quillScreenY < screenCenterY)
                    {
                        // Quill is above center, position keyboard below quill
                        keyboardY = quillScreenY + 50; // Offset below quill
                    }
                    else
                    {
                        // Quill is below center, position keyboard above quill
                        keyboardY = quillScreenY - panelHeight - 50; // Offset above quill
                    }
                    
                    KeyboardGUI.sKeyboard.Show(currentNote.note, (result) => {
                        if (result.Length == 0)
                        {
                            // Empty input cancels the note - keep existing note unchanged
                            currentNote = null;
                        }
                        else
                        {
                            currentNote.note = result;
                            currentNote = null;
                        }
                    }, () => {
                        // Cancel - restore note to initial value or delete if new
                        if (isNewNote)
                        {
                            // Delete new note
                            mapPage.notes.Remove(currentNote);
                        }
                        else
                        {
                            // Restore existing note to initial text
                            currentNote.note = initialNoteText;
                        }
                        currentNote = null;
                    }, new Vector2(keyboardX, keyboardY), "Note:", true, true); // isMapScreenKeyboard = true, allowCancel = true
                }
                
                // Update note text continuously while keyboard is visible
                if (KeyboardGUI.sKeyboard != null && KeyboardGUI.sKeyboard.IsVisible())
                {
                    currentNote.note = KeyboardGUI.sKeyboard.CurrentText;
                }
                
                // Still allow physical keyboard input
                if (Event.current.isKey && Event.current.keyCode == KeyCode.Return)
                {
                    if (KeyboardGUI.sKeyboard == null || !KeyboardGUI.sKeyboard.IsVisible())
                    {
                        if (currentNote.note.Length == 0)
                        {
                            mapPage.notes.Remove(currentNote);
                        }
                        currentNote = null;
                    }
                }
            }

#if false
            if (Gamepad.current?.xButton.isPressed ?? false)
            {
                for (int x = 0; x < 64; ++x)
                {
                    for (int y = 0; y < 64; ++y)
                    {
                        Tile t = LevelLoader.GetTile(x, y);
                        Tile p = LevelLoader.GetClosestTile(Camera.current.transform.position);
                        Vector2Int playerInt = new Vector2Int(p.x, p.y);
                        LevelLoader.GetLevel().pvs.CacheVisibilityFrom(playerInt);
                        if (LevelLoader.GetLevel().pvs.IsVisible(t) || (Gamepad.current?.bButton.isPressed ?? false))
                        {
                            GUI.Label(new Rect(xScale * (33 + 3 * x), Screen.height - yScale * (3 * y + 10), 20, 20), $"{t.type}");
                        }
                    }
                }
            }
#endif
#if false
            if (Gamepad.current?.bButton.isPressed ?? false)
            {
                for (int x = 0; x < 64; ++x)
                {
                    for (int y = 0; y < 64; ++y)
                    {
                        Tile t = LevelLoader.GetTile(x, y);
                        GUI.Label(new Rect(xScale * (33 + 3 * x), Screen.height - yScale * (3 * y + 10), 20, 20), $"{t.floorHeight}");
                    }
                }
            }
#endif
            GuiInput.TryConsumeClickInPanel(mapRect);
        }
    }
    
    public MapSaveData SaveToData()
    {
        MapSaveData saveData = new MapSaveData();
        
        // Save all map pages (starting from index 1, since 0 is unused)
        for (int i = 1; i < mapPages.Length; i++)
        {
            if (mapPages[i] != null)
            {
                MapPageSaveData pageData = new MapPageSaveData();
                
                // Only save mapped data for levels 1-8 (actual explorable levels)
                // Level 9 has no map, levels 10-20 are note-only pages
                if (i <= 8 && mapPages[i].mapped != null)
                {
                    pageData.mappedRLE = MapSaveData.EncodeMappedToRLE(mapPages[i].mapped);
                }
                
                // Always save notes for all levels
                if (mapPages[i].notes != null)
                {
                    foreach (MapNote note in mapPages[i].notes)
                    {
                        pageData.notes.Add(new MapNote
                        {
                            note = note.note,
                            x = note.x,
                            y = note.y
                        });
                    }
                }
                
                saveData.pages.Add(pageData);
            }
            else
            {
                saveData.pages.Add(null);
            }
        }
        
        return saveData;
    }
    
    public void LoadFromData(MapSaveData data)
    {
        if (data == null || data.pages == null) return;
        
        // Restore map pages (starting from index 1)
        for (int i = 1; i < mapPages.Length && i - 1 < data.pages.Count; i++)
        {
            if (data.pages[i - 1] != null && mapPages[i] != null)
            {
                MapPageSaveData pageData = data.pages[i - 1];
                
                // Restore mapped array from RLE (only for levels 1-8)
                if (!string.IsNullOrEmpty(pageData.mappedRLE) && mapPages[i].mapped != null)
                {
                    mapPages[i].mapped = MapSaveData.DecodeMappedFromRLE(pageData.mappedRLE, 64 * 64);
                    
                    // Only update tiles if the level is already loaded
                    // We don't want to load all levels just for map data - they'll be loaded when needed
                    if (LevelLoader.sLevelLoader.levels[i] != null)
                    {
                        // Save mapped array before resetting (notes will be restored from pageData after Init)
                        bool[] savedMapped = (bool[])mapPages[i].mapped.Clone();
                        
                        // Reset col and bg properly (includes CloneStamp operations to hide UI elements)
                        mapPages[i].Init();
                        
                        // Restore mapped array
                        mapPages[i].mapped = savedMapped;
                        
                        
                        // Redraw all revealed tiles (pass level index so DrawWall uses correct level's tiles)
                        for (int tileIdx = 0; tileIdx < mapPages[i].mapped.Length; tileIdx++)
                        {
                            if (mapPages[i].mapped[tileIdx])
                            {
                                int x = tileIdx % 64;
                                int y = tileIdx / 64;
                                Tile t = LevelLoader.sLevelLoader.levels[i].tiles[x, y];
                                if (t != null)
                                {
                                    mapPages[i].UpdateTile(t, i);
                                }
                            }
                        }
                        
                        // Apply the texture changes
                        mapPages[i].bg.SetPixels(mapPages[i].col);
                        mapPages[i].bg.Apply();
                    }
                    // If level is not loaded yet, the map will be updated when the level is first loaded
                    // (via MapScreen.Update() which calls UpdateTile as tiles are revealed)
                }
                
                // Restore notes from save data
                mapPages[i].notes.Clear();
                if (pageData.notes != null)
                {
                    foreach (MapNote savedNote in pageData.notes)
                    {
                        mapPages[i].notes.Add(new MapNote
                        {
                            note = savedNote.note,
                            x = savedNote.x,
                            y = savedNote.y
                        });
                    }
                }
            }
        }
    }
}
