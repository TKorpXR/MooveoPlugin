using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

public class ViveTrackerManager : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Le Prefab contenant le script ViveTrackerPoseDriver")]
    [SerializeField] private GameObject _trackerPrefab;
    [SerializeField] private bool _debug = true;
    
    private Dictionary<int, ViveTrackerPoseDriver> _activeTrackers = new Dictionary<int, ViveTrackerPoseDriver>();


    private void Update()
    {
        FindTrackers();
    }
    private void FindTrackers()
    {
        if(_debug) Debug.Log("[ViveTrackerManager] Recherche des trackers...");
        foreach (var device in InputSystem.devices)
        {
            if (device is TrackedDevice trackedDevice)
            {
                if(_debug) Debug.Log($"[ViveTrackerManager] Détecté : {device.displayName}");
                // On fusionne tous les noms possibles de l'appareil en minuscules
                string searchString = (device.name + device.displayName + device.layout).ToLower();

                // On cherche les mots clés "tracker" ou "device1" (le nom virtuel de VIU)
                if (searchString.Contains("tracker") || searchString.Contains("device1"))
                {
                    if(_debug) Debug.Log($"[ViveTrackerManager] ViveTracker : {device.displayName}");
                    if (!_activeTrackers.ContainsKey(trackedDevice.deviceId))
                    {
                        if (_debug) Debug.Log($"[ViveTrackerManager] Nouveau Tracker détecté : {device.displayName}. Instanciation du prefab.");
                    
                        GameObject newTrackerObj = Instantiate(_trackerPrefab, transform);
                        newTrackerObj.name = $"ViveTracker_{device.deviceId}";
                    
                        ViveTrackerPoseDriver driver = newTrackerObj.GetComponent<ViveTrackerPoseDriver>();
                        if (driver != null)
                        {
                            driver.Init(trackedDevice);
                            _activeTrackers.Add(device.deviceId, driver);
                        }
                        else
                        {
                            Debug.LogError("[ViveTrackerManager] Le prefab ne contient pas le composant ViveTrackerPoseDriver !");
                            Destroy(newTrackerObj);
                        }
                    }
                }
            }
        }
    }

    private void RemoveDevice(InputDevice device)
    {
        if (_activeTrackers.ContainsKey(device.deviceId))
        {
            if (_debug) Debug.Log($"[ViveTrackerManager] Tracker déconnecté : {device.displayName}. Destruction de l'objet.");
            
            ViveTrackerPoseDriver driver = _activeTrackers[device.deviceId];
            if (driver != null)
            {
                Destroy(driver.gameObject);
            }
            
            _activeTrackers.Remove(device.deviceId);
        }
    }
}
