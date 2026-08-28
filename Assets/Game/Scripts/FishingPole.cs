using UnityEngine;

public class FishingPole : UUObject
{
    public float fishingDistance = 5.0f;
    public AudioClip fishFlop;

    private float timeOfLastCatch;

    public override string GetUseText()
    {
        return "Fish";
    }
    
    public override EEquipAction Equip()
    {
        // check the player is on dry land and pointing at some water close by
        bool goodSpot = false;
        if (!PlayerObject.Player.isInWater)
        {
            if (Physics.Raycast(PlayerObject.Player.mainCamera.transform.position, PlayerObject.Player.mainCamera.transform.forward, out RaycastHit hit, fishingDistance,
                    LayerMasks.EnvironmentAndCeiling))
            {
                Tile t = LevelLoader.GetTile((int)(hit.point.x / Tile.xzScale), (int)(hit.point.z / Tile.xzScale));
                if (t.GetFloorTerrain() == ETerrainType.Water && t.type == 1 && Mathf.Abs(t.GetFloorY(hit.point.x, hit.point.z) - hit.point.y) < 0.1f)
                {
                    goodSpot = true;
                }
            }
        }

        if (!goodSpot)
        {
            Messages.Add(1, 101);
        }
        else
        {
            Skills.ESkillTestResult skillResult = Skills.GetResult(Skills.GetSkill(ESkill.Track), 0);
            if (Magic.sMagic.IsSpellActive(Magic.ESpell.FreezeTime))
            {
                // fish won't bite when time is frozen
                skillResult = Skills.ESkillTestResult.Failure;
            }
            switch (skillResult)
            {
            case Skills.ESkillTestResult.CriticalSuccess:
            case Skills.ESkillTestResult.Success:
                if (Time.time < timeOfLastCatch + 3.0f)
                {
                    Messages.Add(1, 102);
                }
                else
                {
                    // generate a lovely fish and drop it at the player's feet
                    UUObject fish = LevelLoader.CreateObjectOfType(EObjectType.Fish);
                    if (fish != null)
                    {
                        // make sure it's fresh
                        fish.quality = 60;
                        // Initialize name properties so fish has proper name
                        fish.PostLoadInitialize();
                        Vector3 playerPos = PlayerObject.Player.transform.position; 
                        Vector3 pos = playerPos + 0.5f * PlayerObject.Player.transform.forward;
                        // make sure it doesn't fall in the water by clamping it within the tile that the player is in
                        Tile t = LevelLoader.GetTile((int)(playerPos.x / Tile.xzScale), (int)(playerPos.z / Tile.xzScale));
                        Vector3 tileCenter = t.GetCenter();
                        Vector3 off = pos - tileCenter;
                        off.x = Mathf.Clamp(off.x, -1.0f, 1.0f);
                        off.z = Mathf.Clamp(off.z, -1.0f, 1.0f);
                        fish.transform.position = tileCenter + off;
                        LevelLoader.AddToWorld(fish);
                        Messages.Add(1, 99);

                        Utils.PlayClip(fishFlop, fish.transform.position);

                        ++PlayerData.sData.numFishCaught;
                    }
                    timeOfLastCatch = Time.time;
                }
                break;
            case Skills.ESkillTestResult.Failure:
            case Skills.ESkillTestResult.CriticalFailure:
                Messages.Add(1, 100);
                break;
            }
        }

        return EEquipAction.Use;
    }
}
