using System;
using NUnit.Framework;
using UnityEngine;

namespace Mooveo.Positioning.Tests
{
    /// <summary>Tests de la couche de données du positionnement (repère de l'aire, immobilité, session, classement).</summary>
    public class PositioningTests
    {
        private const float Dt = 1f / 60f;

        // Source minimale pilotée par le test.
        private sealed class FakeSource : IPointerSource
        {
            public Vector3 Position;
            public bool Tracked = true;
            public event Action TriggerPressed;
            public void Press() => TriggerPressed?.Invoke();

            public bool TryGetPose(out PointerPose pose)
            {
                pose = new PointerPose(Position, Quaternion.identity);
                return Tracked;
            }
        }

        private static CaptureSettings Settings(bool autoLock = false)
        {
            CaptureSettings s = CaptureSettings.CreateDefault();
            s.AutoLock = autoLock;
            return s;
        }

        // ---- Repère de l'aire ----

        [Test]
        public void AreaSpace_RoundTrip()
        {
            var space = new PlayAreaSpace(new Vector3(1f, 1.5f, 3f), new Vector3(0.6f, 0.2f, -0.8f), 4f, 2f, true);
            var point = new Vector3(-0.4f, 1.1f, 0.7f);
            Vector3 back = space.FromArea(space.ToArea(point));
            Assert.That((back - point).magnitude, Is.LessThan(1e-4f));
        }

        [Test]
        public void AreaSpace_PlayerInFront_HasPositiveDepth_AndRightIsPositive()
        {
            PlayAreaSpace space = PlayAreaSpace.Default; // écran à z = 2,5, droite = +X
            Vector3 area = space.ToArea(new Vector3(0.5f, 1f, 0.5f));
            Assert.That(area.x, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(area.y, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(area.z, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void AreaSpace_RotatedRoom_LateralFollowsWallAxis()
        {
            // Écran tourné de 90° dans l'espace de suivi : la droite des joueurs est -Z.
            var space = new PlayAreaSpace(new Vector3(3f, 1.3f, 0f), new Vector3(0f, 0f, -1f), 3f, 1.7f, true);
            Assert.That(space.Forward.x, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(space.Lateral(new Vector3(0f, 1f, -1f)), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(space.ToArea(Vector3.zero).z, Is.EqualTo(3f).Within(1e-4f));
        }

        // ---- Validation (immobilité) ----

        private static float Run(PositionCapture capture, Func<float, Vector3> path, float from, float duration)
        {
            float t = from;
            for (; t < from + duration; t += Dt) capture.Sample(new PointerPose(path(t), Quaternion.identity), t);
            return t;
        }

        [Test]
        public void Capture_StillDevice_Locks()
        {
            var capture = new PositionCapture(Settings());
            Run(capture, _ => new Vector3(0.2f, 1f, 1f), 0f, 0.5f);
            Assert.IsTrue(capture.Toggle());
            Assert.AreEqual(CaptureState.Locked, capture.State);
            Assert.That(capture.LockedPose.Position.x, Is.EqualTo(0.2f).Within(1e-4f));
        }

        [Test]
        public void Capture_MovingDevice_IsRefused_ThenRefusalExpires()
        {
            var capture = new PositionCapture(Settings());
            float t = Run(capture, time => new Vector3(time * 0.5f, 1f, 1f), 0f, 0.5f); // 0,5 m/s
            Assert.IsFalse(capture.Toggle());
            Assert.AreEqual(CaptureState.Refused, capture.State);
            Run(capture, _ => Vector3.one, t, 2f);
            Assert.AreEqual(CaptureState.Free, capture.State);
        }

        [Test]
        public void Capture_SmallJitter_WithinTolerance_Locks()
        {
            var capture = new PositionCapture(Settings());
            Run(capture, time => new Vector3(Mathf.Sin(time * 40f) * 0.015f, 1f, 1f), 0f, 0.5f);
            Assert.IsTrue(capture.Toggle());
        }

        [Test]
        public void Capture_TooEarly_IsRefused()
        {
            var capture = new PositionCapture(Settings());
            Run(capture, _ => Vector3.one, 0f, 0.1f); // fenêtre pas encore couverte
            Assert.IsFalse(capture.Toggle());
        }

        [Test]
        public void Capture_SecondPress_Unlocks_AndLockedPoseIsFrozen()
        {
            var capture = new PositionCapture(Settings());
            float t = Run(capture, _ => new Vector3(0f, 1f, 1f), 0f, 0.5f);
            capture.Toggle();
            Run(capture, _ => new Vector3(1f, 1f, 1f), t, 0.5f);
            Assert.That(capture.DisplayPose.Position.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.IsTrue(capture.Toggle());
            Assert.IsFalse(capture.IsLocked);
            Assert.That(capture.DisplayPose.Position.x, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Capture_Untracked_IsRefused()
        {
            var capture = new PositionCapture(Settings());
            Run(capture, _ => Vector3.one, 0f, 0.5f);
            capture.Sample(null, 0.6f);
            Assert.IsFalse(capture.Toggle());
        }

        [Test]
        public void Capture_AutoLock_AfterDelay_WithProgress()
        {
            CaptureSettings settings = Settings(autoLock: true);
            var capture = new PositionCapture(settings);
            float t = Run(capture, _ => Vector3.one, 0f, 1.2f);
            Assert.IsFalse(capture.IsLocked);
            Assert.That(capture.AutoLockProgress, Is.InRange(0.3f, 0.7f));
            Run(capture, _ => Vector3.one, t, 1.5f);
            Assert.IsTrue(capture.IsLocked);
            Assert.AreEqual(0f, capture.AutoLockProgress);
        }

        [Test]
        public void Capture_AutoLock_ResetByMovement()
        {
            var capture = new PositionCapture(Settings(autoLock: true));
            float t = Run(capture, _ => Vector3.one, 0f, 1.5f);
            t = Run(capture, time => new Vector3(time, 1f, 1f), t, 0.3f); // bouge
            Assert.AreEqual(0f, capture.AutoLockProgress);
            Run(capture, _ => Vector3.zero, t, 1.5f);
            Assert.IsFalse(capture.IsLocked);
        }

        // ---- Session ----

        [Test]
        public void Session_ReadyAfterDelay_OnceAllLocked_ViaTrigger()
        {
            var session = new PositionSession(PlayAreaSpace.Default, Settings());
            var a = new FakeSource { Position = new Vector3(-0.5f, 1f, 1f) };
            var b = new FakeSource { Position = new Vector3(0.5f, 1f, 1f) };
            session.Add(0, "J1", Color.red, a);
            session.Add(1, "J2", Color.blue, b);
            int ready = 0;
            session.Ready += () => ready++;

            float t = 0f;
            for (; t < 0.5f; t += Dt) session.Tick(t);
            a.Press();
            Assert.IsFalse(session.AllLocked);
            b.Press();
            Assert.IsTrue(session.AllLocked);
            Assert.IsFalse(session.IsReady);
            float lockedAt = t;
            for (; t < lockedAt + 1f; t += Dt) session.Tick(t);
            Assert.IsTrue(session.IsReady);
            Assert.AreEqual(1, ready);

            a.Press(); // libère
            session.Tick(t);
            Assert.IsFalse(session.IsReady);
            session.Dispose();
        }

        [Test]
        public void Session_SimulatedParticipant_LockedImmediately()
        {
            var session = new PositionSession(PlayAreaSpace.Default, Settings());
            session.Add(0, "J1", Color.red, new FakeSource { Position = Vector3.one }, simulated: true);
            session.Tick(0f);
            Assert.IsTrue(session.AllLocked);
        }

        [Test]
        public void Session_SourceSwap_ResubscribesTrigger()
        {
            var session = new PositionSession(PlayAreaSpace.Default, Settings());
            var oldSource = new FakeSource { Position = Vector3.one };
            var newSource = new FakeSource { Position = Vector3.one };
            Participant p = session.Add(0, "J1", Color.red, oldSource);
            p.SetSource(newSource);
            for (float t = 0f; t < 0.5f; t += Dt) session.Tick(t);
            oldSource.Press();
            Assert.IsFalse(p.Capture.IsLocked);
            newSource.Press();
            Assert.IsTrue(p.Capture.IsLocked);
        }

        [Test]
        public void Session_Snapshots_InAreaSpace()
        {
            var session = new PositionSession(PlayAreaSpace.Default, Settings());
            session.Add(0, "J1", Color.red, new FakeSource { Position = new Vector3(-0.7f, 1f, 1f) }, simulated: true);
            session.Tick(0f);
            var snapshots = session.Snapshots();
            Assert.AreEqual(1, snapshots.Count);
            Assert.That(snapshots[0].Lateral, Is.EqualTo(-0.7f).Within(1e-4f));
            Assert.That(snapshots[0].Area.z, Is.EqualTo(1.5f).Within(1e-4f));
        }

        // ---- Classement ----

        [Test]
        public void Ordering_SortsLeftToRight_UnknownLast_Stable()
        {
            int[] order = LateralOrdering.Order(new float?[] { 0.4f, null, -1f, 0.4f, -0.2f });
            CollectionAssert.AreEqual(new[] { 2, 4, 0, 3, 1 }, order);
        }

        [Test]
        public void Ordering_LeftHalf_ThreePlayers_TwoOnTheLeft()
        {
            bool[] left = LateralOrdering.LeftHalf(new float?[] { 1f, -1f, 0f });
            CollectionAssert.AreEqual(new[] { false, true, true }, left);
        }

        [Test]
        public void Ordering_LeftHalf_FourPlayers_And_Single()
        {
            CollectionAssert.AreEqual(new[] { true, false, false, true },
                LateralOrdering.LeftHalf(new float?[] { -2f, 1f, 2f, -1f }));
            CollectionAssert.AreEqual(new[] { true }, LateralOrdering.LeftHalf(new float?[] { 5f }));
        }
    }
}
