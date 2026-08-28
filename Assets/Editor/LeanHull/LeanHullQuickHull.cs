using System.Collections.Generic;
using UnityEngine;

namespace Kweepa.LeanHull
{
    // A streamlined C# implementation of the 3D QuickHull algorithm.
    // Original algorithm published in 1996 by C.B. Barber, D.P. Dobkin, and H. Huhdanpaa: 
    // "The Quickhull algorithm for convex hulls".
    // 
    // The algorithm operates by:
    // 1. Finding extreme points on XYZ to form an initial tetrahedron.
    // 2. Iteratively processing points outside this shape, finding the furthest point.
    // 3. Extending the active faces outward to encompass that point.
    // 4. Repeating until all points are either engulfed or become part of the convex shell.
    public static class LeanHullQuickHull
    {
        private struct Face
        {
            public readonly int v0, v1, v2;
            public readonly Vector3 normal;
            public readonly float dist;

            public Face(int v0, int v1, int v2, Vector3[] verts)
            {
                this.v0 = v0;
                this.v1 = v1;
                this.v2 = v2;
                Vector3 a = verts[v0], b = verts[v1], c = verts[v2];
                normal = Vector3.Cross(b - a, c - a).normalized;
                dist = Vector3.Dot(normal, a);
            }
        }

        private readonly struct Edge
        {
            public readonly int v0, v1;
            public Edge(int a, int b) { v0 = a; v1 = b; }
            public override bool Equals(object obj)
            {
                if (obj is not Edge e) return false;
                return (v0 == e.v0 && v1 == e.v1) || (v0 == e.v1 && v1 == e.v0);
            }
            public override int GetHashCode() => Mathf.Min(v0, v1).GetHashCode() ^ Mathf.Max(v0, v1).GetHashCode();
        }

        private const float degenerateEpsilon = 1e-10f;
        /// <summary>Points this close to the current hull are treated as coplanar and not added as vertices (fewer triangles on flat facets).</summary>
        private const float coplanarEpsilon = 1e-5f;

        private static void ApplyPerturbation(Vector3[] working, System.Random rnd)
        {
            for (int i = 0; i < working.Length; ++i)
                working[i] += new Vector3((float)rnd.NextDouble() * 0.0002f - 0.0001f, (float)rnd.NextDouble() * 0.0002f - 0.0001f, (float)rnd.NextDouble() * 0.0002f - 0.0001f);
        }

        public static Mesh Generate(Vector3[] points)
        {
            if (points == null || points.Length < 4) return null;

            Vector3[] working = (Vector3[])points.Clone();
            var rnd = new System.Random(42);

            int i0 = 0, i1 = 0, i2 = 0, i3 = 0;
            bool tetrahedronOk = false;
            for (int attempt = 0; attempt < 2 && !tetrahedronOk; attempt++)
            {
                if (attempt > 0) ApplyPerturbation(working, rnd);

                float maxDist = 0;
                for (int i = 0; i < working.Length; ++i)
                {
                    for (int j = i + 1; j < working.Length; ++j)
                    {
                        float d = (working[i] - working[j]).sqrMagnitude;
                        if (d > maxDist) { maxDist = d; i0 = i; i1 = j; }
                    }
                }

                maxDist = 0;
                for (int i = 0; i < working.Length; ++i)
                {
                    if (i == i0 || i == i1) continue;
                    float d = Vector3.Cross(working[i] - working[i0], working[i] - working[i1]).sqrMagnitude;
                    if (d > maxDist) { maxDist = d; i2 = i; }
                }
                if (maxDist < degenerateEpsilon) continue; // collinear, need perturb

                Vector3 normal = Vector3.Cross(working[i1] - working[i0], working[i2] - working[i0]).normalized;
                maxDist = 0;
                for (int i = 0; i < working.Length; ++i)
                {
                    if (i == i0 || i == i1 || i == i2) continue;
                    float d = Mathf.Abs(Vector3.Dot(working[i] - working[i0], normal));
                    if (d > maxDist) { maxDist = d; i3 = i; }
                }
                if (maxDist < degenerateEpsilon) continue; // coplanar, need perturb

                if (Vector3.Dot(working[i3] - working[i0], normal) > 0)
                {
                    (i1, i2) = (i2, i1);
                }
                tetrahedronOk = true;
            }

            if (!tetrahedronOk) return null;

            List<Face> faces = new()
            {
                new Face(i0, i1, i2, working),
                new Face(i0, i2, i3, working),
                new Face(i2, i1, i3, working),
                new Face(i1, i0, i3, working)
            };

            bool[] used = new bool[working.Length];
            used[i0] = used[i1] = used[i2] = used[i3] = true;

            List<int> visibleFaces = new(128);
            Dictionary<Edge, int> edgeCount = new(128);
            List<Edge> boundary = new(128);

            int iterations = 0;
            const int maxIterations = 1000;
            while (iterations++ < maxIterations)
            {
                int furthestPoint = -1;
                float maxD = 0.001f;
                foreach (Face face in faces)
                {
                    for (int i = 0; i < working.Length; ++i)
                    {
                        if (used[i]) continue;
                        float d = Vector3.Dot(face.normal, working[i]) - face.dist;
                        if (d > maxD)
                        {
                            maxD = d;
                            furthestPoint = i;
                        }
                    }
                }

                if (furthestPoint == -1) break;

                used[furthestPoint] = true;

                // Discard points nearly coplanar with the hull: do not extend, just consume (fewer vertices on flat facets)
                if (maxD < coplanarEpsilon) continue;

                visibleFaces.Clear();
                for (int i = 0; i < faces.Count; ++i)
                {
                    if (Vector3.Dot(faces[i].normal, working[furthestPoint]) - faces[i].dist > 0.0001f)
                        visibleFaces.Add(i);
                }

                edgeCount.Clear();
                foreach (int fIdx in visibleFaces)
                {
                    Face f = faces[fIdx];
                    Edge e1 = new (f.v0, f.v1), e2 = new (f.v1, f.v2), e3 = new (f.v2, f.v0);

                    edgeCount[e1] = edgeCount.TryGetValue(e1, out int val1) ? val1 + 1 : 1;
                    edgeCount[e2] = edgeCount.TryGetValue(e2, out int val2) ? val2 + 1 : 1;
                    edgeCount[e3] = edgeCount.TryGetValue(e3, out int val3) ? val3 + 1 : 1;
                }

                boundary.Clear();
                foreach (var kvp in edgeCount)
                {
                    if (kvp.Value == 1) boundary.Add(kvp.Key);
                }

                visibleFaces.Sort((a, b) => b.CompareTo(a));
                foreach (int fIdx in visibleFaces) faces.RemoveAt(fIdx);

                Vector3 center = (working[i0] + working[i1] + working[i2] + working[i3]) / 4f;
                foreach (Edge e in boundary)
                {
                    Face newFace = new Face(e.v0, e.v1, furthestPoint, working);
                    if (Vector3.Dot(newFace.normal, center - working[furthestPoint]) > 0)
                        newFace = new Face(e.v1, e.v0, furthestPoint, working);
                    faces.Add(newFace);
                }
            }

            if (iterations >= maxIterations)
            {
                Debug.LogWarning($"LeanHull: QuickHull generation hit fallback limit of {maxIterations} iterations! The generated hull might be malformed or incomplete due to extreme mesh density or coplanar geometry.");
            }

            Mesh mesh = new() { name = "QuickHullMesh" };
            List<Vector3> newVerts = new();
            List<int> newTris = new();
            Dictionary<int, int> oldToNew = new();

            foreach (Face f in faces)
            {
                if (!oldToNew.ContainsKey(f.v0)) { oldToNew[f.v0] = newVerts.Count; newVerts.Add(working[f.v0]); }
                if (!oldToNew.ContainsKey(f.v1)) { oldToNew[f.v1] = newVerts.Count; newVerts.Add(working[f.v1]); }
                if (!oldToNew.ContainsKey(f.v2)) { oldToNew[f.v2] = newVerts.Count; newVerts.Add(working[f.v2]); }

                newTris.Add(oldToNew[f.v0]);
                newTris.Add(oldToNew[f.v1]);
                newTris.Add(oldToNew[f.v2]);
            }

            mesh.vertices = newVerts.ToArray();
            mesh.triangles = newTris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
