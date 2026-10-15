using System.Threading.Tasks;
using UnityEngine;

public class HotFolderChecker : IDeviceChecker, IAsyncDeviceChecker
{
    public string DeviceName => "HotFolderPrint";
    public DeviceType Type => DeviceType.SOFTWARE;

    // TEST uniquement (cf. CalibrationManager._debugHotFolderHangSeconds, éditeur seulement) :
    // durée artificielle de chaque scan, pour reproduire un contrôle qui ne répond pas.
    private readonly float _debugHangSeconds;

    public HotFolderChecker(float debugHangSeconds = 0f) => _debugHangSeconds = debugHangSeconds;

    public bool IsConnected()
    {
        string path = GlobalSettings.Core.GlobalSettings.Instance.HotFolderEXEPath.Value;
        if (string.IsNullOrEmpty(path)) return false;

        string processName = "HotFolderPrint";

        return AppLauncher.IsProcessLaunched(processName);
    }

    // Le chemin est lu ICI, sur le fil principal (GlobalSettings est un singleton de scène) —
    // seul le scan de processus, potentiellement lent, part sur le fil d'arrière-plan.
    public Task<bool> IsConnectedAsync()
    {
        string path = GlobalSettings.Core.GlobalSettings.Instance.HotFolderEXEPath.Value;
        float hang = _debugHangSeconds;
        if (string.IsNullOrEmpty(path) && hang <= 0f) return Task.FromResult(false);

        return Task.Run(() =>
        {
            // Simulation d'un scan qui ne répond pas — sur le fil d'arrière-plan, là où un vrai
            // scan lent bloquerait.
            if (hang > 0f) System.Threading.Thread.Sleep((int)(hang * 1000f));
            return !string.IsNullOrEmpty(path) && AppLauncher.IsProcessLaunched("HotFolderPrint");
        });
    }

    public void RefreshDevice() { }
}
