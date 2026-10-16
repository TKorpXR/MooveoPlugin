using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Vérifications de BUILD.
///
/// ⚠ Elles PASSENT TOUJOURS EN ÉDITEUR, et c'est exactement le mode de panne qu'elles traquent :
/// l'asset est présent dans le projet, absent du build. Un verdict vert en éditeur ne prouve donc
/// rien — elles se déclarent « non concluantes » (<see cref="CheckResult.Unknown"/>) pour ne pas
/// donner une fausse assurance à celui qui teste avant de builder.
/// </summary>
[Preserve]
public class RequiredShadersCheck : SyncSystemCheck
{
    /// <summary>
    /// Tous les shaders que le projet charge par NOM. Un shader chargé ainsi n'est référencé par
    /// aucun matériau : sans entrée dans les *Always Included Shaders*, il est stripé du build et
    /// l'effet disparaît sans la moindre erreur (CLAUDE.md §7).
    /// Liste relevée sur les `Shader.Find` du code — à tenir à jour si l'on en ajoute un.
    ///
    /// SDK : il n'en charge aucun. Le JEU déclare les siens au démarrage, avant la campagne de
    /// vérifications (GraffWall : ses shaders de fresque, de cône, du mode Enfant…).
    /// </summary>
    public static readonly List<string> Required = new List<string>();

    public override string Category => CheckCategory.Build;
    public override string Name => "Shaders requis";
    public override int Order => 0;

    protected override CheckResult Evaluate()
    {
        if (Required.Count == 0) return CheckResult.Ok("aucun shader chargé par nom");

        var missing = new List<string>();
        foreach (string name in Required)
            if (Shader.Find(name) == null) missing.Add(name);

        if (missing.Count > 0)
            return CheckResult.Error("Shader(s) introuvable(s) : " + string.Join(", ", missing),
                                     "Ajoutez-les aux Always Included Shaders : sans eux, l'effet " +
                                     "correspondant disparaît sans erreur.");

        return Application.isEditor
            ? CheckResult.Unknown($"{Required.Count} shaders trouvés — non concluant en éditeur, " +
                                  "à revérifier dans un build.")
            : CheckResult.Ok($"{Required.Count} shaders");
    }
}

/// <summary>
/// Icônes chargées par `resource(...)` en USS, donc depuis un dossier Resources. Le chemin est écrit
/// dans la feuille de style : une icône déplacée casse l'affichage sans rien signaler, le bouton
/// reste juste vide.
/// </summary>
[Preserve]
public class UiIconsCheck : SyncSystemCheck
{
    /// <summary>
    /// Icônes du SDK (chargées par le code ou par ses feuilles de style). Le JEU ajoute les siennes au
    /// démarrage (GraffWall : icônes de ses menus, <c>Icons/GraffWall/…</c>).
    /// </summary>
    public static readonly List<string> Required = new List<string>
    {
        "Icons/Mooveo/drag_pan_100dp_FFFFFF_FILL0_wght400_GRAD0_opsz48",
        "Icons/Mooveo/lock",
        "Icons/Mooveo/unlock",
        "Icons/arrow_back",
        "Icons/banner",
        "Icons/brush",
        "Icons/camera",
        "Icons/check",
        "Icons/close",
        "Icons/delete",
        "Icons/eye",
        "Icons/folder",
        "Icons/gallery",
        "Icons/palett",
        "Icons/print",
        "Icons/refresh",
        "Icons/save",
        "Icons/undo"
    };

    public override string Category => CheckCategory.Build;
    public override string Name => "Icônes de l'interface";
    public override int Order => 10;

    protected override CheckResult Evaluate()
    {
        var missing = new List<string>();
        foreach (string path in Required)
            if (Resources.Load<Texture2D>(path) == null) missing.Add(path);

        if (missing.Count > 0)
            return CheckResult.Warning($"{missing.Count} icône(s) introuvable(s) : " +
                                       string.Join(", ", missing),
                                       "Les boutons concernés s'afficheront vides.");

        return Application.isEditor
            ? CheckResult.Unknown($"{Required.Count} icônes trouvées — non concluant en éditeur.")
            : CheckResult.Ok($"{Required.Count} icônes");
    }
}
