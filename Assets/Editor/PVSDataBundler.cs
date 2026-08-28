using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Editor tool to generate PVS data files and save them directly to StreamingAssets for bundling with builds.
/// </summary>
public class PVSDataBundler
{
    private const int MIN_LEVEL = 1;
    private const int MAX_LEVEL = 9; // Levels array is 1-indexed, size 10

    [MenuItem("Tools/Generate PVS Data for Build")]
    public static void GeneratePVSDataForBuild()
    {
        string streamingAssetsPVSDir = Path.Combine(Application.dataPath, "StreamingAssets", "PVS");
        
        // Create PVS directory in StreamingAssets if it doesn't exist
        if (!Directory.Exists(streamingAssetsPVSDir))
        {
            Directory.CreateDirectory(streamingAssetsPVSDir);
            Debug.Log($"Created directory: {streamingAssetsPVSDir}");
        }

        List<int> existingLevels = new List<int>();
        List<int> generatedLevels = new List<int>();
        List<int> missingLevels = new List<int>();

        // Process each level
        for (int level = MIN_LEVEL; level <= MAX_LEVEL; level++)
        {
            string fileName = $"pvs_data_level{level}.dat";
            string streamingAssetsPath = Path.Combine(streamingAssetsPVSDir, fileName);

            // Check if file already exists in StreamingAssets
            if (File.Exists(streamingAssetsPath))
            {
                existingLevels.Add(level);
                Debug.Log($"PVS data for level {level} already exists in StreamingAssets, skipping generation.");
            }
            else
            {
                // File doesn't exist - generate it directly to StreamingAssets
                Debug.Log($"Generating PVS data for level {level}...");
                
                if (TryGeneratePVSForLevel(level, streamingAssetsPath))
                {
                    generatedLevels.Add(level);
                    Debug.Log($"Successfully generated PVS data for level {level}");
                }
                else
                {
                    Debug.LogError($"Failed to generate PVS data for level {level}.");
                    missingLevels.Add(level);
                }
            }
        }

        // Refresh asset database to show new files in Unity
        AssetDatabase.Refresh();

        // Summary
        Debug.Log("=== PVS Data Bundling Summary ===");
        Debug.Log($"Already exists in StreamingAssets: {existingLevels.Count} levels ({string.Join(", ", existingLevels)})");
        Debug.Log($"Generated: {generatedLevels.Count} levels ({string.Join(", ", generatedLevels)})");
        if (missingLevels.Count > 0)
        {
            Debug.LogWarning($"Failed: {missingLevels.Count} levels ({string.Join(", ", missingLevels)}). These levels will not have PVS data in the build.");
        }
        Debug.Log("=================================");
    }

    /// <summary>
    /// Attempts to generate PVS data for a level by loading it in the editor and saving directly to StreamingAssets.
    /// This requires LevelLoader and DataLoader to be initialized (game must be in play mode).
    /// </summary>
    private static bool TryGeneratePVSForLevel(int levelNumber, string savePath)
    {
        // Check if we're in play mode - required for LevelLoader to be initialized
        if (!Application.isPlaying)
        {
            Debug.LogWarning($"Cannot generate PVS data for level {levelNumber} outside of play mode. " +
                           $"Please enter play mode first, then run this tool again.");
            return false;
        }

        // In play mode, we can use the existing LevelLoader
        if (LevelLoader.sLevelLoader == null)
        {
            Debug.LogWarning($"LevelLoader.sLevelLoader is null. Cannot generate PVS data for level {levelNumber}. " +
                           $"Make sure the game is running and LevelLoader is initialized.");
            return false;
        }

        try
        {
            // LoadLevelGeometry may return early if level is already loaded,
            // so we need to ensure the level is loaded
            LevelLoader.sLevelLoader.LoadLevelGeometry(levelNumber);
            
            // Get the level and generate PVS data directly to StreamingAssets
            Level level = LevelLoader.GetLevel();
            if (level != null && level.pvs != null)
            {
                // Call GenerateAndSavePVS directly with the StreamingAssets path
                level.pvs.GenerateAndSavePVS(levelNumber, savePath);
                return true;
            }
            else
            {
                Debug.LogError($"Level {levelNumber} was not loaded successfully");
                return false;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to generate PVS data for level {levelNumber}: {e.Message}");
            return false;
        }
    }
}
