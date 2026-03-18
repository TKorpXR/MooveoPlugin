using System;
using System.Collections.Generic;
using UnityEngine;

public enum ControllerButton
{
    None,
    Trigger,
    A,
    B,
    Thumb
}

public class KeyBindingManager : MonoBehaviour
{
    public static KeyBindingManager Instance;

    public class KeyBinding
    {
        public string Name;
        public List<KeyCode> KeyCombo;
        public List<ControllerButton> ControllerCombo;
        public Action OnActivate;
    }

    private List<KeyBinding> _bindings = new List<KeyBinding>();
    public List<KeyBinding> Bindings => _bindings;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        
        DontDestroyOnLoad(gameObject);
    }

    public void RegisterBinding(string name, List<KeyCode> keys, List<ControllerButton> buttons, Action action)
    {
        _bindings.Add(new KeyBinding
        {
            Name = name,
            KeyCombo = keys ?? new List<KeyCode>(),
            ControllerCombo = buttons ?? new List<ControllerButton>(),
            OnActivate = action
        });
    }

    // Helper to safely execute binding action
    public void ExecuteBinding(KeyBinding binding)
    {
        binding.OnActivate?.Invoke();
    }

    private void Update()
    {
        // Keyboard checks remain centralized
        foreach (var binding in _bindings)
        {
            if (CheckKeyboardCombo(binding.KeyCombo))
            {
                binding.OnActivate?.Invoke();
            }
        }
    }

    private bool CheckKeyboardCombo(List<KeyCode> combo)
    {
        if (combo == null || combo.Count == 0) return false;

        if (combo.Count == 1) return Input.GetKeyDown(combo[0]);

        for (int i = 0; i < combo.Count - 1; i++)
        {
            if (!Input.GetKey(combo[i])) return false;
        }
        return Input.GetKeyDown(combo[combo.Count - 1]);
    }
}
