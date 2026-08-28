// ==============================================================================
//  LeanHull - Size optimized Convex Hull Generator
//  Copyright (c) 2026 Kweepa
//  All rights reserved.
//
//  This script is part of the LeanHull tool suite.
//  Do not distribute or share this code without explicit permission.
// ==============================================================================

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Kweepa.LeanHull
{
    // Preview texture rendering and hull drawing (GL/camera) for LeanHullWindow.
    public partial class LeanHullWindow
    {
        private long GetEdge(int v1, int v2)
        {
            if (v1 < v2) return ((long)v1 << 32) | (uint)v2;
            return ((long)v2 << 32) | (uint)v1;
        }

        private void EnsureWireMaterials()
        {
            if (_wireMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    _wireMaterial = new Material(shader);
                    _wireMaterial.hideFlags = HideFlags.HideAndDontSave;
                    _wireMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    _wireMaterial.SetInt("_ZWrite", 0);
                    _wireMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                }
            }
            if (_wireAAMaterial == null)
            {
                Shader aaShader = Shader.Find("LeanHull/WireframeAA");
                if (aaShader != null)
                {
                    _wireAAMaterial = new Material(aaShader);
                    _wireAAMaterial.hideFlags = HideFlags.HideAndDontSave;
                    _wireAAMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    _wireAAMaterial.SetInt("_ZWrite", 0);
                    _wireAAMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                }
            }
        }

        private void DrawHulls(PreviewItem item, List<Mesh> hulls, Color color, Camera cam, int texSize, int splitMode, float splitPosition)
        {
            if (hulls == null || hulls.Count == 0) return;

            EnsureWireMaterials();
            if (_wireAAMaterial == null) return;

            Vector3 camPos = cam.transform.position;
            Vector3 camForward = cam.transform.forward;

            // Calculate the exact clipping plane in screen space
            float x_ndc = splitPosition * 2.0f - 1.0f;
            Matrix4x4 vp = cam.projectionMatrix * cam.worldToCameraMatrix;
            Vector3 planeNormal = new Vector3(
                vp.m00 - x_ndc * vp.m30,
                vp.m01 - x_ndc * vp.m31,
                vp.m02 - x_ndc * vp.m32
            ).normalized;
            Plane splitPlane = new Plane(planeNormal, cam.transform.position);

            float halfFovRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;

            for (int pass = 0; pass < 2; ++pass)
            {
                bool renderingFront = pass == 1;

                if (!_wireAAMaterial.SetPass(0)) continue;
                GL.Begin(GL.QUADS);

                for (int m = 0; m < hulls.Count; ++m)
                {
                    Mesh hull = hulls[m];
                    if (hull == null) continue;
                    Matrix4x4 finalMatrix = item.hullMatrices[m];

                    Color hullColor = (item.highlightedHullIndex >= 0 && m == item.highlightedHullIndex) ? Color.yellow : color;
                    Color hullPassColor = renderingFront ? hullColor : new Color(hullColor.r * 0.5f, hullColor.g * 0.5f, hullColor.b * 0.5f, hullColor.a * 0.5f);

                    Vector3[] verts = hull.vertices;
                    int[] tris = hull.triangles;

                    Dictionary<long, bool> edgeIsFront = new Dictionary<long, bool>();

                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        Vector3 w0 = finalMatrix.MultiplyPoint3x4(verts[tris[i]]);
                        Vector3 w1 = finalMatrix.MultiplyPoint3x4(verts[tris[i + 1]]);
                        Vector3 w2 = finalMatrix.MultiplyPoint3x4(verts[tris[i + 2]]);

                        Vector3 normal = Vector3.Cross(w1 - w0, w2 - w0).normalized;
                        Vector3 viewDir = (camPos - w0).normalized;
                        bool isFront = Vector3.Dot(normal, viewDir) > 0;

                        long e1 = GetEdge(tris[i], tris[i + 1]);
                        long e2 = GetEdge(tris[i + 1], tris[i + 2]);
                        long e3 = GetEdge(tris[i + 2], tris[i]);

                        if (!edgeIsFront.ContainsKey(e1) || isFront) edgeIsFront[e1] = isFront;
                        if (!edgeIsFront.ContainsKey(e2) || isFront) edgeIsFront[e2] = isFront;
                        if (!edgeIsFront.ContainsKey(e3) || isFront) edgeIsFront[e3] = isFront;
                    }

                    foreach (var kvp in edgeIsFront)
                    {
                        if (kvp.Value != renderingFront) continue;

                        long edge = kvp.Key;
                        int v1idx = (int)(edge >> 32);
                        int v2idx = (int)(edge & 0xFFFFFFFF);

                        Vector3 w1 = finalMatrix.MultiplyPoint3x4(verts[v1idx]);
                        Vector3 w2 = finalMatrix.MultiplyPoint3x4(verts[v2idx]);

                        if (splitMode != 0)
                        {
                            bool side1 = splitPlane.GetSide(w1);
                            bool side2 = splitPlane.GetSide(w2);
                            bool keepPositiveSide = (splitMode == 2);
                            if (keepPositiveSide ? (!side1 && !side2) : (side1 && side2)) continue;
                            if (side1 != side2)
                            {
                                float d1 = splitPlane.GetDistanceToPoint(w1);
                                float d2 = splitPlane.GetDistanceToPoint(w2);
                                float t = Mathf.Abs(d1) / (Mathf.Abs(d1) + Mathf.Abs(d2));
                                if (keepPositiveSide ? !side1 : side1) w1 = Vector3.Lerp(w1, w2, t);
                                else w2 = Vector3.Lerp(w1, w2, t);
                            }
                        }

                        Vector3 seg = w2 - w1;
                        float segLen = seg.magnitude;
                        if (segLen < 0.0001f) continue;

                        Vector3 segDir = seg / segLen;
                        Vector3 right = Vector3.Cross(camForward, segDir).normalized;
                        if (right.sqrMagnitude < 0.0001f) continue;

                        Vector3 mid = (w1 + w2) * 0.5f;
                        float dist = Vector3.Distance(camPos, mid);
                        float halfWidth = Mathf.Max(0.0001f, dist * 2f * Mathf.Tan(halfFovRad) / texSize * 1.5f);

                        Vector3 r = right * halfWidth;

                        GL.Color(hullPassColor);
                        GL.TexCoord2(-1f, 0f); GL.Vertex(w1 - r);
                        GL.TexCoord2(1f, 0f);  GL.Vertex(w1 + r);
                        GL.TexCoord2(1f, 0f);  GL.Vertex(w2 + r);
                        GL.TexCoord2(-1f, 0f); GL.Vertex(w2 - r);
                    }
                }
                GL.End();
            }
        }

        private Texture2D RenderItem(PreviewItem item)
        {
            if (_pru == null) _pru = new PreviewRenderUtility();

            int renderSize = _singlePreviewSizePixels > 0 ? _singlePreviewSizePixels : Mathf.RoundToInt(_previewSize);
            float logicalSize = renderSize / EditorGUIUtility.pixelsPerPoint;
            _pru.BeginPreview(new Rect(0, 0, logicalSize, logicalSize), GUIStyle.none);

            try
            {
                float magnitude = item.bounds.extents.magnitude;
                if (magnitude < 0.1f) magnitude = 0.1f;
                float distance = magnitude * 3.5f;

                Quaternion camRot = Quaternion.Euler(_previewDir.y, _previewDir.x, 0);
                Vector3 camPos = item.bounds.center + camRot * new Vector3(0, 0, -distance);

                _pru.camera.transform.position = camPos;
                _pru.camera.transform.LookAt(item.bounds.center);
                _pru.camera.nearClipPlane = 0.01f;
                _pru.camera.farClipPlane = distance * 2.5f;
                _pru.camera.clearFlags = CameraClearFlags.Nothing;
                _pru.camera.backgroundColor = Color.black;

                _pru.camera.orthographic = false;
                _pru.camera.fieldOfView = 30f;
                _pru.camera.aspect = 1f;

                int tSize = renderSize;
                if (_pru.camera.targetTexture != null) tSize = _pru.camera.targetTexture.width;

                RenderTexture.active = _pru.camera.targetTexture;
                GL.Viewport(new Rect(0, 0, tSize, tSize));
                GL.Clear(true, true, Color.black);

                EnsureWireMaterials();

                if (_wireMaterial != null)
                {
                    GL.PushMatrix();
                    GL.LoadOrtho();
                    _wireMaterial.SetPass(0);

                    int rings = 20;
                    int segments = 40;
                    for (int r = 0; r < rings; r++)
                    {
                        float r1 = (float)r / rings;
                        float r2 = (float)(r + 1) / rings;

                        float a1 = 1.0f - Mathf.Pow(Mathf.Clamp01((r1 - 0.15f) / 0.85f), 2.0f);
                        float a2 = 1.0f - Mathf.Pow(Mathf.Clamp01((r2 - 0.15f) / 0.85f), 2.0f);

                        float floor = 0.15f;
                        a1 = Mathf.Max(a1, floor);
                        a2 = Mathf.Max(a2, floor);

                        Color c1 = new Color(_previewBgColor.r * a1, _previewBgColor.g * a1, _previewBgColor.b * a1, 1);
                        Color c2 = new Color(_previewBgColor.r * a2, _previewBgColor.g * a2, _previewBgColor.b * a2, 1);

                        GL.Begin(GL.QUADS);
                        for (int i = 0; i < segments; i++)
                        {
                            float ang1 = i * Mathf.PI * 2 / segments;
                            float ang2 = (i + 1) * Mathf.PI * 2 / segments;

                            float s1 = Mathf.Sin(ang1); float c1_val = Mathf.Cos(ang1);
                            float s2 = Mathf.Sin(ang2); float c2_val = Mathf.Cos(ang2);

                            float radius1 = r1 * 0.8f;
                            float radius2 = r2 * 0.8f;

                            GL.Color(c1);
                            GL.Vertex3(0.5f + c1_val * radius1, 0.5f + s1 * radius1, 0);
                            GL.Vertex3(0.5f + c2_val * radius1, 0.5f + s2 * radius1, 0);
                            GL.Color(c2);
                            GL.Vertex3(0.5f + c2_val * radius2, 0.5f + s2 * radius2, 0);
                            GL.Vertex3(0.5f + c1_val * radius2, 0.5f + s1 * radius2, 0);
                        }
                        GL.End();
                    }
                    GL.PopMatrix();
                }

                _pru.lights[0].transform.parent = _pru.camera.transform;
                _pru.lights[0].transform.localPosition = Vector3.zero;
                _pru.lights[0].transform.localRotation = Quaternion.Euler(30, 30, 0);
                _pru.lights[0].intensity = 1.0f;

                _pru.lights[1].transform.parent = _pru.camera.transform;
                _pru.lights[1].transform.localPosition = Vector3.zero;
                _pru.lights[1].transform.localRotation = Quaternion.Euler(-30, -30, 0);
                _pru.lights[1].intensity = 0.5f;
                _pru.ambientColor = new Color(0.3f, 0.3f, 0.3f);

                int texSize = renderSize;
                if (_pru.camera.targetTexture != null) texSize = _pru.camera.targetTexture.width;

                for (int j = 0; j < item.renderMeshes.Count; ++j)
                {
                    Mesh mesh = item.renderMeshes[j];
                    if (mesh == null) continue;
                    Matrix4x4 finalMatrix = item.renderMatrices[j];
                    Vector3 scale = j < item.renderScales.Count ? item.renderScales[j] : Vector3.one;
                    Material[] mats = item.renderMaterials[j];
                    int submeshCount = mesh.subMeshCount;
                    int reflectionMask = LeanHullMeshReflection.GetReflectionMask(scale);
                    Mesh drawMesh = reflectionMask != 0 ? LeanHullMeshReflection.GetOrCreateReflectedMesh(mesh, reflectionMask) : mesh;
                    Matrix4x4 drawMatrix = reflectionMask != 0 ? LeanHullMeshReflection.GetRenderSafeMatrix(finalMatrix, scale) : finalMatrix;
                    for (int i = 0; i < mats.Length && i < submeshCount; ++i)
                    {
                        if (mats[i] != null) _pru.DrawMesh(drawMesh, drawMatrix, mats[i], i);
                    }
                }

                _pru.camera.Render();
                RenderTexture.active = _pru.camera.targetTexture;

                GL.PushMatrix();
                try
                {
                    GL.LoadProjectionMatrix(_pru.camera.projectionMatrix);
                    GL.modelview = _pru.camera.worldToCameraMatrix;

                    DrawHulls(item, item.hullMeshes, _hullLineColor, _pru.camera, texSize, 1, item.splitPosition);

                    Color.RGBToHSV(_hullLineColor, out float h, out float s, out float v);
                    Color origColor = Color.HSVToRGB((h + 0.666f) % 1.0f, s, v);
                    DrawHulls(item, item.originalHullMeshes, origColor, _pru.camera, texSize, 2, item.splitPosition);

                    GL.LoadOrtho();
                    _wireMaterial.SetPass(0);
                    GL.Begin(GL.LINES);
                    GL.Color(new Color(1, 1, 1, 0)); GL.Vertex3(item.splitPosition, 0, 0);
                    GL.Color(new Color(1, 1, 1, 1)); GL.Vertex3(item.splitPosition, 0.5f, 0);
                    GL.Color(new Color(1, 1, 1, 1)); GL.Vertex3(item.splitPosition, 0.5f, 0);
                    GL.Color(new Color(1, 1, 1, 0)); GL.Vertex3(item.splitPosition, 1, 0);
                    GL.End();
                }
                finally
                {
                    GL.PopMatrix();
                }

                Texture2D finalTex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
                finalTex.ReadPixels(new Rect(0, 0, texSize, texSize), 0, 0);
                finalTex.Apply();
                RenderTexture.active = null;

                finalTex.filterMode = FilterMode.Point;
                return finalTex;
            }
            finally
            {
                _pru.EndPreview();
            }
        }
    }
}
