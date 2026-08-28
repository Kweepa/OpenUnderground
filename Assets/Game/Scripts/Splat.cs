using UnityEngine;
using UnityEngine.Rendering;

public enum SplatType
{
    Blood,
    Fire1,
    Fire2,
    Fire3,
    Splash,
    Magic,
    Spark,
    Lightning
}

public class Splat : MonoBehaviour
{
    public SplatType splatType;
    public float animSpeed = 8.0f;

    private float lifeTime;
    private int texIndex;

    private MeshRenderer cachedRenderer;

    private static readonly int[] startIndex = { 0, 21, 26, 31, 36, 40, 45, 50 };
    private static readonly int[] numFrames = { 5, 5, 5, 5, 4, 5, 5, 4 };

    private Texture2D GetTex()
    {
        int animoIndex = startIndex[(int)splatType] + texIndex;
        return DataLoader.sDataLoader.animoTex[animoIndex];
    }
    
    public void Start()
    {
        cachedRenderer = gameObject.GetComponentInChildren<MeshRenderer>();

        Shader spriteShader = Shader.Find( "Transparent/Diffuse" );
        cachedRenderer.material = new Material(spriteShader) { mainTexture = GetTex() };
        
        cachedRenderer.lightProbeUsage = LightProbeUsage.Off;
        
        // Add randomness to playback speed
        animSpeed *= Random.Range(0.9f, 1.2f);
        
        // Add randomness to size
        transform.localScale *= Random.Range(0.8f, 1.1f);
    }

    public void Update()
    {
        lifeTime += animSpeed * Time.deltaTime;
        int newTexIndex = (int) lifeTime;
        if (newTexIndex >= numFrames[(int)splatType])
        {
            Destroy(gameObject);
        }
        else if (texIndex != newTexIndex)
        {
            texIndex = newTexIndex;
            cachedRenderer.material.mainTexture = GetTex();
        }
    }
}
