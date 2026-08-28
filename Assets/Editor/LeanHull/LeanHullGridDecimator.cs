// ==============================================================================
//  LeanHull - Size optimized Convex Hull Generator
//  Copyright (c) 2026 Kweepa
//  All rights reserved.
//
//  This script is part of the LeanHull tool suite.
//  Do not distribute or share this code without explicit permission.
// ==============================================================================

using UnityEngine;
using System.Collections.Generic;

namespace Kweepa.LeanHull
{
    // Grid-based decimation for reducing vertex count before QuickHull.
    public static class LeanHullGridDecimator
    {
        public static Vector3[] Decimate(Vector3[] points, int targetCount)
        {
            if (points.Length <= targetCount) return points;

            Bounds b = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 p in points) b.Encapsulate(p);

            Vector3 size = b.size;
            float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (maxDim == 0) return new[] { points[0] };

            // Binary search for the right grid cell size to yield ~targetCount cells
            float minCellSize = maxDim / 1000f;
            float maxCellSize = maxDim;
            float currentCellSize = (minCellSize + maxCellSize) * 0.5f;

            Dictionary<Vector3Int, Vector3> occupiedCells = new Dictionary<Vector3Int, Vector3>();

            for (int iter = 0; iter < 10; ++iter) // 10 iterations of binary search is usually enough
            {
                occupiedCells.Clear();
                float invCellSize = 1f / currentCellSize;

                foreach (Vector3 p in points)
                {
                    Vector3Int cellPos = new Vector3Int(
                        Mathf.FloorToInt(p.x * invCellSize),
                        Mathf.FloorToInt(p.y * invCellSize),
                        Mathf.FloorToInt(p.z * invCellSize));

                    if (!occupiedCells.ContainsKey(cellPos))
                    {
                        occupiedCells[cellPos] = p; // Keep first point found in cell
                    }
                }

                if (occupiedCells.Count > targetCount)
                {
                    minCellSize = currentCellSize;
                }
                else
                {
                    maxCellSize = currentCellSize;
                }
                currentCellSize = (minCellSize + maxCellSize) * 0.5f;
            }

            // Fallback if we still have too many (edge case with very uneven distribution)
            if (occupiedCells.Count > targetCount * 1.5f)
            {
                List<Vector3> list = new List<Vector3>(occupiedCells.Values);
                Vector3[] finalResult = new Vector3[targetCount];
                float step = (float)list.Count / targetCount;
                for (int i = 0; i < targetCount; ++i)
                {
                    finalResult[i] = list[Mathf.FloorToInt(i * step)];
                }
                return finalResult;
            }

            Vector3[] result = new Vector3[occupiedCells.Count];
            occupiedCells.Values.CopyTo(result, 0);
            return result;
        }
    }
}
