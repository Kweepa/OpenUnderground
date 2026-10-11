using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// The Cheats component, drawn in the order Cheats declares its fields, with a few rows that hold
/// more than one: the level with a + and a - button; the tile to teleport to as two boxes, which
/// teleport when either is changed, and a Go button; the object, the stack and the creature to
/// spawn, each a list to search with a Spawn button beside it, and the creature's distance and
/// attitude on the row below; and the skill to set beside its value. Whatever is set here is kept
/// for the next run.
/// </summary>
[CustomEditor(typeof(Cheats))]
public class CheatsEditor : Editor
{
    private const string TileXControl = "CheatsTeleportTileX";
    private const string TileYControl = "CheatsTeleportTileY";

    // Drawn on the row of another field.
    private static readonly HashSet<string> drawnInRow = new HashSet<string> { "critterDistance", "critterHostile", "skillValue" };

    // The stacks worth spawning in a pile: the ammunition and the money.
    private static readonly EObjectType[] stackKinds =
    {
        EObjectType.SlingStone, EObjectType.Arrow, EObjectType.CrossbowBolt, EObjectType.Coin, EObjectType.GoldCoin,
    };

    // What each list offers, in alphabetical order.
    private static EObjectType[] objectTypes;
    private static EObjectType[] stackTypes;
    private static EObjectType[] critterTypes;
    private static ESkill[] skills;
    private static string[] skillNames;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        MakeLists();

        Cheats cheats = (Cheats)target;
        FieldInfo[] kept = Cheats.KeptFields();
        object[] before = Array.ConvertAll(kept, f => f.GetValue(cheats));
        Action action = null;

        // Read before the boxes are drawn: the box that has the focus takes the key when it draws.
        Event e = Event.current;
        string focused = GUI.GetNameOfFocusedControl();
        bool enterInTile = e.type == EventType.KeyDown
            && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            && (focused == TileXControl || focused == TileYControl);

        SerializedProperty prop = serializedObject.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            switch (prop.name)
            {
            case "m_Script":
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(prop);
                }
                break;
            case "level":
                DrawLevel(prop);
                break;
            case "teleportToTile":
                if (DrawTeleport(prop) || enterInTile)
                {
                    action = cheats.Teleport;
                }
                break;
            case "objectToSpawn":
                if (DrawSpawn("Spawn Object", prop, objectTypes))
                {
                    action = cheats.SpawnObject;
                }
                break;
            case "stackToSpawn":
                if (DrawSpawn("Spawn Stack", prop, stackTypes))
                {
                    action = cheats.SpawnStack;
                }
                break;
            case "critterToSpawn":
                if (DrawSpawn("Spawn Creature", prop, critterTypes))
                {
                    action = cheats.SpawnCritter;
                }
                DrawCritterOptions();
                break;
            case "skillToSet":
                DrawSkill(prop);
                break;
            default:
                if (!drawnInRow.Contains(prop.name))
                {
                    EditorGUILayout.PropertyField(prop, true);
                }
                break;
            }
        }

        serializedObject.ApplyModifiedProperties();

        for (int i = 0; i < kept.Length; i++)
        {
            if (!Equals(before[i], kept[i].GetValue(cheats)))
            {
                cheats.KeepInEditorPrefs(kept[i]);
            }
        }

        // After the apply, so the action sees what was just picked or typed.
        if (action != null && Application.isPlaying)
        {
            action();
        }
    }

    /// <summary>The level, with a + and a - button that stop at the first and the last level.</summary>
    private static void DrawLevel(SerializedProperty level)
    {
        float height = EditorGUIUtility.singleLineHeight;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(level);
        using (new EditorGUI.DisabledScope(level.intValue >= Cheats.LastLevel))
        {
            if (GUILayout.Button("+", GUILayout.Width(22), GUILayout.Height(height)))
            {
                level.intValue++;
            }
        }
        using (new EditorGUI.DisabledScope(level.intValue <= Cheats.FirstLevel))
        {
            if (GUILayout.Button("-", GUILayout.Width(22), GUILayout.Height(height)))
            {
                level.intValue--;
            }
        }
        EditorGUILayout.EndHorizontal();

        // A level typed outside the ones there are does nothing in the game, so it is not kept.
        int clamped = Mathf.Clamp(level.intValue, Cheats.FirstLevel, Cheats.LastLevel);
        if (clamped != level.intValue)
        {
            level.intValue = clamped;
        }
    }

    /// <summary>
    /// The tile as X and Y on one row, with a Go button as wide as the Spawn ones. True when either
    /// box has just been changed or the button is clicked.
    /// </summary>
    private static bool DrawTeleport(SerializedProperty tile)
    {
        float height = EditorGUIUtility.singleLineHeight;
        Vector2Int v = tile.vector2IntValue;
        bool clicked;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Teleport");
        GUI.SetNextControlName(TileXControl);
        int x = EditorGUILayout.DelayedIntField(v.x, GUILayout.MinWidth(30));
        GUILayout.Label("x", GUILayout.ExpandWidth(false));
        GUI.SetNextControlName(TileYControl);
        int y = EditorGUILayout.DelayedIntField(v.y, GUILayout.MinWidth(30));
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            clicked = GUILayout.Button("Go", GUILayout.Width(ButtonWidth()), GUILayout.Height(height));
        }
        EditorGUILayout.EndHorizontal();

        if (x != v.x || y != v.y)
        {
            tile.vector2IntValue = new Vector2Int(x, y);
            return true;
        }
        return clicked;
    }

    /// <summary>The width of the buttons beside the rows: the Spawn button's.</summary>
    private static float ButtonWidth()
    {
        return GUI.skin.button.CalcSize(new GUIContent("Spawn")).x;
    }

    /// <summary>
    /// A list of types to search, with a Spawn button beside it. True when the button is clicked.
    /// </summary>
    private bool DrawSpawn(string label, SerializedProperty prop, EObjectType[] types)
    {
        float height = EditorGUIUtility.singleLineHeight;
        GUIContent spawn = new GUIContent("Spawn");
        bool clicked;

        if (Array.IndexOf(types, (EObjectType)prop.intValue) < 0)
        {
            // Not one of this list's types: take the first, so the button spawns what is shown.
            prop.intValue = (int)types[0];
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(label);
        Rect rect = EditorGUILayout.GetControlRect(false, height, GUILayout.ExpandWidth(true));
        if (EditorGUI.DropdownButton(rect, new GUIContent(((EObjectType)prop.intValue).ToString()), FocusType.Keyboard))
        {
            Cheats cheats = (Cheats)target;
            string path = prop.propertyPath;
            PopupWindow.Show(rect, new TypePopup(types, (EObjectType)prop.intValue, rect.width, picked => Pick(cheats, path, picked)));
        }
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            clicked = GUILayout.Button(spawn, GUILayout.Width(ButtonWidth()), GUILayout.Height(height));
        }
        EditorGUILayout.EndHorizontal();
        return clicked;
    }

    /// <summary>What the list picked, written as the Inspector writes it, and kept.</summary>
    private static void Pick(Cheats cheats, string path, EObjectType picked)
    {
        if (cheats == null)
        {
            return;
        }

        SerializedObject so = new SerializedObject(cheats);
        so.FindProperty(path).intValue = (int)picked;
        so.ApplyModifiedProperties();
        cheats.KeepInEditorPrefs(typeof(Cheats).GetField(path));
    }

    /// <summary>The creature's distance with its attitude beside it, on the row under its list.</summary>
    private void DrawCritterOptions()
    {
        SerializedProperty distance = serializedObject.FindProperty("critterDistance");
        SerializedProperty hostile = serializedObject.FindProperty("critterHostile");
        GUIContent hostileLabel = new GUIContent(hostile.displayName);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(distance);
        GUILayout.Space(8);
        GUILayout.Label(hostileLabel, GUILayout.Width(EditorStyles.label.CalcSize(hostileLabel).x));
        EditorGUILayout.PropertyField(hostile, GUIContent.none, GUILayout.Width(16));
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>The skill, in alphabetical order, with its value beside it.</summary>
    private void DrawSkill(SerializedProperty prop)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Set Skill");
        int index = Mathf.Max(0, Array.IndexOf(skills, (ESkill)prop.intValue));
        int picked = EditorGUILayout.Popup(index, skillNames);
        if (picked != index || prop.intValue != (int)skills[index])
        {
            prop.intValue = (int)skills[picked];
        }
        EditorGUILayout.PropertyField(serializedObject.FindProperty("skillValue"), GUIContent.none, GUILayout.Width(50));
        EditorGUILayout.EndHorizontal();
    }

    private static void MakeLists()
    {
        if (skills != null)
        {
            return;
        }

        skills = (ESkill[])Enum.GetValues(typeof(ESkill));
        Array.Sort(skills, (a, b) => string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase));
        skillNames = Array.ConvertAll(skills, s => s.ToString());

        List<EObjectType> objects = new List<EObjectType>();
        List<EObjectType> critters = new List<EObjectType>();
        foreach (EObjectType type in Enum.GetValues(typeof(EObjectType)))
        {
            ((int)type >= 64 && (int)type < 128 ? critters : objects).Add(type);
        }
        objectTypes = SortedByName(objects.ToArray());
        stackTypes = SortedByName((EObjectType[])stackKinds.Clone());
        critterTypes = SortedByName(critters.ToArray());
    }

    private static EObjectType[] SortedByName(EObjectType[] types)
    {
        Array.Sort(types, (a, b) => string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase));
        return types;
    }

    /// <summary>
    /// A list of object types under its row, with a search box on top: typing narrows it to the
    /// names that contain the text. Up and Down move the highlight, Enter picks it, a click picks
    /// the one clicked, Escape closes.
    /// </summary>
    private class TypePopup : PopupWindowContent
    {
        private const float ListHeight = 320.0f;

        private readonly EObjectType[] types;
        private readonly Action<EObjectType> picked;
        private readonly float width;
        private readonly List<EObjectType> shown = new List<EObjectType>();
        private SearchField searchField;
        private string search = "";
        private int highlighted;
        private Vector2 scroll;
        private bool scrollToHighlight = true;

        public TypePopup(EObjectType[] types, EObjectType current, float width, Action<EObjectType> picked)
        {
            this.types = types;
            this.picked = picked;
            this.width = Mathf.Max(240.0f, width);
            Filter();
            highlighted = Mathf.Max(0, shown.IndexOf(current));
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(width, ListHeight);
        }

        public override void OnOpen()
        {
            searchField = new SearchField();
            searchField.SetFocus();
        }

        public override void OnGUI(Rect rect)
        {
            float row = EditorGUIUtility.singleLineHeight;
            Rect searchRect = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, row);
            Rect listRect = new Rect(rect.x, searchRect.yMax + 4, rect.width, rect.yMax - searchRect.yMax - 4);

            // Before the search box draws, or it takes the keys.
            Event e = Event.current;
            if (e.type == EventType.KeyDown && shown.Count > 0)
            {
                if (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.UpArrow)
                {
                    highlighted = Mathf.Clamp(highlighted + (e.keyCode == KeyCode.DownArrow ? 1 : -1), 0, shown.Count - 1);
                    scrollToHighlight = true;
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    e.Use();
                    Pick(shown[highlighted]);
                    return;
                }
            }

            string typed = searchField.OnGUI(searchRect, search);
            if (typed != search)
            {
                search = typed;
                Filter();
                highlighted = 0;
                scroll = Vector2.zero;
            }

            if (scrollToHighlight)
            {
                scrollToHighlight = false;
                float top = highlighted * row;
                if (top < scroll.y)
                {
                    scroll.y = top;
                }
                else if (top + row > scroll.y + listRect.height)
                {
                    scroll.y = top + row - listRect.height;
                }
            }

            Rect view = new Rect(0, 0, listRect.width - 16, shown.Count * row);
            scroll = GUI.BeginScrollView(listRect, scroll, view);
            for (int i = 0; i < shown.Count; i++)
            {
                Rect r = new Rect(0, i * row, view.width, row);
                if (i == highlighted)
                {
                    EditorGUI.DrawRect(r, new Color(0.24f, 0.48f, 0.90f, 0.5f));
                }
                if (GUI.Button(r, shown[i].ToString(), EditorStyles.label))
                {
                    GUI.EndScrollView();
                    Pick(shown[i]);
                    return;
                }
            }
            GUI.EndScrollView();
        }

        private void Filter()
        {
            shown.Clear();
            foreach (EObjectType type in types)
            {
                if (search.Length == 0 || type.ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    shown.Add(type);
                }
            }
        }

        private void Pick(EObjectType type)
        {
            picked(type);
            editorWindow.Close();
        }
    }
}
