using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using HTC.UnityPlugin.Vive;
using HTC.UnityPlugin.VRModuleManagement; // L'accès direct au matériel

public class ViveTrackerInputDebugger : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Désactivez pour arrêter de spammer la console")]
    [SerializeField] private bool _enableDebug = true;
    [Tooltip("Différence minimum pour afficher un changement d'axe (évite le spam)")]
    [SerializeField] private float _axisDeltaThreshold = 0.05f;

    [Header("Infos (Lecture Seule)")]
    [SerializeField] private string _trackerName = "Non détecté";

    // Variables Input System
    private InputDevice _myTrackerInputDevice;

    // Variables HTC VIU
    private uint _viuDeviceIndex = VRModule.INVALID_DEVICE_INDEX;
    private bool _isUsingVIU = false;

    // Caches pour l'Input System
    private Dictionary<string, float> _previousInputSystemAxes = new Dictionary<string, float>();

    // Caches pour le matériel brut (RAW Hardware)
    private HashSet<VRModuleRawButton> _previousRawButtons = new HashSet<VRModuleRawButton>();
    private Dictionary<VRModuleRawAxis, float> _previousRawAxes = new Dictionary<VRModuleRawAxis, float>();

    public void InitWithInputSystem(InputDevice tracker)
    {
        _myTrackerInputDevice = tracker;
        _trackerName = tracker.displayName;
        _isUsingVIU = false;
        _previousInputSystemAxes.Clear();
        Debug.Log($"[InputDebugger] 🕵️‍♂️ Démarré en mode Input System pour : {_trackerName}");
    }

    public void InitWithVIU(uint deviceIndex, string roleName)
    {
        _viuDeviceIndex = deviceIndex;
        _trackerName = $"VIU_{roleName}";
        _isUsingVIU = true;

        _previousRawButtons.Clear();
        _previousRawAxes.Clear();
        foreach (VRModuleRawAxis axis in Enum.GetValues(typeof(VRModuleRawAxis)))
        {
            _previousRawAxes[axis] = 0f;
        }

        Debug.Log($"[InputDebugger] 🕵️‍♂️ Démarré en mode MATÉRIEL BRUT (RAW) pour : {_trackerName} (Index Puce: {_viuDeviceIndex})");
    }

    void Update()
    {
        if (!_enableDebug) return;

        if (_isUsingVIU) UpdateWithRawVIU();
        else UpdateWithInputSystem();
    }

    private void UpdateWithRawVIU()
    {
        if (_viuDeviceIndex == VRModule.INVALID_DEVICE_INDEX) return;
        
        IVRModuleDeviceState state = VRModule.GetCurrentDeviceState(_viuDeviceIndex);

        if (state == null || !state.isConnected) return;
        
        foreach (VRModuleRawButton button in Enum.GetValues(typeof(VRModuleRawButton)))
        {
            bool isPressed = state.GetButtonPress(button);
            bool wasPressed = _previousRawButtons.Contains(button);

            if (isPressed && !wasPressed)
            {
                Debug.Log($"<color=magenta>[RAW HARDWARE]</color> {_trackerName} | BOUTON PRESSÉ : {button}");
                _previousRawButtons.Add(button);
            }
            else if (!isPressed && wasPressed)
            {
                Debug.Log($"<color=magenta>[RAW HARDWARE]</color> {_trackerName} | BOUTON RELÂCHÉ : {button}");
                _previousRawButtons.Remove(button);
            }
        }

        // 2. Écoute des Axes Bruts
        foreach (VRModuleRawAxis axis in Enum.GetValues(typeof(VRModuleRawAxis)))
        {
            float currentValue = state.GetAxisValue(axis);
            
            if (_previousRawAxes.TryGetValue(axis, out float previousValue))
            {
                if (Mathf.Abs(currentValue - previousValue) > _axisDeltaThreshold)
                {
                    Debug.Log($"<color=yellow>[RAW HARDWARE]</color> {_trackerName} | AXE MODIFIÉ : {axis} = {currentValue:F2}");
                    _previousRawAxes[axis] = currentValue;
                }
            }
            else
            {
                _previousRawAxes[axis] = currentValue;
            }
        }
    }

    private void UpdateWithInputSystem()
    {
        if (_myTrackerInputDevice == null || !_myTrackerInputDevice.added) return;

        foreach (var control in _myTrackerInputDevice.allControls)
        {
            if (control is ButtonControl button)
            {
                if (button.wasPressedThisFrame)
                    Debug.Log($"<color=green>[InputSystem DETECT]</color> {_trackerName} | BOUTON PRESSÉ : {button.name}");
                
                if (button.wasReleasedThisFrame)
                    Debug.Log($"<color=green>[InputSystem DETECT]</color> {_trackerName} | BOUTON RELÂCHÉ : {button.name}");
            }
            else if (control is AxisControl axisControl && !control.name.Contains("position") && !control.name.Contains("rotation"))
            {
                float currentValue = axisControl.ReadValue();
                _previousInputSystemAxes.TryGetValue(control.name, out float previousValue);

                if (Mathf.Abs(currentValue - previousValue) > _axisDeltaThreshold)
                {
                    Debug.Log($"<color=yellow>[InputSystem DETECT]</color> {_trackerName} | AXE MODIFIÉ : {control.name} = {currentValue:F2}");
                    _previousInputSystemAxes[control.name] = currentValue;
                }
            }
        }
    }
}