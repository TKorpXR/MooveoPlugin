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
    /// </summary>
    private static readonly string[] Required =
    {
        "Custom/StencilPreview",
        "GraffWall/ConeProjectionMask",
        "GraffWall/KidsRainbowIcon",
        "GraffWall/TeamFrescoCrop",
        "GraffWall/TeamFrescoVeil",
        "GraffWall/WallObjectOverlay",
        "Hidden/Internal-Colored",
        "Sprites/Default",
        "Unlit/Transparent"
    };

    public override string Category => CheckCategory.Build;
    public override string Name => "Shaders requis";
    public override int Order => 0;

    protected override CheckResult Evaluate()
    {
        var missing = new List<string>();
        foreach (string name in Required)
            if (Shader.Find(name) == null) missing.Add(name);

        if (missing.Count > 0)
            return CheckResult.Error("Shader(s) introuvable(s) : " + string.Join(", ", missing),
                                     "Ajoutez-les aux Always Included Shaders : sans eux, l'effet " +
                                     "correspondant disparaît sans erreur.");

        return Application.isEditor
            ? CheckResult.Unknown($"{Required.Length} shaders trouvés — non concluant en éditeur, " +
                                  "à revérifier dans un build.")
            : CheckResult.Ok($"{Required.Length} shaders");
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
    private static readonly string[] Required =
    {
        "Icons/check",
        "Icons/close",
        "Icons/GraffWall/home_200dp_FFFFFF_FILL1_wght400_GRAD0_opsz48",
        "Icons/GraffWall/settings_200dp_FFFFFF_FILL0_wght400_GRAD0_opsz48",
        "Icons/GraffWall/menu_200dp_FFFFFF_FILL0_wght400_GRAD0_opsz48",
        "Icons/GraffWall/print_200dp_FFFFFF_FILL0_wght400_GRAD0_opsz48",
        "Icons/GraffWall/palette_200dp_FFFFFF_FILL0_wght400_GRAD0_opsz48",
        "Icons/GraffWall/sticker_200dp_FFFFFF_FILL0_wght400_GRAD0_opsz48",
        "Icons/GraffWall/wallpaper_200dp_FFFFFF_FILL0_wght400_GRAD0_opsz48"
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
            ? CheckResult.Unknown($"{Required.Length} icônes trouvées — non concluant en éditeur.")
            : CheckResult.Ok($"{Required.Length} icônes");
    }
}
