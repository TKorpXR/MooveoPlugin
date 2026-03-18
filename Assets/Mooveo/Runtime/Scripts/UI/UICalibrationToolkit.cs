using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class UICalibrationToolkit : MonoBehaviour
{
    public static UICalibrationToolkit instance;

    [Header("Hybrid UI Setup")]
    [SerializeField] private UIDocument _docOverlay;
    [SerializeField] private UIDocument _docWorld;

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
    
    private VisualElement _rootOverlay;
    private VisualElement _rootWorld;
    
    private VisualElement _pnlCheckingDevices;
    private ScrollView _devicesList;
    private VisualElement _pnlCalibrationOverlay;
    private VisualElement _calibrationMarksContainer;
    private VisualElement _pnlValidationFooter;
    private Button _btnRefine;
    private Button _btnStartOver;
    private Button _btnValidate;
    private VisualElement _pnlPopupLaunch;
    private Button _btnPopupRecalibrate;
    private Button _btnPopupLaunch;
    
    private CalibrationController _tester;
    private Coroutine _mainFlowRoutine;
    private Coroutine _secondaryFlowRoutine;
    private Dictionary<string, IDeviceChecker> _devices = new Dictionary<string, IDeviceChecker>();
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

    private void Awake()
    {
        if (instance == null) instance = this;
        else Destroy(this);
    }

    private void OnEnable()
    {

        if (_docOverlay == null && GetComponent<UIDocument>() != null) _docOverlay = GetComponent<UIDocument>();
        
        if (_docOverlay != null || _docWorld != null)
        {
            InitializeUI();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromCalibrationEvents();
    }
    private void Start()
    {
        _devices.Add("hmd", new HMDChecker());
        _devices.Add("leftController", new LeftControllerChecker());
        _devices.Add("rightController", new RightControllerChecker());
        _devices.Add("steam", new SteamVRChecker());
        _devices.Add("eos", new EosUtilitychecker());
        _devices.Add("hotfolder", new HotFolderChecker());
        
        AnimateSpinners();
    }

    private void Update()
    {
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
                _pnlCalibrationOverlay = _rootOverlay.Q<VisualElement>("pnl-calibration-overlay");
                _calibrationMarksContainer = _rootOverlay.Q<VisualElement>("calibration-marks-container");
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
            _mainFlowRoutine = StartCoroutine(MainFlow(3f));
        }
        private IEnumerator MainFlow(float seconds)
        {
            yield return StartCoroutine(CheckAllDevicesLoop(seconds, false));
            
            yield return StartCoroutine(CalibrationLoop());
            
            //Debug.Log("MainFlow -> Sequence finished. Waiting for user UI interaction (Validate/Refine).");
        }
        private IEnumerator SecondaryFlow(float seconds)
        {
            yield return StartCoroutine(CheckAllDevicesLoop(seconds, true));
        }
        public void StartCheckingDevices()
        {
            StopAllFlows();
            _secondaryFlowRoutine = StartCoroutine(SecondaryFlow(3f));
        }
        private IEnumerator CheckAllDevicesLoop(float seconds, bool triggerProceed)
        {
            ShowPanel(_pnlCheckingDevices);
    
            var lblPressA = _rootOverlay.Q<Label>("lbl-press-a");
            if(lblPressA != null) lblPressA.visible = false;
    
            _allDevicesValid = false;
            
            RebuildDevicesList();
    
            while (!_allDevicesValid)
            {
                float fakeDelay = 0.7f;
    
                UpdateDeviceStatusUI("steam", "SteamVR", GlobalSettings.Core.GlobalSettings.Instance.SteamVREXEPath.Value);
                yield return new WaitForSeconds(fakeDelay);
    
                UpdateDeviceStatusUI("hmd", "HMD", "");
                yield return new WaitForSeconds(fakeDelay);
                
                // Controller Logic Merged
                bool leftOK = _devices["leftController"].IsConnected();
                bool rightOK = _devices["rightController"].IsConnected();
                bool anyControllerOK = leftOK || rightOK;
                UpdateCombinedDeviceStatusUI("anyController", "Controllers", anyControllerOK);
                yield return new WaitForSeconds(fakeDelay);
                
                UpdateDeviceStatusUI("eos", "EOS Utility", GlobalSettings.Core.GlobalSettings.Instance.EosUtilityEXEPath.Value);
                yield return new WaitForSeconds(fakeDelay);
    
                UpdateDeviceStatusUI("hotfolder", "Hot Folder", GlobalSettings.Core.GlobalSettings.Instance.HotFolderEXEPath.Value); 
                yield return new WaitForSeconds(fakeDelay);
                
                 bool steamOK = _devices["steam"].IsConnected();
                 bool hmdOK = _devices["hmd"].IsConnected();
                 bool eosOK = _devices["eos"].IsConnected();
                 bool hotFolderOK = _devices["hotfolder"].IsConnected();
    
                 _allDevicesValid = (steamOK && hmdOK && anyControllerOK && eosOK && hotFolderOK);
    
                 if (_allDevicesValid)
                 {
                     UpdateDeviceStatusUI("steam", "SteamVR", "");
                     UpdateDeviceStatusUI("hmd", "HMD", "");
                     UpdateCombinedDeviceStatusUI("anyController", "Controllers", anyControllerOK);
                     UpdateDeviceStatusUI("eos", "EOS Utility", "");
                     UpdateDeviceStatusUI("hotfolder", "Hot Folder", "");
                 }
                 else 
                 {
                     yield return new WaitForSeconds(1f);
                 }
            }
    
            OnDevicesChecked?.Invoke(true);
            
            if(lblPressA != null) 
            {
                lblPressA.visible = true;
                lblPressA.style.opacity = 1f; // Ensure it starts visible
                StartCoroutine(BlinkText(lblPressA));
            }
    
            yield return StartCoroutine(WaitForControllerClick());
            
             if(triggerProceed) 
             {
                 CalibrationManager.instance.ProceedAfterChecks();
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
                if (!AllDevicesStillValid()) 
                {
                    //Debug.LogWarning("Disconnected -> Back to checking");
                    _allDevicesValid = false;
                    
                    UnsubscribeFromCalibrationEvents();
                    CalibrationManager.instance.OnCalibrationEnded -= OnCalibrationFinished;
                    
                    StartCoroutine(CheckAllDevicesLoop(0.5f, false));
                    yield break;
                }
        
                yield return new WaitForSeconds(0.1f);
            }
            
            //Debug.Log("CalibrationLoop -> 3 points acquired.");
            UnsubscribeFromCalibrationEvents();
            CalibrationManager.instance.OnCalibrationEnded -= OnCalibrationFinished;
            
            var lblInstruction = _pnlCalibrationOverlay.Q<Label>("lbl-instruction");
            if(lblInstruction != null) lblInstruction.text = "Calibration Done. Press 'A' to verify.";
            
            yield return StartCoroutine(WaitForControllerClick());
            
            if (_tester != null)
            {
                CalibrationManager.instance.TestCalibrationSetupPlayArea(_tester);
            }
            
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
            if (_waitingForClick && !_calibratingStep)
            {
                if (calibrationController != null) _tester = calibrationController;
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
            else
            {
                OnCalibrationFinished(); 
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
        private void OnPointError()
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
       bool leftConnected = false;
           bool rightConnected = false;
       
           foreach (var kvp in _devices)
           {
               if (kvp.Key == "leftController")
               {
                   leftConnected = kvp.Value.IsConnected();
                   continue; 
               }
               if (kvp.Key == "rightController")
               {
                   rightConnected = kvp.Value.IsConnected();
                   continue;
               }
               
               if (!kvp.Value.IsConnected())
               {
                   //Debug.LogWarning($"Device disconnected during calibration: {kvp.Key}");
                   return false;
               }
           }
           
           if (!leftConnected && !rightConnected)
           {
               //Debug.LogWarning("No controllers connected during calibration.");
               return false;
           }
       
           return true;
    }
        private void ClearCalibrationUIData()
        {
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
        private void UpdateDeviceStatusUI(string key, string label, string exePath)
    {
        if(!_devices.ContainsKey(key)) return;
        
        var checker = _devices[key];
        bool contentected = checker.IsConnected();

        if(!contentected && !string.IsNullOrEmpty(exePath))
        {
             if (!AppLauncher.IsProcessLaunched(exePath)) 
             {
                 AppLauncher.LaunchProcess(exePath);
             }
        }

        VisualElement row = _devicesList.Q<VisualElement>($"device-row-{key}");
        if(row == null) return;
        
        Label lbl = row.Q<Label>();
        VisualElement icon = row.Q<VisualElement>("status-icon");
        
        if (contentected)
        {
            lbl.text = $"OK: {checker.DeviceName}";
            icon.RemoveFromClassList("icon-status-loading");
            icon.AddToClassList("icon-status-ok");
            icon.transform.rotation = Quaternion.identity;
        }
        else
        {
            lbl.text = string.IsNullOrEmpty(exePath) ? $"Checking {label}..." : $"Launching {label}...";
            icon.RemoveFromClassList("icon-status-ok");
            icon.AddToClassList("icon-status-loading");
        }
    }
        public void OpenPopupLaunching()
    {
        ShowPanel(_pnlPopupLaunch);
    }
        public void ClosePopupLaunching()
    {
        SetPanelVisibility(_pnlPopupLaunch, false);
    }
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

    private void SetPanelVisibility(VisualElement panel, bool visible)
    {
        if (panel == null) return;

        if (visible)
        {
            panel.RemoveFromClassList("animated-panel--hidden");
            panel.pickingMode = PickingMode.Position;
        }
        else
        {
            panel.AddToClassList("animated-panel--hidden");
            panel.pickingMode = PickingMode.Ignore;
        }
    }
    #endregion
}
