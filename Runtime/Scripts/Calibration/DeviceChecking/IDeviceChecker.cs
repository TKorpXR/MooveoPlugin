using UnityEngine;

public interface IDeviceChecker
{
    string DeviceName { get; }
    DeviceType Type { get; }
    bool IsConnected();
    void RefreshDevice();

}

public enum DeviceType
{
    NULL,
    SOFTWARE,
    CONTROLLER,
    TRACKER,
    HEADSET
}