using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;

/// <summary>
/// A helper enum to identify how a line-of-sight check was resolved.
/// </summary>
public enum VisibilityResult
{
    Success_StraightPath,
    Success_StraightChannel,
    Success_PortalClip,
    Success_VertexPivot,
    Failure_StraightPathBlocked,
    Failure_ChannelNotFound,
    Failure_WallsIntersect,
    Failure_NoPathFound
}

/// <summary>
/// Manages the pre-calculation, compression, storage, and querying of 
/// the Potentially Visible Set (PVS) for the level.
/// </summary>
public class PVSManager
{
    private Dictionary<Vector2Int, byte[]> pvsBitmaskData;
    private Tile[,] tiles;
    private int currentLevelNumber;

    // Cache for runtime performance
    private byte[] _cachedVisibilityBits;
    private Vector2Int _cachedFromCoord = new Vector2Int(-1, -1); // Initialize to an invalid coord

    // Increment this version number whenever the PVS generation logic changes
    // to force invalidation of old, incompatible PVS files.
    private const int PVS_DATA_VERSION = 5;

    // Grid dimensions are fixed at 64x64
    private const int GRID_DIM = 64;
    private const int TOTAL_TILES = GRID_DIM * GRID_DIM;
    private const int BITFIELD_SIZE = TOTAL_TILES / 8;

    #region Performance Optimization Cache
    /// <summary>
    /// A reusable cache of collections to avoid allocations during the intensive PVS generation process.
    /// </summary>
    private class VisibilityWorkCache
    {
        public readonly List<Vector2Int> LeftWall = new();
        public readonly List<Vector2Int> RightWall = new();
        public readonly List<Tile> PathTiles = new();

        public void ClearForNewCheck()
        {
            LeftWall.Clear();
            RightWall.Clear();
            PathTiles.Clear();
        }
    }
    #endregion

    /// <summary>
    /// Checks if PVS data is loaded for the current level. It tries to load from disk,
    /// validates it with a CRC check, and if it fails or is missing, it regenerates the data.
    /// This should be called when a level is loaded.
    /// </summary>
    public void EnsurePVSIsReady(int levelNumber)
    {
        currentLevelNumber = levelNumber;
        tiles = LevelLoader.GetLevel().tiles;

        // Only check StreamingAssets (bundled with build)
        string streamingAssetsPath = GetPVSFilePath(levelNumber);
        if (LoadPVS(streamingAssetsPath))
        {
            // Success! The data was loaded from StreamingAssets.
            return;
        }
        
        // PVS data not found in StreamingAssets - log warning and continue without PVS data
        UnityEngine.Debug.LogWarning($"PVS data for level {levelNumber} not found in StreamingAssets. Visibility checks will be disabled for this level.");
        pvsBitmaskData = null; // Ensure it's null so visibility checks return false
    }

    #region Public API

    /// <summary>
    /// Caches the visibility bitmask for a specific starting tile for faster subsequent checks.
    /// Call this when the camera/player moves to a new tile.
    /// </summary>
    public void CacheVisibilityFrom(Vector2Int from)
    {
        if (from == _cachedFromCoord)
        {
            return; // Data for this tile is already cached.
        }

        if (pvsBitmaskData != null && pvsBitmaskData.TryGetValue(from, out byte[] visibilityBits))
        {
            _cachedVisibilityBits = visibilityBits;
            _cachedFromCoord = from;
        }
        else
        {
            // If the 'from' tile has no PVS data (e.g., it's a solid wall), cache null.
            _cachedVisibilityBits = null;
            _cachedFromCoord = from;
        }
    }

    /// <summary>
    /// Checks visibility between two tiles using the pre-calculated bitmask data.
    /// This performs a dictionary lookup and is less efficient for repeated calls from the same 'from' tile.
    /// </summary>
    public bool IsVisible(Vector2Int from, Tile to)
    {
        if (pvsBitmaskData != null && pvsBitmaskData.TryGetValue(from, out byte[] visibilityBits))
        {
            int bitIndex = to.y * GRID_DIM + to.x;
            int byteIndex = bitIndex / 8;
            int bitInByte = bitIndex % 8;

            return (visibilityBits[byteIndex] & (1 << bitInByte)) != 0;
        }
        return false;
    }
    
    /// <summary>
    /// Checks visibility to a target tile using the cached visibility data.
    /// Ensure CacheVisibilityFrom() has been called with the correct origin first.
    /// This is the most efficient method for repeated checks from the same point.
    /// </summary>
    public bool IsVisible(Tile to)
    {
        if (_cachedVisibilityBits == null)
        {
            return false;
        }

        int bitIndex = to.y * GRID_DIM + to.x;
        int byteIndex = bitIndex / 8;
        int bitInByte = bitIndex % 8;

        if (byteIndex < 0 || byteIndex >= _cachedVisibilityBits.Length)
        {
            return false;
        }

        return (_cachedVisibilityBits[byteIndex] & (1 << bitInByte)) != 0;
    }
    
    #endregion
    
    /// <summary>
    /// Generates the PVS for the specified level, stores it, and saves it to disk with a CRC.
    /// This method is intended for use by editor tools only, not runtime.
    /// </summary>
    public void GenerateAndSavePVS(int levelNumber, string savePath)
    {
        currentLevelNumber = levelNumber;
        tiles = LevelLoader.GetLevel().tiles;
        UnityEngine.Debug.Log($"Starting PVS generation and compression for level {levelNumber}...");
        
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var newData = new ConcurrentDictionary<Vector2Int, byte[]>();
        var resultsTally = new ConcurrentDictionary<VisibilityResult, int>();
        
        var walkableTiles = new List<Vector2Int>();
        for (int y = 0; y < GRID_DIM; y++)
        {
            for (int x = 0; x < GRID_DIM; x++)
            {
                if (tiles[x, y].type != 0)
                {
                    walkableTiles.Add(new Vector2Int(x, y));
                }
            }
        }
        
        int totalWalkable = walkableTiles.Count;
        int processedCount = 0;

        var cancellationTokenSource = new CancellationTokenSource();
        Task progressReporter = Task.Run(async () => {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                int currentProgress = (int)(((float)processedCount / totalWalkable) * 100);
                UnityEngine.Debug.Log($"[{DateTime.Now.ToLongTimeString()}] PVS Generation Progress: {currentProgress}% ({processedCount}/{totalWalkable})");
                await Task.Delay(10000, cancellationTokenSource.Token);
            }
        }, cancellationTokenSource.Token);


        Parallel.ForEach(walkableTiles, 
            () => new VisibilityWorkCache(),
            (startTileCoord, loopState, localCache) => 
            {
                byte[] visibilityBits = new byte[BITFIELD_SIZE];
                for (int targetY = 0; targetY < GRID_DIM; targetY++)
                {
                    for (int targetX = 0; targetX < GRID_DIM; targetX++)
                    {
                        var endTileCoord = new Vector2Int(targetX, targetY);
                        if (startTileCoord == endTileCoord || tiles[targetX, targetY].type == 0) continue;
                        
                        VisibilityResult result = GetLineOfSightResult(startTileCoord, endTileCoord, localCache);
                        
                        resultsTally.AddOrUpdate(result, 1, (key, count) => count + 1);
                        
                        if (IsSuccessResult(result))
                        {
                            int bitIndex = targetY * GRID_DIM + targetX;
                            int byteIndex = bitIndex / 8;
                            int bitInByte = bitIndex % 8;
                            visibilityBits[byteIndex] |= (byte)(1 << bitInByte);
                        }
                    }
                }
                newData.TryAdd(startTileCoord, visibilityBits);
                Interlocked.Increment(ref processedCount);
                return localCache;
            },
            (finalCache) => { });
        
        cancellationTokenSource.Cancel();
        
        stopwatch.Stop();
        UnityEngine.Debug.Log($"PVS bitmask generation complete for {newData.Count} tiles in {stopwatch.ElapsedMilliseconds}ms. Starting PVS bleeding post-process...");
        var bleedStopwatch = new Stopwatch();
        bleedStopwatch.Start();

        // Post-processing step to "bleed" visibility into neighboring tiles for rendering stability.
        var finalPvsData = new Dictionary<Vector2Int, byte[]>();
        foreach (var kvp in newData)
        {
            var startTileCoord = kvp.Key;
            var originalVisibilityBits = kvp.Value;
            var bleededVisibilityBits = new byte[BITFIELD_SIZE];
            Array.Copy(originalVisibilityBits, bleededVisibilityBits, BITFIELD_SIZE);

            for (int y = 0; y < GRID_DIM; y++)
            {
                for (int x = 0; x < GRID_DIM; x++)
                {
                    int bitIndex = y * GRID_DIM + x;
                    int byteIndex = bitIndex / 8;
                    int bitInByte = bitIndex % 8;

                    // If the original tile is visible, make its neighbors visible too.
                    if ((originalVisibilityBits[byteIndex] & (1 << bitInByte)) != 0)
                    {
                        // Bleed to all 8 neighbors
                        for (int ny = -1; ny <= 1; ny++)
                        {
                            for (int nx = -1; nx <= 1; nx++)
                            {
                                if (nx == 0 && ny == 0) continue;

                                int neighborX = x + nx;
                                int neighborY = y + ny;

                                // Check bounds and ensure the neighbor is a walkable tile
                                if (neighborX >= 0 && neighborX < GRID_DIM &&
                                    neighborY >= 0 && neighborY < GRID_DIM &&
                                    tiles[neighborX, neighborY].type != 0)
                                {
                                    int neighborBitIndex = neighborY * GRID_DIM + neighborX;
                                    int neighborByteIndex = neighborBitIndex / 8;
                                    int neighborBitInByte = neighborBitIndex % 8;
                                    bleededVisibilityBits[neighborByteIndex] |= (byte)(1 << neighborBitInByte);
                                }
                            }
                        }
                    }
                }
            }
            finalPvsData[startTileCoord] = bleededVisibilityBits;
        }

        bleedStopwatch.Stop();
        UnityEngine.Debug.Log($"PVS bleeding post-process complete in {bleedStopwatch.ElapsedMilliseconds}ms.");

        pvsBitmaskData = finalPvsData;


        UnityEngine.Debug.Log("--- Visibility Check Results Breakdown ---");
        foreach (var kvp in resultsTally.OrderBy(kvp => kvp.Key.ToString()))
        {
            UnityEngine.Debug.Log($"- {kvp.Key}: {kvp.Value:N0}");
        }
        UnityEngine.Debug.Log("------------------------------------");

        SavePVS(savePath);
    }

    #region Save, Load, and CRC Logic

    private void SavePVS(string filePath)
    {
        try
        {
            uint crc = CalculateLevelCRC();
            using (var writer = new BinaryWriter(File.Open(filePath, FileMode.Create)))
            {
                writer.Write(crc);
                writer.Write(pvsBitmaskData.Count);
                foreach (var kvp in pvsBitmaskData)
                {
                    writer.Write(kvp.Key.x);
                    writer.Write(kvp.Key.y);
                    writer.Write(kvp.Value);
                }
            }
            UnityEngine.Debug.Log($"PVS data for level {currentLevelNumber} (Version: {PVS_DATA_VERSION}, CRC: {crc:X8}) saved to {filePath}");
        }
        catch(Exception e) { UnityEngine.Debug.LogError($"Failed to save PVS data: {e.Message}"); }
    }

    private bool LoadPVS(string filePath)
    {
        if (!File.Exists(filePath)) return false;

        try
        {
            uint currentLevelCrc = CalculateLevelCRC();
            using (var reader = new BinaryReader(File.Open(filePath, FileMode.Open)))
            {
                uint savedCrc = reader.ReadUInt32();
                if (savedCrc != currentLevelCrc)
                {
                    UnityEngine.Debug.LogWarning($"PVS data for level {currentLevelNumber} is out of date. (File CRC: {savedCrc:X8}, Level CRC: {currentLevelCrc:X8}). Recalculation is needed.");
                    return false;                    
                }

                pvsBitmaskData = new Dictionary<Vector2Int, byte[]>();
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    int x = reader.ReadInt32();
                    int y = reader.ReadInt32();
                    byte[] data = reader.ReadBytes(BITFIELD_SIZE);
                    pvsBitmaskData[new Vector2Int(x, y)] = data;
                }
            }
            UnityEngine.Debug.Log($"PVS data for level {currentLevelNumber} (Version: {PVS_DATA_VERSION}) loaded and validated successfully.");
            return true;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to load or validate PVS data: {e.Message}");
            return false;
        }
    }

    private uint CalculateLevelCRC()
    {
        using var memoryStream = new MemoryStream();
        using (var writer = new BinaryWriter(memoryStream))
        {
            for (int y = 0; y < GRID_DIM; y++)
            {
                for (int x = 0; x < GRID_DIM; x++)
                {
                    Tile t = tiles[x, y];
                    writer.Write(t.type);
                }
            }
        }
        return CRC32.Compute(memoryStream.ToArray());
    }

    #endregion

    #region Core Visibility Logic (Ported from debugged HTML Visualizer)
    
    private struct LineSegment { public Vector2 p1; public Vector2 p2; }
    private struct Wedge { public Vector2Int apex; public Vector2Int p1; public Vector2Int p2; }

    private VisibilityResult GetLineOfSightResult(Vector2Int start, Vector2Int end, VisibilityWorkCache cache)
    {
        cache.ClearForNewCheck();
        
        // --- STAGE 1: STRAIGHT PATH CHECK ---
        bool isStraightPath = false;
        if (start.x == end.x)
        {
            isStraightPath = true;
            for (int y = Mathf.Min(start.y, end.y) + 1; y < Mathf.Max(start.y, end.y); y++)
            {
                cache.PathTiles.Add(tiles[start.x, y]);
            }
        }
        else if (start.y == end.y)
        {
            isStraightPath = true;
            for (int x = Mathf.Min(start.x, end.x) + 1; x < Mathf.Max(start.x, end.x); x++)
            {
                cache.PathTiles.Add(tiles[x, start.y]);
            }
        }

        if (isStraightPath)
        {
            foreach(var tile in cache.PathTiles)
            {
                if (tile.type == 0) return VisibilityResult.Failure_StraightPathBlocked;
            }
            return VisibilityResult.Success_StraightPath;
        }

        // --- STAGE 2: CHANNEL FINDING ---
        FindVisibilityChannel(start, end, cache.LeftWall, cache.RightWall);
        if (cache.LeftWall.Count == 0 || cache.RightWall.Count == 0) return VisibilityResult.Failure_ChannelNotFound;
        
        // --- STAGE 3: WALL INTERSECTION CHECK ---
        for (int i = 0; i < cache.LeftWall.Count - 1; i++)
        {
            for (int j = 0; j < cache.RightWall.Count - 1; j++)
            {
                if (DoLinesIntersect(cache.LeftWall[i], cache.LeftWall[i+1], cache.RightWall[j], cache.RightWall[j+1]))
                {
                    return VisibilityResult.Failure_WallsIntersect;
                }
            }
        }

        // --- STAGE 4: EFFECTIVELY STRAIGHT WALL CHECK ---
        if (IsWallEffectivelyStraight(cache.LeftWall, start, end) || IsWallEffectivelyStraight(cache.RightWall, start, end))
        {
            return VisibilityResult.Success_StraightChannel;
        }
        
        // --- STAGE 5: PORTAL CLIPPING ---
        var originalStartSeg = new LineSegment { p1 = cache.LeftWall[0], p2 = cache.RightWall[0] };
        var originalEndSeg = new LineSegment { p1 = cache.LeftWall[^1], p2 = cache.RightWall[^1] };

        for (int i = 0; i < cache.LeftWall.Count - 1; i++)
        {
            for (int j = 0; j < cache.RightWall.Count - 1; j++)
            {
                LineSegment portalStartSeg = originalStartSeg;
                LineSegment portalEndSeg = originalEndSeg;

                var leftClipLine = (p1: (Vector2)cache.LeftWall[i], p2: (Vector2)cache.LeftWall[i + 1]);
                var rightClipLine = (p1: (Vector2)cache.RightWall[j], p2: (Vector2)cache.RightWall[j + 1]);

                ClipLineSegment(ref portalStartSeg, leftClipLine.p1, leftClipLine.p2);
                ClipLineSegment(ref portalEndSeg, leftClipLine.p1, leftClipLine.p2);
                
                // Flip the direction of the right clip line
                ClipLineSegment(ref portalStartSeg, rightClipLine.p2, rightClipLine.p1);
                ClipLineSegment(ref portalEndSeg, rightClipLine.p2, rightClipLine.p1);

                if ((portalStartSeg.p1 - portalStartSeg.p2).sqrMagnitude > 1e-6f && 
                    (portalEndSeg.p1 - portalEndSeg.p2).sqrMagnitude > 1e-6f)
                {
                    return VisibilityResult.Success_PortalClip;
                }
            }
        }

        // --- STAGE 6: VERTEX PIVOTING ---
        var wallsToTest = new[] { (wall: cache.LeftWall, opposite: cache.RightWall), (wall: cache.RightWall, opposite: cache.LeftWall) };
        foreach (var (wall, opposite) in wallsToTest)
        {
            for (int i = 1; i < wall.Count - 1; i++)
            {
                Vector2Int pivot = wall[i];
                
                var startWedge = new Wedge {
                    apex = pivot,
                    p1 = wall[i - 1],
                    p2 = (wall == cache.LeftWall) ? cache.RightWall[0] : cache.LeftWall[0]
                };

                var endWedge = new Wedge {
                    apex = pivot,
                    p1 = wall[i + 1],
                    p2 = (wall == cache.LeftWall) ? cache.RightWall[^1] : cache.LeftWall[^1]
                };

                // Enforce Clockwise ordering
                if (Cross(startWedge.p1 - pivot, startWedge.p2 - pivot) < 0) (startWedge.p1, startWedge.p2) = (startWedge.p2, startWedge.p1);
                if (Cross(endWedge.p1 - pivot, endWedge.p2 - pivot) < 0) (endWedge.p1, endWedge.p2) = (endWedge.p2, endWedge.p1);

                for (int j = 1; j < opposite.Count - 1; j++)
                {
                    Vector2Int clipper = opposite[j];
                    
                    // Clip Start Wedge
                    Vector2Int clipperOffsetStart = clipper - startWedge.apex;
                    float crossLeftStart = Cross(startWedge.p1 - startWedge.apex, clipperOffsetStart);
                    float crossRightStart = Cross(startWedge.p2 - startWedge.apex, clipperOffsetStart);

                    if (crossLeftStart > 0 && crossRightStart < 0)
                    {
                        if (wall == cache.LeftWall) startWedge.p1 = clipper;
                        else startWedge.p2 = clipper;
                    }
                    
                    // Clip End Wedge
                    Vector2Int clipperOffsetEnd = clipper - endWedge.apex;
                    float crossLeftEnd = Cross(endWedge.p1 - endWedge.apex, clipperOffsetEnd);
                    float crossRightEnd = Cross(endWedge.p2 - endWedge.apex, clipperOffsetEnd);
                    
                    if (crossLeftEnd > 0 && crossRightEnd < 0)
                    {
                        if (wall == cache.LeftWall) endWedge.p2 = clipper;
                        else endWedge.p1 = clipper;
                    }
                }
                
                var rotatedEndWedge = new Wedge {
                    apex = pivot,
                    p1 = pivot + (pivot - endWedge.p1),
                    p2 = pivot + (pivot - endWedge.p2)
                };

                if (CheckAngleOverlap(startWedge, rotatedEndWedge))
                {
                    return VisibilityResult.Success_VertexPivot;
                }
            }
        }

        return VisibilityResult.Failure_NoPathFound;
    }

    private void FindVisibilityChannel(Vector2Int start, Vector2Int end, List<Vector2Int> leftWall, List<Vector2Int> rightWall)
    {
        Vector2 startCenter = new Vector2(start.x + 0.5f, start.y + 0.5f);
        Vector2 endCenter = new Vector2(end.x + 0.5f, end.y + 0.5f);
        Vector2 pathDir = (endCenter - startCenter).normalized;

        Vector2 leftDir = new Vector2(-pathDir.y, pathDir.x);
        Vector2 rightDir = new Vector2(pathDir.y, -pathDir.x);

        Vector2Int startLeftCorner = GetExtremeCorner(start, leftDir);
        Vector2Int endLeftCorner = GetExtremeCorner(end, leftDir);
        Vector2Int startRightCorner = GetExtremeCorner(start, rightDir);
        Vector2Int endRightCorner = GetExtremeCorner(end, rightDir);
        
        const float BIAS = 1e-5f;
        Vector2 biasVec = rightDir * BIAS;

        Vector2 biasedStartLeft = new Vector2(startLeftCorner.x, startLeftCorner.y) + biasVec;
        Vector2 biasedEndLeft = new Vector2(endLeftCorner.x, endLeftCorner.y) + biasVec;
        Vector2 biasedStartRight = new Vector2(startRightCorner.x, startRightCorner.y) - biasVec;
        Vector2 biasedEndRight = new Vector2(endRightCorner.x, endRightCorner.y) - biasVec;
        
        FindChannelWall_RayMarch(biasedStartLeft, biasedEndLeft, endLeftCorner, true, leftWall, start, end);
        FindChannelWall_RayMarch(biasedStartRight, biasedEndRight, endRightCorner, false, rightWall, start, end);
    }

    private void FindChannelWall_RayMarch(Vector2 startCorner, Vector2 endCorner, Vector2Int finalCorner, bool isLeftWall, List<Vector2Int> wallCorners, Vector2Int startTile, Vector2Int endTile)
    {
        wallCorners.Clear();
        wallCorners.Add(new Vector2Int(Mathf.RoundToInt(startCorner.x), Mathf.RoundToInt(startCorner.y)));

        int currentGridX = Mathf.FloorToInt(startCorner.x);
        if (startCorner.x >= GRID_DIM) currentGridX = GRID_DIM - 1;
        int currentGridY = Mathf.FloorToInt(startCorner.y);
        if (startCorner.y >= GRID_DIM) currentGridY = GRID_DIM - 1;
        
        Vector2 rayDir = endCorner - startCorner;
        Vector2Int step = new Vector2Int((int)Mathf.Sign(rayDir.x), (int)Mathf.Sign(rayDir.y));

        if (rayDir.x == 0 || rayDir.y == 0)
        {
            wallCorners.Add(finalCorner);
            return;
        }

        float tDeltaX = Mathf.Abs(1 / rayDir.x);
        float tDeltaY = Mathf.Abs(1 / rayDir.y);
        
        float tMaxX = (step.x > 0) ? (currentGridX + 1 - startCorner.x) * tDeltaX : (startCorner.x - currentGridX) * tDeltaX;
        float tMaxY = (step.y > 0) ? (currentGridY + 1 - startCorner.y) * tDeltaY : (startCorner.y - currentGridY) * tDeltaY;
        
        int safety = 0;
        while(safety++ < GRID_DIM * 4)
        {
            var tileToCheck = new Vector2Int(currentGridX, currentGridY);
            if (tileToCheck.x == endTile.x && tileToCheck.y == endTile.y) break;
            
            if (tileToCheck.x != startTile.x || tileToCheck.y != startTile.y)
            {
                if (tileToCheck.x >= 0 && tileToCheck.x < GRID_DIM && tileToCheck.y >= 0 && tileToCheck.y < GRID_DIM)
                {
                    if (tiles[tileToCheck.x, tileToCheck.y].type == 0)
                    {
                        var dir = new Vector2(rayDir.y, -rayDir.x);
                        Vector2Int obstacleCorner = GetExtremeCorner(new Vector2Int(tileToCheck.x, tileToCheck.y), isLeftWall ? dir : -dir);
                        CheckAndFixConvexity(wallCorners, obstacleCorner, isLeftWall);
                        wallCorners.Add(obstacleCorner);
                    }
                }
            }

            if (tMaxX < tMaxY) { tMaxX += tDeltaX; currentGridX += step.x; }
            else { tMaxY += tDeltaY; currentGridY += step.y; }
        }
        
        CheckAndFixConvexity(wallCorners, finalCorner, isLeftWall);
        wallCorners.Add(finalCorner);
    }
    
    private void CheckAndFixConvexity(List<Vector2Int> wallCorners, Vector2Int newCorner, bool isLeftWall)
    {
        while (wallCorners.Count >= 2)
        {
            Vector2Int p1 = wallCorners[^2];
            Vector2Int p2 = wallCorners[^1];
            Vector2Int v_last = p2 - p1;
            Vector2Int v_new = newCorner - p2;
            float cross = Cross(v_last, v_new);

            if ((isLeftWall && cross >= 0) || (!isLeftWall && cross <= 0))
            {
                wallCorners.RemoveAt(wallCorners.Count - 1);
            }
            else
            {
                break;
            }
        }
    }
    
    #endregion

    #region Geometric and Grid Helpers

    private bool IsSuccessResult(VisibilityResult result)
    {
        return result == VisibilityResult.Success_StraightPath ||
               result == VisibilityResult.Success_StraightChannel ||
               result == VisibilityResult.Success_PortalClip ||
               result == VisibilityResult.Success_VertexPivot;
    }

    /// <summary>
    /// Gets the path to PVS data file in StreamingAssets.
    /// </summary>
    private string GetPVSFilePath(int levelNumber)
    {
        string fileName = $"pvs_data_level{levelNumber}.dat";
        return Path.Combine(Application.streamingAssetsPath, "PVS", fileName);
    }
    
    private Vector2Int GetExtremeCorner(Vector2Int tile, Vector2 dir)
    {
        var corners = new Vector2Int[] {
            new(tile.x, tile.y), new(tile.x + 1, tile.y),
            new(tile.x + 1, tile.y + 1), new(tile.x, tile.y + 1)
        };
        float maxDot = -Mathf.Infinity;
        Vector2Int ext = corners[0];
        foreach (var c in corners)
        {
            float dot = c.x * dir.x + c.y * dir.y;
            if (dot > maxDot)
            {
                maxDot = dot;
                ext = c;
            }
        }
        return ext;
    }

    private bool IsWallEffectivelyStraight(List<Vector2Int> wall, Vector2Int startTile, Vector2Int endTile)
    {
        if (wall.Count <= 2) return true;

        var startCorners = new HashSet<Vector2Int> {
            new(startTile.x, startTile.y), new(startTile.x+1, startTile.y),
            new(startTile.x+1, startTile.y+1), new(startTile.x, startTile.y+1)
        };
        var endCorners = new HashSet<Vector2Int> {
            new(endTile.x, endTile.y), new(endTile.x+1, endTile.y),
            new(endTile.x+1, endTile.y+1), new(endTile.x, endTile.y+1)
        };

        for (int i = 1; i < wall.Count - 1; i++)
        {
            if (!startCorners.Contains(wall[i]) && !endCorners.Contains(wall[i]))
            {
                return false;
            }
        }
        return true;
    }
    
    private void ClipLineSegment(ref LineSegment subject, Vector2 clipP1, Vector2 clipP2)
    {
        Vector2 p1 = subject.p1; Vector2 p2 = subject.p2;
        Vector2 clipEdge = clipP2 - clipP1;
        
        float p1_side = CrossFloat(clipEdge, p1 - clipP1);
        float p2_side = CrossFloat(clipEdge, p2 - clipP1);

        bool isP1Inside = p1_side > 0;
        bool isP2Inside = p2_side > 0;

        if (isP1Inside && isP2Inside) return;
        if (!isP1Inside && !isP2Inside) { subject.p1 = Vector2.zero; subject.p2 = Vector2.zero; return; }

        if (!GetLineIntersectionFloat(p1, p2, clipP1, clipP2, out Vector2 intersection)) {
             subject.p1 = Vector2.zero; subject.p2 = Vector2.zero; return; 
        }

        if (isP1Inside) subject.p2 = intersection;
        else subject.p1 = intersection;
    }

    private bool DoLinesIntersect(Vector2Int p1, Vector2Int p2, Vector2Int p3, Vector2Int p4)
    {
        // cross product of lines
        int d = (p1.x - p2.x) * (p3.y - p4.y) - (p1.y - p2.y) * (p3.x - p4.x);
        if (d == 0) return false; // lines are parallel
        int s = d > 0 ? 1 : -1;
        d = Mathf.Abs(d);
        
        // normally we would divide t and u by d and compare with 0..1
        // but instead we can multiply t and u by d to cancel out the divide
        // then everything can be integer

        int t = s * ((p1.x - p3.x) * (p3.y - p4.y) - (p1.y - p3.y) * (p3.x - p4.x));
        if (t < 0 || t > d) return false;

        int u = -s * ((p1.x - p2.x) * (p1.y - p3.y) - (p1.y - p2.y) * (p1.x - p3.x));
        if (u < 0 || u > d) return false;

        return true;
    }
    
    private bool GetLineIntersectionFloat(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, out Vector2 result)
    {
        result = Vector2.zero;
        float d = (p1.x - p2.x) * (p3.y - p4.y) - (p1.y - p2.y) * (p3.x - p4.x);
        if (Mathf.Abs(d) < 1e-6f) return false;

        float t = ((p1.x - p3.x) * (p3.y - p4.y) - (p1.y - p3.y) * (p3.x - p4.x)) / d;
        
        result = new Vector2(p1.x + t * (p2.x - p1.x), p1.y + t * (p2.y - p1.y));
        return true;
    }
    
    private static float Cross(Vector2Int a, Vector2Int b) => a.y * b.x - a.x * b.y;
    private static float CrossFloat(Vector2 a, Vector2 b) => a.y * b.x - a.x * b.y;
    private static bool OnRight(Vector2Int pivot, Vector2Int v1, Vector2Int v2) => Cross(v1 - pivot, v2 - pivot) > 0;
    
    private static bool CheckAngleOverlap(Wedge wedge1, Wedge wedge2)
    {
        return OnRight(wedge1.apex, wedge1.p1, wedge2.p2) && !OnRight(wedge1.apex, wedge1.p2, wedge2.p1);
    }

    #endregion

    #region Nested CRC32 Implementation
    private static class CRC32
    {
        private static readonly uint[] table;
        static CRC32()
        {
            uint poly = 0xedb88320;
            table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int j = 0; j < 8; j++) { c = (c & 1) == 1 ? (poly ^ (c >> 1)) : (c >> 1); }
                table[i] = c;
            }
        }
        public static uint Compute(byte[] bytes)
        {
            uint crc = 0xffffffff;
            foreach (byte b in bytes) { crc = table[(crc ^ b) & 0xff] ^ (crc >> 8); }
            return crc ^ 0xffffffff;
        }
    }
    #endregion
}
