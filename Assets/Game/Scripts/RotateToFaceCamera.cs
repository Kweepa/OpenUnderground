using UnityEngine;

public class RotateToFaceCamera : MonoBehaviour
{
    public bool inXZPlane;
    public float extraYaw;
    
    void Update()
    {
        if (inXZPlane)
        {
            Vector3 off = PlayerObject.Player.mainCamera.transform.position - transform.position;
            off.y = 0.0f;
            transform.rotation = Quaternion.LookRotation(off) * Quaternion.Euler(0, extraYaw, 0);
        }
        else
        {
            // used by the sprites
            transform.rotation = PlayerObject.Player.mainCamera.transform.rotation;
        }
    }
}
