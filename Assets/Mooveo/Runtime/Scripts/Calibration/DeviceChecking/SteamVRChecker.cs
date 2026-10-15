using System.Diagnostics;
using System.Threading.Tasks;

public class SteamVRChecker : IDeviceChecker, IAsyncDeviceChecker
{
    public string DeviceName => "SteamVR";
    public DeviceType Type => DeviceType.SOFTWARE;

    public bool IsConnected()
    {
        return AppLauncher.IsProcessLaunched("vrserver")
               || AppLauncher.IsProcessLaunched("vrmonitor")
               || AppLauncher.IsProcessLaunched("vrcompositor");
    }

    // `Process.GetProcessesByName` (derrière `AppLauncher.IsProcessLaunched`) est un appel OS
    // connu pour parfois traîner (énumération WMI) : aucune API Unity ici, sûr hors fil principal.
    public Task<bool> IsConnectedAsync() => Task.Run(IsConnected);

    public void RefreshDevice() { }
}
