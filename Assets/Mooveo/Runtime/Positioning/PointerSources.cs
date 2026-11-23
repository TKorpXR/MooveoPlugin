using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mooveo.Positioning
{
    /// <summary>Pose d'un appareil suivi (manette, tracker) dans l'espace de suivi.</summary>
    public struct PointerPose
    {
        public Vector3 Position;
        public Quaternion Rotation;

        public PointerPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    /// <summary>
    /// Appareil d'un participant : sa pose dans l'espace de suivi et sa gâchette. Implémentations : un objet suivi + son
    /// <see cref="InputReader"/> (<see cref="TrackedPointerSource"/>), un appareil simulé au clavier
    /// (<see cref="SimulatedPointerSource"/>), ou toute source propre au jeu.
    /// </summary>
    public interface IPointerSource
    {
        /// <summary>Pose courante ; faux si l'appareil n'est pas suivi en ce moment.</summary>
        bool TryGetPose(out PointerPose pose);

        /// <summary>Gâchette pressée.</summary>
        event Action TriggerPressed;
    }

    /// <summary>
    /// Objet suivi (TrackedPoseDriver / SteamVR) dont la position et la rotation LOCALES sont celles de l'espace de suivi,
    /// et sa gâchette lue par l'<see cref="InputReader"/> du SDK. Cas courant : manettes et trackers des joueurs.
    /// </summary>
    public sealed class TrackedPointerSource : IPointerSource, IDisposable
    {
        private readonly Transform _pose;
        private readonly InputReader _reader;
        private Action _triggerPressed;

        public TrackedPointerSource(Transform pose, InputReader reader)
        {
            _pose = pose;
            _reader = reader;
            if (_reader != null) _reader.TriggerPressed += OnTrigger;
        }

        /// <summary>Appareil (InputDevice) lu, s'il est connu (sert d'identifiant au jeu).</summary>
        public InputDevice Device { get; set; }

        public event Action TriggerPressed
        {
            add => _triggerPressed += value;
            remove => _triggerPressed -= value;
        }

        public bool TryGetPose(out PointerPose pose)
        {
            pose = default;
            if (_pose == null) return false;
            pose = new PointerPose(_pose.localPosition, _pose.localRotation);
            return true;
        }

        private void OnTrigger() => _triggerPressed?.Invoke();

        public void Dispose()
        {
            if (_reader != null) _reader.TriggerPressed -= OnTrigger;
            _triggerPressed = null;
        }
    }

    /// <summary>
    /// Source branchée par le jeu : la pose est lue par une fonction (null = non suivi), la gâchette signalée par
    /// <see cref="Press"/> (ex. un curseur de menu qui a déjà sa propre gâchette).
    /// </summary>
    public sealed class DelegatePointerSource : IPointerSource
    {
        private readonly Func<PointerPose?> _pose;
        private Action _triggerPressed;

        public DelegatePointerSource(Func<PointerPose?> pose) => _pose = pose;

        public event Action TriggerPressed
        {
            add => _triggerPressed += value;
            remove => _triggerPressed -= value;
        }

        public bool TryGetPose(out PointerPose pose)
        {
            PointerPose? value = _pose?.Invoke();
            pose = value ?? default;
            return value.HasValue;
        }

        public void Press() => _triggerPressed?.Invoke();
    }

    /// <summary>
    /// Appareil simulé pour tester sans casque : posé dans l'aire de jeu, déplacé au clavier quand il est actif
    /// (flèches : gauche / droite et avant / arrière, Page préc. / suiv. : hauteur), gâchette = Entrée. Sans clavier actif, il
    /// reste immobile (validation possible par <see cref="Press"/>).
    /// </summary>
    public sealed class SimulatedPointerSource : IPointerSource
    {
        private Action _triggerPressed;
        private Vector3 _area;
        private readonly PlayAreaSpace _space;

        /// <param name="space">Aire de jeu.</param>
        /// <param name="areaPosition">Position de départ dans le repère de l'aire (x latéral, y hauteur, z devant l'écran).</param>
        public SimulatedPointerSource(PlayAreaSpace space, Vector3 areaPosition)
        {
            _space = space;
            _area = areaPosition;
        }

        /// <summary>Piloté au clavier en ce moment.</summary>
        public bool KeyboardDriven { get; set; }
        /// <summary>Vitesse de déplacement au clavier (m/s).</summary>
        public float Speed { get; set; } = 1.2f;

        public event Action TriggerPressed
        {
            add => _triggerPressed += value;
            remove => _triggerPressed -= value;
        }

        /// <summary>Position dans le repère de l'aire.</summary>
        public Vector3 AreaPosition
        {
            get => _area;
            set => _area = value;
        }

        public bool TryGetPose(out PointerPose pose)
        {
            Poll();
            pose = new PointerPose(_space.FromArea(_area), Quaternion.LookRotation(_space.Forward, Vector3.up));
            return true;
        }

        /// <summary>Appui sur la gâchette (tests).</summary>
        public void Press() => _triggerPressed?.Invoke();

        private int _polledFrame = -1;

        private void Poll()
        {
            if (!KeyboardDriven || Time.frameCount == _polledFrame) return;
            _polledFrame = Time.frameCount;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            float step = Speed * Time.unscaledDeltaTime;
            if (keyboard.leftArrowKey.isPressed) _area.x -= step;
            if (keyboard.rightArrowKey.isPressed) _area.x += step;
            if (keyboard.upArrowKey.isPressed) _area.z -= step;
            if (keyboard.downArrowKey.isPressed) _area.z += step;
            if (keyboard.pageUpKey.isPressed) _area.y += step;
            if (keyboard.pageDownKey.isPressed) _area.y -= step;
            if (keyboard.enterKey.wasPressedThisFrame) Press();
        }
    }
}
