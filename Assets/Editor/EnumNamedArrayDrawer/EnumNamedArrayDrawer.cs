using UnityEngine;
using UnityEditor;
using System;
using System.Text.RegularExpressions;

// This drawer targets any field with the [EnumNamedArray] attribute.
[CustomPropertyDrawer(typeof(EnumNamedArrayAttribute))]
public class EnumNamedArrayDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EnumNamedArrayAttribute enumAttribute = attribute as EnumNamedArrayAttribute;

        // Get the index from the property path
        Match match = Regex.Match(property.propertyPath, @".*\[(\d+)\]");
        int index = int.Parse(match.Groups[1].Value) + enumAttribute.offset;

        // Get the corresponding enum name
        string[] enumNames = Enum.GetNames(enumAttribute.TargetEnum);
        string enumName = index < enumNames.Length ? enumNames[index] : "Invalid";

        // Draw the element's field with the enum name as the label
        // includeChildren = true allows complex types to be drawn with foldouts
        EditorGUI.PropertyField(position, property, new GUIContent(enumName), true);
    }
    
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // Return the height needed for the property, including children if expanded
        return EditorGUI.GetPropertyHeight(property, true);
    }
}
