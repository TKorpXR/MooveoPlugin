using System;
using UnityEngine;

[RequireComponent(typeof(InputReader))]
public class DefaultController : MonoBehaviour
{
    [SerializeField] private InputReader _reader;
    [SerializeField] protected ControllerShortcutHandler _shortcutHandler;
    [SerializeField] private bool _debug = false;

    public bool IsTracked = false;

    public int PlayerID;

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
    public virtual void HandleTrigger(float value)
    {
        if (_debug) Debug.Log($"[{nameof(DefaultController)}] HandleTrigger called with value: {value}");
    }
    
    public virtual void HandleTriggerPressed( )
    {
        if (_debug) Debug.Log($"[{nameof(DefaultController)}] HandleTriggerPressed called");
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

    public void HandleIsTracked(bool isTracked)
    {
        if (_debug) Debug.Log($"[{nameof(DefaultController)}] HandleIsTracked called with isTracked: {isTracked}");
        IsTracked = isTracked;
    }

    public virtual void Bind()
    {
        if (_reader == null) return;

        _reader.TriggerChanged += HandleTrigger;
        _reader.TriggerPressed += HandleTriggerPressed;

        _reader.AButtonPressed += HandleAPressed;
        _reader.AButtonReleased += HandleAReleased;

        _reader.BButtonPressed += HandleBPressed;
        _reader.BButtonReleased += HandleBReleased;
        
        _reader.ThumbButtonPressed += HandleThumb;

        _reader.ScrollChanged += HandleScroll;

        _reader.ScrollChanged += HandleScroll;

        _shortcutHandler.Init(_reader);
    }

    public virtual void Unbind()
    {
        if (_reader == null) return;

        _reader.TriggerChanged -= HandleTrigger;
        _reader.TriggerPressed -= HandleTriggerPressed;

        _reader.AButtonPressed -= HandleAPressed;
        _reader.AButtonReleased -= HandleAReleased;

        _reader.BButtonPressed -= HandleBPressed;
        _reader.BButtonReleased -= HandleBReleased;
        
        _reader.ThumbButtonPressed -= HandleThumb;

        _reader.ScrollChanged -= HandleScroll;

    }

    public virtual void HandleScroll(Vector2 value)
    {
        if (_debug) Debug.Log($"[{nameof(DefaultController)}] HandleScroll called with value: {value}");
        throw new NotImplementedException();
    }
}
