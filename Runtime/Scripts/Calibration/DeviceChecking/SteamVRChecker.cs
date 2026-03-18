using System.Diagnostics;

public class SteamVRChecker : IDeviceChecker
{
    public string DeviceName => "SteamVR";
    public DeviceType Type => DeviceType.SOFTWARE;

    public bool IsConnected()
    {
        return AppLauncher.IsProcessLaunched("vrserver") 
               || AppLauncher.IsProcessLaunched("vrmonitor") 
               || AppLauncher.IsProcessLaunched("vrcompositor");
    }

    public void RefreshDevice() { }
}
