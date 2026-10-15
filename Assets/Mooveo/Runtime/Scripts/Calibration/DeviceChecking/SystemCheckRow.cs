using System.Collections.Generic;

/// <summary>
/// Expose une <see cref="ISystemCheck"/> comme une ligne de la liste de vérification de la
/// calibration. Un pont, pas une réimplémentation : le verdict vient de la campagne lancée par
/// <see cref="SystemCheckRunner"/>, cette classe ne fait que le lire.
///
/// ⚠ <c>IDeviceChecker.IsConnected</c> est SYNCHRONE, et plusieurs vérifications ne le sont pas
/// (réseau, modèle de détourage, Python). D'où ce découplage : la campagne tourne une fois en
/// arrière-plan, chaque ligne consulte ensuite le résultat mis en cache. Tant qu'aucun verdict n'est
/// rendu, la ligne reste sur son spinner — ce que <see cref="Pending"/> signale.
///
/// ⚠ Toutes ces lignes sont INFORMATIVES. Bloquer le démarrage sur « aucune fresque trouvée » ou
/// « serveur IA injoignable » empêcherait de peindre pour des fonctionnalités annexes ; et l'issue
/// demande explicitement que les vérifications ne bloquent pas.
/// </summary>
public class SystemCheckRow : IDeviceChecker, IOptionalDeviceChecker
{
    private readonly string _checkName;

    public SystemCheckRow(string checkName) => _checkName = checkName;

    public string DeviceName => _checkName;
    public DeviceType Type => DeviceType.SOFTWARE;

    public bool IsConnected()
    {
        CheckReport report = Find();
        return report != null && report.Severity == CheckSeverity.Ok;
    }

    /// <summary>Verdict pas encore rendu : la campagne tourne toujours.</summary>
    public bool Pending => Find() == null && SystemStatus.Running;

    public bool IsError => Find()?.Severity == CheckSeverity.Error;

    public string Warning
    {
        get
        {
            CheckReport report = Find();
            if (report == null) return "non vérifié";
            if (report.TimedOut) return "délai dépassé";

            // Le constat seul : la ligne est étroite, l'action corrective se lit dans la
            // notification et dans le panneau « État du système ».
            string message = report.Result != null ? report.Result.Message : null;
            return string.IsNullOrEmpty(message) ? "indisponible" : Shorten(message);
        }
    }

    public void RefreshDevice() { }

    private CheckReport Find()
    {
        IReadOnlyList<CheckReport> reports = SystemStatus.Reports;
        for (int i = 0; i < reports.Count; i++)
            if (reports[i].Name == _checkName) return reports[i];
        return null;
    }

    /// <summary>Une ligne de liste ne tient pas un paragraphe : on garde la première phrase.</summary>
    private static string Shorten(string message)
    {
        message = message.Replace('\n', ' ').Trim();
        int stop = message.IndexOf(". ", System.StringComparison.Ordinal);
        if (stop > 0) message = message.Substring(0, stop + 1);
        return message.Length <= 70 ? message : message.Substring(0, 67) + "…";
    }
}
