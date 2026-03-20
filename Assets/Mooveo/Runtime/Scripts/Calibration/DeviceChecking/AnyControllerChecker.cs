using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;
using InputDevice = UnityEngine.XR.InputDevice;

public class AnyControllerChecker : IDeviceChecker
{
    private InputDevice[] _devices = new InputDevice[2];
    private TrackedDevice _tracker;
    
    public string DeviceName => _devices[0].isValid ? _devices[0].name : (_devices[1].isValid ? _devices[1].name : "No Controllers");
    public DeviceType Type => DeviceType.CONTROLLER;

    public AnyControllerChecker()
    {
        RefreshDevice();
    }

    public void RefreshDevice()
    {
        _devices[0] = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        _devices[1] = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
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
    }

    public bool IsConnected()
    {
        foreach (InputDevice device in _devices)
        {
            if (device.isValid)
            {
                if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked))
                    return tracked;
            }
        }
        if (_tracker == null || !_tracker.added)
        {
            RefreshDevice();
        }
        
        return _tracker != null && _tracker.added;
    }
}
