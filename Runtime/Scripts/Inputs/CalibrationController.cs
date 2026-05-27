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
    
//#if UNITY_EDITOR
    private bool _wasSimulateTrigger = false;
//#endif


    private void Start()
    {
        GlobalSettings.Core.GlobalSettings.Instance.CursorRadiusMIN.Bind(() => _factorRadiusMIN, value => _factorRadiusMIN = value);
        GlobalSettings.Core.GlobalSettings.Instance.CursorRadiusMAX.Bind(() => _factorRadiusMAX, value => _factorRadiusMAX = value);

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
        
        if (_cam == null) return;
        _simulatedPaintDepth = Vector3.Distance(transform.position, _canvasPosition); // TODO FAIRE EN SORTE QUE LA DEPTH NE DEPENDE PAS DU CENTRE MAIS DE TOUTE LA SURFACE
        _simulatePaintDecalRadius = Mathf.Lerp(_factorRadiusMIN, _factorRadiusMAX, Mathf.Clamp01(_simulatedPaintDepth));
        if(_cursor != null) 
            _cursor.UpdateCursor(transform, _cam, _simulatePaintDecalRadius);

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

    public override void HandleTriggerPressed( )
    {
        base.HandleTriggerPressed();
        CalibrationManager.instance.HandleClick(transform);
    }

    [ContextMenu("HandleAPressed")]
    public override void HandleAPressed()
    {
        base.HandleAPressed();
        CalibrationManager.instance.NotifyUIClickOnce(this);
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
    }

    public void SetupForTest(Camera cam, Vector3 canvasPosition)
    {
        _cam = cam;
        _canvasPosition = canvasPosition;
        CalibrationManager.instance.AddNewTester(this);
        _cursor = CalibrationManager.instance.GetAssociatedCursor(this);
        _cursor.Init();
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
