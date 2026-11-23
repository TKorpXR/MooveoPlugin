using System.Collections.Generic;
using UnityEngine;

namespace Mooveo.Positioning
{
    /// <summary>État d'un participant transmis à son repère dans l'aperçu (à chaque image).</summary>
    public struct MarkerState
    {
        public Participant Participant;
        /// <summary>Couleur du joueur.</summary>
        public Color Color;
        /// <summary>Couleur du joueur atténuée (position non validée).</summary>
        public Color DimColor;
        public CaptureState State;
        /// <summary>Appareil suivi (ou position validée) : le repère est affiché.</summary>
        public bool Visible;
        /// <summary>Avancement (0-1) de la validation automatique.</summary>
        public float AutoLockProgress;
        /// <summary>Échelle de pulsation de l'appareil (1 = repos).</summary>
        public float Pulse;
        /// <summary>Position dans le repère de l'aire (x latéral, y hauteur, z devant l'écran).</summary>
        public Vector3 AreaPosition;
        public bool IsLocked => State == CaptureState.Locked;
    }

    /// <summary>
    /// Composant d'un modèle d'appareil (prefab du style) qui réagit à l'état du joueur : couleur, validation, refus…
    /// Appelé à chaque image tant que l'aperçu est actif.
    /// </summary>
    public interface IPositionMarkerView
    {
        void UpdateMarker(in MarkerState state);
    }

    /// <summary>
    /// Teinte des renderers choisis à la couleur du joueur (atténuée tant que la position n'est pas validée), sans toucher
    /// aux matériaux partagés (blocs de propriétés : _BaseColor et _Color). À poser sur un prefab de modèle d'appareil.
    /// </summary>
    public class MarkerTint : MonoBehaviour, IPositionMarkerView
    {
        [SerializeField] private Renderer[] _renderers = new Renderer[0];
        [Tooltip("Atténuer la couleur tant que la position n'est pas validée.")]
        [SerializeField] private bool _dimWhenFree = true;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _block;

        public void SetRenderers(Renderer[] renderers) => _renderers = renderers;

        public void UpdateMarker(in MarkerState state)
        {
            _block ??= new MaterialPropertyBlock();
            Color color = _dimWhenFree && !state.IsLocked ? state.DimColor : state.Color;
            foreach (Renderer r in _renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor(BaseColor, color);
                _block.SetColor(ColorId, color);
                r.SetPropertyBlock(_block);
            }
        }
    }

    /// <summary>Modèles d'appareil stylisés intégrés (formes simples), utilisés quand le style n'en fournit pas.</summary>
    public static class BuiltInDeviceModels
    {
        /// <summary>Crée le modèle du profil sous parent ; les pièces de couleur reçoivent un <see cref="MarkerTint"/>.</summary>
        public static GameObject Create(HardwareProfile profile, Transform parent, Material template)
        {
            var root = new GameObject(profile == HardwareProfile.ViveTrackers ? "ViveTracker" : "PicoController");
            root.transform.SetParent(parent, false);
            var tinted = new List<Renderer>();
            if (profile == HardwareProfile.ViveTrackers) BuildTracker(root.transform, template, tinted);
            else BuildController(root.transform, template, tinted);
            root.AddComponent<MarkerTint>().SetRenderers(tinted.ToArray());
            return root;
        }

        // Manette (type PICO 4) : poignée inclinée dans l'axe de visée, anneau de suivi au-dessus, bout blanc.
        private static void BuildController(Transform root, Material template, List<Renderer> tinted)
        {
            Transform grip = Part(PrimitiveType.Capsule, template, Color.white, root, "Grip", tinted);
            grip.localRotation = Quaternion.Euler(70f, 0f, 0f);
            grip.localPosition = new Vector3(0f, -0.02f, -0.015f);
            grip.localScale = new Vector3(0.042f, 0.06f, 0.042f);

            Transform head = Part(PrimitiveType.Sphere, template, Color.white, root, "Head", tinted);
            head.localPosition = new Vector3(0f, 0.012f, 0.03f);
            head.localScale = new Vector3(0.05f, 0.035f, 0.06f);

            // Anneau de suivi : segments en cercle, dans le plan vertical qui contient l'axe de visée.
            const int segments = 14;
            const float radius = 0.045f;
            var ringCenter = new Vector3(0f, 0.045f, 0.01f);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Transform seg = Part(PrimitiveType.Cube, template, Color.white, root, "Ring", null);
                seg.localPosition = ringCenter + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                seg.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                seg.localScale = new Vector3(0.008f, 0.012f, 2f * Mathf.PI * radius / segments * 1.15f);
            }

            Transform tip = Part(PrimitiveType.Sphere, template, Color.white, root, "Tip", null);
            tip.localPosition = new Vector3(0f, 0.012f, 0.06f);
            tip.localScale = Vector3.one * 0.022f;
        }

        // Vive Tracker : palet bombé, trois branches blanches, pastille centrale.
        private static void BuildTracker(Transform root, Material template, List<Renderer> tinted)
        {
            Transform disc = Part(PrimitiveType.Cylinder, template, Color.white, root, "Puck", tinted);
            disc.localScale = new Vector3(0.07f, 0.012f, 0.07f);
            for (int i = 0; i < 3; i++)
            {
                float a = 90f + i * 120f;
                Transform prong = Part(PrimitiveType.Cube, template, Color.white, root, "Prong", null);
                prong.localRotation = Quaternion.Euler(0f, -a, 0f);
                prong.localPosition = Quaternion.Euler(0f, -a, 0f) * new Vector3(0.042f, 0.006f, 0f);
                prong.localScale = new Vector3(0.035f, 0.02f, 0.018f);
            }
            Transform center = Part(PrimitiveType.Sphere, template, Color.white, root, "Center", null);
            center.localPosition = new Vector3(0f, 0.012f, 0f);
            center.localScale = new Vector3(0.025f, 0.01f, 0.025f);
        }

        internal static Transform Part(PrimitiveType type, Material template, Color color, Transform parent, string name, List<Renderer> tinted)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = PreviewMaterials.Get(template, color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            tinted?.Add(renderer);
            return go.transform;
        }
    }

    /// <summary>Matériaux non éclairés de l'aperçu, partagés par couleur (créés au runtime, jamais enregistrés).</summary>
    internal static class PreviewMaterials
    {
        private static readonly Dictionary<(Material, Color), Material> Cache = new Dictionary<(Material, Color), Material>();
        private static Shader _unlit;

        public static Material Get(Material template, Color color)
        {
            if (Cache.TryGetValue((template, color), out Material material) && material != null) return material;
            if (template != null) material = new Material(template);
            else
            {
                if (_unlit == null) _unlit = Shader.Find("Universal Render Pipeline/Unlit");
                if (_unlit == null) _unlit = Shader.Find("Unlit/Color");
                material = new Material(_unlit);
            }
            material.name = "Preview " + ColorUtility.ToHtmlStringRGB(color);
            material.hideFlags = HideFlags.DontSave;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            Cache[(template, color)] = material;
            return material;
        }
    }
}
