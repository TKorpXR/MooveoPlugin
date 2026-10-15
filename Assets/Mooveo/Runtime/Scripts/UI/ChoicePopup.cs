using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Popup de DÉCISION : un titre, un message, une liste de choix et une sortie neutre. Sert aux
/// réparations (« Nous avons trouvé : »), et affiche aussi une attente avec pastille tournante.
///
/// Construite en C# comme <see cref="GuidePopup"/>, pour la même raison : elle s'affiche sur la
/// GlobalWorldUI ET sur l'overlay de calibration. Même habillage, mêmes touches de manette :
/// POIGNÉE = sortie neutre, POUCE = premier choix (le plus probable, placé en tête exprès).
/// </summary>
public static class ChoicePopup
{
    private static VisualElement _overlay;
    private static VisualElement _card;
    private static Action<int> _onPick;
    private static Action _onStop;
    private static VisualElement _spinner;
    private static IVisualElementScheduledItem _spinnerAnim;
    private static float _spinnerAngle;
    private static bool _hasOptions;

    public static bool IsOpen => _overlay != null && _overlay.parent != null;

    /// <summary>Attente affichée (et non une décision).</summary>
    public static bool InProgress { get; private set; }

    /// <summary>Carte, pour que l'hôte world-space y cale son collider VR.</summary>
    public static VisualElement Card => IsOpen ? _card : null;

    /// <summary>Ouverture et fermeture : l'hôte recale son collider et efface sa bulle d'aide.</summary>
    public static event Action Changed;

    /// <summary>
    /// Décision. Chaque option : première ligne = libellé, suivantes = détail. Rend l'index choisi,
    /// ou <see cref="RepairContext.Cancelled"/> par la sortie neutre. <paramref name="options"/> null
    /// = simple message, la sortie neutre sert alors d'acquittement.
    /// </summary>
    public static void Show(VisualElement host, string title, string message, IList<string> options,
                            string cancelLabel, Action<int> onPick)
    {
        Build(host, title, message);
        if (_card == null) return;

        _onPick = onPick;
        _hasOptions = options != null && options.Count > 0;

        if (_hasOptions) BuildOptions(options);
        BuildFooter(cancelLabel ?? "Annuler", () => Pick(RepairContext.Cancelled));

        Changed?.Invoke();
    }

    /// <summary>Attente : pastille tournante, message, et bouton d'arrêt si <paramref name="onStop"/>.</summary>
    public static void ShowProgress(VisualElement host, string title, string message, string stopLabel, Action onStop)
    {
        Build(host, title, null);
        if (_card == null) return;

        InProgress = true;
        _onStop = onStop;

        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.justifyContent = Justify.Center;
        row.style.alignSelf = Align.Stretch;
        row.style.marginTop = 6;
        row.style.marginBottom = 6;
        _card.Add(row);

        _spinner = new VisualElement();
        _spinner.AddToClassList("icon-status-loading");
        _spinner.style.width = 26;
        _spinner.style.height = 26;
        _spinner.style.marginRight = 14;
        _spinner.style.flexShrink = 0;
        row.Add(_spinner);
        _spinnerAnim = _spinner.schedule.Execute(() =>
        {
            if (_spinner == null) return;
            _spinnerAngle = (_spinnerAngle + 9f) % 360f;
            _spinner.style.rotate = new StyleRotate(new Rotate(new Angle(_spinnerAngle, AngleUnit.Degree)));
        }).Every(16);

        Label text = Paragraph(message, 17, new Color(1f, 1f, 1f, 0.8f));
        text.name = "choice-popup-message";
        text.style.flexShrink = 1;
        row.Add(text);

        if (onStop != null) BuildFooter(stopLabel ?? "Arrêter", Stop);

        Changed?.Invoke();
    }

    /// <summary>Met à jour le message d'une attente en cours (progression d'une recherche).</summary>
    public static void SetProgressMessage(string message)
    {
        if (!InProgress || _card == null) return;
        Label text = _card.Q<Label>("choice-popup-message");
        if (text != null) text.text = message ?? "";
    }

    public static void Close()
    {
        bool wasOpen = IsOpen;
        if (wasOpen) _overlay.RemoveFromHierarchy();

        _spinnerAnim?.Pause();
        _spinnerAnim = null;
        _spinner = null;
        _overlay = null;
        _card = null;
        _onPick = null;
        _onStop = null;
        InProgress = false;
        _hasOptions = false;

        if (wasOpen) Changed?.Invoke();
    }

    /// <summary>Sortie neutre — aussi la POIGNÉE.</summary>
    public static void Cancel()
    {
        if (!IsOpen) return;
        if (InProgress) { if (_onStop != null) Stop(); return; }
        Pick(RepairContext.Cancelled);
    }

    /// <summary>Premier choix — aussi le POUCE. Sans choix (simple message), vaut acquittement.</summary>
    public static void ConfirmFirst()
    {
        if (!IsOpen || InProgress) return;
        Pick(_hasOptions ? 0 : RepairContext.Cancelled);
    }

    // =========================================================================
    // Construction
    // =========================================================================

    private static void Build(VisualElement host, string title, string message)
    {
        Close();
        if (host == null) return;

        _overlay = new VisualElement { name = "choice-popup-overlay" };
        _overlay.style.position = Position.Absolute;
        _overlay.style.left = 0;
        _overlay.style.top = 0;
        _overlay.style.right = 0;
        _overlay.style.bottom = 0;
        _overlay.style.alignItems = Align.Center;
        _overlay.style.justifyContent = Justify.Center;
        _overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
        // Modale : le voile absorbe les clics sans fermer — on sort par un bouton.
        _overlay.pickingMode = PickingMode.Position;

        // feature-window : tout l'intérieur vaut cible UI pour le curseur spray.
        _card = new VisualElement { name = "choice-popup-card" };
        _card.AddToClassList("glass-panel");
        _card.AddToClassList("feature-window");
        // ⚠ Taille RELATIVE à l'hôte : deux documents aux résolutions de référence différentes.
        _card.style.width = Length.Percent(46);
        _card.style.maxWidth = 760;
        _card.style.maxHeight = Length.Percent(90);
        _card.style.alignItems = Align.Center;
        _card.style.paddingLeft = 30;
        _card.style.paddingRight = 30;
        _card.style.paddingTop = 24;
        _card.style.paddingBottom = 22;
        _card.style.overflow = Overflow.Hidden;
        _card.style.backgroundColor = new Color(0.11f, 0.11f, 0.12f, 0.97f);
        _overlay.Add(_card);

        Label heading = new Label(title ?? "");
        heading.AddToClassList("header-text");
        heading.style.fontSize = 24;
        heading.style.letterSpacing = 0;
        heading.style.marginBottom = 0;
        heading.style.whiteSpace = WhiteSpace.Normal;
        heading.style.unityTextAlign = TextAnchor.MiddleCenter;
        heading.style.alignSelf = Align.Stretch;
        heading.style.flexShrink = 0;
        _card.Add(heading);

        VisualElement rule = new VisualElement();
        rule.style.alignSelf = Align.Stretch;
        rule.style.height = 1;
        rule.style.flexShrink = 0;
        rule.style.marginTop = 12;
        rule.style.marginBottom = 14;
        rule.style.backgroundColor = new Color(1f, 1f, 1f, 0.14f);
        _card.Add(rule);

        if (!string.IsNullOrEmpty(message))
        {
            Label text = Paragraph(message, 17, new Color(1f, 1f, 1f, 0.8f));
            text.name = "choice-popup-message";
            text.style.marginBottom = 14;
            _card.Add(text);
        }

        host.Add(_overlay);
        _overlay.BringToFront();

        GuidePopupInput.Ensure();
    }

    private static void BuildOptions(IList<string> options)
    {
        ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.AddToClassList("slim-scroll");
        scroll.style.alignSelf = Align.Stretch;
        scroll.style.flexShrink = 1;
        // ⚠ min-height: 0, sinon une longue liste ne se réduit pas et DÉBORDE sur les boutons.
        scroll.style.minHeight = 0;
        scroll.AddManipulator(new TouchScrollManipulator(scroll));
        _card.Add(scroll);

        for (int i = 0; i < options.Count; i++)
            scroll.Add(OptionButton(options[i], i));
    }

    private static Button OptionButton(string option, int index)
    {
        string[] lines = (option ?? "").Split(new[] { '\n' }, 2);
        bool first = index == 0;

        Button button = new Button { tooltip = lines[0] };
        button.style.flexDirection = FlexDirection.Row;
        button.style.alignItems = Align.Center;
        button.style.height = StyleKeyword.Auto;
        button.style.minHeight = 56;
        button.style.marginLeft = 0;
        button.style.marginRight = 0;
        button.style.marginBottom = 8;
        button.style.paddingTop = 10;
        button.style.paddingBottom = 10;
        button.style.paddingLeft = 18;
        button.style.paddingRight = 18;
        button.style.borderTopWidth = 0;
        button.style.borderBottomWidth = 0;
        button.style.borderLeftWidth = 0;
        button.style.borderRightWidth = 0;
        // Le premier est le choix PROBABLE (celui du pouce) : mis en avant sans être criard.
        button.style.backgroundColor = first
            ? new Color(0.18f, 0.80f, 0.44f, 0.22f)
            : new Color(1f, 1f, 1f, 0.06f);
        SetRadius(button, 12);

        VisualElement text = new VisualElement { pickingMode = PickingMode.Ignore };
        text.style.flexGrow = 1;
        text.style.flexShrink = 1;
        text.style.minWidth = 0;
        button.Add(text);

        Label main = new Label(lines[0]) { pickingMode = PickingMode.Ignore };
        main.style.fontSize = 18;
        main.style.color = Color.white;
        main.style.unityFontStyleAndWeight = FontStyle.Bold;
        main.style.whiteSpace = WhiteSpace.Normal;
        main.style.unityTextAlign = TextAnchor.MiddleLeft;
        text.Add(main);

        if (lines.Length > 1 && !string.IsNullOrEmpty(lines[1]))
        {
            Label detail = new Label(lines[1]) { pickingMode = PickingMode.Ignore };
            detail.style.fontSize = 14;
            detail.style.marginTop = 3;
            detail.style.color = new Color(1f, 1f, 1f, 0.6f);
            detail.style.whiteSpace = WhiteSpace.Normal;
            detail.style.unityTextAlign = TextAnchor.MiddleLeft;
            text.Add(detail);
        }

        if (first)
        {
            Label hint = new Label($"({GuidePopup.ConfirmKeyName})") { pickingMode = PickingMode.Ignore };
            hint.style.fontSize = 14;
            hint.style.marginLeft = 12;
            hint.style.flexShrink = 0;
            hint.style.color = new Color(1f, 1f, 1f, 0.6f);
            button.Add(hint);
        }

        // ⚠ ClickEvent, jamais Button.clicked : le curseur spray n'émet pas la paire attendue.
        button.RegisterCallback<ClickEvent>(e => Pick(index));
        return button;
    }

    private static void BuildFooter(string label, Action action)
    {
        Button button = new Button { tooltip = label };
        button.AddToClassList("control-button-small");
        button.style.flexDirection = FlexDirection.Column;
        button.style.alignItems = Align.Center;
        button.style.justifyContent = Justify.Center;
        // ⚠ .control-button-small impose width: 40px : sans libération le libellé déborde du fond.
        button.style.width = StyleKeyword.Auto;
        button.style.minWidth = 190;
        button.style.height = 58;
        button.style.marginTop = 10;
        button.style.marginLeft = 0;
        button.style.marginRight = 0;
        button.style.paddingLeft = 30;
        button.style.paddingRight = 30;
        button.style.flexShrink = 0;
        SetRadius(button, 14);

        Label main = new Label(label) { pickingMode = PickingMode.Ignore };
        main.style.fontSize = 19;
        main.style.unityFontStyleAndWeight = FontStyle.Bold;
        main.style.color = Color.white;
        button.Add(main);

        Label hint = new Label($"({GuidePopup.DismissKeyName})") { pickingMode = PickingMode.Ignore };
        hint.style.fontSize = 13;
        hint.style.marginTop = 2;
        hint.style.color = new Color(1f, 1f, 1f, 0.6f);
        button.Add(hint);

        button.RegisterCallback<ClickEvent>(e => action());
        _card.Add(button);
    }

    // =========================================================================
    // Réponses
    // =========================================================================

    private static void Pick(int index)
    {
        Action<int> callback = _onPick;
        // ⚠ Deux chemins d'entrée (manette interceptée et écoute directe) peuvent arriver sur le même
        // appui : on ferme AVANT de rappeler, le second trouve la popup fermée et ne fait rien.
        Close();
        callback?.Invoke(index);
    }

    private static void Stop()
    {
        Action stop = _onStop;
        _onStop = null;
        stop?.Invoke();
    }

    private static Label Paragraph(string content, int size, Color color)
    {
        Label label = new Label(content ?? "");
        label.style.fontSize = size;
        label.style.color = color;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.alignSelf = Align.Stretch;
        label.style.flexShrink = 0;
        return label;
    }

    private static void SetRadius(VisualElement element, float radius)
    {
        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
        element.style.borderBottomRightRadius = radius;
    }
}

/// <summary>
/// Aiguillage des touches de manette vers la modale AU PREMIER PLAN. Une décision de réparation
/// s'ouvre par-dessus le bilan de calibration : c'est elle qui doit recevoir la poignée et le pouce,
/// pas la popup qu'elle recouvre.
/// </summary>
public static class ModalPopups
{
    public static bool IsOpen => ChoicePopup.IsOpen || GuidePopup.IsOpen || GraffWall.Remote.PairingManager.IsPairingOpen;

    public static void Dismiss()
    {
        if (ChoicePopup.IsOpen) ChoicePopup.Cancel();
        else if (GraffWall.Remote.PairingManager.IsPairingOpen) GraffWall.Remote.PairingManager.ClosePairing();
        else if (GuidePopup.IsOpen) GuidePopup.Dismiss();
    }

    public static void Confirm()
    {
        if (ChoicePopup.IsOpen) ChoicePopup.ConfirmFirst();
        else if (GraffWall.Remote.PairingManager.IsPairingOpen) GraffWall.Remote.PairingManager.CopyPairingCode();
        else if (GuidePopup.IsOpen) GuidePopup.Confirm();
    }
}
