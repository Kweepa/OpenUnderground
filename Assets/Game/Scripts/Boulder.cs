using UnityEngine;

public class Boulder : UUObject
{
    public AudioClip breakSound;

    public void Break()
    {
        Utils.PlayClip(breakSound, transform.position);

        LevelLoader.worldObj.Remove(this);
        this.gameObject.SetActive(false);

        EObjectType typeToSpawn = 0;
        float radius = 0.5f;
        switch (type)
        {
        case EObjectType.LargeBoulder:
        case EObjectType.MediumBoulder:
            typeToSpawn = EObjectType.Boulder;
            radius = 0.5f;
            
            ParticleSpawner.SpawnParticle(EParticleType.RockBreak, cachedRenderer.bounds.center);
            break;
        case EObjectType.Boulder:
        case EObjectType.SmallBoulder:
            typeToSpawn = EObjectType.SlingStone;
            radius = 0.2f;
            ParticleSpawner.SpawnParticle(EParticleType.RockBreak, cachedRenderer.bounds.center);
            break;
        }

        if (typeToSpawn > 0)
        {
            for (int i = 0; i < 3; ++i)
            {
                UUObject obj = LevelLoader.CreateObjectOfType(typeToSpawn);
                PositionPossessedObject(obj, radius);
            }
        }
    }
}
