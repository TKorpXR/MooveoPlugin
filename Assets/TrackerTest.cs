using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class TrackerTest : MonoBehaviour
{
    [Header("Infos (Lecture Seule)")]
    [SerializeField] private string _trackerName = "Non détecté";
    
    private TrackedDevice _myTracker;

    void Update()
    {
        // 1. Si on n'a pas encore accroché le tracker, on le cherche
        if (_myTracker == null || !_myTracker.added)
        {
            FindTracker();
        }

        // 2. Si on a trouvé le tracker, on lit ses données brutes en temps réel
        if (_myTracker != null)
        {
            // Lecture de la position (Exactement ce que tu vois dans le Debugger)
            var posControl = _myTracker.GetChildControl<Vector3Control>("devicePosition");
            if (posControl != null)
            {
                transform.localPosition = posControl.ReadValue();
            }

            // Lecture de la rotation
            var rotControl = _myTracker.GetChildControl<QuaternionControl>("deviceRotation");
            if (rotControl != null)
            {
                transform.localRotation = rotControl.ReadValue();
            }
        }
    }

    private void FindTracker()
    {
        // On scanne tous les périphériques que l'Input Debugger d'Unity connaît
        foreach (var device in InputSystem.devices)
        {
            // Si c'est un appareil de Tracking ET qu'il contient le mot "Tracker"
            if (device is TrackedDevice && device.name.IndexOf("Tracker", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _myTracker = device as TrackedDevice;
                _trackerName = _myTracker.name;
                Debug.Log($"[DirectTrackerReader] 🎯 Tracker accroché avec succès : {_trackerName}");
                break; // On a trouvé notre tracker, on arrête de chercher
            }
        }
    }
}
