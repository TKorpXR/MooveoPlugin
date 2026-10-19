using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using Valve.VR;

/// <summary>
/// Quels appareils deviennent des joueurs selon le profil matériel courant, et quelle source suit
/// leur position. Règles communes à tout ce qui crée un joueur depuis un appareil (menu, spawner du
/// jeu) — à consommer de préférence via <see cref="MooveoDeviceWatcher"/>.
/// Ce qui ne dépend QUE du profil reste dans <see cref="HardwareDeviceRouting"/>.
/// </summary>
public static class MooveoDeviceRouting
{
    /// <summary>Vrai si les manettes sont suivies par OpenVR (GlobalSettings.Headless).</summary>
    public static bool IsHeadless =>
        GlobalSettings.Core.GlobalSettings.Instance != null && GlobalSettings.Core.GlobalSettings.Instance.Headless.Value;

    /// <summary>
    /// Vrai si l'appareil peut devenir un joueur avec le profil matériel courant ; donne sa main.
    /// <paramref name="rejectReason"/> est renseigné quand l'appareil est écarté pour une raison utile
    /// à journaliser (null pour ce qui n'est simplement pas un contrôleur : casque, stations…).
    /// </summary>
    public static bool TryGetHand(InputDevice device, out bool isLeftHand, out string rejectReason)
    {
        isLeftHand = false;
        rejectReason = null;

        if (!(device is TrackedDevice)) return false;
        if (device is XRHMD || device.name.Contains("HMD") || device.name.Contains("Head")) return false;

        string cleanName = device.name.Replace(" ", "");
        if (cleanName.Contains("TrackingReference") || cleanName.Contains("ValveSRImp")) return false;

        if (HardwareDeviceRouting.Rejects(device, out rejectReason)) return false;

        bool isLeft = device.usages.Contains(CommonUsages.LeftHand);
        bool isRight = device.usages.Contains(CommonUsages.RightHand);
        if (!isLeft && !isRight)
        {
            ETrackedControllerRole role = OpenVRUtility.GetRoleBySerialNumber(device.description.serial);
            isLeft = role == ETrackedControllerRole.LeftHand;
            isRight = role == ETrackedControllerRole.RightHand;
        }
        if (!isLeft && !isRight)
        {
            isLeft = device.name.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0;
            isRight = !isLeft && device.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
        if (!isLeft && !isRight)
        {
            if (!HardwareDeviceRouting.AllowsForcedRightHand)
            {
                rejectReason = $"aucune main identifiée ({HardwareProfiles.Current.DisplayName})";
                return false;
            }
            isRight = true;
        }

        isLeftHand = isLeft;
        return true;
    }

    /// <summary>
    /// Branche UNE seule source de position sur la racine du contrôleur : TrackedPoseDriver alimenté
    /// par la config de la main (OpenXR), ou SteamVR_Tracker retrouvé par numéro de série (Headless,
    /// lecture des boutons en OVRInput). Les deux composants doivent être présents sur l'instance ;
    /// celui qui ne sert pas est désactivé.
    /// </summary>
    public static void ConfigureTracking(GameObject instance, InputReader reader, InputDevice device, bool isLeftHand,
        InputConfig leftConfig, InputConfig rightConfig)
    {
        var poseDriver = instance.GetComponent<TrackedPoseDriver>();
        var steamTracker = instance.GetComponent<SteamVR_Tracker>();
        InputConfig config = isLeftHand ? leftConfig : rightConfig;

        if (IsHeadless)
        {
            EnsureOpenVRPoseSource();
            ETrackedControllerRole role = isLeftHand ? ETrackedControllerRole.LeftHand : ETrackedControllerRole.RightHand;
            uint index = HardwareDeviceRouting.ResolveOpenVRIndex(device.description.serial, role);
            if (steamTracker != null)
            {
                steamTracker.device = OpenVRUtility.GetSteamVRTrackerDevice(index);
                steamTracker.SetRole(role);
                HardwareDeviceRouting.ConfigureTracker(steamTracker);
                steamTracker.enabled = true;
            }
            else Debug.LogWarning($"[MooveoDeviceRouting] Pas de SteamVR_Tracker sur {instance.name} : position non suivie en headless.");

            if (poseDriver != null) poseDriver.enabled = false;
            reader.TargetRole = role;
            reader.ConfigMode = ConfigMode.OVRInput;
        }
        else
        {
            if (steamTracker != null) steamTracker.enabled = false;

            reader.ConfigMode = ConfigMode.InputConfig;
            reader.SetInputConfig(config);

            if (poseDriver != null && config != null)
            {
                if (config.PositionAction != null) poseDriver.positionInput = new InputActionProperty(config.PositionAction);
                if (config.RotationAction != null) poseDriver.rotationInput = new InputActionProperty(config.RotationAction);
                poseDriver.enabled = true;
            }
            else Debug.LogWarning($"[MooveoDeviceRouting] Pas de TrackedPoseDriver sur {instance.name} : position non suivie.");
        }
    }

    /// <summary>
    /// Sans source de poses OpenVR, les SteamVR_Tracker ne bougent pas : crée un SteamVR_Standalone
    /// s'il manque (un jeu qui publie lui-même les poses, cf. <see cref="SteamVR_Standalone.PublishCurrentPoses"/>,
    /// garde le sien désactivé — il existe, il n'est donc pas recréé).
    /// </summary>
    public static void EnsureOpenVRPoseSource()
    {
        if (SteamVR_Standalone.Instance != null) return;
        Debug.LogWarning("[MooveoDeviceRouting] Aucun SteamVR_Standalone dans la scène : création automatique. Ajoute-le à la scène.");
        new GameObject("SteamVR_Standalone (auto)").AddComponent<SteamVR_Standalone>();
    }
}
