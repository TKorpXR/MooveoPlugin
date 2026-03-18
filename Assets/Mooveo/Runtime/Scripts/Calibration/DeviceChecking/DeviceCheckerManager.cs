using System.Collections.Generic;
using UnityEngine;

public class DeviceCheckerManager
{
    private List<IDeviceChecker> _checkers = new List<IDeviceChecker>();

    public DeviceCheckerManager()
    {
        _checkers.Add(new HMDChecker());
        _checkers.Add(new LeftControllerChecker());
        _checkers.Add(new RightControllerChecker());
        
        _checkers.Add(new SteamVRChecker());
        _checkers.Add(new EosUtilitychecker());
        _checkers.Add(new HotFolderChecker());
    }

    public Dictionary<string, bool> CheckAllDevices()
    {
        Dictionary<string, bool> result = new Dictionary<string, bool>();

        foreach (IDeviceChecker checker in _checkers)
        {
            result[checker.DeviceName] = checker.IsConnected();
        }

        return result;
    }
}
