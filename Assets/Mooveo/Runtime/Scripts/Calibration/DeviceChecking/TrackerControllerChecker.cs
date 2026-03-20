using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

public class TrackerControllerChecker : IDeviceChecker
{
    private TrackedDevice _tracker;
    
    public string DeviceName => _tracker?.displayName ?? "Non détecté";
    public DeviceType Type => DeviceType.TRACKER;

    public TrackerControllerChecker()
    {
        RefreshDevice();
    }

    public void RefreshDevice()
    {
        _tracker = null;
        
        foreach (var device in InputSystem.devices)
        {
            if (device is TrackedDevice trackedDevice)
            {
                string searchString = (device.name + device.displayName + device.layout).ToLower();
                
                if (searchString.Contains("tracker") || searchString.Contains("device1"))
                {
                    _tracker = trackedDevice;
                    //Debug.Log($"[TrackerControllerChecker] 🎯 Tracker trouvé : {device.displayName} (Layout: {device.layout})");
                    return;
                }
            }
        }
        
        /*if (!_debugPrinted)
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
        }*/
    }

    public bool IsConnected()
    {
        if (_tracker == null || !_tracker.added)
        {
            RefreshDevice();
        }
        
        return _tracker != null && _tracker.added;
    }
}
