using UnityEngine;

public class EosUtilitychecker : IDeviceChecker
{
    public string DeviceName => "EOS Utility";
    public DeviceType Type => DeviceType.SOFTWARE;

    public bool IsConnected()
    {
        // Récupère le chemin configuré dans GlobalSettings
        string path = GlobalSettings.Core.GlobalSettings.Instance.EosUtilityEXEPath.Value;
        if (string.IsNullOrEmpty(path)) return false;
        
        string processName = "EOS Utility";
        
        return AppLauncher.IsProcessLaunched(processName);
    }

    public void RefreshDevice() { }
}
