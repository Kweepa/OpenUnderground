using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    
    public class Ghoul : Critter
    {
        public Material[] skinMats;
        public Material[] hairMats;
        public GameObject[] bodyParts;
        public GameObject[] hairParts;

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

        private void SetPartsMaterial(GameObject[] parts, Material mat)
        {
            foreach (GameObject part in parts)
            {
                cachedVisibilityGroup.SetGameObjectState(part, true);
                SetPartMaterial(part, mat);
            }
        }

        public override void PostLoadInitialize(bool restoredFromSave = false)
        {
            base.PostLoadInitialize(restoredFromSave);

            int matIndex = 0;
            switch (type)
            {
            case EObjectType.GhoulA:
                matIndex = 0;
                break;
            case EObjectType.GhoulB:
                matIndex = 1;
                break;
            case EObjectType.DarkGhoul:
                matIndex = 2;
                break;
            }

            Material skinMat = skinMats[matIndex];
            Material hairMat = hairMats[matIndex];
            
            SetPartsMaterial(bodyParts, skinMat);
            bool showHair = whoami == EWhoAmI.Shanklick;
            if (hairParts != null)
            {
                foreach (GameObject part in hairParts)
                {
                    cachedVisibilityGroup.SetGameObjectState(part, showHair);
                }
            }
            if (showHair)
            {
                SetPartsMaterial(hairParts, hairMat);
            }
        }
    }
}
