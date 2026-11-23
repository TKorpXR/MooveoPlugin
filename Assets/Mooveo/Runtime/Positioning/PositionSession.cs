using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mooveo.Positioning
{
    /// <summary>
    /// Un participant de l'étape de positionnement : identité affichée (étiquette, couleur), appareil, validation de sa
    /// position. <see cref="Key"/> est libre (index de joueur, slot du jeu…) : le module ne l'interprète pas.
    /// </summary>
    public sealed class Participant
    {
        public Participant(object key, string label, Color color, IPointerSource source, CaptureSettings settings)
        {
            Key = key;
            Label = label;
            Color = color;
            Source = source;
            Capture = new PositionCapture(settings);
        }

        public object Key { get; }
        public string Label { get; set; }
        public Color Color { get; set; }
        /// <summary>Matériel de l'appareil, choisit le modèle affiché ; null = profil matériel courant.</summary>
        public HardwareProfile? DeviceProfile { get; set; }
        /// <summary>Participant de test : verrouillé d'office, sans appareil réel.</summary>
        public bool IsSimulated { get; set; }
        public IPointerSource Source { get; private set; }
        public PositionCapture Capture { get; }

        /// <summary>Change d'appareil (manette reconnectée) ; la session se réabonne à sa gâchette.</summary>
        internal event Action<Participant, IPointerSource> SourceChanging;

        public void SetSource(IPointerSource source)
        {
            if (source == Source) return;
            SourceChanging?.Invoke(this, source);
            Source = source;
        }
    }

    /// <summary>Position validée d'un participant, dans l'espace de suivi et dans le repère de l'aire.</summary>
    public readonly struct PositionSnapshot
    {
        public PositionSnapshot(Participant participant, PointerPose tracking, PlayAreaSpace space)
        {
            Participant = participant;
            Tracking = tracking;
            Area = space.ToArea(tracking.Position);
            AreaRotation = space.ToArea(tracking.Rotation);
        }

        public Participant Participant { get; }
        /// <summary>Pose dans l'espace de suivi.</summary>
        public PointerPose Tracking { get; }
        /// <summary>Position dans le repère de l'aire (x latéral, y hauteur, z devant l'écran).</summary>
        public Vector3 Area { get; }
        public Quaternion AreaRotation { get; }
        public float Lateral => Area.x;
    }

    /// <summary>
    /// Étape de positionnement : les participants, leur validation, et « tout le monde est prêt » (avec un délai, pour que
    /// la gâchette qui valide la dernière position ne déclenche pas aussi la suite). À faire avancer par <see cref="Tick"/>
    /// à chaque image (temps non affecté par la pause, ex. <c>Time.unscaledTime</c>). Indépendante de tout affichage.
    /// </summary>
    public sealed class PositionSession : IDisposable
    {
        private readonly List<Participant> _participants = new List<Participant>();
        private readonly Dictionary<Participant, Action> _triggerHandlers = new Dictionary<Participant, Action>();
        private float _now;
        private float _allLockedSince = float.NaN;
        private bool _readyRaised;

        public PositionSession(PlayAreaSpace space, CaptureSettings settings = null)
        {
            Space = space;
            Settings = settings != null ? settings : CaptureSettings.CreateDefault();
        }

        public PlayAreaSpace Space { get; set; }
        public CaptureSettings Settings { get; }
        public IReadOnlyList<Participant> Participants => _participants;

        /// <summary>Tous les participants sont verrouillés (et il y en a au moins un).</summary>
        public bool AllLocked
        {
            get
            {
                if (_participants.Count == 0) return false;
                foreach (Participant p in _participants) if (!p.Capture.IsLocked) return false;
                return true;
            }
        }

        /// <summary>Tous verrouillés depuis au moins <see cref="CaptureSettings.ReadyDelay"/>.</summary>
        public bool IsReady => AllLocked && !float.IsNaN(_allLockedSince) && _now - _allLockedSince >= Settings.ReadyDelay;

        /// <summary>Un participant a changé d'état (verrouillé, libéré, refusé).</summary>
        public event Action<Participant, CaptureState> StateChanged;
        /// <summary>Émis une fois quand <see cref="IsReady"/> devient vrai (de nouveau après une libération).</summary>
        public event Action Ready;

        public Participant Add(object key, string label, Color color, IPointerSource source, bool simulated = false)
        {
            var participant = new Participant(key, label, color, source, Settings) { IsSimulated = simulated };
            _participants.Add(participant);
            participant.Capture.Locked += c => StateChanged?.Invoke(participant, CaptureState.Locked);
            participant.Capture.Unlocked += c => StateChanged?.Invoke(participant, CaptureState.Free);
            participant.Capture.Refused += c => StateChanged?.Invoke(participant, CaptureState.Refused);
            participant.SourceChanging += OnSourceChanging;
            Subscribe(participant, source);
            return participant;
        }

        public void Remove(Participant participant)
        {
            if (!_participants.Remove(participant)) return;
            Unsubscribe(participant, participant.Source);
            participant.SourceChanging -= OnSourceChanging;
        }

        public Participant Find(object key)
        {
            foreach (Participant p in _participants) if (Equals(p.Key, key)) return p;
            return null;
        }

        /// <summary>Échantillonne les appareils, verrouille d'office les participants de test, met à jour « prêt ».</summary>
        public void Tick(float time)
        {
            _now = time;
            foreach (Participant p in _participants)
            {
                PointerPose pose = default;
                bool tracked = p.Source != null && p.Source.TryGetPose(out pose);
                if (p.IsSimulated && !p.Capture.IsLocked && tracked) p.Capture.ForceLock(pose);
                p.Capture.Sample(tracked ? pose : (PointerPose?)null, time);
            }

            if (!AllLocked)
            {
                _allLockedSince = float.NaN;
                _readyRaised = false;
                return;
            }
            if (float.IsNaN(_allLockedSince)) _allLockedSince = time;
            if (!_readyRaised && IsReady)
            {
                _readyRaised = true;
                Ready?.Invoke();
            }
        }

        /// <summary>Appui sur la gâchette d'un participant (aussi appelé par les sources abonnées).</summary>
        public void Press(Participant participant) => participant?.Capture.Toggle();

        /// <summary>Libère toutes les positions (rouvrir l'étape).</summary>
        public void UnlockAll()
        {
            foreach (Participant p in _participants) p.Capture.Unlock();
        }

        /// <summary>Positions validées, dans l'ordre des participants (verrouillés seulement).</summary>
        public List<PositionSnapshot> Snapshots()
        {
            var list = new List<PositionSnapshot>(_participants.Count);
            foreach (Participant p in _participants)
                if (p.Capture.IsLocked) list.Add(new PositionSnapshot(p, p.Capture.LockedPose, Space));
            return list;
        }

        /// <summary>Position latérale affichée (validée sinon courante) d'un participant ; null s'il n'a jamais été vu.</summary>
        public float? LiveLateral(Participant participant)
        {
            if (participant == null || (!participant.Capture.IsLocked && !participant.Capture.IsTracked)) return null;
            return Space.Lateral(participant.Capture.DisplayPose.Position);
        }

        /// <summary>Se désabonne des gâchettes, participants et positions validées gardés (session close mais lisible).</summary>
        public void DetachSources()
        {
            foreach (Participant p in _participants) Unsubscribe(p, p.Source);
        }

        public void Dispose()
        {
            foreach (Participant p in _participants)
            {
                Unsubscribe(p, p.Source);
                p.SourceChanging -= OnSourceChanging;
            }
            _participants.Clear();
        }

        private void OnSourceChanging(Participant participant, IPointerSource next)
        {
            Unsubscribe(participant, participant.Source);
            Subscribe(participant, next);
        }

        private void Subscribe(Participant participant, IPointerSource source)
        {
            if (source == null) return;
            Action handler = () => Press(participant);
            _triggerHandlers[participant] = handler;
            source.TriggerPressed += handler;
        }

        private void Unsubscribe(Participant participant, IPointerSource source)
        {
            if (source == null || !_triggerHandlers.TryGetValue(participant, out Action handler)) return;
            source.TriggerPressed -= handler;
            _triggerHandlers.Remove(participant);
        }
    }

    /// <summary>Classement de gauche à droite (face à l'écran) et répartition en deux moitiés.</summary>
    public static class LateralOrdering
    {
        /// <summary>
        /// Indices des éléments classés de gauche à droite. Position inconnue (null) : gardés à la fin, dans leur ordre
        /// d'origine ; ex æquo : ordre d'origine (tri stable).
        /// </summary>
        public static int[] Order(IReadOnlyList<float?> laterals)
        {
            var indices = new int[laterals.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            // Tri par insertion : stable, listes de quelques joueurs.
            for (int i = 1; i < indices.Length; i++)
            {
                int current = indices[i];
                int j = i - 1;
                while (j >= 0 && Before(laterals[current], current, laterals[indices[j]], indices[j]))
                {
                    indices[j + 1] = indices[j];
                    j--;
                }
                indices[j + 1] = current;
            }
            return indices;
        }

        /// <summary>
        /// Côté de chaque élément : vrai = moitié gauche. Avec un nombre impair, la gauche a un élément de plus
        /// (à trois : deux à gauche). Un seul élément : à gauche.
        /// </summary>
        public static bool[] LeftHalf(IReadOnlyList<float?> laterals)
        {
            int[] order = Order(laterals);
            int leftCount = (order.Length + 1) / 2;
            var left = new bool[order.Length];
            for (int rank = 0; rank < order.Length; rank++) left[order[rank]] = rank < leftCount;
            return left;
        }

        private static bool Before(float? a, int ia, float? b, int ib)
        {
            if (a.HasValue != b.HasValue) return a.HasValue;
            if (!a.HasValue || Mathf.Approximately(a.Value, b.Value)) return ia < ib;
            return a.Value < b.Value;
        }
    }
}
