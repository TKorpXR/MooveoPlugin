using System.Text;
using Valve.VR;

public static class OpenVRUtility
{
    /// <summary>
    /// Récupère le numéro de série d'un appareil via son index OpenVR.
    /// </summary>
    public static string GetSerialNumber(uint deviceIndex)
    {
        if (OpenVR.System == null) return string.Empty;
        var error = ETrackedPropertyError.TrackedProp_Success;
        var sb = new StringBuilder(64);
        OpenVR.System.GetStringTrackedDeviceProperty(deviceIndex, ETrackedDeviceProperty.Prop_SerialNumber_String, sb, (uint)sb.Capacity, ref error);
        return sb.ToString();
    }
    
    /// <summary>
    /// Trouve l'index OpenVR (0 à 16) d'un appareil à partir de son numéro de série (fourni par Unity).
    /// </summary>
    public static uint GetDeviceIndexBySerialNumber(string serialNumber)
    {
        if (OpenVR.System == null || string.IsNullOrEmpty(serialNumber)) return OpenVR.k_unTrackedDeviceIndexInvalid;
        
        for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
        {
            if (GetSerialNumber(i) == serialNumber)
            {
                return i;
            }
        }
        return OpenVR.k_unTrackedDeviceIndexInvalid;
    }
    
    /// <summary>
    /// Convertit l'index OpenVR (uint) vers l'Enum SteamVR_Tracker.Device.
    /// </summary>
    public static SteamVR_Tracker.Device GetSteamVRTrackerDevice(uint deviceIndex)
    {
        if (deviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid) return SteamVR_Tracker.Device.None;
        
        return (SteamVR_Tracker.Device)(int)deviceIndex;
    }
    
    /// <summary>
    /// Interroge directement SteamVR pour savoir si le numéro de série correspond à la main gauche ou droite.
    /// </summary>
    public static ETrackedControllerRole GetRoleBySerialNumber(string serialNumber)
    {
        if (OpenVR.System == null || string.IsNullOrEmpty(serialNumber)) return ETrackedControllerRole.Invalid;

        uint leftIndex = OpenVR.System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);
        uint rightIndex = OpenVR.System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);

        uint myIndex = GetDeviceIndexBySerialNumber(serialNumber);

        if (myIndex != OpenVR.k_unTrackedDeviceIndexInvalid)
        {
            if (myIndex == leftIndex) return ETrackedControllerRole.LeftHand;
            if (myIndex == rightIndex) return ETrackedControllerRole.RightHand;
        }

        return ETrackedControllerRole.Invalid;
    }
}
