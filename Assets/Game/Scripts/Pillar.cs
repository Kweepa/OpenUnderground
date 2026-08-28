using UnityEngine;

public class Pillar : UUObject
{
    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);

        float xw = LevelLoader.xzScale / 8.0f;

        gameObject.transform.position = initialTile.GetGridCenter() + new Vector3(xw / 2 + x * xw - LevelLoader.xzScale / 2, 0.0f, xw / 2 + y * xw - LevelLoader.xzScale / 2); 

        gameObject.layer = LayerMask.NameToLayer("Environment");

        MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
        BoxCollider boxCollider = gameObject.GetComponent<BoxCollider>();
        
        float h = (16 - initialTile.floorHeight) * LevelLoader.yScale;
        float w = xw / 2;

        float v = 0.5f * (16 - initialTile.floorHeight);

        // need separated faces for the normals
        Vector3[] vertices =
        {
            new(-w, 0, -w),
            new(-w, h, -w),
            new(w, 0, -w),
            new(w, h, -w),

            new(w, 0, -w),
            new(w, h, -w),
            new(w, 0, w),
            new(w, h, w),
            
            new(w, 0, w),
            new(w, h, w),
            new(-w, 0, w),
            new(-w, h, w),
            
            new(-w, 0, w),
            new(-w, h, w),
            new(-w, 0, -w),
            new(-w, h, -w),
            
            // top, for roaming sight
            new(-w, h, -w),
            new(-w, h, w),
            new(w, h, -w),
            new(w, h, w),
        };

        Vector2[] uvs =
        {
            new(0, 0),
            new(0, v),
            new(1, 0),
            new(1, v),

            new(0, 0),
            new(0, v),
            new(1, 0),
            new(1, v),

            new(0, 0),
            new(0, v),
            new(1, 0),
            new(1, v),

            new(0, 0),
            new(0, v),
            new(1, 0),
            new(1, v),
            
            new(0, 0),
            new(0, v),
            new(1, 0),
            new(1, v)
        };

        int[] tris =
        {
            0, 1, 2, 1, 3, 2,
            4, 5, 6, 5, 7, 6,
            8, 9, 10, 9, 11, 10,
            12, 13, 14, 13, 15, 14,
            16, 17, 18, 17, 19, 18
        };
        
        Material[] mats = new Material[1];

        Material mat = LevelLoader.sLevelLoader.GetTmObjectMaterial(option);
        mat.mainTexture.wrapMode = TextureWrapMode.Repeat;
        mats[0] = mat;

        Mesh mesh = new Mesh { subMeshCount = 1, vertices = vertices, uv = uvs };
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.Optimize();

        meshFilter.mesh = mesh;
        boxCollider.size = new Vector3(xw, h, xw);
        boxCollider.center = new Vector3(0, h / 2.0f, 0);
        meshRenderer.lightProbeUsage = 0;
        meshRenderer.materials = mats;
    }

    public override string GetUnderCursorName()
    {
        return null;
    }
}
