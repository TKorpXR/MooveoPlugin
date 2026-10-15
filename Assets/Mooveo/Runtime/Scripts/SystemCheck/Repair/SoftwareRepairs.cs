using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>Ce qu'il faut savoir d'un logiciel tiers pour le retrouver, le lancer ou l'installer.</summary>
public sealed class SoftwareInfo
{
    public string Name;                 // « SteamVR »
    public string Setting;              // réglage qui porte le chemin de l'exécutable
    public string[] ExeNames;           // exécutables acceptés (le premier est le lanceur)
    public string[] ProcessNames;       // processus qui prouvent qu'il tourne (sans .exe)
    public string[] RegistryNames;      // fragments du nom affiché dans « Programmes installés »
    public string[] KnownFolders;       // dossiers d'installation habituels
    public string InstallUrl;           // page de téléchargement ; vide = pas de bouton « Installer »

    public string ConfiguredExe => SettingsAccess.Get(Setting);

    public bool IsRunning() => ProcessNames.Any(ProcessChecks.IsRunning);
}

/// <summary>
/// Les trois logiciels dont dépend un poste GraffWall.
/// ⚠ Pages d'installation de HotFolderPrint et d'EOS Utility : À RENSEIGNER. Vides, le bouton
/// « Installer » n'est simplement pas proposé.
/// </summary>
public static class KnownSoftware
{
    public static readonly SoftwareInfo SteamVR = new SoftwareInfo
    {
        Name = "SteamVR",
        Setting = "SteamVREXEPath",
        ExeNames = new[] { "vrstartup.exe" },
        ProcessNames = new[] { "vrmonitor", "vrserver" },
        RegistryNames = new[] { "SteamVR" },
        KnownFolders = new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\SteamVR\bin\win64",
            @"C:\Program Files\Steam\steamapps\common\SteamVR\bin\win64"
        },
        // Page boutique de SteamVR. ⚠ Une URL `steam://install/250820` ouvrirait Steam directement,
        // mais seulement s'il est installé ET lancé : sur un poste neuf, le clic ne fait alors
        // visiblement rien. La page web marche dans tous les cas et propose l'installation.
        InstallUrl = "https://store.steampowered.com/app/250820/SteamVR/?l=french"
    };

    public static readonly SoftwareInfo EosUtility = new SoftwareInfo
    {
        Name = "EOS Utility",
        Setting = "EosUtilityEXEPath",
        ExeNames = new[] { "EOS Utility.exe", "EOS Utility 3.exe", "EOS Utility 2.exe" },
        ProcessNames = new[] { "EOS Utility", "EOS Utility 3", "EOS Utility 2" },
        RegistryNames = new[] { "EOS Utility" },
        KnownFolders = new[]
        {
            @"C:\Program Files (x86)\Canon\EOS Utility",
            @"C:\Program Files\Canon\EOS Utility",
            @"C:\Program Files (x86)\Canon\EOS Utility\EU3",
            @"C:\Program Files\Canon\EOS Utility\EU3"
        },
        // Page de téléchargement Canon, filtrée sur EOS Utility pour Windows 11.
        InstallUrl = "https://www.canon.fr/support/consumer/software/eos-utility.html" +
                     "?softwareid=tcm:79-1330031&productid=tcm:79-1374397&os=Windows%2011"
    };

    public static readonly SoftwareInfo HotFolderPrint = new SoftwareInfo
    {
        Name = "HotFolderPrint",
        Setting = "HotFolderEXEPath",
        ExeNames = new[] { "HotFolderPrint.exe" },
        ProcessNames = new[] { "HotFolderPrint" },
        RegistryNames = new[] { "HotFolderPrint", "Hot Folder Print" },
        KnownFolders = new[] { @"C:\DNP\HotFolderPrint", @"C:\Program Files\DNP\HotFolderPrint",
                               @"C:\Program Files (x86)\DNP\HotFolderPrint" },
        // Téléchargements DNP, filtrés sur Hot Folder Print.
        InstallUrl = "https://www.dnpphoto.com/downloads?search=1&search_category=7" +
                     "&search_keywords=hot+folder+print"
    };
}

/// <summary>
/// Recherche RAPIDE d'un logiciel, par ordre de fiabilité : processus en cours (chemin certain),
/// programmes installés (registre), dossiers connus. La recherche sur disque vient ensuite, à la
/// demande, par <see cref="FolderSearch"/>.
/// </summary>
public static class SoftwareSearch
{
    private static readonly string[] UninstallKeys =
    {
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    /// <summary>
    /// En arrière-plan : reg.exe coûte une centaine de millisecondes par ruche. La plateforme est lue
    /// ICI, sur le fil principal — les API Unity ne sont pas garanties hors de lui.
    /// </summary>
    public static Task<List<string>> QuickAsync(SoftwareInfo software)
    {
        bool windows = WindowsDialogs.Supported;
        return Task.Run(() => Quick(software, windows));
    }

    private static List<string> Quick(SoftwareInfo software, bool windows)
    {
        var found = new List<string>();
        void Consider(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return;
            try { exe = Path.GetFullPath(exe.Trim().Trim('"')); } catch { return; }
            if (File.Exists(exe) && !found.Contains(exe, StringComparer.OrdinalIgnoreCase)) found.Add(exe);
        }

        foreach (string process in software.ProcessNames) Consider(ExeOfProcess(process));

        foreach (string folder in windows ? InstallFolders(software) : Enumerable.Empty<string>())
            foreach (string exe in software.ExeNames)
                // L'exécutable est souvent plus bas que le dossier déclaré (SteamVR : bin\win64).
                foreach (string hit in FindWithin(folder, exe, 3)) Consider(hit);

        foreach (string folder in software.KnownFolders)
            foreach (string exe in software.ExeNames)
                Consider(Path.Combine(folder, exe));

        return found;
    }

    /// <summary>Dossiers d'installation déclarés dans « Programmes installés ».</summary>
    private static IEnumerable<string> InstallFolders(SoftwareInfo software)
    {
        var folders = new List<string>();
        foreach (string key in UninstallKeys)
        {
            // Un bloc par programme : l'en-tête HKEY_… puis ses valeurs « Nom REG_SZ Valeur ».
            string name = null, location = null, icon = null;
            foreach (string raw in HotFolderLocator.RegQuery($"query \"{key}\" /s"))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase))
                {
                    Collect(software, name, location, icon, folders);
                    name = location = icon = null;
                    continue;
                }

                string value = ValueOf(line, "DisplayName");
                if (value != null) { name = value; continue; }
                value = ValueOf(line, "InstallLocation");
                if (value != null) { location = value; continue; }
                value = ValueOf(line, "DisplayIcon");
                if (value != null) icon = value;
            }
            Collect(software, name, location, icon, folders);
        }
        return folders;
    }

    private static void Collect(SoftwareInfo software, string name, string location, string icon, List<string> folders)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!software.RegistryNames.Any(n => name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) return;

        if (!string.IsNullOrEmpty(location)) folders.Add(location.Trim('"'));

        // DisplayIcon vaut souvent « C:\…\programme.exe,0 » : son dossier est l'installation.
        if (!string.IsNullOrEmpty(icon))
        {
            string path = icon.Split(',')[0].Trim('"');
            try { if (File.Exists(path)) folders.Add(Path.GetDirectoryName(path)); }
            catch { /* chemin mal formé */ }
        }
    }

    /// <summary>« Nom    REG_SZ    Valeur » → Valeur, si la ligne porte ce nom.</summary>
    private static string ValueOf(string line, string valueName)
    {
        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith(valueName + " ", StringComparison.OrdinalIgnoreCase)) return null;
        int at = trimmed.IndexOf("REG_", StringComparison.Ordinal);
        if (at < 0) return null;
        int gap = trimmed.IndexOf(' ', at);
        return gap < 0 ? null : trimmed.Substring(gap).Trim();
    }

    private static string ExeOfProcess(string processName)
    {
        try
        {
            foreach (Process p in Process.GetProcessesByName(processName))
            {
                // MainModule lève si le processus tourne en administrateur : d'où le try par processus.
                try { return p.MainModule?.FileName; }
                catch { /* inaccessible */ }
                finally { p.Dispose(); }
            }
        }
        catch { /* énumération refusée */ }
        return null;
    }

    /// <summary>Cet exécutable dans ce dossier ou ses sous-dossiers, jusqu'à <paramref name="depth"/> niveaux.</summary>
    private static IEnumerable<string> FindWithin(string folder, string exe, int depth)
    {
        var hits = new List<string>();
        var level = new List<string> { folder };
        for (int d = 0; d <= depth && level.Count > 0; d++)
        {
            var next = new List<string>();
            foreach (string dir in level)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    string file = Path.Combine(dir, exe);
                    if (File.Exists(file)) hits.Add(file);
                    if (d < depth) next.AddRange(Directory.GetDirectories(dir));
                }
                catch { /* accès refusé */ }
            }
            level = next;
        }
        return hits;
    }
}

/// <summary>
/// Logiciel introuvable : retrouver l'exécutable, laisser le désigner, ou l'installer. Une fois le
/// chemin enregistré, le logiciel est LANCÉ dans la foulée — c'est ce qu'attend l'opérateur, et la
/// vérification suivante ne lui demandera pas un second geste.
/// </summary>
public sealed class MissingSoftwareRepair : ICheckRepair
{
    private enum Choice { Use, Widen, Pick, Install }

    private readonly SoftwareInfo _software;
    private readonly string _checkName;

    public MissingSoftwareRepair(SoftwareInfo software, string checkName)
    {
        _software = software;
        _checkName = checkName;
    }

    public string Label => $"Retrouver {_software.Name}";
    public bool Unattended => false;

    public IEnumerator Attempt(RepairContext ctx)
    {
        string title = $"{_software.Name} introuvable";

        ctx.Progress(title, "Recherche dans les programmes installés…");
        Task<List<string>> quick = SoftwareSearch.QuickAsync(_software);
        while (!quick.IsCompleted) yield return null;
        ctx.EndProgress();
        List<string> found = quick.Status == TaskStatus.RanToCompletion ? quick.Result : new List<string>();

        bool widened = false;
        while (true)
        {
            var options = new List<string>();
            var actions = new List<Choice>();

            foreach (string exe in found)
            {
                options.Add($"{exe}\nUtiliser ce programme");
                actions.Add(Choice.Use);
            }
            if (!widened)
            {
                options.Add("Chercher plus loin\nTous les disques, 30 secondes au plus");
                actions.Add(Choice.Widen);
            }
            if (WindowsDialogs.Supported)
            {
                options.Add("Choisir l'exécutable\nOuvre l'explorateur Windows");
                actions.Add(Choice.Pick);
            }
            if (!string.IsNullOrEmpty(_software.InstallUrl))
            {
                options.Add($"Installer {_software.Name}\nOuvre la page d'installation et réduit GraffWall");
                actions.Add(Choice.Install);
            }

            string message = $"Réglage {_software.Setting} :\n{_software.ConfiguredExe}\n\n" +
                             (found.Count == 0 ? "Aucune installation trouvée sur ce poste."
                                               : $"{found.Count} installation(s) trouvée(s) :");

            int picked = RepairContext.Cancelled;
            yield return ctx.Choose(title, message, options, i => picked = i);
            if (picked == RepairContext.Cancelled || picked >= actions.Count) yield break;

            switch (actions[picked])
            {
                case Choice.Use:
                    if (Apply(ctx, found[picked])) yield return LaunchSoftwareRepair.Launch(ctx, _software);
                    yield break;

                case Choice.Widen:
                    widened = true;
                    List<FolderSearch.Candidate> wide = null;
                    yield return ctx.Search(title, FolderSearch.FixedDrives(), new FolderSearch.Query
                    {
                        FileNames = _software.ExeNames,
                        MaxDepth = 8,
                        Budget = FolderSearch.WideBudget
                    }, r => wide = r);
                    foreach (FolderSearch.Candidate c in wide ?? new List<FolderSearch.Candidate>())
                        if (!found.Contains(c.Path, StringComparer.OrdinalIgnoreCase)) found.Add(c.Path);
                    continue;

                case Choice.Pick:
                    string chosen = null;
                    string start = FolderSearch.NearestExisting(_software.ConfiguredExe) ??
                                   Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    yield return ctx.PickExecutable($"Exécutable de {_software.Name}", start, p => chosen = p);
                    if (string.IsNullOrEmpty(chosen)) continue;
                    if (Apply(ctx, chosen)) yield return LaunchSoftwareRepair.Launch(ctx, _software);
                    yield break;

                case Choice.Install:
                    InstallSoftware(_software, _checkName);
                    yield break;
            }
        }
    }

    private bool Apply(RepairContext ctx, string exe)
    {
        if (!File.Exists(exe))
        {
            ctx.Fail($"{_software.Name} : {exe} n'existe pas.");
            return false;
        }

        if (!SettingsAccess.TrySet(_software.Setting, exe, out string error)) { ctx.Fail(error); return false; }

        // HotFolderPrint : la détection est en cache, elle doit relire le chemin qu'on vient de donner.
        if (_software == KnownSoftware.HotFolderPrint) HotFolderLocator.Resolve(force: true);
        return true;
    }

    /// <summary>
    /// Ouvre la page d'installation et RÉDUIT GraffWall : en plein écran, le navigateur s'ouvrirait
    /// derrière lui et l'opérateur ne verrait rien se passer. La vérification est rejouée quand
    /// GraffWall reprend le focus — l'opérateur installe, revient, la ligne se met à jour seule.
    /// </summary>
    public static void InstallSoftware(SoftwareInfo software, string checkName)
    {
        Application.OpenURL(software.InstallUrl);
        RepairRunner.RecheckOnFocus(checkName);
        WindowsDialogs.MinimizeGame();
    }
}

/// <summary>Logiciel installé mais fermé : le lancer, et attendre qu'il apparaisse.</summary>
public sealed class LaunchSoftwareRepair : ICheckRepair
{
    /// <summary>Temps laissé au processus pour apparaître. SteamVR est lent à démarrer.</summary>
    private const float StartupSeconds = 15f;

    private readonly SoftwareInfo _software;

    public LaunchSoftwareRepair(SoftwareInfo software) => _software = software;

    public string Label => $"Lancer {_software.Name}";
    public bool Unattended => true;

    public IEnumerator Attempt(RepairContext ctx) => Launch(ctx, _software);

    public static IEnumerator Launch(RepairContext ctx, SoftwareInfo software)
    {
        if (software.IsRunning()) yield break;

        string exe = software.ConfiguredExe;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            ctx.Fail($"{software.Name} : exécutable introuvable ({exe}).");
            yield break;
        }

        if (!AppLauncher.LaunchProcess(exe))
        {
            ctx.Fail($"{software.Name} n'a pas pu être lancé : {exe}");
            yield break;
        }

        ctx.Progress($"Lancement de {software.Name}", "Démarrage en cours…");
        float deadline = Time.realtimeSinceStartup + StartupSeconds;
        while (!software.IsRunning() && Time.realtimeSinceStartup < deadline)
            yield return new WaitForSecondsRealtime(0.5f);
        ctx.EndProgress();

        if (!software.IsRunning())
        {
            Debug.LogWarning($"[LaunchSoftwareRepair] {software.Name} lancé mais absent après {StartupSeconds} s.");
            ctx.Fail($"{software.Name} a été lancé mais n'a pas démarré après {StartupSeconds:0} s. " +
                     "Vérifiez s'il affiche une erreur.");
        }
    }
}
