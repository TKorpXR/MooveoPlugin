using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mooveo.Positioning
{
    /// <summary>Réglages de la validation d'une position (partagés par tous les participants).</summary>
    [CreateAssetMenu(menuName = "Mooveo/Positionnement/Réglages de validation", fileName = "CaptureSettings")]
    public class CaptureSettings : ScriptableObject
    {
        [Tooltip("Écart maximal (m) de l'appareil pendant la fenêtre d'immobilité pour accepter la position.")]
        [Min(0.005f)] public float StillTolerance = 0.05f;
        [Tooltip("Durée (s) d'immobilité regardée avant l'appui (ou exigée par la validation automatique).")]
        [Min(0.05f)] public float StillWindow = 0.35f;
        [Tooltip("Durée (s) d'affichage du refus quand l'appareil bougeait.")]
        [Min(0f)] public float RefusedDuration = 1.4f;
        [Tooltip("Validation automatique : la position se verrouille d'elle-même après AutoLockDelay s d'immobilité.")]
        public bool AutoLock;
        [Tooltip("Durée (s) d'immobilité pour la validation automatique.")]
        [Min(0.2f)] public float AutoLockDelay = 2f;
        [Tooltip("Délai (s) entre la dernière validation et « tout le monde est prêt » (la gâchette qui valide ne déclenche pas la suite).")]
        [Min(0f)] public float ReadyDelay = 0.6f;

        /// <summary>Réglages par défaut (sans asset).</summary>
        public static CaptureSettings CreateDefault()
        {
            var settings = CreateInstance<CaptureSettings>();
            settings.hideFlags = HideFlags.DontSave;
            return settings;
        }
    }

    /// <summary>État de la position d'un participant.</summary>
    public enum CaptureState
    {
        /// <summary>Pas encore validée.</summary>
        Free,
        /// <summary>Validation refusée à l'instant (l'appareil bougeait).</summary>
        Refused,
        /// <summary>Validée et figée.</summary>
        Locked,
    }

    /// <summary>
    /// Validation de la position d'un participant : on note la pose de son appareil à chaque image ; un appui sur la gâchette
    /// la verrouille si l'appareil est resté immobile pendant la fenêtre d'immobilité (sinon refus), un second appui la libère.
    /// Validation automatique en option (immobile assez longtemps). Temps fourni par l'appelant : indépendant de la pause.
    /// </summary>
    public sealed class PositionCapture
    {
        private readonly Queue<(float time, Vector3 position)> _history = new Queue<(float, Vector3)>();
        private readonly CaptureSettings _settings;
        private float _now;
        private float _refusedUntil = float.NegativeInfinity;
        private float _stillSince = float.NaN;

        public PositionCapture(CaptureSettings settings) => _settings = settings;

        /// <summary>Position et rotation validées (espace de suivi), valables si <see cref="IsLocked"/>.</summary>
        public PointerPose LockedPose { get; private set; }
        /// <summary>Dernière pose vue (espace de suivi).</summary>
        public PointerPose CurrentPose { get; private set; }
        /// <summary>Vrai si l'appareil était suivi à la dernière image.</summary>
        public bool IsTracked { get; private set; }
        public bool IsLocked { get; private set; }

        public CaptureState State => IsLocked ? CaptureState.Locked : _now < _refusedUntil ? CaptureState.Refused : CaptureState.Free;

        /// <summary>Pose affichée : celle validée si verrouillée, sinon la pose courante.</summary>
        public PointerPose DisplayPose => IsLocked ? LockedPose : CurrentPose;

        /// <summary>Avancement (0-1) de la validation automatique ; 0 si désactivée ou verrouillée.</summary>
        public float AutoLockProgress =>
            _settings.AutoLock && !IsLocked && !float.IsNaN(_stillSince) ? Mathf.Clamp01((_now - _stillSince) / _settings.AutoLockDelay) : 0f;

        public event Action<PositionCapture> Locked;
        public event Action<PositionCapture> Unlocked;
        public event Action<PositionCapture> Refused;

        /// <summary>Note la pose de l'appareil (à chaque image) ; time : temps non affecté par la pause.</summary>
        public void Sample(PointerPose? pose, float time)
        {
            _now = time;
            IsTracked = pose.HasValue;
            if (!pose.HasValue)
            {
                _history.Clear();
                _stillSince = float.NaN;
                return;
            }
            CurrentPose = pose.Value;
            _history.Enqueue((time, pose.Value.Position));
            while (_history.Count > 0 && time - _history.Peek().time > _settings.StillWindow) _history.Dequeue();

            if (IsLocked || !_settings.AutoLock) return;
            if (IsStill()) { if (float.IsNaN(_stillSince)) _stillSince = time; }
            else _stillSince = float.NaN;
            if (!float.IsNaN(_stillSince) && time - _stillSince >= _settings.AutoLockDelay) Lock();
        }

        /// <summary>Appui sur la gâchette : verrouille si immobile, sinon refuse ; libère si déjà verrouillé. Vrai si l'état a changé.</summary>
        public bool Toggle()
        {
            if (IsLocked)
            {
                Unlock();
                return true;
            }
            if (!IsTracked || !IsStill())
            {
                _refusedUntil = _now + _settings.RefusedDuration;
                Refused?.Invoke(this);
                return false;
            }
            Lock();
            return true;
        }

        /// <summary>Verrouille sans contrôle (participant de test).</summary>
        public void ForceLock(PointerPose pose)
        {
            CurrentPose = pose;
            IsTracked = true;
            Lock();
        }

        public void Unlock()
        {
            if (!IsLocked) return;
            IsLocked = false;
            _stillSince = float.NaN;
            Unlocked?.Invoke(this);
        }

        private void Lock()
        {
            LockedPose = CurrentPose;
            IsLocked = true;
            _refusedUntil = float.NegativeInfinity;
            Locked?.Invoke(this);
        }

        // Immobile : assez d'échantillons couvrant la fenêtre, tous à moins de la tolérance de la pose courante.
        private bool IsStill()
        {
            if (_history.Count < 3) return false;
            if (_now - _history.Peek().time < _settings.StillWindow * 0.7f) return false;
            float spread = 0f;
            foreach (var sample in _history) spread = Mathf.Max(spread, (sample.position - CurrentPose.Position).magnitude);
            return spread <= _settings.StillTolerance;
        }
    }
}
