using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UIElements;

/// <summary>
/// Joue les réparations d'une ou plusieurs vérifications, l'une après l'autre, puis les REVÉRIFIE.
///
/// ⚠ Hébergé sur son propre objet, jamais sur le panneau qui le déclenche : Unity n'exécute pas les
/// `finally` d'une coroutine interrompue (même piège qu'AIManager, CLAUDE.md §12), et fermer le
/// panneau pendant une réparation laisserait le bouton grisé pour toujours.
/// </summary>
[Preserve]
public class RepairRunner : MonoBehaviour
{
    private static RepairRunner _instance;
    private static readonly HashSet<string> _repairing = new HashSet<string>();

    private Coroutine _running;

    // Vérifications à rejouer quand GraffWall reprend le focus : l'opérateur est parti installer.
    private readonly HashSet<string> _awaitingFocus = new HashSet<string>();

    /// <summary>Début et fin d'une série de réparations : les écrans grisent ou rendent leurs boutons.</summary>
    public static event Action Changed;

    public static bool Busy => _instance != null && _instance._running != null;

    /// <summary>Cette vérification est-elle en cours de réparation ?</summary>
    public static bool IsRepairing(string checkName) => _repairing.Contains(checkName);

    private static RepairRunner Ensure()
    {
        if (_instance != null) return _instance;

        GameObject go = new GameObject("RepairRunner") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<RepairRunner>();
        return _instance;
    }

    /// <summary>Les rapports réparables parmi ceux-ci, dans l'ordre.</summary>
    public static List<CheckReport> Repairable(IEnumerable<CheckReport> reports)
    {
        var found = new List<CheckReport>();
        if (reports == null) return found;
        foreach (CheckReport report in reports)
            if (report != null && report.Severity != CheckSeverity.Ok && report.Result != null && report.Result.CanRepair)
                found.Add(report);
        return found;
    }

    /// <summary>
    /// Lance la série. Refusé pendant une campagne ou une autre série : deux passes simultanées se
    /// disputeraient les mêmes rapports. <paramref name="host"/> reçoit les popups de décision.
    /// </summary>
    public static bool Resolve(IEnumerable<CheckReport> reports, VisualElement host)
    {
        if (Busy || SystemStatus.Running || host == null) return false;

        List<CheckReport> targets = Repairable(reports);
        if (targets.Count == 0) return false;

        RepairRunner runner = Ensure();
        runner._running = runner.StartCoroutine(runner.ResolveCo(targets, new RepairContext(host)));
        return true;
    }

    /// <summary>Demande une revérification au retour du focus (après « Installer »).</summary>
    public static void RecheckOnFocus(string checkName)
    {
        if (!string.IsNullOrEmpty(checkName)) Ensure()._awaitingFocus.Add(checkName);
    }

    private IEnumerator ResolveCo(List<CheckReport> targets, RepairContext ctx)
    {
        var jobs = new List<KeyValuePair<CheckReport, ICheckRepair>>();
        foreach (CheckReport report in targets)
            foreach (ICheckRepair repair in report.Result.Repairs)
                jobs.Add(new KeyValuePair<CheckReport, ICheckRepair>(report, repair));

        foreach (CheckReport report in targets) _repairing.Add(report.Name);
        Changed?.Invoke();

        for (int i = 0; i < jobs.Count; i++)
        {
            ctx.Step = jobs.Count > 1 ? $"{i + 1}/{jobs.Count}" : null;
            ICheckRepair repair = jobs[i].Value;
            string name = jobs[i].Key.Name;

            IEnumerator body = null;
            try { body = repair.Attempt(ctx); }
            catch (Exception e) { Report(ctx, name, e); }

            if (body != null)
                yield return SystemCheckRunner.Guard(body, e => Report(ctx, name, e));

            // Une réparation qui rend la main en laissant sa progression affichée ne doit pas la
            // léguer à la suivante.
            ctx.EndProgress();
        }

        ctx.Step = null;
        if (ctx.Failures.Count > 0)
            yield return ctx.Inform("Résolution incomplète", string.Join("\n\n", ctx.Failures));

        var names = new List<string>(_repairing);
        _repairing.Clear();
        _running = null;
        Changed?.Invoke();

        // Le verdict vient de la vérification, jamais de la réparation.
        SystemCheckRunner.RunOnly(names);
    }

    private static void Report(RepairContext ctx, string checkName, Exception e)
    {
        Debug.LogError($"[RepairRunner] Réparation de « {checkName} » : {e}");
        ctx.Fail($"{checkName} : la réparation a échoué ({e.Message}).");
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused || _awaitingFocus.Count == 0 || Busy) return;

        // Campagne en cours : on garde la demande pour le prochain retour de focus plutôt que de la
        // perdre — RunOnly refuse de l'interrompre.
        if (SystemCheckRunner.RunOnly(_awaitingFocus)) _awaitingFocus.Clear();
    }
}
