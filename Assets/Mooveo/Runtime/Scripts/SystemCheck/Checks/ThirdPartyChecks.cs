using System.Diagnostics;
using System.IO;
using UnityEngine.Scripting;
using Debug = UnityEngine.Debug;

/// <summary>Outils communs aux vérifications de logiciels tiers.</summary>
public static class ProcessChecks
{
    /// <summary>Un processus de ce nom tourne-t-il ? Le nom est SANS extension.</summary>
    public static bool IsRunning(string processName)
    {
        try
        {
            Process[] found = Process.GetProcessesByName(processName);
            foreach (Process p in found) p.Dispose();
            return found.Length > 0;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SystemCheck] Énumération des processus refusée : {e.Message}");
            return false;
        }
    }
}

/// <summary>
/// ⚠ L'ancien HotFolderChecker ne regardait QUE le processus. Un logiciel désinstallé et un logiciel
/// fermé donnaient donc le même verdict, alors que les remèdes n'ont rien à voir.
/// </summary>
[Preserve]
public class HotFolderInstalledCheck : SyncSystemCheck
{
    public override string Category => CheckCategory.ThirdParty;
    public override string Name => "HotFolderPrint";
    public override int Order => 0;

    protected override CheckResult Evaluate()
    {
        HotFolderLocator.Result located = HotFolderLocator.Resolve();

        if (string.IsNullOrEmpty(located.Root))
            return CheckResult.Warning("HotFolderPrint n'est pas installé sur ce poste.",
                                       "Sans lui, aucune impression n'est possible.",
                                       HotFolderSetupPopup.GuideId)
                              .With(new MissingSoftwareRepair(KnownSoftware.HotFolderPrint, Name));

        return ProcessChecks.IsRunning("HotFolderPrint")
            ? CheckResult.Ok(located.Root)
            : CheckResult.Warning("HotFolderPrint est installé mais n'est pas lancé.",
                                  "Démarrez-le : les photos envoyées ne seront pas imprimées sans lui.")
                         .With(new LaunchSoftwareRepair(KnownSoftware.HotFolderPrint));
    }
}

/// <summary>
/// Bordures d'impression. Le diagnostic complet vit déjà dans <see cref="HotFolderLocator"/> : on ne
/// le réécrit pas, on le rend consultable depuis « État du système », avec sa procédure illustrée.
/// </summary>
[Preserve]
public class HotFolderBannerCheck : SyncSystemCheck
{
    public override string Category => CheckCategory.ThirdParty;
    public override string Name => "Bordures d'impression";
    public override int Order => 10;

    protected override CheckResult Evaluate()
    {
        HotFolderLocator.Result located = HotFolderLocator.Resolve();

        // Installation absente : c'est l'affaire de la vérification précédente, inutile de crier deux fois.
        if (located.Status == HotFolderLocator.Status.NotInstalled) return CheckResult.Ok();
        if (located.Status == HotFolderLocator.Status.Ok) return CheckResult.Ok();

        return CheckResult.Warning(located.Message,
                                   "La bannière choisie ne sortira pas sur les photos.",
                                   HotFolderSetupPopup.GuideId);
    }
}

/// <summary>
/// Les gabarits sont écrits au lancement : leur absence signale un dossier Import inaccessible, pas
/// un oubli de l'opérateur.
/// </summary>
[Preserve]
public class BannerTemplatesCheck : SyncSystemCheck
{
    public override string Category => CheckCategory.ThirdParty;
    public override string Name => "Gabarits de bordure";
    public override int Order => 20;

    protected override CheckResult Evaluate()
    {
        string folder = HotFolderBannerTemplate.Folder;
        bool landscape = File.Exists(Path.Combine(folder, HotFolderBannerTemplate.LandscapeFileName));
        bool portrait = File.Exists(Path.Combine(folder, HotFolderBannerTemplate.PortraitFileName));

        if (landscape && portrait) return CheckResult.Ok(folder);

        return CheckResult.Warning($"Gabarit(s) à importer absent(s) de :\n{folder}",
                                   MooveoGameHooks.AppName + " n'a pas pu les écrire ; vérifiez les droits sur ce dossier.")
                          .With(new ActionRepair("Réécrire les gabarits", Rewrite));
    }

    /// <summary>Sans question : les gabarits sont produits au code, les réécrire ne peut rien casser.</summary>
    private static string Rewrite()
    {
        HotFolderBannerTemplate.EnsureAll();
        string folder = HotFolderBannerTemplate.Folder;
        bool ok = File.Exists(Path.Combine(folder, HotFolderBannerTemplate.LandscapeFileName)) &&
                  File.Exists(Path.Combine(folder, HotFolderBannerTemplate.PortraitFileName));
        return ok ? null : $"Les gabarits n'ont pas pu être écrits dans {folder}. {PathChecks.Writable(folder)}";
    }
}

[Preserve]
public class EosUtilityCheck : SyncSystemCheck
{
    public override string Category => CheckCategory.ThirdParty;
    public override string Name => "EOS Utility";
    public override int Order => 30;

    protected override CheckResult Evaluate()
    {
        if (GlobalSettings.Core.GlobalSettings.Instance == null) return CheckResult.Ok();

        string exe = GlobalSettings.Core.GlobalSettings.Instance.EosUtilityEXEPath.Value;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            return CheckResult.Warning("EOS Utility est introuvable.",
                                       "La capture photo par appareil ne fonctionnera pas. " +
                                       "Vérifiez EosUtilityEXEPath dans GlobalSettings.json.")
                              .With(new MissingSoftwareRepair(KnownSoftware.EosUtility, Name));

        if (!ProcessChecks.IsRunning(Path.GetFileNameWithoutExtension(exe)))
            return CheckResult.Warning("EOS Utility n'est pas lancé.",
                                       "Démarrez-le et vérifiez que l'appareil photo est connecté.")
                              .With(new LaunchSoftwareRepair(KnownSoftware.EosUtility));

        // Le dossier surveillé est celui où EOS Utility dépose les prises de vue.
        string watched = GlobalSettings.Core.GlobalSettings.Instance.CameraPicturesPath.Value;
        if (string.IsNullOrEmpty(watched) || !Directory.Exists(watched))
            return CheckResult.Warning("Le dossier photo surveillé est introuvable.",
                                       "Réglez CameraPicturesPath, et faites-y pointer EOS Utility.")
                              .With(new MissingFolderRepair("Photos de l'appareil", "CameraPicturesPath", watched,
                                                            new[] { "Photo", "Photos" }, PathChecks.ImageExtensions));

        return CheckResult.Ok();
    }
}

[Preserve]
public class SteamVrCheck : SyncSystemCheck
{
    public override string Category => CheckCategory.ThirdParty;
    public override string Name => "SteamVR";
    public override int Order => 40;

    // Sans SteamVR, ni tracker ni manette : rien à calibrer.
    public override bool Essential => true;

    protected override CheckResult Evaluate()
    {
        if (GlobalSettings.Core.GlobalSettings.Instance == null) return CheckResult.Ok();
        if (MooveoConfigManager.MouseTestActive) return CheckResult.Ok("test souris sans SteamVR");

        string exe = GlobalSettings.Core.GlobalSettings.Instance.SteamVREXEPath.Value;
        if (!string.IsNullOrEmpty(exe) && !File.Exists(exe))
            return CheckResult.Warning("L'exécutable SteamVR est introuvable.",
                                       "Vérifiez SteamVREXEPath dans GlobalSettings.json. " + exe)
                              .With(new MissingSoftwareRepair(KnownSoftware.SteamVR, Name));

        // vrmonitor ou vrserver selon la version : l'un des deux suffit à prouver que SteamVR tourne.
        bool running = ProcessChecks.IsRunning("vrmonitor") || ProcessChecks.IsRunning("vrserver");
        return running
            ? CheckResult.Ok()
            : CheckResult.Warning("SteamVR ne tourne pas.",
                                  "Sans lui, ni tracker ni manette ne seront détectés.")
                         .With(new LaunchSoftwareRepair(KnownSoftware.SteamVR));
    }
}

/// <summary>
/// La configuration de SteamVR correspond-elle au matériel choisi ? Pilote « null » sans casque pour
/// les trackers, vrai casque pour un casque. Mauvaise configuration = SteamVR ignore le casque, ou
/// au contraire en réclame un qui n'existe pas : rien n'est suivi, sans message clair de SteamVR.
///
/// Normalement corrigée au lancement (<see cref="SteamVRBootstrap"/>) ; cette vérification rend
/// VISIBLE un échec de cette correction (droits, SteamVR impossible à fermer) et propose de la refaire.
/// </summary>
[Preserve]
public class SteamVrConfigCheck : SyncSystemCheck
{
    public override string Category => CheckCategory.ThirdParty;
    public override string Name => "Configuration SteamVR";
    public override int Order => 41;

    // Une configuration fausse empêche toute calibration : elle a sa place dans la passe courte.
    public override bool Essential => true;

    protected override CheckResult Evaluate()
    {
        if (MooveoConfigManager.MouseTestActive) return CheckResult.Ok("test souris sans SteamVR");
        if (!HardwareProfiles.HasPersistedChoice)
            return CheckResult.Ok("matériel pas encore choisi");

        HardwareProfileData profile = HardwareProfiles.Current;
        if (!SteamVRConfig.TryLocate(SteamVRBootstrap.VrMonitorHint, out SteamVRConfig.Paths paths, out string error))
            return CheckResult.Warning(error, "Installez SteamVR, ou vérifiez SteamVREXEPath dans GlobalSettings.json.");

        System.Collections.Generic.List<SteamVRConfig.Difference> differences;
        try { differences = SteamVRConfig.Inspect(paths, profile.SteamVRMode); }
        catch (System.Exception e)
        {
            return CheckResult.Warning($"Fichiers de configuration SteamVR illisibles : {e.Message}",
                                       $"Vérifiez {paths.UserSettings}.");
        }

        if (differences.Count == 0) return CheckResult.Ok($"mode {profile.SteamVRMode}");

        string atLaunch = SteamVRBootstrap.LastResult != null && !SteamVRBootstrap.LastResult.Ok
            ? $" Échec de la correction au lancement : {SteamVRBootstrap.LastResult.Message}"
            : "";
        return CheckResult.Error(
                   $"La configuration de SteamVR ne correspond pas au matériel « {profile.DisplayName} » " +
                   $"({differences.Count} réglage(s)).",
                   "« Résoudre » ferme SteamVR, corrige ses fichiers et le relance. Écarts : " +
                   string.Join(" ; ", differences) + "." + atLaunch)
                          .With(new SteamVRConfigRepair());
    }
}
