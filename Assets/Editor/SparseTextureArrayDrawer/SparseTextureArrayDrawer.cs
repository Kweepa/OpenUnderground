// Filename: Editor/SparseTextureArrayDrawer.cs
using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

// Draws a property array based on a "sparse" enum for the keys
// so only enums with defined values are visible in the inspector.
// At the moment it's hardcoded to an attribute defined in Conversations, but it's not dependent on anything conversation-y.

[CustomPropertyDrawer(typeof(Conversations.SparseEnumArrayAttribute))]
public class SparseTextureArrayDrawer : PropertyDrawer
{
    // We still manage the foldout state manually.
    private static Dictionary<string, bool> foldoutStates = new();

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Get the attribute to determine the enum type
        var sparseAttribute = (Conversations.SparseEnumArrayAttribute)attribute;
        var enumType = sparseAttribute.EnumType;
        var enumValues = Enum.GetValues(enumType);

        // Find the "entries" array inside our container class
        var arrayProperty = property.FindPropertyRelative("entries");

        // Auto-manage the array size
        int maxIndex = enumValues.Length > 0 ? enumValues.Cast<int>().Max() : -1;
        int requiredSize = maxIndex + 1;
        if (arrayProperty.arraySize != requiredSize)
        {
            arrayProperty.arraySize = requiredSize;
        }
        
        // --- Manually draw the foldout ---
        string propertyPath = property.propertyPath;
        if (!foldoutStates.ContainsKey(propertyPath))
        {
            foldoutStates[propertyPath] = true;
        }
        
        Rect foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        bool isExpanded = foldoutStates[propertyPath];
        foldoutStates[propertyPath] = EditorGUI.Foldout(foldoutRect, isExpanded, label, true);

        if (isExpanded)
        {
            EditorGUI.indentLevel++;
            Rect elementRect = new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing, position.width, EditorGUIUtility.singleLineHeight);
            
            // Iterate through the DEFINED enum values only
            foreach (var enumValue in enumValues)
            {
                int index = Convert.ToInt32(enumValue);
                SerializedProperty element = arrayProperty.GetArrayElementAtIndex(index);

                // SIMPLIFIED: We can now draw the element directly.
                EditorGUI.PropertyField(elementRect, element, new GUIContent(enumValue.ToString()), true);
                
                elementRect.y += EditorGUI.GetPropertyHeight(element) + EditorGUIUtility.standardVerticalSpacing;
            }
            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // Get our manually-managed foldout state
        string propertyPath = property.propertyPath;
        bool isExpanded = foldoutStates.ContainsKey(propertyPath) && foldoutStates[propertyPath];

        // Start with the height of the foldout header
        float totalHeight = EditorGUIUtility.singleLineHeight;
        
        if (isExpanded)
        {
            var sparseAttribute = (Conversations.SparseEnumArrayAttribute)attribute;
            var enumValues = Enum.GetValues(sparseAttribute.EnumType);
            
            totalHeight += EditorGUIUtility.standardVerticalSpacing;
            totalHeight += (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * enumValues.Length;
        }
        
        return totalHeight;
    }
}
