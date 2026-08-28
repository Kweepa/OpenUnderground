// ==============================================================================
//  LeanHull - Size optimized Convex Hull Generator
//  Copyright (c) 2026 Kweepa
//  All rights reserved.
//
//  This script is part of the LeanHull tool suite.
//  Do not distribute or share this code without explicit permission.
// ==============================================================================

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Kweepa.LeanHull
{
    public partial class LeanHullWindow
    {
        private void RefreshSkinDependentAssets()
        {
            bool isDark = EditorGUIUtility.isProSkin;
            if (_iconMerged == null || isDark != _lastProSkin || _groupBgTex == null)
            {
                _lastProSkin = isDark;
                _iconMerged = Resources.Load<Texture2D>(isDark ? "LeanHullIconMergedDark" : "LeanHullIconMerged");
                _iconSeparated = Resources.Load<Texture2D>(isDark ? "LeanHullIconSeparatedDark" : "LeanHullIconSeparated");
                _iconSweep = Resources.Load<Texture2D>(isDark ? "LeanHullIconSweepDark" : "LeanHullIconSweep");
                if (_groupBgTex != null) DestroyImmediate(_groupBgTex);
                _groupBgTex = new Texture2D(1, 1);
                float g = isDark ? 0.3f : 0.8f;
                _groupBgTex.SetPixel(0, 0, new Color(g, g, g, 1.0f));
                _groupBgTex.Apply();
            }
        }

        private Texture2D GetMergeIcon(bool merged)
        {
            RefreshSkinDependentAssets();
            return merged ? _iconMerged : _iconSeparated;
        }

        private Texture2D GetSweepIcon()
        {
            RefreshSkinDependentAssets();
            return _iconSweep;
        }
        

        private void DrawToolbar()
        {
            EditorGUILayout.BeginVertical("box", GUILayout.Height(30));
            EditorGUILayout.BeginHorizontal();

            GUILayout.Space(1);

            DrawApplyButton();

            GUILayout.Space(3);

            DrawGlobalSettings();
            
            GUILayout.Space(3);
            GUILayout.FlexibleSpace();
            
            DrawHelpButton();
            GUILayout.Space(1);

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint) _toolbarHeight = GUILayoutUtility.GetLastRect().height;
        }

        private void DrawBottomBar()
        {
            EditorGUILayout.BeginVertical("box", GUILayout.Height(30));
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();

            GUILayout.Space(1);
            bool hasGeneratedColliders = HasGeneratedColliders();
            EditorGUI.BeginDisabledGroup(!hasGeneratedColliders);
            DrawFindStaleButton(true);
            GUILayout.Space(3);
            DrawCleanupButton(position.width > hideCleanupLabelWidth);
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();

            DrawSizeSlider();
            GUILayout.Space(4);

            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();

            if (Event.current.type == EventType.Repaint) _bottomBarHeight = GUILayoutUtility.GetLastRect().height;
        }

        private void DrawApplyButton()
        {
            EditorGUILayout.BeginVertical(GUIStyle.none);
            GUILayout.FlexibleSpace();

            EditorGUI.BeginDisabledGroup(_isRendering || _previewItems.Count == 0);
            GUIContent applyContent = new GUIContent("Apply", "WARNING: This operation directly modifies prefab assets and cannot be undone via Ctrl+Z.");
            GUIStyle boldBtn = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            if (GUILayout.Button(applyContent, boldBtn, GUILayout.Width(70), GUILayout.Height(20)))
            {
                RunBatchOptimization();
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        private void DrawGlobalSettings()
        {
            GUIStyle arrowStyle = new GUIStyle(EditorStyles.miniButton) 
            { 
                padding = new RectOffset(2, 2, 2, 2),
                margin = new RectOffset(0, 0, 0, 0)
            };
            GUIStyle textButtonStyle = new GUIStyle(GUI.skin.button)
            {
                padding = new RectOffset(6, 24, 2, 2)
            };
            GUIStyle centeredLabel = new GUIStyle(EditorStyles.label) 
            { 
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            GUIStyle vLabelStyle = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(0, 0, 0, 1), // Push text down to match button baseline
                margin = new RectOffset(0, 0, 0, 0)
            };

            EditorGUI.BeginChangeCheck();

            RefreshSkinDependentAssets();

            // Vertex Count Grouping
            EditorGUILayout.BeginVertical(GUIStyle.none);
            GUILayout.FlexibleSpace();

            GUIStyle groupStyle = new GUIStyle(GUIStyle.none)
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(4, 4, 0, 0)
            };
            Rect groupRect = EditorGUILayout.BeginHorizontal(groupStyle, GUILayout.Height(18));
            if (_groupBgTex != null)
                GUI.DrawTexture(groupRect, _groupBgTex);

            if (position.width > hideVertexCountLabelWidth) GUILayout.Label(new GUIContent("Vertex Count", "The global target number of vertices for the optimized convex hulls. Hold Shift to step by 8."), vLabelStyle, GUILayout.Width(80), GUILayout.Height(18));

            // Vertex Count Tweaker
            int oldVerts = _batchTargetVerts;
            int step = Event.current.shift ? 8 : 1;
            if (GUILayout.Button(EditorGUIUtility.IconContent("back"), arrowStyle, GUILayout.Width(18), GUILayout.Height(18)))
            {
                _batchTargetVerts = Mathf.Max(4, _batchTargetVerts - step);
            }
            GUILayout.Label($"{_batchTargetVerts}", centeredLabel, GUILayout.Width(24), GUILayout.Height(18));
            if (GUILayout.Button(EditorGUIUtility.IconContent("forward"), arrowStyle, GUILayout.Width(18), GUILayout.Height(18)))
            {
                _batchTargetVerts = Mathf.Min(128, _batchTargetVerts + step);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();

            bool vertChanged = oldVerts != _batchTargetVerts;

            GUILayout.Space(4);

            // Merge Toggle
            EditorGUILayout.BeginVertical(GUIStyle.none);
            GUILayout.FlexibleSpace();

            bool oldMerge = _mergeMeshes;
            GUIContent mergeContent;
            if (position.width > hideMergeLabelWidth)
            {
                mergeContent = new GUIContent("Merge", _mergeMeshes ? "Merged (Click to separate)" : "Separated (Click to merge)");
                if (GUILayout.Button(mergeContent, textButtonStyle, GUILayout.Width(68), GUILayout.Height(20)))
                {
                    _mergeMeshes = !_mergeMeshes;
                }
                Rect rect = GUILayoutUtility.GetLastRect();
                Texture2D icon = GetMergeIcon(_mergeMeshes);
                GUI.DrawTexture(new Rect(rect.xMax - 20, rect.y + (rect.height - 14) * 0.5f, 14, 14), icon);
            }
            else
            {
                mergeContent = new GUIContent(GetMergeIcon(_mergeMeshes), _mergeMeshes ? "Merged (Click to separate)" : "Separated (Click to merge)");
                if (GUILayout.Button(mergeContent, arrowStyle, GUILayout.Width(18), GUILayout.Height(20)))
                {
                    _mergeMeshes = !_mergeMeshes;
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();

            bool mergeChanged = oldMerge != _mergeMeshes;

            if (EditorGUI.EndChangeCheck())
            {
                SaveSettings();
                UpdateBatchSelection(true, vertChanged, mergeChanged);
            }
        }
        
        private void DrawFindStaleButton(bool showLabels)
        {
            EditorGUILayout.BeginVertical(GUIStyle.none);
            GUILayout.FlexibleSpace();
            GUIStyle arrowStyle = new GUIStyle(EditorStyles.miniButton)
            {
                padding = new RectOffset(2, 2, 2, 2),
                margin = new RectOffset(0, 0, 0, 0)
            };
            GUIStyle textButtonStyle = new GUIStyle(GUI.skin.button)
            {
                padding = new RectOffset(6, 24, 2, 2),
                margin = new RectOffset(0, 0, 0, 0)
            };
            Texture2D refreshIcon = EditorGUIUtility.IconContent("Refresh").image as Texture2D;
            if (refreshIcon == null) refreshIcon = EditorGUIUtility.IconContent("d_Refresh").image as Texture2D;
            GUIContent findStaleContent;
            if (showLabels)
            {
                findStaleContent = new GUIContent("Find stale", "Select prefabs, models or meshes that use generated colliders but whose source geometry has changed (e.g. after reimport). Re-apply from LeanHull to update their colliders.");
                if (GUILayout.Button(findStaleContent, textButtonStyle, GUILayout.Width(85), GUILayout.Height(20)))
                {
                    FindStaleColliders();
                }
                if (refreshIcon != null)
                {
                    Rect rect = GUILayoutUtility.GetLastRect();
                    GUI.DrawTexture(new Rect(rect.xMax - 20, rect.y + (rect.height - 14) * 0.5f, 14, 14), refreshIcon);
                }
            }
            else
            {
                findStaleContent = new GUIContent(refreshIcon, "Select assets using generated colliders whose geometry changed");
                if (GUILayout.Button(findStaleContent, arrowStyle, GUILayout.Width(18), GUILayout.Height(20)))
                {
                    FindStaleColliders();
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        private void DrawCleanupButton(bool showLabels)
        {
            EditorGUILayout.BeginVertical(GUIStyle.none);
            GUILayout.FlexibleSpace();
            GUIStyle arrowStyle = new GUIStyle(EditorStyles.miniButton)
            {
                padding = new RectOffset(2, 2, 2, 2),
                margin = new RectOffset(0, 0, 0, 0)
            };
            GUIStyle textButtonStyle = new GUIStyle(GUI.skin.button)
            {
                padding = new RectOffset(6, 24, 2, 2),
                margin = new RectOffset(0, 0, 0, 0)
            };

            GUIContent cleanupContent;
            if (showLabels)
            {
                cleanupContent = new GUIContent("Cleanup", "Delete orphaned collision assets that no longer have a matching source model.");
                if (GUILayout.Button(cleanupContent, textButtonStyle, GUILayout.Width(80), GUILayout.Height(20)))
                {
                    CleanupOrphanedColliders();
                }
                Rect rect = GUILayoutUtility.GetLastRect();
                Texture2D icon = GetSweepIcon();
                GUI.DrawTexture(new Rect(rect.xMax - 20, rect.y + (rect.height - 14) * 0.5f, 14, 14), icon);
            }
            else
            {
                cleanupContent = new GUIContent(GetSweepIcon(), "Delete orphaned collision");
                if (GUILayout.Button(cleanupContent, arrowStyle, GUILayout.Width(18), GUILayout.Height(20)))
                {
                    CleanupOrphanedColliders();
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        private void DrawPreviewArea(int rotatorID, int dividerID, Rect activeDragArea)
        {
            GUILayout.Space(1);
            _hoveredItem = null;

            Rect previewArea = EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));

            bool singlePreviewMode = _hierarchyItem != null && _previewItems.Count > 0;

            if (singlePreviewMode)
            {
                // One preview only, square, fitted to remaining area; leave room for border and name under preview
                const float singlePreviewBorder = 8f;
                float labelHeight = EditorGUIUtility.singleLineHeight;
                float availableWidth = activeDragArea.width - singlePreviewBorder;
                float availableHeight = activeDragArea.height - singlePreviewBorder - labelHeight;
                float fitSize = Mathf.Min(availableWidth, availableHeight);
                _singlePreviewSizePixels = Mathf.RoundToInt(fitSize * EditorGUIUtility.pixelsPerPoint);
                _singlePreviewSizePixels = Mathf.Max(_singlePreviewSizePixels, Mathf.RoundToInt(LeanHullConstants.minTileSize));
                if (_singlePreviewSizePixels != _lastSinglePreviewSizePixels)
                {
                    _lastSinglePreviewSizePixels = _singlePreviewSizePixels;
                    if (_hierarchyItem.tex != null) { DestroyImmediate(_hierarchyItem.tex); _hierarchyItem.tex = null; }
                    _hierarchyItem.previewTextureDirty = true;
                }
                float fitLogical = _singlePreviewSizePixels / EditorGUIUtility.pixelsPerPoint;
                _gridScroll = Vector2.zero;
                _lastScrollViewRect = new Rect(0, 0, activeDragArea.width, activeDragArea.height);

                GUILayout.FlexibleSpace();
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(4);
                DrawTile(_hierarchyItem, fitLogical, activeDragArea, dividerID);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();

                _singlePreviewSizePixels = 0;
            }
            else
            {
                // Zoom handling
                Event ev = Event.current;
                if (ev.type == EventType.ScrollWheel && (ev.control || ev.command) && activeDragArea.Contains(ev.mousePosition))
                {
                    float oldSize = _previewSize;
                    float zoomSensitivity = _previewSize * 0.08f;
                    _previewSize = Mathf.Clamp(_previewSize - ev.delta.y * zoomSensitivity, LeanHullConstants.minTileSize, LeanHullConstants.maxTileSize);

                    if (!Mathf.Approximately(oldSize, _previewSize))
                    {
                        InvalidateAllPreviewTextures();
                        SaveSettings();
                        Repaint();
                    }
                    ev.Use();
                }

                if (_previewItems.Count > 0)
                {
                    _gridScroll = EditorGUILayout.BeginScrollView(_gridScroll, GUIStyle.none, GUI.skin.verticalScrollbar);

                    Rect visibleRect = new Rect(_gridScroll.x, _gridScroll.y, activeDragArea.width, activeDragArea.height);
                    DrawPreviewGrid(visibleRect, dividerID);

                    EditorGUILayout.EndScrollView();

                    if (Event.current.type == EventType.Repaint)
                    {
                        _lastScrollViewRect = GUILayoutUtility.GetLastRect();
                    }
                }
                else
                {
                    GUILayout.FlexibleSpace();
                    GUIStyle centeredLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter };
                    EditorGUILayout.LabelField("Select objects in the Project view to begin.", centeredLabel);
                    if (generatedCollidersPath == LeanHullProjectSettingsData.DefaultGeneratedAssetsFolder)
                    {
                        EditorGUILayout.LabelField("You can change where generated assets are saved in Edit → Project Settings → LeanHull.", centeredLabel);
                    }
                    GUILayout.FlexibleSpace();
                }
            }

            if (_hoveredItem != _lastHoveredItem)
            {
                _lastHoveredItem = _hoveredItem;
                Repaint();
            }

            EditorGUILayout.EndVertical();
        }

        private void OnGUI()
        {
            int rotatorID = GUIUtility.GetControlID("PreviewRotator".GetHashCode(), FocusType.Passive);
            int dividerID = GUIUtility.GetControlID("SplitDivider".GetHashCode(), FocusType.Passive);

            // Safety sync: if we lost hotControl for any reason, we are no longer interacting
            if (GUIUtility.hotControl != rotatorID) _isRotating = false;
            if (GUIUtility.hotControl != dividerID)
            {
                _isDraggingSplit = false;
                _draggedItem = null;
            }

            // Only repaint on actual changes (rotation, splitter, settings, resize) â€” not on every mouse move
            // to avoid unnecessary GPU work when just moving the cursor over tiles.
            
            // 1. Draw Top Toolbar
            DrawToolbar();
            float topBarBottom = _toolbarHeight + 5;

            // 2. Draw Bottom Bar
            float bottomBarHeight = _bottomBarHeight > 0 ? _bottomBarHeight : 30;
            Rect bottomRect = new Rect(0, position.height - bottomBarHeight, position.width, bottomBarHeight);
            GUILayout.BeginArea(bottomRect);
            DrawBottomBar();
            GUILayout.EndArea();

            // 3. Middle Area (Preview or Help; optional hierarchy panel on the left)
            float middleHeight = position.height - topBarBottom - bottomBarHeight - 5;
            Rect middleRect = new Rect(0, topBarBottom, position.width, middleHeight);
            bool showHierarchy = _hierarchyItem != null && !_showHelp && !_isOptimizing;
            float previewAreaX = showHierarchy ? hierarchyPanelWidth : 0f;
            float previewAreaWidth = showHierarchy ? middleRect.width - hierarchyPanelWidth : middleRect.width;

            // The activeDragArea for mouse checks inside the BeginArea is local (0, 0, width, height)
            Rect localDragArea = new Rect(0, 0, previewAreaWidth, middleRect.height);

            GUILayout.BeginArea(middleRect);
            if (_showHelp)
            {
                _helpScroll = EditorGUILayout.BeginScrollView(_helpScroll, GUIStyle.none, GUI.skin.verticalScrollbar);
                DrawHelpPage();
                EditorGUILayout.EndScrollView();
            }
            else if (!_isOptimizing)
            {
                // Always use the same Begin/End structure so GUILayout state stays valid when hierarchy is shown or hidden
                GUILayout.BeginArea(new Rect(0, 0, showHierarchy ? hierarchyPanelWidth : 0, middleRect.height));
                if (showHierarchy)
                    DrawHierarchyPanel();
                GUILayout.EndArea();
                GUILayout.BeginArea(new Rect(previewAreaX, 0, previewAreaWidth, middleRect.height));
                DrawPreviewArea(rotatorID, dividerID, localDragArea);
                GUILayout.EndArea();
            }
            GUILayout.EndArea();

            // Handle rotation input AFTER everything so UI buttons get first priority.
            // Only respond to drags in the preview area (not in the hierarchy panel when it's open).
            Rect rotationDragRect = showHierarchy ? new Rect(previewAreaX, topBarBottom, previewAreaWidth, middleHeight) : middleRect;
            HandleRotationInput(rotationDragRect, rotatorID);
        }

        private void DrawHelpPage()
        {
            EditorGUILayout.BeginVertical("box");
            
            GUIStyle helpStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                fontSize = 12,
                richText = true,
                padding = new RectOffset(25, 25, 18, 18),
                alignment = TextAnchor.UpperLeft
            };

            helpStyle.normal.textColor = helpStyle.hover.textColor = helpStyle.active.textColor = helpStyle.focused.textColor = GUI.skin.label.normal.textColor;

            string helpText = "<b>LeanHull</b> is a low poly convex hull generator.\n\n" +
                              "It creates ultra-low poly collision meshes from high poly visual models.\n\n" +
                              "<b>Workflow:</b>\n" +
                              "\u2022 <b>Set output folder (optional):</b> In <b>Edit \u2192 Project Settings \u2192 LeanHull</b>, set where generated hull .asset files are saved. The default is <i>Assets/Game/LeanHull</i>. Change it first if you want assets elsewhere so they don't appear in an unexpected location.\n" +
                              "\u2022 <b>Select Assets:</b> Choose prefabs, imported models, and raw meshes in the Project view.\n" +
                              "\u2022 <b>Global Settings:</b> Adjust the <b>Vertex Count</b> (target count per hull) and <b>Merge</b> (combine sub-meshes) in the top toolbar. Hold <b>Shift</b> while clicking arrows to step by 8 vertices instead of 1.\n" +
                              "\u2022 <b>Individual Overrides:</b> Use the \u25C0 \u25B6 arrows on any tile to tweak its specific vertex count (also supports <b>Shift-click</b>). Use the <b>Merge</b>/<b>Separate</b> icon to toggle merging for that item only.\n" +
                              "\u2022 <b>Which meshes get colliders:</b> Click the <b>Pencil icon</b> on a prefab tile to open the <b>Hull contributors</b> panel. Click the icon next to a GameObject or mesh to <b>exclude</b> it from hull generation (or click again to include it). Excluding a GameObject excludes it and all its children. Your choices are saved per-prefab and survive reimport.\n" +
                              "\u2022 <b>Clearing Overrides:</b> When an item has custom settings, a <b>Padlock icon</b> appears on its tile. Click the padlock to reset that object to the global defaults.\n" +
                              "\u2022 <b>Preview:</b> Rotate thumbnails with the left mouse button. Move the vertical divider to compare the original hull (right) with the optimized result (left). Hold <b>Ctrl + Scroll</b> to quickly resize the preview grid.\n" +
                              "\u2022 <b>Apply:</b> Click <b>Apply</b> to generate and attach the colliders. This process is automatic and handles object renames. <b>Note:</b> Undo is not supported for asset modification; please ensure you have a backup or commit your changes to source control before applying.\n\n" +
                              "<b>Asset Storage:</b>\n" +
                              $"\u2022 <b>External Assets:</b> All generated hulls are saved as .asset files in the <i>{generatedCollidersPath}</i> folder. Change this in <b>Edit \u2192 Project Settings \u2192 LeanHull</b>.\n" +
                              "\u2022 <b>Why not subassets?</b> LeanHull uses external assets to ensure stable visualization in the Unity Editor. This prevents the common Unity bug where collision wireframes disappear during prefab autosaves.\n" +
                              "\u2022 <b>Find Stale:</b> Click <b>Find stale</b> in the bottom bar to select prefabs, models or meshes that use generated colliders but whose source geometry has changed (e.g. after reimport). They appear in the grid with their previous settings restored; click <b>Apply</b> to regenerate the colliders.\n" +
                              "\u2022 <b>Manual Cleanup:</b> Click the <b>Sweep button</b> on the toolbar to automatically find and delete orphaned colliders. Unreferenced prefab colliders are scrubbed, while model colliders are only removed if both unreferenced and the source file is missing.\n\n" +
                              "<i>Click anywhere in this box to return to the preview grid.</i>";

            if (GUILayout.Button(helpText, helpStyle))
            {
                _showHelp = false;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawHierarchyPanel()
        {
            if (_hierarchyItem == null) return;
            PreviewItem item = _hierarchyItem;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(hierarchyPanelWidth - 4), GUILayout.ExpandHeight(true));

            // Title and close button
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Hull contributors", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("\u00D7", "Close"), GUILayout.Width(18), GUILayout.Height(18)))
            {
                if (item.tex != null) { DestroyImmediate(item.tex); item.tex = null; }
                item.previewTextureDirty = true;
                _lastSinglePreviewSizePixels = 0;
                _hierarchyItem = null;
                item.highlightedHullIndex = -1;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                Repaint();
                return;
            }
            EditorGUILayout.EndHorizontal();

            if (item.hullContributors.Count == 0)
            {
                EditorGUILayout.HelpBox("No contributor data yet. Hulls may still be generating.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _hierarchyScroll = EditorGUILayout.BeginScrollView(_hierarchyScroll, GUILayout.ExpandHeight(true));
            const float indentPerLevel = 12f;
            const float rowHeight = 18f;   // compact but fits label font
            const float iconSize = 16f;
            const int iconButtonPadding = 1;
            float iconButtonSize = iconSize + iconButtonPadding * 2f;
            Texture2D goIcon = EditorGUIUtility.IconContent("GameObject Icon")?.image as Texture2D ?? EditorGUIUtility.IconContent("d_GameObject Icon")?.image as Texture2D;
            Texture2D meshIcon = EditorGUIUtility.IconContent("Mesh Icon")?.image as Texture2D ?? EditorGUIUtility.IconContent("d_Mesh Icon")?.image as Texture2D;
            GUIStyle rowLabelStyle = new GUIStyle(EditorStyles.label) { wordWrap = false, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
            HashSet<string> excludeSet = LeanHullPrefabSettings.GetExcludeIdsForPrefab(item.prefabPath);
            List<bool> excludedOnPath = new List<bool>(); // excludedOnPath[d] = true if path from root to depth d has an excluded GO

            for (int i = 0; i < item.hullContributors.Count; i++)
            {
                HullContributorEntry entry = item.hullContributors[i];
                bool entryExcluded = excludeSet.Contains(entry.excludeId);
                while (excludedOnPath.Count > entry.depth)
                    excludedOnPath.RemoveAt(excludedOnPath.Count - 1);
                bool underExcludedGo = entry.depth > 0 && excludedOnPath[entry.depth - 1];
                bool thisBranchExcluded = (entry.isGameObject && entryExcluded) || (entry.depth > 0 && excludedOnPath[entry.depth - 1]);
                if (excludedOnPath.Count <= entry.depth)
                    excludedOnPath.Add(thisBranchExcluded);
                else
                    excludedOnPath[entry.depth] = thisBranchExcluded;
                EditorGUILayout.BeginHorizontal(GUILayout.Height(rowHeight), GUILayout.ExpandWidth(true));

                GUILayout.Space(entry.depth * indentPerLevel);

                // GameObject or mesh icon as mini button; click toggles include/exclude; extra pixel padding so border is clear
                string toggleTooltip = entry.isGameObject
                    ? "Exclude this GameObject and all children from hull (saved in PrefabSettings sidecar). Click to toggle."
                    : "Exclude this mesh from hull (saved in PrefabSettings sidecar). Click to toggle.";
                Texture2D rowIcon = entry.isGameObject ? goIcon : meshIcon;
                GUIContent iconContent = rowIcon != null ? new GUIContent(rowIcon, toggleTooltip) : new GUIContent("", toggleTooltip);
                GUIStyle miniIconStyle = new GUIStyle(EditorStyles.miniButton) { padding = new RectOffset(iconButtonPadding, iconButtonPadding, iconButtonPadding, iconButtonPadding), margin = new RectOffset(0, 0, 0, 0), fixedWidth = iconButtonSize, fixedHeight = iconButtonSize };
                if (GUI.Button(GUILayoutUtility.GetRect(iconButtonSize, iconButtonSize, GUILayout.Width(iconButtonSize), GUILayout.Height(iconButtonSize)), iconContent, miniIconStyle))
                {
                    HashSet<string> set = LeanHullPrefabSettings.GetExcludeIdsForPrefab(item.prefabPath);
                    if (entryExcluded) set.Remove(entry.excludeId);
                    else set.Add(entry.excludeId);
                    LeanHullPrefabSettings.SetExcludeIdsForPrefab(item.prefabPath, set);
                    item.isDirty = true;
                    // Keep hullContributors so the panel doesn't show "No contributor data" â€” exclude state is read from GetExcludeIdsForPrefab each frame
                    _isRendering = true;
                    _renderIndex = 0;
                }

                bool isHighlight = entry.hullIndex >= 0 && item.highlightedHullIndex == entry.hullIndex;
                bool labelGreyed = entryExcluded || underExcludedGo;
                string tooltip = entry.isGameObject ? "GameObject (click icon to exclude/include)" : (entry.hullIndex >= 0 ? "Click to highlight this hull in yellow" : "Excluded from hull");
                GUIContent labelContent = new GUIContent(entry.displayName, tooltip);
                Color labelColor = isHighlight ? Color.yellow : (labelGreyed ? new Color(0.5f, 0.5f, 0.5f) : EditorStyles.label.normal.textColor);
                rowLabelStyle.normal.textColor = rowLabelStyle.hover.textColor = rowLabelStyle.active.textColor = rowLabelStyle.focused.textColor = labelColor;
                Rect rowRect = GUILayoutUtility.GetRect(labelContent, rowLabelStyle, GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight));
                if (GUI.Button(rowRect, labelContent, rowLabelStyle))
                {
                    if (entry.hullIndex >= 0)
                    {
                        item.highlightedHullIndex = item.highlightedHullIndex == entry.hullIndex ? -1 : entry.hullIndex;
                        item.previewTextureDirty = true;
                        if (item.tex != null) { DestroyImmediate(item.tex); item.tex = null; }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawPreviewGrid(Rect activeDragArea, int dividerID)
        {
            float windowWidth = EditorGUIUtility.currentViewWidth - 25f; // Restore scrollbar tolerance
            float logicalSize = _previewSize / EditorGUIUtility.pixelsPerPoint;
            
            // Dynamic spacing: 5px at smallest size (64), up to 20px at largest size (512)
            float tSize = Mathf.InverseLerp(64f, 512f, _previewSize);
            float logicalSpacing = Mathf.Lerp(5f, 20f, tSize);
            
            // Calculate how many items can possibly fit in one row, accounting for spacing
            int itemsPerRow = Mathf.FloorToInt(windowWidth / (logicalSize + logicalSpacing));
            if (itemsPerRow < 1) itemsPerRow = 1;

            GUILayout.Space(5);

            bool isSingleRow = _previewItems.Count <= itemsPerRow;

            for (int i = 0; i < _previewItems.Count; i += itemsPerRow)
            {
                GUILayout.BeginHorizontal();
                
                if (!isSingleRow) 
                {
                    GUILayout.FlexibleSpace(); // Centered for multi-row
                }
                else 
                {
                    GUILayout.Space(logicalSpacing); // Fixed left margin for single row
                }
                
                int endJ = isSingleRow ? _previewItems.Count : itemsPerRow;

                for (int j = 0; j < endJ; ++j)
                {
                    int index = i + j;
                    if (index < _previewItems.Count)
                    {
                        DrawTile(_previewItems[index], logicalSize, activeDragArea, dividerID);
                    }
                    else
                    {
                        // Add an invisible dummy block so the last row's columns align with the rows above it
                        GUILayout.Space(logicalSize);
                    }

                    // Add spacing between items
                    if (j < endJ - 1)
                    {
                        GUILayout.Space(logicalSpacing);
                        if (!isSingleRow) GUILayout.FlexibleSpace();
                    }
                }
                
                GUILayout.FlexibleSpace(); // Flexible right padding (Centering or Left-Justifying)
                GUILayout.EndHorizontal();
                GUILayout.Space(10); // Row spacing
            }
            GUILayout.Space(5);
        }

        private void DrawTile(PreviewItem item, float logicalSize, Rect activeDragArea, int dividerID)
        {
            Texture2D tex = item.tex;
            
            GUILayout.BeginVertical(GUILayout.Width(logicalSize));
            
            Rect texRect = GUILayoutUtility.GetRect(logicalSize, logicalSize, GUILayout.ExpandWidth(false));
            
            if (Event.current.type == EventType.Repaint)
            {
                float vh = _lastScrollViewRect.height > 0 ? _lastScrollViewRect.height : position.height;
                Rect viewRect = new Rect(_gridScroll.x, _gridScroll.y, activeDragArea.width, vh);
                
                if (viewRect.Overlaps(texRect) && item.previewTextureDirty && !item.isDirty)
                {
                    item.tex = RenderItem(item);
                    tex = item.tex;
                    item.previewTextureDirty = false;
                }
            }

            if (tex != null)
            {
                GUI.DrawTexture(texRect, tex);
                DrawTileOverlay(item, texRect, logicalSize, dividerID, activeDragArea);
            }

            bool mouseOverTile = texRect.Contains(Event.current.mousePosition) && activeDragArea.Contains(Event.current.mousePosition);
            if (mouseOverTile)
                _hoveredItem = item;

            // Handle split dragging
            if (mouseOverTile)
            {
                float dividerX = texRect.x + texRect.width * item.splitPosition;
                Rect dividerGrabRect = new Rect(dividerX - 10, texRect.y, 20, texRect.height);
                
                // Only show resize cursor and allow drag if we're not over the top bar
                float barHeight = 16;
                Rect topBarRect = new Rect(texRect.x, texRect.y, texRect.width, barHeight);
                bool overTopBar = topBarRect.Contains(Event.current.mousePosition);

                if (!overTopBar)
                {
                    EditorGUIUtility.AddCursorRect(dividerGrabRect, MouseCursor.ResizeHorizontal);

                    if (Event.current.type == EventType.MouseDown && dividerGrabRect.Contains(Event.current.mousePosition) && GUIUtility.hotControl == 0)
                    {
                        GUIUtility.hotControl = dividerID;
                        _isDraggingSplit = true;
                        _draggedItem = item;
                        Event.current.Use();
                    }
                }
            }

            if (_isDraggingSplit && _draggedItem == item && GUIUtility.hotControl == dividerID)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    float mouseX = Event.current.mousePosition.x - texRect.x;
                    float newSplit = Mathf.Clamp(mouseX / texRect.width, 0.05f, 0.95f);
                    if (!Mathf.Approximately(_splitPosition, newSplit))
                    {
                        _splitPosition = newSplit;
                        foreach (var pItem in _previewItems)
                            pItem.splitPosition = _splitPosition;
                        InvalidateAllPreviewTextures();
                    }
                    Event.current.Use();
                    Repaint();
                }
                else if (Event.current.rawType == EventType.MouseUp)
                {
                    GUIUtility.hotControl = 0;
                    _isDraggingSplit = false;
                    _draggedItem = null;
                    SaveSettings();
                    Event.current.Use();
                }
            }
            
            Rect rowRect = GUILayoutUtility.GetRect(logicalSize, EditorGUIUtility.singleLineHeight);
            DrawTileLabel(item, rowRect);
            
            GUILayout.EndVertical();
        }

        private void DrawTileOverlay(PreviewItem item, Rect texRect, float logicalSize, int dividerID, Rect activeDragArea)
        {
            float barHeight = 16;
            Rect overlayRect = new Rect(texRect.x + 2, texRect.y + 2, texRect.width - 4, barHeight);
            
            EditorGUI.DrawRect(overlayRect, new Color(0, 0, 0, 0.25f));
            
            GUIStyle overlayStyle = new GUIStyle(EditorStyles.miniLabel)
            { 
                alignment = TextAnchor.MiddleLeft, 
                padding = new RectOffset(4, 0, 0, 0),
                normal = { textColor = Color.white },
                active = { textColor = Color.white },
                focused = { textColor = Color.white },
                hover = { textColor = Color.white },
                fontSize = 9,
                fontStyle = FontStyle.Normal
            };

            GUIStyle arrowStyle = new GUIStyle(EditorStyles.miniButton) 
            { 
                padding = new RectOffset(2, 2, 2, 2),
                margin = new RectOffset(0, 0, 0, 0)
            };

            bool isHovering = texRect.Contains(Event.current.mousePosition) && activeDragArea.Contains(Event.current.mousePosition);

            if (item.optimizedVerts > 0)
            {
                if (!_isRotating && isHovering && GUIUtility.hotControl != dividerID)
                {
                    DrawTileTweakers(item, overlayRect, overlayStyle, arrowStyle, item.optimizedVerts);
                }
                else
                {
                    GUI.Label(overlayRect, $"{item.optimizedVerts}/{item.currentVerts}", overlayStyle);
                }

                if (!_isRotating && GUIUtility.hotControl != dividerID)
                {
                    DrawTileRightButtons(item, overlayRect, isHovering, arrowStyle);
                }
            }
            else
            {
                GUI.Label(overlayRect, "...", overlayStyle);
                if (!_isRotating && isHovering)
                {
                    DrawDeselectButton(item, overlayRect, arrowStyle, false);
                }
            }

            float bottomBarHeight = 14f;
            bool showBottomBar = !string.IsNullOrEmpty(item.staleChangeDescription) ||
                (item.prefabPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase) && item.hullContributors.Count > 0);
            if (showBottomBar)
            {
                Rect bottomRect = new Rect(texRect.x + 2, texRect.yMax - 2 - bottomBarHeight, texRect.width - 4, bottomBarHeight);
                EditorGUI.DrawRect(bottomRect, new Color(0, 0, 0, 0.25f));
                int meshContributorCount = 0;
                if (item.prefabPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase) && item.hullContributors.Count > 0)
                {
                    for (int m = 0; m < item.hullContributors.Count; m++)
                        if (!item.hullContributors[m].isGameObject) meshContributorCount++;
                }
                bool isPrefabWithHierarchy = item.prefabPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase) && meshContributorCount > 1;
                float leftX = bottomRect.x + 2f;
                if (isPrefabWithHierarchy)
                {
                    GUIContent pencilContent = EditorGUIUtility.IconContent("editicon.sml") ?? EditorGUIUtility.IconContent("d_editicon.sml");
                    if (pencilContent?.image == null) pencilContent = new GUIContent("\u270E", "Hull hierarchy");
                    else pencilContent.tooltip = "Edit hull hierarchy";
                    Rect pencilRect = new Rect(leftX, bottomRect.y, 18, bottomBarHeight);
                    if (GUI.Button(pencilRect, pencilContent, EditorStyles.iconButton))
                    {
                        _hierarchyItem = item;
                        if (item.tex != null) { DestroyImmediate(item.tex); item.tex = null; }
                        item.previewTextureDirty = true;
                        Repaint();
                    }
                    leftX = pencilRect.xMax + 2f;
                }
                if (!string.IsNullOrEmpty(item.staleChangeDescription))
                {
                    GUIStyle bottomStyle = new GUIStyle(overlayStyle) { alignment = TextAnchor.MiddleCenter };
                    Rect labelRect = new Rect(leftX, bottomRect.y, bottomRect.width - (leftX - bottomRect.x), bottomRect.height);
                    GUI.Label(labelRect, item.staleChangeDescription, bottomStyle);
                }
            }
        }

        private void DrawTileTweakers(PreviewItem item, Rect overlayRect, GUIStyle overlayStyle, GUIStyle arrowStyle, int vertCount)
        {
            float btnSize = 14f;
            float fixedLabelWidth = 22f;
            int step = Event.current.shift ? 8 : 1;

            // Left Arrow
            Rect leftArrow = new Rect(overlayRect.x + 2, overlayRect.y, btnSize, btnSize);
            if (GUI.Button(leftArrow, EditorGUIUtility.IconContent("back"), arrowStyle))
            {
                int currentTarget = item.targetVertOverride > 0 ? item.targetVertOverride : _batchTargetVerts;
                item.targetVertOverride = Mathf.Max(4, currentTarget - step);
                item.isDirty = true;
                _isRendering = true;
                _renderIndex = 0;
                Repaint();
            }

            // Number
            GUIStyle centeredLabel = new GUIStyle(overlayStyle) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0) };
            Rect countRect = new Rect(leftArrow.xMax, overlayRect.y, fixedLabelWidth, overlayRect.height);
            GUI.Label(countRect, $"{vertCount}", centeredLabel);

            // Right Arrow
            Rect rightArrow = new Rect(countRect.xMax, overlayRect.y, btnSize, btnSize);
            if (GUI.Button(rightArrow, EditorGUIUtility.IconContent("forward"), arrowStyle))
            {
                int currentTarget = item.targetVertOverride > 0 ? item.targetVertOverride : _batchTargetVerts;
                item.targetVertOverride = Mathf.Min(128, currentTarget + step);
                item.isDirty = true;
                _isRendering = true;
                _renderIndex = 0;
                Repaint();
            }

            // Merge Button
            bool isMerged = GetEffectiveMerge(item, _mergeMeshes);
            if (item.validMeshCount > 1)
            {
                Rect mergeRect = new Rect(rightArrow.xMax + 2, overlayRect.y, btnSize, btnSize);
                if (GUI.Button(mergeRect, new GUIContent(GetMergeIcon(isMerged), isMerged ? "Merged (Click to separate)" : "Separated (Click to merge)"), arrowStyle))
                {
                    item.mergeOverride = isMerged ? MergeOverride.Separated : MergeOverride.Merged;
                    item.isDirty = true;
                    _isRendering = true;
                    _renderIndex = 0;
                    Repaint();
                }
            }
        }

        private void DrawTileRightButtons(PreviewItem item, Rect overlayRect, bool isHovering, GUIStyle arrowStyle)
        {
            bool hasOverride = item.targetVertOverride > 0 || item.mergeOverride != MergeOverride.None;
            float btnSize = 14f;

            if (hasOverride)
            {
                // Padlock at far right
                Rect lockRect = new Rect(overlayRect.xMax - btnSize - 2, overlayRect.y, btnSize, btnSize);
                if (GUI.Button(lockRect, EditorGUIUtility.IconContent("InspectorLock"), arrowStyle))
                {
                    item.targetVertOverride = 0;
                    item.mergeOverride = MergeOverride.None;
                    item.isDirty = true;
                    _isRendering = true;
                    _renderIndex = 0;
                    Repaint();
                }

                // X to the left of padlock (on hover)
                if (isHovering)
                {
                    DrawDeselectButton(item, lockRect, arrowStyle, true);
                }
            }
            else if (isHovering)
            {
                // No override, just show X at far right on hover
                DrawDeselectButton(item, overlayRect, arrowStyle, false);
            }
        }

        private void DrawDeselectButton(PreviewItem item, Rect anchorRect, GUIStyle arrowStyle, bool shiftLeft)
        {
            float btnSize = 14f;
            float xPos = shiftLeft ? anchorRect.x - btnSize - 2 : anchorRect.xMax - btnSize - 2;
            Rect closeRect = new Rect(xPos, anchorRect.y, btnSize, btnSize);
            
            GUIContent closeContent = EditorGUIUtility.IconContent("Toolbar Minus");
            closeContent.tooltip = "Deselect";
            
            if (GUI.Button(closeRect, closeContent, arrowStyle))
            {
                var list = new List<Object>(Selection.objects);
                list.RemoveAll(o => o != null && o.name == item.name && AssetDatabase.GetAssetPath(o) == item.prefabPath);
                Selection.objects = list.ToArray();
            }
        }

        private void DrawTileLabel(PreviewItem item, Rect rowRect)
        {
            float iconSize = 14f;
            float spacing = -1f;
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel) 
            { 
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                wordWrap = false
            };
            
            GUIContent textContent = new GUIContent(item.name);
            Vector2 textSize = labelStyle.CalcSize(textContent);
            float totalWidth = iconSize + spacing + textSize.x;
            
            float startX = rowRect.x + Mathf.Max(0, (rowRect.width - totalWidth) * 0.5f);
            Rect iconRect = new Rect(startX, rowRect.y + (rowRect.height - iconSize) * 0.5f, iconSize, iconSize);
            Rect textRect = new Rect(startX + iconSize + spacing, rowRect.y, rowRect.width - (startX - rowRect.x) - iconSize - spacing, rowRect.height);

            if (item.icon != null)
            {
                GUI.DrawTexture(iconRect, item.icon, ScaleMode.ScaleToFit);
            }
            GUI.Label(textRect, textContent, labelStyle);
        }

        private void HandleRotationInput(Rect activeDragArea, int controlID)
        {
            if (_isDraggingSplit) return;

            // Rotation handle - after UI items so it doesn't block buttons
            Event ev = Event.current;
            EventType type = ev.type;
            
            // If we are the hotControl, we should also look at rawType because type can be 'Ignore' when outside the window
            if (GUIUtility.hotControl == controlID && type == EventType.Ignore)
            {
                type = ev.rawType;
            }

            if (type == EventType.MouseDown && activeDragArea.Contains(ev.mousePosition) && GUIUtility.hotControl == 0)
            {
                GUIUtility.hotControl = controlID;
                _mouseDownPos = ev.mousePosition;
                ev.Use();
            }
            else if (type == EventType.MouseUp && GUIUtility.hotControl == controlID)
            {
                GUIUtility.hotControl = 0;
                _isRotating = false;
                ev.Use();
                Repaint();
            }
            else if (type == EventType.MouseDrag && GUIUtility.hotControl == controlID)
            {
                // Must ALWAYS use the event if we are the hotControl, 
                // otherwise the ScrollView will steal it for scrolling.
                ev.Use();

                if (!_isRotating && Vector2.Distance(_mouseDownPos, ev.mousePosition) > 5f)
                {
                    _isRotating = true;
                }

                if (_isRotating)
                {
                    _previewDir.x += ev.delta.x;
                    _previewDir.y += ev.delta.y;
                    _previewDir.y = Mathf.Clamp(_previewDir.y, -89f, 89f);
                    InvalidateAllPreviewTextures();
                    Repaint();
                }
            }
        }

        private void DrawSizeSlider()
        {
            EditorGUI.BeginChangeCheck();
            
            // Remove focus on mouse up
            if (Event.current.type == EventType.MouseUp) GUIUtility.keyboardControl = 0;
            
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();

            Rect sliderRect = GUILayoutUtility.GetRect(80, 20, GUILayout.Width(80));
            _previewSize = GUI.HorizontalSlider(sliderRect, _previewSize, LeanHullConstants.minTileSize, LeanHullConstants.maxTileSize, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            EditorGUI.LabelField(sliderRect, new GUIContent("", "Preview Zoom/Size"));
            
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            if (EditorGUI.EndChangeCheck())
            {
                SaveSettings();
                InvalidateAllPreviewTextures();
                Repaint();
            }
        }

        private void DrawHelpButton()
        {
            EditorGUILayout.BeginVertical(GUIStyle.none);
            GUILayout.FlexibleSpace();

            // Help Toggle - now on the right of the slider
            GUIStyle helpBtnStyle = new GUIStyle(EditorStyles.miniButton) 
            { 
                padding = new RectOffset(2, 2, 2, 2),
                margin = new RectOffset(0, 0, 0, 0)
            };
            if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("_Help").image, _showHelp ? "Hide help" : "Show help"), helpBtnStyle, GUILayout.Width(20), GUILayout.Height(20)))
            {
                _showHelp = !_showHelp;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        private void SaveSettings()
        {
            PersistedData data = new PersistedData();
            data.batchTargetVerts = _batchTargetVerts;
            data.mergeMeshes = _mergeMeshes;
            data.previewSize = _previewSize;

            string json = JsonUtility.ToJson(data);
            EditorPrefs.SetString(prefsKey, json);
        }

        private void LoadSettings()
        {
            string json = EditorPrefs.GetString(prefsKey, "");
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

            _batchTargetVerts = data.batchTargetVerts;
            _mergeMeshes = data.mergeMeshes;
            _previewSize = data.previewSize;
        }
    }
}
