using UnityEngine;
using UnityEngine.InputSystem;

public class RumbleManager : MonoBehaviour
{
    private float rumbleTime;

    public void Rumble(float low, float high, float time)
    {
        Gamepad.current?.SetMotorSpeeds(low, high);
        rumbleTime = time;
    }

    public void Stop()
    {
        Gamepad.current?.SetMotorSpeeds(0.0f, 0.0f);
    }

    private void Update()
    {
        if (rumbleTime > 0.0f)
        {
            rumbleTime -= Time.unscaledDeltaTime;
            if (rumbleTime <= 0.0f)
            {
                Stop();
            }
        }
    }
}
