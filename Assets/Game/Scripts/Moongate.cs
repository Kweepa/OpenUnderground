using UnityEngine;

public class Moongate : UUObject
{
    private Color GetColor()
    {
        switch (special)
        {
        case 692:
            return Color.red;
        case 708:
            return Color.blue;
        default:
            return Color.green;
        }
    }

    private void CreateGateGeometry()
    {
        if (cachedRenderer != null)
        {
            cachedRenderer.material.SetColor("_Color", GetColor());

            MeshFilter meshFilter = cachedRenderer.gameObject.AddComponent<MeshFilter>();
            if (meshFilter != null)
            {
                float w = 0.5f;
                float h = 2.4f;
                float d = 0.1f;
                Vector3[] verts =
                {
                    // front
                    new(-w, 0, -d),
                    new(-w, h, -d),
                    new(w, h, -d),
                    new(w, 0, -d),
                    // back
                    new(-w, 0, d),
                    new(-w, h, d),
                    new(w, h, d),
                    new(w, 0, d),
                    // left
                    new(-w, 0, -d),
                    new(-w, h, -d),
                    new(-w, h, d),
                    new(-w, 0, d),
                    // right
                    new(w, 0, -d),
                    new(w, h, -d),
                    new(w, h, d),
                    new(w, 0, d),
                    // top
                    new(-w, h, -d),
                    new(-w, h, d),
                    new(w, h, d),
                    new(w, h, -d)
                };
                Vector2[] uv =
                {
                    // front
                    new(0, 0),
                    new(0, 1),
                    new(1, 1),
                    new(1, 0),
                    // back
                    new(0, 0),
                    new(0, 1),
                    new(1, 1),
                    new(1, 0),
                    // left
                    new(0, 0),
                    new(0, 1),
                    new(0, 1),
                    new(0, 0),
                    // right
                    new(1, 0),
                    new(1, 1),
                    new(1, 1),
                    new(1, 0),
                    // top
                    new(0, 1),
                    new(0, 1),
                    new(1, 1),
                    new(1, 1),
                };
                int[] triangles =
                {
                    // front
                    0, 1, 2, 0, 2, 3,
                    // back
                    4, 6, 5, 4, 7, 6,
                    // left
                    8, 10, 9, 8, 11, 10,
                    // right
                    12, 13, 14, 12, 14, 15,
                    // top
                    16, 17, 18, 16, 18, 19
                };
                meshFilter.mesh = new Mesh();
                meshFilter.mesh.SetVertices(verts);
                meshFilter.mesh.SetUVs(0, uv);
                meshFilter.mesh.SetTriangles(triangles, 0);
                meshFilter.mesh.RecalculateNormals();
                meshFilter.mesh.UploadMeshData(true);
            }
        }
    }

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);
        CreateGateGeometry();
    }
}
