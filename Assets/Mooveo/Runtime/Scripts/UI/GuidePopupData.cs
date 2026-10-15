using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Contenu d'une popup explicative : un titre, des boutons, et une suite de PAGES (titre,
/// description, image). Rien ici n'est propre à un contexte — on en crée autant qu'on veut
/// (configuration d'imprimante, premier lancement, mode de jeu, message d'erreur récurrent…) et
/// <see cref="GuidePopup"/> les affiche toutes de la même façon.
///
/// Mise en place : Assets → Create → GraffWall → Popup explicative. L'asset se dépose dans
/// N'IMPORTE QUEL dossier <c>Resources</c> (le nom du fichier n'a pas d'importance, c'est
/// <see cref="Id"/> qui sert de clé) et le code le retrouve par <see cref="Find"/>. Aucun câblage
/// de scène, aucun prefab à toucher.
/// </summary>
[CreateAssetMenu(fileName = "GuidePopup", menuName = "GraffWall/Popup explicative")]
public class GuidePopupData : ScriptableObject
{
    [Serializable]
    public class Page
    {
        [Tooltip("Titre de la page. Facultatif.")]
        public string Title;

        [TextArea(2, 6), Tooltip("Texte de la page. Facultatif.")]
        public string Description;

        [Tooltip("Illustration de la page. Facultative : une page peut n'être que du texte.")]
        public Texture2D Image;

        public bool IsEmpty => Image == null &&
                               string.IsNullOrEmpty(Title) &&
                               string.IsNullOrEmpty(Description);
    }

    [Tooltip("Clé de recherche utilisée par le code (ex : hotfolder-banner). Doit être unique.")]
    public string Id;

    [Tooltip("Titre de la popup, affiché au-dessus des pages.")]
    public string Title = "Information";

    [Tooltip("Pages, dans l'ordre. Zéro page = popup de texte simple.")]
    public List<Page> Pages = new List<Page>();

    [Header("Boutons")]
    [Tooltip("Bouton de sortie (toujours affiché). Ferme la popup sans rien régler. Deux ou trois mots.")]
    public string DismissLabel = "Plus tard";

    public string DismissTooltip = "Fermer et poursuivre";

    [Tooltip("Bouton d'action, à droite et en vert. Laisser VIDE pour ne pas l'afficher.")]
    public string ConfirmLabel = "";

    public string ConfirmTooltip = "";

    // =========================================================================
    // Recherche
    // =========================================================================

    private static Dictionary<string, GuidePopupData> _byId;

    /// <summary>Popup portant cet Id, null si aucun asset ne le déclare (la popup s'affiche quand même).</summary>
    public static GuidePopupData Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_byId == null) BuildIndex();
        return _byId.TryGetValue(id, out GuidePopupData data) ? data : null;
    }

    private static void BuildIndex()
    {
        _byId = new Dictionary<string, GuidePopupData>();

        // LoadAll et non Load("<nom>") : l'asset peut s'appeler comme on veut, il suffit qu'il
        // vive sous un dossier Resources.
        foreach (GuidePopupData data in Resources.LoadAll<GuidePopupData>(""))
        {
            if (data == null || string.IsNullOrEmpty(data.Id)) continue;

            if (_byId.ContainsKey(data.Id))
            {
                Debug.LogWarning($"[GuidePopupData] Id « {data.Id} » déclaré deux fois, « {data.name} » ignoré.");
                continue;
            }

            _byId[data.Id] = data;
        }
    }

    public List<Page> ValidPages()
    {
        var pages = new List<Page>();
        foreach (Page p in Pages)
            if (p != null && !p.IsEmpty) pages.Add(p);
        return pages;
    }
}
