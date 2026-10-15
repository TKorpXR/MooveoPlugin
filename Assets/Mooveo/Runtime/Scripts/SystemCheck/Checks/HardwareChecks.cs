using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Vérifications MATÉRIEL.
///
/// ⚠ Elles sont INFORMATIVES ici, alors qu'elles bloquent en calibration — et c'est voulu. Arrivé
/// dans GraffWall, l'opérateur a déjà passé la porte : lui interdire de peindre parce qu'une manette
/// s'est éteinte ne règle rien, ça l'empêche juste de travailler. La calibration, elle, a besoin du
/// tracker pour faire son office, donc elle attend.
/// </summary>
[Preserve]
public class DevicesCheck : SyncSystemCheck
{
    public override string Category => CheckCategory.Hardware;
    public override string Name => "Périphériques de jeu";
    // La calibration a ses PROPRES lignes de périphériques : doubler serait du bruit.
    public override CheckScope Scope => CheckScope.Game;
    public override int Order => 0;

    protected override CheckResult Evaluate()
    {
        if (MooveoConfigManager.MouseTestActive) return CheckResult.Ok("bombe simulée à la souris");
        // On RÉUTILISE les vérificateurs de la calibration plutôt que de réécrire la détection :
        // une seule implémentation, un seul endroit à corriger.
        // Station = casque (suivi inside-out) : pas de tracker, mais un casque suivi. Sans ce tri, la
        // ligne réclamerait en permanence un tracker qui n'existe pas dans ce montage.
        var checkers = HardwareProfiles.Current.StationIsHeadset
            ? new List<IDeviceChecker> { new AnyControllerChecker(), new HMDChecker() }
            : new List<IDeviceChecker> { new AnyControllerChecker(), new TrackerChecker() };

        var missing = new List<string>();
        foreach (IDeviceChecker checker in checkers)
        {
            bool connected;
            try { connected = checker.IsConnected(); }
            catch { connected = false; }

            if (!connected) missing.Add(checker.DeviceName);
        }

        if (missing.Count == 0) return CheckResult.Ok();

        // Tout manque : c'est probablement SteamVR qui n'a rien, pas chaque appareil séparément.
        return missing.Count == checkers.Count
            ? CheckResult.Warning("Aucun périphérique de jeu détecté.",
                                  $"Vérifiez que SteamVR tourne, et allumez {HardwareProfiles.Current.DeviceNoun} si besoin.")
            : CheckResult.Warning("Périphérique(s) non détecté(s) : " + string.Join(", ", missing),
                                  "Allumez-le, ou vérifiez son appairage dans SteamVR.");
    }
}

/// <summary>
/// Sans mur ni caméra, rien ne s'affiche ET rien ne le dit : les panneaux world-space se posent
/// relativement au mur, et le curseur vise à travers la caméra.
/// </summary>
[Preserve]
public class SceneRigCheck : SyncSystemCheck
{
    public override string Category => CheckCategory.Hardware;
    public override string Name => "Mur et caméra";
    // Le mur n'existe pas encore pendant la calibration : c'est elle qui le génère.
    public override CheckScope Scope => CheckScope.Game;
    public override int Order => 10;

    protected override CheckResult Evaluate()
    {
        bool wall = MooveoGameHooks.WallTransform == null || MooveoGameHooks.WallTransform() != null;
        bool camera = Camera.main != null || GlobalSettings.Core.GlobalSettings.MainCamera != null;

        if (wall && camera) return CheckResult.Ok();

        var missing = new List<string>();
        if (!wall) missing.Add("mur");
        if (!camera) missing.Add("caméra principale");

        return CheckResult.Error("Élément(s) de scène absent(s) : " + string.Join(", ", missing),
                                 "La calibration n'a probablement pas généré le mur. Relancez-la.");
    }
}

/// <summary>
/// Une calibration absente ou dégénérée pose le mur n'importe où. Les critères sont les mêmes que
/// ceux de <c>CalibrationManager.DoesNeedCalibration</c> : au moins trois points, distincts.
/// </summary>
[Preserve]
public class CalibrationCheck : SyncSystemCheck
{
    private const float MinimumPointDistance = 0.025f;

    public override string Category => CheckCategory.Hardware;
    public override string Name => "Calibration";
    // Inutile de reprocher une calibration absente pendant qu'on la fait.
    public override CheckScope Scope => CheckScope.Game;
    public override int Order => 20;

    protected override CheckResult Evaluate()
    {
        if (MooveoConfigManager.MouseTestActive) return CheckResult.Ok("mur de test temporaire");
        if (!MooveoConfigManager.Exists())
            return CheckResult.Warning("Aucune calibration enregistrée.",
                                       "Passez par Accueil pour calibrer le mur.");

        MooveoConfig config;
        try { config = MooveoConfigManager.Load(); }
        catch (System.Exception e)
        {
            return CheckResult.Warning("Calibration illisible.", e.Message);
        }

        if (config == null || config.Points == null || config.Points.Count < 3)
            return CheckResult.Warning("Calibration incomplète (moins de trois points).",
                                       "Refaites la calibration depuis l'accueil.");

        for (int i = 0; i < config.Points.Count; i++)
        for (int j = i + 1; j < config.Points.Count; j++)
            if (Vector3.Distance(config.Points[i], config.Points[j]) <= MinimumPointDistance)
                return CheckResult.Warning("Calibration dégénérée : deux points trop proches.",
                                           "Le mur sera mal positionné. Refaites la calibration.");

        if (!config.MatchesCurrentHardware)
            return CheckResult.Warning(
                $"Calibration faite avec « {HardwareProfiles.Get(config.RecordedHardware).DisplayName} », " +
                $"jeu configuré pour « {HardwareProfiles.Current.DisplayName} ».",
                "Les deux matériels n'ont pas le même repère : le mur est mal placé. Refaites la calibration.");

        return CheckResult.Ok($"{config.Points.Count} points");
    }
}
