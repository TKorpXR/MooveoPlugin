using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Recherche de dossiers ou de fichiers sur le disque, EN ARRIÈRE-PLAN. Parcourir un disque bloque
/// plusieurs secondes : sur le fil principal, l'image se figerait au projecteur.
///
/// ⚠ Aucune API Unity ici (ni Debug, ni GlobalSettings) : ce code tourne hors du fil principal.
/// ⚠ Parcours en LARGEUR, borné en profondeur ET en temps : les dossiers proches sortent d'abord, et
/// un disque énorme ne fait jamais attendre plus que le budget. « Arrêter » rend ce qui est déjà trouvé.
/// </summary>
public static class FolderSearch
{
    public static readonly TimeSpan QuickBudget = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan WideBudget = TimeSpan.FromSeconds(30);

    /// <summary>Plafond du décompte de fichiers d'un candidat : au-delà, on sait déjà qu'il est plein.</summary>
    private const int CountCap = 5000;

    public sealed class Candidate
    {
        public string Path;
        public int Files;
    }

    public sealed class Query
    {
        /// <summary>Noms de DOSSIER recherchés (casse ignorée). Vide = on ne cherche pas de dossier.</summary>
        public IEnumerable<string> FolderNames;

        /// <summary>Noms de FICHIER recherchés (ex. « vrstartup.exe »). Vide = on ne cherche pas de fichier.</summary>
        public IEnumerable<string> FileNames;

        /// <summary>Extensions comptées dans un dossier candidat (« .png »). Null = tous les fichiers.</summary>
        public string[] Extensions;

        public int MaxDepth = 3;
        public TimeSpan Budget = QuickBudget;
        public int MaxResults = 5;
    }

    /// <summary>Avancement, lu par le fil principal pour l'afficher.</summary>
    public sealed class Progress
    {
        public int Visited;
    }

    // Jamais de candidat utile là-dedans, et certains sont immenses.
    private static readonly HashSet<string> Skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin", "System Volume Information", "Windows", "WinSxS", "node_modules", ".git",
        "Library", "Temp", "obj", "AppData", "ProgramData", "Recovery", "PerfLogs"
    };

    public static Task<List<Candidate>> RunAsync(IEnumerable<string> roots, Query query,
                                                 CancellationToken token, Progress progress)
    {
        List<string> rootList = roots?.Where(r => !string.IsNullOrEmpty(r)).ToList() ?? new List<string>();
        return Task.Run(() => Run(rootList, query, token, progress));
    }

    private static List<Candidate> Run(List<string> roots, Query query, CancellationToken token, Progress progress)
    {
        var folderNames = new HashSet<string>(query.FolderNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var fileNames = (query.FileNames ?? Enumerable.Empty<string>()).ToList();
        var found = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<KeyValuePair<string, int>>();
        Stopwatch clock = Stopwatch.StartNew();

        foreach (string root in roots)
        {
            string full;
            try { full = Path.GetFullPath(root); } catch { continue; }
            if (Directory.Exists(full) && seen.Add(full)) queue.Enqueue(new KeyValuePair<string, int>(full, 0));
        }

        while (queue.Count > 0 && clock.Elapsed < query.Budget && !token.IsCancellationRequested)
        {
            KeyValuePair<string, int> item = queue.Dequeue();
            string dir = item.Key;
            if (progress != null) Interlocked.Increment(ref progress.Visited);

            foreach (string fileName in fileNames)
            {
                string file = Path.Combine(dir, fileName);
                if (File.Exists(file) && !found.ContainsKey(file))
                    found[file] = new Candidate { Path = file, Files = 1 };
            }

            if (item.Value >= query.MaxDepth) continue;

            foreach (string child in SafeChildren(dir))
            {
                if (token.IsCancellationRequested) break;

                string name = Path.GetFileName(child);
                if (Skipped.Contains(name) || !seen.Add(child) || !Browsable(child)) continue;

                if (folderNames.Contains(name) && !found.ContainsKey(child))
                    found[child] = new Candidate { Path = child, Files = CountFiles(child, query.Extensions) };

                queue.Enqueue(new KeyValuePair<string, int>(child, item.Value + 1));
            }
        }

        // Le mieux fourni d'abord : c'est le nombre de fichiers qui départage deux homonymes.
        return found.Values
            .OrderByDescending(c => c.Files)
            .ThenBy(c => c.Path.Length)
            .Take(query.MaxResults)
            .ToList();
    }

    /// <summary>Racines de tous les disques fixes prêts.</summary>
    public static List<string> FixedDrives()
    {
        var drives = new List<string>();
        try
        {
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try { if (d.DriveType == DriveType.Fixed && d.IsReady) drives.Add(d.RootDirectory.FullName); }
                catch { /* lecteur illisible */ }
            }
        }
        catch { /* énumération refusée */ }
        return drives;
    }

    /// <summary>Le dossier existant le plus proche d'un chemin qui, lui, n'existe peut-être plus.</summary>
    public static string NearestExisting(string path)
    {
        try
        {
            string current = string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(current)) return current;
                current = Path.GetDirectoryName(current);
            }
        }
        catch { /* chemin mal formé */ }
        return null;
    }

    /// <summary>Le dossier existant le plus proche, puis ses <paramref name="levels"/> parents.</summary>
    public static List<string> AncestorsOf(string path, int levels)
    {
        var result = new List<string>();
        string current = NearestExisting(path);
        for (int i = 0; i <= levels && !string.IsNullOrEmpty(current); i++)
        {
            result.Add(current);
            try { current = Path.GetDirectoryName(current); }
            catch { break; }
        }
        return result;
    }

    public static int CountFiles(string folder, string[] extensions)
    {
        int count = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (extensions == null || extensions.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    count++;
                if (count >= CountCap) break;
            }
        }
        catch { /* sous-dossier refusé : on garde ce qui a été compté */ }
        return count;
    }

    private static IEnumerable<string> SafeChildren(string dir)
    {
        var children = new List<string>();
        try { children.AddRange(Directory.EnumerateDirectories(dir)); }
        catch { /* accès refusé, dossier disparu */ }
        return children;
    }

    /// <summary>Jonctions et dossiers système : boucles infinies et contenu sans intérêt.</summary>
    private static bool Browsable(string dir)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(dir);
            return (attributes & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden)) == 0;
        }
        catch { return false; }
    }
}
