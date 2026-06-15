using System.IO;
using UnityEngine;

/// <summary>
/// Gère la sauvegarde et le chargement des données de calibrations
/// Le json sauvegardé est deserialisé grace a la class <see cref="MooveoConfig"/>
/// </summary>
public static class MooveoConfigManager
{
    private static string FilePath =>
        Path.Combine(Application.persistentDataPath, "calibration.json");

    /// <summary>
    /// Verifie si le fichier de calibration existe (et donc qu'une calibration a déjà été faite)
    /// </summary>
    /// <returns>true si le fichier est trouvé, false si il n'existe pas a cet endroit</returns>
    public static bool Exists() => File.Exists(FilePath);

    /// <summary>
    /// Sauvegarde la calibration au format .json
    /// </summary>
    /// <param name="config">La class permettant de serializer et deserializer, elle contient toutes les informations relatives a une calibration</param>
    public static void Save(MooveoConfig config)
    {
        string json = JsonUtility.ToJson(config, true);
        File.WriteAllText(FilePath, json);
        Debug.Log("CALIBRATION : Config saved at: " + FilePath);
    }

    /// <summary>
    /// Charge les données de calibration se trouvant dans le .json
    /// </summary>
    /// <returns>retourne un <see cref="MooveoConfig"/> contenant des variables lisibles par les autres scripts</returns>
    public static MooveoConfig Load()
    {
        if (!Exists()) return new MooveoConfig();

        string json = File.ReadAllText(FilePath);
        MooveoConfig config = JsonUtility.FromJson<MooveoConfig>(json);
        Debug.Log($"CALIBRATION : Load Config from json at path : {FilePath}");
        return config;
    }
}