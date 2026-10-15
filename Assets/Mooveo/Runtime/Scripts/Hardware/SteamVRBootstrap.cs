using System.IO;
using UnityEngine;

/// <summary>
/// Met la configuration de SteamVR en accord avec le profil matériel AVANT que SteamVR ne démarre.
///
/// ⚠ Ce n'est pas GraffWall qui démarre SteamVR en premier : c'est le CHARGEUR XR OpenVR, réglé en
/// « Initialize XR on Startup ». XR Management l'initialise à l'étape <c>AfterAssembliesLoaded</c>, et
/// <c>OpenVR.Init</c> lance SteamVR s'il ne tourne pas — avant la première scène, avant tout
/// MonoBehaviour. Corriger la configuration dans une scène (calibration…) arriverait donc TROP TARD :
/// SteamVR tournerait déjà avec l'ancienne. D'où l'étape <c>SubsystemRegistration</c>, la plus précoce
/// que Unity offre, et la lecture du profil DIRECTEMENT dans le fichier (<see cref="GlobalSettingsFile"/>).
///
/// Si SteamVR tourne déjà avec une configuration inadaptée (laissé ouvert, lancé par une application
/// de streaming…), il est FERMÉ : ses réglages ne peuvent pas être modifiés tant qu'il tourne. Le
/// chargeur XR le relance ensuite, avec la bonne configuration.
///
/// Premier lancement (aucun profil choisi) : rien n'est touché — on ne sait pas encore quel matériel
/// est branché. C'est l'écran de choix qui s'en chargera.
/// </summary>
public static class SteamVRBootstrap
{
    /// <summary>Verdict du dernier passage au lancement ; null s'il n'a rien fait (premier lancement, batch).</summary>
    public static SteamVRConfig.Result LastResult { get; private set; }

    /// <summary>Sauvegardes des fichiers de SteamVR avant chaque correction. Hors du projet et du dossier du jeu.</summary>
    public static string BackupRoot => Path.Combine(Application.persistentDataPath, "SteamVRBackups");

    /// <summary>Chemin de vrmonitor.exe déclaré dans les réglages, pour retrouver SteamVR si OpenVR ne le dit pas.</summary>
    public static string VrMonitorHint => GlobalSettings.Core.GlobalSettings.Instance != null
        ? GlobalSettings.Core.GlobalSettings.Instance.SteamVREXEPath.Value
        : GlobalSettingsFile.Read("SteamVREXEPath");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void BeforeXRLoaderStartsSteamVR()
    {
        LastResult = null;

        // CI et tests : ne JAMAIS fermer ni reconfigurer le SteamVR de la machine qui les exécute.
        if (Application.isBatchMode) return;

        if (!HardwareProfiles.HasPersistedChoice)
        {
            Debug.Log("[SteamVRBootstrap] Aucun matériel choisi sur ce poste : configuration SteamVR laissée en l'état.");
            return;
        }

        // Bloquant, et c'est voulu : rien ne s'affiche encore, et le chargeur XR ne doit pas démarrer
        // SteamVR avant la fin. Quelques millisecondes quand tout est en ordre, au plus ~10 s s'il faut
        // fermer SteamVR.
        LastResult = EnsureForCurrentProfile(allowClose: true);
    }

    /// <summary>Vérifie et corrige pour le profil courant (cf. <see cref="SteamVRConfig.Ensure"/>).</summary>
    public static SteamVRConfig.Result EnsureForCurrentProfile(bool allowClose) =>
        SteamVRConfig.Ensure(HardwareProfiles.Current.SteamVRMode, VrMonitorHint, BackupRoot, allowClose);

    /// <summary>
    /// Avant tout lancement EXPLICITE de SteamVR (calibration, bouton, réparation) : corrige les
    /// fichiers s'il le faut. SteamVR ne tourne pas à ce moment — sinon on ne le lancerait pas — donc
    /// rien n'est fermé. Un échec est journalisé mais n'empêche pas le lancement : un SteamVR mal
    /// configuré vaut mieux que pas de SteamVR, et la vérification de démarrage le signalera.
    /// </summary>
    public static void EnsureBeforeLaunch()
    {
        if (!HardwareProfiles.HasPersistedChoice) return;
        SteamVRConfig.Result result = EnsureForCurrentProfile(allowClose: false);
        if (!result.Ok) Debug.LogWarning($"[SteamVRBootstrap] Lancement de SteamVR malgré : {result.Message}");
    }
}
