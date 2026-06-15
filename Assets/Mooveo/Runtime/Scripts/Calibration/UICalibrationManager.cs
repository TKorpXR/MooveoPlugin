using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// Gère toute l'interface utilisateur de Mooveo
/// </summary>
public class UICalibrationManager : MonoBehaviour
{
    [Header("UI Validation Phase")]
    [SerializeField] private GameObject _loadingSpinner;
    [SerializeField] private TextMeshProUGUI _loadingText;

    [Header("UI Calibration")] 
    [SerializeField] List<CalibrationCursorUI> _cursors = new List<CalibrationCursorUI>();
    [SerializeField] private RectTransform _canvasCursors;
    [SerializeField] private RectTransform _textCalibration;
    [SerializeField] private RectTransform _zoneIcon;
    [SerializeField] private UIAnimator _animatorBtnAfterCalibration;

    [SerializeField] private float _zoneRadiusCm = 5f;
    [SerializeField] private float _unitToCanvas = 100f;
    [SerializeField] private GameObject _panelCalibrating;
    [SerializeField] List<Image> _marks = new List<Image>();
    [SerializeField] private List<Image> _marksIcons = new List<Image>();
    [SerializeField] private Sprite _iconError, _iconDone;
    [SerializeField] private Color _colorError, _colorDone;

    [Header(" UI Check Peripheriques")] 
    [SerializeField] private GameObject _panelCheckDevices;
    [SerializeField] private TextMeshProUGUI _hmdsText;
    [SerializeField] private TextMeshProUGUI _controllersText;
    [SerializeField] private TextMeshProUGUI _steamText;
    [SerializeField] private TextMeshProUGUI _eosText;
    [SerializeField] private TextMeshProUGUI _hotFolderText;
    [SerializeField] private GameObject _textValidate;

    [SerializeField]
    private Animator _hmdAnimator, _controllersAnimator, _steamAnimator, _eosAnimator, _hotFolderAnimator;
    public event Action<bool> OnDevicesChecked;
    
    public static UICalibrationManager instance;

    private int _currentIndexPoint = -1;
    private int _nPointToCalibrate = 3;
    private Coroutine _mainFlowRoutine, _secondaryFlowRoutine;
    
    private Dictionary<string, IDeviceChecker> _devices = new Dictionary<string, IDeviceChecker>();  
    private bool _allDevicesValid = false;
    private bool _calibratingStep = false;
    private bool _controllerClicked = false;
    private bool _waitingForClick = false;
    private bool _currentPointHasError = false;
    private bool _exitCalibration = false;
    private bool _checkDevicePanelEnabled = true;
    
    private void Awake()
    {
        if (instance == null) instance = this;
        else Destroy(this);
        StartCoroutine(InitMarksPositions());
    }

    private IEnumerator InitMarksPositions()
    {
        yield return null;
        
        RectTransform parentRect = _marks[0].transform.parent.GetComponent<RectTransform>();
        float width = parentRect.rect.width;
        float height = parentRect.rect.height;
        
        _marks[0].rectTransform.anchoredPosition = new Vector2(-width / 4, 0);
        _marks[1].rectTransform.anchoredPosition = new Vector2(0, 0);
        _marks[2].rectTransform.anchoredPosition = new Vector2(width / 4, 0);
        
        float hOffset = height / 3; 
        float wOffset = width / 3;

        _marks[3].rectTransform.anchoredPosition = new Vector2(-wOffset, hOffset);
        _marks[4].rectTransform.anchoredPosition = new Vector2(0, hOffset);
        _marks[5].rectTransform.anchoredPosition = new Vector2(wOffset, hOffset);
        
        _marks[6].rectTransform.anchoredPosition = new Vector2(-wOffset, -hOffset);
        _marks[7].rectTransform.anchoredPosition = new Vector2(0, -hOffset);
        _marks[8].rectTransform.anchoredPosition = new Vector2(wOffset, -hOffset);
    }

    private void Start()
    {
        _devices.Add("hmd", new HMDChecker());
        _devices.Add("leftController", new LeftControllerChecker());
        _devices.Add("rightController", new RightControllerChecker());
        _devices.Add("steam", new SteamVRChecker());
        _devices.Add("eos", new EosUtilitychecker());
        _devices.Add("hotfolder", new HotFolderChecker());
        //StartCoroutine(MainFlow(3f));
    }

    /// <summary>
    /// Démarrer Mooveo pour la premiere fois (s'occupe d'appeler toute les fonctions relatives a l'initialisation)
    /// </summary>
    /// <param name="nPointToCalibrate">Le nombre de point a placer par l'utilisateur necessaire pour valider la calibration</param>
    public void StartMainFlow(int nPointToCalibrate)
    {
        StopAllFlows();
        ClearCalibrationUIData();
        _nPointToCalibrate = nPointToCalibrate;
        _mainFlowRoutine = StartCoroutine(MainFlow(3f));
    }

    /// <summary>
    /// Vérifie si un casque, une manette, et steamvr sont bien lancés.
    /// </summary>
    public void StartCheckingDevices()
    {
        StopAllFlows();
        _secondaryFlowRoutine = StartCoroutine(SecondaryFlow(3f));
    }

    public void StopAllFlows()
    {
        if(_mainFlowRoutine != null) StopCoroutine(_mainFlowRoutine);
        _mainFlowRoutine = null;
        if(_secondaryFlowRoutine !=null) StopCoroutine(_secondaryFlowRoutine);
        _secondaryFlowRoutine = null;
    }

    /// <summary>
    /// Affiche ou masque l'UI de chargement de la validation asynchrone.
    /// </summary>
    public void SetValidationUI(bool isVisible, string message = "")
    {
        if (_loadingSpinner != null) _loadingSpinner.SetActive(isVisible);
        if (_loadingText != null)
        {
            _loadingText.gameObject.SetActive(isVisible);
            _loadingText.text = message;
        }
    }

    /// <summary>
    /// Marque tous les points avec l'icône d'erreur et affiche un message à l'écran.
    /// </summary>
    public void MarkAllPointsError(string errorMessage)
    {
        for (int i = 0; i < _nPointToCalibrate; i++)
        {
            if (i < _marksIcons.Count)
            {
                _marksIcons[i].enabled = true;
                _marksIcons[i].sprite = _iconError;
                _marksIcons[i].color = _colorError;
            }
        }
        
        SetValidationUI(true, errorMessage);
        if (_loadingSpinner != null) _loadingSpinner.SetActive(false); // On cache le spinner lors d'une erreur
    }

    
    /// <summary>
    /// Donne l'information au manager de passer a l'etape suivante. Cette fonction est appelé par le <see cref="CalibrationManager"/> lorsque l'on appuie sur A
    /// </summary>
    /// <param name="calibrationController">La manette servant a la calibration</param>
    public void NotifyOnNextStep(CalibrationController calibrationController = null)
    {
        if (_waitingForClick && !_calibratingStep)
        {
            _controllerClicked = true;
            Debug.Log("UICalibrationManager → Controller click received");
        }
    }

    /// <summary>
    /// Met a jour l'ui concernant la calibration d'un point (une sorte de chargement allant de 0 a 100%
    /// </summary>
    /// <param name="value">le taux d'avancement du chargement</param>
    public void UpdateCurrentPointProgress(float value)
    {
        if (_currentIndexPoint < 0 || _currentIndexPoint >= _marks.Count) return;
        _marks[_currentIndexPoint].fillAmount = value;
    }
    
    /// <summary>
    /// Met a jour l'ui concernant la calibration d'un point si il y a eu une erreur durant celle ci
    /// </summary>
    public void MarkCurrentPointError(string errorMessage)
    {
        _currentPointHasError = true;
    }
    
    /// <summary>
    /// Met a jour l'ui concernant la calibration d'un point si il a été validé et passe au suivant
    /// </summary>
    public void FinishCurrentPoint()
    {
        if (_currentIndexPoint < 0 || _currentIndexPoint >= _marks.Count) return;

        Image icon = _marksIcons[_currentIndexPoint];
        icon.enabled = true;
        icon.sprite = _currentPointHasError ? _iconError : _iconDone;
        icon.color = _currentPointHasError ? _colorError : _colorDone;
        
        if(!_currentPointHasError) _currentIndexPoint++;
        
        if (_currentIndexPoint < _marks.Count && !_currentPointHasError && _currentIndexPoint < _nPointToCalibrate)
        {
            _marks[_currentIndexPoint].fillAmount = 0f;
            _marksIcons[_currentIndexPoint].enabled = false;
            
            UpdateZoneIconPosition();
        }
        else
        {
            Debug.Log("Calibration terminée !");
        }
        _currentPointHasError = false;
    }
    
    /// <summary>
    /// Initialise le Canvas contenant l'ui du joueur et son curseur en fonction des valeurs de la calibration
    /// </summary>
    /// <param name="position">La position du canvas dans le world</param>
    /// <param name="sizeDelta">La taille en pixel que le Canvas doit avoir</param>
    /// <param name="euler">La rotation du canvas dans le world</param>
    public void InitCanvasCursor(Vector3 position, Vector2 sizeDelta, Vector3 euler)
    {
        _canvasCursors.position = position;//new Vector3(position.x, position.y, position.z + 0.05f);
        _canvasCursors.sizeDelta = sizeDelta;
        _canvasCursors.eulerAngles = euler;
    }
    
    /// <summary>
    /// Ajoute un curseur au Canvas
    /// </summary>
    /// <param name="cursor">Le curseur à ajouter</param>
    public void AddCursor(CalibrationCursorUI cursor)
    {
        if(_cursors.Contains(cursor)) return;
        _cursors.Add(cursor);
    }

    /// <summary>
    /// Enlever un curseur du canvas
    /// </summary>
    /// <param name="cursor">Le curseur à enlever</param>
    public void RemoveCursor(CalibrationCursorUI cursor)
    {
        if(!_cursors.Contains(cursor)) return;
        _cursors.Remove(cursor);
    }
    
    IEnumerator MainFlow(float seconds)
    {
        while (true)
        {
            yield return StartCoroutine(CheckAllDevicesLoop(seconds));
            yield return StartCoroutine(WaitForControllerClick());
            yield return StartCoroutine(CalibrationLoop());
            yield return StartCoroutine(WaitForControllerClick(TestCalibration));
        }

    }
    IEnumerator SecondaryFlow(float seconds)
    {
        while (true)
        {
            yield return StartCoroutine(CheckAllDevicesLoop(seconds));
            yield return StartCoroutine(WaitForControllerClick(TestCalibration));
        }
    }
    
    IEnumerator CheckAllDevicesLoop(float seconds)
    {
        if(_steamAnimator != null) _steamAnimator.SetTrigger("Loading");
        if(_hmdAnimator != null) _hmdAnimator.SetTrigger("Loading");
        if(_controllersAnimator != null) _controllersAnimator.SetTrigger("Loading");
        if(_eosAnimator != null) _eosAnimator.SetTrigger("Loading");
        if(_hotFolderAnimator != null) _hotFolderAnimator.SetTrigger("Loading");
        
        _panelCalibrating.SetActive(false);
        _panelCheckDevices.SetActive(_checkDevicePanelEnabled);
        bool steamOK = false;
        bool hmdOK = false;
        bool leftOK = false;
        bool rightOK = false;
        bool eosOK = false;
        bool hotFolderOK = false;
        
        bool steamLaunchAttempted = false;
        bool eosLaunchAttempted = false;
        bool hotFolderLaunchAttempted = false;

        while (!_allDevicesValid)
        {
            float stepWait = seconds / 6;
            
            if (!steamOK)
            {
                if (!_devices.ContainsKey("steam")) _devices.Add("steam", new SteamVRChecker());
                
                steamOK = CheckAndManageSoftware(
                    _devices["steam"], 
                    _steamText, 
                    _steamAnimator, 
                    "SteamVR", 
                    GlobalSettings.Core.GlobalSettings.Instance.SteamVREXEPath.Value, 
                    ref steamLaunchAttempted
                );
            }
            if (!hmdOK)
            {
                DefaultLoadingText(_hmdsText, "Checking HMD...");
                yield return new WaitForSeconds(seconds / 2);
                if(!_devices.ContainsKey("hmd")) _devices.Add("hmd", new HMDChecker());
                hmdOK = ValidateDevice(_devices["hmd"], _hmdsText, _hmdAnimator, "HMD");
            }

            if (!leftOK)
            {
                DefaultLoadingText(_controllersText, "Checking Left Controller...");
                yield return new WaitForSeconds(seconds / 2);
                if(!_devices.ContainsKey("leftController")) _devices.Add("leftController", new LeftControllerChecker());
                leftOK = ValidateDevice(_devices["leftController"], _controllersText, _controllersAnimator, "Left Controller");
            }

            if (!rightOK)
            {
                DefaultLoadingText(_controllersText, "Checking Right Controller...");
                yield return new WaitForSeconds(seconds / 2);
                if(!_devices.ContainsKey("rightController")) _devices.Add("rightController", new RightControllerChecker());
                rightOK = ValidateDevice(_devices["rightController"], _controllersText, _controllersAnimator, "Right Controller");
            }
            
            if (!eosOK)
            {
                if (!_devices.ContainsKey("eos")) _devices.Add("eos", new EosUtilitychecker());
                
                eosOK = CheckAndManageSoftware(
                    _devices["eos"], 
                    _eosText, 
                    _eosAnimator, 
                    "EOS Utility", 
                    GlobalSettings.Core.GlobalSettings.Instance.EosUtilityEXEPath.Value, 
                    ref eosLaunchAttempted
                );
            }
            
            if (!hotFolderOK)
            {
                if (!_devices.ContainsKey("hotfolder")) _devices.Add("hotfolder", new HotFolderChecker());
                Debug.Log(GlobalSettings.Core.GlobalSettings.Instance.HotFolderEXEPath.Value);
                hotFolderOK = CheckAndManageSoftware(
                    _devices["hotfolder"], 
                    _hotFolderText, 
                    _hotFolderAnimator, 
                    "Hot Folder", 
                    GlobalSettings.Core.GlobalSettings.Instance.HotFolderEXEPath.Value, 
                    ref hotFolderLaunchAttempted
                );
            }

            _allDevicesValid = (steamOK && hmdOK && (leftOK || rightOK) && eosOK && hotFolderOK);

            if (!_allDevicesValid)
                yield return new WaitForSeconds(1f);
        }
        OnDevicesChecked?.Invoke(true);
    }
    
    private bool CheckAndManageSoftware(IDeviceChecker checker, TextMeshProUGUI txt, Animator animator, string displayName, string exePath, ref bool hasLaunched)
    {
        if (checker.IsConnected())
        {
            txt.text = $"OK: {checker.DeviceName}";
            if(animator != null) animator.SetTrigger("Success");
            return true;
        }
        
        if (!hasLaunched)
        {
            txt.text = $"Launching {displayName}...";
            bool success = AppLauncher.LaunchProcess(exePath);
            
            if (success)
            {
                hasLaunched = true;
                Debug.Log($"[UICalibration] Commande de lancement envoyée pour {displayName}");
            }
            else
            {
                txt.text = $"Failed to launch {displayName}";
                if(animator != null) animator.SetTrigger("Failure");
            }
        }
        else
        {
            txt.text = $"Waiting for {displayName}...";
        }
        
        return false;
    }
    IEnumerator WaitForControllerClick(Action onClick = null)
    {
        _textValidate.SetActive(true);
        Debug.Log("Ready → Waiting for controller click…");

        _waitingForClick = true;
        _controllerClicked = false;

        while (true)
        {
            if (!AllDevicesStillValid())
            {
                _waitingForClick = false;
                _allDevicesValid = false;
                _textValidate.SetActive(false);
                yield break;
            }
            
            if (_controllerClicked)
            {
                Debug.Log("Click detected → continue calibration.");
                _textValidate.SetActive(false);
                onClick?.Invoke();
                _waitingForClick = false;
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }
    }
    IEnumerator CalibrationLoop()
    {
        _exitCalibration = false;
        CalibrationManager.instance.OnCalibrationEnded += OnCalibrationFinished;
    
        _panelCheckDevices.SetActive(false);
        _panelCalibrating.SetActive(true);

        _currentIndexPoint = 0;
        _marks[_currentIndexPoint].fillAmount = 0f;
        _marks[_currentIndexPoint].enabled = true;

        UpdateZoneIconPosition();
        SubscribeToCalibrateEvents();

        while (!_exitCalibration)
        {
            if (!AllDevicesStillValid())
            {
                Debug.LogWarning("Disconnected → back to checking");
                _allDevicesValid = false;
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }

        UnSubscribeToCalibrateEvents();
        CalibrationManager.instance.OnCalibrationEnded -= OnCalibrationFinished;
        Debug.Log("CalibrationLoop → exited cleanly");
    }
    
    
    bool ValidateDevice(IDeviceChecker checker, TextMeshProUGUI txt, Animator animator, string errorDeviceName)
    {
        if (checker.IsConnected())
        {
            txt.text = $"OK: {checker.DeviceName}";
            animator.SetTrigger("Success");
            return true;
        }
        else
        {
            txt.text = $"Missing: {errorDeviceName}";
            animator.SetTrigger("Failure");
            return false;
        }
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
        _nPointToCalibrate = 3;

        for(int i = 0; i < _marks.Count; i++)
        {
            _marks[i].color = Color.white;
            _marks[i].fillAmount = 0f;
            
            _marksIcons[i].color = Color.white;
            _marksIcons[i].sprite = null;
            _marksIcons[i].enabled = false;
        }
        
        _animatorBtnAfterCalibration.PlayClose();

    }
    void DefaultLoadingText(TextMeshProUGUI txt, string text) => txt.text = text;
    private void UpdateZoneIconPosition()
    {
        if (_currentIndexPoint < 0 || _currentIndexPoint >= _marks.Count) return;
        
        Vector2 markPos = _marks[_currentIndexPoint].rectTransform.anchoredPosition;

        _zoneIcon.anchoredPosition = markPos;
        
        float radius = _zoneRadiusCm / 100f * _unitToCanvas;
        _zoneIcon.sizeDelta = new Vector2(radius * 2, radius * 2);
        _zoneIcon.gameObject.SetActive(true);
        _textCalibration.anchoredPosition = new Vector3(markPos.x, markPos.y + 150f);
    }
    bool AllDevicesStillValid()
    {
        bool oneControllerIsConnected = false;
        foreach (var device in _devices)
        {
            if(device.Key == "steam") continue;
            
            IDeviceChecker dev = device.Value;
            
            if (dev.Type != DeviceType.CONTROLLER)
            {
                if (!dev.IsConnected())
                {
                    OnDevicesChecked?.Invoke(false);
                    return false;
                }
            }
            else
            {
                if (dev.IsConnected()) oneControllerIsConnected = true;
            }
        }
        
        if (!oneControllerIsConnected)
        {
            OnDevicesChecked?.Invoke(false);
            return false;
        }

        return true;
    }
    private void SubscribeToCalibrateEvents()
    {
        UnSubscribeToCalibrateEvents();
        CalibrationManager.instance.OnUpdatePoint += UpdateCurrentPointProgress;
        CalibrationManager.instance.OnErrorDuringCalibration += MarkCurrentPointError;
        CalibrationManager.instance.OnSubmitPoint += FinishCurrentPoint;
    }

    private void UnSubscribeToCalibrateEvents()
    {
        CalibrationManager.instance.OnUpdatePoint -= UpdateCurrentPointProgress;
        CalibrationManager.instance.OnErrorDuringCalibration -= MarkCurrentPointError;
        CalibrationManager.instance.OnSubmitPoint -= FinishCurrentPoint;
    }
    private void TestCalibration()
    {
        _checkDevicePanelEnabled = false;
        _panelCheckDevices.SetActive(false);
        _animatorBtnAfterCalibration.PlayOpen();
        CalibrationManager.instance.TestCalibrationSetupPlayArea();
    }
    private void OnCalibrationFinished()
    {
        Debug.Log("Calibration terminée → demande de sortie");

        _exitCalibration = true;
    
        CalibrationManager.instance.OnCalibrationEnded -= OnCalibrationFinished;
    }
}

