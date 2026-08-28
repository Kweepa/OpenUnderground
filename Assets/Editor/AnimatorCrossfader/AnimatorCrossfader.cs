// AnimatorCrossfader.cs
// Place this script in a folder named "Editor" in your Unity project.

using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;

public class AnimatorCrossfader : EditorWindow
{
    private GameObject _selectedObject;
    private GameObject _animatorOwnerObject; // The object that actually has the Animator
    private Animator _targetAnimator;
    private Vector2 _scrollPosition;
    
    // 1. Default crossfade duration changed to 0.15f.
    private float _crossfadeDuration = 0.15f;

    private readonly List<(string name, int hash)> _animatorStates = new List<(string, int)>();

    [MenuItem("Tools/Animator Crossfader")]
    public static void ShowWindow()
    {
        GetWindow<AnimatorCrossfader>("Animator Crossfader");
    }

    private void OnEnable()
    {
        Selection.selectionChanged += OnSelectionChanged;
        OnSelectionChanged(); // Initial update
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged()
    {
        _selectedObject = Selection.activeGameObject;
        
        // Clear previous data
        _animatorStates.Clear();
        _targetAnimator = null;
        _animatorOwnerObject = null;

        if (_selectedObject != null)
        {
            // 2. Use GetComponentInParent to find the Animator on the selected object or its ancestors.
            _targetAnimator = _selectedObject.GetComponentInParent<Animator>();

            if (_targetAnimator != null)
            {
                // Store the GameObject that owns the Animator component.
                _animatorOwnerObject = _targetAnimator.gameObject;
                if (_targetAnimator.runtimeAnimatorController != null)
                {
                    FindAllAnimatorStates();
                }
            }
        }
        
        Repaint(); // Force the window to redraw
    }

    private void FindAllAnimatorStates()
    {
        var controller = _targetAnimator.runtimeAnimatorController as AnimatorController;
        if (controller == null) return;

        foreach (var layer in controller.layers)
        {
            AddStatesFromStateMachine(layer.stateMachine);
        }

        _animatorStates.Sort((a, b) => a.name.CompareTo(b.name));
    }

    private void AddStatesFromStateMachine(AnimatorStateMachine stateMachine)
    {
        foreach (var state in stateMachine.states)
        {
            if (!_animatorStates.Any(s => s.hash == state.state.nameHash))
            {
                _animatorStates.Add((state.state.name, state.state.nameHash));
            }
        }

        foreach (var subMachine in stateMachine.stateMachines)
        {
            AddStatesFromStateMachine(subMachine.stateMachine);
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Select a GameObject to find an Animator.", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (_selectedObject == null)
        {
            EditorGUILayout.HelpBox("No GameObject selected.", MessageType.Info);
            return;
        }

        if (_targetAnimator == null)
        {
            EditorGUILayout.HelpBox($"No Animator found on '{_selectedObject.name}' or any of its parents.", MessageType.Warning);
            return;
        }

        // Display which object was selected and which one has the Animator being controlled.
        EditorGUILayout.LabelField("Selected Object:", _selectedObject.name);
        EditorGUILayout.LabelField("Animator On:", _animatorOwnerObject.name);
        EditorGUILayout.Space();
        
        _crossfadeDuration = EditorGUILayout.FloatField("Crossfade Duration", _crossfadeDuration);
        _crossfadeDuration = Mathf.Max(0, _crossfadeDuration);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Available States", EditorStyles.boldLabel);
        
        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to control the animator.", MessageType.Info);
            return;
        }
        
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        {
            foreach (var state in _animatorStates)
            {
                if (GUILayout.Button(state.name))
                {
                    if (_targetAnimator != null)
                    {
                        _targetAnimator.CrossFadeInFixedTime(state.hash, _crossfadeDuration);
                    }
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }
}
