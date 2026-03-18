using System;
using System.IO;
using UnityEngine;

namespace GlobalSettings.Core
{
    public static class JsonHelpers
    {
        public static void Save<T>(T data, string fileName)
        {
            string fullPath = Path.Combine(Application.persistentDataPath, fileName + ".json");
            SaveToFullPath(data, fullPath);
        }
   
        public static void SaveToFullPath<T>(T data, string fullPath)
        {
            try
            {
                string directory = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
   
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(fullPath, json);
                Debug.Log($"Fichier sauvegardé avec succès: {fullPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Erreur lors de la sauvegarde à {fullPath} : {e.Message}");
            }
        }
       
        public static T LoadFromFullPath<T>(string fullPath) where T : new()
        {
            if (File.Exists(fullPath))
            {
                try
                {
                    string json = File.ReadAllText(fullPath);
                    return JsonUtility.FromJson<T>(json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Erreur lecture à {fullPath} : {e.Message}");
                }
            }
            return new T();
        }
    }
}

