using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mooveo.Positioning
{
    /// <summary>
    /// Écran de positionnement prêt à l'emploi : un aperçu (<see cref="PlayAreaPreview"/>), son interface
    /// (<see cref="Element"/>, à placer où l'on veut dans l'UI du jeu) et, à chaque ouverture, une nouvelle
    /// <see cref="PositionSession"/> que le jeu remplit de participants. Avancé en temps réel (marche aussi jeu en pause) ;
    /// à la fermeture, les sources jetables (<see cref="IDisposable"/>) sont libérées et l'aperçu ne rend plus rien.
    /// <code>
    /// var flow = PositionFlow.Create(transform, style, settings);
    /// panel.Add(flow.Element);
    /// PositionSession session = flow.Open(PlayAreaSpace.From(playArea));
    /// session.Add(slot, "J1", color, new TrackedPointerSource(pose, reader));
    /// flow.Ready += () => goButton.SetEnabled(true);
    /// // … flow.Close() puis lire session.Snapshots().
    /// </code>
    /// </summary>
    public class PositionFlow : MonoBehaviour
    {
        private PlayAreaPreviewStyle _style;
        private CaptureSettings _settings;
        private PlayAreaPreview _preview;

        /// <summary>Interface de l'écran (créée une fois, réutilisée d'une ouverture à l'autre).</summary>
        public PlayAreaPreviewElement Element { get; private set; }
        public PlayAreaPreview Preview => _preview;
        /// <summary>Session en cours (ou la dernière, après fermeture).</summary>
        public PositionSession Session { get; private set; }
        public bool IsOpen { get; private set; }
        public CaptureSettings Settings => _settings;

        /// <summary>Un participant de la session ouverte a changé d'état.</summary>
        public event Action<Participant, CaptureState> StateChanged;
        /// <summary>Tous les participants de la session ouverte ont validé (après le délai des réglages).</summary>
        public event Action Ready;

        /// <summary>Crée l'écran sous parent ; style et réglages null = valeurs par défaut.</summary>
        public static PositionFlow Create(Transform parent, PlayAreaPreviewStyle style = null, CaptureSettings settings = null)
        {
            var go = new GameObject("PositionFlow");
            go.transform.SetParent(parent, false);
            var flow = go.AddComponent<PositionFlow>();
            flow._style = style;
            flow._settings = settings != null ? settings : CaptureSettings.CreateDefault();
            flow._preview = PlayAreaPreview.Create(style, go.transform);
            flow._preview.gameObject.SetActive(false);
            flow.Element = new PlayAreaPreviewElement(flow._preview);
            return flow;
        }

        /// <summary>Ouvre une nouvelle session (la précédente est fermée) ; à remplir de participants.</summary>
        public PositionSession Open(PlayAreaSpace space)
        {
            Close();
            Session = new PositionSession(space, _settings);
            Session.StateChanged += OnStateChanged;
            Session.Ready += OnReady;
            _preview.gameObject.SetActive(true);
            _preview.Bind(Session);
            IsOpen = true;
            return Session;
        }

        /// <summary>Ferme la session : plus d'échantillonnage ni de rendu, sources jetables libérées.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _preview.Bind(null);
            _preview.gameObject.SetActive(false);
            Session.StateChanged -= OnStateChanged;
            Session.Ready -= OnReady;
            var sources = new List<IPointerSource>();
            foreach (Participant p in Session.Participants) sources.Add(p.Source);
            // Positions validées gardées (Snapshots reste lisible), abonnements retirés.
            Session.DetachSources();
            foreach (IPointerSource source in sources) (source as IDisposable)?.Dispose();
        }

        private void Update()
        {
            if (IsOpen) Session.Tick(Time.unscaledTime);
        }

        private void OnDestroy() => Close();

        private void OnStateChanged(Participant participant, CaptureState state) => StateChanged?.Invoke(participant, state);
        private void OnReady() => Ready?.Invoke();
    }
}
