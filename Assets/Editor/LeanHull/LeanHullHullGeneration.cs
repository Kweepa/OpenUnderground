// ==============================================================================
//  LeanHull - Size optimized Convex Hull Generator
//  Copyright (c) 2026 Kweepa
//  All rights reserved.
//
//  This script is part of the LeanHull tool suite.
//  Do not distribute or share this code without explicit permission.
// ==============================================================================

using UnityEngine;

namespace Kweepa.LeanHull
{
    // Raw verts to optimized hull mesh pipeline (decimation, QuickHull, simplification).
    public static class LeanHullHullGenerator
    {
        // Generates an optimized convex hull from raw vertices. Returns the simplified hull mesh; originalHull is the unsimplified hull for preview.
        public static Mesh Generate(Vector3[] rawVerts, int targetVerts, out int preSimVertCount, out Mesh originalHull, string debugName = "")
        {
            preSimVertCount = 0;
            originalHull = null;
            if (rawVerts == null || rawVerts.Length < 4) return null;

            Vector3[] processedVerts = rawVerts;

            // Keep QuickHull input small for speed (hull quality is fine from a few hundred samples)
            const int maxVertsForQuickHull = 400;
            if (processedVerts.Length > maxVertsForQuickHull)
            {
                processedVerts = LeanHullGridDecimator.Decimate(processedVerts, maxVertsForQuickHull);
            }

            // High-precision simplification: Only run QEM pre-pass if we are still extremely high poly
            if (processedVerts.Length > 2500)
            {
                Mesh tempMesh = new Mesh { vertices = processedVerts };

                int[] fakeTris = new int[(processedVerts.Length - 2) * 3];
                for (int v = 0; v < processedVerts.Length - 2; ++v)
                {
                    fakeTris[v * 3] = 0;
                    fakeTris[v * 3 + 1] = v + 1;
                    fakeTris[v * 3 + 2] = v + 2;
                }
                tempMesh.triangles = fakeTris;
                Mesh preSimplified = LeanHullMeshSimplifier.Simplify(tempMesh, 1000);
                processedVerts = preSimplified.vertices;
                Object.DestroyImmediate(tempMesh);
                Object.DestroyImmediate(preSimplified);
            }

            Mesh hull = LeanHullQuickHull.Generate(processedVerts);

            if (hull != null)
            {
                originalHull = hull;
                preSimVertCount = Mathf.Min(255, hull.vertexCount);
                int tV = Mathf.Min(targetVerts, hull.vertexCount);
                Mesh mesh = hull;
                while (mesh.vertexCount > tV)
                {
                    Mesh next = LeanHullMeshSimplifier.Simplify(mesh, mesh.vertexCount - 1);
                    if (next == null) break;
                    if (mesh != hull) Object.DestroyImmediate(mesh);
                    mesh = next;
                }
                return mesh;
            }

            return null;
        }
    }
}
