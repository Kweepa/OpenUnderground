using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    public enum ESorceressAge
    {
        Young,
        Old
    }

    public enum ESorceressRobeColor
    {
        Red,
        Blue,
        Green
    }

    public enum ESorceressSkinColor
    {
        Light,
        Dark
    }

    public enum ESorceressHeadband
    {
        None,
        ThreeGems,
        FiveGems
    }
    
    public enum ESorceressHairColour
    {
        Blond,
        Brown
    }

    [System.Serializable]
    public struct SorceressCustomization
    {
        public string name;
        [FilterableEnumList]
        public EWhoAmI whoami;
        public ESorceressAge age;
        public ESorceressRobeColor robeColor;
        public ESorceressSkinColor skinColor;
        public ESorceressHeadband headband;
        public ESorceressHairColour hairColor;
    }

    public class Sorceress : Caster
    {
        // GameObjects for different parts of the mage model
        public GameObject robes;
        public GameObject[] heads;
        public GameObject hands;
        public GameObject[] eyebrows;
        public GameObject[] headbands;
        public GameObject[] stones;
        public GameObject[] hair;
        public GameObject braid;

        // Material arrays to be assigned in the Unity Editor
        public Material[] robeMats;
        public Material[] skinMats;
        public Material[] hairMats;

        // Array of pre-defined customizations
        public SorceressCustomization[] customizations;

        /// <summary>
        /// Sets the material for a specific GameObject's renderer.
        /// </summary>
        private void SetPartMaterial(GameObject part, Material mat)
        {
            if (part != null)
            {
                SkinnedMeshRenderer mesh = part.GetComponent<SkinnedMeshRenderer>();
                if (mesh != null)
                {
                    mesh.material = mat;
                }
            }
        }
        
        private void EnablePart(GameObject[] parts, int index, Material mat)
        {
            GameObject part = parts[index];
            if (mat != null)
            {
                SetPartMaterial(part, mat);
            }
            foreach (GameObject p in parts)
            {
                if (p != null)
                {
                    cachedVisibilityGroup.SetGameObjectState(p, p == part);
                }
            }
        }

        /// <summary>
        /// Applies the full set of customizations to the sorceress model.
        /// </summary>
        private void Customize(SorceressCustomization cust)
        {
            // Set materials for parts that are always visible
            SetPartMaterial(robes, robeMats[(int)cust.robeColor]);
            EnablePart(headbands, (int)cust.headband, null);
            EnablePart(stones, (int)cust.headband, null);

            switch (cust.age)
            {
            case ESorceressAge.Young:
                if (braid != null)
                {
                    cachedVisibilityGroup.SetGameObjectState(braid, false);
                }
                EnablePart(heads, 0, skinMats[(int)cust.skinColor]);
                SetPartMaterial(hands, skinMats[(int)cust.skinColor]);
                {
                    Material hairMat = hairMats[(int)cust.hairColor];
                    EnablePart(hair, 0, hairMat);
                    EnablePart(eyebrows, 0, hairMat);
                }
                break;
            case ESorceressAge.Old:
                EnablePart(heads, 1, null);
                EnablePart(hair, 1, null);
                EnablePart(eyebrows, 1, null);
                if (braid != null)
                {
                    cachedVisibilityGroup.SetGameObjectState(braid, true);
                }
                break;
            }
        }

        /// <summary>
        /// Initializes the critter's appearance after it has been loaded.
        /// </summary>
        public override void PostLoadInitialize(bool restoredFromSave = false)
        {
            base.PostLoadInitialize(restoredFromSave);

            if (whoami != EWhoAmI.None)
            {
                // Look for a pre-defined customization matching this critter's identity
                foreach (SorceressCustomization cust in customizations)
                {
                    if (cust.whoami == whoami)
                    {
                        Customize(cust);
                        break;
                    }
                }
            }
            // If no specific customization was found, just use what's in the prefab by default
        }

        public override void Update()
        {
            base.Update();

            switch (state)
            {
            case EState.Die:
                {
                    float t = Mathf.Min(stateTime, 1.0f);
                    // s-curve
                    float ease = 3.0f * t * t - 2.0f * t * t * t;
                    float yScale = 1.0f - 0.9f * ease;
                    transform.localScale = new Vector3(1.0f, yScale, 1.0f);
                }
                break;
            }
        }

        protected override string[] GetAttackCandidateStates()
        {
            return new[] { "Cast1", "Cast2", "Throw_Knife" };
        }
    }
}
