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
    
    [Header("Input Configuration")]
    [SerializeField] private InputConfig _leftHandConfig;
    [SerializeField] private InputConfig _rightHandConfig;
    [SerializeField] private InputConfig _trackerConfig;

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
        bool isLeft = device.usages.Contains(CommonUsages.LeftHand);
        bool isRight = device.usages.Contains(CommonUsages.RightHand);
        if (isLeft)
        {
            configToUse = _leftHandConfig;
            playerID = 0;
        }
        else if (isRight)
        {
            configToUse = _rightHandConfig;
            playerID = 1;
        }
        else if (device.name.Contains("Tracker") || device.name.Contains("Generic")) 
        {
            configToUse = _trackerConfig;
            playerID = _trackerIdCounter;
            _trackerIdCounter++;
        }
        if (configToUse != null && playerID != -1)
        {
            if (_spawnedPlayerIDs.Contains(playerID)) return;
            SpawnDevice(device, configToUse, playerID, isLeft ? "Left" : isRight ? "Right" : "Tracker");
        }
    }

    private void SpawnDevice(InputDevice device, InputConfig config, int playerID, string roleName)
    {
        if (_controllerBasePrefab == null) return;
        
        GameObject instance = Instantiate(_controllerBasePrefab, _controllersContainer);
        instance.name = $"MooveoPlayer_{playerID}_{roleName}";
        
        if (instance.TryGetComponent(out InputReader reader))
        {
            reader.SetInputConfig(config);
        }
        
        if (instance.TryGetComponent(out TrackedPoseDriver driver))
        {
            if (config.PositionAction != null)
                driver.positionInput = new InputActionProperty(config.PositionAction);
            if (config.RotationAction != null)
                driver.rotationInput = new InputActionProperty(config.RotationAction);
        }
        
        if (instance.TryGetComponent(out DefaultController baseController))
        {
            baseController.PlayerID = playerID;
            _activeDevices.Add(device, baseController);
            _spawnedPlayerIDs.Add(playerID);
            
            SetupPlayer(baseController);
            OnPlayerSpawned?.Invoke(baseController);
        }
    }
    
    protected virtual void SetupPlayer(DefaultController newPlayer)
    {
        Debug.Log($"[Mooveo SDK] Joueur de base {newPlayer.PlayerID} connecté.");
    }
    
    protected virtual void RemovePlayer(DefaultController playerToRemove)
    {
        Debug.Log($"[Mooveo SDK] Joueur {playerToRemove.PlayerID} déconnecté.");
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
