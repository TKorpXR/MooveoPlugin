using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Lecture d'un réglage DIRECTEMENT dans <c>GlobalSettings.json</c>, sans passer par l'instance de
/// <see cref="GlobalSettings"/>.
///
/// Sert au tout début du lancement (<see cref="SteamVRBootstrap"/>, <see cref="HardwareProfiles"/>) :
/// SteamVR est démarré par le chargeur XR AVANT la première scène, donc avant que le MonoBehaviour
/// GlobalSettings n'ait lu son fichier. Ensuite, c'est toujours l'instance qui fait foi.
///
/// Lecture seule, et tolérante : un fichier absent ou illisible rend null, jamais d'exception.
/// </summary>
public static class GlobalSettingsFile
{
    /// <summary>Chemin du fichier, identique à celui de <c>GlobalSettings.Awake</c>. Fil principal seulement.</summary>
    public static string Path => System.IO.Path.Combine(GlobalSettings.Core.GlobalSettings.GetAppRootPath(), "GlobalSettings.json");

    /// <summary>Valeur texte d'un <c>ObservableSetting</c> (<c>{"Nom": {"_value": …}}</c>), ou null.</summary>
    public static string Read(string settingName)
    {
        try
        {
            string path = Path;
            if (!File.Exists(path)) return null;
            JToken value = JObject.Parse(File.ReadAllText(path))[settingName]?["_value"];
            return value?.Type == JTokenType.String ? (string)value : value?.ToString();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GlobalSettingsFile] Lecture de « {settingName} » impossible : {e.Message}");
            return null;
        }
    }
}
