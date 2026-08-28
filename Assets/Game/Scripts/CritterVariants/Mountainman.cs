using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Game.Scripts.CritterVariants
{
    public enum EArmorColor
    {
        None,
        Iron,
        Gold
    };

    public enum EEyeColor
    {
        Blue,
        Brown,
        Green
    };
        
    [Serializable]
    public struct MountainmanCustomization
    {
        public string name;
        [FilterableEnumList]
        public EWhoAmI whoami;
        public EArmorColor helmet;
        public bool gems;
        public EHairColor hairColor;
        public ESkin skin;
        public EEyeColor eyeColor;
        public EMustache mustache;
        public EBeard beard;
        public EArmorColor armorColor;
        public EPants pants;
            
    }

    public class Mountainman : Critter
    {
        public GameObject carriedWeapon;
        public GameObject sheathedWeapon;
        public GameObject helmet;
        public GameObject gems;
        public GameObject hair;
        public GameObject skin;
        public GameObject eyebrows;
        public GameObject eyes;
        public GameObject[] mustaches;
        public GameObject[] beards;
        public GameObject shoulderPads;
        public GameObject legs;

        public Material[] armorMats;
        public Material[] hairMats;
        public Material[] skinMats;
        public Material[] eyeMats;
        public Material[] pantMats;
        
        public MountainmanCustomization[] customizations;

        private void SetPartMaterial(GameObject part, Material mat)
        {
            if (part != null)
            {
                SkinnedMeshRenderer skinnedMeshRenderer = part.GetComponent<SkinnedMeshRenderer>();
                if (skinnedMeshRenderer != null)
                {
                    skinnedMeshRenderer.material = mat;
                }
                else
                {
                    MeshRenderer mesh = part.GetComponent<MeshRenderer>();
                    if (mesh != null)
                    {
                        mesh.material = mat;
                    }
                }
            }
        }

        private void EnablePart(GameObject[] parts, int index, Material mat)
        {
            GameObject part = parts[index];
            SetPartMaterial(part, mat);
            foreach (GameObject p in parts)
            {
                if (p != null)
                {
                    cachedVisibilityGroup.SetGameObjectState(p, p == part);
                }
            }
        }

        private void Customize(MountainmanCustomization cust)
        {
            if (cust.helmet == EArmorColor.None)
            {
                cachedVisibilityGroup.SetGameObjectState(helmet, false);
            }
            else
            {
                cachedVisibilityGroup.SetGameObjectState(helmet, true);
                SetPartMaterial(helmet, armorMats[(int)cust.helmet]);
            }
            cachedVisibilityGroup.SetGameObjectState(gems, cust.gems);
            Material hairMat = hairMats[(int)cust.hairColor];
            SetPartMaterial(hair, hairMat);
            SetPartMaterial(eyebrows, hairMat);
            EnablePart(mustaches, (int)cust.mustache, hairMat);
            EnablePart(beards, (int)cust.beard, hairMat);
            SetPartMaterial(skin, skinMats[(int)cust.skin]);
            SetPartMaterial(eyes, eyeMats[(int)cust.eyeColor]);
            if (cust.armorColor == EArmorColor.None)
            {
                cachedVisibilityGroup.SetGameObjectState(shoulderPads, false);
            }
            else
            {
                cachedVisibilityGroup.SetGameObjectState(shoulderPads, true);
                SetPartMaterial(shoulderPads, armorMats[(int)cust.armorColor]);
            }
            SetPartMaterial(legs, pantMats[(int)cust.pants]);
        }

        public override void PostLoadInitialize(bool restoredFromSave = false)
        {
            base.PostLoadInitialize(restoredFromSave);
            
            bool foundSpecific = false;
            if (whoami != EWhoAmI.None)
            {
                foreach (MountainmanCustomization cust in customizations)
                {
                    if (cust.whoami == whoami)
                    {
                        foundSpecific = true;
                        Customize(cust);
                        break;
                    }
                }
            }
            if (!foundSpecific)
            {
                // Generate deterministic seed from level and objectIndex for consistent appearance
                int level = originalLevel != 0 ? originalLevel : levelIndex;
                int deterministicSeed = (level * 1024) + objectIndex;
                Random.InitState(deterministicSeed);
                
                // randomize
                MountainmanCustomization cust;
                cust.name = "Random";
                cust.whoami = whoami;
                cust.helmet = Random.value < 0.1f ? EArmorColor.None : (Random.value < 0.5f ? EArmorColor.Iron : EArmorColor.Gold);
                cust.gems = false;
                cust.hairColor = Random.value < 0.33f ? EHairColor.Dark : (Random.value < 0.5f ? EHairColor.Red : EHairColor.Silver);
                cust.skin = Random.value < 0.5f ? ESkin.Black : ESkin.Light;
                cust.eyeColor = Utils.RandomEnum<EEyeColor>();
                cust.mustache = Random.value < 0.5f ? EMustache.Small : EMustache.Large;
                cust.beard = Random.value < 0.5f ? EBeard.Thin : EBeard.Full;
                cust.armorColor = Random.value < 0.5f ? EArmorColor.Iron : EArmorColor.Gold; // only specific mountainmen have no armor
                cust.pants = Random.value < 0.5f ? EPants.Tan : EPants.Green;

                Customize(cust);
            }
        }
        
        protected override void SetState(EState newState)
        {
            base.SetState(newState);

            if (carriedWeapon != null && sheathedWeapon != null)
            {
                switch (newState)
                {
                case EState.Flinch:
                case EState.Dead:
                    // don't want to change the state of the weapon for these states
                    break;
                case EState.Die:
                    // Hide weapons when dead to avoid confusion with loot
                    cachedVisibilityGroup.SetGameObjectState(carriedWeapon, false);
                    cachedVisibilityGroup.SetGameObjectState(sheathedWeapon, false);
                    break;
                case EState.TurnToApproach:
                case EState.Approach:
                case EState.CombatIdle:
                case EState.Attack:
                case EState.TurnToFlee:
                case EState.Flee:
                case EState.CombatTurn:
                    cachedVisibilityGroup.SetGameObjectState(carriedWeapon, true);
                    cachedVisibilityGroup.SetGameObjectState(sheathedWeapon, false);
                    break;
                default:
                    cachedVisibilityGroup.SetGameObjectState(carriedWeapon, false);
                    cachedVisibilityGroup.SetGameObjectState(sheathedWeapon, true);
                    break;
                }
            }
        }
    }
}
