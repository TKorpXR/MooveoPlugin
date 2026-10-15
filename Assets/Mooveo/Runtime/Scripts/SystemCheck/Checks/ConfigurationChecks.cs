using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Vérifications de CONFIGURATION : le fichier de réglages, les chemins, les droits d'écriture.
///
/// ⚠ Règle commune à tout ce fichier : `Directory.Exists` ne suffit JAMAIS. Un dossier peut exister
/// en lecture seule, ou sur un partage réseau monté sans droit d'écriture. Seule une écriture réelle
/// tranche, et c'est ce que fait <see cref="PathChecks.Writable"/>.
/// </summary>
public static class PathChecks
{
    /// <summary>Écrit puis supprime un fichier témoin. Renvoie null si tout va bien, le motif sinon.</summary>
    public static string Writable(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string probe = Path.Combine(folder, ".mooveo_write_test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return null;
        }
        catch (System.Exception e)
        {
            return e.Message;
        }
    }

    /// <summary>Nombre de fichiers du dossier, -1 s'il est illisible.</summary>
    public static int CountFiles(string folder, string pattern = "*")
    {
        try
        {
            return Directory.Exists(folder)
                ? Directory.GetFiles(folder, pattern, SearchOption.AllDirectories).Length
                : -1;
        }
        catch { return -1; }
    }

    public static GlobalSettings.Core.GlobalSettings Settings => GlobalSettings.Core.GlobalSettings.Instance;

    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

    /// <summary>
    /// Réparation d'un dossier en défaut : absent → le retrouver ; présent mais refusé en écriture →
    /// en choisir un autre. Les remèdes sont opposés, d'où le test.
    /// </summary>
    public static ICheckRepair FolderRepair(string label, string setting, string path,
                                            string[] aliases = null, string[] extensions = null,
                                            string[] extraRoots = null)
    {
        bool exists = !string.IsNullOrEmpty(path) && Directory.Exists(path);
        return new MissingFolderRepair(label, setting, path, aliases, extensions, extraRoots, pickOnly: exists);
    }

    /// <summary>Chemin du fichier à corriger — le citer évite de le faire chercher.</summary>
    public static string SettingsFilePath =>
        Path.Combine(GlobalSettings.Core.GlobalSettings.GetAppRootPath(), "GlobalSettings.json");
}

/// <summary>
/// ⚠ `GlobalSettings.json` est RÉÉCRIT à chaque lancement (upsert de `LoadSettings`). Sur un dossier
/// en lecture seule, l'écriture échoue en silence et TOUT réglage modifié est perdu au redémarrage
/// suivant — sans que rien ne le dise.
/// </summary>
[Preserve]
public class SettingsFileCheck : SyncSystemCheck
{
    public override string Category => CheckCategory.Configuration;
    public override string Name => "Fichier de réglages";
    public override int Order => 0;

    // Réglages non enregistrables : la calibration qu'on s'apprête à faire serait perdue au redémarrage.
    public override bool Essential => true;

    protected override CheckResult Evaluate()
    {
        string root = GlobalSettings.Core.GlobalSettings.GetAppRootPath();
        string file = Path.Combine(root, "GlobalSettings.json");

        if (!File.Exists(file))
            return CheckResult.Error($"GlobalSettings.json introuvable :\n{file}",
                                     "Les valeurs par défaut du code sont utilisées. Vérifiez l'installation.");

        try { File.ReadAllText(file); }
        catch (System.Exception e)
        {
            return CheckResult.Error("GlobalSettings.json illisible.", e.Message);
        }

        string reason = PathChecks.Writable(root);
        if (reason != null)
            return CheckResult.Error("Les réglages ne peuvent pas être enregistrés.",
                                     $"Le dossier {root} refuse l'écriture : toute modification sera " +
                                     "perdue au redémarrage. " + reason);

        return CheckResult.Ok(file);
    }
}

[Preserve]
public class ExportPathCheck : SyncSystemCheck
{
    public override string Category => CheckCategory.Configuration;
    public override string Name => "Dossier d'export";
    public override int Order => 10;

    protected override CheckResult Evaluate()
    {
        if (PathChecks.Settings == null) return CheckResult.Error("Réglages non chargés.");

        string path = PathChecks.Settings.DefaultExportPath.Value;
        ICheckRepair repair = PathChecks.FolderRepair("d'export", "DefaultExportPath", path, new[] { "save", "Export" });

        if (string.IsNullOrEmpty(path))
            return CheckResult.Error("Aucun dossier d'export configuré.",
                                     "Renseignez DefaultExportPath dans GlobalSettings.json.").With(repair);

        string reason = PathChecks.Writable(path);
        return reason == null
            ? CheckResult.Ok(path)
            : CheckResult.Error($"Écriture refusée dans le dossier d'export :\n{path}",
                                "Donnez les droits d'écriture à ce dossier, ou changez-en. " + reason).With(repair);
    }
}

[Preserve]
public class PrintPathCheck : SyncSystemCheck
{
    public override string Category => CheckCategory.Configuration;
    public override string Name => "Dossier d'impression";
    public override int Order => 20;

    protected override CheckResult Evaluate()
    {
        if (PathChecks.Settings == null) return CheckResult.Error("Réglages non chargés.");

        string path = PathChecks.Settings.DefaultPrintPath.Value;
        // Le dossier surveillé vit sous l'installation HotFolder : on y cherche d'abord.
        string hotFolderRoot = HotFolderLocator.Last?.Root;
        ICheckRepair repair = PathChecks.FolderRepair("d'impression", "DefaultPrintPath", path, null, null,
                                                      string.IsNullOrEmpty(hotFolderRoot) ? null : new[] { hotFolderRoot });

        if (string.IsNullOrEmpty(path))
            return CheckResult.Warning("Aucun dossier d'impression configuré.",
                                       "Renseignez DefaultPrintPath ; sans lui, rien n'arrive à HotFolderPrint.").With(repair);

        string reason = PathChecks.Writable(path);
        return reason == null
            ? CheckResult.Ok(path)
            : CheckResult.Error($"Écriture refusée dans le dossier d'impression :\n{path}",
                                "HotFolderPrint ne recevra aucune photo. " + reason).With(repair);
    }
}

/// <summary>
/// Un disque plein ne casse rien tant qu'on peint : il casse l'export, c'est-à-dire au moment où le
/// client attend sa photo.
/// </summary>
[Preserve]
public class DiskSpaceCheck : SyncSystemCheck
{
    /// <summary>En dessous, une session d'animation ne tient pas.</summary>
    private const long MinimumFreeBytes = 500L * 1024L * 1024L;

    public override string Category => CheckCategory.Configuration;
    public override string Name => "Espace disque";
    public override int Order => 30;

    protected override CheckResult Evaluate()
    {
        if (PathChecks.Settings == null) return CheckResult.Ok();

        string path = PathChecks.Settings.DefaultExportPath.Value;
        if (string.IsNullOrEmpty(path)) return CheckResult.Ok();

        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return CheckResult.Ok();

            DriveInfo drive = new DriveInfo(root);
            long free = drive.AvailableFreeSpace;
            string human = $"{free / (1024f * 1024f * 1024f):0.0} Go libres sur {root}";

            return free >= MinimumFreeBytes
                ? CheckResult.Ok(human)
                : CheckResult.Warning($"Espace disque faible : {human}.",
                                      "Libérez de la place, les exports et impressions vont échouer.");
        }
        catch (System.Exception e)
        {
            // Chemin réseau, lecteur démonté : on ne sait pas, et on le dit.
            return CheckResult.Unknown("Espace disque non mesurable : " + e.Message);
        }
    }
}


// Les vérifications de contenu propres à un jeu (bibliothèques de stickers/fonds, dossiers photo,
// fresques du mode Guidé) restent dans le jeu : la découverte de SystemCheckRunner parcourt
// toutes les assemblies chargées, elles y sont donc trouvées sans rien déclarer ici.
