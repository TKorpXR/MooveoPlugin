using System;
using UnityEngine;

public class CalibrationController : DefaultController
{
    [SerializeField] private CalibrationCursorUI _cursor;
    [SerializeField, Tooltip("Borne d'agrandissement du radius de spray")] private float _factorRadiusMIN = 0.1f;
    [SerializeField, Tooltip("Borne d'agrandissement du radius de spray")] private float _factorRadiusMAX = 0.5f;
    private Camera _cam;
    private Vector3 _canvasPosition;
    private float _simulatePaintDecalRadius;
    private float _simulatedPaintDepth;
    private Transform _wallTransform;
    
    public event Action<bool> OnLockRotationChanged;
    public event Action<bool> OnLockSizeChanged;
    
//#if UNITY_EDITOR
    private bool _wasSimulateTrigger = false;
//#endif

    private bool _lockRotation = false;
    private bool _locksize = false;
    private float _lastScale = 0.0f;


    private void Start()
    {
        GlobalSettings.Core.GlobalSettings.Instance.PaintingRadiusMIN.Bind(() => _factorRadiusMIN, value => _factorRadiusMIN = value);
        GlobalSettings.Core.GlobalSettings.Instance.PaintingRadiusMAX.Bind(() => _factorRadiusMAX, value => _factorRadiusMAX = value);

    }

    private void Update()
    {
//#if UNITY_EDITOR
        if (_simulateTrigger != _wasSimulateTrigger)
        {
            if (_simulateTrigger)
            {
                HandleTrigger(1f);
                HandleTriggerPressed();
            }
            else
            {
                HandleTrigger(0f);
            }

            _wasSimulateTrigger = _simulateTrigger;
        }
//#endif
        
        if (_wallTransform == null)
        {
            if (CalibrationManager.instance != null)
                _wallTransform = CalibrationManager.instance.TransformTestReference;

            if (_wallTransform == null) return;
        }
        
        Plane wallPlane = new Plane(-_wallTransform.forward, _wallTransform.position);
        //Vector3 rayDirection = _lockRotation ? _wallTransform.forward : transform.forward;
        Vector3 rayDirection = _lockRotation ? _wallTransform.forward : transform.forward;
        Ray ray = new Ray(transform.position, rayDirection);
        
        if (wallPlane.Raycast(ray, out float distance))
        {
            Vector3 hitPoint = ray.GetPoint(distance);
            _simulatedPaintDepth = distance;
            
            _simulatePaintDecalRadius = Mathf.Lerp(_factorRadiusMIN, _factorRadiusMAX, Mathf.Clamp01(_simulatedPaintDepth));
            
            if (_cursor != null)
            {
                _cursor.UpdateCursor(hitPoint, _locksize ? _lastScale : _simulatePaintDecalRadius);
                _cursor.SetVisibility(true);
            }
        }
        else
        {
            if (_cursor != null)
            {
                _cursor.SetVisibility(false);
            }
        }

    }

    public override void HandleTrigger(float value)
    {
        base.HandleTrigger(value);
        if (_cursor != null)
        {
            if (value > 0.7f)
            {
                _cursor.CursorInteractor.SetClicking(true);
            }
            else
            {
                _cursor.CursorInteractor.SetClicking(false);
            }
        }
    }

    public override void HandleTriggerPressed()
    {
        base.HandleTriggerPressed();
        CalibrationManager.instance.HandleClick(transform); // Capture de point
        CalibrationManager.instance.NotifyUIClickOnce(this); // Navigation UI
    }

    [ContextMenu("HandleAPressed")]
    public override void HandleAPressed()
    {
        base.HandleAPressed();
        _locksize = !_locksize;
        OnLockSizeChanged?.Invoke(_locksize);
        _lastScale = _simulatePaintDecalRadius;
    }

    public void TryRecalibrate()
    {
        Debug.Log($"[CalibrationController] TryRecalibrate");
        CalibrationManager.instance.StartOverCalibration();
    }

    public override void HandleAReleased()
    {
        base.HandleAReleased();
    }

    public override void HandleBPressed()
    {
        base.HandleBPressed();
    }

    public override void HandleBReleased()
    {
        base.HandleBReleased();
    }

    public override void HandleThumb()
    {
        base.HandleThumb();
        CalibrationManager.instance.HandleThumbClick();
        _lockRotation = !_lockRotation;
        OnLockRotationChanged?.Invoke(_lockRotation);
    }

    public void SetupForTest(Camera cam, Vector3 canvasPosition)
    {
        _cam = cam;
        _canvasPosition = canvasPosition;
        CalibrationManager.instance.AddNewTester(this);
        _cursor = CalibrationManager.instance.GetAssociatedCursor(this);
        _cursor.Init(this);
    }

    private void OnDestroy()
    {
        if (CalibrationManager.instance != null)
        {
            CalibrationManager.instance.RemoveTester(this);
        }
        if (_cursor != null && _cursor.gameObject != null)
        {
            Destroy(_cursor.gameObject);
        }
    }
}
