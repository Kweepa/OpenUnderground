using UnityEngine;

public class OrbRock : UUObject
{
    public AudioClip orbBreakSound;

    private bool TryBreakCrystalBall(Transform t)
    {
        if (LevelLoader.sLevelLoader.loadedLevel == 7)
        {
            UUObject orbCandidate = t.root.GetComponent<UUObject>();
            if (orbCandidate != null && orbCandidate.objectIndex == 820)
            {
                CrystalBall crystalBall = orbCandidate.GetComponent<CrystalBall>();
                
                // Disable particle effect hierarchy if it exists
                if (crystalBall != null && crystalBall.particleEffectHierarchy != null)
                {
                    crystalBall.particleEffectHierarchy.SetActive(false);
                }
                
                // Spawn explosion prefab if it exists
                if (crystalBall != null && crystalBall.explosionPrefab != null)
                {
                    Instantiate(crystalBall.explosionPrefab, orbCandidate.transform.position, Quaternion.identity);
                }
                
                orbCandidate.gameObject.SetActive(false);
                LevelLoader.worldObj.Remove(this);
                
                Messages.Add(1, 133); // the orb is destroyed!

                Utils.PlayClip(orbBreakSound, transform.position);
                
                for (int i = 0; i < 2; ++i)
                {
                    UUObject obj = LevelLoader.CreateObjectOfType(type);
                    obj.PostLoadInitialize();
                    obj.WorldInitialize(transform.position + 0.1f * Random.insideUnitSphere);
                }
                
                // Breaking the orb takes half Tyball's health.
                foreach (Collider col in Physics.OverlapSphere(transform.position, 20.0f, 1 << LayerMask.NameToLayer("Characters")))
                {
                    Critter critter = col.transform.root.GetComponent<Critter>();
                    if (critter != null && critter.type == EObjectType.Tyball)
                    {
                        if (critter.hp > 0)
                        {
                            // round up
                            int damage = (critter.originalHp + 1) / 2;
                            // force a flinch
                            critter.TryDamage(damage, Skills.ESkillTestResult.CriticalSuccess);
                            // also reduce damage and defence for 24 hours
                            critter.TryCurse(24 * 60 * 60);
                        }
                        break;
                    }
                }

                return true;
            }
        }
        return false;
    }

    public override EEquipAction Equip()
    {
        float maxDist = Interaction.sInt.GetInteractionDistance();
        int layerMask = 1 << LayerMask.NameToLayer("Objects");
        if (Physics.Raycast(PlayerObject.Player.mainCamera.transform.position, PlayerObject.Player.mainCamera.transform.forward, out RaycastHit hit, maxDist, layerMask))
        {
            if (TryBreakCrystalBall(hit.collider.transform))
            {
                return EEquipAction.Use;
            }
        }
        return EEquipAction.Nothing;
    }

    public override void OnCollisionEnter(Collision collision)
    {
        base.OnCollisionEnter(collision);
        TryBreakCrystalBall(collision.transform);
    }
}
