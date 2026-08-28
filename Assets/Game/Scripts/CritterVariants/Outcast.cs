using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    public enum EArmor
    {
        None,
        Shiny,
        Dusty
    }

    public enum EHairColor
    {
        Blond,
        Brown,
        Dark,
        Red,
        Silver
    }

    public enum EHair
    {
        Short,
        Long
    }

    public enum EMustache
    {
        None,
        Small,
        Large
    }

    public enum EBeard
    {
        None,
        Goatee,
        Thin,
        Full
    }

    public enum ESkin
    {
        Black,
        Dark,
        Light,
        Red,
        Yellow
    }

    public enum EVest
    {
        Cobalt,
        Iron
    }

    public enum EShirt
    {
        Green,
        Red,
        Tan
    }

    public enum EPants
    {
        Tan,
        Gray,
        Green
    }

    public enum EBoots
    {
        Red,
        Black,
        Brown
    }

    [System.Serializable]
    public struct OutcastCustomization
    {
        public string name;
        [FilterableEnumList]
        public EWhoAmI whoami;
        public EArmor armor;
        public EHairColor hairColor;
        public EHair hair;
        public EMustache mustache;
        public EBeard beard;
        public ESkin skin;
        public EVest vest;
        public EShirt shirt;
        public EPants pants;
        public EBoots boots;
    }

    public class Outcast : Critter
    {
        public GameObject carriedSword;
        public GameObject sheathedSword;
        public GameObject shoulderPads;
        public GameObject[] beards;
        public GameObject[] hairs;
        public GameObject[] mustaches;
        public GameObject[] curls;
        public GameObject brows;
        public GameObject boots;
        public GameObject face;
        public GameObject shirt;
        public GameObject vest;
        public GameObject pants;

        public Material[] armorMats;
        public Material[] skinMats;
        public Material[] hairMats;
        public Material[] vestMats;
        public Material[] bootMats;
        public Material[] shirtMats;
        public Material[] pantsMats;

        public OutcastCustomization[] customizations;

        private void SetPartMaterial(GameObject part, Material mat)
        {
            if (part != null)
            {
                SkinnedMeshRenderer skin = part.GetComponent<SkinnedMeshRenderer>();
                if (skin != null)
                {
                    skin.material = mat;
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

        private void Customize(OutcastCustomization cust)
        {
            if (shoulderPads != null)
            {
                cachedVisibilityGroup.SetGameObjectState(shoulderPads, cust.armor != EArmor.None);
                SetPartMaterial(shoulderPads, armorMats[(int)cust.armor]);
            }

            // hair
            Material hairMat = hairMats[(int)cust.hairColor];
            EnablePart(beards, (int)cust.beard, hairMat);
            EnablePart(hairs, (int)cust.hair, hairMat);
            EnablePart(mustaches, (int)cust.mustache, hairMat);
            SetPartMaterial(brows, hairMat);
            SetPartMaterial(curls[0], hairMat);
            SetPartMaterial(curls[1], hairMat);
                        
            // other bits
            SetPartMaterial(boots, bootMats[(int)cust.boots]);
            SetPartMaterial(vest, vestMats[(int)cust.vest]);
            SetPartMaterial(shirt, shirtMats[(int)cust.shirt]);
            SetPartMaterial(face, skinMats[(int)cust.skin]);
            SetPartMaterial(pants, pantsMats[(int)cust.pants]);
        }

        public override void PostLoadInitialize(bool restoredFromSave = false)
        {
            base.PostLoadInitialize(restoredFromSave);

            bool foundSpecific = false;
            if (whoami != EWhoAmI.None)
            {
                foreach (OutcastCustomization cust in customizations)
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
                
                OutcastCustomization cust;

                cust.name = "Random";
                cust.whoami = whoami;
                cust.armor = Utils.RandomEnum<EArmor>();
                cust.hairColor = Utils.RandomEnum<EHairColor>();
                cust.beard = Utils.RandomEnum<EBeard>();
                cust.hair = Utils.RandomEnum<EHair>();
                cust.mustache = Utils.RandomEnum<EMustache>();
                cust.boots = Utils.RandomEnum<EBoots>();
                cust.pants = Utils.RandomEnum<EPants>();
                cust.vest = Utils.RandomEnum<EVest>();
                cust.skin = Utils.RandomEnum<ESkin>();
                cust.shirt = Utils.RandomEnum<EShirt>();
                
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
