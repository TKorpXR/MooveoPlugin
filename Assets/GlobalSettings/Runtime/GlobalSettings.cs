using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
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
    //public Input DebugConfig;
    public ObservableSetting<bool> VR = new ObservableSetting<bool>() { Value = true };

    [Header("Painting Settings")]
    public ObservableSetting<float> ThresholdDepth = new ObservableSetting<float>() { Value = 0.001f };
    
    public ObservableSetting<float> PaintingOpacityMIN = new ObservableSetting<float>() { Value = 0.1f };
    public ObservableSetting<float> PaintingOpacityMAX = new ObservableSetting<float>() { Value = 1f };
    
    public ObservableSetting<float> PaintingRadiusMIN = new ObservableSetting<float>() { Value = 0.005f };
    public ObservableSetting<float> PaintingRadiusMAX = new ObservableSetting<float>() { Value = 0.15f };
    
    public ObservableSetting<float> PaintingDepthMIN = new ObservableSetting<float>() { Value = 0.01f };
    public ObservableSetting<float> PaintingDepthMAX = new ObservableSetting<float>() { Value = 2f };
    
    [Tooltip("Valeur minimale de la gâchette à partir de laquelle le spray commence à peindre. Permet d’ignorer les légères pressions.")] 
    public ObservableSetting<float> PaintingTriggerThreshold = new ObservableSetting<float>() { Value = 0.1f };
    
    [Tooltip("Fréquence de peinture en coups par seconde. Plus la valeur est élevée, plus le spray produit de points rapprochés (densité du tracé).")]
    public ObservableSetting<float> PaintingFrequencyMIN = new ObservableSetting<float>() { Value = 50 };
    public ObservableSetting<float> PaintingFrequencyMAX = new ObservableSetting<float>() { Value = 100f };
    
    [Tooltip("Détermine la densité d'interpolation entre deux points de peinture.\n" +
             "Une valeur plus élevée augmente le nombre de points peints entre deux positions successives du pinceau, " +
             "produisant un tracé plus lisse mais plus coûteux en performance.")]
    public ObservableSetting<float> PaintingSmoothing = new ObservableSetting<float>() { Value = 5f };
    
    [Tooltip("Nombre d'échantillons utilisés pour lisser le mouvement du pinceau (plus élevé = mouvement plus fluide mais moins réactif).")] 
    public ObservableSetting<int> PaintingSmoothingSampleCount = new ObservableSetting<int>() { Value = 5 };

    [Tooltip("Vitesse de lissage de la valeur de rotation z transmise par la manette au stickerHandler pour rotate le sticker")]
    public ObservableSetting<float> StickerRotationSmoothing = new ObservableSetting<float>() { Value = 10f };
    
    [Tooltip("Vitesse de lissage de la position transmise par la manette au curseur")]
    public ObservableSetting<float> PositionSmoothSpeed = new ObservableSetting<float>() { Value = 7.5f };
    
    [Header("UI Settings")]
    
    [Tooltip("Distance a ne pas depasser entre l'averagePos et la currentPos durant la pahse de calibration (en mètres))")]
    public ObservableSetting<float> DeltaPrecisionCalibration = new ObservableSetting<float>() { Value = 0.03f };

    [Tooltip("Pourcentage de la largeur du mur que l'interface utilisateur doit occuper (0.1 = 10%).")]
    public ObservableSetting<float> UserSizePercentage = new ObservableSetting<float>() { Value = 0.25f };

    [Tooltip("Activé / Désactivé la taille du curseur bloqué en mode UI")]
    public ObservableSetting<bool> LockUICursorSize = new ObservableSetting<bool>() { Value = true };
    
    [Tooltip("Facteur d’échelle appliqué au curseur du spray dans la scène (réticule de visée).")] 
    public ObservableSetting<float> CursorFactorScale = new ObservableSetting<float>() { Value = 1.5f };

    [Tooltip("Seuil a depassé pour considérer que le cursor est en mode 'dragging'")]
    public ObservableSetting<float> DragThreshold = new ObservableSetting<float>() { Value = 6f };
    
    public ObservableSetting<string> StickersPath = new ObservableSetting<string>() { Value = "" };
    
    [Tooltip("Chemin d'accès des stickers dans le stockage")] 
    public ObservableSetting<string> BackgroundsPath = new ObservableSetting<string>() { Value = "" };

    [Tooltip("Liste des dossiers contenant les images à imprimer")]
    public ObservableList<string> PrintImagePaths = new ObservableList<string>()
    {
        ListSetter = new List<string>()
        {
            "C:/Users/smart/AppData/LocalLow/DefaultCompany/GraffWallV3/Exports",
        }

    };

    [Tooltip("Largeur de référence du mur pour le calcul de l'échelle de l'UI (Défaut: 16/9 ~= 1.77)")]
    public ObservableSetting<float> ReferenceWallWidth = new ObservableSetting<float>() { Value = 1.77778f };
    
    [Header("Photo Booth Settings")]
    public ObservableList<PrintFormat> PrintFormats = new ObservableList<PrintFormat>()
    {
        ListSetter = new List<PrintFormat>()
        {
            new PrintFormat() { Name = "DNP 4x6 Paysage (1800x1200px)", TargetResolution = new Vector2Int(1800, 1200) },
            new PrintFormat() { Name = "DNP 4x6 Portrait (1200x1800px)", TargetResolution = new Vector2Int(1200, 1800) },
            new PrintFormat() { Name = "Standard (10x15cm)", TargetResolution = new Vector2Int(1500, 1000) }, // 3:2 Ratio
            new PrintFormat() { Name = "HD (1920x1080)", TargetResolution = new Vector2Int(1920, 1080) },      // 16:9 Ratio
            new PrintFormat() { Name = "Square (Instagram)", TargetResolution = new Vector2Int(1080, 1080) }   // 1:1 Ratio
        }
    };

    public ObservableSetting<string> DefaultPrintPath = new ObservableSetting<string>() {Value = "C:/DNP/HotFolderPrint/Prints/s4x6/DS620"};

    public ObservableSetting<string> DefaultExportPath = new ObservableSetting<string>()
        { Value = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Export") };

    public ObservableSetting<string> SourceBannerPath = new ObservableSetting<string>()
        { Value = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Banners") };
    public ObservableSetting<string> DNPBannerPath = new ObservableSetting<string>()
        { Value = "C:/DNP/HotFolderPrint/Borders" };
    
    public ObservableSetting<string> DNPBannerFileName_Landscape = new ObservableSetting<string>()
        { Value = "a749bf15-cd9d-42ba-be72-f7022e152c3a" };
    public ObservableSetting<string> DNPBannerFileName_Portrait = new ObservableSetting<string>()
        { Value = "C:/DNP/HotFolderPrint/Borders" };
    
    public ObservableSetting<string> HotFolderEXEPath = new ObservableSetting<string>() {Value = "C:/DNP/HotFolderPrint/HotFolderPrint.exe"};
    public ObservableSetting<string> SteamVREXEPath = new ObservableSetting<string>() {Value = "C:/Program Files (x86)/Steam/steamapps/common/SteamVR/bin/win64/vrstartup.exe"};
    public ObservableSetting<string> EosUtilityEXEPath = new ObservableSetting<string>() {Value = "C:/Program Files (x86)/Canon/EOS Utility/EOS Utility.exe"};

    private string SettingsInternalPath => Path.Combine(Application.persistentDataPath, "global_settings.json");
    private string SettingsExternalPath => Path.Combine(Application.streamingAssetsPath, "global_settings.json");

    private string _currentLoadedPath;

    [System.Serializable]
    public class PrintFormat
    {
        public string Name;
        public Vector2Int TargetResolution;
    }

    private void Awake()
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
    private void OnValidate()
    {
        PaintingRadiusMIN.ForceNotify();
        PaintingRadiusMAX.ForceNotify();
        PaintingTriggerThreshold.ForceNotify();
        PaintingFrequencyMIN.ForceNotify();
        PaintingFrequencyMAX.ForceNotify();
        PaintingSmoothing.ForceNotify();
        PaintingSmoothingSampleCount.ForceNotify();
        UserSizePercentage.ForceNotify();
        LockUICursorSize.ForceNotify();
        CursorFactorScale.ForceNotify();
        PaintingOpacityMIN.ForceNotify();
        PaintingOpacityMAX.ForceNotify();
        PaintingDepthMIN.ForceNotify();
        PaintingDepthMAX.ForceNotify();
        StickersPath.ForceNotify();
        BackgroundsPath.ForceNotify();
        DeltaPrecisionCalibration.ForceNotify();
        ReferenceWallWidth.ForceNotify();
        VR.ForceNotify();
        DefaultPrintPath.ForceNotify();
        StickerRotationSmoothing.ForceNotify();
        PositionSmoothSpeed.ForceNotify();
        DragThreshold.ForceNotify();
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

    private void LoadFromPath(string path)
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

