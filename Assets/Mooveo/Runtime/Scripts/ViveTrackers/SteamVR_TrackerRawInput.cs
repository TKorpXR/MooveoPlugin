using UnityEngine;
using Valve.VR;
    
public class SteamVR_TrackerRawInput : MonoBehaviour
{
[Header("Configuration")]
    [Tooltip("L'index de l'appareil (doit correspondre au Device de ton SteamVR_Tracker)")]
    public SteamVR_Tracker.Device targetDevice = SteamVR_Tracker.Device.Device1;

    [Header("Événements de Test (Console)")]
    public bool debugLogs = true;

    // Masques binaires
    private readonly ulong triggerMask = 1ul << (int)EVRButtonId.k_EButton_SteamVR_Trigger; // (33)
    private readonly ulong gripMask    = 1ul << (int)EVRButtonId.k_EButton_Grip;            // (2)
    private readonly ulong menuMask    = 1ul << (int)EVRButtonId.k_EButton_ApplicationMenu; // (1)
    
    private ulong _previousButtons = 0;
    private float _previousTriggerVal = 0f;

    void Update()
    {
        var system = OpenVR.System;
        if (system == null || targetDevice == SteamVR_Tracker.Device.None) return;

        // --- 1. LA POMPE À ÉVÉNEMENTS (LE FIX EST ICI) ---
        // Obligatoire quand on n'a plus le gros plugin SteamVR. 
        // Force OpenVR à rafraîchir les données matérielles.
        VREvent_t vrEvent = new VREvent_t();
        uint eventSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VREvent_t));
        while (system.PollNextEvent(ref vrEvent, eventSize))
        {
            // On vide simplement la file d'attente.
        }
        // --------------------------------------------------

        uint deviceIndex = (uint)targetDevice;
        VRControllerState_t state = new VRControllerState_t();
        uint size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRControllerState_t));
        
        // --- 2. LECTURE DE L'ÉTAT RAFRAÎCHI ---
        if (system.GetControllerState(deviceIndex, ref state, size))
        {
            ulong currentButtons = state.ulButtonPressed;

            // Débogueur Global : Si n'importe quel bouton est pressé, on l'affiche (utile pour débugger les Pogo Pins)
            if (currentButtons != 0 && currentButtons != _previousButtons && debugLogs)
            {
                Debug.Log($"<color=white>[SCAN RAW]</color> Un signal électrique est détecté ! Masque binaire : {currentButtons}");
            }

            bool curTrigger = (currentButtons & triggerMask) != 0;
            bool prevTrigger = (_previousButtons & triggerMask) != 0;
            if (curTrigger && !prevTrigger && debugLogs) Debug.Log($"<color=cyan>[Tracker {targetDevice}]</color> Trigger PRESSÉ");
            if (!curTrigger && prevTrigger && debugLogs) Debug.Log($"<color=cyan>[Tracker {targetDevice}]</color> Trigger RELÂCHÉ");

            bool curGrip = (currentButtons & gripMask) != 0;
            bool prevGrip = (_previousButtons & gripMask) != 0;
            if (curGrip && !prevGrip && debugLogs) Debug.Log($"<color=cyan>[Tracker {targetDevice}]</color> Grip PRESSÉ");
            if (!curGrip && prevGrip && debugLogs) Debug.Log($"<color=cyan>[Tracker {targetDevice}]</color> Grip RELÂCHÉ");

            bool curMenu = (currentButtons & menuMask) != 0;
            bool prevMenu = (_previousButtons & menuMask) != 0;
            if (curMenu && !prevMenu && debugLogs) Debug.Log($"<color=cyan>[Tracker {targetDevice}]</color> Menu PRESSÉ");
            if (!curMenu && prevMenu && debugLogs) Debug.Log($"<color=cyan>[Tracker {targetDevice}]</color> Menu RELÂCHÉ");

            _previousButtons = currentButtons;

            // Axe gâchette (Analogique)
            float triggerAnalog = state.rAxis1.x;
            if (Mathf.Abs(triggerAnalog - _previousTriggerVal) > 0.05f)
            {
                if (debugLogs) Debug.Log($"<color=yellow>[Tracker {targetDevice}]</color> Gâchette : {triggerAnalog:F2}");
                _previousTriggerVal = triggerAnalog;
            }
        }
    }
}
