using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mooveo.Positioning
{
    /// <summary>
    /// Apparence de l'aperçu 3D des positions (<see cref="PlayAreaPreview"/>) et de son interface
    /// (<see cref="PlayAreaPreviewElement"/>) : modèle d'appareil par profil matériel, couleurs, repères, cadrage, calque,
    /// feuille de style et textes. Tout est facultatif : un champ vide garde le rendu par défaut.
    /// </summary>
    [CreateAssetMenu(menuName = "Mooveo/Positionnement/Style de l'aperçu", fileName = "PlayAreaPreviewStyle")]
    public class PlayAreaPreviewStyle : ScriptableObject
    {
        /// <summary>Modèle affiché pour un profil matériel.</summary>
        [Serializable]
        public class DeviceModel
        {
            public HardwareProfile Profile;
            [Tooltip("Prefab du modèle ; vide = modèle stylisé intégré au module. Un composant IPositionMarkerView (ex. MarkerTint) " +
                     "dans le prefab reçoit l'état du joueur ; sinon, tous ses renderers prennent la couleur du joueur si Tint.")]
            public GameObject Prefab;
            [Tooltip("Rotation du modèle par rapport à la pose suivie (degrés).")]
            public Vector3 Euler;
            [Min(0.01f)] public float Scale = 1f;
            [Tooltip("Axe de visée de l'appareil (rotation appliquée à l'avant de la pose) : sens du rayon vers l'écran.")]
            public Vector3 AimEuler;
            [Tooltip("Teinter tous les renderers du prefab à la couleur du joueur (s'il ne gère pas lui-même son état).")]
            public bool Tint = true;
        }

        [Header("Appareils")]
        public List<DeviceModel> DeviceModels = new List<DeviceModel>();
        [Tooltip("Échelle des modèles intégrés (un appareil à taille réelle est minuscule à l'écran).")]
        [Min(0.1f)] public float BuiltInModelScale = 2.2f;

        [Header("Rendu")]
        [Min(64)] public int Width = 1280;
        [Min(64)] public int Height = 720;
        [Range(1, 8)] public int Msaa = 4;
        [Tooltip("Calque du studio de l'aperçu ; -1 = premier calque sans nom (8 à 31). Les autres caméras ne le dessinent pas.")]
        [Range(-1, 31)] public int Layer = -1;
        [Tooltip("Position du studio dans la scène (loin du jeu).")]
        public Vector3 StudioOrigin = new Vector3(0f, -900f, 0f);
        [Tooltip("Matériau de base des repères et des modèles intégrés (couleur dans _BaseColor) ; vide = URP Unlit.")]
        public Material MaterialTemplate;

        [Header("Couleurs")]
        public Color Background = new Color(0.08f, 0.11f, 0.22f);
        public Color Floor = new Color(0.26f, 0.32f, 0.5f);
        public Color Wall = new Color(0.16f, 0.22f, 0.42f);
        public Color Frame = Color.white;
        public Color Neutral = Color.white;
        [Tooltip("Mélange de la couleur du joueur vers le fond pour une position non validée.")]
        [Range(0f, 1f)] public float DimAmount = 0.55f;

        [Header("Repères")]
        public bool ShowBeam = true;
        public bool ShowFloorRing = true;
        public bool ShowStem = true;
        [Min(0.002f)] public float BeamWidth = 0.025f;
        [Min(0.05f)] public float RingRadius = 0.3f;
        [Tooltip("Amplitude de la pulsation d'un appareil dont la position n'est pas validée (0 = aucune).")]
        [Range(0f, 0.5f)] public float PulseWhileFree = 0.12f;
        [Min(0f)] public float FloorSize = 9f;

        [Header("Cadrage")]
        [Range(5f, 85f)] public float Pitch = 35f;
        [Range(15f, 90f)] public float FieldOfView = 42f;
        [Tooltip("Marge (m) autour des anneaux des appareils.")]
        [Min(0f)] public float Margin = 0.2f;
        [Tooltip("Profondeur (m) toujours cadrée devant l'écran, même sans appareil.")]
        [Min(0.2f)] public float MinDepth = 1.5f;
        [Tooltip("Vitesse de glissement de la caméra (0 = immédiat).")]
        [Min(0f)] public float FollowSpeed = 4f;

        [Header("Interface")]
        [Tooltip("Feuille de style ajoutée après celle du module (classes .mooveo-positions*).")]
        public StyleSheet StyleSheet;
        [Tooltip("Ne pas charger la feuille de style par défaut du module.")]
        public bool ReplaceDefaultStyleSheet;
        public string WallText = "ÉCRAN";
        [TextArea] public string FreeText = "En position de jeu,\nappuyez sur la gâchette";
        [TextArea] public string LockedText = "Position validée";
        [TextArea] public string RefusedText = "Ne bougez pas\npendant l'appui !";
        [TextArea] public string AutoLockText = "Ne bougez plus…";
        [TextArea] public string UntrackedText = "Appareil non suivi";

        /// <summary>Modèle déclaré pour ce profil, ou null (modèle intégré).</summary>
        public DeviceModel ModelFor(HardwareProfile profile)
        {
            foreach (DeviceModel model in DeviceModels)
                if (model != null && model.Profile == profile) return model;
            return null;
        }

        /// <summary>Style par défaut (sans asset).</summary>
        public static PlayAreaPreviewStyle CreateDefault()
        {
            var style = CreateInstance<PlayAreaPreviewStyle>();
            style.hideFlags = HideFlags.DontSave;
            style.name = "PlayAreaPreviewStyle (défaut)";
            return style;
        }
    }
}
