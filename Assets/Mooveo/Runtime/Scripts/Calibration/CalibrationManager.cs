using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ricimi;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using Quaternion = UnityEngine.Quaternion;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using UnityEngine.InputSystem;
using Valve.VR;

public enum EDeviceCheckerType
{ 
    HMD, 
    LeftController, 
    RightController, 
    AnyController,
    Tracker,
    SteamVR, 
    EosUtility, 
    HotFolder 
}

[Serializable]
public class DeviceCheckerConfig
{
    public string Key;
    public string Label;
    public EDeviceCheckerType Type;
    public bool RequiresExe;
}

public class CalibrationManager : MonoBehaviour
{
    [Header("Testers Spawning")]
    [SerializeField] private GameObject _testerPrefab;
    [SerializeField] private GameObject _testerTrackerPrefab;
    [SerializeField] private InputConfig _leftHandConfig;
    [SerializeField] private InputConfig _rightHandConfig;
    
    [Header("Params")]
	[SerializeField] private string _sceneName;
    [SerializeField] private Popup _doesLaunchPopup;
    [SerializeField] private GameObject _cursorPrefab;
    [SerializeField] Canvas _userUiCanvas;
    [SerializeField] List<CalibrationController> _testers = new List<CalibrationController>();
    [SerializeField] private float _posAnalysisTime = 1f;
    [SerializeField] protected float _deltaPrecisionDistance;
    [SerializeField] private float _deltaPointsVerification = 0.025f;
    [SerializeField] private Transform _transformTestReference;
    public Transform TransformTestReference => _transformTestReference;
    [SerializeField] Camera _camera;
    
    [Header("Device Checking Settings")]
    [SerializeField] private List<DeviceCheckerConfig> _devicesToCheck = new List<DeviceCheckerConfig>();
    private Dictionary<DeviceCheckerConfig, IDeviceChecker> _activeCheckers = new Dictionary<DeviceCheckerConfig, IDeviceChecker>();
    private Dictionary<UnityEngine.InputSystem.InputDevice, CalibrationController> _activeTesters = new Dictionary<UnityEngine.InputSystem.InputDevice, CalibrationController>();
    private CalibrationController _adminTester;

    public event Action<List<DeviceCheckerConfig>> OnInitDevicesUI;
    public event Action<string, string, bool, string> OnUpdateDeviceUI; 
    public event Action OnAllDevicesReady;
    
    private List<Vector3> _points = new List<Vector3>();
    private List<Vector3> _normals = new List<Vector3>();

    public static CalibrationManager instance;

    private Coroutine _averagePosRoutine;
    private Coroutine _startOverCalibrationCooldown;
    private Coroutine _checkDevicesRoutine; // Ajout pour gérer la boucle proprement
    private Transform _player;
    public enum EState { None, FirstCalibration, RefineCalibration }
    public EState _currentState = EState.None;

    private int _nPointsToCalibrate = 3;
    private Dictionary<CalibrationController, CalibrationCursorUI> _testercursors = new Dictionary<CalibrationController, CalibrationCursorUI>();
    
    [SerializeField] private Slider _debugSlider;
    [SerializeField] protected bool _debug = false;

    protected bool _devicesChecked = false;
    protected bool _needCalibration = false;
    protected bool _calibrated = false;
    
    private bool _handleClick = false;
    protected MooveoConfig _config = new MooveoConfig();

    private bool _enableDebugInputs = false;
    private bool _ignoreConfig = false;

    public event Action OnClickForCalibrating;
    public event Action<string> OnErrorDuringCalibration;
    public event Action OnSubmitPoint;
    public event Action<float> OnUpdatePoint;
    public event Action OnCalibrationEnded;
    

    private void Awake()
    { 
        instance = this;
        Application.runInBackground = true;
    }

    private void OnEnable()
    {
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    private void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
    }

    private void OnDeviceChange(UnityEngine.InputSystem.InputDevice device, InputDeviceChange change)
    {
        switch (change)
        {
            case InputDeviceChange.Added:
            case InputDeviceChange.Reconnected:
            case InputDeviceChange.Enabled:
                CheckAndSpawnTester(device);
                break;
                
            case InputDeviceChange.Removed:
            case InputDeviceChange.Disconnected:
            case InputDeviceChange.Disabled:
                if (_activeTesters.TryGetValue(device, out CalibrationController tester))
                {
                    if (tester != null && tester.gameObject != null)
                        Destroy(tester.gameObject);
                    _activeTesters.Remove(device);
                }
                break;
        }
    }

    public void ScanAndSpawnTesters()
    {
        if (_debug) Debug.Log($"[CalibrationManager] ScanAndSpawn appelé. Total des appareils dans InputSystem : {InputSystem.devices.Count}");
        foreach (var device in InputSystem.devices)
        {
            CheckAndSpawnTester(device);
        }
    }

    private void CheckAndSpawnTester(UnityEngine.InputSystem.InputDevice device)
    {
        if (_debug) Debug.Log($"[CalibrationManager] Évaluation de l'appareil : {device.name} | Type : {device.GetType().Name}");
        if (_activeTesters.ContainsKey(device)) return;

        if (!(device is UnityEngine.InputSystem.TrackedDevice))
        {
            if (_debug) Debug.Log($"[CalibrationManager] L'appareil {device.name} ignoré car pas TrackedDevice.");
            return;
        }
        if (device is UnityEngine.InputSystem.XR.XRHMD || device.name.Contains("HMD") || device.name.Contains("Head"))
        {
            if (_debug) Debug.Log($"[CalibrationManager] L'appareil {device.name} ignoré car casque VR.");
            return;
        }
        string devNameClean = device.name.Replace(" ", "");
        if (devNameClean.Contains("TrackingReference") || devNameClean.Contains("ValveSRImp"))
        {
            if (_debug) Debug.Log($"[CalibrationManager] L'appareil {device.name} ignoré car station de base.");
            return;
        }

        InputConfig configToUse = null;
        int playerID = -1;

        bool isLeft = device.usages.Contains(UnityEngine.InputSystem.CommonUsages.LeftHand);
        bool isRight = device.usages.Contains(UnityEngine.InputSystem.CommonUsages.RightHand);
        
        bool headless = GlobalSettings.Core.GlobalSettings.Instance.Headless.Value;
        if (!isLeft && !isRight)
        {
            ETrackedControllerRole openVRRole = OpenVRUtility.GetRoleBySerialNumber(device.description.serial);
            if (openVRRole == ETrackedControllerRole.LeftHand) isLeft = true;
            else if (openVRRole == ETrackedControllerRole.RightHand) isRight = true;
        }
        
        if (!isLeft && !isRight)
        {
            if (device.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0) isLeft = true;
            if (device.name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0) isRight = true;
        }

        // Default or unassigned
        if (!isLeft && !isRight)
        {
            if (_debug) Debug.LogWarning($"[CalibrationManager] Impossible d'identifier {device.name}. Forçage en Right.");
            isRight = true;
        }

        if (_debug) Debug.Log($"[CalibrationManager] Checking Device: {device.name} | Type: {device.GetType().Name} | Final Left: {isLeft}, Final Right: {isRight}");

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

        if (playerID != -1)
        {
            if (_debug) Debug.Log($"[CalibrationManager] Tentative de spawn pour {device.name} (Player {playerID}) en mode Headless={headless}.");
            GameObject instance;
            if (headless) 
            {
                if (_testerTrackerPrefab == null)
                {
                    if (_debug) Debug.LogError("[CalibrationManager] Erreur : _testerTrackerPrefab est NULL dans l'inspecteur !");
                    return;
                }
                instance = Instantiate(_testerTrackerPrefab);
            }
            else 
            {
                if (_testerPrefab == null)
                {
                    if (_debug) Debug.LogError("[CalibrationManager] Erreur : _testerPrefab est NULL dans l'inspecteur !");
                    return;
                }
                instance = Instantiate(_testerPrefab);
            }
            
            instance.name = $"Tester_{playerID}_{device.name}";

            InputReader reader = instance.GetComponent<InputReader>();
            if (reader != null)
            {
                reader.SetInputConfig(configToUse);
            }
            else
            {
                if (_debug) Debug.LogError($"[CalibrationManager] Pas de InputReader sur {instance.name}");
            }

            CalibrationController tester = instance.GetComponent<CalibrationController>();
            if (tester != null)
            {
                _activeTesters.Add(device, tester);
            }

            if (headless)
            {
                ETrackedControllerRole finalRole = (playerID == 0) ? ETrackedControllerRole.LeftHand : ETrackedControllerRole.RightHand;
                uint deviceIndex = OpenVRUtility.GetDeviceIndexBySerialNumber(device.description.serial);

                SteamVR_Tracker steamVRTracker = instance.GetComponent<SteamVR_Tracker>();
                if (steamVRTracker != null)
                {
                    steamVRTracker.device = OpenVRUtility.GetSteamVRTrackerDevice(deviceIndex);
                    steamVRTracker.SetRole(finalRole);
                    if (_debug) Debug.Log($"[CalibrationManager] SteamVR_Tracker assigné | Device : {steamVRTracker.device}");
                }

                if (reader != null)
                {
                    reader.TargetRole = finalRole;
                    reader.ConfigMode = ConfigMode.OVRInput;
                }
            }
            else
            {
                var driver = instance.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                if (driver != null)
                {
                    if (configToUse.PositionAction != null)
                        driver.positionInput = new InputActionProperty(configToUse.PositionAction);
                    
                    if (configToUse.RotationAction != null)
                        driver.rotationInput = new InputActionProperty(configToUse.RotationAction);
                }
            }
        }
    }

    private void Start()
    {
        GlobalSettings.Core.GlobalSettings.Instance.DeltaPrecisionCalibration.Bind(f => _deltaPrecisionDistance = f);
        _player = Camera.main?.transform;
        GlobalSettings.Core.GlobalSettings.MainCamera = _camera;
        ScanAndSpawnTesters();
        Init();
    }

    private void Update()
    {
        if (_currentState == EState.FirstCalibration || _currentState == EState.RefineCalibration)
        {
            if (_devicesChecked && !AreControllersConnected())
            {
                _devicesChecked = false; // Empêche le spam de l'Update
                if (_averagePosRoutine != null)
                {
                    StopCoroutine(_averagePosRoutine);
                    _averagePosRoutine = null;
                }

                if (UICalibrationToolkit.instance != null)
                {
                    UICalibrationToolkit.instance.ShowPopupError("Le tracker a été perdu ou masqué. Reprise du scan dans 5 secondes...");
                }
                
                StartCoroutine(HandleTrackingLossRoutine());
            }
        }
    }

    private Action<bool> _launchAction;

    public virtual void Init()
    {
        _needCalibration = DoesNeedCalibration();
        InitCheckers();
        OnInitDevicesUI?.Invoke(_devicesToCheck);
        
        // On stoppe l'ancienne boucle si elle tournait encore avant d'en relancer une
        if (_checkDevicesRoutine != null) StopCoroutine(_checkDevicesRoutine);
        _checkDevicesRoutine = StartCoroutine(CheckDevicesLogicLoop());
        
        _config = MooveoConfigManager.Load();

        if (GlobalSettings.Core.GlobalSettings.Instance != null && GlobalSettings.Core.GlobalSettings.Instance.Admin.Value)
        {
            SpawnAdminTester();
        }
    }
    
    public void OpenPopupLaunching()
    {
        if (UICalibrationToolkit.instance != null)
        {
            UICalibrationToolkit.instance.OpenPopupLaunching();
            return; 
        }
        
        if (_doesLaunchPopup != null)
        {
            _doesLaunchPopup.Open();
            CanvasGroup grp = _doesLaunchPopup.GetComponent<CanvasGroup>();
            if(grp) { grp.blocksRaycasts = true; grp.interactable = true; }
        }
    }

    public void ProceedAfterChecks()
    {
        if(UICalibrationToolkit.instance != null)
        {
            UICalibrationToolkit.instance.OnDevicesChecked -= _launchAction;
        }
        
        _devicesChecked = true;
        if (!_needCalibration)
        {
             _config = MooveoConfigManager.Load();
                
            if (_config != null && _config.Points != null && _config.Points.Count >= 3)
            {
                _points = new List<Vector3>(_config.Points);
                TestCalibrationSetupPlayArea();
            }
            OpenPopupLaunching();
        }
        else
        {
            _currentState = (_nPointsToCalibrate == 3) ? EState.FirstCalibration : EState.RefineCalibration;
            UICalibrationToolkit.instance.StartMainFlow(_nPointsToCalibrate);
        }
    }
    
    public bool AreControllersConnected()
    {
        if (GlobalSettings.Core.GlobalSettings.Instance != null && GlobalSettings.Core.GlobalSettings.Instance.Admin.Value) return true;
        bool leftConnected = false;
        bool rightConnected = false;
        bool trackerConnected = false;

        foreach (var kvp in _activeCheckers)
        {
            if (kvp.Key.Type == EDeviceCheckerType.LeftController) leftConnected = kvp.Value.IsConnected();
            if (kvp.Key.Type == EDeviceCheckerType.RightController) rightConnected = kvp.Value.IsConnected();
            if (kvp.Key.Type == EDeviceCheckerType.Tracker) trackerConnected = kvp.Value.IsConnected();
            if (kvp.Key.Type == EDeviceCheckerType.AnyController && kvp.Value.IsConnected()) return true;
        }

        return leftConnected || rightConnected || trackerConnected;
    }

    private void InitCheckers()
    {
        _activeCheckers.Clear();
        foreach (var config in _devicesToCheck)
        {
            IDeviceChecker checker = null;
            switch (config.Type)
            {
                case EDeviceCheckerType.HMD: checker = new HMDChecker(); break;
                case EDeviceCheckerType.LeftController: checker = new LeftControllerChecker(); break;
                case EDeviceCheckerType.RightController: checker = new RightControllerChecker(); break;
                case EDeviceCheckerType.Tracker: checker = new TrackerChecker(); break;
                case EDeviceCheckerType.AnyController: checker = new AnyControllerChecker(); break;
                case EDeviceCheckerType.SteamVR: checker = new SteamVRChecker(); break;
                case EDeviceCheckerType.EosUtility: checker = new EosUtilitychecker(); break;
                case EDeviceCheckerType.HotFolder: checker = new HotFolderChecker(); break;
            }
            if (checker != null) _activeCheckers.Add(config, checker);
        }
    }
    private bool DoesNeedCalibration()
    {
        // AJOUT VITAL : Force la calibration si on a appuyé sur "Start Over" ou "Refine"
        if (_ignoreConfig) return true;

        if (!MooveoConfigManager.Exists()) return true;
        _config = MooveoConfigManager.Load();
        if (_config.Points.Count <= 2) return true;

        for (int i = 0; i < _config.Points.Count; i++)
        {
            for (int j = i + 1; j < _config.Points.Count; j++)
            {
                if (Vector3.Distance(_config.Points[i], _config.Points[j]) <= _deltaPointsVerification)
                {
                    if (_debug) Debug.LogWarning($"Calibration invalide : Les points {i} et {j} sont trop proches.");
                    return true;
                }
            }
        }

        return false;
    }
    private HashSet<string> _reportedMissingFiles = new HashSet<string>();
    
    private IEnumerator CheckDevicesLogicLoop()
    {
        if (GlobalSettings.Core.GlobalSettings.Instance != null && GlobalSettings.Core.GlobalSettings.Instance.Admin.Value)
        {
            foreach (var kvp in _activeCheckers) OnUpdateDeviceUI?.Invoke(kvp.Key.Key, kvp.Key.Label, true, "");
            yield return new WaitForSeconds(0.5f);
            _devicesChecked = true;
            OnAllDevicesReady?.Invoke();
            yield break;
        }

        bool allDevicesValid = false;
        _reportedMissingFiles.Clear();

        while (!allDevicesValid)
        {
            allDevicesValid = true;

            foreach (var kvp in _activeCheckers)
            {
                var config = kvp.Key;
                var checker = kvp.Value;
                
                bool isConnected = checker.IsConnected();
                string exePath = "";

                if (!isConnected && config.RequiresExe)
                {
                    exePath = GetExePathForType(config.Type);
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        if (System.IO.File.Exists(exePath))
                        {
                            if (!AppLauncher.IsProcessLaunched(exePath))
                            {
                                AppLauncher.LaunchProcess(exePath);
                            }
                        }
                        else
                        {
                            if (!_reportedMissingFiles.Contains(exePath))
                            {
                                _reportedMissingFiles.Add(exePath);
                                string varName = config.Type switch {
                                    EDeviceCheckerType.SteamVR => "SteamVREXEPath",
                                    EDeviceCheckerType.EosUtility => "EosUtilityEXEPath",
                                    EDeviceCheckerType.HotFolder => "HotFolderEXEPath",
                                    _ => "chemin"
                                };
                                
                                if (UICalibrationToolkit.instance != null && UICalibrationToolkit.instance.RootOverlay != null)
                                {
                                    var root = UICalibrationToolkit.instance.RootOverlay;
                                    UIToast.Show(root, "Erreur d'exécutable", $"Le fichier exécutable pour '{config.Label}' est introuvable:\n{exePath}\n\nVeuillez vérifier la variable '{varName}' dans GlobalSettings.", false, 8000f);
                                }
                            }
                        }
                    }
                }

                OnUpdateDeviceUI?.Invoke(config.Key, config.Label, isConnected, exePath);

                if (!isConnected) allDevicesValid = false;

                yield return new WaitForSeconds(0.5f); 
            }

            if (!allDevicesValid) yield return new WaitForSeconds(1f);
        }

        _devicesChecked = true;
        OnAllDevicesReady?.Invoke();
    }

    private string GetExePathForType(EDeviceCheckerType type)
    {
        var gs = GlobalSettings.Core.GlobalSettings.Instance;
        switch (type)
        {
            case EDeviceCheckerType.SteamVR: return gs.SteamVREXEPath.Value;
            case EDeviceCheckerType.EosUtility: return gs.EosUtilityEXEPath.Value;
            case EDeviceCheckerType.HotFolder: return gs.HotFolderEXEPath.Value;
            default: return "";
        }
    }
    
    [ContextMenu("Start Over Calibration")]
    public void StartOverCalibration()
    {
        if (_launchAction != null && UICalibrationToolkit.instance != null)
        {
            UICalibrationToolkit.instance.OnDevicesChecked -= _launchAction;
            _launchAction = null;
        }

        if (UICalibrationToolkit.instance != null)
        {
            UICalibrationToolkit.instance.ClosePopupLaunching();
        }

        _ignoreConfig = true;
        
        if (_doesLaunchPopup != null && _doesLaunchPopup.gameObject.activeInHierarchy)
        {
            _doesLaunchPopup.Close();
            CanvasGroup grp = _doesLaunchPopup.GetComponent<CanvasGroup>();
            if(grp) { grp.blocksRaycasts = false; grp.interactable = false; }
        }
        
        if (_startOverCalibrationCooldown != null) return;
        _startOverCalibrationCooldown = StartCoroutine(StartOverCalibrationCooldown());
        ClearTestCalibration(true);
        
        ScanAndSpawnTesters();

        // LA CORRECTION EST ICI : On relance l'initialisation complète au lieu de forcer l'UI
        Init(); 
    }

    public void RefineCalibration()
    {
        _ignoreConfig = true; // S'assurer de forcer la calibration ici aussi
        ClearTestCalibration(true);
        _nPointsToCalibrate = 9;
        _currentState = EState.RefineCalibration;
        
        if (UICalibrationToolkit.instance != null)
            UICalibrationToolkit.instance.ResetUI();
        
        ScanAndSpawnTesters();

        // LA CORRECTION EST ICI AUSSI
        Init();
    }
    
    public void HandleClick(Transform _controllerTransform)
    {
        if (!_devicesChecked || !_needCalibration) return;
        
        // CORRECTION : On bloque la capture de point si on est encore dans les menus d'attente
        if (_currentState == EState.None) return;
        
        if (_averagePosRoutine != null)
        {
            StopCoroutine(_averagePosRoutine);
            _averagePosRoutine = null;
        }

        _handleClick = true;
        _averagePosRoutine = StartCoroutine(AveragePosition(_controllerTransform));
    }

    public void HandleThumbClick()
    {

    }

    [ContextMenu("Launch Game")]
    public void Launch()
    {
        if(_config == null) return;
        if(_calibrated)SaveConfig();
        if (!MooveoConfigManager.Exists())
        {
            if (_debug) Debug.LogError("CALIBRATION : Le fichier n'a pas été sauvegardé, LoadScene annulé.");
            return;
        }

        ClearTestCalibration(false);

        SceneManager.LoadScene(_sceneName);
    }

    public static List<UnityEngine.XR.InputDevice> GetDevicesWithChars(InputDeviceCharacteristics characteristics)
    {
        List<UnityEngine.XR.InputDevice> hmds = new List<UnityEngine.XR.InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(characteristics, hmds);
        return hmds;
    }
    
    public static bool IsSteamVRRunning()
    {
        return OpenXRRuntime.name.Contains("SteamVR");
    }

    public void SaveConfig()
    {
        _config.Points = new List<Vector3>(_points);
        _config.Normals = new List<Vector3>(_normals);
        MooveoConfigManager.Save(_config);
    }
    
    public void TestCalibrationSetupPlayArea()
    {
        if (_points.Count < 3)
        {
            if (_debug) Debug.LogWarning("Pas assez de points pour calibrer le mur.");
            return;
        }
        
        Vector3 center = CalculateCenter(_points, out int centerIndex);
        if (_debug) Debug.Log($"Center is {center}");
        (Vector3 left, Vector3 right) = CalculateLeftAndRight(_points, centerIndex);

        float width = Vector3.Distance(left, right) * 2f;
        
        float screenRatio = (float)Screen.width / Screen.height;
        GlobalSettings.Core.GlobalSettings.ScreenRatio = screenRatio;
        
        float heigh = width / screenRatio;
        
        Vector3 horizontalDir = (right - left).normalized;
        Quaternion rotation = Quaternion.LookRotation(- Vector3.Cross(Vector3.up, horizontalDir), Vector3.up);
        

        _transformTestReference.position = center;
        _transformTestReference.rotation = rotation;
        
        _transformTestReference.localScale = new Vector3(width, heigh, 1f);
        
        GlobalSettings.Core.GlobalSettings.ScreenSize = new Vector2(width, heigh);
        TestCalibrationSetupCamera(center, _transformTestReference.forward, _transformTestReference.up, _transformTestReference.localScale.y);
    }

    public void TestCalibrationSetupCamera(Vector3 planeCenter, Vector3 planeForward, Vector3 planeUp, float planeHeight)
    {
        
        if (_camera == null)
        {
            if (_debug) Debug.LogWarning("Aucune caméra trouvée.");
            return;
        }
        
        float distance = 1.0f;
        Vector3 camPos = planeCenter - planeForward * distance;

        _camera.transform.position = camPos;
        _camera.transform.rotation = Quaternion.LookRotation(planeForward, planeUp);
        
        _camera.orthographic = true;
        
        _camera.orthographicSize = planeHeight * 0.5f;
        float offset = 0.0001f;
        Vector3 canvasCursorPos = planeCenter; 
        Vector2 sizeDelta = _transformTestReference.localScale;
        Vector3 euler = _transformTestReference.eulerAngles;
        UICalibrationToolkit.instance.InitCanvasCursor(canvasCursorPos, sizeDelta, euler);
        
        foreach (var activeTester in _activeTesters.Values)
        {
            if (activeTester != null)
            {
                activeTester.SetupForTest(_camera, canvasCursorPos);
            }
        }
    }
    
    public void AddNewTester(CalibrationController tester)
    {
        if (_testers.Contains(tester))
        {
            if (_debug) Debug.Log($"{tester.name} is already a painter present in painters, returning.");
            return;
        }
        _testers.Add(tester);
        GameObject cursor = Instantiate(_cursorPrefab, _userUiCanvas.transform);
        _testercursors.Add(tester, cursor.GetComponent<CalibrationCursorUI>());
    }
    public CalibrationCursorUI GetAssociatedCursor(CalibrationController tester) => _testercursors[tester];
    public void RemoveTester(CalibrationController calibrationController)
    {
        if (_testercursors.TryGetValue(calibrationController, out CalibrationCursorUI cursor))
        {
            if (cursor != null && cursor.gameObject != null)
                Destroy(cursor.gameObject);
        }
        _testercursors.Remove(calibrationController);
        _testers.Remove(calibrationController);
    }

    IEnumerator AveragePosition(Transform controllerTransform)
    {
        OnClickForCalibrating?.Invoke();
        float elapsedTime = 0f;
        int counter = 0;
        
        Vector3 averagePosition = Vector3.zero;
        Vector3 averageForward = Vector3.zero;
        
        if(_debugSlider != null) _debugSlider.value = 0;
        float slowDistance = 0f;
        float frameDistance = 0f;
        Vector3 initialPos = controllerTransform.position;
        Vector3 previousPos = controllerTransform.position;
        
        while (elapsedTime < _posAnalysisTime)
        {
            slowDistance = Vector3.Distance(initialPos, controllerTransform.position);
            frameDistance = Vector3.Distance(previousPos, controllerTransform.position);

            if (frameDistance > 0.10f)
            {
                if (_debug) Debug.Log($"GLITCH DIST: distance {frameDistance} limit 0.10f");
                OnErrorDuringCalibration?.Invoke("Erreur de tracking. Assurez-vous de ne pas masquer le capteur.");
                yield break;
            }

            if (averagePosition != Vector3.zero && slowDistance > _deltaPrecisionDistance)
            {
                if (_debug) Debug.Log($"ERROR DIST: distance {slowDistance} limit {_deltaPrecisionDistance}");
                OnErrorDuringCalibration?.Invoke("Mouvement détecté. Maintenez le contrôleur parfaitement immobile.");
                yield break;
            }
            
            OnUpdatePoint?.Invoke(elapsedTime / _posAnalysisTime);
            elapsedTime += Time.deltaTime;
            averagePosition += controllerTransform.position;
            averageForward += controllerTransform.up;
            counter++;
            previousPos = controllerTransform.position;
            if(_debugSlider != null) _debugSlider.value = elapsedTime / _posAnalysisTime;
            yield return null;
        }
        
        averagePosition /= counter;
        averageForward /= counter;
        averageForward.Normalize();
        
        if (_debug) Debug.Log($"AVERAGE POS = {averagePosition} | AVERAGE FWD = {averageForward}");        
        
        _points.Add(averagePosition);
        _normals.Add(averageForward);
        
        OnSubmitPoint?.Invoke();
        if(CalibrationEnded()) 
        {
            StartCoroutine(ValidationSequenceRoutine());
        }
    }

    /// <summary>
    /// Fake loading process pour valider asynchronement la calibration et rassurer l'utilisateur final.
    /// </summary>
    private IEnumerator ValidationSequenceRoutine()
    {
        if (_points.Count < 3)
        {
            OnCalibrationEnded?.Invoke();
            yield break;
        }

        Vector3 p1, p2, p3;
        List<Vector3> normalsToTest;

        // Si on est en Refine (9 points), on groupe par colonnes (Gauche, Centre, Droite) 
        // pour créer un axe horizontal parfait à partir des 3 lignes scannées.
        if (_currentState == EState.RefineCalibration && _points.Count >= 9)
        {
            p1 = (_points[0] + _points[3] + _points[6]) / 3f; // Moyenne Colonne Gauche
            p2 = (_points[1] + _points[4] + _points[7]) / 3f; // Moyenne Colonne Centre
            p3 = (_points[2] + _points[5] + _points[8]) / 3f; // Moyenne Colonne Droite

            Vector3 n1 = (_normals[0] + _normals[3] + _normals[6]).normalized;
            Vector3 n2 = (_normals[1] + _normals[4] + _normals[7]).normalized;
            Vector3 n3 = (_normals[2] + _normals[5] + _normals[8]).normalized;
            normalsToTest = new List<Vector3> { n1, n2, n3 };
        }
        else // Cas standard (FirstCalibration à 3 points)
        {
            p1 = _points[0];
            p2 = _points[1];
            p3 = _points[2];
            normalsToTest = _normals;
        }

        float totalWidth = Vector3.Distance(p1, p3);

        // a) Vérification de l'alignement
        if (UICalibrationManager.instance != null) UICalibrationManager.instance.SetValidationUI(true, "Vérification en cours...");
        else if (UICalibrationToolkit.instance != null) UICalibrationToolkit.instance.SetValidationUI(true, "Vérification en cours...");
        yield return new WaitForSeconds(1.5f);

        var lin = CalibrationValidator.CheckLinearity(p1, p2, p3, totalWidth, GlobalSettings.Core.GlobalSettings.Instance.LinearityThreshold.Value);
        if (!lin.isValid)
        {
            if (UICalibrationManager.instance != null) UICalibrationManager.instance.MarkAllPointsError(lin.errorMessage);
            else if (UICalibrationToolkit.instance != null) UICalibrationToolkit.instance.MarkAllPointsError(lin.errorMessage);
            
            yield return HandleCalibrationErrorRoutine();
            yield break;
        }

        // c) Analyse de la symétrie
        if (UICalibrationManager.instance != null) UICalibrationManager.instance.SetValidationUI(true, "Vérification en cours...");
        else if (UICalibrationToolkit.instance != null) UICalibrationToolkit.instance.SetValidationUI(true, "Vérification en cours...");
        yield return new WaitForSeconds(1.5f);

        var sym = CalibrationValidator.CheckSymmetry(p1, p2, p3, totalWidth, GlobalSettings.Core.GlobalSettings.Instance.SymmetryThreshold.Value);
        if (!sym.isValid)
        {
            if (UICalibrationManager.instance != null) UICalibrationManager.instance.MarkAllPointsError(sym.errorMessage);
            else if (UICalibrationToolkit.instance != null) UICalibrationToolkit.instance.MarkAllPointsError(sym.errorMessage);
            
            yield return HandleCalibrationErrorRoutine();
            yield break;
        }

        // e) Vérification des capteurs
        if (UICalibrationManager.instance != null) UICalibrationManager.instance.SetValidationUI(true, "Vérification en cours...");
        else if (UICalibrationToolkit.instance != null) UICalibrationToolkit.instance.SetValidationUI(true, "Vérification en cours...");
        yield return new WaitForSeconds(1.5f);

        var norm = CalibrationValidator.AreNormalsConsistent(normalsToTest);
        if (!norm.isValid)
        {
            if (UICalibrationManager.instance != null) UICalibrationManager.instance.MarkAllPointsError(norm.errorMessage);
            else if (UICalibrationToolkit.instance != null) UICalibrationToolkit.instance.MarkAllPointsError(norm.errorMessage);
            
            yield return HandleCalibrationErrorRoutine();
            yield break;
        }

        // g) Succès, finalisation
        if (UICalibrationManager.instance != null) UICalibrationManager.instance.SetValidationUI(false);
        else if (UICalibrationToolkit.instance != null) UICalibrationToolkit.instance.SetValidationUI(false);
        
        OnCalibrationEnded?.Invoke();
        _calibrated = true;
    }

    private IEnumerator HandleCalibrationErrorRoutine()
    {
        _points.Clear();
        _normals.Clear();
        
        yield return new WaitForSeconds(3f);
        
        if (UICalibrationManager.instance != null) 
        {
            UICalibrationManager.instance.SetValidationUI(false);
            UICalibrationManager.instance.StartMainFlow(_nPointsToCalibrate);
        }
        else if (UICalibrationToolkit.instance != null)
        {
            UICalibrationToolkit.instance.SetValidationUI(false);
            UICalibrationToolkit.instance.StartMainFlow(_nPointsToCalibrate);
        }
        
        // Marquer le flag comme retour au besoin de calibration
        _needCalibration = true;
        _handleClick = false;
        
        // Relancer l'init si nécessaire
        Init();
    }

    private IEnumerator HandleTrackingLossRoutine()
    {
        yield return new WaitForSeconds(4.5f);
        ClearTestCalibration(true);
        ScanAndSpawnTesters();
        Init();
    }

    IEnumerator StartOverCalibrationCooldown()
    {
        yield return new WaitForSeconds(0.5f);
        _startOverCalibrationCooldown = null;
    }
    
    public void NotifyUIClickOnce(CalibrationController calibrationController = null)
    {
        UICalibrationToolkit.instance?.NotifyOnNextStep(calibrationController);
    }

    private bool CalibrationEnded()
    {
        bool isDone = _points.Count == _nPointsToCalibrate;

        if (isDone && UICalibrationToolkit.instance != null)
        {
            UICalibrationToolkit.instance.ShowPressAInstruction();
        }

        _needCalibration = !isDone;
        return isDone;
    }

    private void ClearTestCalibration(bool needCalibration)
    {
        _devicesChecked = false;
        _needCalibration = needCalibration;
        _handleClick = false;
        
        // CORRECTION : Réinitialisation de l'état de calibration
        _currentState = EState.None; 
        
        _points.Clear();
        _normals.Clear();

        foreach (var tester in _activeTesters.Values)
        {
            if (tester != null && tester.gameObject != null)
                Destroy(tester.gameObject);
        }
        _activeTesters.Clear();

        if (_adminTester != null)
        {
            if (_adminTester.gameObject != null)
                Destroy(_adminTester.gameObject);
            _adminTester = null;
        }
    }

    private void SpawnAdminTester()
    {
        GameObject instance = Instantiate(_testerPrefab);
        instance.name = "Tester_Admin_FakeController";
        
        var driver = instance.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        if (driver != null) driver.enabled = false; // Désactive la recherche de matériel VR
        
        _adminTester = instance.GetComponent<CalibrationController>();
        _adminTester.SetupForTest(_camera, Vector3.zero);
    }
    
    private Vector3 CalculateCenter(List<Vector3> points, out int centerIndex)
    {
        if (GlobalSettings.Core.GlobalSettings.Instance.Headless.Value)
        {
            centerIndex = 1;
            return points[1];
        }
        
        Vector3 avg = (_points[0] + _points[1] + _points[2]) / 3f;
        
        centerIndex = 0;
        float minDist = Vector3.Distance(_points[0], avg);
        for (int i = 1; i < 3; i++)
        {
            float d = Vector3.Distance(_points[i], avg);
            if (d < minDist)
            {
                minDist = d;
                centerIndex = i;
            }
        }
        return _points[centerIndex];
    }

    private (Vector3 left, Vector3 right) CalculateLeftAndRight(List<Vector3> points, int centerIndex)
    {
        if (GlobalSettings.Core.GlobalSettings.Instance.Headless.Value)
        {
            return (points[0], points[2]);
        }
        
        List<Vector3> extremites = new List<Vector3>();
        for (int i = 0; i < points.Count; i++)
        {
            if (i != centerIndex)
                extremites.Add(points[i]);
        }

        Vector3 e1 = extremites[0];
        Vector3 e2 = extremites[1];
        
        Vector3 dir1 = (e1 - points[centerIndex]).normalized;
        Vector3 dir2 = (e2 - points[centerIndex]).normalized;
        
        Vector3 camRight = _player.right;
        
        float dot1 = Vector3.Dot(dir1, camRight);
        float dot2 = Vector3.Dot(dir2, camRight);
        
        Vector3 left, right;
        if (dot1 < 0f)
        {
            left = e1;
            right = e2;
        }
        else
        {
            left = e2;
            right = e1;
        }

        return (left, right);
    }
}