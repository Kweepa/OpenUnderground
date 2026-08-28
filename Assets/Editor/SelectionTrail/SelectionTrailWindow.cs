// ==============================================================================
//  The Selection Trail - Recently Selected Object Stack
//  Copyright (c) 2026 Kweepa
//  All rights reserved.
//
//  This script is part of The Selection Trail tool.
//  Do not distribute or share this code without explicit permission.
// ==============================================================================

using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kweepa.SelectionTrail
{
    // A window that keeps the last project and scene selections for quick access.
    // Provides filtering by type (Prefabs, Models, Materials, etc.) and favoriting.

    public class SelectionTrailWindow : EditorWindow
    {
       private string PrefsKey => Application.productName + "_SelectionTrail_Data";

       //------------------------------------------------------------

       [System.Serializable]
       private class Item
       {
          public Item(Object obj, bool favourite)
          {
             targetObject = obj;
             this.favourite = favourite;
          }
          public Object targetObject;
          public bool favourite;
       }

       [System.Serializable]
       private class PersistedItem
       {
          public string guid;
          public bool favourite;
       }

       [System.Serializable]
       private class PersistedData
       {
          public List<PersistedItem> items = new();
          public bool filterPrefab;
          public bool filterModel;
          public bool filterMaterial;
          public bool filterTexture;
          public bool filterAudio;
          public bool filterSceneInfo;
          public bool filterFolder;
          public string searchString;
          public float audioPreviewVolume;
          public bool keepHierarchyObjects;
          public bool showPreviews;
          public bool showHelp;
          public Vector2 helpScroll;
       }

       [SerializeField]
       private List<Item> selections = new();
       [SerializeField]
       private Vector2 scrollPosition;
       [SerializeField]
       private bool keepHierarchyObjects;
       [SerializeField]
       private bool showHelp;
       [SerializeField]
       private Vector2 helpScroll;


       [SerializeField] private bool filterPrefab;
       [SerializeField] private bool filterModel;
       [SerializeField] private bool filterMaterial;
       [SerializeField] private bool filterTexture;
       [SerializeField] private bool filterAudio;
       [SerializeField] private bool filterSceneInfo;
       [SerializeField] private bool filterFolder;
       [SerializeField] private string searchString = "";
       [SerializeField] private bool showPreviews = true;

       private bool showOptions;
       private bool selectedFromHistory;
       private Object lastSelected;

       private static readonly int skPruneToSize = 20;

       private bool reloadResources;
       private bool updateRegistered;
       private Texture2D favOnIcon;
       private Texture2D favOffIcon;
       private Texture2D favOnFocusIcon;
       private Texture2D favOffFocusIcon;
       private GUIContent deleteContent;
       private GUIStyle favStyle;
       private GUIStyle iconToolbarStyle;

       private GUIContent iconPrefab;
       private GUIContent iconModel;
       private GUIContent iconMaterial;
       private GUIContent iconTexture;
       private GUIContent iconAudio;
       private GUIContent iconSceneInfo;
       private GUIContent iconFolder;

       private GUIStyle trashStyle;
       private GUIStyle textToolbarStyle;
       private SearchField toolbarSearchField;

       // Hover preview tooltip state
       private Object hoverPreviewObject;
       private Rect hoverListAreaRect;

       // Audio preview state
       [SerializeField] private float audioPreviewVolume = 0.8f;
       private AudioClip playingPreviewClip;
       private static AudioSource previewAudioSource;

       // Internal drag-reorder state
       private const string DragSourceIndexKey = "SelectionTrail_DragSourceIndex";
       private int internalDragSourceIndex = -1;
       private int internalDragInsertIndex = -1;
       private bool internalDragTargetFavourite;
       private bool internalDragHasTargetFavourite;

       // Cached layout info for the favourites / non-favourites divider
       private Rect lastFavouriteRect;
       private Rect firstNonFavouriteRect;
       private int lastFavouriteVisualIndex = -1;
       private int firstNonFavouriteVisualIndex = -1;
       private bool hasLastFavouriteRect;
       private bool hasFirstNonFavouriteRect;


       //------------------------------------------------------------

       [MenuItem("Window/The Selection Trail")]
       private static void Go()
       {
          var window = GetWindow<SelectionTrailWindow>();
          window.wantsMouseMove = true;
          window.position = new Rect(Screen.width / 2.0f, Screen.height / 2.0f, 400.0f, 640.0f);
          window.titleContent = new GUIContent("The Selection Trail");
          window.Show();
       }

       //------------------------------------------------------------

       private bool FilterSelection(Object selection)
       {
          bool ok = false;
          if (selection != null && focusedWindow != null)
          {
             // can't use type directly because the types are not accessible.
             switch (focusedWindow.GetType().ToString())
             {
             case "UnityEditor.SceneHierarchyWindow":
                ok = keepHierarchyObjects;
                break;
             case "UnityEditor.ProjectBrowser":
                ok = true;
                break;
             }
          }
          return ok;
       }

       //------------------------------------------------------------

       private void OnSelectionChangeInternal()
       {
          if (!selectedFromHistory)
          {
             Object selection = Selection.activeObject;
             if (FilterSelection(selection))
             {
                Item found = selections.Find(x => x.targetObject == selection);
                Item itemToInsert = null;
                if (found != null)
                {
                   if (!found.favourite)
                   {
                      selections.Remove(found);
                      itemToInsert = found;
                   }
                }
                else
                {
                   itemToInsert = new Item(selection, favourite: false);
                }
                if (itemToInsert != null)
                {
                   // find the first non-favourite spot
                   for (int index = selections.Count - 1; index >= 0; --index)
                   {
                      if (!selections[index].favourite)
                      {
                         selections.Insert(index + 1, itemToInsert);
                         itemToInsert = null;
                         break;
                      }
                   }
                   if (itemToInsert != null)
                   {
                      // all favourites, so add at the end
                      selections.Insert(0, itemToInsert);
                   }
                }
             }
             // scroll to the top.
             scrollPosition = Vector2.zero;
             Repaint();
          }
          selectedFromHistory = false;
       }

       //------------------------------------------------------------

       private void UpdateInternal()
       {
          if (lastSelected != Selection.activeObject)
          {
             lastSelected = Selection.activeObject;
             OnSelectionChangeInternal();
          }

          bool foundNonFavourite = false;
          bool favouritesNeedSorting = false;
          for (int index = selections.Count - 1; index >= 0; --index)
          {
             // remove nulls
             if (selections[index].targetObject == null)
             {
                selections.RemoveAt(index);
                Repaint();
             }
             else if (!selections[index].favourite)
             {
                foundNonFavourite = true;
             }
             else if (foundNonFavourite)
             {
                favouritesNeedSorting = true;
             }
          }
          // check if favourites need bubbling up
          if (favouritesNeedSorting)
          {
             // pull the favourites out
             List<Item> favourites = new();
             for (int index = selections.Count - 1; index >= 0; --index)
             {
                if (selections[index].favourite)
                {
                   Item fav = selections[index];
                   selections.RemoveAt(index);
                   favourites.Add(fav);
                }
             }
             // add them to the top
             for (int index = favourites.Count - 1; index >= 0; --index)
             {
                selections.Add(favourites[index]);
             }
             Repaint();
          }
       }

       //------------------------------------------------------------

       private void ReloadResources()
       {
          favOnIcon = Resources.Load("SelectionTrailFavOn") as Texture2D;
          favOffIcon = Resources.Load("SelectionTrailFavOff") as Texture2D;
          favOnFocusIcon = Resources.Load("SelectionTrailFavOnFocus") as Texture2D;
          favOffFocusIcon = Resources.Load("SelectionTrailFavOffFocus") as Texture2D;
          
          deleteContent = EditorGUIUtility.IconContent("TreeEditor.Trash");
          deleteContent.tooltip = "Remove from history";

          iconPrefab = EditorGUIUtility.IconContent("Prefab Icon");
          if (iconPrefab != null) iconPrefab.tooltip = "Prefabs";
          
          iconModel = EditorGUIUtility.IconContent("Mesh Icon");
          if (iconModel != null) iconModel.tooltip = "Models";
          
          iconMaterial = EditorGUIUtility.IconContent("Material Icon");
          if (iconMaterial != null) iconMaterial.tooltip = "Materials";
          
          iconTexture = EditorGUIUtility.IconContent("Texture Icon");
          if (iconTexture != null) iconTexture.tooltip = "Textures";
          
          iconAudio = EditorGUIUtility.IconContent("AudioSource Icon");
          if (iconAudio != null) iconAudio.tooltip = "Audio";

          iconSceneInfo = EditorGUIUtility.IconContent("GameObject Icon");
          if (iconSceneInfo != null) iconSceneInfo.tooltip = "Scene Objects";

          iconFolder = EditorGUIUtility.IconContent("Folder Icon");
          if (iconFolder != null) iconFolder.tooltip = "Folders";

          favStyle = new GUIStyle(EditorStyles.iconButton);
          favStyle.padding = new RectOffset(0, 0, 0, 0);
          favStyle.margin = new RectOffset(4, 3, 0, 0);
          favStyle.fixedWidth = 12;
          favStyle.fixedHeight = 12;
          
          iconToolbarStyle = new GUIStyle(EditorStyles.miniButton);
          iconToolbarStyle.padding = new RectOffset(0, 0, 0, 0);
          iconToolbarStyle.margin = new RectOffset(0, 0, 0, 0);
          iconToolbarStyle.imagePosition = ImagePosition.ImageOnly;
          iconToolbarStyle.alignment = TextAnchor.MiddleCenter;
          iconToolbarStyle.fixedWidth = 18;
          iconToolbarStyle.fixedHeight = 18;

          textToolbarStyle = new GUIStyle(EditorStyles.miniButton);
          textToolbarStyle.imagePosition = ImagePosition.TextOnly;
          textToolbarStyle.padding = new RectOffset(3, 0, 0, 0);
          textToolbarStyle.margin = new RectOffset(0, 0, 0, 0);
          textToolbarStyle.alignment = TextAnchor.MiddleCenter;
          textToolbarStyle.fixedWidth = 0;
          textToolbarStyle.fixedHeight = 18;

          if (toolbarSearchField == null)
             toolbarSearchField = new SearchField();

          trashStyle = new GUIStyle(EditorStyles.iconButton);
          trashStyle.padding = new RectOffset(0, 0, 0, 0);
          trashStyle.margin = new RectOffset(0, 0, 0, 0);
          trashStyle.overflow = new RectOffset(0, 0, 0, 0);
       }

       //------------------------------------------------------------

       private static void DrawSelectedFilterBorder(Rect rect)
       {
          const float border = 2f;
          bool dark = EditorGUIUtility.isProSkin;
          Color edge = dark ? new Color(0.35f, 0.55f, 0.78f, 1f) : new Color(0.25f, 0.45f, 0.75f, 1f);
          EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, border), edge);
          EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - border, rect.width, border), edge);
          EditorGUI.DrawRect(new Rect(rect.x, rect.y, border, rect.height), edge);
          EditorGUI.DrawRect(new Rect(rect.xMax - border, rect.y, border, rect.height), edge);
       }

       //------------------------------------------------------------

       private bool DrawFilterToggle(ref Rect buttonRect, bool value, GUIContent icon)
       {
          bool updatedValue = GUI.Toggle(buttonRect, value, icon, iconToolbarStyle);
          if (Event.current.type == EventType.Repaint && updatedValue)
             DrawSelectedFilterBorder(buttonRect);
          buttonRect.x += buttonRect.width;
          return updatedValue;
       }

       //------------------------------------------------------------
       
       private void DrawHelpPage()
       {
          EditorGUILayout.BeginVertical("box");
          
          GUIStyle helpStyle = new GUIStyle(EditorStyles.label)
          {
             wordWrap = true,
             fontSize = 12,
             richText = true,
             padding = new RectOffset(20, 20, 12, 12),
             alignment = TextAnchor.UpperLeft
          };

          helpStyle.normal.textColor = helpStyle.hover.textColor = helpStyle.active.textColor = helpStyle.focused.textColor = GUI.skin.label.normal.textColor;

          string helpText = "<b>The Selection Trail</b> maintains a stack of your recently selected objects for rapid access.\n\n" +
                            "<b>Workflow & Features:</b>\n" +
                            "\u2022 <b>Filters:</b> Use the type icons in the top bar to toggle visibility for scene objects, prefabs, models, materials, etc. Click <b>All</b> to clear filters.\n" +
                            "\u2022 <b>Scene Objects:</b> The <b>Keep scene objects</b> option (gear menu) controls whether objects from the Hierarchy are added to the trail.\n" +
                            "\u2022 <b>Favourites:</b> Click the <b>Star icon</b> on any item to pin it. Favourites are always kept at the top of the list and survive pruning.\n" +
                            "\u2022 <b>Pruning:</b> Use <b>Prune trail</b> in the gear menu to reduce the trail to the 20 most recent non-favourite items. You can also click the <b>Trashcan icon</b> to remove individual items.\n\n" +
                            "<b>Interactions:</b>\n" +
                            "\u2022 <b>Single Click:</b> Focuses and pings the object in the Project or Hierarchy view.\n" +
                            "\u2022 <b>Double Click:</b> Fully selects the object (useful for quickly populating the Inspector).\n" +
                            "\u2022 <b>Drag & Drop:</b> Click and drag any item to move it into scene fields, folder paths, or directly into the Scene view.\n" +
                            "\u2022 <b>Audio Preview:</b> Hover over an audio clip and press <b>Space</b> to play it.\n\n" +
                            "<i>Click anywhere in this box to return to the trail list.</i>";

          if (GUILayout.Button(helpText, helpStyle))
          {
             showHelp = false;
          }

          EditorGUILayout.EndVertical();
       }

       //------------------------------------------------------------

       private void OnGUI()
       {
          if (Event.current.type == EventType.MouseMove)
          {
             Repaint();
          }

          if (reloadResources)
          {
             reloadResources = false;
             ReloadResources();
          }

          const float toolbarHeight = 18f;
          Rect toolbarRect = GUILayoutUtility.GetRect(0, toolbarHeight, GUILayout.ExpandWidth(true));
          
          bool anyFilter = filterPrefab || filterModel || filterMaterial || filterTexture || filterAudio || filterSceneInfo || filterFolder;
          
          if (Event.current.type == EventType.Repaint)
          {
             Color stripColor = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.16f, 0.16f, 1.0f)
                : new Color(0.73f, 0.73f, 0.73f, 1.0f);
             EditorGUI.DrawRect(toolbarRect, stripColor);
          }
          
          float buttonX = toolbarRect.x;
          float buttonY = toolbarRect.y;
          float buttonSize = toolbarRect.height;

          Rect buttonRect = new Rect(buttonX, buttonY, buttonSize, buttonSize);
          filterSceneInfo = DrawFilterToggle(ref buttonRect, filterSceneInfo, iconSceneInfo);
          filterPrefab = DrawFilterToggle(ref buttonRect, filterPrefab, iconPrefab);
          filterModel = DrawFilterToggle(ref buttonRect, filterModel, iconModel);
          filterMaterial = DrawFilterToggle(ref buttonRect, filterMaterial, iconMaterial);
          filterTexture = DrawFilterToggle(ref buttonRect, filterTexture, iconTexture);
          filterAudio = DrawFilterToggle(ref buttonRect, filterAudio, iconAudio);
          filterFolder = DrawFilterToggle(ref buttonRect, filterFolder, iconFolder);
          
          // "All" button
          Vector2 textSize = textToolbarStyle.CalcSize(new GUIContent("All"));
          buttonRect.width = Mathf.Max(buttonSize, textSize.x + 4f);
          bool clickAll = GUI.Toggle(buttonRect, !anyFilter, "All", textToolbarStyle);
          if (clickAll && anyFilter)
          {
             filterPrefab = filterModel = filterMaterial = filterTexture = filterAudio = filterSceneInfo = filterFolder = false;
          }

          // Search field with magnifier (fills gap between All and gear)
          float gearX = toolbarRect.xMax - buttonSize * 2f;
          float searchX = buttonRect.xMax + 2f;
          float searchWidth = (gearX - 2f) - searchX;
          if (searchWidth > 40f)
          {
             Rect searchRect = new Rect(searchX, buttonRect.y, searchWidth, buttonSize);
             searchString = toolbarSearchField.OnToolbarGUI(searchRect, searchString);
          }

          // Gear button (options) — mutually exclusive with Help
          buttonRect.x = gearX;
          buttonRect.width = buttonSize;
          bool gearPressed = GUI.Toggle(buttonRect, showOptions, new GUIContent(EditorGUIUtility.IconContent("Settings").image, "Options"), iconToolbarStyle);
          if (gearPressed != showOptions)
          {
             showOptions = gearPressed;
             if (showOptions) showHelp = false;
          }

          // Help button — mutually exclusive with Options, turns blue when help is shown
          buttonRect.x = toolbarRect.xMax - buttonSize;
          buttonRect.width = buttonSize;
          bool helpPressed = GUI.Toggle(buttonRect, showHelp, new GUIContent(EditorGUIUtility.IconContent("_Help").image, "Show Help"), iconToolbarStyle);
          if (helpPressed != showHelp)
          {
             showHelp = helpPressed;
             if (showHelp) showOptions = false;
          }


          bool drawnFavourite = false;
          bool drawnSeparator = false;
          bool drawnAnyItem = false;

          if (showHelp)
          {
             helpScroll = GUILayout.BeginScrollView(helpScroll, alwaysShowHorizontal: false, alwaysShowVertical: false);
             DrawHelpPage();
             GUILayout.EndScrollView();
          }
          else if (showOptions)
          {
             DrawOptionsPanel();
          }
          else
          {
             float listTop = toolbarRect.yMax + 1f;
             Rect listAreaRect = new Rect(0f, listTop, position.width, Mathf.Max(0f, position.height - listTop));

             if (Event.current.type == EventType.Repaint)
             {
                hoverPreviewObject = null;
             }

             Event evt = Event.current;
             bool hasInternalDrag = DragAndDrop.GetGenericData(DragSourceIndexKey) is int;
             bool pendingInternalDrop = false;
             bool dragHandledByRow = false;
             hasLastFavouriteRect = false;
             hasFirstNonFavouriteRect = false;
             lastFavouriteVisualIndex = -1;
             firstNonFavouriteVisualIndex = -1;

             scrollPosition = GUILayout.BeginScrollView(scrollPosition, alwaysShowHorizontal: false, alwaysShowVertical: false);

             // Pre-calculate divider visual indices for consistent marker drawing
             int lastFavVis = -1;
             int firstNonFavVis = -1;
             int visCount = 0;
             for (int i = selections.Count - 1; i >= 0; --i)
             {
                Item it = selections[i];
                if (it.targetObject == null) continue;

                bool show = !anyFilter;
                if (anyFilter)
                {
                   string p = AssetDatabase.GetAssetPath(it.targetObject);
                   if (!string.IsNullOrEmpty(p))
                   {
                      if (filterPrefab && it.targetObject is GameObject && p.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) show = true;
                      else if (filterModel && (it.targetObject is Mesh || (it.targetObject is GameObject && !p.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)))) show = true;
                      else if (filterMaterial && it.targetObject is Material) show = true;
                      else if (filterTexture && it.targetObject is Texture) show = true;
                      else if (filterAudio && it.targetObject is AudioClip) show = true;
                      else if (filterFolder && it.targetObject is DefaultAsset && AssetDatabase.IsValidFolder(p)) show = true;
                   }
                   else if (filterSceneInfo && it.targetObject is GameObject) show = true;
                }

                if (show && !string.IsNullOrWhiteSpace(searchString))
                {
                   string p = AssetDatabase.GetAssetPath(it.targetObject);
                   string n = it.targetObject.name;
                   if (it.targetObject is DefaultAsset && AssetDatabase.IsValidFolder(p)) n = p;
                   string mt = n + " " + (p ?? "");
                   string[] ws = searchString.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                   bool mAny = false;
                   foreach (var w in ws) if (mt.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0) { mAny = true; break; }
                   if (!mAny) show = false;
                }

                if (show)
                {
                   if (it.favourite) lastFavVis = visCount;
                   else if (firstNonFavVis == -1 && lastFavVis != -1) firstNonFavVis = visCount;
                   visCount++;
                }
             }
             lastFavouriteVisualIndex = lastFavVis;
             firstNonFavouriteVisualIndex = firstNonFavVis;
             int totalVisualItems = visCount;

             int visualRow = 0; // 0 = top row as drawn, increasing downward
             for (int index = selections.Count - 1; index >= 0; --index)
             {
                Item item = selections[index];

                if (item.targetObject == null)
                {
                   selections.RemoveAt(index);
                   continue;
                }

                bool showItem = !anyFilter;
                if (anyFilter && item.targetObject != null)
                {
                   string path = AssetDatabase.GetAssetPath(item.targetObject);
                   if (!string.IsNullOrEmpty(path))
                   {
                      if (filterPrefab && item.targetObject is GameObject && path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) showItem = true;
                      else if (filterModel && (item.targetObject is Mesh || (item.targetObject is GameObject && !path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)))) showItem = true;
                      else if (filterMaterial && item.targetObject is Material) showItem = true;
                      else if (filterTexture && item.targetObject is Texture) showItem = true;
                      else if (filterAudio && item.targetObject is AudioClip) showItem = true;
                      else if (filterFolder && item.targetObject is DefaultAsset && AssetDatabase.IsValidFolder(path)) showItem = true;
                   }
                   else if (string.IsNullOrEmpty(path))
                   {
                      if (filterSceneInfo && item.targetObject is GameObject) showItem = true;
                   }
                }
                else if (anyFilter && item.targetObject == null)
                {
                   showItem = false; // Don't show missing objects if filtering is active
                }

                // Search: show only if item matches any of the search words (name or path)
                if (showItem && !string.IsNullOrWhiteSpace(searchString))
                {
                   string path = AssetDatabase.GetAssetPath(item.targetObject);
                   string name = item.targetObject.name;
                   if (item.targetObject is DefaultAsset && AssetDatabase.IsValidFolder(path))
                      name = path;
                   string matchTarget = name + " " + (path ?? "");
                   string[] words = searchString.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                   bool matchesAny = false;
                   for (int w = 0; w < words.Length && !matchesAny; w++)
                   {
                      if (matchTarget.IndexOf(words[w], System.StringComparison.OrdinalIgnoreCase) >= 0)
                         matchesAny = true;
                   }
                   if (!matchesAny) showItem = false;
                }

                if (!showItem)
                {
                   continue;
                }

                int visualIndex = visualRow;
                drawnAnyItem = true;

                if (item.favourite)
                {
                   drawnFavourite = true;
                }
                else if (drawnFavourite && !drawnSeparator)
                {
                   drawnSeparator = true;
                   GUILayout.Space(4);
                   Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(1), GUILayout.ExpandWidth(true));
                   EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
                   GUILayout.Space(4);
                }

               GUILayout.BeginHorizontal();

                Rect starRect = GUILayoutUtility.GetRect(12, 12, favStyle);
                starRect.y += (EditorGUIUtility.singleLineHeight - 12) / 2;
                
                if (GUI.Button(starRect, GUIContent.none, favStyle))
                {
                   item.favourite = !item.favourite;
                   Repaint();
                }

                if (Event.current.type == EventType.Repaint)
                {
                   bool isDarkTheme = EditorGUIUtility.isProSkin;
                   bool isStarHover = starRect.Contains(Event.current.mousePosition);
                   Texture2D starIcon;
                   if (isDarkTheme)
                   {
                      // Dark theme: use full hover/non-hover icon set.
                      starIcon = item.favourite
                         ? (isStarHover ? favOnFocusIcon : favOnIcon)
                         : (isStarHover ? favOffFocusIcon : favOffIcon);
                   }
                   else
                   {
                      // Light theme: keep favourite legible and avoid hover variant swap.
                      starIcon = item.favourite ? favOnFocusIcon : favOffIcon;
                   }

                   if (starIcon != null)
                   {
                      GUI.DrawTexture(starRect, starIcon);
                   }
                   else
                   {
                      // Silent fallback if a resource texture is missing.
                      EditorStyles.toggle.Draw(starRect, GUIContent.none, false, false, item.favourite, false);
                   }
                }

                Rect objRect = GUILayoutUtility.GetRect(0, 10000, EditorGUIUtility.singleLineHeight, EditorGUIUtility.singleLineHeight);

                // Track the last favourite row rect and the first non-favourite rect in visual space.
                if (item.favourite)
                {
                   lastFavouriteRect = objRect;
                   lastFavouriteVisualIndex = visualIndex;
                   hasLastFavouriteRect = true;
                }
                else if (!item.favourite && drawnFavourite && !hasFirstNonFavouriteRect)
                {
                   firstNonFavouriteRect = objRect;
                   firstNonFavouriteVisualIndex = visualIndex;
                   hasFirstNonFavouriteRect = true;
                }

                // Inside BeginScrollView, layout rects and mouse position are in content (scrolled) space.
                bool isOverRow = objRect.Contains(Event.current.mousePosition);

                if (Event.current.type == EventType.MouseDrag && isOverRow && item.targetObject != null)
                {
                   DragAndDrop.PrepareStartDrag();
                   DragAndDrop.objectReferences = new Object[] { item.targetObject };
                   string path = AssetDatabase.GetAssetPath(item.targetObject);
                   if (!string.IsNullOrEmpty(path))
                   {
                      DragAndDrop.paths = new string[] { path };
                   }

                   DragAndDrop.SetGenericData(DragSourceIndexKey, index);
                   internalDragSourceIndex = index;
                   internalDragInsertIndex = -1;

                   DragAndDrop.StartDrag("Drag " + item.targetObject.name);
                   Event.current.Use();
                }

                if (Event.current.type == EventType.MouseDown && isOverRow && item.targetObject != null)
                {
                   if (Event.current.clickCount == 1)
                   {
                      EditorGUIUtility.PingObject(item.targetObject);
                   }
                   else if (Event.current.clickCount == 2)
                   {
                      Selection.activeObject = item.targetObject;
                      selectedFromHistory = true;
                   }
                   Event.current.Use();
                }

                if (Event.current.type == EventType.Repaint)
                {
                   bool isDraggingAnything = DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0;
                   bool highlightRow = !isDraggingAnything && isOverRow;

                   EditorStyles.objectField.Draw(objRect, GUIContent.none, highlightRow, false, false, false);

                   // Draw a clear insertion marker at the correct edge for this row when reordering.
                   // At the favourites divider we draw only one bar: below last favourite if dropping to favourites, above first non-favourite if dropping to non-favourites.
                   if (hasInternalDrag)
                   {
                      bool isLastFavRow = visualIndex == lastFavouriteVisualIndex;
                      bool isFirstNonFavRow = visualIndex == firstNonFavouriteVisualIndex;
                      bool insertAtDivider = firstNonFavouriteVisualIndex != -1 && internalDragInsertIndex == firstNonFavouriteVisualIndex;

                      float markerHeight = 2.0f;
                      Color markerColor = EditorGUIUtility.isProSkin ? new Color(0.1f, 0.6f, 1.0f, 1.0f) : new Color(0.1f, 0.4f, 0.8f, 1.0f);

                      if (insertAtDivider)
                      {
                         // Divider drop: show only one bar based on current targetFavourite setting
                         if (internalDragHasTargetFavourite && internalDragTargetFavourite && isLastFavRow)
                         {
                            // Drop at bottom of favorites: draw just under the last favorite row, fully inside the gap
                            float y = objRect.yMax + 1.0f;
                            Rect markerRect = new Rect(objRect.x, y, objRect.width, markerHeight);
                            EditorGUI.DrawRect(markerRect, markerColor);
                         }
                         else if (internalDragHasTargetFavourite && !internalDragTargetFavourite && isFirstNonFavRow)
                         {
                            // Drop at top of non-favorites: draw just above the first non-fav row, fully inside the gap
                            float y = objRect.yMin - 3.0f;
                            Rect markerRect = new Rect(objRect.x, y, objRect.width, markerHeight);
                            EditorGUI.DrawRect(markerRect, markerColor);
                         }
                      }
                      else
                      {
                         // Normal drop: draw above row, unless it's the very bottom of the last row.
                         bool markerHereAbove = internalDragInsertIndex == visualIndex;
                         bool markerHereBelow = (visualIndex == totalVisualItems - 1) && (internalDragInsertIndex == visualIndex + 1);

                         if (markerHereAbove || markerHereBelow)
                         {
                            float y = markerHereBelow ? objRect.yMax - 1.0f : objRect.yMin - 1.0f;
                            Rect markerRect = new Rect(objRect.x, y, objRect.width, markerHeight);
                            EditorGUI.DrawRect(markerRect, markerColor);
                         }
                      }
                   }

                   // Don't show hover previews while a drag is active.
                   if (!isDraggingAnything && isOverRow && showPreviews)
                   {
                      hoverPreviewObject = item.targetObject;
                      hoverListAreaRect = listAreaRect;
                   }
                   
                   Texture icon = AssetPreview.GetMiniThumbnail(item.targetObject);
                   if (icon == null)
                   {
                      GUIContent content = EditorGUIUtility.ObjectContent(item.targetObject, item.targetObject.GetType());
                      if (content != null)
                      {
                         icon = content.image;
                      }
                   }

                   if (icon != null)
                   {
                      Rect iconRect = new Rect(objRect.x + 2, objRect.y + (objRect.height - 14) / 2, 14, 14);
                      GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                   }
                   
                   Rect textRect = new Rect(objRect.x + 18, objRect.y, objRect.width - 20, objRect.height);
                   GUIStyle labelStyle = new GUIStyle(EditorStyles.label);
                   labelStyle.alignment = TextAnchor.MiddleLeft;
                   
                   string labelName = item.targetObject.name;
                   string assetPath = AssetDatabase.GetAssetPath(item.targetObject);
                   if (item.targetObject is DefaultAsset && AssetDatabase.IsValidFolder(assetPath))
                   {
                      labelName = assetPath;
                      GUIContent content = new GUIContent(labelName);
                      if (labelStyle.CalcSize(content).x > textRect.width)
                      {
                         string[] parts = assetPath.Split('/');
                         for (int p = 1; p < parts.Length; ++p)
                         {
                            labelName = ".../" + string.Join("/", parts, p, parts.Length - p);
                            content.text = labelName;
                            if (labelStyle.CalcSize(content).x <= textRect.width)
                            {
                               break;
                            }
                         }
                      }
                   }
                   
                   GUI.Label(textRect, labelName, labelStyle);
                }

                // Calculate the hit rect for the button first to check the mouse position
                Rect trashRect = GUILayoutUtility.GetRect(16, 16);
                trashRect.y += (EditorGUIUtility.singleLineHeight - 16) / 2;

                Color originalColor = GUI.color;
                if (!trashRect.Contains(Event.current.mousePosition))
                {
                   GUI.color = new Color(originalColor.r, originalColor.g, originalColor.b, 0.2f);
                }

                if (GUI.Button(trashRect, deleteContent, trashStyle))
                {
                   selections.Remove(item);
                   Repaint();
                }
                
                GUI.color = originalColor;
                GUILayout.Space(2);
                GUILayout.EndHorizontal();

                // While dragging internally, track potential insert index based on mouse position.
                if (hasInternalDrag && (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform))
                {
                   object generic = DragAndDrop.GetGenericData(DragSourceIndexKey);
                   if (generic is int sourceIndex)
                   {
                      internalDragSourceIndex = sourceIndex;

                      // Only react to drags that are actually over this row's area.
                      if (objRect.Contains(evt.mousePosition))
                      {
                         float midY = objRect.yMin + objRect.height * 0.5f;
                         bool topHalf = evt.mousePosition.y < midY;
                         int insertVisual = topHalf ? visualIndex : visualIndex + 1;
                         internalDragInsertIndex = insertVisual;

                         // When dropping on a row, inherit that row's favourite state.
                         internalDragTargetFavourite = item.favourite;
                         internalDragHasTargetFavourite = true;

                         DragAndDrop.visualMode = DragAndDropVisualMode.Move;

                         if (evt.type == EventType.DragPerform)
                         {
                            pendingInternalDrop = true;
                            dragHandledByRow = true;
                            evt.Use();
                         }
                         else
                         {
                            dragHandledByRow = true;
                            evt.Use();
                         }
                      }
                   }
                }

                visualRow++;
             }
             // If we have an internal drag but no row consumed the event, check if we're over the favourites divider.
             if (hasInternalDrag && (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && !dragHandledByRow)
             {
                if (hasLastFavouriteRect && hasFirstNonFavouriteRect)
                {
                   float top = lastFavouriteRect.yMax;
                   float bottom = firstNonFavouriteRect.yMin;

                   // Use same coordinate space as row hit test (objRect.Contains(evt.mousePosition)) so divider and rows are consistent.
                   float mouseY = evt.mousePosition.y;

                   if (mouseY >= top && mouseY <= bottom)
                   {
                      float mid = (top + bottom) * 0.5f;

                      // In the divider band: above mid = favourites, below mid = non-favourites.
                      bool toFavourites = mouseY < mid;

                      internalDragInsertIndex = firstNonFavouriteVisualIndex; // logical divider position
                      internalDragTargetFavourite = toFavourites;
                      internalDragHasTargetFavourite = true;

                      DragAndDrop.visualMode = DragAndDropVisualMode.Move;

                      if (evt.type == EventType.DragPerform)
                      {
                         pendingInternalDrop = true;
                         evt.Use();
                      }
                      else
                      {
                         evt.Use();
                      }
                   }
                }
             }

             if (selections.Count > 0 && !drawnAnyItem)
             {
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUIStyle warningStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
                warningStyle.alignment = TextAnchor.MiddleCenter;
                warningStyle.normal.textColor = EditorStyles.centeredGreyMiniLabel.normal.textColor;
                
                GUILayout.Label("No items match the filters. Click the All button to clear the filters.", warningStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
             }

             GUILayout.EndScrollView();

             // Suppress the hover preview tooltip while dragging to reduce visual noise
             bool anyDragActive = DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0;
             if (!anyDragActive)
             {
                DrawHoverPreviewTooltip();
             }
             HandleInternalDragDrop(pendingInternalDrop);
             HandleAudioPreviewInput();
          }
       }

       private void DrawOptionsPanel()
       {
          GUILayout.Space(12f);
          GUILayout.BeginVertical();
          GUILayout.Space(8f);

          if (GUILayout.Button(new GUIContent("Prune trail", "Reduce history to the 20 most recent non-favourite items."), GUILayout.Height(24f)))
          {
             if (selections.Count > skPruneToSize)
             {
                selections.RemoveRange(0, selections.Count - skPruneToSize);
                Repaint();
             }
          }

          keepHierarchyObjects = GUILayout.Toggle(keepHierarchyObjects, new GUIContent("Keep scene objects", "Add Hierarchy selections to history."));

          showPreviews = GUILayout.Toggle(showPreviews, new GUIContent("Show previews", "Show preview tooltips when hovering list items."));

          audioPreviewVolume = EditorGUILayout.Slider(
             new GUIContent("Audio preview volume", "Multiplier applied when previewing audio clips."),
             audioPreviewVolume,
             0.0f,
             1.0f);

          GUILayout.EndVertical();
       }

       //------------------------------------------------------------

       private void DrawHoverPreviewTooltip()
       {
          if (!showPreviews || hoverPreviewObject == null)
          {
             return;
          }

          if (Event.current.type != EventType.Repaint)
          {
             return;
          }

          const float previewSize = 100.0f;
          const float padding = 6.0f;
          const float infoHeight = 26.0f; // ~2 lines of miniLabel
          const float mouseOffset = 16.0f;

          Vector2 mousePos = Event.current.mousePosition;
          float width = previewSize + padding * 2.0f;
          float height = previewSize + infoHeight + padding * 3.0f;

          // Keep popup entirely inside the list area (never over toolbar or bottom bar).
          Rect clampRect = hoverListAreaRect;

          // If mouse is in lower half of list area, show popup above the cursor to avoid overlapping the bar.
          bool showAbove = mousePos.y > (clampRect.y + clampRect.height * 0.5f);
          float x = mousePos.x + mouseOffset;
          float y = showAbove ? mousePos.y - height - mouseOffset : mousePos.y + mouseOffset;

          Rect rect = new Rect(x, y, width, height);

          // Clamp so popup stays fully inside list area (no overlap with toolbar or footer).
          rect.x = Mathf.Clamp(rect.x, clampRect.xMin, clampRect.xMax - rect.width);
          rect.y = Mathf.Clamp(rect.y, clampRect.yMin, clampRect.yMax - rect.height);

          Color bg = EditorGUIUtility.isProSkin
             ? new Color(0.16f, 0.16f, 0.16f, 0.95f)
             : new Color(0.95f, 0.95f, 0.95f, 0.95f);
          Color border = EditorGUIUtility.isProSkin
             ? new Color(0.0f, 0.0f, 0.0f, 0.8f)
             : new Color(0.0f, 0.0f, 0.0f, 0.4f);

          EditorGUI.DrawRect(rect, bg);
          EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1.0f), border);
          EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1.0f, rect.width, 1.0f), border);
          EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1.0f, rect.height), border);
          EditorGUI.DrawRect(new Rect(rect.xMax - 1.0f, rect.y, 1.0f, rect.height), border);

          Rect previewRect = new Rect(
             rect.x + padding,
             rect.y + padding,
             previewSize,
             previewSize);

          Texture preview = AssetPreview.GetAssetPreview(hoverPreviewObject);
          if (preview == null)
          {
             preview = AssetPreview.GetMiniThumbnail(hoverPreviewObject);
          }

          if (preview != null)
          {
             GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit);

             // If the preview is still being generated, keep repainting while hovered.
             if (!AssetPreview.IsLoadingAssetPreview(hoverPreviewObject.GetInstanceID()))
             {
                // No-op, but this conditional keeps intent clear.
             }
          }

          Rect infoRect = new Rect(
             rect.x + padding,
             previewRect.yMax + padding,
             rect.width - padding * 2.0f,
             infoHeight);

          GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel)
          {
             wordWrap = true,
             alignment = TextAnchor.UpperLeft
          };

         string infoText = GetContextualInfo(hoverPreviewObject);
         if (!string.IsNullOrEmpty(infoText))
         {
            if (hoverPreviewObject is AudioClip)
            {
               // Top line: contextual info (length, channels, etc.)
               Rect infoLineRect = new Rect(infoRect.x, infoRect.y, infoRect.width, infoRect.height * 0.5f);
               GUI.Label(infoLineRect, infoText, infoStyle);

               // Bottom line: yellow play arrow and "Press Space" on the same line
               Rect controlLineRect = new Rect(infoRect.x, infoLineRect.yMax, infoRect.width, infoRect.height * 0.5f);
               Rect iconRect = new Rect(controlLineRect.x, controlLineRect.y, 16.0f, 16.0f);
               GUIContent playIcon = EditorGUIUtility.IconContent("d_PlayButton");
               if (playIcon != null && playIcon.image != null)
               {
                  Color prevColor = GUI.color;
                  GUI.color = Color.yellow;
                  GUI.DrawTexture(iconRect, playIcon.image, ScaleMode.ScaleToFit);
                  GUI.color = prevColor;
               }

               Rect textRect = new Rect(controlLineRect.x + 18.0f, controlLineRect.y, controlLineRect.width - 18.0f, controlLineRect.height);
               GUI.Label(textRect, "Press Space", infoStyle);
            }
            else
            {
               GUI.Label(infoRect, infoText, infoStyle);
            }
         }
       }

       private static AudioSource GetOrCreatePreviewAudioSource()
       {
          if (previewAudioSource != null)
          {
             return previewAudioSource;
          }

          GameObject go = EditorUtility.CreateGameObjectWithHideFlags(
             "SelectionTrail_AudioPreview",
             HideFlags.HideAndDontSave,
             typeof(AudioSource));

          previewAudioSource = go.GetComponent<AudioSource>();
          previewAudioSource.playOnAwake = false;
          previewAudioSource.loop = false;

          return previewAudioSource;
       }

       private void PlayAudioPreview(AudioClip clip)
       {
          if (clip == null)
          {
             return;
          }

          AudioSource src = GetOrCreatePreviewAudioSource();
          if (src.isPlaying)
          {
             src.Stop();
          }

          src.clip = clip;
          src.volume = Mathf.Clamp01(audioPreviewVolume);
          src.loop = false;
          src.Play();

          playingPreviewClip = clip;
       }

       private void StopAudioPreview()
       {
          if (previewAudioSource != null && previewAudioSource.isPlaying)
          {
             previewAudioSource.Stop();
          }

          playingPreviewClip = null;
       }

       private void HandleAudioPreviewInput()
       {
          Event evt = Event.current;
          if (evt == null)
          {
             return;
          }

          if (playingPreviewClip != null)
          {
             if (!(hoverPreviewObject is AudioClip hoveredClip && hoveredClip == playingPreviewClip))
             {
                StopAudioPreview();
             }
          }

          if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Space)
          {
             if (hoverPreviewObject is AudioClip clip)
             {
                if (playingPreviewClip == clip)
                {
                   StopAudioPreview();
                }
                else
                {
                   PlayAudioPreview(clip);
                }

                evt.Use();
             }
          }
       }

       private void HandleInternalDragDrop(bool performDrop)
       {
          Event evt = Event.current;

          // Clear visual state when drag leaves the window.
          if (evt != null && evt.type == EventType.DragExited)
          {
             internalDragSourceIndex = -1;
             internalDragInsertIndex = -1;
             DragAndDrop.SetGenericData(DragSourceIndexKey, null);
             return;
          }

          if (!performDrop)
          {
             return;
          }

          if (!(DragAndDrop.GetGenericData(DragSourceIndexKey) is int sourceIndex))
          {
             internalDragSourceIndex = -1;
             internalDragInsertIndex = -1;
             return;
          }

          int count = selections.Count;
          if (sourceIndex < 0 || sourceIndex >= count)
          {
             internalDragSourceIndex = -1;
             internalDragInsertIndex = -1;
             return;
          }

          // Build a temporary list in visual order: index 0 = top row as drawn.
          List<Item> visualOrder = new List<Item>(count);
          for (int i = count - 1; i >= 0; --i)
          {
             visualOrder.Add(selections[i]);
          }

          // Find the source in visual space.
          Item sourceItem = selections[sourceIndex];
          int sourceVisual = visualOrder.IndexOf(sourceItem);
          if (sourceVisual < 0)
          {
             internalDragSourceIndex = -1;
             internalDragInsertIndex = -1;
             DragAndDrop.SetGenericData(DragSourceIndexKey, null);
             return;
          }

          int destVisual = Mathf.Clamp(internalDragInsertIndex, 0, count);

          // If dropping back to the same visual position (or immediately after), do nothing.
          // BUT if we are changing the favorite state, we must process it!
          bool isSamePosition = (destVisual == sourceVisual || destVisual == sourceVisual + 1);
          bool isChangingFavourite = internalDragHasTargetFavourite && (sourceItem.favourite != internalDragTargetFavourite);

          if (isSamePosition && !isChangingFavourite)
          {
             internalDragSourceIndex = -1;
             internalDragInsertIndex = -1;
             DragAndDrop.SetGenericData(DragSourceIndexKey, null);
             return;
          }

          // Move inside visualOrder, then reapply to selections.
          visualOrder.RemoveAt(sourceVisual);

          if (destVisual > sourceVisual)
          {
             destVisual--;
          }

          destVisual = Mathf.Clamp(destVisual, 0, visualOrder.Count);

          // If we have an explicit favourite target from the hover logic, use it.
          // Otherwise, keep the item's existing favourite state.
          if (internalDragHasTargetFavourite)
          {
             sourceItem.favourite = internalDragTargetFavourite;
          }

          visualOrder.Insert(destVisual, sourceItem);

          // Write back: visual index 0 is selections[count-1], bottom-up.
          for (int v = 0; v < visualOrder.Count; ++v)
          {
             selections[count - 1 - v] = visualOrder[v];
          }

          internalDragSourceIndex = -1;
          internalDragInsertIndex = -1;
          internalDragHasTargetFavourite = false;
          DragAndDrop.SetGenericData(DragSourceIndexKey, null);

          DragAndDrop.AcceptDrag();
          Repaint();
       }

       // Returns a short line of info that isn't visible from the list entry:
       // folder = item count, prefab = first non-Transform component, mesh = verts/tris, texture = size/format.

       private static string GetContextualInfo(Object obj)
       {
          if (obj == null) return null;

         string path = AssetDatabase.GetAssetPath(obj);

         // Scene object: GameObject with no asset path
         if (obj is GameObject sceneGo && string.IsNullOrEmpty(path))
         {
            return "Scene object";
         }

         // Folder: number of direct items (subfolders + files, excluding .meta)
         if (obj is DefaultAsset && !string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
          {
             string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
             if (Directory.Exists(fullPath))
             {
                int dirs = Directory.GetDirectories(fullPath).Length;
                int files = Directory.GetFiles(fullPath).Count(f => !f.EndsWith(".meta"));
                return dirs + " folders, " + files + " files";
             }
          }

          // Prefab: first non-Transform component on root
          if (obj is GameObject go && !string.IsNullOrEmpty(path) && path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
          {
             foreach (var c in go.GetComponents<Component>())
             {
                if (c != null && !(c is Transform))
                {
                   return "Root: " + c.GetType().Name;
                }
             }
             return "Root: (Transform only)";
          }

          // Model (GameObject from .fbx etc.): itemize meshes, materials, anim clips
          if (obj is GameObject modelRoot && !string.IsNullOrEmpty(path) && !path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
          {
             int meshCount = 0;
             var meshFilters = modelRoot.GetComponentsInChildren<MeshFilter>(true);
             foreach (var mf in meshFilters)
             {
                if (mf != null && mf.sharedMesh != null) meshCount++;
             }
             var skinned = modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
             foreach (var smr in skinned)
             {
                if (smr != null && smr.sharedMesh != null) meshCount++;
             }
             int matCount = 0;
             var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
             foreach (var r in renderers)
             {
                if (r != null && r.sharedMaterials != null) matCount += r.sharedMaterials.Length;
             }
             int animCount = 0;
             var clips = AnimationUtility.GetAnimationClips(modelRoot);
             if (clips != null && clips.Length > 0)
                animCount = clips.Length;
             else
             {
                // FBX etc.: count only sub-assets visible in the Project (matches what the user sees)
                Object[] subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
                if (subAssets != null)
                {
                   foreach (var sub in subAssets)
                      if (sub is AnimationClip) animCount++;
                }
             }
             var parts = new List<string>();
             if (meshCount != 0) parts.Add(meshCount + " mesh" + (meshCount == 1 ? "" : "es"));
             if (matCount != 0) parts.Add(matCount + " material" + (matCount == 1 ? "" : "s"));
             if (animCount != 0) parts.Add(animCount + " anim clip" + (animCount == 1 ? "" : "s"));
             if (parts.Count > 0) return string.Join(", ", parts);
          }

          // Animation clip: length
          if (obj is AnimationClip animClip)
          {
             return animClip.length.ToString("F2") + "s";
          }

          // Audio clip: length and mono/stereo
          if (obj is AudioClip audioClip)
          {
             string ch = audioClip.channels == 1 ? "mono" : (audioClip.channels == 2 ? "stereo" : audioClip.channels + " ch");
             return audioClip.length.ToString("F2") + "s " + ch;
          }

          // Mesh: verts and tris
          if (obj is Mesh mesh)
          {
             int tris = mesh.triangles.Length / 3;
             return mesh.vertexCount + " verts, " + tris + " tris";
          }

         // Material: basic shader info and main texture dimensions (if any)
         if (obj is Material mat)
         {
            string shaderName = mat.shader != null ? mat.shader.name : "(no shader)";
            Texture mainTex = mat.mainTexture;
            if (mainTex != null)
            {
               return shaderName + ", " + mainTex.width + "×" + mainTex.height;
            }
            return shaderName;
         }

         // Texture: dimensions and format
          if (obj is Texture2D tex2d)
          {
             string fmt = tex2d.format.ToString();
             return tex2d.width + "×" + tex2d.height + " " + fmt;
          }
          if (obj is Texture tex)
          {
             return tex.width + "×" + tex.height;
          }

          return null;
       }

       //------------------------------------------------------------

       private void RegisterUpdate()
       {
          // adding to the editor app update so that the window gets updates when docked behind another tab
          if (updateRegistered)
          {
             return;
          }

          EditorApplication.update += UpdateInternal;
          updateRegistered = true;
          reloadResources = true;
       }

       //------------------------------------------------------------

       private void OnEnable()
       {
          wantsMouseMove = true;
          RegisterUpdate();
          LoadData();
       }

       //------------------------------------------------------------

       private void OnDisable()
       {
          UnregisterUpdate();
          SaveData();
       }

       //------------------------------------------------------------

       private void OnDestroy()
       {
          SaveData();
       }

       //------------------------------------------------------------

       private void UnregisterUpdate()
       {
          if (!updateRegistered)
          {
             return;
          }

          EditorApplication.update -= UpdateInternal;
          updateRegistered = false;
       }


       private void SaveData()
       {
          PersistedData data = new();
          
          // Save items
          foreach (Item item in selections)
          {
             if (item.targetObject == null) continue;
             
             string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item.targetObject));
             if (string.IsNullOrEmpty(guid)) continue;
             
             data.items.Add(new PersistedItem { guid = guid, favourite = item.favourite });
          }
          
          // Save UI state
          data.filterPrefab = filterPrefab;
          data.filterModel = filterModel;
          data.filterMaterial = filterMaterial;
          data.filterTexture = filterTexture;
          data.filterAudio = filterAudio;
          data.filterSceneInfo = filterSceneInfo;
          data.filterFolder = filterFolder;
          data.searchString = searchString ?? "";
          data.audioPreviewVolume = audioPreviewVolume;
          data.keepHierarchyObjects = keepHierarchyObjects;
          data.showPreviews = showPreviews;
          data.showHelp = showHelp;
          data.helpScroll = helpScroll;
          
          string json = JsonUtility.ToJson(data);
          EditorPrefs.SetString(PrefsKey, json);
       }

       private void LoadData()
       {
          string json = EditorPrefs.GetString(PrefsKey, "");
          if (string.IsNullOrEmpty(json)) return;
          
          PersistedData data = null;
          try
          {
             data = JsonUtility.FromJson<PersistedData>(json);
          }
          catch
          {
          }

          if (data == null) return;
          
          // Restore UI state
          filterPrefab = data.filterPrefab;
          filterModel = data.filterModel;
          filterMaterial = data.filterMaterial;
          filterTexture = data.filterTexture;
          filterAudio = data.filterAudio;
          filterSceneInfo = data.filterSceneInfo;
          filterFolder = data.filterFolder;
          if (data.searchString != null) searchString = data.searchString;
          keepHierarchyObjects = data.keepHierarchyObjects;
          if (json.IndexOf("audioPreviewVolume", System.StringComparison.Ordinal) >= 0)
             audioPreviewVolume = data.audioPreviewVolume;
          if (json.IndexOf("showPreviews", System.StringComparison.Ordinal) >= 0)
             showPreviews = data.showPreviews;
          showHelp = data.showHelp;
          helpScroll = data.helpScroll;

          // If we already have items, they were restored by Unity's internal 
          // serialization (e.g. during a domain reload). Skip the item load.
          if (selections.Count > 0)
          {
             Repaint();
             return;
          }
          
          selections.Clear();
          foreach (var pItem in data.items)
          {
             string path = AssetDatabase.GUIDToAssetPath(pItem.guid);
             if (string.IsNullOrEmpty(path)) continue;
             
             Object obj = AssetDatabase.LoadAssetAtPath<Object>(path);
             if (obj != null)
             {
                selections.Add(new Item(obj, pItem.favourite));
             }
          }
          
          Repaint();
       }
    }
}
