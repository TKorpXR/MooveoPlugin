using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Récapitulatif des vérifications : ce qui est au vert, ce qui ne l'est pas, et quoi faire.
///
/// Construit en C# comme <see cref="GuidePopup"/>, et pour la même raison : il doit pouvoir
/// s'afficher sur n'importe quel document. L'hôte world-space lui donne son collider VR.
/// </summary>
public static class SystemStatusPanel
{
    private static VisualElement _overlay;
    private static VisualElement _card;
    private static VisualElement _list;
    private static Label _summary;
    private static VisualElement _spinner;
    private static IVisualElementScheduledItem _spinnerAnim;
    private static float _spinnerAngle;
    private static Action _onClosed;
    private static Action _clearTooltip;
    private static VisualElement _host;
    private static Button _rerun;
    private static Button _resolveAll;
    private static VisualElement _summaryRow;
    private static VisualElement _consoleView;
    private static Button _tabChecks;
    private static Button _tabConsole;
    private static bool _consoleTab;

    /// <summary>
    /// ⚠ La liste est reconstruite à chaque verdict (~1/s pendant un scan). Sous un geste, le
    /// défilement saccadait et la ligne pressée disparaissait : tant qu'on la tient, la mise à jour attend.
    /// </summary>
    private static ScrollView _scroll;
    private static bool _listHeld;
    private static float _heldSince;
    private static bool _refreshPending;
    /// <summary>Filet : un relâchement perdu ne doit pas figer le panneau à vie.</summary>
    private const float HoldTimeoutSeconds = 10f;

    /// <summary>
    /// État de chaque bloc repliable, par titre, UNIQUEMENT une fois que l'opérateur l'a touché.
    /// Survit à la fermeture : rouvrir le panneau ne doit pas replier ce qu'on inspectait.
    /// </summary>
    private static readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>();

    /// <summary>Catégories opérateur présentées en bloc repliable ; les autres restent à plat.</summary>
    private static readonly HashSet<string> CollapsibleCategories = new HashSet<string>
    {
        CheckCategory.ThirdParty,
        CheckCategory.Hardware
    };

    private const string TechnicalTitle = "Détails techniques";

    public static bool IsOpen => _overlay != null && _overlay.parent != null;

    /// <summary>Carte du panneau, pour que l'hôte world-space y cale son collider VR.</summary>
    public static VisualElement Card => IsOpen ? _card : null;

    /// <param name="clearTooltip">
    /// Efface la bulle d'aide de l'hôte. La liste est RECONSTRUITE à chaque verdict : le bouton survolé
    /// disparaît sans émettre de PointerLeave, et sa bulle resterait affichée dans le vide.
    /// </param>
    public static void Show(VisualElement host, Action onClosed = null, Action clearTooltip = null)
    {
        Close();
        if (host == null) return;

        _onClosed = onClosed;
        _clearTooltip = clearTooltip;
        _host = host;

        _overlay = new VisualElement { name = "system-status-overlay" };
        _overlay.style.position = Position.Absolute;
        _overlay.style.left = 0;
        _overlay.style.top = 0;
        _overlay.style.right = 0;
        _overlay.style.bottom = 0;
        _overlay.style.alignItems = Align.Center;
        _overlay.style.justifyContent = Justify.Center;
        _overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.75f);
        _overlay.pickingMode = PickingMode.Position;

        _card = new VisualElement { name = "system-status-card" };
        _card.AddToClassList("glass-panel");
        _card.AddToClassList("feature-window");
        _card.style.width = Length.Percent(62);
        _card.style.maxWidth = 1000;
        _card.style.height = Length.Percent(80);
        _card.style.paddingLeft = 32;
        _card.style.paddingRight = 32;
        _card.style.paddingTop = 26;
        _card.style.paddingBottom = 22;
        _card.style.overflow = Overflow.Hidden;
        _card.style.backgroundColor = new Color(0.11f, 0.11f, 0.12f, 0.97f);
        _overlay.Add(_card);

        _consoleTab = false;
        BuildHeader(_card);
        BuildList(_card);
        _consoleView = SystemStatusConsole.Build();
        _consoleView.style.display = DisplayStyle.None;
        _card.Add(_consoleView);
        BuildFooter(_card);

        host.Add(_overlay);
        _overlay.BringToFront();

        // ⚠ Changed et non Completed : écouter la seule fin de campagne figeait le panneau pendant
        // vingt secondes, et « Relancer les vérifications » paraissait ne rien faire.
        SystemStatus.Changed += Refresh;
        CheckSnooze.Changed += Refresh;
        RepairRunner.Changed += Refresh;
        Refresh();
    }

    public static void Close()
    {
        if (_overlay != null && _overlay.parent != null) _overlay.RemoveFromHierarchy();
        SystemStatus.Changed -= Refresh;
        CheckSnooze.Changed -= Refresh;
        RepairRunner.Changed -= Refresh;

        _overlay = null;
        _card = null;
        _list = null;
        _summary = null;
        _spinnerAnim?.Pause();
        _spinnerAnim = null;
        _spinner = null;
        _rerun = null;
        _resolveAll = null;
        _scroll = null;
        SystemStatusConsole.Release();
        _consoleView = null;
        _summaryRow = null;
        _tabChecks = null;
        _tabConsole = null;
        _consoleTab = false;
        _listHeld = false;
        _refreshPending = false;
        _host = null;
        _clearTooltip?.Invoke();
        _clearTooltip = null;

        Action closed = _onClosed;
        _onClosed = null;
        closed?.Invoke();
    }

    // =========================================================================
    // Construction
    // =========================================================================

    private static void BuildHeader(VisualElement card)
    {
        Label title = new Label("État du système");
        title.AddToClassList("header-text");
        title.style.fontSize = 28;
        title.style.letterSpacing = 0;
        title.style.marginBottom = 4;
        title.style.alignSelf = Align.Stretch;
        title.style.unityTextAlign = TextAnchor.MiddleCenter;
        title.style.flexShrink = 0;
        card.Add(title);
        BuildTabs(card);

        // Ligne de résumé : pastille tournante à gauche pendant la campagne, texte à droite.
        VisualElement summaryRow = new VisualElement();
        _summaryRow = summaryRow;
        summaryRow.style.flexDirection = FlexDirection.Row;
        summaryRow.style.alignItems = Align.Center;
        summaryRow.style.justifyContent = Justify.Center;
        summaryRow.style.alignSelf = Align.Stretch;
        summaryRow.style.flexShrink = 0;
        card.Add(summaryRow);

        // Même pastille que les lignes de la calibration et que les chargements du menu Média :
        // un anneau à secteur clair, tourné image par image.
        _spinner = new VisualElement();
        _spinner.AddToClassList("icon-status-loading");
        _spinner.style.width = 22;
        _spinner.style.height = 22;
        _spinner.style.marginRight = 10;
        _spinner.style.flexShrink = 0;
        _spinner.style.display = DisplayStyle.None;
        summaryRow.Add(_spinner);

        _summary = new Label();
        _summary.style.unityTextAlign = TextAnchor.MiddleCenter;
        _summary.style.fontSize = 16;
        _summary.style.color = new Color(1f, 1f, 1f, 0.6f);
        _summary.style.whiteSpace = WhiteSpace.Normal;
        summaryRow.Add(_summary);

        // Visible seulement quand il y a quelque chose à tenter : un bouton toujours présent finit
        // par ne plus être lu.
        _resolveAll = new Button { tooltip = "Tenter de corriger tous les problèmes, un par un" };
        _resolveAll.AddToClassList("control-button-small");
        _resolveAll.AddToClassList("popup-btn-green");
        _resolveAll.style.color = Color.white;
        _resolveAll.style.width = StyleKeyword.Auto;
        _resolveAll.style.height = 48;
        _resolveAll.style.fontSize = 17;
        _resolveAll.style.paddingLeft = 24;
        _resolveAll.style.paddingRight = 24;
        _resolveAll.style.marginTop = 12;
        _resolveAll.style.alignSelf = Align.Center;
        _resolveAll.style.flexShrink = 0;
        _resolveAll.style.display = DisplayStyle.None;
        SetRadius(_resolveAll, 12);
        _resolveAll.RegisterCallback<ClickEvent>(e => RepairRunner.Resolve(OperatorReports(), _host));
        card.Add(_resolveAll);

        VisualElement rule = new VisualElement();
        rule.style.alignSelf = Align.Stretch;
        rule.style.height = 1;
        rule.style.marginTop = 14;
        rule.style.marginBottom = 10;
        rule.style.flexShrink = 0;
        rule.style.backgroundColor = new Color(1f, 1f, 1f, 0.14f);
        card.Add(rule);
    }

    /// <summary>Barre d'onglets : Vérifications / Console.</summary>
    private static void BuildTabs(VisualElement card)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.Center;
        row.style.marginTop = 6;
        row.style.marginBottom = 10;
        row.style.flexShrink = 0;
        card.Add(row);

        _tabChecks = TabButton("Vérifications", "Résultat des vérifications");
        _tabChecks.RegisterCallback<ClickEvent>(e => SelectTab(false));
        row.Add(_tabChecks);

        _tabConsole = TabButton("Console", "Erreurs survenues depuis le lancement");
        _tabConsole.RegisterCallback<ClickEvent>(e => SelectTab(true));
        row.Add(_tabConsole);

        StyleTabs();
    }

    private static Button TabButton(string label, string tooltip)
    {
        Button button = new Button { text = label, tooltip = tooltip };
        button.AddToClassList("control-button-small");
        button.style.width = StyleKeyword.Auto;
        button.style.minWidth = 170;
        button.style.height = 42;
        button.style.fontSize = 17;
        button.style.paddingLeft = 22;
        button.style.paddingRight = 22;
        button.style.marginLeft = 6;
        button.style.marginRight = 6;
        button.style.color = Color.white;
        SetRadius(button, 12);
        return button;
    }

    /// <summary>Affiche la liste des vérifications ou la console.</summary>
    private static void SelectTab(bool console)
    {
        if (_scroll == null || _consoleView == null || _consoleTab == console) return;
        _consoleTab = console;

        _scroll.style.display = console ? DisplayStyle.None : DisplayStyle.Flex;
        _consoleView.style.display = console ? DisplayStyle.Flex : DisplayStyle.None;
        if (_summaryRow != null) _summaryRow.style.display = console ? DisplayStyle.None : DisplayStyle.Flex;

        if (console) SystemStatusConsole.Start();
        else SystemStatusConsole.Stop();

        StyleTabs();
        RefreshActions();
        _clearTooltip?.Invoke();
    }

    private static void StyleTabs()
    {
        if (_tabChecks == null || _tabConsole == null) return;
        _tabChecks.style.backgroundColor = new Color(1f, 1f, 1f, _consoleTab ? 0.06f : 0.22f);
        _tabConsole.style.backgroundColor = new Color(1f, 1f, 1f, _consoleTab ? 0.22f : 0.06f);
    }

    private static void BuildList(VisualElement card)
    {
        ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;
        scroll.style.flexShrink = 1;
        // ⚠ min-height: 0, sinon la liste ne se réduit pas et DÉBORDE par-dessus le pied de carte.
        scroll.style.minHeight = 0;
        // Même barre fine que la liste de vérification de la calibration (règle partagée, VisionOS.uss).
        scroll.AddToClassList("slim-scroll");
        scroll.AddManipulator(new TouchScrollManipulator(scroll));
        card.Add(scroll);

        // TrickleDown : l'appui sur une ligne (descendante) passe d'abord par la liste.
        scroll.RegisterCallback<PointerDownEvent>(_ =>
        {
            _listHeld = true;
            _heldSince = Time.realtimeSinceStartup;
        }, TrickleDown.TrickleDown);
        scroll.RegisterCallback<PointerUpEvent>(_ => ReleaseList(), TrickleDown.TrickleDown);
        // Fin d'un glissement : le manipulateur rend la capture.
        scroll.RegisterCallback<PointerCaptureOutEvent>(_ => ReleaseList());

        _scroll = scroll;
        _list = scroll.contentContainer;
    }

    /// <summary>Fin du geste : la mise à jour mise en attente s'applique.</summary>
    private static void ReleaseList()
    {
        if (!_listHeld) return;
        _listHeld = false;

        if (!_refreshPending || _scroll == null) return;
        _refreshPending = false;

        // ⚠ Frame SUIVANTE : le clic suit le PointerUp dans le même appel, reconstruire maintenant
        // détruirait « Résoudre » avant qu'il l'atteigne.
        _scroll.schedule.Execute(Refresh);
    }

    private static void BuildFooter(VisualElement card)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.Center;
        row.style.marginTop = 16;
        row.style.flexShrink = 0;
        card.Add(row);

        Button close = FooterButton("Fermer", "Fermer le panneau");
        close.style.marginRight = 14;
        close.RegisterCallback<ClickEvent>(e => Close());
        row.Add(close);

        _rerun = FooterButton("Relancer les vérifications", "Refaire toutes les vérifications");
        _rerun.AddToClassList("popup-btn-green");
        _rerun.style.color = Color.white;
        // Même portée que la campagne en cours : relancer depuis la calibration ne doit pas
        // aller chercher le mur, qui n'existe pas encore.
        _rerun.RegisterCallback<ClickEvent>(e =>
            SystemCheckRunner.RunAll(manual: true, scope: SystemCheckRunner.LastScope));
        row.Add(_rerun);
    }

    private static Button FooterButton(string label, string tooltip)
    {
        Button button = new Button { text = label, tooltip = tooltip };
        button.AddToClassList("control-button-small");
        // ⚠ .control-button-small impose width: 40px : sans libération le libellé déborde du fond.
        button.style.width = StyleKeyword.Auto;
        button.style.minWidth = 190;
        button.style.height = 58;
        button.style.fontSize = 19;
        button.style.paddingLeft = 26;
        button.style.paddingRight = 26;
        button.style.marginLeft = 0;
        button.style.marginRight = 0;
        button.style.whiteSpace = WhiteSpace.NoWrap;
        SetRadius(button, 14);
        return button;
    }

    // =========================================================================
    // Contenu
    // =========================================================================

    private static void Refresh()
    {
        if (_list == null) return;

        // Liste tenue : reportée au relâchement (cf. _listHeld).
        if (_listHeld && Time.realtimeSinceStartup - _heldSince < HoldTimeoutSeconds)
        {
            _refreshPending = true;
            return;
        }
        _listHeld = false;
        _refreshPending = false;

        _clearTooltip?.Invoke();
        _list.Clear();

        SetSpinnerVisible(SystemStatus.Running);
        RefreshActions();

        IReadOnlyList<CheckReport> reports = SystemStatus.Reports;

        if (SystemStatus.Running)
        {
            // On liste quand même ce qui est DÉJÀ tombé : le panneau se remplit sous les yeux
            // plutôt que de rester vide jusqu'au bout.
            _summary.text = $"Vérifications en cours… ({reports.Count} terminées)";
            AppendReports(reports);
            return;
        }

        if (reports.Count == 0)
        {
            _summary.text = "Aucune vérification n'a encore été exécutée.";
            return;
        }

        int errors = SystemStatus.Count(CheckSeverity.Error);
        int warnings = SystemStatus.Count(CheckSeverity.Warning);
        int snoozed = 0;
        foreach (CheckReport report in reports)
            if (report.Severity != CheckSeverity.Ok && CheckSnooze.IsSnoozed(report.Key)) snoozed++;

        _summary.text = (errors == 0 && warnings == 0
            ? $"{reports.Count} vérifications, aucun problème — dernière passe à {SystemStatus.LastRun:HH:mm}."
            : $"{errors} erreur(s), {warnings} avertissement(s) sur {reports.Count} vérifications — " +
              $"dernière passe à {SystemStatus.LastRun:HH:mm}.")
            + (snoozed > 0 ? $" Dont {snoozed} ignoré(s)." : "");

        AppendReports(reports);
    }

    /// <summary>Pendant une campagne ou une réparation, rien ne se relance : deux passes se disputeraient les rapports.</summary>
    private static bool Busy => SystemStatus.Running || RepairRunner.Busy;

    private static void RefreshActions()
    {
        if (_rerun != null)
        {
            _rerun.SetEnabled(!Busy);
            _rerun.text = SystemStatus.Running ? "Vérification en cours…" : "Relancer les vérifications";
        }

        if (_resolveAll == null) return;
        int repairable = RepairRunner.Repairable(OperatorReports()).Count;
        _resolveAll.style.display = repairable > 0 && !SystemStatus.Running && !_consoleTab
            ? DisplayStyle.Flex : DisplayStyle.None;
        _resolveAll.SetEnabled(!Busy);
        _resolveAll.text = RepairRunner.Busy ? "Résolution en cours…" : $"Tout résoudre ({repairable})";
    }

    /// <summary>Les rapports du bloc opérateur : le technique n'a pas de réparation à proposer.</summary>
    private static List<CheckReport> OperatorReports()
    {
        var reports = new List<CheckReport>();
        foreach (CheckReport report in SystemStatus.Reports)
            if (CheckCategory.IsOperatorFacing(report.Category)) reports.Add(report);
        return reports;
    }

    /// <summary>
    /// Deux publics : ce que l'opérateur peut corriger seul, en clair ; le technique, replié
    /// derrière une flèche avec son bilan chiffré. À égalité, les vingt lignes techniques noyaient
    /// les deux qui le concernent. Le classement vit dans <see cref="CheckCategory.IsOperatorFacing"/>.
    /// Côté opérateur, logiciels et matériel se replient aussi (<see cref="CollapsibleCategories"/>).
    /// </summary>
    private static void AppendReports(IReadOnlyList<CheckReport> reports)
    {
        var technical = new List<CheckReport>();
        var section = new List<CheckReport>();
        string category = null;

        void FlushSection()
        {
            if (section.Count == 0) return;
            if (CollapsibleCategories.Contains(category))
                // Ouvert d'office s'il y a quelque chose à corriger dedans, tant qu'on n'y a pas touché.
                _list.Add(CollapsibleBlock(category, section, nested: false,
                                           defaultExpanded: WorstOf(section) != CheckSeverity.Ok));
            else
                AppendGrouped(_list, section);
            section = new List<CheckReport>();
        }

        foreach (CheckReport report in reports)
        {
            if (!CheckCategory.IsOperatorFacing(report.Category)) { technical.Add(report); continue; }
            if (report.Category != category) { FlushSection(); category = report.Category; }
            section.Add(report);
        }
        FlushSection();

        // Le technique reste replié par défaut, même en erreur : c'est ce qu'on veut cacher.
        if (technical.Count > 0)
            _list.Add(CollapsibleBlock(TechnicalTitle, technical, nested: true, defaultExpanded: false));
    }

    private static void AppendGrouped(VisualElement container, List<CheckReport> reports)
    {
        string category = null;
        foreach (CheckReport report in reports)
        {
            if (report.Category != category)
            {
                category = report.Category;
                container.Add(CategoryHeader(category));
            }

            container.Add(Row(report));
        }
    }

    /// <summary>
    /// Bloc repliable : flèche, titre, bilan chiffré. <paramref name="nested"/> = le corps garde ses
    /// en-têtes de catégorie (bloc technique, qui en regroupe plusieurs).
    /// ⚠ Le dépliage ne reconstruit RIEN, il bascule l'affichage du corps : reconstruire retirerait
    /// de la hiérarchie le bouton survolé, qui n'émettrait jamais son PointerLeave — et sa bulle
    /// d'aide resterait affichée dans le vide.
    /// </summary>
    private static VisualElement CollapsibleBlock(string title, List<CheckReport> reports, bool nested,
                                                  bool defaultExpanded)
    {
        int ok = 0, warnings = 0, errors = 0;
        foreach (CheckReport report in reports)
        {
            switch (report.Severity)
            {
                case CheckSeverity.Error: errors++; break;
                case CheckSeverity.Warning: warnings++; break;
                default: ok++; break;
            }
        }
        CheckSeverity worst = WorstOf(reports);

        VisualElement block = new VisualElement();
        block.style.marginTop = nested ? 18 : 12;

        Button header = new Button();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.height = StyleKeyword.Auto;
        header.style.minHeight = 52;
        header.style.marginLeft = 0;
        header.style.marginRight = 0;
        header.style.marginBottom = 6;
        header.style.paddingLeft = 14;
        header.style.paddingRight = 16;
        header.style.backgroundColor = new Color(1f, 1f, 1f, 0.04f);
        header.style.borderTopWidth = 0;
        header.style.borderBottomWidth = 0;
        header.style.borderLeftWidth = 0;
        header.style.borderRightWidth = 0;
        SetRadius(header, 12);
        block.Add(header);

        // Replié ne doit pas vouloir dire invisible : la flèche prend la couleur du pire verdict.
        VisualElement arrow = new VisualElement();
        arrow.style.width = 20;
        arrow.style.height = 20;
        arrow.style.flexShrink = 0;
        arrow.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("Icons/arrow_back"));
        arrow.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        arrow.style.unityBackgroundImageTintColor = worst == CheckSeverity.Ok
            ? new Color(1f, 1f, 1f, 0.6f)
            : AccentOf(worst, false);
        arrow.pickingMode = PickingMode.Ignore;
        header.Add(arrow);

        Label label = new Label(title.ToUpperInvariant());
        label.style.fontSize = 14;
        label.style.letterSpacing = 2;
        label.style.color = new Color(1f, 1f, 1f, 0.55f);
        label.style.marginLeft = 12;
        label.style.flexGrow = 1;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.pickingMode = PickingMode.Ignore;
        header.Add(label);

        header.Add(Counter(CheckSeverity.Ok, ok));
        header.Add(Counter(CheckSeverity.Warning, warnings));
        header.Add(Counter(CheckSeverity.Error, errors));

        VisualElement body = new VisualElement();
        if (nested) AppendGrouped(body, reports);
        else foreach (CheckReport report in reports) body.Add(Row(report));
        block.Add(body);

        string lower = title.ToLowerInvariant();

        bool IsOpen() => _expanded.TryGetValue(title, out bool chosen) ? chosen : defaultExpanded;

        void Apply()
        {
            bool open = IsOpen();
            body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            // arrow_back pointe à gauche : 180° = vers la droite (replié), 270° = vers le bas (déplié).
            arrow.style.rotate = new StyleRotate(new Rotate(new Angle(open ? 270f : 180f, AngleUnit.Degree)));
            header.tooltip = open ? $"Masquer : {lower}" : $"Afficher : {lower}";
        }

        header.RegisterCallback<ClickEvent>(e =>
        {
            _expanded[title] = !IsOpen();
            Apply();
        });
        Apply();

        return block;
    }

    private static CheckSeverity WorstOf(List<CheckReport> reports)
    {
        CheckSeverity worst = CheckSeverity.Ok;
        foreach (CheckReport report in reports)
            if (report.Severity > worst) worst = report.Severity;
        return worst;
    }

    /// <summary>Pastille « icône + nombre » du bilan replié, grisée quand elle vaut zéro.</summary>
    private static VisualElement Counter(CheckSeverity severity, int count)
    {
        Color accent = AccentOf(severity, false);
        if (count == 0) accent = new Color(accent.r, accent.g, accent.b, 0.3f);

        VisualElement pill = new VisualElement();
        pill.style.flexDirection = FlexDirection.Row;
        pill.style.alignItems = Align.Center;
        pill.style.marginLeft = 16;
        pill.style.flexShrink = 0;
        pill.pickingMode = PickingMode.Ignore;

        VisualElement icon = new VisualElement();
        icon.style.width = 16;
        icon.style.height = 16;
        icon.style.marginRight = 6;
        icon.style.backgroundImage = new StyleBackground(IconOf(severity));
        icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        icon.style.unityBackgroundImageTintColor = accent;
        icon.pickingMode = PickingMode.Ignore;
        pill.Add(icon);

        Label number = new Label(count.ToString());
        number.style.fontSize = 17;
        number.style.unityFontStyleAndWeight = FontStyle.Bold;
        number.style.color = accent;
        number.pickingMode = PickingMode.Ignore;
        pill.Add(number);

        return pill;
    }

    /// <summary>
    /// Rotation image par image : `icon-status-loading` n'est qu'un anneau, rien ne l'anime tout
    /// seul — c'est déjà ainsi que la calibration fait tourner les siens.
    /// </summary>
    private static void SetSpinnerVisible(bool visible)
    {
        if (_spinner == null) return;

        _spinner.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        if (!visible) { _spinnerAnim?.Pause(); _spinnerAnim = null; return; }
        if (_spinnerAnim != null) return;

        _spinnerAnim = _spinner.schedule.Execute(() =>
        {
            if (_spinner == null) return;
            _spinnerAngle = (_spinnerAngle + 9f) % 360f;
            _spinner.style.rotate = new StyleRotate(new Rotate(new Angle(_spinnerAngle, AngleUnit.Degree)));
        }).Every(16);
    }

    private static VisualElement CategoryHeader(string category)
    {
        Label label = new Label(category.ToUpperInvariant());
        label.style.fontSize = 14;
        label.style.letterSpacing = 2;
        label.style.color = new Color(1f, 1f, 1f, 0.45f);
        label.style.marginTop = 16;
        label.style.marginBottom = 6;
        return label;
    }

    private static VisualElement Row(CheckReport report)
    {
        // Mis en sourdine : grisé, pour qu'on le voie sans qu'il crie.
        bool snoozed = report.Severity != CheckSeverity.Ok && CheckSnooze.IsSnoozed(report.Key);
        Color accent = snoozed
            ? new Color(0.55f, 0.55f, 0.58f)
            : AccentOf(report.Severity, report.Result != null && report.Result.Inconclusive);

        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.FlexStart;
        row.style.paddingTop = 12;
        row.style.paddingBottom = 12;
        row.style.paddingLeft = 14;
        row.style.paddingRight = 14;
        row.style.marginBottom = 6;
        row.style.backgroundColor = new Color(1f, 1f, 1f, 0.04f);
        SetRadius(row, 12);
        row.style.borderLeftWidth = 4;
        row.style.borderLeftColor = accent;

        VisualElement dot = new VisualElement();
        dot.style.width = 22;
        dot.style.height = 22;
        dot.style.marginRight = 12;
        dot.style.marginTop = 2;
        dot.style.flexShrink = 0;
        dot.style.backgroundImage = new StyleBackground(IconOf(report.Severity));
        dot.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        dot.style.unityBackgroundImageTintColor = accent;
        row.Add(dot);

        VisualElement text = new VisualElement();
        text.style.flexGrow = 1;
        text.style.flexShrink = 1;
        text.style.minWidth = 0;
        row.Add(text);

        Label name = new Label(report.Name);
        name.style.color = Color.white;
        name.style.fontSize = 18;
        name.style.unityFontStyleAndWeight = FontStyle.Bold;
        name.style.whiteSpace = WhiteSpace.Normal;
        text.Add(name);

        CheckResult result = report.Result;
        string detail = result != null ? result.Message : null;
        if (report.TimedOut) detail = (detail ?? "") + " (délai dépassé)";
        if (!string.IsNullOrEmpty(detail)) text.Add(Detail(detail, 0.7f));
        if (result != null && !string.IsNullOrEmpty(result.Fix)) text.Add(Detail(result.Fix, 0.5f));

        VisualElement actions = new VisualElement();
        actions.style.flexDirection = FlexDirection.Row;
        actions.style.flexWrap = Wrap.Wrap;
        actions.style.marginTop = 8;
        text.Add(actions);

        if (result != null && !string.IsNullOrEmpty(result.GuideId))
        {
            string guideId = result.GuideId;
            Button guide = RowButton("Voir la procédure", "Ouvrir la marche à suivre",
                                     new Color(accent.r, accent.g, accent.b, 0.28f));
            guide.RegisterCallback<ClickEvent>(e =>
            {
                Close();
                MooveoGameHooks.ShowGuide(guideId);
            });
            actions.Add(guide);
        }

        // « Résoudre » : côté opérateur seulement, et seulement s'il y a une réparation à tenter.
        if (result != null && result.CanRepair && report.Severity != CheckSeverity.Ok &&
            CheckCategory.IsOperatorFacing(report.Category))
        {
            bool repairing = RepairRunner.IsRepairing(report.Name);
            string tooltip = string.Join(", ", result.Repairs.ConvertAll(r => r.Label));
            Button resolve = RowButton(repairing ? "Résolution en cours…" : "Résoudre", tooltip,
                                       new Color(0.18f, 0.80f, 0.44f, 0.30f));
            resolve.SetEnabled(!Busy);
            CheckReport target = report;
            resolve.RegisterCallback<ClickEvent>(e => RepairRunner.Resolve(new[] { target }, _host));
            actions.Add(resolve);
        }

        // ⚠ Un problème ignoré RESTE listé : son échéance, et de quoi le réactiver. Disparu de partout,
        // une semaine de silence sur une imprimante en panne se découvrirait sur le papier du client.
        if (report.Severity != CheckSeverity.Ok)
        {
            string key = report.Key;
            if (snoozed)
            {
                string until = CheckSnooze.Describe(key);
                text.Add(Detail(char.ToUpperInvariant(until[0]) + until.Substring(1) + ".", 0.55f));

                Button wake = RowButton("Réactiver", "Signaler de nouveau ce problème", new Color(1f, 1f, 1f, 0.10f));
                wake.RegisterCallback<ClickEvent>(e => CheckSnooze.Clear(key));
                actions.Add(wake);
            }
            else
            {
                Button ignore = RowButton("Ignorer…", "Ne plus signaler ce problème pendant un temps",
                                          new Color(1f, 1f, 1f, 0.10f));
                string checkName = report.Name;
                ignore.RegisterCallback<ClickEvent>(e => AskSnooze(key, checkName));
                actions.Add(ignore);
            }
        }

        if (actions.childCount == 0) actions.RemoveFromHierarchy();

        return row;
    }

    /// <summary>« Ignorer… » d'une ligne : la durée se choisit dans une popup posée sur le même hôte.</summary>
    private static void AskSnooze(string key, string name)
    {
        if (_host == null) return;

        var options = new List<string>();
        foreach (KeyValuePair<string, TimeSpan?> choice in CheckSnooze.Choices) options.Add(choice.Key);

        ChoicePopup.Show(_host, $"Ignorer « {name} » pour combien de temps ?",
                         "Le problème ne sera plus signalé pendant la durée choisie. Il reste visible ici, grisé.",
                         options, "Annuler", picked =>
                         {
                             if (picked != RepairContext.Cancelled) CheckSnooze.Snooze(new[] { key }, picked);
                         });
    }

    private static Button RowButton(string label, string tooltip, Color background)
    {
        Button button = new Button { text = label, tooltip = tooltip };
        button.style.marginLeft = 0;
        button.style.marginRight = 10;
        button.style.marginTop = 0;
        button.style.height = 40;
        button.style.paddingLeft = 16;
        button.style.paddingRight = 16;
        button.style.fontSize = 15;
        button.style.color = Color.white;
        button.style.backgroundColor = background;
        button.style.borderTopWidth = 0;
        button.style.borderBottomWidth = 0;
        button.style.borderLeftWidth = 0;
        button.style.borderRightWidth = 0;
        SetRadius(button, 10);
        return button;
    }

    private static Label Detail(string content, float alpha)
    {
        Label label = new Label(content);
        label.style.color = new Color(1f, 1f, 1f, alpha);
        label.style.fontSize = 15;
        label.style.marginTop = 3;
        label.style.whiteSpace = WhiteSpace.Normal;
        return label;
    }

    // =========================================================================
    // Habillage
    // =========================================================================

    private static Color AccentOf(CheckSeverity severity, bool inconclusive)
    {
        if (inconclusive) return new Color(0.6f, 0.65f, 0.7f);
        switch (severity)
        {
            case CheckSeverity.Error: return new Color(0.91f, 0.30f, 0.24f);
            case CheckSeverity.Warning: return new Color(0.95f, 0.61f, 0.07f);
            default: return new Color(0.18f, 0.80f, 0.44f);
        }
    }

    private static Texture2D IconOf(CheckSeverity severity)
    {
        switch (severity)
        {
            case CheckSeverity.Error: return Resources.Load<Texture2D>("Icons/close");
            case CheckSeverity.Warning: return UIIcons.WarningTriangle;
            default: return Resources.Load<Texture2D>("Icons/check");
        }
    }

    private static void SetRadius(VisualElement element, float radius)
    {
        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
        element.style.borderBottomRightRadius = radius;
    }
}
