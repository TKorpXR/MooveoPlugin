using UnityEngine;

/// <summary>
/// Vérifie que HotFolderPrint est installé ET qu'une bordure 4x6 est importée dans les DEUX
/// orientations — sans elles, la bannière d'impression ne s'applique pas, en silence.
/// Contrôle NON bloquant (Optional) : on peut peindre sans imprimante.
/// </summary>
public class HotFolderBannerChecker : IDeviceChecker, IOptionalDeviceChecker
{
    private const float RescanSeconds = 3f;
    private float _nextScan;

    public string DeviceName => "Bordures HotFolder";
    public DeviceType Type => DeviceType.SOFTWARE;

    /// <summary>Texte court pour la ligne de la liste (le détail part en toast).</summary>
    public string Warning
    {
        get
        {
            HotFolderLocator.Result r = HotFolderLocator.Last;
            if (r == null) return "état inconnu";

            switch (r.Status)
            {
                case HotFolderLocator.Status.NotInstalled:    return "HotFolderPrint introuvable";
                case HotFolderLocator.Status.NoBordersFolder: return "dossier des bordures introuvable";
                case HotFolderLocator.Status.MissingLandscape: return "aucune bordure 4x6 paysage importée";
                case HotFolderLocator.Status.MissingPortrait:  return "aucune bordure 4x6 portrait importée";
                case HotFolderLocator.Status.MissingBoth:      return "aucune bordure 4x6 importée";
                case HotFolderLocator.Status.BordersLocked:    return "bordure verrouillée ou en lecture seule";
                case HotFolderLocator.Status.ScanFailed:       return "détection en échec";
                default: return "indisponible";
            }
        }
    }

    /// <summary>La détection est synchrone : jamais d'attente.</summary>
    public bool Pending => false;

    /// <summary>Une bordure absente n'empêche pas de peindre : jamais une erreur.</summary>
    public bool IsError => false;

    public bool IsConnected()
    {
        // La boucle interroge toutes les 0,5 s : on ne relance pas un scan disque à ce rythme.
        bool force = Time.unscaledTime >= _nextScan;
        if (force) _nextScan = Time.unscaledTime + RescanSeconds;

        return HotFolderLocator.Resolve(force).Status == HotFolderLocator.Status.Ok;
    }

    public void RefreshDevice() => HotFolderLocator.Resolve(true);
}
