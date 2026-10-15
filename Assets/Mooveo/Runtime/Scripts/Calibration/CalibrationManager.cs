using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
using UnityEngine.UIElements;
using Valve.VR;
// ⚠ "Button" et "Slider" existent À LA FOIS dans UnityEngine.UI (déjà utilisé dans ce fichier) et
// UnityEngine.UIElements — ambigus si on les écrit en nu. "Slider" est qualifié à sa déclaration
// (_debugSlider) ; "Button" est qualifié aux deux endroits où le bouton « Matériel » l'utilise.
// Le "using" reste nécessaire tel quel : sans lui, VisualElement.Q&lt;T&gt;() (méthode d'extension)
// ne se résout pas.

public enum EDeviceCheckerType
{ 
    HMD, 
    LeftController, 
    RightController, 
    AnyController,
    Tracker,
    SteamVR,
    EosUtility,
    HotFolder,
    // ⚠ Enum SÉRIALISÉE dans la scène : on ajoute TOUJOURS à la fin.
    HotFolderBanner,
    /// <summary>Ligne adossée à une ISystemCheck ; sa clé porte le nom de la vérification.</summary>
    SystemCheck
}

[Serializable]
public class DeviceCheckerConfig
{
    public string Key;
    public string Label;
    public EDeviceCheckerType Type;
    public bool RequiresExe;

    [Tooltip("Contrôle informatif : son échec n'empêche pas de démarrer.")]
    public bool Optional;
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
    /// <summary>Contrôle optionnel en échec : clé + raison courte. La ligne passe en avertissement.</summary>
    public event Action<string, string, string, bool> OnDeviceWarning;
    /// <summary>
    /// Un checker <see cref="IAsyncDeviceChecker"/> tourne toujours après <see cref="StalledSeconds"/> :
    /// clé, libellé, secondes écoulées depuis le lancement de CET appel. Ligne toujours BLOQUANTE
    /// (on l'attend encore) — distinct d'<see cref="OnDeviceWarning"/>, qui referme un contrôle
    /// optionnel. Se répète tant que l'appel ne rend pas la main, le compteur progresse.
    /// </summary>
    public event Action<string, string, float> OnDeviceStalled;
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
    
    // ⚠ UnityEngine.UI.Slider en toutes lettres : "Slider" existe aussi dans UnityEngine.UIElements
    // (utilisé par le bouton « Matériel » ci-dessous), ambigu à la compilation sinon.
    [SerializeField] private UnityEngine.UI.Slider _debugSlider;
    [SerializeField] protected bool _debug = false;

    [Tooltip("TEST (éditeur seulement) : fait durer chaque scan HotFolder ce nombre de secondes, " +
             "pour vérifier qu'un contrôle bloqué ne fige pas le reste de la liste. 0 = désactivé.")]
    [SerializeField] private float _debugHotFolderHangSeconds = 0f;

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
        if (HardwareDeviceRouting.Rejects(device, out string rejectReason))
        {
            if (_debug) Debug.Log($"[CalibrationManager] L'appareil {device.name} ignoré : {rejectReason}.");
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
        if (!isLeft && !isRight && HardwareDeviceRouting.AllowsForcedRightHand)
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
                uint deviceIndex = HardwareDeviceRouting.ResolveOpenVRIndex(device.description.serial, finalRole);

                SteamVR_Tracker steamVRTracker = instance.GetComponent<SteamVR_Tracker>();
                if (steamVRTracker != null)
                {
                    steamVRTracker.device = OpenVRUtility.GetSteamVRTrackerDevice(deviceIndex);
                    steamVRTracker.SetRole(finalRole);
                    HardwareDeviceRouting.ConfigureTracker(steamVRTracker);
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
        StartCoroutine(AddHardwareProfileButtonRoutine());
    }

    /// <summary>
    /// Bouton « Matériel » TOUJOURS accessible, quel que soit l'état de la calibration — posé
    /// directement sur <c>RootOverlay</c> et non dans un des panneaux que
    /// <c>UICalibrationToolkit.SetPanelVisibility</c> montre/masque.
    ///
    /// ⚠ Raison d'être : si le profil mémorisé ne correspond PAS au matériel réellement branché
    /// (ex. profil Vive alors qu'un casque PICO est là), SteamVR ne fournit aucun rôle de
    /// contrôleur que le profil actif sache lire — `AnyControllerChecker` ne trouve donc jamais
    /// rien, et la liste de vérification reste bloquée pour toujours. Sans un accès qui ne dépende
    /// PAS d'un contrôleur déjà détecté, l'opérateur n'aurait aucun moyen de s'en sortir : ce
    /// bouton répond à la SOURIS (panneau SCREEN SPACE — OVERLAY), donc joignable à zéro
    /// périphérique VR. Vécu et bloquant le 29/09/2026.
    /// </summary>
    private IEnumerator AddHardwareProfileButtonRoutine()
    {
        VisualElement root = null;
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < deadline)
        {
            root = UICalibrationToolkit.instance != null ? UICalibrationToolkit.instance.RootOverlay : null;
            if (root != null) break;
            yield return null;
        }

        if (root == null)
        {
            Debug.LogError("[CalibrationManager] Panneau de calibration introuvable : le bouton " +
                           "« Matériel » (échappatoire si le profil ne correspond pas au matériel " +
                           "branché) n'a pas pu être posé.");
            yield break;
        }
        if (root.Q<UnityEngine.UIElements.Button>("btn-hardware-profile-escape") != null) yield break; // Déjà posé.
        root.Add(BuildHardwareProfileButton(root));
    }

    /// <summary>
    /// Pastille discrète en haut à droite : point de couleur (identifie le profil actif, PAS un
    /// avertissement — les deux profils sont des configurations valides à égalité) + nom du profil,
    /// dans le même habillage translucide que les boutons de <c>ChoicePopup</c>. Le survol s'anime
    /// par callback, jamais en USS pur (§8 de CLAUDE.md — un bouton stylé inline écraserait un
    /// éventuel `:hover`). Se tient à jour tout seul via <see cref="HardwareProfiles.Changed"/>,
    /// désabonné à sa propre destruction pour ne pas laisser de fermeture accrochée après un
    /// changement de scène.
    /// </summary>
    private UnityEngine.UIElements.Button BuildHardwareProfileButton(VisualElement root)
    {
        var idleColor = new Color(0.11f, 0.11f, 0.12f, 0.85f);
        var hoverColor = new Color(0.20f, 0.20f, 0.22f, 0.92f);
        var borderColor = new Color(1f, 1f, 1f, 0.18f);
        var viveColor = new Color(0.18f, 0.80f, 0.44f);
        var picoColor = new Color(0.35f, 0.62f, 0.95f);

        var button = new UnityEngine.UIElements.Button
            { name = "btn-hardware-profile-escape", tooltip = "Choisir le matériel de suivi utilisé sur ce poste" };
        button.style.position = Position.Absolute;
        button.style.top = 14;
        button.style.right = 14;
        button.style.flexDirection = FlexDirection.Row;
        button.style.alignItems = Align.Center;
        button.style.height = 40;
        button.style.marginLeft = 0; button.style.marginRight = 0; button.style.marginTop = 0; button.style.marginBottom = 0;
        button.style.paddingLeft = 14; button.style.paddingRight = 16; button.style.paddingTop = 0; button.style.paddingBottom = 0;
        button.style.borderTopWidth = 1; button.style.borderBottomWidth = 1;
        button.style.borderLeftWidth = 1; button.style.borderRightWidth = 1;
        button.style.borderTopColor = borderColor; button.style.borderBottomColor = borderColor;
        button.style.borderLeftColor = borderColor; button.style.borderRightColor = borderColor;
        button.style.borderTopLeftRadius = 20; button.style.borderTopRightRadius = 20;
        button.style.borderBottomLeftRadius = 20; button.style.borderBottomRightRadius = 20;
        button.style.backgroundColor = idleColor;

        var dot = new VisualElement { pickingMode = PickingMode.Ignore };
        dot.style.width = 9; dot.style.height = 9;
        dot.style.borderTopLeftRadius = 5; dot.style.borderTopRightRadius = 5;
        dot.style.borderBottomLeftRadius = 5; dot.style.borderBottomRightRadius = 5;
        dot.style.marginRight = 9; dot.style.flexShrink = 0;
        button.Add(dot);

        var label = new Label { pickingMode = PickingMode.Ignore };
        label.style.color = Color.white;
        label.style.fontSize = 15;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        button.Add(label);

        void Refresh()
        {
            HardwareProfileData profile = HardwareProfiles.Current;
            label.text = profile.DisplayName;
            dot.style.backgroundColor = profile.Id == HardwareProfile.Pico4Enterprise ? picoColor : viveColor;
        }
        Refresh();

        System.Action<HardwareProfileData> onChanged = _ => Refresh();
        HardwareProfiles.Changed += onChanged;
        button.RegisterCallback<DetachFromPanelEvent>(e => HardwareProfiles.Changed -= onChanged);

        button.RegisterCallback<PointerEnterEvent>(e => button.style.backgroundColor = hoverColor);
        button.RegisterCallback<PointerLeaveEvent>(e => button.style.backgroundColor = idleColor);
        button.RegisterCallback<ClickEvent>(
            e => StartCoroutine(HardwareProfilePicker.ShowChangeMenu(root, OnHardwareProfileChangedFromCalibration)));

        return button;
    }

    /// <summary>
    /// Même remise à zéro que <see cref="StartOverCalibration"/> : un autre matériel n'a ni le même
    /// suivi ni forcément les mêmes rôles de contrôleur, les testeurs en place doivent être
    /// détruits et redécouverts, pas simplement gardés.
    /// </summary>
    private void OnHardwareProfileChangedFromCalibration(HardwareProfileData picked)
    {
        ClearTestCalibration(true); // Réinitialise aussi la suggestion de bascule.
        ScanAndSpawnTesters();
        Init();
    }

    // =========================================================================================
    // Suggestion de bascule matériel — aucun contrôleur trouvé depuis 20 s (cf. bouton « Matériel »)
    // =========================================================================================

    private const float ControllerMismatchSuggestSeconds = 20f;
    private float _anyControllerFailingSince = -1f;
    private bool _hardwareMismatchSuggested = false;

    private void TrackControllerMismatchSuggestion(bool controllerConnected)
    {
        if (controllerConnected) { ResetControllerMismatchSuggestion(); return; }

        if (_anyControllerFailingSince < 0f)
        {
            _anyControllerFailingSince = Time.realtimeSinceStartup;
            return;
        }
        if (_hardwareMismatchSuggested) return;
        if (Time.realtimeSinceStartup - _anyControllerFailingSince < ControllerMismatchSuggestSeconds) return;

        _hardwareMismatchSuggested = true; // Une seule suggestion par tentative : pas de relance toutes les 20 s.
        SuggestHardwareMismatch();
    }

    private void ResetControllerMismatchSuggestion()
    {
        _anyControllerFailingSince = -1f;
        _hardwareMismatchSuggested = false;
    }

    private void SuggestHardwareMismatch()
    {
        VisualElement root = UICalibrationToolkit.instance != null ? UICalibrationToolkit.instance.RootOverlay : null;
        // Une autre décision est déjà à l'écran (écran de premier choix, etc.) : ne pas la couper.
        if (root == null || ChoicePopup.IsOpen) return;

        HardwareProfileData current = HardwareProfiles.Current;
        ChoicePopup.Show(root, "Aucun contrôleur détecté",
            $"Depuis {ControllerMismatchSuggestSeconds:0} secondes, rien ne répond au profil actif " +
            $"« {current.DisplayName} ». Le matériel réellement branché est peut-être différent.",
            new[] { "Changer de matériel" }, "Continuer d'attendre",
            index =>
            {
                if (index != 0) return;
                StartCoroutine(HardwareProfilePicker.ShowChangeMenu(root, OnHardwareProfileChangedFromCalibration));
            });
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
                    UICalibrationToolkit.instance.ShowPopupError(
                        $"Suivi de {HardwareProfiles.Current.DeviceNoun} perdu : " +
                        $"{HardwareProfiles.Current.TrackingHint}. Reprise du scan dans 5 secondes...");
                }
                
                StartCoroutine(HandleTrackingLossRoutine());
            }
        }
    }

    private Action<bool> _launchAction;

    public virtual void Init()
    {
        MooveoConfigManager.EndMouseTest();
        // AVANT la calibration : les bordures de HotFolder portent un identifiant propre à chaque
        // poste, les réglages livrés sont donc toujours faux ailleurs qu'ici.
        HotFolderLocator.ApplyTo(GlobalSettings.Core.GlobalSettings.Instance);
        // Les gabarits à importer dans HotFolderPrint sont écrits d'office, sans attendre qu'un
        // défaut soit détecté : en build, sur un poste neuf, l'opérateur doit les trouver dans
        // Import/HotFolder même s'il n'a jamais ouvert le menu d'impression.
        HotFolderBannerTemplate.EnsureAll();
        // ⚠ Plus de popup dédiée ICI : la bannière est devenue une vérification comme les autres,
        // et c'est le bilan de fin de campagne qui l'annonce, avec sa procédure illustrée. Deux
        // popups qui s'enchaînent, l'une remplaçant l'autre, ne s'expliquaient pas.

        // ⚠ AVANT le système de vérifications (issue #71) : celles-ci dépendent du profil matériel
        // (HardwareChecks, calibration…) — les lancer sans profil choisi les ferait courir sur un
        // défaut provisoire. Rend immédiatement la main si un choix est déjà mémorisé.
        StartCoroutine(HardwareProfilePicker.ShowIfNeeded(
            () => UICalibrationToolkit.instance != null ? UICalibrationToolkit.instance.RootOverlay : null,
            ContinueInitAfterHardwareChoice));
    }

    private void ContinueInitAfterHardwareChoice()
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

    public bool CanStartMouseTest => _devicesChecked && GlobalSettings.Core.GlobalSettings.Instance != null &&
                                     GlobalSettings.Core.GlobalSettings.Instance.Admin.Value;

    public void StartMouseTest()
    {
        if (!CanStartMouseTest || ModalPopups.IsOpen) return;
        MooveoConfigManager.BeginMouseTest();
        Debug.Log("[CalibrationManager] Ouverture du mur de test à la souris (calibration temporaire).");
        LaunchScene();
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
        // Sondages en cours rattachés à d'anciennes configs : sans intérêt sur une nouvelle
        // campagne, on repart propre plutôt que de laisser une clé morte s'accumuler.
        _asyncPolls.Clear();

        // Ajouté par code : la ligne doit exister sur toutes les scènes de calibration, sans
        // avoir à rouvrir chacune pour la câbler à la main.
        if (!_devicesToCheck.Any(c => c.Type == EDeviceCheckerType.HotFolderBanner))
        {
            _devicesToCheck.Add(new DeviceCheckerConfig
            {
                Key = "hotfolder-banner",
                Label = "Bordures d'impression",
                Type = EDeviceCheckerType.HotFolderBanner,
                Optional = true
            });
        }

        InjectSystemCheckRows();

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
#if UNITY_EDITOR
                // Simulation de blocage : jamais en build, où elle retiendrait le démarrage.
                case EDeviceCheckerType.HotFolder: checker = new HotFolderChecker(_debugHotFolderHangSeconds); break;
#else
                case EDeviceCheckerType.HotFolder: checker = new HotFolderChecker(); break;
#endif
                case EDeviceCheckerType.HotFolderBanner: checker = new HotFolderBannerChecker(); break;
                // La clé EST le nom de la vérification : c'est ce qui relie la ligne au verdict.
                case EDeviceCheckerType.SystemCheck: checker = new SystemCheckRow(config.Key); break;
            }
            if (checker != null) _activeCheckers.Add(config, checker);
        }
    }
    /// <summary>
    /// Ajoute une ligne par vérification de portée Calibration, et lance la campagne qui les
    /// alimente. Par CODE et non dans l'inspecteur : ajouter une vérification ne doit demander
    /// d'ouvrir aucune scène, et la liste doit être la même partout.
    /// </summary>
    private void InjectSystemCheckRows()
    {
        _devicesToCheck.RemoveAll(c => c != null && c.Type == EDeviceCheckerType.SystemCheck);

        // ⚠ Passe COURTE dès qu'une campagne complète a déjà tourné dans la session : venir
        // calibrer depuis GraffWall rejouait vingt vérifications déjà faites, une seconde chacune.
        // Seules les indispensables sont rejouées, les autres gardent leur verdict. Première
        // campagne de la session : passe complète, sinon rien ne serait vérifié du tout.
        bool essentialOnly = SystemStatus.HasRun;

        foreach (string name in SystemCheckRunner.NamesInScope(CheckScope.Calibration, essentialOnly))
        {
            // Déjà couverte par une ligne native de la scène (« SteamVR ») : deux lignes de même clé
            // se disputeraient le même élément d'interface.
            if (_devicesToCheck.Any(c => c != null &&
                    (string.Equals(c.Key, name, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(c.Label, name, StringComparison.OrdinalIgnoreCase))))
                continue;

            _devicesToCheck.Add(new DeviceCheckerConfig
            {
                Key = name,
                Label = name,
                Type = EDeviceCheckerType.SystemCheck,
                Optional = true
            });
        }

        // La campagne tourne en arrière-plan : les lignes affichent leur spinner puis leur verdict.
        SystemCheckRunner.RunAll(scope: CheckScope.Calibration, essentialOnly: essentialOnly);
    }

    private bool DoesNeedCalibration()
    {
        // AJOUT VITAL : Force la calibration si on a appuyé sur "Start Over" ou "Refine"
        if (_ignoreConfig) return true;

        if (!MooveoConfigManager.Exists()) return true;
        _config = MooveoConfigManager.Load();
        if (_config.Points.Count <= 2) return true;

        // Calibration faite avec l'autre matériel : ses points sont dans un autre univers de suivi.
        if (!_config.MatchesCurrentHardware)
        {
            Debug.LogWarning($"[CalibrationManager] Calibration enregistrée avec « {_config.RecordedHardware} », " +
                             $"profil courant « {HardwareProfiles.Current.DisplayName} » : recalibration nécessaire.");
            return true;
        }

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

    /// <summary>
    /// État d'un <see cref="IAsyncDeviceChecker"/> — un par config, jamais deux sondages à la fois.
    /// Le DERNIER verdict survit au sondage suivant : sans lui, la ligne retombait sur « Checking… »
    /// à chaque relance et clignotait entre OK et « en cours ».
    /// </summary>
    private class AsyncCheckPoll
    {
        public Task<bool> Task;
        public float StartedAt;
        public bool HasResult;
        public bool LastResult;
    }
    private readonly Dictionary<DeviceCheckerConfig, AsyncCheckPoll> _asyncPolls = new Dictionary<DeviceCheckerConfig, AsyncCheckPoll>();

    /// <summary>Au-delà, une ligne encore en cours passe de « Checking… » à « pas de réponse ». Voir §13.</summary>
    private const float StalledSeconds = 3f;
    /// <summary>Repère de diagnostic (log seul) pour un checker qui doit rester sur le fil principal.</summary>
    private const long MainThreadCheckerBudgetMs = 250;

    /// <summary>
    /// Point d'entrée UNIQUE pour lire l'état d'un checker dans la boucle.
    /// ⚠ Pour un <see cref="IAsyncDeviceChecker"/>, ne BLOQUE JAMAIS le fil principal : rend le
    /// dernier résultat connu (faux tant qu'aucun sondage n'a encore abouti) et relance un sondage
    /// en arrière-plan dès que le précédent est terminé — jamais deux à la fois. Un sondage qui
    /// traîne au-delà de <see cref="StalledSeconds"/> émet <see cref="OnDeviceStalled"/> : la ligne
    /// le dit, la boucle continue les AUTRES checkers sans attendre celui-ci.
    /// Pour un checker ordinaire, appelle `IsConnected()` tel quel (contrat : quasi instantané, cf.
    /// `IDeviceChecker`) — seule une instrumentation de diagnostic est ajoutée.
    /// </summary>
    /// <param name="known">
    /// Faux tant qu'aucun verdict n'est disponible (premier sondage en cours, ou sondage bloqué).
    /// ⚠ L'appelant ne doit RIEN décider sur un verdict inconnu : « pas encore répondu » n'est pas
    /// « pas lancé » — relancer l'exe là-dessus ouvrirait le logiciel à chaque passage de la boucle.
    /// </param>
    private bool ResolveConnected(DeviceCheckerConfig config, IDeviceChecker checker, out bool stalled, out bool known)
    {
        stalled = false;
        known = true;

        if (!(checker is IAsyncDeviceChecker asyncChecker))
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool result = checker.IsConnected();
            watch.Stop();
            if (watch.ElapsedMilliseconds > MainThreadCheckerBudgetMs)
                Debug.LogWarning($"[CalibrationManager] « {config.Label} » : IsConnected() a mis " +
                    $"{watch.ElapsedMilliseconds} ms sur le fil principal (contrat : quasi instantané). " +
                    "S'il peut attendre du matériel/OS, il doit implémenter IAsyncDeviceChecker.");
            return result;
        }

        if (!_asyncPolls.TryGetValue(config, out AsyncCheckPoll poll))
        {
            poll = new AsyncCheckPoll();
            _asyncPolls[config] = poll;
        }

        // Sondage terminé : on récolte son verdict.
        if (poll.Task != null && poll.Task.IsCompleted)
        {
            if (poll.Task.Status == TaskStatus.RanToCompletion)
            {
                poll.LastResult = poll.Task.Result;
                poll.HasResult = true;
            }
            else
            {
                // Un sondage qui lève n'est pas un logiciel absent : on garde le verdict précédent.
                Debug.LogWarning($"[CalibrationManager] « {config.Label} » : sondage en échec — " +
                    $"{poll.Task.Exception?.GetBaseException().Message}");
            }
            poll.Task = null;
        }

        // Aucun sondage en vol : on en relance un pour la prochaine lecture, SANS jamais l'attendre
        // — c'est ce qui empêche un scan lent de retenir la frame.
        if (poll.Task == null)
        {
            poll.Task = asyncChecker.IsConnectedAsync();
            poll.StartedAt = Time.realtimeSinceStartup;
        }

        float elapsed = Time.realtimeSinceStartup - poll.StartedAt;
        if (elapsed >= StalledSeconds)
        {
            // Plus confirmé depuis trop longtemps : même un ancien « OK » ne vaut plus.
            stalled = true;
            known = false;
            OnDeviceStalled?.Invoke(config.Key, config.Label, elapsed);
            return false;
        }

        known = poll.HasResult;
        return poll.LastResult;
    }

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

                // ⚠ Jamais checker.IsConnected() en direct ici : ResolveConnected() est le seul
                // point qui sait dérouter un IAsyncDeviceChecker hors du fil principal, pour que ce
                // checker-là ne fige jamais la mise à jour des AUTRES lignes (cf. §13).
                bool isConnected = ResolveConnected(config, checker, out bool stalled, out bool known);
                string exePath = "";

                // Contrôle informatif : on l'affiche en avertissement et on passe. Bloquer ici
                // interdirait de peindre sur un poste sans imprimante.
                // ⚠ Le checker fait FOI : la case Optional de la scène ne peut que s'ajouter, pas
                // retirer. Elle avait été oubliée sur la ligne des bordures, et le contrôle bloquait
                // la calibration en boucle au lieu d'afficher son avertissement.
                IOptionalDeviceChecker optional = checker as IOptionalDeviceChecker;
                if (optional != null || config.Optional)
                {
                    // ⚠ TROIS états, pas deux. Sans l'état « en attente », une vérification
                    // asynchrone encore en cours s'afficherait en avertissement avant même d'avoir
                    // rendu son verdict — on accuserait un serveur qui répond très bien.
                    // ⚠ Un checker STALLED (sondage IAsyncDeviceChecker qui traîne) ne repasse PAS
                    // par ces branches : ResolveConnected vient d'émettre OnDeviceStalled, un
                    // « Checking… »/« indisponible » à la ligne suivante l'écraserait aussitôt.
                    if (stalled) { }
                    else if (isConnected) OnUpdateDeviceUI?.Invoke(config.Key, config.Label, true, "");
                    else if (optional != null && optional.Pending)
                        OnUpdateDeviceUI?.Invoke(config.Key, config.Label, false, "");
                    else OnDeviceWarning?.Invoke(config.Key, config.Label,
                             optional != null ? optional.Warning : "indisponible",
                             optional != null && optional.IsError);

                    // ⚠ Un contrôle informatif ne BLOQUE pas sur un avertissement, mais on attend son
                    // VERDICT : sans ça, « Appuyer sur la gâchette » s'affichait — et la gâchette
                    // passait à la suite — alors que la moitié de la liste tournait encore.
                    if (!isConnected && optional != null && optional.Pending) allDevicesValid = false;

                    // ⚠ Cadence COURTE. Ces lignes sont nombreuses (une par vérification système) :
                    // à une demi-seconde chacune, la boucle mettait une dizaine de secondes à
                    // revenir sur les contrôles BLOQUANTS, et une manette rebranchée n'était vue
                    // qu'au tour suivant. Le rythme d'affichage vient de la campagne, pas d'ici.
                    yield return new WaitForSeconds(0.1f);
                    continue;
                }

                // ⚠ `known` : sur un sondage encore sans verdict, « faux » veut dire « on ne sait
                // pas », pas « logiciel fermé » — relancer l'exe ici l'ouvrirait à chaque passage.
                if (!isConnected && known && config.RequiresExe)
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

                // ⚠ Pas de « Checking… » ici si ResolveConnected vient d'annoncer un sondage
                // bloqué : OnDeviceStalled l'a déjà dit sur cette même ligne, l'écraser aussitôt
                // ferait disparaître le signal avant que l'opérateur ait pu le lire.
                if (!stalled) OnUpdateDeviceUI?.Invoke(config.Key, config.Label, isConnected, exePath);

                if (!isConnected) allDevicesValid = false;

                // ⚠ AnyController introuvable après 20 s : c'est le symptôme d'un profil matériel qui
                // ne correspond pas à ce qui est branché (vécu le 29/09/2026, cf. §14 de CLAUDE.md) —
                // SteamVR, configuré pour l'AUTRE profil, ne fournit alors aucun rôle de contrôleur.
                // Le bouton « Matériel » (toujours visible) reste la vraie échappatoire ; ceci ne fait
                // que la SUGGÉRER, pour ne pas laisser deviner pourquoi rien ne se passe.
                if (config.Type == EDeviceCheckerType.AnyController) TrackControllerMismatchSuggestion(isConnected);

                // Contrôle BLOQUANT : c'est lui qui retient le démarrage, on le rafraîchit vite.
                yield return new WaitForSeconds(0.1f);
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

    // "Valider" / "Lancer" : persiste la calibration puis charge GraffWall. La SÉLECTION DE MODE
    // DE JEU n'est PLUS affichée dans la scène de calibration (le scroll de la scrollview ne s'y
    // comportait pas comme en jeu) : elle est ouverte DANS GraffWall si aucun mode n'est déjà
    // choisi (GameModeSelection.PendingMode == null), cf. GlobalWorldUI.ApplyPendingModeWhenReady.
    [ContextMenu("Launch Game")]
    public void Launch()
    {
        if(_config == null) return;
        if(_calibrated)SaveConfig();
        if (!MooveoConfigManager.Exists())
        {
            if (_debug) Debug.LogError("CALIBRATION : Le fichier n'a pas été sauvegardé, lancement annulé.");
            return;
        }

        LaunchScene();
    }

    /// <summary>Charge la scène GraffWall.</summary>
    public void LaunchScene()
    {
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
        // Réduits à 3 points par `MooveoConfigManager.Save` (moyenne par colonne d'un affinage).
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

        // ⚠ Aperçu sur les points RÉDUITS, comme ce qui sera persisté puis relu par GraffWall :
        // sur les neuf points bruts d'un affinage, l'aperçu montrait la seule ligne du milieu.
        List<Vector3> points = MooveoConfig.ReducePoints(_points);
        Vector3 center = CalculateCenter(points, out int centerIndex);
        if (_debug) Debug.Log($"Center is {center}");
        (Vector3 left, Vector3 right) = CalculateLeftAndRight(points, centerIndex);

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
            // Axe du PROFIL : Up pour un tracker plaqué au mur, Forward pour une manette pointée vers
            // l'écran. Il décide du sens du mur dans le jeu (CalibrationGraffWall.SetupPlayArea).
            averageForward += HardwareProfiles.Current.CalibrationNormalAxis == CalibrationAxis.Forward
                ? controllerTransform.forward
                : controllerTransform.up;
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

        // Affinage (9 points) : groupés par colonnes (Gauche, Centre, Droite) pour un axe
        // horizontal moyenné sur les 3 lignes scannées. MÊME réduction que l'aperçu et le fichier
        // (`MooveoConfig.ReducePoints`) : ce qui est validé ici est exactement ce qui sera persisté.
        // Un premier calibrage à 3 points ressort inchangé.
        List<Vector3> reducedPoints = MooveoConfig.ReducePoints(_points);
        List<Vector3> normalsToTest = MooveoConfig.ReduceNormals(_normals);
        Vector3 p1 = reducedPoints[0], p2 = reducedPoints[1], p3 = reducedPoints[2];

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
        ResetControllerMismatchSuggestion(); // Nouvelle tentative : le silence précédent ne vaut plus.

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
        // Tracker-only scenes do not have a hand-controller prefab. Their mouse entry
        // point is the explicit test-wall button, independent of a simulated XR device.
        if (_testerPrefab == null || _adminTester != null) return;
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
        
        // Le PARAMÈTRE, pas le champ `_points` : l'aperçu passe une liste réduite.
        Vector3 avg = (points[0] + points[1] + points[2]) / 3f;

        centerIndex = 0;
        float minDist = Vector3.Distance(points[0], avg);
        for (int i = 1; i < 3; i++)
        {
            float d = Vector3.Distance(points[i], avg);
            if (d < minDist)
            {
                minDist = d;
                centerIndex = i;
            }
        }
        return points[centerIndex];
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
