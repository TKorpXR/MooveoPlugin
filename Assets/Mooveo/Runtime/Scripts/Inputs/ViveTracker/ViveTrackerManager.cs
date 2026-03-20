using System;
using System.Collections.Generic;
using HTC.UnityPlugin.Vive;
using HTC.UnityPlugin.VRModuleManagement;
using UnityEngine;
using UnityEngine.InputSystem;

public enum contextType
{
    Any,
    Calibration,
    Game
}
public class ViveTrackerManager : MonoBehaviour
{
    // Singleton
    public static ViveTrackerManager Instance { get; private set; }
    
    [Header("Configuration")]
    [SerializeField] private GameObject _trackerPrefab;
    [SerializeField] private bool _debug = true;
    [SerializeField] private bool _useViveInputUtility = false;
    [SerializeField] private contextType _contextType = contextType.Any;
    
    private Dictionary<int, ViveTrackerPoseDriver> _activeTrackers = new Dictionary<int, ViveTrackerPoseDriver>();

    private void Awake()
    {
        // Configuration du singleton
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Update()
    {
        if (_useViveInputUtility) FindTrackersWithVIU();
        else FindTrackersWithInputSystem();
    }
    
    /// <summary>
    /// Fonction publique pour vérifier si des trackers sont connectés
    /// </summary>
    public bool AreTrackersConnected()
    {
        return _activeTrackers.Count > 0;
    }
    
    /// <summary>
    /// Fonction publique pour obtenir le nombre de trackers connectés
    /// </summary>
    public int GetConnectedTrackersCount()
    {
        return _activeTrackers.Count;
    }
    
    /// <summary>
    /// Fonction publique pour vérifier l'état des trackers (utilisé par AnyControllerChecker)
    /// </summary>
    public bool CheckTrackersStatus()
    {
        // Forcer la recherche de trackers pour mettre à jour l'état
        if (_useViveInputUtility) FindTrackersWithVIU();
        else FindTrackersWithInputSystem();
        
        return AreTrackersConnected();
    }
    
    private void FindTrackersWithVIU()
    {
        foreach (TrackerRole role in Enum.GetValues(typeof(TrackerRole)))
        {
            if (role == TrackerRole.Invalid) continue;

            uint deviceIndex = ViveRole.GetDeviceIndexEx(role);
            bool isConnected = deviceIndex != VRModule.INVALID_DEVICE_INDEX && 
                               VRModule.GetCurrentDeviceState(deviceIndex).isConnected;

            int deviceId = (int)deviceIndex;

            if (isConnected)
            {
                if (!_activeTrackers.ContainsKey(deviceId))
                {
                    string roleName = role.ToString(); 
                    if (_debug) Debug.Log($"[ViveTrackerManager] VIU Tracker détecté ({roleName}). Instanciation directe et connexion au flux matériel !");
                    
                    GameObject newTrackerObj = Instantiate(_trackerPrefab, transform);
                    newTrackerObj.name = $"ViveTracker_VIU_{roleName}";
                    
                    ViveTrackerPoseDriver driver = newTrackerObj.GetComponent<ViveTrackerPoseDriver>();
                    if (driver != null)
                    {
                        // On passe l'index matériel directement au Driver !
                        driver.InitWithVIU(deviceIndex, roleName);
                        _activeTrackers.Add(deviceId, driver);
                        InputReader inputReader = newTrackerObj.GetComponent<InputReader>();
                        if (inputReader != null)
                        {
                            inputReader.SetInputConfigTargetRole(role);
                        }
                        ViveTrackerInputDebugger inputDebugger = newTrackerObj.GetComponent<ViveTrackerInputDebugger>();
                        if (inputDebugger != null)
                        {
                            inputDebugger.InitWithVIU(deviceIndex, roleName);
                        }
                    }

                    ControllerShortcutHandler shortcutHandler = newTrackerObj.GetComponent<ControllerShortcutHandler>();
                    if (shortcutHandler != null)
                    {
                        shortcutHandler.AddShortcut("Calibrate", ControllerButton.Trigger, ControllerButton.Thumb,
                            () => CalibrationManager.instance.StartOverCalibration());
                    }
                }
            }
            else
            {
                if (_activeTrackers.ContainsKey(deviceId))
                {
                    RemoveDevice(deviceId);
                }
            }
        }
    }
    
    private void FindTrackersWithInputSystem()
    {
        foreach (var device in InputSystem.devices)
        {
            string searchString = (device.name + device.displayName + device.layout).ToLower();

            if (searchString.Contains("tracker") || searchString.Contains("device1"))
            {
                if (!_activeTrackers.ContainsKey(device.deviceId))
                {
                    if (_debug) Debug.Log($"[ViveTrackerManager] Nouveau Tracker InputSystem détecté : {device.displayName}");
                
                    GameObject newTrackerObj = Instantiate(_trackerPrefab, transform);
                    newTrackerObj.name = $"ViveTracker_{device.deviceId}";
                
                    ViveTrackerPoseDriver driver = newTrackerObj.GetComponent<ViveTrackerPoseDriver>();
                    if (driver != null)
                    {
                        driver.InitWithInputSystem(device);
                        _activeTrackers.Add(device.deviceId, driver);
                    }
                }
            }
        }
    }

    private void RemoveDevice(int deviceId)
    {
        if (_activeTrackers.ContainsKey(deviceId))
        {
            if (_debug) Debug.Log($"[ViveTrackerManager] Tracker déconnecté (ID: {deviceId}). Destruction de l'objet.");
            
            ViveTrackerPoseDriver driver = _activeTrackers[deviceId];
            if (driver != null) Destroy(driver.gameObject);
            
            _activeTrackers.Remove(deviceId);
        }
    }
}
