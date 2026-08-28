using UnityEngine;

public class Splash : UUObject
{
    public GameObject splash;
    public AudioClip clip;
    public float minTimeBetween = 0.5f;
    public float maxTimeBetween = 1.0f;

    private ParticleSystem system;
    private float timeToNext;
    
    public void Start()
    {
        if (splash != null)
        {
            system = splash.GetComponentInChildren<ParticleSystem>();
        }
    }

    public override void Update()
    {
        base.Update();

        timeToNext -= Time.deltaTime;
        if (timeToNext < 0.0f && system != null)
        {
            system.Play(true);
            timeToNext = Random.Range(minTimeBetween, maxTimeBetween);
            if (clip != null)
            {
                Vector3 dir = transform.position - PlayerObject.Player.mainCamera.transform.position;
                float volume = 1.0f;
                if (Physics.Raycast(PlayerObject.Player.mainCamera.transform.position, dir.normalized, dir.magnitude - 1.0f, LayerMasks.EnvironmentAndCeiling))
                {
                    volume = 0.5f;
                }
                Utils.PlayClipOccluded(clip, transform.position, volume);
            }
        }
    }
}
