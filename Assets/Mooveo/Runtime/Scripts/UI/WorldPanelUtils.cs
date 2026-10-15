using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Boîte à outils des panneaux world-space (GlobalWorldUI, GuidedDrawPanel, TeamFrescoPanel,
/// KidsModePanel) : dimensionnement des colliders VR, interactivité d'un sous-arbre, fermeture du
/// MainLayout. Ces gestes étaient recopiés à l'identique dans chaque panneau.
///
/// Volontairement une classe STATIQUE et non une classe de base : ces panneaux ont déjà leurs propres
/// Awake/OnEnable/OnDisable. Les faire hériter obligerait à les convertir en overrides, avec le risque
/// qu'un membre privé masque silencieusement celui de la base (piège déjà rencontré avec KidController)
/// et qu'un maillon du cycle de vie saute sans erreur de compilation.
/// </summary>
public static class WorldPanelUtils
{
    private static readonly List<VisualElement> _pickScratch = new List<VisualElement>();

    /// <summary>
    /// Élément sous ce point du panneau. ⚠ À utiliser À LA PLACE de <c>panel.Pick</c>.
    ///
    /// <c>Panel.Pick</c> a un CACHE : si le point tombe dans le même PIXEL ENTIER que la dernière
    /// position connue du pointeur SOURIS, il renvoie l'élément mémorisé pour la souris, sans pointer.
    /// Nos panneaux world-space se pointent en unités LOCALES : tout un menu tient dans quelques
    /// unités, un « pixel » y couvre donc un carré d'une unité de côté. Dans ce carré, près du centre
    /// de l'interface, le spray recevait l'élément de la souris (souvent aucun) : une zone morte
    /// fixe, dans tous les menus. Le cache est aussi faux sur un panneau d'écran pour tout pointeur
    /// qui n'est pas la souris. <c>PickAll</c> pointe réellement, sans cache.
    /// </summary>
    public static VisualElement PickAt(IPanel panel, Vector2 point)
    {
        if (panel == null) return null;
        VisualElement top = panel.PickAll(point, _pickScratch);
        _pickScratch.Clear();
        return top;
    }

    /// <summary>
    /// Redimensionne/positionne un BoxCollider pour qu'il épouse un VisualElement.
    /// Conversion des coordonnées UI Toolkit (origine haut-gauche) vers le repère local Unity
    /// (origine au centre). Sans effet tant que le layout n'est pas calculé (NaN / largeur nulle).
    /// </summary>
    public static void SizeColliderToElement(BoxCollider col, VisualElement el,
                                             VisualElement docRoot, Transform owner)
        => SizeColliderToElements(col, docRoot, owner, el);

    /// <summary>
    /// Même chose, mais sur l'UNION de plusieurs éléments.
    ///
    /// Indispensable dès qu'une commande DÉBORDE de la carte : une languette en position absolue
    /// sous le panneau, par exemple, tombe hors d'un collider dimensionné sur la seule carte. Elle
    /// devient alors totalement INCLIQUABLE avec le curseur du spray — le rayon ne rencontre aucun
    /// collider à cet endroit, donc UI Toolkit n'est jamais interrogé. À la souris (éditeur) le
    /// bouton répond normalement : le défaut ne se voit qu'une fois projeté.
    ///
    /// Les éléments masqués (display: none) sont ignorés : ils comptent une taille nulle mais une
    /// position, et gonfleraient l'union vers un coin.
    /// </summary>
    public static void SizeColliderToElements(BoxCollider col, VisualElement docRoot, Transform owner,
                                              params VisualElement[] els)
    {
        if (col == null || docRoot == null || owner == null || els == null) return;

        Rect rootRect = docRoot.layout;
        if (float.IsNaN(rootRect.width) || rootRect.width <= 0) return;

        Rect union = default;
        bool any = false;
        foreach (var el in els)
        {
            if (el == null || el.resolvedStyle.display == DisplayStyle.None) continue;
            Rect r = el.worldBound;
            if (float.IsNaN(r.width) || r.width <= 0) continue;

            union = any
                ? Rect.MinMaxRect(Mathf.Min(union.xMin, r.xMin), Mathf.Min(union.yMin, r.yMin),
                                  Mathf.Max(union.xMax, r.xMax), Mathf.Max(union.yMax, r.yMax))
                : r;
            any = true;
        }
        if (!any) return;

        float localX = union.center.x - (rootRect.width / 2f);
        float localY = -(union.center.y - (rootRect.height / 2f));

        col.center = new Vector3(localX, localY, 0f);
        col.size = new Vector3(union.width, union.height, 0.05f / owner.localScale.z);
    }

    /// <summary>
    /// Décale un collider VERS la caméra (le long de l'axe local qui pointe vers elle), pour qu'il
    /// gagne le raycast VR quand il chevauche un autre collider. Le sens est calculé depuis la
    /// position réelle de la caméra : robuste quelle que soit l'orientation du panneau.
    /// </summary>
    public static void BiasColliderTowardCamera(BoxCollider col, Transform owner, float worldBias)
    {
        if (col == null || owner == null) return;
        var cam = GlobalSettings.Core.GlobalSettings.MainCamera;
        if (cam == null) return;

        Vector3 camLocal = owner.InverseTransformPoint(cam.transform.position);
        float zSign = Mathf.Sign(camLocal.z);
        float localBias = worldBias / Mathf.Max(0.0001f, owner.localScale.z);

        var c = col.center;
        c.z = zSign * localBias;
        col.center = c;
    }

    /// <summary>
    /// Rend tout un sous-arbre pickable ou non. Indispensable : le pickingMode d'un parent ne
    /// CASCADE PAS — un panneau masqué mais toujours présent resterait cliquable et volerait le
    /// laser (ou le jet de peinture) au mur.
    /// </summary>
    public static void SetSubtreeInteractable(VisualElement root, bool on)
    {
        if (root == null) return;
        var mode = on ? PickingMode.Position : PickingMode.Ignore;
        root.pickingMode = mode;
        root.Query<VisualElement>().ForEach(el => el.pickingMode = mode);
    }

    /// <summary>
    /// L'élément (ou l'un de ses parents) est-il une cible d'interaction ? Sert à décider si le
    /// curseur doit basculer en mode UI plutôt que de peindre.
    ///
    /// Cette règle existait en DEUX copies (PainterController et CursorInteractor) qui avaient
    /// divergé : seule celle du peintre reconnaissait le toast IA. On garde donc l'UNION — le toast
    /// est cliquable, il doit être survolable par les deux chemins.
    /// </summary>
    public static bool IsInteractiveUIElement(VisualElement element)
    {
        for (var current = element; current != null; current = current.parent)
        {
            if (current is Button || current is RadioButton || current is Toggle ||
                current is Scroller || current is Slider || current is SliderInt) return true;

            // Fenêtre de fonctionnalité : tout son intérieur vaut cible (le spray valide au clic).
            if (current.ClassListContains("feature-window")) return true;

            switch (current.name)
            {
                // La barre du menu global compte ENTIÈREMENT, fond compris. Sans elle, survoler le
                // fond entre deux boutons ne valait pas « cible UI » alors que le collider, lui,
                // était bien touché : les deux verdicts se contredisaient d'une frame à l'autre et
                // l'interface clignotait. Le problème est apparu en donnant un fond à la barre —
                // avant, il n'y avait quasiment aucune zone survolable hors des boutons.
                case "menu-wrapper":
                case "sidebar-container":
                case "toolbar-unified":
                case "drag-handle":
                case "tooltip-container":
                case "ai-status-container":
                case "notification":
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Ferme le MainLayout (UserUI) de chaque joueur. Ouvrir un mode de jeu doit le refermer, sinon
    /// il masquerait l'écran du mode. ForceClose ignore le verrou éventuel.
    /// </summary>
    public static void CloseAllUserMenus()
    {
        // Panneaux de joueur déclarés par le jeu (GraffWall : UserUI). Recherche par interface : le SDK
        // ne connaît aucune classe de jeu (cf. MooveoGameHooks.IPlayerOwnedPanel).
        foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (behaviour is MooveoGameHooks.IPlayerOwnedPanel ui && ui.IsOpen) ui.ForceClose();
    }

    // =====================================================================
    // Bulles d'aide (tooltips)
    // =====================================================================

    /// <summary>
    /// Classe posée sur un bouton dont la bulle est affichée par SON panneau (HUD du Guidé, dock
    /// Enfant) : texte dynamique, placement propre. L'affichage générique doit l'ignorer, sinon deux
    /// bulles se disputent le même survol.
    /// </summary>
    public const string TooltipManagedClass = "tooltip-managed";

    public enum TooltipSide { Above, Below, Left }

    /// <summary>
    /// Élément qui porte la bulle sous le pointeur : le plus proche parent doté d'un <c>tooltip</c>,
    /// ou d'un marquage <see cref="TooltipManagedClass"/>. Null si rien à afficher.
    ///
    /// ⚠ L'attribut <c>tooltip="..."</c> d'UI Toolkit ne s'affiche PAS tout seul au runtime : c'est
    /// un mécanisme d'éditeur. Les panneaux le lisent et le montrent eux-mêmes, d'où cette recherche.
    /// La cible réelle d'un survol est souvent l'icône ou le libellé À L'INTÉRIEUR du bouton.
    /// </summary>
    /// <summary>Bouton qui porte la cible d'un clic (souvent son libellé ou son icône), ou null.</summary>
    public static Button ButtonOf(IEventHandler target)
    {
        for (var el = target as VisualElement; el != null; el = el.parent)
            if (el is Button button) return button;
        return null;
    }

    public static VisualElement FindTooltipTarget(IEventHandler target)
    {
        for (var el = target as VisualElement; el != null; el = el.parent)
        {
            if (el.ClassListContains(TooltipManagedClass)) return el;
            if (!string.IsNullOrEmpty(el.tooltip)) return el;
            // Un bouton sans libellé s'arrête là : afficher celui d'un conteneur parent tromperait.
            if (el is Button) return null;
        }
        return null;
    }

    /// <summary>
    /// Côté d'ouverture selon la position de l'élément dans son document : à GAUCHE contre le bord
    /// droit (panneaux latéraux), en DESSOUS dans la moitié haute, au-dessus sinon — la bulle reste
    /// ainsi dans l'écran.
    /// </summary>
    public static TooltipSide AutoTooltipSide(VisualElement anchor, VisualElement docRoot)
    {
        Rect doc = docRoot.worldBound;
        Vector2 c = anchor.worldBound.center;
        if (c.x > doc.xMin + doc.width * 0.75f) return TooltipSide.Left;
        return c.y < doc.yMin + doc.height * 0.5f ? TooltipSide.Below : TooltipSide.Above;
    }

    /// <summary>Affiche la bulle <paramref name="container"/> contre <paramref name="anchor"/>.</summary>
    /// <param name="keepInside">
    /// Recale la bulle dans son parent. ⚠ À réserver aux bulles posées à la RACINE du document
    /// (GlobalWorldUI) : celles du MainLayout vivent dans un conteneur plus petit que l'écran et
    /// sortent volontairement de sa boîte — les y ramener les posait PAR-DESSUS les boutons.
    /// </param>
    public static void ShowTooltip(VisualElement container, Label text, VisualElement anchor, TooltipSide side,
                                   bool keepInside = false)
    {
        if (container == null || text == null || anchor == null) return;
        var parent = container.parent;
        if (parent == null) return;

        const float gap = 10f;
        float w = anchor.layout.width, h = anchor.layout.height;
        text.text = anchor.tooltip;

        // La flèche suit le côté : --bottom la met en haut, --left sur le bord droit.
        container.EnableInClassList("tooltip-box--bottom", side == TooltipSide.Below);
        container.EnableInClassList("tooltip-box--left", side == TooltipSide.Left);

        Vector2 p;
        Translate shift;
        switch (side)
        {
            case TooltipSide.Below:
                p = anchor.ChangeCoordinatesTo(parent, new Vector2(w / 2f, h));
                p.y += gap;
                shift = new Translate(new Length(-50, LengthUnit.Percent), new Length(0, LengthUnit.Pixel));
                break;
            case TooltipSide.Left:
                p = anchor.ChangeCoordinatesTo(parent, new Vector2(0f, h / 2f));
                p.x -= gap;
                shift = new Translate(new Length(-100, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
                break;
            default:
                p = anchor.ChangeCoordinatesTo(parent, new Vector2(w / 2f, 0f));
                p.y -= gap;
                shift = new Translate(new Length(-50, LengthUnit.Percent), new Length(-100, LengthUnit.Percent));
                break;
        }

        // ⚠ .tooltip-box pose `bottom: 100px` en USS : on ancre par le HAUT, il faut le neutraliser.
        container.style.left = p.x;
        container.style.top = p.y;
        container.style.right = new StyleLength(StyleKeyword.Auto);
        container.style.bottom = new StyleLength(StyleKeyword.Auto);
        container.style.translate = new StyleTranslate(shift);
        // Décalage en FRACTION de la bulle, retenu pour le recalage : relu dans `resolvedStyle`, un
        // pourcentage n'est pas garanti résolu en pixels, et le calcul poussait la bulle hors de la
        // fenêtre au lieu de l'y ramener.
        container.userData = new Vector2(side == TooltipSide.Below || side == TooltipSide.Above ? -0.5f : -1f,
                                         side == TooltipSide.Below ? 0f : side == TooltipSide.Left ? -0.5f : -1f);
        container.BringToFront();
        container.RemoveFromClassList("tooltip-box--hidden");

        if (!keepInside) return;

        // ⚠ Centrée sous un bouton collé au bord (la barre de menu est ancrée en haut à gauche), la
        // bulle débordait du document de la moitié de sa largeur. Sa taille n'est connue qu'APRÈS la
        // mise en page du nouveau texte : on la recale à chaque changement de géométrie.
        if (!ClampedTooltips.Contains(container))
        {
            ClampedTooltips.Add(container);
            container.RegisterCallback<GeometryChangedEvent>(e => KeepInsideParent(container));
        }
        KeepInsideParent(container);
    }

    private static readonly HashSet<VisualElement> ClampedTooltips = new HashSet<VisualElement>();

    /// <summary>Ramène la bulle dans son parent (le document), à 8 px du bord.</summary>
    private static void KeepInsideParent(VisualElement box)
    {
        VisualElement parent = box.parent;
        if (parent == null) return;

        float w = box.resolvedStyle.width, h = box.resolvedStyle.height;
        float pw = parent.resolvedStyle.width, ph = parent.resolvedStyle.height;
        if (!(w > 0f) || !(h > 0f) || !(pw > 0f) || !(ph > 0f)) return;

        const float margin = 8f;
        Vector2 fraction = box.userData is Vector2 f ? f : Vector2.zero;
        float left = box.resolvedStyle.left, top = box.resolvedStyle.top;
        float x = left + fraction.x * w, y = top + fraction.y * h;

        float dx = x < margin ? margin - x : Mathf.Min(0f, pw - margin - (x + w));
        float dy = y < margin ? margin - y : Mathf.Min(0f, ph - margin - (y + h));

        // Seuil : le recalage déclenche lui-même un changement de géométrie, qui doit s'arrêter là.
        if (Mathf.Abs(dx) > 0.5f) box.style.left = left + dx;
        if (Mathf.Abs(dy) > 0.5f) box.style.top = top + dy;
    }

    public static void HideTooltip(VisualElement container)
    {
        container?.AddToClassList("tooltip-box--hidden");
    }

    /// <summary>
    /// Bulle d'aide des barres posées SUR un objet du mur (pochoir, sticker), bâties sur
    /// StencilButtons.uxml. Style : `.wall-tooltip` dans StencilButtons.uss — ce document ne charge
    /// pas VisionOS.uss, les bulles des menus n'y existent pas.
    ///
    /// ⚠ Le panneau est retourné (en X pour un pochoir, en Y pour un sticker retourné) : sans
    /// contre-retournement la bulle se lirait à l'envers. <paramref name="counterScale"/> le fournit,
    /// multiplié par la réduction du groupe de boutons pour garder les proportions.
    ///
    /// ⚠ La bulle vit dans `.stencil-ui-root`, de taille FIXE, et s'ouvre toujours vers le CENTRE de
    /// l'objet : elle reste dans la racine, donc la boîte englobante du panneau ne change pas et il ne
    /// bouge pas quand elle apparaît. Placée par ChangeCoordinatesTo et jamais par worldBound, qui est
    /// ici en UNITÉS alors que les styles sont en pixels.
    /// </summary>
    public static TooltipHover AttachWallTooltip(VisualElement docRoot, System.Func<Vector2> counterScale)
    {
        if (docRoot == null) return null;
        var host = docRoot.Q("stencil-ui-root") ?? docRoot;

        var bubble = new Label { name = "wall-tooltip", pickingMode = PickingMode.Ignore };
        bubble.AddToClassList("wall-tooltip");
        bubble.AddToClassList("wall-tooltip--hidden");
        host.Add(bubble);

        return new TooltipHover(docRoot, anchor =>
        {
            const float gap = 24f;
            float w = anchor.layout.width, h = anchor.layout.height;
            if (float.IsNaN(w) || float.IsNaN(host.layout.width)) return;

            Vector2 center = anchor.ChangeCoordinatesTo(host, new Vector2(w / 2f, h / 2f));
            bool towardRight = center.x < host.layout.width / 2f;
            Vector2 edge = anchor.ChangeCoordinatesTo(host, new Vector2(towardRight ? w : 0f, h / 2f));
            // Le conteneur réduit ou retourné peut inverser les bords : on garde le plus extérieur.
            float x = towardRight ? Mathf.Max(edge.x, center.x) + gap : Mathf.Min(edge.x, center.x) - gap;

            bubble.text = anchor.tooltip;
            bubble.style.left = x;
            bubble.style.top = center.y;
            bubble.style.translate = new StyleTranslate(new Translate(
                new Length(towardRight ? 0 : -100, LengthUnit.Percent), new Length(-50, LengthUnit.Percent)));
            bubble.style.scale = new StyleScale(new Scale(counterScale != null ? counterScale() : Vector2.one));
            bubble.BringToFront();
            bubble.RemoveFromClassList("wall-tooltip--hidden");
        },
        () => bubble.AddToClassList("wall-tooltip--hidden"));
    }

    /// <summary>
    /// Suit le survol de TOUT un document pour ses bulles d'aide, avec un seul abonnement à la racine.
    ///
    /// ⚠ Le curseur spray ne passe PAS par le système d'événements d'UI Toolkit : CursorInteractor
    /// fabrique lui-même des PointerEnterEvent / PointerLeaveEvent et les envoie au bouton visé. Il
    /// n'émet jamais de PointerOverEvent — une première version qui n'écoutait que celui-ci n'affichait
    /// plus aucune bulle au spray. Enter et Leave ne REMONTENT pas, mais ils DESCENDENT depuis la
    /// racine : on les capte donc en phase TrickleDown. PointerOver reste écouté pour la souris réelle.
    ///
    /// La disparition est différée d'une frame : en passant d'un bouton à l'autre, l'entrée dans le
    /// suivant l'annule, quel que soit l'ordre dans lequel les deux événements arrivent.
    /// </summary>
    public sealed class TooltipHover
    {
        private readonly VisualElement _root;
        private readonly System.Action<VisualElement> _show;
        private readonly System.Action _hide;
        private readonly System.Func<VisualElement, bool> _ignore;
        private bool _hidePending;

        /// <summary>Élément dont la bulle est affichée, ou null.</summary>
        public VisualElement Target { get; private set; }

        public TooltipHover(VisualElement root, System.Action<VisualElement> show, System.Action hide,
                            System.Func<VisualElement, bool> ignore = null)
        {
            _root = root;
            _show = show;
            _hide = hide;
            _ignore = ignore;
            root.RegisterCallback<PointerEnterEvent>(e => OnEnter(e.target), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerOverEvent>(e => OnEnter(e.target), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerLeaveEvent>(e => OnLeave(e.target), TrickleDown.TrickleDown);
        }

        private void OnEnter(IEventHandler handler)
        {
            var el = FindTooltipTarget(handler);
            if (el != null && _ignore != null && _ignore(el)) el = null;
            // Fond de menu, élément sans libellé : la sortie du bouton précédent s'occupe de masquer.
            if (el == null) return;

            _hidePending = false;
            if (el == Target) return;   // déjà affichée (icône → bouton, ou Enter + Over du même geste)
            Target = el;
            _show(el);
        }

        private void OnLeave(IEventHandler handler)
        {
            if (Target == null || FindTooltipTarget(handler) != Target) return;
            _hidePending = true;
            _root.schedule.Execute(() => { if (_hidePending) Clear(); });
        }

        /// <summary>Masque tout de suite (contenu remplacé, menu fermé).</summary>
        public void Clear()
        {
            _hidePending = false;
            Target = null;
            _hide();
        }
    }
}
