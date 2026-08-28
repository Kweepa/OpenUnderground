using UnityEditor;
using UnityEngine;

// to use this
// add a prefab to an empty scene
// add rigidbodies to the objects you want to simulate
// add the prefab for the objects as children
// run the physics
// then click on the dots on the object transform and select "Modified Component... / Apply to Prefab

namespace Kweepa.EditorPhysicsSimulator
{
    public class EditorPhysicsSimulation : EditorWindow
    {
        bool isPlaying = false;

        private void OnEnable()
        {
            Undo.undoRedoEvent += undoRedoEvent;
        }

        private void OnDisable()
        {
            Undo.undoRedoEvent -= undoRedoEvent;
            Stop();
        }

        private void undoRedoEvent(in UndoRedoInfo undo)
        {
            Stop();
        }

        private void OnGUI()
        {
            if (isPlaying == false)
            {
                if (GUILayout.Button("►"))
                {
                    isPlaying = true;
                    EditorApplication.update += StepPhysics;
                }
            }
            else
            {
                if (GUILayout.Button("■")) Stop();
            }
        }

        void Stop()
        {
            isPlaying = false;
            EditorApplication.update -= StepPhysics;
        }

        private void StepPhysics()
        {
            var simMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Physics.Simulate(Time.fixedDeltaTime);
            Physics.simulationMode = simMode;
        }

        [MenuItem("Tools/Physics Simulator")]
        private static void OpenWindow()
        {
            GetWindow<EditorPhysicsSimulation>(false, "Physics", true);
        }
    }
}
