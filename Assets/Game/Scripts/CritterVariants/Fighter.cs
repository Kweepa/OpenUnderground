using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Game.Scripts.CritterVariants
{
    public enum EHeadband
    {
        Silver,
        Gold,
        None
    }

    public enum EFighterArmor
    {
        Silver,
        Gold,
        None
    }

    public enum EFighterBody
    {
        BlueShirt,
        RedShirt
    }
    
    [Serializable]
    public struct FighterCustomization
    {
        public string name;
        [FilterableEnumList]
        public EWhoAmI whoami;
        public EHairColor hairColor;
        public EHair hair;
        public bool hasCurls;
        public ESkin skin;
        public EHeadband headband;
        public EEyeColor eyeColor;
        public EFighterArmor armor;
        public EFighterBody body;
    }
    
    public class Fighter : Critter
    {
        public GameObject carriedSword;
        public GameObject sheathedSword;
        public GameObject[] hairs;
        public GameObject[] curls;
        public GameObject headband;
        public GameObject skin;
        public GameObject brows;
        public GameObject eyes;
        public GameObject[] shoulderPads;
        public GameObject[] body;
        
        public Material[] armorMats;
        public Material[] skinMats;
        public Material[] hairMats;
        public Material[] eyeMats;
        public Material[] bodyMats;
        
        public FighterCustomization[] customizations;
        
        private void SetPartMaterial(GameObject part, Material mat)
        {
            if (part != null)
            {
                SkinnedMeshRenderer skinnedMesh = part.GetComponent<SkinnedMeshRenderer>();
                if (skinnedMesh != null)
                {
                    skinnedMesh.material = mat;
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

        private void Customize(FighterCustomization cust)
        {
            foreach (GameObject g in shoulderPads)
            {
                cachedVisibilityGroup.SetGameObjectState(g, cust.armor != EFighterArmor.None);
                SetPartMaterial(g, armorMats[(int)cust.armor]);
            }
            if (headband != null)
            {
                cachedVisibilityGroup.SetGameObjectState(headband, cust.headband != EHeadband.None);
                SetPartMaterial(headband, armorMats[(int)cust.headband]);
            }

            // hair
            Material hairMat = hairMats[(int)cust.hairColor];
            EnablePart(hairs, (int)cust.hair, hairMat);
            SetPartMaterial(brows, hairMat);
            foreach (GameObject g in curls)
            {
                cachedVisibilityGroup.SetGameObjectState(g, cust.hasCurls);
                SetPartMaterial(g, hairMat);
            }
                        
            // other bits
            foreach (GameObject g in body)
            {
                SetPartMaterial(g, bodyMats[(int)cust.body]);
            }
            SetPartMaterial(skin, skinMats[(int)cust.skin]);
            SetPartMaterial(eyes, eyeMats[(int)cust.eyeColor]);
        }

        public override void PostLoadInitialize(bool restoredFromSave = false)
        {
            base.PostLoadInitialize(restoredFromSave);

            bool foundSpecific = false;
            if (whoami != EWhoAmI.None)
            {
                foreach (FighterCustomization cust in customizations)
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
                
                // for now randomize
                FighterCustomization cust;
                cust.name = "Random";
                cust.whoami = whoami;
                cust.skin = Utils.RandomEnum<ESkin>();
                if (cust.skin == ESkin.Black && Random.value < 0.5f)
                {
                    cust.hair = EHair.Short;
                    cust.hairColor = EHairColor.Dark;
                    cust.hasCurls = Random.value < 0.4f;
                }
                else
                {
                    cust.hair = EHair.Long;
                    cust.hairColor = Utils.RandomEnum<EHairColor>();
                    cust.hasCurls = Random.value < 0.8f;
                }
                cust.eyeColor = Utils.RandomEnum<EEyeColor>();
                cust.headband = Utils.RandomEnum<EHeadband>();
                cust.armor = Utils.RandomEnum<EFighterArmor>();
                cust.body = Utils.RandomEnum<EFighterBody>();

                Customize(cust);
            }
        }

        protected override void SetState(EState newState)
        {
            base.SetState(newState);

            if (carriedSword != null && sheathedSword != null)
            {
                switch (newState)
                {
                case EState.Flinch:
                case EState.Dead:
                    // don't want to change the state of the sword for these states
                    break;
                case EState.Die:
                    // Hide weapons when dead to avoid confusion with loot
                    cachedVisibilityGroup.SetGameObjectState(carriedSword, false);
                    cachedVisibilityGroup.SetGameObjectState(sheathedSword, false);
                    break;
                case EState.TurnToApproach:
                case EState.Approach:
                case EState.CombatIdle:
                case EState.Attack:
                case EState.TurnToFlee:
                case EState.Flee:
                case EState.CombatTurn:
                    cachedVisibilityGroup.SetGameObjectState(carriedSword, true);
                    cachedVisibilityGroup.SetGameObjectState(sheathedSword, false);
                    break;
                default:
                    cachedVisibilityGroup.SetGameObjectState(carriedSword, false);
                    cachedVisibilityGroup.SetGameObjectState(sheathedSword, true);
                    break;
                }
            }
        }
    }
}
