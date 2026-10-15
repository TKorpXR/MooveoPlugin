using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Prépare les gabarits de bordure à importer dans HotFolderPrint quand il n'en existe aucun.
/// GraffWall ne peut PAS créer la bordure lui-même : HotFolder ne lit pas le dossier Borders\, il
/// lit sa table Borders (Name, Size, Orientation, FilePath, Selected) et suit le FilePath qui y est
/// inscrit. Un fichier posé à la main n'y figure pas — il resterait invisible pour HotFolder, et
/// serait exactement le fichier fantôme que l'on veut éviter. Seul l'import par HotFolder crée la
/// ligne. On fournit donc le fichier à importer, aux bonnes dimensions, et la marche à suivre.
/// </summary>
public static class HotFolderBannerTemplate
{
    public static string LandscapeFileName => MooveoGameHooks.AppFileName + "_bordure_paysage.png";
    public static string PortraitFileName => MooveoGameHooks.AppFileName + "_bordure_portrait.png";

    private static readonly Vector2Int LandscapeSize = new Vector2Int(1800, 1200); // 4x6 à 300 dpi
    private static readonly Vector2Int PortraitSize = new Vector2Int(1200, 1800);

    /// <summary>
    /// Import/HotFolder, à côté de stickers/, stencils/ et banners/ : c'est le dossier que
    /// l'opérateur connaît déjà, et le fichier est destiné à être importé AILLEURS (dans
    /// HotFolderPrint), pas produit par le jeu — il n'a donc rien à faire dans Export.
    /// </summary>
    public static string Folder => Path.Combine(GlobalSettings.Core.GlobalSettings.GetAppRootPath(), "Import", "HotFolder");

    /// <summary>
    /// Écrit les DEUX gabarits s'ils manquent, sans attendre qu'un défaut soit détecté. Appelé au
    /// lancement : en build, sur un poste neuf, l'opérateur doit trouver les fichiers à importer
    /// dans Import/HotFolder AVANT même d'ouvrir le menu d'impression. Les gabarits sont produits
    /// au runtime, ils ne dépendent d'aucun asset embarqué.
    /// </summary>
    public static void EnsureAll()
    {
        string folder = Folder;
        EnsureTemplate(folder, LandscapeFileName, LandscapeSize);
        EnsureTemplate(folder, PortraitFileName, PortraitSize);
    }

    /// <summary>
    /// Génère les gabarits des orientations manquantes et renvoie le message à afficher à
    /// l'opérateur : ce qui manque, puis quoi faire. Chaîne vide si tout va bien.
    /// </summary>
    public static string PrepareAndDescribe(HotFolderLocator.Result result)
    {
        if (result == null || result.Status == HotFolderLocator.Status.Ok) return "";

        // Un gabarit n'avance à rien sans HotFolderPrint installé, ni quand la bordure existe
        // déjà mais n'est pas modifiable — en réimporter une ne réglerait rien.
        if (result.Status == HotFolderLocator.Status.NotInstalled ||
            result.Status == HotFolderLocator.Status.BordersLocked ||
            result.Status == HotFolderLocator.Status.ScanFailed) return result.Message;

        var missing = new List<(string name, Vector2Int size)>();
        if (!result.HasLandscape) missing.Add((LandscapeFileName, LandscapeSize));
        if (!result.HasPortrait) missing.Add((PortraitFileName, PortraitSize));
        if (missing.Count == 0) return result.Message;

        string folder = Folder;
        var written = new List<string>();

        foreach ((string name, Vector2Int size) in missing)
            if (EnsureTemplate(folder, name, size)) written.Add(name);

        // Le gabarit n'a pas pu être écrit : on le DIT, sinon l'opérateur cherche un fichier absent.
        if (written.Count == 0)
            return result.Message +
                   $"\n\n⚠ Le gabarit à importer n'a pas pu être écrit dans :\n{folder}\n" +
                   "Vérifiez les droits sur ce dossier.";

        string files = string.Join("\n", written.ConvertAll(n => "  • " + n));
        return result.Message + $"\n\nGabarit(s) à importer, déposé(s) ici :\n{folder}\n{files}";
    }

    /// <summary>
    /// Gabarit entièrement TRANSPARENT : tant qu'aucune bannière n'est choisie, les photos sortent
    /// telles quelles. Rien n'est régénéré si le fichier est déjà là.
    /// </summary>
    private static bool EnsureTemplate(string folder, string fileName, Vector2Int size)
    {
        string path = Path.Combine(folder, fileName);

        try
        {
            if (File.Exists(path)) return true;
            Directory.CreateDirectory(folder);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[HotFolderBannerTemplate] Dossier des gabarits inaccessible ({folder}) : {e.Message}");
            return false;
        }

        Texture2D texture = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
        try
        {
            texture.SetPixels32(new Color32[size.x * size.y]); // tout à zéro = transparent
            texture.Apply(false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Debug.Log($"[HotFolderBannerTemplate] Gabarit écrit : {path}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[HotFolderBannerTemplate] Écriture du gabarit impossible ({path}) : {e.Message}");
            return false;
        }
        finally
        {
            if (Application.isPlaying) Object.Destroy(texture);
            else Object.DestroyImmediate(texture);
        }
    }
}
