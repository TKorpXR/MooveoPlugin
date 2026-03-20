using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputReader : MonoBehaviour
{
    [SerializeField] private InputConfig _inputConfig;
    [SerializeField] private bool _ignoreGlobalSettings = false;
    [SerializeField] public bool SimulateVR = false;

    public event Action<float> TriggerChanged;
    public event Action TriggerPressed;
    public event Action TriggerReleased;
    public event Action AButtonPressed;
    public event Action AButtonReleased;
    public event Action BButtonPressed;
    public event Action BButtonReleased;
    public event Action ThumbButtonPressed;
    
    public event Action<Vector2> ScrollChanged;
    public event Action<Vector3> PositionChanged;
    public event Action<Quaternion> RotationChanged;
    public event Action<int> IsTrackedChanged;
    
    // Simulation variables
    private bool _simulatingTrigger = false;

    private Coroutine _initCoroutine;

    private void OnEnable()
    {
        if (_ignoreGlobalSettings)
        {
            InitializeInput();
            return;
        }
        
        if (GlobalSettings.Core.GlobalSettings.Instance == null)
        {
            // Robust fallback: Wait for GlobalSettings to initialize
            _initCoroutine = StartCoroutine(WaitForGlobalSettings());
            return;
        }

        InitializeInput();
    }

    private void OnDisable()
    {
        if (_initCoroutine != null) StopCoroutine(_initCoroutine);
        Unbind();
    }
    
    private IEnumerator WaitForGlobalSettings()
    {
        while (GlobalSettings.Core.GlobalSettings.Instance == null)
        {
            yield return null;
        }
        InitializeInput();
        _initCoroutine = null;
    }

    private void InitializeInput()
    {
        //if (_inputConfig == null && !GlobalSettings.Core.GlobalSettings.Instance.VR.Value) _inputConfig = GlobalSettings.Core.GlobalSettings.Instance.DebugConfig;
        if (_inputConfig != null) Bind();
    }

    public void SetInputConfig(InputConfig config)
    {
        if (_inputConfig == config) return;

        if (_inputConfig != null && isActiveAndEnabled) Unbind();

        _inputConfig = config;

        if (_inputConfig != null && isActiveAndEnabled) Bind();
    }
    
    private void Update()
    {
        if (SimulateVR)
        {
            HandleSimulation();
        }
    }

    private void HandleSimulation()
    {
        if (Mouse.current == null) return;

        // --- Clic Gauche = Trigger ---
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            TriggerPressed?.Invoke();
            TriggerChanged?.Invoke(1f); 
        }
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            TriggerReleased?.Invoke();
            TriggerChanged?.Invoke(0f);
        }
        // Maintien
        if (Mouse.current.leftButton.isPressed)
        {
            TriggerChanged?.Invoke(1f);
        }

        // --- Molette = Profondeur ---
        float scrollY = Mouse.current.scroll.y.ReadValue();
        
        if (Mathf.Abs(scrollY) > 0.01f)
        {
            // CORRECTION ICI : On envoie un Vector2. 
            // On met la direction (-1 ou 1) dans l'axe Y pour simuler un stick vertical ou un scroll standard.
            Vector2 scrollVector = new Vector2(0, Mathf.Sign(scrollY));
            ScrollChanged?.Invoke(scrollVector);
        }
        
        // --- Espace = Thumb Button (Menu) ---
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            ThumbButtonPressed?.Invoke();
        }
    }

    private void Bind()
    {
        BindAction(_inputConfig.TriggerAction,
            ctx => TriggerChanged?.Invoke(ctx.ReadValue<float>()),
            ctx => TriggerChanged?.Invoke(0f));

        BindAction(_inputConfig.TriggerButton,
            ctx => TriggerPressed?.Invoke(),
            ctx => TriggerReleased?.Invoke());

        BindAction(_inputConfig.AButton,
            ctx => AButtonPressed?.Invoke(),
            ctx => AButtonReleased?.Invoke());

        BindAction(_inputConfig.BButton,
            ctx => BButtonPressed?.Invoke(),
            ctx => BButtonReleased?.Invoke());

        BindAction(_inputConfig.ThumbButton,
            ctx => ThumbButtonPressed?.Invoke());
        
        /*BindAction(_inputConfig.LeftStick,
            ctx => StickChanged?.Invoke(ctx.ReadValue<Vector2>()));*/
        
        BindAction(_inputConfig.IsTrackedAction, ctx => 
            IsTrackedChanged?.Invoke(ctx.ReadValue<int>()));
        
        BindAction(_inputConfig.PositionAction, ctx => 
            PositionChanged?.Invoke(ctx.ReadValue<Vector3>()));
        
        BindAction(_inputConfig.RotationAction, ctx => 
            RotationChanged?.Invoke(ctx.ReadValue<Quaternion>()));
        
        BindAction(_inputConfig.ScrollAction, ctx =>
            ScrollChanged?.Invoke(ctx.ReadValue<Vector2>()));
        
    }

    private void Unbind()
    {
        UnbindAction(_inputConfig.TriggerAction,
            ctx => TriggerChanged?.Invoke(ctx.ReadValue<float>()),
            ctx => TriggerChanged?.Invoke(0f));

        UnbindAction(_inputConfig.TriggerButton,
            ctx => TriggerPressed?.Invoke(),
            ctx => TriggerReleased?.Invoke());

        UnbindAction(_inputConfig.AButton,
            ctx => AButtonPressed?.Invoke(),
            ctx => AButtonReleased?.Invoke());

        UnbindAction(_inputConfig.BButton,
            ctx => BButtonPressed?.Invoke(),
            ctx => BButtonReleased?.Invoke());

        UnbindAction(_inputConfig.ThumbButton,
            ctx => ThumbButtonPressed?.Invoke());

        /*UnbindAction(_inputConfig.LeftStick,
            ctx => StickChanged?.Invoke(ctx.ReadValue<Vector2>()));*/
        
        UnbindAction(_inputConfig.IsTrackedAction, ctx => 
            IsTrackedChanged?.Invoke(ctx.ReadValue<int>()));
        
        UnbindAction(_inputConfig.PositionAction, ctx => 
            PositionChanged?.Invoke(ctx.ReadValue<Vector3>()));
        
        UnbindAction(_inputConfig.RotationAction, ctx => 
            RotationChanged?.Invoke(ctx.ReadValue<Quaternion>()));
    }
    
    private void BindAction(InputActionReference actionRef,
        Action<InputAction.CallbackContext> performed,
        Action<InputAction.CallbackContext> canceled = null)
    {
        if (actionRef?.action == null) return;
        if (performed != null) actionRef.action.performed += performed;
        if (canceled != null)  actionRef.action.canceled  += canceled;
        actionRef.action.Enable();
    }

    private void UnbindAction(InputActionReference actionRef,
        Action<InputAction.CallbackContext> performed,
        Action<InputAction.CallbackContext> canceled = null)
    {
        if (actionRef?.action == null) return;
        if (performed != null) actionRef.action.performed -= performed;
        if (canceled != null)  actionRef.action.canceled  -= canceled;
        actionRef.action.Disable();
    }
}
