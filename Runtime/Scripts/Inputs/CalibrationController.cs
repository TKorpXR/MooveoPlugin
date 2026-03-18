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

    private void Start()
    {
        GlobalSettings.Core.GlobalSettings.Instance.PaintingRadiusMIN.Bind(() => _factorRadiusMIN, value => _factorRadiusMIN = value);
        GlobalSettings.Core.GlobalSettings.Instance.PaintingRadiusMAX.Bind(() => _factorRadiusMAX, value => _factorRadiusMAX = value);

    }

    private void Update()
    {
        _simulatedPaintDepth = Vector3.Distance(transform.position, _canvasPosition); // TODO FAIRE EN SORTE QUE LA DEPTH NE DEPENDE PAS DU CENTRE MAIS DE TOUTE LA SURFACE
        _simulatePaintDecalRadius = Mathf.Lerp(_factorRadiusMIN, _factorRadiusMAX, Mathf.Clamp01(_simulatedPaintDepth));
        if(_cursor != null) 
            _cursor.UpdateCursor(transform, _cam, _simulatePaintDecalRadius);
    }

    public override void HandleTrigger(float value)
    {
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
        CalibrationManager.instance.HandleClick(transform);
    }

    public override void HandleAPressed()
    {
        CalibrationManager.instance.NotifyUIClickOnce(this);
    }

    public override void HandleAReleased()
    {
        
    }

    public override void HandleBPressed()
    {
        
    }

    public override void HandleBReleased()
    {
        
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
