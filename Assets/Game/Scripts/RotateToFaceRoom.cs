using UnityEngine;
using Random = UnityEngine.Random;

public class RotateToFaceRoom : MonoBehaviour
{
    public float randomYaw = 25.0f;
    
    public void Update()
    {
        UUObject obj = GetComponent<UUObject>();
        if (obj != null && obj.initialTile != null)
        {
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < 4; ++i)
            {
                Vector3 direction = new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back }[i];
                int mask = LayerMasks.EnvironmentAndCeiling;
                if (Physics.Raycast(transform.position + 0.1f * Vector3.up, direction, out RaycastHit hit, 2.0f, LayerMasks.EnvironmentOnly))
                {
                    normal += hit.normal;
                }
            }

            if (normal != Vector3.zero)
            {
                float yaw = Mathf.Rad2Deg * Mathf.Atan2(normal.x, normal.z) + Random.Range(-randomYaw, randomYaw);
                transform.SetPositionAndRotation(transform.position, Quaternion.AngleAxis(yaw, Vector3.up));
            }

        }
        Destroy(this); // removes component once done
    }
}
