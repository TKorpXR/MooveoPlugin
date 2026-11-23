using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if MOOVEO_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Mooveo.Positioning
{
    /// <summary>
    /// Aperçu 3D de l'aire de jeu rendu dans une texture : sol, écran calibré, et pour chaque participant d'une
    /// <see cref="PositionSession"/> son appareil (modèle du profil matériel) à sa position et son orientation réelles, avec
    /// rayon et point d'impact sur l'écran, tige et anneau au sol (plein une fois la position validée). Caméra derrière les
    /// joueurs, en plongée, qui cadre l'écran et tous les appareils. Studio isolé sur un calque dédié, que les autres caméras
    /// ne dessinent pas. Apparence : <see cref="PlayAreaPreviewStyle"/>. Interface : <see cref="PlayAreaPreviewElement"/>.
    /// </summary>
    public class PlayAreaPreview : MonoBehaviour
    {
        private sealed class Marker
        {
            public Transform Pivot, Root, Model, Beam, Hit, Stem, Ring, RingFill;
            public Vector3 ModelScale;
            public Quaternion Aim = Quaternion.identity;
            public IPositionMarkerView[] Views;
            public Vector3 TagPoint;
            public bool Visible;
        }

        private PlayAreaPreviewStyle _style;
        private Camera _camera;
        private Transform _space;
        private Transform _wallPanel, _wallTop, _wallBottom, _wallLeft, _wallRight, _floor;
        private readonly Dictionary<Participant, Marker> _markers = new Dictionary<Participant, Marker>();
        private readonly List<Participant> _stale = new List<Participant>();
        private Vector3 _cameraPosition, _cameraTarget;
        private bool _cameraPlaced;
        private PlayAreaSpace _area = PlayAreaSpace.Default;

        /// <summary>Image de l'aperçu.</summary>
        public RenderTexture Texture { get; private set; }
        public PlayAreaPreviewStyle Style => _style;
        /// <summary>Calque du studio.</summary>
        public int Layer { get; private set; }
        /// <summary>Session affichée (mise à jour à chaque image tant qu'elle est liée).</summary>
        public PositionSession Session { get; private set; }

        /// <summary>Crée l'aperçu (style null = style par défaut).</summary>
        public static PlayAreaPreview Create(PlayAreaPreviewStyle style = null, Transform parent = null)
        {
            var go = new GameObject("PlayAreaPreview");
            if (parent != null) go.transform.SetParent(parent, false);
            var preview = go.AddComponent<PlayAreaPreview>();
            preview.Build(style != null ? style : PlayAreaPreviewStyle.CreateDefault());
            return preview;
        }

        /// <summary>Affiche cette session (null : plus rien).</summary>
        public void Bind(PositionSession session)
        {
            Session = session;
            if (session == null) Clear();
        }

        /// <summary>Position (0-1, origine en bas à gauche) de l'étiquette d'un participant dans l'image.</summary>
        public bool TryGetTagPoint(Participant participant, out Vector2 viewport)
        {
            viewport = default;
            if (participant == null || !_markers.TryGetValue(participant, out Marker marker) || !marker.Visible) return false;
            Vector3 v = _camera.WorldToViewportPoint(marker.TagPoint);
            viewport = new Vector2(v.x, v.y);
            return v.z > 0f;
        }

        /// <summary>Position (0-1) du haut de l'écran calibré dans l'image (étiquette « ÉCRAN »).</summary>
        public Vector2 WallLabelPoint()
        {
            Vector3 top = _space.TransformPoint(new Vector3(0f, _area.WallCenter.y + _area.WallHeight * 0.5f + 0.1f, 0f));
            Vector3 v = _camera.WorldToViewportPoint(top);
            return new Vector2(v.x, v.y);
        }

        private void Build(PlayAreaPreviewStyle style)
        {
            _style = style;
            Layer = PreviewLayer.Resolve(style.Layer);
            transform.position = style.StudioOrigin;

            Texture = new RenderTexture(style.Width, style.Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = Mathf.ClosestPowerOfTwo(Mathf.Clamp(style.Msaa, 1, 8)),
                name = "PlayAreaPreview",
            };

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.targetTexture = Texture;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = style.Background;
            _camera.fieldOfView = style.FieldOfView;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 60f;
            _camera.cullingMask = 1 << Layer;
#if MOOVEO_URP
            UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
            data.allowXRRendering = false;
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
#else
            _camera.stereoTargetEye = StereoTargetEyeMask.None;
#endif

            _space = new GameObject("Space").transform;
            _space.SetParent(transform, false);
            Material template = style.MaterialTemplate;
            _floor = BuiltInDeviceModels.Part(PrimitiveType.Cylinder, template, style.Floor, _space, "Floor", null);
            _wallPanel = BuiltInDeviceModels.Part(PrimitiveType.Cube, template, style.Wall, _space, "Wall", null);
            _wallTop = BuiltInDeviceModels.Part(PrimitiveType.Cube, template, style.Frame, _space, "WallTop", null);
            _wallBottom = BuiltInDeviceModels.Part(PrimitiveType.Cube, template, style.Frame, _space, "WallBottom", null);
            _wallLeft = BuiltInDeviceModels.Part(PrimitiveType.Cube, template, style.Frame, _space, "WallLeft", null);
            _wallRight = BuiltInDeviceModels.Part(PrimitiveType.Cube, template, style.Frame, _space, "WallRight", null);
            PreviewLayer.Apply(gameObject, Layer);
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += HideFromOtherCameras;
            Camera.onPreCull += HideFromOtherCameras; // pipeline intégré : pas de beginCameraRendering
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HideFromOtherCameras;
            Camera.onPreCull -= HideFromOtherCameras;
        }

        private void HideFromOtherCameras(ScriptableRenderContext context, Camera cam) => HideFromOtherCameras(cam);

        private void OnDestroy()
        {
            if (Texture != null) Texture.Release();
        }

        // Isolation : toute autre caméra perd le calque du studio avant son rendu (calque dédié, rien d'autre n'y vit).
        private void HideFromOtherCameras(Camera cam)
        {
            if (cam != _camera && Layer != 0) cam.cullingMask &= ~(1 << Layer);
        }

        private void LateUpdate()
        {
            if (Session == null) return;
            _area = Session.Space;
            _camera.enabled = true;
            LayoutRoom();

            _stale.Clear();
            foreach (Participant p in _markers.Keys) if (!Contains(Session.Participants, p)) _stale.Add(p);
            foreach (Participant p in _stale) Remove(p);

            // Points à cadrer : coins de l'écran (avec la place de son étiquette), profondeur minimale, appareils et anneaux.
            _framePoints.Clear();
            float half = _area.WallWidth * 0.5f, wallY = _area.WallCenter.y, wallH = _area.WallHeight * 0.5f;
            foreach (float x in new[] { -half, half })
            {
                _framePoints.Add(new Vector3(x, wallY + wallH + 0.3f, 0f));
                _framePoints.Add(new Vector3(x, 0f, 0f));
                _framePoints.Add(new Vector3(x, 0f, -_style.MinDepth));
            }
            float reach = _style.RingRadius + _style.Margin;
            foreach (Participant p in Session.Participants)
            {
                if (!_markers.TryGetValue(p, out Marker marker)) _markers[p] = marker = NewMarker(p);
                UpdateMarker(p, marker);
                if (!marker.Visible) continue;
                Vector3 local = marker.Root.localPosition;
                _framePoints.Add(local + Vector3.up * 0.45f); // étiquette
                _framePoints.Add(new Vector3(local.x - reach, 0f, local.z - reach));
                _framePoints.Add(new Vector3(local.x + reach, 0f, local.z - reach));
                _framePoints.Add(new Vector3(local.x - reach, 0f, local.z + reach));
                _framePoints.Add(new Vector3(local.x + reach, 0f, local.z + reach));
            }
            FrameCamera();
        }

        private static bool Contains(IReadOnlyList<Participant> list, Participant p)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == p) return true;
            return false;
        }

        // Repère du studio : sol à y = 0 (celui du suivi), écran en z = 0 centré en x = 0, joueurs en z < 0.
        private static Vector3 ToStudio(Vector3 area) => new Vector3(area.x, area.y, -area.z);

        private void UpdateMarker(Participant p, Marker m)
        {
            PositionCapture capture = p.Capture;
            bool visible = capture.IsLocked || capture.IsTracked;
            m.Visible = visible;
            m.Pivot.gameObject.SetActive(visible);

            Color dim = Color.Lerp(p.Color, _style.Background, _style.DimAmount);
            float pulse = capture.IsLocked ? 1f : 1f + _style.PulseWhileFree * Mathf.Sin(Time.unscaledTime * 7f);
            Vector3 areaPosition = _area.ToArea(capture.DisplayPose.Position);
            var state = new MarkerState
            {
                Participant = p,
                Color = p.Color,
                DimColor = dim,
                State = capture.State,
                Visible = visible,
                AutoLockProgress = capture.AutoLockProgress,
                Pulse = pulse,
                AreaPosition = areaPosition,
            };
            foreach (IPositionMarkerView view in m.Views) view.UpdateMarker(state);
            if (!visible) return;

            Vector3 position = ToStudio(areaPosition);
            Quaternion rotation = _area.ToArea(capture.DisplayPose.Rotation);
            m.Root.localPosition = position;
            m.Root.localRotation = rotation;
            m.Model.localScale = m.ModelScale * pulse;

            Material color = PreviewMaterials.Get(_style.MaterialTemplate, p.Color);
            Material dimMaterial = PreviewMaterials.Get(_style.MaterialTemplate, dim);

            // Rayon vers l'écran jusqu'à son plan (ou 4 m), point d'impact.
            Vector3 dir = rotation * m.Aim * Vector3.forward;
            bool hits = dir.z > 0.05f;
            float length = hits ? Mathf.Clamp(-position.z / dir.z, 0.2f, 8f) : 4f;
            m.Beam.gameObject.SetActive(_style.ShowBeam);
            m.Hit.gameObject.SetActive(_style.ShowBeam && hits);
            if (_style.ShowBeam)
            {
                m.Beam.localPosition = position + dir * (length * 0.5f);
                m.Beam.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                m.Beam.localScale = new Vector3(_style.BeamWidth, length * 0.5f, _style.BeamWidth);
                m.Beam.GetComponent<Renderer>().sharedMaterial = color;
                m.Hit.localPosition = position + dir * length;
                m.Hit.localScale = Vector3.one * (_style.BeamWidth * 6f);
                m.Hit.GetComponent<Renderer>().sharedMaterial = color;
            }

            // Tige et anneau au sol : le disque intérieur se remplit pendant la validation automatique, plein une fois validé.
            var floorPoint = new Vector3(position.x, 0f, position.z);
            m.Stem.gameObject.SetActive(_style.ShowStem);
            if (_style.ShowStem)
            {
                m.Stem.localPosition = (floorPoint + position) * 0.5f;
                m.Stem.localRotation = Quaternion.identity;
                m.Stem.localScale = new Vector3(_style.BeamWidth, Mathf.Max(0.005f, position.y * 0.5f), _style.BeamWidth);
            }
            m.Ring.gameObject.SetActive(_style.ShowFloorRing);
            m.RingFill.gameObject.SetActive(_style.ShowFloorRing);
            if (_style.ShowFloorRing)
            {
                float d = _style.RingRadius * 2f;
                m.Ring.localPosition = floorPoint + Vector3.up * 0.004f;
                m.Ring.localScale = new Vector3(d, 0.004f, d);
                m.Ring.GetComponent<Renderer>().sharedMaterial = color;
                float fill = capture.IsLocked ? 1f : Mathf.Lerp(0.78f, 0.96f, capture.AutoLockProgress);
                m.RingFill.localPosition = floorPoint + Vector3.up * 0.008f;
                m.RingFill.localScale = new Vector3(d * fill, 0.004f, d * fill);
                m.RingFill.GetComponent<Renderer>().sharedMaterial = capture.IsLocked ? color : dimMaterial;
            }

            m.TagPoint = _space.TransformPoint(position + Vector3.up * 0.35f);
        }

        // Structure d'un marqueur : Marker (pivot neutre du studio) → Device (pose de l'appareil) → modèle, et les repères
        // (rayon, impact, tige, anneaux) en coordonnées du studio, à côté : ils ne tournent pas avec l'appareil.
        private Marker NewMarker(Participant p)
        {
            var m = new Marker();
            m.Pivot = new GameObject("Marker " + p.Label).transform;
            m.Pivot.SetParent(_space, false);
            m.Root = new GameObject("Device").transform;
            m.Root.SetParent(m.Pivot, false);

            HardwareProfile profile = p.DeviceProfile ?? HardwareProfiles.Current.Id;
            PlayAreaPreviewStyle.DeviceModel model = _style.ModelFor(profile);
            bool customPrefab = model != null && model.Prefab != null;
            GameObject instance;
            if (customPrefab)
            {
                instance = Instantiate(model.Prefab, m.Root, false);
                // Prefab sans composant d'état : teinté entièrement si demandé.
                if (instance.GetComponentInChildren<IPositionMarkerView>(true) == null && model.Tint)
                    instance.AddComponent<MarkerTint>().SetRenderers(instance.GetComponentsInChildren<Renderer>(true));
                foreach (Collider c in instance.GetComponentsInChildren<Collider>(true)) Destroy(c);
            }
            else instance = BuiltInDeviceModels.Create(profile, m.Root, _style.MaterialTemplate);

            m.Model = instance.transform;
            m.Model.localPosition = Vector3.zero;
            m.Model.localRotation = model != null ? Quaternion.Euler(model.Euler) : Quaternion.identity;
            float scale = model != null ? model.Scale : 1f;
            m.ModelScale = Vector3.one * (customPrefab ? scale : _style.BuiltInModelScale * scale);
            m.Aim = model != null ? Quaternion.Euler(model.AimEuler) : Quaternion.identity;
            m.Views = instance.GetComponentsInChildren<IPositionMarkerView>(true);

            Material template = _style.MaterialTemplate;
            m.Beam = BuiltInDeviceModels.Part(PrimitiveType.Cylinder, template, p.Color, m.Pivot, "Beam", null);
            m.Hit = BuiltInDeviceModels.Part(PrimitiveType.Sphere, template, p.Color, m.Pivot, "Hit", null);
            m.Stem = BuiltInDeviceModels.Part(PrimitiveType.Cylinder, template, _style.Neutral, m.Pivot, "Stem", null);
            m.Ring = BuiltInDeviceModels.Part(PrimitiveType.Cylinder, template, p.Color, m.Pivot, "Ring", null);
            m.RingFill = BuiltInDeviceModels.Part(PrimitiveType.Cylinder, template, p.Color, m.Pivot, "RingFill", null);

            PreviewLayer.Apply(m.Pivot.gameObject, Layer);
            return m;
        }

        private void Remove(Participant p)
        {
            if (!_markers.TryGetValue(p, out Marker m)) return;
            if (m.Pivot != null) Destroy(m.Pivot.gameObject);
            _markers.Remove(p);
        }

        private void Clear()
        {
            _stale.Clear();
            _stale.AddRange(_markers.Keys);
            foreach (Participant p in _stale) Remove(p);
            if (_camera != null) _camera.enabled = false;
        }

        private void LayoutRoom()
        {
            float y = _area.WallCenter.y, w = _area.WallWidth, h = _area.WallHeight;
            const float frame = 0.06f;
            _floor.localScale = new Vector3(_style.FloorSize, 0.01f, _style.FloorSize);
            _floor.localPosition = new Vector3(0f, -0.01f, -_style.FloorSize * 0.25f);
            _wallPanel.localPosition = new Vector3(0f, y, 0.03f);
            _wallPanel.localScale = new Vector3(w, h, 0.02f);
            _wallTop.localPosition = new Vector3(0f, y + h * 0.5f, 0f);
            _wallTop.localScale = new Vector3(w + frame * 2f, frame, frame);
            _wallBottom.localPosition = new Vector3(0f, y - h * 0.5f, 0f);
            _wallBottom.localScale = new Vector3(w + frame * 2f, frame, frame);
            _wallLeft.localPosition = new Vector3(-w * 0.5f, y, 0f);
            _wallLeft.localScale = new Vector3(frame, h, frame);
            _wallRight.localPosition = new Vector3(w * 0.5f, y, 0f);
            _wallRight.localScale = new Vector3(frame, h, frame);
        }

        private readonly List<Vector3> _framePoints = new List<Vector3>();

        // Caméra derrière les joueurs, en plongée fixe (Pitch), placée au plus près pour que tous les points tiennent dans
        // l'image, centrés (glisse en douceur).
        private void FrameCamera()
        {
            _camera.fieldOfView = _style.FieldOfView;
            Quaternion look = Quaternion.Euler(_style.Pitch, 0f, 0f);
            Vector3 right = look * Vector3.right, up = look * Vector3.up, forward = look * Vector3.forward;
            const float fill = 0.92f; // marge dans l'image
            float tanV = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * fill;
            float tanH = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * _camera.aspect * fill;

            // Centre : milieu de l'emprise vue de face (axes droite / haut de la caméra), profondeur moyenne.
            float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue, sumF = 0f;
            foreach (Vector3 p in _framePoints)
            {
                float r = Vector3.Dot(p, right), u = Vector3.Dot(p, up);
                minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
                minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
                sumF += Vector3.Dot(p, forward);
            }
            Vector3 target = right * ((minR + maxR) * 0.5f) + up * ((minU + maxU) * 0.5f) + forward * (sumF / _framePoints.Count);

            // Recul nécessaire : chaque point dans le cône de vision (caméra en target - forward × distance).
            float distance = 0.5f;
            foreach (Vector3 p in _framePoints)
            {
                Vector3 d = p - target;
                float need = Mathf.Max(Mathf.Abs(Vector3.Dot(d, right)) / tanH, Mathf.Abs(Vector3.Dot(d, up)) / tanV) - Vector3.Dot(d, forward);
                distance = Mathf.Max(distance, need);
            }
            Vector3 position = target - forward * distance;

            Vector3 worldTarget = _space.TransformPoint(target), worldPosition = _space.TransformPoint(position);
            float k = _cameraPlaced && _style.FollowSpeed > 0f ? 1f - Mathf.Exp(-_style.FollowSpeed * Time.unscaledDeltaTime) : 1f;
            _cameraPlaced = true;
            _cameraPosition = Vector3.Lerp(_cameraPosition, worldPosition, k);
            _cameraTarget = Vector3.Lerp(_cameraTarget, worldTarget, k);
            _camera.transform.position = _cameraPosition;
            _camera.transform.LookAt(_cameraTarget);
        }
    }

    /// <summary>Calque dédié de l'aperçu.</summary>
    public static class PreviewLayer
    {
        /// <summary>Calque demandé, ou (-1) le dernier calque sans nom (de 31 à 8) ; à défaut, Default avec un avertissement.</summary>
        public static int Resolve(int requested)
        {
            if (requested >= 0 && requested <= 31) return requested;
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            Debug.LogWarning("[Mooveo.Positioning] Aucun calque libre pour l'aperçu des positions : il est rendu sur le calque " +
                             "Default (isolé seulement par la distance). Réservez un calque dans PlayAreaPreviewStyle.Layer.");
            return 0;
        }

        public static void Apply(GameObject root, int layer)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
