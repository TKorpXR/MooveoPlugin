using System;
using UnityEngine.UIElements;

/// <summary>
/// Branchement de <see cref="GuidePopup"/> sur le cas de la bannière d'impression : le diagnostic
/// vivant vient de <see cref="HotFolderLocator"/>, les pages illustrées d'un
/// <see cref="GuidePopupData"/> d'Id « hotfolder-banner » (facultatif — sans lui, seul le texte
/// s'affiche).
///
/// ⚠ Passer outre ne mémorise RIEN : la popup revient à chaque ouverture d'un menu qui a besoin de
/// l'impression. C'est au moment d'imprimer que l'avertissement compte.
/// </summary>
public static class HotFolderSetupPopup
{
    public const string GuideId = "hotfolder-banner";

    /// <summary>
    /// Affiche la popup si la configuration est en défaut, ou si l'appelant signale une erreur
    /// (échec d'écriture, qui peut survenir détection réussie). Renvoie true si elle s'est ouverte.
    /// </summary>
    public static bool ShowIfNeeded(VisualElement host, string forcedMessage = null,
                                    Action onClosed = null, Action beforeShow = null)
    {
        if (host == null) return false;

        HotFolderLocator.Result result = HotFolderLocator.Resolve(true);
        if (result.Status == HotFolderLocator.Status.Ok && string.IsNullOrEmpty(forcedMessage)) return false;

        // Dégagement des autres interfaces : seulement une fois qu'on SAIT qu'on va afficher.
        beforeShow?.Invoke();
        Show(host, forcedMessage ?? HotFolderBannerTemplate.PrepareAndDescribe(result), onClosed);
        return true;
    }

    public static void Show(VisualElement host, string message, Action onClosed = null)
    {
        GuidePopupData guide = GuidePopupData.Find(GuideId);
        GuidePopup.Show(host, guide, WithProcedure(message, guide), onClosed, Retry);
    }

    /// <summary>
    /// La marche à suivre vit dans les pages illustrées. Sans asset, elle doit quand même être dite
    /// quelque part : on la replie alors en une ligne dans le message.
    /// </summary>
    private static string WithProcedure(string message, GuidePopupData guide)
    {
        if (guide != null && guide.ValidPages().Count > 0) return message;

        return message + "\n\nDans Hot Folder Print : menu ☰ → Settings → onglet BORDERS → " +
               "Size « 4 x 6 » → ADD BORDER. Chargez le gabarit paysage et le gabarit portrait, " +
               "enregistrez, puis cochez la bordure créée pour l'activer.";
    }

    /// <summary>Bouton d'action : relance la détection. null = réglé, sinon le diagnostic à jour.</summary>
    private static string Retry()
    {
        HotFolderLocator.Result result = HotFolderLocator.Resolve(true);
        HotFolderLocator.ApplyTo(GlobalSettings.Core.GlobalSettings.Instance, force: true);

        if (result.Status == HotFolderLocator.Status.Ok)
        {
            MooveoGameHooks.ShowToast("Bannière d'impression",
                "Bordures détectées, la bannière s'appliquera.", UIToast.Kind.Success);
            return null;
        }

        return HotFolderBannerTemplate.PrepareAndDescribe(result);
    }
}
