using System;
using System.Collections.Generic;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace GlobalSettings.Core
{
/// <summary>
/// Réglages SYSTÈME du SDK Mooveo : calibration, tracking, profil matériel, curseur, chemins des
/// logiciels tiers et des dossiers d'échange. Aligné sur la classe GlobalSettings de GraffWall.
///
/// Fichier unique : <c>GlobalSettings.json</c> à la RACINE de l'application (<see cref="GetAppRootPath"/>),
/// en éditeur comme en build. ⚠ Jamais persistentDataPath ni StreamingAssets : SteamVRBootstrap et
/// <c>GlobalSettingsFile</c> lisent ce même fichier AVANT le chargement de toute scène (le
/// chargeur XR démarre SteamVR très tôt), ils doivent le trouver au même endroit.
///
/// Chargement « Smart Merge » (<see cref="LoadSettings"/>) : les valeurs du JSON écrasent celles du
/// code, puis le fichier est RÉÉCRIT aussitôt — un réglage ajouté au code apparaît ainsi dans le JSON
/// du client au lancement suivant, avec sa valeur par défaut, sans rien perdre de l'existant.
///
/// Réglages d'un JEU : soit un composant de réglages à part (cf. ExampleProjectSettings), soit une
/// classe dérivée — JsonUtility sérialise aussi les champs ajoutés, et les points de surcharge
/// (<see cref="ApplyDefaultPaths"/>, <see cref="RequiredDirectories"/>, <see cref="ForceNotifyAll"/>)
/// permettent d'y ajouter chemins par défaut, dossiers et notifications.
/// </summary>
[DefaultExecutionOrder(-100)] //Permet d'executer ce script en premier
public class GlobalSettings : MonoBehaviour
{
    public const string FileName = "GlobalSettings.json";

    public static GlobalSettings Instance;
    public static Vector2 ScreenSize = new Vector2(Screen.width, Screen.height);
    public static float ScreenRatio = 0f;
    public static Camera MainCamera;

    [Header("General Settings")]

    [Tooltip("Indique si le mode headless est actif (sans casque => implique de n'avoir que des Tracker)")]
    public ObservableSetting<bool> Headless = new ObservableSetting<bool>() { Value = false };

    [Tooltip("Indique si le mode Admin est actif (Simulateur Souris/Clavier sans casque VR)")]
    public ObservableSetting<bool> Admin = new ObservableSetting<bool>() { Value = false };

    [Tooltip("Matériel de suivi choisi sur ce poste (nom d'un HardwareProfile). VIDE = jamais choisi : " +
             "le choix est proposé au lancement. Lu et écrit par HardwareProfiles, EN BUILD SEULEMENT — " +
             "en éditeur ce fichier est versionné et sert de source au package client.")]
    public ObservableSetting<string> TrackingHardware = new ObservableSetting<string>() { Value = "" };

    [Header("Calibration")]

    [Tooltip("Distance a ne pas depasser entre l'averagePos et la currentPos durant la phase de calibration (en mètres))")]
    public ObservableSetting<float> DeltaPrecisionCalibration = new ObservableSetting<float>() { Value = 0.05f };

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

    [Header("Curseur")]

    [Tooltip("Facteur d'échelle appliqué au curseur dans la scène (réticule de visée).")]
    public ObservableSetting<float> CursorFactorScale = new ObservableSetting<float>() { Value = 1.4f };

    [Tooltip("Seuil a depassé pour considérer que le cursor est en mode 'dragging'")]
    public ObservableSetting<float> DragThreshold = new ObservableSetting<float>() { Value = 6f };

    [Tooltip("Rayon du curseur au plus PRÈS de la surface (calibration et jeu).")]
    public ObservableSetting<float> PaintingRadiusMIN = new ObservableSetting<float>() { Value = 0.0025f };
    [Tooltip("Rayon du curseur au plus LOIN de la surface (calibration et jeu).")]
    public ObservableSetting<float> PaintingRadiusMAX = new ObservableSetting<float>() { Value = 0.15f };

    [Header("Dossiers")]

    [Tooltip("Dossier d'export. Vide = <racine>/Export/save.")]
    public ObservableSetting<string> DefaultExportPath = new ObservableSetting<string>() { Value = "" };

    [Tooltip("Dossier des journaux d'erreurs. Vide = Export/Logs.")]
    public ObservableSetting<string> LogsPath = new ObservableSetting<string>() { Value = "" };

    [Tooltip("Chemin d'accès des photos de l'appareil photo")]
    public ObservableSetting<string> CameraPicturesPath = new ObservableSetting<string>() { Value = "" };

    [Header("Impression (HotFolderPrint)")]

    public ObservableSetting<string> DefaultPrintPath = new ObservableSetting<string>()
        { Value = "C:/DNP/HotFolderPrint/Prints/s4x6/DS620" };

    // ⚠ Ces trois réglages sont DÉTECTÉS au lancement par HotFolderLocator : le nom des fichiers de
    // bordure est un identifiant généré par HotFolder à l'import, différent sur chaque poste. Une
    // valeur en dur ici ne vaudrait que pour la machine qui l'a écrite.
    [Tooltip("Détecté automatiquement — dossier des bordures de HotFolderPrint")]
    public ObservableSetting<string> DNPBannerPath = new ObservableSetting<string>() { Value = "" };
    [Tooltip("Détecté automatiquement — nom du fichier de bordure paysage")]
    public ObservableSetting<string> DNPBannerFileName_Landscape = new ObservableSetting<string>() { Value = "" };
    [Tooltip("Détecté automatiquement — nom du fichier de bordure portrait")]
    public ObservableSetting<string> DNPBannerFileName_Portrait = new ObservableSetting<string>() { Value = "" };

    [Header("Logiciels tiers")]

    public ObservableSetting<string> HotFolderEXEPath = new ObservableSetting<string>()
        { Value = "C:/DNP/HotFolderPrint/HotFolderPrint.exe" };
    public ObservableSetting<string> SteamVREXEPath = new ObservableSetting<string>()
        { Value = "C:/Program Files (x86)/Steam/steamapps/common/SteamVR/bin/win64/vrstartup.exe" };
    public ObservableSetting<string> EosUtilityEXEPath = new ObservableSetting<string>()
        { Value = "C:/Program Files (x86)/Canon/EOS Utility/EOS Utility.exe" };

    [System.Serializable]
    public class PrintFormat
    {
        public string Name;
        public Vector2Int TargetResolution;
    }

    /// <summary>Chemin du fichier JSON — calculé dans Awake().</summary>
    protected string _settingsFilePath;

    /// <summary>Chemin du fichier de réglages actuellement utilisé.</summary>
    public string SettingsFilePath => _settingsFilePath ?? Path.Combine(GetAppRootPath(), FileName);

    // =========================================================================
    // Racine de l'application
    // =========================================================================

    /// <summary>
    /// Racine de l'application, accessible sans instance. Tout code qui doit atteindre un fichier posé
    /// À CÔTÉ du jeu (GlobalSettings.json, Import/, Export/, tools/) DOIT passer par ici et jamais
    /// reconstruire le chemin depuis <c>Application.dataPath</c> : celui-ci ne désigne pas le même
    /// niveau en éditeur (.../Assets) et en build (.../Build/Jeu_Data), donc un chemin juste en
    /// éditeur vise à côté une fois le jeu compilé.
    /// - Éditeur : un niveau au-dessus d'Assets/ (= racine du projet Unity).
    /// - Build   : deux niveaux au-dessus de Build/Jeu_Data/.
    /// </summary>
    public static string AppRoot => Application.isEditor
        ? Path.GetFullPath(Path.Combine(Application.dataPath, "../"))
        : Path.GetFullPath(Path.Combine(Application.dataPath, "../../"));

    public static string GetAppRootPath() => AppRoot;

    // =========================================================================
    // Cycle de vie
    // =========================================================================

    protected virtual void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        string root = GetAppRootPath();

        // Fichier JSON maître — source unique de vérité (<racine>/GlobalSettings.json)
        _settingsFilePath = Path.Combine(root, FileName);

        ApplyDefaultPaths(root);

        foreach (string dir in RequiredDirectories(root))
        {
            if (string.IsNullOrEmpty(dir) || Directory.Exists(dir)) continue;
            try
            {
                Directory.CreateDirectory(dir);
                Debug.Log($"[GlobalSettings] Dossier créé : {dir}");
            }
            catch (Exception e)
            {
                // Signalé ensuite par les vérifications de démarrage (dossiers d'export, d'impression…).
                Debug.LogWarning($"[GlobalSettings] Impossible de créer {dir} : {e.Message}");
            }
        }

        LoadSettings();
    }

    /// <summary>
    /// Complète les chemins laissés VIDES. ⚠ Appelé deux fois : dans Awake ET après la lecture du
    /// JSON — le package client distribue un GlobalSettings.json portable dont les chemins gérés sont
    /// volontairement vides, et FromJsonOverwrite passe APRÈS les valeurs posées dans Awake.
    /// Un jeu dérivé appelle la base puis complète les siens.
    /// </summary>
    protected virtual void ApplyDefaultPaths(string root)
    {
        if (string.IsNullOrEmpty(CameraPicturesPath.Value))
            CameraPicturesPath.Value = Path.Combine(root, "Import", "Photo");
        if (string.IsNullOrEmpty(DefaultExportPath.Value))
            DefaultExportPath.Value = Path.Combine(root, "Export", "save");
        if (string.IsNullOrEmpty(LogsPath.Value))
            LogsPath.Value = Path.Combine(root, "Export", "Logs");
    }

    /// <summary>Dossiers créés au lancement s'ils manquent. Un jeu dérivé ajoute les siens.</summary>
    protected virtual IEnumerable<string> RequiredDirectories(string root)
    {
        yield return Path.Combine(root, "Import", "Photo");
        // Gabarits de bordure à importer dans HotFolderPrint (cf. HotFolderBannerTemplate).
        yield return Path.Combine(root, "Import", "HotFolder");
        yield return Path.Combine(root, "Export", "save");
        yield return Path.Combine(root, "Export", "print");
        yield return Path.Combine(root, "Export", "Logs");
    }

    // =========================================================================
    // Chargement — Smart Merge (fusion / upsert)
    // =========================================================================

    /// <summary>
    /// 1. FromJsonOverwrite : le JSON écrase les valeurs du code (les variables absentes du JSON
    ///    gardent leur valeur par défaut). Toutes les variables, Admin compris, sont lues telles quelles.
    /// 2. Complète les chemins laissés vides.
    /// 3. Réécrit immédiatement le JSON (upsert) : réglages du client + nouveautés du code.
    /// </summary>
    public virtual void LoadSettings()
    {
        if (string.IsNullOrEmpty(_settingsFilePath))
            _settingsFilePath = Path.Combine(GetAppRootPath(), FileName);

        if (File.Exists(_settingsFilePath))
        {
            try
            {
                string json = File.ReadAllText(_settingsFilePath);
                JsonUtility.FromJsonOverwrite(json, this);
                Debug.Log($"[GlobalSettings] Chargé depuis : {_settingsFilePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GlobalSettings] Erreur de chargement depuis {_settingsFilePath}: {e.Message}");
            }
        }
        else
        {
            Debug.Log($"[GlobalSettings] Aucun JSON trouvé dans {_settingsFilePath}. Valeurs par défaut du code appliquées.");
        }

        ApplyDefaultPaths(GetAppRootPath());

        SaveToJson();
        ForceNotifyAll();
    }

    /// <summary>Compatibilité avec l'ancienne API du SDK.</summary>
    public void LoadFromJson() => LoadSettings();

    // =========================================================================
    // Sauvegarde
    // =========================================================================

    public void SaveToJson(string path = null)
    {
        // Toujours le fichier de la racine — jamais AppData ni StreamingAssets.
        if (string.IsNullOrEmpty(path))
            path = SettingsFilePath;

        string json = JsonUtility.ToJson(this, true);

        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, json);
            Debug.Log($"[GlobalSettings] Sauvegardé vers : {path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GlobalSettings] Erreur de sauvegarde vers {path}: {e.Message}");
        }
    }

    public void SaveSettings()
    {
        SaveToJson();
        Debug.Log("[GlobalSettings] Paramètres sauvegardés au runtime.");
    }

    /// <summary>Notifie tous les abonnés (après chargement). Un jeu dérivé appelle la base puis notifie les siens.</summary>
    public virtual void ForceNotifyAll()
    {
        Headless.ForceNotify();
        Admin.ForceNotify();
        TrackingHardware.ForceNotify();
        DeltaPrecisionCalibration.ForceNotify();
        LinearityThreshold.ForceNotify();
        SymmetryThreshold.ForceNotify();
        CursorFactorScale.ForceNotify();
        DragThreshold.ForceNotify();
        PaintingRadiusMIN.ForceNotify();
        PaintingRadiusMAX.ForceNotify();
        DefaultExportPath.ForceNotify();
        LogsPath.ForceNotify();
        CameraPicturesPath.ForceNotify();
        DefaultPrintPath.ForceNotify();
        DNPBannerPath.ForceNotify();
        DNPBannerFileName_Landscape.ForceNotify();
        DNPBannerFileName_Portrait.ForceNotify();
        HotFolderEXEPath.ForceNotify();
        SteamVREXEPath.ForceNotify();
        EosUtilityEXEPath.ForceNotify();
    }

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        // Les seuils de calibration s'éditent par curseur [Range] : on les reporte dans les réglages.
        LinearityThreshold.Value = LinearityThresholdEditor;
        SymmetryThreshold.Value = SymmetryThresholdEditor;

        ForceNotifyAll();
    }
#endif
}
}
