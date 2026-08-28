using System;
using UnityEngine;

/// <summary>
/// Attribute to display array elements using a specified property value as the foldout label.
/// Usage: [PropertyLabelArray("propertyName")]
/// </summary>
public class PropertyLabelArrayAttribute : PropertyAttribute
{
    public string PropertyName { get; private set; }

    /// <summary>
    /// Create a PropertyLabelArray attribute.
    /// </summary>
    /// <param name="propertyName">The name of the property to use as the label</param>
    public PropertyLabelArrayAttribute(string propertyName)
    {
        PropertyName = propertyName;
    }
}

