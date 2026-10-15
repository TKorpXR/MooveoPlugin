using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Class Serializable qui enregistre toutes les infos relatives à la calibration.
/// <see cref="Points"/> est à la liste des coordonées des points placés lors de la calibration, qui permette l'affichage correcte de l'activité
/// </summary>
[System.Serializable]
public class MooveoConfig
{
    public List<Vector3> Points = new List<Vector3>();
    public List<Vector3> Normals = new List<Vector3>(); //orientation du joueur -> permet de determiner la normale au mur

    /// <summary>
    /// Matériel de suivi avec lequel la calibration a été faite (nom de <see cref="HardwareProfile"/>).
    /// ⚠ Les points sont en coordonnées de l'univers de suivi : stations de base et casque PICO en
    /// définissent deux SANS RAPPORT. Une calibration faite avec l'un pose le mur n'importe où avec
    /// l'autre, sans aucune erreur. Vide = fichier antérieur aux profils = Vive Trackers.
    /// </summary>
    public string TrackingHardware = "";

    /// <summary>
    /// Profil de la calibration enregistrée.
    /// ⚠ Vive Trackers EN DUR, et non <c>HardwareProfiles.Default</c> : un fichier sans ce champ date
    /// d'avant les profils, il a donc été fait avec les trackers. C'est un fait sur le passé, pas un
    /// défaut — changer le profil de production ne doit pas réinterpréter les anciennes calibrations.
    /// </summary>
    public HardwareProfile RecordedHardware =>
        HardwareProfiles.TryParse(TrackingHardware, out HardwareProfile p) ? p : global::HardwareProfile.ViveTrackers;

    /// <summary>Vrai si la calibration a été faite avec le matériel du profil courant.</summary>
    public bool MatchesCurrentHardware => RecordedHardware == HardwareProfiles.Current.Id;

    /// <summary>
    /// ⚠ Tous les lecteurs de la calibration (<c>CalculateCenter</c> / <c>CalculateLeftAndRight</c>,
    /// <c>HardwareChecks.CalibrationCheck</c>…) ne savent lire QUE le format à trois points
    /// Gauche / Centre / Droite, par les indices 0/1/2. Un affinage en produit neuf (trois lignes ×
    /// trois colonnes, cf. <c>UICalibrationToolkit.InitMarksPositions</c> : 0/3/6 = gauche, 1/4/7 =
    /// centre, 2/5/8 = droite). Lus bruts, seuls ceux de la ligne du milieu comptaient : la moyenne
    /// par colonne que la validation venait de vérifier était perdue, sans aucun message.
    /// D'où cette réduction, SEULE source du calcul : validation, aperçu, écriture et lecture du
    /// fichier passent tous par elle, et ne peuvent donc pas diverger.
    /// </summary>
    public const int RefinePointCount = 9;

    /// <summary>Moyenne par colonne les neuf points d'un affinage en trois. Tout autre nombre de
    /// points ressort inchangé (copie).</summary>
    public static List<Vector3> ReducePoints(List<Vector3> points)
    {
        if (points == null) return new List<Vector3>();
        if (points.Count < RefinePointCount) return new List<Vector3>(points);
        return new List<Vector3>
        {
            (points[0] + points[3] + points[6]) / 3f, // Colonne Gauche
            (points[1] + points[4] + points[7]) / 3f, // Colonne Centre
            (points[2] + points[5] + points[8]) / 3f  // Colonne Droite
        };
    }

    /// <summary>Même réduction pour les normales, renormalisées.</summary>
    public static List<Vector3> ReduceNormals(List<Vector3> normals)
    {
        if (normals == null) return new List<Vector3>();
        if (normals.Count < RefinePointCount) return new List<Vector3>(normals);
        return new List<Vector3>
        {
            (normals[0] + normals[3] + normals[6]).normalized,
            (normals[1] + normals[4] + normals[7]).normalized,
            (normals[2] + normals[5] + normals[8]).normalized
        };
    }

    /// <summary>Ramène la configuration au format à trois points. Idempotent.</summary>
    public void ReduceToThreePoints()
    {
        Points = ReducePoints(Points);
        Normals = ReduceNormals(Normals);
    }
}
