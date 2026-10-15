using System;
using System.Collections;
using System.Collections.Generic;

public enum CheckSeverity { Ok, Warning, Error }

/// <summary>
/// Où une vérification a un sens. Le mur, les managers de GraffWall et le service de détourage
/// n'existent pas dans la scène de calibration : les y contrôler produirait un rouge permanent qui
/// ne veut rien dire, et on apprend vite à ne plus regarder une liste qui crie toujours.
/// </summary>
[System.Flags]
public enum CheckScope
{
    Calibration = 1,
    Game = 2,
    Both = Calibration | Game
}

/// <summary>Catégories d'affichage. Chaînes et non enum : une catégorie s'ajoute sans rien casser.</summary>
public static class CheckCategory
{
    public const string Configuration = "Configuration";
    public const string Hardware      = "Matériel";
    public const string ThirdParty    = "Logiciels tiers";
    public const string Services      = "Services";
    public const string Build         = "Build";
    public const string Integrity     = "Intégrité";

    /// <summary>
    /// Vrai si un opérateur sans compétence technique peut corriger seul ce qui s'y signale — un
    /// dossier, un logiciel à lancer, une manette à rallumer. Le reste est replié dans « État du
    /// système » : l'afficher à égalité noierait les deux lignes qui le concernent.
    /// ⚠ Seul endroit qui en décide : une catégorie ajoutée sans passer ici tombe dans le technique.
    /// </summary>
    public static bool IsOperatorFacing(string category)
        => category == Configuration || category == ThirdParty || category == Hardware;
}

/// <summary>Verdict d'une vérification : ce qui ne va pas, et surtout quoi faire.</summary>
public sealed class CheckResult
{
    public CheckSeverity Severity;

    /// <summary>Ce qui a été constaté, en une phrase lisible au projecteur.</summary>
    public string Message;

    /// <summary>L'action corrective. Sans elle, un diagnostic ne sert à rien sur un poste d'exploitation.</summary>
    public string Fix;

    /// <summary>Id d'un GuidePopupData pour la procédure illustrée. Facultatif.</summary>
    public string GuideId;

    /// <summary>
    /// Vrai quand le verdict ne vaut QUE dans le contexte courant — typiquement les vérifications de
    /// build, qui passent toujours en éditeur. Sans ce drapeau elles donnent une fausse assurance.
    /// </summary>
    public bool Inconclusive;

    /// <summary>
    /// Réparations proposées par le bouton « Résoudre ». Plusieurs possibles : « 2 dossiers
    /// introuvables » en demande une par dossier. Null = rien à tenter depuis GraffWall.
    /// </summary>
    public List<ICheckRepair> Repairs;

    public bool CanRepair => Repairs != null && Repairs.Count > 0;

    /// <summary>Ajoute des réparations, en chaîne : <c>CheckResult.Error(…).With(repair)</c>.</summary>
    public CheckResult With(params ICheckRepair[] repairs)
    {
        if (repairs == null) return this;
        foreach (ICheckRepair repair in repairs)
        {
            if (repair == null) continue;
            if (Repairs == null) Repairs = new List<ICheckRepair>();
            Repairs.Add(repair);
        }
        return this;
    }

    public static CheckResult Ok(string message = null)
        => new CheckResult { Severity = CheckSeverity.Ok, Message = message };

    public static CheckResult Warning(string message, string fix = null, string guideId = null)
        => new CheckResult { Severity = CheckSeverity.Warning, Message = message, Fix = fix, GuideId = guideId };

    public static CheckResult Error(string message, string fix = null, string guideId = null)
        => new CheckResult { Severity = CheckSeverity.Error, Message = message, Fix = fix, GuideId = guideId };

    public static CheckResult Unknown(string message)
        => new CheckResult { Severity = CheckSeverity.Ok, Message = message, Inconclusive = true };
}

/// <summary>
/// Contrat d'une vérification de démarrage.
///
/// ⚠ Même principe que les modes de jeu (CLAUDE.md §6) : AJOUTER une vérification = ajouter une
/// classe. Aucun fichier existant ne les énumère, <see cref="SystemCheckRunner"/> les découvre.
/// Le plus simple est d'hériter de <see cref="SystemCheck"/> ou de <see cref="SyncSystemCheck"/>.
/// </summary>
public interface ISystemCheck
{
    string Category { get; }
    string Name { get; }

    /// <summary>Scènes où la vérification a un sens (cf. <see cref="CheckScope"/>).</summary>
    CheckScope Scope { get; }

    /// <summary>
    /// Indispensable AVANT de calibrer. Seules ces vérifications sont rejouées par la calibration
    /// quand une campagne complète a déjà tourné dans la session (cf. <see cref="SystemCheckRunner"/>).
    /// </summary>
    bool Essential { get; }

    /// <summary>Ordre d'affichage dans sa catégorie.</summary>
    int Order { get; }

    /// <summary>Délai maximal. Passé ce temps le runner abandonne et signale l'attente.</summary>
    float TimeoutSeconds { get; }

    /// <summary>
    /// Exécution. Appeler <paramref name="report"/> EXACTEMENT une fois. Ne pas appeler du tout est
    /// traité comme un dépassement de délai, pas comme un succès.
    /// </summary>
    IEnumerator Run(Action<CheckResult> report);
}

/// <summary>Base commune : évite de réécrire Order et TimeoutSeconds dans chaque vérification.</summary>
public abstract class SystemCheck : ISystemCheck
{
    public abstract string Category { get; }
    public abstract string Name { get; }
    public virtual CheckScope Scope => CheckScope.Both;
    public virtual bool Essential => false;
    public virtual int Order => 100;
    public virtual float TimeoutSeconds => 5f;
    public abstract IEnumerator Run(Action<CheckResult> report);
}

/// <summary>Vérification SYNCHRONE : la grande majorité. Une seule méthode à écrire.</summary>
public abstract class SyncSystemCheck : SystemCheck
{
    protected abstract CheckResult Evaluate();

    public sealed override IEnumerator Run(Action<CheckResult> report)
    {
        report(Evaluate());
        yield break;
    }
}
