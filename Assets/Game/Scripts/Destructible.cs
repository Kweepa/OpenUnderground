using UnityEngine;

public class Destructible : MonoBehaviour
{
    public bool breakImmediately;
    public float chanceForChunkToSurvive = 0.5f;

    public void Start()
    {
        if (breakImmediately)
        {
            BreakApart();
        }
    }

    public void BreakApart()
    {
        // Get all child objects that have a collider
        foreach (Collider childCollider in GetComponentsInChildren<Collider>())
        {
            if (Random.value < chanceForChunkToSurvive)
            {
                // Detach the child from this object, making it independent
                childCollider.transform.SetParent(null); 

                LevelObject piece = childCollider.gameObject.AddComponent<DestructiblePiece>();
                piece.temporary = true;
                LevelLoader.worldObj.AddLast(piece);

                // Add a Rigidbody to the newly independent piece
                Rigidbody rb = childCollider.gameObject.AddComponent<Rigidbody>();

                // Optional: Add mass, drag, or explosive force
                rb.mass = 1f;
            }
        }

        // The original parent object is now empty, so destroy it
        Destroy(gameObject);
    }
}
