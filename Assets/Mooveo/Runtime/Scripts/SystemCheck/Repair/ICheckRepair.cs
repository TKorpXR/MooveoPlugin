using System.Collections;

/// <summary>
/// Réparation proposée par le bouton « Résoudre » d'une vérification.
///
/// ⚠ Une réparation n'ouvre JAMAIS de fenêtre elle-même : tout dialogue passe par le
/// <see cref="RepairContext"/>. C'est ce qui permet de la jouer aussi bien depuis « État du
/// système » que depuis l'écran de calibration, deux documents distincts.
///
/// ⚠ Elle ne décide pas non plus du verdict : <see cref="RepairRunner"/> REVÉRIFIE après coup. Une
/// réparation qui se déclarerait réussie sans vérification est précisément l'échec silencieux que ce
/// système existe pour supprimer.
/// </summary>
public interface ICheckRepair
{
    /// <summary>Libellé du geste, pour le bouton et la progression (« Lancer SteamVR »).</summary>
    string Label { get; }

    /// <summary>Vrai si la réparation aboutit sans poser la moindre question.</summary>
    bool Unattended { get; }

    /// <summary>
    /// Coroutine. Un échec se signale par <see cref="RepairContext.Fail"/>, pas par une exception —
    /// une exception est rattrapée et transformée en échec, mais son message est technique.
    /// </summary>
    IEnumerator Attempt(RepairContext ctx);
}
