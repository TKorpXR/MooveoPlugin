using System;

/// <summary>
/// Logiciels tiers dont le SDK fournit les vérifications (et les réparations « Résoudre »).
/// Combinable : <c>MooveoSoftware.SteamVR | MooveoSoftware.EosUtility</c>.
/// </summary>
[Flags]
public enum MooveoSoftware
{
    None = 0,
    SteamVR = 1 << 0,
    /// <summary>Impression DNP : installation, bordures 4x6, gabarits, dossier d'impression.</summary>
    HotFolderPrint = 1 << 1,
    /// <summary>Appareil photo Canon : installation, lancement, dossier des prises de vue.</summary>
    EosUtility = 1 << 2,
    All = SteamVR | HotFolderPrint | EosUtility
}

/// <summary>
/// Vérification d'un logiciel tiers FOURNIE PAR LE SDK : elle ne s'exécute (et n'apparaît, à
/// l'écran de calibration comme dans « État du système ») que si le jeu a activé son logiciel.
/// </summary>
public interface ISoftwareCheck
{
    MooveoSoftware Software { get; }
}

/// <summary>
/// Activation des vérifications de logiciels tiers.
///
/// ⚠ AUCUNE n'est active par défaut. Un jeu Mooveo n'utilise pas forcément l'impression ni
/// l'appareil photo : les vérifier d'office afficherait « HotFolderPrint introuvable » sur un poste
/// qui n'en a jamais eu besoin. Le jeu déclare donc ce qu'il utilise, AVANT le chargement de la
/// première scène :
/// <code>
/// [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
/// private static void ConfigureMooveo() => MooveoSoftwareChecks.Enable(MooveoSoftware.SteamVR);
/// </code>
/// Désactivé, un logiciel ne déclenche plus rien non plus en dehors des vérifications : pas de
/// détection HotFolder ni d'écriture de gabarits de bordure au démarrage de la calibration.
///
/// Logiciel PROPRE AU JEU (ni SteamVR, ni HotFolderPrint, ni EOS) : écrire sa vérification dans le
/// jeu — une classe dérivant de <see cref="SyncSystemCheck"/> (ou <see cref="SystemCheck"/>), avec
/// un constructeur sans argument et <c>[Preserve]</c>. Elle est découverte automatiquement et active
/// d'office : c'est le jeu qui l'a déclarée. Pour un bouton « Résoudre », décrire le logiciel par un
/// <see cref="SoftwareInfo"/> (son <c>Setting</c> peut nommer un réglage d'une classe dérivée de
/// GlobalSettings) et l'attacher au résultat : <c>.With(new MissingSoftwareRepair(info, Name))</c>
/// ou <c>.With(new LaunchSoftwareRepair(info))</c>.
///
/// Les lignes « natives » de la liste de calibration (types SteamVR, EosUtility, HotFolder… du
/// CalibrationManager) restent pilotées par la scène : les y mettre suffit à les afficher.
/// </summary>
public static class MooveoSoftwareChecks
{
    /// <summary>Logiciels dont les vérifications sont actives.</summary>
    public static MooveoSoftware Enabled { get; private set; } = MooveoSoftware.None;

    public static void Enable(MooveoSoftware software) => Enabled |= software;

    public static void Disable(MooveoSoftware software) => Enabled &= ~software;

    /// <summary>Vrai si TOUS les logiciels demandés sont actifs.</summary>
    public static bool IsEnabled(MooveoSoftware software)
        => software != MooveoSoftware.None && (Enabled & software) == software;

    /// <summary>Faux pour une vérification de logiciel du SDK dont le logiciel n'est pas activé.</summary>
    public static bool Allows(ISystemCheck check)
        => !(check is ISoftwareCheck software) || IsEnabled(software.Software);
}
