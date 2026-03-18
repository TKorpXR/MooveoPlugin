using UnityEngine;
using UnityEngine.XR;

public class LeftControllerChecker : IDeviceChecker
{
    private InputDevice _device;
    
    public string DeviceName => _device.name;
    public DeviceType Type => DeviceType.CONTROLLER;

    public LeftControllerChecker()
    {
        RefreshDevice();
    }

    public void RefreshDevice()
    {
        _device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
    }

    public bool IsConnected()
    {
        // Si le device n’existe plus → on tente de le récupérer
        if (!_device.isValid)
        {
            RefreshDevice();
        }

        // Après refresh → il peut être valide ou non
        if (!_device.isValid)
            return false;

        if (_device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked))
            return tracked;

        return false;
    }
}
