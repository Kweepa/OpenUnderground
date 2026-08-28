using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    public enum ETeeth
    {
        Teeth1,
        Teeth2,
        Teeth3
    }

    [System.Serializable]
    public struct TrollCustomization
    {
        public string name;
        [FilterableEnumList]
        public EWhoAmI whoami;
        public ETeeth teeth;
        public bool earring;
    }
    
    public class Troll : Critter
    {
        public GameObject[] teeth;
        public GameObject body;
        public GameObject earring;

        public Material[] bodyMats;
        
        public TrollCustomization[] customizations;

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

        private void EnablePart(GameObject[] parts, int index)
        {
            GameObject part = parts[index];
            foreach (GameObject p in parts)
            {
                if (p != null)
                {
                    cachedVisibilityGroup.SetGameObjectState(p, p == part);
                }
            }
        }

        private void Customize(TrollCustomization cust)
        {
            cachedVisibilityGroup.SetGameObjectState(earring, cust.earring);
            EnablePart(teeth, (int)cust.teeth);

            switch (type)
            {
            case EObjectType.Troll:
                SetPartMaterial(body, bodyMats[0]);
                break;
            case EObjectType.FeralTroll:
                SetPartMaterial(body, bodyMats[1]);
                break;
            case EObjectType.GreatTroll:
                SetPartMaterial(body, bodyMats[2]);
                break;
            }
        }

        public override void PostLoadInitialize(bool restoredFromSave = false)
        {
            base.PostLoadInitialize(restoredFromSave);
            
            bool foundSpecific = false;
            if (whoami != EWhoAmI.None)
            {
                foreach (TrollCustomization cust in customizations)
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
                TrollCustomization cust;
                cust.name = "Random";
                cust.whoami = whoami;
                cust.earring = false;
                cust.teeth = Utils.RandomEnum<ETeeth>();

                Customize(cust);
            }
        }
    }
}
