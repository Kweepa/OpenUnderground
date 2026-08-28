using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kweepa.LeanHull
{
    // A fast C# implementation of the Quadric Error Metrics (QEM) edge-collapse simplification algorithm.
    // Original algorithm published in 1997 by Michael Garland and Paul S. Heckbert:
    // "Surface Simplification Using Quadric Error Metrics".
    // 
    // The algorithm operates by:
    // 1. Assigning a 4x4 symmetric matrix (Quadric) to every vertex, representing the sum of squared distances to its adjacent planes.
    // 2. Calculating the optimal position and "error cost" for collapsing any valid edge `(v1, v2) -> v_new`.
    // 3. Iteratively collapsing the edges with the absolute lowest error cost.
    // 4. This ensures that the overall geometric shape and volume degrade as smoothly and accurately as mathematically possible.
    // 
    // Note: After hitting the target vertex count, the result is processed by SimpleQuickHull to guarantee the final state is strictly convex.
    public class LeanHullMeshSimplifier
    {
        private class Vertex
        {
            public Vector3 p;
            public int tstart;
            public int tcount;
            public SymmetricMatrix q;
            public bool border;
        }

        private class Triangle
        {
            public readonly int[] v = new int[3];
            public readonly double[] err = new double[4];
            public bool deleted;
            public bool dirty;
            public Vector3 n;
        }

        private List<Vertex> vertices;
        private List<Triangle> triangles;
        private List<int> refs;
        
        // Track state during edge collapses
        private readonly List<Triangle> deleted0 = new();
        private readonly List<Triangle> deleted1 = new();

        public static Mesh Simplify(Mesh sourceMesh, int targetCount)
        {
            LeanHullMeshSimplifier simplifier = new LeanHullMeshSimplifier();
            return simplifier.Process(sourceMesh, targetCount);
        }

        private Mesh Process(Mesh sourceMesh, int targetCount)
        {
            Vector3[] srcVerts = sourceMesh.vertices;
            int[] srcTris = sourceMesh.triangles;

            if (srcVerts.Length <= targetCount)
            {
                return LeanHullQuickHull.Generate(srcVerts);
            }

            vertices = new();
            triangles = new();
            refs = new();

            foreach (Vector3 p in srcVerts)
            {
                vertices.Add(new() { p = p });
            }

            for (int i = 0; i < srcTris.Length; i += 3)
            {
                Triangle t = new();
                t.v[0] = srcTris[i];
                t.v[1] = srcTris[i + 1];
                t.v[2] = srcTris[i + 2];
                triangles.Add(t);
            }

            UpdateMesh(0);

            foreach (Vertex v in vertices)
            {
                v.q = new SymmetricMatrix(0.0);
            }

            foreach (Triangle t in triangles)
            {
                Vector3 n = t.n;
                Vector3 p0 = vertices[t.v[0]].p;
                double d = -Vector3.Dot(n, p0);
                SymmetricMatrix sm = new(n.x, n.y, n.z, d);
                vertices[t.v[0]].q += sm;
                vertices[t.v[1]].q += sm;
                vertices[t.v[2]].q += sm;
            }

            foreach (Triangle t in triangles)
            {
                CalculateError(t);
            }

            int currentVertices = vertices.Count;
            int iteration = 0;

            while (currentVertices > targetCount)
            {
                UpdateMesh(iteration);

                foreach (Triangle t in triangles)
                {
                    t.dirty = false;
                }

                foreach (Triangle t in triangles)
                {
                    if (!t.deleted) CalculateError(t);
                }

                var edgeCandidates = new Dictionary<(int, int), (double err, int i0, int i1)>();
                foreach (Triangle t in triangles)
                {
                    if (t.deleted || t.dirty) continue;

                    for (int j = 0; j < 3; ++j)
                    {
                        int i0 = t.v[j];
                        int i1 = t.v[(j + 1) % 3];
                        if (vertices[i0].border || vertices[i1].border) continue;

                        int lo = Math.Min(i0, i1);
                        int hi = Math.Max(i0, i1);
                        var key = (lo, hi);
                        double err = t.err[j];
                        if (!edgeCandidates.TryGetValue(key, out var existing) || err < existing.err)
                            edgeCandidates[key] = (err, i0, i1);
                    }
                }

                var sorted = new List<(double err, int i0, int i1)>(edgeCandidates.Values);
                sorted.Sort((a, b) => a.err.CompareTo(b.err));

                bool collapsed = false;
                foreach (var (_, i0, i1) in sorted)
                {
                    if (TryCollapseEdge(i0, i1))
                    {
                        currentVertices--;
                        collapsed = true;
                        break;
                    }
                }
                if (!collapsed) break;
                iteration++;
            }

            CompactMesh();

            List<Vector3> outVerts = new();
            List<int> outTris = new();
            foreach (Vertex v in vertices)
            {
                outVerts.Add(v.p);
            }
            foreach (Triangle t in triangles)
            {
                if (!t.deleted)
                {
                    outTris.Add(t.v[0]);
                    outTris.Add(t.v[1]);
                    outTris.Add(t.v[2]);
                }
            }

            // Use QEM output directly; QuickHull is only run once at the start (in GenerateOptimizedHull) to produce the initial hull.
            Mesh newMesh = new Mesh();
            newMesh.SetVertices(outVerts);
            newMesh.SetTriangles(outTris, 0);
            newMesh.RecalculateBounds();
            newMesh.name = sourceMesh.name + "_SimplifiedQEM";

            vertices = null;
            triangles = null;
            refs = null;

            return newMesh;
        }

        private bool TryCollapseEdge(int i0, int i1)
        {
            Vertex v0 = vertices[i0];
            Vertex v1 = vertices[i1];

            if (v0.border || v1.border) return false;

            CalculateError(v0, v1, out Vector3 p);

            deleted0.Clear();
            deleted1.Clear();
            bool flipped = false;

            for (int k = 0; k < v0.tcount; ++k)
            {
                Triangle t0 = triangles[refs[v0.tstart + k]];
                if (t0.deleted) continue;
                int idx = t0.v[0] == i0 ? 0 : (t0.v[1] == i0 ? 1 : 2);
                int id1 = t0.v[(idx + 1) % 3];
                int id2 = t0.v[(idx + 2) % 3];

                if (id1 == i1 || id2 == i1)
                {
                    deleted0.Add(t0);
                    continue;
                }

                Vector3 d1 = vertices[id1].p - p;
                Vector3 d2 = vertices[id2].p - p;
                
                // Coplanar sanity check: prevent creating degenerate triangles
                if (Mathf.Abs(Vector3.Dot(d1.normalized, d2.normalized)) > 0.999f) 
                {
                     flipped = true; break;
                }
                
                Vector3 n = Vector3.Cross(d1, d2).normalized;
                deleted1.Add(t0);
                
                // Normal flip condition bounds how aggressively we warp the surface
                if (Vector3.Dot(n, t0.n) < 0.2f)
                {
                    flipped = true; break;
                }
            }

            if (flipped) return false;

            v0.p = p;
            v0.q += v1.q;

            foreach (Triangle t0 in deleted0)
            {
                t0.deleted = true;
            }

            foreach (Triangle t1 in deleted1)
            {
                t1.v[t1.v[0] == i0 ? 0 : (t1.v[1] == i0 ? 1 : 2)] = i0;
                RecomputeTriangleNormal(t1);
                t1.dirty = true;
                CalculateError(t1);
            }
            
            for (int k = 0; k < v1.tcount; ++k)
            {
                Triangle t1 = triangles[refs[v1.tstart + k]];
                if (t1.deleted) continue;
                int idx = t1.v[0] == i1 ? 0 : (t1.v[1] == i1 ? 1 : 2);
                t1.v[idx] = i0;
                RecomputeTriangleNormal(t1);
                t1.dirty = true;
                CalculateError(t1);
            }

            return true;
        }

        private void RecomputeTriangleNormal(Triangle t)
        {
            Vector3 p0 = vertices[t.v[0]].p;
            Vector3 p1 = vertices[t.v[1]].p;
            Vector3 p2 = vertices[t.v[2]].p;
            t.n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
        }

        private void UpdateMesh(int iteration)
        {
            if (iteration > 0)
            {
                int dst = 0;
                for (int i = 0; i < triangles.Count; ++i)
                {
                    if (!triangles[i].deleted)
                    {
                        triangles[dst++] = triangles[i];
                    }
                }
                triangles.RemoveRange(dst, triangles.Count - dst);
            }

            foreach (Vertex v in vertices)
            {
                v.tstart = 0;
                v.tcount = 0;
            }

            foreach (Triangle t in triangles)
            {
                Vector3 p0 = vertices[t.v[0]].p;
                Vector3 p1 = vertices[t.v[1]].p;
                Vector3 p2 = vertices[t.v[2]].p;
                t.n = Vector3.Cross(p1 - p0, p2 - p0).normalized;

                vertices[t.v[0]].tcount++;
                vertices[t.v[1]].tcount++;
                vertices[t.v[2]].tcount++;
            }

            int tstart = 0;
            foreach (Vertex v in vertices)
            {
                v.tstart = tstart;
                tstart += v.tcount;
                v.tcount = 0;
            }

            if (refs.Capacity < tstart) refs.Capacity = tstart;
            while (refs.Count < tstart) refs.Add(0);

            for (int i = 0; i < triangles.Count; ++i)
            {
                Triangle t = triangles[i];
                for (int j = 0; j < 3; ++j)
                {
                    Vertex v = vertices[t.v[j]];
                    refs[v.tstart + v.tcount] = i;
                    v.tcount++;
                }
            }

            if (iteration == 0)
            {
                // Convex/closed meshes (e.g. hulls) have no border edges; leave all border = false.
                // Skipping border detection avoids spurious rejection when mesh comes from QEM output.
                foreach (Vertex v in vertices)
                {
                    v.border = false;
                }
            }
        }

        private void CompactMesh()
        {
            int dst = 0;
            foreach (Vertex v in vertices)
            {
                v.tcount = 0;
            }

            for (int i = 0; i < triangles.Count; ++i)
            {
                if (!triangles[i].deleted)
                {
                    Triangle t = triangles[i];
                    triangles[dst++] = t;
                    vertices[t.v[0]].tcount = 1;
                    vertices[t.v[1]].tcount = 1;
                    vertices[t.v[2]].tcount = 1;
                }
            }
            triangles.RemoveRange(dst, triangles.Count - dst);

            dst = 0;
            for (int i = 0; i < vertices.Count; ++i)
            {
                if (vertices[i].tcount > 0)
                {
                    vertices[i].tstart = dst;
                    vertices[dst].p = vertices[i].p;
                    dst++;
                }
            }

            foreach (Triangle t in triangles)
            {
                t.v[0] = vertices[t.v[0]].tstart;
                t.v[1] = vertices[t.v[1]].tstart;
                t.v[2] = vertices[t.v[2]].tstart;
            }
            vertices.RemoveRange(dst, vertices.Count - dst);
        }

        private double CalculateError(Vertex v1, Vertex v2, out Vector3 p_result)
        {
            SymmetricMatrix q = v1.q + v2.q;
            bool border = v1.border && v2.border;
            double det = q.det(0, 1, 2, 1, 4, 5, 2, 5, 7);

            if (det != 0 && !border)
            {
                p_result = new(
                    (float)(-1 / det * q.det(1, 2, 3, 4, 5, 6, 5, 7, 8)),
                    (float)(1 / det * q.det(0, 2, 3, 1, 5, 6, 2, 7, 8)),
                    (float)(-1 / det * q.det(0, 1, 3, 1, 4, 6, 2, 5, 8)));
                return VertexError(q, p_result.x, p_result.y, p_result.z);
            }

            Vector3 p3 = (v1.p + v2.p) / 2.0f;
            double error1 = VertexError(q, v1.p.x, v1.p.y, v1.p.z);
            double error2 = VertexError(q, v2.p.x, v2.p.y, v2.p.z);
            double error3 = VertexError(q, p3.x, p3.y, p3.z);

            if (error1 < error2)
            {
                if (error1 < error3)
                {
                    p_result = v1.p;
                    return error1;
                }
            }
            else
            {
                if (error2 < error3)
                {
                    p_result = v2.p;
                    return error2;
                }
            }
            p_result = p3;
            return error3;
        }

        private void CalculateError(Triangle t)
        {
            Vector3 p;
            t.err[0] = CalculateError(vertices[t.v[0]], vertices[t.v[1]], out p);
            t.err[1] = CalculateError(vertices[t.v[1]], vertices[t.v[2]], out p);
            t.err[2] = CalculateError(vertices[t.v[2]], vertices[t.v[0]], out p);
            t.err[3] = Math.Min(t.err[0], Math.Min(t.err[1], t.err[2]));
        }

        private double VertexError(SymmetricMatrix q, double x, double y, double z) =>
            q.m[0] * x * x + 2 * q.m[1] * x * y + 2 * q.m[2] * x * z + 2 * q.m[3] * x + q.m[4] * y * y
                + 2 * q.m[5] * y * z + 2 * q.m[6] * y + q.m[7] * z * z + 2 * q.m[8] * z + q.m[9];

        private struct SymmetricMatrix
        {
            public double[] m;

            public SymmetricMatrix(double c)
            {
                m = new double[10];
                for (int i = 0; i < 10; ++i) m[i] = c;
            }

            public SymmetricMatrix(double m11, double m12, double m13, double m14,
                                   double m22, double m23, double m24,
                                   double m33, double m34,
                                   double m44)
            {
                m = new double[10];
                m[0] = m11; m[1] = m12; m[2] = m13; m[3] = m14;
                m[4] = m22; m[5] = m23; m[6] = m24;
                m[7] = m33; m[8] = m34;
                m[9] = m44;
            }

            public SymmetricMatrix(double a, double b, double c, double d)
            {
                m = new double[10];
                m[0] = a * a; m[1] = a * b; m[2] = a * c; m[3] = a * d;
                m[4] = b * b; m[5] = b * c; m[6] = b * d;
                m[7] = c * c; m[8] = c * d;
                m[9] = d * d;
            }

            public double det(int a11, int a12, int a13,
                              int a21, int a22, int a23,
                              int a31, int a32, int a33) =>
                m[a11] * m[a22] * m[a33] + m[a13] * m[a21] * m[a32] + m[a12] * m[a23] * m[a31]
                    - m[a13] * m[a22] * m[a31] - m[a11] * m[a23] * m[a32] - m[a12] * m[a21] * m[a33];

            public static SymmetricMatrix operator +(SymmetricMatrix a, SymmetricMatrix b) =>
                new(a.m[0] + b.m[0], a.m[1] + b.m[1], a.m[2] + b.m[2], a.m[3] + b.m[3],
                                            a.m[4] + b.m[4], a.m[5] + b.m[5], a.m[6] + b.m[6],
                                            a.m[7] + b.m[7], a.m[8] + b.m[8],
                                            a.m[9] + b.m[9]);
        }
    }
}
