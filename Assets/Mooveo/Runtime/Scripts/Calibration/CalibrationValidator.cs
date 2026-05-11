using System.Collections.Generic;
using UnityEngine;

public static class CalibrationValidator
{
    /// <summary>
    /// Master function : Exécute la suite de tests géométriques.
    /// </summary>
    public static bool IsCalibrationValid(List<Vector3> points, float maxAlign = 0.05f, float maxSym = 0.15f, float maxHoriz = 0.2f)    
    {
        if (points.Count < 3) return false;

        Vector3 p1 = points[0];
        Vector3 p2 = points[1];
        Vector3 p3 = points[2];

        float totalWidth = Vector3.Distance(p1, p3);
        if (totalWidth < 0.1f) 
        {
            Debug.LogWarning("Calibration Échouée : Largeur du mur insuffisante.");
            return false;
        }

        // Exécution en cascade
        return CheckLinearity(p1, p2, p3, totalWidth, maxAlign) &&
               CheckSymmetry(p1, p2, p3, totalWidth, maxSym) &&
               CheckHorizontality(p1, p2, p3, totalWidth, maxHoriz);
    }
    
    /// <summary>
    /// Étape 1 : Vérifie que P2 n'est pas trop éloigné du segment P1-P3 (Mur bombé/creusé).
    /// </summary>
    public static bool CheckLinearity(Vector3 p1, Vector3 p2, Vector3 p3, float width, float threshold)
    {
        Vector3 lineDir = (p3 - p1).normalized;
        Vector3 v2 = p2 - p1;
        float projectionDistance = Vector3.Dot(v2, lineDir);
        Vector3 closestPointOnLine = p1 + lineDir * projectionDistance;
        
        float deviation = Vector3.Distance(p2, closestPointOnLine);
        bool isValid = deviation <= width * threshold;

        if (!isValid) 
            Debug.LogWarning($"[Validation] Linéarité incorrecte : déviation de {deviation:F3}m (seuil: {width * threshold:F3}m).");
        
        return isValid;
    }

    /// <summary>
    /// Étape 2 : Vérifie que P2 est bien situé vers le milieu du segment (Équidistance).
    /// </summary>
    public static bool CheckSymmetry(Vector3 p1, Vector3 p2, Vector3 p3, float width, float threshold)
    {
        float dist12 = Vector3.Distance(p1, p2);
        float dist23 = Vector3.Distance(p2, p3);
        float symmetryDelta = Mathf.Abs(dist12 - dist23);
        
        bool isValid = symmetryDelta <= width * threshold;

        if (!isValid)
            Debug.LogWarning($"[Validation] Asymétrie excessive : écart de {symmetryDelta:F3}m.");

        return isValid;
    }

    /// <summary>
    /// Étape 3 : Vérifie que le mur n'est pas trop incliné verticalement.
    /// </summary>
    public static bool CheckHorizontality(Vector3 p1, Vector3 p2, Vector3 p3, float width, float threshold)
    {
        float heightDelta = Mathf.Max(Mathf.Abs(p1.y - p2.y), Mathf.Abs(p3.y - p2.y));
        bool isValid = heightDelta <= width * threshold;

        if (!isValid)
            Debug.LogWarning($"[Validation] Horizontalité hors seuil : écart Y de {heightDelta:F3}m.");

        return isValid;
    }

    /// <summary>
    /// Test de cohérence des normales (Orientation des trackers).
    /// </summary>
    public static bool AreNormalsConsistent(List<Vector3> normals, float minDotProduct = 0.85f)
    {
        if (normals.Count < 3) return false;

        float dot12 = Vector3.Dot(normals[0], normals[1]);
        float dot13 = Vector3.Dot(normals[0], normals[2]);

        bool isValid = dot12 >= minDotProduct && dot13 >= minDotProduct;

        if (!isValid)
            Debug.LogWarning($"[Validation] Normales incohérentes (Dot12: {dot12:F2}, Dot13: {dot13:F2}).");

        return isValid;
    }
}
