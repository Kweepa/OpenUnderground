using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

[CustomPropertyDrawer(typeof(EObjectTypeEntry))]
[CustomPropertyDrawer(typeof(FilterableEnumListAttribute))]
public class FilterableEnumListDrawer : PropertyDrawer
{
    // Track which list item is being edited (property path + index)
    private static Dictionary<string, int> editingIndices = new Dictionary<string, int>();
    
    // Track dropdown state per property
    private static Dictionary<string, bool> dropdownOpen = new Dictionary<string, bool>();
    
    // Track search text per property
    private static Dictionary<string, string> searchTexts = new Dictionary<string, string>();
    
    // Track scroll positions per property
    private static Dictionary<string, Vector2> scrollPositions = new Dictionary<string, Vector2>();
    
    // Cache filtered enum values per property (now stores Array instead of List<EObjectType>)
    private static Dictionary<string, Array> filteredEnums = new Dictionary<string, Array>();
    
    // Cache enum type per property path
    private static Dictionary<string, Type> enumTypes = new Dictionary<string, Type>();
    
    // Cache all enum values per enum type
    private static Dictionary<Type, Array> enumValuesCache = new Dictionary<Type, Array>();
    
    private const float itemHeight = 18f;
    private const float searchFieldHeight = 20f;
    private const float maxDropdownHeight = 300f;
    private const int visibleItemCount = (int)(maxDropdownHeight / itemHeight);

    // Helper method to get enum type from SerializedProperty
    private Type GetEnumType(SerializedProperty property)
    {
        string propertyPath = property.propertyPath;
        
        // Check cache first
        if (enumTypes.ContainsKey(propertyPath))
        {
            return enumTypes[propertyPath];
        }
        
        Type enumType = null;
        
        // Check if this is an EObjectTypeEntry struct (has a "value" field)
        SerializedProperty valueProperty = property.FindPropertyRelative("value");
        if (valueProperty != null && valueProperty.propertyType == SerializedPropertyType.Enum)
        {
            // This is an EObjectTypeEntry - it's always EObjectType
            enumType = typeof(EObjectType);
        }
        else if (property.propertyType == SerializedPropertyType.Enum)
        {
            // Direct enum property - detect type from field info
            enumType = GetEnumTypeFromProperty(property);
        }
        else if (property.isArray)
        {
            // Array/list - check first element
            SerializedProperty firstElement = property.arraySize > 0 ? property.GetArrayElementAtIndex(0) : null;
            if (firstElement != null && firstElement.propertyType == SerializedPropertyType.Enum)
            {
                enumType = GetEnumTypeFromProperty(firstElement);
            }
        }
        
        if (enumType != null && enumType.IsEnum)
        {
            enumTypes[propertyPath] = enumType;
            // Cache enum values for this type if not already cached
            if (!enumValuesCache.ContainsKey(enumType))
            {
                enumValuesCache[enumType] = Enum.GetValues(enumType);
            }
        }
        
        return enumType ?? typeof(EObjectType); // Fallback to EObjectType for backward compatibility
    }
    
    // Helper method to get enum type from property using reflection
    private Type GetEnumTypeFromProperty(SerializedProperty property)
    {
        try
        {
            UnityEngine.Object targetObject = property.serializedObject.targetObject;
            if (targetObject != null)
            {
                string[] pathParts = property.propertyPath.Split('.');
                Type currentType = targetObject.GetType();
                FieldInfo fieldInfo = null;
                
                foreach (string part in pathParts)
                {
                    if (part == "Array")
                        continue;
                    
                    if (part.StartsWith("data["))
                    {
                        // Array element - get element type
                        if (fieldInfo != null)
                        {
                            Type fieldType = fieldInfo.FieldType;
                            if (fieldType.IsArray)
                            {
                                currentType = fieldType.GetElementType();
                            }
                            else if (fieldType.IsGenericType && typeof(System.Collections.IList).IsAssignableFrom(fieldType))
                            {
                                currentType = fieldType.GetGenericArguments()[0];
                            }
                        }
                        // Reset fieldInfo since we're now inside the array element type
                        fieldInfo = null;
                        continue;
                    }
                    
                    fieldInfo = currentType.GetField(part, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fieldInfo != null)
                    {
                        currentType = fieldInfo.FieldType;
                    }
                    else
                    {
                        return null;
                    }
                }
                
                if (fieldInfo != null)
                {
                    Type fieldType = fieldInfo.FieldType;
                    if (fieldType.IsEnum)
                    {
                        return fieldType;
                    }
                }
                // If we ended up with a type that's an enum, return it
                else if (currentType != null && currentType.IsEnum)
                {
                    return currentType;
                }
            }
        }
        catch { }
        
        return null;
    }
    
    // Helper method to get cached enum values for a type
    private Array GetEnumValues(Type enumType)
    {
        if (!enumValuesCache.ContainsKey(enumType))
        {
            enumValuesCache[enumType] = Enum.GetValues(enumType);
        }
        return enumValuesCache[enumType];
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Check if this is an EObjectTypeEntry struct (has a "value" field)
        SerializedProperty valueProperty = property.FindPropertyRelative("value");
        if (valueProperty != null && valueProperty.propertyType == SerializedPropertyType.Enum)
        {
            // This is an EObjectTypeEntry - draw the enum with our custom dropdown
            DrawEnumWithDropdown(position, valueProperty, label);
            EditorGUI.EndProperty();
            return;
        }
        
        // Check if this is a direct enum property with FilterableEnumListAttribute
        if (property.propertyType == SerializedPropertyType.Enum && attribute is FilterableEnumListAttribute)
        {
            DrawEnumWithDropdown(position, property, label);
            EditorGUI.EndProperty();
            return;
        }

        // Check if this is a list element (shouldn't have the attribute, but handle it)
        if (property.propertyPath.Contains(".Array.data["))
        {
            EditorGUI.PropertyField(position, property, label, true);
            EditorGUI.EndProperty();
            return;
        }

        // For List<T>, Unity serializes it with an "Array" child property
        SerializedProperty arrayProperty = property.FindPropertyRelative("Array");
        
        // If Array property not found, check if property itself is an array
        // (this handles arrays, but List<T> should have Array child)
        if (arrayProperty == null)
        {
            if (property.isArray)
            {
                arrayProperty = property;
            }
            else
            {
                // Not a list/array, use default drawer
                EditorGUI.PropertyField(position, property, label, true);
                EditorGUI.EndProperty();
                return;
            }
        }
        
        // Verify it's actually an array
        if (!arrayProperty.isArray)
        {
            EditorGUI.PropertyField(position, property, label, true);
            EditorGUI.EndProperty();
            return;
        }
        
        // Verify the element type is an enum
        SerializedProperty firstElement = arrayProperty.arraySize > 0 ? arrayProperty.GetArrayElementAtIndex(0) : null;
        if (firstElement != null && firstElement.propertyType != SerializedPropertyType.Enum)
        {
            // Not an enum list, use default drawer
            EditorGUI.PropertyField(position, property, label, true);
            EditorGUI.EndProperty();
            return;
        }
        
        // Detect enum type for this property
        Type enumType = GetEnumType(arrayProperty);
        if (enumType == null || !enumType.IsEnum)
        {
            EditorGUI.PropertyField(position, property, label, true);
            EditorGUI.EndProperty();
            return;
        }

        string propertyPath = property.propertyPath;

        // Initialize dictionaries if needed
        if (!dropdownOpen.ContainsKey(propertyPath))
        {
            dropdownOpen[propertyPath] = false;
            searchTexts[propertyPath] = "";
            scrollPositions[propertyPath] = Vector2.zero;
            editingIndices[propertyPath] = -1;
        }

        Rect currentRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

        // Draw list header
        Rect headerRect = currentRect;
        headerRect.width -= 60f;
        EditorGUI.LabelField(headerRect, label.text + $" ({arrayProperty.arraySize})");
        
        // Add button
        Rect addButtonRect = new Rect(position.x + position.width - 55f, position.y, 55f, EditorGUIUtility.singleLineHeight);
        if (GUI.Button(addButtonRect, "Add"))
        {
            arrayProperty.arraySize++;
            SerializedProperty newElement = arrayProperty.GetArrayElementAtIndex(arrayProperty.arraySize - 1);
            // Find the value property inside the struct
            SerializedProperty valueProp = newElement.FindPropertyRelative("value");
            if (valueProp != null)
            {
                valueProp.enumValueIndex = 0; // Set to first enum value
            }
            // Don't call ApplyModifiedProperties() - Unity handles struct serialization automatically
        }

        currentRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        // Draw list items
        for (int i = 0; i < arrayProperty.arraySize; i++)
        {
            SerializedProperty element = arrayProperty.GetArrayElementAtIndex(i);
            
            Rect itemRect = new Rect(currentRect.x, currentRect.y, currentRect.width, EditorGUIUtility.singleLineHeight);
            
            // Index label
            Rect indexRect = new Rect(itemRect.x, itemRect.y, 30f, itemRect.height);
            EditorGUI.LabelField(indexRect, i.ToString());
            
            // Enum field
            Rect enumRect = new Rect(itemRect.x + 35f, itemRect.y, itemRect.width - 85f, itemRect.height);
            
            // Check if this item is being edited
            bool isEditing = editingIndices[propertyPath] == i && dropdownOpen[propertyPath];
            
            // Get enum type and values for this element
            Type elementEnumType = GetEnumType(element);
            Array elementEnumValues = GetEnumValues(elementEnumType);
            
            // Draw current enum value as a clickable popup button
            // Use reflection for struct properties to avoid serialization errors
            bool isStructElement = element.propertyPath.Contains(".value");
            object currentValue;
            if (isStructElement)
            {
                currentValue = GetEnumValueFromStructProperty(element);
            }
            else
            {
                int enumIndex = element.enumValueIndex;
                if (enumIndex < 0 || enumIndex >= elementEnumValues.Length)
                {
                    enumIndex = 0;
                    element.enumValueIndex = 0;
                }
                currentValue = elementEnumValues.GetValue(enumIndex);
            }
            string currentValueName = currentValue.ToString();
            
            // Draw popup button and handle clicks
            if (GUI.Button(enumRect, currentValueName, EditorStyles.popup))
            {
                // Toggle dropdown for this item
                if (isEditing)
                {
                    dropdownOpen[propertyPath] = false;
                    editingIndices[propertyPath] = -1;
                }
                else
                {
                    // Close any other open dropdowns
                    foreach (var key in dropdownOpen.Keys.ToList())
                    {
                        dropdownOpen[key] = false;
                        editingIndices[key] = -1;
                    }
                    
                    dropdownOpen[propertyPath] = true;
                    editingIndices[propertyPath] = i;
                    searchTexts[propertyPath] = "";
                    scrollPositions[propertyPath] = Vector2.zero;
                }
            }
            
            // Remove button
            Rect removeRect = new Rect(itemRect.x + itemRect.width - 25f, itemRect.y, 25f, itemRect.height);
            if (GUI.Button(removeRect, "×"))
            {
                arrayProperty.DeleteArrayElementAtIndex(i);
                // Don't call ApplyModifiedProperties() - Unity handles struct serialization automatically
                if (isEditing)
                {
                    dropdownOpen[propertyPath] = false;
                    editingIndices[propertyPath] = -1;
                }
                EditorGUI.EndProperty();
                return;
            }
            
            // Draw dropdown if this item is being edited (before incrementing currentRect)
            if (isEditing)
            {
                Rect dropdownStartRect = new Rect(currentRect.x, currentRect.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing, currentRect.width, 0);
                DrawDropdown(dropdownStartRect, propertyPath, element);
                // Use enum type already calculated above for height calculation
                currentRect.y += GetDropdownHeight(propertyPath, elementEnumType) + EditorGUIUtility.standardVerticalSpacing;
            }
            
            currentRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        }

        // Handle clicks outside dropdown to close it
        if (Event.current.type == EventType.MouseDown && dropdownOpen[propertyPath])
        {
            bool clickedInside = false;
            int editingIndex = editingIndices[propertyPath];
            if (editingIndex >= 0 && editingIndex < arrayProperty.arraySize)
            {
                Rect itemRect = new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing + 
                    (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * editingIndex, 
                    position.width, EditorGUIUtility.singleLineHeight);
                Rect dropdownRect = GetDropdownRect(itemRect, propertyPath);
                clickedInside = dropdownRect.Contains(Event.current.mousePosition);
            }
            
            if (!clickedInside)
            {
                dropdownOpen[propertyPath] = false;
                editingIndices[propertyPath] = -1;
                Event.current.Use();
            }
        }

        EditorGUI.EndProperty();
    }

    // Helper method to get enum value from a struct property using reflection
    private object GetEnumValueFromStructProperty(SerializedProperty structProperty)
    {
        Type enumType = GetEnumType(structProperty);
        Array enumValues = GetEnumValues(enumType);
        
        if (!structProperty.propertyPath.Contains(".value"))
            return enumValues.GetValue(0);
            
        try
        {
            UnityEngine.Object targetObject = structProperty.serializedObject.targetObject;
            if (targetObject != null)
            {
                string fullPath = structProperty.propertyPath;
                int arrayIndex = fullPath.IndexOf(".Array.data[");
                if (arrayIndex > 0)
                {
                    string listName = fullPath.Substring(0, arrayIndex);
                    int startBracket = fullPath.IndexOf('[', arrayIndex) + 1;
                    int endBracket = fullPath.IndexOf(']', startBracket);
                    int index = int.Parse(fullPath.Substring(startBracket, endBracket - startBracket));
                    
                    FieldInfo listField = targetObject.GetType().GetField(listName, BindingFlags.Public | BindingFlags.Instance);
                    if (listField != null)
                    {
                        object listObj = listField.GetValue(targetObject);
                        if (listObj is System.Collections.IList list && index >= 0 && index < list.Count)
                        {
                            object entry = list[index];
                            // Use reflection to get the "value" field from the struct
                            FieldInfo valueField = entry.GetType().GetField("value");
                            if (valueField != null)
                            {
                                return valueField.GetValue(entry);
                            }
                        }
                    }
                }
            }
        }
        catch { }
        
        return enumValues.GetValue(0);
    }

    private void DrawDropdown(Rect itemRect, string propertyPath, SerializedProperty elementProperty)
    {
        Rect dropdownRect = GetDropdownRect(itemRect, propertyPath);
        
        // Get enum type for this property
        Type enumType = GetEnumType(elementProperty);
        Array allEnumValues = GetEnumValues(enumType);
        
        // Update filtered enum list
        UpdateFilteredEnums(propertyPath, enumType, allEnumValues);
        Array filtered = filteredEnums[propertyPath];
        
        // Draw dropdown background
        GUI.Box(dropdownRect, "", EditorStyles.helpBox);
        
        Rect contentRect = new Rect(dropdownRect.x + 2f, dropdownRect.y + 2f, dropdownRect.width - 4f, dropdownRect.height - 4f);
        
        // Search field with unique control name for focus
        Rect searchRect = new Rect(contentRect.x, contentRect.y, contentRect.width, searchFieldHeight);
        string searchControlName = "FilterableEnumSearch_" + propertyPath;
        
        // Set control name and create the TextField
        GUI.SetNextControlName(searchControlName);
        string newSearchText = EditorGUI.TextField(searchRect, searchTexts[propertyPath]);
        if (newSearchText != searchTexts[propertyPath])
        {
            searchTexts[propertyPath] = newSearchText;
            scrollPositions[propertyPath] = Vector2.zero; // Reset scroll when filter changes
            UpdateFilteredEnums(propertyPath, enumType, allEnumValues); // Re-filter
        }
        
        // Scrollable list
        Rect scrollRect = new Rect(contentRect.x, contentRect.y + searchFieldHeight + 2f, contentRect.width, contentRect.height - searchFieldHeight - 2f);
        float totalHeight = filtered.Length * itemHeight;
        
        Rect viewRect = new Rect(0, 0, scrollRect.width - 20f, totalHeight);
        scrollPositions[propertyPath] = GUI.BeginScrollView(scrollRect, scrollPositions[propertyPath], viewRect);
        
        // Virtualized rendering - only render visible items
        float scrollY = scrollPositions[propertyPath].y;
        int startIndex = Mathf.Max(0, (int)(scrollY / itemHeight) - 1);
        int endIndex = Mathf.Min(filtered.Length, startIndex + visibleItemCount + 2);
        
        // Draw visible items
        for (int i = startIndex; i < endIndex; i++)
        {
            object enumValue = filtered.GetValue(i);
            Rect enumItemRect = new Rect(0, i * itemHeight, viewRect.width, itemHeight);
            
            // Get current enum value from SerializedProperty - this is the source of truth
            int currentEnumIndex = elementProperty.enumValueIndex;
            if (currentEnumIndex < 0 || currentEnumIndex >= allEnumValues.Length)
            {
                currentEnumIndex = 0;
            }
            object currentValue = allEnumValues.GetValue(currentEnumIndex);
            
            // Highlight current selection - compare actual enum values, not indices
            if (currentValue.Equals(enumValue))
            {
                EditorGUI.DrawRect(enumItemRect, new Color(0.3f, 0.5f, 0.9f, 0.3f));
            }
            
            if (GUI.Button(enumItemRect, enumValue.ToString(), EditorStyles.label))
            {
                // Find the index of this enum value in allEnumValues array by comparing integer values
                // This is more reliable than Array.IndexOf for enums with explicit values
                int targetIndex = -1;
                int enumIntValue = Convert.ToInt32(enumValue);
                for (int j = 0; j < allEnumValues.Length; j++)
                {
                    if (Convert.ToInt32(allEnumValues.GetValue(j)) == enumIntValue)
                    {
                        targetIndex = j;
                        break;
                    }
                }
                
                if (targetIndex >= 0 && targetIndex < allEnumValues.Length)
                {
                    // Check if we're modifying a struct property
                    bool isStructModification = elementProperty.propertyPath.Contains(".value");
                    
                    if (isStructModification)
                    {
                        // For struct properties, update SerializedProperty directly
                        // This works for nested structs in Unity
                        UnityEngine.Object targetObject = elementProperty.serializedObject.targetObject;
                        if (targetObject != null)
                        {
                            Undo.RecordObject(targetObject, "Change Enum Value");
                            
                            if (elementProperty.propertyType == SerializedPropertyType.Enum)
                            {
                                elementProperty.enumValueIndex = targetIndex;
                                elementProperty.serializedObject.ApplyModifiedProperties();
                                EditorUtility.SetDirty(targetObject);
                            }
                        }
                    }
                    else
                    {
                        // For non-struct properties, modify directly
                        elementProperty.enumValueIndex = targetIndex;
                        elementProperty.serializedObject.ApplyModifiedProperties();
                    }
                    
                    // Close dropdown and consume event
                    dropdownOpen[propertyPath] = false;
                    editingIndices[propertyPath] = -1;
                    Event.current.Use();
                    
                    // Force repaint
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }
            }
        }
        
        GUI.EndScrollView();
    }

    private Rect GetDropdownRect(Rect itemRect, string propertyPath)
    {
        float dropdownWidth = 250f;
        float dropdownHeight = GetDropdownHeight(propertyPath);
        
        return new Rect(itemRect.x, itemRect.y, dropdownWidth, dropdownHeight);
    }

    private float GetDropdownHeight(string propertyPath, Type enumType = null)
    {
        // Get enum type - use provided type or try to find it from cached property paths
        if (enumType == null)
        {
            enumType = enumTypes.ContainsKey(propertyPath) ? enumTypes[propertyPath] : typeof(EObjectType);
        }
        Array allEnumValues = GetEnumValues(enumType);
        
        UpdateFilteredEnums(propertyPath, enumType, allEnumValues);
        int count = filteredEnums.ContainsKey(propertyPath) ? filteredEnums[propertyPath].Length : allEnumValues.Length;
        return Mathf.Min(maxDropdownHeight, count * itemHeight + searchFieldHeight + 4f);
    }

    private void DrawEnumWithDropdown(Rect position, SerializedProperty enumProperty, GUIContent label)
    {
        string propertyPath = enumProperty.propertyPath;
        
        // Get enum type for this property
        Type enumType = GetEnumType(enumProperty);
        Array allEnumValues = GetEnumValues(enumType);

        // Initialize dictionaries if needed
        if (!dropdownOpen.ContainsKey(propertyPath))
        {
            dropdownOpen[propertyPath] = false;
            searchTexts[propertyPath] = "";
            scrollPositions[propertyPath] = Vector2.zero;
            editingIndices[propertyPath] = -1;
        }

        // Draw label and get the remaining rect for the enum field
        Rect enumRect = EditorGUI.PrefixLabel(position, label);
        
        // Get current enum value
        // Always read from SerializedProperty for consistency - it's the source of truth
        int enumIndex = enumProperty.enumValueIndex;
        if (enumIndex < 0 || enumIndex >= allEnumValues.Length)
        {
            enumIndex = 0;
            enumProperty.enumValueIndex = 0;
        }
        object currentValue = allEnumValues.GetValue(enumIndex);
        string currentValueName = currentValue.ToString();
        
        // Draw popup button
        if (GUI.Button(enumRect, currentValueName, EditorStyles.popup))
        {
            bool isEditing = dropdownOpen[propertyPath];
            if (isEditing)
            {
                dropdownOpen[propertyPath] = false;
                editingIndices[propertyPath] = -1;
            }
            else
            {
                // Close any other open dropdowns
                foreach (var key in dropdownOpen.Keys.ToList())
                {
                    dropdownOpen[key] = false;
                    editingIndices[key] = -1;
                }
                
                dropdownOpen[propertyPath] = true;
                editingIndices[propertyPath] = 0; // Not used for single enum, but needed for state
                searchTexts[propertyPath] = "";
                scrollPositions[propertyPath] = Vector2.zero;
            }
        }
        
        // Draw dropdown if open
        if (dropdownOpen[propertyPath])
        {
            Rect dropdownStartRect = new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing, position.width, 0);
            DrawDropdown(dropdownStartRect, propertyPath, enumProperty);
        }
    }

    private void UpdateFilteredEnums(string propertyPath, Type enumType, Array allEnumValues)
    {
        string search = searchTexts.ContainsKey(propertyPath) ? searchTexts[propertyPath] : "";
        
        if (string.IsNullOrEmpty(search))
        {
            filteredEnums[propertyPath] = allEnumValues;
        }
        else
        {
            string searchLower = search.ToLowerInvariant();
            List<object> filtered = new List<object>();
            foreach (object enumValue in allEnumValues)
            {
                if (enumValue.ToString().ToLowerInvariant().Contains(searchLower))
                {
                    filtered.Add(enumValue);
                }
            }
            // Create array of same type
            Array filteredArray = Array.CreateInstance(enumType, filtered.Count);
            for (int i = 0; i < filtered.Count; i++)
            {
                filteredArray.SetValue(filtered[i], i);
            }
            filteredEnums[propertyPath] = filteredArray;
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // Check if this is an EObjectTypeEntry struct
        SerializedProperty valueProperty = property.FindPropertyRelative("value");
        if (valueProperty != null && valueProperty.propertyType == SerializedPropertyType.Enum)
        {
            float height = EditorGUIUtility.singleLineHeight;
            string propertyPath = valueProperty.propertyPath;
            if (dropdownOpen.ContainsKey(propertyPath) && dropdownOpen[propertyPath])
            {
                Type enumType = GetEnumType(valueProperty);
                height += GetDropdownHeight(propertyPath, enumType) + EditorGUIUtility.standardVerticalSpacing;
            }
            return height;
        }
        
        // Check if this is a direct enum property with FilterableEnumListAttribute
        if (property.propertyType == SerializedPropertyType.Enum && attribute is FilterableEnumListAttribute)
        {
            float height = EditorGUIUtility.singleLineHeight;
            string propertyPath = property.propertyPath;
            if (dropdownOpen.ContainsKey(propertyPath) && dropdownOpen[propertyPath])
            {
                Type enumType = GetEnumType(property);
                height += GetDropdownHeight(propertyPath, enumType) + EditorGUIUtility.standardVerticalSpacing;
            }
            return height;
        }

        // Check if this is a list element, use default height
        if (property.propertyPath.Contains(".Array.data["))
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        SerializedProperty arrayProperty = property.FindPropertyRelative("Array");
        if (arrayProperty == null || !arrayProperty.isArray)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        float height2 = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing; // Header
        
        height2 += (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * arrayProperty.arraySize;
        
        // Add dropdown height if open
        string propertyPath2 = property.propertyPath;
        if (dropdownOpen.ContainsKey(propertyPath2) && dropdownOpen[propertyPath2])
        {
            // Try to get enum type from first element if available
            SerializedProperty firstElement = arrayProperty.arraySize > 0 ? arrayProperty.GetArrayElementAtIndex(0) : null;
            Type enumType = firstElement != null ? GetEnumType(firstElement) : typeof(EObjectType);
            height2 += GetDropdownHeight(propertyPath2, enumType) + EditorGUIUtility.standardVerticalSpacing;
        }
        
        return height2;
    }
}
