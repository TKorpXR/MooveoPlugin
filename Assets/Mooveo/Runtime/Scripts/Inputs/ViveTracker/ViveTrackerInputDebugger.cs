using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using HTC.UnityPlugin.Vive;
using HTC.UnityPlugin.VRModuleManagement;
using Valve.VR;

public class ViveTrackerInputDebugger : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Désactivez pour arrêter de spammer la console")]
    [SerializeField] private bool _enableDebug = true;
    [Tooltip("Différence minimum pour afficher un changement d'axe")]
    [SerializeField] private float _axisDeltaThreshold = 0.05f;

    [Header("Infos (Lecture Seule)")]
    [SerializeField] private string _trackerName = "Non détecté";

    private InputDevice _myTrackerInputDevice;
    private uint _viuDeviceIndex = VRModule.INVALID_DEVICE_INDEX;
    private bool _isUsingVIU = false;
    
    private Dictionary<string, float> _previousInputSystemAxes = new Dictionary<string, float>();
    private HashSet<int> _previousRawButtons = new HashSet<int>();
    private Dictionary<int, float> _previousRawAxes = new Dictionary<int, float>();

    public void InitWithInputSystem(InputDevice tracker)
    {
        _myTrackerInputDevice = tracker;
        _trackerName = tracker.displayName;
        _isUsingVIU = false;
        _previousInputSystemAxes.Clear();
        Debug.Log($"[InputDebugger] 🕵️‍♂️ Mode Input System : {_trackerName}");
    }

    public void InitWithVIU(uint deviceIndex, string roleName)
    {
        _viuDeviceIndex = deviceIndex;
        _trackerName = $"VIU_{roleName}";
        _isUsingVIU = true;

        _previousRawButtons.Clear();
        _previousRawAxes.Clear();
        Debug.Log($"[InputDebugger] Mode OPENVR RAW  : {_trackerName}");
    }

    void Update()
    {
        if (!_enableDebug) return;

        if (_isUsingVIU) UpdateWithOpenVRRaw();
        else UpdateWithInputSystem();
    }

    private void UpdateWithOpenVRRaw()
    {
        if (_viuDeviceIndex == VRModule.INVALID_DEVICE_INDEX) return;
        
        var system = OpenVR.System;
        if (system == null) return;

        VRControllerState_t state = new VRControllerState_t();
        uint size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRControllerState_t));
        bool success = system.GetControllerState(_viuDeviceIndex, ref state, size);
        
        if (!success) return;
        
        for (int i = 0; i < 64; i++)
        {
            ulong bit = 1ul << i;
            bool isPressed = (state.ulButtonPressed & bit) != 0;
            bool wasPressed = _previousRawButtons.Contains(i);

            if (isPressed && !wasPressed)
            {
                string btnName = ((EVRButtonId)i).ToString().Replace("k_EButton_", "");
                Debug.Log($"<color=magenta>[OPENVR RAW]</color> {_trackerName} | PRESSION : {btnName} (ID: {i})");
                _previousRawButtons.Add(i);
            }
            else if (!isPressed && wasPressed)
            {
                Debug.Log($"<color=magenta>[OPENVR RAW]</color> {_trackerName} | RELÂCHÉ (ID: {i})");
                _previousRawButtons.Remove(i);
            }
        }
        
        float[] currentAxes = new float[] { state.rAxis0.x, state.rAxis1.x, state.rAxis2.x, state.rAxis3.x, state.rAxis4.x };
        for (int i = 0; i < currentAxes.Length; i++)
        {
            if (!_previousRawAxes.ContainsKey(i)) _previousRawAxes[i] = 0f;

            if (Mathf.Abs(currentAxes[i] - _previousRawAxes[i]) > _axisDeltaThreshold)
            {
                Debug.Log($"<color=yellow>[OPENVR RAW]</color> {_trackerName} | AXE {i} : {currentAxes[i]:F2}");
                _previousRawAxes[i] = currentAxes[i];
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
                    Debug.Log($"<color=green>[InputSystem]</color> {button.name} PRESSÉ");
                if (button.wasReleasedThisFrame)
                    Debug.Log($"<color=green>[InputSystem]</color> {button.name} RELÂCHÉ");
            }
        }
    }
}