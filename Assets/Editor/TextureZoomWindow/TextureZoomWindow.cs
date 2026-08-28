using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Static storage for the last clicked texture in Inspector
public static class TextureZoomTracker
{
    public static Texture2D lastClickedTexture;
    public static string lastClickedPropertyPath;
}

// Custom PropertyDrawer that wraps the default behavior and tracks clicks
[CustomPropertyDrawer(typeof(Texture2D), true)]
public class Texture2DTrackerDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // Use Unity's default property field drawing
        EditorGUI.PropertyField(position, property, label, true);
        
        // Track when user clicks/interacts with this field
        if (Event.current.type == EventType.MouseDown && position.Contains(Event.current.mousePosition))
        {
            Texture2D tex = property.objectReferenceValue as Texture2D;
            if (tex != null)
            {
                TextureZoomTracker.lastClickedTexture = tex;
                TextureZoomTracker.lastClickedPropertyPath = property.propertyPath;
            }
        }
        // Also track when the value changes (e.g., via object picker)
        else if (Event.current.type == EventType.ExecuteCommand && Event.current.commandName == "ObjectSelectorUpdated")
        {
            Texture2D tex = property.objectReferenceValue as Texture2D;
            if (tex != null)
            {
                TextureZoomTracker.lastClickedTexture = tex;
                TextureZoomTracker.lastClickedPropertyPath = property.propertyPath;
            }
        }
    }
}

public class TextureZoomWindow : EditorWindow
{
    private Texture2D mSelection;

    [MenuItem("Window/Texture Zoom")]
    private static void Go()
    {
        GetWindow<TextureZoomWindow>("Texture Zoom");
    }

    private void OnEnable()
    {
        Selection.selectionChanged += OnSelectionChange;
        EditorApplication.update += OnUpdate;
        OnSelectionChange();
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChange;
        EditorApplication.update -= OnUpdate;
    }

    private void OnUpdate()
    {
        // Check if a texture was clicked in the Inspector
        if (TextureZoomTracker.lastClickedTexture != null && TextureZoomTracker.lastClickedTexture != mSelection)
        {
            mSelection = TextureZoomTracker.lastClickedTexture;
            Repaint();
        }
    }
    
    private void OnSelectionChange()
    {
        // First check if a Texture2D is directly selected
        Texture2D directSelection = Selection.activeObject as Texture2D;
        if (directSelection != null)
        {
            mSelection = directSelection;
            TextureZoomTracker.lastClickedTexture = directSelection;
        }
        else
        {
            // Check if we have a tracked texture from Inspector interaction
            if (TextureZoomTracker.lastClickedTexture != null)
            {
                mSelection = TextureZoomTracker.lastClickedTexture;
            }
            else
            {
                // Try to find a texture in the selected object
                UnityEngine.Object selectedObj = Selection.activeObject;
                if (selectedObj != null)
                {
                    Texture2D foundTex = FindFirstTextureInObject(selectedObj);
                    if (foundTex != null)
                    {
                        mSelection = foundTex;
                    }
                }
            }
        }

        Repaint();
    }

    private Texture2D FindFirstTextureInObject(UnityEngine.Object obj)
    {
        if (obj == null) return null;

        // Check if it's a GameObject - check its components
        GameObject go = obj as GameObject;
        if (go != null)
        {
            Component[] components = go.GetComponents<Component>();
            foreach (Component comp in components)
            {
                if (comp != null)
                {
                    Texture2D tex = FindFirstTextureInSerializedObject(comp);
                    if (tex != null) return tex;
                }
            }
        }
        else
        {
            Texture2D tex = FindFirstTextureInSerializedObject(obj);
            if (tex != null) return tex;
        }

        return null;
    }

    private Texture2D FindFirstTextureInSerializedObject(UnityEngine.Object obj)
    {
        if (obj == null) return null;

        SerializedObject serializedObject = new SerializedObject(obj);
        SerializedProperty iterator = serializedObject.GetIterator();

        // Iterate through all properties to find first texture
        if (iterator.NextVisible(true))
        {
            do
            {
                Texture2D tex = FindTextureInProperty(iterator);
                if (tex != null) return tex;
            } while (iterator.NextVisible(false));
        }

        return null;
    }

    private Texture2D FindTextureInProperty(SerializedProperty property)
    {
        // Check if this property is a Texture2D
        if (property.propertyType == SerializedPropertyType.ObjectReference)
        {
            return property.objectReferenceValue as Texture2D;
        }
        // Check if it's an array/list
        else if (property.isArray && property.arraySize > 0)
        {
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                if (element.propertyType == SerializedPropertyType.ObjectReference)
                {
                    Texture2D tex = element.objectReferenceValue as Texture2D;
                    if (tex != null) return tex;
                }
            }
        }
        return null;
    }

    private void OnGUI()
    {
        if (mSelection != null)
        {
            // Show property path if available
            if (!string.IsNullOrEmpty(TextureZoomTracker.lastClickedPropertyPath))
            {
                EditorGUILayout.LabelField($"Property: {TextureZoomTracker.lastClickedPropertyPath}", EditorStyles.miniLabel);
            }
            
            // Draw the selected texture
            Rect textureRect = GUILayoutUtility.GetRect(position.width - 20, position.height - 50, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUI.DrawTexture(textureRect, mSelection, ScaleMode.ScaleToFit, true, 0);
            
            // Show texture info
            EditorGUILayout.LabelField($"Size: {mSelection.width} x {mSelection.height}", EditorStyles.miniLabel);
        }
        else if (Selection.activeObject != null)
        {
            EditorGUILayout.HelpBox("Click on a Texture2D field in the Inspector to view it here.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("Select a Texture2D or an object with Texture2D fields, then click on a texture field in the Inspector.", MessageType.Info);
        }
    }
}
