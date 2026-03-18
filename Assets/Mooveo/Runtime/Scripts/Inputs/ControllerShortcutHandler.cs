using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

public class ControllerShortcutHandler : MonoBehaviour
{
    [Serializable]
    public class ShortcutEntry
    {
        public string Name;
        public ControllerButton Button1;
        public ControllerButton Button2;
        public UnityEvent OnTrigger;
    }

    [SerializeField]
    private List<ShortcutEntry> _shortcuts = new List<ShortcutEntry>();

    private InputReader _reader;
    private Dictionary<ControllerButton, bool> _buttonStates = new Dictionary<ControllerButton, bool>();
    private Dictionary<ControllerButton, bool> _buttonDown = new Dictionary<ControllerButton, bool>();

    private bool _initialized = false;

    public void Init(InputReader reader)
    {
        Unbind();
        _reader = reader;
        
        foreach(ControllerButton btn in System.Enum.GetValues(typeof(ControllerButton)))
        {
            _buttonStates[btn] = false;
            _buttonDown[btn] = false;
        }

        Bind();
        _initialized = true;
    }

    private void Bind()
    {
        if (_reader == null) return;
        _reader.TriggerPressed += OnTriggerPressed;
        _reader.TriggerReleased += OnTriggerReleased;
        _reader.AButtonPressed += OnAPressed;
        _reader.AButtonReleased += OnAReleased;
        _reader.BButtonPressed += OnBPressed;
        _reader.BButtonReleased += OnBReleased;
        _reader.ThumbButtonPressed += OnThumbPressed;
    }

    private void OnDisable()
    {
        ClearFrameData();
        foreach(var key in new List<ControllerButton>(_buttonStates.Keys)) _buttonStates[key] = false;
    }

    private void OnDestroy()
    {
        Unbind();
    }

    private void Unbind()
    {
        if (_reader == null) return;
        _reader.TriggerPressed -= OnTriggerPressed;
        _reader.TriggerReleased -= OnTriggerReleased;
        _reader.AButtonPressed -= OnAPressed;
        _reader.AButtonReleased -= OnAReleased;
        _reader.BButtonPressed -= OnBPressed;
        _reader.BButtonReleased -= OnBReleased;
        _reader.ThumbButtonPressed -= OnThumbPressed;
    }

    private void Update()
    {
        if (!_initialized) return;

        // Check each shortcut
        foreach (var shortcut in _shortcuts)
        {
            if (CheckShortcut(shortcut))
            {
                shortcut.OnTrigger?.Invoke();
            }
        }
        
        if (KeyBindingManager.Instance != null)
        {
            foreach (var binding in KeyBindingManager.Instance.Bindings)
            {
                if (binding.ControllerCombo != null && binding.ControllerCombo.Count > 0)
                {
                    if (CheckControllerCombo(binding.ControllerCombo))
                    {
                        KeyBindingManager.Instance.ExecuteBinding(binding);
                        break; 
                    }
                }
            }
        }

        ClearFrameData();
    }
    
    private bool CheckShortcut(ShortcutEntry entry)
    {
        bool b1Active = entry.Button1 == ControllerButton.None || (_buttonStates[entry.Button1]);
        bool b2Active = entry.Button2 == ControllerButton.None || (_buttonStates[entry.Button2]);
        
        if (!b1Active || !b2Active) return false;

        bool b1JustPressed = entry.Button1 != ControllerButton.None && _buttonDown[entry.Button1];
        bool b2JustPressed = entry.Button2 != ControllerButton.None && _buttonDown[entry.Button2];
        
        return (b1JustPressed || b2JustPressed);
    }

    private bool CheckControllerCombo(List<ControllerButton> combo)
    {
        if (combo == null || combo.Count == 0) return false;

        bool allHeld = true;
        bool anyJustPressed = false;

        foreach(var btn in combo)
        {
            if (!_buttonStates.ContainsKey(btn) || !_buttonStates[btn]) 
            {
                allHeld = false;
                break;
            }
            if (_buttonDown.ContainsKey(btn) && _buttonDown[btn]) anyJustPressed = true;
        }

        return allHeld && anyJustPressed;
    }

    public void ClearFrameData()
    {
        var keys = new List<ControllerButton>(_buttonDown.Keys);
        foreach(var k in keys) _buttonDown[k] = false;
        
        if(_buttonStates.ContainsKey(ControllerButton.Thumb))
             _buttonStates[ControllerButton.Thumb] = false; 
    }

    private void SetState(ControllerButton btn, bool state)
    {
        if(state && (!_buttonStates.ContainsKey(btn) || !_buttonStates[btn])) 
            _buttonDown[btn] = true;
        
        _buttonStates[btn] = state;
    }

    private void OnTriggerPressed() => SetState(ControllerButton.Trigger, true);
    private void OnTriggerReleased() => SetState(ControllerButton.Trigger, false); 
    private void OnAPressed() => SetState(ControllerButton.A, true);
    private void OnAReleased() => SetState(ControllerButton.A, false);
    private void OnBPressed() => SetState(ControllerButton.B, true);
    private void OnBReleased() => SetState(ControllerButton.B, false);
    private void OnThumbPressed() => SetState(ControllerButton.Thumb, true);
}
