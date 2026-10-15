using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;

public class UICalibrationToolkit : MonoBehaviour
{
    public static UICalibrationToolkit instance;

    /// <summary>
    /// Aide affichée sur « Commencer » en test à la souris (mode Admin). Point d'extension : le jeu
    /// la remplace par la description de SES contrôles souris (GraffWall : <c>AdminMouseSpray.ControlsHint</c>).
    /// </summary>
    public static string MouseTestControlsHint = "Clic gauche : menus · Clic droit : action principale · Molette : taille";

    [Header("Hybrid UI Setup")]
    [SerializeField] private UIDocument _docOverlay;
    [SerializeField] private UIDocument _docWorld;
    [SerializeField] private Transform _canvasCursors;


    [Header("Configuration")]
    [SerializeField] private float _zoneRadiusCm = 5f;
    
    [Header("UI Dimensions Reference")]
    [Tooltip("Largeur en pixels de votre UI Document (ex: 1920)")]
    [SerializeField] private float _uiWidthPixels = 1920f;
    [Tooltip("Pixels Per Unit de votre Panel Settings (ex: 100)")]
    [SerializeField] private float _uiPanelPPU = 100f;
    
    [Header("UI Calibration Loading Params")]
    [SerializeField] private float _lerpSpeed = 15f;

    [SerializeField] private float _reDrawThreshold =  0.0001f;
    
    public event Action<bool> OnDevicesChecked;
    
    public VisualElement RootOverlay => _rootOverlay;
    private VisualElement _rootOverlay;
    private VisualElement _rootWorld;
    
    private VisualElement _pnlCheckingDevices;
    private ScrollView _devicesList;
    private Button _btnStart;
    private bool _readyToStart;
    private VisualElement _pnlCalibrationOverlay;
    private VisualElement _calibrationMarksContainer;
    private VisualElement _pnlValidationFooter;
    private Button _btnRefine;
    private Button _btnStartOver;
    private Button _btnValidate;
    private VisualElement _pnlPopupLaunch;
    private Button _btnPopupRecalibrate;
    private Button _btnPopupLaunch;

    private Coroutine _mainFlowRoutine;
    private Coroutine _secondaryFlowRoutine;
    private bool _allDevicesValid = false;
    private bool _calibratingStep = false;
    private bool _controllerClicked = false;
    private bool _waitingForClick = false;
    private bool _currentPointHasError = false;
    private bool _exitCalibration = false;
    private bool _checkDevicePanelEnabled = true;

    private int _currentIndexPoint = -1;
    private int _nPointToCalibrate = 3;

    private List<VisualElement> _markElements = new List<VisualElement>();
    private List<CalibrationCursorUI> _cursors = new List<CalibrationCursorUI>();
    
    private float _targetProgress = 0f;
    private float _displayedProgress = 0f;
    
    private VisualElement _loadingOverlay;
    private VisualElement _loadingSpinner;
    private Label _loadingText;
    private IVisualElementScheduledItem _spinnerAnim;
    
    private bool _subscribed = false;

    // --- Défilement automatique de la liste de vérification ---
    // La liste dépasse largement la hauteur disponible depuis qu'elle porte les vérifications
    // système : on n'en montre qu'une fenêtre, et on la fait suivre au fur et à mesure des verdicts.
    private const int VisibleDeviceRows = 6;

    /// <summary>
    /// Délai entre le verdict d'une ligne et le défilement. Sans lui, une ligne qui bascule ET
    /// s'en va dans la même frame ne se voit jamais : on veut lire la coche AVANT que la liste
    /// avance.
    /// </summary>
    private const long DeviceScrollDelayMs = 500;

    private const float DeviceScrollSeconds = 0.35f;
    private const long DeviceReorderMs = 260;

    private readonly HashSet<string> _resolvedDeviceRows = new HashSet<string>();

    // Dernier verdict AFFICHÉ, et temps laissé à la liste pour finir de défiler après lui
    // (délai de défilement + défilement + réorganisation, avec de la marge).
    private float _lastRowResolvedAt;
    private const float ListSettleSeconds = 1.3f;

    // Filet : une ligne qui ne rendrait jamais son verdict à l'écran ne doit pas bloquer à vie.
    private const float ReadyTimeoutSeconds = 20f;

    private const string ReadyPrompt = "Appuyer sur la gâchette pour commencer";
    private Coroutine _readyRoutine;
    private float _deviceRowHeight;
    private float _deviceScrollTarget;
    private IVisualElementScheduledItem _deviceScrollAnim;

    /// <summary>
    /// L'opérateur a pris la liste en main (glisser, poignée, molette) : le défilement automatique
    /// se tait jusqu'à la prochaine ouverture du panneau. Sans ça, il ramenait la liste sur la
    /// première ligne en cours au verdict suivant — la souris se battait contre lui et perdait.
    /// </summary>
    private bool _deviceScrollManual;
    /// <summary>Liste déjà équipée : InitializeUI peut repasser, un second manipulateur doublerait le défilement.</summary>
    private ScrollView _wiredDevicesList;

    private void Awake()
    {
        if (instance == null) instance = this;
        else Destroy(this);
    }

    private const string ResolveButtonClass = "device-resolve";

    // Clés des lignes injectées pour une vérification système (les seules que le bilan retire).
    private readonly HashSet<string> _systemCheckRows = new HashSet<string>();

    // Le bilan affiché propose-t-il « Tout résoudre » ? Sinon son bouton vert relance seulement.
    private bool _summaryOffersRepair;

    private void OnEnable()
    {

        if (_docOverlay == null && GetComponent<UIDocument>() != null) _docOverlay = GetComponent<UIDocument>();
        
        if (_docOverlay != null || _docWorld != null)
        {
            InitializeUI();
        }
        
        if (CalibrationManager.instance != null)
        {
            Debug.Log("[UICalibrationToolkit] Subscribing to calibration events");
            CalibrationManager.instance.OnInitDevicesUI += BuildDevicesList;
            CalibrationManager.instance.OnUpdateDeviceUI += UpdateDeviceStatusUI;
            CalibrationManager.instance.OnDeviceWarning += SetDeviceWarning;
            CalibrationManager.instance.OnDeviceStalled += SetDeviceStalled;
            CalibrationManager.instance.OnAllDevicesReady += HandleAllDevicesReady;
            _subscribed = true;
        }

        SystemStatus.Completed -= HandleSystemChecksCompleted;
        SystemStatus.Completed += HandleSystemChecksCompleted;
        RepairRunner.Changed -= OnRepairStateChanged;
        RepairRunner.Changed += OnRepairStateChanged;
    }

    private void OnDisable()
    {
        UnsubscribeFromCalibrationEvents();
        if (CalibrationManager.instance != null)
        {
            CalibrationManager.instance.OnInitDevicesUI -= BuildDevicesList;
            CalibrationManager.instance.OnUpdateDeviceUI -= UpdateDeviceStatusUI;
            CalibrationManager.instance.OnDeviceWarning -= SetDeviceWarning;
            CalibrationManager.instance.OnDeviceStalled -= SetDeviceStalled;
            CalibrationManager.instance.OnAllDevicesReady -= HandleAllDevicesReady;
        }

        SystemStatus.Completed -= HandleSystemChecksCompleted;
        RepairRunner.Changed -= OnRepairStateChanged;
    }
    private void Start()
    {
        if (!_subscribed)
        {
            CalibrationManager.instance.OnInitDevicesUI += BuildDevicesList;
            CalibrationManager.instance.OnUpdateDeviceUI += UpdateDeviceStatusUI;
            CalibrationManager.instance.OnDeviceWarning += SetDeviceWarning;
            CalibrationManager.instance.OnDeviceStalled += SetDeviceStalled;
            CalibrationManager.instance.OnAllDevicesReady += HandleAllDevicesReady;
            _subscribed = true;
        }
        AnimateSpinners();
    }

    private void Update()
    {
        if (_readyToStart && !ModalPopups.IsOpen && Keyboard.current != null &&
            !Keyboard.current.leftAltKey.isPressed && !Keyboard.current.rightAltKey.isPressed &&
            (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            ActivateStart();
        if (_currentIndexPoint >= 0 && _currentIndexPoint < _markElements.Count)
        {
            float newProgress = Mathf.Lerp(_displayedProgress, _targetProgress, Time.deltaTime * _lerpSpeed);
            
            if (Mathf.Abs(_displayedProgress - newProgress) > _reDrawThreshold)
            {
                _displayedProgress = newProgress;
                _currentProgress = _displayedProgress;

                var currentMark = _markElements[_currentIndexPoint];
                var fill = currentMark.Q<VisualElement>(className: "calibration-point-fill");
                fill?.MarkDirtyRepaint();
            }
        }    
    }

    #region SetupUI
        private void InitializeUI()
        {
            if (_docOverlay != null)
            {
                _rootOverlay = _docOverlay.rootVisualElement;
                _pnlCheckingDevices = _rootOverlay.Q<VisualElement>("pnl-checking-devices");
                _devicesList = _rootOverlay.Q<ScrollView>("devices-list");
                _btnStart = _rootOverlay.Q<Button>("btn-start");
                if (_btnStart != null)
                {
                    _btnStart.UnregisterCallback<ClickEvent>(OnStartClicked);
                    _btnStart.RegisterCallback<ClickEvent>(OnStartClicked);
                }
                WireDevicesListScrolling();
                // Invitation cachée d'emblée : elle n'apparaît qu'une fois la liste prête.
                HideReadyPrompt();
                _pnlCalibrationOverlay = _rootOverlay.Q<VisualElement>("pnl-calibration-overlay");
                _calibrationMarksContainer = _rootOverlay.Q<VisualElement>("calibration-marks-container");
                
                _loadingOverlay = _rootOverlay.Q<VisualElement>("loading-overlay");
                _loadingSpinner = _rootOverlay.Q<VisualElement>("loading-spinner");
                _loadingText = _rootOverlay.Q<Label>("loading-text");
                
                // CRÉATION DYNAMIQUE SI NON TROUVÉ DANS L'UXML
                if (_loadingOverlay == null && _pnlCalibrationOverlay != null)
                {
                    _loadingOverlay = new VisualElement();
                    _loadingOverlay.name = "loading-overlay";
                    _loadingOverlay.style.position = Position.Absolute;
                    _loadingOverlay.style.left = 0;
                    _loadingOverlay.style.top = 0;
                    _loadingOverlay.style.right = 0;
                    _loadingOverlay.style.bottom = 0;
                    _loadingOverlay.style.alignItems = Align.Center;
                    _loadingOverlay.style.justifyContent = Justify.Center;
                    _loadingOverlay.style.backgroundColor = new Color(0, 0, 0, 0.7f); // Voile assombri

                    _loadingText = new Label();
                    _loadingText.name = "loading-text";
                    _loadingText.style.color = Color.white;
                    _loadingText.style.fontSize = 28;
                    _loadingText.style.marginBottom = 20;

                    _loadingSpinner = new VisualElement();
                    _loadingSpinner.name = "loading-spinner";
                    _loadingSpinner.style.width = 60;
                    _loadingSpinner.style.height = 60;
                    _loadingSpinner.AddToClassList("icon-status-loading");

                    _loadingOverlay.Add(_loadingText);
                    _loadingOverlay.Add(_loadingSpinner);
                    _pnlCalibrationOverlay.Add(_loadingOverlay);
                }
                
                if (_loadingOverlay != null) _loadingOverlay.style.display = DisplayStyle.None;
            }
            
            if (_docWorld != null)
            {
                _rootWorld = _docWorld.rootVisualElement;
                _pnlValidationFooter = _rootWorld.Q<VisualElement>("pnl-validation-footer");
                
                _btnRefine = _rootWorld.Q<Button>("btn-refine");
                _btnStartOver = _rootWorld.Q<Button>("btn-start-over");
                _btnValidate = _rootWorld.Q<Button>("btn-validate");
                //Debug.Log($"[UI_BIND_DEBUG] btn-validate found: {(_btnValidate != null)}"); // AJOUTE CETTE LOG
                
                _pnlPopupLaunch = _rootWorld.Q<VisualElement>("pnl-popup-launch");
                _btnPopupRecalibrate = _rootWorld.Q<Button>("btn-popup-recalibrate");
                _btnPopupLaunch = _rootWorld.Q<Button>("btn-popup-launch");

                // FIX: Hide panels that are NOT supposed to be in World Space (Ghost panels from shared UXML)
                _rootWorld.Q<VisualElement>("pnl-checking-devices")?.AddToClassList("animated-panel--hidden"); 
                _rootWorld.Q<VisualElement>("pnl-calibration-overlay")?.AddToClassList("animated-panel--hidden");
                // Also force hide via style to be sure they don't interfere with layout
                var ghostChecking = _rootWorld.Q<VisualElement>("pnl-checking-devices");
                if(ghostChecking != null) ghostChecking.style.display = DisplayStyle.None;
                
                var ghostOverlay = _rootWorld.Q<VisualElement>("pnl-calibration-overlay");
                if(ghostOverlay != null) ghostOverlay.style.display = DisplayStyle.None;
            }
            
            // FIX: Hide panels that are NOT supposed to be in Overlay Space (Ghost panels from shared UXML)
            if (_rootOverlay != null)
            {
                var ghostFooter = _rootOverlay.Q<VisualElement>("pnl-validation-footer");
                if(ghostFooter != null) 
                {
                    ghostFooter.AddToClassList("animated-panel--hidden");
                    ghostFooter.style.display = DisplayStyle.None;
                }
                
                var ghostPopup = _rootOverlay.Q<VisualElement>("pnl-popup-launch");
                if(ghostPopup != null) 
                {
                    ghostPopup.AddToClassList("animated-panel--hidden");
                    ghostPopup.style.display = DisplayStyle.None;
                }
            }
            
            // Initialize panels state (Hidden by default)
            SetPanelVisibility(_pnlCheckingDevices, false);
            SetPanelVisibility(_pnlCalibrationOverlay, false);
            SetPanelVisibility(_pnlValidationFooter, false);
            SetPanelVisibility(_pnlPopupLaunch, false);

            _btnRefine?.RegisterCallback<ClickEvent>(evt => CalibrationManager.instance.RefineCalibration());
            _btnStartOver?.RegisterCallback<ClickEvent>(evt => CalibrationManager.instance.StartOverCalibration());
            _btnValidate?.RegisterCallback<ClickEvent>(evt => CalibrationManager.instance.Launch());
            
            _btnPopupRecalibrate?.RegisterCallback<ClickEvent>(evt => CalibrationManager.instance.StartOverCalibration());
            _btnPopupLaunch?.RegisterCallback<ClickEvent>(evt => CalibrationManager.instance.Launch());
            
            ShowPanel(_pnlCheckingDevices);
            InitMarksPositions(); // Create marks in UI (in Overlay)
        }
    private float _currentProgress = 0f;

    private void InitMarksPositions()
    {
        _calibrationMarksContainer.Clear();
        _markElements.Clear();

        var positions = new List<(float x, float y)>
        {
            (25f, 50f), // 0: Left
            (50f, 50f), // 1: Center
            (75f, 50f), // 2: Right
            (20f, 20f), // 3: Top-Left
            (50f, 20f), // 4: Top-Center
            (80f, 20f), // 5: Top-Right
            (20f, 80f), // 6: Bottom-Left
            (50f, 80f), // 7: Bottom-Center
            (80f, 80f)  // 8: Bottom-Right
        };

        for (int i = 0; i < positions.Count; i++)
        {
            VisualElement mark = new VisualElement();
            mark.AddToClassList("calibration-point");
            mark.style.left = Length.Percent(positions[i].x);
            mark.style.top = Length.Percent(positions[i].y);

            mark.style.translate = new StyleTranslate(new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent), 0));

            VisualElement fill = new VisualElement();
            fill.AddToClassList("calibration-point-fill");
            fill.userData = i; // CRITICAL: Store index for callback
            fill.generateVisualContent += OnGenerateVisualContent;
            mark.Add(fill);

            VisualElement icon = new VisualElement();
            icon.AddToClassList("calibration-point-icon");
            mark.Add(icon);

            _calibrationMarksContainer.Add(mark);
            _markElements.Add(mark);
            mark.visible = false;
        }
    }

    private void OnGenerateVisualContent(MeshGenerationContext mgc)
    {
        VisualElement fill = mgc.visualElement;
        
        if (fill.userData == null) return;
        int index = (int)fill.userData;
        
        bool isError = fill.parent.ClassListContains("error");

        float progress = 0f;
        if (index < _currentIndexPoint) progress = 1f;
        else if (index == _currentIndexPoint) progress = _currentProgress;

        var painter = mgc.painter2D;
        painter.BeginPath(); 
    
        float w = fill.layout.width;
        float h = fill.layout.height;
        if (float.IsNaN(w) || w == 0) w = 80f; 
        if (float.IsNaN(h) || h == 0) h = 80f;

        painter.lineWidth = 6f;
        painter.lineCap = LineCap.Round;
    
        float radius = (w / 2f) - (painter.lineWidth / 2f);
        Vector2 center = new Vector2(w / 2f, h / 2f);
        
        if (isError)
        {
            painter.strokeColor = new Color(231/255f, 76/255f, 60/255f); // Rouge
        }
        else
        {
            painter.strokeColor = new Color(1f, 1f, 1f, 0.1f);
        }
    
        painter.Arc(center, radius, 0f, 360f);
        painter.Stroke();
        
        if (!isError && progress > 0.001f)
        {
            painter.BeginPath();
            painter.strokeColor = new Color(46/255f, 204/255f, 113/255f); // Vert
        
            float startAngle = -90f;
            float endAngle = -90f + (progress * 360f);
        
            painter.Arc(center, radius, startAngle, endAngle);
            painter.Stroke();
        }
    }

    private void RebuildDevicesList()
    {
        _devicesList.Clear();

        AddDeviceRow("steam", "SteamVR");
        AddDeviceRow("hmd", "HMD");
        AddDeviceRow("anyController", "Controllers");
        AddDeviceRow("eos", "EOS Utility");
        AddDeviceRow("hotfolder", "Hot Folder");
    }
    private void AddDeviceRow(string key, string label)
    {
        Debug.Log($"key {key} | label {label}");
        VisualElement row = new VisualElement();
        row.AddToClassList("device-row");
        row.name = $"device-row-{key}";

        Label lbl = new Label(label);
        lbl.AddToClassList("device-name");
        row.Add(lbl);

        VisualElement icon = new VisualElement();
        icon.AddToClassList("status-icon");
        icon.AddToClassList("icon-status-loading");
        icon.name = "status-icon";
        row.Add(icon);

        _devicesList.Add(row);
    }
    // Removed old DrawCircularProgress, logic moved to OnGenerateVisualContent

    #endregion

    #region Flows
        public void StartMainFlow(int nPointToCalibrate)
        {
            StopAllFlows();
            ClearCalibrationUIData();
            _nPointToCalibrate = nPointToCalibrate;
            _mainFlowRoutine = StartCoroutine(CalibrationLoop());
        }
        public void StartCheckingDevices()
        {
            StopAllFlows();
            if (CalibrationManager.instance != null)
            {
                CalibrationManager.instance.Init(); 
            }
        }
        
        private IEnumerator WaitForControllerClick(Action onClick = null)
        {
             _waitingForClick = true;
            _controllerClicked = false;
            
            //Debug.Log("Waiting for Click...");
    
            while (true)
            {
                if(_controllerClicked)
                {
                    onClick?.Invoke();
                    _waitingForClick = false;
                    yield break;
                }
                 yield return null;
            }
        }
        private IEnumerator CalibrationLoop()
        {
            _exitCalibration = false;
            
            CalibrationManager.instance.OnCalibrationEnded += OnCalibrationFinished;
        
            ShowPanel(_pnlCalibrationOverlay);
            
            _currentIndexPoint = 0;
            for(int i = 0; i < _markElements.Count; i++)
            {
                var mark = _markElements[i];
                mark.RemoveFromClassList("valid");
                mark.RemoveFromClassList("error");
            
                var fill = mark.Q<VisualElement>(className: "calibration-point-fill");
                // fill.style.scale = new Scale(Vector3.zero); // Removed to allow visibility
                
                mark.visible = (i == 0); 
            }
            
            SubscribeToCalibrationEvents();
        
            //Debug.Log("Calibration Loop Started : Waiting for user input via CalibrationManager...");
            
            while (!_exitCalibration)
            {
                yield return new WaitForSeconds(0.1f);
            }
            
            //Debug.Log("CalibrationLoop -> 3 points acquired.");
            UnsubscribeFromCalibrationEvents();
            CalibrationManager.instance.OnCalibrationEnded -= OnCalibrationFinished;
            
            yield return StartCoroutine(WaitForControllerClick());
            
            CalibrationManager.instance.TestCalibrationSetupPlayArea();
            
            ShowPanel(_pnlValidationFooter);
        }
        public void StopAllFlows()
        {
            if (_mainFlowRoutine != null) StopCoroutine(_mainFlowRoutine);
            _mainFlowRoutine = null;
            if (_secondaryFlowRoutine != null) StopCoroutine(_secondaryFlowRoutine);
            _secondaryFlowRoutine = null;
        }
    #endregion

    #region CalibrationManagerCommunication
        public void NotifyOnNextStep(CalibrationController calibrationController = null)
        {
            // ⚠ Une popup modale ouverte BLOQUE la progression : la gâchette sert à cliquer dedans,
            // elle ne doit pas faire passer à l'étape suivante par la même occasion.
            if (ModalPopups.IsOpen) return;

            if (_waitingForClick && !_calibratingStep)
            {
                _controllerClicked = true;
                //Debug.Log("UICalibrationToolkit -> Controller click received");
            }
        }
        private void OnCalibrationFinished()
        {
            _exitCalibration = true;
        }
        private void OnPointSubmitted()
        {
            if (_currentIndexPoint < 0 || _currentIndexPoint >= _markElements.Count) return;

            var finishedMark = _markElements[_currentIndexPoint];
            
            var icon = finishedMark.Q<VisualElement>(className: "calibration-point-icon");
            if (icon != null) icon.style.opacity = 1;
            
            finishedMark.AddToClassList("valid");
    
            _currentIndexPoint++; 
            _currentPointHasError = false;

            // Force repaint on the previous mark to ensure it draws as full circle (index < _currentIndexPoint)
            finishedMark.Q<VisualElement>(className: "calibration-point-fill")?.MarkDirtyRepaint();

            if (_currentIndexPoint < _nPointToCalibrate)
            {
                _markElements[_currentIndexPoint].visible = true;
                _targetProgress = 0f;
                _displayedProgress = 0f;
                _currentProgress = 0f;
            }
        }
        private void OnUpdatePointProgress(float value)
        {
            if (_currentIndexPoint < 0 || _currentIndexPoint >= _markElements.Count) return;
            if (_currentPointHasError)
            {
                var mark = _markElements[_currentIndexPoint];
                mark.RemoveFromClassList("error");
                
                var icon = mark.Q<VisualElement>(className: "calibration-point-icon");
                if(icon != null) icon.style.opacity = 0;
        
                _currentPointHasError = false;
                
                mark.Q<VisualElement>(className: "calibration-point-fill")?.MarkDirtyRepaint();
            }

            _targetProgress = value;
        }
        private void OnPointError(string errorMessage)
        {
            if (_currentIndexPoint < 0 || _currentIndexPoint >= _markElements.Count) return;
    
            var mark = _markElements[_currentIndexPoint];
            mark.AddToClassList("error");
            var icon = mark.Q<VisualElement>(className: "calibration-point-icon");
            if(icon != null) icon.style.opacity = 1;

            _currentPointHasError = true;
        
            _targetProgress = 0f;
            _displayedProgress = 0f;
            _currentProgress = 0f;
        
            mark.Q<VisualElement>(className: "calibration-point-fill")?.MarkDirtyRepaint();
            
            ShowPopupError(errorMessage);
        }
    #endregion

    #region Helpers
        private void SubscribeToCalibrationEvents()
    {
        if (CalibrationManager.instance == null) return;
    
        UnsubscribeFromCalibrationEvents();
    
        CalibrationManager.instance.OnUpdatePoint += OnUpdatePointProgress;

        CalibrationManager.instance.OnSubmitPoint += OnPointSubmitted; 
        CalibrationManager.instance.OnErrorDuringCalibration += OnPointError;
    }
        private void UnsubscribeFromCalibrationEvents()
    {
        if (CalibrationManager.instance == null) return;

        CalibrationManager.instance.OnUpdatePoint -= OnUpdatePointProgress;
        CalibrationManager.instance.OnSubmitPoint -= OnPointSubmitted;
        CalibrationManager.instance.OnErrorDuringCalibration -= OnPointError;
    }
    private bool AllDevicesStillValid()
    {
        if (CalibrationManager.instance != null)
        {
            return CalibrationManager.instance.AreControllersConnected();
        }
        return true;
    }
        private void ClearCalibrationUIData()
        {
            SetValidationUI(false);
            _allDevicesValid = false;
            _calibratingStep = false;
            _controllerClicked = false;
            _waitingForClick = false;
            _currentPointHasError = false;
            _exitCalibration = false;
            _checkDevicePanelEnabled = true;
            _currentIndexPoint = -1;
            
            _targetProgress = 0f;
            _displayedProgress = 0f;
            _currentProgress = 0f;
        
            foreach(var m in _markElements)
            {
                m.RemoveFromClassList("valid");
                m.RemoveFromClassList("error");
                
                var icon = m.Q<VisualElement>(className: "calibration-point-icon");
                if (icon != null) icon.style.opacity = 0; 
                
                VisualElement fill = m.Q<VisualElement>(className: "calibration-point-fill");
                if(fill != null) 
                {
                    fill.MarkDirtyRepaint(); 
                }
            
                m.visible = false;
            }
        
            SetPanelVisibility(_pnlValidationFooter, false);
        }

        /// <summary>
        /// Affiche ou masque l'UI de chargement de la validation asynchrone pour UI Toolkit.
        /// </summary>
        public void SetValidationUI(bool isVisible, string message = "")
        {
            if (_loadingOverlay != null)
            {
                _loadingOverlay.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (isVisible)
            {
                if (_loadingText != null)
                {
                    _loadingText.text = message;
                    _loadingText.style.display = DisplayStyle.Flex;
                }

                if (_loadingSpinner != null)
                {
                    _loadingSpinner.style.display = DisplayStyle.Flex;
                    if (_spinnerAnim == null)
                    {
                        _spinnerAnim = _loadingSpinner.schedule.Execute(() => {
                            _loadingSpinner.transform.rotation = _loadingSpinner.transform.rotation * Quaternion.Euler(0, 0, -15);
                        }).Every(30);
                    }
                    else
                    {
                        _spinnerAnim.Resume();
                    }
                }
            }
            else
            {
                if (_spinnerAnim != null)
                {
                    _spinnerAnim.Pause();
                }
            }
        }

        /// <summary>
        /// Marque tous les points avec la classe d'erreur visuelle (UI Toolkit) et affiche le message à l'écran.
        /// </summary>
        public void MarkAllPointsError(string errorMessage)
        {
            for (int i = 0; i < _nPointToCalibrate; i++)
            {
                if (i < _markElements.Count)
                {
                    var mark = _markElements[i];
                    mark.AddToClassList("error");
                    var icon = mark.Q<VisualElement>(className: "calibration-point-icon");
                    if (icon != null) icon.style.opacity = 1f;
                    
                    mark.visible = true;
                    mark.Q<VisualElement>(className: "calibration-point-fill")?.MarkDirtyRepaint();
                }
            }
            
            SetValidationUI(true, errorMessage);
            if (_loadingSpinner != null) _loadingSpinner.style.display = DisplayStyle.None; // Cache le spinner pour laisser seule l'erreur visible
        }

        public void ShowPopupError(string errorMessage)
        {
            var lblInstruction = _pnlCalibrationOverlay?.Q<Label>("lbl-instruction");
            if(lblInstruction != null) lblInstruction.text = errorMessage;
            
            SetValidationUI(true, errorMessage);
            if (_loadingSpinner != null) _loadingSpinner.style.display = DisplayStyle.None;
            
            StartCoroutine(ClearErrorRoutine());
        }

        private IEnumerator ClearErrorRoutine()
        {
            yield return new WaitForSeconds(3f);
            SetValidationUI(false);
            var lblInstruction = _pnlCalibrationOverlay?.Q<Label>("lbl-instruction");
            if(lblInstruction != null) lblInstruction.text = "";
        }

        public void ShowPressAInstruction(string text = "Appuyer sur la gâchette pour valider")
        {
            var lblInstruction = _pnlCalibrationOverlay?.Q<Label>("lbl-instruction");
            if(lblInstruction != null) lblInstruction.text = text;
        }

        public void ResetUI()
        {
            foreach (var mark in _markElements)
            {
                mark.RemoveFromClassList("valid");
                mark.RemoveFromClassList("error");
                
                var icon = mark.Q<VisualElement>(className: "calibration-point-icon");
                if (icon != null) icon.style.opacity = 0; 
                
                var fill = mark.Q<VisualElement>(className: "calibration-point-fill");
                if (fill != null) fill.MarkDirtyRepaint();
                
                mark.visible = false;
            }
            
            var lblInstruction = _pnlCalibrationOverlay?.Q<Label>("lbl-instruction");
            if (lblInstruction != null) lblInstruction.text = "";
            
            _targetProgress = 0f;
            _displayedProgress = 0f;
            _currentProgress = 0f;
            _currentIndexPoint = -1;
        }

        private void SetCursorsInteractivity(bool isInteractive)
    {
        foreach(var cursor in _cursors)
        {
            if(cursor != null && cursor.CursorInteractor != null)
            {
                cursor.CursorInteractor.enabled = isInteractive;
            }
        }
    }
    #endregion

    #region UserInteractionSetup
    public void AddCursor(CalibrationCursorUI cursor)
    {
        if (!_cursors.Contains(cursor)) 
        {
            _cursors.Add(cursor);

            bool shouldBeActive = (_pnlValidationFooter != null && _pnlValidationFooter.style.display == DisplayStyle.Flex) || 
                                  (_pnlPopupLaunch != null && _pnlPopupLaunch.style.display == DisplayStyle.Flex);
            
            if(cursor.CursorInteractor != null)
            {
                cursor.CursorInteractor.enabled = shouldBeActive;
            }
        }
    }
    public void RemoveCursor(CalibrationCursorUI cursor)
    {
        if (_cursors.Contains(cursor)) _cursors.Remove(cursor);
    }
    public void InitCanvasCursor(Vector3 position, Vector2 sizeDelta, Vector3 euler)
    {
        if (_docWorld != null)
        {
            _docWorld.transform.position = position;
            _docWorld.transform.eulerAngles = euler;
            
            float uiWidthInMeters = _uiWidthPixels / _uiPanelPPU;
            
            if (uiWidthInMeters <= 0) uiWidthInMeters = 19.2f;
            
            float dynamicScale = sizeDelta.x / uiWidthInMeters;
            
            _docWorld.transform.localScale = new Vector3(dynamicScale, dynamicScale, 1f);
            _canvasCursors.position = position;
            _canvasCursors.eulerAngles = euler;
        
            //Debug.Log($"[UICalibrationToolkit] UI Scaled to fit {sizeDelta.x}m width. Result Scale: {dynamicScale}");
        }
    }
    #endregion

    #region UIAnims&Updates
        private void AnimateSpinners()
    {
        if (_rootOverlay == null) return;
        _rootOverlay.schedule.Execute(() => 
        {
            var loaders = _rootOverlay.Query<VisualElement>(className: "icon-status-loading").Build();
            foreach(var icon in loaders)
            {
                icon.transform.rotation = icon.transform.rotation * Quaternion.Euler(0, 0, -10);
            }
        }).Every(30); 
    }
        private void UpdateCombinedDeviceStatusUI(string key, string label, bool isConnected)
    {
        VisualElement row = _devicesList.Q<VisualElement>($"device-row-{key}");
        if (row == null) return;

        Label lbl = row.Q<Label>();
        VisualElement icon = row.Q<VisualElement>("status-icon");

        if (isConnected)
        {
            lbl.text = $"OK: {label}";
            icon.RemoveFromClassList("icon-status-loading");
            icon.AddToClassList("icon-status-ok");

            icon.transform.rotation = Quaternion.identity; 
        }
        else
        {
            lbl.text = $"Checking {label}...";
            icon.RemoveFromClassList("icon-status-ok");
            icon.AddToClassList("icon-status-loading");
        }
    }
        
    private void BuildDevicesList(List<DeviceCheckerConfig> configs)
    {
        _devicesList.Clear();
        _resolvedDeviceRows.Clear();
        _systemCheckRows.Clear();
        _devicesList.scrollOffset = Vector2.zero;
        HideReadyPrompt();

        foreach (var config in configs)
        {
            AddDeviceRow(config.Key, config.Label);
            if (config.Type == EDeviceCheckerType.SystemCheck) _systemCheckRows.Add(config.Key);
        }

        LimitDeviceListHeight();
        ShowPanel(_pnlCheckingDevices);
    }

    /// <summary>
    /// Borne la liste à six lignes. La hauteur est MESURÉE sur la première ligne plutôt qu'écrite en
    /// dur : elle dépend de la feuille de style et de la résolution de référence du panneau.
    /// </summary>
    private void LimitDeviceListHeight()
    {
        if (_devicesList == null || _devicesList.childCount == 0) return;

        VisualElement first = _devicesList[0];
        first.RegisterCallback<GeometryChangedEvent>(OnFirstDeviceRowMeasured);
    }

    private void OnFirstDeviceRowMeasured(GeometryChangedEvent evt)
    {
        VisualElement row = evt.target as VisualElement;
        if (row == null) return;
        row.UnregisterCallback<GeometryChangedEvent>(OnFirstDeviceRowMeasured);

        float height = row.resolvedStyle.height
                     + row.resolvedStyle.marginTop + row.resolvedStyle.marginBottom;
        if (height <= 1f) return;

        _deviceRowHeight = height;
        _devicesList.style.maxHeight = height * VisibleDeviceRows;
    }

    /// <summary>
    /// Fait suivre la fenêtre visible : tant qu'on tient dans les six lignes on ne bouge pas, puis
    /// on avance d'un cran à chaque nouveau verdict. Une ligne déjà rendue ne recompte pas — la
    /// boucle de vérification repasse sur chaque contrôle toutes les demi-secondes.
    /// </summary>
    private void NotifyDeviceRowResolved(string key)
    {
        if (_devicesList == null || string.IsNullOrEmpty(key)) return;

        // Une ligne déjà rendue ne recompte pas : la boucle repasse sur chaque contrôle toutes les
        // demi-secondes, et la liste défilerait sans fin.
        if (!_resolvedDeviceRows.Add(key)) return;
        _lastRowResolvedAt = Time.unscaledTime;

        VisualElement row = _devicesList.Q<VisualElement>($"device-row-{key}");
        if (row == null) return;

        PopStatusIcon(row);

        // ⚠ La ligne rejoint le bloc des lignes RÉSOLUES, en tête. Les contrôles encore en cours
        // sont donc repoussés vers le bas, et restent dans la fenêtre visible : sans cela, une
        // vérification lente sortait du champ par le haut AVANT d'avoir répondu, et l'on ne voyait
        // jamais son verdict.
        MoveRowToResolvedBlock(row);

        float height = _deviceRowHeight > 1f ? _deviceRowHeight : row.resolvedStyle.height;
        if (height <= 1f) return;

        // ⚠ La fenêtre se cale sur la PREMIÈRE ligne encore en cours, pas sur la dernière validée.
        // Ce qui intéresse l'opérateur, c'est ce qui reste à vérifier : une liste qui suit les
        // coches vertes lui montre exactement ce dont il n'a rien à faire.
        // Bornée pour ne jamais défiler au-delà du dernier écran.
        int total = _devicesList.contentContainer.childCount;
        int firstPending = Mathf.Min(_resolvedDeviceRows.Count, Mathf.Max(0, total - VisibleDeviceRows));
        _deviceScrollTarget = firstPending * height;

        _devicesList.schedule.Execute(StartDeviceScroll).StartingIn(DeviceScrollDelayMs);
    }

    /// <summary>
    /// Range la ligne à la fin du bloc résolu. L'invariant tenu par cette méthode est ce qui garde
    /// les contrôles en cours à l'écran : résolus en haut dans l'ordre des verdicts, en cours en
    /// dessous. La fenêtre visible, calée sur le nombre de résolus, montre donc toujours la fin du
    /// bloc résolu ET le début de ce qui reste à faire.
    /// </summary>
    private void MoveRowToResolvedBlock(VisualElement row)
    {
        VisualElement content = _devicesList.contentContainer;
        int target = Mathf.Clamp(_resolvedDeviceRows.Count - 1, 0, content.childCount - 1);
        if (content.IndexOf(row) == target) return;

        // Positions AVANT déplacement : elles serviront à jouer le glissement à rebours.
        var before = new Dictionary<VisualElement, float>();
        for (int i = 0; i < content.childCount; i++) before[content[i]] = content[i].layout.y;

        row.RemoveFromHierarchy();
        content.Insert(target, row);

        // La nouvelle disposition n'est connue qu'au layout suivant.
        content.schedule.Execute(() => SlideRowsFromPreviousPositions(before)).StartingIn(0);
    }

    /// <summary>
    /// Glissement « à rebours » : on replace chaque ligne là où elle était, puis on la laisse
    /// rejoindre sa nouvelle place. Un déplacement dans la hiérarchie est instantané — sans ce
    /// procédé, les lignes SAUTENT et l'on ne comprend pas ce qui vient de bouger.
    /// </summary>
    private void SlideRowsFromPreviousPositions(Dictionary<VisualElement, float> before)
    {
        foreach (KeyValuePair<VisualElement, float> pair in before)
        {
            VisualElement row = pair.Key;
            if (row == null || row.parent == null) continue;

            float delta = pair.Value - row.layout.y;
            if (Mathf.Abs(delta) < 0.5f) continue;

            // Valeur de départ posée SANS transition, bascule à la frame suivante (CLAUDE.md §8).
            row.style.transitionProperty = new StyleList<StylePropertyName>(new List<StylePropertyName>());
            row.style.translate = new StyleTranslate(new Translate(0, delta));

            row.schedule.Execute(() =>
            {
                row.style.transitionProperty =
                    new StyleList<StylePropertyName>(new List<StylePropertyName> { "translate" });
                row.style.transitionDuration =
                    new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(DeviceReorderMs, TimeUnit.Millisecond) });
                row.style.translate = new StyleTranslate(new Translate(0, 0));
            }).StartingIn(20);
        }
    }

    /// <summary>
    /// Bref agrandissement de la pastille au moment du verdict : un simple changement de classe
    /// passe inaperçu sur une ligne parmi vingt.
    /// </summary>
    private void PopStatusIcon(VisualElement row)
    {
        VisualElement icon = row.Q<VisualElement>("status-icon");
        if (icon == null) return;

        icon.style.transitionProperty =
            new StyleList<StylePropertyName>(new List<StylePropertyName> { "scale" });
        icon.style.transitionDuration =
            new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(180, TimeUnit.Millisecond) });

        icon.style.scale = new StyleScale(new Scale(Vector2.one * 1.6f));
        icon.schedule.Execute(() => icon.style.scale = new StyleScale(new Scale(Vector2.one)))
            .StartingIn(180);
    }

    /// <summary>
    /// Rend la liste défilable à la souris et au spray, comme « État du système ».
    /// ⚠ La poignée native (`ClampedDragger`, un `Clickable`) ne répond PAS aux événements que
    /// fabriquent `MouseUIBridge` et le curseur spray : ils portent `button = -1`, et `Clickable`
    /// n'accepte que le bouton gauche. `TouchScrollManipulator`, lui, n'applique pas ce filtre, et
    /// il est enregistré en TrickleDown sur la ScrollView : il reçoit l'appui AVANT la poignée, qui
    /// est son descendant. C'est lui qui fait défiler, que l'on tienne la poignée ou le contenu.
    /// </summary>
    private void WireDevicesListScrolling()
    {
        if (_devicesList == null || _devicesList == _wiredDevicesList) return;
        _wiredDevicesList = _devicesList;

        _devicesList.AddManipulator(new TouchScrollManipulator(_devicesList));
        // TrickleDown : l'appui sur la poignée (descendante de la liste) passe d'abord par ici.
        _devicesList.RegisterCallback<PointerDownEvent>(_ => TakeManualScroll(), TrickleDown.TrickleDown);
        _devicesList.RegisterCallback<WheelEvent>(_ => TakeManualScroll(), TrickleDown.TrickleDown);
    }

    /// <summary>L'opérateur prend la liste en main : l'animation en cours s'arrête sur-le-champ.</summary>
    private void TakeManualScroll()
    {
        _deviceScrollManual = true;
        _deviceScrollAnim?.Pause();
    }

    /// <summary>
    /// Défilement ANIMÉ. `scrollOffset` n'est pas une propriété de style : aucune transition USS ne
    /// s'y applique, il faut donc l'interpoler à la main.
    /// </summary>
    private void StartDeviceScroll()
    {
        // L'opérateur a la main : on ne lui reprend pas la liste (cf. _deviceScrollManual).
        if (_devicesList == null || _deviceScrollManual) return;

        _deviceScrollAnim?.Pause();

        float from = _devicesList.scrollOffset.y;
        float to = _deviceScrollTarget;
        if (Mathf.Abs(to - from) < 0.5f) return;

        float started = Time.unscaledTime;
        _deviceScrollAnim = _devicesList.schedule.Execute(() =>
        {
            if (_devicesList == null) return;

            float t = Mathf.Clamp01((Time.unscaledTime - started) / DeviceScrollSeconds);
            // Lissage aux deux bouts : un défilement linéaire se lit comme un saut.
            float eased = t * t * (3f - 2f * t);
            _devicesList.scrollOffset = new Vector2(_devicesList.scrollOffset.x,
                                                    Mathf.Lerp(from, to, eased));

            if (t >= 1f) _deviceScrollAnim?.Pause();
        }).Every(16);
    }

    private void UpdateDeviceStatusUI(string key, string label, bool isConnected, string exePath)
    {
        VisualElement row = _devicesList.Q<VisualElement>($"device-row-{key}");
        if (row == null) return;
        
        Label lbl = row.Q<Label>();
        VisualElement icon = row.Q<VisualElement>("status-icon");

        icon.RemoveFromClassList("icon-status-warning");
        // L'image et la teinte du triangle sont posées en INLINE : retirer la classe ne suffit pas.
        icon.style.backgroundImage = StyleKeyword.Null;
        icon.style.unityBackgroundImageTintColor = StyleKeyword.Null;

        if (isConnected)
        {
            lbl.text = $"OK: {label}";
            icon.RemoveFromClassList("icon-status-loading");
            icon.AddToClassList("icon-status-ok");
            icon.transform.rotation = Quaternion.identity;
            NotifyDeviceRowResolved(key);
        }
        else
        {
            lbl.text = string.IsNullOrEmpty(exePath) ? $"Checking {label}..." : $"Launching {label}...";
            icon.RemoveFromClassList("icon-status-ok");
            icon.AddToClassList("icon-status-loading");
        }
    }
    /// <summary>Contrôle optionnel en échec : ni coche verte ni spinner sans fin, mais une raison.</summary>
    private void SetDeviceWarning(string key, string label, string reason, bool isError)
    {
        VisualElement row = _devicesList.Q<VisualElement>($"device-row-{key}");
        if (row == null) return;

        Label lbl = row.Q<Label>();
        VisualElement icon = row.Q<VisualElement>("status-icon");

        if (lbl != null) lbl.text = $"{label} : {reason}";
        if (icon == null) return;

        icon.RemoveFromClassList("icon-status-ok");
        icon.RemoveFromClassList("icon-status-loading");
        icon.AddToClassList("icon-status-warning");
        NotifyDeviceRowResolved(key);
        // Triangle dessiné au code : le projet n'a pas d'icône correspondante, et la coche verte
        // des autres lignes dirait exactement le contraire de ce qui se passe.
        // La texture sort en BLANC : la teinte est posée ici, en inline comme l'image — laissée à la
        // feuille de style, elle se faisait écraser et le triangle restait blanc.
        icon.style.backgroundImage = new StyleBackground(UIIcons.WarningTriangle);
        icon.style.unityBackgroundImageTintColor = isError
            ? new Color(0.91f, 0.30f, 0.24f)
            : new Color(0.95f, 0.61f, 0.07f);
        icon.transform.rotation = Quaternion.identity;
    }

    /// <summary>
    /// Un checker asynchrone tourne toujours au-delà du seuil (cf. CalibrationManager.
    /// StalledSeconds) : la ligne le dit, distinct de « Checking… » ET d'un avertissement optionnel
    /// résolu — CE contrôle reste BLOQUANT, on l'attend encore, on ne l'a pas classé en échec.
    /// ⚠ Ne PAS appeler NotifyDeviceRowResolved ici : rien n'est résolu, le sondage continue.
    /// </summary>
    private void SetDeviceStalled(string key, string label, float elapsedSeconds)
    {
        VisualElement row = _devicesList.Q<VisualElement>($"device-row-{key}");
        if (row == null) return;

        Label lbl = row.Q<Label>();
        VisualElement icon = row.Q<VisualElement>("status-icon");

        if (lbl != null) lbl.text = $"{label} : pas de réponse depuis {elapsedSeconds:0}s";
        if (icon == null) return;

        icon.RemoveFromClassList("icon-status-ok");
        icon.RemoveFromClassList("icon-status-loading");
        icon.AddToClassList("icon-status-warning");
        // Même triangle que SetDeviceWarning, en orange : signal visuel distinct du spinner, sans
        // prétendre à un diagnostic (« indisponible ») qu'on n'a pas encore — l'appel tourne peut-
        // être toujours et pourrait encore aboutir.
        icon.style.backgroundImage = new StyleBackground(UIIcons.WarningTriangle);
        icon.style.unityBackgroundImageTintColor = new Color(0.95f, 0.61f, 0.07f);
        icon.transform.rotation = Quaternion.identity;
    }

    /// <summary>
    /// Fin de campagne : la liste ne garde que ce qui ne va pas, et le détail part dans la popup.
    ///
    /// ⚠ Une liste de vingt lignes vertes n'apprend rien à personne — elle noie les deux qui
    /// comptent. Tout est de toute façon consultable dans « État du système ».
    /// </summary>
    private void HandleSystemChecksCompleted()
    {
        if (_devicesList == null) return;

        List<CheckReport> problems = CurrentProblems();

        HideResolvedRows(problems);
        AttachResolveButtons(problems);

        // ⚠ Les problèmes mis en sourdine restent dans la LISTE (on voit qu'ils sont là) mais ne
        // rouvrent plus le bilan : c'était tout l'objet de les ignorer.
        List<CheckReport> active = problems.FindAll(r => !CheckSnooze.IsSnoozed(r.Key));

        if (active.Count == 0)
        {
            // Tout est rentré dans l'ordre (ou tout est ignoré) : on referme un bilan sans objet.
            // Close et non Dismiss : ce n'est pas l'opérateur qui renonce, la question « ignorer
            // pour combien de temps ? » n'a pas lieu d'être.
            if (GuidePopup.IsOpen) GuidePopup.Close();
            return;
        }

        problems = active;
        string message = DescribeProblems(problems);
        bool offersRepair = RepairRunner.Repairable(problems).Count > 0;

        // Popup déjà ouverte : on remplace juste son texte, sans repartir de la première page — sauf
        // si son bouton d'action ne correspond plus (plus rien à réparer, ou l'inverse).
        if (GuidePopup.IsOpen && offersRepair == _summaryOffersRepair) { GuidePopup.SetDynamicMessage(message); return; }
        if (_rootOverlay == null) return;
        _summaryOffersRepair = offersRepair;

        // Si une anomalie porte une procédure illustrée, on l'emmène avec elle.
        GuidePopupData illustrated = null;
        foreach (CheckReport report in problems)
        {
            string id = report.Result != null ? report.Result.GuideId : null;
            if (string.IsNullOrEmpty(id)) continue;
            illustrated = GuidePopupData.Find(id);
            break;
        }

        // ⚠ Contenu bâti au RUNTIME, et pas seulement repris d'un asset : sans lui la popup
        // s'intitulait « Information » et n'affichait AUCUN bouton d'action — GuidePopup ne
        // construit le bouton vert que si l'asset en déclare le libellé.
        GuidePopupData data = ScriptableObject.CreateInstance<GuidePopupData>();
        data.Id = "system-check-summary";
        data.Title = "Vérifications au démarrage";
        data.DismissLabel = "Ignorer…";
        data.DismissTooltip = "Ne plus signaler ces points pendant un temps";
        // Quelque chose à réparer : le bouton vert le TENTE, au lieu de demander à l'opérateur de
        // corriger à la main puis de le dire.
        data.ConfirmLabel = offersRepair ? "Tout résoudre" : "J'ai corrigé";
        data.ConfirmTooltip = offersRepair ? "Tenter de corriger ces points, un par un" : "Relancer les vérifications";
        if (illustrated != null) data.Pages = illustrated.Pages;

        GuidePopup.Show(_rootOverlay, data, message, null,
                        offersRepair ? (Func<string>)ResolveAllFromSummary : RelaunchSystemChecks,
                        AskSnoozeDuration);
    }

    /// <summary>
    /// « Ignorer… » du bilan : pour combien de temps ? Porte sur les anomalies AFFICHÉES, relues au
    /// clic. Renoncer vaut l'ancien « Plus tard » : le bilan est fermé, rien n'est retenu.
    /// </summary>
    private void AskSnoozeDuration()
    {
        List<string> keys = CurrentProblems()
            .FindAll(r => !CheckSnooze.IsSnoozed(r.Key))
            .ConvertAll(r => r.Key);
        if (keys.Count == 0 || _rootOverlay == null) return;

        var options = new List<string>();
        foreach (KeyValuePair<string, TimeSpan?> choice in CheckSnooze.Choices) options.Add(choice.Key);

        string message = keys.Count == 1
            ? "Ce point ne sera plus signalé pendant la durée choisie. Il reste visible dans « État du système »."
            : $"Ces {keys.Count} points ne seront plus signalés pendant la durée choisie. Ils restent visibles dans « État du système ».";

        ChoicePopup.Show(_rootOverlay, "Ignorer pour combien de temps ?", message, options,
                         "Seulement maintenant", picked =>
                         {
                             if (picked != RepairContext.Cancelled) CheckSnooze.Snooze(keys, picked);
                         });
    }

    /// <summary>
    /// Anomalies de la dernière campagne. ⚠ Après une passe courte, les rapports contiennent aussi
    /// ceux de la campagne précédente : le bilan ne parle que de ce qui vient d'être vérifié, sinon il
    /// ramène tout ce que la passe courte existe pour ne pas rejouer.
    /// </summary>
    private static List<CheckReport> CurrentProblems()
    {
        var problems = new List<CheckReport>();
        foreach (CheckReport report in SystemStatus.Reports)
            if (report.Severity != CheckSeverity.Ok && SystemCheckRunner.WasPlayed(report.Name))
                problems.Add(report);
        return problems;
    }

    /// <summary>
    /// « Tout résoudre » du bilan. Relu AU CLIC, pas capturé à l'affichage : une ligne a pu être
    /// réparée entre-temps par son propre bouton. Le bilan s'efface pour laisser la place aux
    /// décisions ; il revient de lui-même à la fin de la revérification s'il reste quelque chose.
    /// </summary>
    private string ResolveAllFromSummary()
    {
        List<CheckReport> active = CurrentProblems().FindAll(r => !CheckSnooze.IsSnoozed(r.Key));
        if (RepairRunner.Resolve(active, _rootOverlay)) return null;
        return RelaunchSystemChecks();
    }

    /// <summary>
    /// Petit bouton « Résoudre » sur chaque ligne en anomalie qui a une réparation. Posé AVANT la
    /// pastille : c'est elle qui ferme la ligne à droite, comme sur toutes les autres.
    /// </summary>
    private void AttachResolveButtons(List<CheckReport> problems)
    {
        foreach (CheckReport report in problems)
        {
            VisualElement row = _devicesList.Q<VisualElement>($"device-row-{report.Name}");
            if (row == null) continue;

            row.Q<Button>(className: ResolveButtonClass)?.RemoveFromHierarchy();
            if (report.Result == null || !report.Result.CanRepair) continue;

            Button resolve = new Button
            {
                text = "Résoudre",
                tooltip = string.Join(", ", report.Result.Repairs.ConvertAll(r => r.Label))
            };
            resolve.AddToClassList(ResolveButtonClass);
            resolve.style.flexShrink = 0;
            resolve.style.height = 30;
            resolve.style.marginLeft = 10;
            resolve.style.marginRight = 10;
            resolve.style.paddingLeft = 14;
            resolve.style.paddingRight = 14;
            resolve.style.fontSize = 14;
            resolve.style.color = Color.white;
            resolve.style.backgroundColor = new Color(0.18f, 0.80f, 0.44f, 0.35f);
            resolve.style.borderTopWidth = 0;
            resolve.style.borderBottomWidth = 0;
            resolve.style.borderLeftWidth = 0;
            resolve.style.borderRightWidth = 0;
            resolve.style.borderTopLeftRadius = 8;
            resolve.style.borderTopRightRadius = 8;
            resolve.style.borderBottomLeftRadius = 8;
            resolve.style.borderBottomRightRadius = 8;
            resolve.SetEnabled(!RepairRunner.Busy);

            CheckReport target = report;
            resolve.RegisterCallback<ClickEvent>(e => RepairRunner.Resolve(new[] { target }, _rootOverlay));

            VisualElement icon = row.Q<VisualElement>("status-icon");
            row.Insert(icon != null ? row.IndexOf(icon) : row.childCount, resolve);
        }
    }

    /// <summary>Une réparation en cours grise les autres boutons : un clic refusé en silence passe pour un bouton mort.</summary>
    private void OnRepairStateChanged()
    {
        _devicesList?.Query<Button>(className: ResolveButtonClass).ForEach(b => b.SetEnabled(!RepairRunner.Busy));
    }

    /// <summary>Retire les lignes au vert. Les anomalies gardent leur place et leur ordre.</summary>
    private void HideResolvedRows(List<CheckReport> problems)
    {
        var keep = new HashSet<string>();
        foreach (CheckReport report in problems) keep.Add(report.Name);

        // Lignes issues d'une vérification système : ce sont les SEULES qu'on peut retirer.
        // ⚠ Relevées sur le TYPE de ligne, pas sur le nom : la vérification « SteamVR » porte le même
        // nom que la ligne native de la scène, qui, elle, doit rester — c'est sur elle que la
        // calibration attend pour démarrer.
        var owned = new HashSet<string>(_systemCheckRows);

        VisualElement content = _devicesList.contentContainer;
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            VisualElement row = content[i];
            string key = row.name != null && row.name.StartsWith("device-row-")
                ? row.name.Substring("device-row-".Length)
                : null;

            // On ne touche QU'aux lignes de vérification système : les périphériques gardent les
            // leurs, c'est sur elles que la calibration s'appuie pour démarrer.
            if (key == null || !owned.Contains(key)) continue;
            if (!keep.Contains(key)) row.RemoveFromHierarchy();
        }

        // Retour en haut automatique, sauf si l'opérateur fait défiler lui-même.
        if (!_deviceScrollManual) _devicesList.scrollOffset = Vector2.zero;
    }

    private static string DescribeProblems(List<CheckReport> problems)
    {
        var lines = new List<string>();
        foreach (CheckReport report in problems)
        {
            CheckResult r = report.Result;
            string detail = r != null ? r.Message : "";
            lines.Add($"• {report.Name} — {detail}");
            if (r != null && !string.IsNullOrEmpty(r.Fix)) lines.Add($"   {r.Fix}");
        }

        string head = problems.Count == 1
            ? "Un point demande votre attention :"
            : $"{problems.Count} points demandent votre attention :";

        return head + "\n\n" + string.Join("\n", lines);
    }

    /// <summary>
    /// Relance la campagne. Elle est ASYNCHRONE : on ne peut pas rendre le verdict ici. La popup
    /// reste donc ouverte, et c'est la fin de campagne qui la met à jour ou la referme.
    /// </summary>
    private static string RelaunchSystemChecks()
    {
        SystemCheckRunner.RunAgain();
        return "Nouvelle vérification en cours…";
    }

    private void HandleAllDevicesReady()
    {
        if (_readyRoutine != null) StopCoroutine(_readyRoutine);
        _readyRoutine = StartCoroutine(ShowReadyWhenListSettled());
    }

    /// <summary>
    /// ⚠ « Prêt » pour la logique n'est pas « prêt » à l'écran. La campagne rend ses verdicts plus
    /// vite que la liste ne les montre (délai, défilement, réorganisation) : l'invitation à appuyer
    /// sur la gâchette, et la gâchette elle-même, n'arrivent qu'une fois CHAQUE ligne affichée dans
    /// son état final et la liste immobile.
    /// </summary>
    private IEnumerator ShowReadyWhenListSettled()
    {
        float deadline = Time.unscaledTime + ReadyTimeoutSeconds;
        while (!DeviceListSettled() && Time.unscaledTime < deadline) yield return null;

        _readyToStart = true;
        bool mouseTest = CalibrationManager.instance != null && CalibrationManager.instance.CanStartMouseTest;
        if (_btnStart != null)
        {
            _btnStart.text = mouseTest ? "Ouvrir le mur à la souris" : "Commencer";
            _btnStart.tooltip = mouseTest ? MouseTestControlsHint : "Commencer";
            _btnStart.style.display = DisplayStyle.Flex;
        }

        var prompt = _rootOverlay != null ? _rootOverlay.Q<Label>("lbl-press-a") : null;
        if (prompt != null)
        {
            prompt.text = mouseTest ? "Mode test sans matériel · clic ou Entrée" : "Clic, Entrée ou gâchette pour commencer";
            prompt.visible = true;
            prompt.style.opacity = 1f;
            StartCoroutine(BlinkText(prompt));
        }

        yield return WaitForControllerClick(() =>
        {
            _readyToStart = false;
            if (_btnStart != null) _btnStart.style.display = DisplayStyle.None;
            CalibrationManager.instance.ProceedAfterChecks();
        });
        _readyRoutine = null;
    }

    private bool DeviceListSettled()
    {
        if (_devicesList == null) return true;
        if (SystemStatus.Running || RepairRunner.Busy) return false;
        if (_resolvedDeviceRows.Count < _devicesList.contentContainer.childCount) return false;
        return Time.unscaledTime - _lastRowResolvedAt >= ListSettleSeconds;
    }

    /// <summary>
    /// Masque l'invitation. ⚠ Le libellé porte dans l'UXML un texte d'une autre époque (« Appuyez sur
    /// 'A' ») et il était visible dès l'ouverture de l'écran : on le vide ET on le cache tant que la
    /// liste n'est pas prête.
    /// </summary>
    private void HideReadyPrompt()
    {
        _readyToStart = false;
        if (_btnStart != null) _btnStart.style.display = DisplayStyle.None;
        if (_readyRoutine != null) { StopCoroutine(_readyRoutine); _readyRoutine = null; }
        _waitingForClick = false;

        var prompt = _rootOverlay != null ? _rootOverlay.Q<Label>("lbl-press-a") : null;
        if (prompt == null) return;
        prompt.text = ReadyPrompt;
        prompt.visible = false;
    }

    private void OnStartClicked(ClickEvent evt) => ActivateStart();

    private void ActivateStart()
    {
        if (!_readyToStart || ModalPopups.IsOpen || CalibrationManager.instance == null) return;
        if (CalibrationManager.instance.CanStartMouseTest)
        {
            _readyToStart = false;
            CalibrationManager.instance.StartMouseTest();
        }
        else NotifyOnNextStep();
    }

    public void OpenPopupLaunching()
    {
        ShowPanel(_pnlPopupLaunch);
    }
    public void ClosePopupLaunching()
    {
        SetPanelVisibility(_pnlPopupLaunch, false);
    }

    // La sélection de mode de jeu n'est PLUS affichée dans la scène de calibration : "Valider" /
    // "Lancer" charge directement GraffWall, où la sélection s'ouvre si aucun mode n'a été choisi
    // (cf. CalibrationManager.Launch + GlobalWorldUI.ApplyPendingModeWhenReady).

    private IEnumerator BlinkText(VisualElement element)
    {
        while(element.visible)
        {
             // Fade Out
            yield return TweenOpacity(element, 1f, 0.2f, 0.5f);
            // Fade In
            yield return TweenOpacity(element, 0.2f, 1f, 0.5f);
        }
    }
    private IEnumerator TweenOpacity(VisualElement element, float start, float end, float duration)
    {
        float elapsed = 0f;
        while(elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            // Ease In Out Quad
            float easedT = t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
            
            element.style.opacity = Mathf.Lerp(start, end, easedT);
            yield return null;
        }
        element.style.opacity = end;
    }
    private void ShowPanel(VisualElement panelToShow)
    {
        // Cache tous les panneaux via classe CSS (transition fluide)
        SetPanelVisibility(_pnlCheckingDevices, false);
        SetPanelVisibility(_pnlCalibrationOverlay, false);
        SetPanelVisibility(_pnlValidationFooter, false);
        SetPanelVisibility(_pnlPopupLaunch, false);
        
        // Affiche le panneau demandé
        if (panelToShow != null)
        {
            SetPanelVisibility(panelToShow, true);
        }

        // Réouverture de la liste : le défilement automatique reprend la main.
        if (panelToShow == _pnlCheckingDevices) _deviceScrollManual = false;
        
        bool isOverlayPanel = (panelToShow == _pnlCheckingDevices || panelToShow == _pnlCalibrationOverlay);
        bool isWorldSpacePanel = (panelToShow == _pnlValidationFooter || panelToShow == _pnlPopupLaunch);
        
        if (isOverlayPanel)
        {
            if(_docOverlay != null) _docOverlay.rootVisualElement.style.display = DisplayStyle.Flex;
            if(_docWorld != null) _docWorld.rootVisualElement.style.display = DisplayStyle.None; // On cache le World pour voir l'Overlay net
        }

        else
        {
            if(_docOverlay != null) _docOverlay.rootVisualElement.style.display = DisplayStyle.None; // On cache l'Overlay
            if(_docWorld != null) _docWorld.rootVisualElement.style.display = DisplayStyle.Flex;
        }
        
        SetCursorsInteractivity(isWorldSpacePanel);
    }

    /// <summary>Durée du fondu de `.animated-panel` (VisionOS.uss) : le panneau n'est masqué qu'après.</summary>
    private const long PanelFadeMs = 300;

    /// <summary>
    /// ⚠ L'OPACITÉ ne retire rien du pointage : `Panel.PerformPick` n'écarte un élément qu'en
    /// `display: none`, en `pickingMode = Ignore` ou invisible (`visibility`, héritée). Et
    /// `pickingMode = Ignore` sur le panneau ne protège PAS ses enfants. Un panneau plein écran
    /// simplement rendu transparent (`pnl-calibration-overlay`, déclaré APRÈS la liste, donc
    /// au-dessus) retenait donc tous les pointages : la souris n'atteignait jamais la liste de
    /// vérification — ni sa barre de défilement, ni ses boutons « Résoudre ».
    /// D'où `visibility: hidden`, posée une fois le fondu terminé pour ne pas le couper.
    /// </summary>
    private void SetPanelVisibility(VisualElement panel, bool visible)
    {
        if (panel == null) return;

        if (visible)
        {
            // Visible AVANT de retirer la classe : le fondu d'entrée doit rester visible.
            panel.style.visibility = StyleKeyword.Null;
            panel.RemoveFromClassList("animated-panel--hidden");
            panel.pickingMode = PickingMode.Position;
        }
        else
        {
            panel.AddToClassList("animated-panel--hidden");
            panel.pickingMode = PickingMode.Ignore;
            panel.schedule.Execute(() =>
            {
                // Rouvert pendant le fondu : on ne le masque pas.
                if (panel.ClassListContains("animated-panel--hidden"))
                    panel.style.visibility = Visibility.Hidden;
            }).StartingIn(PanelFadeMs);
        }
    }
    #endregion
}
