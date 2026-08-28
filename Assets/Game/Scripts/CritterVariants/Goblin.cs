using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Game.Scripts.CritterVariants
{
    public enum EGoblinHead
    {
        FlatHead,
        SquareJaw
    }

    public enum EGoblinArmour
    {
        Iron,
        Gold,
        Silver
    }

    public enum EStraps
    {
        Brown,
        Green
    }

    public enum EGoblinSkin
    {
        Green,
        Grey
    }

    public enum EGoblinEyes
    {
        Red,
        Yellow,
        Green
    }

    public enum ELoincloth
    {
        Red,
        Brown,
        Green
    }

    public enum EGoblinWeapon
    {
        Club,
        Sword,
        HiddenSword
    }
    
    [Serializable]
    public struct GoblinCustomization
    {
        public string name;
        [FilterableEnumList]
        public EWhoAmI whoami;
        public EGoblinSkin skin;
        public EGoblinEyes eyes;
        public EGoblinHead head;
        public EGoblinArmour armour;
        public EStraps straps;
        public ELoincloth loincloth;
        public EGoblinWeapon weapon;
        public bool hasCloak;
        public bool hasExtraLeftTooth;
        public bool hasExtraRightTooth;
    }


    public class Goblin : Critter
    {
        public GameObject armour;
        public GameObject[] straps;
        public GameObject[] loincloth;
        public GameObject legs;
        public GameObject[] heads;
        public GameObject[] eyes;
        public GameObject sling;
        public GameObject[] cloak;
        public GameObject[] weapons;
        public GameObject[] teeth;

        public Material[] headMats;
        public Material[] eyeMats;
        public Material[] armourMats;
        public Material[] strapMats;
        public Material[] loinclothMats;
        public Material[] legMats;
        
        public GoblinCustomization[] customizations;

        public GameObject projectileLaunchBone;

        private bool hiddenWeapon;

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
                    if (cachedVisibilityGroup == null)
                    {
                        Debug.Log("Why");
                    }
                    else
                    {
                        cachedVisibilityGroup.SetGameObjectState(p, p == part);
                    }
                }
            }
        }

        public void ShowSlingEvent()
        {
            if (sling != null)
            {
                cachedVisibilityGroup.SetGameObjectState(sling, true);
            }
        }

        private void HideSlingEvent()
        {
            if (sling != null)
            {
                cachedVisibilityGroup.SetGameObjectState(sling, false);
            }
        }

        protected override Vector3 GetProjectileLaunchPosition(bool estimated)
        {
            if (estimated || projectileLaunchBone == null)
            {
                float characterHeight = cachedCharacterController.height + cachedCharacterController.radius;
                return transform.position + characterHeight * Vector3.up + cachedCharacterController.radius * (transform.forward - transform.right);
            }
            return projectileLaunchBone.transform.position;
        }

        protected override void SetState(EState newState)
        {
            switch (state)
            {
            case EState.ProjectileAttack:
                HideSlingEvent();
                break;
            case EState.CombatIdle:
                if (hiddenWeapon)
                {
                    cachedVisibilityGroup.SetGameObjectState(weapons[(int)EGoblinWeapon.Sword], true);
                    hiddenWeapon = false;
                }
                break;
            case EState.Die:
                // Hide all weapons when dead to avoid confusion with loot
                if (weapons != null)
                {
                    foreach (GameObject weapon in weapons)
                    {
                        if (weapon != null)
                        {
                            cachedVisibilityGroup.SetGameObjectState(weapon, false);
                        }
                    }
                }
                break;
            }

            base.SetState(newState);
        }

        public override void PostLoadInitialize(bool restoredFromSave = false)
        {
            base.PostLoadInitialize(restoredFromSave);

            bool foundSpecific = false;
            if (whoami != EWhoAmI.None)
            {
                foreach (GoblinCustomization whoCust in customizations)
                {
                    if (whoCust.whoami == whoami)
                    {
                        Customize(whoCust);
                        foundSpecific = true;
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
                
                GoblinCustomization cust;

                cust.name = "Random";
                cust.whoami = whoami;
                cust.armour = Utils.RandomEnum<EGoblinArmour>();
                cust.head = Utils.RandomEnum<EGoblinHead>();
                cust.straps = Utils.RandomEnum<EStraps>();
                cust.eyes = Utils.RandomEnum<EGoblinEyes>();
                cust.loincloth = Utils.RandomEnum<ELoincloth>();
                // skin depends on the type
                cust.skin = type is EObjectType.GoblinA or EObjectType.GoblinB or EObjectType.GoblinD ? EGoblinSkin.Green : EGoblinSkin.Grey;
                // loincloth, straps, eyes can't match skin colour
                if (cust.skin == EGoblinSkin.Green)
                {
                    if (cust.loincloth == ELoincloth.Green) cust.loincloth = ELoincloth.Brown;
                    if (cust.eyes == EGoblinEyes.Green) cust.eyes = EGoblinEyes.Yellow;
                    if (cust.straps == EStraps.Green) cust.straps = EStraps.Brown;
                }
                cust.weapon = Random.value < 0.5f ? EGoblinWeapon.Club : EGoblinWeapon.Sword;
                cust.hasCloak = false;
                cust.hasExtraLeftTooth = Random.value < 0.5f;
                cust.hasExtraRightTooth = Random.value < 0.5f;

                Customize(cust);
            }
        }
        
        private void Customize(GoblinCustomization cust)
        {
            EnablePart(heads, (int)cust.head, headMats[2 * (int)cust.head + (int)cust.skin]);
            EnablePart(eyes, (int)cust.head, eyeMats[3 * (int)cust.head + (int)cust.eyes]);
                        
            SetPartMaterial(armour, armourMats[(int)cust.armour]);
            foreach (GameObject go in straps)
            {
                SetPartMaterial(go, strapMats[(int)cust.straps]);
            }
            foreach (GameObject go in loincloth)
            {
                SetPartMaterial(go, loinclothMats[(int)cust.loincloth]);
            }
            SetPartMaterial(legs, legMats[(int)cust.skin]);

            foreach (GameObject go in cloak)
            {
                cachedVisibilityGroup.SetGameObjectState(go, cust.hasCloak);
            }

            foreach (GameObject go in weapons)
            {
                cachedVisibilityGroup.SetGameObjectState(go, false);
            }

            hiddenWeapon = false;
            if (cust.weapon != EGoblinWeapon.HiddenSword)
            {
                cachedVisibilityGroup.SetGameObjectState(weapons[(int)cust.weapon], true);
            }
            else
            {
                hiddenWeapon = true;
            }

            cachedVisibilityGroup.SetGameObjectState(sling, false);
            
            cachedVisibilityGroup.SetGameObjectState(teeth[0], cust.hasExtraLeftTooth);
            cachedVisibilityGroup.SetGameObjectState(teeth[1], cust.hasExtraRightTooth);
        }
    }
}
