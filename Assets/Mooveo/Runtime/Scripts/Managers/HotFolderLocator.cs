using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Debug = UnityEngine.Debug;

/// <summary>
/// Retrouve HotFolderPrint (DNP) sur le poste et, pour chaque orientation, le FICHIER de bordure
/// que le logiciel ira lire. Ce fichier porte un identifiant généré par HotFolder À L'IMPORT :
/// il est DIFFÉRENT sur chaque machine, il ne peut donc pas être codé en dur ni versionné.
/// Aucune dépendance Unity hors Debug : testable et appelable avant toute scène.
/// </summary>
public static class HotFolderLocator
{
    public enum Status
    {
        Ok,
        NotInstalled,       // HotFolderPrint introuvable
        NoBordersFolder,    // installé, mais pas de dossier Borders
        MissingLandscape,   // aucune bordure paysage importée
        MissingPortrait,    // aucune bordure portrait importée
        MissingBoth,
        BordersLocked,      // bordure présente mais non modifiable (verrou, lecture seule)
        ScanFailed          // la détection elle-même a échoué
    }

    public sealed class Result
    {
        public Status Status = Status.NotInstalled;
        public string Root;            // C:\DNP\HotFolderPrint
        public string ExePath;         // <Root>\HotFolderPrint.exe
        public string BordersFolder;   // <Root>\Borders
        public string LandscapeFile;   // chemin complet, null si absente
        public string PortraitFile;
        public string Message;         // message prêt à afficher, vide si Ok

        public bool Found => Status == Status.Ok;
        public bool HasLandscape => !string.IsNullOrEmpty(LandscapeFile);
        public bool HasPortrait => !string.IsNullOrEmpty(PortraitFile);
    }

    /// <summary>Format recherché : 4x6 (1800x1200 px à 300 dpi), le seul imprimé par GraffWall.</summary>
    private const string PreferredSize = "4x6";

    private static readonly string[] KnownRoots =
    {
        @"C:\DNP\HotFolderPrint",
        @"D:\DNP\HotFolderPrint"
    };

    private static Result _cached;

    /// <summary>Dernier résultat connu, sans relancer de scan (null si jamais résolu).</summary>
    public static Result Last => _cached;

    /// <summary>
    /// Résout l'installation et les deux fichiers de bordure. Mis en cache : le scan ne se refait
    /// qu'à la demande (force), typiquement quand l'opérateur vient d'importer une bordure.
    /// </summary>
    public static Result Resolve(bool force = false)
    {
        if (_cached != null && !force) return _cached;

        try
        {
            return _cached = Log(Scan());
        }
        catch (Exception e)
        {
            // Filet de sécurité : un défaut de détection ne doit jamais empêcher de lancer le jeu.
            Debug.LogError($"[HotFolderLocator] Détection interrompue : {e}");
            return _cached = new Result
            {
                Status = Status.ScanFailed,
                Message = "La détection de la bannière d'impression a échoué :\n" + e.Message
            };
        }
    }

    private static Result Scan()
    {
        Result result = new Result();

        try
        {
            result.Root = FindRoot();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HotFolderLocator] Recherche de l'installation interrompue : {e.Message}");
        }

        if (string.IsNullOrEmpty(result.Root))
        {
            result.Status = Status.NotInstalled;
            result.Message = "HotFolderPrint est introuvable sur ce poste. Installez-le " +
                             "(dossier habituel C:\\DNP\\HotFolderPrint), puis relancez GraffWall.";
            return result;
        }

        string exe = Path.Combine(result.Root, "HotFolderPrint.exe");
        if (File.Exists(exe)) result.ExePath = exe;

        result.BordersFolder = Path.Combine(result.Root, "Borders");
        if (!Directory.Exists(result.BordersFolder))
        {
            result.Status = Status.NoBordersFolder;
            result.Message = $"Le dossier des bordures de HotFolderPrint est introuvable :\n{result.BordersFolder}\n" +
                             "Importez une bordure 4x6 dans HotFolderPrint.";
            return result;
        }

        // Source AUTORITAIRE : la base de HotFolder dit quel fichier correspond à quelle
        // orientation. Repli sur les dimensions du PNG si elle est illisible ou verrouillée.
        if (!TryReadFromDatabase(result, out string landscape, out string portrait))
            ScanBordersFolder(result.BordersFolder, out landscape, out portrait);

        result.LandscapeFile = Writable(landscape, out string landscapeLock);
        result.PortraitFile = Writable(portrait, out string portraitLock);

        // Bordure trouvée mais inutilisable : ce n'est PAS « aucune bordure importée », et le
        // remède est l'inverse — il ne faut surtout pas en réimporter une.
        string locked = landscapeLock ?? portraitLock;
        if (locked != null)
        {
            result.Status = Status.BordersLocked;
            result.Message = "La bordure de HotFolderPrint ne peut pas être modifiée :\n" + locked +
                             "\n\nFermez HotFolderPrint, ou retirez l'attribut « lecture seule » du fichier.";
            return result;
        }

        if (result.HasLandscape && result.HasPortrait)
        {
            result.Status = Status.Ok;
            result.Message = "";
        }
        else if (!result.HasLandscape && !result.HasPortrait)
        {
            result.Status = Status.MissingBoth;
            result.Message = "Aucune bordure 4x6 n'est importée dans HotFolderPrint. " +
                             "Importez-en une en paysage ET une en portrait, puis relancez GraffWall.";
        }
        else if (!result.HasLandscape)
        {
            result.Status = Status.MissingLandscape;
            result.Message = "Aucune bordure 4x6 PAYSAGE importée dans HotFolderPrint. " +
                             "Importez-en une pour que la bannière paysage s'applique.";
        }
        else
        {
            result.Status = Status.MissingPortrait;
            result.Message = "Aucune bordure 4x6 PORTRAIT importée dans HotFolderPrint. " +
                             "Importez-en une pour que la bannière portrait s'applique.";
        }

        return result;
    }

    /// <summary>Reporte le résultat dans GlobalSettings, sans jamais écraser un réglage VALIDE.</summary>
    public static void ApplyTo(GlobalSettings.Core.GlobalSettings settings, bool force = false)
    {
        if (settings == null) return;
        Result r = Resolve(force);
        if (string.IsNullOrEmpty(r.Root)) return;

        if (!string.IsNullOrEmpty(r.ExePath) && !File.Exists(settings.HotFolderEXEPath.Value))
            settings.HotFolderEXEPath.Value = r.ExePath;

        if (!Directory.Exists(settings.DNPBannerPath.Value))
            settings.DNPBannerPath.Value = r.BordersFolder;

        // Les réglages portent un NOM DE FICHIER, pas un chemin : un réglage qui n'existe pas dans
        // le dossier des bordures est faux (le portrait contenait un chemin de dossier) et remplacé.
        if (r.HasLandscape && !IsValidBannerName(settings.DNPBannerFileName_Landscape.Value, r.BordersFolder))
            settings.DNPBannerFileName_Landscape.Value = Path.GetFileName(r.LandscapeFile);

        if (r.HasPortrait && !IsValidBannerName(settings.DNPBannerFileName_Portrait.Value, r.BordersFolder))
            settings.DNPBannerFileName_Portrait.Value = Path.GetFileName(r.PortraitFile);
    }

    /// <summary>Chemin du fichier de bordure à écraser pour une orientation, null si indisponible.</summary>
    public static string BannerFileFor(bool landscape)
    {
        Result r = Resolve();
        return landscape ? r.LandscapeFile : r.PortraitFile;
    }

    private static bool IsValidBannerName(string name, string bordersFolder)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.IndexOfAny(new[] { '/', '\\' }) >= 0) return false;   // un chemin, pas un nom
        return File.Exists(Path.Combine(bordersFolder, name));
    }

    // =========================================================================
    // 1. Trouver l'installation
    // =========================================================================

    private static string FindRoot()
    {
        // Choix EXPLICITE de l'opérateur (bouton « Résoudre ») : il prime, sinon un HotFolder installé
        // hors des chemins connus resterait introuvable même après qu'on l'a désigné.
        string fromSetting = RootFromSetting();
        if (IsRoot(fromSetting)) return fromSetting;

        string fromRegistry = RootFromRegistry();
        if (IsRoot(fromRegistry)) return fromRegistry;

        foreach (string candidate in KnownRoots)
            if (IsRoot(candidate)) return candidate;

        foreach (DriveInfo drive in SafeDrives())
        {
            string candidate = Path.Combine(drive.Name, @"DNP\HotFolderPrint");
            if (IsRoot(candidate)) return candidate;
        }

        string fromProcess = RootFromProcess();
        return IsRoot(fromProcess) ? fromProcess : null;
    }

    private static string RootFromSetting()
    {
        string exe = GlobalSettings.Core.GlobalSettings.Instance != null ? GlobalSettings.Core.GlobalSettings.Instance.HotFolderEXEPath.Value : null;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;
        return Path.GetDirectoryName(exe);
    }

    private static bool IsRoot(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        path = path.TrimEnd('/', '\\');
        return Directory.Exists(path) &&
               (File.Exists(Path.Combine(path, "HotFolderPrint.exe")) ||
                Directory.Exists(Path.Combine(path, "Borders")));
    }

    private static IEnumerable<DriveInfo> SafeDrives()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { yield break; }

        foreach (DriveInfo d in drives)
        {
            bool usable;
            try { usable = d.DriveType == DriveType.Fixed && d.IsReady; }
            catch { usable = false; }
            if (usable) yield return d;
        }
    }

    /// <summary>
    /// HotFolderPrint renseigne son InstallLocation dans la base de désinstallation. On passe par
    /// reg.exe et non par Microsoft.Win32.Registry, absent des assemblies référencées par Unity.
    /// </summary>
    private static string RootFromRegistry()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string[] hives =
        {
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (string hive in hives)
        foreach (string view in new[] { "/reg:64", "/reg:32" })
        {
            foreach (string line in RegQuery($"query \"{hive}\" /s /v InstallLocation {view}"))
            {
                if (line.IndexOf("HotFolderPrint", StringComparison.OrdinalIgnoreCase) < 0) continue;

                // Format : "    InstallLocation    REG_SZ    C:\DNP\HotFolderPrint\"
                int at = line.IndexOf("REG_SZ", StringComparison.Ordinal);
                if (at < 0) continue;

                string value = line.Substring(at + "REG_SZ".Length).Trim().Trim('"').TrimEnd('/', '\\');
                if (!string.IsNullOrEmpty(value)) return value;
            }
        }
#endif
        return null;
    }

    /// <summary>Sortie brute de <c>reg.exe</c>, une ligne par entrée. Partagé avec SoftwareSearch.</summary>
    internal static string[] RegQuery(string arguments)
    {
        try
        {
            var info = new ProcessStartInfo("reg.exe", arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (Process p = Process.Start(info))
            {
                if (p == null) return Array.Empty<string>();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return output.Split('\n');
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HotFolderLocator] Lecture du registre impossible : {e.Message}");
            return Array.Empty<string>();
        }
    }

    private static string RootFromProcess()
    {
        try
        {
            foreach (Process p in Process.GetProcessesByName("HotFolderPrint"))
            {
                // MainModule lève si le process tourne en administrateur : d'où le try par process.
                try { return Path.GetDirectoryName(p.MainModule?.FileName); }
                catch { /* process inaccessible */ }
                finally { p.Dispose(); }
            }
        }
        catch { /* énumération refusée */ }

        return null;
    }

    // =========================================================================
    // 2a. Trouver les bordures — la base de HotFolder
    // =========================================================================

    /// <summary>
    /// La table des bordures est stockée en CLAIR dans la base SQLite de HotFolder : nom, taille,
    /// orientation, puis l'URL du fichier. On la lit en texte brut plutôt que par un plugin natif
    /// SQLite — lecture seule, base ouverte en partage puisque HotFolder la tient.
    /// </summary>
    private static bool TryReadFromDatabase(Result result, out string landscape, out string portrait)
    {
        landscape = null;
        portrait = null;

        string dbPath = FindDatabaseFile(result.Root);
        if (dbPath == null) return false;

        string text;
        try
        {
            using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] bytes = new byte[fs.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = fs.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                text = System.Text.Encoding.ASCII.GetString(bytes, 0, read);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HotFolderLocator] Base HotFolder illisible ({dbPath}) : {e.Message}");
            return false;
        }

        // On ne retient QUE les bordures importées par l'opérateur (<Root>\Borders). Celles livrées
        // avec le logiciel (<Root>\Res\Borders) ne doivent jamais être écrasées.
        string prefix = "file:///" + result.BordersFolder.Replace('\\', '/').TrimEnd('/') + "/";

        var candidates = new List<(string path, string size, bool landscape)>();
        int at = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        while (at >= 0)
        {
            bool? isLandscape = OrientationBefore(text, at);
            string file = FileAfter(text, at + prefix.Length, result.BordersFolder);

            if (isLandscape.HasValue && file != null)
                candidates.Add((file, SizeBefore(text, at), isLandscape.Value));

            at = text.IndexOf(prefix, at + 1, StringComparison.OrdinalIgnoreCase);
        }

        landscape = Best(candidates, true);
        portrait = Best(candidates, false);
        return landscape != null || portrait != null;
    }

    /// <summary>Le nom du fichier de base varie selon les versions : on prend le premier vrai SQLite.</summary>
    private static string FindDatabaseFile(string root)
    {
        string dataDir = Path.Combine(root, @"Res\Data");
        if (!Directory.Exists(dataDir)) return null;

        try
        {
            foreach (string file in Directory.GetFiles(dataDir).OrderBy(f => f.Length))
                if (IsSqlite(file)) return file;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HotFolderLocator] Dossier de données illisible ({dataDir}) : {e.Message}");
        }

        return null;
    }

    private static bool IsSqlite(string path)
    {
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] header = new byte[16];
                if (fs.Read(header, 0, 16) < 16) return false;
                return System.Text.Encoding.ASCII.GetString(header) == "SQLite format 3\0";
            }
        }
        catch { return false; }
    }

    /// <summary>Orientation collée devant l'URL dans l'enregistrement (aucun séparateur en base).</summary>
    private static bool? OrientationBefore(string text, int at)
    {
        if (at >= 9 && string.CompareOrdinal(text, at - 9, "Landscape", 0, 9) == 0) return true;
        if (at >= 8 && string.CompareOrdinal(text, at - 8, "Portrait", 0, 8) == 0) return false;
        return null;
    }

    /// <summary>Taille (« s4x6 ») collée devant l'orientation. Vide si illisible.</summary>
    private static string SizeBefore(string text, int at)
    {
        int end = at - (OrientationBefore(text, at) == true ? 9 : 8);
        int start = end;
        while (start > 0 && end - start < 12 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        return start < end ? text.Substring(start, end - start) : "";
    }

    /// <summary>
    /// Lit le nom de fichier qui suit l'URL. Les enregistrements ne sont pas délimités : la fin du
    /// nom est suivie d'un ou deux octets d'en-tête du suivant, qu'on retire en validant l'existence.
    /// </summary>
    private static string FileAfter(string text, int start, string bordersFolder)
    {
        int end = start;
        while (end < text.Length && text[end] >= ' ' && text[end] <= '~' &&
               text[end] != '/' && text[end] != '\\' && end - start < 260) end++;

        for (int len = end - start; len > 0 && end - start - len < 4; len--)
        {
            string candidate = Path.Combine(bordersFolder, text.Substring(start, len));
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>Priorité au format 4x6 ; à égalité, la bordure importée le plus récemment.</summary>
    private static string Best(List<(string path, string size, bool landscape)> candidates, bool landscape)
    {
        return candidates
            .Where(c => c.landscape == landscape)
            .OrderByDescending(c => c.size != null && c.size.IndexOf(PreferredSize, StringComparison.OrdinalIgnoreCase) >= 0)
            .ThenByDescending(c => LastWrite(c.path))
            .Select(c => c.path)
            .FirstOrDefault();
    }

    // =========================================================================
    // 2b. Repli — les dimensions du PNG
    // =========================================================================

    /// <summary>
    /// Les bordures importées sont des PNG SANS extension : l'orientation se lit dans l'en-tête
    /// (largeur/hauteur aux offsets 16 et 20, en big-endian).
    /// </summary>
    private static void ScanBordersFolder(string bordersFolder, out string landscape, out string portrait)
    {
        landscape = null;
        portrait = null;

        string[] files;
        try { files = Directory.GetFiles(bordersFolder); }
        catch (Exception e)
        {
            Debug.LogWarning($"[HotFolderLocator] Dossier des bordures illisible : {e.Message}");
            return;
        }

        var found = new List<(string path, int w, int h)>();
        foreach (string file in files)
            if (TryReadPngSize(file, out int w, out int h)) found.Add((file, w, h));

        landscape = BestBySize(found, true);
        portrait = BestBySize(found, false);
    }

    private static string BestBySize(List<(string path, int w, int h)> found, bool landscape)
    {
        return found
            .Where(f => landscape ? f.w > f.h : f.h > f.w)
            .OrderByDescending(f => Math.Max(f.w, f.h) == 1800 && Math.Min(f.w, f.h) == 1200) // 4x6 à 300 dpi
            .ThenByDescending(f => LastWrite(f.path))
            .Select(f => f.path)
            .FirstOrDefault();
    }

    private static bool TryReadPngSize(string path, out int width, out int height)
    {
        width = height = 0;
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] head = new byte[24];
                if (fs.Read(head, 0, 24) < 24) return false;
                if (head[0] != 0x89 || head[1] != 'P' || head[2] != 'N' || head[3] != 'G') return false;

                width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                return width > 0 && height > 0;
            }
        }
        catch { return false; }
    }

    // =========================================================================
    // Utilitaires
    // =========================================================================

    private static DateTime LastWrite(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    /// <summary>
    /// Un fichier verrouillé ou en lecture seule n'est pas un candidat. <paramref name="lockReason"/>
    /// distingue « pas de bordure » de « bordure inutilisable » : les deux remèdes sont opposés.
    /// </summary>
    private static string Writable(string path, out string lockReason)
    {
        lockReason = null;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
            return path;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HotFolderLocator] Bordure non inscriptible ({path}) : {e.Message}");
            lockReason = $"{path}\n({e.Message})";
            return null;
        }
    }

    private static Result Log(Result r)
    {
        if (r.Status == Status.Ok)
            Debug.Log($"[HotFolderLocator] HotFolderPrint : {r.Root}\n" +
                      $"  paysage  : {r.LandscapeFile}\n  portrait : {r.PortraitFile}");
        else
            Debug.LogWarning($"[HotFolderLocator] {r.Status} — {r.Message}");

        return r;
    }
}
