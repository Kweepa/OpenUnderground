// C#
// To use this script:
// 1. Create a folder named "Editor" in your "Assets" directory if you don't already have one.
// 2. Place this script inside the "Editor" folder.
// 3. Unity will automatically compile it.
// 4. A new menu item will appear under "Tools" -> "Clean Missing Scripts in Prefabs".
// 5. Click this menu item to run the script. It will scan all prefabs in your project.

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class PrefabMissingScriptCleaner
{
    [MenuItem("Tools/Clean Missing Scripts in Prefabs")]
    public static void CleanMissingScripts()
    {
        // Get the GUIDs of all .prefab files in the project
        string[] allPrefabGuids = AssetDatabase.FindAssets("t:Prefab");
        int prefabsScanned = 0;
        int prefabsCleaned = 0;
        int componentsRemoved = 0;

        if (allPrefabGuids.Length == 0)
        {
            Debug.Log("No prefabs found in the project.");
            return;
        }

        Debug.Log($"Found {allPrefabGuids.Length} prefabs. Starting scan...");

        // Loop through each prefab GUID
        foreach (string guid in allPrefabGuids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

            if (prefab != null)
            {
                prefabsScanned++;
                bool wasModified = false;

                // We need to check the root object and all its children
                List<GameObject> allGameObjects = new List<GameObject>();
                GetAllGameObjectsInChildren(prefab, allGameObjects);

                foreach (GameObject go in allGameObjects)
                {
                    // This function counts the number of components with missing scripts
                    int missingScriptCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);

                    if (missingScriptCount > 0)
                    {
                        // This function removes the components with missing scripts
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                        
                        componentsRemoved += missingScriptCount;
                        wasModified = true;
                    }
                }

                // If we made changes, save the prefab asset
                if (wasModified)
                {
                    prefabsCleaned++;
                    Debug.Log($"Cleaned {componentsRemoved} missing component(s) from prefab: {assetPath}", prefab);
                    PrefabUtility.SavePrefabAsset(prefab);
                }
            }
        }

        Debug.Log($"<color=green>Scan complete!</color> Scanned: {prefabsScanned} prefabs. Cleaned: {prefabsCleaned} prefabs. Total components removed: {componentsRemoved}.");
    }

    /// <summary>
    /// Recursively gets all child GameObjects, including the parent.
    /// </summary>
    private static void GetAllGameObjectsInChildren(GameObject parent, List<GameObject> list)
    {
        list.Add(parent);
        foreach (Transform child in parent.transform)
        {
            GetAllGameObjectsInChildren(child.gameObject, list);
        }
    }
}
