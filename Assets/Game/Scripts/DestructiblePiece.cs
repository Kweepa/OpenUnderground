using System;
using UnityEngine;
using Random = UnityEngine.Random;

public class DestructiblePiece : LevelObject
{
    private float deathTime;

    public void OnEnable()
    {
        temporary = true;
    }

    public void OnBecameInvisible()
    {
        if (deathTime > 7.0f)
        {
            LevelLoader.GetLevel().worldObj.Remove(this);
            Destroy(gameObject);
        }
    }

    public void Update()
    {
        deathTime += Time.deltaTime;
    }
}
