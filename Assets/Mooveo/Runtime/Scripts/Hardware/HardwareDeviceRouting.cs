using System.Linq;
using UnityEngine;
using Valve.VR;

/// <summary>
/// Ce que le PROFIL MATÉRIEL change à la découverte d'un appareil et à son branchement sur un
/// peintre ou un testeur de calibration.
///
/// ⚠ <c>PainterSpawner.CheckAndSpawn</c> et <c>CalibrationManager.CheckAndSpawnTester</c> dupliquent
/// la même classification. Tout ce qui dépend du profil passe ICI, pour que les deux chaînes ne
/// divergent pas. En profil Vive, chaque méthode est neutre : le chemin des trackers ne change pas.
/// </summary>
public static class HardwareDeviceRouting
{
    /// <summary>
    /// Vrai si le profil écarte cet appareil. Profil PICO : seule une MANETTE, identifiée main gauche
    /// ou main droite, peut devenir une bombe — le casque est réel désormais, et un appareil sans
    /// rôle n'est pas une bombe.
    /// </summary>
    public static bool Rejects(UnityEngine.InputSystem.InputDevice device, out string reason)
    {
        reason = null;
        HardwareProfileData profile = HardwareProfiles.Current;
        if (!profile.OnlyControllersArePainters) return false;

        uint index = OpenVRUtility.GetDeviceIndexBySerialNumber(device.description.serial);
        if (index != OpenVR.k_unTrackedDeviceIndexInvalid && OpenVR.System != null)
        {
            ETrackedDeviceClass deviceClass = OpenVR.System.GetTrackedDeviceClass(index);
            if (deviceClass != ETrackedDeviceClass.Controller)
            {
                reason = $"classe OpenVR {deviceClass}, seule une manette peut peindre ({profile.DisplayName})";
                return true;
            }
        }

        bool handed = device.usages.Contains(UnityEngine.InputSystem.CommonUsages.LeftHand)
                   || device.usages.Contains(UnityEngine.InputSystem.CommonUsages.RightHand)
                   || OpenVRUtility.GetRoleBySerialNumber(device.description.serial) != ETrackedControllerRole.Invalid;
        if (!handed)
        {
            reason = $"aucun rôle main gauche / droite ({profile.DisplayName})";
            return true;
        }
        return false;
    }

    /// <summary>
    /// Un appareil non identifié peut-il être forcé en main droite ? Oui pour les trackers (un
    /// tracker sans rôle reste une bombe), non en profil PICO (cf. <see cref="Rejects"/>).
    /// </summary>
    public static bool AllowsForcedRightHand => !HardwareProfiles.Current.OnlyControllersArePainters;

    /// <summary>
    /// Index OpenVR de l'appareil : par numéro de série, et en profil PICO, par rôle si la série ne
    /// donne rien.
    /// </summary>
    public static uint ResolveOpenVRIndex(string serial, ETrackedControllerRole role)
    {
        uint index = OpenVRUtility.GetDeviceIndexBySerialNumber(serial);
        if (index != OpenVR.k_unTrackedDeviceIndexInvalid) return index;
        if (!HardwareProfiles.Current.ResolveIndexByRole || OpenVR.System == null) return index;
        return OpenVR.System.GetTrackedDeviceIndexForControllerRole(role);
    }

    /// <summary>Réglages du <see cref="SteamVR_Tracker"/> propres au profil.</summary>
    public static void ConfigureTracker(SteamVR_Tracker tracker)
    {
        if (tracker == null) return;
        // Ne touche au prefab que si le profil le demande : en Vive la valeur sérialisée reste.
        if (HardwareProfiles.Current.ResolveIndexByRole) tracker.resolveIndexByRole = true;
    }

    /// <summary>
    /// Place la buse (fils « Spray ») selon le profil. Profil Vive : rien, le prefab porte le réglage
    /// d'origine pour un tracker monté sur la bombe.
    /// </summary>
    public static void ApplyNozzle(Transform nozzle)
    {
        HardwareProfileData profile = HardwareProfiles.Current;
        if (nozzle == null || !profile.OverrideNozzle) return;
        nozzle.localPosition = profile.NozzleLocalPosition;
        nozzle.localRotation = Quaternion.Euler(profile.NozzleLocalEuler);
    }
}
