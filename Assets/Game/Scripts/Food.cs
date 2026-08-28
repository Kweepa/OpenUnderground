using UnityEngine;
using System.Collections;

public class Food : UUObject
{
    public AudioClip eat;
    public AudioClip cook;

    protected EEquipAction EatSolid(int foodValue)
    {
        if (PlayerData.sData.hunger < foodValue)
        {
            Messages.Add(1, 126);
            return EEquipAction.Nothing;
        }

        PlayerObject.Player.RestoreHealth(foodValue * GetQualityIndex() / 40);
        PlayerData.sData.hunger = Mathf.Max(0, PlayerData.sData.hunger - foodValue);
        Utils.PlayClip2d(eat);

        if (isEnchanted)
        {
            Magic.sMagic.TryCast(enchantmentName, anonymous: true);
        }

        return EEquipAction.Consume;
    }

    protected EEquipAction EatSolidWithMessage(int messageId, int foodValue)
    {
        EEquipAction action = EatSolid(foodValue);
        if (action == EEquipAction.Consume)
        {
            Messages.Add(1, messageId);
        }

        return action;
    }
    
    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);

        if (isEnchanted)
        {
            int enchantmentIndex = special;
            if (option == 0)
            {
                // just the loaf of bread on level 8
                enchantmentIndex &= 255;
            }
            else
            {
                // just the bottle of ale on level 1
                enchantmentIndex = 256 + (special & 63);
            }
            enchantmentName = StringLoader.GetString(6, enchantmentIndex);
        }

        // identify Wine of Compassion immediately
        if (type == EObjectType.BottleOfWine)
        {
            loreResult = Skills.ESkillTestResult.CriticalSuccess;
        }
    }
    
    public override EEquipAction Equip() // consume/eat
    {
        // Cook corn into popcorn when looking at campfire or lava
        if (type == EObjectType.EarOfCorn && (IsLookingAtCampfire() || IsLookingAtLava()))
        {
            UUObject popcorn = LevelLoader.CreateObjectOfType(EObjectType.Popcorn);
            if (popcorn != null)
            {
                popcorn.quality = quality; // Preserve quality from corn
                popcorn.PostLoadInitialize(); // Initialize name properties so popcorn has proper name
                Inventory.Add(popcorn);
                Messages.Add($"You cook the {singularName} into {popcorn.singularName}.");
                Utils.PlayClip2d(cook);
                return EEquipAction.Consume;
            }
        }

        if (type == EObjectType.BottleOfWine)
        {
            Messages.Add(1, 127); // unable to open
            return EEquipAction.Use; // don't try anything else
        }

        // actual food - restore some health & reduce hunger, depending on various factors :|
        int foodValue = DataLoader.sDataLoader.objectsData.nutritionStats[(int)type & 15].FoodValue;

        if (type is EObjectType.PlantB or EObjectType.PlantC)
        {
            foodValue = 5;
        }

        if (foodValue > 127)
        {
            foodValue = 256 - foodValue;

            Utils.PlayClip2d(eat);

            if (isEnchanted)
            {
                Magic.sMagic.TryCast(enchantmentName, true);
            }

            switch (type)
            {
            case EObjectType.BottleOfWater:
                Messages.Add(1, 237);
                PlayerData.sData.hp = Mathf.Min(PlayerData.sData.hp + 2, PlayerData.sData.vitality);
                return EEquipAction.Consume;
            case EObjectType.FlaskOfPort:
                Messages.Add(1, 238);
                break;
            case EObjectType.BottleOfAle:
                Messages.Add(1, 239);
                break;
            }
            
            // it's alcohol...
            PlayerData.sData.drunkenness += foodValue;

            Skills.ESkillTestResult result = Skills.GetResult(PlayerData.sData.strength, PlayerData.sData.drunkenness);

            switch (result)
            {
            case Skills.ESkillTestResult.CriticalFailure:
                PlayerObject.Player.StartCoroutine(SleepItOff());
                break;
            case Skills.ESkillTestResult.Failure:
                // screenshake
                PlayerObject.Player.GetComponentInChildren<ScreenShake>().Shake();
                break;
            case Skills.ESkillTestResult.Success:
                // nuffim
                break;
            case Skills.ESkillTestResult.CriticalSuccess:
                Messages.Add(1, 242);
                PlayerData.sData.hp = Mathf.Min(PlayerData.sData.hp + 2, PlayerData.sData.vitality);
                break;
            }

            return EEquipAction.Consume;
        }
        else
        {
            EEquipAction action = EatSolid(foodValue);
            if (action != EEquipAction.Consume)
            {
                return action;
            }

            bool defaultMessage = true;

            switch (type)
            {
            case EObjectType.Popcorn:
                PlayerData.sData.atePopcorn = true;
                break;
            case EObjectType.Mushroom:
                Messages.Add(1, 232);
                defaultMessage = false;
                // TODO: add to mana
                // turn on hallucination effect
                PlayerObject.Player.GetComponent<PlayerEffectsController>().StartMushroomTrip();
                PlayerData.sData.tripped = true;
                break;
            case EObjectType.Toadstool:
                Messages.Add(1, 231);
                defaultMessage = false;
                // TODO: poison the player
                break;
            case EObjectType.PlantB:
            case EObjectType.PlantC:
                Messages.Add(1, 236); // eat around thorny flowers
                defaultMessage = false;
                break;
            }

            if (defaultMessage)
            {
                int tasteIndex = Mathf.Clamp(GetQualityIndex() + Random.Range(-1, 2), 0, 4);
                Messages.Add($"That {singularName}{StringLoader.GetString(1, 172 + tasteIndex)}");
                if (tasteIndex == 0)
                {
                    // rotten
                    PlayerObject.AddPoison(Random.Range(1, 4));
                }
            }

            return action;
        }
    }

    private IEnumerator SleepItOff()
    {
        PlayerObject.DisableControls(EControlMask.Resting, true);
        
        Messages.Add(1, 241);
        
        PlayerObject.Player.fadeIn = false;

        while (PlayerObject.Player.fade < 1.0f)
        {
            PlayerObject.Player.fade += Time.unscaledDeltaTime;
            yield return null;
        }
        PlayerObject.Player.fade = 1.0f;
        
        PlayerData.sData.gameTime += 8 * 60 * 60; // 8 hours
        PlayerData.sData.drunkenness = 0;
        PlayerData.sData.passedOutDrunk = true;

        yield return new WaitForSeconds(1.0f);

        if (!Utils.SafeToSleepOffDrink() && !Cheats.sCheats.invincible)
        {
            // go into the death routine
            PlayerObject.DisableControls(EControlMask.Resting, false);
            PlayerObject.Player.Damage(Skills.ESkillTestResult.Success, PlayerData.sData.hp + 1, EDamageType.Direct);
            yield break;
        }
        
        Messages.Add(1, 243);
        
        Door.CloseRandomDoors();

        while (PlayerObject.Player.fade > 0.0f)
        {
            PlayerObject.Player.fade -= Time.unscaledDeltaTime;
            yield return null;
        }
        PlayerObject.Player.fade = 0.0f;

        PlayerObject.DisableControls(EControlMask.Resting, false);
    }

    protected override int GetQualityOffset()
    {
        // 18 for worm-infested (meat, apple, fish)
        // 24 for moldy (bread, cheese)
        switch (type)
        {
        case EObjectType.PieceOfMeat:
        case EObjectType.Apple:
        case EObjectType.Fish:
            return 18;
        case EObjectType.PlantB:
        case EObjectType.PlantC:
        case EObjectType.DeadRotworm:
        case EObjectType.RotwormStew:
            return -1;
        default:
            return 24;
        }
    }

    protected override string GetIdentifiedName(string baseName)
    {
        if (type == EObjectType.BottleOfWine)
        {
            return StringLoader.GetString(1, 264);
        }

        return base.GetIdentifiedName(baseName);
    }

    private bool IsLookingAtCampfire()
    {
        if (Interaction.sInt == null || Interaction.sInt.centeredObject == null)
        {
            return false;
        }
        
        float thresholdDist = Interaction.sInt.GetInteractionDistance();
        float distance = Vector3.Distance(
            PlayerObject.Player.mainCamera.transform.position,
            Interaction.sInt.centeredObject.transform.position);
        return Interaction.sInt.centeredObject.type == EObjectType.Campfire 
            && distance < thresholdDist;
    }

    private bool IsLookingAtLava()
    {
        if (Interaction.sInt == null)
        {
            return false;
        }
        
        float maxDist = Interaction.sInt.GetInteractionDistance();
        int layerMask = LayerMasks.EnvironmentAndCeiling;
        
        if (Physics.Raycast(PlayerObject.Player.mainCamera.transform.position, 
            PlayerObject.Player.mainCamera.transform.forward, 
            out RaycastHit hit, maxDist, layerMask))
        {
            // Check that the hit normal is facing upwards (not a wall)
            if (hit.normal.y <= 0.5f)
            {
                return false;
            }
            
            Tile t = LevelLoader.GetClosestTile(hit.point);
            if (t != null && t.GetFloorTerrain() == ETerrainType.Lava)
            {
                // Check that we're close to the actual floor height (not looking at a bridge over lava)
                // Similar to how TouchingTerrain checks: hit point should be close to floor height
                float floorY = t.GetFloorY(hit.point.x, hit.point.z);
                if (hit.point.y < floorY + 1.0f)
                {
                    return true;
                }
            }
        }
        
        return false;
    }

    public override string GetUseText()
    {
        if (type == EObjectType.EarOfCorn && (IsLookingAtCampfire() || IsLookingAtLava()))
        {
            return "Cook";
        }
        
        return type is < EObjectType.BottleOfAle or >= EObjectType.PlantB ? "Eat" : "Drink";
    }
}
