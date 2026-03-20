using System;
using UnityEngine;

public enum EUIClickInput
{
    TriggerAxis,
    ButtonA,
    ButtonB 
}
public class CalibrationController : DefaultController
{
    [Header("References")]
    [SerializeField] private CalibrationCursorUI _cursor;
    [SerializeField] private Transform _trackerTransform;
    
    [Header("Painting Settings")]
    [SerializeField, Tooltip("Borne d'agrandissement du radius de spray")] private float _factorRadiusMIN = 0.1f;
    [SerializeField, Tooltip("Borne d'agrandissement du radius de spray")] private float _factorRadiusMAX = 0.5f;
    
    [Header("UI Interaction Settings")]
    [SerializeField, Tooltip("Quel bouton utiliser pour interagir avec l'UI ?")] 
    private EUIClickInput _uiClickInput = EUIClickInput.TriggerAxis;
    
    [SerializeField, Tooltip("Valeur à partir de laquelle le clic devient TRUE (Uniquement pour TriggerAxis)")] 
    private float _triggerThresholdTrue = 0.7f;
    
    [SerializeField, Tooltip("Valeur à partir de laquelle le clic redevient FALSE (Uniquement pour TriggerAxis)")] 
    private float _triggerThresholdFalse = 0.6f;

    private Transform _cursorAnchor;
    private Camera _cam;
    private Vector3 _canvasPosition;
    private float _simulatePaintDecalRadius;
    private float _simulatedPaintDepth;

    private void Start()
    {
        GlobalSettings.Core.GlobalSettings.Instance.CursorRadiusMIN.Bind(() => _factorRadiusMIN, value => _factorRadiusMIN = value);
        GlobalSettings.Core.GlobalSettings.Instance.CursorRadiusMAX.Bind(() => _factorRadiusMAX, value => _factorRadiusMAX = value);

        if(_trackerTransform != null) _cursorAnchor = _trackerTransform;
        else _cursorAnchor = transform;
    }

    private void Update()
    {
        _simulatedPaintDepth = Vector3.Distance(transform.position, _canvasPosition); // TODO FAIRE EN SORTE QUE LA DEPTH NE DEPENDE PAS DU CENTRE MAIS DE TOUTE LA SURFACE
        _simulatePaintDecalRadius = Mathf.Lerp(_factorRadiusMIN, _factorRadiusMAX, Mathf.Clamp01(_simulatedPaintDepth));
        if(_cursor != null) 
            _cursor.UpdateCursor(_cursorAnchor, _cam, _simulatePaintDecalRadius);
    }

    public override void HandleTrigger(float value)
    {
        if (_cursor != null && _uiClickInput == EUIClickInput.TriggerAxis)
        {
            // Logique de l'Hystérésis
            if (value >= _triggerThresholdTrue)
            {
                _cursor.CursorInteractor.SetClicking(true);
            }
            else if (value <= _triggerThresholdFalse)
            {
                _cursor.CursorInteractor.SetClicking(false);
            }
        }
    }

    public override void HandleTriggerPressed( )
    {
        CalibrationManager.instance.HandleClick(transform);
    }

    public override void HandleAPressed()
    {
        CalibrationManager.instance.NotifyUIClickOnce(this);
        if (_cursor != null && _uiClickInput == EUIClickInput.ButtonA)
        {
            _cursor.CursorInteractor.SetClicking(true);
        }
    }

    public override void HandleAReleased()
    {
        if (_cursor != null && _uiClickInput == EUIClickInput.ButtonA)
        {
            _cursor.CursorInteractor.SetClicking(false);
        }
    }

    public override void HandleBPressed()
    {
        if (_cursor != null && _uiClickInput == EUIClickInput.ButtonB)
        {
            _cursor.CursorInteractor.SetClicking(true);
        }
    }

    public override void HandleBReleased()
    {
        if (_cursor != null && _uiClickInput == EUIClickInput.ButtonB)
        {
            _cursor.CursorInteractor.SetClicking(false);
        }
    }

    public override void HandleThumb()
    {
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

    public void ClearTest()
    {
        CalibrationManager.instance.RemoveTester(this);
        if(_cursor != null) _cursor.Destroy(); //plus robuse, si on call la recalibration a un mauvais moment, evite un crash car le cursor a deja été destroy
    }
}
