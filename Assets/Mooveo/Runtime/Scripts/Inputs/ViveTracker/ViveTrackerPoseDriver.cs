using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using HTC.UnityPlugin.VRModuleManagement; // Ajout indispensable pour VIU

public class ViveTrackerPoseDriver : MonoBehaviour
{
    [Header("Infos (Lecture Seule)")]
    [SerializeField] private string _trackerName = "Non détecté";

    [Header("Références optionnelles")]
    [SerializeField] private TrackedPoseDriver _trackedPoseDriver;
    [SerializeField] private GameObject _controller;
    [SerializeField] private bool _debug = false;
    
    // Variables pour l'Input System
    private InputDevice _myTrackerInputDevice;
    
    // Variables pour HTC VIU
    private uint _viuDeviceIndex = VRModule.INVALID_DEVICE_INDEX;
    private bool _isUsingVIU = false;

    /// <summary>
    /// Initialisation via l'Input System d'Unity
    /// </summary>
    public void InitWithInputSystem(InputDevice tracker)
    {
        _myTrackerInputDevice = tracker;
        _trackerName = tracker.displayName;
        _isUsingVIU = false;
        CompleteInit();
    }

    /// <summary>
    /// Initialisation directe via l'API matérielle de HTC VIU
    /// </summary>
    public void InitWithVIU(uint deviceIndex, string roleName)
    {
        _viuDeviceIndex = deviceIndex;
        _trackerName = $"VIU_{roleName}";
        _isUsingVIU = true;
        CompleteInit();
    }

    private void CompleteInit()
    {
        if (_trackedPoseDriver != null) _trackedPoseDriver.enabled = false;
        if (_controller != null) _controller.SetActive(true);
        if (_debug) Debug.Log($"[ViveTrackerPoseDriver] ✔️ Initialisé : {_trackerName}");
    }

    void Update()
    {
        if (_isUsingVIU) UpdateWithVIU();
        else UpdateWithInputSystem();
    }

    private void UpdateWithVIU()
    {
        if (_viuDeviceIndex == VRModule.INVALID_DEVICE_INDEX) return;

        // Lecture directe depuis la DLL C++ de HTC / SteamVR !
        IVRModuleDeviceState state = VRModule.GetCurrentDeviceState(_viuDeviceIndex);
        
        if (state != null && state.isConnected && state.isPoseValid)
        {
            transform.localPosition = state.position;
            transform.localRotation = state.rotation;
        }
    }

    private void UpdateWithInputSystem()
    {
        if (_myTrackerInputDevice == null || !_myTrackerInputDevice.added) return;

        var posControl = _myTrackerInputDevice.GetChildControl<Vector3Control>("deviceposition") 
                      ?? _myTrackerInputDevice.GetChildControl<Vector3Control>("devicePosition")
                      ?? _myTrackerInputDevice.GetChildControl<Vector3Control>("position");

        if (posControl != null) transform.localPosition = posControl.ReadValue();

        var rotControl = _myTrackerInputDevice.GetChildControl<QuaternionControl>("devicerotation") 
                      ?? _myTrackerInputDevice.GetChildControl<QuaternionControl>("deviceRotation")
                      ?? _myTrackerInputDevice.GetChildControl<QuaternionControl>("rotation");

        if (rotControl != null) transform.localRotation = rotControl.ReadValue();
    }

    private void OnDrawGizmos()
    {
        if (!_debug) return;
        Debug.DrawRay(transform.position, -transform.up * 10f, Color.red);
    }
}