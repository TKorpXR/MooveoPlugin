using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mooveo.Positioning
{
    /// <summary>
    /// Interface de l'étape de positionnement (UI Toolkit) : l'image de <see cref="PlayAreaPreview"/>, l'étiquette de chaque
    /// joueur posée au-dessus de son appareil, l'étiquette de l'écran, et une carte d'état par joueur (consigne, refus,
    /// jauge de validation automatique, détail fourni par le jeu). Toute l'apparence passe par des classes USS :
    /// <c>.mooveo-positions</c>, <c>__view</c>, <c>__overlay</c>, <c>__wall-label</c>, <c>__tag</c>, <c>__cards</c>,
    /// <c>__card</c> (+ <c>--locked</c>, <c>--refused</c>, <c>--auto</c>, <c>--untracked</c>), <c>__card-name</c>,
    /// <c>__card-status</c>, <c>__card-detail</c>, <c>__gauge</c>, <c>__gauge-fill</c>. Seule la couleur de chaque joueur est
    /// posée en ligne (fond de son étiquette et de son nom).
    /// </summary>
    public class PlayAreaPreviewElement : VisualElement
    {
        public const string UssClass = "mooveo-positions";
        public const string ViewClass = UssClass + "__view";
        public const string OverlayClass = UssClass + "__overlay";
        public const string WallLabelClass = UssClass + "__wall-label";
        public const string TagClass = UssClass + "__tag";
        public const string CardsClass = UssClass + "__cards";
        public const string CardClass = UssClass + "__card";
        public const string CardNameClass = UssClass + "__card-name";
        public const string CardStatusClass = UssClass + "__card-status";
        public const string CardDetailClass = UssClass + "__card-detail";
        public const string GaugeClass = UssClass + "__gauge";
        public const string GaugeFillClass = UssClass + "__gauge-fill";
        public const string LockedModifier = "--locked";
        public const string RefusedModifier = "--refused";
        public const string AutoModifier = "--auto";
        public const string UntrackedModifier = "--untracked";

        /// <summary>Feuille de style par défaut du module (Resources).</summary>
        public const string DefaultStyleSheetPath = "MooveoPositioning/PlayAreaPreview";

        private sealed class Row
        {
            public Participant Participant;
            public VisualElement Card, Gauge, GaugeFill;
            public Label Tag, Name, Status, Detail;
        }

        private readonly VisualElement _view, _overlay, _cards;
        private readonly Label _wallLabel;
        private readonly List<Row> _rows = new List<Row>();
        private PlayAreaPreview _preview;
        private IVisualElementScheduledItem _refresh;

        /// <summary>Détail affiché sous l'état d'un joueur (ex. « rame à gauche ») ; null ou vide = rien.</summary>
        public Func<Participant, string> DetailProvider { get; set; }

        /// <summary>Image de l'aperçu (pour la dimensionner depuis le jeu si besoin).</summary>
        public VisualElement View => _view;
        public VisualElement Cards => _cards;

        public PlayAreaPreviewElement()
        {
            AddToClassList(UssClass);
            pickingMode = PickingMode.Ignore;

            _view = new VisualElement { pickingMode = PickingMode.Ignore };
            _view.AddToClassList(ViewClass);
            Add(_view);

            _overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            _overlay.AddToClassList(OverlayClass);
            _view.Add(_overlay);

            _wallLabel = new Label { pickingMode = PickingMode.Ignore };
            _wallLabel.AddToClassList(WallLabelClass);
            _overlay.Add(_wallLabel);

            _cards = new VisualElement { pickingMode = PickingMode.Ignore };
            _cards.AddToClassList(CardsClass);
            Add(_cards);

            // Hauteur de l'image = largeur × proportions de la texture.
            _view.RegisterCallback<GeometryChangedEvent>(_ => FitHeight());
            RegisterCallback<AttachToPanelEvent>(_ => _refresh = schedule.Execute(Refresh).Every(0));
            RegisterCallback<DetachFromPanelEvent>(_ => _refresh?.Pause());
        }

        public PlayAreaPreviewElement(PlayAreaPreview preview) : this() => SetPreview(preview);

        /// <summary>Aperçu affiché ; charge les feuilles de style de son style.</summary>
        public void SetPreview(PlayAreaPreview preview)
        {
            _preview = preview;
            if (preview == null) return;
            _view.style.backgroundImage = Background.FromRenderTexture(preview.Texture);
            PlayAreaPreviewStyle style = preview.Style;
            if (!style.ReplaceDefaultStyleSheet)
            {
                var sheet = Resources.Load<StyleSheet>(DefaultStyleSheetPath);
                if (sheet != null && !styleSheets.Contains(sheet)) styleSheets.Add(sheet);
            }
            if (style.StyleSheet != null && !styleSheets.Contains(style.StyleSheet)) styleSheets.Add(style.StyleSheet);
            _wallLabel.text = style.WallText;
            FitHeight();
        }

        private void FitHeight()
        {
            if (_preview == null || _preview.Texture == null) return;
            float width = _view.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0f) return;
            float height = width * _preview.Texture.height / _preview.Texture.width;
            if (Mathf.Abs(_view.resolvedStyle.height - height) > 0.5f) _view.style.height = height;
        }

        /// <summary>Met à jour étiquettes et cartes (appelé tout seul à chaque image tant que l'élément est affiché).</summary>
        public void Refresh()
        {
            PositionSession session = _preview != null ? _preview.Session : null;
            IReadOnlyList<Participant> participants = session != null ? session.Participants : Array.Empty<Participant>();
            while (_rows.Count < participants.Count) _rows.Add(NewRow());
            while (_rows.Count > participants.Count)
            {
                Row last = _rows[_rows.Count - 1];
                last.Tag.RemoveFromHierarchy();
                last.Card.RemoveFromHierarchy();
                _rows.RemoveAt(_rows.Count - 1);
            }

            float width = _view.resolvedStyle.width, height = _view.resolvedStyle.height;
            bool laidOut = !float.IsNaN(width) && width > 0f && !float.IsNaN(height) && height > 0f;
            if (laidOut && _preview != null)
            {
                Vector2 wall = _preview.WallLabelPoint();
                _wallLabel.style.left = wall.x * width;
                _wallLabel.style.top = (1f - wall.y) * height;
            }

            PlayAreaPreviewStyle style = _preview != null ? _preview.Style : null;
            for (int i = 0; i < participants.Count; i++)
            {
                Participant p = participants[i];
                Row row = _rows[i];
                row.Participant = p;
                PositionCapture capture = p.Capture;
                CaptureState state = capture.State;
                float auto = capture.AutoLockProgress;

                row.Tag.text = p.Label;
                row.Tag.style.backgroundColor = p.Color;
                row.Name.text = p.Label;
                row.Name.style.backgroundColor = p.Color;

                Vector2 tag = default;
                bool tagVisible = laidOut && _preview.TryGetTagPoint(p, out tag);
                row.Tag.style.display = tagVisible ? DisplayStyle.Flex : DisplayStyle.None;
                if (tagVisible)
                {
                    row.Tag.style.left = tag.x * width;
                    row.Tag.style.top = (1f - tag.y) * height;
                }

                bool untracked = !capture.IsLocked && !capture.IsTracked;
                Toggle(row, LockedModifier, state == CaptureState.Locked);
                Toggle(row, RefusedModifier, state == CaptureState.Refused);
                Toggle(row, AutoModifier, auto > 0f);
                Toggle(row, UntrackedModifier, untracked);

                if (style != null)
                    row.Status.text = state == CaptureState.Locked ? style.LockedText
                        : state == CaptureState.Refused ? style.RefusedText
                        : untracked ? style.UntrackedText
                        : auto > 0f ? style.AutoLockText
                        : style.FreeText;
                row.GaugeFill.style.width = Length.Percent(auto * 100f);

                string detail = DetailProvider?.Invoke(p);
                row.Detail.text = detail ?? string.Empty;
                row.Detail.style.display = string.IsNullOrEmpty(detail) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        private static void Toggle(Row row, string modifier, bool on)
        {
            row.Card.EnableInClassList(CardClass + modifier, on);
            row.Tag.EnableInClassList(TagClass + modifier, on);
        }

        private Row NewRow()
        {
            var row = new Row
            {
                Tag = new Label { pickingMode = PickingMode.Ignore },
                Card = new VisualElement { pickingMode = PickingMode.Ignore },
                Name = new Label { pickingMode = PickingMode.Ignore },
                Status = new Label { pickingMode = PickingMode.Ignore },
                Detail = new Label { pickingMode = PickingMode.Ignore },
                Gauge = new VisualElement { pickingMode = PickingMode.Ignore },
                GaugeFill = new VisualElement { pickingMode = PickingMode.Ignore },
            };
            row.Tag.AddToClassList(TagClass);
            row.Card.AddToClassList(CardClass);
            row.Name.AddToClassList(CardNameClass);
            row.Status.AddToClassList(CardStatusClass);
            row.Detail.AddToClassList(CardDetailClass);
            row.Gauge.AddToClassList(GaugeClass);
            row.GaugeFill.AddToClassList(GaugeFillClass);
            row.Gauge.Add(row.GaugeFill);
            row.Card.Add(row.Name);
            row.Card.Add(row.Status);
            row.Card.Add(row.Gauge);
            row.Card.Add(row.Detail);
            _overlay.Add(row.Tag);
            _cards.Add(row.Card);
            return row;
        }
    }
}
