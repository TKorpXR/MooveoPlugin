using System;
using System.Collections.Generic;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace GlobalSettings.Core
{
    [DefaultExecutionOrder(-100)] //Permet d'executer ce script en premier
public class GlobalSettings: MonoBehaviour
{
    public static GlobalSettings Instance;
    public static Vector2 ScreenSize = new Vector2(Screen.width, Screen.height);
    public static float ScreenRatio = 0f;
    public static Camera MainCamera;
    
    [Header("General Settings")]
    
    [Tooltip("Indique si le mode headless est actif (sans casque => implique de n'avoir que des Tracker)")]
    public ObservableSetting<bool> Headless = new ObservableSetting<bool>() { Value = false };
    
    [Tooltip(
        "Seuil de tolérance (en % de la largeur du mur) pour accepter que les points forment une ligne droite exclusive. Une valeur de 0.05 équivaut à un niveau de tolérance de 5%.")]
    [Range(0f, 1f)]
    [SerializeField]
    private float LinearityThresholdEditor = 0.05f;

    [Tooltip(
        "Seuil de tolérance (en % de la largeur du mur) pour accepter que l'espacement entre Gauche->Centre et Centre->Droite soit symétrique. Une valeur de 0.05 équivaut à 5% de tolérance.")]
    [Range(0f, 1f)]
    [SerializeField]
    private float SymmetryThresholdEditor = 0.15f;

    [HideInInspector] public ObservableSetting<float> LinearityThreshold = new ObservableSetting<float>() { Value = 0.05f };
    [HideInInspector] public ObservableSetting<float> SymmetryThreshold = new ObservableSetting<float>() { Value = 0.05f };
    
    public ObservableSetting<string> DefaultExportPath = new ObservableSetting<string>()
        { Value = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Export") };
    
    public ObservableSetting<string> HotFolderEXEPath = new ObservableSetting<string>() {Value = "C:/DNP/HotFolderPrint/HotFolderPrint.exe"};
    public ObservableSetting<string> SteamVREXEPath = new ObservableSetting<string>() {Value = "C:/Program Files (x86)/Steam/steamapps/common/SteamVR/bin/win64/vrstartup.exe"};
    public ObservableSetting<string> EosUtilityEXEPath = new ObservableSetting<string>() {Value = "C:/Program Files (x86)/Canon/EOS Utility/EOS Utility.exe"};

    [Header("UI Settings")]
    
    [Tooltip("Distance a ne pas depasser entre l'averagePos et la currentPos durant la pahse de calibration (en mètres))")]
    public ObservableSetting<float> DeltaPrecisionCalibration = new ObservableSetting<float>() { Value = 0.03f };
    
    [Tooltip("Facteur d’échelle appliqué au curseur du spray dans la scène (réticule de visée).")] 
    public ObservableSetting<float> CursorFactorScale = new ObservableSetting<float>() { Value = 1.5f };

    [Tooltip("Seuil a depassé pour considérer que le cursor est en mode 'dragging'")]
    public ObservableSetting<float> DragThreshold = new ObservableSetting<float>() { Value = 6f };
    
    public ObservableSetting<float> CursorRadiusMIN = new ObservableSetting<float>() { Value = 0.005f };
    public ObservableSetting<float> CursorRadiusMAX = new ObservableSetting<float>() { Value = 0.15f };
    
    protected string SettingsInternalPath => Path.Combine(Application.persistentDataPath, "global_settings.json");
    protected string SettingsExternalPath => Path.Combine(Application.streamingAssetsPath, "global_settings.json");

    protected string _currentLoadedPath;

    [System.Serializable]
    public class PrintFormat
    {
        public string Name;
        public Vector2Int TargetResolution;
    }

    protected void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        
        
#if !UNITY_EDITOR
        LoadFromJson();
#endif
        if (!Directory.Exists(DefaultExportPath.Value))
        {
            Debug.Log($"Path DefaultExportPath :{DefaultExportPath.Value} not found, creating a new path to " +
                      $"{Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Export")}");
            DefaultExportPath.Value = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Export");
            if(!Directory.Exists(DefaultExportPath.Value))
                Directory.CreateDirectory(DefaultExportPath.Value);
        }
    }

    #if UNITY_EDITOR
    protected void OnValidate()
    {
        Headless.ForceNotify();
        DefaultExportPath.ForceNotify();
        HotFolderEXEPath.ForceNotify();
        SteamVREXEPath.ForceNotify();
        EosUtilityEXEPath.ForceNotify();
        DeltaPrecisionCalibration.ForceNotify();
        CursorFactorScale.ForceNotify();
        DragThreshold.ForceNotify();
        CursorRadiusMIN.ForceNotify();
        CursorRadiusMAX.ForceNotify();
    }
    #endif

    public void SaveToJson(string path = null)
    {
        if (string.IsNullOrEmpty(path)) path = _currentLoadedPath ?? SettingsExternalPath;
        
        string json = JsonUtility.ToJson(this, true);
        
        try
        {
            // Ensure directory exists if we are saving to a specific path
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            
            File.WriteAllText(path, json);
            Debug.Log("GlobalSettings saved to: " + path);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save GlobalSettings to {path}: {e.Message}");
        }
    }

    public void LoadFromJson()
    {
        string path = null;
        
        if (File.Exists(SettingsExternalPath))
        {
            path = SettingsExternalPath;
        }
        else if (File.Exists(SettingsInternalPath))
        {
            path = SettingsInternalPath;
        }
        
        if (path != null)
        {
            _currentLoadedPath = path;
            LoadFromPath(path);
        }
        else
        {
            
            Debug.Log("GlobalSettings not found. Creating default...");

            try
            {
                // Try creating in StreamingAssets
                if (!Directory.Exists(Application.streamingAssetsPath)) 
                    Directory.CreateDirectory(Application.streamingAssetsPath);

                path = SettingsExternalPath;
                SaveToJson(path);
                _currentLoadedPath = path;
            }
            catch (Exception)
            {
                // Fallback to PersistentData
                path = SettingsInternalPath;
                SaveToJson(path);
                _currentLoadedPath = path;
            }
        }
    }

    protected void LoadFromPath(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            JsonUtility.FromJsonOverwrite(json, this);
            Debug.Log("GlobalSettings Loaded from: " + path);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load GlobalSettings from {path}: {e.Message}");
        }
    }
    
    
}
}

