using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// A controller for the visibility of a GameObject and all its rendering children.
/// Other scripts should use this component's public methods to change GameObject states
/// instead of enabling/disabling GameObjects directly. This ensures that the
/// group's visibility state is always consistent.
/// </summary>
public class VisibilityGroup : MonoBehaviour
{
    // The "source of truth" for what the enabled state of each GameObject *should* be.
    private Dictionary<GameObject, bool> logicalObjectStates;
    
    // Tracks the current visibility of the entire group.
    private bool IsVisible { get; set; } = true;

    private void Awake()
    {
        Initialize();
    }

    /// <summary>
    /// Finds all child GameObjects with renderers and records their initial active state.
    /// This can be called again if you dynamically add/remove objects.
    /// </summary>
    public void Initialize()
    {
        logicalObjectStates = new Dictionary<GameObject, bool>();
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true); // Include inactive children

        foreach (Renderer rend in renderers)
        {
            // Store the GameObject, not the renderer
            if (!logicalObjectStates.ContainsKey(rend.gameObject))
            {
                logicalObjectStates[rend.gameObject] = rend.gameObject.activeSelf;
            }
        }
    }

    /// <summary>
    /// Rebuilds logical visibility so every renderer GameObject defaults to visible. Used when instantiating
    /// from a live template that may have been PVS-culled (children inactive) so Initialize() would
    /// incorrectly record all-false logical states.
    /// </summary>
    public void ReinitializeLogicalDefaultsAllVisible()
    {
        logicalObjectStates = new Dictionary<GameObject, bool>();
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer rend in renderers)
        {
            GameObject go = rend.gameObject;
            if (!logicalObjectStates.ContainsKey(go))
            {
                logicalObjectStates[go] = true;
            }
        }

        IsVisible = true;
    }

    /// <summary>
    /// Sets the intended visibility state for a specific GameObject within the group.
    /// If the GameObject is not already managed, it will be added to the group automatically.
    /// This change will only be applied visually if the group is currently visible.
    /// </summary>
    /// <param name="obj">The GameObject to change.</param>
    /// <param name="isVisible">The intended state (true for visible, false for hidden).</param>
    public void SetGameObjectState(GameObject obj, bool isVisible)
    {
        // Using the indexer will add the GameObject if it's not already managed,
        // or update its state if it is. This is more robust than checking with ContainsKey.
        logicalObjectStates[obj] = isVisible;

        // If the group is currently visible, apply the change immediately.
        if (IsVisible)
        {
            obj.SetActive(isVisible);
        }
    }

    /// <summary>
    /// Hides the entire group by disabling all managed GameObjects.
    /// The logical states are preserved.
    /// </summary>
    public void Hide()
    {
        if (!IsVisible) return;
        IsVisible = false;

        foreach (var entry in logicalObjectStates)
        {
            if (entry.Key != null)
            {
                entry.Key.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Shows the entire group by restoring all GameObjects to their last known logical state.
    /// </summary>
    public void Show()
    {
        if (IsVisible) return;
        IsVisible = true;

        foreach (var entry in logicalObjectStates)
        {
            if (entry.Key != null)
            {
                // Restore the GameObject to its correct logical state.
                entry.Key.SetActive(entry.Value);
            }
        }
    }

    /// <summary>
    /// Forces a flush/sync of visibility state based on logicalObjectStates.
    /// This ensures that after loading, the visibility state is correctly synchronized
    /// regardless of the current IsVisible flag. GameObjects will be set to match their
    /// logical states, and IsVisible will be updated accordingly.
    /// </summary>
    public void FlushVisibilityState()
    {
        if (logicalObjectStates == null || logicalObjectStates.Count == 0)
        {
            return;
        }

        // Check if any logical states are true
        bool anyLogicalStateTrue = false;
        foreach (var entry in logicalObjectStates)
        {
            if (entry.Value)
            {
                anyLogicalStateTrue = true;
                break;
            }
        }

        // IsVisible may disagree with logical aggregation until here (e.g. Instantiate after PVS Hide on a template,
        // or deserialization order). This method exists to reconcile — do not treat as an error.
        IsVisible = anyLogicalStateTrue;

        foreach (var entry in logicalObjectStates)
        {
            if (entry.Key != null)
            {
                entry.Key.SetActive(entry.Value);
            }
        }
    }
}
