using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

public class MooveoDeviceManager : MonoBehaviour
{
    public static MooveoDeviceManager Instance;

    [Header("Mooveo Base Configuration")]
    [SerializeField, Tooltip("Prefab Generique avec un DefaultController")] private GameObject _controllerBasePrefab;
    [SerializeField] private Transform _controllersContainer;
    
    [Header("Input Configuration (Unity Input System)")]
    [SerializeField] protected InputConfig _leftHandConfig;
    [SerializeField] protected InputConfig _rightHandConfig;
    [SerializeField] protected InputConfig _trackerConfig;

    [Header("OpenVR Integration (Kiosk Mode)")]
    [Tooltip("Cochez cette case pour forcer l'écoute des Pogo Pins via OpenVR pour les Vive Trackers")]
    [SerializeField] private bool _useOpenVRForTrackers = false;

    public event Action<DefaultController> OnPlayerSpawned;
    public event Action<DefaultController> OnPlayerRemoved;
    
    private Dictionary<InputDevice, DefaultController> _activeDevices = new Dictionary<InputDevice, DefaultController>();
    private HashSet<int> _spawnedPlayerIDs = new HashSet<int>();
    private int _trackerIdCounter = 2; // 0 et 1 pour gauche et droite
    
    private void Awake()
    {
        if(Instance != null) Destroy(this);
        Instance = this;
    }
    
    private void OnEnable()
    {
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    private void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
    }
    
    public void ScanAndSpawn()
    {
        foreach (var device in InputSystem.devices)
        {
            CheckAndSpawn(device);
        }
    }
    
    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        switch (change)
        {
            case InputDeviceChange.Added:
            case InputDeviceChange.Reconnected:
            case InputDeviceChange.Enabled:
                CheckAndSpawn(device);
                break;
                    
            case InputDeviceChange.Removed:
            case InputDeviceChange.Disconnected:
            case InputDeviceChange.Disabled:
                RemoveDevice(device);
                break;
        }
    }
    
    private void CheckAndSpawn(InputDevice device)
    {
        if (_activeDevices.ContainsKey(device)) return;
        
        // On ne gère que les appareils de Tracking (Manettes ou Vive Trackers)
        if (!(device is TrackedDevice)) return;
        
        InputConfig configToUse = null;
        int playerID = -1;
        string roleName = "Unknown";
        
        // Variables pour l'injection OpenVR
        Valve.VR.ETrackedControllerRole openVrRole = Valve.VR.ETrackedControllerRole.Invalid;
        bool useOpenVRMode = false;

        bool isLeft = device.usages.Contains(CommonUsages.LeftHand);
        bool isRight = device.usages.Contains(CommonUsages.RightHand);
        bool isTrackerDevice = device.name.Contains("Tracker") || device.name.Contains("Generic");

        if (isLeft)
        {
            configToUse = _leftHandConfig; // Toujours assigné pour gérer la position spatiale
            playerID = 0;
            roleName = "Left";
            
            // OPTIONNEL : On override le système de boutons si le toggle OpenVR est actif ET que c'est un Tracker
            if (_useOpenVRForTrackers && isTrackerDevice)
            {
                useOpenVRMode = true;
                openVrRole = Valve.VR.ETrackedControllerRole.LeftHand;
            }
        }
        else if (isRight)
        {
            configToUse = _rightHandConfig; // Toujours assigné
            playerID = 1;
            roleName = "Right";
            
            // OPTIONNEL : On override si OpenVR est actif
            if (_useOpenVRForTrackers && isTrackerDevice)
            {
                useOpenVRMode = true;
                openVrRole = Valve.VR.ETrackedControllerRole.RightHand;
            }
        }
        else if (isTrackerDevice) 
        {
            configToUse = _trackerConfig; // Toujours assigné
            playerID = _trackerIdCounter;
            roleName = "Tracker";
            _trackerIdCounter++;
            
            // Note : Si on a un 3ème tracker (ex: Pied) et qu'on utilise OpenVR, 
            // il faudra étendre cette logique pour assigner d'autres rôles OpenVR.
        }

        if (configToUse != null && playerID != -1)
        {
            if (_spawnedPlayerIDs.Contains(playerID)) return;
            SpawnDevice(device, configToUse, playerID, roleName, useOpenVRMode, openVrRole);
        }
    }

    private void SpawnDevice(InputDevice device, InputConfig config, int playerID, string roleName, bool useOpenVRMode, Valve.VR.ETrackedControllerRole openVrRole)
    {
        if (_controllerBasePrefab == null) return;
        
        GameObject instance = Instantiate(_controllerBasePrefab, _controllersContainer);
        instance.name = $"MooveoPlayer_{playerID}_{roleName}";
        
        if (instance.TryGetComponent(out InputReader reader))
        {
            if (useOpenVRMode)
            {
                reader.ConfigMode = ConfigMode.OVRInput;
                reader.TargetRole = openVrRole;
                if (instance.TryGetComponent(out SteamVR_Tracker tracker))
                {
                    tracker.SetRole(openVrRole);
                }
            }
            else
            {
                reader.ConfigMode = ConfigMode.InputConfig;
                reader.SetInputConfig(config);
                if (instance.TryGetComponent(out DefaultController controller))
                {
                    controller.SetupTrackedPoseDriver(
                        new InputActionProperty(config.PositionAction), 
                        new InputActionProperty(config.RotationAction));
                }
            }
        }
        
        if (instance.TryGetComponent(out DefaultController baseController))
        {
            baseController.PlayerID = playerID;
            baseController.HandleIsTracked(true);
            
            _activeDevices.Add(device, baseController);
            _spawnedPlayerIDs.Add(playerID);
            
            SetupPlayer(baseController);
            OnPlayerSpawned?.Invoke(baseController);
        }
    }
    
    protected virtual void SetupPlayer(DefaultController newPlayer)
    {
        Debug.Log($"<color=yellow>[Mooveo SDK]</color> Joueur {newPlayer.PlayerID} connecté. (Tracker OpenVR: {newPlayer.GetComponent<InputReader>().ConfigMode})");
    }
    
    protected virtual void RemovePlayer(DefaultController playerToRemove)
    {
        Debug.Log($"<color=yellow>[Mooveo SDK]</color> Joueur {playerToRemove.PlayerID} déconnecté.");
    }
    
    private void RemoveDevice(InputDevice device)
    {
        if (_activeDevices.TryGetValue(device, out DefaultController baseController))
        {
            if (baseController != null && baseController.gameObject != null)
            {
                RemovePlayer(baseController);
                OnPlayerRemoved?.Invoke(baseController);
                _spawnedPlayerIDs.Remove(baseController.PlayerID);
                Destroy(baseController.gameObject);
            }
            _activeDevices.Remove(device);
        }
    }
}