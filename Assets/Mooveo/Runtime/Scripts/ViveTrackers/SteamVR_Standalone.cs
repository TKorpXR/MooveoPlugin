using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Valve.VR;

public class SteamVR_Standalone : MonoBehaviour {
    public static SteamVR_Standalone Instance;
    public static event Action<TrackedDevicePose_t[]> OnNewPose;

    public TrackedDevicePose_t[] poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    public TrackedDevicePose_t[] gamePoses = new TrackedDevicePose_t[0];

    private static readonly TrackedDevicePose_t[] _directPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

    /// <summary>Diffuse des poses à tous les <see cref="SteamVR_Tracker"/> abonnés.</summary>
    public static void PublishPoses(TrackedDevicePose_t[] devicePoses) => OnNewPose?.Invoke(devicePoses);

    /// <summary>
    /// Lit les poses ACTUELLES directement dans OpenVR (sans passer par le compositeur) et les diffuse.
    /// ⚠ À utiliser quand l'affichage XR est coupé (écran seul, mode headless) : le compositeur ne
    /// rafraîchit plus ses poses, <c>GetLastPoses</c> rendrait des poses figées. Dans ce cas, désactiver
    /// ce composant et appeler cette méthode à chaque frame. Faux si OpenVR n'est pas initialisé.
    /// </summary>
    public static bool PublishCurrentPoses(ETrackingUniverseOrigin origin = ETrackingUniverseOrigin.TrackingUniverseStanding)
    {
        CVRSystem system = OpenVR.System;
        if (system == null) return false;
        system.GetDeviceToAbsoluteTrackingPose(origin, 0f, _directPoses);
        OnNewPose?.Invoke(_directPoses);
        return true;
    }

    void UpdatePoses() {
        OpenVR.Compositor?.GetLastPoses(poses, gamePoses);
        OnNewPose?.Invoke(poses);
    }

    void OnEnable() {
        Instance = this;
        RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
    }
    void OnDisable() => RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
    void Update() => UpdatePoses();
    void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras) => UpdatePoses();
}
