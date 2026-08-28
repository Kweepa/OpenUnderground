// BoneWeightVisualizer.cs
//
// Place this script in a folder named 'Editor' in your Unity project.
// Access it from the Unity menu: Tools > Bone Weight Visualizer.
// [VERSION 18 - Final 'Select Bone' Logic Fix]

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;

public class BoneWeightVisualizer : EditorWindow
{
    // Model Data
    private GameObject targetObject;
    private SkinnedMeshRenderer[] allRenderers;
    private int selectedRendererIndex = 0;
    private string[] rendererNames;
    
    private SkinnedMeshRenderer currentRenderer;
    private Mesh sharedMesh;
    private Transform[] bones;
    private Dictionary<int, List<int>> boneHierarchy;
    private HashSet<int> foldoutStates = new HashSet<int>();

    // Preview Controls
    private PreviewRenderUtility previewRenderUtility;
    private Material previewMaterial;
    private Material wireframeMaterial;
    private Material gizmoMaterial;

    private Vector2 rotation = new Vector2(120, 20);
    private Vector3 panOffset = Vector3.zero;
    private float zoom = 1.0f;
    private Vector2 drag;
    
    private readonly Vector2 initialRotation = new Vector2(120, 20);

    // Selection States
    private int selectedVertexIndex = -1;
    private List<KeyValuePair<int, float>> selectedVertexWeights = new List<KeyValuePair<int, float>>();
    private int selectedBoneIndex = -1;
    private List<int> verticesAffectedByBone = new List<int>();

    // UI State
    private Vector2 boneListScrollPos;
    private GUIStyle boneLabelStyle;
    private GUIStyle selectedBoneLabelStyle;
    private GUIStyle hoverBoneLabelStyle;
    private Texture2D hoverBackground;
    private Texture2D selectedBackground;
    private bool stylesInitialized = false;

    [MenuItem("Tools/Bone Weight Visualizer")]
    public static void ShowWindow()
    {
        GetWindow<BoneWeightVisualizer>("Bone Weight Visualizer");
    }

    #region Setup and Teardown

    void OnEnable()
    {
        previewRenderUtility = new PreviewRenderUtility();
        previewRenderUtility.camera.fieldOfView = 30.0f;
        previewRenderUtility.camera.nearClipPlane = 0.01f;
        previewRenderUtility.camera.farClipPlane = 1000.0f;

        SetupMaterials();
        SceneView.duringSceneGui += OnSceneGUI;
    }

    void OnDisable()
    {
        if (previewRenderUtility != null) previewRenderUtility.Cleanup();
        DestroyImmediate(previewMaterial);
        DestroyImmediate(wireframeMaterial);
        DestroyImmediate(gizmoMaterial);
        DestroyImmediate(hoverBackground);
        DestroyImmediate(selectedBackground);
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void SetupMaterials()
    {
        Shader standardShader = Shader.Find("Standard");
        if (standardShader)
        {
            previewMaterial = new Material(standardShader) { color = new Color(0.7f, 0.7f, 0.7f) };
        }

        Shader wireframeShader = Shader.Find("Custom/Wireframe");
        if (wireframeShader)
        {
            wireframeMaterial = new Material(wireframeShader);
        }

        gizmoMaterial = new Material(Shader.Find("Hidden/Internal-Colored"))
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        gizmoMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        gizmoMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        gizmoMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        gizmoMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        gizmoMaterial.SetInt("_ZWrite", 0);
    }

    private void SetupGUIStyles()
    {
        boneLabelStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(2, 2, 2, 2), alignment = TextAnchor.MiddleLeft };
        
        hoverBackground = MakeTex(1, 1, new Color(0.5f, 0.5f, 0.5f, 0.15f));
        selectedBackground = MakeTex(1, 1, new Color(0.0f, 0.7f, 0.7f, 0.25f));

        hoverBoneLabelStyle = new GUIStyle(boneLabelStyle);
        hoverBoneLabelStyle.normal.background = hoverBackground;

        selectedBoneLabelStyle = new GUIStyle(boneLabelStyle);
        selectedBoneLabelStyle.normal.background = selectedBackground;
        selectedBoneLabelStyle.normal.textColor = Color.cyan;
        selectedBoneLabelStyle.fontStyle = FontStyle.Bold;

        stylesInitialized = true;
    }
    
    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; ++i) pix[i] = col;
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }

    #endregion

    #region GUI Drawing

    void OnGUI()
    {
        if (!stylesInitialized)
        {
            SetupGUIStyles();
        }

        DrawToolbar();
        
        if (targetObject == null || currentRenderer == null)
        {
            EditorGUILayout.HelpBox("Please assign a GameObject with at least one SkinnedMeshRenderer.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        DrawLeftPanel();
        DrawRightPanel();
        EditorGUILayout.EndHorizontal();

        Repaint();
    }
    
    void OnSceneGUI(SceneView sceneView) { }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GameObject newTargetObject = (GameObject)EditorGUILayout.ObjectField("Skinned Model", targetObject, typeof(GameObject), false, GUILayout.Width(350));
        if (newTargetObject != targetObject) SetTargetObject(newTargetObject);
        
        if (allRenderers != null && allRenderers.Length > 1)
        {
            int newRendererIndex = EditorGUILayout.Popup(selectedRendererIndex, rendererNames, EditorStyles.toolbarPopup, GUILayout.Width(200));
            if (newRendererIndex != selectedRendererIndex)
            {
                selectedRendererIndex = newRendererIndex;
                SetCurrentRenderer(newRendererIndex);
            }
        }
        GUILayout.FlexibleSpace();
        
        if (GUILayout.Button("Reset View", EditorStyles.toolbarButton))
        {
            ResetView();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(300), GUILayout.ExpandHeight(true));
        
        GUILayout.Label("Bone Hierarchy", EditorStyles.boldLabel);
        boneListScrollPos = EditorGUILayout.BeginScrollView(boneListScrollPos, "box");

        if (bones != null && boneHierarchy != null)
        {
            var rootBoneQuery = boneHierarchy.Where(kvp =>
                bones[kvp.Key] != null &&
                (bones[kvp.Key].parent == null || !bones.Contains(bones[kvp.Key].parent))
            );
            foreach (var rootBoneIndex in rootBoneQuery)
            {
                DrawBoneRecursive(rootBoneIndex.Key, 0);
            }
        }
        EditorGUILayout.EndScrollView();
        
        GUILayout.Label("Selected Vertex Info", EditorStyles.boldLabel);
        if (selectedVertexIndex != -1)
        {
            EditorGUILayout.LabelField("Vertex Index:", selectedVertexIndex.ToString());
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Influencing Bones & Weights:");

            foreach (var pair in selectedVertexWeights)
            {
                DisplayBoneWeightInfo(pair.Key, pair.Value);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Click on a vertex in the preview to see its weights.", MessageType.None);
        }
        EditorGUILayout.EndVertical();
    }
    
    private void DisplayBoneWeightInfo(int boneIndex, float weight)
    {
        string boneName = (boneIndex < bones.Length && bones[boneIndex] != null) ? bones[boneIndex].name : "N/A";
        EditorGUILayout.LabelField($" - {boneName}", $"{weight:F3}");
    }

    private void DrawBoneRecursive(int boneIndex, int indentLevel)
    {
        if (boneIndex < 0 || boneIndex >= bones.Length || bones[boneIndex] == null) return;

        Rect lineRect = GUILayoutUtility.GetRect(16f, 18f, GUILayout.ExpandWidth(true));
        lineRect.x += (indentLevel * 15);
        lineRect.width -= (indentLevel * 15);

        bool hasChildren = boneHierarchy.ContainsKey(boneIndex) && boneHierarchy[boneIndex].Count > 0;
        
        Rect foldoutRect = new Rect(lineRect.x, lineRect.y, 15, lineRect.height);
        if (hasChildren)
        {
            bool isExpanded = foldoutStates.Contains(boneIndex);
            bool newExpandedState = EditorGUI.Foldout(foldoutRect, isExpanded, GUIContent.none);
            if (newExpandedState != isExpanded)
            {
                if (newExpandedState) foldoutStates.Add(boneIndex);
                else foldoutStates.Remove(boneIndex);
            }
        }

        Rect labelRect = new Rect(lineRect.x + 15, lineRect.y, lineRect.width - 15, lineRect.height);
        bool isSelected = (selectedBoneIndex == boneIndex);
        GUIStyle currentStyle;

        if (isSelected)
        {
            currentStyle = selectedBoneLabelStyle;
        }
        else
        {
            bool isHovering = Event.current.type == EventType.Repaint && labelRect.Contains(Event.current.mousePosition);
            currentStyle = isHovering ? hoverBoneLabelStyle : boneLabelStyle;
        }
        
        Color originalColor = GUI.color;
        
        bool isInfluencing = selectedVertexIndex != -1 && selectedVertexWeights.Any(pair => pair.Key == boneIndex);
                              
        if (isInfluencing && !isSelected) GUI.color = Color.yellow;

        if (GUI.Button(labelRect, bones[boneIndex].name, currentStyle))
        {
            SelectBone(boneIndex);
        }
        
        GUI.color = originalColor;
        
        if (hasChildren && foldoutStates.Contains(boneIndex))
        {
            foreach (var childIndex in boneHierarchy[boneIndex])
            {
                DrawBoneRecursive(childIndex, indentLevel + 1);
            }
        }
    }

    private void DrawRightPanel()
    {
        Rect previewRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        if (currentRenderer != null && previewMaterial != null)
        {
            previewRenderUtility.BeginPreview(previewRect, GUIStyle.none);
            HandlePreviewInput(previewRect);
            
            Bounds bounds = sharedMesh.bounds;
            float magnitude = bounds.size.magnitude;
            
            Matrix4x4 modelMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);
            
            Quaternion camRotation = Quaternion.Euler(rotation.y, rotation.x, 0);
            Vector3 camPosition = bounds.center + panOffset + (camRotation * Vector3.forward * -magnitude * zoom);
            previewRenderUtility.camera.transform.SetPositionAndRotation(camPosition, camRotation);
            
            if(previewMaterial) previewRenderUtility.DrawMesh(sharedMesh, modelMatrix, previewMaterial, 0);
            if(wireframeMaterial) previewRenderUtility.DrawMesh(sharedMesh, modelMatrix, wireframeMaterial, 0);
            
            previewRenderUtility.camera.Render();

            GL.PushMatrix();
            GL.LoadProjectionMatrix(previewRenderUtility.camera.projectionMatrix);
            GL.modelview = previewRenderUtility.camera.worldToCameraMatrix * modelMatrix;
            
            DrawGizmos(magnitude);
            
            GL.PopMatrix();
            
            Texture result = previewRenderUtility.EndPreview();
            GUI.DrawTexture(previewRect, result);
        }
    }

    #endregion

    #region Data Handling & Logic

    private void SetTargetObject(GameObject obj)
    {
        targetObject = obj;
        allRenderers = null;
        rendererNames = null;

        if (targetObject != null)
        {
            allRenderers = targetObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (allRenderers.Length > 0)
            {
                rendererNames = allRenderers.Select(r => r.name).ToArray();
                SetCurrentRenderer(0);
            }
            else SetCurrentRenderer(-1);
        }
        else SetCurrentRenderer(-1);
    }
    
    private void SetCurrentRenderer(int index)
    {
        selectedVertexIndex = -1;
        selectedBoneIndex = -1;
        verticesAffectedByBone.Clear();
        selectedVertexWeights.Clear();
        ResetView();

        if (index >= 0 && index < allRenderers.Length)
        {
            currentRenderer = allRenderers[index];
            sharedMesh = currentRenderer.sharedMesh;
            bones = currentRenderer.bones;
            BuildBoneHierarchy();
            ExpandAllBones();
        }
        else
        {
            currentRenderer = null;
            sharedMesh = null;
            bones = null;
            boneHierarchy = null;
            foldoutStates.Clear();
        }
    }

    private void BuildBoneHierarchy()
    {
        boneHierarchy = new Dictionary<int, List<int>>();
        var boneIndexMap = new Dictionary<Transform, int>();
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] != null)
            {
                boneIndexMap[bones[i]] = i;
                boneHierarchy[i] = new List<int>();
            }
        }

        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            Transform parent = bones[i].parent;
            if (parent != null && boneIndexMap.TryGetValue(parent, out int parentIndex) && bones[parentIndex] != null)
            {
                boneHierarchy[parentIndex].Add(i);
            }
        }
    }
    
    private void ExpandAllBones()
    {
        foldoutStates.Clear();
        if (bones != null)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                if (boneHierarchy.ContainsKey(i) && boneHierarchy[i].Count > 0)
                {
                    foldoutStates.Add(i);
                }
            }
        }
    }

    private void SelectVertex(Vector2 mousePosition, Rect previewRect)
    {
        Vector2 localMousePos = mousePosition - previewRect.position;
        Vector2 viewportPoint = new Vector2(localMousePos.x / previewRect.width, 1.0f - (localMousePos.y / previewRect.height));

        if (viewportPoint.x < 0 || viewportPoint.x > 1 || viewportPoint.y < 0 || viewportPoint.y > 1)
        {
            return;
        }
        
        Ray ray = previewRenderUtility.camera.ViewportPointToRay(viewportPoint);
        
        Vector3[] vertices = sharedMesh.vertices;
        int[] triangles = sharedMesh.triangles;
        float closestDistance = float.MaxValue;
        int hitVertexIndex = -1;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];

            Vector3 v0 = vertices[i0];
            Vector3 v1 = vertices[i1];
            Vector3 v2 = vertices[i2];
            
            if (RayIntersectsTriangle(ray, v0, v1, v2, out float distance, out Vector3 barycentricCoord))
            {
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    if (barycentricCoord.x > barycentricCoord.y && barycentricCoord.x > barycentricCoord.z)
                    {
                        hitVertexIndex = i0;
                    }
                    else if (barycentricCoord.y > barycentricCoord.z)
                    {
                        hitVertexIndex = i1;
                    }
                    else
                    {
                        hitVertexIndex = i2;
                    }
                }
            }
        }

        if (hitVertexIndex != -1)
        {
            selectedVertexIndex = hitVertexIndex;
            GetVertexWeights(hitVertexIndex); 
            
            selectedBoneIndex = -1;
            verticesAffectedByBone.Clear();
        }
    }
    
    private void GetVertexWeights(int vertexIndex)
    {
        selectedVertexWeights.Clear();
        
        var allBoneWeights = sharedMesh.GetAllBoneWeights();
        var bonesPerVertex = sharedMesh.GetBonesPerVertex();

        if (!allBoneWeights.IsCreated || !bonesPerVertex.IsCreated || bonesPerVertex.Length != sharedMesh.vertexCount)
        {
            // Fallback to the old system if the new one has no data or is invalid
            var boneWeight = sharedMesh.boneWeights[vertexIndex];
            if(boneWeight.weight0 > 0) selectedVertexWeights.Add(new KeyValuePair<int, float>(boneWeight.boneIndex0, boneWeight.weight0));
            if(boneWeight.weight1 > 0) selectedVertexWeights.Add(new KeyValuePair<int, float>(boneWeight.boneIndex1, boneWeight.weight1));
            if(boneWeight.weight2 > 0) selectedVertexWeights.Add(new KeyValuePair<int, float>(boneWeight.boneIndex2, boneWeight.weight2));
            if(boneWeight.weight3 > 0) selectedVertexWeights.Add(new KeyValuePair<int, float>(boneWeight.boneIndex3, boneWeight.weight3));
            if(allBoneWeights.IsCreated) allBoneWeights.Dispose();
            if(bonesPerVertex.IsCreated) bonesPerVertex.Dispose();
            return;
        }

        int weightStartIndex = 0;
        for (int i = 0; i < vertexIndex; i++)
        {
            weightStartIndex += bonesPerVertex[i];
        }

        int numBonesForThisVertex = bonesPerVertex[vertexIndex];
        for (int i = 0; i < numBonesForThisVertex; i++)
        {
            BoneWeight1 weight = allBoneWeights[weightStartIndex + i];
            if (weight.weight > 0)
            {
                selectedVertexWeights.Add(new KeyValuePair<int, float>(weight.boneIndex, weight.weight));
            }
        }
        
        allBoneWeights.Dispose();
        bonesPerVertex.Dispose();
    }
    
    private bool RayIntersectsTriangle(Ray ray, Vector3 vertex0, Vector3 vertex1, Vector3 vertex2, out float distance, out Vector3 barycentricCoord)
    {
        barycentricCoord = Vector3.zero;
        distance = 0f;
        const float Epsilon = 1e-6f;

        Vector3 edge1 = vertex1 - vertex0;
        Vector3 edge2 = vertex2 - vertex0;
        Vector3 h = Vector3.Cross(ray.direction, edge2);
        float a = Vector3.Dot(edge1, h);

        if (a > -Epsilon && a < Epsilon)
            return false;

        float f = 1.0f / a;
        Vector3 s = ray.origin - vertex0;
        float u = f * Vector3.Dot(s, h);

        if (u < 0.0f || u > 1.0f)
            return false;

        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(ray.direction, q);

        if (v < 0.0f || u + v > 1.0f)
            return false;

        float t = f * Vector3.Dot(edge2, q);

        if (t > Epsilon)
        {
            distance = t;
            barycentricCoord.x = 1 - u - v;
            barycentricCoord.y = u;
            barycentricCoord.z = v;
            return true;
        }

        return false;
    }

    private void SelectBone(int boneIndex)
    {
        selectedBoneIndex = boneIndex;
        verticesAffectedByBone.Clear();
        var allBoneWeights = sharedMesh.GetAllBoneWeights();
        var bonesPerVertex = sharedMesh.GetBonesPerVertex();

        // --- DEFINITIVE FIX for 'Select Bone' logic ---
        if (!allBoneWeights.IsCreated || !bonesPerVertex.IsCreated || bonesPerVertex.Length != sharedMesh.vertexCount)
        {
            // Fallback for meshes with only 4 bones
            var legacyWeights = sharedMesh.boneWeights;
            for (int i = 0; i < legacyWeights.Length; i++)
            {
                 if ((legacyWeights[i].boneIndex0 == boneIndex && legacyWeights[i].weight0 > 0) ||
                     (legacyWeights[i].boneIndex1 == boneIndex && legacyWeights[i].weight1 > 0) ||
                     (legacyWeights[i].boneIndex2 == boneIndex && legacyWeights[i].weight2 > 0) ||
                     (legacyWeights[i].boneIndex3 == boneIndex && legacyWeights[i].weight3 > 0))
                 {
                     verticesAffectedByBone.Add(i);
                 }
            }
        }
        else
        {
            // The correct way to iterate through all weights for all vertices
            int weightIndexOffset = 0;
            for (int vertexIndex = 0; vertexIndex < bonesPerVertex.Length; vertexIndex++)
            {
                int numBonesForThisVertex = bonesPerVertex[vertexIndex];
                for (int i = 0; i < numBonesForThisVertex; i++)
                {
                    if (allBoneWeights[weightIndexOffset + i].boneIndex == boneIndex && allBoneWeights[weightIndexOffset + i].weight > 0)
                    {
                        verticesAffectedByBone.Add(vertexIndex);
                        break; // Found it for this vertex, move to the next one
                    }
                }
                weightIndexOffset += numBonesForThisVertex; // Move offset to the start of the next vertex's weights
            }
        }
        
        if (allBoneWeights.IsCreated) allBoneWeights.Dispose();
        if (bonesPerVertex.IsCreated) bonesPerVertex.Dispose();
        
        selectedVertexIndex = -1;
        selectedVertexWeights.Clear();
    }

    #endregion

    #region Interaction & Gizmos

    private void HandlePreviewInput(Rect previewRect)
    {
        Event e = Event.current;
        if (previewRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.ScrollWheel)
            {
                zoom *= (1.0f - e.delta.y * 0.05f);
                zoom = Mathf.Max(zoom, 0.1f);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && (e.button == 2 || (e.button == 1)))
            {
                float panSpeed = sharedMesh.bounds.size.magnitude * 0.001f;
                panOffset -= previewRenderUtility.camera.transform.right * e.delta.x * panSpeed;
                panOffset += previewRenderUtility.camera.transform.up * e.delta.y * panSpeed;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && e.alt)
            {
                drag = e.delta;
                rotation.x += drag.x * 0.5f;
                rotation.y += drag.y * 0.5f;
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                SelectVertex(e.mousePosition, previewRect);
                e.Use();
            }
        }
    }

    private void DrawGizmos(float modelMagnitude)
    {
        gizmoMaterial.SetPass(0);
        GL.Begin(GL.QUADS);
        Vector3[] vertices = sharedMesh.vertices;
        
        if (selectedVertexIndex != -1)
        {
            GL.Color(Color.red);
            float size = modelMagnitude * 0.0025f;
            DrawBillboardQuad(vertices[selectedVertexIndex], size);
        }

        if (selectedBoneIndex != -1)
        {
            GL.Color(Color.green);
            float size = modelMagnitude * 0.00125f;
            foreach (int vertIndex in verticesAffectedByBone)
            {
                DrawBillboardQuad(vertices[vertIndex], size);
            }
        }
        GL.End();
    }
    
    void DrawBillboardQuad(Vector3 center, float size)
    {
        Vector3 up = previewRenderUtility.camera.transform.up * size;
        Vector3 right = previewRenderUtility.camera.transform.right * size;
        
        GL.Vertex(center - right - up);
        GL.Vertex(center + right - up);
        GL.Vertex(center + right + up);
        GL.Vertex(center - right + up);
    }
    
    void ResetView()
    {
        rotation = initialRotation;
        panOffset = Vector3.zero;
        zoom = 1.0f;
    }

    #endregion
}
