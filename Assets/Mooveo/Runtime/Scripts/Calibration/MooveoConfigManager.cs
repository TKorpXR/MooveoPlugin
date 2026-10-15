using System.IO;
using UnityEngine;

/// <summary>
/// Gère la sauvegarde et le chargement des données de calibrations
/// Le json sauvegardé est deserialisé grace a la class <see cref="MooveoConfig"/>
/// </summary>
public static class MooveoConfigManager
{
    // A mouse test uses an in-memory wall; never replace a venue's tracked calibration.
    public static bool MouseTestActive { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void EndMouseTest() => MouseTestActive = false;

    public static void BeginMouseTest()
    {
        if (GlobalSettings.Core.GlobalSettings.Instance == null || !GlobalSettings.Core.GlobalSettings.Instance.Admin.Value)
            throw new System.InvalidOperationException("Le test souris est réservé au mode administrateur.");
        MouseTestActive = true;
    }

    private static MooveoConfig MouseTestConfig() => new MooveoConfig
    {
        Points = new System.Collections.Generic.List<Vector3>
            { Vector3.left, Vector3.zero, Vector3.right },
        Normals = new System.Collections.Generic.List<Vector3>
            { Vector3.forward, Vector3.forward, Vector3.forward }
    };

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
        if (MouseTestActive) return;
        // Le fichier ne porte JAMAIS plus de trois points, quel que soit l'appelant ou l'état de
        // la calibration : ses lecteurs n'en savent pas lire davantage (cf. MooveoConfig).
        config.ReduceToThreePoints();
        // Tampon du matériel, posé ICI pour valoir quel que soit l'appelant.
        config.TrackingHardware = HardwareProfiles.Current.Id.ToString();
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
        if (MouseTestActive) return MouseTestConfig();
        if (!Exists()) return new MooveoConfig();

        string json = File.ReadAllText(FilePath);
        MooveoConfig config = JsonUtility.FromJson<MooveoConfig>(json);
        // Fichiers écrits avant le correctif : un affinage y a laissé ses neuf points bruts. Les
        // réduire ici répare les postes concernés sans leur imposer de recalibrer.
        config?.ReduceToThreePoints();
        Debug.Log($"CALIBRATION : Load Config from json at path : {FilePath}");
        return config;
    }
}
