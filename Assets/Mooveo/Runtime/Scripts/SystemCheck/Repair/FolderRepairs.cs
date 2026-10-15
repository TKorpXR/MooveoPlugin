using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Dossier introuvable, ou inscriptible nulle part : chercher, proposer, laisser choisir, ou créer.
///
/// Déroulé : recherche rapide autour du chemin configuré (3 s) → proposition des candidats → au
/// besoin, recherche sur tous les disques (30 s), explorateur Windows, ou création sur place. Le
/// chemin retenu est écrit dans le réglage PAR SON NOM, puis enregistré.
///
/// ⚠ En ÉDITEUR, `StickersPath` et `StencilsPath` sont forcés sur les chemins de développement à
/// chaque chargement de GlobalSettings : une réparation de ces deux réglages ne survit pas à un
/// relancement en éditeur. En build, si.
/// </summary>
public sealed class MissingFolderRepair : ICheckRepair
{
    private enum Choice { Use, Widen, Pick, Create }

    private readonly string _label;         // « Stickers »
    private readonly string _setting;       // « StickersPath »
    private readonly string _configured;    // chemin actuel du réglage
    private readonly string[] _names;       // noms de dossier acceptés
    private readonly string[] _extensions;  // fichiers attendus dedans, pour classer les candidats
    private readonly string[] _extraRoots;  // où chercher en priorité (racine HotFolder…)
    private readonly bool _pickOnly;        // dossier présent mais refusé en écriture : on en choisit un autre

    public MissingFolderRepair(string label, string setting, string configured,
                               IEnumerable<string> aliases = null, string[] extensions = null,
                               string[] extraRoots = null, bool pickOnly = false)
    {
        _label = label;
        _setting = setting;
        _configured = configured;
        _extensions = extensions;
        _extraRoots = extraRoots;
        _pickOnly = pickOnly;

        var names = new List<string>();
        string leaf = LeafOf(configured);
        if (!string.IsNullOrEmpty(leaf)) names.Add(leaf);
        if (aliases != null) names.AddRange(aliases);
        _names = names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string Label => _pickOnly ? $"Choisir un autre dossier {_label}" : $"Retrouver le dossier {_label}";
    public bool Unattended => false;

    public IEnumerator Attempt(RepairContext ctx)
    {
        string title = _pickOnly ? $"Dossier {_label} inutilisable" : $"Dossier {_label} introuvable";
        var found = new List<FolderSearch.Candidate>();

        if (!_pickOnly)
            yield return ctx.Search(title, QuickRoots(), Query(3, FolderSearch.QuickBudget), r => found = r);

        bool widened = _pickOnly;
        while (true)
        {
            var options = new List<string>();
            var actions = new List<Choice>();

            foreach (FolderSearch.Candidate c in found)
            {
                options.Add($"{c.Path}\n{Describe(c.Files)}");
                actions.Add(Choice.Use);
            }
            if (!widened)
            {
                options.Add("Chercher plus loin\nTous les disques, 30 secondes au plus");
                actions.Add(Choice.Widen);
            }
            if (WindowsDialogs.Supported)
            {
                options.Add("Choisir moi-même\nOuvre l'explorateur Windows");
                actions.Add(Choice.Pick);
            }
            if (!_pickOnly && Path.IsPathRooted(_configured ?? ""))
            {
                options.Add($"Créer le dossier vide\n{_configured}");
                actions.Add(Choice.Create);
            }

            int picked = RepairContext.Cancelled;
            yield return ctx.Choose(title, Message(found.Count), options, i => picked = i);
            if (picked == RepairContext.Cancelled || picked >= actions.Count) yield break;

            switch (actions[picked])
            {
                case Choice.Use:
                    Apply(ctx, found[picked].Path);
                    yield break;

                case Choice.Widen:
                    widened = true;
                    yield return ctx.Search(title, FolderSearch.FixedDrives(), Query(8, FolderSearch.WideBudget),
                                            r => found = r);
                    continue;

                case Choice.Pick:
                    string chosen = null;
                    yield return ctx.PickFolder($"Dossier {_label} ({_setting})", StartFolder(), p => chosen = p);
                    // Annulé dans Windows : on revient à la proposition plutôt que de tout abandonner.
                    if (string.IsNullOrEmpty(chosen)) continue;
                    Apply(ctx, chosen);
                    yield break;

                case Choice.Create:
                    Create(ctx);
                    yield break;
            }
        }
    }

    private string Message(int count)
    {
        string where = $"Réglage {_setting} :\n{(string.IsNullOrEmpty(_configured) ? "(vide)" : _configured)}";
        if (_pickOnly) return where + "\n\n" + MooveoGameHooks.AppName + " ne peut pas écrire dans ce dossier. Choisissez-en un autre.";
        return count == 0
            ? where + "\n\nAucun dossier correspondant trouvé à proximité."
            : where + $"\n\n{count} dossier(s) trouvé(s) — le plus fourni en premier :";
    }

    private void Apply(RepairContext ctx, string path)
    {
        // Un dossier qu'on ne peut pas remplir ne répare rien : même règle que les vérifications.
        string refused = PathChecks.Writable(path);
        if (refused != null)
        {
            ctx.Fail($"{_label} : impossible d'écrire dans {path}. {refused}");
            return;
        }

        if (!SettingsAccess.TrySet(_setting, path, out string error)) ctx.Fail(error);
    }

    private void Create(RepairContext ctx)
    {
        string refused = PathChecks.Writable(_configured);   // crée le dossier, puis y écrit un témoin
        if (refused != null) ctx.Fail($"{_label} : impossible de créer {_configured}. {refused}");
    }

    private FolderSearch.Query Query(int depth, TimeSpan budget) => new FolderSearch.Query
    {
        FolderNames = _names,
        Extensions = _extensions,
        MaxDepth = depth,
        Budget = budget
    };

    private IEnumerable<string> QuickRoots()
    {
        var roots = new List<string>();
        if (_extraRoots != null) roots.AddRange(_extraRoots);
        roots.AddRange(FolderSearch.AncestorsOf(_configured, 2));
        roots.Add(GlobalSettings.Core.GlobalSettings.GetAppRootPath());
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        return roots;
    }

    private string StartFolder() => FolderSearch.NearestExisting(_configured) ?? GlobalSettings.Core.GlobalSettings.GetAppRootPath();

    private static string Describe(int files) => files switch
    {
        0 => "vide",
        1 => "1 fichier",
        _ => files >= 5000 ? "plus de 5 000 fichiers" : $"{files} fichiers"
    };

    private static string LeafOf(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try { return Path.GetFileName(path.TrimEnd('/', '\\')); }
        catch { return null; }
    }
}

/// <summary>
/// Dossier dont le chemin n'est PAS un réglage (déduit d'un autre) : seule la création a un sens.
/// Sans question — créer un dossier vide à l'endroit attendu ne peut rien casser.
/// </summary>
public sealed class CreateFolderRepair : ICheckRepair
{
    private readonly string _label;
    private readonly string _path;

    public CreateFolderRepair(string label, string path)
    {
        _label = label;
        _path = path;
    }

    public string Label => $"Créer le dossier {_label}";
    public bool Unattended => true;

    public IEnumerator Attempt(RepairContext ctx)
    {
        string refused = PathChecks.Writable(_path);
        if (refused != null) ctx.Fail($"{_label} : impossible de créer {_path}. {refused}");
        yield break;
    }
}

/// <summary>Réparation sans question, faite d'un seul geste (réécrire les gabarits…).</summary>
public sealed class ActionRepair : ICheckRepair
{
    private readonly string _label;
    private readonly Func<string> _action;   // null = réussi, sinon le motif de l'échec

    public ActionRepair(string label, Func<string> action)
    {
        _label = label;
        _action = action;
    }

    public string Label => _label;
    public bool Unattended => true;

    public IEnumerator Attempt(RepairContext ctx)
    {
        string failure = _action?.Invoke();
        if (failure != null) ctx.Fail(failure);
        yield break;
    }
}
