using System;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem;

[RequireComponent(typeof(InputReader))]
public class DefaultController : MonoBehaviour
{
//#if UNITY_EDITOR
    [SerializeField, Tooltip("Simule l'appui Trigger en éditeur sans manette")]
    protected bool _simulateTrigger = false;
//#endif
    [SerializeField] protected TrackedPoseDriver _poseDriver;
    [SerializeField] private InputReader _reader;
    [SerializeField] protected ControllerShortcutHandler _shortcutHandler;
    [SerializeField] protected bool _debug = false;

    public bool IsTracked = false;

    public int PlayerID;
    
    public bool SimulateTrigger 
    { 
        get => _simulateTrigger; 
        set => _simulateTrigger = value; 
    }

    private void OnValidate()
    {
        if(GetComponent<InputReader>() != null)
            _reader = GetComponent<InputReader>();
        else Debug.LogError("no input reader found on gameobject");
    }

    private void OnEnable()
    {
        Bind();
    }

    private void OnDisable()
    {
        Unbind();
    }
    
    public void SetupTrackedPoseDriver(InputActionProperty position, InputActionProperty rotation)
    {
        if (_poseDriver != null)
        {
            _poseDriver.positionInput = position;
            _poseDriver.rotationInput = rotation;
        }
        else 
        {
            Debug.LogWarning($"[{gameObject.name}] TrackedPoseDriver manquant sur le prefab !");
        }
    }
    
    public virtual void HandleTrigger(float value)
    {
        if (_debug) Debug.Log($"[{nameof(DefaultController)}] HandleTrigger called with value: {value}");
    }
    
    public virtual void HandleTriggerPressed( )
    {
        if (_debug) Debug.Log($"[{nameof(DefaultController)}] HandleTriggerPressed called");
    }

    public virtual void HandleTriggerReleased( )
    {
        if (_debug) Debug.Log($"[{nameof(DefaultController)}] HandleTriggerReleased called");
    }

    public virtual void HandleAPressed()
    {
        if (_debug) Debug.Log($"[{nameof(DefaultController)}] HandleAPressed called");
    }

    public virtual void HandleAReleased()
    {
        if (_debug) Debug.Log($"[{nameof(DefaultController)}] HandleAReleased called");
    }

    public virtual void HandleBPressed()
    {
        if (_debug) Debug.Log($"[{nameof(DefaultController)}] HandleBPressed called");
    }

    public virtual void HandleBReleased()
    {
        if (_debug) Debug.Log($"[{nameof(DefaultController)}] HandleBReleased called");
    }

    public virtual void HandleThumb()
    {
        if (_debug) Debug.Log($"[{nameof(DefaultController)}] HandleThumb called");
    }

    public virtual void HandleIsTracked(bool isTracked)
    {
        if (_debug) Debug.Log($"[{nameof(DefaultController)}] HandleIsTracked called with isTracked: {isTracked}");
        IsTracked = isTracked;
    }

    public virtual void HandleDeviceConnected(bool connected) { }

    public virtual void Bind()
    {
        if (_reader == null) return;

        _reader.IsTracked += HandleIsTracked;
        _reader.DeviceConnectedChanged += HandleDeviceConnected;
        
        _reader.TriggerChanged += HandleTrigger;
        _reader.TriggerPressed += HandleTriggerPressed;
        _reader.TriggerReleased += HandleTriggerReleased;

        _reader.AButtonPressed += OnAPressed;
        _reader.AButtonReleased += HandleAReleased;

        _reader.BButtonPressed += HandleBPressed;
        _reader.BButtonReleased += HandleBReleased;
        
        _reader.ThumbButtonPressed += OnThumb;

        _reader.ScrollChanged += HandleScroll;

        _reader.ScrollChanged += HandleScroll;

        _shortcutHandler.Init(_reader);
    }

    public virtual void Unbind()
    {
        if (_reader == null) return;

        _reader.IsTracked -= HandleIsTracked;
        _reader.DeviceConnectedChanged -= HandleDeviceConnected;

        _reader.TriggerChanged -= HandleTrigger;
        _reader.TriggerPressed -= HandleTriggerPressed;
        _reader.TriggerReleased -= HandleTriggerReleased;

        _reader.AButtonPressed -= OnAPressed;
        _reader.AButtonReleased -= HandleAReleased;

        _reader.BButtonPressed -= HandleBPressed;
        _reader.BButtonReleased -= HandleBReleased;
        
        _reader.ThumbButtonPressed -= OnThumb;

        _reader.ScrollChanged -= HandleScroll;

    }

    // =========================================================================
    // Popup modale — interception AVANT les gestionnaires métier
    // =========================================================================
    // ⚠ Posée ICI, dans la classe de base, elle vaut pour TOUS les contrôleurs (peintre comme
    // testeur de calibration) et couvre les deux scènes en un seul point. Sans consommation, la
    // poignée déclencherait aussi l'annulation du peintre en fermant la popup.
    // La GÂCHETTE n'est PAS interceptée : c'est elle qui clique dans l'interface. Le blocage de la
    // progression se fait à l'arrivée (UICalibrationToolkit.NotifyOnNextStep).

    private void OnAPressed()
    {
        if (ModalPopups.IsOpen) { ModalPopups.Dismiss(); return; }
        HandleAPressed();
    }

    private void OnThumb()
    {
        if (ModalPopups.IsOpen) { ModalPopups.Confirm(); return; }
        HandleThumb();
    }

    public virtual void HandleScroll(Vector2 value)
    {
        if (_debug) Debug.Log($"[{nameof(DefaultController)}] HandleScroll called with value: {value}");
        throw new NotImplementedException();
    }
}
