using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mooveo.Positioning.Tests
{
    /// <summary>
    /// Écran de positionnement en jeu : session avancée image par image, validation par la gâchette d'un appareil simulé,
    /// « prêt » après le délai, rendu de l'aperçu et isolement de son calque.
    /// </summary>
    public class PositionFlowPlayTests
    {
        private PositionFlow _flow;
        private GameObject _otherCamera;

        [TearDown]
        public void TearDown()
        {
            if (_flow != null) Object.Destroy(_flow.gameObject);
            if (_otherCamera != null) Object.Destroy(_otherCamera);
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }

        [UnityTest]
        public IEnumerator SimulatedDevices_LockByTrigger_ThenReady()
        {
            _flow = PositionFlow.Create(null);
            PositionSession session = _flow.Open(PlayAreaSpace.Default);
            var left = new SimulatedPointerSource(session.Space, new Vector3(-0.6f, 1.05f, 2f));
            var right = new SimulatedPointerSource(session.Space, new Vector3(0.6f, 1.05f, 2f));
            Participant j1 = session.Add(0, "J1", Color.red, left);
            Participant j2 = session.Add(1, "J2", Color.blue, right);
            int ready = 0;
            _flow.Ready += () => ready++;

            yield return WaitSeconds(0.5f); // fenêtre d'immobilité couverte
            left.Press();
            right.Press();
            Assert.IsTrue(j1.Capture.IsLocked && j2.Capture.IsLocked);
            Assert.IsFalse(session.IsReady, "« Prêt » attend le délai après la dernière validation.");

            yield return WaitSeconds(session.Settings.ReadyDelay + 0.2f);
            Assert.IsTrue(session.IsReady);
            Assert.AreEqual(1, ready);

            // Aperçu : étiquettes dans l'image, de gauche à droite comme les joueurs.
            Assert.IsTrue(_flow.Preview.TryGetTagPoint(j1, out Vector2 tag1));
            Assert.IsTrue(_flow.Preview.TryGetTagPoint(j2, out Vector2 tag2));
            Assert.That(tag1.x, Is.InRange(0f, 1f));
            Assert.That(tag2.y, Is.InRange(0f, 1f));
            Assert.Less(tag1.x, tag2.x);

            // Fermeture : positions validées encore lisibles, plus d'échantillonnage.
            _flow.Close();
            Assert.AreEqual(2, session.Snapshots().Count);
            Assert.Less(session.Snapshots()[0].Lateral, session.Snapshots()[1].Lateral);
        }

        [UnityTest]
        public IEnumerator MovingDevice_IsRefused()
        {
            _flow = PositionFlow.Create(null);
            PositionSession session = _flow.Open(PlayAreaSpace.Default);
            var source = new SimulatedPointerSource(session.Space, new Vector3(0f, 1f, 2f));
            Participant p = session.Add(0, "J1", Color.green, source);
            CaptureState? last = null;
            _flow.StateChanged += (participant, state) => last = state;

            float end = Time.unscaledTime + 0.5f;
            while (Time.unscaledTime < end)
            {
                source.AreaPosition += new Vector3(0.6f * Time.unscaledDeltaTime, 0f, 0f); // 0,6 m/s
                yield return null;
            }
            source.Press();
            Assert.IsFalse(p.Capture.IsLocked);
            Assert.AreEqual(CaptureState.Refused, last);
        }

        [UnityTest]
        public IEnumerator PreviewLayer_IsHiddenFromOtherCameras()
        {
            _flow = PositionFlow.Create(null);
            PositionSession session = _flow.Open(PlayAreaSpace.Default);
            session.Add(0, "J1", Color.red, new SimulatedPointerSource(session.Space, new Vector3(0f, 1f, 2f)), simulated: true);

            _otherCamera = new GameObject("OtherCamera");
            var cam = _otherCamera.AddComponent<Camera>();
            cam.targetTexture = new RenderTexture(64, 64, 16);
            cam.cullingMask = ~0;
            yield return null;
            yield return null;

            int layer = _flow.Preview.Layer;
            if (layer == 0) Assert.Ignore("Aucun calque libre dans ce projet : isolement par la distance seulement.");
            Assert.AreEqual(0, cam.cullingMask & (1 << layer), "Une autre caméra dessine encore le calque de l'aperçu.");
            Assert.AreEqual(layer, _flow.Preview.gameObject.layer);
            cam.targetTexture.Release();
        }
    }
}
