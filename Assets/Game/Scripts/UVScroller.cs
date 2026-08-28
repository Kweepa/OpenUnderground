using UnityEngine;

public class UVScroller : MonoBehaviour
{
    public int materialIndex;
    public float scrollSpeedU;
    public float scrollSpeedV = 0.1f;
    [Tooltip("The name of the texture property in the shader to modify (e.g., '_MainTex').")]
    public string textureName = "_MainTex";

    private Material material;

    private void Start()
    {
        Renderer rend = GetComponent<Renderer>();
        if (materialIndex < rend.materials.Length)
        {
            material = rend.materials[materialIndex];
            if (!material.HasProperty(textureName))
            {
                material = null;
            }
        }
    }

    private void Update()
    {
        if (material != null)
        {
            Vector2 offset = new Vector2(Time.time * scrollSpeedU, Time.time * scrollSpeedV);
            material.SetTextureOffset(textureName, offset);
        }
    }
}
