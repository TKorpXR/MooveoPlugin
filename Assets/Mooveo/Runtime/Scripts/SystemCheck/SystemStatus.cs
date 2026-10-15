using System;
using System.Collections.Generic;

/// <summary>Résultat d'UNE vérification, tel qu'on le consulte après coup.</summary>
public sealed class CheckReport
{
    public string Category;
    public string Name;
    public CheckResult Result;
    public float Seconds;
    public bool TimedOut;
    public string Error;   // exception attrapée, le cas échéant

    /// <summary>Rang d'affichage (catégorie puis ordre), fixé par le runner.</summary>
    public int Index;

    public CheckSeverity Severity => Result != null ? Result.Severity : CheckSeverity.Warning;
    public string Key => "check:" + Name;
}

/// <summary>
/// État consolidé de la dernière campagne de vérifications. Statique et sans dépendance de scène :
/// le panneau « État du système » le lit, les vérifications l'alimentent, rien ne les couple.
/// </summary>
public static class SystemStatus
{
    private static readonly List<CheckReport> _reports = new List<CheckReport>();

    public static IReadOnlyList<CheckReport> Reports => _reports;
    public static DateTime LastRun { get; private set; }
    public static bool Running { get; internal set; }

    /// <summary>Émis à la fin d'une campagne, campagne vide comprise.</summary>
    public static event Action Completed;

    /// <summary>
    /// Émis à CHAQUE étape : ouverture, verdict, fin. ⚠ Écouter seulement <see cref="Completed"/>
    /// figeait le panneau pendant toute la campagne — `Begin` vide les rapports sans rien dire, et
    /// « Relancer les vérifications » paraissait ne rien faire pendant vingt secondes.
    /// </summary>
    public static event Action Changed;

    public static int Count(CheckSeverity severity)
    {
        int n = 0;
        foreach (CheckReport r in _reports)
            if (r.Severity == severity) n++;
        return n;
    }

    public static CheckSeverity Worst
    {
        get
        {
            CheckSeverity worst = CheckSeverity.Ok;
            foreach (CheckReport r in _reports)
                if (r.Severity > worst) worst = r.Severity;
            return worst;
        }
    }

    /// <summary>
    /// Pire gravité HORS problèmes mis en sourdine (<see cref="CheckSnooze"/>) : c'est ce que montre
    /// l'indicateur de la barre de menu. Sans ça, ignorer une panne pour la semaine laissait l'icône
    /// rouge toute la semaine — et l'on apprend vite à ne plus la regarder.
    /// </summary>
    public static CheckSeverity WorstActive
    {
        get
        {
            CheckSeverity worst = CheckSeverity.Ok;
            foreach (CheckReport r in _reports)
                if (r.Severity > worst && !CheckSnooze.IsSnoozed(r.Key)) worst = r.Severity;
            return worst;
        }
    }

    /// <summary>Une campagne a-t-elle déjà abouti dans cette session ?</summary>
    public static bool HasRun => LastRun != default;

    /// <summary>
    /// Ouvre une campagne. <paramref name="replaced"/> null = campagne complète, tout repart de zéro.
    /// Sinon seules ces vérifications sont retirées : une passe PARTIELLE garde les verdicts des
    /// autres, sans quoi « État du système » se viderait de tout ce qu'elle n'a pas rejoué.
    /// ⚠ Retirer les rejouées n'est pas optionnel : leur ancien verdict resterait lisible pendant la
    /// passe, et la ligne de calibration basculerait tout de suite sur un résultat périmé.
    /// </summary>
    internal static void Begin(ICollection<string> replaced = null)
    {
        if (replaced == null) _reports.Clear();
        else _reports.RemoveAll(r => replaced.Contains(r.Name));
        Changed?.Invoke();
    }

    /// <summary>Insère à son rang : une passe partielle ajoute en fin des verdicts déjà classés.</summary>
    internal static void Add(CheckReport report)
    {
        int i = _reports.Count;
        while (i > 0 && _reports[i - 1].Index > report.Index) i--;
        _reports.Insert(i, report);
        Changed?.Invoke();
    }

    internal static void End()
    {
        LastRun = DateTime.Now;
        Running = false;
        Changed?.Invoke();
        Completed?.Invoke();
    }
}
