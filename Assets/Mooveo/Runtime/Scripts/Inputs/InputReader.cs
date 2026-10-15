using System;
using System.Collections;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.InputSystem;
using Valve.VR;

public enum ConfigMode { InputConfig, OVRInput }
public class InputReader : MonoBehaviour
{
    public ConfigMode ConfigMode;
    [SerializeField, ShowIf("ConfigMode", ConfigMode.InputConfig)] private InputConfig _inputConfig;
    [SerializeField] public bool SimulateVR = false;
    [Tooltip("Sélectionnez le rôle de ce tracker (LeftHand ou RightHand)")]
    [ShowIf("ConfigMode", ConfigMode.OVRInput)]public ETrackedControllerRole TargetRole = ETrackedControllerRole.LeftHand;

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
    public event Action<bool> IsTracked;
    public event Action<bool> DeviceConnectedChanged;
    private bool? _lastDeviceConnected;

    // Simulation variables
    private bool _simulatingTrigger = false;

    private Coroutine _initCoroutine;

    #region OpenVR Vive Trackers variables

    // Masques du PROFIL MATÉRIEL (HardwareProfiles). Vive : gâchette = Pin 4, poignée = Pin 3,
    // pouce = Touchpad — les constantes d'origine. PICO : mêmes bits + A/X au pouce, B/Y en « B ».
    private static ulong TriggerMask => HardwareProfiles.Current.TriggerMask;
    private static ulong GripMask    => HardwareProfiles.Current.GripMask;
    private static ulong ThumbMask   => HardwareProfiles.Current.ThumbMask;
    private static ulong BMask       => HardwareProfiles.Current.BMask;

    // Dernière valeur analogique émise : on ne renvoie que les variations.
    private float _lastAnalogTrigger;
    private const float AnalogTriggerEpsilon = 0.01f;

    private TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    
    // Dictionnaire pour stocker l'état précédent de chaque appareil (si plusieurs trackers)
    private Dictionary<uint, ulong> _previousButtonsState = new Dictionary<uint, ulong>();
    private Dictionary<uint, bool> _previousTrackedState = new Dictionary<uint, bool>();

    #endregion

    private void OnEnable()
    {
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
        if (SimulationInputEnabled && GlobalSettings.Core.GlobalSettings.Instance != null && GlobalSettings.Core.GlobalSettings.Instance.Admin.Value)
        {
            SimulateVR = true;
        }
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
        if (SimulationInputEnabled && GlobalSettings.Core.GlobalSettings.Instance != null && GlobalSettings.Core.GlobalSettings.Instance.Admin.Value)
        {
            SimulateVR = true;
        }

        if (SimulateVR)
        {
            HandleSimulation();
        }
        else if (ConfigMode == ConfigMode.OVRInput) HandleOpenVRRawInput();
    }

    private void HandleOpenVRRawInput()
    {
        var system = OpenVR.System;
        if (system == null) return;
        
        VREvent_t vrEvent = new VREvent_t();
        uint eventSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VREvent_t));
        while (system.PollNextEvent(ref vrEvent, eventSize)) { }
        
        uint targetDeviceIndex = system.GetTrackedDeviceIndexForControllerRole(TargetRole);
        if (targetDeviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid)
        {
            ReportDeviceConnected(false);
            return;
        }

        // --- 1. GESTION DU TRACKING (Garantie à 100% si le tracker est vu) ---
        // On récupère les poses brutes de tous les appareils (sans demander les boutons)
        system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0f, _poses);
        TrackedDevicePose_t pose = _poses[targetDeviceIndex];
        ReportDeviceConnected(pose.bDeviceIsConnected);

        bool isCurrentlyTracked = pose.bDeviceIsConnected && pose.bPoseIsValid;

        if (!_previousTrackedState.ContainsKey(targetDeviceIndex))
            _previousTrackedState[targetDeviceIndex] = false;

        if (isCurrentlyTracked != _previousTrackedState[targetDeviceIndex])
        {
            _previousTrackedState[targetDeviceIndex] = isCurrentlyTracked;
            IsTracked?.Invoke(isCurrentlyTracked); // Ça s'activera toujours, bouton ou pas !
        }

        // --- 2. GESTION DES BOUTONS (Pogo Pins) ---
        uint size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRControllerState_t));
        VRControllerState_t state = new VRControllerState_t();
        
        // On utilise GetControllerState seul. 
        // Si SteamVR refuse de donner les boutons sur cette frame (retourne false), 
        // on ignore cette étape, mais le IsTracked (au-dessus) a quand même été mis à jour !
        if (system.GetControllerState(targetDeviceIndex, ref state, size))
        {
            ulong currentButtons = state.ulButtonPressed;
            
            if (!_previousButtonsState.ContainsKey(targetDeviceIndex))
                _previousButtonsState[targetDeviceIndex] = 0;

            ulong prevButtons = _previousButtonsState[targetDeviceIndex];
            
            bool analog = HardwareProfiles.Current.AnalogTrigger;

            bool curTrigger = (currentButtons & TriggerMask) != 0;
            bool prevTrigger = (prevButtons & TriggerMask) != 0;
            if (curTrigger && !prevTrigger)
            {
                TriggerPressed?.Invoke();
                _lastAnalogTrigger = analog ? PressedTriggerValue(state) : 1f;
                TriggerChanged?.Invoke(_lastAnalogTrigger);
            }
            else if (curTrigger && analog)
            {
                // Appui maintenu : la course de la gâchette dose le jet (si NumericTrigger est coupé).
                float value = PressedTriggerValue(state);
                if (Mathf.Abs(value - _lastAnalogTrigger) > AnalogTriggerEpsilon)
                {
                    _lastAnalogTrigger = value;
                    TriggerChanged?.Invoke(value);
                }
            }
            if (!curTrigger && prevTrigger)
            {
                TriggerReleased?.Invoke();
                _lastAnalogTrigger = 0f;
                TriggerChanged?.Invoke(0f);
            }

            bool curGrip = (currentButtons & GripMask) != 0;
            bool prevGrip = (prevButtons & GripMask) != 0;
            if (curGrip && !prevGrip) AButtonPressed?.Invoke();
            if (!curGrip && prevGrip) AButtonReleased?.Invoke();

            // ⚠ Masque à PLUSIEURS bits (PICO : clic du stick OU A/X) : un seul front par appui,
            // même si les deux boutons sont enfoncés l'un après l'autre.
            bool curMenu = (currentButtons & ThumbMask) != 0;
            bool prevMenu = (prevButtons & ThumbMask) != 0;
            if (curMenu && !prevMenu) ThumbButtonPressed?.Invoke();

            // B : jamais émis par un tracker (masque nul dans le profil Vive).
            if (BMask != 0)
            {
                bool curB = (currentButtons & BMask) != 0;
                bool prevB = (prevButtons & BMask) != 0;
                if (curB && !prevB) BButtonPressed?.Invoke();
                if (!curB && prevB) BButtonReleased?.Invoke();
            }

            _previousButtonsState[targetDeviceIndex] = currentButtons;
        }
    }

    // Le bit « appuyé » est posé par le pilote à son propre seuil : la course analogique peut encore
    // valoir moins. Jamais zéro tant que le bit est là, sinon le peintre croirait à un relâchement.
    private static float PressedTriggerValue(VRControllerState_t state) =>
        Mathf.Clamp(state.rAxis1.x, 0.05f, 1f);

    // Tracker éteint : Unity ne le signale qu'au rallumage, OpenVR tout de suite.
    private void ReportDeviceConnected(bool connected)
    {
        // Jamais vu : un rôle non attribué n'est pas une déconnexion.
        if (_lastDeviceConnected == null && !connected) return;
        if (_lastDeviceConnected == connected) return;
        _lastDeviceConnected = connected;
        DeviceConnectedChanged?.Invoke(connected);
    }

    // Multiple admin sprays share one mouse. Only the selected spray consumes its input.
    public bool SimulationInputEnabled { get; set; } = true;
    // La bombe Admin réserve le clic gauche aux menus. Les autres simulations gardent leur commande.
    public bool SimulationUsesRightButton { get; set; }

    private void HandleSimulation()
    {
        if (!SimulationInputEnabled || Mouse.current == null) return;

        var trigger = SimulationUsesRightButton ? Mouse.current.rightButton : Mouse.current.leftButton;
        if (trigger.wasPressedThisFrame)
        {
            TriggerPressed?.Invoke();
            TriggerChanged?.Invoke(1f); 
        }
        if (trigger.wasReleasedThisFrame)
        {
            TriggerReleased?.Invoke();
            TriggerChanged?.Invoke(0f);
        }
        // Maintien
        if (trigger.isPressed)
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

        if (ConfigMode == ConfigMode.InputConfig)
        {
            BindAction(_inputConfig.IsTrackedAction, ctx => 
                IsTracked?.Invoke(ctx.ReadValue<bool>())); //TO DO CAST EN BOOL
        }

        BindAction(_inputConfig.PositionAction, ctx => 
            PositionChanged?.Invoke(ctx.ReadValue<Vector3>()));
        
        BindAction(_inputConfig.RotationAction, ctx => 
            RotationChanged?.Invoke(ctx.ReadValue<Quaternion>()));
        
        BindAction(_inputConfig.ScrollAction, ctx =>
            ScrollChanged?.Invoke(ctx.ReadValue<Vector2>()));
        
    }

    private void Unbind()
    {
        if (_inputConfig == null) return;

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
