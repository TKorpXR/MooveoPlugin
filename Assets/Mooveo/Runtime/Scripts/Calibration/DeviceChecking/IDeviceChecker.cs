using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// ⚠ Contrat : <see cref="IsConnected"/> est appelé sur le fil PRINCIPAL, en plein milieu d'une
/// boucle qui met à jour toutes les lignes de la liste de vérification (`CalibrationManager.
/// CheckDevicesLogicLoop`). Il DOIT rendre la main quasi instantanément — une API Unity mise en
/// cache (InputSystem, XR), rien qui attende un pilote, un process ou le réseau. Un appel lent y
/// gèle la frame entière, donc TOUTES les lignes, pas seulement la sienne (Unity ne présente rien
/// tant que l'Update en cours n'est pas revenu).
/// Tout ce qui peut attendre quelque chose d'externe (scan de processus, disque, réseau) implémente
/// <see cref="IAsyncDeviceChecker"/> à la place : la boucle l'exécute alors hors du fil principal et
/// continue les autres checkers sans l'attendre, comme `FolderSearch.RunAsync`/`RepairContext.Await`
/// le font déjà pour les recherches disque et les boîtes de dialogue Windows (cf. CLAUDE.md §13).
/// </summary>
public interface IDeviceChecker
{
    string DeviceName { get; }
    DeviceType Type { get; }
    bool IsConnected();
    void RefreshDevice();

}

/// <summary>
/// Marque un checker dont <see cref="IsConnectedAsync"/> ne touche AUCUNE API Unity (InputSystem,
/// XR, GameObject…) — seulement du code managé pur ou des appels OS (ex. `Process.
/// GetProcessesByName`, connu pour parfois traîner via WMI). La boucle de vérification peut alors
/// l'exécuter via `Task.Run` sans risquer un crash Unity (API non thread-safe), et sans bloquer la
/// mise à jour des autres lignes pendant qu'il tourne.
/// </summary>
public interface IAsyncDeviceChecker
{
    Task<bool> IsConnectedAsync();
}

/// <summary>
/// Contrôle INFORMATIF : son échec n'empêche pas de démarrer, et il sait dire pourquoi en quelques
/// mots. C'est le CHECKER qui le déclare, pas sa configuration de scène : oublier de cocher la case
/// transformait une simple alerte en blocage — spinner à vie et calibration qui ne démarre jamais.
/// </summary>
public interface IOptionalDeviceChecker
{
    /// <summary>Raison courte de l'échec, affichée sur la ligne.</summary>
    string Warning { get; }

    /// <summary>Verdict pas encore rendu : la ligne garde son spinner au lieu d'accuser trop tôt.</summary>
    bool Pending { get; }

    /// <summary>Échec GRAVE : triangle rouge plutôt qu'orange. Ne bloque pas pour autant.</summary>
    bool IsError { get; }
}

public enum DeviceType
{
    NULL,
    SOFTWARE,
    CONTROLLER,
    TRACKER,
    HEADSET
}