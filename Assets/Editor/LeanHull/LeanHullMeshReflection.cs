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
    // Cached reflected meshes and render-safe matrix for negative scale.
    public static class LeanHullMeshReflection
    {
        private static readonly Dictionary<(Mesh, int), Mesh> ReflectedMeshCache = new Dictionary<(Mesh, int), Mesh>();

        // Reflection mask: bit 0 = flip X, bit 1 = flip Y, bit 2 = flip Z. Used to cache reflected meshes per (mesh, axis flip).
        public static int GetReflectionMask(Vector3 scale)
        {
            int mask = 0;
            if (scale.x < 0) mask |= 1;
            if (scale.y < 0) mask |= 2;
            if (scale.z < 0) mask |= 4;
            return mask;
        }

        // Returns a cached mesh with vertices and normals reflected on axes where scale is negative, and winding flipped.
        // Draw this with GetRenderSafeMatrix(matrix, scale) so the visual matches the convex hull and lighting is correct.
        public static Mesh GetOrCreateReflectedMesh(Mesh source, int reflectionMask)
        {
            if (source == null || reflectionMask == 0) return source;
            var key = (source, reflectionMask);
            if (ReflectedMeshCache.TryGetValue(key, out Mesh reflected)) return reflected;
            reflected = Object.Instantiate(source);
            reflected.name = source.name + " (Reflected)";
            Vector3[] verts = source.vertices;
            Vector3[] reflectedVerts = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                reflectedVerts[i] = new Vector3(
                    (reflectionMask & 1) != 0 ? -v.x : v.x,
                    (reflectionMask & 2) != 0 ? -v.y : v.y,
                    (reflectionMask & 4) != 0 ? -v.z : v.z);
            }
            reflected.vertices = reflectedVerts;
            if (source.normals != null && source.normals.Length == verts.Length)
            {
                Vector3[] norms = source.normals;
                Vector3[] reflectedNorms = new Vector3[norms.Length];
                for (int i = 0; i < norms.Length; i++)
                {
                    Vector3 n = norms[i];
                    reflectedNorms[i] = new Vector3(
                        (reflectionMask & 1) != 0 ? -n.x : n.x,
                        (reflectionMask & 2) != 0 ? -n.y : n.y,
                        (reflectionMask & 4) != 0 ? -n.z : n.z);
                }
                reflected.normals = reflectedNorms;
            }
            if (source.tangents != null && source.tangents.Length == verts.Length)
            {
                Vector4[] tangs = source.tangents;
                Vector4[] reflectedTangs = new Vector4[tangs.Length];
                for (int i = 0; i < tangs.Length; i++)
                {
                    Vector4 t = tangs[i];
                    reflectedTangs[i] = new Vector4(
                        (reflectionMask & 1) != 0 ? -t.x : t.x,
                        (reflectionMask & 2) != 0 ? -t.y : t.y,
                        (reflectionMask & 4) != 0 ? -t.z : t.z,
                        t.w);
                }
                reflected.tangents = reflectedTangs;
            }
            for (int s = 0; s < source.subMeshCount; s++)
            {
                int[] tris = source.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3)
                    (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                reflected.SetTriangles(tris, s);
            }
            reflected.RecalculateBounds();
            ReflectedMeshCache[key] = reflected;
            return reflected;
        }

        // Returns a matrix with the same vertex transform but positive scale (columns negated where scale was negative). Use with reflected mesh.
        public static Matrix4x4 GetRenderSafeMatrix(Matrix4x4 m, Vector3 scale)
        {
            if (scale.x >= 0 && scale.y >= 0 && scale.z >= 0) return m;
            Vector4 c0 = m.GetColumn(0);
            Vector4 c1 = m.GetColumn(1);
            Vector4 c2 = m.GetColumn(2);
            Vector4 c3 = m.GetColumn(3);
            if (scale.x < 0) c0 = new Vector4(-c0.x, -c0.y, -c0.z, c0.w);
            if (scale.y < 0) c1 = new Vector4(-c1.x, -c1.y, -c1.z, c1.w);
            if (scale.z < 0) c2 = new Vector4(-c2.x, -c2.y, -c2.z, c2.w);
            Matrix4x4 result = new Matrix4x4();
            result.SetColumn(0, c0);
            result.SetColumn(1, c1);
            result.SetColumn(2, c2);
            result.SetColumn(3, c3);
            return result;
        }
    }
}
