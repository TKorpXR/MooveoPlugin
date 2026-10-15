using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Registre des panneaux UI Toolkit en SCREEN SPACE que le curseur spray doit pouvoir atteindre.
///
/// ⚠ Pourquoi ce registre existe. Le spray ne clique l'interface que par RAYCAST PHYSIQUE : chaque
/// panneau world-space porte un collider, le rayon le touche, et UI Toolkit est ensuite interrogé au
/// point d'impact. Un panneau en overlay n'a pas de collider — il n'est pas dans le monde. Sans ce
/// détour, ses boutons seraient cliquables à la souris et INVISIBLES pour la bombe, alors qu'une
/// erreur doit pouvoir se fermer au spray (issue #28).
///
/// On ne balaie pas tous les UIDocument de la scène : seuls ceux qui s'inscrivent ici acceptent ce
/// pointage. Un panneau plein écran non déclaré (l'overlay de calibration, le débogueur d'entrées)
/// avalerait sinon tous les clics destinés au mur.
/// </summary>
public static class ScreenSpaceUI
{
    private static readonly List<UIDocument> _documents = new List<UIDocument>();

    public static void Register(UIDocument document)
    {
        if (document != null && !_documents.Contains(document)) _documents.Add(document);
    }

    public static void Unregister(UIDocument document) => _documents.Remove(document);

    /// <summary>
    /// Cherche une cible d'interaction sous ce point de l'ÉCRAN, du panneau le plus haut au plus bas.
    /// ⚠ Seule une VRAIE cible compte (<see cref="WorldPanelUtils.IsInteractiveUIElement"/>) : les
    /// conteneurs plein écran sont pickables par défaut et retiendraient tout.
    /// </summary>
    public static bool TryPick(Vector2 screenPosition, out UIDocument document,
                               out VisualElement element, out Vector2 panelPosition)
    {
        document = null;
        element = null;
        panelPosition = default;

        // Le plus haut d'abord : même règle d'affichage que le rendu (Sort Order du PanelSettings).
        _documents.Sort((a, b) => Order(b).CompareTo(Order(a)));

        for (int i = 0; i < _documents.Count; i++)
        {
            UIDocument doc = _documents[i];
            if (doc == null || !doc.isActiveAndEnabled) continue;

            IPanel panel = doc.rootVisualElement?.panel;
            if (panel == null) continue;

            Vector2 local = ToPanel(panel, screenPosition);
            VisualElement picked = WorldPanelUtils.PickAt(panel, local);
            if (picked == null || !WorldPanelUtils.IsInteractiveUIElement(picked)) continue;

            document = doc;
            element = picked;
            panelPosition = local;
            return true;
        }

        return false;
    }

    private static float Order(UIDocument document)
        => document != null && document.panelSettings != null ? document.panelSettings.sortingOrder : 0f;

    /// <summary>
    /// Point de l'ÉCRAN (origine en bas à gauche, comme tout le reste du moteur) dans le repère du
    /// panneau. ⚠ Une seule source pour cette conversion : le pointage du spray, celui de la souris
    /// et le doublon de curseur dessiné dans le panneau doivent tomber au même pixel.
    ///
    /// ⚠ Le repère d'un panneau a son origine EN HAUT à gauche, celui de l'écran EN BAS à gauche.
    /// Selon les versions d'UI Toolkit, <c>ScreenToPanel</c> fait ce retournement ou attend qu'on
    /// l'ait fait. Se tromper ne produit AUCUNE erreur : le point tombe à la hauteur miroir, donc
    /// dans le vide — le spray traverse une notification ancrée en bas sans rien y trouver, et le
    /// curseur redessiné atterrit en haut de l'écran, où rien ne le montre. D'où la déduction
    /// ci-dessous : on compare deux points dont on connaît l'ordre au lieu de parier.
    /// </summary>
    public static Vector2 ToPanel(IPanel panel, Vector2 screenPosition)
    {
        // Un panneau d'ÉCRAN couvre exactement la fenêtre : sa racine s'étend sur toute la surface,
        // quel que soit le mode de mise à l'échelle. La conversion est donc une simple règle de
        // trois, exacte et sans convention implicite — c'est le chemin normal.
        Vector2 size = panel != null && panel.visualTree != null ? panel.visualTree.layout.size : Vector2.zero;
        if (size.x > 1f && size.y > 1f && Screen.width > 0 && Screen.height > 0)
            return new Vector2(screenPosition.x / Screen.width * size.x,
                               (Screen.height - screenPosition.y) / Screen.height * size.y);

        // Avant la première mise en page, la taille n'est pas connue : on s'en remet au moteur.
        return RuntimePanelUtils.ScreenToPanel(panel, screenPosition);
    }

    /// <summary>
    /// La caméra dessinée À L'ÉCRAN : sans cible d'œil, sans texture de rendu, sur l'écran 0.
    /// ⚠ Surtout pas <c>Camera.main</c>, qui est la caméra du CASQUE dès que l'affichage XR tourne
    /// (CLAUDE.md §8) : un panneau d'écran se pointe depuis la caméra réellement projetée.
    /// </summary>
    public static Camera DisplayCamera()
    {
        // Celle du curseur spray d'abord, si c'est bien elle qui s'affiche : c'est pour elle que les
        // colliders sont décalés, l'ordre des panneaux est pensé depuis là.
        Camera main = GlobalSettings.Core.GlobalSettings.MainCamera;
        if (main != null && main.isActiveAndEnabled && main.targetTexture == null &&
            main.stereoTargetEye == StereoTargetEyeMask.None)
            return main;

        Camera best = null;
        foreach (Camera cam in Camera.allCameras)
        {
            if (!cam.isActiveAndEnabled || cam.targetTexture != null || cam.targetDisplay != 0) continue;
            if (cam.stereoTargetEye != StereoTargetEyeMask.None) continue;
            if (best == null || cam.depth > best.depth) best = cam;
        }
        return best != null ? best : (GlobalSettings.Core.GlobalSettings.MainCamera != null ? GlobalSettings.Core.GlobalSettings.MainCamera : Camera.main);
    }
}
