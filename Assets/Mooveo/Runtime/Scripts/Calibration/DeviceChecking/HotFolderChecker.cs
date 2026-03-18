using UnityEngine;

public class HotFolderChecker : IDeviceChecker
{
    public string DeviceName => "HotFolderPrint";
    public DeviceType Type => DeviceType.SOFTWARE;

    public bool IsConnected()
    {
        string path = GlobalSettings.Core.GlobalSettings.Instance.HotFolderEXEPath.Value;
        if (string.IsNullOrEmpty(path)) return false;

        string processName = "HotFolderPrint";
        
        return AppLauncher.IsProcessLaunched(processName);
    }

    public void RefreshDevice() { }
}
