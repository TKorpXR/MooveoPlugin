using UnityEngine;

namespace Mooveo.Positioning
{
    /// <summary>
    /// Repère de l'aire de jeu, construit sur l'écran calibré : x = position latérale le long de l'écran (positive à droite
    /// pour un joueur qui fait face à l'écran), y = hauteur (celle de l'espace de suivi), z = distance devant l'écran
    /// (positive du côté des joueurs). Tout ce qui raisonne « à gauche / à droite de l'autre joueur » ou « près / loin de
    /// l'écran » passe par ce repère, indépendant de l'orientation de la pièce dans l'espace de suivi.
    /// </summary>
    public readonly struct PlayAreaSpace
    {
        /// <summary>Écran utilisé sans calibration : 3 × 1,7 m, centre à 1,3 m de haut, 2,5 m devant l'origine du suivi.</summary>
        public static PlayAreaSpace Default => new PlayAreaSpace(new Vector3(0f, 1.3f, 2.5f), Vector3.right, 3f, 1.7f, false);

        /// <summary>Centre de l'écran (espace de suivi).</summary>
        public Vector3 WallCenter { get; }
        /// <summary>Axe « droite » des joueurs, horizontal et normé (espace de suivi).</summary>
        public Vector3 Right { get; }
        /// <summary>Sens joueurs → écran, horizontal et normé (espace de suivi).</summary>
        public Vector3 Forward { get; }
        public float WallWidth { get; }
        public float WallHeight { get; }
        /// <summary>Faux : écran par défaut, aucune calibration chargée.</summary>
        public bool IsCalibrated { get; }

        public PlayAreaSpace(Vector3 wallCenter, Vector3 right, float wallWidth, float wallHeight, bool isCalibrated)
        {
            right.y = 0f;
            Right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
            Forward = Vector3.Cross(Right, Vector3.up);
            WallCenter = wallCenter;
            WallWidth = Mathf.Max(0.1f, wallWidth);
            WallHeight = Mathf.Max(0.1f, wallHeight);
            IsCalibrated = isCalibrated;
        }

        /// <summary>Repère de l'écran calibré ; sans calibration (ou sans aire), <see cref="Default"/>.</summary>
        public static PlayAreaSpace From(MooveoPlayArea area)
        {
            if (area == null || !area.HasPlayArea) return Default;
            return new PlayAreaSpace(area.PlayAreaCenter, area.PlayAreaRight, area.PlayAreaWidth, area.PlayAreaHeight, true);
        }

        /// <summary>Point de l'espace de suivi → repère de l'aire (x latéral, y hauteur, z distance devant l'écran).</summary>
        public Vector3 ToArea(Vector3 tracking)
        {
            Vector3 d = tracking - WallCenter;
            return new Vector3(Vector3.Dot(d, Right), tracking.y, -Vector3.Dot(d, Forward));
        }

        /// <summary>Repère de l'aire → point de l'espace de suivi.</summary>
        public Vector3 FromArea(Vector3 area)
        {
            Vector3 p = WallCenter + Right * area.x - Forward * area.z;
            p.y = area.y;
            return p;
        }

        /// <summary>Orientation de l'espace de suivi → repère de l'aire (z de l'aire vers l'écran).</summary>
        public Quaternion ToArea(Quaternion tracking) => Quaternion.Inverse(Quaternion.LookRotation(Forward, Vector3.up)) * tracking;

        /// <summary>Position latérale d'un point de l'espace de suivi (m, positive à droite).</summary>
        public float Lateral(Vector3 tracking) => Vector3.Dot(tracking - WallCenter, Right);
    }
}
