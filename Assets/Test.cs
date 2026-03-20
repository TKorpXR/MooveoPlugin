using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

public class Test : MonoBehaviour
{
    [Header("Infos (Lecture Seule)")]
    [SerializeField] private string _trackerName = "Non détecté";

    [SerializeField] private TrackedPoseDriver _trackedPoseDriver;
    [SerializeField] private GameObject _controller;
    
    private TrackedDevice _myTracker;
    private bool _debugPrinted = false;

    void Update()
    {
        // 1. On cherche le tracker
        if (_myTracker == null || !_myTracker.added)
        {
            FindTracker();
        }

        // 2. Si on a le tracker, on lit ses données (avec gestion des majuscules/minuscules)
        if (_myTracker != null)
        {
            // On essaie tous les noms possibles pour la Position
            var posControl = _myTracker.GetChildControl<Vector3Control>("deviceposition") 
                          ?? _myTracker.GetChildControl<Vector3Control>("devicePosition")
                          ?? _myTracker.GetChildControl<Vector3Control>("position");

            if (posControl != null) transform.localPosition = posControl.ReadValue();

            // On essaie tous les noms possibles pour la Rotation
            var rotControl = _myTracker.GetChildControl<QuaternionControl>("devicerotation") 
                          ?? _myTracker.GetChildControl<QuaternionControl>("deviceRotation")
                          ?? _myTracker.GetChildControl<QuaternionControl>("rotation");

            if (rotControl != null) transform.localRotation = rotControl.ReadValue();
        }
    }

    private void FindTracker()
    {
        foreach (var device in InputSystem.devices)
        {
            if (device is TrackedDevice trackedDevice)
            {
                // On fusionne tous les noms possibles de l'appareil en minuscules
                string searchString = (device.name + device.displayName + device.layout).ToLower();

                // On cherche les mots clés "tracker" ou "device1" (le nom virtuel de VIU)
                if (searchString.Contains("tracker") || searchString.Contains("device1"))
                {
                    _myTracker = trackedDevice;
                    _trackerName = device.displayName;
                    Debug.Log($"[DirectTrackerReader] 🎯 Tracker trouvé : {_trackerName} (Layout: {device.layout})");

                    if (_trackedPoseDriver != null) _trackedPoseDriver.enabled = false;
                    if(_controller != null) _controller.SetActive(true);
                    return;
                }
            }
        }
        
        // Mode Diagnostic : Si rien n'a marché, on affiche la liste des appareils pour comprendre
        if (!_debugPrinted)
        {
            Debug.LogWarning("⚠️ Aucun tracker trouvé ! Voici la liste des appareils de Tracking vus par Unity :");
            foreach (var d in InputSystem.devices)
            {
                if (d is TrackedDevice)
                {
                    Debug.Log($"🔍 Appareil vu -> Name: '{d.name}' | DisplayName: '{d.displayName}' | Layout: '{d.layout}'");
                }
            }
            _debugPrinted = true;
        }
    }

    private void OnDrawGizmos()
    {
        Debug.DrawRay(transform.position, -transform.up * 100f);
    }
}
