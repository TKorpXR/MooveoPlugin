using System.Threading.Tasks;
using UnityEngine;

public class EosUtilitychecker : IDeviceChecker, IAsyncDeviceChecker
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

    // Le chemin est lu ICI, sur le fil principal (GlobalSettings est un singleton de scène) —
    // seul le scan de processus, potentiellement lent, part sur le fil d'arrière-plan.
    public Task<bool> IsConnectedAsync()
    {
        string path = GlobalSettings.Core.GlobalSettings.Instance.EosUtilityEXEPath.Value;
        if (string.IsNullOrEmpty(path)) return Task.FromResult(false);

        return Task.Run(() => AppLauncher.IsProcessLaunched("EOS Utility"));
    }

    public void RefreshDevice() { }
}
