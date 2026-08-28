using UnityEngine;
using UnityEditor;
using System.Text.RegularExpressions;

/// <summary>
/// Property drawer that displays array elements using a specified property value as the foldout label.
/// </summary>
[CustomPropertyDrawer(typeof(PropertyLabelArrayAttribute))]
public class PropertyLabelArrayDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        PropertyLabelArrayAttribute labelAttribute = attribute as PropertyLabelArrayAttribute;

        // Get the index from the property path
        Match match = Regex.Match(property.propertyPath, @".*\[(\d+)\]");
        if (!match.Success)
        {
            // Not an array element, draw normally
            EditorGUI.PropertyField(position, property, label, true);
            return;
        }

        int index = int.Parse(match.Groups[1].Value);
        
        // Try to find the specified property within this array element
        SerializedProperty labelProperty = property.FindPropertyRelative(labelAttribute.PropertyName);
        
        string labelText;
        if (labelProperty != null)
        {
            // Get the display value based on property type
            labelText = GetPropertyDisplayValue(labelProperty);
        }
        else
        {
            // Fallback to element index if property not found
            labelText = $"Element {index}";
        }

        // Draw the element's field with the custom label
        EditorGUI.PropertyField(position, property, new GUIContent(labelText), true);
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // Return the height needed for the property, including children if expanded
        return EditorGUI.GetPropertyHeight(property, true);
    }

    /// <summary>
    /// Get a display-friendly string value from a SerializedProperty.
    /// </summary>
    private string GetPropertyDisplayValue(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer:
                return property.intValue.ToString();
            
            case SerializedPropertyType.Boolean:
                return property.boolValue.ToString();
            
            case SerializedPropertyType.Float:
                return property.floatValue.ToString("F2");
            
            case SerializedPropertyType.String:
                return string.IsNullOrEmpty(property.stringValue) ? "(empty)" : property.stringValue;
            
            case SerializedPropertyType.Enum:
                return property.enumDisplayNames[property.enumValueIndex];
            
            case SerializedPropertyType.ObjectReference:
                return property.objectReferenceValue != null 
                    ? property.objectReferenceValue.name 
                    : "(none)";
            
            case SerializedPropertyType.Color:
                return property.colorValue.ToString();
            
            case SerializedPropertyType.Vector2:
                return property.vector2Value.ToString();
            
            case SerializedPropertyType.Vector3:
                return property.vector3Value.ToString();
            
            case SerializedPropertyType.Vector4:
                return property.vector4Value.ToString();
            
            default:
                // For unsupported types, try to get a string representation
                return property.displayName;
        }
    }
}

