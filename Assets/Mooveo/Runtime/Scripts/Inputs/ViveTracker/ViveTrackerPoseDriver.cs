using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

public class ViveTrackerPoseDriver : MonoBehaviour
{
    [Header("Infos (Lecture Seule)")]
    [SerializeField] private string _trackerName = "Non détecté";

    [Header("Références optionnelles")]
    [SerializeField] private bool _debug = false;
    
    private TrackedDevice _myTracker;

    /// <summary>
    /// Appelé par le ViveTrackerManager lors de l'instanciation
    /// </summary>
    public void Init(TrackedDevice tracker)
    {
        _myTracker = tracker;
        _trackerName = tracker.displayName;

        if (_debug) Debug.Log($"[ViveTrackerPoseDriver] ✔️ Initialisé pour écouter le tracker : {_trackerName}");
    }

    void Update()
    {
        if (_myTracker == null || !_myTracker.added) return;
        
        var posControl = _myTracker.GetChildControl<Vector3Control>("deviceposition") 
                      ?? _myTracker.GetChildControl<Vector3Control>("devicePosition")
                      ?? _myTracker.GetChildControl<Vector3Control>("position");

        if (posControl != null) transform.localPosition = posControl.ReadValue();
        
        var rotControl = _myTracker.GetChildControl<QuaternionControl>("devicerotation") 
                      ?? _myTracker.GetChildControl<QuaternionControl>("deviceRotation")
                      ?? _myTracker.GetChildControl<QuaternionControl>("rotation");

        if (rotControl != null) transform.localRotation = rotControl.ReadValue();
    }

    private void OnDrawGizmos()
    {
        if (!_debug) return;
        Debug.DrawRay(transform.position, -transform.up * 10f, Color.red);
    }
}
