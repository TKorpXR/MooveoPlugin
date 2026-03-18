using UnityEngine;
using UnityEngine.XR;

public class HMDChecker : IDeviceChecker
{
    private InputDevice _device;
    
    public string DeviceName => _device.name;
    public DeviceType Type => DeviceType.HEADSET;

    public HMDChecker()
    {
        RefreshDevice();
    }

    public void RefreshDevice()
    {
        _device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
    }

    public bool IsConnected()
    {
        if (!_device.isValid)
        {
            RefreshDevice();
        }
        
        if (!_device.isValid)
            return false;

        if (_device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked))
            return tracked;

        return false;
    }
}
