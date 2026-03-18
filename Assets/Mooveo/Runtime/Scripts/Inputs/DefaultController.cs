using System;
using UnityEngine;

[RequireComponent(typeof(InputReader))]
public class DefaultController : MonoBehaviour
{
    [SerializeField] private InputReader _reader;
    [SerializeField] protected ControllerShortcutHandler _shortcutHandler;

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
        
    }
    
    public virtual void HandleTriggerPressed( )
    {
        
    }

    public virtual void HandleAPressed()
    {
        
    }

    public virtual void HandleAReleased()
    {
        
    }

    public virtual void HandleBPressed()
    {
        
    }

    public virtual void HandleBReleased()
    {
        
    }

    public virtual void HandleThumb()
    {
        
    }

    public void HandleIsTracked(bool isTracked)
    {
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
        throw new NotImplementedException();
    }
}
