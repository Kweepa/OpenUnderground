// To enable debug logging for this tool, go to Project Settings > Player > Other Settings
// and add "DEBUG_ANIMATION_EDITOR" to the Scripting Define Symbols for the editor platform.
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class AnimationEventEditor : EditorWindow
{
    private AnimationClip activeClip;
    private GameObject prefabAsset;
    private GameObject previewInstance;
    private float normalizedTime;
    private AnimationEvent[] originalEvents;
    private List<AnimationEvent> modifiedEvents;
    private AnimationEvent selectedEvent;
    private Editor gameObjectEditor; // For character preview

    private List<AnimationClip> availableClips = new List<AnimationClip>();
    private string[] clipNames = new string[0];
    private int selectedClipIndex = -1;
    private bool isDraggingEvent = false;

    private const float TimelineHeight = 50f;
    private const float MarkerWidth = 8f;
    private const float MarkerHeight = 16f;
    private const string EventFileExtension = ".events";

    // Store last selected object to maintain context
    private static GameObject lastSelectedPrefab;

    [MenuItem("Tools/Animation Event Editor")]
    public static void ShowWindow()
    {
        GetWindow<AnimationEventEditor>("Animation Events");
    }

    void OnEnable()
    {
        // Attempt to restore state when the window is enabled
        if (lastSelectedPrefab != null)
        {
            SelectPrefab(lastSelectedPrefab);
        }
        // Subscribe to selection changes
        Selection.selectionChanged += OnSelectionChanged;
    }

    void OnDisable()
    {
        // Unsubscribe to prevent memory leaks
        Selection.selectionChanged -= OnSelectionChanged;
        // Clean up preview instance and animation mode
        CleanUpPreview();
    }

    private void CleanUpPreview()
    {
        if (AnimationMode.InAnimationMode())
        {
            AnimationMode.StopAnimationMode();
        }
        if (gameObjectEditor != null)
        {
            DestroyImmediate(gameObjectEditor);
            gameObjectEditor = null;
        }
        if (previewInstance != null)
        {
            DestroyImmediate(previewInstance);
            previewInstance = null;
        }
    }

    private void OnSelectionChanged()
    {
        // If selection is a prefab, auto-select it.
        var selected = Selection.activeGameObject;
        if (selected != null && PrefabUtility.IsPartOfPrefabAsset(selected))
        {
            if(selected != prefabAsset)
            {
                 SelectPrefab(selected);
            }
        }
    }

    private void SelectPrefab(GameObject prefab)
    {
        CleanUpPreview();

        prefabAsset = prefab;
        lastSelectedPrefab = prefab;
        availableClips.Clear();
        clipNames = new string[0];
        activeClip = null;
        selectedClipIndex = -1;

        if (prefabAsset == null)
        {
            Repaint();
            return;
        }

        Animator animator = prefabAsset.GetComponent<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning("Selected prefab does not have an Animator with a controller.");
            Repaint();
            return;
        }

        availableClips.AddRange(animator.runtimeAnimatorController.animationClips.Distinct());
        clipNames = availableClips.Select(c => c.name).ToArray();

        if (availableClips.Count > 0)
        {
            previewInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset);
            previewInstance.hideFlags = HideFlags.HideAndDontSave;
            
            gameObjectEditor = Editor.CreateEditor(previewInstance);
            SetSelectedClip(0);
        }

        Repaint();
    }

    private void SetSelectedClip(int index)
    {
        if (index < 0 || index >= availableClips.Count) return;

        selectedClipIndex = index;
        activeClip = availableClips[index];
        LoadEventsFromClip();
        normalizedTime = 0; // Reset scrubber
        ScrubAnimation();
    }

    private void LoadEventsFromClip()
    {
        if (activeClip == null) return;
        originalEvents = AnimationUtility.GetAnimationEvents(activeClip);
        modifiedEvents = new List<AnimationEvent>(originalEvents);
        selectedEvent = null;
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Animation Event Editor", EditorStyles.boldLabel);

        GameObject newPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab", prefabAsset, typeof(GameObject), false);
        if (newPrefab != prefabAsset)
        {
            SelectPrefab(newPrefab);
        }

        if (availableClips.Count > 0)
        {
            int newClipIndex = EditorGUILayout.Popup("Animation Clip", selectedClipIndex, clipNames);
            if (newClipIndex != selectedClipIndex)
            {
                SetSelectedClip(newClipIndex);
            }
        }

        if (activeClip == null || previewInstance == null)
        {
            EditorGUILayout.HelpBox("Select a Prefab with an Animator and an Animation Controller.", MessageType.Info);
            return;
        }

        // Main controls area
        EditorGUILayout.BeginVertical();
        DrawTimeline();
        DrawEventEditor();
        DrawActionButtons();
        EditorGUILayout.EndVertical();

        // Character preview area
        DrawPreview();
    }

    private void DrawPreview()
    {
        if (gameObjectEditor == null) return;
        
        EditorGUILayout.LabelField("Preview", EditorStyles.centeredGreyMiniLabel);
        Rect previewRect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        GUI.Box(previewRect, "");
        gameObjectEditor.OnPreviewGUI(previewRect, EditorStyles.helpBox);
    }

    private void DrawTimeline()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Timeline", EditorStyles.centeredGreyMiniLabel);
        
        Rect timelineRect = GUILayoutUtility.GetRect(position.width - 20, TimelineHeight, GUILayout.ExpandWidth(true));
        GUI.Box(timelineRect, "");

        int tickCount = 10;
        for (int i = 0; i <= tickCount; i++)
        {
            float t = (float)i / tickCount;
            float x = timelineRect.x + t * timelineRect.width;
            Rect tickRect = new Rect(x, timelineRect.y, 1, 5);
            GUI.Box(tickRect, "");
            GUI.Label(new Rect(x - 15, timelineRect.y + 5, 40, 20), (t * activeClip.length).ToString("F2"));
        }

        if (modifiedEvents != null)
        {
            foreach (var animEvent in modifiedEvents.ToList()) // ToList() to allow modification during iteration
            {
                float eventNormalizedTime = animEvent.time / activeClip.length;
                float xPos = timelineRect.x + eventNormalizedTime * timelineRect.width;
                Rect eventMarkerRect = new Rect(xPos - MarkerWidth / 2, timelineRect.y + timelineRect.height - MarkerHeight, MarkerWidth, MarkerHeight);
                
                EditorGUI.DrawRect(eventMarkerRect, animEvent == selectedEvent ? Color.yellow : Color.cyan);

                if (Event.current.type == EventType.MouseDown && eventMarkerRect.Contains(Event.current.mousePosition))
                {
                    selectedEvent = animEvent;
                    isDraggingEvent = true;
                    normalizedTime = eventNormalizedTime;
                    Event.current.Use();
                    ScrubAnimation();
                }
            }
        }
        
        if (isDraggingEvent && Event.current.type == EventType.MouseDrag)
        {
            if(selectedEvent != null)
            {
                normalizedTime = Mathf.Clamp01((Event.current.mousePosition.x - timelineRect.x) / timelineRect.width);
                selectedEvent.time = normalizedTime * activeClip.length;
                ScrubAnimation();
                ApplyEventsToClip();
                Event.current.Use();
            }
        }
        
        if (isDraggingEvent && Event.current.type == EventType.MouseUp)
        {
            isDraggingEvent = false;
            Event.current.Use();
        }

        float scrubberX = timelineRect.x + normalizedTime * timelineRect.width;
        Rect scrubberRect = new Rect(scrubberX - 1, timelineRect.y, 2, timelineRect.height);
        EditorGUI.DrawRect(scrubberRect, Color.red);
        
        if (!isDraggingEvent && Event.current.type == EventType.MouseDrag && timelineRect.Contains(Event.current.mousePosition))
        {
            normalizedTime = Mathf.Clamp01((Event.current.mousePosition.x - timelineRect.x) / timelineRect.width);
            ScrubAnimation();
            Event.current.Use();
        }
        
        float newNormalizedTime = EditorGUILayout.Slider("Time", normalizedTime, 0f, 1f);
        if(newNormalizedTime != normalizedTime) {
            normalizedTime = newNormalizedTime;
            ScrubAnimation();
        }
    }
    
    private void ScrubAnimation()
    {
        if (previewInstance == null || activeClip == null)
        {
#if DEBUG_ANIMATION_EDITOR
            Debug.LogWarning($"ScrubAnimation aborted: previewInstance is {(previewInstance == null ? "null" : "valid")}, activeClip is {(activeClip == null ? "null" : "valid ('" + activeClip.name + "')")}");
#endif
            return;
        }

        Animator animator = previewInstance.GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogWarning($"No animator on preview instance");
            return;
        }

        animator.enabled = false;

        try
        {
            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
            }

            AnimationMode.BeginSampling();

#if DEBUG_ANIMATION_EDITOR
            Debug.Log($"Scrubbing clip '{activeClip.name}' on '{previewInstance.name}' to time {normalizedTime * activeClip.length} ({normalizedTime:P2})");
#endif

            // This is the correct method to use for sampling.
            activeClip.SampleAnimation(previewInstance, normalizedTime * activeClip.length);
            
            AnimationMode.EndSampling();
        }
        finally
        {
            // Ensure the animator speed is restored even if an error occurs
        }

        if (gameObjectEditor != null)
        {
            gameObjectEditor.Repaint();
        }
        Repaint();
    }

    private void DrawEventEditor()
    {
        EditorGUILayout.Space();
        if (selectedEvent == null)
        {
            EditorGUILayout.HelpBox("Select an event on the timeline to edit it, or add a new one.", MessageType.Info);
            if (GUILayout.Button("Add New Event at Scrubber"))
            {
                AddNewEvent();
            }
        }
        else
        {
            EditorGUILayout.LabelField("Selected Event", EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();

            string newFunctionName = EditorGUILayout.TextField("Function Name", selectedEvent.functionName);
            float newTime = EditorGUILayout.FloatField("Time", selectedEvent.time);
            string newStringParam = EditorGUILayout.TextField("String Parameter", selectedEvent.stringParameter);
            float newFloatParam = EditorGUILayout.FloatField("Float Parameter", selectedEvent.floatParameter);
            int newIntParam = EditorGUILayout.IntField("Int Parameter", selectedEvent.intParameter);
            Object newObjParam = EditorGUILayout.ObjectField("Object Parameter", selectedEvent.objectReferenceParameter, typeof(Object), true);

            if (EditorGUI.EndChangeCheck())
            {
                var updatedEvent = new AnimationEvent
                {
                    functionName = newFunctionName,
                    time = Mathf.Clamp(newTime, 0, activeClip.length),
                    stringParameter = newStringParam,
                    floatParameter = newFloatParam,
                    intParameter = newIntParam,
                    objectReferenceParameter = newObjParam
                };
                
                int index = modifiedEvents.IndexOf(selectedEvent);
                if(index != -1) {
                    modifiedEvents[index] = updatedEvent;
                    selectedEvent = updatedEvent;
                    ApplyEventsToClip();
                }
            }

            if (GUILayout.Button("Delete Selected Event", GUILayout.Width(200)))
            {
                if (EditorUtility.DisplayDialog("Delete Event?", "Are you sure you want to delete this event?", "Yes", "No"))
                {
                    modifiedEvents.Remove(selectedEvent);
                    selectedEvent = null;
                    ApplyEventsToClip();
                }
            }
        }
    }
    
    private void AddNewEvent()
    {
        var newEvent = new AnimationEvent
        {
            time = normalizedTime * activeClip.length,
            functionName = "NewEvent"
        };
        modifiedEvents.Add(newEvent);
        selectedEvent = newEvent;
        ApplyEventsToClip();
    }
    
    private void DrawActionButtons()
    {
        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("Revert Changes"))
        {
            if(EditorUtility.DisplayDialog("Revert Changes?", "Are you sure you want to discard your changes to this clip?", "Yes", "No"))
            {
                LoadEventsFromClip();
                ApplyEventsToClip();
            }
        }
        
        if (GUILayout.Button("Save Events to File"))
        {
            SaveEventsToFile();
        }
        
        if (GUILayout.Button("Load Events from File"))
        {
            LoadEventsFromFile();
        }
        
        EditorGUILayout.EndHorizontal();
    }
    
    private void ApplyEventsToClip()
    {
        if (activeClip == null) return;
        
        Undo.RecordObject(activeClip, "Modify Animation Events");
        AnimationUtility.SetAnimationEvents(activeClip, modifiedEvents.ToArray());
        EditorUtility.SetDirty(activeClip);
        originalEvents = modifiedEvents.ToArray(); // Refresh original events state
        Repaint();
    }

    [System.Serializable]
    private class SerializableEvent
    {
        public string functionName;
        public float time;
        public string stringParameter;
        public float floatParameter;
        public int intParameter;
        public string objectReferencePath;
        public string objectReferenceType;
    }

    [System.Serializable]
    private class EventContainer
    {
        public List<SerializableEvent> events = new List<SerializableEvent>();
    }
    
    private void SaveEventsToFile()
    {
        if (activeClip == null) return;
        
        string clipPath = AssetDatabase.GetAssetPath(activeClip);
        string savePath = Path.ChangeExtension(clipPath, EventFileExtension);

        EventContainer container = new EventContainer();
        foreach (var e in modifiedEvents)
        {
            var se = new SerializableEvent
            {
                functionName = e.functionName,
                time = e.time,
                stringParameter = e.stringParameter,
                floatParameter = e.floatParameter,
                intParameter = e.intParameter
            };
            if(e.objectReferenceParameter != null)
            {
                se.objectReferencePath = AssetDatabase.GetAssetPath(e.objectReferenceParameter);
                se.objectReferenceType = e.objectReferenceParameter.GetType().AssemblyQualifiedName;
            }
            container.events.Add(se);
        }

        string json = JsonUtility.ToJson(container, true);
        File.WriteAllText(savePath, json);
        
        AssetDatabase.Refresh();
        Debug.Log($"Saved events to '{savePath}'.");
    }
    
    private void LoadEventsFromFile()
    {
        if (activeClip == null) return;

        string clipPath = AssetDatabase.GetAssetPath(activeClip);
        string loadPath = Path.ChangeExtension(clipPath, EventFileExtension);

        if (!File.Exists(loadPath))
        {
            Debug.LogError($"No event file found at '{loadPath}'");
            return;
        }

        string json = File.ReadAllText(loadPath);
        EventContainer container = JsonUtility.FromJson<EventContainer>(json);

        modifiedEvents.Clear();
        foreach (var se in container.events)
        {
            var e = new AnimationEvent
            {
                functionName = se.functionName,
                time = se.time,
                stringParameter = se.stringParameter,
                floatParameter = se.floatParameter,
                intParameter = se.intParameter
            };

            if (!string.IsNullOrEmpty(se.objectReferencePath))
            {
                System.Type type = System.Type.GetType(se.objectReferenceType);
                if(type != null)
                {
                    e.objectReferenceParameter = AssetDatabase.LoadAssetAtPath(se.objectReferencePath, type);
                }
            }
            modifiedEvents.Add(e);
        }

        selectedEvent = null;
        ApplyEventsToClip(); // Automatically apply loaded events
        Debug.Log($"Loaded events from '{loadPath}'.");
    }
}
