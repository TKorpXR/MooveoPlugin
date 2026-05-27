using UnityEngine;
using UnityEngine.XR;
public class AnyControllerChecker : IDeviceChecker
{
    private InputDevice[] _devices = new InputDevice[2];
    
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
        RefreshDevice();
        return false;
    }
}
