using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Affiche les notifications de <see cref="NotificationCenter"/> dans un UIDocument en
/// <b>SCREEN SPACE — OVERLAY</b>, ancré en bas à droite de la FENÊTRE.
///
/// ⚠ Pourquoi plus de world-space (l'ancienne version). Un panneau world-space est un quad posé
/// devant le mur : sa taille est en unités de monde, son origine est son PIVOT, et « en bas à
/// droite » ne veut dire quelque chose que si le document couvre exactement l'écran. Il fallait donc
/// accorder trois réglages (taille fixe du document, pivot, échelle recalculée sur la largeur du
/// mur) sur un objet créé au runtime — un seul de travers et la pile se recentrait sur le mur ou
/// suivait le joueur. S'y ajoutaient un collider VR à dimensionner à la main, l'ordre d'affichage
/// entre documents world-space qui dépend de la DISTANCE à la caméra (CLAUDE.md §11), et une
/// résolution de référence partagée avec la GlobalWorldUI. En overlay, tout cela disparaît : le
/// panneau EST l'écran, `bottom`/`right` suffisent, et le `Sort Order` du PanelSettings (200) le
/// place au-dessus de tout le reste.
///
/// ⚠ Rien n'est pickable par défaut (`picking-mode="Ignore"` dans l'UXML) : seuls les BOUTONS le
/// sont — le `pickingMode` d'un parent n'empêche pas ses enfants d'être trouvés. Une notification ne
/// peut donc pas voler le laser ni empêcher de peindre sous elle. Contrepartie du passage en
/// overlay : il n'y a plus de collider, donc plus de raycast physique — le curseur spray atteint ces
/// boutons par <see cref="ScreenSpaceUI"/>, où cet hôte s'inscrit.
/// </summary>
public class NotificationHost : MonoBehaviour
{
    // Assets, tous dans Resources : aucun câblage de scène, l'hôte se construit seul dans n'importe
    // quelle scène (jeu comme calibration).
    private const string PanelSettingsPath = "Notifications/NotificationPanelSettings";
    private const string HostTemplatePath = "Notifications/NotificationHost";
    private const string CardTemplatePath = "Notifications/NotificationCard";
    private const string StyleSheetPath = "Notifications/Notifications";

    /// <summary>Au-delà, on ne lit plus rien : la plus ancienne cède la place.</summary>
    private const int MaxVisible = 4;

    /// <summary>Durée des transitions d'apparition et de disparition (doit suivre la feuille de style).</summary>
    private const long FadeMs = 220;

    /// <summary>
    /// Délai avant le REPLI d'une carte : message et boutons se rétractent, il ne reste que la
    /// couleur, l'icône et le titre. Dépliées en plein jeu, les cartes gênent la peinture ; repliées,
    /// elles disent encore d'un coup d'œil de quoi il s'agit.
    /// </summary>
    private const long CollapseDelayMs = 5000;

    /// <summary>Préfixe des clés émises par les vérifications (<see cref="CheckReport.Key"/>).</summary>
    private const string CheckKeyPrefix = "check:";

    private static NotificationHost _instance;

    private UIDocument _document;
    private VisualElement _stack;
    private VisualElement _cursorLayer;
    private VisualTreeAsset _cardTemplate;
    private readonly List<VisualElement> _cursorEchoes = new List<VisualElement>();

    /// <summary>Tout ce qu'il faut pour piloter une carte après sa création.</summary>
    private sealed class Card
    {
        public VisualElement Root;      // TemplateContainer, toujours transparent au pointeur
        public VisualElement Frame;     // la carte elle-même : c'est elle qu'on replie
        public VisualElement Details;   // message + boutons : le bloc qui se rétracte (et qui ROGNE)
        public VisualElement Message;
        public VisualElement Actions;
        public IVisualElementScheduledItem Expiry;     // disparition programmée (cartes temporaires)
        public IVisualElementScheduledItem Collapse;   // repli programmé
        public bool Compact;                           // bulle d'état : une ligne, ni repli ni limite de pile
        public bool Collapsed;
        public bool Busy;                              // choix de durée ouvert : ni repli ni disparition
        public Action FillActions;                     // reconstruit les boutons d'origine
    }

    private readonly Dictionary<Notification, Card> _cards = new Dictionary<Notification, Card>();

    public static NotificationHost Instance => _instance;

    /// <summary>
    /// Crée l'hôte s'il n'existe pas. Appelé automatiquement au chargement d'une scène : les pannes
    /// de démarrage doivent pouvoir s'afficher sans qu'on ait pensé à câbler quoi que ce soit.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => Ensure();

    /// <summary>Crée l'hôte à la demande et le rend. ⚠ Unity exige que le point d'entrée automatique
    /// ci-dessus soit `void` : d'où les deux méthodes.</summary>
    public static NotificationHost Ensure()
    {
        if (_instance != null) return _instance;

        GameObject go = new GameObject("NotificationHost");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<NotificationHost>();
        return _instance;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;

        var panelSettings = Resources.Load<PanelSettings>(PanelSettingsPath);
        var hostTemplate = Resources.Load<VisualTreeAsset>(HostTemplatePath);
        _cardTemplate = Resources.Load<VisualTreeAsset>(CardTemplatePath);
        var styleSheet = Resources.Load<StyleSheet>(StyleSheetPath);

        if (panelSettings == null || hostTemplate == null || _cardTemplate == null)
        {
            Debug.LogError("[NotificationHost] Assets introuvables dans Resources/Notifications : " +
                           "aucune notification ne pourra s'afficher.");
            enabled = false;
            return;
        }

        // ⚠ Un UXML qui NE S'IMPORTE PAS donne quand même un VisualTreeAsset — vide. `Instantiate()`
        // réussit alors, et c'est la première recherche d'élément qui plante, très loin de la cause.
        // (Vécu : un commentaire XML contenant deux tirets à la suite.) On le dit ici, une fois.
        if (_cardTemplate.Instantiate().Q<VisualElement>("notification") == null)
        {
            Debug.LogError($"[NotificationHost] Le gabarit {CardTemplatePath} est vide : son UXML n'a " +
                           "pas pu être importé. Voir les erreurs d'import dans la console.");
            enabled = false;
            return;
        }

        _document = gameObject.AddComponent<UIDocument>();
        _document.panelSettings = panelSettings;
        _document.visualTreeAsset = hostTemplate;

        VisualElement root = _document.rootVisualElement;
        if (styleSheet != null) root.styleSheets.Add(styleSheet);

        _stack = root.Q<VisualElement>("notification-stack");
        // La racine du document ne doit rien intercepter : elle couvre TOUT l'écran.
        root.pickingMode = PickingMode.Ignore;

        // ⚠ Couche des curseurs, ajoutée APRÈS la pile : elle se dessine donc par-dessus les cartes.
        // Un panneau en overlay recouvre toute la scène 3D — le curseur spray, qui vit sur un canvas
        // WORLD-SPACE, passerait forcément dessous. Le seul moyen de le faire passer devant est de le
        // redessiner ici (cf. UpdateCursorEchoes).
        _cursorLayer = new VisualElement { name = "notification-cursors", pickingMode = PickingMode.Ignore };
        _cursorLayer.AddToClassList("notification-cursor-layer");
        root.Add(_cursorLayer);

        // Le curseur spray n'a pas de collider à viser en overlay : il passe par ce registre.
        ScreenSpaceUI.Register(_document);
    }

    private void OnEnable()
    {
        NotificationCenter.Posted += Show;
        NotificationCenter.Closed += Remove;
        NotificationCenter.SetHostReady(true);
    }

    private void OnDisable()
    {
        NotificationCenter.SetHostReady(false);
        NotificationCenter.Posted -= Show;
        NotificationCenter.Closed -= Remove;
    }

    private void OnDestroy()
    {
        ScreenSpaceUI.Unregister(_document);
        if (_instance == this) _instance = null;
    }

    // =========================================================================
    // Curseurs spray redessinés DANS le panneau
    // =========================================================================

    /// <summary>Au-delà, on arrête de dessiner : quatre bombes, quelques images chacune.</summary>
    private const int MaxEchoes = 16;

    private readonly List<SprayCursorUI.CursorLayer> _cursorLayers = new List<SprayCursorUI.CursorLayer>();

    /// <summary>
    /// Redessine chaque curseur spray qui passe SUR une carte, image par image : sa place, sa
    /// taille, ses sprites et ses teintes sont RELEVÉS sur le vrai curseur à chaque frame. Il garde
    /// donc son apparence du moment — disque translucide et liseré en couleur inverse en mode UI,
    /// cercle de peinture sinon, icône d'outil comprise.
    ///
    /// ⚠ Pourquoi un doublon plutôt qu'un réglage d'ordre d'affichage. Le curseur est une image d'un
    /// canvas WORLD-SPACE : il fait partie de la scène 3D. Un panneau UI Toolkit en overlay est
    /// dessiné APRÈS toute la scène — aucun `Sort Order`, aucune file de rendu ne peut faire passer
    /// le curseur devant lui. Le seul endroit d'où il peut apparaître au-dessus d'une notification,
    /// c'est le panneau lui-même. Le doublon se superpose EXACTEMENT au vrai curseur (mêmes coins
    /// projetés), il n'y a donc ni décalage ni second curseur visible.
    ///
    /// Il n'apparaît que sur une carte : partout ailleurs le vrai curseur n'est masqué par rien.
    /// </summary>
    private void LateUpdate()
    {
        int used = 0;

        IPanel panel = _document != null && _document.rootVisualElement != null
            ? _document.rootVisualElement.panel : null;

        if (_cursorLayer != null && panel != null && _cards.Count > 0 && UIManager.Instance != null)
        {
            Camera cam = ScreenSpaceUI.DisplayCamera();
            IReadOnlyList<SprayCursorUI> cursors = UIManager.Instance.Cursors;

            for (int i = 0; cam != null && i < cursors.Count && used < MaxEchoes; i++)
            {
                SprayCursorUI cursor = cursors[i];
                if (cursor == null || !cursor.isActiveAndEnabled) continue;
                if (cursor.CollectScreenLayers(cam, _cursorLayers) == 0) continue;

                // L'ensemble du curseur décide : une seule de ses images touchant une carte suffit,
                // sinon on découperait le curseur en deux entre l'écran et le panneau.
                Rect inPanel = ToPanelRect(panel, _cursorLayers[0].ScreenRect);
                for (int l = 1; l < _cursorLayers.Count; l++)
                    inPanel = Union(inPanel, ToPanelRect(panel, _cursorLayers[l].ScreenRect));

                if (!OverlapsACard(inPanel)) continue;

                for (int l = 0; l < _cursorLayers.Count && used < MaxEchoes; l++)
                    DrawEcho(used++, ToPanelRect(panel, _cursorLayers[l].ScreenRect), _cursorLayers[l]);
            }
        }

        for (int i = used; i < _cursorEchoes.Count; i++)
            _cursorEchoes[i].style.display = DisplayStyle.None;
    }

    /// <summary>Rectangle écran (origine en bas) vers le repère du panneau (origine en haut).</summary>
    private static Rect ToPanelRect(IPanel panel, Rect screenRect)
    {
        Vector2 a = ScreenSpaceUI.ToPanel(panel, new Vector2(screenRect.xMin, screenRect.yMin));
        Vector2 b = ScreenSpaceUI.ToPanel(panel, new Vector2(screenRect.xMax, screenRect.yMax));
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                               Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    /// <summary>Sur une CARTE, pas sur la pile : celle-ci est large et surtout vide, le doublon y
    /// serait dessiné pour rien par-dessus le vrai curseur.</summary>
    private bool OverlapsACard(Rect inPanel)
    {
        foreach (KeyValuePair<Notification, Card> pair in _cards)
            if (pair.Value.Frame != null && pair.Value.Frame.worldBound.Overlaps(inPanel)) return true;
        return false;
    }

    private static Rect Union(Rect a, Rect b)
        => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                           Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    private void DrawEcho(int index, Rect inPanel, SprayCursorUI.CursorLayer layer)
    {
        while (_cursorEchoes.Count <= index)
        {
            var created = new VisualElement { name = "notification-cursor", pickingMode = PickingMode.Ignore };
            created.AddToClassList("notification-cursor");
            _cursorLayer.Add(created);
            _cursorEchoes.Add(created);
        }

        VisualElement echo = _cursorEchoes[index];
        echo.style.display = DisplayStyle.Flex;
        echo.style.left = inPanel.xMin;
        echo.style.top = inPanel.yMin;
        echo.style.width = inPanel.width;
        echo.style.height = inPanel.height;

        // Le sprite RÉEL, teinté comme l'image d'origine — alpha compris : c'est lui qui porte la
        // translucidité du disque en mode UI, et l'opacité pleine du liseré.
        if (layer.Sprite != null)
        {
            echo.style.backgroundImage = new StyleBackground(layer.Sprite);
            echo.style.unityBackgroundImageTintColor = layer.Color;
            echo.style.backgroundColor = Color.clear;
        }
        else
        {
            // Sans sprite, uGUI dessine un aplat de la couleur : même chose ici.
            echo.style.backgroundImage = new StyleBackground(StyleKeyword.None);
            echo.style.backgroundColor = layer.Color;
        }
    }

    // =========================================================================
    // Cycle de vie d'une carte
    // =========================================================================

    private void Show(Notification notification)
    {
        if (_stack == null || notification == null || notification.Dismissed) return;
        if (_cards.ContainsKey(notification)) return;

        Card card = BuildCard(notification);
        // Ajoutée en DERNIER : la pile étant ancrée par le bas, la plus récente reste au plus près du
        // coin et pousse les précédentes vers le haut.
        _stack.Add(card.Root);
        _cards[notification] = card;

        TrimOverflow();

        // ⚠ L'apparition ne peut pas s'interpoler depuis le premier rendu : l'état de départ est dans
        // la feuille de style, on bascule la classe à la frame suivante (CLAUDE.md §8).
        card.Root.schedule.Execute(() => card.Frame.AddToClassList("notification--visible")).StartingIn(20);

        KeepActionsVisible(card);

        if (!notification.Persistent)
            card.Expiry = card.Root.schedule
                .Execute(() => NotificationCenter.Dismiss(notification))
                .StartingIn((long)notification.DurationMs);

        // Une bulle est déjà à sa taille minimale : rien à replier.
        if (!card.Compact) ScheduleCollapse(notification, card);
    }

    /// <summary>
    /// Filet de sécurité : un bouton de la carte ne doit JAMAIS sortir du cadre. Le bloc `details`
    /// est rogné (`overflow: hidden`) par le plafond qui sert à animer le repli — un message
    /// exceptionnellement long y poussait « Résoudre » dehors, où il est invisible ET incliquable :
    /// l'opérateur n'a alors plus aucun moyen d'agir sur le problème qu'on vient de lui annoncer.
    /// Les plafonds de la feuille de style sont calculés pour que ça n'arrive pas ; ceci le GARANTIT,
    /// en rognant le MESSAGE de ce qui dépasse (le détail complet reste dans « État du système »).
    ///
    /// ⚠ Mesuré une fois, APRÈS la mise en page, et jamais pendant une animation : en plein repli ou
    /// dépliage le bloc dépasse par construction, et mesurer là rognerait un message qui tient très
    /// bien.
    /// </summary>
    private void KeepActionsVisible(Card card)
    {
        card.Root.schedule.Execute(() =>
        {
            if (card.Collapsed || card.Details == null || card.Message == null) return;
            if (card.Actions == null || card.Actions.childCount == 0) return;

            float overflow = card.Actions.worldBound.yMax - card.Details.worldBound.yMax;
            if (overflow <= 1f) return;

            float height = card.Message.resolvedStyle.height;
            if (height <= 0f) return;

            card.Message.style.maxHeight = Mathf.Max(48f, height - overflow);
        }).StartingIn(400);   // au-delà de la transition de dépliage (0,32 s)
    }

    /// <summary>
    /// Plafonne les cartes de DIAGNOSTIC. ⚠ Les bulles d'état ne comptent pas : elles sont au plus une
    /// par joueur (clé), mais quatre joueurs en panne auraient chassé de l'écran, ancienneté oblige,
    /// une carte persistante portant « Résoudre » (CLAUDE.md §13).
    /// </summary>
    private void TrimOverflow()
    {
        while (true)
        {
            int count = 0;
            VisualElement oldestRoot = null;
            Notification oldestOwner = null;

            foreach (VisualElement child in _stack.Children())
            {
                Notification owner = OwnerOf(child);
                if (owner != null && _cards[owner].Compact) continue;

                count++;
                if (oldestRoot == null) { oldestRoot = child; oldestOwner = owner; }
            }

            if (count <= MaxVisible) return;

            if (oldestOwner != null) NotificationCenter.Dismiss(oldestOwner);
            else oldestRoot.RemoveFromHierarchy();   // carte déjà fermée, en fondu de sortie
        }
    }

    private Notification OwnerOf(VisualElement root)
    {
        foreach (KeyValuePair<Notification, Card> pair in _cards)
            if (pair.Value.Root == root) return pair.Key;
        return null;
    }

    private void Remove(Notification notification)
    {
        if (notification == null || !_cards.TryGetValue(notification, out Card card)) return;

        _cards.Remove(notification);
        card.Expiry?.Pause();
        card.Collapse?.Pause();

        card.Frame.RemoveFromClassList("notification--visible");
        card.Root.schedule.Execute(card.Root.RemoveFromHierarchy).StartingIn(FadeMs);
    }

    // =========================================================================
    // Construction
    // =========================================================================

    private Card BuildCard(Notification notification)
    {
        TemplateContainer container = _cardTemplate.Instantiate();
        // Le TemplateContainer s'insère entre la pile et la carte : sans cela il garderait une
        // largeur nulle et la carte ne s'alignerait pas à droite.
        container.style.width = Length.Percent(100);
        container.pickingMode = PickingMode.Ignore;

        VisualElement root = container.Q<VisualElement>("notification") ?? container;
        root.AddToClassList(LevelClass(notification.Level));

        var card = new Card
        {
            Root = container,
            Frame = root,
            Details = container.Q<VisualElement>("details"),
            Message = container.Q<Label>("message"),
            Actions = container.Q<VisualElement>("actions"),
            Compact = notification.Compact
        };
        if (notification.Compact) root.AddToClassList("notification--compact");

        container.Q<Label>("title").text = notification.Title;
        container.Q<Label>("message").text = notification.Message;

        VisualElement icon = container.Q<VisualElement>("icon");
        if (icon != null) icon.style.backgroundImage = new StyleBackground(IconOf(notification.Level));

        Button close = container.Q<Button>("close");
        if (close != null)
        {
            // Une carte qui disparaît seule n'a pas besoin de bouton de fermeture. Une bulle non plus,
            // même persistante : elle se retire d'elle-même quand l'état du joueur change.
            close.style.display = notification.Persistent && !notification.Compact ? DisplayStyle.Flex : DisplayStyle.None;
            // ⚠ ClickEvent et jamais Button.clicked : le curseur spray n'émet pas la paire
            // PointerDown/PointerUp qu'attend le manipulateur Clickable (CLAUDE.md §8).
            close.RegisterCallback<ClickEvent>(e => NotificationCenter.Dismiss(notification));
        }

        card.FillActions = () => FillActions(notification, card);
        card.FillActions();

        // Repliée, une carte d'erreur se redéplie d'un clic : son fond redevient alors pickable, le
        // temps du repli seulement.
        root.RegisterCallback<ClickEvent>(e =>
        {
            if (!card.Collapsed || WorldPanelUtils.ButtonOf(e.target) != null) return;
            Expand(card);
            ScheduleCollapse(notification, card);
        });

        return card;
    }

    private void FillActions(Notification notification, Card card)
    {
        VisualElement actions = card.Actions;
        if (actions == null) return;
        actions.Clear();

        // « Résoudre » : exactement celui d'« État du système », sur la carte qui annonce le
        // problème. Sans lui, l'opérateur lit un diagnostic puis doit aller chercher le remède dans
        // un panneau — alors que la réparation est à un clic.
        CheckReport report = ReportOf(notification);
        bool canRepair = report != null && report.Result != null && report.Result.CanRepair &&
                         report.Severity != CheckSeverity.Ok &&
                         CheckCategory.IsOperatorFacing(report.Category);

        if (canRepair)
        {
            Button resolve = CardButton("Résoudre",
                                        string.Join(", ", report.Result.Repairs.ConvertAll(r => r.Label)),
                                        "notification__button--primary");
            resolve.SetEnabled(!RepairRunner.Busy);
            resolve.RegisterCallback<ClickEvent>(e =>
            {
                // La carte s'en va : la réparation prend la main, et le verdict qui suit
                // (RunOnly, systématique même si l'opérateur annule) réannonce ce qui reste.
                if (UIManager.Instance != null && UIManager.Instance.ResolveCheck(report))
                    NotificationCenter.Dismiss(notification);
            });
            actions.Add(resolve);
        }

        if (!string.IsNullOrEmpty(notification.ActionLabel) && notification.Action != null)
        {
            // Un SEUL bouton vert par carte, celui qui règle le problème : à côté de « Résoudre »,
            // la procédure illustrée n'est plus l'action principale.
            Button action = CardButton(notification.ActionLabel, notification.ActionLabel,
                                       canRepair ? null : "notification__button--primary");
            action.RegisterCallback<ClickEvent>(e =>
            {
                notification.Action?.Invoke();
                NotificationCenter.Dismiss(notification);
            });
            actions.Add(action);
        }

        // Carte d'une vérification : on peut l'IGNORER pour une durée, pas seulement la fermer.
        if (!string.IsNullOrEmpty(notification.Key) && notification.Key.StartsWith(CheckKeyPrefix))
        {
            Button ignore = CardButton("Ignorer", "Ne plus signaler ce problème pendant un temps", null);
            ignore.RegisterCallback<ClickEvent>(e => ShowSnoozeChoices(notification, card));
            actions.Add(ignore);
        }
    }

    /// <summary>Le rapport de vérification derrière une carte, s'il y en a un (clé `check:…`).</summary>
    private static CheckReport ReportOf(Notification notification)
    {
        if (notification == null || string.IsNullOrEmpty(notification.Key) ||
            !notification.Key.StartsWith(CheckKeyPrefix)) return null;

        foreach (CheckReport report in SystemStatus.Reports)
            if (report != null && report.Key == notification.Key) return report;

        return null;
    }

    /// <summary>
    /// Choix de la durée SUR PLACE, dans la carte : une popup à part demanderait son propre hôte, et
    /// la carte est déjà là. Disparition et repli sont suspendus le temps du choix, sinon la carte
    /// s'en irait sous le curseur.
    /// </summary>
    private void ShowSnoozeChoices(Notification notification, Card card)
    {
        card.Busy = true;
        card.Expiry?.Pause();
        card.Collapse?.Pause();

        VisualElement actions = card.Actions;
        actions.Clear();

        for (int i = 0; i < CheckSnooze.Choices.Count; i++)
        {
            int choice = i;
            string label = CheckSnooze.Choices[i].Key;
            Button option = CardButton(label, "Ignorer " + label.ToLowerInvariant(), "notification__button--snooze");
            option.RegisterCallback<ClickEvent>(e =>
            {
                CheckSnooze.Snooze(new[] { notification.Key }, choice);
                NotificationCenter.Dismiss(notification);
            });
            actions.Add(option);
        }

        Button cancel = CardButton("Annuler", "Ne pas ignorer ce problème", null);
        cancel.RegisterCallback<ClickEvent>(e =>
        {
            card.Busy = false;
            card.FillActions();
            KeepActionsVisible(card);
            card.Expiry?.Resume();
            ScheduleCollapse(notification, card);
        });
        actions.Add(cancel);

        // Cinq boutons au lieu d'un : la rangée peut passer sur deux lignes, donc remesurer.
        KeepActionsVisible(card);
    }

    private static Button CardButton(string label, string tooltip, string modifier)
    {
        Button button = new Button { text = label, tooltip = tooltip };
        button.AddToClassList("notification__button");
        if (!string.IsNullOrEmpty(modifier)) button.AddToClassList(modifier);
        return button;
    }

    // =========================================================================
    // Repli
    // =========================================================================

    private void ScheduleCollapse(Notification notification, Card card)
    {
        card.Collapse?.Pause();
        card.Collapse = card.Root.schedule.Execute(() => Collapse(card)).StartingIn(CollapseDelayMs);
    }

    /// <summary>
    /// Repli : c'est la feuille de style qui anime (`max-height`), pas le C#. ⚠ Ne jamais essayer
    /// d'interpoler une `height` depuis `auto` — c'est impossible, et mesurer la carte pour figer sa
    /// hauteur était la source de la version précédente.
    /// </summary>
    private void Collapse(Card card)
    {
        if (card.Collapsed || card.Busy) return;
        card.Collapsed = true;
        card.Frame.AddToClassList("notification--collapsed");
        // Repliée, la CARTE (et non le conteneur pleine largeur) accepte le clic qui la redéplie.
        card.Frame.pickingMode = PickingMode.Position;
    }

    private void Expand(Card card)
    {
        if (!card.Collapsed) return;
        card.Collapsed = false;
        card.Frame.RemoveFromClassList("notification--collapsed");
        KeepActionsVisible(card);
        // Dépliée, elle laisse de nouveau passer le laser : seuls ses boutons restent visés.
        card.Frame.pickingMode = PickingMode.Ignore;
    }

    // =========================================================================
    // Habillage
    // =========================================================================

    private static string LevelClass(NotificationLevel level)
    {
        switch (level)
        {
            case NotificationLevel.Error: return "notification--error";
            case NotificationLevel.Warning: return "notification--warning";
            default: return "notification--info";
        }
    }

    private static Texture2D IconOf(NotificationLevel level)
    {
        switch (level)
        {
            case NotificationLevel.Error: return Resources.Load<Texture2D>("Icons/close");
            case NotificationLevel.Warning: return UIIcons.WarningTriangle;
            default: return Resources.Load<Texture2D>("Icons/check");
        }
    }
}
