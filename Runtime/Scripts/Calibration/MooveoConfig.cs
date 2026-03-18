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
}
