using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class SkinnedMeshBoundsBaker : Editor
{
    [MenuItem("Tools/Skinned Mesh Bounds Baker", false, 0)]
    public static void DeepBake()
    {
        GameObject target = Selection.activeGameObject;
        if (!target)
        {
            Debug.LogError("Select an object to bake bounds.");
            return;
        }

        var anim = target.GetComponent<Animator>();
        var renderers = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        if (anim == null || renderers.Length == 0)
        {
            Debug.LogError("Target must have an Animator and SkinnedMeshRenderers.");
            return;
        }

        Undo.RecordObjects(renderers, "Bake Skinned Bounds");
        Dictionary<GameObject, bool> initialActiveStates = new Dictionary<GameObject, bool>();

        foreach (var smr in renderers)
        {
            initialActiveStates[smr.gameObject] = smr.gameObject.activeSelf;
            smr.gameObject.SetActive(true); // Must be active to sample
            smr.updateWhenOffscreen = true; // Must be true to calculate bounds
            smr.localBounds = smr.sharedMesh.bounds;
        }

        var clips = anim.runtimeAnimatorController.animationClips;
        
        // Initialize bounds dictionary for each renderer
        Dictionary<SkinnedMeshRenderer, Bounds> accumulatedBounds = new Dictionary<SkinnedMeshRenderer, Bounds>();
        foreach (var smr in renderers)
        {
            accumulatedBounds[smr] = smr.localBounds; // Start with current bounds
        }
        
        foreach (AnimationClip clip in clips)
        {
            for (float i = 0; i <= 1.0f; i += 0.1f)
            {
                float sampleTime = clip.length * i;
                clip.SampleAnimation(target, sampleTime);
                
                foreach (var smr in renderers)
                {
                    Bounds currentBounds = smr.localBounds;
                    Bounds accumulated = accumulatedBounds[smr];
                    accumulated.Encapsulate(currentBounds); // Expand to include current sample
                    accumulatedBounds[smr] = accumulated;
                }
            }
        }
        
        Debug.Log("Finished all animation sampling loops."); // Verification Log 3
        
        // Apply accumulated bounds after sampling all animations
        foreach (var smr in renderers)
        {
            smr.localBounds = accumulatedBounds[smr];
        }

        foreach (var smr in renderers)
        {
            smr.updateWhenOffscreen = false;

            Bounds final = smr.localBounds;
            float paddingAmount = final.size.magnitude * 0.075f; 
            final.Expand(paddingAmount); 
            smr.localBounds = final;

            if (initialActiveStates.ContainsKey(smr.gameObject))
            {
                smr.gameObject.SetActive(initialActiveStates[smr.gameObject]);
            }

            EditorUtility.SetDirty(smr);
        }

        // Reset animation to default/T-pose to avoid leaving character in animated pose
        if (anim != null)
        {
            anim.Rebind();
        }

        Debug.Log($"Baking complete. Processed {renderers.Length} meshes and restored active states.");
    }
}
