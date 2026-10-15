using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Onglet Console du panneau d'état : liste façon console Unity, filtrable (erreurs, tous les logs, résumé de session), la plus récente en bas, mise à jour en direct.</summary>
public static class SystemStatusConsole
{
    private const int MaxRows = 300;
    private const long PollMilliseconds = 400;
    private const float BottomTolerance = 24f;

    private static readonly List<ErrorLogWriter.Entry> Entries = new List<ErrorLogWriter.Entry>();
    private static readonly Dictionary<long, Label> Counters = new Dictionary<long, Label>();

    private static VisualElement _root;
    private static ScrollView _scroll;
    private static Label _hint;
    private static Label _empty;
    private static Button _filterErrors;
    private static Button _filterAll;
    private static Button _filterSummary;
    private static ErrorLogWriter.LogView _view = ErrorLogWriter.LogView.Errors;
    private static IVisualElementScheduledItem _poll;
    private static int _shownVersion;
    private static long _lastShownId;
    private static int _rowCount;

    /// <summary>Construit la vue (info, liste défilante) ; l'appelant l'ajoute à la carte.</summary>
    public static VisualElement Build()
    {
        Counters.Clear();
        _shownVersion = -1;
        _lastShownId = 0;
        _rowCount = 0;
        _view = ErrorLogWriter.LogView.Errors;

        _root = new VisualElement { name = "system-status-console" };
        _root.style.flexGrow = 1;
        _root.style.flexShrink = 1;
        _root.style.minHeight = 0;

        _hint = new Label();
        _hint.style.fontSize = 13;
        _hint.style.color = new Color(1f, 1f, 1f, 0.5f);
        _hint.style.whiteSpace = WhiteSpace.Normal;
        _hint.style.marginBottom = 8;
        _hint.style.flexShrink = 0;
        _root.Add(_hint);
        BuildFilters(_root);

        _empty = new Label();
        _empty.style.fontSize = 16;
        _empty.style.color = new Color(1f, 1f, 1f, 0.5f);
        _empty.style.unityTextAlign = TextAnchor.MiddleCenter;
        _empty.style.marginTop = 24;
        _empty.style.flexShrink = 0;
        _root.Add(_empty);

        _scroll = new ScrollView(ScrollViewMode.Vertical);
        _scroll.style.flexGrow = 1;
        _scroll.style.flexShrink = 1;
        _scroll.style.minHeight = 0;
        _scroll.AddToClassList("slim-scroll");
        _scroll.AddManipulator(new TouchScrollManipulator(_scroll));
        _root.Add(_scroll);
        StyleFilters();

        return _root;
    }

    /// <summary>Affiche l'état courant puis le tient à jour.</summary>
    public static void Start()
    {
        if (_root == null) return;
        Poll();
        _poll?.Pause();
        _poll = _root.schedule.Execute(Poll).Every(PollMilliseconds);
    }

    /// <summary>Arrête la mise à jour en direct.</summary>
    public static void Stop()
    {
        _poll?.Pause();
        _poll = null;
    }

    /// <summary>Libère la vue à la fermeture du panneau.</summary>
    public static void Release()
    {
        Stop();
        Counters.Clear();
        _root = null;
        _scroll = null;
        _hint = null;
        _empty = null;
        _filterErrors = null;
        _filterAll = null;
        _filterSummary = null;
    }

    /// <summary>Ajoute les nouvelles erreurs en bas, met à jour les compteurs et suit le bas de la liste.</summary>
    private static void Poll()
    {
        if (_root == null) return;

        string hint = ErrorLogWriter.IsWritingFile
            ? "Journaux : " + ErrorLogWriter.LogsDirectory
            : "Erreurs en mémoire seulement (fichiers désactivés).";
        if (_hint.text != hint) _hint.text = hint;

        int version = ErrorLogWriter.Version;
        if (version == _shownVersion) return;
        _shownVersion = version;

        bool followBottom = IsAtBottom();
        VisualElement lastAdded = null;

        ErrorLogWriter.Read(Entries, _view);
        foreach (ErrorLogWriter.Entry entry in Entries)
        {
            if (entry.Id > _lastShownId)
            {
                lastAdded = AddRow(entry);
                _lastShownId = entry.Id;
            }
            else if (Counters.TryGetValue(entry.Id, out Label counter))
            {
                SetCount(counter, entry.Count);
            }
        }

        TrimOldest(followBottom);
        _empty.text = _view == ErrorLogWriter.LogView.Errors ? "Aucune erreur depuis le lancement."
            : _view == ErrorLogWriter.LogView.All ? "Aucun log depuis le lancement."
            : "Aucun événement depuis le lancement.";
        _empty.style.display = _rowCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;

        if (followBottom && lastAdded != null) ScrollToBottomWhenLaidOut(lastAdded);
    }

    /// <summary>Boutons de filtre : erreurs seules, ou tous les logs.</summary>
    private static void BuildFilters(VisualElement parent)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 8;
        row.style.flexShrink = 0;
        parent.Add(row);

        _filterErrors = FilterButton("Erreurs", "Afficher les erreurs seules");
        _filterErrors.RegisterCallback<ClickEvent>(e => SelectFilter(ErrorLogWriter.LogView.Errors));
        row.Add(_filterErrors);

        _filterAll = FilterButton("Tout", "Afficher tous les logs : messages, avertissements et erreurs");
        _filterAll.RegisterCallback<ClickEvent>(e => SelectFilter(ErrorLogWriter.LogView.All));
        row.Add(_filterAll);

        _filterSummary = FilterButton("Résumé", "Déroulé de la session : modes, clics, outils, notifications");
        _filterSummary.RegisterCallback<ClickEvent>(e => SelectFilter(ErrorLogWriter.LogView.Summary));
        row.Add(_filterSummary);
    }

    private static Button FilterButton(string label, string tooltip)
    {
        Button button = new Button { text = label, tooltip = tooltip };
        button.AddToClassList("control-button-small");
        button.style.width = StyleKeyword.Auto;
        button.style.minWidth = 110;
        button.style.height = 34;
        button.style.fontSize = 15;
        button.style.paddingLeft = 16;
        button.style.paddingRight = 16;
        button.style.marginLeft = 0;
        button.style.marginRight = 8;
        button.style.color = Color.white;
        SetRadius(button, 10);
        return button;
    }

    /// <summary>Change de filtre et repart d'une liste vide, remplie par la relève suivante.</summary>
    private static void SelectFilter(ErrorLogWriter.LogView view)
    {
        if (_scroll == null || _view == view) return;
        _view = view;

        _scroll.contentContainer.Clear();
        Counters.Clear();
        _rowCount = 0;
        _lastShownId = 0;
        _shownVersion = -1;
        _scroll.scrollOffset = Vector2.zero;

        StyleFilters();
        Poll();
    }

    private static void StyleFilters()
    {
        if (_filterErrors == null || _filterAll == null || _filterSummary == null) return;
        _filterErrors.style.backgroundColor = new Color(1f, 1f, 1f, _view == ErrorLogWriter.LogView.Errors ? 0.22f : 0.06f);
        _filterAll.style.backgroundColor = new Color(1f, 1f, 1f, _view == ErrorLogWriter.LogView.All ? 0.22f : 0.06f);
        _filterSummary.style.backgroundColor = new Color(1f, 1f, 1f, _view == ErrorLogWriter.LogView.Summary ? 0.22f : 0.06f);
    }

    /// <summary>Vrai si la liste est tout en bas (ou vide) : c'est alors qu'elle suit les nouvelles erreurs.</summary>
    private static bool IsAtBottom()
    {
        return _scroll.verticalScroller.highValue - _scroll.scrollOffset.y <= BottomTolerance;
    }

    /// <summary>Supprime les plus anciennes lignes au-delà du maximum, sans décaler ce qu'on lit.</summary>
    private static void TrimOldest(bool followBottom)
    {
        VisualElement content = _scroll.contentContainer;
        while (_rowCount > MaxRows)
        {
            VisualElement oldest = content[0];
            if (oldest.userData is long id) Counters.Remove(id);

            if (!followBottom)
            {
                float height = oldest.layout.height;
                if (!float.IsNaN(height))
                    _scroll.scrollOffset = new Vector2(0f, Mathf.Max(0f, _scroll.scrollOffset.y - height));
            }

            oldest.RemoveFromHierarchy();
            _rowCount--;
        }
    }

    /// <summary>Descend en bas une fois la dernière ligne mise en page (sa hauteur n'est connue qu'alors).</summary>
    private static void ScrollToBottomWhenLaidOut(VisualElement row)
    {
        EventCallback<GeometryChangedEvent> scroll = null;
        scroll = evt =>
        {
            row.UnregisterCallback(scroll);
            if (_scroll != null) _scroll.ScrollTo(row);
        };
        row.RegisterCallback(scroll);
    }

    /// <summary>Ajoute une ligne en bas : heure, niveau, première ligne du message, puis la trace en plus discret.</summary>
    private static VisualElement AddRow(ErrorLogWriter.Entry entry)
    {
        Color accent = AccentOf(entry.Level);
        string[] messageLines = entry.Message.Replace("\r\n", "\n").Split('\n');
        string firstLine = messageLines[0];
        string subtitle = SubtitleOf(entry, messageLines);

        VisualElement row = new VisualElement { userData = entry.Id };
        row.style.flexShrink = 0;
        row.style.paddingLeft = 10;
        row.style.paddingRight = 10;
        row.style.paddingTop = 5;
        row.style.paddingBottom = 5;
        row.style.backgroundColor = new Color(1f, 1f, 1f, _rowCount % 2 == 0 ? 0.03f : 0.07f);
        row.style.borderLeftWidth = 3;
        row.style.borderLeftColor = accent;

        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.FlexStart;
        row.Add(header);

        header.Add(Text("[" + entry.Time.ToString("HH:mm:ss") + "]", 14, new Color(1f, 1f, 1f, 0.5f), 8));
        Label level = Text(entry.Level, 13, accent, 8);
        level.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.Add(level);

        Label message = Text(firstLine, 15, new Color(1f, 1f, 1f, 0.92f), 0);
        message.style.whiteSpace = WhiteSpace.Normal;
        message.style.flexShrink = 1;
        message.style.flexGrow = 1;
        header.Add(message);

        Label counter = Text("", 13, Color.white, 0);
        counter.style.marginLeft = 8;
        counter.style.paddingLeft = 8;
        counter.style.paddingRight = 8;
        counter.style.backgroundColor = new Color(1f, 1f, 1f, 0.22f);
        SetRadius(counter, 10);
        SetCount(counter, entry.Count);
        header.Add(counter);
        Counters[entry.Id] = counter;

        if (!string.IsNullOrEmpty(subtitle))
        {
            Label sub = Text(subtitle, 13, new Color(1f, 1f, 1f, 0.5f), 0);
            sub.style.whiteSpace = WhiteSpace.Normal;
            row.Add(sub);
        }

        string details = DetailsOf(entry, messageLines);
        if (!string.IsNullOrEmpty(details))
        {
            Label detail = Text(details, 13, new Color(1f, 1f, 1f, 0.6f), 0);
            detail.style.whiteSpace = WhiteSpace.Normal;
            detail.style.marginTop = 4;
            detail.style.display = DisplayStyle.None;
            row.Add(detail);

            row.RegisterCallback<ClickEvent>(_ =>
                detail.style.display = detail.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None);
        }

        _scroll.contentContainer.Add(row);
        _rowCount++;
        return row;
    }

    /// <summary>Ligne discrète sous le message : première ligne de la trace, sinon deuxième ligne du message.</summary>
    private static string SubtitleOf(ErrorLogWriter.Entry entry, string[] messageLines)
    {
        if (!string.IsNullOrWhiteSpace(entry.StackTrace))
        {
            foreach (string line in entry.StackTrace.Replace("\r\n", "\n").Split('\n'))
                if (!string.IsNullOrWhiteSpace(line)) return line.Trim();
        }
        return messageLines.Length > 1 ? messageLines[1].Trim() : "";
    }

    /// <summary>Texte déplié au toucher : suite du message puis trace complète.</summary>
    private static string DetailsOf(ErrorLogWriter.Entry entry, string[] messageLines)
    {
        string rest = messageLines.Length > 1 ? string.Join("\n", messageLines, 1, messageLines.Length - 1).TrimEnd() : "";
        string stack = string.IsNullOrWhiteSpace(entry.StackTrace) ? "" : entry.StackTrace.TrimEnd();
        return rest.Length > 0 && stack.Length > 0 ? rest + "\n\n" + stack : rest + stack;
    }

    private static void SetCount(Label counter, int count)
    {
        counter.text = count > 1 ? count.ToString() : "";
        counter.style.display = count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static Label Text(string content, int size, Color color, float marginRight)
    {
        Label label = new Label(content);
        label.style.fontSize = size;
        label.style.color = color;
        label.style.marginRight = marginRight;
        label.style.marginLeft = 0;
        label.style.marginTop = 0;
        label.style.marginBottom = 0;
        label.style.paddingLeft = 0;
        label.style.paddingRight = 0;
        return label;
    }

    private static Color AccentOf(string level)
    {
        switch (level)
        {
            case "LOG": return new Color(0.6f, 0.65f, 0.7f);
            case "SESSION": return new Color(0.35f, 0.65f, 0.95f);
            case "MODE": return new Color(0.65f, 0.5f, 0.95f);
            case "CLIC": return new Color(0.4f, 0.8f, 0.85f);
            case "OUTIL": return new Color(0.4f, 0.85f, 0.55f);
            case "NOTIF": return new Color(0.95f, 0.61f, 0.07f);
            case "IMPRESSION":
            case "SAUVEGARDE": return new Color(0.95f, 0.5f, 0.75f);
            case "WARNING": return new Color(0.95f, 0.80f, 0.20f);
            case "ASSERT":
            case "TASK": return new Color(0.95f, 0.61f, 0.07f);
            default: return new Color(0.91f, 0.30f, 0.24f);
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
