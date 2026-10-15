using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Découvre et exécute les <see cref="ISystemCheck"/> au lancement, sans bloquer le démarrage, puis
/// signale chaque anomalie par une notification globale.
///
/// ⚠ La découverte se fait par RÉFLEXION pour tenir la règle « ajouter une vérification = ajouter
/// une classe ». Contrepartie : le stripping d'IL2CPP peut supprimer une classe que personne ne
/// référence, et la vérification disparaîtrait EN BUILD SEULEMENT — exactement le genre d'échec
/// silencieux que cette fonctionnalité existe pour supprimer. D'où deux garde-fous : l'attribut
/// [Preserve] sur les classes de base, et <see cref="MinimumExpectedChecks"/>, qui signale une
/// moisson anormalement maigre au lieu de la passer sous silence.
/// </summary>
[Preserve]
public class SystemCheckRunner : MonoBehaviour
{
    /// <summary>Plancher de vigilance : en dessous, on suspecte le stripping plutôt que la chance.</summary>
    private const int MinimumExpectedChecks = 2;

    /// <summary>
    /// Durée PLANCHER d'une vérification. Les contrôles synchrones rendent tous leur verdict dans
    /// la même frame : sans ce rythme, les vingt lignes de la liste basculeraient d'un coup, et
    /// l'opérateur ne verrait rien d'autre qu'un clignotement. Les faire défiler une par une est ce
    /// qui rend la campagne lisible — et ce qui donne du sens au défilement automatique.
    /// ⚠ C'est du confort d'affichage, jamais un délai imposé au démarrage : la campagne est
    /// asynchrone, rien ne l'attend.
    /// </summary>
    private const float MinimumSecondsPerCheck = 1f;

    /// <summary>Plancher de la passe courte : deux ou trois lignes, pas besoin d'étirer.</summary>
    private const float EssentialSecondsPerCheck = 0.35f;

    private static SystemCheckRunner _instance;
    private static List<ISystemCheck> _checks;
    private static readonly HashSet<string> _lastRunNames = new HashSet<string>();

    // Vérifications jouées par la passe EN COURS, pour les annonces. Distinct de _lastRunNames : une
    // revérification ciblée s'y AJOUTE, sans redéfinir ce que « la dernière campagne » a couvert.
    private readonly HashSet<string> _announced = new HashSet<string>();
    private HashSet<string> _only;

    private Coroutine _running;
    private bool _manual;
    private bool _essentialOnly;
    private CheckScope _scope = CheckScope.Game;

    /// <summary>Crée le runner à la demande — aucun câblage de scène, comme WallObjectManager.</summary>
    public static SystemCheckRunner Ensure()
    {
        if (_instance != null) return _instance;

        GameObject go = new GameObject("SystemCheckRunner") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<SystemCheckRunner>();
        return _instance;
    }

    /// <summary>
    /// Lance une campagne. <paramref name="manual"/> distingue la relance demandée par l'opérateur,
    /// qui mérite un retour même quand tout va bien, du passage automatique au démarrage.
    /// </summary>
    /// <param name="essentialOnly">
    /// Ne rejoue que les vérifications <see cref="ISystemCheck.Essential"/> ; les autres gardent le
    /// verdict de la campagne précédente.
    /// </param>
    public static void RunAll(bool manual = false, CheckScope scope = CheckScope.Game, bool essentialOnly = false)
    {
        SystemCheckRunner runner = Ensure();
        if (runner._running != null) runner.StopCoroutine(runner._running);

        runner._manual = manual;
        runner._scope = scope;
        runner._essentialOnly = essentialOnly;
        runner._running = runner.StartCoroutine(runner.RunAllCo());
    }

    /// <summary>
    /// Revérifie ces seules vérifications, quelle que soit la portée — après une réparation. Les
    /// autres gardent leur verdict. Refusé pendant une campagne : l'interrompre laisserait des
    /// vérifications sans verdict.
    /// </summary>
    public static bool RunOnly(IEnumerable<string> names)
    {
        SystemCheckRunner runner = Ensure();
        if (runner._running != null || names == null) return false;

        runner._only = new HashSet<string>(names);
        if (runner._only.Count == 0) { runner._only = null; return false; }

        runner._manual = false;
        runner._running = runner.StartCoroutine(runner.RunAllCo());
        return true;
    }

    /// <summary>Relance la dernière campagne à l'identique (portée ET passe courte ou complète).</summary>
    public static void RunAgain(bool manual = true)
        => RunAll(manual, LastScope, _instance != null && _instance._essentialOnly);

    /// <summary>Portée de la dernière campagne, pour la relancer à l'identique.</summary>
    public static CheckScope LastScope => _instance != null ? _instance._scope : CheckScope.Game;

    /// <summary>
    /// Vérifications réellement jouées par la dernière campagne. Après une passe courte, les
    /// rapports de <see cref="SystemStatus"/> contiennent AUSSI ceux de la passe d'avant : qui veut
    /// commenter « ce qui vient d'être vérifié » doit filtrer sur cette liste.
    /// </summary>
    public static IReadOnlyCollection<string> LastRunNames => _lastRunNames;

    /// <summary>Cette vérification a-t-elle été jouée par la dernière campagne ?</summary>
    public static bool WasPlayed(string name) => _lastRunNames.Contains(name);

    /// <summary>
    /// Noms des vérifications applicables à cette portée, dans l'ordre d'affichage. Sert à bâtir
    /// les lignes de la liste de calibration sans que celle-ci connaisse la moindre vérification.
    /// </summary>
    public static IEnumerable<string> NamesInScope(CheckScope scope, bool essentialOnly = false)
    {
        foreach (ISystemCheck check in Discover())
            if (Selected(check, scope, essentialOnly)) yield return check.Name;
    }

    /// <summary>
    /// Hors portée : la vérification n'a pas de sens dans cette scène, on ne l'exécute même pas —
    /// un verdict absent vaut mieux qu'un rouge permanent qui ne veut rien dire.
    /// </summary>
    private static bool Selected(ISystemCheck check, CheckScope scope, bool essentialOnly)
        => (check.Scope & scope) != 0 && (!essentialOnly || check.Essential);

    private IEnumerator RunAllCo()
    {
        List<ISystemCheck> all = Discover();
        HashSet<string> only = _only;
        _only = null;

        var selected = new List<ISystemCheck>();
        foreach (ISystemCheck check in all)
        {
            bool wanted = only != null ? only.Contains(check.Name) : Selected(check, _scope, _essentialOnly);
            if (wanted) selected.Add(check);
        }

        _announced.Clear();
        foreach (ISystemCheck check in selected) _announced.Add(check.Name);

        // Revérification ciblée : elle complète la dernière campagne, elle ne la remplace pas.
        if (only == null) _lastRunNames.Clear();
        _lastRunNames.UnionWith(_announced);

        SystemStatus.Running = true;
        bool partial = only != null || _essentialOnly;
        SystemStatus.Begin(partial ? _announced : null);

        float floor = partial ? EssentialSecondsPerCheck : MinimumSecondsPerCheck;
        foreach (ISystemCheck check in selected)
            yield return RunOne(check, all.IndexOf(check), floor);

        SystemStatus.End();
        Announce();
        _running = null;
    }

    // =========================================================================
    // Exécution d'une vérification
    // =========================================================================

    private IEnumerator RunOne(ISystemCheck check, int index, float floor)
    {
        CheckReport report = new CheckReport { Category = check.Category, Name = check.Name, Index = index };

        CheckResult result = null;
        Exception failure = null;
        float start = Time.realtimeSinceStartup;
        float deadline = start + Mathf.Max(0.1f, check.TimeoutSeconds);

        IEnumerator body;
        try
        {
            body = check.Run(r => result = r);
        }
        catch (Exception e)
        {
            // Une exception AVANT le premier yield sort ici, pas dans le garde.
            body = null;
            failure = e;
        }

        if (body != null)
        {
            IEnumerator guarded = Guard(body, e => failure = e);
            while (result == null && failure == null && Time.realtimeSinceStartup < deadline)
            {
                if (!guarded.MoveNext()) break;
                yield return guarded.Current;
            }
        }

        report.Seconds = Time.realtimeSinceStartup - start;

        // Rythme d'affichage : on complète jusqu'au plancher si la vérification a été instantanée.
        float remaining = floor - report.Seconds;
        if (remaining > 0f) yield return new WaitForSecondsRealtime(remaining);

        if (failure != null)
        {
            report.Error = failure.Message;
            report.Result = CheckResult.Warning(
                "La vérification a échoué : " + failure.Message,
                "Consultez le journal pour la trace complète.");
            Debug.LogError($"[SystemCheck] {check.Name} a levé : {failure}");
        }
        else if (result == null)
        {
            report.TimedOut = true;
            report.Result = CheckResult.Warning(
                $"Aucune réponse après {check.TimeoutSeconds:0.#} s.",
                "Le service est peut-être injoignable ; réessayez depuis « État du système ».");
        }
        else
        {
            report.Result = result;
        }

        SystemStatus.Add(report);
    }

    /// <summary>
    /// ⚠ C# interdit un `catch` autour d'un `yield` : une exception levée entre deux yield sortirait
    /// de la coroutine sans qu'aucun verdict ne soit rendu, et le runner attendrait son délai
    /// maximal pour rien. On avance donc l'itérateur à la main (même parade que AiGenerationGate).
    /// </summary>
    internal static IEnumerator Guard(IEnumerator inner, Action<Exception> onError)
    {
        while (true)
        {
            object current;
            try
            {
                if (!inner.MoveNext()) yield break;
                current = inner.Current;
            }
            catch (Exception e)
            {
                onError(e);
                yield break;
            }

            yield return current;
        }
    }

    // =========================================================================
    // Découverte
    // =========================================================================

    /// <summary>
    /// Types candidats : ceux du SDK ET ceux de toute assembly qui le référence (le jeu :
    /// Assembly-CSharp ou ses asmdefs). ⚠ Le SDK est une assembly à part : ne parcourir que la
    /// sienne rendrait INVISIBLES les vérifications déclarées par le jeu — or « ajouter une
    /// vérification = ajouter une classe » doit valoir aussi côté jeu. Seules les assemblies qui
    /// référencent le SDK peuvent implémenter ISystemCheck : les autres sont sautées sans les charger.
    /// </summary>
    private static IEnumerable<Type> CandidateTypes(Type contract)
    {
        Assembly sdk = contract.Assembly;
        string sdkName = sdk.GetName().Name;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly != sdk)
            {
                bool referencesSdk = false;
                foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                {
                    if (reference.Name == sdkName) { referencesSdk = true; break; }
                }
                if (!referencesSdk) continue;
            }

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }

            foreach (Type type in types)
            {
                if (type != null) yield return type;
            }
        }
    }

    private static List<ISystemCheck> Discover()
    {
        if (_checks != null) return _checks;

        _checks = new List<ISystemCheck>();
        Type contract = typeof(ISystemCheck);

        foreach (Type type in CandidateTypes(contract))
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!contract.IsAssignableFrom(type)) continue;
            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                Debug.LogWarning($"[SystemCheck] {type.Name} ignorée : pas de constructeur sans argument.");
                continue;
            }

            try { _checks.Add((ISystemCheck)Activator.CreateInstance(type)); }
            catch (Exception e) { Debug.LogError($"[SystemCheck] {type.Name} non instanciable : {e.Message}"); }
        }

        _checks.Sort((a, b) =>
        {
            int byCategory = string.CompareOrdinal(a.Category, b.Category);
            return byCategory != 0 ? byCategory : a.Order.CompareTo(b.Order);
        });

        if (_checks.Count < MinimumExpectedChecks)
            Debug.LogError($"[SystemCheck] Seulement {_checks.Count} vérification(s) trouvée(s). " +
                           "En build, c'est le signe d'un stripping trop agressif : vérifiez link.xml.");

        return _checks;
    }

    // =========================================================================
    // Restitution
    // =========================================================================

    private void Announce()
    {
        int played = 0;
        int snoozed = 0;
        bool allOk = true;

        foreach (CheckReport report in SystemStatus.Reports)
        {
            // Passe courte : les verdicts conservés ont déjà été annoncés, on ne les réémet pas.
            if (!_announced.Contains(report.Name)) continue;
            played++;

            // Réglé depuis (réparation, relance) : sa carte n'a plus lieu d'être.
            if (report.Severity == CheckSeverity.Ok) { NotificationCenter.DismissByKey(report.Key); continue; }

            // Mis en sourdine par l'opérateur : on ne le réannonce pas, et sa carte s'en va. Il reste
            // consultable, grisé, dans « État du système ».
            if (CheckSnooze.IsSnoozed(report.Key)) { NotificationCenter.DismissByKey(report.Key); snoozed++; continue; }
            allOk = false;

            CheckResult r = report.Result;
            string message = string.IsNullOrEmpty(r.Fix) ? r.Message : r.Message + "\n" + r.Fix;

            string actionLabel = null;
            Action action = null;
            if (!string.IsNullOrEmpty(r.GuideId))
            {
                actionLabel = "Voir la procédure";
                string guideId = r.GuideId;
                action = () => UIManager.Instance?.ShowGuidePopup(guideId);
            }

            // La CLÉ évite d'empiler la même carte à chaque relance de la campagne.
            NotificationCenter.Notify(
                report.Severity == CheckSeverity.Error ? NotificationLevel.Error : NotificationLevel.Warning,
                report.Name, message, actionLabel, action, key: report.Key);
        }

        // Tout est au vert : on ne dit rien au démarrage (le silence EST le bon signal), mais une
        // relance demandée à la main doit répondre quelque chose.
        if (_manual && allOk)
            NotificationCenter.Info("État du système",
                $"{played} vérifications passées, aucun problème détecté." +
                (snoozed > 0 ? $" {snoozed} problème(s) ignoré(s), visibles dans « État du système »." : ""));
    }
}
