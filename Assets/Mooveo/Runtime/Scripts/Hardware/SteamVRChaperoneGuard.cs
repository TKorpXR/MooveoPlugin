using System;
using UnityEngine;
using Valve.VR;

/// <summary>
/// En mode sans casque, journalise l'univers du casque factice et, si SteamVR signale une pièce non
/// configurée, enregistre une pièce neutre (évite l'assistant de SteamVR, qui ferme GraffWall).
/// S'installe seul au chargement de la première scène.
/// </summary>
public sealed class SteamVRChaperoneGuard : MonoBehaviour
{
    /// <summary>Intervalle entre deux vérifications.</summary>
    private const float CheckIntervalSeconds = 5f;

    /// <summary>Dimensions de la pièce neutre (identiques à celles de <see cref="SteamVRConfig"/>).</summary>
    private const float PlayAreaX = 3f, PlayAreaZ = 2.25f, WallHeight = 2.43f, SeatedHeight = 1.2f;

    private float _nextCheck;
    private ulong _loggedUniverse = ulong.MaxValue;
    private ChaperoneCalibrationState _loggedState;
    private ulong _committedUniverse = ulong.MaxValue;

    /// <summary>Crée le garde-fou, persistant entre les scènes.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Application.isBatchMode) return;
        var host = new GameObject("[SteamVRChaperoneGuard]") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(host);
        host.AddComponent<SteamVRChaperoneGuard>();
    }

    /// <summary>Lance une vérification à intervalle régulier, sans jamais lever.</summary>
    private void Update()
    {
        if (Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + CheckIntervalSeconds;

        try { Check(); }
        catch (Exception e)
        {
            Debug.LogWarning($"[SteamVRChaperoneGuard] Vérification de la configuration de pièce impossible : {e.Message}");
        }
    }

    /// <summary>Journalise univers et état ; enregistre une pièce neutre si l'état est en erreur.</summary>
    private void Check()
    {
        if (!HardwareProfiles.HasPersistedChoice ||
            !string.Equals(HardwareProfiles.Current.SteamVRMode, SteamVRConfig.ModeNoHeadset, StringComparison.OrdinalIgnoreCase))
            return;

        CVRSystem system = OpenVR.System;
        CVRChaperone chaperone = OpenVR.Chaperone;
        if (system == null || chaperone == null) return;

        ETrackedPropertyError error = ETrackedPropertyError.TrackedProp_Success;
        ulong universe = system.GetUint64TrackedDeviceProperty(
            OpenVR.k_unTrackedDeviceIndex_Hmd, ETrackedDeviceProperty.Prop_CurrentUniverseId_Uint64, ref error);
        ChaperoneCalibrationState state = chaperone.GetCalibrationState();

        if (universe != _loggedUniverse || state != _loggedState)
        {
            _loggedUniverse = universe;
            _loggedState = state;
            string expected = SteamVRConfig.NullDriverUniverseId;
            string message = $"[SteamVRChaperoneGuard] Casque factice : univers {universe} ({error}), " +
                             $"configuration de pièce {state} (attendu : univers {expected}, OK).";
            if (universe.ToString() == expected && state == ChaperoneCalibrationState.OK) Debug.Log(message);
            else Debug.LogWarning(message);
        }

        if ((int)state < (int)ChaperoneCalibrationState.Error || _committedUniverse == universe) return;
        _committedUniverse = universe;
        CommitNeutralRoom(universe);
    }

    /// <summary>Enregistre une pièce neutre pour l'univers courant via <c>IVRChaperoneSetup</c>.</summary>
    private static void CommitNeutralRoom(ulong universe)
    {
        CVRChaperoneSetup setup = OpenVR.ChaperoneSetup;
        if (setup == null) return;

        setup.RevertWorkingCopy();
        setup.SetWorkingPlayAreaSize(PlayAreaX, PlayAreaZ);
        setup.SetWorkingCollisionBoundsInfo(Walls());

        HmdMatrix34_t standing = Translation(0f, 0f, 0f);
        HmdMatrix34_t seated = Translation(0f, SeatedHeight, 0f);
        setup.SetWorkingStandingZeroPoseToRawTrackingPose(ref standing);
        setup.SetWorkingSeatedZeroPoseToRawTrackingPose(ref seated);

        bool ok = setup.CommitWorkingCopy(EChaperoneConfigFile.Live);
        if (ok)
            Debug.Log($"[SteamVRChaperoneGuard] Configuration de pièce neutre enregistrée pour l'univers {universe} : " +
                      "l'assistant de SteamVR ne s'ouvrira plus aux prochains démarrages.");
        else
            Debug.LogWarning($"[SteamVRChaperoneGuard] SteamVR a refusé la configuration de pièce de l'univers {universe}.");
    }

    /// <summary>Les quatre murs de la pièce neutre.</summary>
    private static HmdQuad_t[] Walls()
    {
        float x = PlayAreaX / 2f, z = PlayAreaZ / 2f;
        var corners = new[] { (-x, -z), (x, -z), (x, z), (-x, z) };
        var walls = new HmdQuad_t[corners.Length];
        for (int i = 0; i < corners.Length; i++)
        {
            var (x0, z0) = corners[i];
            var (x1, z1) = corners[(i + 1) % corners.Length];
            walls[i] = new HmdQuad_t
            {
                vCorners0 = new HmdVector3_t { v0 = x0, v1 = 0f, v2 = z0 },
                vCorners1 = new HmdVector3_t { v0 = x0, v1 = WallHeight, v2 = z0 },
                vCorners2 = new HmdVector3_t { v0 = x1, v1 = WallHeight, v2 = z1 },
                vCorners3 = new HmdVector3_t { v0 = x1, v1 = 0f, v2 = z1 }
            };
        }
        return walls;
    }

    /// <summary>Matrice de translation pure.</summary>
    private static HmdMatrix34_t Translation(float x, float y, float z) => new HmdMatrix34_t
    {
        m0 = 1f, m1 = 0f, m2 = 0f, m3 = x,
        m4 = 0f, m5 = 1f, m6 = 0f, m7 = y,
        m8 = 0f, m9 = 0f, m10 = 1f, m11 = z
    };
}
