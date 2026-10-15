using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Popup explicative GÉNÉRIQUE : titre, message vivant, pages feuilletables (titre / description /
/// image) et deux boutons. Tout le contenu vient d'un <see cref="GuidePopupData"/> — ce fichier ne
/// connaît aucun contexte métier et sert à n'importe quel sujet.
///
/// Construite EN C# et non en UXML : elle doit s'afficher indifféremment sur l'overlay de
/// calibration et sur la GlobalWorldUI, qui sont deux documents distincts. Les classes de style
/// employées (glass-panel, header-text, control-button-small, popup-btn-green) existent dans les
/// deux feuilles.
/// </summary>
public static class GuidePopup
{
    // Touches de manette câblées sur les deux sorties (cf. DefaultController). Le libellé affiché
    // sous chaque bouton vient d'ici : les deux doivent bouger ensemble.
    public const string DismissKeyName = "poignée";
    public const string ConfirmKeyName = "pouce";

    // Retour d'échec de l'action : couleur d'avertissement du projet, et durée de la pulsation.
    private static readonly Color FailureColor = new Color(0.95f, 0.61f, 0.07f);
    private const long FlashMs = 450;

    private static VisualElement _overlay;
    private static VisualElement _card;

    private static GuidePopupData _data;
    private static Action _onDismiss;
    private static Func<string> _onConfirm;   // renvoie null = régler & fermer, sinon nouveau message
    private static Action _onDismissPressed;  // sortie neutre DEMANDÉE (bouton ou poignée), pas toute fermeture

    // Page 1 du carrousel quand un message vivant est fourni : construite ici, jamais dans l'asset.
    private static GuidePopupData.Page _messagePage;
    private static List<GuidePopupData.Page> _pages;
    private static int _pageIndex;
    private static Label _pageTitle;
    private static VisualElement _pageImage;
    private static Label _pageText;
    private static VisualElement _dots;
    private static Button _confirmButton;
    private static VisualElement _failure;
    private static Label _failureText;

    public static bool IsOpen => _overlay != null && _overlay.parent != null;

    /// <summary>Carte de la popup, pour que l'hôte world-space y cale son collider VR.</summary>
    public static VisualElement Card => IsOpen ? _card : null;

    /// <summary>
    /// Affiche la popup. <paramref name="dynamicMessage"/> est le texte VIVANT (diagnostic du
    /// moment), distinct des pages, qui elles sont figées dans l'asset.
    /// <paramref name="onConfirm"/> alimente le bouton d'action : renvoyer null ferme la popup,
    /// renvoyer un texte la garde ouverte avec ce message — de quoi enchaîner les tentatives sans
    /// laisser croire que le bouton n'a rien fait.
    /// </summary>
    /// <param name="onDismissPressed">
    /// Appelée seulement quand l'opérateur CHOISIT la sortie neutre. ⚠ Distincte de
    /// <paramref name="onDismiss"/>, qui part à TOUTE fermeture — y compris quand la popup est
    /// remplacée par une autre ou fermée parce que le problème est réglé.
    /// </param>
    public static void Show(VisualElement host, GuidePopupData data, string dynamicMessage = null,
                            Action onDismiss = null, Func<string> onConfirm = null,
                            Action onDismissPressed = null)
    {
        Close();
        if (host == null) return;

        _data = data;
        _onDismiss = onDismiss;
        _onConfirm = onConfirm;
        _onDismissPressed = onDismissPressed;
        _pageIndex = 0;

        _overlay = new VisualElement { name = "guide-popup-overlay" };
        _overlay.style.position = Position.Absolute;
        _overlay.style.left = 0;
        _overlay.style.top = 0;
        _overlay.style.right = 0;
        _overlay.style.bottom = 0;
        _overlay.style.alignItems = Align.Center;
        _overlay.style.justifyContent = Justify.Center;
        _overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.75f);
        // ⚠ Bande libre en bas : c'est là que la calibration écrit « Appuyez sur la gâchette pour
        // commencer ». Recouverte, on ne comprend pas pourquoi la popup ne répond pas au clic.
        _overlay.style.paddingBottom = Length.Percent(6);
        // Modale : le voile absorbe les clics mais ne FERME pas — on sort par un bouton, pour que
        // la décision de passer outre soit explicite.
        _overlay.pickingMode = PickingMode.Position;

        // feature-window : tout l'intérieur vaut cible UI pour le curseur spray (WorldPanelUtils).
        _card = new VisualElement { name = "guide-popup-card" };
        _card.AddToClassList("glass-panel");
        _card.AddToClassList("feature-window");
        // ⚠ Taille RELATIVE au document hôte, jamais en pixels seuls : la popup s'affiche aussi bien
        // sur la GlobalWorldUI (référence 1920) que sur l'overlay de calibration, dont la résolution
        // de référence est bien plus petite — 900 px y couvraient presque tout l'écran.
        // Le plafond en pixels évite l'excès inverse sur un très grand panneau.
        _card.style.width = Length.Percent(46);
        _card.style.maxWidth = 700;
        _card.style.maxHeight = Length.Percent(96);
        _card.style.alignItems = Align.Center;
        _card.style.paddingLeft = 30;
        _card.style.paddingRight = 30;
        _card.style.paddingTop = 26;
        _card.style.paddingBottom = 22;
        // Rien ne doit déborder par-dessus l'interface qui se trouve derrière.
        _card.style.overflow = Overflow.Hidden;
        // .glass-panel est translucide : correct pour un panneau posé sur le mur, illisible pour une
        // modale, où l'interface du dessous transparaissait à travers le texte.
        _card.style.backgroundColor = new Color(0.11f, 0.11f, 0.12f, 0.97f);
        _overlay.Add(_card);

        Label title = new Label(data != null && !string.IsNullOrEmpty(data.Title) ? data.Title : "Information");
        title.AddToClassList("header-text");
        // .header-text pose un letter-spacing de 2 : sur un titre de popup il force un retour à la
        // ligne inutile et grignote la hauteur du contenu.
        title.style.fontSize = 26;
        title.style.letterSpacing = 0;
        title.style.marginBottom = 0;
        title.style.whiteSpace = WhiteSpace.Normal;
        title.style.unityTextAlign = TextAnchor.MiddleCenter;
        title.style.alignSelf = Align.Stretch;
        title.style.flexShrink = 0;
        _card.Add(title);

        // Filet de séparation : c'est LUI qui distingue le titre de la popup du titre de la page.
        // Deux libellés en gras à quelques pixels l'un de l'autre se lisent comme un seul bloc.
        VisualElement rule = new VisualElement();
        rule.style.alignSelf = Align.Stretch;
        rule.style.height = 1;
        rule.style.flexShrink = 0;
        rule.style.marginTop = 14;
        rule.style.marginBottom = 16;
        rule.style.backgroundColor = new Color(1f, 1f, 1f, 0.14f);
        _card.Add(rule);

        BuildPages(_card, dynamicMessage);
        BuildButtons(_card);

        host.Add(_overlay);
        _overlay.BringToFront();

        // Écoute manette + clavier, indépendante du routage des contrôleurs.
        GuidePopupInput.Ensure();
    }

    public static void Close()
    {
        if (_overlay != null && _overlay.parent != null) _overlay.RemoveFromHierarchy();

        _overlay = null;
        _card = null;
        _data = null;
        _onConfirm = null;
        _onDismissPressed = null;
        _pages = null;
        _messagePage = null;
        _pageTitle = null;
        _pageImage = null;
        _pageText = null;
        _dots = null;
        _confirmButton = null;
        _failure = null;
        _failureText = null;

        Action dismissed = _onDismiss;
        _onDismiss = null;
        dismissed?.Invoke();
    }

    /// <summary>
    /// Réécrit le message vivant. Il occupe la PREMIÈRE page, et on y revient : c'est le diagnostic
    /// du moment, il doit être lu avant la marche à suivre.
    /// </summary>
    public static void SetDynamicMessage(string message)
    {
        if (_messagePage == null) return;

        _messagePage.Description = message ?? "";
        _pageIndex = 0;
        ApplyPage();
    }

    // =========================================================================
    // Pages
    // =========================================================================

    private static void BuildPages(VisualElement card, string dynamicMessage)
    {
        _pages = new List<GuidePopupData.Page>();
        _messagePage = null;

        // Le message vivant est une PAGE À PART, la première : mêlé à chaque page d'instruction il
        // mangeait toute la hauteur et faisait lire deux choses à la fois.
        if (!string.IsNullOrEmpty(dynamicMessage))
        {
            _messagePage = new GuidePopupData.Page { Description = dynamicMessage };
            _pages.Add(_messagePage);
        }

        if (_data != null) _pages.AddRange(_data.ValidPages());
        if (_pages.Count == 0) return;

        // Corps centré : quand la page n'a pas d'image, son texte se pose au milieu de la carte au
        // lieu de rester collé en haut d'un grand vide.
        VisualElement body = new VisualElement();
        body.style.alignSelf = Align.Stretch;
        body.style.flexGrow = 1;
        body.style.flexShrink = 1;
        body.style.justifyContent = Justify.Center;
        // ⚠ LE point qui décide de tout. Un élément flex a pour taille minimale AUTOMATIQUE celle de
        // son contenu : sans ce 0, un corps trop grand ne se réduit pas, il DÉBORDE de la carte et
        // se dessine par-dessus le titre, les flèches et les boutons. On voit alors des textes
        // superposés et l'on croit à un problème de police ou de marges.
        body.style.minHeight = 0;
        // Filet de sécurité : si malgré tout ça ne rentre pas, on rogne ICI plutôt que d'écrire
        // par-dessus les voisins.
        body.style.overflow = Overflow.Hidden;
        card.Add(body);

        _pageTitle = Paragraph(20, Color.white);
        _pageTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        _pageTitle.style.marginBottom = 12;
        body.Add(_pageTitle);

        // L'illustration prend tout ce que le texte laisse, au lieu d'imposer une hauteur en pixels
        // qui déborde sur un petit panneau et paraît minuscule sur un grand.
        _pageImage = new VisualElement { name = "guide-popup-image" };
        _pageImage.style.alignSelf = Align.Stretch;
        _pageImage.style.flexGrow = 1;
        _pageImage.style.flexShrink = 1;
        // Même règle : c'est l'illustration qui cède la place, jamais le texte. Un plancher en
        // pixels la ferait déborder du corps sur une petite page.
        _pageImage.style.minHeight = 0;
        _pageImage.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
        _pageImage.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        _pageImage.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
        SetRadius(_pageImage, 16);
        body.Add(_pageImage);

        _pageText = Paragraph(16, new Color(1f, 1f, 1f, 0.72f));
        _pageText.style.marginTop = 12;
        body.Add(_pageText);

        // Une seule page : ni flèches, ni points, ni compteur.
        if (_pages.Count > 1) BuildNavigation(card);

        // flexGrow ne distribue que l'espace LIBRE : sans hauteur définie sur la carte, il n'y en a
        // aucun et l'illustration resterait à son plancher. On ne la fixe donc que s'il y a une
        // image à montrer — une popup de texte seul garde sa hauteur naturelle.
        // Pourcentage de la boîte de contenu de l'hôte, déjà amputée de sa bande basse réservée.
        if (_pages.Exists(p => p.Image != null)) card.style.height = Length.Percent(97);

        ApplyPage();
    }

    private static void BuildNavigation(VisualElement card)
    {
        VisualElement nav = new VisualElement();
        nav.style.flexDirection = FlexDirection.Row;
        nav.style.alignItems = Align.Center;
        nav.style.justifyContent = Justify.Center;
        nav.style.marginTop = 12;
        nav.style.flexShrink = 0;
        card.Add(nav);

        nav.Add(NavButton("◀", "Page précédente", -1));

        _dots = new VisualElement();
        _dots.style.flexDirection = FlexDirection.Row;
        _dots.style.alignItems = Align.Center;
        _dots.style.marginLeft = 22;
        _dots.style.marginRight = 22;
        nav.Add(_dots);

        for (int i = 0; i < _pages.Count; i++)
        {
            VisualElement dot = new VisualElement();
            dot.style.width = 10;
            dot.style.height = 10;
            dot.style.marginLeft = 4;
            dot.style.marginRight = 4;
            SetRadius(dot, 5);
            _dots.Add(dot);
        }

        nav.Add(NavButton("▶", "Page suivante", 1));

        // Pas de compteur « 2 / 5 » : les pastilles disent déjà où l'on en est, et un troisième
        // repère sous les flèches alourdissait le bas de la carte.
    }

    private static void Step(int delta)
    {
        if (_pages == null || _pages.Count == 0) return;
        _pageIndex = (_pageIndex + delta + _pages.Count) % _pages.Count;
        ApplyPage();
    }

    private static void ApplyPage()
    {
        if (_pages == null || _pages.Count == 0) return;

        GuidePopupData.Page page = _pages[_pageIndex];

        SetOptionalText(_pageTitle, page.Title);
        SetOptionalText(_pageText, page.Description);

        if (_pageImage != null)
        {
            _pageImage.style.display = page.Image != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (page.Image != null) _pageImage.style.backgroundImage = new StyleBackground(page.Image);
        }

        if (_dots == null) return;
        for (int i = 0; i < _dots.childCount; i++)
            _dots[i].style.backgroundColor = i == _pageIndex ? Color.white : new Color(1f, 1f, 1f, 0.3f);
    }

    // =========================================================================
    // Boutons
    // =========================================================================

    private static void BuildButtons(VisualElement card)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.Center;
        row.style.marginTop = 16;
        row.style.alignSelf = Align.Stretch;
        row.style.flexShrink = 0;   // les sorties ne doivent JAMAIS être écrasées par le flex
        card.Add(row);

        // Grammaire des popups du projet : refus neutre à gauche, action souhaitée en vert à droite.
        string dismissLabel = _data != null && !string.IsNullOrEmpty(_data.DismissLabel)
            ? _data.DismissLabel : "Plus tard";

        Button dismiss = ActionButton(dismissLabel, _data?.DismissTooltip, DismissKeyName);
        dismiss.style.marginRight = 14;
        dismiss.RegisterCallback<ClickEvent>(e => Dismiss());
        row.Add(dismiss);

        if (_onConfirm == null || _data == null || string.IsNullOrEmpty(_data.ConfirmLabel)) return;

        _confirmButton = ActionButton(_data.ConfirmLabel, _data.ConfirmTooltip, ConfirmKeyName);
        _confirmButton.AddToClassList("popup-btn-green");
        _confirmButton.style.color = Color.white;
        _confirmButton.RegisterCallback<ClickEvent>(e => Confirm());
        row.Add(_confirmButton);

        BuildFailureLine(card);
    }

    /// <summary>
    /// Ligne d'échec de l'action, masquée tant qu'on n'a pas essayé. Sans elle, un appui qui ne
    /// règle rien ne se voit pas : le diagnostic réaffiché est le MÊME, la popup ne bouge pas, et
    /// l'opérateur conclut que le bouton est mort.
    /// </summary>
    private static void BuildFailureLine(VisualElement card)
    {
        _failure = new VisualElement { name = "guide-popup-failure" };
        _failure.style.flexDirection = FlexDirection.Row;
        _failure.style.alignItems = Align.Center;
        _failure.style.justifyContent = Justify.Center;
        _failure.style.alignSelf = Align.Stretch;
        _failure.style.marginTop = 12;
        _failure.style.flexShrink = 0;
        _failure.style.display = DisplayStyle.None;
        card.Add(_failure);

        VisualElement icon = new VisualElement();
        icon.style.width = 22;
        icon.style.height = 22;
        icon.style.marginRight = 10;
        icon.style.flexShrink = 0;
        icon.style.backgroundImage = new StyleBackground(UIIcons.WarningTriangle);
        icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        icon.style.unityBackgroundImageTintColor = FailureColor;
        _failure.Add(icon);

        _failureText = new Label();
        _failureText.style.whiteSpace = WhiteSpace.Normal;
        _failureText.style.fontSize = 16;
        _failureText.style.color = FailureColor;
        _failure.Add(_failureText);
    }

    /// <summary>
    /// Signale que l'action vient d'échouer : ligne d'avertissement + pulsation du bouton. La
    /// pulsation est REJOUÉE à chaque appui, y compris quand la ligne est déjà affichée — c'est
    /// elle qui prouve que l'appui a bien été pris en compte.
    /// </summary>
    private static void ShowFailure(string reason)
    {
        if (_failure == null) return;

        _failureText.text = string.IsNullOrEmpty(reason)
            ? "Toujours rien de détecté. Reprenez les étapes ci-dessus."
            : reason;
        _failure.style.display = DisplayStyle.Flex;

        if (_confirmButton == null) return;
        _confirmButton.style.backgroundColor = FailureColor;
        _confirmButton.schedule
            .Execute(() => { if (_confirmButton != null) _confirmButton.style.backgroundColor = StyleKeyword.Null; })
            .StartingIn(FlashMs);
    }

    /// <summary>Sortie neutre — aussi câblée sur la POIGNÉE de la manette.</summary>
    public static void Dismiss()
    {
        if (!IsOpen) return;
        Action pressed = _onDismissPressed;
        Close();
        pressed?.Invoke();
    }

    /// <summary>Page précédente / suivante, pour un pilotage sans viser les flèches.</summary>
    public static void PreviousPage() => Step(-1);
    public static void NextPage() => Step(1);

    /// <summary>Action — aussi câblée sur le bouton POUCE. Sans action définie, ne fait rien.</summary>
    public static void Confirm()
    {
        Func<string> confirm = _onConfirm;
        // ⚠ Deux chemins d'entrée peuvent arriver sur le même appui (interception du contrôleur et
        // écoute directe) : sans cette garde, l'action se jouerait deux fois.
        if (confirm == null || !IsOpen) return;

        string stillWrong = confirm();
        if (stillWrong == null) { Close(); return; }

        // Toujours en défaut : on remet le diagnostic À JOUR et on le SIGNALE. Le message est
        // souvent identique au précédent — sans retour visuel, l'appui paraît sans effet.
        SetDynamicMessage(stillWrong);
        ShowFailure(null);
    }

    /// <summary>
    /// Bouton d'action, avec sous-titre indiquant la touche de manette correspondante : au spray,
    /// viser un bouton est lent, et la plupart des opérateurs n'ont pas de souris.
    /// </summary>
    private static Button ActionButton(string label, string tooltip, string key)
    {
        Button button = new Button { tooltip = string.IsNullOrEmpty(tooltip) ? label : tooltip };
        button.AddToClassList("control-button-small");
        button.style.flexDirection = FlexDirection.Column;
        button.style.alignItems = Align.Center;
        button.style.justifyContent = Justify.Center;

        Label main = new Label(label);
        main.style.fontSize = 21;
        main.style.unityFontStyleAndWeight = FontStyle.Bold;
        main.style.color = Color.white;
        button.Add(main);

        Label hint = new Label($"({key})");
        hint.style.fontSize = 14;
        hint.style.marginTop = 2;
        hint.style.color = new Color(1f, 1f, 1f, 0.6f);
        button.Add(hint);

        // ⚠ .control-button-small impose width: 40px — sans cette libération, le libellé DÉBORDE
        // du bouton : il s'affiche, mais en dehors de son fond.
        button.style.width = StyleKeyword.Auto;
        button.style.minWidth = 190;
        button.style.height = 62;
        button.style.fontSize = 21;
        button.style.paddingLeft = 32;
        button.style.paddingRight = 32;
        button.style.marginLeft = 0;
        button.style.marginRight = 0;
        button.style.whiteSpace = WhiteSpace.NoWrap;
        SetRadius(button, 14);
        // ⚠ ClickEvent et jamais Button.clicked : le curseur spray n'émet pas la paire
        // PointerDown/PointerUp qu'attend le manipulateur Clickable.
        return button;
    }

    private static Button NavButton(string glyph, string tooltip, int delta)
    {
        Button button = new Button { text = glyph, tooltip = tooltip };
        button.AddToClassList("control-button-small");
        button.style.width = 46;
        button.style.height = 46;
        button.style.fontSize = 20;
        button.style.marginLeft = 0;
        button.style.marginRight = 0;
        SetRadius(button, 23);
        button.RegisterCallback<ClickEvent>(e => Step(delta));
        return button;
    }

    // =========================================================================
    // Petits utilitaires de style
    // =========================================================================

    private static Label Paragraph(int fontSize, Color color)
    {
        Label label = new Label();
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.color = color;
        label.style.fontSize = fontSize;
        label.style.marginLeft = 20;
        label.style.marginRight = 20;
        // La carte est en align-items: center : sans étirement, le label se dimensionne sur son
        // contenu et le retour à la ligne ne se déclenche pas.
        label.style.alignSelf = Align.Stretch;
        // Le texte ne se comprime pas : c'est l'illustration qui absorbe la place manquante.
        label.style.flexShrink = 0;
        return label;
    }

    private static void SetOptionalText(Label label, string text)
    {
        if (label == null) return;
        label.text = text ?? "";
        label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private static void SetRadius(VisualElement element, float radius)
    {
        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
        element.style.borderBottomRightRadius = radius;
    }
}
